using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml;
using Studio;
using Timeline;
using UnityEngine;
using UnityEngine.UI;
using Native = Timeline.Timeline;

namespace GinTimelineHack
{
    // Guarded regression checks for Char Setup, the anchor sphere and Auto Key.
    // They need a selected character and several frames, so they run as a coroutine
    // started by the isolated test driver. They never run during normal use.
    public sealed partial class Plugin
    {
        // null while the checks are running.
        public string WorkflowTestResult;

        static bool InIsolatedTestGame()
        {
            DirectoryInfo game = Directory.GetParent(Application.dataPath);
            return Array.IndexOf(Environment.GetCommandLineArgs(), "-ginLifeHacksVerify") >= 0
                && game != null
                && game.Name == "test-game"
                && File.Exists(Path.Combine(game.FullName, ".kk-workshop-test"));
        }

        public IEnumerator RunWorkflowTests()
        {
            WorkflowTestResult = null;
            StringBuilder result = new StringBuilder();
            if (!InIsolatedTestGame() || !attached)
            {
                WorkflowTestResult = "SKIP workflow checks: attached plugin in the marked isolated test-game required";
                yield break;
            }

            List<OCIChar> characters = SelectedCharacters();
            if (characters.Count != 1)
            {
                WorkflowTestResult = "FAIL workflow checks: select exactly one character";
                yield break;
            }

            OCIChar character = characters[0];
            Step(result, "Char Setup rows", delegate
            {
                CheckCharacterRows(character, result);
            });
            yield return null;

            // ---- Auto Key ------------------------------------------------------------------
            Interpolable hips = FindRow(character, "guideObjectPos", "cf_t_hips(work)");
            Interpolable waist = FindRow(character, "boneRot", "cf_j_waist02");
            Interpolable face = FindRow(character, "groupBlendShapeLink", "cf_O_face");
            if (hips == null || waist == null || face == null)
            {
                result.AppendLine("FAIL Auto Key: Char Setup rows missing");
                WorkflowTestResult = result.ToString();
                yield break;
            }

            int keysBefore = TotalKeys(character);
            string curveBefore = autoKeyCurveSetting.Value;
            Step(result, "Auto Key curve menu", delegate
            {
                CheckCurveMenu(result);
            });
            autoKeyCurveSetting.Value = "Stairs";
            SetAutoKey(true);
            Native.Seek(1f);
            for (int i = 0; i < 4; i++)
            {
                yield return null;
            }

            Step(result, "Auto Key ignores seeking", delegate
            {
                Require(autoKey && TotalKeys(character) == keysBefore, "seeking or enabling Auto Key must not write keys");
            });

            // An edit without any user input is absorbed, not keyed.
            GuideObject hipsGuide = (GuideObject)hips.parameter;
            hipsGuide.changeAmount.pos += new Vector3(0.05f, 0f, 0f);
            autoKeyLastInput = -10f;
            for (int i = 0; i < 12; i++)
            {
                yield return null;
            }

            Step(result, "Auto Key needs user input", delegate
            {
                Require(TotalKeys(character) == keysBefore, "a change without input must not write keys");
            });

            // Edits with user input: IK target, KKPE bone, KKPE face Blend Shape.
            Vector3 hipsPosition = hipsGuide.changeAmount.pos + new Vector3(0.1f, 0.02f, 0f);
            Quaternion waistRotation = Quaternion.Euler(12f, 3f, 0f);
            hipsGuide.changeAmount.pos = hipsPosition;
            object bonesEditor = Get<object>(waist.parameter, "key");
            bonesEditor.GetType().GetMethod("SetBoneRotation", Flags).Invoke(bonesEditor, new object[] { Get<Transform>(waist.parameter, "value"), waistRotation });
            object faceRenderer = Get<object>(face.parameter, "blendRenderer");
            faceRenderer.GetType().GetMethod("SetBlendShapeWeight", Flags, null, new Type[] { typeof(int), typeof(float), typeof(bool) }, null)
                .Invoke(faceRenderer, new object[] { 0, 55f, false });
            for (int i = 0; i < 8; i++)
            {
                autoKeyLastInput = Time.unscaledTime;
                yield return null;
            }

            Step(result, "Auto Key writes edited rows", delegate
            {
                Timeline.Keyframe hipsKey = KeyAt(hips, 1f), waistKey = KeyAt(waist, 1f), faceKey = KeyAt(face, 1f);
                Require(hipsKey != null && (Vector3)hipsKey.value == hipsPosition, "IK target key holds the edited position");
                Require(waistKey != null && Quaternion.Angle((Quaternion)waistKey.value, waistRotation) < 0.01f, "KKPE bone key holds the edited rotation");
                Require(faceKey != null && Mathf.Abs(((float[])faceKey.value)[0] - 55f) < 0.01f, "face group key holds the applied weight");
                Require(TotalKeys(character) == keysBefore + 3, "only the three edited rows received a key");
                Require(SameCurve(hipsKey.curve, PresetCurve(Array.IndexOf(CurveNames, "Stairs"))) && SameCurve(waistKey.curve, hipsKey.curve), "added keys use the chosen Stairs curve");
                result.AppendLine("PASS Auto Key: IK target, KKPE bone and face Blend Shape edits keyed at the playhead with the chosen curve (Stairs); untouched rows unchanged");
            });

            // A second edit at the same time updates the key instead of adding one.
            hipsPosition += new Vector3(0f, 0.03f, 0f);
            hipsGuide.changeAmount.pos = hipsPosition;
            for (int i = 0; i < 8; i++)
            {
                autoKeyLastInput = Time.unscaledTime;
                yield return null;
            }

            Step(result, "Auto Key updates an existing key", delegate
            {
                Require(TotalKeys(character) == keysBefore + 3 && (Vector3)KeyAt(hips, 1f).value == hipsPosition, "existing key updated in place");
                Require(SameCurve(KeyAt(hips, 1f).curve, PresetCurve(Array.IndexOf(CurveNames, "Stairs"))), "the updated key keeps its curve");
                result.AppendLine("PASS Auto Key: a further edit at the same time updates the existing key");
            });

            // Playback writes values through Timeline itself: nothing may be keyed.
            Native.Play();
            for (int i = 0; i < 20; i++)
            {
                autoKeyLastInput = Time.unscaledTime;
                yield return null;
            }

            Native.Pause();
            for (int i = 0; i < 6; i++)
            {
                autoKeyLastInput = Time.unscaledTime;
                yield return null;
            }

            Step(result, "Auto Key ignores playback", delegate
            {
                Require(TotalKeys(character) == keysBefore + 3, "playback must not write keys");
                result.AppendLine("PASS Auto Key: seeking, playback and changes without input write nothing");
            });

            // Off: edits are not keyed.
            SetAutoKey(false);
            hipsGuide.changeAmount.pos += new Vector3(0.2f, 0f, 0f);
            for (int i = 0; i < 8; i++)
            {
                autoKeyLastInput = Time.unscaledTime;
                yield return null;
            }

            Step(result, "Auto Key off", delegate
            {
                Require(!autoKey && TotalKeys(character) == keysBefore + 3, "Auto Key off must not write keys");
                Require(autoKeyStripe != null && !autoKeyStripe.gameObject.activeSelf && autoKeyButton.color == ButtonColor && autoKeyDot.color == DotOffColor, "indicator shows the off state");
                result.AppendLine("PASS Auto Key off: edits are left to the S key; indicator hidden");
            });

            // ---- keying rows by name (GIN Pad's Key now) ----
            Native.Seek(2f);
            for (int i = 0; i < 4; i++)
            {
                yield return null;
            }

            int keysBeforeNamed = TotalKeys(character);
            string named = KeyNamedRows(character, "cf_t_hips(work), cf_j_waist02");
            for (int i = 0; i < 6; i++)
            {
                yield return null;
            }

            int keysAfterNamed = TotalKeys(character);
            string again = KeyNamedRows(character, "cf_t_hips(work),cf_j_waist02");
            for (int i = 0; i < 6; i++)
            {
                yield return null;
            }

            Step(result, "keying rows by name", delegate
            {
                Require(named.StartsWith("Keyed:", StringComparison.Ordinal) && again.StartsWith("Keyed:", StringComparison.Ordinal), "requests accepted: " + named);
                Require(KeyAt(hips, 2f) != null && KeyAt(waist, 2f) != null && KeyAt(face, 2f) == null, "the named rows have a key at 2 s, the face does not");
                Require(keysAfterNamed > keysBeforeNamed && TotalKeys(character) == keysAfterNamed, "a second request at the same time updates the keys instead of adding");
                result.AppendLine("PASS keying rows by name: " + named + "; the hips IK target and the waist bone keyed, the face left alone, a repeat updates in place");
            });

            autoKeyCurveSetting.Value = curveBefore;
            ShowCurveChoice();
            Native.Stop();
            WorkflowTestResult = result.ToString().TrimEnd();
        }

