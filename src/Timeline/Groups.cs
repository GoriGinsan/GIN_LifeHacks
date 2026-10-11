using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Timeline;
using Key = System.Collections.Generic.KeyValuePair<float, Timeline.Keyframe>;
using Object = System.Object;
namespace GinTimelineHack
{
    public sealed partial class Plugin
    {
        internal sealed class GroupUi
        {
            internal object group, header;
            internal RectTransform overlay, headerRect;
            internal GroupGraphic row;
            internal Text arrow;
            internal Image selection;
            internal void Destroy() { if (overlay != null) UnityEngine.Object.Destroy(overlay.gameObject); if (row != null) UnityEngine.Object.Destroy(row.gameObject); if (arrow != null) UnityEngine.Object.Destroy(arrow.gameObject); }
        }

        void RefreshGroups()
        {
            if (!attached || inView || grid == null) return;
            HashSet<object> alive = new HashSet<object>();
            foreach (object h in Get<IList>(native, "_displayedOwnerHeader"))
            {
                object g = Get<object>(h, "group"); GameObject go = Get<GameObject>(h, "gameObject");
                if (g == null || !go.activeSelf) continue;
                alive.Add(g); GroupUi ui;
                if (!groupRows.TryGetValue(g, out ui)) { ui = CreateGroupUi(g, h); groupRows.Add(g, ui); }
                if (!Object.ReferenceEquals(ui.header, h)) { ui.Destroy(); ui = CreateGroupUi(g, h); groupRows[g] = ui; }
                ui.overlay.gameObject.SetActive(true); ui.arrow.gameObject.SetActive(true);
                ui.headerRect = (RectTransform)go.transform;
                bool selected = selectedGroups.Contains(g);
                ui.selection.color = selected ? new Color(.2f, .8f, .42f, .25f) : Color.clear;
                bool expanded = Get<bool>(Get<object>(g, "obj"), "expanded");
                ui.arrow.text = expanded ? "▼" : "▶";
                ui.row.gameObject.SetActive(summaries.Value);
                if (summaries.Value)
                {
                    RectTransform rt = ui.row.rectTransform;
                    rt.anchorMin = new Vector2(0, 1); rt.anchorMax = Vector2.one; rt.pivot = new Vector2(0, .5f);
                    rt.anchoredPosition = new Vector2(0, ui.headerRect.anchoredPosition.y); rt.sizeDelta = new Vector2(0, ui.headerRect.rect.height);
                    PopulateGroupSummary(ui.row); ui.row.zoom = Get<float>(native, "_zoomLevel"); ui.row.selected = selected;
                    ui.row.SetVerticesDirty();
                }
            }
            List<object> remove = new List<object>();
            foreach (KeyValuePair<object, GroupUi> pair in groupRows)
            {
                if (alive.Contains(pair.Key)) continue;
                pair.Value.Destroy(); remove.Add(pair.Key);
            }
            foreach (object g in remove) groupRows.Remove(g);
            // Selection survives collapsing a parent, but discarded/deleted group identities do not.
            List<INode> tree = RootNodes(); HashSet<object> all = new HashSet<object>(); GatherGroups(tree, all);
            selectedGroups.RemoveWhere(delegate(object g) { return !all.Contains(g); });
            HookNativeGroupDrags();
        }
        static void GatherGroups(List<INode> nodes, HashSet<object> result)
        { foreach (INode n in nodes) { IGroupNode g = n as IGroupNode; if (g == null) continue; result.Add(g); GatherGroups(g.children, result); } }

