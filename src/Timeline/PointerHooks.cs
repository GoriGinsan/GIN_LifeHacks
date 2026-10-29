using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
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
        static void GridUpdated()
        {
            if (Self != null && Self.attached)
            {
                Self.nextRefresh = 0;
                Self.HookNativeGroupDrags();
            }
        }

        static void TrackSelectionChanged()
        {
            if (Self == null || !Self.attached || Self.rebuilding)
            {
                return;
            }

            if (Get<IList>(Self.native, "_selectedInterpolables").Count > 0)
            {
                Self.selectedGroups.Clear();
            }
        }

        sealed class GridState
        {
            internal PointerEventData.InputButton button;
            internal int insertionDepth;
            internal List<GameObject> inactive = new List<GameObject>();
        }

        static bool GridPrefix(PointerEventData eventData, out GridState __state)
        {
            __state = null;
            Plugin p = Self;
            if (p == null || !p.attached || eventData == null)
            {
                return true;
            }

            __state = new GridState();
            __state.button = eventData.button;
            __state.insertionDepth = routedInsertionDepth;
            bool routed = System.Object.ReferenceEquals(p.keyboardAddEvent, eventData) || p.MatchesAddGesture(eventData);
            if (routed)
            {
                routedInsertionDepth++;
                eventData.button = PointerEventData.InputButton.Middle;
            }

            if (eventData.button == PointerEventData.InputButton.Middle && TextFocused())
            {
                eventData.button = PointerEventData.InputButton.Left;
                return false;
            }

            if (p.rightCandidates.Value)
            {
                foreach (object d in Get<IList>(p.native, "_displayedInterpolableModels"))
                {
                    GameObject go = Get<GameObject>(d, "gameObject");
                    if (go.activeSelf)
                    {
                        __state.inactive.Add(go);
                        go.SetActive(false);
                    }
                }
            }

            // The original handler chooses the nearest row even when the empty area has no track.
            // Decline insertion there, while retaining native middle/right behavior on real rows.
            if (eventData.button == PointerEventData.InputButton.Middle && !GridInputKey(KeyCode.LeftControl) && !GridInputKey(KeyCode.LeftAlt))
            {
                Vector2 local;
                if (RectTransformUtility.ScreenPointToLocalPointInRectangle(p.grid, eventData.position, eventData.pressEventCamera, out local))
                {
                    bool hit = false;
                    foreach (object d in Get<IList>(p.native, "_displayedInterpolables"))
                    {
                        GameObject go = Get<GameObject>(d, "gameObject");
                        if (!go.activeSelf)
                        {
                            continue;
                        }

                        RectTransform rt = (RectTransform)go.transform;
                        if (Math.Abs(local.y - rt.anchoredPosition.y) <= rt.rect.height / 2f + .1f)
                        {
                            hit = true;
                            break;
                        }
                    }

                    if (!hit && p.rightCandidates.Value)
                    {
                        eventData.button = PointerEventData.InputButton.Left;
                        return false;
                    }
                }
            }

            p.trackFocus = true;
            return true;
        }

        static void GridPostfix()
        {
        }

        static Exception GridFinalizer(PointerEventData eventData, GridState __state, Exception __exception)
        {
            if (__state != null)
            {
                eventData.button = __state.button;
                routedInsertionDepth = __state.insertionDepth;
                foreach (GameObject go in __state.inactive)
                {
                    if (go != null)
                    {
                        go.SetActive(true);
                    }
                }
            }

            return __exception;
        }

        void HookKeys()
        {
            // Native key right-click (seek) and native modifier gestures intentionally remain unchanged.
            // Only the empty-grid handler routes configured insertion; key events are never rewritten to Middle.
            foreach (object d in Get<IList>(native, "_displayedKeyframes"))
            {
                object captured = d;
                GameObject go = Get<GameObject>(d, "gameObject");
                if (keyActions.ContainsKey(go))
                {
                    continue;
                }

                PointerAction action = go.AddComponent<PointerAction>();
                action.action = delegate (PointerEventData e)
                {
                    if (e.button != PointerEventData.InputButton.Left || Ctrl || Shift || Alt)
                    {
                        return;
                    }

                    Timeline.Keyframe key = Get<Timeline.Keyframe>(captured, "keyframe");
                    if (key == null)
                    {
                        return;
                    }

                    foreach (Key pair in key.parent.keyframes)
                    {
                        if (Object.ReferenceEquals(pair.Value, key))
                        {
                            anchor = pair;
                            range = lastPhase = null;
                            break;
                        }
                    }

                    selectedGroups.Clear();
                    trackFocus = true;
                };
                keyActions.Add(go, action);
            }
        }
    }
}
