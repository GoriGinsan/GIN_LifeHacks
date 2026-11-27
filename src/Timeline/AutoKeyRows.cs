using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Xml;
using BepInEx.Configuration;
using HarmonyLib;
using Studio;
using Timeline;
using UnityEngine;
using UnityEngine.UI;
using Native = Timeline.Timeline;

namespace GinTimelineHack
{
    // Auto Key's "Add missing rows" (the check box at the bottom of the Auto Key menu).
    //
    // With it on, an edit of something that has no Timeline row yet adds the row and keys it at
    // the playhead, as Auto Key keys a row that exists. Rows are added the way Timeline's own
    // scene loader builds them, at the top level of the row list. Four sources:
    //
    //   guide objects   every guide of the watched objects: a character's root, IK targets and FK
    //                   nodes, an item's root and FK nodes. Compared as plain values, no Timeline
    //                   call, so the many FK nodes of a character cost almost nothing.
    //   KKPE            the bone and Blend Shape overrides KKPE holds for the watched objects
    //                   (also what GIN Pad edits): a bone gets bonePos / boneRot / boneScale, a
    //                   mesh one groupBlendShape row (all its shapes in one row).
    //   Material Editor its Studio window's edits of float, colour, texture offset / scale and
    //                   keyword properties, caught where the window applies them; the row is the
    //                   one Material Editor's own Timeline button would add.
    //   everything else every other row type Timeline offers for the watched object (item colours,
    //                   lights, clothing states, eyes, ...), asked from the row type itself.
    //
    // Never added (nor keyed unless the AutoKey / Rows list names them): Camera Zoom, Camera Original
    // Position / Rotation, Camera Position / Rotation, Animation Time, Animation Speed, Animation.
    //
    // Watched objects: the objects selected in Studio's work list (the first MaxWatched) and the
    // object GIN Pad edits.
    //
    // Keeping it light:
    //   * comparisons run 20 times a second during input and 10 times without, never every
    //     frame; between them only the rows waiting for their key are looked at;
    //   * the list of "everything else" is rebuilt only on a mouse press, when the selection
    //     changes, and every half second without input;
    //   * a row type whose value changes without any input (an animated value) or reads a new
    //     object on every call is marked as drifting and no longer watched, so it can neither
    //     add rows nor cost time.
    public sealed partial class Plugin
    {
        const int MaxWatched = 8;
        const float AutoKeyActiveInterval = 0.05f;   // seconds between full checks during input
        const float AutoRowsRebuildInterval = 0.5f;   // seconds between candidate rebuilds without input
        const float AddRowsGap = 4f;                  // space above the check box in the menu
        static readonly string[] GuideIds = { "guideObjectPos", "guideObjectRot", "guideObjectScale" };
        static readonly string[] BoneComponents = { "position", "rotation", "scale" };
        static readonly string[] BoneIds = { "bonePos", "boneRot", "boneScale" };

        ConfigEntry<bool> autoKeyAddRowsSetting;

        // A watched guide object: its values as last seen, and the rows it would get (built once).
        sealed class GuideWatch
        {
            internal ObjectCtrlInfo Oci;
            internal Vector3 Pos, Rot, Scale;
            internal readonly Interpolable[] Rows = new Interpolable[3];
        }

        // A row of "everything else" or of KKPE that does not exist yet, with its last signature.
        sealed class RowCandidate
        {
            internal Interpolable Row;
            internal object Signature;
            internal int Built;   // rebuild in which it was last offered
        }

        // A Material Editor edit waiting to become a row.
        sealed class MaterialEdit
        {
            internal object Ui, Data;
            internal GameObject Target;
            internal string Material, Property, RowType, Method;
        }

        readonly List<ObjectCtrlInfo> watched = new List<ObjectCtrlInfo>();
        readonly List<ObjectCtrlInfo> watchedNow = new List<ObjectCtrlInfo>();
        readonly Dictionary<GuideObject, GuideWatch> guideWatch = new Dictionary<GuideObject, GuideWatch>();
        readonly List<GuideObject> guideScratch = new List<GuideObject>();
        readonly Dictionary<int, RowCandidate> otherCandidates = new Dictionary<int, RowCandidate>();
        readonly Dictionary<long, RowCandidate> kkpeCandidates = new Dictionary<long, RowCandidate>();   // part's instance id * 4 + component (3: Blend Shapes)
        readonly HashSet<ObjectCtrlInfo> kkpeSeen = new HashSet<ObjectCtrlInfo>();
        readonly Dictionary<string, HashSet<string>> drifting = new Dictionary<string, HashSet<string>>();
        readonly List<MaterialEdit> materialEdits = new List<MaterialEdit>();
        readonly Dictionary<string, List<Interpolable>> materialRows = new Dictionary<string, List<Interpolable>>();   // rows made per edited property
        readonly List<int> candidateScratch = new List<int>();
        readonly List<long> kkpeScratch = new List<long>();
        readonly XmlDocument kkpeXml = new XmlDocument();
        bool autoRowsRebuildDue = true, autoRowsViewDirty;
        float autoRowsRebuildAt;
        int autoRowsBuild, autoRowsAdded;
        static Type poseControllerType;
        static bool poseControllerSearched;

