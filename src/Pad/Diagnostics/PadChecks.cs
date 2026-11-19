using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using BepInEx;
using GinShared;
using HarmonyLib;
using Studio;
using UnityEngine;
using UnityEngine.EventSystems;

namespace GinPadHack
{
    // Guarded regression checks. They need the marked test game and the fixture character
    // selected; they never run during normal use.
    public sealed partial class Plugin
    {
        public string RunIsolatedTests()
        {
            if (!IsIsolatedTest())
            {
                return "SKIP Pad tests: isolated test-game only";
            }

            return (toolbarToggle == null ? "FAIL Pad: toolbar button missing\n" : "PASS Pad: KKAPI toolbar button added\n")
                + CheckCameraBlock() + "\n"
                + CheckGizmoBlock() + "\n"
                + window.RunIsolatedTests();
        }

        // While the window owns the pointer, Studio's camera methods must be skipped entirely:
        // the original InputMouseProc would clear its "dragging" latch when no button is held.
        string CheckCameraBlock()
        {
            try
            {
                Studio.CameraControl camera = Singleton<Studio.Studio>.Instance.cameraCtrl;
                BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                MethodInfo mouse = typeof(Studio.CameraControl).GetMethod("InputMouseProc", flags);
                MethodInfo wheel = typeof(Studio.CameraControl).GetMethod("InputMouseWheelZoomProc", flags);
                FieldInfo dragging = typeof(Studio.CameraControl).GetField("dragging", flags);
                foreach (MethodInfo method in new MethodInfo[] { mouse, wheel })
                {
                    Patches patches = Harmony.GetPatchInfo(method);
                    if (patches == null || !patches.Owners.Contains(Guid))
                    {
                        return "FAIL Pad camera block: " + method.Name + " is not patched";
                    }
                }

                bool wasVisible = window.Visible;
                window.Visible = true;
                window.SetPointerOwnershipForTest(true);
                dragging.SetValue(camera, true);
                bool moved = (bool)mouse.Invoke(camera, null) || (bool)wheel.Invoke(camera, null);
                bool latchKept = (bool)dragging.GetValue(camera);
                window.SetPointerOwnershipForTest(false);
                window.Visible = false;
                bool released = !window.OwnsPointer;
                mouse.Invoke(camera, null);
                bool latchCleared = !(bool)dragging.GetValue(camera);
                window.Visible = wasVisible;
                if (moved || !latchKept || !released || !latchCleared)
                {
                    return "FAIL Pad camera block: moved=" + moved + " latchKept=" + latchKept + " released=" + released + " latchCleared=" + latchCleared;
                }

                // The pin: a camera change made by anything else during the press is undone.
                MethodInfo lateUpdate = typeof(Studio.CameraControl).GetMethod("LateUpdate", flags);
                Studio.CameraControl.CameraData start = camera.Export();
                window.Visible = true;
                window.SetPointerOwnershipForTest(true);
                lateUpdate.Invoke(camera, null);
                Studio.CameraControl.CameraData turned = camera.Export();
                turned.rotate += new Vector3(0f, 25f, 0f);
                camera.Import(turned);
                lateUpdate.Invoke(camera, null);
                bool pinned = camera.Export().rotate == start.rotate;
                window.SetPointerOwnershipForTest(false);
                lateUpdate.Invoke(camera, null);
                camera.Import(turned);
                lateUpdate.Invoke(camera, null);
                bool freeAgain = camera.Export().rotate == turned.rotate;
                camera.Import(start);
                lateUpdate.Invoke(camera, null);
                window.Visible = wasVisible;
                if (!pinned || !freeAgain)
                {
                    return "FAIL Pad camera pin: pinned=" + pinned + " freeAgain=" + freeAgain;
                }

                return "PASS Pad camera block: Studio's mouse and wheel camera methods are skipped while a press that began on the window is held, and run again afterwards; "
                    + "a camera turn made by anything else during the press is undone, and allowed again after release";
            }
            catch (Exception error)
            {
                return "FAIL Pad camera block: " + error;
            }
        }

        // Studio's gizmo arrows and rings take the mouse through the EventSystem (3D raycasts).
        // With the window over a gizmo, the EventSystem must find only the Pad's catcher; next to
        // the window it must find the gizmo again.
        string CheckGizmoBlock()
        {
            bool wasVisible = window.Visible;
            try
            {
                Studio.GuideBase gizmo = null;
                Collider collider = null;
                foreach (Studio.GuideBase candidate in UnityEngine.Object.FindObjectsOfType<Studio.GuideBase>())
                {
                    Collider c = candidate.GetComponent<Collider>();
                    if (candidate.isActiveAndEnabled && c != null && c.enabled && (candidate is Studio.GuideMove || candidate is Studio.GuideRotation))
                    {
                        gizmo = candidate;
                        collider = c;
                        break;
                    }
                }

                if (gizmo == null)
                {
                    return "FAIL Pad gizmo block: no active gizmo in the test scene";
                }

                Camera camera = Camera.main;
                foreach (PhysicsRaycaster raycaster in UnityEngine.Object.FindObjectsOfType<PhysicsRaycaster>())
                {
                    if (raycaster.isActiveAndEnabled)
                    {
                        camera = raycaster.eventCamera;
                    }
                }

                Vector3 screen = camera.WorldToScreenPoint(collider.bounds.center);
                window.Visible = false;
                GameObject alone = FirstHit(screen);
                window.Visible = true;
                window.PlaceForTest(new Rect(screen.x - 100f, Screen.height - screen.y - 100f, 200f, 200f));
                GameObject covered = FirstHit(screen);
                window.PlaceForTest(new Rect(screen.x + 150f, Screen.height - screen.y - 100f, 200f, 200f));
                GameObject beside = FirstHit(screen);
                bool gizmoAlone = alone != null && alone.GetComponentInParent<Studio.GuideBase>() != null;
                bool caught = catcher.IsCatcher(covered);
                bool gizmoBeside = beside != null && beside.GetComponentInParent<Studio.GuideBase>() != null;
                string state = "alone " + Name(alone) + ", under the window " + Name(covered) + ", window beside " + Name(beside);
                if (!gizmoAlone || !caught || !gizmoBeside)
                {
                    return "FAIL Pad gizmo block: " + state;
                }

                return "PASS Pad gizmo block: at Studio's " + gizmo.GetType().Name + " gizmo the EventSystem finds only the Pad's catcher while the window is over it, and the gizmo again when the window is beside it (" + state + ")";
            }
            catch (Exception error)
            {
                return "FAIL Pad gizmo block: " + error;
            }
            finally
            {
                window.Visible = wasVisible;
            }
        }

        static GameObject FirstHit(Vector3 screen)
        {
            PointerEventData data = new PointerEventData(EventSystem.current);
            data.position = new Vector2(screen.x, screen.y);
            List<RaycastResult> results = new List<RaycastResult>();
            EventSystem.current.RaycastAll(data, results);
            return results.Count == 0 ? null : results[0].gameObject;
        }

        static string Name(GameObject target)
        {
            return target == null ? "nothing" : target.name;
        }

        // Ask GIN Timeline life hack for a whole-face key at the playhead, as "Key now" does.
        public string RequestFaceKey()
        {
            if (!IsIsolatedTest())
            {
                return "SKIP Pad face key: isolated test-game only";
            }

            PadTarget target = Kkpe.Target();
            string status = target == null ? "no target" : TimelineBridge.KeyFace(target.Character);
            return (status.StartsWith("Face keyed:", StringComparison.Ordinal) ? "PASS" : "FAIL") + " Pad face key requested: " + status;
        }

        // The item Shapes tab: KKPE attaches to a new item in its next Update and lists the item's
        // Blend Shapes a moment later, and it applies a weight in its LateUpdate, so the test driver
        // calls these three with time in between.
        public string StartItemShapesTest()
        {
            return IsIsolatedTest() ? window.AddItemWithShapes() : "SKIP Pad item Shapes: isolated test-game only";
        }

        public string FinishItemShapesTest()
        {
            return IsIsolatedTest() ? window.CheckItemShapes() : "SKIP Pad item Shapes: isolated test-game only";
        }

        public string ConfirmItemShapesTest()
        {
            return IsIsolatedTest() ? window.CheckItemShapeShown() : "SKIP Pad item Shapes: isolated test-game only";
        }

        public string PreparePreview(int tab)
        {
            if (!IsIsolatedTest())
            {
                return "SKIP Pad preview: isolated test-game only";
            }

            ShowWindow(true);
            return window.PrepareIsolatedPreview(tab);
        }

        internal static bool IsIsolatedTest()
        {
            return Array.IndexOf(Environment.GetCommandLineArgs(), "-ginLifeHacksVerify") >= 0
                && new DirectoryInfo(Paths.GameRootPath).Name.Equals("test-game", StringComparison.OrdinalIgnoreCase)
                && File.Exists(Path.Combine(Paths.GameRootPath, ".kk-workshop-test"));
        }
    }

    public sealed partial class PadWindow
    {
        // Puts the window there and counts it as drawn this frame.
        internal void PlaceForTest(Rect place)
        {
            window = place;
            drawn = place;
            lastDrawFrame = Time.frameCount;
        }

        // What a layout pass leaves behind: the working rectangle shrunk to 10 x 10.
        internal void ShrinkForTest()
        {
            window.width = window.height = 10f;
        }

        internal void SetPointerOwnershipForTest(bool owned)
        {
            anyButtonHeld = owned;
            pressBeganOnWindow = owned;
        }

        internal string RunIsolatedTests()
        {
            StringBuilder log = new StringBuilder();
            PadTarget target = Kkpe.Target();
            if (target == null)
            {
                return "FAIL Pad: no KKPE target for the selected character";
            }

            log.AppendLine("PASS Pad: selected character found through KKPE without Advanced mode (" + target.Name + ")");
            Check(log, "eye link", delegate { return CheckEyeLink(target); });
            Check(log, "Face tab list", delegate { return CheckFaceList(target); });
            Check(log, "pupil limits", delegate { return CheckPupils(target); });
            Check(log, "expression map", delegate { return CheckMap(target); });
            Check(log, "Body tab", delegate { return CheckBody(target); });
            Check(log, "Body drag limits", delegate { return CheckDragLimits(target); });
            Check(log, "Body undo", delegate { return CheckPoseUndo(target); });
            Check(log, "endless drag", delegate { return CheckEndlessDrag(target); });
            Check(log, "corner controls", delegate { return CheckCorners(); });
            Check(log, "Shift axis lock", delegate { return CheckAxisLock(); });
            Check(log, "keying", delegate { return CheckKeying(target); });
            Check(log, "item mode", delegate { return CheckItems(target); });
            Check(log, "scrolling", delegate { return CheckScrolling(); });
            Check(log, "tabs", delegate { return CheckTabs(); });
            return log.ToString().TrimEnd();
        }

