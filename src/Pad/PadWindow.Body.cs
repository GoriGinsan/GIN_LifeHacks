using System.Collections.Generic;
using GinShared;
using Studio;
using UnityEngine;

namespace GinPadHack
{
    // Body tab: a figure of buttons, seen from the front like KKPE's own window.
    //
    //   grey   IK target  - a Studio IK target ("cf_t_hips(work)" ...). Values are Studio's own.
    //   green  bone       - a bone that otherwise needs KKPE's Advanced mode. Edited through KKPE's
    //                       bone editor; a bone that Studio FK controls is rotated on its FK node.
    //
    // Selecting: click selects one part, Ctrl + click adds or removes a part. A selection holds
    // either IK targets or bones, never both. The last selected part is the "primary": the bars
    // show its values and it is the one selected in Studio.
    //
    // Editing, always applied to the whole selection:
    //   drag on a green button         turn the bone freely, as seen from the camera
    //   right drag on a green button   roll the bone around the viewing direction
    //   middle click on a green button put the bone's rotation back to its default
    //   drag on a grey button          move the IK target horizontally (floor plane)
    //   right drag on a grey button    move the IK target up and down
    //   bars                           add to one axis; right click a bone's bar removes that override
    //   Key now, middle click a bar    key the selection in the Timeline at the playhead
    //                                  (with Delay Key: each part later by its delay, PadWindow.Delay.cs)
    //   Ctrl + Z / Shift + Z           Studio's Undo / Redo: each press on the tab is one step
    //                                  (drags, bars, resets, limb copies; see PoseUndo.cs)
    //
    // Some bones only turn the way the body really bends when dragged on the figure: the neck
    // never twists (its own X and Z only), the shoulder joints only swing (Y). The bars below the
    // figure keep all three axes for them.
    //
    // Every selected part receives the same movement or turn as seen on screen; nothing is mirrored.
    // Parts that are edited less often (shoulder joints, bust 3, waist, buttocks, kokan, toes) are
    // small buttons. The Blend Shapes of the body mesh are the "Shapes" tab (DrawBodyShapes).
    public sealed partial class PadWindow
    {
        enum Via
        {
            Ik,
            Bone
        }

        sealed class BodyPart
        {
            internal string Label, Name;
            internal Via Via;
            internal bool Move, Rotate, Scale, Small;
            internal Vector3 DragAxes = Vector3.one;   // which of the bone's own axes a drag on the button may turn
            internal Rect Place;       // inside the figure, at UiScale 1
        }

        const float FigureWidth = 306f, Pitch = 34f, PartHeight = 30f, SmallHeight = 22f;
        const int FigureRows = 11;
        const float TitleWidth = 32f, AxisBarWidth = 89f, ScaleBarWidth = 66f, FunctionWidth = 74f;
        const float BarMovePerPixel = 0.002f, BoneMovePerPixel = 0.0002f, DegreesPerPixel = 0.4f, ScalePerPixel = 0.003f;
        const float DragMovePerPixel = 0.003f, DragDegreesPerPixel = 0.5f;
        const int PoseSettleFrames = 2;   // Swap pose and the limb copies happen in KKPE's next IK pass
        const string BodyRenderer = "o_body_a";

        // KKPE enum values: RootMotion.FinalIK.FullBodyBipedChain and Studio.OIBoneInfo.BoneGroup.
        const int ChainLeftArm = 0, ChainRightArm = 1, ChainLeftLeg = 2, ChainRightLeg = 3;
        const int GroupRightLeg = 2, GroupLeftLeg = 4, GroupRightArm = 8, GroupLeftArm = 16, GroupRightHand = 32, GroupLeftHand = 64;

        static readonly int PartHint = "GinPadBodyPart".GetHashCode();
        static readonly string[] AxisNames = { "X", "Y", "Z" };
        static readonly string[] LimbLabels = { "R arm → L", "L arm → R", "R hand → L", "L hand → R", "R leg → L", "L leg → R", "Swap pose" };
        static readonly BodyPart[] BodyParts = BuildBodyParts();

        readonly List<BodyPart> selection = new List<BodyPart>();   // the last entry is the primary part
        readonly List<ShapeRow> bodyShapes = new List<ShapeRow>();
        BodyPart collapseOnClick;      // pressed member of a group: a click without a drag selects it alone
        bool pressDragged;
        int pressButton;
        Component bodyShapesOwner;
        string bodyShapesRenderer = BodyRenderer;   // only the layout preview of the tests points this elsewhere
        Vector2 bodyShapesScroll;
        Rect bodyArea;                 // the Body tab as last painted, inside the window
        PoseSnapshot poseBefore;       // the pose when the last press on the tab began; null when none is waiting
        int poseReleasedFrame = -1;