        bool AutoKeyAddRows
        {
            get
            {
                return autoKeyAddRowsSetting != null && autoKeyAddRowsSetting.Value;
            }
        }

        void BindAutoKeyRowsSetting()
        {
            autoKeyAddRowsSetting = Config.Bind(
                "AutoKey",
                "AddMissingRows",
                false,
                "With Auto Key on, an edit of something that has no Timeline row yet adds the row and keys it (guide objects, KKPE bones and Blend Shapes, Material Editor properties and every other row type Timeline offers for the selected objects). Also the check box at the bottom of the Auto Key menu.");
            autoKeyAddRowsSetting.SettingChanged += delegate
            {
                ResetAutoRows();
                ShowAddRowsState();
            };
        }

        void ResetAutoRows()
        {
            watched.Clear();
            guideWatch.Clear();
            otherCandidates.Clear();
            kkpeCandidates.Clear();
            kkpeSeen.Clear();
            materialEdits.Clear();
            materialRows.Clear();
            neverAddedRows.Clear();
            autoRowsRebuildDue = true;
        }

        // ---- the check per poll ----------------------------------------------------------------

        // Called from PollAutoKey's full check, after the existing rows were compared.
        void PollMissingRows(bool userActive)
        {
            if (!AutoKeyAddRows)
            {
                materialEdits.Clear();
                return;
            }

            Dictionary<int, Interpolable> registered = Get<Dictionary<int, Interpolable>>(native, "_interpolables");
            bool selectionChanged = RefreshWatched();
            CompareGuides(registered, userActive);
            CompareKkpe(registered, userActive);
            // A rebuild keeps what it knew, so a change waits for the next comparison.
            if (selectionChanged || autoRowsRebuildDue || (!userActive && Time.unscaledTime >= autoRowsRebuildAt))
            {
                RebuildOtherCandidates(registered);
            }
            else
            {
                CompareOtherCandidates(registered, userActive);
            }

            AddMaterialEditorRows(registered);
            if (autoRowsViewDirty)
            {
                autoRowsViewDirty = false;
                Call("UpdateInterpolablesView");
                nextRefresh = 0;
            }
        }

        // The objects to watch now. True when the set changed: their values are then taken as
        // they are, without counting as an edit.
        bool RefreshWatched()
        {
            watchedNow.Clear();
            ObjectCtrlInfo[] selected = Studio.Studio.Instance == null || Studio.Studio.Instance.treeNodeCtrl == null ? null : Studio.Studio.Instance.treeNodeCtrl.selectObjectCtrl;
            if (selected != null)
            {
                foreach (ObjectCtrlInfo oci in selected)
                {
                    if (oci != null && watchedNow.Count < MaxWatched && !watchedNow.Contains(oci))
                    {
                        watchedNow.Add(oci);
                    }
                }
            }

            foreach (PadNote note in padNotes.Values)
            {
                if (note.Target != null && Time.unscaledTime - note.Time <= PadNoteSeconds && !watchedNow.Contains(note.Target))
                {
                    watchedNow.Add(note.Target);
                }
            }

            bool same = watchedNow.Count == watched.Count;
            for (int i = 0; same && i < watched.Count; i++)
            {
                same = watched[i] == watchedNow[i];
            }

            if (same)
            {
                return false;
            }

            watched.Clear();
            watched.AddRange(watchedNow);
            ForgetUnwatched();
            return true;
        }

