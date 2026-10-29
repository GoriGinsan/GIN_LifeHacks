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
        private static void BlendPrefix(object __instance)
        {
            blendContext = __instance;
            currentBlendKey = null;
            if (current == null || !current.host.VisibilityTools.Value)
            {
                return;
            }

            current.DrawVisibilityToolbar(__instance);
        }

        private static Exception BlendFinalizer(Exception __exception)
        {
            blendContext = null;
            currentBlendKey = null;
            return __exception;
        }

        private void DrawVisibilityToolbar(object editor)
        {
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Display templates", GUILayout.ExpandWidth(false)))
            {
                templatesOpen = !templatesOpen;
                if (templatesOpen)
                {
                    templateRect.x = Mathf.Clamp(NativeRect.xMax + 8, 0, Screen.width - 480);
                    templateRect.y = Mathf.Clamp(NativeRect.y, 0, Screen.height - 380);
                    RefreshTemplates();
                }
            }

            showHidden = GUILayout.Toggle(showHidden, "Show hidden rows", GUILayout.ExpandWidth(false));
            if (GUILayout.Button("Reveal all", GUILayout.ExpandWidth(false)))
            {
                hidden.Clear();
                Changed();
            }

            if (ReflectionAccess.Get(editor, "_faceRenderer") == ReflectionAccess.Get(editor, "_skinnedMeshTarget") && ReflectionAccess.Get(editor, "_faceRenderer") != null)
            {
                if (GUILayout.Button("Expand Eyes / Mouth", GUILayout.ExpandWidth(false)))
                {
                    SetFaceSections(editor, false);
                }

                if (GUILayout.Button("Collapse both", GUILayout.ExpandWidth(false)))
                {
                    SetFaceSections(editor, true);
                }
            }

            GUILayout.EndHorizontal();
        }

        private static string RendererKey(object editor)
        {
            return Convert.ToString(
                ReflectionAccess.Get(ReflectionAccess.Get(editor, "_skinnedMeshTarget"), "_fullPath"),
                CultureInfo.InvariantCulture);
        }

        private static IDictionary Separators(object editor)
        {
            object target = ReflectionAccess.Get(editor, "_target");
            bool female = (bool)ReflectionAccess.Get(target, "isFemale");
            return (IDictionary)ReflectionAccess.Get(editor.GetType(), female ? "_femaleSeparators" : "_maleSeparators");
        }

        private static string Section(object editor, int index)
        {
            if (ReflectionAccess.Get(editor, "_faceRenderer") != ReflectionAccess.Get(editor, "_skinnedMeshTarget"))
            {
                return "";
            }

            IDictionary separators = Separators(editor);
            string section = "";
            int nearest = Int32.MinValue;
            foreach (DictionaryEntry e in separators)
            {
                int start = Convert.ToInt32(e.Key);
                if (start <= index && start > nearest)
                {
                    nearest = start;
                    section = Convert.ToString(e.Value);
                }
            }

            return section;
        }

        private void SetFaceSections(object editor, bool close)
        {
            foreach (DictionaryEntry e in Separators(editor))
            {
                string key = RendererKey(editor) + "\n" + e.Value;
                if (close)
                {
                    collapsed.Add(key);
                }
                else
                {
                    collapsed.Remove(key);
                }
            }

            Changed();
        }



        // Inserted just after the native section heading and before any row layout.
        private static bool RowVisible(object editor, KeyValuePair<string, int> row)
        {
            currentBlendKey = RendererKey(editor) + "\n" + row.Key;
            if (current == null || !current.host.VisibilityTools.Value)
            {
                return true;
            }

            if (current.collapsed.Contains(RendererKey(editor) + "\n" + Section(editor, row.Value)))
            {
                return false;
            }

            return current.showHidden || !current.hidden.Contains(currentBlendKey);
        }

        private static void SectionHeading(string text, GUIStyle style, GUILayoutOption[] options)
        {
            if (current == null || !current.host.VisibilityTools.Value || blendContext == null)
            {
                GUILayout.Label(text, style, options);
                return;
            }

            string key = RendererKey(blendContext) + "\n" + text;
            bool closed = current.collapsed.Contains(key);
            if (GUILayout.Button((closed ? "▶ " : "▼ ") + text, style, options))
            {
                if (closed)
                {
                    current.collapsed.Remove(key);
                }
                else
                {
                    current.collapsed.Add(key);
                }

                current.Changed();
            }
        }

        private static bool BlendButton(string text, GUILayoutOption[] options)
        {
            if (current != null && current.host.VisibilityTools.Value && currentBlendKey != null && (text == "Rename" || text == "Save"))
            {
                Color color = GUI.color;
                GUI.color = Color.white;
                bool isHidden = current.hidden.Contains(currentBlendKey);
                if (GUILayout.Button(isHidden ? "Show" : "Hide", GUILayout.Width(54)))
                {
                    if (isHidden)
                    {
                        current.hidden.Remove(currentBlendKey);
                    }
                    else
                    {
                        current.hidden.Add(currentBlendKey);
                    }

                    current.Changed();
                }

                GUI.color = color;
            }

            if (current != null && current.host.LargeControls.Value && (text == "-1" || text == "+1"))
            {
                List<GUILayoutOption> larger = new List<GUILayoutOption>(options);
                larger.Add(GUILayout.MinWidth(32));
                return GUILayout.Button(text, larger.ToArray());
            }

            return GUILayout.Button(text, options);
        }

        private static string TextField(string text, GUILayoutOption[] options)
        {
            GUI.SetNextControlName("GIN.Adv.NativeText." + (textIndex++));
            return GUILayout.TextField(text, options);
        }
    }
}
