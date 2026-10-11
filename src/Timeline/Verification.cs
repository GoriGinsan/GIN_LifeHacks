using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using HarmonyLib;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Timeline;
using Key = System.Collections.Generic.KeyValuePair<float, Timeline.Keyframe>;
using Native = Timeline.Timeline;
namespace GinTimelineHack
{
    public sealed partial class Plugin
    {
        public string SetEnabledForIsolatedTest(bool enabled)
        {
            DirectoryInfo dir = Directory.GetParent(Application.dataPath);
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-ginLifeHacksVerify") < 0 || dir == null || dir.Name != "test-game" || !File.Exists(Path.Combine(dir.FullName, ".kk-workshop-test"))) return "SKIP: isolated verification only";
            enabledSetting.Value = enabled;
            if (enabled) { retryAt = 0; return "PASS Timeline enabled; waiting for normal attach path"; }
            bool ownedPatch = false;
            foreach (string method in new string[] { "UpdateInterpolablesView", "OnKeyframeContainerMouseDown", "UpdateGrid", "UpdateInterpolableSelection" })
            {
                Patches patches = Harmony.GetPatchInfo(Method(method));
                if (patches != null && patches.Owners.Contains(Guid)) ownedPatch = true;
            }
            return !attached && relocated.Count == 0 && owned.Count == 0 && !ownedPatch ? "PASS Timeline OFF: native candidates restored, only owned UI and patches removed" : "FAIL Timeline OFF restoration";
        }
        public string RunIsolatedTests()
        {
            DirectoryInfo dir = Directory.GetParent(Application.dataPath);
            bool argument = Array.IndexOf(Environment.GetCommandLineArgs(), "-ginLifeHacksVerify") >= 0;
            if (!argument || dir == null || dir.Name != "test-game" || !File.Exists(Path.Combine(dir.FullName, ".kk-workshop-test"))) return "SKIP: marked isolated test-game and -ginLifeHacksVerify required";
            if (!attached) return "FAIL: Timeline plugin is not attached";
            if (new List<Interpolable>(Native.GetAllInterpolables(false)).Count != 0 || ReadSelection().Count != 0 || Native.isPlaying) return "SKIP: empty stopped Timeline required";
            if (Ctrl || Shift || Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt)) return "SKIP: release modifier keys";
            StringBuilder result = new StringBuilder(); List<Interpolable> created = new List<Interpolable>(); List<INode> groups = new List<INode>();
            bool oldVisible = Native.InterfaceVisible, oldRight = rightCandidates.Value;
            KeyboardShortcut oldAdd = addShortcut.Value;
            Vector2 oldScroll = Get<ScrollRect>(native, "_verticalScrollView").normalizedPosition;
            Key? oldAnchor = anchor; List<Key> oldRange = range, oldPhase = lastPhase;
            try
            {
                Native.InterfaceVisible = true; rightCandidates.Value = true; addShortcut.Value = new KeyboardShortcut(KeyCode.Mouse1, KeyCode.LeftControl);
                List<InterpolableModel> models = Get<List<InterpolableModel>>(native, "_interpolableModelsList");
                InterpolableModel fov = models.Find(delegate(InterpolableModel m) { return m.owner == "Timeline" && m.id == "cameraFOV"; });
                InterpolableModel pos = models.Find(delegate(InterpolableModel m) { return m.owner == "Timeline" && m.id == "cameraOPos"; });
                Require(fov != null && pos != null, "native camera model discovery");
                float playhead = Native.playbackTime;
                Require(AddCandidate(fov), "right candidate add at playhead");
                Interpolable a = new List<Interpolable>(Native.GetAllInterpolables(false))[0]; created.Add(a);
                Require(a.keyframes.ContainsKey(playhead) && a.keyframes.Count == 1, "candidate time equals playhead");
                Timeline.Keyframe original = a.keyframes[playhead]; object oldValue = original.value;
                Require(!AddCandidate(fov) && a.keyframes.Count == 1 && System.Object.ReferenceEquals(original, a.keyframes[playhead]) && System.Object.Equals(oldValue, original.value), "candidate repeated addition preserves existing key");
                result.AppendLine("PASS right candidate: native track registration at playhead; duplicate is non-destructive");
                // Keep the actual first key in place; choose disjoint times for pointer probes.
                Call("UpdateInterpolablesView"); Canvas.ForceUpdateCanvases(); Call("UpdateGrid"); Canvas.ForceUpdateCanvases();
                object display = FindDisplay(a); Require(display != null, "native registered camera display");
                RectTransform row = (RectTransform)Get<GameObject>(display, "gameObject").transform;
                Get<ScrollRect>(native, "_verticalScrollView").verticalNormalizedPosition = 1; Canvas.ForceUpdateCanvases();
                try { RunInputInteractionTests(result, a, row); }
                catch (Exception inputError) { result.AppendLine("FAIL input: " + Error(inputError)); }
                foreach (object d in Get<IList>(native, "_displayedInterpolableModels"))
                { GameObject go = Get<GameObject>(d, "gameObject"); if (go.activeSelf) Require(go.transform.parent == candidateContent, "active candidate is on right"); }
                Require(row.parent == content, "registered track remains original content");
                result.AppendLine("PASS candidate relocation: native models in right scroll panel; registered tracks stay native");

                // Empty only the fixture's camera keys, then build nonuniform timing to catch index/parity mistakes.
                a.keyframes.Clear();
                Interpolable b = (Interpolable)Call("AddInterpolable", pos); created.Add(b);
                float[][] times = new float[][] { new float[] { .1f, .4f, .9f, 1.7f, 2.1f, 4.8f }, new float[] { .2f, .6f, 1.1f, 1.9f, 3.2f, 4.9f } };
                for (int i = 0; i < 2; i++) foreach (float t in times[i]) AddAt(created[i], t);
                List<Key> all = new List<Key>(); all.AddRange(a.keyframes); all.AddRange(b.keyframes);
                Dictionary<Timeline.Keyframe, object> values = new Dictionary<Timeline.Keyframe, object>(); Dictionary<Timeline.Keyframe, UnityEngine.Keyframe[]> curves = new Dictionary<Timeline.Keyframe, UnityEngine.Keyframe[]>();
                foreach (Key k in all) { values[k.Value] = k.Value.value; curves[k.Value] = k.Value.curve.keys; }
                Select(new List<Key>(new Key[] { new Key(.4f, a.keyframes[.4f]) })); Require(CaptureAnchor(), "capture explicit anchor");
                List<Key> candidates = new List<Key>();
                foreach (Key k in a.keyframes) if (k.Key >= .4f && k.Key != .9f) candidates.Add(k);
                foreach (Key k in b.keyframes) if (k.Key >= 1.1f) candidates.Add(k);
                Select(candidates); Require(SelectAlternating(true), "odd selection"); AssertTimes(a, new float[] { .4f, 1.7f, 4.8f }); AssertTimes(b, new float[] { 1.9f, 4.9f });
                Require(SelectAlternating(false), "even selection"); AssertTimes(a, new float[] { 2.1f }); AssertTimes(b, new float[] { 1.1f, 3.2f });
                Require(SelectAlternating(true), "phase flip");
                foreach (Key k in all) Require(Exists(k) && System.Object.Equals(values[k.Value], k.Value.value) && CurvesEqual(curves[k.Value], k.Value.curve.keys), "alternation never mutates keys");
                result.AppendLine("PASS odd/even: anchor=1; independent track time order; omitted keys counted; phase flips preserve complete key data");
                Select(all); foreach (Key k in a.keyframes) k.Value.value = -123f;
                bool oldTrackFocus = trackFocus, oldAdvancedFocus = advancedFocus;
                trackFocus = advancedFocus = false;
                Require(CanUseShortcut(true), "S eligibility remains after viewport/Move Controller focus");
                Require(CommitCurrentValues(), "S command native UseCurrentValue after focus loss");
                trackFocus = oldTrackFocus; advancedFocus = oldAdvancedFocus;
                foreach (Key k in all) Require(System.Object.Equals(k.Value.parent.GetValue(), k.Value.value) && CurvesEqual(curves[k.Value], k.Value.curve.keys), "S updates all selected values only");
                result.AppendLine("PASS S command after focus loss: all selected native key values updated; times and interpolation curves retained");

                List<InterpolableModel> extra = models.FindAll(delegate(InterpolableModel m) { return m.owner == "Timeline" && m != fov && m != pos && m.IsCompatibleWithTarget(null) && m.id.StartsWith("camera"); });
                Require(extra.Count >= 2, "two additional native camera models");
                Interpolable c = (Interpolable)Call("AddInterpolable", extra[0]); created.Add(c);
                Interpolable dTrack = (Interpolable)Call("AddInterpolable", extra[1]); created.Add(dTrack);
                object tree = Get<object>(native, "_interpolablesTree");
                object ga = CreateTestGroup(tree, new List<Interpolable>(new Interpolable[] { a, b }), "GIN test A"); groups.Add((INode)ga);
                object gb = CreateTestGroup(tree, new List<Interpolable>(new Interpolable[] { c }), "GIN test B"); groups.Add((INode)gb);
                List<float> union = GroupTimes((IGroupNode)ga); Require(union.Count == 12, "aggregate union across descendants");
                Require(AddGroupKeys((IGroupNode)ga, 5.5f) == 2, "group bulk adds every descendant");
                Timeline.Keyframe existingA = a.keyframes[5.5f], existingB = b.keyframes[5.5f];
                Require(AddGroupKeys((IGroupNode)ga, 5.5f) == 0 && System.Object.ReferenceEquals(existingA, a.keyframes[5.5f]) && System.Object.ReferenceEquals(existingB, b.keyframes[5.5f]), "group repeated bulk add preserves keys");
                if (nativeFindOccupant != null)
                {
                    int countA = a.keyframes.Count, countB = b.keyframes.Count;
                    Require(AddGroupKeys((IGroupNode)ga, 5.500001f) == 0 && a.keyframes.Count == countA && b.keyframes.Count == countB && Status.IndexOf("2 existing") >= 0, "latest native near-time collision is treated as existing");
                    result.AppendLine("PASS latest Timeline collision policy: approximately-equal times reuse native FindOccupant and preserve both existing keys");
                }
                Set(Get<object>(ga, "obj"), "expanded", false); Call("UpdateInterpolablesView"); Canvas.ForceUpdateCanvases(); RefreshGroups();
                Require(groupRows.ContainsKey(ga) && groupRows[ga].row.gameObject.activeSelf && groupRows[ga].row.times.Count == 13, "collapsed group retains aggregate markers");
                result.AppendLine("PASS group union: one summary per distinct time, collapsed visible; bulk add and existing-key preservation");
                RunGroupInteractionTests(result, ga, gb, a, b, c, dTrack);
                List<INode> roots = RootNodes(); INode leafD = (INode)tree.GetType().GetMethod("GetLeafNode", Flags).Invoke(tree, new object[] { dTrack });
                roots.Clear(); roots.Add(leafD); roots.Add((INode)ga); roots.Add((INode)gb);
                IGroupNode parentA = ((INode)ga).parent, parentB = ((INode)gb).parent;
                SelectGroup(ga, false); SelectGroup(gb, true); Require(MoveSelected(-1), "multi group move");
                Require(roots[0] == ga && roots[1] == gb && roots[2] == leafD, "selected group block moves up preserving internal order");
                Require(MoveSelected(1) && roots[0] == leafD && roots[1] == ga && roots[2] == gb, "selected group block moves back down");
                Require(((INode)ga).parent == parentA && ((INode)gb).parent == parentB && ((IGroupNode)ga).children.Count == 2, "group move preserves parents and children");
                selectedGroups.Clear(); Call("SelectInterpolable", (object)new Interpolable[] { b }); Require(MoveSelected(-1), "selected leaf moves");
                Require(((LeafNode<Interpolable>)((IGroupNode)ga).children[0]).obj == b, "leaf moves within group only");
                result.AppendLine("PASS Shift Up/Down command: selected sibling group block and leaf reorder; parent/children preserved");
                rightCandidates.Value = false; Require(relocated.Count == 0, "candidate toggle restores original parents");
                rightCandidates.Value = true; Require(relocated.Count > 0, "candidate toggle restores right panel");
                result.AppendLine("PASS candidate panel OFF/ON restores/reapplies native row locations");
                Require(guardedCamera != null && guardedCamera.keyCondition == keyGuard && guardedCamera.noCtrlCondition == mouseGuard && guardedCamera.zoomCondition == zoomGuard, "camera wrappers installed");
                Require(cameraDragging != null && cameraDragging.FieldType == typeof(bool), "camera drag latch contract");
                bool savedDragging = (bool)cameraDragging.GetValue(guardedCamera);
                try
                {
                    cameraDragging.SetValue(guardedCamera, true); ClearReleasedDrag(true, true); Require((bool)cameraDragging.GetValue(guardedCamera), "held mouse retains drag latch");
                    ClearReleasedDrag(false, false); Require((bool)cameraDragging.GetValue(guardedCamera), "viewport release remains native");
                    ClearReleasedDrag(true, false); Require(!(bool)cameraDragging.GetValue(guardedCamera), "release over extension clears drag latch");
                }
                finally { cameraDragging.SetValue(guardedCamera, savedDragging); }
                result.AppendLine("PASS camera guard: prior conditions retained, owned-UI release clears drag latch, held/viewport state preserved");
                result.AppendLine("NOT TESTED by this method: physical mouse delivery, held modifiers, dense production projects, external camera input timing");
            }
            catch (Exception e) { result.AppendLine("FAIL Timeline: " + Error(e)); Logger.LogError(e); }
            finally
            {
                selectedGroups.Clear();
                for (int i = created.Count - 1; i >= 0; i--) try { Call("RemoveInterpolable", created[i]); } catch (Exception e) { result.AppendLine("FAIL cleanup track: " + Error(e)); }
                object tree = Get<object>(native, "_interpolablesTree");
                foreach (INode group in groups) try { tree.GetType().GetMethod("Remove", Flags).Invoke(tree, new object[] { group }); } catch { }
                anchor = oldAnchor; range = oldRange; lastPhase = oldPhase;
                rightCandidates.Value = oldRight; addShortcut.Value = oldAdd; Native.InterfaceVisible = oldVisible;
                Get<ScrollRect>(native, "_verticalScrollView").normalizedPosition = oldScroll; Call("UpdateInterpolablesView");
                if (new List<Interpolable>(Native.GetAllInterpolables(false)).Count == 0) result.AppendLine("PASS cleanup: only fixture tracks/groups removed; native Timeline empty"); else result.AppendLine("FAIL cleanup: tracks remain");
            }
            return result.ToString();
        }
        public string PreparePreview()
        {
            DirectoryInfo dir = Directory.GetParent(Application.dataPath);
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-ginLifeHacksVerify") < 0 || dir == null || dir.Name != "test-game" || !File.Exists(Path.Combine(dir.FullName, ".kk-workshop-test"))) return "SKIP preview: isolated verification only";
            if (!attached || new List<Interpolable>(Native.GetAllInterpolables(false)).Count != 0) return "SKIP preview: empty Timeline required";
            Native.InterfaceVisible = true;
            List<Interpolable> tracks = new List<Interpolable>();
            foreach (InterpolableModel model in Get<List<InterpolableModel>>(native, "_interpolableModelsList"))
            {
                if (model.owner != "Timeline" || (model.id != "cameraFOV" && model.id != "cameraOPos" && model.id != "cameraORot")) continue;
                Interpolable track = (Interpolable)Call("AddInterpolable", model); if (track == null) continue; tracks.Add(track);
                foreach (float time in new float[] { 0, .5f, 1, 1.5f, 2, 3, 4, 5, 6, 8 }) AddAt(track, time);
            }
            if (tracks.Count >= 2)
            {
                object g = CreateTestGroup(Get<object>(native, "_interpolablesTree"), new List<Interpolable>(new Interpolable[] { tracks[0], tracks[1] }), "Camera motion");
                SelectGroup(g, false);
                List<Key> keys = new List<Key>(tracks[0].keyframes); Select(keys); anchor = keys[0]; SelectAlternating(true);
            }
            // Isolated screenshot layout only: the distributed plugin never resizes or relocates the user's Timeline window.
            window.anchorMin = window.anchorMax = new Vector2(0, 1); window.pivot = new Vector2(0, 1); window.anchoredPosition = new Vector2(45, -560); window.sizeDelta = new Vector2(1470, 440);
            Call("UpdateInterpolablesView"); Canvas.ForceUpdateCanvases(); RefreshGroups();
            Get<GameObject>(native, "_keyframeWindow").SetActive(false);
            return "PASS preview: disposable native camera tracks and aggregate group prepared; original game never touched";
        }
        static void Require(bool condition, string text) { if (!condition) throw new InvalidOperationException(text); }
        object FindDisplay(Interpolable track)
        { foreach (object d in Get<IList>(native, "_displayedInterpolables")) if (Get<GameObject>(d, "gameObject").activeSelf && Get<LeafNode<Interpolable>>(d, "interpolable").obj == track) return d; return null; }
        PointerEventData EventAt(RectTransform row, float time, PointerEventData.InputButton button)
        {
            Vector3 point = new Vector3(time * 30 * Get<float>(native, "_zoomLevel"), row.anchoredPosition.y, 0);
            PointerEventData e = new PointerEventData(EventSystem.current); e.button = button; e.pointerId = button == PointerEventData.InputButton.Right ? -2 : -3;
            e.position = RectTransformUtility.WorldToScreenPoint(null, grid.TransformPoint(point)); e.pressPosition = e.position;
            RaycastResult hit = new RaycastResult(); hit.gameObject = grid.gameObject; hit.module = canvas.GetComponent<GraphicRaycaster>(); e.pointerPressRaycast = hit; e.pointerCurrentRaycast = hit; return e;
        }
        static object CreateTestGroup(object tree, List<Interpolable> tracks, string name)
        {
            Type groupType = typeof(Native).GetNestedType("InterpolableGroup", BindingFlags.NonPublic);
            object obj = Activator.CreateInstance(groupType, true); Set(obj, "name", name);
            return tree.GetType().GetMethod("GroupTogether", Flags).Invoke(tree, new object[] { tracks, obj });
        }
        void AssertTimes(Interpolable track, float[] expected)
        { List<float> actual = new List<float>(); foreach (Key k in ReadSelection()) if (k.Value.parent == track) actual.Add(k.Key); Require(actual.Count == expected.Length, "selection count differs"); foreach (float time in expected) Require(actual.Contains(time), "selected time differs: " + time); }
        static bool CurvesEqual(UnityEngine.Keyframe[] a, UnityEngine.Keyframe[] b)
        { if (a.Length != b.Length) return false; for (int i = 0; i < a.Length; i++) if (a[i].time != b[i].time || a[i].value != b[i].value || a[i].inTangent != b[i].inTangent || a[i].outTangent != b[i].outTangent) return false; return true; }
    }
}