        // The figure, 306 wide at UiScale 1, seen from the front: the character's right side is on the
        // left, as on KKPE's window, and every left / right pair is mirrored around x = 153. Legs stand
        // a little apart (knees and feet further out than the thighs) to leave room for the kokan
        // button between the knees.
        static BodyPart[] BuildBodyParts()
        {
            List<BodyPart> parts = new List<BodyPart>();
            //                                                  row    x  width   move  rotate scale
            Bone(parts, "Head", "cf_j_head",                     0, 122, 62,      false, true, false);
            Small(parts, "R.Sho", "cf_d_shoulder_R",             1,  25, 41);
            Bone(parts, "Neck", "cf_j_neck",                     1, 122, 62,      false, true, false);
            Small(parts, "L.Sho", "cf_d_shoulder_L",             1, 240, 41);
            Ik(parts, "R.Shoulder", "cf_t_shoulder_R",           2,  47, 76);
            Ik(parts, "L.Shoulder", "cf_t_shoulder_L",           2, 183, 76);
            Ik(parts, "R.Elbow", "cf_t_elbo_R",                  3,  14, 64);
            Bone(parts, "R.Bust1", "cf_d_bust01_R",              3,  94, 58,      true, true, true);
            Bone(parts, "L.Bust1", "cf_d_bust01_L",              3, 154, 58,      true, true, true);
            Ik(parts, "L.Elbow", "cf_t_elbo_L",                  3, 228, 64);
            Ik(parts, "R.Hand", "cf_t_hand_R",                   4,   0, 64);
            Small(parts, "R.Bust3", "cf_d_bust03_R",             4, 103, 47,      true, true, true);
            Small(parts, "L.Bust3", "cf_d_bust03_L",             4, 156, 47,      true, true, true);
            Ik(parts, "L.Hand", "cf_t_hand_L",                   4, 242, 64);
            Small(parts, "Waist", "cf_j_waist02",                5, 128, 50);
            Ik(parts, "Body", "cf_t_hips",                       6, 116, 74);
            Small(parts, "R.Butt", "cf_d_siri01_R",              7,   5, 43,      false, true, true);
            Ik(parts, "R.Thigh", "cf_t_waist_R",                 7,  56, 86);
            Ik(parts, "L.Thigh", "cf_t_waist_L",                 7, 164, 86);
            Small(parts, "L.Butt", "cf_d_siri01_L",              7, 258, 43,      false, true, true);
            Ik(parts, "R.Knee", "cf_t_knee_R",                   8,  31, 86);
            Small(parts, "kokan", "cf_d_kokan",                  8, 131, 44);
            Ik(parts, "L.Knee", "cf_t_knee_L",                   8, 189, 86);
            Ik(parts, "R.Foot", "cf_t_leg_R",                    9,  13, 86);
            Ik(parts, "L.Foot", "cf_t_leg_L",                    9, 207, 86);
            Small(parts, "R.Toes", "cf_j_toes_R",               10,  20, 56);
            Small(parts, "L.Toes", "cf_j_toes_L",               10, 230, 56);

            // How these bones really bend: the neck does not twist, the shoulder joints only swing.
            Find(parts, "cf_j_neck").DragAxes = new Vector3(1f, 0f, 1f);
            Find(parts, "cf_d_shoulder_R").DragAxes = Vector3.up;
            Find(parts, "cf_d_shoulder_L").DragAxes = Vector3.up;
            return parts.ToArray();
        }

        static BodyPart Find(List<BodyPart> parts, string name)
        {
            return parts.Find(delegate (BodyPart part) { return part.Name == name; });
        }

        // name: the IK target's name without Studio's "(work)" suffix.
        // Whether an IK target can also be rotated is asked from Studio when it is edited.
        static void Ik(List<BodyPart> parts, string label, string name, int row, float x, float width)
        {
            Add(parts, label, name, Via.Ik, row, x, width, true, true, false);
        }

        static void Bone(List<BodyPart> parts, string label, string name, int row, float x, float width, bool move, bool rotate, bool scale)
        {
            Add(parts, label, name, Via.Bone, row, x, width, move, rotate, scale);
        }

        // A small green button: a bone edited less often. Rotation only unless said otherwise.
        static void Small(List<BodyPart> parts, string label, string name, int row, float x, float width)
        {
            Small(parts, label, name, row, x, width, false, true, false);
        }

        static void Small(List<BodyPart> parts, string label, string name, int row, float x, float width, bool move, bool rotate, bool scale)
        {
            Add(parts, label, name, Via.Bone, row, x, width, move, rotate, scale);
            BodyPart part = parts[parts.Count - 1];
            part.Small = true;
            part.Place = new Rect(x, row * Pitch + (PartHeight - SmallHeight) / 2f, width, SmallHeight);
        }

