using System.Collections.Generic;
using Studio;
using UnityEngine;

namespace GinPadHack
{
    // Item mode (see PadWindow.cs): Move Controller's controls (MoveControllerKOI) inside the Pad.
    //
    // Move tab
    //   square            left drag: move on the floor plane as seen from the camera,
    //                     right drag: move up and down. At Sens 1 the selection keeps up with the
    //                     mouse on screen; Shift is finer.
    //   View  X  Y  Z     turn around the camera's right axis, the world's up axis and the viewing
    //                     direction. A right drag on Y turns the selection around the first object,
    //                     as a group.
    //   Own   X  Y  Z     turn around the object's own axes (red, green, blue); while an FK node is
    //                     selected, the FK node(s) instead
    //   click (no drag)   on a turn button: 90 degrees (right click: 90 degrees back)
    //   To camera         move to the camera's target point (right click: the first object goes
    //                     there, the others keep their places around it)
    //   Anim              drag: scrub the animation of animated items and characters
    //   Reset FK          the FK node(s) back to no rotation
    //   wheel             with an FK node selected: the next / previous node of the chain.
    //                     Alt: always a single node; Shift: grow or shrink a range of nodes that
    //                     turn together; plain: move the node, or the range, along the chain
    //   Every move and turn is one step of Studio's Undo.
    //
    // Shapes tab: the Blend Shapes of the first selected item that has any, through KKPE, so they
    // are saved with the scene and animated by Timeline. Middle click a bar to key its renderer.
    // KKPE keeps its editor off on a new item; the first edit here switches it on (no need for
    // KKPE's right click > Enable).
    public sealed partial class PadWindow
    {
        enum ItemTab
        {
            Move,
            Shapes
        }

        static readonly string[] ItemTabNames = { "Move", "Shapes" };

        // At UiScale 1.
        const float MoveSquareSize = 96f, TurnButtonWidth = 58f, TurnButtonHeight = 29f, RowTitleWidth = 34f;
        const float ItemDegreesPerPixel = 0.5f, ClickTurnDegrees = 90f, AnimSecondsPerPixel = 0.01f;

        static readonly Color TurnGrey = new Color(0.40f, 0.41f, 0.46f, 1f);
        static readonly Color ToolColor = new Color(0.27f, 0.29f, 0.34f, 1f);
        static readonly Color Unusable = new Color(0.19f, 0.19f, 0.21f, 1f);

        readonly FkWalk fkWalk = new FkWalk();
        readonly Dictionary<int, Vector3[]> undoBefore = new Dictionary<int, Vector3[]>();   // key -> position, rotation
        readonly List<ShapeRow> itemShapes = new List<ShapeRow>();
        PadTarget itemShapesOwner;
        bool itemShapesOff;            // KKPE's editor on the item is still off (read in the Layout pass)
        Vector2 itemShapesScroll;

        void DrawItemTabs(ItemSelection items)
        {
            GUILayout.BeginHorizontal();
            bool any = false;
            if (shownItemTabs[(int)ItemTab.Move])
            {
                GUILayout.BeginVertical(GUILayout.ExpandWidth(false));
                DrawItemMove(items);
                GUILayout.EndVertical();
                GUILayout.Space(PadUi.Px(4f));
                any = true;
            }

            if (shownItemTabs[(int)ItemTab.Shapes] && items.Shapes != null)
            {
                GUILayout.BeginVertical(GUILayout.ExpandWidth(false));
                DrawItemShapes(items.Shapes);
                GUILayout.EndVertical();
                any = true;
            }

            if (!any)
            {
                GUILayout.Label("Open Move" + (items.Shapes != null ? " or Shapes" : ""), PadUi.Small);
            }

            GUILayout.EndHorizontal();
        }

        // ---- Move tab --------------------------------------------------------------------------

