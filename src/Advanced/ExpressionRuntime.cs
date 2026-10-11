using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace GinAdvanceHack
{
    internal static class ExpressionReflect
    {
        internal const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        internal static Type Type(string name)
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies()) { Type value = assembly.GetType(name, false); if (value != null) return value; }
            return null;
        }
        internal static object Get(object instance, string name)
        {
            if (instance == null) return null;
            Type type = instance as Type ?? instance.GetType(); object target = instance is Type ? null : instance;
            for (; type != null; type = type.BaseType)
            {
                FieldInfo field = type.GetField(name, Flags | BindingFlags.DeclaredOnly); if (field != null) return field.GetValue(target);
                PropertyInfo property = type.GetProperty(name, Flags | BindingFlags.DeclaredOnly);
                // BonesEditor overrides only the isEnabled setter; its getter remains on the base class.
                if (property != null && property.GetGetMethod(true) != null) return property.GetValue(target, null);
            }
            return null;
        }
        internal static object Call(object instance, string name, Type[] types, params object[] args)
        {
            if (instance == null) throw new InvalidOperationException("KKPE target is unavailable.");
            MethodInfo method = instance.GetType().GetMethod(name, Flags, null, types, null);
            if (method == null) throw new MissingMethodException(instance.GetType().FullName, name);
            return method.Invoke(instance, args);
        }
    }

    internal sealed class ExpressionTarget
    {
        internal object Pose, Bones, Blends;
        internal Component Component;
        internal Studio.OCIChar Character;
        internal string Name;
    }

    internal sealed class ExpressionRenderer
    {
        internal object Wrapper;
        internal SkinnedMeshRenderer Renderer;
        internal string Path, Role;
    }

    internal static class ExpressionRuntime
    {
        internal static readonly string[] BoneNames = { "cf_J_hitomi_tx_L", "cf_J_hitomi_tx_R", "cf_j_tang_01", "cf_j_tang_03", "cf_j_tang_04", "cf_j_tang_05" };
        internal static ExpressionTarget Current()
        {
            Type main = ExpressionReflect.Type("HSPE.MainWindow");
            object window = ExpressionReflect.Get(main, "_self"); object pose = ExpressionReflect.Get(window, "_poseTarget");
            Component component = pose as Component;
            object generic = ExpressionReflect.Get(pose, "_target");
            Studio.OCIChar character = ExpressionReflect.Get(generic, "ociChar") as Studio.OCIChar;
            if (component == null || character == null || Studio.Studio.Instance == null) return null;
            // Avoid applying to a stale KKPE target after the Studio selection has changed.
            Studio.TreeNodeObject[] selection = Studio.Studio.Instance.treeNodeCtrl.selectNodes;
            bool selected = false; foreach (Studio.TreeNodeObject node in selection) if (node == character.treeNodeObject) selected = true;
            if (!selected) return null;
            ExpressionTarget target = new ExpressionTarget(); target.Pose = pose; target.Component = component; target.Character = character;
            target.Bones = ExpressionReflect.Get(pose, "_bonesEditor"); target.Blends = ExpressionReflect.Get(pose, "_blendShapesEditor");
            target.Name = Convert.ToString(ExpressionReflect.Get(character.treeNodeObject, "textName"));
            if (String.IsNullOrEmpty(target.Name)) target.Name = component.name;
            return target.Bones == null || target.Blends == null ? null : target;
        }

        internal static string RelativePath(Transform item, Transform root)
        {
            if (item == root) return "";
            List<string> parts = new List<string>(); Transform at = item;
            while (at != null && at != root) { parts.Add(at.name); at = at.parent; }
            if (at != root) return null;
            parts.Reverse(); return String.Join("/", parts.ToArray());
        }

        internal static string Role(object editor, SkinnedMeshRenderer renderer)
        {
            IDictionary aliases = ExpressionReflect.Get(editor.GetType(), "_skinnedMeshAliases") as IDictionary;
            if (aliases != null && aliases.Contains(renderer.name)) return Convert.ToString(aliases[renderer.name]);
            return "";
        }

        internal static List<ExpressionRenderer> Renderers(ExpressionTarget target)
        {
            List<ExpressionRenderer> result = new List<ExpressionRenderer>();
            IDictionary renderers = ExpressionReflect.Get(target.Blends, "_blendRenderers") as IDictionary;
            if (renderers == null) throw new InvalidOperationException("KKPE Blend Shape renderer list is unavailable.");
            foreach (DictionaryEntry entry in renderers)
            {
                SkinnedMeshRenderer renderer = ExpressionReflect.Get(entry.Value, "_renderer") as SkinnedMeshRenderer;
                if (renderer == null || renderer.sharedMesh == null) continue;
                string path = RelativePath(renderer.transform, target.Component.transform); if (path == null) continue;
                ExpressionRenderer item = new ExpressionRenderer(); item.Wrapper = entry.Value; item.Renderer = renderer; item.Path = path; item.Role = Role(target.Blends, renderer);
                result.Add(item);
            }
            return result;
        }

        internal static Transform FindBone(ExpressionTarget target, string name, string path)
        {
            Transform[] transforms = target.Component.GetComponentsInChildren<Transform>(true);
            // Use the same ownership boundary as KKPE, excluding independently parented Studio objects.
            List<Transform> candidates = new List<Transform>();
            foreach (Transform transform in transforms)
            {
                if (!String.Equals(transform.name, name, StringComparison.OrdinalIgnoreCase)) continue;
                if (!System.Object.Equals(ExpressionReflect.Call(target.Bones, "ValidBoneTarget", new Type[] { typeof(Transform) }, transform), true)) continue;
                if (!String.IsNullOrEmpty(path) && RelativePath(transform, target.Component.transform) == path) return transform;
                candidates.Add(transform);
            }
            if (candidates.Count == 1) return candidates[0];
            if (candidates.Count == 0) return null;
            Transform activeCandidate = null; int activeCount = 0;
            foreach (Transform candidate in candidates) if (candidate.gameObject.activeInHierarchy) { activeCandidate = candidate; activeCount++; }
            if (activeCount == 1) return activeCandidate;
            // Koikatsu can keep a second tongue skeleton in an inactive mesh. Prefer a uniquely
            // weighted bone of the live tongue mesh, never the first name match in the hierarchy.
            List<Transform> weighted = new List<Transform>();
            foreach (SkinnedMeshRenderer renderer in target.Component.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (!renderer.enabled || !renderer.gameObject.activeInHierarchy || Role(target.Blends, renderer) != "Tongue") continue;
                foreach (Transform bone in renderer.bones) if (bone != null) foreach (Transform candidate in candidates)
                    if (RelativePath(bone, candidate) != null && !weighted.Contains(candidate)) weighted.Add(candidate);
            }
            if (weighted.Count == 1) return weighted[0];
            object characterInfo = ExpressionReflect.Get(target.Character, "charInfo");
            GameObject nativeHead = ExpressionReflect.Get(characterInfo, "objHeadBone") as GameObject;
            Transform headCandidate = null; int headCount = 0;
            if (nativeHead != null) foreach (Transform candidate in candidates)
            {
                if (RelativePath(candidate, nativeHead.transform) != null) { headCandidate = candidate; headCount++; }
            }
            return headCount == 1 ? headCandidate : null;
        }

        internal static object BoneValue(ExpressionTarget target, Transform bone, string member)
        {
            // Native KKPE FK controls take precedence over old manual rotation edits.
            if (member == "rotation") { Studio.OCIChar.BoneInfo active = ActiveFK(target, bone); if (active != null) return Quaternion.Euler(active.guideObject.changeAmount.rot); }
            IDictionary dirty = ExpressionReflect.Get(target.Bones, "_dirtyBones") as IDictionary;
            if (dirty != null && dirty.Contains(bone.gameObject))
            {
                object editable = ExpressionReflect.Get(dirty[bone.gameObject], member);
                if (editable != null && System.Object.Equals(ExpressionReflect.Get(editable, "hasValue"), true)) return ExpressionReflect.Get(editable, "value");
            }
            if (member == "position") return bone.localPosition;
            if (member == "scale") return bone.localScale;
            return bone.localRotation;
        }

        internal static Studio.OCIChar.BoneInfo ActiveFK(ExpressionTarget target, Transform bone)
        {
            object generic = ExpressionReflect.Get(target.Bones, "_target");
            if (!System.Object.Equals(ExpressionReflect.Get(generic, "fkEnabled"), true)) return null;
            Studio.OCIChar.BoneInfo result = FindFK(generic, bone.gameObject);
            return result != null && result.active ? result : null;
        }

        internal static Studio.OCIChar.BoneInfo FindFK(object generic, GameObject bone)
        {
            if (generic == null || bone == null) return null;
            // Newer KKPE discovers FK nodes added after its target was constructed. Use the same
            // native lookup as SetBoneRotation so these nodes are not mistaken for manual edits.
            MethodInfo lookup = generic.GetType().GetMethod("GetBoneInfo", ExpressionReflect.Flags, null, new Type[] { typeof(GameObject) }, null);
            if (lookup != null) return lookup.Invoke(generic, new object[] { bone }) as Studio.OCIChar.BoneInfo;
            // KKPE 2.18.1 has only the original cached dictionary.
            IDictionary fk = ExpressionReflect.Get(generic, "fkObjects") as IDictionary;
            return fk != null && fk.Contains(bone) ? fk[bone] as Studio.OCIChar.BoneInfo : null;
        }

        internal static float ShapeValue(ExpressionRenderer renderer, int index)
        {
            string name = renderer.Renderer.sharedMesh.GetBlendShapeName(index);
            IDictionary dirty = ExpressionReflect.Get(renderer.Wrapper, "_dirtyBlends") as IDictionary;
            if (dirty != null && dirty.Contains(name)) return Convert.ToSingle(ExpressionReflect.Get(dirty[name], "weight"));
            return renderer.Renderer.GetBlendShapeWeight(index);
        }

        internal static ExpressionPreset Capture(ExpressionTarget target, string name, string group, out string report)
        {
            if (target == null) throw new InvalidOperationException("Select a character and open KKPE Advanced mode.");
            ExpressionPreset preset = new ExpressionPreset(); preset.Name = name; preset.GroupId = group; preset.CharacterName = target.Name;
            List<string> missing = new List<string>(); List<ExpressionRenderer> renderers = Renderers(target);
            bool eyes = false, teeth = false, brows = false, extra = false;
            foreach (ExpressionRenderer item in renderers)
            {
                bool entire = item.Role == "Eyes / Mouth" || item.Role == "Teeth" || item.Role == "Eyebrows";
                if (item.Role == "Eyes / Mouth") eyes = true; if (item.Role == "Teeth") teeth = true; if (item.Role == "Eyebrows") brows = true;
                for (int index = 0; index < item.Renderer.sharedMesh.blendShapeCount; index++)
                {
                    string shape = item.Renderer.sharedMesh.GetBlendShapeName(index);
                    bool requested = shape == "kuti_ha.ha00_name_op"; if (requested) extra = true;
                    if (!entire && !requested) continue;
                    ExpressionShape entry = new ExpressionShape(); entry.RendererPath = item.Path; entry.RendererName = item.Renderer.name; entry.Role = item.Role; entry.Name = shape; entry.Weight = ShapeValue(item, index);
                    preset.Shapes.Add(entry);
                }
            }
            if (!eyes) missing.Add("Eyes / Mouth"); if (!teeth) missing.Add("Teeth"); if (!brows) missing.Add("Eyebrows"); if (!extra) missing.Add("kuti_ha.ha00_name_op");
            foreach (string requestedName in BoneNames)
            {
                Transform bone = FindBone(target, requestedName, null); if (bone == null) { missing.Add(requestedName); continue; }
                ExpressionBone entry = new ExpressionBone(); entry.Name = bone.name; entry.Path = RelativePath(bone, target.Component.transform); entry.Components = ExpressionStorage.AllowedComponents(requestedName);
                if ((entry.Components & 1) != 0) { Vector3 v = (Vector3)BoneValue(target, bone, "position"); entry.Position = new ExpressionVector(v.x, v.y, v.z); }
                if ((entry.Components & 2) != 0) { Quaternion q = (Quaternion)BoneValue(target, bone, "rotation"); entry.Rotation.X = q.x; entry.Rotation.Y = q.y; entry.Rotation.Z = q.z; entry.Rotation.W = q.w; }
                if ((entry.Components & 4) != 0) { Vector3 v = (Vector3)BoneValue(target, bone, "scale"); entry.Scale = new ExpressionVector(v.x, v.y, v.z); }
                preset.Bones.Add(entry);
            }
            if (preset.Shapes.Count == 0 && preset.Bones.Count == 0) throw new InvalidOperationException("No requested expression components exist on this character.");
            ExpressionStorage.Validate(preset);
            report = preset.Shapes.Count + " Blend Shapes / " + preset.Bones.Count + " Bones" + (missing.Count == 0 ? "" : " · Missing: " + String.Join(", ", missing.ToArray()));
            return preset;
        }

        internal static ExpressionRenderer ResolveRenderer(List<ExpressionRenderer> renderers, ExpressionShape shape)
        {
            ExpressionRenderer fallback = null; int count = 0;
            foreach (ExpressionRenderer item in renderers)
            {
                bool allowed = shape.Name == "kuti_ha.ha00_name_op" || (item.Role == shape.Role && (item.Role == "Eyes / Mouth" || item.Role == "Teeth" || item.Role == "Eyebrows"));
                if (!allowed || item.Renderer.name != shape.RendererName) continue;
                if (item.Path == shape.RendererPath) return item;
                fallback = item; count++;
            }
            return count == 1 ? fallback : null;
        }

        internal static int ShapeIndex(Mesh mesh, string name)
        {
            for (int i = 0; i < mesh.blendShapeCount; i++) if (mesh.GetBlendShapeName(i) == name) return i;
            return -1;
        }

        internal static string Apply(ExpressionTarget target, ExpressionPreset preset)
        {
            if (target == null) throw new InvalidOperationException("Select a character and open KKPE Advanced mode.");
            ExpressionStorage.Validate(preset);
            List<ExpressionRenderer> renderers = Renderers(target); List<string> missing = new List<string>(); int shapes = 0, bones = 0;
            List<ExpressionRenderer> changed = new List<ExpressionRenderer>();
            foreach (ExpressionShape entry in preset.Shapes)
            {
                ExpressionRenderer item = ResolveRenderer(renderers, entry);
                if (item == null || ShapeIndex(item.Renderer.sharedMesh, entry.Name) < 0) { missing.Add(entry.RendererName + "/" + entry.Name); continue; }
                // KKPE's string overload records original/dirty values and handles replacement meshes by name.
                object applied = ExpressionReflect.Call(item.Wrapper, "SetBlendShapeWeight", new Type[] { typeof(string), typeof(float) }, entry.Name, entry.Weight);
                if (applied == null) { missing.Add(entry.Name); continue; }
                shapes++; if (!changed.Contains(item)) changed.Add(item);
            }
            foreach (ExpressionRenderer item in changed) ExpressionReflect.Call(item.Wrapper, "ApplyDirty", Type.EmptyTypes);
            foreach (ExpressionBone entry in preset.Bones)
            {
                Transform bone = FindBone(target, entry.Name, entry.Path); if (bone == null) { missing.Add(entry.Name); continue; }
                int mask = entry.Components & ExpressionStorage.AllowedComponents(entry.Name);
                if ((mask & 1) != 0) ExpressionReflect.Call(target.Bones, "SetBonePosition", new Type[] { typeof(Transform), typeof(Vector3) }, bone, new Vector3(entry.Position.X, entry.Position.Y, entry.Position.Z));
                if ((mask & 2) != 0)
                {
                    Quaternion rotation = new Quaternion(entry.Rotation.X, entry.Rotation.Y, entry.Rotation.Z, entry.Rotation.W);
                    Studio.OCIChar.BoneInfo fk = ActiveFK(target, bone);
                    // The native KKPE setter deliberately skips active FK bones. Use the native persisted FK amount.
                    if (fk != null) fk.guideObject.changeAmount.rot = rotation.eulerAngles;
                    else ExpressionReflect.Call(target.Bones, "SetBoneRotation", new Type[] { typeof(Transform), typeof(Quaternion) }, bone, rotation);
                }
                if ((mask & 4) != 0) ExpressionReflect.Call(target.Bones, "SetBoneScale", new Type[] { typeof(Transform), typeof(Vector3) }, bone, new Vector3(entry.Scale.X, entry.Scale.Y, entry.Scale.Z));
                bones++;
            }
            ExpressionReflect.Call(target.Bones, "ApplyBoneManualCorrection", Type.EmptyTypes);
            string result = target.Name + ": " + shapes + " Blend Shapes / " + bones + " Bones applied";
            if (missing.Count > 0) result += " · Skipped " + missing.Count + ": " + String.Join(", ", missing.ToArray());
            if (!System.Object.Equals(ExpressionReflect.Get(target.Bones, "isEnabled"), true)) result += " · Bones module is disabled; enable it in KKPE to show its saved values.";
            return result;
        }

        internal static byte[] CaptureThumbnail()
        {
            Studio.Studio studio = Studio.Studio.Instance;
            Camera camera = studio != null && studio.cameraCtrl != null ? studio.cameraCtrl.mainCmaera : null;
            if (camera == null) throw new InvalidOperationException("Studio viewport camera is unavailable.");
            // Render at the existing viewport aspect, then read its center. The Studio UI is not part of this camera.
            int height = 256; int width = Mathf.Clamp(Mathf.RoundToInt(height * camera.aspect), 128, 1024);
            RenderTexture previousTarget = camera.targetTexture, previousActive = RenderTexture.active;
            RenderTexture temporary = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);
            Texture2D image = null;
            try
            {
                camera.targetTexture = temporary; camera.Render(); RenderTexture.active = temporary;
                int size = Math.Min(width, height); image = new Texture2D(size, size, TextureFormat.RGB24, false);
                image.ReadPixels(new Rect((width - size) / 2, (height - size) / 2, size, size), 0, 0); image.Apply();
                return image.EncodeToPNG();
            }
            finally
            {
                camera.targetTexture = previousTarget; RenderTexture.active = previousActive;
                RenderTexture.ReleaseTemporary(temporary); if (image != null) UnityEngine.Object.Destroy(image);
            }
        }
    }
}

