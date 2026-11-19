using System.Runtime.InteropServices;
using UnityEngine;

namespace GinShared
{
    // Endless drags, the way Move Controller (MoveControllerKOI) and Studio's own camera do them.
    //
    // A drag that changes a value by how far the mouse moves (a bar, a drag button) would stop at
    // the edge of the screen. So once such a drag really moves, the cursor is hidden and parked in
    // the middle of the game's screen, put back there every frame, and the movement is read from
    // the mouse itself (Input "Mouse X" / "Mouse Y"), which goes on beyond any edge. On release
    // the cursor shows again where the drag began to move.
    //
    // Why the middle and not where the drag began (as Move Controller does): a cursor held at the
    // screen edge cannot move further out, Windows then reports no movement and the drag gets no
    // more events. Studio's camera does not take over the parked cursor: it only starts to turn on
    // a press that is not on a window, and these drags all begin on one.
    //
    // Speed: the mouse axes count the mouse's own movement, without Windows' pointer speed and
    // acceleration (in the test game one axis unit was 20 counts of the mouse, while the visible
    // cursor moved up to twice as far as the counts). So how many screen pixels one axis unit is
    // worth is measured from the cursor's free movement whenever no drag holds it, and a held
    // drag runs as fast as the visible cursor did.
    //
    // This file is compiled into each GIN LifeHacks plugin (see build.ps1); every plugin has its
    // own copy of the state below.
    //
    // Use:
    //   every drag event      movement = EndlessDrag.Moved(what the event itself reports)
    //   release               EndlessDrag.End()
    //   the plugin's Update   EndlessDrag.Update()   (reads the mouse, keeps the cursor parked,
    //                                                and lets go after a release that no control
    //                                                reported)
    internal static class EndlessDrag
    {
        // Pixels per axis unit until enough free movement was seen: one count of the mouse is one
        // pixel at Windows' default pointer speed without acceleration.
        const float DefaultPixelsPerAxisUnit = 20f;
        const float MinPixelsPerAxisUnit = 5f, MaxPixelsPerAxisUnit = 100f;
        const float MeasuredEnough = 20f;   // axis units of free movement before the measurement counts
        const float MeasureWindow = 300f;   // older movement fades once this much is measured

        [StructLayout(LayoutKind.Sequential)]
        struct ScreenPoint
        {
            public int X, Y;
        }

        [DllImport("user32.dll")]
        static extern bool GetCursorPos(out ScreenPoint point);

        [DllImport("user32.dll")]
        static extern bool SetCursorPos(int x, int y);

        static bool holding;           // a drag of this plugin holds the cursor
        static bool parked;            // the cursor really was moved (not in a test run in the background)
        static ScreenPoint home, park; // desktop pixels: where the drag began to move, and the parking place
        static Vector2 pending;        // mouse movement since the last drag event, GUI pixels (y down)
        static float heldGain;         // pixels per axis unit for the drag in progress
        static int heldFrame;          // the frame in which holding began

        static float measuredPixels, measuredUnits;   // free cursor movement: screen pixels and axis units
        static Vector2 lastPointer;
        static bool lastPointerUsable;

        internal static bool Holding
        {
            get
            {
                return holding;
            }
        }

        // The movement of one drag event in GUI pixels (y down). Until the cursor is held this is
        // what the event reports, and the first real movement starts holding it; from then on it
        // is the mouse's own movement since the previous event.
        internal static Vector2 Moved(Vector2 eventMovement)
        {
            if (holding)
            {
                Vector2 moved = pending;
                pending = Vector2.zero;
                return moved;
            }

            if (eventMovement != Vector2.zero)
            {
                Begin();
            }

            return eventMovement;
        }

        static void Begin()
        {
            holding = true;
            pending = Vector2.zero;
            heldGain = PixelsPerAxisUnit();
            heldFrame = Time.frameCount;

            // Park only a cursor the user is driving; a test run in the background leaves it alone.
            parked = Application.isFocused && GetCursorPos(out home);
            if (parked)
            {
                // The middle of the game's screen in desktop pixels: Unity counts from the bottom
                // left of the game's screen, the desktop from the top left.
                Vector2 pointer = Input.mousePosition;
                park.X = home.X + Mathf.RoundToInt(Screen.width * 0.5f - pointer.x);
                park.Y = home.Y + Mathf.RoundToInt(pointer.y - Screen.height * 0.5f);
                Cursor.visible = false;
                SetCursorPos(park.X, park.Y);
            }
        }

        internal static void End()
        {
            if (!holding)
            {
                return;
            }

            holding = false;
            pending = Vector2.zero;
            if (parked)
            {
                parked = false;
                SetCursorPos(home.X, home.Y);
                Cursor.visible = true;
            }

            lastPointerUsable = false;   // the jump back home is no movement of the user's
        }

        internal static void Update()
        {
            Vector2 axes = new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y"));
            if (!holding)
            {
                MeasureFreeMovement(axes);
                return;
            }

            if (!Input.GetMouseButton(0) && !Input.GetMouseButton(1) && !Input.GetMouseButton(2))
            {
                End();
                return;
            }

            // In the frame in which holding began, the event that began it already reported this
            // frame's movement (a uGUI drag event can come before this Update in the same frame).
            if (Time.frameCount != heldFrame)
            {
                pending += new Vector2(axes.x, -axes.y) * heldGain;
            }

            if (parked)
            {
                SetCursorPos(park.X, park.Y);
            }
        }

        // How far the visible cursor moved for the axes' movement in this frame. Only frames in
        // which nothing pins the cursor (Studio's camera holds it while turning) and the cursor
        // stays inside the game's screen, where Windows does not stop it at an edge.
        static void MeasureFreeMovement(Vector2 axes)
        {
            Vector2 pointer = Input.mousePosition;
            bool usable = Application.isFocused && !GameCursor.isLock
                && pointer.x >= 1f && pointer.y >= 1f && pointer.x <= Screen.width - 2f && pointer.y <= Screen.height - 2f;
            if (usable && lastPointerUsable)
            {
                AddMeasurement((pointer - lastPointer).magnitude, axes.magnitude);
            }

            lastPointer = pointer;
            lastPointerUsable = usable;
        }

        internal static void AddMeasurement(float pixels, float units)
        {
            // Too small to say anything, or a jump of the cursor that the mouse did not make
            // (a program put it somewhere else).
            if (pixels < 1f || units < 0.05f || pixels / units > MaxPixelsPerAxisUnit * 1.5f)
            {
                return;
            }

            measuredPixels += pixels;
            measuredUnits += units;
            if (measuredUnits > MeasureWindow)
            {
                measuredPixels *= 0.5f;
                measuredUnits *= 0.5f;
            }
        }

        internal static float PixelsPerAxisUnit()
        {
            if (measuredUnits < MeasuredEnough)
            {
                return DefaultPixelsPerAxisUnit;
            }

            return Mathf.Clamp(measuredPixels / measuredUnits, MinPixelsPerAxisUnit, MaxPixelsPerAxisUnit);
        }

        // For the isolated tests.
        internal static void AddMovementForTest(Vector2 pixels)
        {
            pending += pixels;
        }

        internal static void ForgetMeasurementForTest()
        {
            measuredPixels = measuredUnits = 0f;
        }
    }
}
