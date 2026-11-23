using UnityEngine;

namespace GinPadHack
{
    // Small controls just outside the window's top corners; they move with the window.
    //
    //   top left    ▶ / ❚❚   play or pause the Timeline (needs Timeline itself, not its GIN helper)
    //               ↔        drag left or right to move the playhead. Sens and Shift work as on the
    //                        bars, playback pauses first, and the drag goes on beyond the screen
    //                        edge. While it is held, and a moment after, a large readout on the
    //                        window shows the time, how far it moved since the press, the frame
    //                        and where the playhead is in the whole animation.
    //   top right   knob     drag right to enlarge the whole window, left to shrink it. Everything
    //                        scales together (the UiScale setting), so the proportions stay; the
    //                        size is remembered when the knob is let go. Right click: the default.
    //
    // They are ordinary IMGUI controls drawn after the window (IMGUI gives them the mouse before
    // the window), and OnWindow counts them as part of the window, so the camera, Studio's gizmo
    // and Studio's own buttons do not take presses on them. The window is kept on the screen far
    // enough for them to stay reachable.
    public sealed partial class PadWindow
    {
        // At UiScale 1.
        const float CornerButtonWidth = 24f, CornerButtonHeight = 21f, CornerGap = 2f;
        const float KnobSize = 7f, KnobGrabSize = 14f;   // the knob as drawn, and the area that takes the mouse
        const float ReadoutWidth = 250f, ReadoutBigRow = 36f, ReadoutSmallRow = 26f, ReadoutTrack = 9f;

        const float SecondsPerPixel = 1f / 2400f;  // of the animation's length: 2400 pixels cross it at Sens 1
        const float MinSecondsPerPixel = 0.0025f;
        const float ReadoutSeconds = 1.5f;         // the readout stays this long after the release

        static readonly int KnobHint = "GinPadResizeKnob".GetHashCode();
        static readonly Color ReadoutBack = new Color(0.05f, 0.05f, 0.07f, 0.93f);
        static readonly Color Later = new Color(0.55f, 0.90f, 0.55f, 1f), Earlier = new Color(1f, 0.70f, 0.40f, 1f);
        static readonly Color KnobIdle = new Color(0.72f, 0.76f, 0.84f, 1f), KnobLit = new Color(0.45f, 0.72f, 1f, 1f);

        bool timeDragging;
        float timeAtPress;                 // the playhead when the time drag began
        float readoutUntil = -1f;          // realtime until which the readout stays after the release

        float resizeScale = -1f;           // the scale while the knob is held; -1 when it is not
        float knobStartX, knobStartWidth, knobStartScale;

        // The scale the window is drawn at: the knob's while it is held, otherwise the setting.
        float CurrentScale
        {
            get
            {
                return resizeScale > 0f ? resizeScale : host.UiScale.Value;
            }
        }

        static bool ShowsTimeButtons
        {
            get
            {
                return TimelineBridge.HasPlayback;
            }
        }

        // ---- where -------------------------------------------------------------------------------

        Rect PlayRect()
        {
            float width = PadUi.Px(CornerButtonWidth);
            return new Rect(drawn.x - width - PadUi.Px(CornerGap), drawn.y, width, PadUi.Px(CornerButtonHeight));
        }

        Rect TimeDragRect()
        {
            Rect play = PlayRect();
            return new Rect(play.x, play.yMax + PadUi.Px(CornerGap), play.width, play.height);
        }

        // The area that takes the mouse: twice the drawn knob, reaching right and down from it
        // (away from the window), so the small knob is still easy to grab.
        Rect KnobRect()
        {
            float size = PadUi.Px(KnobGrabSize);
            return new Rect(drawn.xMax + PadUi.Px(CornerGap), drawn.y, size, size);
        }

        // The knob as drawn: in the grab area's top left corner, beside the window's top right corner.
        Rect KnobDrawnRect()
        {
            Rect grab = KnobRect();
            return new Rect(grab.x, grab.y, PadUi.Px(KnobSize), PadUi.Px(KnobSize));
        }

