using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Xml;
using Studio;
using Timeline;
using UnityEngine;
using Native = Timeline.Timeline;

namespace GinTimelineHack
{
    // Bulk registration of Timeline rows from a template.
    //
    // A template uses the same XML shape that Timeline writes into a scene:
    //
    //   <root time="0">
    //     <interpolableGroup name="FACE">
    //       <interpolable owner="KKPE" id="boneRot" parameter="..." bgColorR="1" bgColorG="1" bgColorB="1" />
    //     </interpolableGroup>
    //   </root>
    //
    // Every row that the target does not have yet is created by its own native provider and
    // receives one key holding the current value. Rows that already exist keep their keys and
    // colour, so pressing a registration button twice only fills what is missing.
    //
    // With RegistrationReport.GatherExisting (Char Setup) an existing row is also moved into the
    // group the template gives it, wherever it was, taking the template's colour on the way;
    // and every template group is put in template
    // order: the template's rows first, then anything else the user keeps in that group. A group
    // that a move leaves empty is removed by Timeline itself.
    //
    // A row with setupObject="id" belongs to RegistrationReport.Targets[id] instead of the target
    // (Char Setup: rows of the items around a character). A group is shared by the target and
    // all of those objects.
    // The result is ordinary Timeline data: playback needs no GIN plugin.
    public sealed partial class Plugin
    {
        public sealed class RegistrationReport
        {
            public bool GatherExisting;      // move existing rows into the template's groups
            public readonly Dictionary<string, ObjectCtrlInfo> Targets = new Dictionary<string, ObjectCtrlInfo>();
            public int Added, Existing, Moved;
            public readonly List<string> Unavailable = new List<string>();

            public string Summary()
            {
                string text = Added + " added / " + Existing + " existing";
                if (Moved > 0)
                {
                    text += " (" + Moved + " moved into their groups)";
                }

                if (Unavailable.Count > 0)
                {
                    text += " / " + Unavailable.Count + " unavailable (" + Unavailable[0] + (Unavailable.Count > 1 ? ", ..." : "") + ")";
                }

                return text;
            }
        }

        // A template group. The native group is looked up or created only when a row needs it,
        // so a template whose rows all exist already never leaves an empty group behind.
        sealed class GroupSlot
        {
            public string Name;
            public GroupSlot Parent;
            public IGroupNode Node;
            public bool Searched;
            public readonly List<Interpolable> Rows = new List<Interpolable>();   // in template order
        }

        Type nativeGroupType;
        MethodInfo treeAddGroup, treeAddLeaf, treeParentTo, treeGetLeaf;

        // Entry point for other plugins (GIN Advance mode life hack calls it through reflection).
        public static string RegisterTracksFromXml(ObjectCtrlInfo target, string templateXml)
        {
            if (Self == null || !Self.attached)
            {
                return "GIN Timeline life hack is not attached to Timeline";
            }

            if (target == null)
            {
                return "No target object";
            }

            XmlDocument document = new XmlDocument();
            document.LoadXml(templateXml);
            RegistrationReport report = new RegistrationReport();
            Self.RegisterTracks(target, document.DocumentElement, report);
            string summary = "Timeline rows: " + report.Summary();
            Self.Note(summary);
            return summary;
        }

        internal void RegisterTracks(ObjectCtrlInfo target, XmlNode template, RegistrationReport report)
        {
            float time = ReadFloat(template, "time", 0f);
            List<GroupSlot> groups = new List<GroupSlot>();
            RegisterChildren(target, template, null, time, report, groups);
            if (report.GatherExisting)
            {
                foreach (GroupSlot group in groups)
                {
                    PutInTemplateOrder(group);
                }
            }

            Call("UpdateInterpolablesView");
            nextRefresh = 0;
            if (report.Unavailable.Count > 0)
            {
                Logger.LogInfo("Rows not registered: " + String.Join(", ", report.Unavailable.ToArray()));
            }
        }

        void RegisterChildren(ObjectCtrlInfo target, XmlNode parent, GroupSlot group, float time, RegistrationReport report, List<GroupSlot> groups)
        {
            foreach (XmlNode node in parent.ChildNodes)
            {
                if (node.Name == "interpolableGroup")
                {
                    GroupSlot child = new GroupSlot();
                    child.Name = Attribute(node, "name");
                    child.Parent = group;
                    groups.Add(child);
                    RegisterChildren(target, node, child, time, report, groups);
                }
                else if (node.Name == "interpolable")
                {
                    Interpolable track = RegisterTrack(target, node, group, time, report);
                    if (track != null && group != null)
                    {
                        group.Rows.Add(track);
                    }
                }
            }
        }

