using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Xml;
using Studio;
using UnityEngine;

namespace GinTimelineHack
{
    // Char Setup, part 2: the objects around the character and the NodesConstraints links.
    //
    // <objects> of CharacterSetup.xml lists items and folders with their local position,
    // rotation and scale ("sizes"), colours, visibility and Material Editor settings:
    //   point="8"         attached to that Studio accessory point of the character
    //   point="beside"    placed next to the character in the workspace (its parent folder)
    //   no point          a child of the enclosing item or folder
    //
    // Existing objects are kept as they are. An attached object counts as existing when its parent
    // already holds the same item (same item list entry) or a folder of the same name; an object
    // beside the character counts as existing when a NodesConstraints link of the template already
    // ties it to its partner. Modded items are found through Sideloader by mod GUID and the mod's
    // own item number; an item whose mod is not installed is skipped, with its rows.
    public sealed partial class Plugin
    {
        internal const string SetupObjectAttribute = "setupObject";
        const string BesidePoint = "beside";
        const string NodesConstraintsType = "NodesConstraints.NodesConstraints";

        internal sealed class SetupReport
        {
            public int Added, Kept, Links;
            public readonly List<string> Unavailable = new List<string>();

            public string Summary()
            {
                string text = "objects " + Added + " added / " + Kept + " kept";
                if (Links > 0)
                {
                    text += " / " + Links + " link(s) added";
                }

                if (Unavailable.Count > 0)
                {
                    text += " / " + Unavailable.Count + " unavailable (" + Unavailable[0] + (Unavailable.Count > 1 ? ", ..." : "") + ")";
                }

                return text;
            }
        }

        // Template object id ("k57") -> the object in this scene.
        internal Dictionary<string, ObjectCtrlInfo> EnsureSetupObjects(OCIChar character, SetupReport report)
        {
            Dictionary<string, ObjectCtrlInfo> placed = new Dictionary<string, ObjectCtrlInfo>();
            Dictionary<TreeNodeObject, List<ObjectCtrlInfo>> unused = new Dictionary<TreeNodeObject, List<ObjectCtrlInfo>>();
            foreach (XmlNode node in SetupTemplate().DocumentElement["objects"].ChildNodes)
            {
                XmlElement element = node as XmlElement;
                if (element == null)
                {
                    continue;
                }

                string point = element.GetAttribute("point");
                try
                {
                    if (point == BesidePoint)
                    {
                        EnsureBeside(character, element, placed, report);
                        continue;
                    }

                    TreeNodeObject pointNode = AccessoryPointNode(character, XmlConvert.ToInt32(point));
                    if (pointNode == null)
                    {
                        report.Unavailable.Add("accessory point " + point);
                        continue;
                    }

                    EnsureObject(element, pointNode, placed, unused, report);
                }
                catch (Exception e)
                {
                    Logger.LogError("Char Setup object " + element.GetAttribute("id") + ": " + e);
                    report.Unavailable.Add(element.GetAttribute("id") + " (" + Error(e) + ")");
                }
            }

            return placed;
        }

        // One template object under a parent node, then its children under it.
        void EnsureObject(XmlElement element, TreeNodeObject parent, Dictionary<string, ObjectCtrlInfo> placed,
            Dictionary<TreeNodeObject, List<ObjectCtrlInfo>> unused, SetupReport report)
        {
            ObjectCtrlInfo existing = TakeExisting(element, parent, unused);
            ObjectCtrlInfo created = existing == null ? CreateObject(element, parent, report) : null;
            ObjectCtrlInfo found = existing ?? created;
            if (found == null)
            {
                return;   // reported by CreateObject
            }

            if (existing != null)
            {
                report.Kept++;
            }

            placed[element.GetAttribute("id")] = found;
            foreach (XmlNode node in element.ChildNodes)
            {
                XmlElement child = node as XmlElement;
                if (child != null && (child.Name == "item" || child.Name == "folder"))
                {
                    EnsureObject(child, found.treeNodeObject, placed, unused, report);
                }
            }
        }

