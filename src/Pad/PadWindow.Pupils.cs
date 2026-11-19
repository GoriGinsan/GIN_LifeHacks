using UnityEngine;

namespace GinPadHack
{
    // The pupils (bones cf_J_hitomi_tx_L / _R), moved through KKPE's bone editor. Their controls are
    // on the Map tab: the right mouse button drags the gaze on the map, the bars are below it.
    //
    // The controls work with four numbers:
    //   gaze    where both pupils look (the average of their X and Y)
    //   spread  how far the pupils are moved apart or together in X
    //   height  how far the left pupil sits above the right one
    //   size    their X/Y scale
    //
    // Limits (setting LimitPupils): a pupil that moves too far sideways leaves the eye, so its X
    // is clamped to PupilLeftMinX..PupilLeftMaxX (mirrored for the right pupil). The eyeball is
    // curved, so Z follows X: PupilNoseSideDepth at the limit towards the nose, PupilOuterSideDepth
    // at the outer limit, evenly in between. Which X direction is "towards the nose" is measured
    // on the character, not assumed.
    public sealed partial class PadWindow
    {
        const string LeftPupil = "cf_J_hitomi_tx_L", RightPupil = "cf_J_hitomi_tx_R";
        const float GazeRangeY = 0.025f;                  // pad edge, in the bone's local units
        const float SpreadRange = 0.012f, HeightRange = 0.010f;
        const float SizeMin = 0.5f, SizeMax = 1.5f, SizeWheelStep = 0.05f;

        static readonly int GazePadHint = "GinPadGaze".GetHashCode();

        Transform pupilLeft, pupilRight;
        Component pupilOwner;                             // the character the pupil bones belong to
        float leftNoseSign = -1f, rightNoseSign = 1f;     // +1: the pupil's local +X points to the nose
        Vector2 gaze;
        float spread, pupilHeight, pupilSize = 1f;
        Vector3 leftPosition, rightPosition, leftScale = Vector3.one, rightScale = Vector3.one;

        bool HasPupils
        {
            get
            {
                return pupilLeft != null && pupilRight != null;
            }
        }

        // Largest sideways gaze the pads offer: the wider of the two limits.
        float GazeRangeX
        {
            get
            {
                return Mathf.Max(Mathf.Abs(host.PupilLeftMinX.Value), Mathf.Abs(host.PupilLeftMaxX.Value));
            }
        }

        void FindPupils(PadTarget target)
        {
            pupilOwner = target.Pose;
            pupilLeft = target.Bone(LeftPupil);
            pupilRight = target.Bone(RightPupil);
            if (HasPupils)
            {
                leftNoseSign = NoseSign(pupilLeft, pupilRight);
                rightNoseSign = NoseSign(pupilRight, pupilLeft);
            }
        }

        // +1 when the pupil's local +X leads towards the other eye, -1 when it leads away.
        static float NoseSign(Transform pupil, Transform other)
        {
            Transform parent = pupil.parent;
            Vector3 towardsOther = parent.InverseTransformPoint(other.position) - parent.InverseTransformPoint(pupil.position);
            return towardsOther.x >= 0f ? 1f : -1f;
        }

        void ReadPupils(PadTarget target)
        {
            if (!HasPupils)
            {
                return;
            }

            leftPosition = Kkpe.Position(target, pupilLeft);
            rightPosition = Kkpe.Position(target, pupilRight);
            leftScale = Kkpe.Scale(target, pupilLeft);
            rightScale = Kkpe.Scale(target, pupilRight);
            gaze = (Vector2)(leftPosition + rightPosition) / 2f;
            spread = (leftPosition.x - rightPosition.x) / 2f;
            pupilHeight = (leftPosition.y - rightPosition.y) / 2f;
            pupilSize = (leftScale.x + rightScale.x) / 2f;
        }

        // Write gaze, spread and height to both pupils. The four numbers keep the values the user
        // asked for even while a limit holds a pupil back, so moving back releases it again.
        void WritePupils(PadTarget target)
        {
            leftPosition = Limited(new Vector3(gaze.x + spread, gaze.y + pupilHeight, leftPosition.z), leftNoseSign);
            rightPosition = Limited(new Vector3(gaze.x - spread, gaze.y - pupilHeight, rightPosition.z), rightNoseSign);
            Kkpe.SetPosition(target, pupilLeft, leftPosition);
            Kkpe.SetPosition(target, pupilRight, rightPosition);
            Edited();
        }

        void WritePupilSize(PadTarget target)
        {
            leftScale = new Vector3(pupilSize, pupilSize, leftScale.z);
            rightScale = new Vector3(pupilSize, pupilSize, rightScale.z);
            Kkpe.SetScale(target, pupilLeft, leftScale);
            Kkpe.SetScale(target, pupilRight, rightScale);
            Edited();
        }