        // Returns the row, new or existing; null when the target cannot have it.
        Interpolable RegisterTrack(ObjectCtrlInfo groupOwner, XmlNode node, GroupSlot group, float time, RegistrationReport report)
        {
            ObjectCtrlInfo target = groupOwner;
            string setupObject = Attribute(node, SetupObjectAttribute);
            if (setupObject != null && !report.Targets.TryGetValue(setupObject, out target))
            {
                report.Unavailable.Add(TrackLabel(node) + " of " + setupObject);
                return null;
            }

            string label = TrackLabel(node);
            InterpolableModel model = FindModel(Attribute(node, "owner"), Attribute(node, "id"));
            // Like Timeline's scene loader, a row that carries its own parameter does not ask the
            // model whether the target fits: Material Editor's models only say yes while a property
            // is selected in Material Editor's window. Reading the value below still checks the row.
            if (model == null || (model.readParameterFromXml == null && !model.IsCompatibleWithTarget(target)))
            {
                report.Unavailable.Add(label);
                return null;
            }

            Dictionary<int, Interpolable> registered = Get<Dictionary<int, Interpolable>>(native, "_interpolables");
            Interpolable track;
            object value;
            try
            {
                track = CreateTrack(target, model, node);
                if (track == null)
                {
                    report.Unavailable.Add(label);
                    return null;
                }

                Interpolable existing;
                if (registered.TryGetValue(track.GetHashCode(), out existing))
                {
                    report.Existing++;
                    if (report.GatherExisting && MoveLeaf(existing, groupOwner, report, group))
                    {
                        // A row brought in from elsewhere takes its group's colour; rows that were
                        // in place already keep whatever colour the user gave them.
                        existing.color = ReadColor(node, existing.color);
                        report.Moved++;
                    }

                    return existing;
                }

                // Providers throw here when the bone or renderer named by the template is absent.
                value = track.GetValue();
            }
            catch (Exception e)
            {
                Logger.LogDebug("Row unavailable: " + label + " - " + Error(e));
                report.Unavailable.Add(label);
                return null;
            }

            if (value == null)
            {
                report.Unavailable.Add(label);
                return null;
            }

            track.color = ReadColor(node, track.color);
            track.alias = Attribute(node, "alias") ?? "";
            registered.Add(track.GetHashCode(), track);
            AddLeaf(track, groupOwner, report, group);
            track.keyframes.Add(time, new Timeline.Keyframe(value, track, AnimationCurve.Linear(0f, 0f, 1f, 1f)));
            report.Added++;
            return track;
        }

        // Same construction as the native scene loader.
        static Interpolable CreateTrack(ObjectCtrlInfo target, InterpolableModel model, XmlNode node)
        {
            if (model.readParameterFromXml == null)
            {
                return new Interpolable(target, model);
            }

            object parameter = model.readParameterFromXml(target, node);
            return parameter == null ? null : new Interpolable(target, parameter, model);
        }

        InterpolableModel FindModel(string owner, string id)
        {
            foreach (InterpolableModel model in Get<List<InterpolableModel>>(native, "_interpolableModelsList"))
            {
                if (model.owner == owner && model.id == id)
                {
                    return model;
                }
            }

            return null;
        }

        // ---- native tree -------------------------------------------------------------------

        void AddLeaf(Interpolable track, ObjectCtrlInfo target, RegistrationReport report, GroupSlot group)
        {
            object tree = Get<object>(native, "_interpolablesTree");
            if (treeAddLeaf == null)
            {
                treeAddLeaf = RequireTreeMethod(tree, "AddLeaf");
            }

            treeAddLeaf.Invoke(tree, new object[] { track, ResolveGroup(tree, target, report, group) });
        }

        // Move an existing row into its template group (the top level when the template has none).
        // Returns false when it is there already.
        bool MoveLeaf(Interpolable track, ObjectCtrlInfo target, RegistrationReport report, GroupSlot group)
        {
            object tree = Get<object>(native, "_interpolablesTree");
            if (treeParentTo == null)
            {
                treeParentTo = ParentToMethod(tree);
                treeGetLeaf = RequireTreeMethod(tree, "GetLeafNode", 1);
            }

            INode leaf = (INode)treeGetLeaf.Invoke(tree, new object[] { track });
            if (leaf == null)
            {
                return false;
            }

            IGroupNode destination = ResolveGroup(tree, target, report, group);
            if (leaf.parent == destination)
            {
                return false;
            }

            treeParentTo.Invoke(tree, new object[] { leaf, destination });
            return true;
        }

