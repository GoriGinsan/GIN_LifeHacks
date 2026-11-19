using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Studio;
using UnityEngine;

namespace GinPadHack
{
    // A selected object together with its KKPE editors: a character, or (for the Shapes tab of
    // item mode) an item. KKPE puts a PoseController on both.
    internal sealed class PadTarget
    {
        internal ObjectCtrlInfo Owner;  // the character or the item
        internal OCIChar Character;     // null for an item
        internal Component Pose;        // HSPE.PoseController on the object
        internal object Bones, Blends;  // KKPE's BonesEditor and BlendShapesEditor
        internal string Name;

        readonly Dictionary<string, Transform> bones = new Dictionary<string, Transform>();
        readonly Dictionary<Transform, OCIChar.BoneInfo> fkNodes = new Dictionary<Transform, OCIChar.BoneInfo>();
        int fkNodeCount = -1;

        // A bone of the character's own skeleton by name; null when it does not exist.
        internal Transform Bone(string name)
        {
            Transform bone;
            if (bones.TryGetValue(name, out bone) && bone != null)
            {
                return bone;
            }

            bone = null;
            GameObject skeleton = Character.charInfo.objBodyBone;
            if (skeleton != null)
            {
                foreach (Transform candidate in skeleton.GetComponentsInChildren<Transform>(true))
                {
                    // A second, inactive copy of a bone can exist (the tongue has one): prefer the live one.
                    if (candidate.name == name && (bone == null || (candidate.gameObject.activeInHierarchy && !bone.gameObject.activeInHierarchy)))
                    {
                        bone = candidate;
                    }
                }
            }

            bones[name] = bone;
            return bone;
        }

        // The Studio FK node that currently drives this bone, or null.
        // KKPE leaves such a bone alone, so its rotation must be written to the FK node.
        internal OCIChar.BoneInfo ActiveFk(Transform bone)
        {
            if (!Character.oiCharInfo.enableFK)
            {
                return null;
            }

            if (fkNodeCount != Character.listBones.Count)
            {
                fkNodeCount = Character.listBones.Count;
                fkNodes.Clear();
                foreach (OCIChar.BoneInfo node in Character.listBones)
                {
                    if (node.guideObject != null && node.guideObject.transformTarget != null)
                    {
                        fkNodes[node.guideObject.transformTarget] = node;
                    }
                }
            }

            OCIChar.BoneInfo found;
            return fkNodes.TryGetValue(bone, out found) && found.active ? found : null;
        }

        // The Studio IK target by its name without Studio's "(work)" suffix: "cf_t_hips" finds "cf_t_hips(work)".
        internal OCIChar.IKInfo IkTarget(string name)
        {
            foreach (OCIChar.IKInfo target in Character.listIKTarget)
            {
                Transform transform = target.guideObject == null ? null : target.guideObject.transformTarget;
                if (transform != null && (transform.name == name + "(work)" || transform.name == name))
                {
                    return target;
                }
            }

            return null;
        }
    }

    // One renderer with Blend Shapes, as KKPE tracks it.
    internal sealed class BlendMesh
    {
        internal object Wrapper;        // KKPE's private BlendRenderer
        internal SkinnedMeshRenderer Renderer;
        internal string Role;           // KKPE's alias: "Eyes / Mouth", "Eyebrows", "Teeth", "Body" ...
    }

    // Everything this plugin reads from or writes to KKPE (HSPE 2.21).
    // All writes go through KKPE's own setters, so the values are ordinary KKPE data:
    // they are saved with the scene and animated by Timeline exactly like manual edits.
    internal static class Kkpe
    {
        internal const string FaceRole = "Eyes / Mouth", BrowRole = "Eyebrows", TeethRole = "Teeth";

        static readonly Type[] NameAndWeight = { typeof(string), typeof(float) };
        static readonly Type[] BoneAndVector = { typeof(Transform), typeof(Vector3) };
        static readonly Type[] BoneAndQuaternion = { typeof(Transform), typeof(Quaternion) };
        static readonly Type[] ResetParameters = { typeof(Transform), typeof(Transform), typeof(bool), typeof(int) };

        static PadTarget current;
        static int currentFrame = -1;

        // The first selected character, or null when none is selected or KKPE is not installed.
        internal static PadTarget Target()
        {
            if (currentFrame == Time.frameCount)
            {
                return current;
            }

            currentFrame = Time.frameCount;
            OCIChar character = SelectedCharacter();
            if (character == null)
            {
                current = null;
            }
            else if (current == null || current.Character != character || current.Pose == null)
            {
                current = Attach(character);
            }

            return current;
        }