        // The window and its corner controls: what the camera and click guards treat as the window.
        bool OnWindow(Vector2 point)
        {
            if (drawn.Contains(point) || KnobRect().Contains(point))
            {
                return true;
            }

            return ShowsTimeButtons && (PlayRect().Contains(point) || TimeDragRect().Contains(point));
        }

        // The window stays on the screen together with its corner controls.
        void KeepOnScreen()
        {
            float left = ShowsTimeButtons ? PadUi.Px(CornerButtonWidth + CornerGap) : 0f;
            float right = PadUi.Px(KnobGrabSize + CornerGap);
            window.x = Mathf.Clamp(window.x, left, Mathf.Max(left, Screen.width - drawn.width - right));
            window.y = Mathf.Clamp(window.y, 0f, Mathf.Max(0f, Screen.height - 24f));
        }

        // ---- drawing (Draw calls this after the window) -------------------------------------------

        void DrawCorners()
        {
            if (ShowsTimeButtons)
            {
                if (GUI.Button(PlayRect(), TimelineBridge.IsPlaying ? "❚❚" : "▶", PadUi.CornerButton))
                {
                    TimelineBridge.TogglePlay();
                }

                Vector2 pixels;
                int button;
                TimeDrag(PadUi.DragButton(TimeDragRect(), "↔", PadUi.Neutral, out pixels, out button), pixels);
            }

            ResizeKnob();
        }

        // ---- the time drag ---------------------------------------------------------------------

        void TimeDrag(PadUi.Press press, Vector2 pixels)
        {
            switch (press)
            {
                case PadUi.Press.Down:
                    TimelineBridge.Pause();
                    timeAtPress = TimelineBridge.PlaybackTime;
                    timeDragging = true;
                    break;
                case PadUi.Press.Dragged:
                    float step = Mathf.Max(MinSecondsPerPixel, TimelineBridge.Duration * SecondsPerPixel);
                    TimelineBridge.Seek(TimelineBridge.PlaybackTime + pixels.x * step);
                    break;
                case PadUi.Press.Released:
                case PadUi.Press.Clicked:
                    EndTimeDrag();
                    break;
            }
        }

        void EndTimeDrag()
        {
            if (timeDragging)
            {
                timeDragging = false;
                readoutUntil = Time.realtimeSinceStartup + ReadoutSeconds;
            }
        }

        bool ReadoutShown
        {
            get
            {
                return ShowsTimeButtons && (timeDragging || Time.realtimeSinceStartup < readoutUntil);
            }
        }

        // "+1.25 s": how far the playhead moved since the press.
        string TimeChangeText()
        {
            float moved = TimelineBridge.PlaybackTime - timeAtPress;
            return (moved < 0f ? "-" : "+") + Mathf.Abs(moved).ToString("0.00") + " s";
        }