        static void Add(List<BodyPart> parts, string label, string name, Via via, int row, float x, float width, bool move, bool rotate, bool scale)
        {
            BodyPart part = new BodyPart();
            part.Label = label;
            part.Name = name;
            part.Via = via;
            part.Move = move;
            part.Rotate = rotate;
            part.Scale = scale;
            part.Place = new Rect(x, row * Pitch, width, PartHeight);
            parts.Add(part);
        }

        // ---- drawing -------------------------------------------------------------------------

        void DrawBody(PadTarget target)
        {
            // Before any control of the tab takes the press: remember the pose for Studio's Undo.
            if (Event.current.type == EventType.MouseDown && bodyArea.Contains(Event.current.mousePosition))
            {
                RememberPose(target);
            }

            AdoptDelayEdit();
            GUILayout.BeginHorizontal();
            GUILayout.BeginVertical(PadUi.Width(FigureWidth));
            DrawFigureAndBars(target);
            GUILayout.EndVertical();
            if (Event.current.type == EventType.Repaint)
            {
                bodyArea = GUILayoutUtility.GetLastRect();
            }

            GUILayout.Space(PadUi.Px(2f));
            DrawDelayPanel(target);   // narrow: the window keeps its width (see PadWindow.Delay.cs)
            GUILayout.EndHorizontal();
        }

        void DrawFigureAndBars(PadTarget target)
        {
            Rect area = GUILayoutUtility.GetRect(PadUi.Px(FigureWidth), PadUi.Px(FigureRows * Pitch), GUILayout.ExpandWidth(false));
            if (Event.current.type == EventType.Repaint)
            {
                delayHoverFigure = null;   // the part under the pointer lights its mark on the delay bar
            }

            foreach (BodyPart part in BodyParts)
            {
                Rect place = new Rect(
                    area.x + PadUi.Px(part.Place.x),
                    area.y + PadUi.Px(part.Place.y),
                    PadUi.Px(part.Place.width),
                    PadUi.Px(part.Place.height));
                if (delayEditShown)
                {
                    DelayPartButton(place, target, part);   // Edit: the buttons set delays
                }
                else
                {
                    PartButton(place, target, part);
                }
            }

            // KKPE's own limb functions, then the tools for the selection.
            for (int first = 0; first < 8; first += 4)
            {
                GUILayout.BeginHorizontal();
                for (int i = first; i < first + 4; i++)
                {
                    if (i < LimbLabels.Length)
                    {
                        if (GUILayout.Button(LimbLabels[i], PadUi.Button, PadUi.Width(FunctionWidth)))
                        {
                            CopyLimb(target, i);
                        }
                    }
                    else
                    {
                        GUI.enabled = selection.Count > 0 && selection[0].Via == Via.Bone;
                        if (GUILayout.Button("Reset bone", PadUi.Button, PadUi.Width(FunctionWidth)))
                        {
                            ResetSelectedBones(target);
                        }

                        GUI.enabled = true;
                    }
                }

                GUILayout.EndHorizontal();
            }

            // The primary part's real name, and "Key now": a middle click on the figure resets a
            // bone's rotation, so keying the selection has a button of its own.
            GUILayout.BeginHorizontal(PadUi.Width(FigureWidth));
            GUILayout.Label(SelectionCaption(target), PadUi.Small, GUILayout.MaxWidth(PadUi.Px(FigureWidth - 66f)));
            GUILayout.FlexibleSpace();
            GUI.enabled = shownSelection.Count > 0;
            if (GUILayout.Button("Key now", PadUi.Button, PadUi.Width(60)))
            {
                KeyBodySelection(target);
            }

            GUI.enabled = true;
            GUILayout.EndHorizontal();

            if (shownPart != null)
            {
                if (shownPart.Via == Via.Ik)
                {
                    DrawIkBars(target);
                }
                else
                {
                    DrawBoneBars(target);
                }
            }
        }

        // What exactly is being edited: the primary part's real transform name.
        string SelectionCaption(PadTarget target)
        {
            if (shownPart == null)
            {
                return "";
            }

            Transform transform = shownPart.Via == Via.Ik ? target.IkTarget(shownPart.Name).guideObject.transformTarget : target.Bone(shownPart.Name);
            return transform.name + (shownSelection.Count > 1 ? "  + " + (shownSelection.Count - 1) : "");
        }