        static OCIChar SelectedCharacter()
        {
            if (!Singleton<Studio.Studio>.IsInstance())
            {
                return null;
            }

            foreach (ObjectCtrlInfo selected in Singleton<Studio.Studio>.Instance.treeNodeCtrl.selectObjectCtrl)
            {
                OCIChar character = selected as OCIChar;
                if (character != null)
                {
                    return character;
                }
            }

            return null;
        }

        static PadTarget Attach(OCIChar character)
        {
            Type poseType = Reflect.Type("HSPE.PoseController");
            Transform root = character.guideObject == null ? null : character.guideObject.transformTarget;
            Component pose = poseType == null || root == null ? null : root.GetComponent(poseType);
            if (pose == null)
            {
                return null;
            }

            PadTarget target = new PadTarget();
            target.Owner = character;
            target.Character = character;
            target.Pose = pose;
            target.Bones = Reflect.Get(pose, "_bonesEditor");
            target.Blends = Reflect.Get(pose, "_blendShapesEditor");
            target.Name = character.treeNodeObject == null ? pose.name : character.treeNodeObject.textName;
            return target.Bones == null || target.Blends == null ? null : target;
        }

        // KKPE's Blend Shape editor on an item; null when KKPE has none there.
        internal static PadTarget ItemTarget(ObjectCtrlInfo item)
        {
            Type poseType = Reflect.Type("HSPE.PoseController");
            Transform root = item.guideObject == null ? null : item.guideObject.transformTarget;
            Component pose = poseType == null || root == null ? null : root.GetComponent(poseType);
            if (pose == null)
            {
                return null;
            }

            PadTarget target = new PadTarget();
            target.Owner = item;
            target.Pose = pose;
            target.Blends = Reflect.Get(pose, "_blendShapesEditor");
            target.Name = item.treeNodeObject == null ? pose.name : item.treeNodeObject.textName;
            return target.Blends == null ? null : target;
        }

        // KKPE starts its editor on a new item switched off ("to optimize things"), and an editor
        // that is off applies no Blend Shape weight. Switch it on, as KKPE's own "Enable" button
        // does; KKPE saves that with the scene. True when it was off.
        internal static bool SwitchOn(PadTarget target)
        {
            Behaviour pose = target.Pose as Behaviour;
            if (pose == null || pose.enabled)
            {
                return false;
            }

            pose.enabled = true;
            return true;
        }

        internal static bool IsSwitchedOn(PadTarget target)
        {
            Behaviour pose = target.Pose as Behaviour;
            return pose != null && pose.enabled;
        }

        // ---- Blend Shapes -------------------------------------------------------------------

        internal static List<BlendMesh> Meshes(PadTarget target)
        {
            List<BlendMesh> meshes = new List<BlendMesh>();
            IDictionary renderers = Reflect.Get<IDictionary>(target.Blends, "_blendRenderers");
            IDictionary aliases = Reflect.Get<IDictionary>(target.Blends.GetType(), "_skinnedMeshAliases");
            if (renderers == null)
            {
                return meshes;
            }

            foreach (DictionaryEntry entry in renderers)
            {
                SkinnedMeshRenderer renderer = Reflect.Get<SkinnedMeshRenderer>(entry.Value, "_renderer");
                if (renderer == null || renderer.sharedMesh == null)
                {
                    continue;
                }

                BlendMesh mesh = new BlendMesh();
                mesh.Wrapper = entry.Value;
                mesh.Renderer = renderer;
                mesh.Role = aliases != null && aliases.Contains(renderer.name) ? Convert.ToString(aliases[renderer.name]) : "";
                meshes.Add(mesh);
            }

            return meshes;
        }

        // The weight KKPE holds for a Blend Shape, or the mesh's own weight when KKPE has no override.
        internal static float Weight(BlendMesh mesh, int index)
        {
            IDictionary overrides = Reflect.Get<IDictionary>(mesh.Wrapper, "_dirtyBlends");
            string name = mesh.Renderer.sharedMesh.GetBlendShapeName(index);
            if (overrides != null && overrides.Contains(name))
            {
                return Convert.ToSingle(Reflect.Get(overrides[name], "weight"));
            }

            return mesh.Renderer.GetBlendShapeWeight(index);
        }

