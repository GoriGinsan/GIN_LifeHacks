using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;
using Timeline;
using Key = System.Collections.Generic.KeyValuePair<float, Timeline.Keyframe>;
using Native = Timeline.Timeline;

namespace GinTimelineHack
{
    public sealed partial class Plugin
    {
        internal static bool Alt
        {
            get
            {
                return Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
            }
        }

        GroupDragState groupDrag;
        readonly Dictionary<Component, NativeDragBridge> nativeGroupDrags = new Dictionary<Component, NativeDragBridge>();
        MethodInfo nativeTryMoveKeyframe;
        RectTransform nativeSelectionArea;
        int nativeSelectionSibling;
        Type shalltyPerformanceType;
        FieldInfo performanceModeField, performanceDisplayField, performanceKeysField, performanceDraggingField;
        FieldInfo performancePointsField, performanceLinesField, performanceSelectedLinesField;
        MethodInfo performanceGenerateLines;

        void InstallGroupInteraction()
        {
            // Verified names/signatures in Timeline 1.4.2 and 1.5.6. Groups remain display-only.
            RequireField(typeof(Native), "_areaSelectFirstPoint", typeof(Vector2), false);
            RequireField(typeof(Native), "_isAreaSelecting", typeof(bool), false);
            RequireField(typeof(Native), "_selectionArea", typeof(RectTransform), false);
            foreach (string name in new string[] { "PotentiallyBeginAreaSelect", "BeginAreaSelect", "UpdateAreaSelect", "EndAreaSelect" })
            {
                RequireMethod(typeof(Native), name, typeof(void), false, typeof(PointerEventData));
            }

            RequireMethod(typeof(Native), "UpdateKeyframeSelection", typeof(void), false);
            Patch("EndAreaSelect", "GroupAreaPrefix", "GroupAreaPostfix", null);
            Patch("BeginAreaSelect", null, "GroupAreaBeginPostfix", null);
            Patch("UpdateAreaSelect", null, "GroupAreaBeginPostfix", null);
            Patch("UpdateKeyframeSelection", null, "GroupSelectionPostfix", null);
            harmony.Patch(Method("SelectKeyframes", typeof(IEnumerable<Key>)), null, H("GroupSelectionPostfix"), null, null, null);
            nativeTryMoveKeyframe = typeof(Native).GetMethod("TryMoveKeyframe", Flags, null, new Type[] { typeof(Timeline.Keyframe), typeof(float) }, null);
            nativeSelectionArea = Get<RectTransform>(native, "_selectionArea");
            nativeSelectionSibling = nativeSelectionArea == null ? 0 : nativeSelectionArea.GetSiblingIndex();
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.GetName().Name != "KK_ShalltyUtils")
                {
                    continue;
                }

                shalltyPerformanceType = assembly.GetType("ShalltyUtils.PerformanceMode", false);
                if (shalltyPerformanceType == null)
                {
                    continue;
                }

                performanceModeField = RequireField(shalltyPerformanceType, "performanceMode", typeof(bool), true);
                performanceDisplayField = RequireField(
                    shalltyPerformanceType,
                    "display",
                    typeof(Native).GetNestedType("KeyframeDisplay", BindingFlags.NonPublic),
                    true);
                performanceKeysField = RequireField(shalltyPerformanceType, "allKeyframes", typeof(List<Key>), true);
                performanceDraggingField = RequireField(shalltyPerformanceType, "isDraggin", typeof(bool), true);
                performancePointsField = RequireField(shalltyPerformanceType, "allKeyframesPoints", typeof(List<Vector2>), true);
                performanceLinesField = shalltyPerformanceType.GetField("allKeyframesLines", StaticFlags);
                performanceSelectedLinesField = shalltyPerformanceType.GetField("allSelectedKeyframesLines", StaticFlags);
                if (performanceLinesField == null || performanceSelectedLinesField == null || performanceLinesField.FieldType != performanceSelectedLinesField.FieldType)
                {
                    throw new NotSupportedException("ShalltyUtils performance line contract differs");
                }

