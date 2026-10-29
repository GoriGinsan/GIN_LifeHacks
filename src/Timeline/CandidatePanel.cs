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
        void CreateSidebar()
        {
            sidebar = MakeRect("GIN Right Interpolables", window, true);
            Image back = sidebar.gameObject.AddComponent<Image>();
            back.color = new Color(.13f, .26f, .36f, .99f);
            RectTransform title = MakeRect("Title", sidebar, false);
            title.anchorMin = new Vector2(0, 1);
            title.anchorMax = Vector2.one;
            title.pivot = new Vector2(.5f, 1);
            title.sizeDelta = new Vector2(0, 40);
            title.anchoredPosition = Vector2.zero;
            candidateTitle = Label(title, "Interpolables\n" + AddShortcutLabel(), 14);
            RectTransform view = MakeRect("Viewport", sidebar, false);
            Fill(view, 2, 2, 17, 42);
            Image viewBackground = view.gameObject.AddComponent<Image>();
            viewBackground.color = new Color(.13f, .26f, .36f, .99f);
            Mask mask = view.gameObject.AddComponent<Mask>();
            mask.showMaskGraphic = true;
            candidateContent = MakeRect("Content", view, false);
            candidateContent.anchorMin = new Vector2(0, 1);
            candidateContent.anchorMax = Vector2.one;
            candidateContent.pivot = new Vector2(.5f, 1);
            candidateContent.anchoredPosition = Vector2.zero;
            candidateContent.sizeDelta = Vector2.zero;
            VerticalLayoutGroup layout = candidateContent.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.spacing = 0;
            ContentSizeFitter fit = candidateContent.gameObject.AddComponent<ContentSizeFitter>();
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            candidateScroll = sidebar.gameObject.AddComponent<ScrollRect>();
            candidateScroll.viewport = view;
            candidateScroll.content = candidateContent;
            candidateScroll.horizontal = false;
            candidateScroll.vertical = true;
            candidateScroll.scrollSensitivity = 30;
            candidateScroll.movementType = ScrollRect.MovementType.Clamped;
            RectTransform bar = MakeRect("Scrollbar", sidebar, false);
            bar.anchorMin = new Vector2(1, 0);
            bar.anchorMax = Vector2.one;
            bar.offsetMin = new Vector2(-15, 3);
            bar.offsetMax = new Vector2(-2, -42);
            Image b = bar.gameObject.AddComponent<Image>();
            b.color = new Color(.09f, .17f, .23f, 1);
            RectTransform thumb = MakeRect("Handle", bar, false);
            Fill(thumb, 1, 1, 1, 1);
            Image ti = thumb.gameObject.AddComponent<Image>();
            ti.color = new Color(.75f, .8f, .85f, 1);
            Scrollbar sb = bar.gameObject.AddComponent<Scrollbar>();
            sb.direction = Scrollbar.Direction.BottomToTop;
            sb.handleRect = thumb;
            sb.targetGraphic = ti;
            candidateScroll.verticalScrollbar = sb;
            PositionPanels();
        }

        static void ViewPrefix()
        {
            if (Self != null && Self.attached)
            {
                Self.inView = true;
                Self.RestoreCandidates();
            }
        }

        static void ViewPostfix()
        {
            if (Self == null || !Self.attached)
            {
                return;
            }

            Self.inView = false;
            Self.RebuildCandidates();
            Self.nextRefresh = 0;
        }

        void RestoreCandidates()
        {
            foreach (RectSnapshot s in relocated.Values)
            {
                s.Restore();
            }

            relocated.Clear();
        }

        void RebuildCandidates()
        {
            if (!attached || rebuilding || inView || candidateContent == null)
            {
                return;
            }

            rebuilding = true;
            try
            {
                if (!rightCandidates.Value)
                {
                    RestoreCandidates();
                    sidebar.gameObject.SetActive(false);
                    Call("UpdateInterpolablesView");
                    return;
                }

                sidebar.gameObject.SetActive(true);
                HashSet<GameObject> move = new HashSet<GameObject>();
                foreach (object h in Get<IList>(native, "_displayedOwnerHeader"))
                {
                    if (Get<object>(h, "group") == null)
                    {
                        move.Add(Get<GameObject>(h, "gameObject"));
                    }
                }

                foreach (object d in Get<IList>(native, "_displayedInterpolableModels"))
                {
                    object captured = d;
                    GameObject row = Get<GameObject>(d, "gameObject");
                    move.Add(row);
                    if (!modelActions.ContainsKey(row))
                    {
                        PointerAction action = row.AddComponent<PointerAction>();
                        action.action = delegate (PointerEventData e)
                        {
                            if (!rightCandidates.Value || !row.activeInHierarchy || TextFocused())
                            {
                                return;
                            }

                            trackFocus = true;
                            if ((e.button == PointerEventData.InputButton.Middle && !Ctrl) || MatchesAddGesture(e))
                            {
                                AddCandidate(Get<InterpolableModel>(captured, "model"));
                            }
                        };
                        modelActions.Add(row, action);
                        Graphic graphic = row.GetComponent<Graphic>();
                        if (graphic != null)
                        {
                            graphic.raycastTarget = true;
                        }
                    }
                }

                List<GameObject> ordered = new List<GameObject>();
                for (int i = 0; i < content.childCount; i++)
                {
                    GameObject child = content.GetChild(i).gameObject;
                    if (move.Contains(child) && child.activeSelf)
                    {
                        ordered.Add(child);
                    }
                }

                foreach (GameObject go in ordered)
                {
                    if (!relocated.ContainsKey(go))
                    {
                        relocated.Add(go, new RectSnapshot((RectTransform)go.transform));
                    }

                    go.transform.SetParent(candidateContent, false);
                    go.transform.SetAsLastSibling();
                    go.transform.localScale = Vector3.one;
                }

                // Native separators include candidate rows; keep only the registered tree part.
                int count = 0;
                foreach (object d in Get<IList>(native, "_displayedInterpolables"))
                {
                    if (Get<GameObject>(d, "gameObject").activeSelf)
                    {
                        count++;
                    }
                }

                foreach (object h in Get<IList>(native, "_displayedOwnerHeader"))
                {
                    if (Get<object>(h, "group") != null && Get<GameObject>(h, "gameObject").activeSelf)
                    {
                        count++;
                    }
                }

                IList heights = Get<IList>(native, "_gridHeights");
                while (heights.Count > count)
                {
                    heights.RemoveAt(heights.Count - 1);
                }

                LayoutRebuilder.MarkLayoutForRebuild(content);
                LayoutRebuilder.MarkLayoutForRebuild(candidateContent);
                PositionPanels();
            }
            finally
            {
                rebuilding = false;
            }
        }

        public bool AddCandidate(InterpolableModel model)
        {
            if (model == null || !model.IsCompatibleWithTarget(Get<Studio.ObjectCtrlInfo>(native, "_selectedOCI")))
            {
                Note("This track does not support the selected target");
                return false;
            }

            float time = Native.playbackTime;
            Interpolable track = Call("AddInterpolable", model) as Interpolable;
            if (track == null)
            {
                Note("No track is available for the selected target");
                return false;
            }

            bool added = AddAt(track, time);
            Note(added ? "Added at " + time.ToString("0.###") + " s: " + track.name : "Existing key preserved at " + time.ToString("0.###") + " s");
            return added;
        }

        internal bool AddAt(Interpolable track, float time)
        {
            if (time < 0 || Single.IsNaN(time) || Single.IsInfinity(time))
            {
                return false;
            }

            if (OccupiedAt(track, time))
            {
                return false;
            }

            Call("AddKeyframe", track, time);
            return track.keyframes.ContainsKey(time);
        }

        internal sealed class RectSnapshot
        {
            RectTransform rect;
            Transform parent;
            int index;
            Vector2 amin, amax, pivot, position, size;
            Vector3 scale;

            internal RectSnapshot(RectTransform rt)
            {
                rect = rt;
                parent = rt.parent;
                index = rt.GetSiblingIndex();
                amin = rt.anchorMin;
                amax = rt.anchorMax;
                pivot = rt.pivot;
                position = rt.anchoredPosition;
                size = rt.sizeDelta;
                scale = rt.localScale;
            }

            internal void Restore()
            {
                if (rect == null || parent == null)
                {
                    return;
                }

                rect.SetParent(parent, false);
                rect.SetSiblingIndex(index);
                rect.anchorMin = amin;
                rect.anchorMax = amax;
                rect.pivot = pivot;
                rect.anchoredPosition = position;
                rect.sizeDelta = size;
                rect.localScale = scale;
            }
        }
    }
}