        // The record button and the curve menu: six rows, the chosen one lit, a click on a row
        // chooses that curve and closes the menu; every preset is Timeline's own curve.
        void CheckCurveMenu(StringBuilder result)
        {
            Require(autoKeyButton != null && autoKeyDot != null && autoKeyDot.sprite != null && autoKeyArrow != null, "record button and arrow built");
            Require(autoKeyButton.rectTransform.sizeDelta.x <= RecordWidth && autoKeyButton.GetComponentInChildren<Text>() == null, "the record button is a small icon without text");
            SetAutoKey(true);
            Require(autoKeyButton.color == RecordOnColor && autoKeyDot.color == DotOnColor, "on: blue button, red dot");
            SetAutoKey(false);
            ShowCurveMenu(true);
            Require(autoKeyMenu.gameObject.activeSelf && curveButtons.Count == CurveNames.Length, "menu opens with " + curveButtons.Count + " rows");
            for (int i = 1; i < CurveNames.Length; i++)
            {
                AnimationCurve preset = PresetCurve(i);
                Require(preset != null && preset.keys.Length >= 2, CurveNames[i] + " preset read from Timeline");
                Require(curveButtons[i].transform.Find("Curve").GetComponent<Image>().sprite != null, CurveNames[i] + " row has its curve icon");
            }

            curveButtons[4].onClick.Invoke();
            Require(autoKeyCurveSetting.Value == "Hermite" && !autoKeyMenu.gameObject.activeSelf && curveRows[4].color == RecordOnColor && curveRows[0].color.a == 0f, "a row click chooses Hermite and closes the menu");
            Require(SameCurve(AutoKeyCurve(), PresetCurve(4)), "the chosen curve is Timeline's Hermite preset");
            curveButtons[0].onClick.Invoke();
            Require(autoKeyCurveSetting.Value == PreviousCurve && AutoKeyCurve() == null, "Previous key leaves the curve to Timeline");
            result.AppendLine("PASS Auto Key control: small record icon (blue / red dot when on), arrow menu with Previous key + Line, Top, Bottom, Hermite, Stairs read from Timeline; a click chooses and closes");
        }