        void DrawItemMove(ItemSelection items)
        {
            if (Event.current.type == EventType.Layout)
            {
                fkWalk.Sync(items.Objects);
            }

            Vector2 pixels;
            int button;
            GUILayout.BeginVertical();
            GUILayout.BeginHorizontal();
            PadUi.Press press = PadUi.DragButton("Move\nL: floor\nR: up / down", ToolColor, MoveSquareSize, MoveSquareSize, out pixels, out button);
            if (press != PadUi.Press.None)
            {
                MoveSquare(items, press, pixels, button);
            }

            GUILayout.BeginVertical();
            GUILayout.BeginHorizontal();
            GUILayout.Label("View", PadUi.AxisTitle, PadUi.Width(RowTitleWidth));
            for (int axis = 0; axis < 3; axis++)
            {
                press = PadUi.DragButton(AxisNames[axis], TurnGrey, TurnButtonWidth, TurnButtonHeight, out pixels, out button);
                if (press != PadUi.Press.None)
                {
                    TurnInView(items, axis, press, pixels, button);
                }
            }

            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label(fkWalk.HasBone ? "FK" : "Own", PadUi.AxisTitle, PadUi.Width(RowTitleWidth));
            for (int axis = 0; axis < 3; axis++)
            {
                press = PadUi.DragButton(AxisNames[axis], PadUi.AxisColors[axis], TurnButtonWidth, TurnButtonHeight, out pixels, out button);
                if (press != PadUi.Press.None)
                {
                    TurnOwnAxis(items, axis, press, pixels, button);
                }
            }

            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Space(PadUi.Px(RowTitleWidth) + 2f);
            press = PadUi.DragButton("To camera", ToolColor, TurnButtonWidth, TurnButtonHeight, out pixels, out button);
            if (press == PadUi.Press.Clicked)
            {
                MoveToCamera(items, button == 1);
            }

            bool animated = items.Animators().Count > 0;
            press = PadUi.DragButton("Anim", animated ? ToolColor : Unusable, TurnButtonWidth, TurnButtonHeight, out pixels, out button);
            if (animated && press == PadUi.Press.Dragged)
            {
                ScrubAnimations(items, pixels.x);
            }

            press = PadUi.DragButton("Reset FK", fkWalk.HasBone ? ToolColor : Unusable, TurnButtonWidth, TurnButtonHeight, out pixels, out button);
            if (fkWalk.HasBone && press == PadUi.Press.Clicked)
            {
                ResetFk();
            }

            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();

            string caption = fkWalk.HasBone
                ? "FK " + fkWalk.ActiveName + (fkWalk.Count > 1 ? "  (" + fkWalk.Count + " nodes)" : "") + "   wheel: next node"
                : items.Roots.Count + (items.Roots.Count == 1 ? " object" : " objects");
            GUILayout.Label(caption, PadUi.Small, PadUi.Width(MoveSquareSize + RowTitleWidth + 3f * TurnButtonWidth + 8f));
            GUILayout.EndVertical();
            StepFkWithWheel(GUILayoutUtility.GetLastRect());
        }

        // Left drag: on the floor plane as seen from the camera. Right drag: up and down.
        void MoveSquare(ItemSelection items, PadUi.Press press, Vector2 pixels, int button)
        {
            if (!Step(items, press))
            {
                return;
            }

            Transform view = Singleton<Studio.Studio>.Instance.cameraCtrl.mainCmaera.transform;
            Vector3 right = Flat(view.right, Vector3.right);
            Vector3 away = Flat(view.forward, Flat(view.up, Vector3.forward));
            Vector3 world = button == 1 ? Vector3.up * -pixels.y : right * pixels.x - away * pixels.y;
            world *= WorldPerPixel(items.Roots[0].guideObject.transformTarget.position);
            foreach (ObjectCtrlInfo info in items.Roots)
            {
                info.guideObject.MoveWorld(world);
            }
        }