        delegate string CheckBody_();

        static void Check(StringBuilder log, string name, CheckBody_ body)
        {
            try
            {
                log.AppendLine("PASS Pad " + name + ": " + body());
            }
            catch (Exception error)
            {
                log.AppendLine("FAIL Pad " + name + ": " + Root(error).Message);
            }
        }

        static void Require(bool condition, string what)
        {
            if (!condition)
            {
                throw new InvalidOperationException(what);
            }
        }

        static bool Near(float actual, float expected)
        {
            return Mathf.Abs(actual - expected) < 0.0001f * Mathf.Max(1f, Mathf.Abs(expected) * 100f);
        }

        static BlendMesh MeshWithRole(PadTarget target, string role)
        {
            foreach (BlendMesh mesh in Kkpe.Meshes(target))
            {
                if (mesh.Role == role)
                {
                    return mesh;
                }
            }

            throw new InvalidOperationException("no renderer with role " + role);
        }

        // Setting an eye shape of the face must reach the linked renderers (the eyelashes).
        static string CheckEyeLink(PadTarget target)
        {
            BlendMesh face = MeshWithRole(target, Kkpe.FaceRole);
            string shape = face.Renderer.sharedMesh.GetBlendShapeName(0);
            Require(Kkpe.SetWeight(face, shape, 77f), "KKPE accepted " + shape);
            Require(Near(Kkpe.Weight(face, 0), 77f), "face override stored");
            IList linked = Reflect.Get<IList>(face.Wrapper, "_linkedBlendRenderers");
            Require(linked != null && linked.Count > 0, "face has linked renderers");
            int followers = 0;
            List<string> names = new List<string>();
            foreach (object other in linked)
            {
                SkinnedMeshRenderer renderer = Reflect.Get<SkinnedMeshRenderer>(other, "_renderer");
                IDictionary overrides = Reflect.Get<IDictionary>(other, "_dirtyBlends");
                foreach (DictionaryEntry entry in overrides)
                {
                    if (Near(Convert.ToSingle(Reflect.Get(entry.Value, "weight")), 77f))
                    {
                        followers++;
                        names.Add(renderer == null ? "?" : renderer.name);
                        break;
                    }
                }
            }

            Require(followers > 0, "a linked renderer received the weight");
            Kkpe.SetWeight(face, shape, 0f);
            return shape + " = 77 reached " + followers + " of " + linked.Count + " linked renderers (" + String.Join(", ", names.ToArray()) + ")";
        }

        string CheckFaceList(PadTarget target)
        {
            ShapeList chosen = host.FaceList.Value;
            Require((ShapeList)host.FaceList.DefaultValue == ShapeList.Used, "Used is the default list");
            host.FaceList.Value = ShapeList.Used;
            RefreshFaceShapes(target);
            int listed = eyeShapes.Count + browShapes.Count + mouthShapes.Count + toothShapes.Count;
            foreach (List<ShapeRow> section in new List<ShapeRow>[] { eyeShapes, browShapes, mouthShapes, toothShapes })
            {
                foreach (ShapeRow shape in section)
                {
                    Require(!hiddenByDefault.Contains(Kkpe.ShortName(shape.Name)), shape.Name + " is hidden by default");
                }
            }

            // Labels: the game's mesh prefix goes, a name that uses dots itself is kept whole.
            string[][] labels =
            {
                new string[] { "eye_face.f00_egao_cl", "f00_egao_cl" },
                new string[] { "kuti_face.f00_def_op", "f00_def_op" },
                new string[] { "mayuge.mayu00_def_cl", "mayu00_def_cl" },
                new string[] { "kuti_ha.ha00_name_op", "ha00_name_op" },
                new string[] { "P.Open1.L", "P.Open1.L" },
                new string[] { "S.Huge.R", "S.Huge.R" },
                new string[] { "P.Out2.add.U", "P.Out2.add.U" },
                new string[] { "P.Clit Up", "P.Clit Up" },
                new string[] { "vagina.open", "vagina.open" },
                new string[] { "Close", "Close" },
            };
            foreach (string[] label in labels)
            {
                Require(Kkpe.ShortName(label[0]) == label[1], label[0] + " is labelled " + Kkpe.ShortName(label[0]) + ", expected " + label[1]);
            }

            Require(hiddenByDefault.Count == 5 && hiddenByDefault.Contains("mayu00_def_op") && hiddenShapes >= 5, "the five default-hidden shapes are counted as hidden");
            Require(Mathf.Approximately((float)host.BarSensitivity.DefaultValue, 0.6f), "sensitivity default is 0.6");
            host.FaceList.Value = ShapeList.All;
            RefreshFaceShapes(target);
            int everything = eyeShapes.Count + browShapes.Count + mouthShapes.Count + toothShapes.Count;
            Require(hiddenShapes == 0 && everything > listed, "All lists everything");

            // Used: a shape that is not zero appears, a "def" shape does not even then.
            ShapeRow raised = null, neverListed = null;
            foreach (ShapeRow shape in eyeShapes)
            {
                bool isDefault = hiddenByDefault.Contains(Kkpe.ShortName(shape.Name));
                if (shape.Weight < NonZero && isDefault && neverListed == null)
                {
                    neverListed = shape;
                }
                else if (shape.Weight < NonZero && !isDefault && raised == null)
                {
                    raised = shape;
                }
            }

            Require(raised != null && neverListed != null, "an unused eye shape and a def eye shape exist");
            Kkpe.SetWeight(raised.Mesh, raised.Name, 30f);
            Kkpe.SetWeight(neverListed.Mesh, neverListed.Name, 30f);
            host.FaceList.Value = ShapeList.Used;
            RefreshFaceShapes(target);
            Require(eyeShapes.Exists(delegate(ShapeRow shape) { return shape.Name == raised.Name; }), "Used lists a shape that is not zero");
            Require(!eyeShapes.Exists(delegate(ShapeRow shape) { return shape.Name == neverListed.Name; }), "Used leaves a def shape out even when it is not zero");
            Require(eyeShapes.Count + browShapes.Count + mouthShapes.Count + toothShapes.Count == listed + 1, "nothing else joined the list");
            Kkpe.SetWeight(raised.Mesh, raised.Name, 0f);
            Kkpe.SetWeight(neverListed.Mesh, neverListed.Name, 0f);
            host.FaceList.Value = ShapeList.All;
            RefreshFaceShapes(target);

            // Fav: nothing but the pinned shapes, even one that is in use and not zero.
            ShapeRow favourite = eyeShapes[1], other = eyeShapes[2];
            HashSet<string> pinnedBefore = new HashSet<string>(pinned);
            pinned.Clear();
            pinned.Add(favourite.PinKey);
            Kkpe.SetWeight(other.Mesh, other.Name, 40f);
            host.FaceList.Value = ShapeList.Favorites;
            RefreshFaceShapes(target);
            int favourites = eyeShapes.Count + browShapes.Count + mouthShapes.Count + toothShapes.Count;
            Require(favourites == 1 && eyeShapes[0].Name == favourite.Name, "Fav lists only the pinned shape (" + favourites + " listed)");
            Require(hiddenShapes == everything - 1, "everything else is counted as hidden");
            pinned.Clear();
            RefreshFaceShapes(target);
            Require(eyeShapes.Count + browShapes.Count + mouthShapes.Count + toothShapes.Count == 0, "without pins Fav lists nothing");
            Kkpe.SetWeight(other.Mesh, other.Name, 0f);
            foreach (string key in pinnedBefore)
            {
                pinned.Add(key);
            }

            host.FaceList.Value = chosen;
            RefreshFaceShapes(target);
            return listed + " shapes listed with Used, " + (everything - listed) + " hidden; a shape joins Used when it is not zero, a def shape never; " + everything + " with All; Fav lists exactly the pinned shapes; " + labels.Length + " label rules (GIN Kupaa3 / Puffy names keep their L / R part)";
        }

        string CheckPupils(PadTarget target)
        {
            FindPupils(target);
            Require(HasPupils, "pupil bones found");
            ReadPupils(target);
            float min = host.PupilLeftMinX.Value, max = host.PupilLeftMaxX.Value;
            float nose = host.PupilNoseDepth.Value, outer = host.PupilOuterDepth.Value;
            Require(leftNoseSign == -rightNoseSign, "the pupils' nose sides are opposite");

            // Far beyond both limits, one way and then the other.
            spread = 0f;
            pupilHeight = 0f;
            gaze = new Vector2(0.05f, 0f);
            WritePupils(target);
            Vector3 left = Kkpe.Position(target, pupilLeft), right = Kkpe.Position(target, pupilRight);
            Require(Near(left.x, max) && Near(right.x, -min), "+X stops at " + max + " (left) and " + (-min) + " (right): " + left.x + ", " + right.x);
            float leftDepthAtMax = leftNoseSign > 0f ? nose : outer;
            Require(Near(left.z, leftDepthAtMax) && Near(right.z, leftNoseSign > 0f ? outer : nose), "depth at the +X limit: " + left.z + ", " + right.z);

            gaze = new Vector2(-0.05f, 0f);
            WritePupils(target);
            left = Kkpe.Position(target, pupilLeft);
            right = Kkpe.Position(target, pupilRight);
            Require(Near(left.x, min) && Near(right.x, -max), "-X stops at " + min + " (left) and " + (-max) + " (right): " + left.x + ", " + right.x);
            Require(Near(left.z, leftNoseSign > 0f ? outer : nose) && Near(right.z, leftNoseSign > 0f ? nose : outer), "depth at the -X limit");

            // Inside the range: spread and height are kept, depth lies between the two ends.
            gaze = new Vector2(0.002f, 0.004f);
            spread = 0.003f;
            pupilHeight = 0.002f;
            WritePupils(target);
            left = Kkpe.Position(target, pupilLeft);
            right = Kkpe.Position(target, pupilRight);
            Require(Near(left.x, 0.005f) && Near(right.x, -0.001f), "spread moves the pupils apart");
            Require(Near(left.y, 0.006f) && Near(right.y, 0.002f), "height difference applied");
            Require(left.z < Mathf.Max(nose, outer) && left.z > Mathf.Min(nose, outer), "depth between the ends");
            pupilSize = 0.8f;
            WritePupilSize(target);
            Require(Near(Kkpe.Scale(target, pupilRight).x, 0.8f), "size applied");
            ReadPupils(target);
            Require(Near(spread, 0.003f) && Near(pupilHeight, 0.002f), "values read back");
            return "left pupil's +X points " + (leftNoseSign > 0f ? "towards" : "away from") + " the nose on this character; X clamped to "
                + min + ".." + max + " (mirrored on the right), Z " + nose + " at the nose side to " + outer + " outside; spread, height and size stored by KKPE";
        }

