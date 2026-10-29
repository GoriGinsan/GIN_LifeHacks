using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
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
        MethodInfo nativeFindOccupant;
        Version nativeVersion;
        bool contractValidated;





        // These two released builds are validated deliberately. Unknown builds keep native UI intact.
        // The old binary remains the compilation baseline; no new public assembly reference is required.
        bool ValidateNativeContract()
        {
            if (contractValidated)
            {
                return true;
            }

            nativeVersion = typeof(Native).Assembly.GetName().Version;
            bool legacy = nativeVersion == new Version(1, 4, 2, 0);
            bool current = nativeVersion == new Version(1, 5, 6, 0);
            if (!legacy && !current)
            {
                string text = "Supported Timeline builds: 1.4.2 / 1.5.6.0; detected " + nativeVersion;
                if (message != text)
                {
                    Logger.LogWarning(text);
                }

                Note(text);
                return false;
            }
            Type nativeType = typeof(Native);
            RequireField(nativeType, "_self", nativeType, true);
            RequireField(nativeType, "_ui", typeof(Canvas), false);
            RequireField(nativeType, "_timelineWindow", typeof(RectTransform), false);
            RequireField(nativeType, "_resizeHandle", typeof(RectTransform), false);
            RequireField(nativeType, "_keyframesContainer", typeof(RectTransform), false);
            RequireField(nativeType, "_verticalScrollView", typeof(ScrollRect), false);
            RequireField(nativeType, "_selectedOCI", typeof(Studio.ObjectCtrlInfo), false);
            RequireField(nativeType, "_selectedInterpolables", typeof(List<Interpolable>), false);
            RequireField(nativeType, "_gridHeights", typeof(List<float>), false);
            RequireField(nativeType, "_interpolableModelsList", typeof(List<InterpolableModel>), false);
            RequireField(nativeType, "_zoomLevel", typeof(float), false);
            RequireField(nativeType, "_blockLength", typeof(float), false);
            RequireField(nativeType, "_divisions", typeof(int), false);
            RequireField(nativeType, "_keyframeWindow", typeof(GameObject), false);

            Type group = nativeType.GetNestedType("InterpolableGroup", BindingFlags.Public | BindingFlags.NonPublic);
            if (group == null)
            {
                throw new NotSupportedException("Timeline native group type differs");
            }

            RequireField(group, "name", typeof(string), false);
            RequireField(group, "expanded", typeof(bool), false);
            Type groupNode = typeof(GroupNode<>).MakeGenericType(group);
            Type treeType = typeof(Tree<,>).MakeGenericType(typeof(Interpolable), group);
            RequireField(nativeType, "_interpolablesTree", treeType, false);
            RequireMethod(treeType, "MoveUp", typeof(void), false, typeof(IEnumerable<INode>));
            RequireMethod(treeType, "MoveDown", typeof(void), false, typeof(IEnumerable<INode>));
            RequireMethod(treeType, "GetLeafNode", typeof(LeafNode<Interpolable>), false, typeof(Interpolable));
            RequireMethod(treeType, "GroupTogether", groupNode, false, typeof(IEnumerable<Interpolable>), group);
            RequireMethod(treeType, "Remove", typeof(void), false, typeof(INode));

            Type header = RequireDisplay("HeaderDisplay", "_displayedOwnerHeader");
            RequireField(header, "container", typeof(RectTransform), false);
            RequireField(header, "group", groupNode, false);
            Type row = RequireDisplay("InterpolableDisplay", "_displayedInterpolables");
            RequireField(row, "interpolable", typeof(LeafNode<Interpolable>), false);
            Type model = RequireDisplay("InterpolableModelDisplay", "_displayedInterpolableModels");
            RequireField(model, "model", typeof(InterpolableModel), false);
            Type key = RequireDisplay("KeyframeDisplay", "_displayedKeyframes");
            RequireField(key, "keyframe", typeof(Timeline.Keyframe), false);

            foreach (string m in new string[] { "UpdateInterpolablesView", "UpdateGrid", "UpdateInterpolableSelection", "ClearSelectedInterpolables", "UseCurrentValue" })
            {
                RequireMethod(nativeType, m, typeof(void), false);
            }

            RequireMethod(nativeType, "OnKeyframeContainerMouseDown", typeof(void), false, typeof(PointerEventData));
            RequireMethod(nativeType, "AddInterpolable", typeof(Interpolable), false, typeof(InterpolableModel));
            RequireMethod(nativeType, "AddKeyframe", typeof(void), false, typeof(Interpolable), typeof(float));
            RequireMethod(nativeType, "RemoveInterpolable", typeof(void), false, typeof(Interpolable));
            RequireMethod(nativeType, "SelectKeyframes", typeof(void), false, typeof(IEnumerable<Key>));
            RequireMethod(nativeType, "SelectInterpolable", typeof(void), false, typeof(Interpolable[]));
            RequireMethod(nativeType, "GetSelectedKeyframes", typeof(IEnumerable<Key>), true);
            RequireMethod(nativeType, "GetAllInterpolables", typeof(IEnumerable<Interpolable>), true, typeof(bool));
            RequireProperty(nativeType, "InterfaceVisible", typeof(bool), true);
            RequireProperty(nativeType, "playbackTime", typeof(float), true);
            RequireField(typeof(Interpolable), "keyframes", typeof(SortedList<float, Timeline.Keyframe>), false);
            RequireField(typeof(Timeline.Keyframe), "parent", typeof(Interpolable), false);
            RequireField(typeof(Timeline.Keyframe), "value", typeof(object), false);
            RequireField(typeof(Timeline.Keyframe), "curve", typeof(AnimationCurve), false);
            nativeFindOccupant = current ? RequireMethod(
                nativeType,
                "FindOccupant",
                typeof(Timeline.Keyframe),
                true,
                typeof(Interpolable),
                typeof(float),
                typeof(Timeline.Keyframe)) : null;

            // The r2.25 publisher specifically requires updated ShalltyUtils. Keep old Shallty/Timeline
            // paired instead of claiming a mixed pair works merely because both assemblies loaded.
            foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (a.GetName().Name != "KK_ShalltyUtils")
                {
                    continue;
                }

                Version v = a.GetName().Version;
                if (current && v < new Version(1, 3, 1, 0))
                {
                    throw new NotSupportedException("Incompatible ShalltyUtils " + v + " loaded from \"" + a.Location + "\". " +
                    "When using ShalltyUtils with Timeline 1.5.6.0, update ShalltyUtils to 1.3.1 or later. " +
                    "Close Studio and replace this DLL with the KK build from https://github.com/Lusiocc/ShalltyUtils/releases/tag/v1.3.1 . " +
                    "Updating HSPlugins does not update ShalltyUtils.");
                }
            }
            contractValidated = true;
            Logger.LogInfo("Validated Timeline " + nativeVersion + " UI, selection, native tree and key insertion contract");
            return true;
        }

        static Type RequireDisplay(string name, string listField)
        {
            Type type = typeof(Native).GetNestedType(name, BindingFlags.Public | BindingFlags.NonPublic);
            if (type == null)
            {
                throw new NotSupportedException("Timeline display type differs: " + name);
            }

            RequireField(typeof(Native), listField, typeof(List<>).MakeGenericType(type), false);
            RequireField(type, "gameObject", typeof(GameObject), false);
            return type;
        }

        static FieldInfo RequireField(Type type, string name, Type expected, bool isStatic)
        {
            FieldInfo field = type.GetField(name, isStatic ? StaticFlags : Flags);
            if (field == null || field.IsStatic != isStatic || field.FieldType != expected)
            {
                throw new NotSupportedException("Timeline field contract differs: " + type.Name + "." + name);
            }

            return field;
        }

        static MethodInfo RequireMethod(Type type, string name, Type expectedReturn, bool isStatic, params Type[] arguments)
        {
            MethodInfo method = type.GetMethod(name, isStatic ? StaticFlags : Flags, null, arguments, null);
            if (method == null || method.IsStatic != isStatic || method.ReturnType != expectedReturn)
            {
                throw new NotSupportedException("Timeline method contract differs: " + type.Name + "." + name);
            }

            return method;
        }

        static PropertyInfo RequireProperty(Type type, string name, Type expected, bool isStatic)
        {
            PropertyInfo p = type.GetProperty(name, isStatic ? StaticFlags : Flags);
            if (p == null || !p.CanRead || p.PropertyType != expected)
            {
                throw new NotSupportedException("Timeline property contract differs: " + name);
            }

            return p;
        }

        internal bool OccupiedAt(Interpolable track, float time)
        {
            // Latest Timeline rejects approximately-equal times after pixel-coordinate round trips.
            // Use the provider's own rule; do not bypass it or report its rejection as a failed insert.
            return nativeFindOccupant != null ? nativeFindOccupant.Invoke(null, new object[] { track, time, null }) != null : track.keyframes.ContainsKey(time);
        }
    }
}