        // A template group's children: its rows in template order, then whatever else is in it,
        // in the order it had.
        static void PutInTemplateOrder(GroupSlot group)
        {
            if (group.Node == null || group.Rows.Count == 0)
            {
                return;
            }

            List<INode> children = group.Node.children;
            List<INode> ordered = new List<INode>();
            foreach (Interpolable row in group.Rows)
            {
                foreach (INode child in children)
                {
                    LeafNode<Interpolable> leaf = child as LeafNode<Interpolable>;
                    if (leaf != null && ReferenceEquals(leaf.obj, row) && !ordered.Contains(child))
                    {
                        ordered.Add(child);
                        break;
                    }
                }
            }

            foreach (INode child in children)
            {
                if (!ordered.Contains(child))
                {
                    ordered.Add(child);
                }
            }

            children.Clear();
            children.AddRange(ordered);
        }

        // Tree.ParentTo(INode, GroupNode): the overload for one node, not the one for a list.
        static MethodInfo ParentToMethod(object tree)
        {
            foreach (MethodInfo method in tree.GetType().GetMethods(Flags))
            {
                ParameterInfo[] parameters = method.GetParameters();
                if (method.Name == "ParentTo" && parameters.Length == 2 && parameters[0].ParameterType == typeof(INode))
                {
                    return method;
                }
            }

            throw new NotSupportedException("Timeline tree contract differs: ParentTo");
        }

        // Reuse the target's group of that name, or create it.
        IGroupNode ResolveGroup(object tree, ObjectCtrlInfo target, RegistrationReport report, GroupSlot slot)
        {
            if (slot == null)
            {
                return null;
            }

            if (slot.Node != null)
            {
                return slot.Node;
            }

            IGroupNode parent = ResolveGroup(tree, target, report, slot.Parent);
            if (!slot.Searched)
            {
                slot.Searched = true;
                slot.Node = FindGroup(parent == null ? RootNodes() : parent.children, slot.Name, target, report.Targets.Values);
            }

            if (slot.Node == null)
            {
                if (treeAddGroup == null)
                {
                    treeAddGroup = RequireTreeMethod(tree, "AddGroup");
                    nativeGroupType = typeof(Native).GetNestedType("InterpolableGroup", BindingFlags.Public | BindingFlags.NonPublic);
                }

                object data = Activator.CreateInstance(nativeGroupType, true);
                Set(data, "name", slot.Name);
                slot.Node = (IGroupNode)treeAddGroup.Invoke(tree, new object[] { data, parent });
            }

            return slot.Node;
        }

        // Groups are shared by name only between rows of the same object (or of the objects
        // registered with it): two characters each get their own "BODY" group.
        static IGroupNode FindGroup(List<INode> siblings, string name, ObjectCtrlInfo target, ICollection<ObjectCtrlInfo> related)
        {
            foreach (INode node in siblings)
            {
                IGroupNode group = node as IGroupNode;
                if (group == null || Get<string>(Get<object>(group, "obj"), "name") != name)
                {
                    continue;
                }

                List<Interpolable> rows = Descendants(group);
                if (rows.Count == 0)
                {
                    return group;
                }

                foreach (Interpolable row in rows)
                {
                    if (row.oci == target || related.Contains(row.oci))
                    {
                        return group;
                    }
                }
            }

            return null;
        }

        static MethodInfo RequireTreeMethod(object tree, string name)
        {
            return RequireTreeMethod(tree, name, 2);
        }

        static MethodInfo RequireTreeMethod(object tree, string name, int parameterCount)
        {
            MethodInfo method = tree.GetType().GetMethod(name, Flags);
            if (method == null || method.GetParameters().Length != parameterCount)
            {
                throw new NotSupportedException("Timeline tree contract differs: " + name);
            }

            return method;
        }

        // ---- template attributes -----------------------------------------------------------

        static string Attribute(XmlNode node, string name)
        {
            XmlAttribute attribute = node.Attributes == null ? null : node.Attributes[name];
            return attribute == null ? null : attribute.Value;
        }

        static float ReadFloat(XmlNode node, string name, float fallback)
        {
            string text = Attribute(node, name);
            return text == null ? fallback : XmlConvert.ToSingle(text);
        }

        static Color ReadColor(XmlNode node, Color fallback)
        {
            if (Attribute(node, "bgColorR") == null)
            {
                return fallback;
            }

            return new Color(ReadFloat(node, "bgColorR", 1f), ReadFloat(node, "bgColorG", 1f), ReadFloat(node, "bgColorB", 1f));
        }

        // Short name for messages: "boneRot cf_j_tang_03".
        static string TrackLabel(XmlNode node)
        {
            string path = Attribute(node, "guideObjectPath") ?? Attribute(node, "parameter") ?? Attribute(node, "parameter1") ?? "";
            string leaf = path.Substring(path.LastIndexOf('/') + 1);
            string index = Attribute(node, "parameter2");
            return (Attribute(node, "id") + " " + leaf + (index == null ? "" : " " + index)).Trim();
        }

        internal static string Invariant(float value)
        {
            return XmlConvert.ToString(value);
        }

        internal static string Invariant(int value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }
    }
}