        GroupUi CreateGroupUi(object g, object h)
        {
            GroupUi ui = new GroupUi(); ui.group = g; ui.header = h;
            RectTransform parent = Get<RectTransform>(h, "container");
            ui.overlay = MakeRect("GIN Group selection", parent, false); Fill(ui.overlay, 22, 0, 0, 0);
            ui.selection = ui.overlay.gameObject.AddComponent<Image>(); ui.selection.color = Color.clear;
            PointerAction action = ui.overlay.gameObject.AddComponent<PointerAction>();
            action.action = delegate(PointerEventData e)
            {
                trackFocus = true;
                if (e.button == PointerEventData.InputButton.Left) SelectGroup(g, Ctrl);
                else InvokeNativePointer(parent.gameObject, e);
            };
            RectTransform arrowRect = MakeRect("GIN Group expand arrow", parent, false); arrowRect.anchorMin = Vector2.zero; arrowRect.anchorMax = new Vector2(0, 1); arrowRect.offsetMin = new Vector2(2, 0); arrowRect.offsetMax = new Vector2(20, 0);
            ui.arrow = Label(arrowRect, "▼", 12); ui.arrow.raycastTarget = false;
            // The arrow container intentionally has no event handler: the original header still opens/closes it.
            ui.arrow = arrowRect.gameObject.AddComponent<Text>(); ui.arrow.font = font; ui.arrow.fontSize = 12; ui.arrow.alignment = TextAnchor.MiddleCenter; ui.arrow.raycastTarget = false;
            Transform redundant = arrowRect.Find("Label"); if (redundant != null) UnityEngine.Object.Destroy(redundant.gameObject);
            RectTransform rr = MakeRect("GIN Group summary", grid, false); ui.row = rr.gameObject.AddComponent<GroupGraphic>(); ui.row.plugin = this; ui.row.group = (IGroupNode)g; ui.row.color = Color.white;
            return ui;
        }
        static void InvokeNativePointer(GameObject go, PointerEventData e)
        {
            foreach (Component c in go.GetComponents<Component>())
            {
                if (c == null || c is PointerAction) continue;
                FieldInfo f = c.GetType().GetField("onPointerDown", Flags);
                if (f != null) { Delegate handler = f.GetValue(c) as Delegate; if (handler != null) { handler.DynamicInvoke(e); return; } }
            }
        }
        public void SelectGroup(object g, bool additive)
        {
            rebuilding = true;
            try { Call("ClearSelectedInterpolables"); } finally { rebuilding = false; }
            if (!additive) selectedGroups.Clear();
            if (additive && selectedGroups.Contains(g)) selectedGroups.Remove(g); else selectedGroups.Add(g);
            trackFocus = true; nextRefresh = 0; Note(selectedGroups.Count + " group(s) selected — Shift + Up / Down");
        }
        internal static List<Interpolable> Descendants(IGroupNode group)
        {
            List<Interpolable> result = new List<Interpolable>();
            foreach (INode node in group.children)
            {
                IGroupNode nested = node as IGroupNode;
                if (nested != null) result.AddRange(Descendants(nested));
                else { LeafNode<Interpolable> leaf = node as LeafNode<Interpolable>; if (leaf != null) result.Add(leaf.obj); }
            }
            return result;
        }
        internal static List<float> GroupTimes(IGroupNode group)
        {
            SortedDictionary<float, bool> times = new SortedDictionary<float, bool>();
            foreach (Interpolable track in Descendants(group)) foreach (float time in track.keyframes.Keys) times[time] = true;
            return new List<float>(times.Keys);
        }
        public int AddGroupKeys(IGroupNode group, float time)
        {
            int added = 0, existing = 0, failed = 0;
            foreach (Interpolable track in Descendants(group))
            {
                if (OccupiedAt(track, time)) { existing++; continue; }
                try { if (AddAt(track, time)) added++; else failed++; } catch (Exception e) { failed++; Logger.LogWarning("Group insertion: " + Error(e)); }
            }
            nextRefresh = 0; Note("Group: " + added + " added / " + existing + " existing" + (failed == 0 ? "" : " / " + failed + " unavailable")); return added;
        }
        internal void SelectGroupKeys(IGroupNode group, float time)
        {
            List<Key> keys = Ctrl ? ReadSelection() : new List<Key>();
            List<Key> represented = new List<Key>();
            foreach (Interpolable track in Descendants(group))
            {
                Timeline.Keyframe k; if (!track.keyframes.TryGetValue(time, out k)) continue;
                represented.Add(new Key(time, k));
            }
            bool allSelected = Ctrl && represented.Count > 0;
            foreach (Key pair in represented) if (!keys.Contains(pair)) allSelected = false;
            foreach (Key pair in represented) { if (allSelected) keys.Remove(pair); else if (!keys.Contains(pair)) keys.Add(pair); }
            Select(keys); if (keys.Count > 0) anchor = keys[0]; range = lastPhase = null;
            RefreshGroupSelection();
            trackFocus = true;
        }
        internal float TimeFromLocalX(float x)
        {
            float time = 10f * x / (300f * Get<float>(native, "_zoomLevel"));
            if (Shift)
            { float beat = Get<float>(native, "_blockLength") / Get<int>(native, "_divisions"); if (beat > 0) time = Mathf.Round(time / beat) * beat; }
            return Math.Max(0, time);
        }
        internal bool GroupAddButton(PointerEventData e)
        { return (!Ctrl && e.button == PointerEventData.InputButton.Middle) || MatchesAddGesture(e); }
        List<INode> RootNodes() { return Get<List<INode>>(Get<object>(native, "_interpolablesTree"), "tree"); }
        public bool MoveSelected(int direction)
        {
            object tree = Get<object>(native, "_interpolablesTree");
            List<INode> selected = new List<INode>();
            foreach (object group in selectedGroups) selected.Add((INode)group);
            MethodInfo leafMethod = tree.GetType().GetMethod("GetLeafNode", Flags);
            foreach (Interpolable track in Get<IList>(native, "_selectedInterpolables"))
            { INode node = (INode)leafMethod.Invoke(tree, new object[] { track }); if (!selected.Contains(node)) selected.Add(node); }
            List<INode> top = new List<INode>();
            foreach (INode node in selected)
            {
                bool parentSelected = false; IGroupNode p = node.parent;
                while (p != null) { if (selected.Contains(p)) { parentSelected = true; break; } p = p.parent; }
                if (!parentSelected) top.Add(node);
            }
            if (top.Count == 0) { Note("Select a track or group name first"); return false; }
            // Native Tree.MoveUp/Down preserves parents and saves the order in native scene data.
            MethodInfo move = tree.GetType().GetMethod(direction < 0 ? "MoveUp" : "MoveDown", Flags, null, new Type[] { typeof(IEnumerable<INode>) }, null);
            move.Invoke(tree, new object[] { top });
            Call("UpdateInterpolablesView"); Note("Moved " + top.Count + (direction < 0 ? " up" : " down")); return true;
        }
    }