        // Grey buttons: around the camera's right axis, the world's up axis, the viewing direction.
        void TurnInView(ItemSelection items, int axis, PadUi.Press press, Vector2 pixels, int button)
        {
            float degrees = TurnDegrees(press, pixels, button);
            if (!Step(items, press) || degrees == 0f)
            {
                return;
            }

            Transform view = Singleton<Studio.Studio>.Instance.cameraCtrl.mainCmaera.transform;
            Vector3 around = axis == 0 ? view.right : axis == 1 ? Vector3.up : view.forward;
            bool asGroup = axis == 1 && button == 1 && press == PadUi.Press.Dragged;
            Vector3 pivot = items.Roots[0].guideObject.transformTarget.position;
            foreach (ObjectCtrlInfo info in items.Roots)
            {
                GuideObject guide = info.guideObject;
                if (asGroup)
                {
                    Vector3 position = guide.transformTarget.position;
                    guide.MoveWorld(pivot + Quaternion.AngleAxis(degrees, around) * (position - pivot) - position);
                }

                guide.Rotation(around, degrees);
            }
        }

        // Coloured buttons: around the object's own axes, or the selected FK node(s).
        void TurnOwnAxis(ItemSelection items, int axis, PadUi.Press press, Vector2 pixels, int button)
        {
            float degrees = TurnDegrees(press, pixels, button);
            if (!Step(items, press) || degrees == 0f)
            {
                return;
            }

            Vector3 turn = Vector3.zero;
            turn[axis] = degrees;
            List<OCIChar.BoneInfo> bones = fkWalk.Bones();
            if (bones.Count > 0)
            {
                foreach (OCIChar.BoneInfo bone in bones)
                {
                    bone.guideObject.changeAmount.rot = TurnedOnOwnAxes(bone.guideObject.changeAmount.rot, turn);
                }
            }
            else
            {
                foreach (ObjectCtrlInfo info in items.Roots)
                {
                    info.guideObject.changeAmount.rot = TurnedOnOwnAxes(info.guideObject.changeAmount.rot, turn);
                }
            }
        }

        static Vector3 TurnedOnOwnAxes(Vector3 euler, Vector3 turn)
        {
            return (Quaternion.Euler(euler) * Quaternion.Euler(turn)).eulerAngles;
        }

        // A drag turns with the mouse; a click without a drag turns by 90 degrees (right: back).
        static float TurnDegrees(PadUi.Press press, Vector2 pixels, int button)
        {
            if (press == PadUi.Press.Dragged)
            {
                return -pixels.x * ItemDegreesPerPixel;
            }

            if (press == PadUi.Press.Clicked)
            {
                return button == 1 ? -ClickTurnDegrees : ClickTurnDegrees;
            }

            return 0f;
        }

        void MoveToCamera(ItemSelection items, bool keepArrangement)
        {
            Vector3 target = Singleton<Studio.Studio>.Instance.cameraCtrl.targetPos;
            Vector3 first = items.Roots[0].guideObject.transformTarget.position;
            BeginUndo(items);
            foreach (ObjectCtrlInfo info in items.Roots)
            {
                Vector3 position = info.guideObject.transformTarget.position;
                Vector3 goal = keepArrangement ? target + position - first : target;
                info.guideObject.MoveWorld(goal - position);
            }

            CommitUndo();
        }

        void ScrubAnimations(ItemSelection items, float pixels)
        {
            foreach (Animator animator in items.Animators())
            {
                AnimatorClipInfo[] clips = animator.GetCurrentAnimatorClipInfo(0);
                if (clips.Length == 0 || clips[0].clip == null || clips[0].clip.length <= 0f)
                {
                    continue;
                }

                AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
                float time = state.normalizedTime + pixels * AnimSecondsPerPixel / clips[0].clip.length;
                animator.Play(0, 0, state.loop ? time : Mathf.Clamp01(time));   // 0: the current state
            }
        }