        // Clamp X and set Z for one pupil. "noseSign" tells which way its nose side is.
        Vector3 Limited(Vector3 position, float noseSign)
        {
            if (!host.LimitPupils.Value)
            {
                return position;
            }

            // The configured range is the left pupil's; expressed towards the nose it fits both.
            float towardsNoseLimit = leftNoseSign > 0f ? host.PupilLeftMaxX.Value : -host.PupilLeftMinX.Value;
            float outwardLimit = leftNoseSign > 0f ? -host.PupilLeftMinX.Value : host.PupilLeftMaxX.Value;
            float towardsNose = Mathf.Clamp(position.x * noseSign, -outwardLimit, towardsNoseLimit);
            float outwardShare = Mathf.InverseLerp(towardsNoseLimit, -outwardLimit, towardsNose);
            return new Vector3(
                towardsNose * noseSign,
                position.y,
                Mathf.Lerp(host.PupilNoseDepth.Value, host.PupilOuterDepth.Value, outwardShare));
        }

        // ---- the gaze on the Map ------------------------------------------------------------

        // Drag with the right mouse button on the map to move the gaze (Shift: only horizontally or
        // only vertically); the wheel changes the pupil size. Only the gaze knob is drawn here.
        void GazePad(Rect pad, PadTarget target)
        {
            int id = GUIUtility.GetControlID(GazePadHint, FocusType.Passive, pad);
            Event current = Event.current;
            switch (current.GetTypeForControl(id))
            {
                case EventType.MouseDown:
                    if (current.button == 1 && pad.Contains(current.mousePosition))
                    {
                        GUIUtility.hotControl = id;
                        gazeLock.Release();
                        gaze = PadToGaze(pad, gazeLock.Pointer(current, KnobOnScreen(pad)));
                        WritePupils(target);
                        current.Use();
                    }

                    break;
                case EventType.MouseDrag:
                    if (GUIUtility.hotControl == id)
                    {
                        gaze = PadToGaze(pad, gazeLock.Pointer(current, KnobOnScreen(pad)));
                        WritePupils(target);
                        current.Use();
                    }

                    break;
                case EventType.MouseUp:
                    if (GUIUtility.hotControl == id)
                    {
                        GUIUtility.hotControl = 0;
                        gazeLock.Release();
                        current.Use();
                    }

                    break;
                case EventType.ScrollWheel:
                    if (pad.Contains(current.mousePosition))
                    {
                        pupilSize = Mathf.Clamp(pupilSize - Mathf.Sign(current.delta.y) * SizeWheelStep, SizeMin, SizeMax);
                        WritePupilSize(target);
                        current.Use();
                    }

                    break;
                case EventType.Repaint:
                    Vector2 knob = KnobOnScreen(pad);
                    float radius = PadUi.Px(3f + 4f * pupilSize);
                    PadUi.Fill(new Rect(knob.x - radius, knob.y - radius, radius * 2f, radius * 2f), new Color(.35f, .85f, 1f, 1f));
                    break;
            }
        }

        Vector2 KnobOnScreen(Rect pad)
        {
            Vector2 sign = PadSigns();
            return new Vector2(
                pad.center.x + Mathf.Clamp(sign.x * gaze.x / GazeRangeX, -1f, 1f) * pad.width / 2f,
                pad.center.y - Mathf.Clamp(sign.y * gaze.y / GazeRangeY, -1f, 1f) * pad.height / 2f);
        }

        Vector2 PadToGaze(Rect pad, Vector2 mouse)
        {
            Vector2 sign = PadSigns();
            float x = Mathf.Clamp((mouse.x - pad.center.x) / (pad.width / 2f), -1f, 1f);
            float y = Mathf.Clamp((pad.center.y - mouse.y) / (pad.height / 2f), -1f, 1f);
            return new Vector2(sign.x * x * GazeRangeX, sign.y * y * GazeRangeY);
        }

        // Which way the pupil bones' +X and +Y point on screen right now (+1: right / up).
        // Dragging right on the map therefore moves the pupils right on screen from any camera
        // angle, also when the face is seen upside down.
        Vector2 PadSigns()
        {
            Camera view = Singleton<Studio.Studio>.IsInstance() ? Singleton<Studio.Studio>.Instance.cameraCtrl.mainCmaera : null;
            Transform frame = HasPupils ? pupilLeft.parent : null;
            float x = -1f, y = 1f;
            if (view != null && frame != null)
            {
                x = Vector3.Dot(frame.right, view.transform.right) >= 0f ? 1f : -1f;
                y = Vector3.Dot(frame.up, view.transform.up) >= 0f ? 1f : -1f;
            }

            return new Vector2(x, y);
        }
    }
}
