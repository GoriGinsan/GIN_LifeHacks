using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Xml;
using UnityEngine;

namespace GinAdvanceHack
{
    public sealed partial class ExpressionLibrary
    {
        private sealed class DirtySnapshot
        {
            internal IDictionary Target;
            internal readonly List<DictionaryEntry> Entries = new List<DictionaryEntry>();
            internal DirtySnapshot(IDictionary target)
            {
                Target = target;
                if (target != null) foreach (DictionaryEntry entry in target)
                {
                    object clone = entry.Value == null ? null : typeof(System.Object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(entry.Value, null);
                    Entries.Add(new DictionaryEntry(entry.Key, clone));
                }
            }
            internal void Restore()
            {
                if (Target == null) return; Target.Clear(); foreach (DictionaryEntry entry in Entries) Target.Add(entry.Key, entry.Value);
            }
        }
        private static void Check(bool condition, string name) { if (!condition) throw new InvalidOperationException(name); }
        private static bool Near(float a, float b) { return Math.Abs(a - b) < 0.0001f; }
        private static bool Same(Vector3 a, Vector3 b) { return (a-b).sqrMagnitude < 0.00000001f; }
        private static bool IsIsolated()
        {
            string game = Path.GetDirectoryName(Application.dataPath);
            return String.Equals(Path.GetFileName(game), "test-game", StringComparison.OrdinalIgnoreCase) && File.Exists(Path.Combine(game, ".kk-workshop-test"));
        }

        private static string VerifyFKLookup(ExpressionTarget target)
        {
            object generic = ExpressionReflect.Get(target.Bones, "_target");
            IDictionary cache = ExpressionReflect.Get(generic, "fkObjects") as IDictionary;
            Check(cache != null, "KKPE FK lookup cache is unavailable.");
            Studio.OCIChar.BoneInfo candidate = null;
            foreach (Studio.OCIChar.BoneInfo bone in target.Character.listBones)
                if (bone.guideObject != null && bone.guideObject.transformTarget != null) { candidate = bone; break; }
            Check(candidate != null, "The vanilla fixture has no native FK bone to verify.");
            GameObject boneObject = candidate.guideObject.transformTarget.gameObject;
            Check(System.Object.ReferenceEquals(ExpressionRuntime.FindFK(generic, boneObject), candidate), "Expression FK lookup does not resolve the native bone.");
            MethodInfo dynamicLookup = generic.GetType().GetMethod("GetBoneInfo", ExpressionReflect.Flags, null, new Type[] { typeof(GameObject) }, null);
            if (dynamicLookup == null) return "PASS legacy KKPE FK dictionary lookup";
            // Simulate a bone registered after the cached target was built. Restore the exact cache entry.
            bool existed = cache.Contains(boneObject); object original = existed ? cache[boneObject] : null;
            try
            {
                cache.Remove(boneObject);
                Check(System.Object.ReferenceEquals(ExpressionRuntime.FindFK(generic, boneObject), candidate), "New KKPE uncached FK bone lookup failed.");
            }
            finally { if (existed) cache[boneObject] = original; else cache.Remove(boneObject); }
            return "PASS native KKPE GetBoneInfo lookup for an uncached FK bone / cache restored";
        }

        public string PrepareIsolatedPreview()
        {
            if (!IsIsolated()) return "SKIP expression preview: not the marked test game";
            ExpressionTarget target = ExpressionRuntime.Current(); if (target == null) return "FAIL expression preview: no character selected";
            byte[] png = ExpressionRuntime.CaptureThumbnail();
            ExpressionGroups previewGroups = new ExpressionGroups(); List<ExpressionPreset> preview = new List<ExpressionPreset>();
            for (int n = 0; n < 2; n++)
            {
                ExpressionGroup group = new ExpressionGroup(); group.Name = n == 0 ? "Preview A / 検証A" : "Preview B / 検証B"; previewGroups.Items.Add(group);
                string report; ExpressionPreset preset = ExpressionRuntime.Capture(target, "Current expression " + (n + 1), group.Id, out report);
                Texture2D image = new Texture2D(2, 2, TextureFormat.RGB24, false); image.LoadImage(png); images[preset.Id] = image; preview.Add(preset);
            }
            groups = previewGroups; presets.Clear(); presets.AddRange(preview); saveGroup = previewGroups.Items[0].Id;
            Status("Verification preview: two in-memory copies of the actual current expression and viewport capture. No preset files written.");
            Show();
            return "PASS expression preview: two real captured thumbnails in independent columns; memory only";
        }

