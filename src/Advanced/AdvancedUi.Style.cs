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
        private GUISkin GetSkin(GUISkin original)
        {
            if (localSkin == null || sourceSkin != original)
            {
                if (localSkin != null)
                {
                    UnityEngine.Object.Destroy(localSkin);
                }

                sourceSkin = original;
                localSkin = UnityEngine.Object.Instantiate(original) as GUISkin;
                localSkin.name = "GIN Advanced mode scoped skin";
            }

            // All styles live in a cloned skin. Other IMGUI windows retain their own skin.
            if (host.LargeControls.Value)
            {
                localSkin.button.fixedHeight = host.ButtonHeight.Value;
                localSkin.textField.fixedHeight = host.ButtonHeight.Value;
                localSkin.horizontalSlider.fixedHeight = host.ThumbSize.Value + 4;
                localSkin.horizontalSliderThumb.fixedWidth = host.ThumbSize.Value;
                localSkin.horizontalSliderThumb.fixedHeight = host.ThumbSize.Value;
                localSkin.verticalScrollbar.fixedWidth = host.ThumbSize.Value;
                localSkin.verticalScrollbarThumb.fixedWidth = host.ThumbSize.Value;
                localSkin.horizontalScrollbar.fixedHeight = host.ThumbSize.Value;
                localSkin.horizontalScrollbarThumb.fixedHeight = host.ThumbSize.Value;
            }
            else
            {
                localSkin.button = new GUIStyle(original.button);
                localSkin.textField = new GUIStyle(original.textField);
                localSkin.horizontalSlider = new GUIStyle(original.horizontalSlider);
                localSkin.horizontalSliderThumb = new GUIStyle(original.horizontalSliderThumb);
                localSkin.verticalScrollbar = new GUIStyle(original.verticalScrollbar);
                localSkin.verticalScrollbarThumb = new GUIStyle(original.verticalScrollbarThumb);
                localSkin.horizontalScrollbar = new GUIStyle(original.horizontalScrollbar);
                localSkin.horizontalScrollbarThumb = new GUIStyle(original.horizontalScrollbarThumb);
            }

            int gap = host.CompactSpacing.Value ? 1 : original.button.margin.top;
            localSkin.button.margin = new RectOffset(2, 2, gap, gap);
            localSkin.textField.margin = new RectOffset(2, 2, gap, gap);
            localSkin.label.margin = host.CompactSpacing.Value ? new RectOffset(2, 2, 0, 0) : new RectOffset(
                original.label.margin.left,
                original.label.margin.right,
                original.label.margin.top,
                original.label.margin.bottom);
            localSkin.horizontalSlider.margin = host.CompactSpacing.Value ? new RectOffset(4, 4, 0, 0) : new RectOffset(
                original.horizontalSlider.margin.left,
                original.horizontalSlider.margin.right,
                original.horizontalSlider.margin.top,
                original.horizontalSlider.margin.bottom);
            return localSkin;
        }

        private sealed class SkinState
        {
            internal GUISkin Previous;
        }

        private static void WindowPrefix(out SkinState __state)
        {
            __state = new SkinState();
            __state.Previous = GUI.skin;
            textIndex = 0;
            if (current != null)
            {
                GUI.skin = current.GetSkin(GUI.skin);
            }
        }

        private static Exception WindowFinalizer(Exception __exception, SkinState __state)
        {
            if (__state != null && __state.Previous != null)
            {
                GUI.skin = __state.Previous;
            }

            return __exception;
        }

        private static void AfterNativeGUI()
        {
            AdvancedUi a = current;
            if (a == null || !NativeVisible)
            {
                return;
            }

            string focus = GUI.GetNameOfFocusedControl();
            if (a.clearTextFocus)
            {
                if (!String.IsNullOrEmpty(focus) && focus.StartsWith("GIN.Adv.", StringComparison.Ordinal))
                {
                    GUI.FocusControl(null);
                    focus = "";
                }

                a.clearTextFocus = false;
            }

            textInput = !String.IsNullOrEmpty(focus) && focus.StartsWith("GIN.Adv.", StringComparison.Ordinal);
            textFrame = Time.frameCount;
            if (a.host.Library != null && a.host.Expressions.Value)
            {
                a.host.Library.Draw(NativeRect);
            }

            if (a.templatesOpen)
            {
                Color previous = GUI.color;
                GUI.color = new Color(.17f, .17f, .17f, 1);
                GUI.DrawTexture(a.templateRect, Texture2D.whiteTexture);
                GUI.color = previous;
                a.templateRect = GUILayout.Window(
                    0x47494131,
                    a.templateRect,
                    a.DrawTemplates,
                    "Display templates — GIN",
                    GUILayout.Width(480),
                    GUILayout.Height(380));
            }
        }
    }
}
