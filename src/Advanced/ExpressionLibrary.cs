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
            root = Path.GetFullPath(storageDirectory);
            notify = status;
            try
            {
                Directory.CreateDirectory(root);
                Reload();
            }
            catch (Exception error)
            {
                Status("Expression library: " + Error(error));
            }
        }

        public bool Visible
        {
            get
            {
                return visible;
            }
        }

        public Rect[] InputRects
        {
            get
            {
                if (disposed || Time.frameCount - lastDrawFrame > 1)
                {
                    return new Rect[0];
                }

                if (deletion != null || moving != null)
                {
                    return new Rect[]
                    {
                        new Rect(0, 0, Screen.width, Screen.height)
                    };
                }

                List<Rect> rects = new List<Rect>();
                rects.Add(launcher);
                if (visible)
                {
                    rects.Add(window);
                }

                return rects.ToArray();
            }
        }

        public bool IsPointerOverUI
        {
            get
            {
                if (Time.frameCount - lastDrawFrame > 1)
                {
                    return false;
                }

                Vector2 point = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
                if (deletion != null || moving != null)
                {
                    return new Rect(0, 0, Screen.width, Screen.height).Contains(point);
                }

                return launcher.Contains(point) || (visible && window.Contains(point));
            }
        }

        public bool TextInputActive
        {
            get
            {
                return visible && Time.frameCount - lastDrawFrame <= 1 && textInput;
            }
        }

        public void Show()
        {
            visible = true;
            window.width = Math.Min(1120, Screen.width - 20);
            window.height = Math.Min(710, Screen.height - 35);
            window.x = Mathf.Clamp(window.x, 0, Screen.width - window.width);
            window.y = Mathf.Clamp(window.y, 0, Screen.height - window.height);
        }

        private static string Error(Exception error)
        {
            while (error.InnerException != null)
            {
                error = error.InnerException;
            }

            return error.Message;
        }

        private void Status(string text)
        {
            message = text;
            if (notify != null)
            {
                notify(text);
            }
        }

        private string GroupsPath
        {
            get
            {
                return Path.Combine(root, "groups.xml");
            }
        }

        private void Reload()
        {
            ExpressionGroups loaded = File.Exists(GroupsPath) ? ExpressionStorage.Read<ExpressionGroups>(GroupsPath) : new ExpressionGroups();
            groups = new ExpressionGroups();
            HashSet<string> ids = new HashSet<string>();
            foreach (ExpressionGroup group in loaded.Items)
            {
                if ((group.Id != "default" && !ExpressionStorage.ValidId(group.Id)) || ids.Contains(group.Id))
                {
                    continue;
                }

                ids.Add(group.Id);
                groups.Items.Add(group);
            }

            if (!ids.Contains("default"))
            {
                ExpressionGroup basic = new ExpressionGroup();
                basic.Id = "default";
                basic.Name = "Expressions";
                groups.Items.Insert(0, basic);
                ids.Add(basic.Id);
            }

            presets.Clear();
            int skipped = 0;
            foreach (string file in Directory.GetFiles(root, "*.ginexpression.xml", SearchOption.TopDirectoryOnly))
            {
                try
                {
                    ExpressionPreset preset = ExpressionStorage.Read<ExpressionPreset>(file);
                    ExpressionStorage.Validate(preset);
                    if (!String.Equals(
                        Path.GetFileName(file),
                        Path.GetFileName(ExpressionStorage.PresetPath(root, preset.Id)),
                        StringComparison.OrdinalIgnoreCase))
                    {
                        throw new IOException("File identity differs from its content.");
                    }

                    if (!ids.Contains(preset.GroupId))
                    {
                        preset.GroupId = "default";
                    }

                    presets.Add(preset);
                }
                catch (Exception error)
                {
                    skipped++;
                    if (notify != null)
                    {
                        notify("Skipped expression " + Path.GetFileName(file) + ": " + Error(error));
                    }
                }
            }

            presets.Sort(delegate (ExpressionPreset a, ExpressionPreset b)
            {
                return String.Compare(b.CreatedUtc, a.CreatedUtc, StringComparison.Ordinal);
            });
            ExpressionStorage.Write(GroupsPath, groups);
            if (!ids.Contains(saveGroup))
            {
                saveGroup = "default";
            }

            if (skipped > 0)
            {
                Status("Skipped " + skipped + " invalid expression files; see BepInEx log.");
            }
        }

        public void Tick()
        {
            if (disposed)
            {
                return;
            }

            while (pending.Count > 0)
            {
                try
                {
                    pending.Dequeue()();
                }
                catch (Exception error)
                {
                    Status("Expression library: " + Error(error));
                }
            }
        }

        private void Save(string expressionName, string group, Component expected)
        {
            ExpressionTarget target = ExpressionRuntime.Current();
            if (target == null || target.Component != expected)
            {
                throw new InvalidOperationException("Character selection changed before capture. Please save again.");
            }

            string report;
            ExpressionPreset preset = ExpressionRuntime.Capture(target, expressionName, group, out report);
            byte[] thumbnail = ExpressionRuntime.CaptureThumbnail();
            string imagePath = ExpressionStorage.ImagePath(root, preset.Id);
            try
            {
                using (FileStream stream = new FileStream(imagePath, FileMode.CreateNew, FileAccess.Write))
                {
                    stream.Write(thumbnail, 0, thumbnail.Length);
                }

                ExpressionStorage.Write(ExpressionStorage.PresetPath(root, preset.Id), preset);
                presets.Insert(0, preset);
            }
            catch
            {
                if (File.Exists(imagePath))
                {
                    File.Delete(imagePath);
                }

                throw;
            }

            Status("Saved: " + preset.Name + " · " + report);
        }

        private void Delete(string id)
        {
            ExpressionPreset preset = presets.Find(delegate (ExpressionPreset item)
            {
                return item.Id == id;
            });
            if (preset == null)
            {
                return;
            }

            // Called only after the explicit confirmation. Never recurse and never derive paths from display names.
            string file = ExpressionStorage.PresetPath(root, id), image = ExpressionStorage.ImagePath(root, id);
            if (File.Exists(file))
            {
                File.Delete(file);
            }

            if (File.Exists(image))
            {
                File.Delete(image);
            }

            Texture2D cached;
            if (images.TryGetValue(id, out cached) && cached != null)
            {
                UnityEngine.Object.Destroy(cached);
            }

            images.Remove(id);
            presets.Remove(preset);
            edits.Remove(id);
            Status("Deleted: " + preset.Name);
        }

        public void Dispose()
        {
            disposed = true;
            pending.Clear();
            visible = false;
            foreach (Texture2D image in images.Values)
            {
                if (image != null)
                {
                    UnityEngine.Object.Destroy(image);
                }
            }

            images.Clear();
        }
    }
}
