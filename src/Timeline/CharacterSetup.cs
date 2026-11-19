using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml;
using Studio;
using Timeline;
using UnityEngine;
using Native = Timeline.Timeline;

namespace GinTimelineHack
{
    // "Char Setup": everything GIN uses to start animating a character, for each selected character.
    //
    // The setup is CharacterSetup.xml, embedded in the DLL and generated from a setup scene by
    // src/tools/extract_character_setup.py. It holds
    //   * objects around the character - items on its accessory points and beside it (SetupObjects.cs);
    //   * NodesConstraints links between those objects;
    //   * Timeline rows of the character and of those objects, in groups and with row colours.
    //
    // Only what is missing is added: existing objects and rows are kept with their keys. An existing
    // row found outside its group is moved into it. The second and later characters get their own
    // colour theme (SetupThemes.cs). The result is ordinary Studio / Timeline / Material Editor /
    // NodesConstraints scene data: playback needs no GIN plugin.
    public sealed partial class Plugin
    {
        const string SetupResource = "CharacterSetup.xml";
        const string BodyRenderer = "o_body_a";
        const string EachShapeAttribute = "eachShape";

        static XmlDocument setupTemplate;

        static XmlDocument SetupTemplate()
        {
            if (setupTemplate == null)
            {
                using (Stream stream = typeof(Plugin).Assembly.GetManifestResourceStream(SetupResource))
                {
                    if (stream == null)
                    {
                        throw new InvalidOperationException(SetupResource + " is missing from the DLL");
                    }

                    XmlDocument document = new XmlDocument();
                    document.Load(stream);
                    setupTemplate = document;
                }
            }

            return setupTemplate;
        }

        // ---- button action ------------------------------------------------------------------

        public bool SetupSelectedCharacters()
        {
            List<OCIChar> characters = SelectedCharacters();
            if (characters.Count == 0)
            {
                Note("Char Setup: select a character in the workspace first");
                return false;
            }

            RegistrationReport rows = new RegistrationReport();
            rows.GatherExisting = true;
            SetupReport objects = new SetupReport();
            string warning = "";
            foreach (OCIChar character in characters)
            {
                SetupCharacter(character, rows, objects);
                if (HasWholeRendererRow(character, BodyRenderer))
                {
                    warning = " / " + BodyRenderer + " still has a whole-renderer BS row: it overrides the single rows, delete it";
                }
            }

            Studio.Studio.Instance.treeNodeCtrl.SelectSingle(characters[0].treeNodeObject, false);   // "true" would toggle an already selected node off
            Note("Char Setup: rows " + rows.Summary() + " / " + objects.Summary() + warning);
            return true;
        }

        // Objects first (the rows of objects need them), then the links, then every row.
        internal void SetupCharacter(OCIChar character, RegistrationReport rows, SetupReport objects)
        {
            int theme = ThemeFor(character);
            Dictionary<string, ObjectCtrlInfo> placed = EnsureSetupObjects(character, objects);
            EnsureSetupConstraints(placed, objects);
            rows.Targets.Clear();
            foreach (KeyValuePair<string, ObjectCtrlInfo> pair in placed)
            {
                rows.Targets[pair.Key] = pair.Value;
            }

            RegisterTracks(character, BuildSetupRows(character, placed, theme, rows), rows);
        }

        static List<OCIChar> SelectedCharacters()
        {
            List<OCIChar> characters = new List<OCIChar>();
            foreach (ObjectCtrlInfo info in Studio.Studio.Instance.treeNodeCtrl.selectObjectCtrl)
            {
                OCIChar character = info as OCIChar;
                if (character != null && !characters.Contains(character))
                {
                    characters.Add(character);
                }
            }

            return characters;
        }

        // ---- rows ----------------------------------------------------------------------------

