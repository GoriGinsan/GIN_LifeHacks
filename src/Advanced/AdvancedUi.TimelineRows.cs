using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Xml;
using Studio;
using UnityEngine;

namespace GinAdvanceHack
{
    // Blend Shapes tab: "Timeline 0:00" buttons.
    //
    // They register the selected renderer's Blend Shapes as Timeline rows with one key at 0:00
    // holding the current weights. Rows that exist already are kept as they are.
    //
    //   All      - every Blend Shape of the renderer
    //   Filtered - only the rows currently listed (Search text, hidden rows, collapsed sections)
    //
    // Face and eyebrow renderers always become ONE group row ("BS (renderer, L)"): keying their
    // shapes one by one breaks the expression. Other renderers get one row per Blend Shape.
    // The rows are created by GIN Timeline life hack through KKPE's own Timeline providers,
    // so the saved scene contains only native Timeline / KKPE data.
    internal sealed partial class AdvancedUi
    {
        private const string CharacterGroup = "FACE";        // same group and colour as Char Setup
        private const string ItemGroup = "BLENDSHAPES";
        private const string RowColorR = "0.8450549", RowColorG = "0.8930275", RowColorB = "0.941";
        private const float TimelineMessageSeconds = 8f;

        private string timelineMessage = "";
        private float timelineMessageUntil;

        private void DrawTimelineRowButtons(object editor)
        {
            if (ReflectionAccess.Get(editor, "_skinnedMeshTarget") == null)
            {
                return;
            }

            GUILayout.BeginHorizontal();
            GUILayout.Label("Timeline 0:00", GUILayout.ExpandWidth(false));
            if (GUILayout.Button("Key all", GUILayout.ExpandWidth(false)))
            {
                RegisterBlendShapeRows(editor, false);
            }

            if (GUILayout.Button("Key filtered", GUILayout.ExpandWidth(false)))
            {
                RegisterBlendShapeRows(editor, true);
            }

            if (Time.realtimeSinceStartup < timelineMessageUntil)
            {
                GUILayout.Label(timelineMessage);
            }

            GUILayout.EndHorizontal();
        }

        private void RegisterBlendShapeRows(object editor, bool filteredOnly)
        {
            try
            {
                MethodInfo register = TimelineRegistration();
                if (register == null)
                {
                    ShowTimelineMessage("Needs GIN Timeline life hack 1.1.0 or later");
                    return;
                }

                ObjectCtrlInfo target = ReflectionAccess.Get(ReflectionAccess.Get(editor, "_target"), "oci") as ObjectCtrlInfo;
                string template = BuildTemplate(editor, target is OCIChar, filteredOnly);
                if (template == null)
                {
                    ShowTimelineMessage("No Blend Shape is listed");
                    return;
                }

                ShowTimelineMessage((string)register.Invoke(null, new object[] { target, template }));
            }
            catch (Exception e)
            {
                Exception cause = e.InnerException ?? e;
                ShowTimelineMessage(cause.Message);
                host.Report("Timeline rows: " + cause);
            }
        }

        private static MethodInfo TimelineRegistration()
        {
            Type timeline = ReflectionAccess.Type("GinTimelineHack.Plugin");
            return timeline == null ? null : timeline.GetMethod(
                "RegisterTracksFromXml",
                BindingFlags.Public | BindingFlags.Static,
                null,
                new Type[] { typeof(ObjectCtrlInfo), typeof(string) },
                null);
        }

        // Timeline's own XML shape; see TrackRegistration.cs in the Timeline plugin.
        private string BuildTemplate(object editor, bool isCharacter, bool filteredOnly)
        {
            object blendRenderer = ReflectionAccess.Get(editor, "_skinnedMeshTarget");
            SkinnedMeshRenderer mesh = (SkinnedMeshRenderer)ReflectionAccess.Get(blendRenderer, "_renderer");
            Component owner = (Component)ReflectionAccess.Get(editor, "_parent");
            string path = PathFrom(owner.transform, mesh.transform);

            XmlDocument document = new XmlDocument();
            XmlElement root = document.CreateElement("root");
            root.SetAttribute("time", "0");
            document.AppendChild(root);
            XmlElement group = document.CreateElement("interpolableGroup");
            group.SetAttribute("name", isCharacter ? CharacterGroup : ItemGroup);
            root.AppendChild(group);

            if (RegistersAsGroup(mesh.name))
            {
                // The linked variant drives eyelashes and pupils with the face; it exists for characters only.
                XmlElement row = Row(document, isCharacter ? "groupBlendShapeLink" : "groupBlendShape");
                row.SetAttribute("parameter", path);
                group.AppendChild(row);
                return document.OuterXml;
            }

            foreach (KeyValuePair<string, int> shape in (IEnumerable<KeyValuePair<string, int>>)ReflectionAccess.Get(blendRenderer, "_blendIndics"))
            {
                if (filteredOnly && !RowListed(editor, shape))
                {
                    continue;
                }

                XmlElement row = Row(document, "lastBlendShape");
                row.SetAttribute("parameter1", path);
                row.SetAttribute("parameter2", XmlConvert.ToString(shape.Value));
                group.AppendChild(row);
            }

            return group.HasChildNodes ? document.OuterXml : null;
        }

        private static XmlElement Row(XmlDocument document, string id)
        {
            XmlElement row = document.CreateElement("interpolable");
            row.SetAttribute("owner", "KKPE");
            row.SetAttribute("id", id);
            row.SetAttribute("bgColorR", RowColorR);
            row.SetAttribute("bgColorG", RowColorG);
            row.SetAttribute("bgColorB", RowColorB);
            return row;
        }

        private bool RegistersAsGroup(string rendererName)
        {
            foreach (string name in host.TimelineGroupRenderers.Value.Split(','))
            {
                if (name.Trim() == rendererName)
                {
                    return true;
                }
            }

            return false;
        }

        // The row is in the list right now: it passes KKPE's Search box and GIN's hide / collapse state.
        private static bool RowListed(object editor, KeyValuePair<string, int> shape)
        {
            string search = ReflectionAccess.Get(editor, "_search") as string ?? "";
            IDictionary<string, string> aliases = ReflectionAccess.Get(editor.GetType(), "_blendShapeAliases") as IDictionary<string, string>;
            string alias;
            bool matchesSearch = shape.Key.IndexOf(search, StringComparison.CurrentCultureIgnoreCase) >= 0;
            if (!matchesSearch && aliases != null && aliases.TryGetValue(shape.Key, out alias) && alias != null)
            {
                matchesSearch = alias.IndexOf(search, StringComparison.CurrentCultureIgnoreCase) >= 0;
            }

            bool visible = RowVisible(editor, shape);
            currentBlendKey = null;   // RowVisible records the row it was asked about for the Hide button
            return matchesSearch && visible;
        }

        // "BodyTop/.../cf_O_face": the form KKPE saves.
        private static string PathFrom(Transform root, Transform leaf)
        {
            if (leaf == root)
            {
                return "";
            }

            StringBuilder path = new StringBuilder(leaf.name);
            for (Transform parent = leaf.parent; parent != null && parent != root; parent = parent.parent)
            {
                path.Insert(0, "/");
                path.Insert(0, parent.name);
            }

            return path.ToString();
        }

        private void ShowTimelineMessage(string text)
        {
            timelineMessage = text;
            timelineMessageUntil = Time.realtimeSinceStartup + TimelineMessageSeconds;
        }
    }
}