        string CheckMap(PadTarget target)
        {
            ExpressionMap learned = ExpressionMap.Default();
            Require(learned.Eyes.Count == DefaultExpressionMap.Eyes.Length && learned.Mouths.Count == DefaultExpressionMap.Mouths.Length, "default map loaded");
            Require(learned.Eyes.Count == 20 && learned.Mouths.Count == 28 && learned.Brows.Count == 11, "the author's map: 20 eye rows, 28 mouth columns, 11 brows");
            string eyeShape = DefaultExpressionMap.EyeShapes[2];
            float a, b;
            learned.Eyes[0].TryGetValue(eyeShape, out a);
            learned.Eyes[1].TryGetValue(eyeShape, out b);
            Require(Near(ExpressionMap.Blend(learned.Eyes, 0.25f, eyeShape), a * 0.75f + b * 0.25f), "blend between two rows");
            Require(Near(ExpressionMap.Blend(learned.Eyes, 99f, eyeShape), ExpressionMap.Blend(learned.Eyes, learned.Eyes.Count - 1, eyeShape)), "blend clamps at the last row");

            // File round trip.
            string file = Path.Combine(storage, "expression-map-test.txt");
            learned.Save(file);
            ExpressionMap reloaded = ExpressionMap.Load(file);
            File.Delete(file);
            Require(reloaded.Eyes.Count == learned.Eyes.Count && reloaded.Mouths.Count == learned.Mouths.Count && reloaded.Brows.Count == learned.Brows.Count, "saved map reloads");
            float teeth;
            reloaded.Mouths[3].TryGetValue(ExpressionMap.TeethKey, out teeth);
            Require(Near(teeth, DefaultExpressionMap.Teeth[3]), "teeth weight survives the file");

            // Apply to the character: mouth only, then eyes and brows.
            map = learned;
            BlendMesh face = MeshWithRole(target, Kkpe.FaceRole);
            Mesh shapes = face.Renderer.sharedMesh;
            int mouthStart = Kkpe.MouthStart(target);
            Require(mouthStart > 0 && mouthStart < shapes.blendShapeCount, "mouth section found at " + mouthStart);
            Kkpe.SetWeight(face, shapes.GetBlendShapeName(1), 12f);          // an eye shape the mouth must not touch
            mapPoint = new Vector2(3f, 2f);
            ApplyMap(target, false, true, false);
            Require(Near(Kkpe.Weight(face, 1), 12f), "mouth-only apply leaves the eyes alone");
            int checkedShapes = 0;
            for (int index = mouthStart; index < shapes.blendShapeCount; index++)
            {
                float expected;
                learned.Mouths[3].TryGetValue(shapes.GetBlendShapeName(index), out expected);
                Require(Near(Kkpe.Weight(face, index), expected), shapes.GetBlendShapeName(index) + " = " + expected);
                checkedShapes++;
            }

            BlendMesh tooth = MeshWithRole(target, Kkpe.TeethRole);
            Require(Near(Kkpe.Weight(tooth, ExpressionMap.TeethShapeIndex), DefaultExpressionMap.Teeth[3]), "teeth row set");

            mapPoint = new Vector2(3f, 2.5f);
            browPoint = 4f;
            ApplyMap(target, true, false, true);
            for (int index = 0; index < mouthStart; index++)
            {
                string name = shapes.GetBlendShapeName(index);
                float low, high;
                learned.Eyes[2].TryGetValue(name, out low);
                learned.Eyes[3].TryGetValue(name, out high);
                Require(Near(Kkpe.Weight(face, index), (low + high) / 2f), name + " is halfway between rows 3 and 4");
                checkedShapes++;
            }

            BlendMesh brow = MeshWithRole(target, Kkpe.BrowRole);
            int browIndex = brow.Renderer.sharedMesh.GetBlendShapeIndex(DefaultExpressionMap.BrowShapes[0]);
            Require(browIndex >= 0 && Near(Kkpe.Weight(brow, browIndex), DefaultExpressionMap.Brows[4][0]), "brow pose applied");

            Dictionary<string, float> current = CurrentPose(target, FacePart.Mouth);
            Require(current.Count == learned.Mouths[3].Count, "current mouth pose reads back as stop 4 (" + current.Count + " weights)");
            string editor = CheckMapMaking(target, face, mouthStart);
            return learned.Mouths.Count + " mouth columns x " + learned.Eyes.Count + " eye rows + " + learned.Brows.Count + " brow poses; "
                + checkedShapes + " weights verified on the character; parts apply independently; file round trip; " + editor;
        }

        // The pose editor of the Map: open a pose, change it with a bar, reorder, add, delete, undo,
        // store the current face, restore the defaults - and the file follows by itself.
        string CheckMapMaking(PadTarget target, BlendMesh face, int mouthStart)
        {
            Require(!File.Exists(MapPath), "the test game has no expression map of its own");
            bool mapWasOpen = tabs[(int)Tab.Map];
            ShapeList chosen = host.FaceList.Value;
            tabs[(int)Tab.Map] = true;
            host.FaceList.Value = ShapeList.Favorites;
            try
            {
                int mouthCount = map.Mouths.Count, eyeCount = map.Eyes.Count;
                Dictionary<string, float> learnedPose = map.Mouths[5];

                // Open mouth pose 6: the character shows it and its direction of the map is locked.
                Request(MapEdit.Open, FacePart.Mouth, 5);
                TickMap(false);
                Require(IsMaking(FacePart.Mouth) && this.making.Stop == 5 && Near(mapPoint.x, 5f), "mouth pose 6 opened");
                Require(SamePose(CurrentPose(target, FacePart.Mouth), learnedPose), "the character shows the opened pose");
                MoveMapPoint(new Rect(0f, 0f, 100f, 100f), new Vector2(10f, 10f), false);
                Require(Near(mapPoint.x, 5f) && Near(mapPoint.y, (eyeCount - 1) * 0.1f), "a map drag moves the eyes but not the open mouth pose");
                TickMap(false);

                // With Fav the editor still lists what the pose consists of.
                RefreshFaceShapes(target);
                Require(mouthShapes.Count >= learnedPose.Count - (learnedPose.ContainsKey(ExpressionMap.TeethKey) ? 1 : 0) && hiddenShapes > 0, "the editor lists the pose's own shapes with Fav (" + mouthShapes.Count + ")");
                Require(mapTeeth.Count == 1 && mapTeeth[0].Index == ExpressionMap.TeethShapeIndex, "the editor has the teeth row");

                // A bar edit becomes the pose and reaches the file once the mouse is released.
                string shape = face.Renderer.sharedMesh.GetBlendShapeName(mouthStart + 2);
                float before;
                learnedPose.TryGetValue(shape, out before);
                float edited = before > 50f ? 20f : 80f;
                Kkpe.SetWeight(face, shape, edited);
                makingEdited = true;
                TickMap(true);
                float stored;
                map.Mouths[5].TryGetValue(shape, out stored);
                Require(Near(stored, edited) && !File.Exists(MapPath), "the edit is in the pose, the file waits for the mouse release");
                TickMap(false);
                Require(File.Exists(MapPath), "the map was saved by itself");
                ExpressionMap saved = ExpressionMap.Load(MapPath);
                saved.Mouths[5].TryGetValue(shape, out stored);
                Require(Near(stored, edited) && saved.Mouths.Count == mouthCount, "the saved file holds the edited pose");

                // Later / earlier swap neighbours, a copy is added after the pose, delete removes it.
                Dictionary<string, float> editedPose = map.Mouths[5], next = map.Mouths[6];
                Request(MapEdit.Later, FacePart.Mouth, 5);
                TickMap(false);
                Require(map.Mouths[6] == editedPose && map.Mouths[5] == next && this.making.Stop == 6 && Near(mapPoint.x, 6f), "Later swaps the pose with its neighbour and follows it");
                Request(MapEdit.Earlier, FacePart.Mouth, 6);
                TickMap(false);
                Require(map.Mouths[5] == editedPose && this.making.Stop == 5, "Earlier brings it back");
                Request(MapEdit.AddCopy, FacePart.Mouth, 5);
                TickMap(false);
                Require(map.Mouths.Count == mouthCount + 1 && this.making.Stop == 6 && SamePose(map.Mouths[6], editedPose) && map.Mouths[6] != editedPose, "a copy is added after the pose and opened");
                Request(MapEdit.Delete, FacePart.Mouth, 6);
                TickMap(false);
                Require(map.Mouths.Count == mouthCount && map.Mouths[5] == editedPose && map.Mouths[6] == next, "Delete removes the copy");

                // Undo: the pose as it was when it was opened.
                Request(MapEdit.Close, FacePart.Mouth, 6);
                TickMap(false);
                Require(this.making == null, "the editor closes");
                Request(MapEdit.Open, FacePart.Mouth, 5);
                TickMap(false);
                Kkpe.SetWeight(face, shape, before > 50f ? 5f : 95f);
                makingEdited = true;
                TickMap(false);
                Request(MapEdit.Undo, FacePart.Mouth, 5);
                TickMap(false);
                Require(map.Mouths[5] == editedPose && SamePose(CurrentPose(target, FacePart.Mouth), editedPose), "Undo restores the pose and the character");

                // Right click: the face of the character becomes an eye pose.
                string eyeShape = face.Renderer.sharedMesh.GetBlendShapeName(3);
                ApplyMap(target, true, false, false);
                Kkpe.SetWeight(face, eyeShape, 63f);
                Request(MapEdit.StoreCurrentFace, FacePart.Eyes, 7);
                TickMap(false);
                map.Eyes[7].TryGetValue(eyeShape, out stored);
                Require(IsMaking(FacePart.Eyes) && Near(stored, 63f) && Near(mapPoint.y, 7f), "the current face was stored as eye pose 8");

                // A click goes straight to a pose (and closes the editor of that part); a right click opens.
                Request(MapEdit.Open, FacePart.Mouth, 2);
                TickMap(false);
                Request(MapEdit.Select, FacePart.Mouth, 12);
                TickMap(false);
                Require(this.making == null && Near(mapPoint.x, 12f) && SamePose(CurrentPose(target, FacePart.Mouth), map.Mouths[12]), "a click selects mouth pose 13 directly and closes its editor");
                Request(MapEdit.Open, FacePart.Mouth, 4);
                TickMap(false);
                Request(MapEdit.Select, FacePart.Eyes, 3);
                TickMap(false);
                Require(IsMaking(FacePart.Mouth) && Near(mapPoint.y, 3f), "selecting an eye row leaves the open mouth pose open");
                Request(MapEdit.Close, FacePart.Mouth, 4);
                TickMap(false);

                // Defaults: the learned map again, the user's map kept as a backup.
                Request(MapEdit.Defaults, FacePart.Eyes, 7);
                TickMap(false);
                string backup = Path.Combine(storage, MapBackupFile);
                Require(this.making == null && !File.Exists(MapPath) && File.Exists(backup), "Defaults removes the map file and keeps a backup");
                Require(SamePose(map.Mouths[5], learnedPose) && map.Eyes.Count == eyeCount, "the default poses are back");
                File.Delete(backup);
                return "pose editor: open, bar edit, autosave on release, earlier / later, add copy, delete, undo, store current face and Defaults (with backup) verified; a click selects a pose directly";
            }
            finally
            {
                this.making = null;
                requestedEdit = MapEdit.None;
                makingEdited = mapUnsaved = false;
                map = ExpressionMap.Default();
                File.Delete(MapPath);
                File.Delete(Path.Combine(storage, MapBackupFile));
                tabs[(int)Tab.Map] = mapWasOpen;
                host.FaceList.Value = chosen;
                faceListStale = true;
            }
        }

