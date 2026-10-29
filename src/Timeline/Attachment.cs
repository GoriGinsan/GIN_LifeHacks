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
            toolbar = MakeRect("GIN Timeline tools", window, true);
            Put(toolbar, 330, -26, 338, 24);
            MakeButton(toolbar, "Anchor", 0, 0, 68, 24, delegate
            {
                CaptureAnchor();
            });
            MakeButton(
                toolbar,
                "1 / 3 / 5",
                70,
                0,
                88,
                24,
                delegate
            {
                SelectAlternating(true);
            });
            MakeButton(
                toolbar,
                "2 / 4 / 6",
                160,
                0,
                88,
                24,
                delegate
            {
                SelectAlternating(false);
            });
            MakeButton(
                toolbar,
                "+ Tracks",
                250,
                0,
                86,
                24,
                delegate
            {
                rightCandidates.Value = !rightCandidates.Value;
                Config.Save();
            });
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

            RectTransform status = MakeRect("GIN Timeline feedback", toolbar, false);
            Put(status, 0, -23, 650, 21);
            statusText = status.gameObject.AddComponent<Text>();
            statusText.font = font;
            statusText.fontSize = 14;
            statusText.color = Color.white;
            statusText.raycastTarget = false;
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

            modelActions.Clear();
            keyActions.Clear();
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