        // One button of the figure. It selects on press and edits the selection while dragged.
        void PartButton(Rect place, PadTarget target, BodyPart part)
        {
            int id = GUIUtility.GetControlID(PartHint, FocusType.Passive, place);
            Event current = Event.current;
            Vector2 moved;
            if (PadUi.TakeHeldMovement(id, current.delta, out moved))   // once per frame, beyond the screen edge
            {
                pressDragged = true;
                if (selection.Contains(part))
                {
                    DragSelection(target, part, moved * (PadUi.Fine(current) ? PadUi.FineFactor : 1f) * PadUi.Sensitivity, pressButton == 1);
                }
            }

            switch (current.GetTypeForControl(id))
            {
                case EventType.MouseDown:
                    if (!place.Contains(current.mousePosition) || !Usable(target, part))
                    {
                        break;
                    }

                    if (current.button == 0 || current.button == 1)
                    {
                        GUIUtility.hotControl = id;
                        pressButton = current.button;
                        pressDragged = false;
                        Press(target, part, current.control);
                        current.Use();
                    }
                    else if (current.button == 2)
                    {
                        if (part.Via == Via.Bone)
                        {
                            ResetRotation(target, part);
                        }

                        current.Use();
                    }

                    break;
                case EventType.MouseUp:
                    if (GUIUtility.hotControl == id)
                    {
                        GUIUtility.hotControl = 0;
                        EndlessDrag.End();
                        if (collapseOnClick == part && !pressDragged)
                        {
                            selection.Clear();
                            selection.Add(part);
                            SelectInStudio(target);
                        }

                        collapseOnClick = null;
                        current.Use();
                    }

                    break;
                case EventType.Repaint:
                    if (place.Contains(current.mousePosition))
                    {
                        delayHoverFigure = part;
                    }

                    Color previous = GUI.color;
                    if (!Usable(target, part))
                    {
                        GUI.color = new Color(1f, 1f, 1f, 0.35f);
                    }

                    GUIStyle style = part.Via == Via.Ik ? PadUi.PartButton : part.Small ? PadUi.SmallBoneButton : PadUi.BoneButton;
                    style.Draw(place, part.Label, place.Contains(current.mousePosition), GUIUtility.hotControl == id, selection.Contains(part), false);
                    GUI.color = previous;
                    break;
            }
        }

        // ---- selection -----------------------------------------------------------------------

        // An IK target is usable while Studio IK shows it; a bone when the character has it.
        static bool Usable(PadTarget target, BodyPart part)
        {
            if (part.Via == Via.Bone)
            {
                return target.Bone(part.Name) != null;
            }

            OCIChar.IKInfo ik = target.IkTarget(part.Name);
            return ik != null && ik.active && target.Character.oiCharInfo.enableIK;
        }

        void Press(PadTarget target, BodyPart part, bool addOrRemove)
        {
            collapseOnClick = null;
            if (addOrRemove)
            {
                if (selection.Count > 0 && selection[0].Via != part.Via)
                {
                    selection.Clear();
                    selection.Add(part);
                    Status("IK targets and bones cannot be selected together");
                }
                else if (!selection.Remove(part))
                {
                    selection.Add(part);
                }
            }
            else if (selection.Count > 1 && selection.Contains(part))
            {
                // Keep the group so that it can be dragged by any member; this part becomes the primary.
                selection.Remove(part);
                selection.Add(part);
                collapseOnClick = part;
            }
            else
            {
                selection.Clear();
                selection.Add(part);
            }

            SelectInStudio(target);
        }

        // Select the primary part's guide object in Studio (IK target, or the FK node of a bone),
        // so that Studio's gizmo and other tools act on the same thing.
        // Studio does not allow several IK targets to be selected at once.
        void SelectInStudio(PadTarget target)
        {
            if (selection.Count == 0 || !Singleton<GuideObjectManager>.IsInstance())
            {
                return;
            }

            BodyPart primary = selection[selection.Count - 1];
            GuideObject guide;
            if (primary.Via == Via.Ik)
            {
                guide = target.IkTarget(primary.Name).guideObject;
            }
            else
            {
                OCIChar.BoneInfo fk = target.ActiveFk(target.Bone(primary.Name));
                guide = fk == null ? null : fk.guideObject;
            }

            if (guide != null)
            {
                Singleton<GuideObjectManager>.Instance.selectObject = guide;
            }
        }

        // Layout pass: what the bars show for this GUI pass (see PadWindow.cs).
        void AdoptBodySelection(PadTarget target)
        {
            shownSelection.Clear();
            shownPart = null;
            if (target == null)
            {
                return;
            }

            foreach (BodyPart part in selection)
            {
                if (Usable(target, part))
                {
                    shownSelection.Add(part);
                    shownPart = part;
                }
            }
        }

        // ---- one edit for the whole selection ------------------------------------------------
        //
        // Every selected part receives the SAME movement or turn in world space, never a mirrored
        // one: with both shoulders selected, dragging left moves both of them left on screen.