        void ForgetUnwatched()
        {
            List<GuideObject> guides = new List<GuideObject>();
            foreach (KeyValuePair<GuideObject, GuideWatch> entry in guideWatch)
            {
                if (entry.Key == null || !watched.Contains(entry.Value.Oci))
                {
                    guides.Add(entry.Key);
                }
            }

            foreach (GuideObject guide in guides)
            {
                guideWatch.Remove(guide);
            }

            kkpeScratch.Clear();
            foreach (KeyValuePair<long, RowCandidate> entry in kkpeCandidates)
            {
                if (!watched.Contains(entry.Value.Row.oci))
                {
                    kkpeScratch.Add(entry.Key);
                }
            }

            foreach (long key in kkpeScratch)
            {
                kkpeCandidates.Remove(key);
            }

            kkpeSeen.RemoveWhere(delegate (ObjectCtrlInfo oci) { return !watched.Contains(oci); });
        }

        // ---- guide objects ---------------------------------------------------------------------

        void CompareGuides(Dictionary<int, Interpolable> registered, bool userActive)
        {
            foreach (ObjectCtrlInfo oci in watched)
            {
                guideScratch.Clear();
                GuidesOf(oci, guideScratch);
                foreach (GuideObject guide in guideScratch)
                {
                    if (guide == null)
                    {
                        continue;
                    }

                    ChangeAmount amount = guide.changeAmount;
                    GuideWatch watch;
                    if (!guideWatch.TryGetValue(guide, out watch))
                    {
                        watch = new GuideWatch();
                        watch.Oci = oci;
                        watch.Pos = amount.pos;
                        watch.Rot = amount.rot;
                        watch.Scale = amount.scale;
                        guideWatch[guide] = watch;
                        continue;
                    }

                    bool pos = watch.Pos != amount.pos, rot = watch.Rot != amount.rot, scale = watch.Scale != amount.scale;
                    if (!pos && !rot && !scale)
                    {
                        continue;
                    }

                    watch.Pos = amount.pos;
                    watch.Rot = amount.rot;
                    watch.Scale = amount.scale;
                    if (!userActive)
                    {
                        continue;
                    }

                    if (pos)
                    {
                        AddGuideRow(registered, guide, watch, 0);
                    }

                    if (rot)
                    {
                        AddGuideRow(registered, guide, watch, 1);
                    }

                    if (scale)
                    {
                        AddGuideRow(registered, guide, watch, 2);
                    }
                }
            }
        }

        void AddGuideRow(Dictionary<int, Interpolable> registered, GuideObject guide, GuideWatch watch, int component)
        {
            Interpolable row = watch.Rows[component];
            if (row == null)
            {
                InterpolableModel model = FindModel("Timeline", GuideIds[component]);
                if (model == null)
                {
                    return;
                }

                row = new Interpolable(watch.Oci, guide, model);
                watch.Rows[component] = row;
            }

            if (!registered.ContainsKey(row.GetHashCode()))
            {
                AddAutoRow(registered, row, EditSignature(row));
            }
        }

        // Every guide object of an object that a user can move: the object's own, and for a
        // character its IK targets and FK nodes, for an item its FK nodes.
        static void GuidesOf(ObjectCtrlInfo oci, List<GuideObject> into)
        {
            if (oci.guideObject != null)
            {
                into.Add(oci.guideObject);
            }

            OCIChar character = oci as OCIChar;
            if (character != null)
            {
                foreach (OCIChar.IKInfo ik in character.listIKTarget)
                {
                    into.Add(ik.guideObject);
                }

                foreach (OCIChar.BoneInfo bone in character.listBones)
                {
                    into.Add(bone.guideObject);
                }

                return;
            }

            OCIItem item = oci as OCIItem;
            if (item != null && item.listBones != null)
            {
                foreach (OCIChar.BoneInfo bone in item.listBones)
                {
                    into.Add(bone.guideObject);
                }
            }
        }

        // ---- KKPE overrides ---------------------------------------------------------------------

        void CompareKkpe(Dictionary<int, Interpolable> registered, bool userActive)
        {
            foreach (ObjectCtrlInfo oci in watched)
            {
                Component pose = PoseControllerOf(oci);
                if (pose == null)
                {
                    continue;
                }

                // First sight of the object: its overrides are taken as they are.
                bool first = kkpeSeen.Add(oci);
                Transform root = oci.guideObject.transformTarget;
                try
                {
                    CompareKkpeBones(registered, oci, pose, root, first, userActive);
                    CompareKkpeBlends(registered, oci, pose, root, first, userActive);
                }
                catch (Exception e)
                {
                    Logger.LogDebug("Auto Key rows, KKPE: " + Error(e));
                }
            }
        }