        // Set one Blend Shape and, like Timeline's "BS (renderer, L)" rows, the same shape of the
        // linked renderers (eyelashes, tears, ...). Without the link a closed eye loses its eyelashes.
        internal static bool SetWeight(BlendMesh mesh, string shape, float weight)
        {
            if (!SetOverride(mesh.Wrapper, shape, weight))
            {
                return false;
            }

            IList linked = Reflect.Get<IList>(mesh.Wrapper, "_linkedBlendRenderers");
            if (linked == null || linked.Count == 0)
            {
                return true;
            }

            IDictionary indices = Reflect.Get<IDictionary>(mesh.Wrapper, "_blendIndics");
            IList keys = Reflect.Get<IList>(mesh.Wrapper, "_linkKeynameList");
            int index = indices != null && indices.Contains(shape) ? (int)indices[shape] : -1;
            if (keys == null || index < 0 || index >= keys.Count)
            {
                return true;   // not one of the linked (eye) shapes
            }

            object key = keys[index];
            foreach (object other in linked)
            {
                IDictionary otherKeys = Reflect.Get<IDictionary>(other, "_linkKeynames");
                IList otherNames = Reflect.Get<IList>(other, "_blendNames");
                if (otherKeys != null && otherNames != null && otherKeys.Contains(key))
                {
                    SetOverride(other, (string)otherNames[(int)otherKeys[key]], weight);
                }
            }

            return true;
        }

        static bool SetOverride(object wrapper, string shape, float weight)
        {
            MethodInfo setter = Reflect.Method(wrapper.GetType(), "SetBlendShapeWeight", NameAndWeight);
            return setter != null && setter.Invoke(wrapper, new object[] { shape, weight }) != null;
        }

        // First Blend Shape index of KKPE's "Mouth" section on the face renderer.
        internal static int MouthStart(PadTarget target)
        {
            bool female = Reflect.Get<bool>(Reflect.Get(target.Blends, "_target"), "isFemale");
            IDictionary separators = Reflect.Get<IDictionary>(target.Blends.GetType(), female ? "_femaleSeparators" : "_maleSeparators");
            if (separators != null)
            {
                foreach (DictionaryEntry entry in separators)
                {
                    if (Convert.ToString(entry.Value) == "Mouth")
                    {
                        return Convert.ToInt32(entry.Key);
                    }
                }
            }

            return int.MaxValue;
        }

        // KKPE alias if the user renamed the shape, otherwise the name without its mesh prefix
        // ("eye_face.f00_egao_cl" -> "f00_egao_cl"; see ShortName).
        internal static string ShapeLabel(PadTarget target, string shape)
        {
            IDictionary aliases = Reflect.Get<IDictionary>(target.Blends.GetType(), "_blendShapeAliases");
            if (aliases != null && aliases.Contains(shape))
            {
                return Convert.ToString(aliases[shape]);
            }

            return ShortName(shape);
        }

        // The name without the game's mesh prefix: "eye_face.f00_egao_cl" -> "f00_egao_cl",
        // "mayuge.mayu00_def_cl" -> "mayu00_def_cl".
        //
        // Only that one prefix is removed. Names that use dots themselves keep every part, because
        // the last part alone is often just "L" or "R": GIN Kupaa3 "P.Open1.L", "S.Huge.R",
        // "P.Out2.add.U", GIN Puffy "vagina.open". The game's prefixes are lower case, and what
        // follows them is a single word with underscores.
        internal static string ShortName(string shape)
        {
            int dot = shape.IndexOf('.');
            if (dot <= 0 || dot == shape.Length - 1)
            {
                return shape;
            }

            string prefix = shape.Substring(0, dot), rest = shape.Substring(dot + 1);
            bool gamePrefix = prefix == prefix.ToLowerInvariant() && rest.IndexOf('.') < 0 && rest.IndexOf('_') > 0;
            return gamePrefix ? rest : shape;
        }

        // ---- bones ---------------------------------------------------------------------------

        internal static bool BonesEnabled(PadTarget target)
        {
            return Reflect.Get<bool>(target.Bones, "isEnabled");
        }

        internal static Vector3 Position(PadTarget target, Transform bone)
        {
            object value = Override(target, bone, "position");
            return value != null ? (Vector3)value : bone.localPosition;
        }

        internal static Vector3 Scale(PadTarget target, Transform bone)
        {
            object value = Override(target, bone, "scale");
            return value != null ? (Vector3)value : bone.localScale;
        }

        internal static Quaternion Rotation(PadTarget target, Transform bone)
        {
            OCIChar.BoneInfo fk = target.ActiveFk(bone);
            if (fk != null)
            {
                return Quaternion.Euler(fk.guideObject.changeAmount.rot);
            }

            object value = Override(target, bone, "rotation");
            return value != null ? (Quaternion)value : bone.localRotation;
        }

        internal static void SetPosition(PadTarget target, Transform bone, Vector3 position)
        {
            Reflect.Call(target.Bones, "SetBonePosition", BoneAndVector, bone, position);
        }

        internal static void SetScale(PadTarget target, Transform bone, Vector3 scale)
        {
            Reflect.Call(target.Bones, "SetBoneScale", BoneAndVector, bone, scale);
        }

