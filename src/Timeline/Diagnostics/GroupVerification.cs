using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Timeline;
using Key = System.Collections.Generic.KeyValuePair<float, Timeline.Keyframe>;

namespace GinTimelineHack
{
    public sealed partial class Plugin
    {
        // Invoked only by the existing marked, empty-scene isolated verifier.
        void RunGroupInteractionTests(StringBuilder result, object ga, object gb, Interpolable a, Interpolable b, Interpolable c, Interpolable d)
        {
            Interpolable[] tracks = new Interpolable[] { a, b, c, d };
            Dictionary<Interpolable, List<Key>> saved = new Dictionary<Interpolable, List<Key>>();
            foreach (Interpolable track in tracks)
            {
                saved.Add(track, new List<Key>(track.keyframes));
            }

            List<Key> selection = ReadSelection();
            bool expanded = Get<bool>(Get<object>(ga, "obj"), "expanded");
            bool oldSummaries = summaries.Value;
            summaries.Value = true;
            Toggle allToggle = Get<Toggle>(native, "_allToggle");
            bool oldAll = allToggle.isOn;
            bool oldPerformance = PerformanceModeActive;
            try
            {
                // Camera interpolables can be hidden by their own ShouldShow predicate in the
                // native filtered view. The mixed-drag fixture requires one actually visible key.
                allToggle.isOn = true;
                Set(Get<object>(ga, "obj"), "expanded", false);
                FlushGroupFixtureView();
                GroupGraphic row = groupRows[ga].row;
                Select(new List<Key>());
                RefreshGroupSelection();
                AssertGroupMeshColor(row, Color.red);
                List<Key> groupKeys = new List<Key>();
                groupKeys.AddRange(a.keyframes);
                groupKeys.AddRange(b.keyframes);
                Select(groupKeys);
                AssertGroupMeshColor(row, Color.green);
                Select(new List<Key>());
                AssertGroupMeshColor(row, Color.red);
                result.AppendLine("PASS group marker mesh: inactive red, native selected keys green, clearing selection restores red immediately");

                PointerEventData begin = GroupAreaEvent(row.rectTransform, 0, row.rectTransform.rect.height * .45f);
                PointerEventData end = GroupAreaEvent(row.rectTransform, 6, -row.rectTransform.rect.height * .45f);
                Call("PotentiallyBeginAreaSelect", begin);
                Call("BeginAreaSelect", begin);
                Call("UpdateAreaSelect", end);
                Call("EndAreaSelect", end);
                Require(ReadSelection().Count == groupKeys.Count, "collapsed group included by real native rectangle handler");
                foreach (Key k in groupKeys)
                {
                    Require(ReadSelection().Contains(k), "collapsed rectangle selects original descendant objects");
                }

                result.AppendLine("PASS collapsed group rectangle: real native area event selects all represented descendant keys without expanding");

                Set(Get<object>(ga, "obj"), "expanded", true);
                FlushGroupFixtureView();
                row = groupRows[ga].row;
                float top = row.rectTransform.anchoredPosition.y + row.rectTransform.rect.height;
                float bottom = top;
                foreach (Interpolable track in new Interpolable[] { a, b })
                {
                    object display = FindDisplay(track);
                    Require(display != null, "expanded descendant display exists");
                    RectTransform rt = (RectTransform)Get<GameObject>(display, "gameObject").transform;
                    bottom = Mathf.Min(bottom, rt.anchoredPosition.y - rt.rect.height);
                }
                Require(GroupAreaExtraKeys(0, 6, bottom, top).Count == 0, "expanded rectangle deduplicates leaf and group row hits");
                result.AppendLine("PASS expanded group rectangle: keys already hit by native leaf rows are not counted or Ctrl-toggled twice");

                Set(Get<object>(ga, "obj"), "expanded", false);
                FlushGroupFixtureView();
                row = groupRows[ga].row;
                Select(groupKeys);
                Dictionary<Timeline.Keyframe, object> values = new Dictionary<Timeline.Keyframe, object>();
                Dictionary<Timeline.Keyframe, UnityEngine.Keyframe[]> curves = new Dictionary<Timeline.Keyframe, UnityEngine.Keyframe[]>();
                foreach (Key k in groupKeys)
                {
                    values[k.Value] = k.Value.value;
                    curves[k.Value] = k.Value.curve.keys;
                }
                float reference = groupKeys[0].Key;
                begin = EventAt(row.rectTransform, reference, PointerEventData.InputButton.Left);
                end = EventAt(row.rectTransform, reference + .75f, PointerEventData.InputButton.Left);
                end.pressPosition = begin.position;
                row.OnInitializePotentialDrag(begin);
                row.BeginDragWithModifiers(begin, true);
                Require(groupDrag != null, "summary Alt drag enters custom native-key move");
                row.OnDrag(end);
                foreach (Key k in groupKeys)
                {
                    Require(Exists(k), "drag preview does not mutate scene data");
                }

                float delta = groupDrag.delta;
                row.OnEndDrag(end);
                Require(
                    Mathf.Abs(delta - .75f) < .0001f && ReadSelection().Count == groupKeys.Count,
                    "collapsed summary drag commits all selected keys");
                foreach (Key k in groupKeys)
                {
                    Require(
                        k.Value.parent.keyframes.ContainsKey(k.Key + delta) && k.Value.parent.keyframes[k.Key + delta] == k.Value,
                        "group drag preserves native key object");
                    Require(
                        System.Object.Equals(values[k.Value], k.Value.value) && CurvesEqual(curves[k.Value], k.Value.curve.keys),
                        "group drag preserves key value and curve");
                }
                Require(!Get<bool>(Get<object>(ga, "obj"), "expanded"), "drag does not expand collapsed group");
                result.AppendLine("PASS summary Alt-drag event path: collapsed children move together, preview is non-mutating, key values/curves/selection/group state retained");

                List<Key> shifted = ReadSelection();
                float blockerTime = shifted[0].Key + 8;
                Require(AddAt(shifted[0].Value.parent, blockerTime), "collision fixture inserted");
                Timeline.Keyframe blocker = shifted[0].Value.parent.keyframes[blockerTime];
                Require(!MoveGroupKeys(shifted, 8), "group move collision blocks complete operation");
                foreach (Key k in shifted)
                {
                    Require(Exists(k), "collision leaves all selected keys at original times");
                }

                Require(shifted[0].Value.parent.keyframes[blockerTime] == blocker, "collision does not overwrite blocking key");
                shifted[0].Value.parent.keyframes.Remove(blockerTime);
                result.AppendLine("PASS group move collision: complete move rejected before mutation; selected and blocking keys preserved");

                Require(AddAt(d, 6.25f), "visible mixed-selection fixture inserted");
                List<Key> mixed = new List<Key>(shifted);
                mixed.Add(new Key(6.25f, d.keyframes[6.25f]));
                Select(mixed);
                FlushGroupFixtureView();
                float visibleTime = 6.25f;
                if (PerformanceModeActive)
                {
                    VerifyPerformanceMixedDrag(result, d, visibleTime, mixed);
                    mixed = ReadSelection();
                    visibleTime = d.keyframes.Keys[0];
                    shalltyPerformanceType.GetMethod("TogglePerformanceMode", StaticFlags).Invoke(null, null);
                    Require(!PerformanceModeActive, "native key-display fixture uses actual provider mode toggle");
                    FlushGroupFixtureView();
                }
                object nativeDisplay = null;
                foreach (object candidate in Get<System.Collections.IList>(native, "_displayedKeyframes"))
                {
                    if (Get<Timeline.Keyframe>(candidate, "keyframe") == d.keyframes[visibleTime])
                    {
                        nativeDisplay = candidate;
                    }
                }

                if (nativeDisplay == null)
                {
                    object tree = Get<object>(native, "_interpolablesTree");
                    INode leaf = (INode)tree.GetType().GetMethod("GetLeafNode", Flags).Invoke(tree, new object[] { d });
                    StringBuilder ancestry = new StringBuilder();
                    IGroupNode parent = leaf.parent;
                    while (parent != null)
                    {
                        object obj = Get<object>(parent, "obj");
                        ancestry.Append(obj == null ? "<root>" : Get<string>(obj, "name") + ":" + Get<bool>(obj, "expanded"));
                        ancestry.Append("/");
                        parent = parent.parent;
                    }
                    result.AppendLine("INFO mixed fixture: id=" + d.id + "; name=" + d.name + "; ShouldShow=" + d.ShouldShow() + "; All=" + allToggle.isOn + "; leafDisplay=" + (FindDisplay(d) != null) + "; rootLeaf=" + RootNodes().Contains(leaf) + "; ancestors=" + ancestry);
                }
                Require(nativeDisplay != null, "mixed selection visible native key display exists after native grid flush");
                Require(
                    Get<GameObject>(nativeDisplay, "gameObject").activeInHierarchy,
                    "mixed selection visible native key is active");
                Require(HasHiddenSelectedKeys(), "mixed selection contains selected collapsed keys with no visible display");
                NativeDragBridge bridge = null;
                foreach (NativeDragBridge candidate in nativeGroupDrags.Values)
                {
                    if (System.Object.ReferenceEquals(candidate.Display, nativeDisplay))
                    {
                        bridge = candidate;
                        break;
                    }
                }

                Require(bridge != null, "native key drag delegates bridged");
                RectTransform visibleRow = (RectTransform)Get<GameObject>(FindDisplay(d), "gameObject").transform;
                begin = EventAt(visibleRow, visibleTime, PointerEventData.InputButton.Left);
                end = EventAt(visibleRow, visibleTime + .5f, PointerEventData.InputButton.Left);
                end.pressPosition = begin.position;
                bridge.BeginWithModifiers(begin, true);
                Require(groupDrag != null, "native Alt drag takes safe hidden-key path");
                bridge.Drag(end);
                float mixedDelta = groupDrag.delta;
                bridge.End(end);
                foreach (Key k in mixed)
                {
                    Require(
                        k.Value.parent.keyframes.ContainsKey(k.Key + mixedDelta) && k.Value.parent.keyframes[k.Key + mixedDelta] == k.Value,
                        "mixed drag moves hidden and visible native keys");
                }

                result.AppendLine("PASS mixed Alt-drag: native visible-key handler safely moves selected collapsed-group keys without missing-display exception");

                List<Key> finalKeys = ReadSelection();
                begin = EventAt(groupRows[ga].row.rectTransform, finalKeys[0].Key, PointerEventData.InputButton.Left);
                Require(
                    BeginGroupKeyDrag((IGroupNode)ga, finalKeys[0].Key, begin, groupRows[ga].row.gameObject),
                    "cancel fixture drag starts");
                end = EventAt(groupRows[ga].row.rectTransform, finalKeys[0].Key + 2, PointerEventData.InputButton.Left);
                end.pressPosition = begin.position;
                UpdateGroupKeyDrag(end);
                CancelGroupKeyDrag();
                foreach (Key k in finalKeys)
                {
                    Require(Exists(k), "cancel leaves scene data unchanged");
                }

                result.AppendLine("PASS group drag cancel: preview discarded and all original key times retained");
            }
            finally
            {
                RestorePerformanceDragState(groupDrag);
                groupDrag = null;
                foreach (Interpolable track in tracks)
                {
                    track.keyframes.Clear();
                    foreach (Key k in saved[track])
                    {
                        track.keyframes.Add(k.Key, k.Value);
                    }
                }
                Set(Get<object>(ga, "obj"), "expanded", expanded);
                summaries.Value = oldSummaries;
                allToggle.isOn = oldAll;
                if (shalltyPerformanceType != null && PerformanceModeActive != oldPerformance)
                {
                    shalltyPerformanceType.GetMethod("TogglePerformanceMode", StaticFlags).Invoke(null, null);
                }

                Select(selection);
                FlushGroupFixtureView();
            }
        }