        static bool SameCurve(AnimationCurve a, AnimationCurve b)
        {
            if (a == null || b == null || a.keys.Length != b.keys.Length)
            {
                return false;
            }

            for (int i = 0; i < a.keys.Length; i++)
            {
                UnityEngine.Keyframe x = a.keys[i], y = b.keys[i];
                if (!Same(x.time, y.time) || !Same(x.value, y.value) || !Same(x.inTangent, y.inTangent) || !Same(x.outTangent, y.outTangent))
                {
                    return false;
                }
            }

            return true;
        }

        // Stairs uses infinite tangents, and Mathf.Approximately(Infinity, Infinity) is false.
        static bool Same(float a, float b)
        {
            return a == b || Mathf.Approximately(a, b);
        }

        // After the Face window asked for a whole-face key: every face row has a key at that time,
        // the tongue rows (not requested) do not.
        public string CheckFaceKeys(float time)
        {
            if (!InIsolatedTestGame() || !attached)
            {
                return "SKIP face key check: isolated test-game required";
            }

            try
            {
                List<OCIChar> characters = SelectedCharacters();
                Require(characters.Count == 1, "one character selected");
                List<Interpolable> face = FaceRows(characters[0], false);
                Require(face.Count >= 6, "face rows found: " + face.Count);
                foreach (Interpolable row in face)
                {
                    Require(KeyAt(row, time) != null, "face row keyed: " + row.name);
                }

                Interpolable group = FindRow(characters[0], "groupBlendShapeLink", "cf_O_face");
                float[] keyed = (float[])KeyAt(group, time).value;
                float[] live = (float[])group.GetValue();
                for (int i = 0; i < keyed.Length; i++)
                {
                    Require(Mathf.Abs(keyed[i] - live[i]) < 0.01f, "face key holds the applied weights");
                }

                Interpolable tongue = FindRow(characters[0], "boneRot", "cf_j_tang_03");
                Require(tongue != null && KeyAt(tongue, time) == null, "tongue rows are left alone unless requested");
                Dictionary<string, bool[]> usage = FaceShapeUsage(characters[0]);
                Require(usage.ContainsKey("cf_O_face") && usage.ContainsKey("cf_O_tooth") && usage["cf_O_face"].Length == keyed.Length, "usage reported per renderer");
                return "PASS whole-face key: " + face.Count + " face rows keyed at " + time + " s with the applied values; tongue rows untouched; shape usage reported";
            }
            catch (Exception e)
            {
                return "FAIL whole-face key: " + Error(e);
            }
        }