                performanceGenerateLines = RequireMethod(
                    shalltyPerformanceType,
                    "GenerateKeyframesLines",
                    typeof(void),
                    true,
                    performanceLinesField.FieldType,
                    typeof(List<Vector2>));
                MethodInfo update = RequireMethod(shalltyPerformanceType, "UpdateKeyframes", typeof(void), true);
                harmony.Patch(update, null, H("GroupPerformanceUpdated"), null, null, null);
            }
        }

        void ResetGroupInteraction()
        {
            RestorePerformanceDragState(groupDrag);
            groupDrag = null;
            foreach (NativeDragBridge bridge in nativeGroupDrags.Values)
            {
                bridge.Restore();
            }

            nativeGroupDrags.Clear();
            if (nativeSelectionArea != null)
            {
                nativeSelectionArea.SetSiblingIndex(nativeSelectionSibling);
            }

            nativeSelectionArea = null;
            shalltyPerformanceType = null;
            performanceModeField = performanceDisplayField = performanceKeysField = performanceDraggingField = null;
            performancePointsField = performanceLinesField = performanceSelectedLinesField = null;
            performanceGenerateLines = null;
        }

        void PollGroupInteraction()
        {
            if (groupDrag != null && (Input.GetKeyDown(KeyCode.Escape) || groupDrag.owner == null || !groupDrag.owner.activeInHierarchy || !Native.InterfaceVisible))
            {
                CancelGroupKeyDrag();
            }
        }

        internal void PopulateGroupSummary(GroupGraphic row)
        {
            HashSet<Timeline.Keyframe> active = SelectedIdentities();
            SortedDictionary<float, bool> states = new SortedDictionary<float, bool>();
            foreach (Interpolable track in Descendants(row.group))
            {
                foreach (Key pair in track.keyframes)
                {
                    float time = pair.Key;
                    if (groupDrag != null && groupDrag.identities.Contains(pair.Value))
                    {
                        time += groupDrag.delta;
                    }

                    bool wasSelected;
                    states.TryGetValue(time, out wasSelected);
                    states[time] = wasSelected || active.Contains(pair.Value);
                }
            }

            row.times = new List<float>(states.Keys);
            row.selectedTimes.Clear();
            foreach (KeyValuePair<float, bool> state in states)
            {
                if (state.Value)
                {
                    row.selectedTimes.Add(state.Key);
                }
            }
        }

        internal bool AddGroupAtPointer(Vector2 screen, Camera camera)
        {
            foreach (GroupUi ui in groupRows.Values)
            {
                if (ui.row == null || !ui.row.gameObject.activeInHierarchy || !RectTransformUtility.RectangleContainsScreenPoint(ui.row.rectTransform, screen, camera))
                {
                    continue;
                }

                Vector2 local;
                if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(ui.row.rectTransform, screen, camera, out local))
                {
                    return false;
                }

                AddGroupKeys((IGroupNode)ui.group, TimeForAdd(local.x - ui.row.rectTransform.rect.xMin, true));
                return true;
            }
            return false;
        }

        HashSet<Timeline.Keyframe> SelectedIdentities()
        {
            HashSet<Timeline.Keyframe> set = new HashSet<Timeline.Keyframe>();
            foreach (Key k in ReadSelection())
            {
                set.Add(k.Value);
            }

            return set;
        }

        void RefreshGroupSelection()
        {
            foreach (GroupUi ui in groupRows.Values)
            {
                if (ui.row == null)
                {
                    continue;
                }

                PopulateGroupSummary(ui.row);
                ui.row.SetVerticesDirty();
            }
        }

        static void GroupSelectionPostfix()
        {
            Plugin p = Self;
            if (p != null && p.attached)
            {
                p.RefreshGroupSelection();
            }
        }

        static void GroupAreaBeginPostfix()
        {
            Plugin p = Self;
            if (p != null && p.attached && p.nativeSelectionArea != null)
            {
                p.nativeSelectionArea.SetAsLastSibling();
            }
        }

        static void GroupPerformanceUpdated()
        {
            Plugin p = Self;
            if (p != null && p.attached)
            {
                p.HookPerformanceDrag();
            }
        }

        bool PerformanceModeActive
        {
            get
            {
                return performanceModeField != null && (bool)performanceModeField.GetValue(null);
            }
        }

        sealed class GroupAreaState
        {
            internal List<Key> extra;
            internal bool toggle;
        }

        static void GroupAreaPrefix(PointerEventData e, out GroupAreaState __state)
        {
            __state = null;
            Plugin p = Self;
            if (p == null || !p.attached || !p.summaries.Value || e == null)
            {
                return;
            }

            Vector2 end;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(p.grid, e.position, e.pressEventCamera, out end))
            {
                return;
            }

            Vector2 start = Get<Vector2>(p.native, "_areaSelectFirstPoint");
            float scale = 30f * Get<float>(p.native, "_zoomLevel");
            float first = start.x / scale, last = end.x / scale;
            // Follow native area-selection rounding, vertical coverage and Ctrl toggling exactly.
            if (Input.GetKey(KeyCode.LeftShift))
            {
                last = p.SnapGroupTime(last);
            }

            float minY = Mathf.Min(start.y, end.y), maxY = Mathf.Max(start.y, end.y);
            if (Input.GetKey(KeyCode.LeftAlt))
            {
                minY = p.grid.rect.yMin;
                maxY = p.grid.rect.yMax;
            }
            __state = new GroupAreaState();
            __state.toggle = Input.GetKey(KeyCode.LeftControl);
            __state.extra = p.GroupAreaExtraKeys(Mathf.Min(first, last), Mathf.Max(first, last), minY, maxY);
        }




        // Exclude leaf rows already visited by native area selection. Otherwise Ctrl would toggle
        // expanded children twice, and nested groups would select the same key more than once.
        internal List<Key> GroupAreaExtraKeys(float first, float last, float minY, float maxY)
        {
            HashSet<Timeline.Keyframe> nativeHits = new HashSet<Timeline.Keyframe>();
            foreach (object display in Get<IList>(native, "_displayedInterpolables"))
            {
                GameObject go = Get<GameObject>(display, "gameObject");
                if (!go.activeSelf)
                {
                    break;
                }

                float y = ((RectTransform)go.transform).anchoredPosition.y;
                if (y <= minY || y >= maxY)
                {
                    continue;
                }

                Interpolable track = Get<LeafNode<Interpolable>>(display, "interpolable").obj;
                foreach (Key k in track.keyframes)
                {
                    if (k.Key >= first && k.Key <= last)
                    {
                        nativeHits.Add(k.Value);
                    }
                }
            }
            List<Key> result = new List<Key>();
            HashSet<Timeline.Keyframe> seen = new HashSet<Timeline.Keyframe>(nativeHits);
            foreach (GroupUi ui in groupRows.Values)
            {
                if (ui.row == null || !ui.row.gameObject.activeInHierarchy)
                {
                    continue;
                }

                float y = ui.row.rectTransform.anchoredPosition.y;
                if (y <= minY || y >= maxY)
                {
                    continue;
                }

                foreach (Interpolable track in Descendants((IGroupNode)ui.group))
                {
                    foreach (Key k in track.keyframes)
                    {
                        if (k.Key >= first && k.Key <= last && seen.Add(k.Value))
                        {
                            result.Add(k);
                        }
                    }
                }
            }
            return result;
        }

        static void GroupAreaPostfix(GroupAreaState __state)
        {
            Plugin p = Self;
            if (p == null || !p.attached || __state == null || __state.extra.Count == 0)
            {
                return;
            }

            List<Key> selected = p.ReadSelection();
            foreach (Key k in __state.extra)
            {
                int at = selected.FindIndex(delegate (Key current)
                {
                    return current.Value == k.Value;
                });
                if (at >= 0)
                {
                    if (__state.toggle)
                    {
                        selected.RemoveAt(at);
                    }
                }
                else
                {
                    selected.Add(k);
                }
            }
            p.Select(selected);
            p.RefreshGroupSelection();
            p.trackFocus = true;
        }

        float SnapGroupTime(float time)
        {
            float beat = Get<float>(native, "_blockLength") / Get<int>(native, "_divisions");
            if (beat <= 0)
            {
                return time;
            }

            float mod = time % beat;
            return mod / beat > .5f ? time + beat - mod : time - mod;
        }

        sealed class GroupDragState
        {
            internal List<Key> originals;
            internal HashSet<Timeline.Keyframe> identities;
            internal float startX, referenceTime, delta, minimumTime;
            internal GameObject owner;
            internal bool savedPerformanceDrag, originalPerformanceDrag;
        }

        internal bool BeginGroupKeyDrag(IGroupNode group, float time, PointerEventData e, GameObject owner)
        {
            HashSet<Timeline.Keyframe> selected = SelectedIdentities();
            bool hitSelected = false;
            foreach (Interpolable track in Descendants(group))
            {
                Timeline.Keyframe key;
                if (track.keyframes.TryGetValue(time, out key) && selected.Contains(key))
                {
                    hitSelected = true;
                }
            }
            if (!hitSelected)
            {
                SelectGroupKeys(group, time);
            }

            return BeginSelectionDrag(time, e, owner);
        }

        bool BeginSelectionDrag(float referenceTime, PointerEventData e, GameObject owner)
        {
            Vector2 point;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(grid, e.pressPosition, e.pressEventCamera, out point))
            {
                return false;
            }

            List<Key> keys = ReadSelection();
            keys.RemoveAll(delegate (Key k)
            {
                return !Exists(k);
            });
            if (keys.Count == 0)
            {
                return false;
            }

            GroupDragState state = new GroupDragState();
            state.originals = keys;
            state.startX = point.x;
            state.referenceTime = referenceTime;
            state.identities = new HashSet<Timeline.Keyframe>();
            state.minimumTime = Single.PositiveInfinity;
            state.owner = owner;
            foreach (Key k in keys)
            {
                state.identities.Add(k.Value);
                state.minimumTime = Mathf.Min(state.minimumTime, k.Key);
            }
            if (PerformanceModeActive)
            {
                state.savedPerformanceDrag = true;
                state.originalPerformanceDrag = (bool)performanceDraggingField.GetValue(null);
                performanceDraggingField.SetValue(null, true);
            }
            groupDrag = state;
            Native.isPlaying = false;
            trackFocus = true;
            return true;
        }

        internal void UpdateGroupKeyDrag(PointerEventData e)
        {
            if (groupDrag == null)
            {
                return;
            }

            Vector2 point;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(grid, e.position, e.pressEventCamera, out point))
            {
                return;
            }

            float delta = (point.x - groupDrag.startX) / (30f * Get<float>(native, "_zoomLevel"));
            if (Shift)
            {
                delta = SnapGroupTime(groupDrag.referenceTime + delta) - groupDrag.referenceTime;
            }

            groupDrag.delta = Mathf.Max(-groupDrag.minimumTime, delta);
            foreach (object display in Get<IList>(native, "_displayedKeyframes"))
            {
                Timeline.Keyframe key = Get<Timeline.Keyframe>(display, "keyframe");
                if (key == null || !groupDrag.identities.Contains(key))
                {
                    continue;
                }

                GameObject go = Get<GameObject>(display, "gameObject");
                if (!go.activeSelf)
                {
                    continue;
                }

                int index = key.parent.keyframes.IndexOfValue(key);
                if (index < 0)
                {
                    continue;
                }

                RectTransform rt = (RectTransform)go.transform;
                rt.anchoredPosition = new Vector2(
                    (key.parent.keyframes.Keys[index] + groupDrag.delta) * 30f * Get<float>(native, "_zoomLevel"),
                    rt.anchoredPosition.y);
            }
            if (PerformanceModeActive)
            {
                PreviewPerformanceKeys();
                object display = performanceDisplayField.GetValue(null);
                if (display != null)
                {
                    Timeline.Keyframe key = Get<Timeline.Keyframe>(display, "keyframe");
                    GameObject go = Get<GameObject>(display, "gameObject");
                    if (key != null && go != null && groupDrag.identities.Contains(key))
                    {
                        int index = key.parent.keyframes.IndexOfValue(key);
                        if (index >= 0)
                        {
                            RectTransform rt = (RectTransform)go.transform;
                            rt.anchoredPosition = new Vector2(
                                (key.parent.keyframes.Keys[index] + groupDrag.delta) * 30f * Get<float>(native, "_zoomLevel"),
                                rt.anchoredPosition.y);
                        }
                    }
                }
            }
            RefreshGroupSelection();
        }

        void PreviewPerformanceKeys()
        {
            List<Key> keys = (List<Key>)performanceKeysField.GetValue(null);
            List<Vector2> originalPoints = (List<Vector2>)performancePointsField.GetValue(null);
            if (keys.Count != originalPoints.Count)
            {
                return;
            }

            float scale = 30f * Get<float>(native, "_zoomLevel");
            List<Vector2> allPoints = new List<Vector2>(originalPoints);
            Dictionary<Timeline.Keyframe, Vector2> visible = new Dictionary<Timeline.Keyframe, Vector2>();
            for (int i = 0; i < keys.Count; i++)
            {
                Vector2 point = originalPoints[i];
                if (groupDrag.identities.Contains(keys[i].Value))
                {
                    point.x = (keys[i].Key + groupDrag.delta) * scale;
                }

                allPoints[i] = point;
                visible[keys[i].Value] = point;
            }
            List<Vector2> selectedPoints = new List<Vector2>();
            foreach (Key k in groupDrag.originals)
            {
                Vector2 point;
                if (visible.TryGetValue(k.Value, out point))
                {
                    selectedPoints.Add(point);
                }
            }
            // Only provider drawing buffers change during drag. Original Timeline keys and
            // Shallty's time/identity list remain untouched until the checked native commit.
            performanceGenerateLines.Invoke(null, new object[] { performanceLinesField.GetValue(null), allPoints });
            performanceGenerateLines.Invoke(null, new object[] { performanceSelectedLinesField.GetValue(null), selectedPoints });
        }

        internal void EndGroupKeyDrag(PointerEventData e)
        {
            if (groupDrag == null)
            {
                return;
            }

            UpdateGroupKeyDrag(e);
            GroupDragState drag = groupDrag;
            RestorePerformanceDragState(drag);
            groupDrag = null;
            MoveGroupKeys(drag.originals, drag.delta);
        }

        internal void CancelGroupKeyDrag()
        {
            if (groupDrag == null)
            {
                return;
            }

            RestorePerformanceDragState(groupDrag);
            groupDrag = null;
            Call("UpdateGrid");
            RefreshGroupSelection();
        }

        void RestorePerformanceDragState(GroupDragState state)
        {
            if (state != null && state.savedPerformanceDrag && performanceDraggingField != null)
            {
                performanceDraggingField.SetValue(null, state.originalPerformanceDrag);
            }
        }

        internal bool MoveGroupKeys(List<Key> originals, float delta)
        {
            if (originals.Count == 0 || Single.IsNaN(delta) || Single.IsInfinity(delta))
            {
                return false;
            }

            HashSet<Timeline.Keyframe> moving = new HashSet<Timeline.Keyframe>();
            Dictionary<Timeline.Keyframe, float> targets = new Dictionary<Timeline.Keyframe, float>();
            foreach (Key k in originals)
            {
                float destination = k.Key + delta;
                if (!Exists(k) || destination < 0 || Single.IsNaN(destination) || Single.IsInfinity(destination))
                {
                    Note("Move cancelled: selected keys changed during drag");
                    RestoreGroupDragView();
                    return false;
                }
                moving.Add(k.Value);
                targets[k.Value] = k.Key + delta;
            }
            foreach (Key k in originals)
            {
                float target = targets[k.Value];
                foreach (Key other in k.Value.parent.keyframes)
                {
                    if (other.Value == k.Value)
                    {
                        continue;
                    }

                    float otherTime = moving.Contains(other.Value) ? targets[other.Value] : other.Key;
                    bool conflict = nativeFindOccupant != null ? Mathf.Approximately(target, otherTime) : target == otherTime;
                    if (!conflict)
                    {
                        continue;
                    }

                    Note("Move blocked: keyframes would overlap existing keys");
                    RestoreGroupDragView();
                    return false;
                }
            }
            if (delta == 0)
            {
                RestoreGroupDragView();
                return true;
            }
            List<Key> ordered = new List<Key>(originals);
            ordered.Sort(delegate (Key a, Key b)
            {
                return delta > 0 ? b.Key.CompareTo(a.Key) : a.Key.CompareTo(b.Key);
            });
            try
            {
                if (nativeTryMoveKeyframe != null)
                {
                    // ShalltyUtils records undo through this native entry point. Move outermost
                    // keys first so a selected neighbour vacates its destination before the next.
                    foreach (Key k in ordered)
                    {
                        if (!(bool)nativeTryMoveKeyframe.Invoke(native, new object[] { k.Value, targets[k.Value] }))
                        {
                            throw new InvalidOperationException("Native Timeline rejected a checked group move");
                        }
                    }
                }
                else
                {
                    foreach (Key k in ordered)
                    {
                        k.Value.parent.keyframes.Remove(k.Key);
                    }

                    foreach (Key k in ordered)
                    {
                        k.Value.parent.keyframes.Add(targets[k.Value], k.Value);
                    }
                }
            }
            catch (Exception error)
            {
                // Restore the same objects, including their values and curves, if a provider fails.
                foreach (Key k in originals)
                {
                    int at = k.Value.parent.keyframes.IndexOfValue(k.Value);
                    if (at >= 0)
                    {
                        k.Value.parent.keyframes.RemoveAt(at);
                    }
                }
                foreach (Key k in originals)
                {
                    if (!k.Value.parent.keyframes.ContainsKey(k.Key))
                    {
                        k.Value.parent.keyframes.Add(k.Key, k.Value);
                    }
                }

                Select(originals);
                RestoreGroupDragView();
                Logger.LogWarning("Group move restored: " + Error(error));
                return false;
            }
            List<Key> selected = new List<Key>();
            foreach (Key k in originals)
            {
                selected.Add(new Key(targets[k.Value], k.Value));
            }

            if (anchor.HasValue && moving.Contains(anchor.Value.Value))
            {
                anchor = new Key(targets[anchor.Value.Value], anchor.Value.Value);
            }

            range = lastPhase = null;
            Select(selected);
            RestoreGroupDragView();
            Note("Moved " + selected.Count + " keys");
            return true;
        }

        void RestoreGroupDragView()
        {
            Call("UpdateGrid");
            Call("UpdateKeyframeWindow", false);
            RefreshGroupSelection();
            nextRefresh = 0;
        }





        // A visible native key may be dragged while other selected keys are in collapsed groups.
        // Native drag dereferences a missing KeyframeDisplay in that case; bridge only that case.
        void HookNativeGroupDrags()
        {
            foreach (object display in Get<IList>(native, "_displayedKeyframes"))
            {
                HookDisplayDrag(display);
            }

            HookPerformanceDrag();
        }

        void HookPerformanceDrag()
        {
            if (PerformanceModeActive)
            {
                object display = performanceDisplayField.GetValue(null);
                if (display != null)
                {
                    HookDisplayDrag(display);
                }
            }
        }

        void HookDisplayDrag(object display)
        {
            GameObject go = Get<GameObject>(display, "gameObject");
            if (go == null)
            {
                return;
            }

            foreach (Component component in go.GetComponents<Component>())
            {
                if (component == null)
                {
                    continue;
                }

                NativeDragBridge existing;
                if (nativeGroupDrags.TryGetValue(component, out existing))
                {
                    existing.Reinstall();
                    continue;
                }
                {
                    Type type = component.GetType();
                    FieldInfo begin = type.GetField("onBeginDrag", Flags), drag = type.GetField("onDrag", Flags), end = type.GetField("onEndDrag", Flags);
                    if (begin == null || drag == null || end == null || begin.FieldType != typeof(Action<PointerEventData>))
                    {
                        continue;
                    }

                    NativeDragBridge bridge = new NativeDragBridge(this, component, display, begin, drag, end);
                    bridge.Install();
                    nativeGroupDrags.Add(component, bridge);
                }
            }
        }

        bool HasHiddenSelectedKeys()
        {
            HashSet<Timeline.Keyframe> visible = new HashSet<Timeline.Keyframe>();
            if (PerformanceModeActive)
            {
                foreach (Key k in (List<Key>)performanceKeysField.GetValue(null))
                {
                    visible.Add(k.Value);
                }
            }
            else
            {
                foreach (object display in Get<IList>(native, "_displayedKeyframes"))
                {
                    if (Get<GameObject>(display, "gameObject").activeSelf)
                    {
                        visible.Add(Get<Timeline.Keyframe>(display, "keyframe"));
                    }
                }
            }

            foreach (Key k in ReadSelection())
            {
                if (Exists(k) && !visible.Contains(k.Value))
                {
                    return true;
                }
            }

            return false;
        }

        sealed class NativeDragBridge
        {
            readonly Plugin plugin;
            readonly Component component;
            readonly object display;
            readonly FieldInfo beginField, dragField, endField;
            readonly Action<PointerEventData> originalBegin, originalDrag, originalEnd;
            readonly Action<PointerEventData> begin, drag, end;
            bool intercepted;

            internal object Display
            {
                get
                {
                    return display;
                }
            }

            internal NativeDragBridge(Plugin p, Component c, object d, FieldInfo b, FieldInfo move, FieldInfo finish)
            {
                plugin = p;
                component = c;
                display = d;
                beginField = b;
                dragField = move;
                endField = finish;
                originalBegin = (Action<PointerEventData>)b.GetValue(c);
                originalDrag = (Action<PointerEventData>)move.GetValue(c);
                originalEnd = (Action<PointerEventData>)finish.GetValue(c);
                begin = Begin;
                drag = Drag;
                end = End;
            }

            internal void Install()
            {
                beginField.SetValue(component, begin);
                dragField.SetValue(component, drag);
                endField.SetValue(component, end);
            }

            internal void Reinstall()
            {
                // Shallty's performance hover renderer reassigns these same delegates each frame.
                // Reapply only over the originals we captured, leaving another plugin's hooks alone.
                if (System.Object.Equals(beginField.GetValue(component), originalBegin))
                {
                    beginField.SetValue(component, begin);
                }

                if (System.Object.Equals(dragField.GetValue(component), originalDrag))
                {
                    dragField.SetValue(component, drag);
                }

                if (System.Object.Equals(endField.GetValue(component), originalEnd))
                {
                    endField.SetValue(component, end);
                }
            }

            internal void Restore()
            {
                if (component == null)
                {
                    return;
                }

                if (System.Object.Equals(beginField.GetValue(component), begin))
                {
                    beginField.SetValue(component, originalBegin);
                }

                if (System.Object.Equals(dragField.GetValue(component), drag))
                {
                    dragField.SetValue(component, originalDrag);
                }

                if (System.Object.Equals(endField.GetValue(component), end))
                {
                    endField.SetValue(component, originalEnd);
                }
            }

            void Begin(PointerEventData e)
            {
                BeginWithModifiers(e, Alt);
            }

            internal void BeginWithModifiers(PointerEventData e, bool alt)
            {
                intercepted = false;
                if (alt && e.button == PointerEventData.InputButton.Left && !TextFocused() && plugin.HasHiddenSelectedKeys())
                {
                    Timeline.Keyframe key = Get<Timeline.Keyframe>(display, "keyframe");
                    if (key != null)
                    {
                        int index = key.parent.keyframes.IndexOfValue(key);
                        if (index >= 0)
                        {
                            intercepted = plugin.BeginSelectionDrag(key.parent.keyframes.Keys[index], e, component.gameObject);
                        }
                    }
                }
                if (intercepted)
                {
                    e.Use();
                }
                else if (originalBegin != null)
                {
                    originalBegin(e);
                }
            }

            internal void Drag(PointerEventData e)
            {
                if (intercepted)
                {
                    if (Input.GetKey(KeyCode.Escape))
                    {
                        plugin.CancelGroupKeyDrag();
                    }
                    else
                    {
                        plugin.UpdateGroupKeyDrag(e);
                    }

                    e.Use();
                }
                else if (originalDrag != null)
                {
                    originalDrag(e);
                }
            }

            internal void End(PointerEventData e)
            {
                if (intercepted)
                {
                    plugin.EndGroupKeyDrag(e);
                    intercepted = false;
                    e.Use();
                }
                else if (originalEnd != null)
                {
                    originalEnd(e);
                }
            }
        }
    }
}