    public sealed class GroupGraphic : MaskableGraphic, IPointerDownHandler, IInitializePotentialDragHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        internal Plugin plugin;
        internal IGroupNode group;
        internal List<float> times = new List<float>();
        internal readonly HashSet<float> selectedTimes = new HashSet<float>();
        internal float zoom;
        internal bool selected;
        bool areaDrag, keyDrag;
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear(); Rect r = rectTransform.rect;
            Quad(mesh, new Vector2(r.xMin, r.yMin), new Vector2(r.xMin, r.yMax), new Vector2(r.xMax, r.yMax), new Vector2(r.xMax, r.yMin), selected ? new Color(.12f, .46f, .27f, .65f) : new Color(.13f, .26f, .36f, .97f));
            float half = Mathf.Min(6, r.height * .34f);
            // One mesh per group. Avoid thousands of independent UI objects for dense animation tracks.
            foreach (float time in times)
            {
                float x = r.xMin + 30 * zoom * time;
                if (x < r.xMin - half || x > r.xMax + half) continue;
                if (mesh.currentVertCount > 64000) break;
                Color32 c = selectedTimes.Contains(time) ? Color.green : Color.red;
                Quad(mesh, new Vector2(x, -half), new Vector2(x - half, 0), new Vector2(x, half), new Vector2(x + half, 0), c);
            }
        }
        static void Quad(VertexHelper mesh, Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color32 color)
        {
            int index = mesh.currentVertCount;
            mesh.AddVert(a, color, Vector2.zero); mesh.AddVert(b, color, Vector2.zero); mesh.AddVert(c, color, Vector2.zero); mesh.AddVert(d, color, Vector2.zero);
            mesh.AddTriangle(index, index + 1, index + 2); mesh.AddTriangle(index, index + 2, index + 3);
        }
        public void OnPointerDown(PointerEventData e)
        {
            if (plugin == null || Plugin.TextFocused()) return;
            Vector2 local; if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform, e.position, e.pressEventCamera, out local)) return;
            float x = local.x - rectTransform.rect.xMin;
            if (plugin.GroupAddButton(e)) { plugin.AddGroupKeys(group, plugin.TimeForAdd(x, plugin.MatchesAddGesture(e))); return; }
            if (e.button == PointerEventData.InputButton.Right)
            { float seek = NearestTime(x); if (seek >= 0) plugin.Call("SeekPlaybackTime", seek); return; }
            if (e.button != PointerEventData.InputButton.Left) return;
            if (Plugin.Alt) return;
            float nearest = NearestTime(x);
            if (nearest >= 0) plugin.SelectGroupKeys(group, nearest); else plugin.SelectGroup(group, Plugin.Ctrl);
        }
        float NearestTime(float x)
        {
            float nearest = -1, pixels = 8;
            foreach (float t in times) { float d = Math.Abs(x - 30 * zoom * t); if (d < pixels) { pixels = d; nearest = t; } }
            return nearest;
        }
        public void OnInitializePotentialDrag(PointerEventData e)
        {
            areaDrag = keyDrag = false;
            if (plugin == null || Plugin.TextFocused() || e.button != PointerEventData.InputButton.Left) return;
            plugin.Call("PotentiallyBeginAreaSelect", e);
        }
        public void OnBeginDrag(PointerEventData e)
        { BeginDragWithModifiers(e, Plugin.Alt); }
        internal void BeginDragWithModifiers(PointerEventData e, bool alt)
        {
            if (plugin == null || Plugin.TextFocused() || e.button != PointerEventData.InputButton.Left) return;
            Vector2 point;
            if (alt && RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform, e.pressPosition, e.pressEventCamera, out point))
            {
                float nearest = NearestTime(point.x - rectTransform.rect.xMin);
                if (nearest >= 0) keyDrag = plugin.BeginGroupKeyDrag(group, nearest, e, gameObject);
            }
            if (!keyDrag) { areaDrag = true; plugin.Call("BeginAreaSelect", e); }
            e.Use();
        }
        public void OnDrag(PointerEventData e)
        {
            if (plugin == null) return;
            if (keyDrag) plugin.UpdateGroupKeyDrag(e);
            else if (areaDrag) plugin.Call("UpdateAreaSelect", e);
            e.Use();
        }
        public void OnEndDrag(PointerEventData e)
        {
            if (plugin == null) return;
            if (keyDrag) plugin.EndGroupKeyDrag(e);
            else if (areaDrag) plugin.Call("EndAreaSelect", e);
            keyDrag = areaDrag = false; e.Use();
        }
        void Update()
        { if (plugin != null && keyDrag && Input.GetKeyDown(KeyCode.Escape)) { plugin.CancelGroupKeyDrag(); keyDrag = false; } }
    }
}