        // Key the Timeline rows of the selected parts (IK targets, FK nodes and KKPE bones) at the
        // playhead; with Delay Key on, each part that many seconds later.
        void KeyBodySelection(PadTarget target)
        {
            if (delayKeyOn && TimelineBridge.CanDelayKeys)
            {
                Status(TimelineBridge.KeyNamed(target.Character, DelayedNames(target, shownSelection)));
                return;
            }

            List<string> names = new List<string>();
            foreach (BodyPart part in shownSelection)
            {
                string name = RowName(target, part);
                if (name != null)
                {
                    names.Add(name);
                }
            }

            Status(TimelineBridge.KeyNamed(target.Character, names));
        }

        void MoveIkTargets(PadTarget target, List<BodyPart> parts, Vector3 world)
        {
            foreach (BodyPart part in parts)
            {
                OCIChar.IKInfo ik = target.IkTarget(part.Name);
                if (ik != null && ik.active)
                {
                    GuideObject guide = ik.guideObject;
                    guide.changeAmount.pos += guide.transformTarget.parent.InverseTransformVector(world);
                }
            }
        }

        void TurnIkTargets(PadTarget target, List<BodyPart> parts, Quaternion world)
        {
            foreach (BodyPart part in parts)
            {
                OCIChar.IKInfo ik = target.IkTarget(part.Name);
                if (ik != null && ik.active && ik.guideObject.enableRot)
                {
                    GuideObject guide = ik.guideObject;
                    guide.changeAmount.rot = TurnedInWorld(guide.transformTarget.parent.rotation, Quaternion.Euler(guide.changeAmount.rot), world).eulerAngles;
                }
            }
        }

        void MoveBones(PadTarget target, List<BodyPart> parts, Vector3 world)
        {
            foreach (BodyPart part in parts)
            {
                Transform bone = target.Bone(part.Name);
                if (bone != null && part.Move)
                {
                    Kkpe.SetPosition(target, bone, Kkpe.Position(target, bone) + bone.parent.InverseTransformVector(world));
                }
            }
        }

        // "limited": a drag on the figure, which keeps each bone to its DragAxes.
        void TurnBones(PadTarget target, List<BodyPart> parts, Quaternion world, bool limited)
        {
            foreach (BodyPart part in parts)
            {
                Transform bone = target.Bone(part.Name);
                if (bone != null && part.Rotate)
                {
                    Quaternion before = Kkpe.Rotation(target, bone);
                    Quaternion after = TurnedInWorld(bone.parent.rotation, before, world);
                    if (limited && part.DragAxes != Vector3.one)
                    {
                        after = OnlyAxes(before, after, part.DragAxes);
                    }

                    Kkpe.SetRotation(target, bone, after);
                }
            }
        }

        // The turn from "before" to "after", kept to the given Euler axes (1: free, 0: kept).
        // Works on the change of each angle, so a bone already turned on a kept axis stays so.
        static Quaternion OnlyAxes(Quaternion before, Quaternion after, Vector3 free)
        {
            Vector3 from = before.eulerAngles, to = after.eulerAngles;

            // The same rotation has a second set of Euler angles; take the one nearer "before",
            // otherwise a turn through X = 90 degrees would look like a jump on every axis.
            Vector3 other = new Vector3(180f - to.x, to.y + 180f, to.z + 180f);
            if (AngleDistance(from, other) < AngleDistance(from, to))
            {
                to = other;
            }

            Vector3 limitedAngles = from;
            for (int axis = 0; axis < 3; axis++)
            {
                limitedAngles[axis] += free[axis] * Mathf.DeltaAngle(from[axis], to[axis]);
            }

            return Quaternion.Euler(limitedAngles);
        }

        static float AngleDistance(Vector3 a, Vector3 b)
        {
            return Mathf.Abs(Mathf.DeltaAngle(a.x, b.x)) + Mathf.Abs(Mathf.DeltaAngle(a.y, b.y)) + Mathf.Abs(Mathf.DeltaAngle(a.z, b.z));
        }

        // A local rotation after its object was turned by a world rotation.
        static Quaternion TurnedInWorld(Quaternion parent, Quaternion local, Quaternion world)
        {
            return Quaternion.Inverse(parent) * world * parent * local;
        }

        // The world rotation that turning an object around its own axes (a bar edit) amounts to.
        static Quaternion WorldTurn(Quaternion parent, Quaternion local, Vector3 ownAxesTurn)
        {
            Quaternion orientation = parent * local;
            return orientation * Quaternion.Euler(ownAxesTurn) * Quaternion.Inverse(orientation);
        }