        // The verifier invokes this only on its generated vanilla character in the marked test copy.
        public string RunIsolatedTests()
        {
            if (!IsIsolated()) return "SKIP expression tests: not the marked isolated test game";
            StringBuilder result = new StringBuilder(); ExpressionTarget target = ExpressionRuntime.Current();
            if (target == null) return "FAIL expression tests: no selected character / KKPE target";
            string temporary = Path.Combine(root, "test-" + Guid.NewGuid().ToString("N"));
            List<string> files = new List<string>(); List<DirtySnapshot> snapshots = new List<DirtySnapshot>(); ExpressionPreset original = null;
            try
            {
                Directory.CreateDirectory(temporary);
                snapshots.Add(new DirtySnapshot(ExpressionReflect.Get(target.Bones, "_dirtyBones") as IDictionary));
                foreach (ExpressionRenderer renderer in ExpressionRuntime.Renderers(target)) snapshots.Add(new DirtySnapshot(ExpressionReflect.Get(renderer.Wrapper, "_dirtyBlends") as IDictionary));
                result.AppendLine(VerifyFKLookup(target));
                string captureReport; original = ExpressionRuntime.Capture(target, "検証 / Smile: test", "default", out captureReport);
                result.AppendLine("PASS expression capture: " + captureReport);
                Check(original.Shapes.Count > 0, "No requested Blend Shapes were captured.");
                Check(original.Bones.Count == 6, "The vanilla fixture must resolve all six requested bones without selecting its inactive duplicate tongue skeleton.");
                bool teethExtra = original.Shapes.Exists(delegate(ExpressionShape s) { return s.Name == "kuti_ha.ha00_name_op"; });
                result.AppendLine((teethExtra ? "PASS" : "SKIP") + " stable named morph kuti_ha.ha00_name_op (never hard-coded index 29)");
                string file = ExpressionStorage.PresetPath(temporary, original.Id); files.Add(file); ExpressionStorage.Write(file, original);
                ExpressionPreset modified = ExpressionStorage.Read<ExpressionPreset>(file); ExpressionStorage.Validate(modified);
                Check(modified.Name == original.Name && modified.Shapes.Count == original.Shapes.Count && modified.Bones.Count == original.Bones.Count, "Expression XML roundtrip mismatch.");
                result.AppendLine("PASS expression Unicode name / XML roundtrip / GUID filename");
                bool rejected = false; try { ExpressionStorage.PresetPath(temporary, "../../bad"); } catch (IOException) { rejected = true; }
                Check(rejected, "Expression path traversal accepted.");
                ExpressionPreset forbidden = new ExpressionPreset(); ExpressionBone forbiddenBone = new ExpressionBone(); forbiddenBone.Name = "cf_J_hitomi_tx_L"; forbiddenBone.Components = 2; forbidden.Bones.Add(forbiddenBone);
                rejected = false; try { ExpressionStorage.Validate(forbidden); } catch (IOException) { rejected = true; }
                Check(rejected, "Unrequested eye bone rotation accepted.");
                result.AppendLine("PASS expression path identity / forbidden component mask rejection");
                Dictionary<Transform, Quaternion> unchangedRotations = new Dictionary<Transform, Quaternion>();
                Dictionary<Transform, Vector3> unchangedPositions = new Dictionary<Transform, Vector3>();
                foreach (ExpressionBone bone in modified.Bones)
                {
                    Transform transform = ExpressionRuntime.FindBone(target, bone.Name, bone.Path);
                    if ((bone.Components & 2) == 0) unchangedRotations[transform] = (Quaternion)ExpressionRuntime.BoneValue(target, transform, "rotation");
                    if ((bone.Components & 1) == 0) unchangedPositions[transform] = (Vector3)ExpressionRuntime.BoneValue(target, transform, "position");
                    if ((bone.Components & 1) != 0) bone.Position.X += 0.013f;
                    if ((bone.Components & 4) != 0) bone.Scale.Y += 0.027f;
                    if ((bone.Components & 2) != 0) { Quaternion q = Quaternion.Euler(7, 3, 11); bone.Rotation.X = q.x; bone.Rotation.Y = q.y; bone.Rotation.Z = q.z; bone.Rotation.W = q.w; }
                }
                for (int i = 0; i < modified.Shapes.Count; i++) modified.Shapes[i].Weight = 17 + i % 7;
                string applied = ExpressionRuntime.Apply(target, modified);
                foreach (ExpressionBone bone in modified.Bones)
                {
                    Transform transform = ExpressionRuntime.FindBone(target, bone.Name, bone.Path);
                    if ((bone.Components & 1) != 0) Check(Same((Vector3)ExpressionRuntime.BoneValue(target, transform, "position"), new Vector3(bone.Position.X, bone.Position.Y, bone.Position.Z)), "Bone position was not recorded in KKPE.");
                    if ((bone.Components & 4) != 0) Check(Same((Vector3)ExpressionRuntime.BoneValue(target, transform, "scale"), new Vector3(bone.Scale.X, bone.Scale.Y, bone.Scale.Z)), "Bone scale was not recorded in KKPE.");
                    if ((bone.Components & 2) != 0) Check(Quaternion.Angle((Quaternion)ExpressionRuntime.BoneValue(target, transform, "rotation"), new Quaternion(bone.Rotation.X, bone.Rotation.Y, bone.Rotation.Z, bone.Rotation.W)) < 0.05f, "Bone rotation was not recorded in KKPE.");
                }
                foreach (KeyValuePair<Transform, Quaternion> pair in unchangedRotations) Check(Quaternion.Angle(pair.Value, (Quaternion)ExpressionRuntime.BoneValue(target, pair.Key, "rotation")) < 0.01f, "Unrequested eye bone rotation changed.");
                foreach (KeyValuePair<Transform, Vector3> pair in unchangedPositions) Check(Same(pair.Value, (Vector3)ExpressionRuntime.BoneValue(target, pair.Key, "position")), "Unrequested tongue position changed.");
                Check(unchangedRotations.Count == 2 && unchangedPositions.Count == 3, "Component mask coverage did not include both eyes and tongue 03/04/05.");
                List<ExpressionRenderer> renderers = ExpressionRuntime.Renderers(target);
                foreach (ExpressionShape shape in modified.Shapes)
                {
                    ExpressionRenderer renderer = ExpressionRuntime.ResolveRenderer(renderers, shape);
                    Check(renderer != null && Near(ExpressionRuntime.ShapeValue(renderer, ExpressionRuntime.ShapeIndex(renderer.Renderer.sharedMesh, shape.Name)), shape.Weight), "Blend Shape value was not recorded in KKPE.");
                }
                result.AppendLine("PASS native KKPE expression apply: " + applied);
                result.AppendLine("PASS unrequested eye Rotation / tongue 03-05 Position unchanged");
                StringBuilder persisted = new StringBuilder();
                using (StringWriter text = new StringWriter(persisted)) using (XmlTextWriter xml = new XmlTextWriter(text))
                {
                    xml.WriteStartElement("expressionPersistence");
                    ExpressionReflect.Call(target.Bones, "SaveXml", new Type[] { typeof(XmlTextWriter) }, xml);
                    ExpressionReflect.Call(target.Blends, "SaveXml", new Type[] { typeof(XmlTextWriter) }, xml);
                    xml.WriteEndElement(); xml.Flush();
                }
                XmlDocument sceneXml = new XmlDocument(); sceneXml.XmlResolver = null; sceneXml.LoadXml(persisted.ToString());
                // KKPE's existing scene schema writes mesh path + current mesh index. The new library's
                // files use stable names; confirm the native schema contains the dynamically resolved index.
                foreach (ExpressionShape shape in modified.Shapes)
                {
                    ExpressionRenderer renderer = ExpressionRuntime.ResolveRenderer(renderers, shape);
                    int actualIndex = ExpressionRuntime.ShapeIndex(renderer.Renderer.sharedMesh, shape.Name); bool found = false;
                    foreach (XmlNode meshNode in sceneXml.SelectNodes("/expressionPersistence/skinnedMeshes/skinnedMesh"))
                    {
                        if (meshNode.Attributes["name"].Value != shape.RendererPath) continue;
                        foreach (XmlNode shapeNode in meshNode.SelectNodes("blendShape"))
                            if (XmlConvert.ToInt32(shapeNode.Attributes["index"].Value) == actualIndex && Near(XmlConvert.ToSingle(shapeNode.Attributes["weight"].Value), shape.Weight)) found = true;
                    }
                    Check(found, "KKPE native SaveXml omitted expression morph " + shape.Name);
                }
                foreach (ExpressionBone bone in modified.Bones)
                {
                    XmlNode found = null; foreach (XmlNode node in sceneXml.SelectNodes("/expressionPersistence/advancedObjects/object")) if (node.Attributes["name"].Value == bone.Path) found = node;
                    Check(found != null, "KKPE native SaveXml omitted bone " + bone.Name);
                    if ((bone.Components & 1) != 0) Check(found.Attributes["posX"] != null && Near(XmlConvert.ToSingle(found.Attributes["posX"].Value), bone.Position.X), "KKPE native SaveXml omitted bone position.");
                    if ((bone.Components & 4) != 0) Check(found.Attributes["scaleY"] != null && Near(XmlConvert.ToSingle(found.Attributes["scaleY"].Value), bone.Scale.Y), "KKPE native SaveXml omitted bone scale.");
                }
                result.AppendLine("PASS KKPE native scene XML contains all expression morph weights at their actual mesh indices and bone edits");
                // Missing properties are skipped by exact name; no index fallback or unrelated transform editing.
                modified.Shapes[0].Name = "GIN_missing_shape_test";
                string missingResult = ExpressionRuntime.Apply(target, modified); Check(missingResult.IndexOf("Skipped", StringComparison.Ordinal) >= 0, "Missing Blend Shape was not reported.");
                result.AppendLine("PASS missing named Blend Shape is reported and skipped");
                ExpressionRuntime.Apply(target, original);
                if (SystemInfo.graphicsDeviceVersion.IndexOf("NULL", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    Camera camera = Studio.Studio.Instance.cameraCtrl.mainCmaera; RenderTexture beforeTarget = camera.targetTexture, beforeActive = RenderTexture.active;
                    byte[] png = ExpressionRuntime.CaptureThumbnail(); Check(png.Length > 64 && png[0] == 137 && png[1] == 80 && png[2] == 78 && png[3] == 71, "Expression camera capture did not return PNG.");
                    Check(camera.targetTexture == beforeTarget && RenderTexture.active == beforeActive, "Thumbnail capture failed to restore camera render state.");
                    string pngFile = ExpressionStorage.ImagePath(temporary, original.Id); files.Add(pngFile); File.WriteAllBytes(pngFile, png);
                    result.AppendLine("PASS center viewport PNG thumbnail / camera target and active render texture restored");
                }
                else result.AppendLine("SKIP thumbnail render: null graphics device");
            }
            catch (Exception error) { result.AppendLine("FAIL expression tests: " + Error(error)); }
            finally
            {
                try { if (original != null) ExpressionRuntime.Apply(target, original); } catch (Exception error) { result.AppendLine("FAIL expression value restore: " + Error(error)); }
                foreach (DirtySnapshot snapshot in snapshots) snapshot.Restore();
                foreach (string file in files) if (File.Exists(file)) File.Delete(file);
                if (Directory.Exists(temporary) && Directory.GetFileSystemEntries(temporary).Length == 0) Directory.Delete(temporary, false);
            }
            return result.ToString();
        }
    }
}