        static Vector3 performanceTestMouse;

        static Vector3 PerformanceTestMousePosition()
        {
            return performanceTestMouse;
        }

        static IEnumerable<CodeInstruction> PerformanceTestMouseTranspiler(IEnumerable<CodeInstruction> code)
        {
            MethodInfo getter = typeof(Input).GetProperty("mousePosition").GetGetMethod();
            MethodInfo replacement = typeof(Plugin).GetMethod("PerformanceTestMousePosition", StaticFlags);
            foreach (CodeInstruction original in code)
            {
                CodeInstruction instruction = new CodeInstruction(original);
                if (instruction.opcode == OpCodes.Call && System.Object.Equals(instruction.operand, getter))
                {
                    instruction.operand = replacement;
                }

                yield return instruction;
            }
        }

        void VerifyPerformanceMixedDrag(StringBuilder result, Interpolable visibleTrack, float time, List<Key> mixed)
        {
            Require(
                PerformanceModeActive && HasHiddenSelectedKeys(),
                "performance fixture includes folded selected descendants");
            RectTransform row = (RectTransform)Get<GameObject>(FindDisplay(visibleTrack), "gameObject").transform;
            PointerEventData begin = EventAt(row, time, PointerEventData.InputButton.Left);
            PointerEventData end = EventAt(row, time + .5f, PointerEventData.InputButton.Left);
            end.pressPosition = begin.position;
            performanceTestMouse = begin.position;
            Harmony probe = new Harmony(Guid + ".performance-hover-test");
            MethodInfo update = shalltyPerformanceType.GetMethod("UpdateKeyframes", StaticFlags);
            try
            {
                probe.Patch(update, null, null, H("PerformanceTestMouseTranspiler"), null, null);
                // The provider's separate vector-line hover predicate also reads mousePosition.
                // Drive both actual hit-test sites with the same pointer, otherwise its final
                // no-hover branch deactivates the fixture widget before a drag can begin.
                Type predicates = shalltyPerformanceType.GetNestedType("<>c", BindingFlags.NonPublic);
                MethodInfo hover = predicates == null ? null : predicates.GetMethod("<UpdateKeyframes>b__15_0", Flags);
                Require(
                    hover != null && hover.ReturnType == typeof(bool) && hover.GetParameters().Length == 1 && hover.GetParameters()[0].ParameterType.FullName == "Vectrosity.VectorLine",
                    "verified performance hover-predicate contract");
                probe.Patch(hover, null, null, H("PerformanceTestMouseTranspiler"), null, null);
                Canvas.ForceUpdateCanvases();
                update.Invoke(null, null);
                Canvas.ForceUpdateCanvases();
                update.Invoke(null, null);
                object display = performanceDisplayField.GetValue(null);
                Require(
                    display != null && Get<Timeline.Keyframe>(display, "keyframe") == visibleTrack.keyframes[time],
                    "actual Shallty performance hover targets fixture key");
                Require(
                    Get<GameObject>(display, "gameObject").activeInHierarchy,
                    "actual performance hover widget active before drag begins");
                NativeDragBridge bridge = null;
                foreach (NativeDragBridge candidate in nativeGroupDrags.Values)
                {
                    if (System.Object.ReferenceEquals(candidate.Display, display))
                    {
                        bridge = candidate;
                        break;
                    }
                }

                Require(bridge != null, "performance hover drag delegates bridged after provider rewrites");
                bridge.BeginWithModifiers(begin, true);
                Require(
                    groupDrag != null && (bool)performanceDraggingField.GetValue(null),
                    "performance drag retains hover widget while selected descendants are hidden");
                bridge.Drag(end);
                float delta = groupDrag.delta;
                foreach (Key k in mixed)
                {
                    Require(Exists(k), "performance preview preserves original native scene keys");
                }

                IList<Vector2> preview = null;
                foreach (object line in (System.Collections.IList)performanceSelectedLinesField.GetValue(null))
                {
                    List<Vector2> points = Get<List<Vector2>>(line, "points2");
                    if (points.Count > 0)
                    {
                        preview = points;
                        break;
                    }
                }
                Require(
                    preview != null && Mathf.Abs(preview[0].x - (time + delta) * 30f * Get<float>(native, "_zoomLevel")) < .001f,
                    "performance green glyph preview follows drag");
                performanceTestMouse = end.position;
                update.Invoke(null, null);
                Require(
                    Get<GameObject>(display, "gameObject").activeSelf,
                    "performance hover widget remains active away from original key");
                bridge.End(end);
                foreach (Key k in mixed)
                {
                    Require(
                        k.Value.parent.keyframes.ContainsKey(k.Key + delta) && k.Value.parent.keyframes[k.Key + delta] == k.Value,
                        "performance mixed drag commits hidden and visible native keys");
                }

                Require(!(bool)performanceDraggingField.GetValue(null), "performance drag state restored");
                result.AppendLine("PASS Shallty performance-mode mixed Alt-drag: actual hover UI, rewritten delegate bridge, visible glyph preview, folded descendants, checked native commit and drag-state restoration");
            }
            finally
            {
                probe.UnpatchSelf();
            }
        }