        // ---- dragging on a button -----------------------------------------------------------

        // "pixels": mouse movement of this event, already scaled by sensitivity and Shift.
        void DragSelection(PadTarget target, BodyPart grabbed, Vector2 pixels, bool rightButton)
        {
            Transform view = Singleton<Studio.Studio>.Instance.cameraCtrl.mainCmaera.transform;
            if (grabbed.Via == Via.Bone)
            {
                // Turn as the bones are seen on screen: left / right around the camera's up axis,
                // up / down around its right axis; the right button rolls around the viewing direction.
                Quaternion turn = rightButton
                    ? Quaternion.AngleAxis(-pixels.x * DragDegreesPerPixel, view.forward)
                    : Quaternion.AngleAxis(-pixels.x * DragDegreesPerPixel, view.up) * Quaternion.AngleAxis(-pixels.y * DragDegreesPerPixel, view.right);
                TurnBones(target, selection, turn, true);
            }
            else if (rightButton)
            {
                MoveIkTargets(target, selection, Vector3.up * (-pixels.y * DragMovePerPixel));
            }
            else
            {
                // On the floor plane: screen right is the camera's right, screen up is away from the camera.
                Vector3 right = Flat(view.right, Vector3.right);
                Vector3 away = Flat(view.forward, Flat(view.up, Vector3.forward));
                MoveIkTargets(target, selection, (right * pixels.x - away * pixels.y) * DragMovePerPixel);
            }

            BodyEdited(target, selection);
        }

        // A direction laid on the floor plane; the fallback is used when it points straight up or down.
        static Vector3 Flat(Vector3 direction, Vector3 fallback)
        {
            direction.y = 0f;
            return direction.sqrMagnitude > 0.0001f ? direction.normalized : fallback;
        }

        // ---- bars of the primary part --------------------------------------------------------
        //
        // A bar adds to one axis of the primary part. The other selected parts follow with the same
        // movement or turn in world space, as with a drag.

        void DrawIkBars(PadTarget target)
        {
            GuideObject primary = target.IkTarget(shownPart.Name).guideObject;
            Transform frame = primary.transformTarget.parent;
            bool reset;
            Vector3 move = AxisBars("Move", primary.changeAmount.pos, "0.000", BarMovePerPixel, out reset);
            if (move != Vector3.zero)
            {
                MoveIkTargets(target, shownSelection, frame.TransformVector(move));
                BodyEdited(target, shownSelection);
            }

            if (!primary.enableRot)
            {
                return;
            }

            // One bar moves per event, so "turn" is a turn around one of the target's own axes.
            Vector3 turn = AxisBars("Rot", SignedAngles(primary.changeAmount.rot), "0.0", DegreesPerPixel, out reset);
            if (turn != Vector3.zero)
            {
                TurnIkTargets(target, shownSelection, WorldTurn(frame.rotation, Quaternion.Euler(primary.changeAmount.rot), turn));
                BodyEdited(target, shownSelection);
            }
        }

        void DrawBoneBars(PadTarget target)
        {
            Transform primary = target.Bone(shownPart.Name);
            bool reset;
            if (shownPart.Rotate)
            {
                Quaternion rotation = Kkpe.Rotation(target, primary);
                Vector3 turn = AxisBars("Rot", SignedAngles(rotation.eulerAngles), "0.0", DegreesPerPixel, out reset);
                if (reset)
                {
                    ResetBones(target, shownSelection, false, true, false);
                }
                else if (turn != Vector3.zero)
                {
                    TurnBones(target, shownSelection, WorldTurn(primary.parent.rotation, rotation, turn), false);
                    BodyEdited(target, shownSelection);
                }
            }

            if (shownPart.Move)
            {
                Vector3 move = AxisBars("Move", Kkpe.Position(target, primary), "0.0000", BoneMovePerPixel, out reset);
                if (reset)
                {
                    ResetBones(target, shownSelection, true, false, false);
                }
                else if (move != Vector3.zero)
                {
                    MoveBones(target, shownSelection, primary.parent.TransformVector(move));
                    BodyEdited(target, shownSelection);
                }
            }

            if (shownPart.Scale)
            {
                Vector3 scale = Kkpe.Scale(target, primary);
                GUILayout.BeginHorizontal();
                GUILayout.Label("Scale", PadUi.AxisTitle, PadUi.Width(TitleWidth));
                Vector3 grow = Vector3.zero;
                bool anyReset = false;
                for (int axis = 0; axis < 3; axis++)
                {
                    grow[axis] = PadUi.DeltaBar(AxisNames[axis], scale[axis].ToString("0.00"), ScalePerPixel, PadUi.AxisColors[axis], out reset, PadUi.Width(ScaleBarWidth));
                    anyReset |= reset;
                }

                // "All" grows the three axes together.
                float all = PadUi.DeltaBar("All", "", ScalePerPixel, PadUi.Neutral, out reset, PadUi.Width(ScaleBarWidth));
                anyReset |= reset;
                GUILayout.EndHorizontal();
                grow += new Vector3(all, all, all);
                if (anyReset)
                {
                    ResetBones(target, shownSelection, false, false, true);
                }
                else if (grow != Vector3.zero)
                {
                    foreach (BodyPart part in shownSelection)
                    {
                        Transform bone = target.Bone(part.Name);
                        if (part.Scale)
                        {
                            Kkpe.SetScale(target, bone, Kkpe.Scale(target, bone) + grow);
                        }
                    }

                    BodyEdited(target, shownSelection);
                }
            }
        }