        static bool SamePose(Dictionary<string, float> a, Dictionary<string, float> b)
        {
            if (a.Count != b.Count)
            {
                return false;
            }

            foreach (KeyValuePair<string, float> weight in a)
            {
                float other;
                if (!b.TryGetValue(weight.Key, out other) || Mathf.Abs(other - weight.Value) > 0.01f)
                {
                    return false;
                }
            }

            return true;
        }

        string CheckBody(PadTarget target)
        {
            int bones = 0, ik = 0;
            List<string> missing = new List<string>();
            target.Character.ActiveKinematicMode(OICharInfo.KinematicMode.IK, true, true);
            foreach (BodyPart part in BodyParts)
            {
                bool found = part.Via == Via.Bone ? target.Bone(part.Name) != null : target.IkTarget(part.Name) != null;
                if (!found)
                {
                    missing.Add(part.Name);
                }
                else if (part.Via == Via.Bone)
                {
                    bones++;
                }
                else
                {
                    ik++;
                }
            }

            Require(missing.Count == 0, "missing on the fixture: " + String.Join(", ", missing.ToArray()));
            foreach (string label in new string[] { "kokan", "R.Sho", "L.Sho" })
            {
                BodyPart small = Part(label);
                Require(small.Via == Via.Bone && small.Small && small.Rotate && !small.Move && !small.Scale, label + " is a small rotation-only bone button");
            }

            Require(Part("kokan").Name == "cf_d_kokan" && Part("R.Sho").Name == "cf_d_shoulder_R" && Part("L.Sho").Name == "cf_d_shoulder_L", "kokan and shoulder joints edit the bones Char Setup keys");
            Require(Part("kokan").Place.xMin > Part("R.Knee").Place.xMax && Part("kokan").Place.xMax < Part("L.Knee").Place.xMin && Part("kokan").Place.y > Part("R.Thigh").Place.y, "kokan sits between the knees");
            Require(Part("R.Sho").Place.y < Part("R.Shoulder").Place.y && Part("R.Sho").Place.xMin < Part("R.Shoulder").Place.xMin
                && Part("L.Sho").Place.y < Part("L.Shoulder").Place.y && Part("L.Sho").Place.xMax > Part("L.Shoulder").Place.xMax, "shoulder joints are above and outside the shoulders");
            foreach (string label in new string[] { "R.Butt", "L.Butt", "R.Toes", "L.Toes", "Waist", "R.Bust3", "L.Bust3" })
            {
                Require(Part(label).Small, label + " is small");
            }

            for (int a = 0; a < BodyParts.Length; a++)
            {
                Require(BodyParts[a].Place.xMin >= 0f && BodyParts[a].Place.xMax <= FigureWidth && BodyParts[a].Place.yMax <= FigureRows * Pitch, BodyParts[a].Label + " lies inside the figure");
                for (int b = a + 1; b < BodyParts.Length; b++)
                {
                    Require(!BodyParts[a].Place.Overlaps(BodyParts[b].Place), BodyParts[a].Label + " and " + BodyParts[b].Label + " do not overlap");
                }
            }

            Require(Part("R.Foot").Place.xMin < Part("R.Thigh").Place.xMin && Part("L.Foot").Place.xMax > Part("L.Thigh").Place.xMax, "the legs stand apart");

            // A bone edit goes to KKPE and can be removed again.
            Transform toes = target.Bone("cf_j_toes_L");
            Quaternion before = Kkpe.Rotation(target, toes);
            Kkpe.SetRotation(target, toes, before * Quaternion.Euler(20f, 0f, 0f));
            Require(Quaternion.Angle(Kkpe.Rotation(target, toes), before * Quaternion.Euler(20f, 0f, 0f)) < 0.5f && Quaternion.Angle(Kkpe.Rotation(target, toes), before) > 19f, "toe rotation stored by KKPE");
            Kkpe.ResetBone(target, toes, false, true, false);
            IDictionary overrides = Reflect.Get<IDictionary>(target.Bones, "_dirtyBones");
            Require(!overrides.Contains(toes.gameObject), "toe override removed");

            Transform bust = target.Bone("cf_d_bust03_R");
            Vector3 scale = Kkpe.Scale(target, bust);
            Kkpe.SetScale(target, bust, scale + new Vector3(0.1f, 0.1f, 0.1f));
            Kkpe.SetPosition(target, bust, Kkpe.Position(target, bust) + new Vector3(0.001f, 0f, 0f));
            Require(Near(Kkpe.Scale(target, bust).x, scale.x + 0.1f), "bust scale stored by KKPE");
            Kkpe.ResetBone(target, bust, true, true, true);

            // IK targets are Studio's own values, found by their exact "(work)" names.
            BodyPart leftHand = Part("L.Hand"), rightHand = Part("R.Hand"), hips = Part("Body"), leftToes = Part("L.Toes"), rightToes = Part("R.Toes");
            OCIChar.IKInfo hand = target.IkTarget(leftHand.Name), otherHand = target.IkTarget(rightHand.Name);
            Require(target.IkTarget(hips.Name).guideObject.transformTarget.name == "cf_t_hips(work)", "Body edits cf_t_hips(work)");
            Require(hand.guideObject.transformTarget.name == "cf_t_hand_L(work)" && Usable(target, leftHand), "left hand IK usable");

            // Selection: click = one part, Ctrl adds / removes, IK and bones never mix.
            selection.Clear();
            Press(target, leftHand, false);
            Require(selection.Count == 1 && Singleton<GuideObjectManager>.Instance.selectObject == hand.guideObject, "IK target selected in Studio");
            Press(target, rightHand, true);
            Require(selection.Count == 2 && selection[1] == rightHand, "Ctrl adds a second IK target");
            Press(target, leftToes, true);
            Require(selection.Count == 1 && selection[0] == leftToes, "a bone replaces an IK selection instead of mixing");
            Press(target, rightToes, true);
            Press(target, rightToes, true);
            Require(selection.Count == 1 && selection[0] == leftToes, "Ctrl removes a selected part");
            Press(target, leftHand, false);
            Press(target, rightHand, true);

            // Dragging a grey button: left = floor plane, right = height; every selected target moves the same way.
            Transform body = target.Character.guideObject.transformTarget;
            Vector3 leftBefore = hand.guideObject.changeAmount.pos, rightBefore = otherHand.guideObject.changeAmount.pos;
            DragSelection(target, leftHand, new Vector2(30f, -20f), false);
            Vector3 leftMoved = hand.guideObject.changeAmount.pos - leftBefore, rightMoved = otherHand.guideObject.changeAmount.pos - rightBefore;
            Require(leftMoved.magnitude > 0.05f && Mathf.Abs(body.TransformVector(leftMoved).y) < 0.0001f, "left drag moves on the floor plane: " + leftMoved);
            Require(Near(rightMoved.x, leftMoved.x) && Near(rightMoved.z, leftMoved.z) && Near(rightMoved.y, leftMoved.y), "the other hand moves the same way, not mirrored: " + rightMoved);
            DragSelection(target, leftHand, new Vector2(30f, -20f), true);
            Vector3 lifted = hand.guideObject.changeAmount.pos - leftBefore - leftMoved;
            Require(lifted.y > 0.05f && Mathf.Abs(lifted.x) < 0.0001f && Mathf.Abs(lifted.z) < 0.0001f, "right drag moves only up and down: " + lifted);
            hand.guideObject.changeAmount.pos = leftBefore;
            otherHand.guideObject.changeAmount.pos = rightBefore;

            // Dragging a green button turns every selected bone by the same world rotation.
            Press(target, leftToes, false);
            Press(target, rightToes, true);
            Transform toesLeft = target.Bone(leftToes.Name), toesRight = target.Bone(rightToes.Name);
            Quaternion leftStart = Kkpe.Rotation(target, toesLeft), rightStart = Kkpe.Rotation(target, toesRight);
            DragSelection(target, rightToes, new Vector2(40f, 10f), false);
            Quaternion leftWorld = toesLeft.parent.rotation * Kkpe.Rotation(target, toesLeft) * Quaternion.Inverse(toesLeft.parent.rotation * leftStart);
            Quaternion rightWorld = toesRight.parent.rotation * Kkpe.Rotation(target, toesRight) * Quaternion.Inverse(toesRight.parent.rotation * rightStart);
            Require(Quaternion.Angle(Quaternion.identity, rightWorld) > 5f, "dragging turns the grabbed bone");
            Require(Quaternion.Angle(leftWorld, rightWorld) < 0.5f, "both bones receive the same world turn");

            // A bar edit on the primary reaches the other selected part as the same world movement.
            Vector3 barMove = hand.guideObject.transformTarget.parent.TransformVector(new Vector3(0.02f, 0f, 0f));
            Press(target, leftHand, false);
            Press(target, rightHand, true);
            MoveIkTargets(target, selection, barMove);
            Require(Near(hand.guideObject.changeAmount.pos.x, leftBefore.x + 0.02f) && Near(otherHand.guideObject.changeAmount.pos.x, rightBefore.x + 0.02f), "a bar moves both hands the same way");
            hand.guideObject.changeAmount.pos = leftBefore;
            otherHand.guideObject.changeAmount.pos = rightBefore;

            // Middle click on a green button: the rotation override of the whole selection is removed.
            Press(target, leftToes, false);
            Press(target, rightToes, true);
            Require(Quaternion.Angle(Kkpe.Rotation(target, toesLeft), leftStart) > 5f, "the toes are still turned before the reset");
            ResetRotation(target, rightToes);
            Require(!overrides.Contains(toesLeft.gameObject) && !overrides.Contains(toesRight.gameObject), "middle click removes the rotation of both selected bones");
            Kkpe.SetRotation(target, toesLeft, leftStart * Quaternion.Euler(15f, 0f, 0f));
            Kkpe.SetRotation(target, toesRight, rightStart * Quaternion.Euler(15f, 0f, 0f));
            Press(target, leftHand, false);
            ResetRotation(target, leftToes);
            Require(!overrides.Contains(toesLeft.gameObject) && overrides.Contains(toesRight.gameObject), "outside the selection only the clicked bone is reset");
            Kkpe.ResetBone(target, toesRight, false, true, false);

            Quaternion own = WorldTurn(Quaternion.Euler(0f, 90f, 0f), Quaternion.identity, new Vector3(30f, 0f, 0f));
            Require(Quaternion.Angle(own, Quaternion.AngleAxis(30f, Vector3.forward * -1f)) < 0.5f || Quaternion.Angle(own, Quaternion.AngleAxis(30f, Vector3.forward)) < 0.5f, "a turn around an own axis becomes a world turn");
            foreach (string method in new string[] { "CopyLimbToTwin", "CopyHandToTwin", "SwapPose" })
            {
                Require(target.Pose.GetType().GetMethod(method) != null, "KKPE has " + method);
            }

            Require(Kkpe.CallPose(target, "CopyHandToTwin", GroupRightHand), "KKPE accepted a hand copy");
            int bodyShapeCount = 0;
            foreach (BlendMesh mesh in Kkpe.Meshes(target))
            {
                if (mesh.Renderer.name == BodyRenderer)
                {
                    bodyShapeCount = mesh.Renderer.sharedMesh.blendShapeCount;
                }
            }

            return bones + " bones and " + ik + " IK targets found (Body = cf_t_hips(work)); kokan between the knees and both shoulder joints are small rotation-only buttons, no buttons overlap; bone edits stored and reset through KKPE; Ctrl multi-selection keeps IK and bones apart; "
                + "button drags move IK targets on the floor plane / in height and turn bones, every selected part the same way (no mirroring); "
                + "middle click resets bone rotation; "
                + "limb copy functions present; " + BodyRenderer + " has " + bodyShapeCount + " Blend Shapes on the fixture";
        }

