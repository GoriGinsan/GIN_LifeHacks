using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;
using Timeline;
using Native = Timeline.Timeline;

namespace GinTimelineHack
{
    public sealed partial class Plugin
    {
        static int routedInsertionDepth;
        static int nativeTextControl, nativeTextFrame = -10;
        PointerEventData keyboardAddEvent;
        static readonly KeyCode[] inputModifiers = new KeyCode[] { KeyCode.LeftControl, KeyCode.RightControl, KeyCode.LeftShift, KeyCode.RightShift, KeyCode.LeftAlt, KeyCode.RightAlt, KeyCode.LeftCommand, KeyCode.RightCommand, KeyCode.LeftWindows, KeyCode.RightWindows };

        void InstallInputHooks()
        {
            HarmonyMethod input = H("GridInputTranspiler");
            harmony.Patch(Method("OnKeyframeContainerMouseDown"), null, null, input, null, null);
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.GetName().Name != "KK_ShalltyUtils")
                {
                    continue;
                }

                Type hooks = assembly.GetType("ShalltyUtils.Hooks", false);
                MethodInfo postfix = hooks == null ? null : hooks.GetMethod("OnKeyframeContainerMouseDownPostfix", StaticFlags);
                if (postfix == null)
                {
                    throw new NotSupportedException("ShalltyUtils key insertion handler differs");
                }

