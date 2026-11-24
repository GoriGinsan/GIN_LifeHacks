using GinShared;
using Studio;
using UnityEngine;

namespace GinPadHack
{
    // Body tab: the small grey trapezoid between the feet moves the whole character (Studio's own
    // position of the character, the guide object of its root), neither a bone nor an IK target.
    // Its grey and its shape keep it apart from the figure's buttons.
    //
    //   drag           move the character horizontally (floor plane), as seen from the camera
    //   right drag     move the character up and down
    //   click          select the character's root in Studio, for Studio's own gizmo
    //   middle click   key the character's position row in the Timeline at the playhead
    //
    // It is not part of the figure's selection; Ctrl + Z undoes a drag like any edit of the tab
    // (PoseUndo.cs keeps the root's position).
    public sealed partial class PadWindow
    {
        internal const string RootLabel = "Root";

        // Inside the figure at UiScale 1: between the toes, under the feet.
        internal static readonly Rect RootPlace = new Rect(127f, 10 * Pitch + (PartHeight - SmallHeight) / 2f, 52f, SmallHeight);

        static readonly int RootHint = "GinPadRoot".GetHashCode();

        int rootPressButton;
        bool rootPressDragged;

        static GuideObject RootGuide(PadTarget target)
        {
            return target.Character == null ? null : target.Character.guideObject;
        }

        static bool RootUsable(PadTarget target)
        {
            GuideObject guide = RootGuide(target);
            return guide != null && guide.enablePos && guide.transformTarget != null;
        }

        // Move the character's root by a world movement.
        internal static void MoveRoot(PadTarget target, Vector3 world)
        {
            GuideObject guide = RootGuide(target);
            if (guide == null || !guide.enablePos)
            {
                return;
            }

            Transform parent = guide.transformTarget.parent;
            guide.changeAmount.pos += parent == null ? world : parent.InverseTransformVector(world);
        }

        void RootButton(Rect place, PadTarget target)
        {
            int id = GUIUtility.GetControlID(RootHint, FocusType.Passive, place);
            Event current = Event.current;
            bool usable = !delayEditShown && RootUsable(target);
            Vector2 moved;
            if (PadUi.TakeHeldMovement(id, current.delta, out moved))   // once per frame, beyond the screen edge
            {
                rootPressDragged = true;
                DragRoot(target, moved * (PadUi.Fine(current) ? PadUi.FineFactor : 1f) * PadUi.Sensitivity, rootPressButton == 1);
            }

            switch (current.GetTypeForControl(id))
            {
                case EventType.MouseDown:
                    if (!usable || !PadUi.InsideTrapezoid(place, current.mousePosition))
                    {
                        break;
                    }

                    if (current.button == 0 || current.button == 1)
                    {
                        GUIUtility.hotControl = id;
                        rootPressButton = current.button;
                        rootPressDragged = false;
                        current.Use();
                    }
                    else if (current.button == 2)
                    {
                        KeyRoot(target);
                        current.Use();
                    }

                    break;
                case EventType.MouseUp:
                    if (GUIUtility.hotControl == id)
                    {
                        GUIUtility.hotControl = 0;
                        EndlessDrag.End();
                        if (!rootPressDragged && rootPressButton == 0 && Singleton<GuideObjectManager>.IsInstance())
                        {
                            Singleton<GuideObjectManager>.Instance.selectObject = RootGuide(target);
                            Status("Root selected in Studio: drag it to move the whole character");
                        }

                        current.Use();
                    }

                    break;
                case EventType.Repaint:
                    bool hover = usable && PadUi.InsideTrapezoid(place, current.mousePosition);
                    bool on = usable && Singleton<GuideObjectManager>.IsInstance() && Singleton<GuideObjectManager>.Instance.selectObject == RootGuide(target);
                    PadUi.DrawTrapezoidButton(place, RootLabel, hover, GUIUtility.hotControl == id, on, usable);
                    break;
            }
        }

        // The same movement as a drag on an IK target button (DragSelection).
        void DragRoot(PadTarget target, Vector2 pixels, bool rightButton)
        {
            if (rightButton)
            {
                MoveRoot(target, Vector3.up * (-pixels.y * DragMovePerPixel));
            }
            else
            {
                Transform view = Singleton<Studio.Studio>.Instance.cameraCtrl.mainCmaera.transform;
                Vector3 right = Flat(view.right, Vector3.right);
                Vector3 away = Flat(view.forward, Flat(view.up, Vector3.forward));
                MoveRoot(target, (right * pixels.x - away * pixels.y) * DragMovePerPixel);
            }

            Edited();
        }

        void KeyRoot(PadTarget target)
        {
            GuideObject guide = RootGuide(target);
            Status(TimelineBridge.KeyNamed(target.Character, new string[] { guide.transformTarget.name }));
        }
    }
}