        static void Step(StringBuilder result, string name, Action check)
        {
            try
            {
                check();
            }
            catch (Exception e)
            {
                result.AppendLine("FAIL " + name + ": " + Error(e));
            }
        }

        void CheckCharacterRows(OCIChar character, StringBuilder result)
        {
            // Two template rows exist already in the wrong place: one in a group of the user's, one
            // at the top level. Char Setup must move both into BODY and put BODY in template order.
            Transform root = character.guideObject.transformTarget;
            string straySetup = "<root><interpolableGroup name=\"STRAY\">"
                + "<interpolable owner=\"KKPE\" id=\"boneRot\" parameter=\"" + PathFrom(root, FindBone(root, "cf_j_waist02")) + "\" />"
                + "</interpolableGroup>"
                + "<interpolable owner=\"Timeline\" id=\"guideObjectRot\" guideObjectPath=\"" + PathFrom(root, FindBone(root, "cf_j_head")) + "\" />"
                + "</root>";
            XmlDocument stray = new XmlDocument();
            stray.LoadXml(straySetup);
            RegistrationReport strayReport = new RegistrationReport();
            RegisterTracks(character, stray.DocumentElement, strayReport);
            Require(strayReport.Added == 2 && GroupNamed("STRAY") != null, "two stray rows prepared: " + strayReport.Summary());

            int rowsBefore = RowsOf(character).Count;
            RegistrationReport first = new RegistrationReport();
            first.GatherExisting = true;
            SetupReport objects = new SetupReport();
            SetupCharacter(character, first, objects);
            Require(first.Added > 70 && RowsOf(character).Count == rowsBefore + first.Added - CountRowsOfOthers(first), "rows registered: " + first.Summary());
            Require(first.Moved >= 2 && GroupNamed("STRAY") == null, "existing rows were moved into their groups and the emptied group is gone: " + first.Summary());
            foreach (Interpolable row in Native.GetAllInterpolables(false))
            {
                if (row.oci == character || first.Targets.ContainsValue(row.oci))
                {
                    Require(row.keyframes.Count >= 1 && KeyAt(row, 0f) != null && KeyAt(row, 0f).value != null, "row has its 0:00 key: " + row.name);
                }
            }

            Dictionary<string, int> sizes = GroupSizes();
            Require(sizes.ContainsKey("R_FINGER") && sizes["R_FINGER"] == 15 && sizes["L_FINGER"] == 15 && sizes["MUNE_SHIRI"] == 12, "finger and chest groups");
            Require(sizes.ContainsKey("FACE") && sizes["FACE"] >= 12 && sizes.ContainsKey("BODY") && sizes["BODY"] == 25, "face and body groups: " + Describe(sizes));
            Require(!sizes.ContainsKey("O_BODY_A"), "no body shape group for a body mesh without Blend Shapes");
            Require(FindRow(character, "groupBlendShapeLink", BodyRenderer) == null && !HasWholeRendererRow(character, BodyRenderer), "the body mesh has no whole-renderer row");

            Interpolable head = FindRow(character, "guideObjectRot", "cf_j_head");
            Interpolable self = FindRow(character, "guideObjectPos", root.name);
            Require(head != null && Near(head.color, TemplateColor("bone", "cf_j_head")) && self != null && Near(self.color, CharacterRowColor()), "row colours follow the template");
            IGroupNode body = (IGroupNode)LeafOf(head).parent;
            Interpolable waist = FindRow(character, "boneRot", "cf_j_waist02");
            Require(((LeafNode<Interpolable>)body.children[0]).obj == head && ((LeafNode<Interpolable>)body.children[5]).obj == waist, "a moved row takes its template place");
            Require(Near(waist.color, TemplateColor("bone", "cf_j_waist02")), "a moved row takes its group colour");
            string[] added = { "cf_d_shoulder_L", "cf_d_shoulder_R", "cf_d_kokan" };
            for (int i = 0; i < added.Length; i++)
            {
                Interpolable row = ((LeafNode<Interpolable>)body.children[22 + i]).obj;
                Require(row.id == "boneRot" && Get<Transform>(row.parameter, "value").name == added[i], "BODY ends with the KKPE rotation of " + added[i]);
            }

            string objectState = CheckSetupObjects(character, first, objects, sizes);

            RegistrationReport second = new RegistrationReport();
            second.GatherExisting = true;
            SetupReport secondObjects = new SetupReport();
            int allRows = new List<Interpolable>(Native.GetAllInterpolables(false)).Count;
            SetupCharacter(character, second, secondObjects);
            Require(second.Added == 0 && second.Moved == 0 && new List<Interpolable>(Native.GetAllInterpolables(false)).Count == allRows, "second run adds and moves no row: " + second.Summary());
            Require(secondObjects.Added == 0 && secondObjects.Links == 0 && secondObjects.Kept == objects.Added + objects.Kept, "second run creates no object and no link: " + secondObjects.Summary());
            Require(GroupSizes().Count == sizes.Count, "second run creates no duplicate group");
            result.AppendLine("PASS Char Setup rows: " + first.Summary() + "; existing rows gathered in template order; 25 BODY rows end with shoulders and kokan; colours and 0:00 keys match the template; repeat run adds nothing");
            result.AppendLine("PASS Char Setup objects: " + objectState);
            result.AppendLine("PASS Char Setup body shapes: " + CheckShapeOrder(character));
            result.AppendLine("PASS Char Setup themes: " + CheckThemes(character));
        }