        // The template's rows in Timeline's own XML shape, resolved on this character:
        //   bone / renderer   -> the path Timeline and KKPE store, found on this character
        //   eachShape         -> one KKPE single Blend Shape row per shape of the renderer, the
        //                        listed indices first (the setup scene's order), then the others
        //   object            -> a row of a setup object; registered on that object
        // Row colours are given this character's theme.
        internal XmlNode BuildSetupRows(OCIChar character, Dictionary<string, ObjectCtrlInfo> placed, int theme, RegistrationReport report)
        {
            Transform root = character.guideObject.transformTarget;
            Dictionary<string, Transform> bones = new Dictionary<string, Transform>();
            foreach (Transform bone in root.GetComponentsInChildren<Transform>(true))
            {
                if (!bones.ContainsKey(bone.name))
                {
                    bones.Add(bone.name, bone);
                }
            }

            XmlDocument document = new XmlDocument();
            XmlElement rootNode = document.CreateElement("root");
            document.AppendChild(rootNode);
            CopySetupRows(SetupTemplate().DocumentElement["rows"], rootNode, root, bones, placed, theme, report);
            return rootNode;
        }

        void CopySetupRows(XmlNode source, XmlElement target, Transform root, Dictionary<string, Transform> bones,
            Dictionary<string, ObjectCtrlInfo> placed, int theme, RegistrationReport report)
        {
            foreach (XmlNode node in source.ChildNodes)
            {
                XmlElement row = node as XmlElement;
                if (row == null)
                {
                    continue;
                }

                if (row.Name == "interpolableGroup")
                {
                    XmlElement group = target.OwnerDocument.CreateElement("interpolableGroup");
                    group.SetAttribute("name", row.GetAttribute("name"));
                    target.AppendChild(group);
                    CopySetupRows(row, group, root, bones, placed, theme, report);
                    continue;
                }

                Color color = Themed(ReadVector(row.GetAttribute("color"), Color.white), theme);
                if (row.HasAttribute("object"))
                {
                    ObjectCtrlInfo owner;
                    if (!placed.TryGetValue(row.GetAttribute("object"), out owner))
                    {
                        report.Unavailable.Add(row.GetAttribute("id") + " of " + row.GetAttribute("object"));
                        continue;
                    }

                    target.AppendChild(ObjectRowXml(target.OwnerDocument, row, owner, color));
                    continue;
                }

                string boneName = row.HasAttribute("renderer") ? row.GetAttribute("renderer") : row.GetAttribute("bone");
                Transform bone;
                if (boneName.Length == 0)
                {
                    bone = root;
                }
                else if (!bones.TryGetValue(boneName, out bone))
                {
                    report.Unavailable.Add(row.GetAttribute("id") + " " + boneName);
                    continue;
                }

                if (row.HasAttribute(EachShapeAttribute))
                {
                    foreach (int index in ShapeOrder(bone, row.GetAttribute(EachShapeAttribute)))
                    {
                        target.AppendChild(CharacterRowXml(target.OwnerDocument, row, PathFrom(root, bone), index, color));
                    }

                    continue;
                }

                int blendIndex = row.HasAttribute("index") ? XmlConvert.ToInt32(row.GetAttribute("index")) : -1;
                target.AppendChild(CharacterRowXml(target.OwnerDocument, row, PathFrom(root, bone), blendIndex, color));
            }
        }

        // Every Blend Shape of the renderer: the listed indices that exist first, then the rest.
        // On the body of the setup scene this is exactly the scene's order.
        static List<int> ShapeOrder(Transform rendererObject, string listed)
        {
            SkinnedMeshRenderer renderer = rendererObject.GetComponent<SkinnedMeshRenderer>();
            int count = renderer == null || renderer.sharedMesh == null ? 0 : renderer.sharedMesh.blendShapeCount;
            List<int> order = new List<int>();
            foreach (string text in listed.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int index = XmlConvert.ToInt32(text);
                if (index < count && !order.Contains(index))
                {
                    order.Add(index);
                }
            }

            for (int index = 0; index < count; index++)
            {
                if (!order.Contains(index))
                {
                    order.Add(index);
                }
            }

            return order;
        }