        // The Pad's own scroll bar, and a press on the window claiming the pointer from the GUI event.
        string CheckScrolling()
        {
            Rect bar = new Rect(0f, 0f, PadUi.Px(PadUi.ScrollBarWidth), 200f);
            float content = 600f;
            float value = PadUi.ScrollAfterPress(bar, 0f, content, 195f);
            Require(value > 350f && value <= 400f, "a press near the bottom of the track scrolls near the end: " + value);
            Require(Mathf.Approximately(PadUi.ScrollAfterPress(bar, value, content, 190f), value), "a press on the thumb does not jump");
            float back = PadUi.ScrollAfterDrag(bar, value, content, -100f);
            Require(back < value - 100f && back >= 0f, "dragging up scrolls back: " + value + " -> " + back);
            Require(PadUi.ScrollAfterDrag(bar, back, content, -10000f) == 0f && PadUi.ScrollAfterDrag(bar, back, content, 10000f) == 400f, "scrolling stops at both ends");

            bool wasVisible = Visible;
            Visible = true;
            SetPointerOwnershipForTest(false);
            Event press = new Event();
            press.type = EventType.MouseDown;
            press.mousePosition = window.center;
            ClaimPress(press);
            bool claimed = OwnsPointer;
            SetPointerOwnershipForTest(false);
            press.mousePosition = new Vector2(window.xMax + 50f, window.yMax + 50f);
            ClaimPress(press);
            bool outside = pressBeganOnWindow;
            SetPointerOwnershipForTest(false);
            Visible = wasVisible;
            Require(claimed && !outside, "a press on the window, and only there, claims the pointer for Studio's camera block");

            // The reported failure: right after a layout pass the working rectangle is 10 x 10.
            Rect place = new Rect(200f, 150f, 600f, 400f);
            PlaceForTest(place);
            Visible = true;
            ShrinkForTest();
            SetPointerOwnershipForTest(false);
            press.mousePosition = new Vector2(place.x + 300f, place.y + 9f);   // on the header, as in the log
            ClaimPress(press);
            bool headerClaimed = pressBeganOnWindow;
            SetPointerOwnershipForTest(false);
            bool covered = Covers(new Vector2(place.x + 300f, Screen.height - (place.y + 9f)));
            Visible = wasVisible;
            Require(headerClaimed && covered, "a press on the header counts as on the window even while the layout rectangle is shrunk");
            return "own scroll bar: a press jumps, a drag follows the pointer, both ends stop; a press on the window (header included) blocks the camera even while the layout rectangle is shrunk";
        }

        static BodyPart Part(string label)
        {
            foreach (BodyPart part in BodyParts)
            {
                if (part.Label == label)
                {
                    return part;
                }
            }

            throw new InvalidOperationException("no body part " + label);
        }

        // Tabs are toggles shown side by side in a fixed order and remembered in the settings.
        string CheckTabs()
        {
            bool[] before = (bool[])tabs.Clone();
            try
            {
                Require(TabNames[0] == "Map" && TabNames[1] == "Face" && TabNames[2] == "Body" && TabNames[3] == "Shapes", "tab order Map, Face, Body, Shapes");
                Require(ItemTabNames[0] == "Move" && ItemTabNames[1] == "Shapes" && (string)host.OpenItemTabs.DefaultValue == "Move", "item tabs Move, Shapes; Move open by default");
                for (int i = 0; i < tabs.Length; i++)
                {
                    tabs[i] = i == (int)Tab.Map || i == (int)Tab.Body;
                }

                string remembered = OpenTabNames(TabNames, tabs);
                Require(remembered == "Map,Body", "open tabs remembered: " + remembered);
                for (int i = 0; i < tabs.Length; i++)
                {
                    tabs[i] = false;
                }

                ReadOpenTabs(remembered, TabNames, tabs);
                Require(tabs[(int)Tab.Map] && tabs[(int)Tab.Body] && !tabs[(int)Tab.Face] && !tabs[(int)Tab.Shapes], "open tabs read back");

                // Sizes: the whole window 5 % smaller than at 1.1, the Body tab another 10 % narrower,
                // every Blend Shape bar as long as the Face tab's, the map a little less tall.
                Require(Mathf.Approximately((float)host.UiScale.DefaultValue, 1.045f), "UiScale default 1.045 (1.1 - 5 %)");
                Require(Mathf.Approximately(FigureWidth, 306f) && Mathf.Approximately(AxisBarWidth, 89f) && FunctionWidth * 4f + 8f <= FigureWidth
                    && TitleWidth + 3f * (AxisBarWidth + 2f) <= FigureWidth + 4f, "the Body tab is 10 % narrower (306) and its rows fit");
                Require(ShapeBarWidth + 8f + 3f <= ColumnWidth, "a Face bar with its pin fits the column");
                Require(MapHeight < 440f && MapHeight > MapWidth, "the map is a little less tall (" + MapHeight + " x " + MapWidth + ")");
                Require(2f * (LowerBarWidth + 2f) + LowerButtonWidth + 2f + StripSize <= ContentWidth, "two bars and a button fit below the map");
                return "Map, Face, Body, Shapes (and Move, Shapes for items) as toggles, open tabs side by side and remembered; "
                    + "UiScale 1.045, Body 306 wide, shape bars " + ShapeBarWidth + " everywhere, map " + MapWidth + " x " + MapHeight;
            }
            finally
            {
                Array.Copy(before, tabs, tabs.Length);
            }
        }

        // Neck and shoulder joints turn only the way the body bends when dragged on the figure.
        string CheckDragLimits(PadTarget target)
        {
            Require(Part("Neck").DragAxes == new Vector3(1f, 0f, 1f) && Part("R.Sho").DragAxes == Vector3.up && Part("L.Sho").DragAxes == Vector3.up
                && Part("Head").DragAxes == Vector3.one, "Neck X / Z, shoulder joints Y, the head free");

            // Through X = 90 degrees Unity reports other Euler angles for the same turn; Y stays kept.
            Quaternion kept = OnlyAxes(Quaternion.Euler(85f, 10f, 0f), Quaternion.Euler(85f, 10f, 0f) * Quaternion.Euler(10f, 0f, 0f), new Vector3(1f, 0f, 1f));
            Require(Mathf.Abs(Mathf.DeltaAngle(kept.eulerAngles.y, 10f)) < 0.5f || Mathf.Abs(Mathf.DeltaAngle(kept.eulerAngles.y, 190f)) < 0.5f, "a turn through X = 90 keeps Y: " + kept.eulerAngles);

            string result = "";
            foreach (string label in new string[] { "Neck", "R.Sho" })
            {
                BodyPart part = Part(label);
                Transform bone = target.Bone(part.Name);
                Quaternion start = Kkpe.Rotation(target, bone);
                selection.Clear();
                Press(target, part, false);
                DragSelection(target, part, new Vector2(40f, 25f), false);
                DragSelection(target, part, new Vector2(-15f, 30f), true);
                Vector3 a = start.eulerAngles, b = Kkpe.Rotation(target, bone).eulerAngles;
                Vector3 change = new Vector3(Mathf.DeltaAngle(a.x, b.x), Mathf.DeltaAngle(a.y, b.y), Mathf.DeltaAngle(a.z, b.z));
                for (int axis = 0; axis < 3; axis++)
                {
                    bool free = part.DragAxes[axis] > 0f;
                    Require(free || Mathf.Abs(change[axis]) < 0.05f, label + " did not turn on " + AxisNames[axis] + ": " + change);
                }

                Require(change.magnitude > 1f, label + " turned: " + change);

                // The bars keep every axis.
                TurnBones(target, selection, WorldTurn(bone.parent.rotation, Kkpe.Rotation(target, bone), new Vector3(5f, 5f, 5f)), false);
                Vector3 c = Kkpe.Rotation(target, bone).eulerAngles;
                Require(Mathf.Abs(Mathf.DeltaAngle(b.x, c.x)) + Mathf.Abs(Mathf.DeltaAngle(b.y, c.y)) + Mathf.Abs(Mathf.DeltaAngle(b.z, c.z)) > 3f, label + " bars turn freely");
                Kkpe.ResetBone(target, bone, false, true, false);
                result += label + " drag " + change.ToString("0.0") + "; ";
            }

            selection.Clear();
            return result + "the bars below the figure keep all three axes";
        }

