using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Timeline;
using Key = System.Collections.Generic.KeyValuePair<float, Timeline.Keyframe>;
using Native = Timeline.Timeline;
using Object = System.Object;

namespace GinTimelineHack
{
    public sealed partial class Plugin
    {
        internal static bool Ctrl
        {
            get
            {
                return Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            }
        }

        internal static bool Shift
        {
            get
            {
                return Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            }
        }

        void UpdateFocus()
        {
            bool? overAdvanced = AdvanceProperty("IsPointerOverAdvancedUI");
            bool? overExtension = AdvanceProperty("IsPointerOverExtensionUI");
            advancedFocus = (overAdvanced.HasValue ? overAdvanced.Value : PointerOverNativeAdvanced()) || (overExtension.HasValue && overExtension.Value);
            if (advancedFocus)
            {
                trackFocus = false;
                return;
            }

            if (EventSystem.current == null)
            {
                trackFocus = false;
                return;
            }

            PointerEventData p = new PointerEventData(EventSystem.current);
            p.position = Input.mousePosition;
            List<RaycastResult> results = new List<RaycastResult>();
            EventSystem.current.RaycastAll(p, results);
            if (results.Count == 0)
            {
                trackFocus = false;
                return;
            }

            Transform t = results[0].gameObject.transform;
            trackFocus = t.IsChildOf(canvas.transform);
        }

        bool CanUseShortcut(bool allowKkpe)
        {
            if (!attached || TextFocused())
            {
                return false;
            }

            // Committing a selected key follows the native Keyframe button, not mouse focus.
            if (allowKkpe)
            {
                return ReadSelection().Count > 0;
            }

            return Native.InterfaceVisible && trackFocus;
        }

        static bool KkpeEditing()
        {
            bool? extension = AdvanceProperty("IsAdvancedVisible");
            if (extension.HasValue)
            {
                return extension.Value;
            }

            // Fallback to the verified KKPE selected target's advanced state when the companion DLL is absent.
            foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (a.GetName().Name != "KKPE")
                {
                    continue;
                }

                Type t = a.GetType("HSPE.MainWindow", false);
                if (t == null)
                {
                    return false;
                }

                FieldInfo self = t.GetField("_self", StaticFlags);
                object w = self == null ? null : self.GetValue(null);
                if (w == null)
                {
                    return false;
                }

                FieldInfo target = t.GetField("_poseTarget", StaticFlags | Flags);
                object pose = target == null ? null : target.GetValue(w);
                if (pose == null)
                {
                    return false;
                }

                Type poseType = a.GetType("HSPE.PoseController", false);
                FieldInfo draw = poseType == null ? null : poseType.GetField("_drawAdvancedMode", StaticFlags);
                return draw != null && (bool)draw.GetValue(null);
            }

            return false;
        }

        static bool PointerOverNativeAdvanced()
        {
            if (!KkpeEditing())
            {
                return false;
            }

            foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (a.GetName().Name != "KKPE")
                {
                    continue;
                }

                Type t = a.GetType("HSPE.MainWindow", false);
                if (t == null)
                {
                    return false;
                }

                FieldInfo self = t.GetField("_self", StaticFlags);
                object main = self == null ? null : self.GetValue(null);
                FieldInfo rect = t.GetField("_advancedModeRect", Flags);
                return main != null && rect != null && ((Rect)rect.GetValue(main)).Contains(new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y));
            }

            return false;
        }

        internal static bool TextFocused()
        {
            bool? advancedText = AdvanceProperty("IsTextInputActive");
            if (SettingsOpen() || IsNativeTextEditing() || (advancedText.HasValue && advancedText.Value) || !String.IsNullOrEmpty(Input.compositionString))
            {
                return true;
            }

            EventSystem ev = EventSystem.current;
            InputField field = ev == null || ev.currentSelectedGameObject == null ? null : ev.currentSelectedGameObject.GetComponentInParent<InputField>();
            if (field != null && field.isFocused)
            {
                return true;
            }

            try
            {
                if (Studio.Studio.Instance != null && Studio.Studio.Instance.isInputNow)
                {
                    return true;
                }
            }
            catch
            {
            }

            return false;
        }

        static bool? AdvanceProperty(string name)
        {
            foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type t = a.GetType("GinAdvanceHack.Plugin", false);
                if (t == null)
                {
                    continue;
                }

                PropertyInfo p = t.GetProperty(name, StaticFlags);
                if (p != null && p.PropertyType == typeof(bool))
                {
                    return (bool)p.GetValue(null, null);
                }
            }

            return null;
        }

        static bool ShortcutDown(KeyboardShortcut key)
        {
            if (key.IsDown())
            {
                return true;
            }

            // Accept the right Shift key for the two defaults, without changing custom assignments.
            if (!Input.GetKey(KeyCode.RightShift) || !Input.GetKeyDown(key.MainKey))
            {
                return false;
            }

            int count = 0;
            foreach (KeyCode mod in key.Modifiers)
            {
                if (mod != KeyCode.LeftShift)
                {
                    return false;
                }

                count++;
            }

            return count == 1 && !Ctrl && !Input.GetKey(KeyCode.LeftAlt) && !Input.GetKey(KeyCode.RightAlt);
        }

        bool CommitShortcutPressed(bool down)
        {
            KeyboardShortcut binding = commitShortcut.Value;
            bool plainS = binding.MainKey == KeyCode.S;
            foreach (KeyCode modifier in binding.Modifiers)
            {
                plainS = false;
                break;
            }

            if (!plainS)
            {
                return down ? binding.IsDown() : Input.GetKey(binding.MainKey);
            }

            return (down ? Input.GetKeyDown(KeyCode.S) : Input.GetKey(KeyCode.S)) && DefaultCommitModifiers(ReadPhysicalKey);
        }

        static bool DefaultCommitModifiers(Func<KeyCode, bool> held)
        {
            // Shift/Alt are commonly held during fine movement. Ctrl+S belongs to Studio save.
            return !held(KeyCode.LeftControl) && !held(KeyCode.RightControl) && !held(KeyCode.LeftCommand) && !held(KeyCode.RightCommand) && !held(KeyCode.LeftWindows) && !held(KeyCode.RightWindows);
        }
    }
}
