using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Native = Timeline.Timeline;

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

            return IsShiftPlus(key, key.MainKey) && !Ctrl && !Input.GetKey(KeyCode.LeftAlt) && !Input.GetKey(KeyCode.RightAlt);
        }

        // Main key and every modifier of the binding are held (either Shift counts for Left Shift).
        static bool ShortcutHeld(KeyboardShortcut key)
        {
            if (key.MainKey == KeyCode.None || !Input.GetKey(key.MainKey))
            {
                return false;
            }

            foreach (KeyCode modifier in key.Modifiers)
            {
                bool held = modifier == KeyCode.LeftShift ? Shift : Input.GetKey(modifier);
                if (!held)
                {
                    return false;
                }
            }

            return true;
        }

        static bool HasModifiers(KeyboardShortcut key)
        {
            foreach (KeyCode modifier in key.Modifiers)
            {
                return true;
            }

            return false;
        }

        // True for exactly "Left Shift + main": the reorder defaults up to version 1.0.6.
        static bool IsShiftPlus(KeyboardShortcut key, KeyCode main)
        {
            if (key.MainKey != main)
            {
                return false;
            }

            int count = 0;
            foreach (KeyCode modifier in key.Modifiers)
            {
                if (modifier != KeyCode.LeftShift)
                {
                    return false;
                }

                count++;
            }

            return count == 1;
        }

        // "UpArrow / DownArrow" for the feedback line.
        string MoveShortcutLabel()
        {
            return upShortcut.Value + " / " + downShortcut.Value;
        }

        bool CommitShortcutPressed(bool down)
        {
            KeyboardShortcut binding = commitShortcut.Value;
            bool plainS = binding.MainKey == KeyCode.S && !HasModifiers(binding);

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