        internal static void SetRotation(PadTarget target, Transform bone, Quaternion rotation)
        {
            OCIChar.BoneInfo fk = target.ActiveFk(bone);
            if (fk != null)
            {
                fk.guideObject.changeAmount.rot = rotation.eulerAngles;
            }
            else
            {
                Reflect.Call(target.Bones, "SetBoneRotation", BoneAndQuaternion, bone, rotation);
            }
        }

        // Put a bone back to its default: remove KKPE's overrides, as its Reset Pos. / Rot. / Scale
        // buttons do. A bone that Studio FK controls has its rotation on the FK node, where the
        // default is zero.
        internal static void ResetBone(PadTarget target, Transform bone, bool position, bool rotation, bool scale)
        {
            if (position)
            {
                Reflect.Call(target.Bones, "ResetBonePos", ResetParameters, bone, null, false, 0);
            }

            if (rotation)
            {
                OCIChar.BoneInfo fk = target.ActiveFk(bone);
                if (fk != null)
                {
                    fk.guideObject.changeAmount.rot = Vector3.zero;
                }

                Reflect.Call(target.Bones, "ResetBoneRot", ResetParameters, bone, null, false, 0);
            }

            if (scale)
            {
                Reflect.Call(target.Bones, "ResetBoneScale", ResetParameters, bone, null, false, 0);
            }
        }

        // KKPE's override of one component ("position", "rotation" or "scale"); null without one.
        static object Override(PadTarget target, Transform bone, string component)
        {
            IDictionary overrides = Reflect.Get<IDictionary>(target.Bones, "_dirtyBones");
            if (overrides == null || !overrides.Contains(bone.gameObject))
            {
                return null;
            }

            return OverrideValue(overrides[bone.gameObject], component);
        }

        // One component of KKPE's TransformData; null when KKPE leaves it to the game.
        static object OverrideValue(object transformData, string component)
        {
            object editable = Reflect.Get(transformData, component);
            return Reflect.Get<bool>(editable, "hasValue") ? Reflect.Get(editable, "value") : null;
        }

        // Every bone that KKPE overrides, with its overrides (for the Body tab's Undo).
        internal static Dictionary<Transform, BoneOverride> Overrides(PadTarget target)
        {
            Dictionary<Transform, BoneOverride> result = new Dictionary<Transform, BoneOverride>();
            IDictionary overrides = Reflect.Get<IDictionary>(target.Bones, "_dirtyBones");
            if (overrides == null)
            {
                return result;
            }

            foreach (DictionaryEntry entry in overrides)
            {
                GameObject bone = entry.Key as GameObject;
                if (bone == null)
                {
                    continue;
                }

                BoneOverride values = new BoneOverride();
                values.Position = (Vector3?)OverrideValue(entry.Value, "position");
                values.Rotation = (Quaternion?)OverrideValue(entry.Value, "rotation");
                values.Scale = (Vector3?)OverrideValue(entry.Value, "scale");
                result[bone.transform] = values;
            }

            return result;
        }

        // Give a bone exactly these overrides: a part without a value is removed, as KKPE's Reset
        // buttons do. Unlike SetRotation this never writes to an FK node (Undo restores those itself).
        internal static void SetOverrides(PadTarget target, Transform bone, BoneOverride values)
        {
            if (values.Position.HasValue)
            {
                SetPosition(target, bone, values.Position.Value);
            }
            else
            {
                Reflect.Call(target.Bones, "ResetBonePos", ResetParameters, bone, null, false, 0);
            }

            if (values.Rotation.HasValue)
            {
                Reflect.Call(target.Bones, "SetBoneRotation", BoneAndQuaternion, bone, values.Rotation.Value);
            }
            else
            {
                Reflect.Call(target.Bones, "ResetBoneRot", ResetParameters, bone, null, false, 0);
            }

            if (values.Scale.HasValue)
            {
                SetScale(target, bone, values.Scale.Value);
            }
            else
            {
                Reflect.Call(target.Bones, "ResetBoneScale", ResetParameters, bone, null, false, 0);
            }
        }

        // ---- limb copy (the buttons on KKPE's own window) --------------------------------------

        // KKPE's CopyLimbToTwin(chain, boneGroup), CopyHandToTwin(boneGroup) or SwapPose().
        internal static bool CallPose(PadTarget target, string method, params int[] enumValues)
        {
            foreach (MethodInfo candidate in target.Pose.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance))
            {
                ParameterInfo[] parameters = candidate.GetParameters();
                if (candidate.Name != method || parameters.Length != enumValues.Length)
                {
                    continue;
                }

                object[] arguments = new object[parameters.Length];
                for (int i = 0; i < parameters.Length; i++)
                {
                    arguments[i] = Enum.ToObject(parameters[i].ParameterType, enumValues[i]);
                }

                candidate.Invoke(target.Pose, arguments);
                return true;
            }

            return false;
        }
    }
}