        // An object beside the character exists when the template's link to it exists already.
        void EnsureBeside(OCIChar character, XmlElement element, Dictionary<string, ObjectCtrlInfo> placed, SetupReport report)
        {
            string id = element.GetAttribute("id");
            ObjectCtrlInfo found = null;
            foreach (XmlElement link in SetupTemplate().DocumentElement.SelectNodes("constraint"))
            {
                ObjectCtrlInfo partner;
                if (found == null && link.GetAttribute("child") == id && placed.TryGetValue(link.GetAttribute("parent"), out partner))
                {
                    found = ObjectOf(ConstrainedChild(partner.guideObject.transformTarget));
                }
            }

            if (found != null)
            {
                report.Kept++;
            }
            else
            {
                found = CreateObject(element, character.treeNodeObject.parent, report);
                if (found == null)
                {
                    return;
                }
            }

            placed[id] = found;
            Dictionary<TreeNodeObject, List<ObjectCtrlInfo>> unused = new Dictionary<TreeNodeObject, List<ObjectCtrlInfo>>();
            foreach (XmlNode node in element.ChildNodes)
            {
                XmlElement child = node as XmlElement;
                if (child != null && (child.Name == "item" || child.Name == "folder"))
                {
                    EnsureObject(child, found.treeNodeObject, placed, unused, report);
                }
            }
        }

        // The first object under the parent that matches the template entry and is not taken yet.
        static ObjectCtrlInfo TakeExisting(XmlElement element, TreeNodeObject parent, Dictionary<TreeNodeObject, List<ObjectCtrlInfo>> unused)
        {
            List<ObjectCtrlInfo> candidates;
            if (!unused.TryGetValue(parent, out candidates))
            {
                candidates = new List<ObjectCtrlInfo>();
                foreach (ObjectCtrlInfo info in Studio.Studio.Instance.dicObjectCtrl.Values)
                {
                    if (info.treeNodeObject != null && info.treeNodeObject.parent == parent)
                    {
                        candidates.Add(info);
                    }
                }

                unused[parent] = candidates;
            }

            int group = -1, category = -1, no = -1;
            bool resolved = element.Name == "item" && ResolveItem(element, out group, out category, out no);
            for (int i = 0; i < candidates.Count; i++)
            {
                OCIFolder folder = candidates[i] as OCIFolder;
                OIItemInfo item = candidates[i].objectInfo as OIItemInfo;
                bool same = element.Name == "folder"
                    ? folder != null && folder.name == element.GetAttribute("name")
                    : resolved && item != null && item.group == group && item.category == category && item.no == no;
                if (same)
                {
                    ObjectCtrlInfo match = candidates[i];
                    candidates.RemoveAt(i);
                    return match;
                }
            }

            return null;
        }

        ObjectCtrlInfo CreateObject(XmlElement element, TreeNodeObject parent, SetupReport report)
        {
            ObjectCtrlInfo created;
            if (element.Name == "folder")
            {
                OCIFolder folder = AddObjectFolder.Add();
                folder.name = element.GetAttribute("name");
                created = folder;
            }
            else
            {
                int group, category, no;
                if (!ResolveItem(element, out group, out category, out no))
                {
                    string mod = element.GetAttribute("mod");
                    report.Unavailable.Add(mod.Length > 0 ? mod + " " + element.GetAttribute("no") : "item " + element.GetAttribute("no"));
                    return null;
                }

                created = AddObjectItem.Add(group, category, no);
                if (created == null)
                {
                    report.Unavailable.Add("item " + no);
                    return null;
                }
            }

            if (parent != null)
            {
                Studio.Studio.Instance.treeNodeCtrl.SetParent(created.treeNodeObject, parent);
            }

            // Parenting keeps the world placement; the template's values are local to the parent.
            ChangeAmount amount = created.guideObject.changeAmount;
            amount.pos = (Vector4)ReadVector(element.GetAttribute("position"), Color.clear);
            amount.rot = (Vector4)ReadVector(element.GetAttribute("rotation"), Color.clear);
            amount.scale = (Vector4)ReadVector(element.GetAttribute("scale"), Color.white);
            OCIItem item = created as OCIItem;
            if (item != null)
            {
                ApplyItemLook(item, element);
                ApplyMaterialEditor(item, element);
            }

            report.Added++;
            return created;
        }