        void CompareKkpeBones(Dictionary<int, Interpolable> registered, ObjectCtrlInfo oci, Component pose, Transform root, bool first, bool userActive)
        {
            object editor = Get<object>(pose, "_bonesEditor");
            IDictionary overrides = editor == null ? null : Get<IDictionary>(editor, "_dirtyBones");
            if (overrides == null)
            {
                return;
            }

            foreach (DictionaryEntry entry in overrides)
            {
                GameObject bone = entry.Key as GameObject;
                if (bone == null || entry.Value == null)
                {
                    continue;
                }

                for (int component = 0; component < 3; component++)
                {
                    object editable = Get<object>(entry.Value, BoneComponents[component]);
                    if (editable == null || !Get<bool>(editable, "hasValue"))
                    {
                        continue;
                    }

                    object value = Get<object>(editable, "value");
                    long key = (long)bone.GetInstanceID() * 4 + component;
                    KkpeSeen(registered, key, value, first, userActive, oci, root, bone.transform, BoneIds[component]);
                }
            }
        }

        void CompareKkpeBlends(Dictionary<int, Interpolable> registered, ObjectCtrlInfo oci, Component pose, Transform root, bool first, bool userActive)
        {
            object editor = Get<object>(pose, "_blendShapesEditor");
            IDictionary renderers = editor == null ? null : Get<IDictionary>(editor, "_blendRenderers");
            if (renderers == null)
            {
                return;
            }

            foreach (DictionaryEntry entry in renderers)
            {
                if (entry.Value == null)
                {
                    continue;
                }

                IDictionary overrides = Get<IDictionary>(entry.Value, "_dirtyBlends");
                SkinnedMeshRenderer renderer = Get<SkinnedMeshRenderer>(entry.Value, "_renderer");
                if (overrides == null || overrides.Count == 0 || renderer == null)
                {
                    continue;
                }

                // One number that changes whenever any override weight of the renderer changes.
                double signature = overrides.Count;
                int position = 0;
                foreach (DictionaryEntry shape in overrides)
                {
                    signature += Get<float>(shape.Value, "weight") * (1 + position % 251);
                    position++;
                }

                long key = (long)renderer.GetInstanceID() * 4 + 3;
                KkpeSeen(registered, key, signature, first, userActive, oci, root, renderer.transform, "groupBlendShape");
            }
        }

        // An override seen now. A new one, or a changed one, under the user's hands adds its row.
        void KkpeSeen(Dictionary<int, Interpolable> registered, long key, object value, bool first, bool userActive, ObjectCtrlInfo oci, Transform root, Transform part, string id)
        {
            RowCandidate candidate;
            bool known = kkpeCandidates.TryGetValue(key, out candidate);
            if (known && SameValue(candidate.Signature, value))
            {
                return;
            }

            if (!known)
            {
                Interpolable row = KkpeRow(oci, root, part, id);
                if (row == null)
                {
                    return;
                }

                candidate = new RowCandidate();
                candidate.Row = row;
                kkpeCandidates[key] = candidate;
            }

            candidate.Signature = value;
            if (first || !userActive || registered.ContainsKey(candidate.Row.GetHashCode()))
            {
                return;
            }

            AddAutoRow(registered, candidate.Row, EditSignature(candidate.Row));
        }

        // A KKPE row built from the same XML Timeline saves it with: the bone's or the mesh's path
        // from the object's root.
        Interpolable KkpeRow(ObjectCtrlInfo oci, Transform root, Transform part, string id)
        {
            InterpolableModel model = FindModel("KKPE", id);
            if (model == null || model.readParameterFromXml == null)
            {
                return null;
            }

            XmlElement node = kkpeXml.CreateElement("interpolable");
            node.SetAttribute("owner", "KKPE");
            node.SetAttribute("id", id);
            node.SetAttribute("parameter", RelativePath(root, part));
            try
            {
                object parameter = model.readParameterFromXml(oci, node);
                return parameter == null ? null : new Interpolable(oci, parameter, model);
            }
            catch (Exception e)
            {
                Logger.LogDebug("Auto Key rows, KKPE " + id + " " + part.name + ": " + Error(e));
                return null;
            }
        }

        // "BodyTop/p_cf_body_bone/.../cf_j_head": the form Timeline and KKPE save.
        static string RelativePath(Transform root, Transform part)
        {
            if (part == root)
            {
                return "";
            }

            StringBuilder path = new StringBuilder(part.name);
            for (Transform parent = part.parent; parent != null && parent != root; parent = parent.parent)
            {
                path.Insert(0, "/");
                path.Insert(0, parent.name);
            }

            return path.ToString();
        }