        // Studio's Undo for the Body tab: one step per press, whatever it changed.
        string CheckPoseUndo(PadTarget target)
        {
            UndoRedoManager undo = Singleton<UndoRedoManager>.Instance;
            BodyPart neck = Part("Neck"), hand = Part("L.Hand");
            Transform bone = target.Bone(neck.Name);
            GuideObject handGuide = target.IkTarget(hand.Name).guideObject;
            Quaternion neckStart = Kkpe.Rotation(target, bone);
            Vector3 handStart = handGuide.changeAmount.pos;
            try
            {
                // A drag on the figure and an IK move during one press: one step.
                int depth = UndoStack.Depth();
                RememberPose(target);
                selection.Clear();
                Press(target, neck, false);
                DragSelection(target, neck, new Vector2(30f, 12f), false);
                MoveIkTargets(target, new List<BodyPart> { hand }, new Vector3(0.05f, 0f, 0f));
                CommitPoseUndo();
                Quaternion neckEdited = Kkpe.Rotation(target, bone);
                Vector3 handEdited = handGuide.changeAmount.pos;
                Require(Quaternion.Angle(neckEdited, neckStart) > 1f && handEdited != handStart, "the press changed the neck and the hand");
                Require(UndoStack.Depth() == depth + 1, "one press is one step (" + (UndoStack.Depth() - depth) + ")");
                undo.Undo();
                Require(Quaternion.Angle(Kkpe.Rotation(target, bone), neckStart) < 0.01f && handGuide.changeAmount.pos == handStart, "Undo puts the neck and the hand back");
                undo.Redo();
                Require(Quaternion.Angle(Kkpe.Rotation(target, bone), neckEdited) < 0.01f && handGuide.changeAmount.pos == handEdited, "Redo makes the edit again");

                // Reset bone removes the override; Undo brings it back.
                RememberPose(target);
                ResetSelectedBones(target);
                CommitPoseUndo();
                Require(Quaternion.Angle(Kkpe.Rotation(target, bone), neckEdited) > 1f, "Reset bone reset the neck");
                undo.Undo();
                Require(Quaternion.Angle(Kkpe.Rotation(target, bone), neckEdited) < 0.01f, "Undo of Reset bone brings the neck's rotation back");

                // A press that changes nothing leaves no step.
                depth = UndoStack.Depth();
                RememberPose(target);
                CommitPoseUndo();
                Require(UndoStack.Depth() == depth, "no change, no step");

                // KKPE's own step (Swap pose, limb copies) pushed during the press is taken off again.
                Type kkpeStep = Reflect.Type("HSPE.Commands.MoveRotateEqualsCommand");
                Require(kkpeStep != null, "KKPE's step type found");
                Array none = Array.CreateInstance(typeof(GuideCommand.EqualsInfo), 0);
                RememberPose(target);
                undo.Push((ICommand)Activator.CreateInstance(kkpeStep, new object[] { none, none }));
                MoveIkTargets(target, new List<BodyPart> { hand }, new Vector3(0f, 0.03f, 0f));
                CommitPoseUndo();
                Require(UndoStack.Depth() == depth + 1, "KKPE's step was merged into the press's step");
                undo.Undo();
                Require(handGuide.changeAmount.pos == handEdited, "one Undo takes the whole press back");

                // The step is made a few frames after the release: KKPE does Swap pose in its next IK pass.
                RememberPose(target);
                MoveIkTargets(target, new List<BodyPart> { hand }, new Vector3(0f, 0.03f, 0f));
                TickBody(true);
                TickBody(false);
                Require(poseBefore != null, "not yet in the frame of the release");
                poseReleasedFrame = Time.frameCount - PoseSettleFrames;
                TickBody(false);
                Require(poseBefore == null && UndoStack.Depth() == depth + 1, "made " + PoseSettleFrames + " frames after the release");
                undo.Undo();
                return "a drag and an IK move in one press are one step; Undo / Redo; Undo of Reset bone; no step without a change; KKPE's own step merged; the step waits " + PoseSettleFrames + " frames after the release";
            }
            finally
            {
                poseBefore = null;
                Kkpe.ResetBone(target, bone, false, true, false);
                handGuide.changeAmount.pos = handStart;
                selection.Clear();
            }
        }

        // Drags go on beyond the screen edge: the cursor is held and the mouse's own movement used.
        // (The test game runs in the background, so the cursor itself is not parked here; the
        // parking was checked with real mouse input in the test game.)
        string CheckEndlessDrag(PadTarget target)
        {
            BodyPart hand = Part("L.Hand");
            GuideObject handGuide = target.IkTarget(hand.Name).guideObject;
            Vector3 handStart = handGuide.changeAmount.pos;
            try
            {
                Require(EndlessDrag.Moved(Vector2.zero) == Vector2.zero && !EndlessDrag.Holding, "no movement: the cursor is not held");
                Require(EndlessDrag.Moved(new Vector2(4f, 0f)) == new Vector2(4f, 0f) && EndlessDrag.Holding, "the first movement is the event's own and starts holding the cursor");
                Vector2 far = new Vector2(Screen.width * 3f, -50f);
                EndlessDrag.AddMovementForTest(far);
                Require(EndlessDrag.Moved(new Vector2(-1f, 0f)) == far, "movement three screens wide is passed on; the event's own (pinned) movement no longer counts");
                Require(EndlessDrag.Moved(new Vector2(7f, 0f)) == Vector2.zero, "each movement is passed on once");

                // A Body drag with that much movement: the hand goes as far as the movement says.
                selection.Clear();
                Press(target, hand, false);
                EndlessDrag.AddMovementForTest(new Vector2(Screen.width * 2f, 0f));
                Vector2 pixels = EndlessDrag.Moved(Vector2.zero) * PadUi.Sensitivity;
                DragSelection(target, hand, pixels, false);
                float moved = (handGuide.transformTarget.parent.TransformVector(handGuide.changeAmount.pos - handStart)).magnitude;
                float expected = pixels.x * DragMovePerPixel;
                Require(Mathf.Abs(moved - expected) < expected * 0.02f + 0.0001f, "a drag of two screen widths moved the hand " + moved.ToString("0.000") + " (" + expected.ToString("0.000") + " expected)");

                EndlessDrag.End();
                Require(!EndlessDrag.Holding && EndlessDrag.Moved(new Vector2(3f, 0f)) == new Vector2(3f, 0f), "after the release events count again");
                EndlessDrag.End();

                // A release that no control saw: the next Update lets go (no button is held in the test).
                EndlessDrag.Moved(new Vector2(2f, 0f));
                EndlessDrag.Update();
                Require(!EndlessDrag.Holding, "Update lets go once no mouse button is held");

                // Speed: pixels per axis unit measured from free cursor movement.
                EndlessDrag.ForgetMeasurementForTest();
                Require(Near(EndlessDrag.PixelsPerAxisUnit(), 20f), "20 pixels per axis unit before anything is measured");
                for (int i = 0; i < 30; i++)
                {
                    EndlessDrag.AddMeasurement(120f, 3f);   // an accelerated cursor: 40 pixels per unit
                }

                float gain = EndlessDrag.PixelsPerAxisUnit();
                EndlessDrag.AddMeasurement(0.5f, 3f);   // too small to count
                Require(Near(gain, 40f) && Near(EndlessDrag.PixelsPerAxisUnit(), 40f), "the measured speed is used: " + gain);
                for (int i = 0; i < 400; i++)
                {
                    EndlessDrag.AddMeasurement(30f, 3f);   // the user slowed the pointer down: 10 per unit
                }

                float later = EndlessDrag.PixelsPerAxisUnit();
                Require(later < 12f && later >= 10f, "older movement fades: " + later);
                EndlessDrag.ForgetMeasurementForTest();
                return "the first movement holds the cursor, movement beyond the screen is passed on once, a Body drag of two screen widths moved the hand " + moved.ToString("0.000") + "; release and a missed release both let go; held drags run at the measured cursor speed (20 px per axis unit until measured, 40 measured, fading to " + later.ToString("0.0") + ")";
            }
            finally
            {
                EndlessDrag.End();
                handGuide.changeAmount.pos = handStart;
                selection.Clear();
            }
        }