        void ResetFk()
        {
            List<OCIChar.BoneInfo> bones = fkWalk.Bones();
            BeginUndo(null);
            foreach (OCIChar.BoneInfo bone in bones)
            {
                Remember(bone.guideObject);
                bone.guideObject.changeAmount.rot = Vector3.zero;
            }

            CommitUndo();
            Status("FK rotation reset: " + bones.Count + " node(s)");
        }

        // The wheel anywhere on the Move tab steps through the FK chain.
        void StepFkWithWheel(Rect tab)
        {
            Event current = Event.current;
            if (current.type != EventType.ScrollWheel || !tab.Contains(current.mousePosition) || current.control || !fkWalk.HasBone)
            {
                return;
            }

            fkWalk.Step(current.delta.y < 0f ? 1 : -1, current.shift, current.alt);
            current.Use();
        }

        // How far the mouse moves an object at this position, in world units per screen pixel:
        // at Sens 1 the object keeps up with the mouse on screen.
        static float WorldPerPixel(Vector3 position)
        {
            Camera view = Singleton<Studio.Studio>.Instance.cameraCtrl.mainCmaera;
            float distance = Mathf.Max(0.1f, Vector3.Dot(position - view.transform.position, view.transform.forward));
            return 2f * distance * Mathf.Tan(view.fieldOfView * 0.5f * Mathf.Deg2Rad) / Screen.height;
        }

        // ---- undo ------------------------------------------------------------------------------
        //
        // A press remembers where everything was; the release (or a click) becomes one Undo step.

        // Called for every press phase of a move or turn button; false when nothing is to be done.
        bool Step(ItemSelection items, PadUi.Press press)
        {
            if (items.Roots.Count == 0)
            {
                return false;
            }

            switch (press)
            {
                case PadUi.Press.Down:
                    BeginUndo(items);
                    return false;
                case PadUi.Press.Dragged:
                    Edited();
                    return true;
                case PadUi.Press.Clicked:
                    BeginUndo(items);   // a click turns once, after the press was remembered
                    return true;
                default:
                    return false;
            }
        }

        void BeginUndo(ItemSelection items)
        {
            undoBefore.Clear();
            if (items == null)
            {
                return;
            }

            foreach (ObjectCtrlInfo info in items.Roots)
            {
                Remember(info.guideObject);
            }

            foreach (OCIChar.BoneInfo bone in fkWalk.Bones())
            {
                Remember(bone.guideObject);
            }
        }

        void Remember(GuideObject guide)
        {
            undoBefore[guide.dicKey] = new Vector3[] { guide.changeAmount.pos, guide.changeAmount.rot };
        }

        // Runs in Update too, so that a release the window did not see still ends the step.
        void CommitUndo()
        {
            if (undoBefore.Count == 0)
            {
                return;
            }

            TransformsCommand command = new TransformsCommand();
            foreach (KeyValuePair<int, Vector3[]> before in undoBefore)
            {
                ChangeAmount now = Studio.Studio.GetChangeAmount(before.Key);
                if (now != null && (now.pos != before.Value[0] || now.rot != before.Value[1]))
                {
                    command.Add(before.Key, before.Value[0], now.pos, before.Value[1], now.rot);
                }
            }

            undoBefore.Clear();
            if (command.Count > 0 && Singleton<UndoRedoManager>.IsInstance())
            {
                Singleton<UndoRedoManager>.Instance.Push(command);
            }
        }

        // Called from Tick. A click is committed while it is handled; a drag when the mouse is up.
        void TickItems(bool mouseHeld)
        {
            if (!mouseHeld)
            {
                CommitUndo();
            }
        }

        // ---- Shapes tab --------------------------------------------------------------------------