        static string Describe(Dictionary<string, int> sizes)
        {
            List<string> parts = new List<string>();
            foreach (KeyValuePair<string, int> size in sizes)
            {
                parts.Add(size.Key + " " + size.Value);
            }

            return string.Join(", ", parts.ToArray());
        }

        int CountRowsOfOthers(RegistrationReport report)
        {
            int count = 0;
            foreach (Interpolable row in Native.GetAllInterpolables(false))
            {
                if (report.Targets.ContainsValue(row.oci))
                {
                    count++;
                }
            }

            return count;
        }

        // The objects around the character: attached where the template says, with its sizes;
        // the stream sphere beside the character, linked to the anchor sphere.
        string CheckSetupObjects(OCIChar character, RegistrationReport rows, SetupReport objects, Dictionary<string, int> sizes)
        {
            int templateObjects = SetupTemplate().DocumentElement["objects"].SelectNodes(".//item|.//folder").Count;
            Require(objects.Added + objects.Unavailable.Count >= templateObjects - 0 || objects.Added == templateObjects, "every template object accounted for: " + objects.Summary());
            ObjectCtrlInfo anchor;
            Require(rows.Targets.TryGetValue("k538", out anchor), "the anchor sphere is placed");
            Require(anchor.treeNodeObject.parent == AccessoryPointNode(character, 51) && Singleton<Info>.Instance.dicAccessoryPointInfo[51].key == "a_n_dan", "the anchor sphere is on a_n_dan");
            Require(Mathf.Approximately(((OCIItem)anchor).itemInfo.alpha, 0f), "the anchor sphere is transparent (alpha 0)");
            Require((anchor.guideObject.changeAmount.pos - new Vector3(-0.0005601165f, 0.06343164f, -0.02073893f)).magnitude < 1e-5f, "the anchor sphere has the scene's local position");
            string points = "8=" + Singleton<Info>.Instance.dicAccessoryPointInfo[8].key + ", 51=a_n_dan, 52=" + Singleton<Info>.Instance.dicAccessoryPointInfo[52].key;
            if (objects.Unavailable.Count > 0)
            {
                return objects.Summary() + " (mods missing in this run); accessory points " + points;
            }

            Require(objects.Added == templateObjects, "all " + templateObjects + " objects added: " + objects.Summary());
            Require(Mathf.Abs(rows.Targets["k57"].guideObject.changeAmount.scale.x - 0.5839336f) < 1e-5f && rows.Targets["k57"].treeNodeObject.parent == AccessoryPointNode(character, 8), "breath steam: point 8, size 0.584");
            Require(Mathf.Abs(rows.Targets["k1336"].guideObject.changeAmount.scale.x - 0.001f) < 1e-7f && rows.Targets["k1336"].treeNodeObject.parent == AccessoryPointNode(character, 52), "splash: a_n_kokan, size 0.001");
            OCIFolder strings = rows.Targets["k84"] as OCIFolder;
            Require(strings != null && strings.name == "STRINGS" && strings.treeNodeObject.child.Count == 10, "STRINGS folder with 10 items on a_n_dan");
            ObjectCtrlInfo stream = rows.Targets["k1197"];
            Require(stream.treeNodeObject.parent == character.treeNodeObject.parent, "the stream sphere is beside the character");
            Require(ConstrainedChild(anchor.guideObject.transformTarget) == stream.guideObject.transformTarget, "NodesConstraints links the anchor sphere to the stream sphere");
            foreach (Renderer renderer in ((OCIItem)stream).objectItem.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer.name == "item_O_Sphere")
                {
                    Require(!renderer.enabled, "Material Editor hides the stream sphere");
                }
            }