        // Middle click on a green button: the rotation of that bone goes back to its default.
        // A member of the selection takes the whole selection with it.
        void ResetRotation(PadTarget target, BodyPart part)
        {
            List<BodyPart> parts = new List<BodyPart>();
            if (selection.Contains(part))
            {
                parts.AddRange(selection);
            }
            else
            {
                parts.Add(part);
            }

            ResetBones(target, parts, false, true, false);
            Status("Rotation reset: " + part.Label + (parts.Count > 1 ? "  + " + (parts.Count - 1) : ""));
        }

        // Remove one kind of KKPE override from every listed bone that has that kind of bar.
        void ResetBones(PadTarget target, List<BodyPart> parts, bool position, bool rotation, bool scale)
        {
            foreach (BodyPart part in parts)
            {
                Transform bone = target.Bone(part.Name);
                if (bone != null)
                {
                    Kkpe.ResetBone(target, bone, position && part.Move, rotation && part.Rotate, scale && part.Scale);
                }
            }

            BodyEdited(target, parts);
        }

        // A title and three coloured bars "X Y Z" showing the values. Returns what to add on each axis.
        static Vector3 AxisBars(string title, Vector3 shown, string format, float unitsPerPixel, out bool reset)
        {
            Vector3 change = Vector3.zero;
            reset = false;
            GUILayout.BeginHorizontal();
            GUILayout.Label(title, PadUi.AxisTitle, PadUi.Width(TitleWidth));
            for (int axis = 0; axis < 3; axis++)
            {
                bool axisReset;
                change[axis] = PadUi.DeltaBar(AxisNames[axis], shown[axis].ToString(format), unitsPerPixel, PadUi.AxisColors[axis], out axisReset, PadUi.Width(AxisBarWidth));
                reset |= axisReset;
            }

            GUILayout.EndHorizontal();
            return change;
        }

        // Euler angles in -180..180, easier to read than 0..360.
        static Vector3 SignedAngles(Vector3 angles)
        {
            return new Vector3(Mathf.DeltaAngle(0f, angles.x), Mathf.DeltaAngle(0f, angles.y), Mathf.DeltaAngle(0f, angles.z));
        }

        void ResetSelectedBones(PadTarget target)
        {
            int count = 0;
            foreach (BodyPart part in selection)
            {
                Transform bone = part.Via == Via.Bone ? target.Bone(part.Name) : null;
                if (bone != null)
                {
                    Kkpe.ResetBone(target, bone, true, true, true);
                    count++;
                }
            }

            BodyEdited(target, selection);
            Status("KKPE overrides removed from " + count + " bone(s)");
        }

        // ---- Studio's Undo for the pose ----------------------------------------------------------
        //
        // A press on the tab takes a snapshot of the pose. A few frames after every mouse button is
        // released (buttons act on release, and KKPE's Swap pose and limb copies only in its next
        // IK pass) the difference becomes one step of Studio's Undo. Nothing changed: no step.

        void RememberPose(PadTarget target)
        {
            CommitPoseUndo();   // a step still waiting for KKPE
            poseBefore = new PoseSnapshot(target);
            poseReleasedFrame = -1;
        }

        // Called from Tick every frame.
        void TickBody(bool mouseHeld)
        {
            if (poseBefore == null || mouseHeld)
            {
                poseReleasedFrame = -1;
                return;
            }

            if (poseReleasedFrame < 0)
            {
                poseReleasedFrame = Time.frameCount;
            }
            else if (Time.frameCount - poseReleasedFrame >= PoseSettleFrames)
            {
                CommitPoseUndo();
            }
        }

        void CommitPoseUndo()
        {
            PoseSnapshot before = poseBefore;
            poseBefore = null;
            if (before == null || before.Target.Pose == null || !Singleton<UndoRedoManager>.IsInstance())
            {
                return;
            }

            PoseCommand command = PoseCommand.Between(before, new PoseSnapshot(before.Target));
            if (command != null)
            {
                UndoStack.Push(command, before.UndoDepth);
            }
        }

