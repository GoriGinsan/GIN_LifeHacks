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
        readonly Vector3[] panelCorners = new Vector3[4];
        internal RectTransform MakeRect(string name, Transform parent, bool own)
        {
            GameObject go = new GameObject(name, typeof(RectTransform)); go.transform.SetParent(parent, false);
            if (own) owned.Add(go); return (RectTransform)go.transform;
        }
        internal static void Put(RectTransform rt, float x, float y, float w, float h)
        { rt.anchorMin = rt.anchorMax = new Vector2(0, 1); rt.pivot = new Vector2(0, 1); rt.anchoredPosition = new Vector2(x, -y); rt.sizeDelta = new Vector2(w, h); }
        internal static void Fill(RectTransform rt, float l, float b, float r, float t)
        { rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = new Vector2(l, b); rt.offsetMax = new Vector2(-r, -t); rt.localScale = Vector3.one; }
        Text Label(Transform parent, string value, int size)
        {
            RectTransform rt = MakeRect("Label", parent, false); Fill(rt, 3, 1, 3, 1);
            Text text = rt.gameObject.AddComponent<Text>(); text.font = font; text.fontSize = size; text.text = value; text.color = Color.white;
            text.alignment = TextAnchor.MiddleCenter; text.raycastTarget = false; return text;
        }
        Button MakeButton(Transform parent, string value, float x, float y, float w, float h, Action action)
        {
            RectTransform rt = MakeRect(value, parent, false); Put(rt, x, y, w, h);
            Image bg = rt.gameObject.AddComponent<Image>(); bg.color = new Color(.16f, .29f, .39f, 1);
            Button button = rt.gameObject.AddComponent<Button>(); button.targetGraphic = bg;
            ColorBlock colors = button.colors; colors.highlightedColor = new Color(.7f, .85f, 1); button.colors = colors;
            Label(rt, value, 14); button.onClick.AddListener(delegate { try { action(); } catch (Exception e) { Note(Error(e)); Logger.LogError(e); } }); return button;
        }
        void CreateSidebar()
        {
            sidebar = MakeRect("GIN Right Interpolables", window, true);
            Image back = sidebar.gameObject.AddComponent<Image>(); back.color = new Color(.13f, .26f, .36f, .99f);
            RectTransform title = MakeRect("Title", sidebar, false); title.anchorMin = new Vector2(0, 1); title.anchorMax = Vector2.one; title.pivot = new Vector2(.5f, 1); title.sizeDelta = new Vector2(0, 40); title.anchoredPosition = Vector2.zero;
            candidateTitle = Label(title, "Interpolables\n" + AddShortcutLabel(), 14);
            RectTransform view = MakeRect("Viewport", sidebar, false); Fill(view, 2, 2, 17, 42);
            Image viewBackground = view.gameObject.AddComponent<Image>(); viewBackground.color = new Color(.13f, .26f, .36f, .99f);
            Mask mask = view.gameObject.AddComponent<Mask>(); mask.showMaskGraphic = true;
            candidateContent = MakeRect("Content", view, false); candidateContent.anchorMin = new Vector2(0, 1); candidateContent.anchorMax = Vector2.one; candidateContent.pivot = new Vector2(.5f, 1); candidateContent.anchoredPosition = Vector2.zero;
            candidateContent.sizeDelta = Vector2.zero;
            VerticalLayoutGroup layout = candidateContent.gameObject.AddComponent<VerticalLayoutGroup>(); layout.childForceExpandWidth = true; layout.childForceExpandHeight = false; layout.spacing = 0;
            ContentSizeFitter fit = candidateContent.gameObject.AddComponent<ContentSizeFitter>(); fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            candidateScroll = sidebar.gameObject.AddComponent<ScrollRect>(); candidateScroll.viewport = view; candidateScroll.content = candidateContent; candidateScroll.horizontal = false; candidateScroll.vertical = true; candidateScroll.scrollSensitivity = 30; candidateScroll.movementType = ScrollRect.MovementType.Clamped;
            RectTransform bar = MakeRect("Scrollbar", sidebar, false); bar.anchorMin = new Vector2(1, 0); bar.anchorMax = Vector2.one; bar.offsetMin = new Vector2(-15, 3); bar.offsetMax = new Vector2(-2, -42);
            Image b = bar.gameObject.AddComponent<Image>(); b.color = new Color(.09f, .17f, .23f, 1);
            RectTransform thumb = MakeRect("Handle", bar, false); Fill(thumb, 1, 1, 1, 1); Image ti = thumb.gameObject.AddComponent<Image>(); ti.color = new Color(.75f, .8f, .85f, 1);
            Scrollbar sb = bar.gameObject.AddComponent<Scrollbar>(); sb.direction = Scrollbar.Direction.BottomToTop; sb.handleRect = thumb; sb.targetGraphic = ti; candidateScroll.verticalScrollbar = sb;
            PositionPanels();
        }
        void PositionPanels()
        {
            if (sidebar == null || window == null) return;
            if (candidateTitle != null) candidateTitle.text = "Interpolables\n" + AddShortcutLabel();
            sidebar.anchorMin = new Vector2(1, 0); sidebar.anchorMax = Vector2.one; sidebar.offsetMin = new Vector2(4, 0); sidebar.offsetMax = new Vector2(candidateWidth.Value + 4, 0);
            sidebar.gameObject.SetActive(rightCandidates.Value);
            if (toolbar != null)
            {
                float x = 330, y = -26;
                float right = 0, top = 0; bool found = false;
                foreach (RectTransform rt in toolbarNeighbors)
                {
                    if (rt == null || !rt.gameObject.activeInHierarchy) continue; rt.GetWorldCorners(panelCorners);
                    Vector3 tr = window.InverseTransformPoint(panelCorners[2]);
                    if (!found || tr.x > right) { right = tr.x; top = tr.y; found = true; }
                }
                if (found) { x = right - window.rect.xMin + 3; y = window.rect.yMax - top; }
                // Always test the preferred placement, including the feedback even while it is blank.
                // Testing the previous lower placement would alternate sides every frame.
                Put(toolbar, x, y, 338, 24);
                if (statusText != null) Put(statusText.rectTransform, 0, -23, 650, 21);
                Camera camera = PanelCamera();
                Rect bounds = ToolScreenBounds(camera);
                bool below = bounds.yMax > Screen.height - 2;
                // Screen-edge clamping can move the tools toward the handle too.
                // Evaluate the preferred placement afresh each frame so widening restores it.
                if (!below)
                {
                    KeepToolsOnScreen(camera);
                    if (resizeHandle != null && resizeHandle.gameObject.activeInHierarchy)
                    {
                        Rect handle = PanelScreenBounds(resizeHandle, camera);
                        handle.xMin -= 4; handle.xMax += 4; handle.yMin -= 4; handle.yMax += 4;
                        below = PanelScreenBounds(toolbar, camera).Overlaps(handle);
                    }
                }
                if (below)
                {
                    Put(toolbar, x, window.rect.height + 3, 338, 24);
                    if (statusText != null) Put(statusText.rectTransform, 0, 26, 650, 21);
                }
                KeepToolsOnScreen(camera);
            }
        }
        void KeepToolsOnScreen(Camera camera)
        {
            Rect bounds = ToolScreenBounds(camera);
            Vector2 shift = new Vector2(ScreenShift(bounds.xMin, bounds.xMax, Screen.width), ScreenShift(bounds.yMin, bounds.yMax, Screen.height));
            if (shift.sqrMagnitude <= .0001f) return;
            Vector2 before, after;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(window, bounds.center, camera, out before) &&
                RectTransformUtility.ScreenPointToLocalPointInRectangle(window, bounds.center + shift, camera, out after))
                toolbar.anchoredPosition += after - before;
        }
        Camera PanelCamera()
        {
            Canvas root = canvas == null ? null : canvas.rootCanvas;
            return root == null || root.renderMode == RenderMode.ScreenSpaceOverlay ? null : root.worldCamera;
        }
        Rect PanelScreenBounds(RectTransform rt, Camera camera)
        {
            rt.GetWorldCorners(panelCorners);
            Vector2 first = RectTransformUtility.WorldToScreenPoint(camera, panelCorners[0]);
            Rect bounds = new Rect(first.x, first.y, 0, 0);
            for (int i = 1; i < panelCorners.Length; i++)
            {
                Vector2 point = RectTransformUtility.WorldToScreenPoint(camera, panelCorners[i]);
                bounds.xMin = Math.Min(bounds.xMin, point.x); bounds.xMax = Math.Max(bounds.xMax, point.x);
                bounds.yMin = Math.Min(bounds.yMin, point.y); bounds.yMax = Math.Max(bounds.yMax, point.y);
            }
            return bounds;
        }
        Rect ToolScreenBounds(Camera camera)
        {
            Rect bounds = PanelScreenBounds(toolbar, camera);
            if (statusText != null)
            {
                Rect feedback = PanelScreenBounds(statusText.rectTransform, camera);
                bounds.xMin = Math.Min(bounds.xMin, feedback.xMin); bounds.xMax = Math.Max(bounds.xMax, feedback.xMax);
                bounds.yMin = Math.Min(bounds.yMin, feedback.yMin); bounds.yMax = Math.Max(bounds.yMax, feedback.yMax);
            }
            return bounds;
        }
        static float ScreenShift(float min, float max, float screenSize)
        {
            if (max - min > screenSize - 4) return 2 - min;
            if (min < 2) return 2 - min;
            if (max > screenSize - 2) return screenSize - 2 - max;
            return 0;
        }
        static void ViewPrefix()
        { if (Self != null && Self.attached) { Self.inView = true; Self.RestoreCandidates(); } }
        static void ViewPostfix()
        {
            if (Self == null || !Self.attached) return;
            Self.inView = false; Self.RebuildCandidates(); Self.nextRefresh = 0;
        }
        static void GridUpdated() { if (Self != null && Self.attached) { Self.nextRefresh = 0; Self.HookNativeGroupDrags(); } }
        static void TrackSelectionChanged()
        {
            if (Self == null || !Self.attached || Self.rebuilding) return;
            if (Get<IList>(Self.native, "_selectedInterpolables").Count > 0) Self.selectedGroups.Clear();
        }
        void RestoreCandidates()
        {
            foreach (RectSnapshot s in relocated.Values) s.Restore(); relocated.Clear();
        }
        void RebuildCandidates()
        {
            if (!attached || rebuilding || inView || candidateContent == null) return;
            rebuilding = true;
            try
            {
                if (!rightCandidates.Value)
                {
                    RestoreCandidates(); sidebar.gameObject.SetActive(false); Call("UpdateInterpolablesView"); return;
                }
                sidebar.gameObject.SetActive(true);
                HashSet<GameObject> move = new HashSet<GameObject>();
                foreach (object h in Get<IList>(native, "_displayedOwnerHeader"))
                    if (Get<object>(h, "group") == null) move.Add(Get<GameObject>(h, "gameObject"));
                foreach (object d in Get<IList>(native, "_displayedInterpolableModels"))
                {
                    object captured = d; GameObject row = Get<GameObject>(d, "gameObject"); move.Add(row);
                    if (!modelActions.ContainsKey(row))
                    {
                        PointerAction action = row.AddComponent<PointerAction>();
                        action.action = delegate(PointerEventData e)
                        {
                            if (!rightCandidates.Value || !row.activeInHierarchy || TextFocused()) return;
                            trackFocus = true;
                            if ((e.button == PointerEventData.InputButton.Middle && !Ctrl) || MatchesAddGesture(e)) AddCandidate(Get<InterpolableModel>(captured, "model"));
                        };
                        modelActions.Add(row, action);
                        Graphic graphic = row.GetComponent<Graphic>(); if (graphic != null) graphic.raycastTarget = true;
                    }
                }
                List<GameObject> ordered = new List<GameObject>();
                for (int i = 0; i < content.childCount; i++)
                { GameObject child = content.GetChild(i).gameObject; if (move.Contains(child) && child.activeSelf) ordered.Add(child); }
                foreach (GameObject go in ordered)
                {
                    if (!relocated.ContainsKey(go)) relocated.Add(go, new RectSnapshot((RectTransform)go.transform));
                    go.transform.SetParent(candidateContent, false); go.transform.SetAsLastSibling(); go.transform.localScale = Vector3.one;
                }
                // Native separators include candidate rows; keep only the registered tree part.
                int count = 0;
                foreach (object d in Get<IList>(native, "_displayedInterpolables")) if (Get<GameObject>(d, "gameObject").activeSelf) count++;
                foreach (object h in Get<IList>(native, "_displayedOwnerHeader")) if (Get<object>(h, "group") != null && Get<GameObject>(h, "gameObject").activeSelf) count++;
                IList heights = Get<IList>(native, "_gridHeights"); while (heights.Count > count) heights.RemoveAt(heights.Count - 1);
                LayoutRebuilder.MarkLayoutForRebuild(content); LayoutRebuilder.MarkLayoutForRebuild(candidateContent);
                PositionPanels();
            }
            finally { rebuilding = false; }
        }
        public bool AddCandidate(InterpolableModel model)
        {
            if (model == null || !model.IsCompatibleWithTarget(Get<Studio.ObjectCtrlInfo>(native, "_selectedOCI"))) { Note("This track does not support the selected target"); return false; }
            float time = Native.playbackTime;
            Interpolable track = Call("AddInterpolable", model) as Interpolable;
            if (track == null) { Note("No track is available for the selected target"); return false; }
            bool added = AddAt(track, time); Note(added ? "Added at " + time.ToString("0.###") + " s: " + track.name : "Existing key preserved at " + time.ToString("0.###") + " s"); return added;
        }
        internal bool AddAt(Interpolable track, float time)
        {
            if (time < 0 || Single.IsNaN(time) || Single.IsInfinity(time)) return false;
            if (OccupiedAt(track, time)) return false;
            Call("AddKeyframe", track, time); return track.keyframes.ContainsKey(time);
        }

        sealed class GridState
        {
            internal PointerEventData.InputButton button;
            internal int insertionDepth;
            internal List<GameObject> inactive = new List<GameObject>();
        }
        static bool GridPrefix(PointerEventData eventData, out GridState __state)
        {
            __state = null; Plugin p = Self;
            if (p == null || !p.attached || eventData == null) return true;
            __state = new GridState(); __state.button = eventData.button; __state.insertionDepth = routedInsertionDepth;
            bool routed = System.Object.ReferenceEquals(p.keyboardAddEvent, eventData) || p.MatchesAddGesture(eventData);
            if (routed)
            {
                routedInsertionDepth++;
                eventData.button = PointerEventData.InputButton.Middle;
            }
            if (eventData.button == PointerEventData.InputButton.Middle && TextFocused())
            { eventData.button = PointerEventData.InputButton.Left; return false; }
            if (p.rightCandidates.Value)
                foreach (object d in Get<IList>(p.native, "_displayedInterpolableModels"))
                {
                    GameObject go = Get<GameObject>(d, "gameObject");
                    if (go.activeSelf) { __state.inactive.Add(go); go.SetActive(false); }
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
                        GameObject go = Get<GameObject>(d, "gameObject"); if (!go.activeSelf) continue;
                        RectTransform rt = (RectTransform)go.transform;
                        if (Math.Abs(local.y - rt.anchoredPosition.y) <= rt.rect.height / 2f + .1f) { hit = true; break; }
                    }
                    if (!hit && p.rightCandidates.Value) { eventData.button = PointerEventData.InputButton.Left; return false; }
                }
            }
            p.trackFocus = true; return true;
        }
        static void GridPostfix() { }
        static Exception GridFinalizer(PointerEventData eventData, GridState __state, Exception __exception)
        {
            if (__state != null)
            {
                eventData.button = __state.button;
                routedInsertionDepth = __state.insertionDepth;
                foreach (GameObject go in __state.inactive) if (go != null) go.SetActive(true);
            }
            return __exception;
        }

        void HookKeys()
        {
            // Native key right-click (seek) and native modifier gestures intentionally remain unchanged.
            // Only the empty-grid handler routes configured insertion; key events are never rewritten to Middle.
            foreach (object d in Get<IList>(native, "_displayedKeyframes"))
            {
                object captured = d; GameObject go = Get<GameObject>(d, "gameObject"); if (keyActions.ContainsKey(go)) continue;
                PointerAction action = go.AddComponent<PointerAction>(); action.action = delegate(PointerEventData e)
                {
                    if (e.button != PointerEventData.InputButton.Left || Ctrl || Shift || Alt) return;
                    Timeline.Keyframe key = Get<Timeline.Keyframe>(captured, "keyframe"); if (key == null) return;
                    foreach (Key pair in key.parent.keyframes) if (Object.ReferenceEquals(pair.Value, key)) { anchor = pair; range = lastPhase = null; break; }
                    selectedGroups.Clear(); trackFocus = true;
                };
                keyActions.Add(go, action);
            }
        }

        internal sealed class RectSnapshot
        {
            RectTransform rect; Transform parent; int index; Vector2 amin, amax, pivot, position, size; Vector3 scale;
            internal RectSnapshot(RectTransform rt) { rect = rt; parent = rt.parent; index = rt.GetSiblingIndex(); amin = rt.anchorMin; amax = rt.anchorMax; pivot = rt.pivot; position = rt.anchoredPosition; size = rt.sizeDelta; scale = rt.localScale; }
            internal void Restore() { if (rect == null || parent == null) return; rect.SetParent(parent, false); rect.SetSiblingIndex(index); rect.anchorMin = amin; rect.anchorMax = amax; rect.pivot = pivot; rect.anchoredPosition = position; rect.sizeDelta = size; rect.localScale = scale; }
        }
    }
    public sealed class PointerAction : MonoBehaviour, IPointerDownHandler
    {
        public Action<PointerEventData> action;
        public void OnPointerDown(PointerEventData e) { if (action != null) action(e); }
    }
}