        static Component PoseControllerOf(ObjectCtrlInfo oci)
        {
            if (!poseControllerSearched)
            {
                poseControllerSearched = true;
                poseControllerType = FindLoadedType("HSPE.PoseController");
            }

            Transform root = oci.guideObject == null ? null : oci.guideObject.transformTarget;
            return poseControllerType == null || root == null ? null : root.GetComponent(poseControllerType);
        }

        // InterpolableModel.GetParameter is not public: the parameter Timeline's list would use now.
        static MethodInfo getParameter;

        static object ParameterOf(InterpolableModel model, ObjectCtrlInfo oci)
        {
            if (getParameter == null)
            {
                getParameter = typeof(InterpolableModel).GetMethod("GetParameter", Flags, null, new Type[] { typeof(ObjectCtrlInfo) }, null);
            }

            return getParameter.Invoke(model, new object[] { oci });
        }

        static Type FindLoadedType(string fullName)
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type = assembly.GetType(fullName, false);
                if (type != null)
                {
                    return type;
                }
            }

            return null;
        }

        // ---- everything else ------------------------------------------------------------------

        // The rows Timeline would offer for the watched objects now, the way its list builds them.
        // Rows that exist are left to Auto Key itself. A known candidate keeps its signature.
        void RebuildOtherCandidates(Dictionary<int, Interpolable> registered)
        {
            autoRowsRebuildDue = false;
            autoRowsRebuildAt = Time.unscaledTime + AutoRowsRebuildInterval;
            autoRowsBuild++;
            List<InterpolableModel> models = Get<List<InterpolableModel>>(native, "_interpolableModelsList");
            foreach (ObjectCtrlInfo oci in watched)
            {
                foreach (InterpolableModel model in models)
                {
                    if (!WatchedElsewhere(model) && !IsDrifting(model.owner, model.id))
                    {
                        OfferCandidate(registered, oci, model);
                    }
                }
            }

            candidateScratch.Clear();
            foreach (KeyValuePair<int, RowCandidate> entry in otherCandidates)
            {
                if (entry.Value.Built != autoRowsBuild)
                {
                    candidateScratch.Add(entry.Key);
                }
            }

            foreach (int hash in candidateScratch)
            {
                otherCandidates.Remove(hash);
            }
        }

        void OfferCandidate(Dictionary<int, Interpolable> registered, ObjectCtrlInfo oci, InterpolableModel model)
        {
            Interpolable row;
            try
            {
                if (!model.IsCompatibleWithTarget(oci))
                {
                    return;
                }

                if (Get<object>(model, "_getParameter") == null)
                {
                    row = new Interpolable(oci, model);
                }
                else
                {
                    object parameter = ParameterOf(model, oci);
                    if (parameter == null)
                    {
                        return;
                    }

                    row = new Interpolable(oci, parameter, model);
                }
            }
            catch
            {
                return;
            }

            int hash = row.GetHashCode();
            if (registered.ContainsKey(hash))
            {
                return;
            }

            RowCandidate candidate;
            if (otherCandidates.TryGetValue(hash, out candidate))
            {
                candidate.Built = autoRowsBuild;
                return;
            }

            // A value that is a new object on every read can never look unchanged.
            object signature = EditSignature(row);
            if (signature == NotWatched || !SameValue(signature, EditSignature(row)))
            {
                MarkDrifting(model.owner, model.id);
                return;
            }

            candidate = new RowCandidate();
            candidate.Row = row;
            candidate.Signature = signature;
            candidate.Built = autoRowsBuild;
            otherCandidates[hash] = candidate;
        }

        void CompareOtherCandidates(Dictionary<int, Interpolable> registered, bool userActive)
        {
            candidateScratch.Clear();
            foreach (KeyValuePair<int, RowCandidate> entry in otherCandidates)
            {
                RowCandidate candidate = entry.Value;
                if (IsDrifting(candidate.Row.owner, candidate.Row.id))
                {
                    candidateScratch.Add(entry.Key);
                    continue;
                }

                object now = EditSignature(candidate.Row);
                if (SameValue(candidate.Signature, now))
                {
                    continue;
                }

                candidate.Signature = now;
                if (!userActive)
                {
                    // Changed with nobody touching anything: an animated value.
                    MarkDrifting(candidate.Row.owner, candidate.Row.id);
                    candidateScratch.Add(entry.Key);
                    continue;
                }

                candidateScratch.Add(entry.Key);
                if (!registered.ContainsKey(entry.Key) && now != NotWatched)
                {
                    AddAutoRow(registered, candidate.Row, now);
                }
            }

            foreach (int hash in candidateScratch)
            {
                otherCandidates.Remove(hash);
            }
        }

        // Row types watched by their own source above.
        // Guide objects (IK targets, FK nodes, items) only ever get Timeline's own "Selected
        // GuideObject Pos / Rot / Scl" rows from the guide source, never the guide object rows other
        // plugins add under their own owner (ShalltyUtils lists guideObjectXPos, ... under KKPE).
        static bool WatchedElsewhere(InterpolableModel model)
        {
            if (model.id.StartsWith("guideObject", StringComparison.Ordinal))
            {
                return true;
            }

            if (NeverAdded(model.owner, model.id, ModelName(model)))
            {
                return true;
            }

            if (model.owner == "KKPE")
            {
                return model.id.StartsWith("bone", StringComparison.Ordinal) || model.id.EndsWith("BlendShape", StringComparison.Ordinal)
                    || model.id.StartsWith("groupBlendShape", StringComparison.Ordinal);
            }

            return model.owner == "MaterialEditor";
        }

        // Rows Auto Key never adds and, unless the AutoKey / Rows list names them, never keys:
        // the camera's and the character animation's rows. Matched by name and by id.
        static readonly HashSet<string> NeverAddedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Camera Zoom", "Camera Original Position", "Camera Original Rotation", "Camera Position", "Camera Postion", "Camera Rotation",
            "Animation Time", "Animation Speed", "Animation"
        };

        static readonly HashSet<string> NeverAddedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "charAnimation", "AnimationTime", "AnimationSpeed"
        };

        internal static bool NeverAdded(string owner, string id, string name)
        {
            if (id != null && (NeverAddedIds.Contains(id) || (owner == "Timeline" && id.IndexOf("camera", StringComparison.OrdinalIgnoreCase) >= 0)))
            {
                return true;
            }

            return name != null && NeverAddedNames.Contains(name.Trim());
        }

        static string ModelName(InterpolableModel model)
        {
            try
            {
                return model.name;
            }
            catch
            {
                return null;
            }
        }

        bool IsDrifting(string owner, string id)
        {
            HashSet<string> ids;
            return owner != null && id != null && drifting.TryGetValue(owner, out ids) && ids.Contains(id);
        }

        void MarkDrifting(string owner, string id)
        {
            if (owner == null || id == null)
            {
                return;
            }

            HashSet<string> ids;
            if (!drifting.TryGetValue(owner, out ids))
            {
                ids = new HashSet<string>();
                drifting[owner] = ids;
            }

            if (ids.Add(id))
            {
                Logger.LogDebug("Auto Key rows: " + owner + " / " + id + " changes by itself and is not watched");
            }
        }

        // ---- Material Editor ---------------------------------------------------------------------

        void InstallMaterialEditorHook()
        {
            Type studio = FindLoadedType("KK_Plugins.MaterialEditor.MEStudio");
            if (studio == null)
            {
                return;
            }

            HarmonyMethod postfix = H("MaterialEditorEdited");
            foreach (string name in new string[] { "SetMaterialFloatProperty", "SetMaterialColorProperty", "SetMaterialTextureOffset", "SetMaterialTextureScale", "SetMaterialKeywordProperty" })
            {
                MethodInfo method = studio.GetMethod(name, Flags | BindingFlags.DeclaredOnly);
                if (method == null || method.GetParameters().Length != 5)
                {
                    continue;
                }

                try
                {
                    harmony.Patch(method, null, postfix, null, null, null);
                }
                catch (Exception e)
                {
                    Logger.LogWarning("Auto Key rows: Material Editor " + name + " not watched - " + Error(e));
                }
            }
        }

        // Postfix of Material Editor's Studio window: (data, material, property, value, gameObject).
        static void MaterialEditorEdited(object __instance, MethodBase __originalMethod, object[] __args)
        {
            Plugin self = Self;
            if (self == null || !self.autoKey || !self.AutoKeyAddRows || __args == null || __args.Length != 5)
            {
                return;
            }

            Material material = __args[1] as Material;
            string property = __args[2] as string;
            GameObject target = __args[4] as GameObject;
            if (material == null || property == null || target == null)
            {
                return;
            }

            string method = __originalMethod.Name;
            MaterialEdit edit = new MaterialEdit();
            edit.Ui = __instance;
            edit.Data = __args[0];
            edit.Target = target;
            edit.Material = MaterialName(material);
            edit.Property = property;
            edit.Method = method;
            edit.RowType = method == "SetMaterialFloatProperty" ? "FloatProperty"
                : method == "SetMaterialColorProperty" ? "ColorProperty"
                : method == "SetMaterialKeywordProperty" ? "KeywordProperty"
                : "TextureOffsetScale";
            foreach (MaterialEdit waiting in self.materialEdits)
            {
                if (waiting.Target == edit.Target && waiting.Material == edit.Material && waiting.Property == edit.Property && waiting.Method == edit.Method)
                {
                    return;
                }
            }

            self.materialEdits.Add(edit);
        }

        // Material Editor's own form of a material's name.
        static string MaterialName(Material material)
        {
            return material.name.Replace("(Instance)", "").Replace(" Instance", "").Trim();
        }

        // Each waiting edit: select its property for Timeline as Material Editor's Timeline button
        // does, take the rows Material Editor then offers, and put its own selection back.
        void AddMaterialEditorRows(Dictionary<int, Interpolable> registered)
        {
            if (materialEdits.Count == 0)
            {
                return;
            }

            List<MaterialEdit> edits = new List<MaterialEdit>(materialEdits);
            materialEdits.Clear();
            foreach (MaterialEdit edit in edits)
            {
                try
                {
                    AddMaterialEditorRow(registered, edit);
                }
                catch (Exception e)
                {
                    Logger.LogDebug("Auto Key rows, Material Editor " + edit.Property + ": " + Error(e));
                }
            }
        }

        void AddMaterialEditorRow(Dictionary<int, Interpolable> registered, MaterialEdit edit)
        {
            // A slider drag calls Material Editor every frame: once its row exists, nothing to do.
            string key = edit.Target.GetInstanceID() + "|" + edit.Material + "|" + edit.Property + "|" + edit.Method;
            List<Interpolable> made;
            if (materialRows.TryGetValue(key, out made) && made.TrueForAll(delegate (Interpolable row) { return registered.ContainsKey(row.GetHashCode()); }))
            {
                return;
            }

            made = new List<Interpolable>();
            materialRows[key] = made;
            ObjectCtrlInfo oci = edit.Data as ObjectCtrlInfo ?? OwnerOf(edit.Target);
            if (oci == null)
            {
                return;
            }

            MemberInfo selection = StaticOrInstanceMember(edit.Ui.GetType(), "selectedInterpolable");
            if (selection == null)
            {
                return;
            }

            Type selectedType = selection is FieldInfo ? ((FieldInfo)selection).FieldType : ((PropertyInfo)selection).PropertyType;
            FieldInfo rowTypeField = selectedType.GetField("RowType", Flags);
            if (rowTypeField == null)
            {
                return;
            }

            object rowType = Enum.Parse(rowTypeField.FieldType, edit.RowType);
            object selected = Activator.CreateInstance(selectedType, Flags, null, new object[] { edit.Target, rowType, edit.Material, edit.Property, "" }, null);
            object previous = ReadMember(selection, edit.Ui);
            WriteMember(selection, edit.Ui, selected);
            try
            {
                foreach (InterpolableModel model in Get<List<InterpolableModel>>(native, "_interpolableModelsList"))
                {
                    if (model.owner != "MaterialEditor" || !FitsEdit(model.id, edit.Method) || !model.IsCompatibleWithTarget(oci))
                    {
                        continue;
                    }

                    object parameter = ParameterOf(model, oci);
                    if (parameter == null)
                    {
                        continue;
                    }

                    Interpolable row = new Interpolable(oci, parameter, model);
                    made.Add(row);
                    if (!registered.ContainsKey(row.GetHashCode()))
                    {
                        AddAutoRow(registered, row, EditSignature(row));
                    }
                }
            }
            finally
            {
                WriteMember(selection, edit.Ui, previous);
            }
        }

        // A texture offset edit adds the offset row, not the scale row of the same property.
        static bool FitsEdit(string id, string method)
        {
            string lower = id.ToLowerInvariant();
            if (method == "SetMaterialTextureOffset")
            {
                return lower.Contains("offset");
            }

            if (method == "SetMaterialTextureScale")
            {
                return lower.Contains("scale");
            }

            return !lower.Contains("projector");
        }

        // The watched (else selected) object that holds this game object.
        ObjectCtrlInfo OwnerOf(GameObject target)
        {
            ObjectCtrlInfo fallback = null;
            foreach (ObjectCtrlInfo oci in watched)
            {
                Transform root = oci.guideObject == null ? null : oci.guideObject.transformTarget;
                if (root != null && target.transform.IsChildOf(root))
                {
                    return oci;
                }

                if (fallback == null)
                {
                    fallback = oci;
                }
            }

            return fallback;
        }

        static MemberInfo StaticOrInstanceMember(Type type, string name)
        {
            const BindingFlags any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy;
            for (Type at = type; at != null; at = at.BaseType)
            {
                MemberInfo member = (MemberInfo)at.GetField(name, any) ?? at.GetProperty(name, any);
                if (member != null)
                {
                    return member;
                }
            }

            return null;
        }

        static object ReadMember(MemberInfo member, object instance)
        {
            FieldInfo field = member as FieldInfo;
            return field != null ? field.GetValue(field.IsStatic ? null : instance) : ((PropertyInfo)member).GetValue(instance, null);
        }

        static void WriteMember(MemberInfo member, object instance, object value)
        {
            FieldInfo field = member as FieldInfo;
            if (field != null)
            {
                field.SetValue(field.IsStatic ? null : instance, value);
            }
            else
            {
                ((PropertyInfo)member).SetValue(instance, value, null);
            }
        }

        // ---- adding a row ---------------------------------------------------------------------------

        // Register a new row as Timeline's scene loader does (top level of the list); Auto Key
        // writes its key once the edit pauses, like the key of a row that exists.
        void AddAutoRow(Dictionary<int, Interpolable> registered, Interpolable row, object signature)
        {
            int hash = row.GetHashCode();
            if (registered.ContainsKey(hash))
            {
                return;
            }

            try
            {
                if (row.GetValue() == null)
                {
                    return;
                }

                registered.Add(hash, row);
                AddLeaf(row, row.oci, new RegistrationReport(), null);
            }
            catch (Exception e)
            {
                registered.Remove(hash);
                Logger.LogDebug("Auto Key rows: " + row.owner + " / " + row.id + " - " + Error(e));
                return;
            }

            autoKeyBaseline[row] = signature;
            autoKeyChangedFrame[row] = Time.frameCount;
            autoRowsAdded++;
            autoRowsViewDirty = true;
        }

        // ---- the check box in the Auto Key menu ----------------------------------------------------

        Image addRowsBox, addRowsCheck;

        void BuildAddRowsToggle(RectTransform menu, float top)
        {
            RectTransform line = MakeRect("Separator", menu, false);
            Put(line, MenuPadding + 2f, top + 1f, MenuWidth - 2 * MenuPadding - 4f, 1f);
            line.gameObject.AddComponent<Image>().color = new Color(1f, 1f, 1f, .15f);

            RectTransform row = MakeRect("Add missing rows", menu, false);
            Put(row, MenuPadding, top + AddRowsGap, MenuWidth - 2 * MenuPadding, MenuRowHeight - 1f);
            Image rowBack = row.gameObject.AddComponent<Image>();
            rowBack.color = new Color(0f, 0f, 0f, 0f);
            Button button = row.gameObject.AddComponent<Button>();
            button.targetGraphic = rowBack;
            ColorBlock colors = button.colors;
            colors.highlightedColor = new Color(.7f, .85f, 1f, 1f);
            button.colors = colors;
            button.onClick.AddListener(delegate
            {
                Guarded(delegate
                {
                    autoKeyAddRowsSetting.Value = !autoKeyAddRowsSetting.Value;
                    Config.Save();
                    Note(AutoKeyAddRows
                        ? "Auto Key adds missing rows: an edit without a row gets one"
                        : "Auto Key keys existing rows only");
                });
            });

            RectTransform box = MakeRect("Box", row, false);
            Put(box, 6f, (MenuRowHeight - 1f - 12f) / 2f, 12f, 12f);
            addRowsBox = box.gameObject.AddComponent<Image>();
            addRowsBox.raycastTarget = false;
            RectTransform check = MakeRect("Check", box, false);
            Fill(check, 2f, 2f, 2f, 2f);
            addRowsCheck = check.gameObject.AddComponent<Image>();
            addRowsCheck.raycastTarget = false;

            Text label = Label(row, "Add missing rows", 12);
            label.alignment = TextAnchor.MiddleLeft;
            label.rectTransform.offsetMin = new Vector2(24f, 0f);
            ShowAddRowsState();
        }

        void ShowAddRowsState()
        {
            if (addRowsBox == null || addRowsCheck == null)
            {
                return;
            }

            addRowsBox.color = new Color(.78f, .78f, .8f, 1f);
            addRowsCheck.color = AutoKeyAddRows ? DotOnColor : MenuColor;
        }
    }
}