        // ---- KKPE's limb copy buttons -----------------------------------------------------------

        void CopyLimb(PadTarget target, int action)
        {
            bool done;
            switch (action)
            {
                case 0:
                    done = Kkpe.CallPose(target, "CopyLimbToTwin", ChainRightArm, GroupRightArm);
                    break;
                case 1:
                    done = Kkpe.CallPose(target, "CopyLimbToTwin", ChainLeftArm, GroupLeftArm);
                    break;
                case 2:
                    done = Kkpe.CallPose(target, "CopyHandToTwin", GroupRightHand);
                    break;
                case 3:
                    done = Kkpe.CallPose(target, "CopyHandToTwin", GroupLeftHand);
                    break;
                case 4:
                    done = Kkpe.CallPose(target, "CopyLimbToTwin", ChainRightLeg, GroupRightLeg);
                    break;
                case 5:
                    done = Kkpe.CallPose(target, "CopyLimbToTwin", ChainLeftLeg, GroupLeftLeg);
                    break;
                default:
                    done = Kkpe.CallPose(target, "SwapPose");
                    break;
            }

            if (done)
            {
                AllPartsEdited(target);
            }

            Status(done ? LimbLabels[action] + ": done by KKPE" : "This KKPE version has no " + LimbLabels[action]);
        }

        // ---- Blend Shapes of the body mesh ------------------------------------------------------

        void DrawBodyShapes(PadTarget target)
        {
            if (Event.current.type == EventType.Layout)
            {
                if (bodyShapesOwner != target.Pose)
                {
                    bodyShapesOwner = target.Pose;
                    bodyShapes.Clear();
                    foreach (BlendMesh mesh in Kkpe.Meshes(target))
                    {
                        for (int index = 0; mesh.Renderer.name == bodyShapesRenderer && index < mesh.Renderer.sharedMesh.blendShapeCount; index++)
                        {
                            ShapeRow shape = new ShapeRow();
                            shape.Mesh = mesh;
                            shape.Index = index;
                            shape.Name = mesh.Renderer.sharedMesh.GetBlendShapeName(index);
                            shape.Label = Kkpe.ShapeLabel(target, shape.Name);
                            bodyShapes.Add(shape);
                        }
                    }
                }

                foreach (ShapeRow shape in bodyShapes)
                {
                    shape.Weight = Kkpe.Weight(shape.Mesh, shape.Index);
                }
            }

            // Beside the figure and never taller than it: one column, two when one is not enough,
            // and a scroll bar when two are not enough either.
            float height = PadUi.Px(FigureRows * Pitch);
            int fitting = Mathf.Max(1, Mathf.FloorToInt((height - PadUi.Px(15f)) / (PadUi.BarHeight + 2f)));
            int columns = bodyShapes.Count > fitting ? 2 : 1;
            bool scrolls = bodyShapes.Count > fitting * 2;

            GUILayout.BeginVertical(GUILayout.Width((PadUi.Px(ShapeBarWidth) + 2f) * columns + (scrolls ? PadUi.Px(PadUi.ScrollBarWidth) + 6f : 0f)));
            GUILayout.Label(bodyShapes.Count == 0 ? "No Blend Shapes on " + bodyShapesRenderer : "Body shapes", PadUi.Heading);
            if (scrolls)
            {
                bodyShapesScroll = PadUi.BeginScroll(bodyShapesScroll, (PadUi.Px(ShapeBarWidth) + 2f) * columns + 2f, height - PadUi.Px(15f));
            }

            for (int i = 0; i < bodyShapes.Count; i += columns)
            {
                GUILayout.BeginHorizontal();
                for (int column = 0; column < columns && i + column < bodyShapes.Count; column++)
                {
                    BodyShapeBar(bodyShapes[i + column]);
                }

                GUILayout.EndHorizontal();
            }

            if (scrolls)
            {
                float rows = Mathf.Ceil(bodyShapes.Count / (float)columns);
                bodyShapesScroll = PadUi.EndScroll(bodyShapesScroll, rows * (PadUi.BarHeight + 2f) + 4f, height - PadUi.Px(15f));
            }

            GUILayout.EndVertical();
        }

        void BodyShapeBar(ShapeRow shape)
        {
            float weight = PadUi.Bar(shape.Label, shape.Weight, 0f, 100f, 0f, "0", PadUi.Width(ShapeBarWidth));
            if (weight != shape.Weight && Kkpe.SetWeight(shape.Mesh, shape.Name, weight))
            {
                shape.Weight = weight;
                Edited();
            }
        }
    }
}
