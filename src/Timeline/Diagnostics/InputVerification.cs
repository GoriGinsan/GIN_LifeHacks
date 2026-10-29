using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
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
        static HashSet<KeyCode> verificationHeld;
        static bool verificationShalltySawMaskedControl;

        static bool VerificationPhysicalKey(KeyCode key, ref bool __result)
        {
            __result = verificationHeld != null && verificationHeld.Contains(key);
            return false;
        }

        static void VerificationShalltyGrid(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Middle && ReadPhysicalKey(KeyCode.LeftControl) && !GridInputKey(KeyCode.LeftControl))
            {
                verificationShalltySawMaskedControl = true;
            }
        }

        void RunInputInteractionTests(StringBuilder result, Interpolable a, RectTransform row)
        {
            // Called only from RunIsolatedTests after its marked-directory, command-line and empty-scene checks.
            // Patch our physical-read wrapper temporarily; never patch Unity's global Input implementation.
            Harmony probe = new Harmony(Guid + ".isolated-input-probe");
            KeyboardShortcut oldBinding = addShortcut.Value;
            int oldControl = GUIUtility.keyboardControl, oldTextControl = nativeTextControl, oldTextFrame = nativeTextFrame;
            try
            {
                verificationHeld = new HashSet<KeyCode>();
                probe.Patch(
                    typeof(Plugin).GetMethod("ReadPhysicalKey", StaticFlags),
                    H("VerificationPhysicalKey"),
                    null,
                    null,
                    null,
                    null);
                bool shalltyPresent = false;
                foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    Type hooks = assembly.GetType("ShalltyUtils.Hooks", false);
                    MethodInfo method = hooks == null ? null : hooks.GetMethod("OnKeyframeContainerMouseDownPostfix", StaticFlags);
                    if (method == null)
                    {
                        continue;
                    }

                    shalltyPresent = true;
                    probe.Patch(method, H("VerificationShalltyGrid"), null, null, null, null);
                }
                verificationShalltySawMaskedControl = false;
                addShortcut.Value = new KeyboardShortcut(KeyCode.Mouse1, KeyCode.LeftControl);
                PointerEventData e = ProbeGridEvent(row, .37f, PointerEventData.InputButton.Right);
                int before = a.keyframes.Count;
                Call("OnKeyframeContainerMouseDown", e);
                Require(
                    a.keyframes.Count == before && e.button == PointerEventData.InputButton.Right,
                    "plain right does not add under Ctrl+Right default");
                verificationHeld.Add(KeyCode.LeftControl);
                Require(MatchesAddGesture(e), "default Ctrl+Right gesture matches held Control");
                Call("OnKeyframeContainerMouseDown", e);
                if (a.keyframes.Count != before + 1 || !HasProbeKey(a, .37f))
                {
                    Vector2 local;
                    RectTransformUtility.ScreenPointToLocalPointInRectangle(grid, e.position, e.pressEventCamera, out local);
                    result.AppendLine("INFO insertion probe: keys=" + String.Join(
                        ",",
                        new List<float>(a.keyframes.Keys).ConvertAll<string>(delegate (float value)
                    {
                        return value.ToString("R");
                    }).ToArray()) + "; local=" + local + "; row=" + row.anchoredPosition + "; event=" + e.button + "; camera=" + e.pressEventCamera + "; Shallty=" + verificationShalltySawMaskedControl);
                }
                Require(
                    a.keyframes.Count == before + 1 && HasProbeKey(a, .37f),
                    "Ctrl+Right traverses native insertion with Control held");
                Require(
                    e.button == PointerEventData.InputButton.Right && routedInsertionDepth == 0 && GridInputKey(KeyCode.LeftControl),
                    "insertion scope and event button restore after postfixes");
                if (shalltyPresent)
                {
                    Require(verificationShalltySawMaskedControl, "Shallty postfix sees Middle and scoped unpressed Control");
                }

                verificationHeld.Clear();
                e = ProbeGridEvent(row, .73f, PointerEventData.InputButton.Middle);
                Call("OnKeyframeContainerMouseDown", e);
                Require(a.keyframes.Count == before + 2 && HasProbeKey(a, .73f), "native middle insertion remains available");
                verificationHeld.Add(KeyCode.LeftControl);
                e = ProbeGridEvent(row, .89f, PointerEventData.InputButton.Middle);
                Call("OnKeyframeContainerMouseDown", e);
                Require(a.keyframes.Count == before + 2, "Ctrl+Middle grid remains native no-insert");
                verificationHeld.Clear();
                verificationHeld.Add(KeyCode.RightControl);
                e = ProbeGridEvent(row, 1.11f, PointerEventData.InputButton.Right);
                Call("OnKeyframeContainerMouseDown", e);
                Require(a.keyframes.Count == before + 3 && HasProbeKey(a, 1.11f), "default also accepts RightControl");
                verificationHeld.Clear();
                verificationHeld.Add(KeyCode.LeftAlt);
                addShortcut.Value = new KeyboardShortcut(KeyCode.Mouse1, KeyCode.LeftAlt);
                e = ProbeGridEvent(row, 1.39f, PointerEventData.InputButton.Right);
                Call("OnKeyframeContainerMouseDown", e);
                Require(a.keyframes.Count == before + 4 && HasProbeKey(a, 1.39f), "custom mouse binding routes native insertion");
                verificationHeld.Clear();
                addShortcut.Value = new KeyboardShortcut(KeyCode.K, KeyCode.LeftControl);
                verificationHeld.Add(KeyCode.LeftControl);
                Require(AddModifiersMatch(addShortcut.Value, ReadPhysicalKey), "custom keyboard modifier matcher");
                e = ProbeGridEvent(row, 1.67f, PointerEventData.InputButton.Middle);
                keyboardAddEvent = e;
                try
                {
                    Call("OnKeyframeContainerMouseDown", e);
                }
                finally
                {
                    keyboardAddEvent = null;
                }
                Require(
                    a.keyframes.Count == before + 5 && HasProbeKey(a, 1.67f) && routedInsertionDepth == 0,
                    "keyboard route uses identical native + Shallty pipeline");
                verificationHeld.Clear();
                Require(!AddModifiersMatch(addShortcut.Value, ReadPhysicalKey), "custom keyboard shortcut requires its modifier");
                addShortcut.Value = KeyboardShortcut.Empty;
                Require(
                    !MatchesAddGesture(e) && !AddModifiersMatch(addShortcut.Value, ReadPhysicalKey),
                    "cleared custom insertion shortcut disabled");
                addShortcut.Value = new KeyboardShortcut(KeyCode.Mouse1, KeyCode.LeftControl);
                verificationHeld.Add(KeyCode.LeftControl);
                e = ProbeGridEvent(row, 1.91f, PointerEventData.InputButton.Right);
                e.position = RectTransformUtility.WorldToScreenPoint(
                    null,
                    grid.TransformPoint(new Vector3(30 * Get<float>(native, "_zoomLevel") * 1.91f, row.anchoredPosition.y - 3000, 0)));
                Call("OnKeyframeContainerMouseDown", e);
                Require(
                    a.keyframes.Count == before + 5 && routedInsertionDepth == 0,
                    "empty grid area does not choose nearest track");

                verificationHeld.Clear();
                verificationHeld.Add(KeyCode.LeftShift);
                verificationHeld.Add(KeyCode.LeftAlt);
                Require(DefaultCommitModifiers(ReadPhysicalKey), "default S remains available during Shift/Alt manipulation");
                verificationHeld.Add(KeyCode.LeftControl);
                Require(!DefaultCommitModifiers(ReadPhysicalKey), "Ctrl+S remains Studio scene save without committing keys");
                verificationHeld.Remove(KeyCode.LeftControl);
                verificationHeld.Add(KeyCode.RightControl);
                Require(!DefaultCommitModifiers(ReadPhysicalKey), "right Ctrl+S is also reserved for scene save");
                verificationHeld.Clear();
                result.AppendLine("PASS default S modifiers: held Shift/Alt allowed; left/right Ctrl+S reserved for native scene save");
                GUIUtility.keyboardControl = 918271;
                nativeTextControl = 0;
                nativeTextFrame = -10;
                Require(!IsNativeTextEditing(), "non-text keyboard focus does not block S");
                NativeTextFieldDrawn(918271);
                Require(IsNativeTextEditing() && TextFocused(), "observed native IMGUI text entry blocks edit shortcuts");
                GUIUtility.keyboardControl = 918272;
                Require(!IsNativeTextEditing(), "switching to another control releases native text guard");
                result.AppendLine("PASS input: Ctrl+Right default (left/right Control), plain Right unchanged, native Middle retained, Ctrl+Middle preserved, custom mouse/keyboard routing, disabled binding and empty-row protection");
                result.AppendLine("PASS input scope: native insertion and Shallty postfix observe scoped Control suppression; original pointer/button/input state restored");
                result.AppendLine("PASS S text guard: actual observed IMGUI text control blocks input; arbitrary non-text keyboard focus does not");
            }
            finally
            {
                probe.UnpatchSelf();
                verificationHeld = null;
                keyboardAddEvent = null;
                addShortcut.Value = oldBinding;
                routedInsertionDepth = 0;
                GUIUtility.keyboardControl = oldControl;
                nativeTextControl = oldTextControl;
                nativeTextFrame = oldTextFrame;
            }
        }

        PointerEventData ProbeGridEvent(RectTransform row, float time, PointerEventData.InputButton button)
        {
            return EventAt(row, time, button);
        }

        static bool HasProbeKey(Interpolable track, float time)
        {
            foreach (float actual in track.keyframes.Keys)
            {
                if (Math.Abs(actual - time) < .0001f)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