        void FlushGroupFixtureView()
        {
            // Native UpdateInterpolablesView queues UpdateGrid for a later frame; these regression
            // calls run synchronously, so complete the same native grid step before reading displays.
            Call("UpdateInterpolablesView");
            Canvas.ForceUpdateCanvases();
            Call("UpdateGrid");
            Canvas.ForceUpdateCanvases();
            RefreshGroups();
        }

        PointerEventData GroupAreaEvent(RectTransform row, float time, float yOffset)
        {
            PointerEventData e = EventAt(row, time, PointerEventData.InputButton.Left);
            e.position = RectTransformUtility.WorldToScreenPoint(
                null,
                grid.TransformPoint(new Vector3(time * 30 * Get<float>(native, "_zoomLevel"), row.anchoredPosition.y + yOffset, 0)));
            e.pressPosition = e.position;
            return e;
        }

        static void AssertGroupMeshColor(GroupGraphic row, Color expected)
        {
            VertexHelper helper = new VertexHelper();
            Mesh mesh = new Mesh();
            try
            {
                typeof(GroupGraphic).GetMethod("OnPopulateMesh", Flags, null, new Type[] { typeof(VertexHelper) }, null).Invoke(row, new object[] { helper });
                helper.FillMesh(mesh);
                Color32 color = expected;
                Color32[] colors = mesh.colors32;
                Require(colors.Length > 4, "summary mesh contains keys");
                for (int i = 4; i < colors.Length; i++)
                {
                    Require(
                        colors[i].r == color.r && colors[i].g == color.g && colors[i].b == color.b,
                        "summary mesh key color follows native selection");
                }
            }
            finally
            {
                helper.Dispose();
                UnityEngine.Object.Destroy(mesh);
            }
        }
    }
}