        // The controls outside the window's top corners: play / pause and the time drag (top left),
        // the resize knob (top right).
        string CheckCorners()
        {
            Rect windowBefore = window, drawnBefore = drawn;
            bool visibleBefore = Visible;
            float scaleBefore = host.UiScale.Value;
            float playheadBefore = ShowsTimeButtons ? TimelineBridge.PlaybackTime : 0f;   // later checks key at this time
            try
            {
                Require(ShowsTimeButtons, "Timeline's playback is found without the GIN helper's toolbar buttons");
                Visible = true;
                PlaceForTest(new Rect(400f, 200f, 500f, 420f));
                Rect play = PlayRect(), time = TimeDragRect(), knob = KnobRect();
                Require(play.xMax <= drawn.x && play.y == drawn.y && time.x == play.x && time.y > play.yMax, "play above time, outside the top left corner: " + play + " " + time);
                Require(knob.x >= drawn.xMax && knob.y == drawn.y, "the knob outside the top right corner: " + knob);
                Rect disc = KnobDrawnRect();
                Require(disc.position == knob.position && Mathf.Abs(disc.width * 2f - knob.width) <= 1f, "the knob is drawn half as large as the area that takes the mouse: " + disc + " / " + knob);
                Require(OnWindow(play.center) && OnWindow(time.center) && OnWindow(knob.center) && !OnWindow(new Vector2(play.x - 3f, play.center.y)) && !OnWindow(new Vector2(knob.xMax + 3f, knob.center.y)),
                    "the corner controls count as the window, just beside them does not");
                Require(Covers(new Vector2(knob.center.x, Screen.height - knob.center.y)) && Covers(new Vector2(time.center.x, Screen.height - time.center.y)),
                    "Studio's camera, gizmo and buttons do not get presses on them");

                // Play / pause.
                TimelineBridge.Pause();
                TimelineBridge.TogglePlay();
                bool started = TimelineBridge.IsPlaying;
                TimelineBridge.TogglePlay();
                Require(started && !TimelineBridge.IsPlaying, "the play button plays and pauses");

                // The time drag and its readout.
                TimelineBridge.Seek(1f);
                TimelineBridge.TogglePlay();
                TimeDrag(PadUi.Press.Down, Vector2.zero);
                Require(!TimelineBridge.IsPlaying && timeDragging && ReadoutShown, "a press pauses playback and shows the readout");
                float step = Mathf.Max(MinSecondsPerPixel, TimelineBridge.Duration * SecondsPerPixel);
                float start = TimelineBridge.PlaybackTime;
                TimeDrag(PadUi.Press.Dragged, new Vector2(60f, 0f));
                float moved = TimelineBridge.PlaybackTime - start;
                Require(Mathf.Abs(moved - 60f * step) < 0.002f && TimeChangeText() == "+" + moved.ToString("0.00") + " s", "60 pixels move the playhead " + moved.ToString("0.000") + " s, the readout says " + TimeChangeText());
                TimeDrag(PadUi.Press.Dragged, new Vector2(-100000f, 0f));
                Require(TimelineBridge.PlaybackTime == 0f && TimeChangeText().StartsWith("-", StringComparison.Ordinal), "the playhead stops at 0 and the readout shows the way back: " + TimeChangeText());
                TimeDrag(PadUi.Press.Released, Vector2.zero);
                Require(!timeDragging && ReadoutShown, "after the release the readout stays a moment");

                // The resize knob: the right edge follows the pointer, every size grows by the same factor.
                drawn = new Rect(400f, 200f, 500f, 420f);
                BeginResize(drawn.xMax);
                ResizeTo(drawn.xMax + 125f);
                Require(Near(resizeScale, scaleBefore * 1.25f) && Near(CurrentScale, resizeScale), "a quarter wider is a quarter larger: " + resizeScale);
                ResizeTo(-100000f);
                Require(Near(resizeScale, Plugin.MinUiScale), "never smaller than " + Plugin.MinUiScale);
                ResizeTo(100000f);
                Require(Near(resizeScale, Plugin.MaxUiScale), "never larger than " + Plugin.MaxUiScale);
                ResizeTo(drawn.xMax - 100f);
                float chosen = resizeScale;
                EndResize();
                Require(resizeScale < 0f && Mathf.Abs(host.UiScale.Value - chosen) < 0.001f, "the size is remembered in the setting UiScale: " + host.UiScale.Value);

                // Kept on the screen with room for the corner controls.
                drawn = new Rect(-300f, 50f, 500f, 420f);
                window = drawn;
                KeepOnScreen();
                Require(window.x >= PadUi.Px(CornerButtonWidth + CornerGap) - 0.01f, "room for the time buttons on the left: " + window.x);
                window.x = Screen.width + 100f;
                KeepOnScreen();
                Require(window.x + drawn.width + PadUi.Px(KnobGrabSize + CornerGap) <= Screen.width + 0.01f, "room for the knob on the right: " + window.x);
                return "play / time stacked outside the top left corner, the knob (7 drawn, 14 to grab) outside the top right, both guarded like the window; play / pause; 60 px moved the playhead " + moved.ToString("0.00")
                    + " s with the readout; the knob scales the whole window (" + Plugin.MinUiScale + " .. " + Plugin.MaxUiScale + ") and the size is remembered; the window stays on screen with room for both";
            }
            finally
            {
                host.UiScale.Value = scaleBefore;
                resizeScale = -1f;
                timeDragging = false;
                readoutUntil = -1f;
                window = windowBefore;
                drawn = drawnBefore;
                Visible = visibleBefore;
                if (ShowsTimeButtons)
                {
                    TimelineBridge.Pause();
                    TimelineBridge.Seek(playheadBefore);
                }
            }
        }

        // Shift + drag on the Map: one direction only, no jump on press, relative until release.
        string CheckAxisLock()
        {
            AxisLock locked = new AxisLock();
            Event e = new Event();
            Vector2 marker = new Vector2(50f, 60f);
            e.shift = true;
            e.mousePosition = new Vector2(100f, 100f);
            Require(locked.Pointer(e, marker) == marker, "Shift + press keeps the marker where it is");
            e.mousePosition = new Vector2(130f, 110f);
            Require(locked.Pointer(e, marker) == new Vector2(80f, 60f), "mostly sideways: only horizontal");
            e.mousePosition = new Vector2(105f, 140f);
            Require(locked.Pointer(e, new Vector2(0f, 0f)) == new Vector2(50f, 100f), "mostly down: only vertical");
            e.shift = false;
            e.mousePosition = new Vector2(120f, 130f);
            Require(locked.Pointer(e, marker) == new Vector2(70f, 90f), "Shift released: free, still relative");
            locked.Release();
            Require(locked.Pointer(e, marker) == e.mousePosition, "after the release the pointer is used as it is");

            // On the map: a Shift drag changes only the mouth column.
            Vector2 before = mapPoint;
            Rect pad = new Rect(0f, 0f, 270f, 190f);
            mapPoint = new Vector2(3f, 5f);
            AxisLock onMap = new AxisLock();
            e.shift = true;
            e.mousePosition = new Vector2(10f, 10f);
            MoveMapPoint(pad, onMap.Pointer(e, MarkerOnScreen(pad)), false);
            e.mousePosition = new Vector2(70f, 25f);
            MoveMapPoint(pad, onMap.Pointer(e, MarkerOnScreen(pad)), false);
            bool horizontal = mapPoint.x > 3.5f && Near(mapPoint.y, 5f);
            mapPoint = before;
            mouthDirty = eyesDirty = false;
            Require(horizontal, "a Shift drag on the map moves only through the mouths");
            return "Shift + drag follows the larger direction only, the press does not jump, letting go of Shift frees the other direction";
        }

        // Middle click requests and the Body's Key now (needs GIN Timeline life hack and Char Setup rows).
        string CheckKeying(PadTarget target)
        {
            PadUi.RequestKey();
            Require(PadUi.TakeKeyRequest() && !PadUi.TakeKeyRequest(), "a middle click request is taken once");
            selection.Clear();
            Press(target, Part("L.Hand"), false);
            Press(target, Part("Neck"), true);   // a bone replaces the IK selection
            AdoptBodySelection(target);
            KeyBodySelection(target);
            string status = message;
            selection.Clear();
            AdoptBodySelection(target);
            Require(status.StartsWith("Keyed:", StringComparison.Ordinal), "Key now keyed the neck: " + status);
            return "middle click requests are taken once; Body Key now: " + status;
        }

        // Item mode with a Studio item that has FK nodes: the Move tab's moves, turns, undo and the
        // wheel's FK stepping, and the Shapes tab when the item has Blend Shapes.
        string CheckItems(PadTarget character)
        {
            Require(ItemSelectionNow() == null, "a character alone is not item mode");
            int group, category, number;
            Require(FindFkItem(out group, out category, out number), "the test game has an item with at least 4 FK nodes");
            OCIItem item = AddObjectItem.Add(group, category, number);
            Require(item != null, "item added");
            TreeNodeCtrl tree = Singleton<Studio.Studio>.Instance.treeNodeCtrl;
            try
            {
                tree.SelectSingle(item.treeNodeObject, false);
                ItemSelection items = ItemSelectionNow();
                Require(items != null && items.Roots.Count == 1 && items.Roots[0] == item, "a selected item switches to item mode");

                // Move square: left = floor plane, right = height. Each drag is one Undo step.
                Transform moved = item.guideObject.transformTarget;
                Vector3 start = moved.position, startRotation = item.guideObject.changeAmount.rot;
                MoveSquare(items, PadUi.Press.Down, Vector2.zero, 0);
                MoveSquare(items, PadUi.Press.Dragged, new Vector2(30f, -20f), 0);
                CommitUndo();
                Vector3 afterFloor = moved.position;
                Require((afterFloor - start).magnitude > 0.001f && Mathf.Abs(afterFloor.y - start.y) < 0.0001f, "left drag moves on the floor plane: " + (afterFloor - start));
                MoveSquare(items, PadUi.Press.Down, Vector2.zero, 1);
                MoveSquare(items, PadUi.Press.Dragged, new Vector2(30f, -20f), 1);
                CommitUndo();
                Vector3 lifted = moved.position - afterFloor;
                Require(lifted.y > 0.001f && Mathf.Abs(lifted.x) < 0.0001f && Mathf.Abs(lifted.z) < 0.0001f, "right drag moves only up: " + lifted);

                // Turns: a click on View Y is 90 degrees around the world's up axis; Own X -90 around its own X.
                Quaternion beforeTurn = moved.rotation;
                TurnInView(items, 1, PadUi.Press.Clicked, Vector2.zero, 0);
                CommitUndo();
                Require(Mathf.Abs(Quaternion.Angle(beforeTurn, moved.rotation) - 90f) < 0.5f && Vector3.Angle(moved.up, beforeTurn * Vector3.up) < 0.5f, "View Y click turns 90 degrees around up");
                Vector3 ownBefore = item.guideObject.changeAmount.rot;
                TurnOwnAxis(items, 0, PadUi.Press.Clicked, Vector2.zero, 1);
                CommitUndo();
                Require(Quaternion.Angle(Quaternion.Euler(item.guideObject.changeAmount.rot), Quaternion.Euler(ownBefore) * Quaternion.Euler(-90f, 0f, 0f)) < 0.5f, "Own X right click turns -90 degrees on the item's own X");

                // Studio's Undo takes the four steps back.
                for (int i = 0; i < 4; i++)
                {
                    Singleton<UndoRedoManager>.Instance.Undo();
                }

                Require((moved.position - start).magnitude < 0.0001f && Quaternion.Angle(Quaternion.Euler(item.guideObject.changeAmount.rot), Quaternion.Euler(startRotation)) < 0.5f, "Undo restores position and rotation");

                // FK stepping with the wheel.
                item.ActiveFK(true);
                List<OCIChar.BoneInfo> bones = item.listBones;
                Singleton<GuideObjectManager>.Instance.selectObject = bones[0].guideObject;
                fkWalk.Sync(items.Objects);
                Require(fkWalk.HasBone && fkWalk.ActiveName == bones[0].guideObject.transformTarget.name, "the selected FK node is found");
                fkWalk.Step(1, false, false);
                fkWalk.Sync(items.Objects);
                Require(Singleton<GuideObjectManager>.Instance.selectObject == bones[1].guideObject && fkWalk.Count == 1, "the wheel selects the next node");
                fkWalk.Step(1, true, false);
                Require(fkWalk.Count == 2 && bones[2].guideObject.isActive, "Shift + wheel grows a range");
                fkWalk.Step(1, false, false);
                List<OCIChar.BoneInfo> range = fkWalk.Bones();
                Require(range.Count == 2 && range[0] == bones[2] && range[1] == bones[3] && !bones[1].guideObject.isActive, "the wheel slides the range along the chain");
                Vector3 untouched = bones[1].guideObject.changeAmount.rot;
                Quaternion second = Quaternion.Euler(bones[2].guideObject.changeAmount.rot), third = Quaternion.Euler(bones[3].guideObject.changeAmount.rot);
                TurnOwnAxis(items, 2, PadUi.Press.Clicked, Vector2.zero, 0);
                CommitUndo();
                Require(Quaternion.Angle(Quaternion.Euler(bones[2].guideObject.changeAmount.rot), second * Quaternion.Euler(0f, 0f, 90f)) < 0.5f
                    && Quaternion.Angle(Quaternion.Euler(bones[3].guideObject.changeAmount.rot), third * Quaternion.Euler(0f, 0f, 90f)) < 0.5f
                    && bones[1].guideObject.changeAmount.rot == untouched, "Own Z turns the range's nodes (and only them) 90 degrees on their own Z");
                ResetFk();
                Require(bones[2].guideObject.changeAmount.rot == Vector3.zero && bones[3].guideObject.changeAmount.rot == Vector3.zero, "Reset FK zeroes the range");
                fkWalk.Step(-1, false, true);
                fkWalk.Sync(items.Objects);
                Require(fkWalk.Count == 1 && Singleton<GuideObjectManager>.Instance.selectObject == bones[1].guideObject, "Alt + wheel selects a single node again");

                // Several objects: the character joins the item; item mode stays.
                tree.SelectMultiple(item.treeNodeObject, character.Character.treeNodeObject);
                ItemSelection both = ItemSelectionNow();
                Require(both != null && both.Objects.Contains(item) && both.Objects.Contains(character.Character), "item and character together are item mode (" + (both == null ? 0 : both.Objects.Count) + " objects)");

                string shapes = items.Shapes == null ? "this item has no Blend Shapes, so no Shapes tab" : "Shapes tab offered (" + Kkpe.Meshes(items.Shapes).Count + " renderer(s))";
                return "item " + group + "/" + category + "/" + number + " with " + bones.Count + " FK nodes: floor and height moves, View Y and Own X clicks, four Undo steps; "
                    + "wheel: next node, Shift range, slide, Alt single; Own Z and Reset FK on the range; with a character also selected item mode stays; " + shapes;
            }
            finally
            {
                tree.DeleteNode(item.treeNodeObject);
                tree.SelectSingle(character.Character.treeNodeObject, false);
                ItemSelection.Forget();
                fkWalk.Sync(new List<ObjectCtrlInfo>());
            }
        }