        // Colours, alpha, outline, emission and visibility as saved in the setup scene.
        static void ApplyItemLook(OCIItem item, XmlElement element)
        {
            string[] colors = element.GetAttribute("colors").Split('|');
            for (int i = 0; i < colors.Length && i < item.itemInfo.color.Length; i++)
            {
                item.SetColor(ReadVector(colors[i], item.itemInfo.color[i]), i);
            }

            item.SetAlpha(XmlConvert.ToSingle(element.GetAttribute("alpha")));
            item.SetLineColor(ReadVector(element.GetAttribute("lineColor"), item.itemInfo.lineColor));
            item.SetLineWidth(XmlConvert.ToSingle(element.GetAttribute("lineWidth")));
            item.SetEmissionColor(ReadVector(element.GetAttribute("emissionColor"), item.itemInfo.emissionColor));
            item.SetEmissionPower(XmlConvert.ToSingle(element.GetAttribute("emissionPower")));
            item.SetLightCancel(XmlConvert.ToSingle(element.GetAttribute("lightCancel")));
            if (element.GetAttribute("visible") == "0")
            {
                item.treeNodeObject.SetVisible(false);
            }
        }

        // ---- items from the item list -----------------------------------------------------------

        // The item's numbers in this game. A modded item is looked up through Sideloader by its mod
        // GUID and the mod's own number (Sideloader may renumber it); its group and category are
        // then wherever Studio's item list has that number.
        static bool ResolveItem(XmlElement element, out int group, out int category, out int no)
        {
            group = XmlConvert.ToInt32(element.GetAttribute("group"));
            category = XmlConvert.ToInt32(element.GetAttribute("category"));
            no = XmlConvert.ToInt32(element.GetAttribute("no"));
            string mod = element.GetAttribute("mod");
            if (mod.Length > 0)
            {
                int local = SideloaderItemNumber(mod, no);
                if (local < 0)
                {
                    return false;
                }

                no = local;
            }

            return FindItemList(no, ref group, ref category);
        }

        static int SideloaderItemNumber(string guid, int slot)
        {
            Type resolver = CompanionType("Sideloader.AutoResolver.UniversalAutoResolver");
            PropertyInfo infos = resolver == null ? null : resolver.GetProperty("LoadedStudioResolutionInfos", StaticFlags);
            IEnumerable list = infos == null ? null : infos.GetValue(null, null) as IEnumerable;
            if (list == null)
            {
                return -1;
            }

            foreach (object info in list)
            {
                if (Get<bool>(info, "ResolveItem") && Get<int>(info, "Slot") == slot && Get<string>(info, "GUID") == guid)
                {
                    return Get<int>(info, "LocalSlot");
                }
            }

            return -1;
        }

        static bool FindItemList(int no, ref int group, ref int category)
        {
            Dictionary<int, Dictionary<int, Dictionary<int, Info.ItemLoadInfo>>> items = Singleton<Info>.Instance.dicItemLoadInfo;
            Dictionary<int, Dictionary<int, Info.ItemLoadInfo>> categories;
            Dictionary<int, Info.ItemLoadInfo> numbers;
            if (items.TryGetValue(group, out categories) && categories.TryGetValue(category, out numbers) && numbers.ContainsKey(no))
            {
                return true;
            }

            foreach (KeyValuePair<int, Dictionary<int, Dictionary<int, Info.ItemLoadInfo>>> g in items)
            {
                foreach (KeyValuePair<int, Dictionary<int, Info.ItemLoadInfo>> c in g.Value)
                {
                    if (c.Value.ContainsKey(no))
                    {
                        group = g.Key;
                        category = c.Key;
                        return true;
                    }
                }
            }

            return false;
        }