                harmony.Patch(postfix, null, null, H("GridInputTranspiler"), null, null);
            }
            // Observe actual IMGUI text controls without treating every keyboard-focused slider/button as text.
            foreach (MethodInfo method in typeof(GUI).GetMethods(StaticFlags))
            {
                ParameterInfo[] parameters = method.GetParameters();
                if (method.Name != "DoTextField" || parameters.Length < 2 || parameters[0].ParameterType != typeof(Rect) || parameters[1].ParameterType != typeof(int))
                {
                    continue;
                }

                harmony.Patch(method, null, H("NativeTextFieldDrawn"), null, null, null);
            }
        }

        void ResetInputHooks()
        {
            routedInsertionDepth = 0;
            keyboardAddEvent = null;
            nativeTextControl = 0;
            nativeTextFrame = -10;
        }

        static IEnumerable<CodeInstruction> GridInputTranspiler(IEnumerable<CodeInstruction> source)
        {
            MethodInfo getKey = typeof(Input).GetMethod("GetKey", new Type[] { typeof(KeyCode) });
            MethodInfo replacement = typeof(Plugin).GetMethod("GridInputKey", StaticFlags);
            int replaced = 0;
            List<CodeInstruction> output = new List<CodeInstruction>();
            foreach (CodeInstruction original in source)
            {
                CodeInstruction instruction = new CodeInstruction(original);
                if (instruction.opcode == OpCodes.Call && System.Object.Equals(instruction.operand, getKey))
                {
                    instruction.operand = replacement;
                    replaced++;
                }
                output.Add(instruction);
            }
            if (replaced == 0)
            {
                throw new NotSupportedException("Timeline insertion input contract differs");
            }

            return output;
        }

        static bool ReadPhysicalKey(KeyCode key)
        {
            return Input.GetKey(key);
        }

        static bool GridInputKey(KeyCode key)
        {
            if (routedInsertionDepth > 0)
            {
                if (key == KeyCode.LeftControl)
                {
                    return false;
                }
                // Alt assigned as part of the add gesture must not silently invoke native multi-track insertion.
                if (key == KeyCode.LeftAlt && Self != null && Self.addShortcut != null && BindingUses(Self.addShortcut.Value, KeyCode.LeftAlt))
                {
                    return false;
                }

                if (key == KeyCode.LeftShift && Self != null && Self.addShortcut != null && BindingUses(Self.addShortcut.Value, KeyCode.LeftShift))
                {
                    return false;
                }
            }
            return ReadPhysicalKey(key);
        }

        static bool BindingUses(KeyboardShortcut shortcut, KeyCode key)
        {
            if (shortcut.MainKey == key)
            {
                return true;
            }

            foreach (KeyCode modifier in shortcut.Modifiers)
            {
                if (modifier == key)
                {
                    return true;
                }
            }

            return false;
        }

        internal float TimeForAdd(float x, bool configuredGesture)
        {
            if (configuredGesture && (BindingUses(addShortcut.Value, KeyCode.LeftShift) || BindingUses(addShortcut.Value, KeyCode.RightShift)))
            {
                return Math.Max(0, x / (30f * Get<float>(native, "_zoomLevel")));
            }

            return TimeFromLocalX(x);
        }

        static void NativeTextFieldDrawn(int __1)
        {
            if (GUIUtility.keyboardControl != __1 || __1 == 0)
            {
                return;
            }

            nativeTextControl = __1;
            nativeTextFrame = Time.frameCount;
        }

        internal static bool IsNativeTextEditing()
        {
            return GUIUtility.keyboardControl != 0 && GUIUtility.keyboardControl == nativeTextControl && Time.frameCount - nativeTextFrame <= 2;
        }

        internal static bool SettingsOpen()
        {
            // No reference or required dependency on ConfigurationManager; inspect the instance actually loaded.
            foreach (BepInEx.PluginInfo info in BepInEx.Bootstrap.Chainloader.PluginInfos.Values)
            {
                if (info.Instance == null || info.Instance.GetType().FullName != "ConfigurationManager.ConfigurationManager")
                {
                    continue;
                }

                Type type = info.Instance.GetType();
                PropertyInfo windowOpen = type.GetProperty("DisplayingWindow", Flags);
                if (windowOpen != null && windowOpen.PropertyType == typeof(bool) && (bool)windowOpen.GetValue(info.Instance, null))
                {
                    return true;
                }

                Type drawer = type.Assembly.GetType("ConfigurationManager.SettingFieldDrawer", false);
                PropertyInfo rebinding = drawer == null ? null : drawer.GetProperty("SettingKeyboardShortcut", StaticFlags);
                if (rebinding != null && rebinding.PropertyType == typeof(bool) && (bool)rebinding.GetValue(null, null))
                {
                    return true;
                }
            }
            return false;
        }

        static bool DefaultAddBinding(KeyboardShortcut shortcut)
        {
            if (shortcut.MainKey != KeyCode.Mouse1)
            {
                return false;
            }

            int count = 0;
            foreach (KeyCode modifier in shortcut.Modifiers)
            {
                if (modifier != KeyCode.LeftControl)
                {
                    return false;
                }

                count++;
            }
            return count == 1;
        }

        internal static bool AddModifiersMatch(KeyboardShortcut shortcut, Func<KeyCode, bool> held)
        {
            if (shortcut.MainKey == KeyCode.None)
            {
                return false;
            }

            bool defaultControl = DefaultAddBinding(shortcut);
            HashSet<KeyCode> allowed = new HashSet<KeyCode>();
            allowed.Add(shortcut.MainKey);
            foreach (KeyCode modifier in shortcut.Modifiers)
            {
                allowed.Add(modifier);
                if (defaultControl && modifier == KeyCode.LeftControl)
                {
                    if (!held(KeyCode.LeftControl) && !held(KeyCode.RightControl))
                    {
                        return false;
                    }

                    allowed.Add(KeyCode.RightControl);
                }
                else if (!held(modifier))
                {
                    return false;
                }
            }
            foreach (KeyCode modifier in inputModifiers)
            {
                if (!allowed.Contains(modifier) && held(modifier))
                {
                    return false;
                }
            }

            return true;
        }

        internal bool MatchesAddGesture(PointerEventData e)
        {
            if (e == null || addShortcut == null || SettingsOpen() || TextFocused())
            {
                return false;
            }

            KeyCode button = e.button == PointerEventData.InputButton.Left ? KeyCode.Mouse0 : e.button == PointerEventData.InputButton.Right ? KeyCode.Mouse1 : KeyCode.Mouse2;
            return addShortcut.Value.MainKey == button && AddModifiersMatch(addShortcut.Value, ReadPhysicalKey);
        }

        internal string AddShortcutLabel()
        {
            if (addShortcut == null || addShortcut.Value.MainKey == KeyCode.None)
            {
                return "Middle click: add at playhead";
            }

            return addShortcut.Value.ToString().Replace("LeftControl", "Ctrl").Replace("RightControl", "Right Ctrl").Replace("Mouse0", "Left click").Replace("Mouse1", "Right click").Replace("Mouse2", "Middle click") + " / Middle: add";
        }

        void PollAddShortcut()
        {
            if (!attached || !Native.InterfaceVisible || TextFocused() || SettingsOpen())
            {
                return;
            }

            KeyCode main = addShortcut.Value.MainKey;
            // uGUI delivers mouse 0/1/2 to their actual target; polling them too would insert twice.
            if (main == KeyCode.None || (main >= KeyCode.Mouse0 && main <= KeyCode.Mouse2) || !Input.GetKeyDown(main) || !AddModifiersMatch(addShortcut.Value, ReadPhysicalKey))
            {
                return;
            }

            AddAtPointer(Input.mousePosition);
        }

        internal bool AddAtPointer(Vector2 point)
        {
            if (!attached || EventSystem.current == null || TextFocused() || SettingsOpen())
            {
                return false;
            }

            PointerEventData e = new PointerEventData(EventSystem.current);
            e.position = point;
            e.button = PointerEventData.InputButton.Middle;
            List<RaycastResult> hits = new List<RaycastResult>();
            EventSystem.current.RaycastAll(e, hits);
            if (hits.Count == 0)
            {
                return false;
            }

            Transform target = hits[0].gameObject.transform;
            e.pointerCurrentRaycast = e.pointerPressRaycast = hits[0];
            if (rightCandidates.Value && candidateContent != null && target.IsChildOf(candidateContent))
            {
                foreach (object display in Get<IList>(native, "_displayedInterpolableModels"))
                {
                    GameObject row = Get<GameObject>(display, "gameObject");
                    if (row.activeInHierarchy && (target == row.transform || target.IsChildOf(row.transform)))
                    {
                        return AddCandidate(Get<InterpolableModel>(display, "model"));
                    }
                }
                return false;
            }
            if (grid == null || (target != grid && !target.IsChildOf(grid)))
            {
                return false;
            }

            if (AddGroupAtPointer(point, e.pressEventCamera))
            {
                return true;
            }

            keyboardAddEvent = e;
            try
            {
                Call("OnKeyframeContainerMouseDown", e);
            }
            finally
            {
                keyboardAddEvent = null;
            }
            return true;
        }
    }
}