            int hiddenStreams = 0;
            foreach (TreeNodeObject child in stream.treeNodeObject.child)
            {
                hiddenStreams += child.visible ? 0 : 1;
            }

            Require(hiddenStreams == 7, "the 7 water streams are hidden: " + hiddenStreams);
            int splashQueue = 0;
            foreach (Renderer renderer in ((OCIItem)rows.Targets["k1336"]).objectItem.GetComponentsInChildren<Renderer>(true))
            {
                foreach (Material material in renderer.sharedMaterials)
                {
                    if (material != null && Formatted(material.name) == "m_splash")
                    {
                        splashQueue = material.renderQueue;
                    }
                }
            }

            Require(splashQueue == 9999, "Material Editor settings reach the splash material (render queue " + splashQueue + ")");
            Require(sizes.ContainsKey("STREAM") && sizes["STREAM"] == 11 && sizes["FOG"] == 2 && sizes["STRINGS"] == 10, "STREAM, FOG and STRINGS rows on the objects: " + Describe(sizes));
            return objects.Summary() + "; accessory points " + points + "; sizes, transparency, hidden streams, Material Editor settings and the NodesConstraints link match the scene";
        }

        // The body's single Blend Shape rows: the scene's indices first, then the rest.
        string CheckShapeOrder(OCIChar character)
        {
            Transform face = FindBone(character.guideObject.transformTarget, "cf_O_face");
            int count = face.GetComponent<SkinnedMeshRenderer>().sharedMesh.blendShapeCount;
            List<int> order = ShapeOrder(face, "3 1 999 3");
            Require(order.Count == count && order[0] == 3 && order[1] == 1 && order[2] == 0 && order[3] == 2, "listed shapes first, missing and repeated ones skipped, then the others");
            XmlElement each = (XmlElement)SetupTemplate().DocumentElement.SelectSingleNode("rows/interpolableGroup[@name='O_BODY_A']/interpolable");
            Require(each != null && each.GetAttribute("renderer") == BodyRenderer && each.GetAttribute(EachShapeAttribute).StartsWith("0 1 15 32 33 2", StringComparison.Ordinal), "O_BODY_A keeps the scene's order");
            return "one row per shape in the scene's order (0 1 15 32 33 2 ...), the rest after; checked on a mesh with " + count + " shapes";
        }

        // Themes: white and grey stay, tints turn; a second character gets the second theme.
        string CheckThemes(OCIChar character)
        {
            Color royal = CharacterRowColor();
            Require(Themed(Color.white, 2) == Color.white && Themed(new Color(0.5f, 0.5f, 0.5f), 3) == new Color(0.5f, 0.5f, 0.5f), "white and grey rows stay");
            for (int a = 0; a < ThemeCount; a++)
            {
                for (int b = a + 1; b < ThemeCount; b++)
                {
                    Require(!Near(Themed(royal, a), Themed(royal, b)), "themes " + a + " and " + b + " differ");
                }
            }

            Require(ThemeFor(character) == 0, "the first character keeps blue");
            string card = Path.Combine(Path.Combine(Path.GetDirectoryName(Application.dataPath), "UserData"), "GINLifeHacksTests/vanilla-female.png");
            OCIChar second = AddObjectFemale.Add(card);
            Require(second != null, "second character added");
            try
            {
                Require(ThemeFor(second) == 1, "a second character gets the second theme");
                RegistrationReport rows = new RegistrationReport();
                rows.GatherExisting = true;
                SetupCharacter(second, rows, new SetupReport());
                Interpolable head = FindRow(second, "guideObjectRot", "cf_j_head");
                Require(head != null && Near(head.color, Themed(TemplateColor("bone", "cf_j_head"), 1)), "its rows are in the rose theme");
                Require(ThemeFor(second) == 1 && ThemeFor(character) == 0, "each character keeps its theme");
                int bodies = 0;
                foreach (KeyValuePair<string, int> size in GroupSizes())
                {
                    bodies += size.Key == "BODY" ? 1 : 0;
                }

                Require(BodyGroups() == 2, "each character has its own BODY group");
            }
            finally
            {
                Studio.Studio.Instance.treeNodeCtrl.DeleteNode(second.treeNodeObject);
                Studio.Studio.Instance.treeNodeCtrl.SelectSingle(character.treeNodeObject, false);
            }

            return "white / grey rows unchanged, 5 distinct themes; a second character's rows are rose while the first stays blue";
        }

