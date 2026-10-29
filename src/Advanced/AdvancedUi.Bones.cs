using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using System.Xml;
using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace GinAdvanceHack
{
    internal sealed partial class AdvancedUi
    {
        private sealed class ScrubGesture
        {
            internal float StartX, LastX, Total, Started, RepeatAt;
            internal bool Dragged, Repeated;
        }

        private static void BonesPrefix(object __instance)
        {
            bonesContext = __instance;
        }

        private static Exception BonesFinalizer(Exception __exception)
        {
            bonesContext = null;
            return __exception;
        }

        private static bool BoneButton(string text, GUILayoutOption[] options)
        {
            if (current != null && current.host.TwinShortcut.Value && bonesContext != null && (text == "Copy Position" || text == "Copy Rotation" || text == "Copy Scale"))
            {
                bool enabled = GUI.enabled;
                Transform twin = ReflectionAccess.Get(bonesContext, "_twinBoneTarget") as Transform;
                GUI.enabled = enabled && twin != null;
                if (GUILayout.Button(new GUIContent("L/R", "Select the opposite bone"), GUILayout.Width(48)))
                {
                    SwitchTwin(bonesContext);
                }

                GUI.enabled = enabled;
            }

            return GUILayout.Button(text, options);
        }

        private static bool SwitchTwin(object editor)
        {
            Transform twin = ReflectionAccess.Get(editor, "_twinBoneTarget") as Transform;
            if (twin == null)
            {
                return false;
            }

            ReflectionAccess.Call(editor, "ChangeBoneTarget", twin);
            return (ReflectionAccess.Get(editor, "_boneTarget") as Transform) == twin;
        }

        private static bool VectorPrefix(
            object __instance,
            Vector3 value,
            Color xColor,
            Color yColor,
            Color zColor,
            float customInc,
            string xLabel,
            string yLabel,
            string zLabel,
            Action onValueChanged,
            bool scaleEditor,
            ref Vector3 __result)
        {
            if (current == null || !current.host.ScrubBones.Value || __instance != bonesContext)
            {
                return true;
            }

            Vector3 result = value;
            Color color = GUI.color;
            GUILayout.BeginVertical();
            string[] labels =
            {
                xLabel,
                yLabel,
                zLabel
            };
            Color[] colors =
            {
                xColor,
                yColor,
                zColor
            };
            for (int i = 0; i < 3; i++)
            {
                GUI.color = colors[i];
                float typed;
                bool set;
                float delta = DrawAxis(result[i], labels[i], customInc, scaleEditor ? 1f : 0f, out typed, out set);
                if (set || delta != 0f)
                {
                    result[i] = (set ? typed : result[i]) + delta;
                    if (onValueChanged != null)
                    {
                        onValueChanged();
                    }
                }
            }

            GUILayout.EndVertical();
            GUI.color = color;
            __result = result;
            return false;
        }

        private static bool QuaternionPrefix(
            object __instance,
            Quaternion value,
            Color xColor,
            Color yColor,
            Color zColor,
            float customInc,
            string xLabel,
            string yLabel,
            string zLabel,
            Action onValueChanged,
            ref Quaternion __result)
        {
            if (current == null || !current.host.ScrubBones.Value || __instance != bonesContext)
            {
                return true;
            }

            Quaternion result = value;
            Color color = GUI.color;
            GUILayout.BeginVertical();
            string[] labels =
            {
                xLabel,
                yLabel,
                zLabel
            };
            Color[] colors =
            {
                xColor,
                yColor,
                zColor
            };
            Vector3[] axes =
            {
                Vector3.right,
                Vector3.up,
                Vector3.forward
            };
            for (int i = 0; i < 3; i++)
            {
                GUI.color = colors[i];
                Vector3 euler = result.eulerAngles;
                float typed;
                bool set;
                float delta = DrawAxis(euler[i], labels[i], customInc, 0f, out typed, out set);
                if (set)
                {
                    euler[i] = typed;
                    result = Quaternion.Euler(euler);
                }

                // Native KKPE increment buttons post-multiply local-axis rotations.
                if (delta != 0f)
                {
                    result = result * Quaternion.AngleAxis(delta, axes[i]);
                }

                if ((set || delta != 0f) && onValueChanged != null)
                {
                    onValueChanged();
                }
            }

            GUILayout.EndVertical();
            GUI.color = color;
            __result = result;
            return false;
        }

        private static float DrawAxis(float value, string label, float step, float reset, out float typed, out bool set)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(Mathf.Clamp(GUI.skin.label.CalcSize(new GUIContent(label)).x + 4, 26, 100)));
            string before = value.ToString("0.#####", CultureInfo.InvariantCulture);
            GUI.SetNextControlName("GIN.Adv.Axis." + (textIndex++));
            string after = GUILayout.TextField(before, GUILayout.MinWidth(72));
            typed = value;
            set = after != before && Single.TryParse(after, NumberStyles.Float, CultureInfo.InvariantCulture, out typed) && !Single.IsNaN(typed) && !Single.IsInfinity(typed);
            if (GUILayout.Button(reset.ToString(CultureInfo.InvariantCulture), GUILayout.Width(28)))
            {
                typed = reset;
                set = true;
            }

            float delta = ScrubButton(-Mathf.Abs(step)) + ScrubButton(Mathf.Abs(step));
            GUILayout.EndHorizontal();
            return delta;
        }

        internal static float ScrubDelta(float pixels, float step, float pixelsPerStep, bool fine, bool coarse)
        {
            return pixels * Mathf.Abs(step) / Mathf.Max(1f, pixelsPerStep) * (fine ? .1f : coarse ? 10f : 1f);
        }

        private static float ScrubButton(float signedStep)
        {
            string text = signedStep.ToString("+0.#####;-0.#####", CultureInfo.InvariantCulture);
            GUIContent content = new GUIContent(text, "Click: one step / hold and drag horizontally / Shift: fine / Ctrl: coarse / Esc: cancel");
            Rect rect = GUILayoutUtility.GetRect(content, GUI.skin.button, GUILayout.MinWidth(64), GUILayout.Height(current.host.ButtonHeight.Value));
            int id = GUIUtility.GetControlID(0x47494e53, FocusType.Passive, rect);
            Event e = Event.current;
            ScrubGesture gesture;
            float delta = 0;
            if (e.type == EventType.Repaint)
            {
                GUI.skin.button.Draw(rect, content, rect.Contains(e.mousePosition), GUIUtility.hotControl == id, false, false);
            }

            if (!GUI.enabled)
            {
                return 0;
            }

            if (e.type == EventType.MouseDown && e.button == 0 && rect.Contains(e.mousePosition))
            {
                GUIUtility.hotControl = id;
                GUIUtility.keyboardControl = 0;
                gesture = new ScrubGesture();
                gesture.StartX = gesture.LastX = e.mousePosition.x;
                gesture.Started = Time.realtimeSinceStartup;
                gesture.RepeatAt = gesture.Started + .45f;
                scrubs[id] = gesture;
                e.Use();
            }

            if (GUIUtility.hotControl != id || !scrubs.TryGetValue(id, out gesture))
            {
                return 0;
            }

            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
            {
                delta = -gesture.Total;
                GUIUtility.hotControl = 0;
                scrubs.Remove(id);
                e.Use();
                return delta;
            }

            if (e.type == EventType.MouseDrag)
            {
                if (!gesture.Dragged && Mathf.Abs(e.mousePosition.x - gesture.StartX) >= 3)
                {
                    gesture.Dragged = true;
                }

                if (gesture.Dragged)
                {
                    delta = ScrubDelta(e.mousePosition.x - gesture.LastX, signedStep, current.host.ScrubPixels.Value, e.shift, e.control);
                    gesture.Total += delta;
                    gesture.LastX = e.mousePosition.x;
                }

                e.Use();
            }
            else if (e.type == EventType.MouseUp && e.button == 0)
            {
                if (!gesture.Dragged && !gesture.Repeated && rect.Contains(e.mousePosition))
                {
                    delta = signedStep;
                }

                GUIUtility.hotControl = 0;
                scrubs.Remove(id);
                e.Use();
            }
            else if (e.type == EventType.Repaint && !gesture.Dragged && Time.realtimeSinceStartup >= gesture.RepeatAt)
            {
                delta = signedStep;
                gesture.Total += delta;
                gesture.Repeated = true;
                gesture.RepeatAt = Time.realtimeSinceStartup + .09f;
            }

            return delta;
        }

        private static void ReleaseScrub()
        {
            if (scrubs.ContainsKey(GUIUtility.hotControl))
            {
                GUIUtility.hotControl = 0;
            }

            scrubs.Clear();
        }
    }
}