        // ---- accessory points -----------------------------------------------------------------

        static TreeNodeObject AccessoryPointNode(OCIChar character, int point)
        {
            foreach (KeyValuePair<TreeNodeObject, int> node in character.dicAccessoryPoint)
            {
                if (node.Value == point)
                {
                    return node.Key;
                }
            }

            return null;
        }

        static ObjectCtrlInfo ObjectOf(Transform transform)
        {
            if (transform == null)
            {
                return null;
            }

            foreach (ObjectCtrlInfo info in Studio.Studio.Instance.dicObjectCtrl.Values)
            {
                if (info.guideObject != null && info.guideObject.transformTarget == transform)
                {
                    return info;
                }
            }

            return null;
        }

        // ---- Material Editor (optional, never referenced at compile time) ------------------------

        void ApplyMaterialEditor(OCIItem item, XmlElement element)
        {
            if (element.SelectSingleNode("renderer|shader|float|color") == null)
            {
                return;
            }

            Type studio = CompanionType("KK_Plugins.MaterialEditor.MEStudio");
            Type api = CompanionType("MaterialEditorAPI.MaterialAPI");
            MethodInfo getController = studio == null ? null : studio.GetMethod("GetSceneController", StaticFlags);
            object controller = getController == null ? null : getController.Invoke(null, null);
            if (controller == null || api == null)
            {
                Logger.LogWarning("Char Setup: Material Editor not found, " + element.GetAttribute("id") + " keeps its original look");
                return;
            }

            int id = item.objectInfo.dicKey;
            GameObject go = item.objectItem;
            Type properties = api.GetNestedType("RendererProperties", BindingFlags.Public | BindingFlags.NonPublic);
            foreach (XmlElement setting in element.SelectNodes("renderer|shader|float|color"))
            {
                try
                {
                    if (setting.Name == "renderer")
                    {
                        foreach (Renderer renderer in go.GetComponentsInChildren<Renderer>(true))
                        {
                            if (Formatted(renderer.name) == setting.GetAttribute("name"))
                            {
                                object property = Enum.ToObject(properties, XmlConvert.ToInt32(setting.GetAttribute("property")));
                                Invoke(controller, "SetRendererProperty", id, renderer, property, setting.GetAttribute("value"), true);
                            }
                        }

                        continue;
                    }

                    foreach (Material material in Materials(api, go, setting.GetAttribute("material")))
                    {
                        if (setting.Name == "shader")
                        {
                            if (setting.HasAttribute("shader"))
                            {
                                Invoke(controller, "SetMaterialShader", id, material, setting.GetAttribute("shader"), true);
                            }

                            Invoke(controller, "SetMaterialShaderRenderQueue", id, material, XmlConvert.ToInt32(setting.GetAttribute("queue")), true);
                        }
                        else if (setting.Name == "float")
                        {
                            Invoke(controller, "SetMaterialFloatProperty", id, material, setting.GetAttribute("property"), XmlConvert.ToSingle(setting.GetAttribute("value")), true);
                        }
                        else
                        {
                            Invoke(controller, "SetMaterialColorProperty", id, material, setting.GetAttribute("property"), ReadVector(setting.GetAttribute("value"), Color.white), true);
                        }
                    }
                }
                catch (Exception e)
                {
                    Logger.LogWarning("Char Setup: Material Editor " + setting.Name + " on " + element.GetAttribute("id") + ": " + Error(e));
                }
            }
        }

        // The item's materials of that name, as Material Editor lists them.
        static List<Material> Materials(Type api, GameObject go, string name)
        {
            List<Material> found = new List<Material>();
            MethodInfo rendererList = api.GetMethod("GetRendererList", StaticFlags, null, new Type[] { typeof(GameObject) }, null);
            MethodInfo materials = api.GetMethod("GetMaterials", StaticFlags, null, new Type[] { typeof(GameObject), typeof(Renderer) }, null);
            foreach (Renderer renderer in (IEnumerable)rendererList.Invoke(null, new object[] { go }))
            {
                foreach (Material material in (IEnumerable)materials.Invoke(null, new object[] { go, renderer }))
                {
                    if (material != null && Formatted(material.name) == name && !found.Contains(material))
                    {
                        found.Add(material);
                    }
                }
            }

            return found;
        }