        int BodyGroups()
        {
            int count = 0;
            foreach (INode node in RootNodes())
            {
                IGroupNode group = node as IGroupNode;
                count += group != null && Get<string>(Get<object>(group, "obj"), "name") == "BODY" ? 1 : 0;
            }

            return count;
        }

        Dictionary<string, int> GroupSizes()
        {
            Dictionary<string, int> sizes = new Dictionary<string, int>();
            foreach (INode node in RootNodes())
            {
                IGroupNode group = node as IGroupNode;
                if (group != null)
                {
                    sizes[Get<string>(Get<object>(group, "obj"), "name")] = Descendants(group).Count;
                }
            }

            return sizes;
        }

        INode LeafOf(Interpolable row)
        {
            object tree = Get<object>(native, "_interpolablesTree");
            return (INode)tree.GetType().GetMethod("GetLeafNode", Flags).Invoke(tree, new object[] { row });
        }

        static Color TemplateColor(string attribute, string value)
        {
            foreach (XmlElement row in SetupTemplate().DocumentElement.SelectNodes("rows//interpolable"))
            {
                if (row.GetAttribute(attribute) == value)
                {
                    return ReadVector(row.GetAttribute("color"), Color.white);
                }
            }

            throw new InvalidOperationException("no template row with " + attribute + " " + value);
        }

        static bool Near(Color a, Color b)
        {
            return Mathf.Abs(a.r - b.r) < 0.002f && Mathf.Abs(a.g - b.g) < 0.002f && Mathf.Abs(a.b - b.b) < 0.002f;
        }

        static Transform FindBone(Transform root, string name)
        {
            foreach (Transform bone in root.GetComponentsInChildren<Transform>(true))
            {
                if (bone.name == name)
                {
                    return bone;
                }
            }

            throw new InvalidOperationException("no " + name + " on the fixture");
        }

        IGroupNode GroupNamed(string name)
        {
            foreach (INode node in RootNodes())
            {
                IGroupNode group = node as IGroupNode;
                if (group != null && Get<string>(Get<object>(group, "obj"), "name") == name)
                {
                    return group;
                }
            }

            return null;
        }

        static List<Interpolable> RowsOf(ObjectCtrlInfo target)
        {
            List<Interpolable> rows = new List<Interpolable>();
            foreach (Interpolable row in Native.GetAllInterpolables(false))
            {
                if (row.oci == target)
                {
                    rows.Add(row);
                }
            }

            return rows;
        }

        static int TotalKeys(ObjectCtrlInfo target)
        {
            int keys = 0;
            foreach (Interpolable row in RowsOf(target))
            {
                keys += row.keyframes.Count;
            }

            return keys;
        }

        // Row of the given type whose bone, guide object or renderer has this name.
        static Interpolable FindRow(ObjectCtrlInfo target, string id, string leaf)
        {
            foreach (Interpolable row in RowsOf(target))
            {
                if (row.id != id || row.parameter == null)
                {
                    continue;
                }

                string name;
                GuideObject guide = row.parameter as GuideObject;
                if (guide != null)
                {
                    name = guide.transformTarget.name;
                }
                else if (id.StartsWith("bone", StringComparison.Ordinal))
                {
                    name = Get<Transform>(row.parameter, "value").name;
                }
                else
                {
                    string path = Get<string>(row.parameter, "rendererPath");
                    name = path.Substring(path.LastIndexOf('/') + 1);
                }

                if (name == leaf)
                {
                    return row;
                }
            }

            return null;
        }
    }
}