        // Drawn at the end of the window's contents, over its top left part, in window coordinates.
        //   3.27 s                 of 10.00 s
        //   +1.25 s           frame 196 / 600
        //   [=======|======..................]   the whole animation; | where the drag began
        void DrawTimeReadout()
        {
            if (Event.current.type != EventType.Repaint || !ReadoutShown)
            {
                return;
            }

            float now = TimelineBridge.PlaybackTime, length = Mathf.Max(0.0001f, TimelineBridge.Duration);
            int frames = TimelineBridge.FrameRate;
            float pad = PadUi.Px(6f);
            float width = Mathf.Min(PadUi.Px(ReadoutWidth), window.width - PadUi.Px(8f));
            float height = PadUi.Px(ReadoutBigRow + ReadoutSmallRow + ReadoutTrack) + pad * 3f;
            Rect panel = new Rect(PadUi.Px(4f), PadUi.Px(26f), width, height);
            PadUi.Fill(panel, ReadoutBack);

            Rect big = new Rect(panel.x + pad, panel.y + pad, panel.width - pad * 2f, PadUi.Px(ReadoutBigRow));
            GUI.Label(big, now.ToString("0.00") + " s", PadUi.BigTime);
            GUI.Label(big, "of " + length.ToString("0.00") + " s", PadUi.TimeDetail);

            Rect change = new Rect(big.x, big.yMax, big.width, PadUi.Px(ReadoutSmallRow));
            Color previous = GUI.contentColor;
            GUI.contentColor = now < timeAtPress ? Earlier : Later;
            GUI.Label(change, TimeChangeText(), PadUi.TimeChange);
            GUI.contentColor = previous;
            GUI.Label(change, "frame " + Mathf.RoundToInt(now * frames) + " / " + Mathf.RoundToInt(length * frames), PadUi.TimeDetail);

            Rect track = new Rect(big.x, change.yMax + pad, big.width, PadUi.Px(ReadoutTrack));
            PadUi.Fill(track, new Color(0.18f, 0.19f, 0.23f, 1f));
            PadUi.Fill(new Rect(track.x, track.y, track.width * Mathf.Clamp01(now / length), track.height), new Color(0.28f, 0.58f, 0.95f, 1f));
            float start = track.x + track.width * Mathf.Clamp01(timeAtPress / length);
            PadUi.Fill(new Rect(start - 1f, track.y - PadUi.Px(3f), 2f, track.height + PadUi.Px(6f)), Color.white);
        }

        // ---- the resize knob ---------------------------------------------------------------------

        void ResizeKnob()
        {
            Rect knob = KnobRect();
            int id = GUIUtility.GetControlID(KnobHint, FocusType.Passive, knob);
            Event current = Event.current;
            switch (current.GetTypeForControl(id))
            {
                case EventType.MouseDown:
                    if (!knob.Contains(current.mousePosition))
                    {
                        break;
                    }

                    if (current.button == 0)
                    {
                        GUIUtility.hotControl = id;
                        BeginResize(current.mousePosition.x);
                    }
                    else if (current.button == 1)
                    {
                        host.UiScale.Value = Plugin.DefaultUiScale;
                        Status("Window size back to the default");
                    }

                    current.Use();
                    break;
                case EventType.MouseDrag:
                    if (GUIUtility.hotControl == id)
                    {
                        ResizeTo(current.mousePosition.x);
                        current.Use();
                    }

                    break;
                case EventType.MouseUp:
                    if (GUIUtility.hotControl == id)
                    {
                        GUIUtility.hotControl = 0;
                        EndResize();
                        current.Use();
                    }

                    break;
                case EventType.Repaint:
                    bool lit = GUIUtility.hotControl == id || knob.Contains(current.mousePosition);
                    Color previous = GUI.color;
                    GUI.color = lit ? KnobLit : KnobIdle;
                    GUI.DrawTexture(KnobDrawnRect(), PadUi.Disc);
                    GUI.color = previous;
                    break;
            }
        }

        // The window's left edge stays where it is and its right edge follows the pointer; every
        // size of the window grows by the same factor.
        void BeginResize(float pointerX)
        {
            knobStartX = pointerX;
            knobStartWidth = Mathf.Max(1f, drawn.width);
            knobStartScale = host.UiScale.Value;
            resizeScale = knobStartScale;
        }

        void ResizeTo(float pointerX)
        {
            float width = knobStartWidth + pointerX - knobStartX;
            resizeScale = Mathf.Clamp(knobStartScale * width / knobStartWidth, Plugin.MinUiScale, Plugin.MaxUiScale);
        }

        void EndResize()
        {
            if (resizeScale <= 0f)
            {
                return;
            }

            host.UiScale.Value = Mathf.Round(resizeScale * 1000f) / 1000f;   // written to the settings file
            resizeScale = -1f;
            Status("Window size " + host.UiScale.Value.ToString("0.00") + " (remembered)");
        }

        // From Tick: a release that the window did not see still ends the drags.
        void TickCorners(bool mouseHeld)
        {
            if (!mouseHeld)
            {
                EndTimeDrag();
                EndResize();
            }
        }
    }
}