        // Material Editor compares names without Unity's " (Instance)" suffix.
        static string Formatted(string name)
        {
            return name.Replace("(Instance)", "").Trim();
        }

        static void Invoke(object target, string method, params object[] arguments)
        {
            foreach (MethodInfo candidate in target.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public))
            {
                ParameterInfo[] parameters = candidate.GetParameters();
                if (candidate.Name != method || parameters.Length != arguments.Length)
                {
                    continue;
                }

                bool fits = true;
                for (int i = 0; i < parameters.Length && fits; i++)
                {
                    fits = arguments[i] == null || parameters[i].ParameterType.IsAssignableFrom(arguments[i].GetType());
                }

                if (fits)
                {
                    candidate.Invoke(target, arguments);
                    return;
                }
            }

            throw new MissingMethodException(target.GetType().Name, method);
        }

        // ---- NodesConstraints (optional, never referenced at compile time) ----------------------

        void EnsureSetupConstraints(Dictionary<string, ObjectCtrlInfo> placed, SetupReport report)
        {
            foreach (XmlElement link in SetupTemplate().DocumentElement.SelectNodes("constraint"))
            {
                ObjectCtrlInfo parent, child;
                if (!placed.TryGetValue(link.GetAttribute("parent"), out parent) || !placed.TryGetValue(link.GetAttribute("child"), out child))
                {
                    continue;   // an end is unavailable; already reported
                }

                object plugin = NodesConstraintsPlugin();
                if (plugin == null)
                {
                    report.Unavailable.Add("NodesConstraints");
                    return;
                }

                Transform parentTransform = parent.guideObject.transformTarget, childTransform = child.guideObject.transformTarget;
                if (ConstrainedChild(parentTransform) == childTransform)
                {
                    continue;
                }

                MethodInfo add = null;
                foreach (MethodInfo method in plugin.GetType().GetMethods(Flags))
                {
                    if (method.Name == "AddConstraint" && method.GetParameters().Length == 10)
                    {
                        add = method;
                    }
                }

                if (add == null)
                {
                    report.Unavailable.Add("NodesConstraints.AddConstraint");
                    return;
                }

                Color rotation = ReadVector(link.GetAttribute("rotationOffset"), new Color(0f, 0f, 0f, 1f));
                add.Invoke(plugin, new object[]
                {
                    link.GetAttribute("enabled") != "0", parentTransform, childTransform,
                    link.GetAttribute("position") == "1", (Vector3)(Vector4)ReadVector(link.GetAttribute("positionOffset"), Color.clear),
                    link.GetAttribute("rotation") == "1", new Quaternion(rotation.r, rotation.g, rotation.b, rotation.a),
                    link.GetAttribute("scale") == "1", (Vector3)(Vector4)ReadVector(link.GetAttribute("scaleOffset"), Color.white),
                    ""
                });
                report.Links++;
            }
        }

        static object NodesConstraintsPlugin()
        {
            Type type = CompanionType(NodesConstraintsType);
            FieldInfo self = type == null ? null : type.GetField("_self", StaticFlags);
            return self == null ? null : self.GetValue(null);
        }

        // The child of a NodesConstraints link whose parent is this transform, or null.
        internal static Transform ConstrainedChild(Transform parent)
        {
            object plugin = NodesConstraintsPlugin();
            IEnumerable links = plugin == null ? null : Get<IEnumerable>(plugin, "_constraints");
            if (links == null)
            {
                return null;
            }

            foreach (object link in links)
            {
                if (Get<Transform>(link, "parentTransform") == parent && !Get<bool>(link, "destroyed"))
                {
                    return Get<Transform>(link, "childTransform");
                }
            }

            return null;
        }
    }
}
