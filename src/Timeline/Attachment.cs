using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using Timeline;
using Native = Timeline.Timeline;

namespace GinTimelineHack
{
    public sealed partial class Plugin
    {
        void Attach()
        {
            if (!ValidateNativeContract())
            {
                return;
            }

            native = typeof(Native).GetField("_self", StaticFlags).GetValue(null) as Native;
            if (native == null)
            {
                return;
            }

            canvas = Get<Canvas>(native, "_ui");
            window = Get<RectTransform>(native, "_timelineWindow");
            resizeHandle = Get<RectTransform>(native, "_resizeHandle");
            grid = Get<RectTransform>(native, "_keyframesContainer");
            ScrollRect vertical = Get<ScrollRect>(native, "_verticalScrollView");
            if (canvas == null || window == null || grid == null || vertical == null)
            {
                return;
            }

            content = vertical.content;
            Text original = window.GetComponentInChildren<Text>();
            font = original == null ? Resources.GetBuiltinResource<Font>("Arial.ttf") : original.font;
            // Method contract validation occurs before modifying any native hierarchy.
            Method("UpdateInterpolablesView");
            Method("OnKeyframeContainerMouseDown");
            Method("UseCurrentValue");
            Method("AddKeyframe", typeof(Interpolable), typeof(float));
            Method("AddInterpolable", typeof(InterpolableModel));
            harmony = new Harmony(Guid);
            Patch("UpdateInterpolablesView", "ViewPrefix", "ViewPostfix", null);
            Patch("OnKeyframeContainerMouseDown", "GridPrefix", "GridPostfix", "GridFinalizer");
            Patch("UpdateGrid", null, "GridUpdated", null);
            Patch("UpdateInterpolableSelection", null, "TrackSelectionChanged", null);
            InstallInputHooks();
            InstallGroupInteraction();
            InstallAutoKey();
            BuildToolbar();
            foreach (Button b in window.GetComponentsInChildren<Button>(true))
            {
                if (b.transform.IsChildOf(toolbar))
                {
                    continue;
                }

                Text label = b.GetComponentInChildren<Text>();
                string text = label == null ? "" : label.text.Trim();
                if (text == "▲" || text == "▼" || text == "↑" || text == "↓")
                {
                    toolbarNeighbors.Add((RectTransform)b.transform);
                }
            }

            CreateSidebar();
            attached = true;
            InstallCameraGuard();
            Call("UpdateInterpolablesView");
            Note("GIN Timeline life hack enabled");
            Logger.LogInfo("Native Timeline " + nativeVersion + " attached without replacing window or data.");
        }

        void Detach()
        {
            attached = false;
            guardEpoch++;
            if (guardedCamera != null && guardedCamera.keyCondition == keyGuard)
            {
                guardedCamera.keyCondition = previousKeys;
            }

            if (guardedCamera != null && guardedCamera.noCtrlCondition == mouseGuard)
            {
                guardedCamera.noCtrlCondition = previousMouse;
            }

            if (guardedCamera != null && guardedCamera.zoomCondition == zoomGuard)
            {
                guardedCamera.zoomCondition = previousZoom;
            }

            guardedCamera = null;
            keyGuard = previousKeys = null;
            mouseGuard = previousMouse = zoomGuard = previousZoom = null;
            ResetGroupInteraction();
            ResetInputHooks();
            ResetAutoKey();
            faceKeyTarget = null;
            if (harmony != null)
            {
                harmony.UnpatchSelf();
                harmony = null;
            }

            RestoreCandidates();
            foreach (KeyValuePair<GameObject, PointerAction> a in modelActions)
            {
                if (a.Value != null)
                {
                    Destroy(a.Value);
                }
            }

            foreach (KeyValuePair<GameObject, PointerAction> a in keyActions)
            {
                if (a.Value != null)
                {
                    Destroy(a.Value);
                }
            }

            foreach (KeyValuePair<GameObject, PointerAction> a in headerActions)
            {
                if (a.Value != null)
                {
                    Destroy(a.Value);
                }
            }

            modelActions.Clear();
            keyActions.Clear();
            headerActions.Clear();
            foreach (GroupUi row in groupRows.Values)
            {
                row.Destroy();
            }

            groupRows.Clear();
            selectedGroups.Clear();
            toolbarNeighbors.Clear();
            foreach (GameObject go in owned)
            {
                if (go != null)
                {
                    Destroy(go);
                }
            }

            owned.Clear();
            toolbar = sidebar = candidateContent = resizeHandle = null;
            if (native != null && content != null)
            {
                try
                {
                    Call("UpdateInterpolablesView");
                }
                catch
                {
                }
            }

            native = null;
            canvas = null;
            window = grid = content = null;
        }
    }
}