        static XmlElement CharacterRowXml(XmlDocument document, XmlElement template, string path, int blendIndex, Color color)
        {
            XmlElement node = NewRow(document, template, color);
            string id = template.GetAttribute("id");
            if (id.StartsWith("guideObject", StringComparison.Ordinal))
            {
                node.SetAttribute("guideObjectPath", path);
            }
            else if (blendIndex >= 0)
            {
                node.SetAttribute("parameter1", path);
                node.SetAttribute("parameter2", Invariant(blendIndex));
            }
            else
            {
                node.SetAttribute("parameter", path);
            }

            return node;
        }

        // A row of a setup object. Material Editor rows of an item find their material on the item;
        // the path only tells two rows apart, written as Material Editor itself writes it.
        static XmlElement ObjectRowXml(XmlDocument document, XmlElement template, ObjectCtrlInfo owner, Color color)
        {
            XmlElement node = NewRow(document, template, color);
            node.SetAttribute(SetupObjectAttribute, template.GetAttribute("object"));
            if (template.GetAttribute("id").StartsWith("guideObject", StringComparison.Ordinal))
            {
                node.SetAttribute("guideObjectPath", "");
            }

            if (template.GetAttribute("owner") == "MaterialEditor")
            {
                OCIItem item = owner as OCIItem;
                node.SetAttribute("gameObjectPath", item == null ? owner.guideObject.transformTarget.name : FullPath(item.objectItem.transform));
                node.SetAttribute("materialName", template.GetAttribute("materialName"));
                node.SetAttribute("propertyName", template.GetAttribute("propertyName"));
                node.SetAttribute("rendererName", template.GetAttribute("rendererName"));
            }

            return node;
        }

        static XmlElement NewRow(XmlDocument document, XmlElement template, Color color)
        {
            XmlElement node = document.CreateElement("interpolable");
            node.SetAttribute("owner", template.GetAttribute("owner"));
            node.SetAttribute("id", template.GetAttribute("id"));
            node.SetAttribute("bgColorR", Invariant(color.r));
            node.SetAttribute("bgColorG", Invariant(color.g));
            node.SetAttribute("bgColorB", Invariant(color.b));
            return node;
        }

        // "BodyTop/p_cf_body_bone/.../cf_j_head": the form Timeline and KKPE save.
        static string PathFrom(Transform root, Transform bone)
        {
            if (bone == root)
            {
                return "";
            }

            StringBuilder path = new StringBuilder(bone.name);
            for (Transform parent = bone.parent; parent != null && parent != root; parent = parent.parent)
            {
                path.Insert(0, "/");
                path.Insert(0, parent.name);
            }

            return path.ToString();
        }

        static string FullPath(Transform transform)
        {
            StringBuilder path = new StringBuilder(transform.name);
            for (Transform parent = transform.parent; parent != null; parent = parent.parent)
            {
                path.Insert(0, "/");
                path.Insert(0, parent.name);
            }

            return path.ToString();
        }

        // "r g b" or "r g b a" (also used for positions "x y z" with Vector3 conversion).
        internal static Color ReadVector(string text, Color fallback)
        {
            if (string.IsNullOrEmpty(text))
            {
                return fallback;
            }

            string[] parts = text.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            Color value = new Color(0f, 0f, 0f, 1f);
            for (int i = 0; i < parts.Length && i < 4; i++)
            {
                value[i] = XmlConvert.ToSingle(parts[i]);
            }

            return value;
        }

        // Earlier Char Setup versions keyed the body mesh as one "BS (renderer, L)" row. Such a row
        // writes every shape of the renderer and would undo the single rows; it is left to the
        // user to delete, because it may hold keys.
        static bool HasWholeRendererRow(OCIChar character, string renderer)
        {
            foreach (Interpolable row in Native.GetAllInterpolables(false))
            {
                if (row.oci == character && row.owner == "KKPE" && (row.id == "groupBlendShapeLink" || row.id == "groupBlendShape") && row.parameter != null)
                {
                    string path = Get<string>(row.parameter, "rendererPath");
                    if (path != null && path.Substring(path.LastIndexOf('/') + 1) == renderer)
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }
}
