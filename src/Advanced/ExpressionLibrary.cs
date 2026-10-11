using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace GinAdvanceHack
{
    public sealed partial class ExpressionLibrary : IDisposable
    {
        private const int WindowId = 0x47494E31, ConfirmId = 0x47494E32;
        private readonly string root;
        private readonly Action<string> notify;
        private ExpressionGroups groups = new ExpressionGroups();
        private readonly List<ExpressionPreset> presets = new List<ExpressionPreset>();
        private readonly Dictionary<string, Texture2D> images = new Dictionary<string, Texture2D>();
        private readonly Dictionary<string, Vector2> scrolls = new Dictionary<string, Vector2>();
        private readonly Dictionary<string, string> edits = new Dictionary<string, string>();
        private readonly Queue<Action> pending = new Queue<Action>();
        private Rect window = new Rect(100, 100, 1120, 710), launcher, confirmation;
        private Vector2 horizontal, pickerScroll;
        private string name = "Expression", groupName = "", saveGroup = "default", message = "Save the current character's expression. Click a thumbnail to apply it.";
        private string deletion, moving;
        private bool visible, disposed, textInput;
        private int lastDrawFrame = -100;
        private GUIStyle wrapping, caption, title;

        public ExpressionLibrary(string storageDirectory, Action<string> status)
        {
            root = Path.GetFullPath(storageDirectory); notify = status;
            try { Directory.CreateDirectory(root); Reload(); }
            catch (Exception error) { Status("Expression library: " + Error(error)); }
        }

        public bool Visible { get { return visible; } }
        public Rect[] InputRects
        {
            get
            {
                if (disposed || Time.frameCount - lastDrawFrame > 1) return new Rect[0];
                if (deletion != null || moving != null) return new Rect[] { new Rect(0, 0, Screen.width, Screen.height) };
                List<Rect> rects = new List<Rect>(); rects.Add(launcher);
                if (visible) rects.Add(window);
                return rects.ToArray();
            }
        }
        public bool IsPointerOverUI
        {
            get { if (Time.frameCount - lastDrawFrame > 1) return false; Vector2 point = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y); if (deletion != null || moving != null) return new Rect(0, 0, Screen.width, Screen.height).Contains(point); return launcher.Contains(point) || (visible && window.Contains(point)); }
        }
        public bool TextInputActive { get { return visible && Time.frameCount - lastDrawFrame <= 1 && textInput; } }
        public void Show()
        {
            visible = true;
            window.width = Math.Min(1120, Screen.width - 20); window.height = Math.Min(710, Screen.height - 35);
            window.x = Mathf.Clamp(window.x, 0, Screen.width - window.width); window.y = Mathf.Clamp(window.y, 0, Screen.height - window.height);
        }

        private static string Error(Exception error)
        {
            while (error.InnerException != null) error = error.InnerException;
            return error.Message;
        }
        private void Status(string text) { message = text; if (notify != null) notify(text); }
        private string GroupsPath { get { return Path.Combine(root, "groups.xml"); } }
        private void Reload()
        {
            ExpressionGroups loaded = File.Exists(GroupsPath) ? ExpressionStorage.Read<ExpressionGroups>(GroupsPath) : new ExpressionGroups();
            groups = new ExpressionGroups(); HashSet<string> ids = new HashSet<string>();
            foreach (ExpressionGroup group in loaded.Items)
            {
                if ((group.Id != "default" && !ExpressionStorage.ValidId(group.Id)) || ids.Contains(group.Id)) continue;
                ids.Add(group.Id); groups.Items.Add(group);
            }
            if (!ids.Contains("default")) { ExpressionGroup basic = new ExpressionGroup(); basic.Id = "default"; basic.Name = "Expressions"; groups.Items.Insert(0, basic); ids.Add(basic.Id); }
            presets.Clear(); int skipped = 0;
            foreach (string file in Directory.GetFiles(root, "*.ginexpression.xml", SearchOption.TopDirectoryOnly))
            {
                try
                {
                    ExpressionPreset preset = ExpressionStorage.Read<ExpressionPreset>(file); ExpressionStorage.Validate(preset);
                    if (!String.Equals(Path.GetFileName(file), Path.GetFileName(ExpressionStorage.PresetPath(root, preset.Id)), StringComparison.OrdinalIgnoreCase)) throw new IOException("File identity differs from its content.");
                    if (!ids.Contains(preset.GroupId)) preset.GroupId = "default";
                    presets.Add(preset);
                }
                catch (Exception error) { skipped++; if (notify != null) notify("Skipped expression " + Path.GetFileName(file) + ": " + Error(error)); }
            }
            presets.Sort(delegate(ExpressionPreset a, ExpressionPreset b) { return String.Compare(b.CreatedUtc, a.CreatedUtc, StringComparison.Ordinal); });
            ExpressionStorage.Write(GroupsPath, groups);
            if (!ids.Contains(saveGroup)) saveGroup = "default";
            if (skipped > 0) Status("Skipped " + skipped + " invalid expression files; see BepInEx log.");
        }

        public void Tick()
        {
            if (disposed) return;
            while (pending.Count > 0)
            {
                try { pending.Dequeue()(); }
                catch (Exception error) { Status("Expression library: " + Error(error)); }
            }
        }

        public void Draw(Rect advancedRect)
        {
            if (disposed) return;
            lastDrawFrame = Time.frameCount;
            float x = advancedRect.xMax + 5;
            float y = advancedRect.y;
            if (x + 158 > Screen.width) { x = advancedRect.xMax - 158; y = advancedRect.yMax + 4; }
            launcher = new Rect(Mathf.Clamp(x, 0, Math.Max(0, Screen.width - 158)), Mathf.Clamp(y, 0, Math.Max(0, Screen.height - 28)), 158, 28);
            if (GUI.Button(launcher, "Expressions"))
            {
                visible = !visible;
                if (visible)
                {
                    window.width = Math.Min(1120, Screen.width - 20); window.height = Math.Min(710, Screen.height - 35);
                    window.x = Mathf.Clamp(window.x, 0, Screen.width - window.width); window.y = Mathf.Clamp(window.y, 0, Screen.height - window.height);
                }
                else { GUI.FocusControl(null); deletion = moving = null; }
            }
            if (!visible) { textInput = false; return; }
            if (wrapping == null)
            {
                wrapping = new GUIStyle(GUI.skin.label); wrapping.wordWrap = true;
                caption = new GUIStyle(GUI.skin.label); caption.wordWrap = false; caption.fontSize = 12;
                title = new GUIStyle(caption); title.alignment = TextAnchor.MiddleCenter;
            }
            bool wasEnabled = GUI.enabled; GUI.enabled = wasEnabled && deletion == null && moving == null;
            window = GUI.Window(WindowId, window, DrawWindow, ""); GUI.enabled = wasEnabled;
            if (deletion != null || moving != null)
            {
                confirmation = new Rect((Screen.width - 460) / 2, (Screen.height - (moving == null ? 170 : 360)) / 2, 460, moving == null ? 170 : 360);
                GUI.ModalWindow(ConfirmId, confirmation, DrawConfirmation, "");
            }
            textInput = GUI.GetNameOfFocusedControl().StartsWith("GINExpression.", StringComparison.Ordinal);
        }

        private void DrawWindow(int id)
        {
            OpaqueBackground(new Rect(0, 0, window.width, window.height));
            GUI.Label(new Rect(12, 2, window.width - 55, 22), "GIN Expressions — KKPE Advanced mode", title);
            if (GUI.Button(new Rect(window.width - 34, 2, 29, 20), "×")) { visible = false; GUI.FocusControl(null); }
            ExpressionTarget target = ExpressionRuntime.Current();
            GUI.Label(new Rect(12, 27, window.width - 24, 23), "Target: " + (target == null ? "Select a character in Studio" : target.Name), caption);
            GUI.Label(new Rect(12, 55, 90, 26), "Name", caption); GUI.SetNextControlName("GINExpression.NewName");
            name = GUI.TextField(new Rect(103, 55, Math.Max(112, window.width - 463), 26), name, 100);
            bool wasEnabled = GUI.enabled; GUI.enabled = wasEnabled && target != null && name.Trim().Length > 0;
            if (GUI.Button(new Rect(window.width - 346, 55, 216, 27), "Save current + thumbnail"))
            {
                string saveName = name.Trim(), destination = saveGroup;
                Component expected = target.Component;
                pending.Enqueue(delegate { Save(saveName, destination, expected); });
            }
            GUI.enabled = wasEnabled;
            if (GUI.Button(new Rect(window.width - 120, 55, 108, 27), "Reload")) pending.Enqueue(Reload);
            GUI.Label(new Rect(12, 89, 110, 24), "New group", caption); GUI.SetNextControlName("GINExpression.NewGroup");
            groupName = GUI.TextField(new Rect(124, 88, 190, 27), groupName, 60);
            GUI.enabled = wasEnabled && groupName.Trim().Length > 0;
            if (GUI.Button(new Rect(320, 88, 86, 27), "+ Create"))
            {
                string wanted = groupName.Trim(); pending.Enqueue(delegate { ExpressionGroup g = new ExpressionGroup(); g.Name = wanted; groups.Items.Add(g); saveGroup = g.Id; ExpressionStorage.Write(GroupsPath, groups); groupName = ""; });
            }
            GUI.enabled = wasEnabled;
            GUI.Label(new Rect(416, 88, window.width - 428, 28), "Save to: " + GroupTitle(saveGroup) + "   •   Choose [Save here] on a column.", caption);
            float columnsY = 124, columnHeight = Math.Max(180, window.height - 229), columnWidth = 226;
            Rect outer = new Rect(10, columnsY, window.width - 20, columnHeight + 24);
            horizontal = GUI.BeginScrollView(outer, horizontal, new Rect(0, 0, Math.Max(window.width - 40, groups.Items.Count * (columnWidth + 7)), columnHeight), true, false);
            for (int i = 0; i < groups.Items.Count; i++) DrawGroup(groups.Items[i], new Rect(i * (columnWidth + 7), 0, columnWidth, columnHeight));
            GUI.EndScrollView();
            GUI.Label(new Rect(12, window.height - 74, window.width - 24, 65), message.Length <= 580 ? message : message.Substring(0, 580) + "… (see log)", wrapping);
            GUI.DragWindow(new Rect(0, 0, window.width - 40, 23));
        }

        private string GroupTitle(string id) { foreach (ExpressionGroup group in groups.Items) if (group.Id == id) return group.Name; return "Expressions"; }
        private string Edit(string id, string original) { string value; return edits.TryGetValue(id, out value) ? value : original; }
        private void DrawGroup(ExpressionGroup group, Rect rect)
        {
            GUI.Box(rect, ""); GUI.BeginGroup(rect);
            GUI.SetNextControlName("GINExpression.Group." + group.Id);
            edits[group.Id] = GUI.TextField(new Rect(5, 5, rect.width - 82, 24), Edit(group.Id, group.Name), 60);
            if (GUI.Button(new Rect(rect.width - 73, 5, 68, 24), "Rename"))
            {
                string newName = edits[group.Id].Trim(); if (newName.Length > 0) pending.Enqueue(delegate { group.Name = newName; ExpressionStorage.Write(GroupsPath, groups); });
            }
            Color previous = GUI.color; if (saveGroup == group.Id) GUI.color = new Color(0.64f, 0.92f, 0.68f);
            if (GUI.Button(new Rect(5, 34, rect.width - 10, 24), saveGroup == group.Id ? "✓ Save here" : "Save here")) saveGroup = group.Id;
            GUI.color = previous;
            List<ExpressionPreset> items = presets.FindAll(delegate(ExpressionPreset p) { return p.GroupId == group.Id; });
            Vector2 scroll; if (!scrolls.TryGetValue(group.Id, out scroll)) scroll = Vector2.zero;
            Rect view = new Rect(4, 65, rect.width - 8, rect.height - 70);
            scroll = GUI.BeginScrollView(view, scroll, new Rect(0, 0, rect.width - 29, Math.Max(view.height - 1, items.Count * 198)), false, true);
            for (int n = 0; n < items.Count; n++)
            {
                ExpressionPreset preset = items[n]; float y = n * 198;
                Rect imageRect = new Rect(2, y + 2, rect.width - 36, 132);
                Texture2D thumbnail = Thumbnail(preset.Id);
                if (GUI.Button(imageRect, thumbnail == null ? new GUIContent("Apply") : new GUIContent(thumbnail)))
                {
                    ExpressionPreset chosen = preset; pending.Enqueue(delegate { Status(ExpressionRuntime.Apply(ExpressionRuntime.Current(), chosen)); });
                }
                GUI.SetNextControlName("GINExpression.Preset." + preset.Id);
                edits[preset.Id] = GUI.TextField(new Rect(2, y + 139, rect.width - 36, 24), Edit(preset.Id, preset.Name), 100);
                if (GUI.Button(new Rect(2, y + 168, 60, 23), "Rename"))
                {
                    string newName = edits[preset.Id].Trim(); ExpressionPreset chosen = preset;
                    if (newName.Length > 0) pending.Enqueue(delegate { chosen.Name = newName; ExpressionStorage.Write(ExpressionStorage.PresetPath(root, chosen.Id), chosen); });
                }
                if (GUI.Button(new Rect(66, y + 168, 58, 23), "Move…")) { moving = preset.Id; pickerScroll = Vector2.zero; GUI.FocusControl(null); }
                if (GUI.Button(new Rect(128, y + 168, rect.width - 162, 23), "Delete")) { deletion = preset.Id; GUI.FocusControl(null); }
            }
            GUI.EndScrollView(); scrolls[group.Id] = scroll;
            GUI.EndGroup();
        }

        private Texture2D Thumbnail(string id)
        {
            Texture2D image; if (images.TryGetValue(id, out image)) return image;
            string path = ExpressionStorage.ImagePath(root, id);
            try
            {
                if (File.Exists(path) && new FileInfo(path).Length < 4 * 1024 * 1024)
                {
                    image = new Texture2D(2, 2, TextureFormat.RGB24, false);
                    if (!image.LoadImage(File.ReadAllBytes(path))) { UnityEngine.Object.Destroy(image); image = null; }
                }
            }
            catch (Exception error) { if (image != null) UnityEngine.Object.Destroy(image); image = null; if (notify != null) notify("Expression thumbnail: " + Error(error)); }
            images[id] = image; return image;
        }

        private void DrawConfirmation(int id)
        {
            OpaqueBackground(new Rect(0, 0, confirmation.width, confirmation.height));
            GUI.Label(new Rect(12, 2, confirmation.width - 24, 22), deletion != null ? "Delete expression" : "Move to group", title);
            if (deletion != null)
            {
                GUI.Label(new Rect(16, 37, 428, 42), "Do you really want to delete this expression?", wrapping);
                ExpressionPreset preset = presets.Find(delegate(ExpressionPreset p) { return p.Id == deletion; });
                GUI.Label(new Rect(16, 80, 428, 26), preset == null ? "" : preset.Name);
                if (GUI.Button(new Rect(62, 120, 156, 30), "Delete"))
                {
                    string chosen = deletion; deletion = null; pending.Enqueue(delegate { Delete(chosen); });
                }
                if (GUI.Button(new Rect(238, 120, 156, 30), "Cancel")) deletion = null;
            }
            else if (moving != null)
            {
                pickerScroll = GUI.BeginScrollView(new Rect(12, 32, 436, 266), pickerScroll, new Rect(0, 0, 411, groups.Items.Count * 35), false, true);
                for (int n = 0; n < groups.Items.Count; n++)
                {
                    ExpressionGroup group = groups.Items[n];
                    if (GUI.Button(new Rect(0, n * 35, 406, 30), group.Name))
                    {
                        string chosen = moving, destination = group.Id; moving = null;
                        pending.Enqueue(delegate { ExpressionPreset preset = presets.Find(delegate(ExpressionPreset p) { return p.Id == chosen; }); if (preset != null) { preset.GroupId = destination; ExpressionStorage.Write(ExpressionStorage.PresetPath(root, preset.Id), preset); } });
                    }
                }
                GUI.EndScrollView(); if (GUI.Button(new Rect(136, 313, 190, 29), "Cancel")) moving = null;
            }
        }

        private static void OpaqueBackground(Rect rect)
        {
            Color previous = GUI.color; GUI.color = new Color(0.245f, 0.245f, 0.26f, 1f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture); GUI.color = previous;
        }

        private void Save(string expressionName, string group, Component expected)
        {
            ExpressionTarget target = ExpressionRuntime.Current(); if (target == null || target.Component != expected) throw new InvalidOperationException("Character selection changed before capture. Please save again.");
            string report; ExpressionPreset preset = ExpressionRuntime.Capture(target, expressionName, group, out report);
            byte[] thumbnail = ExpressionRuntime.CaptureThumbnail(); string imagePath = ExpressionStorage.ImagePath(root, preset.Id);
            try
            {
                using (FileStream stream = new FileStream(imagePath, FileMode.CreateNew, FileAccess.Write)) stream.Write(thumbnail, 0, thumbnail.Length);
                ExpressionStorage.Write(ExpressionStorage.PresetPath(root, preset.Id), preset); presets.Insert(0, preset);
            }
            catch { if (File.Exists(imagePath)) File.Delete(imagePath); throw; }
            Status("Saved: " + preset.Name + " · " + report);
        }

        private void Delete(string id)
        {
            ExpressionPreset preset = presets.Find(delegate(ExpressionPreset item) { return item.Id == id; }); if (preset == null) return;
            // Called only after the explicit confirmation. Never recurse and never derive paths from display names.
            string file = ExpressionStorage.PresetPath(root, id), image = ExpressionStorage.ImagePath(root, id);
            if (File.Exists(file)) File.Delete(file); if (File.Exists(image)) File.Delete(image);
            Texture2D cached; if (images.TryGetValue(id, out cached) && cached != null) UnityEngine.Object.Destroy(cached);
            images.Remove(id); presets.Remove(preset); edits.Remove(id); Status("Deleted: " + preset.Name);
        }

        public void Dispose()
        {
            disposed = true; pending.Clear(); visible = false;
            foreach (Texture2D image in images.Values) if (image != null) UnityEngine.Object.Destroy(image);
            images.Clear();
        }
    }
}