        OCIItem shapesTestItem;
        OCIChar shapesTestCharacter;
        ShapeRow shapesTestRow;

        internal string AddItemWithShapes()
        {
            int group, category, number;
            if (!FindItem(ShapesTestBundle, 0, out group, out category, out number))
            {
                return "SKIP Pad item Shapes: no GIN Puffy Studio item in this test game";
            }

            PadTarget character = Kkpe.Target();
            shapesTestCharacter = character == null ? null : character.Character;
            shapesTestItem = AddObjectItem.Add(group, category, number);
            Singleton<Studio.Studio>.Instance.treeNodeCtrl.SelectSingle(shapesTestItem.treeNodeObject, false);
            return "PASS Pad item Shapes: GIN Puffy item " + group + "/" + category + "/" + number + " added and selected; KKPE attaches in the next frames";
        }

        internal string CheckItemShapes()
        {
            if (shapesTestItem == null)
            {
                return "SKIP Pad item Shapes: no item was added";
            }

            try
            {
                ItemSelection items = ItemSelectionNow();
                Require(items != null && items.Shapes != null && items.TabsUsable[(int)ItemTab.Shapes], "the Shapes tab is offered for an item with Blend Shapes");
                Require(items.Shapes.Owner == shapesTestItem && items.Shapes.Character == null, "the Shapes tab edits the item itself");
                ReadItemShapes(items.Shapes);
                int total = 0;
                foreach (BlendMesh mesh in Kkpe.Meshes(items.Shapes))
                {
                    total += mesh.Renderer.sharedMesh.blendShapeCount;
                }

                Require(itemShapes.Count == total && total > 0, "one bar per Blend Shape (" + itemShapes.Count + " of " + total + ")");
                Require(!Kkpe.IsSwitchedOn(items.Shapes) && itemShapesOff, "KKPE's editor starts off on a new item, and the tab says so");
                ShapeRow first = itemShapes[0];
                SetItemShape(items.Shapes, first, 42f);
                Require(Kkpe.IsSwitchedOn(items.Shapes), "the first edit switched KKPE's editor on");
                Require(Near(Kkpe.Weight(first.Mesh, first.Index), 42f), "a bar's weight is stored by KKPE");
                shapesTestRow = first;
                string key = TimelineBridge.KeyNamed(items.Shapes.Owner, new string[] { first.Mesh.Renderer.name });
                return "PASS Pad item Shapes: " + itemShapes.Count + " bars for " + Kkpe.Meshes(items.Shapes).Count + " renderer(s) of the GIN Puffy item; KKPE was off and the first edit switched it on (" + first.Label + " = 42); middle click asks Timeline: " + key;
            }
            catch (Exception error)
            {
                RemoveShapesTestItem();
                return "FAIL Pad item Shapes: " + Root(error).Message;
            }
        }

        // A few frames later: KKPE's LateUpdate has put the weight on the mesh.
        internal string CheckItemShapeShown()
        {
            if (shapesTestItem == null || shapesTestRow == null)
            {
                return "SKIP Pad item Shapes shown: no edited item";
            }

            try
            {
                float shown = shapesTestRow.Mesh.Renderer.GetBlendShapeWeight(shapesTestRow.Index);
                Kkpe.SetWeight(shapesTestRow.Mesh, shapesTestRow.Name, 0f);
                return (Mathf.Abs(shown - 42f) < 0.01f ? "PASS" : "FAIL") + " Pad item Shapes shown: the mesh shows " + shown.ToString("0.##") + " for " + shapesTestRow.Label + " (42 set through the Pad)";
            }
            catch (Exception error)
            {
                return "FAIL Pad item Shapes shown: " + Root(error).Message;
            }
            finally
            {
                RemoveShapesTestItem();
            }
        }

        void RemoveShapesTestItem()
        {
            TreeNodeCtrl tree = Singleton<Studio.Studio>.Instance.treeNodeCtrl;
            if (shapesTestItem != null)
            {
                tree.DeleteNode(shapesTestItem.treeNodeObject);
            }

            shapesTestItem = null;
            shapesTestRow = null;
            if (shapesTestCharacter != null)
            {
                tree.SelectSingle(shapesTestCharacter.treeNodeObject, false);
            }

            ItemSelection.Forget();
        }

        static ItemSelection ItemSelectionNow()
        {
            ItemSelection.Forget();
            return ItemSelection.Current();
        }

        const string ShapesTestBundle = "gin_puffystudioitems";   // GIN Puffy Studio items have Blend Shapes

        // The first item of the game's lists that has at least four FK nodes.
        static bool FindFkItem(out int group, out int category, out int number)
        {
            return FindItem(null, 4, out group, out category, out number);
        }

        // The first item whose asset bundle path contains "bundle" (any, when null) and that has
        // at least "fkNodes" FK nodes.
        static bool FindItem(string bundle, int fkNodes, out int group, out int category, out int number)
        {
            foreach (KeyValuePair<int, Dictionary<int, Dictionary<int, Info.ItemLoadInfo>>> g in Singleton<Info>.Instance.dicItemLoadInfo)
            {
                foreach (KeyValuePair<int, Dictionary<int, Info.ItemLoadInfo>> c in g.Value)
                {
                    foreach (KeyValuePair<int, Info.ItemLoadInfo> n in c.Value)
                    {
                        bool inBundle = bundle == null || (n.Value.bundlePath != null && n.Value.bundlePath.IndexOf(bundle, StringComparison.OrdinalIgnoreCase) >= 0);
                        if (inBundle && (fkNodes == 0 || (n.Value.bones != null && n.Value.bones.Count >= fkNodes)))
                        {
                            group = g.Key;
                            category = c.Key;
                            number = n.Key;
                            return true;
                        }
                    }
                }
            }

            group = category = number = -1;
            return false;
        }

        // 0: Face, 1: Map with the pose editor, 2: Map, Body and Shapes side by side,
        // 3: item mode (an FK item with its second node selected).
        internal string PrepareIsolatedPreview(int previewTab)
        {
            if (previewTab == 3)
            {
                int group, category, number;
                if (!FindItem(ShapesTestBundle, 2, out group, out category, out number) && !FindFkItem(out group, out category, out number))
                {
                    return "FAIL Pad preview: no FK item";
                }

                OCIItem item = AddObjectItem.Add(group, category, number);
                Singleton<Studio.Studio>.Instance.treeNodeCtrl.SelectSingle(item.treeNodeObject, false);
                item.ActiveFK(true);
                Singleton<GuideObjectManager>.Instance.selectObject = item.listBones[1].guideObject;
                itemTabs[(int)ItemTab.Move] = itemTabs[(int)ItemTab.Shapes] = true;
                window.x = 1180f;
                window.y = 120f;
                return "PASS Pad preview prepared: item mode";
            }

            for (int i = 0; i < tabs.Length; i++)
            {
                tabs[i] = previewTab == 0 ? i == (int)Tab.Face : previewTab == 1 ? i == (int)Tab.Map : i != (int)Tab.Face;
            }

            faceListStale = true;
            window.x = 1180f;
            window.y = 120f;
            making = null;
            if (previewTab == 1)
            {
                // The reported case: a face with many mouth shapes stored with a right click,
                // so that the pose editor's list needs its scroll bar.
                PadTarget target = Kkpe.Target();
                if (target != null)
                {
                    BlendMesh face = MeshWithRole(target, Kkpe.FaceRole);
                    int mouthStart = Kkpe.MouthStart(target);
                    for (int index = mouthStart; index < face.Renderer.sharedMesh.blendShapeCount && index < mouthStart + 36; index++)
                    {
                        Kkpe.SetWeight(face, face.Renderer.sharedMesh.GetBlendShapeName(index), 10f + index % 7 * 9f);
                    }
                }

                mapPreviewOnly = true;
                Request(MapEdit.StoreCurrentFace, FacePart.Mouth, 9);
            }

            if (previewTab == 2)
            {
                selection.Clear();
                selection.Add(Part("L.Bust1"));
                selection.Add(Part("R.Bust1"));   // a pair; R.Bust1 is the primary: rotation, move and scale bars

                // The fixture's body has no Blend Shapes: lay out the panel with the face's instead.
                PadTarget target = Kkpe.Target();
                if (target != null)
                {
                    bodyShapesRenderer = MeshWithRole(target, Kkpe.FaceRole).Renderer.name;
                    bodyShapesOwner = null;
                    window.x = 60f;   // three tabs make the window wide
                }
            }

            return "PASS Pad preview prepared: " + (previewTab == 0 ? "Face" : previewTab == 1 ? "Map" : "Map + Body + Shapes");
        }
    }
}