        void DrawItemShapes(PadTarget item)
        {
            if (Event.current.type == EventType.Layout)
            {
                ReadItemShapes(item);
            }

            // Each renderer under its name; one column of bars, two when there are many, and a scroll
            // bar when they do not fit on the screen.
            float room = Mathf.Max(160f, Screen.height - 260f);
            int columns = itemShapes.Count > 16 ? 2 : 1;
            float height = 0f;
            for (int start = 0; start < itemShapes.Count; start = RendererEnd(start))
            {
                int rows = (RendererEnd(start) - start + columns - 1) / columns;
                height += PadUi.Px(15f) + rows * (PadUi.BarHeight + 2f);
            }

            bool scrolls = height > room;
            float width = (PadUi.Px(ShapeBarWidth) + 2f) * columns;
            GUILayout.BeginVertical(GUILayout.Width(width + (scrolls ? PadUi.Px(PadUi.ScrollBarWidth) + 6f : 0f)));
            if (itemShapesOff)
            {
                GUILayout.Label("KKPE is off on this item: the first edit switches it on", PadUi.Small, GUILayout.Width(width));
            }

            if (scrolls)
            {
                itemShapesScroll = PadUi.BeginScroll(itemShapesScroll, width + 2f, room);
            }

            for (int start = 0; start < itemShapes.Count; start = RendererEnd(start))
            {
                int end = RendererEnd(start);
                GUILayout.Label(itemShapes[start].Mesh.Renderer.name, PadUi.Heading);
                for (int i = start; i < end; i += columns)
                {
                    GUILayout.BeginHorizontal();
                    for (int column = 0; column < columns && i + column < end; column++)
                    {
                        ItemShapeBar(item, itemShapes[i + column]);
                    }

                    GUILayout.EndHorizontal();
                }
            }

            if (scrolls)
            {
                itemShapesScroll = PadUi.EndScroll(itemShapesScroll, height + 4f, room);
            }

            GUILayout.EndVertical();
        }

        // Every Blend Shape of the item's renderers, with KKPE's current weights.
        void ReadItemShapes(PadTarget item)
        {
            if (itemShapesOwner != item)
            {
                itemShapesOwner = item;
                itemShapes.Clear();
                foreach (BlendMesh mesh in Kkpe.Meshes(item))
                {
                    Mesh shapes = mesh.Renderer.sharedMesh;
                    for (int index = 0; index < shapes.blendShapeCount; index++)
                    {
                        ShapeRow shape = new ShapeRow();
                        shape.Mesh = mesh;
                        shape.Index = index;
                        shape.Name = shapes.GetBlendShapeName(index);
                        shape.Label = Kkpe.ShapeLabel(item, shape.Name);
                        itemShapes.Add(shape);
                    }
                }
            }

            foreach (ShapeRow shape in itemShapes)
            {
                shape.Weight = Kkpe.Weight(shape.Mesh, shape.Index);
            }

            itemShapesOff = !Kkpe.IsSwitchedOn(item);
        }

        // KKPE leaves its editor off on a new item, and an editor that is off shows no weight, so
        // the first edit switches it on (what KKPE's right click > Enable does by hand).
        void SetItemShape(PadTarget item, ShapeRow shape, float weight)
        {
            if (Kkpe.SwitchOn(item))
            {
                Status("KKPE's Advanced mode switched on for " + item.Name);
            }

            if (Kkpe.SetWeight(shape.Mesh, shape.Name, weight))
            {
                shape.Weight = weight;
                Edited();
            }
        }

        // The index after the last shape of the renderer that the shape at "start" belongs to.
        int RendererEnd(int start)
        {
            int end = start;
            while (end < itemShapes.Count && itemShapes[end].Mesh == itemShapes[start].Mesh)
            {
                end++;
            }

            return end;
        }

        void ItemShapeBar(PadTarget item, ShapeRow shape)
        {
            float weight = PadUi.Bar(shape.Label, shape.Weight, 0f, 100f, 0f, "0", PadUi.Width(ShapeBarWidth));
            if (weight != shape.Weight)
            {
                SetItemShape(item, shape, weight);
            }

            if (PadUi.TakeKeyRequest())
            {
                Status(TimelineBridge.KeyNamed(item.Owner, new string[] { shape.Mesh.Renderer.name }));
            }
        }
    }
}
