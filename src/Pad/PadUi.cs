using GinShared;
using UnityEngine;

namespace GinPadHack
{
    // Drawing helpers shared by the tabs: sizes, solid (non-transparent) buttons and the drag bars.
    //
    // Sizes in the tab code are written for scale 1 and pass through Px / Width / Height, so the
    // "UiScale" setting enlarges the whole window with sharp text.
    //
    // A drag bar replaces the slider. Press anywhere on the bar and move left or right:
    //   * the value follows the mouse movement, not the position of a small thumb;
    //   * "Sensitivity" scales how fast it follows, Shift makes it four times finer;
    //   * the drag keeps working after the pointer leaves the bar;
    //   * right click puts the value back to its default;
    //   * middle click asks for a Timeline key (see TakeKeyRequest).
    internal static class PadUi
    {
        internal const float FineFactor = 0.25f;

        // What a drag button (DragButton) did in this event.
        internal enum Press
        {
            None,
            Down,       // the button was pressed
            Dragged,    // it is held and the mouse moved: use "pixels"
            Released,   // released after a drag
            Clicked     // released without a drag
        }

        static readonly Color Track = new Color(0.13f, 0.13f, 0.15f, 1f);
        static readonly Color FillColor = new Color(0.20f, 0.42f, 0.72f, 1f);
        static readonly Color FillHeld = new Color(0.28f, 0.58f, 0.95f, 1f);
        static readonly Color Idle = new Color(0.25f, 0.26f, 0.30f, 1f);
        static readonly Color Hover = new Color(0.34f, 0.36f, 0.42f, 1f);
        static readonly Color Accent = new Color(0.22f, 0.50f, 0.88f, 1f);
        static readonly Color BoneIdle = new Color(0.16f, 0.36f, 0.33f, 1f);
        static readonly Color PinLit = new Color(0.95f, 0.78f, 0.25f, 1f);
        static readonly Color MarkerLit = new Color(1f, 0.72f, 0.2f, 1f);
        static readonly Color DelayOn = new Color(0.45f, 0.22f, 0.85f, 1f);
        static readonly Color RotateIdle = new Color(0.52f, 0.34f, 0.12f, 1f);   // Body tab, Rotate mode
        static readonly Color RotateRootTint = new Color(1f, 0.72f, 0.38f, 1f);

        // The Body tab's Root trapezoid: plain grey without the blue tint of the other buttons.
        static readonly Color RootIdle = new Color(0.42f, 0.42f, 0.42f, 1f);
        static readonly Color RootHover = new Color(0.52f, 0.52f, 0.52f, 1f);
        static readonly Color RootOn = new Color(0.60f, 0.60f, 0.60f, 1f);
        static readonly Color RootHeld = new Color(0.68f, 0.68f, 0.68f, 1f);
        const float TrapezoidTop = 0.62f;   // the top edge as a part of the bottom edge

        // Axis colours of the Body tab bars: X red, Y green, Z blue.
        internal static readonly Color[] AxisColors =
        {
            new Color(0.62f, 0.17f, 0.17f, 1f),
            new Color(0.15f, 0.47f, 0.20f, 1f),
            new Color(0.17f, 0.30f, 0.70f, 1f)
        };
        internal static readonly Color Neutral = new Color(0.30f, 0.31f, 0.36f, 1f);

        internal static float Sensitivity = 1f;
        internal static GUIStyle Window, Button, PartButton, RotatePartButton, BoneButton, PinOff, PinOn, Small, Heading, BarBox, BarLabel, BarValue;
        internal static GUIStyle AxisBarBox, AxisTitle, Marker, MarkerOn, SmallBoneButton, CenterLabel, DragBox;
        internal static GUIStyle CornerButton, BigTime, TimeChange, TimeDetail;
        internal static GUIStyle RootText;
        internal static GUIStyle DelayKeyButton, DelayGridLabel, DelayRangeLabel, DelayMarkLabel, DelayMarkLabelRight, DelayHint, DelayTyping, DelayVersus;
        internal static Texture2D TriangleDown, TriangleRight, Disc;

        static readonly int BarHint = "GinPadBar".GetHashCode();
        static readonly int ClickHint = "GinPadClick".GetHashCode();
        static readonly int ScrollHint = "GinPadScroll".GetHashCode();
        static readonly int DragButtonHint = "GinPadDragButton".GetHashCode();
        static float lastMouseY;
        static int clickButton, dragButton;
        static bool dragMoved, keyRequested;
        static Texture2D idleTexture, hoverTexture, accentTexture, pinTexture, boneTexture, markerTexture, delayOnTexture, rotateTexture;
        static Texture2D rootIdleTexture, rootHoverTexture, rootOnTexture, rootHeldTexture;
        static float scale = 1f, builtScale;
        static float lastMouseX;

        // A middle click on a bar or pad asks for a Timeline key at the playhead. The tab that drew
        // the control takes the request right after drawing it and knows what to key.
        internal static void RequestKey()
        {
            keyRequested = true;
        }

        internal static bool TakeKeyRequest()
        {
            bool requested = keyRequested;
            keyRequested = false;
            return requested;
        }

        // ---- sizes ---------------------------------------------------------------------------

        // A length written for scale 1, in screen pixels.
        internal static float Px(float length)
        {
            return Mathf.Round(length * scale);
        }

        internal static GUILayoutOption Width(float width)
        {
            return GUILayout.Width(Px(width));
        }

        internal static GUILayoutOption Height(float height)
        {
            return GUILayout.Height(Px(height));
        }

        internal static float BarHeight
        {
            get
            {
                return Px(17f);
            }
        }

        // The X / Y / Z bars of the Body tab are half as tall again: they are dragged all the time.
        internal static float AxisBarHeight
        {
            get
            {
                return Px(25.5f);
            }
        }

        // ---- styles --------------------------------------------------------------------------

        // Called for every GUI pass; the styles are rebuilt only when the scale changed (the resize
        // knob changes it on every drag event). The textures are made once.
        internal static void EnsureStyles(float uiScale)
        {
            if (rootIdleTexture != null && Button != null && idleTexture != null && RootText != null && RotatePartButton != null && builtScale == uiScale)
            {
                return;
            }

            scale = builtScale = uiScale;
            if (idleTexture == null)
            {
                idleTexture = Solid(Idle);
                hoverTexture = Solid(Hover);
                accentTexture = Solid(Accent);
                pinTexture = Solid(PinLit);
                boneTexture = Solid(BoneIdle);
                markerTexture = Solid(MarkerLit);
                TriangleDown = Triangle(false);
                TriangleRight = Triangle(true);
                Disc = MakeDisc();
            }

            if (delayOnTexture == null)
            {
                delayOnTexture = Solid(DelayOn);
            }

            if (rotateTexture == null)
            {
                rotateTexture = Solid(RotateIdle);
            }

            if (rootIdleTexture == null)
            {
                rootIdleTexture = Trapezoid(RootIdle);
                rootHoverTexture = Trapezoid(RootHover);
                rootOnTexture = Trapezoid(RootOn);
                rootHeldTexture = Trapezoid(RootHeld);
            }

            Window = new GUIStyle();
            Window.padding = new RectOffset(5, 5, 4, 4);

            Button = new GUIStyle(GUI.skin.button);
            Button.normal.background = Button.focused.background = idleTexture;
            Button.hover.background = hoverTexture;
            Button.active.background = Button.onNormal.background = Button.onHover.background = Button.onActive.background = accentTexture;
            Button.normal.textColor = Button.hover.textColor = Button.active.textColor = Color.white;
            Button.onNormal.textColor = Button.onHover.textColor = Button.onActive.textColor = Color.white;
            Button.border = new RectOffset(0, 0, 0, 0);
            Button.margin = new RectOffset(1, 1, 1, 1);
            Button.padding = new RectOffset(4, 4, 1, 2);
            Button.fontSize = Mathf.RoundToInt(11f * scale);
            Button.fixedHeight = Px(18f);
            Button.alignment = TextAnchor.MiddleCenter;
            Button.wordWrap = false;

            // Body tab figure: larger buttons whose size is the rectangle they are drawn in.
            // IK targets keep the normal colour, bones edited through KKPE are green.
            PartButton = new GUIStyle(Button);
            PartButton.fixedHeight = 0f;
            PartButton.fontSize = Mathf.RoundToInt(12f * scale);
            // In the Body tab's Rotate mode an IK target that turns is amber: a drag turns it.
            RotatePartButton = new GUIStyle(PartButton);
            RotatePartButton.normal.background = RotatePartButton.focused.background = rotateTexture;
            BoneButton = new GUIStyle(PartButton);
            BoneButton.normal.background = BoneButton.focused.background = boneTexture;
            SmallBoneButton = new GUIStyle(BoneButton);
            SmallBoneButton.fontSize = Mathf.RoundToInt(10f * scale);
            SmallBoneButton.padding = new RectOffset(1, 1, 0, 0);

            // The small square in front of a Blend Shape bar that keeps the shape listed.
            PinOff = new GUIStyle();
            PinOff.normal.background = idleTexture;
            PinOff.hover.background = hoverTexture;
            PinOff.margin = new RectOffset(1, 0, 1, 1);
            PinOff.fixedHeight = BarHeight;
            PinOn = new GUIStyle(PinOff);
            PinOn.normal.background = PinOn.hover.background = pinTexture;

            Small = new GUIStyle(GUI.skin.label);
            Small.fontSize = Mathf.RoundToInt(11f * scale);
            Small.wordWrap = false;
            Small.clipping = TextClipping.Clip;
            Small.normal.textColor = Color.white;
            Small.padding = new RectOffset(2, 2, 0, 0);
            Small.margin = new RectOffset(1, 1, 1, 1);

            Heading = new GUIStyle(Small);
            Heading.fontSize = Mathf.RoundToInt(10f * scale);
            Heading.fontStyle = FontStyle.Bold;
            Heading.normal.textColor = new Color(0.72f, 0.80f, 0.92f, 1f);
            Heading.fixedHeight = Px(13f);

            BarBox = new GUIStyle();
            BarBox.fixedHeight = BarHeight;
            BarBox.margin = new RectOffset(1, 1, 1, 1);
            BarBox.stretchWidth = true;

            BarLabel = new GUIStyle(Small);
            BarLabel.alignment = TextAnchor.MiddleLeft;
            BarLabel.padding = new RectOffset(4, 2, 0, 0);
            BarLabel.margin = new RectOffset(0, 0, 0, 0);

            BarValue = new GUIStyle(BarLabel);
            BarValue.alignment = TextAnchor.MiddleRight;
            BarValue.padding = new RectOffset(2, 4, 0, 0);

            CenterLabel = new GUIStyle(BarLabel);
            CenterLabel.alignment = TextAnchor.MiddleCenter;
            CenterLabel.padding = new RectOffset(1, 1, 0, 0);
            DragBox = new GUIStyle();
            DragBox.margin = new RectOffset(1, 1, 1, 1);

            AxisBarBox = new GUIStyle(BarBox);
            AxisBarBox.fixedHeight = AxisBarHeight;
            AxisTitle = new GUIStyle(Small);
            AxisTitle.fixedHeight = AxisBarHeight;
            AxisTitle.alignment = TextAnchor.MiddleLeft;

            // The small triangle buttons along the Map: one per pose, lit while that pose is being made.
            Marker = new GUIStyle();
            Marker.normal.background = idleTexture;
            Marker.hover.background = hoverTexture;
            Marker.active.background = accentTexture;
            MarkerOn = new GUIStyle(Marker);
            MarkerOn.normal.background = MarkerOn.hover.background = MarkerOn.active.background = markerTexture;

            // The play / time buttons outside the window's top left corner (PadWindow.Corners.cs).
            CornerButton = new GUIStyle(Button);
            CornerButton.fixedHeight = 0f;
            CornerButton.padding = new RectOffset(0, 0, 0, 0);
            CornerButton.margin = new RectOffset(0, 0, 0, 0);

            // The readout while the time is dragged: large enough to read from the corner of an eye.
            BigTime = new GUIStyle(Small);
            BigTime.fontSize = Mathf.RoundToInt(30f * scale);
            BigTime.fontStyle = FontStyle.Bold;
            BigTime.alignment = TextAnchor.MiddleLeft;
            BigTime.clipping = TextClipping.Overflow;
            TimeChange = new GUIStyle(BigTime);
            TimeChange.fontSize = Mathf.RoundToInt(20f * scale);
            TimeDetail = new GUIStyle(Small);
            TimeDetail.fontSize = Mathf.RoundToInt(12f * scale);
            TimeDetail.alignment = TextAnchor.MiddleRight;
            TimeDetail.normal.textColor = new Color(0.78f, 0.82f, 0.90f, 1f);

            // The Body tab's Delay Key panel (PadWindow.Delay.cs).
            DelayKeyButton = new GUIStyle(Button);
            DelayKeyButton.fixedHeight = Px(32f);
            DelayKeyButton.fontSize = Mathf.RoundToInt(11f * scale);
            DelayKeyButton.padding = new RectOffset(1, 1, 1, 1);
            DelayKeyButton.fontStyle = FontStyle.Bold;
            DelayKeyButton.onNormal.background = DelayKeyButton.onHover.background = DelayKeyButton.onActive.background = delayOnTexture;
            DelayGridLabel = new GUIStyle(Small);
            DelayGridLabel.fontSize = Mathf.RoundToInt(8f * scale);
            DelayGridLabel.padding = new RectOffset(1, 1, 0, 0);
            DelayGridLabel.margin = new RectOffset(0, 0, 0, 0);
            DelayGridLabel.alignment = TextAnchor.LowerLeft;
            DelayGridLabel.normal.textColor = new Color(1f, 1f, 1f, 0.7f);
            DelayRangeLabel = new GUIStyle(DelayGridLabel);
            DelayRangeLabel.alignment = TextAnchor.LowerRight;
            DelayRangeLabel.fontStyle = FontStyle.Bold;
            DelayRangeLabel.normal.textColor = Color.white;
            DelayMarkLabel = new GUIStyle(Small);
            DelayMarkLabel.fontSize = Mathf.RoundToInt(10f * scale);
            DelayMarkLabel.margin = new RectOffset(0, 0, 0, 0);
            DelayMarkLabel.alignment = TextAnchor.MiddleLeft;
            DelayMarkLabel.clipping = TextClipping.Overflow;
            DelayMarkLabelRight = new GUIStyle(DelayMarkLabel);
            DelayMarkLabelRight.alignment = TextAnchor.MiddleRight;
            DelayHint = new GUIStyle(Small);
            DelayHint.fontSize = Mathf.RoundToInt(10f * scale);
            DelayHint.alignment = TextAnchor.MiddleCenter;
            DelayHint.normal.textColor = new Color(0.78f, 0.82f, 0.90f, 1f);
            DelayTyping = new GUIStyle(GUI.skin.textField);
            DelayTyping.fontSize = Mathf.RoundToInt(11f * scale);
            DelayTyping.alignment = TextAnchor.MiddleCenter;
            DelayVersus = new GUIStyle(TimeDetail);
            DelayVersus.alignment = TextAnchor.MiddleLeft;

            // The label on the Body tab's Root trapezoid, a little low to sit in its wider part.
            RootText = new GUIStyle(Small);
            RootText.fontSize = Mathf.RoundToInt(10f * scale);
            RootText.fontStyle = FontStyle.Bold;
            RootText.alignment = TextAnchor.MiddleCenter;
            RootText.clipping = TextClipping.Overflow;
            RootText.padding = new RectOffset(0, 0, Mathf.RoundToInt(2f * scale), 0);
            RootText.margin = new RectOffset(0, 0, 0, 0);
        }

        // ---- the Root trapezoid of the Body tab -----------------------------------------------

        // Whether a point lies on the trapezoid drawn in this rectangle (narrow top, full bottom).
        internal static bool InsideTrapezoid(Rect place, Vector2 point)
        {
            if (!place.Contains(point))
            {
                return false;
            }

            float down = (point.y - place.yMin) / Mathf.Max(1f, place.height);
            float halfWidth = place.width * 0.5f * (TrapezoidTop + (1f - TrapezoidTop) * down);
            return Mathf.Abs(point.x - place.center.x) <= halfWidth;
        }

        // "rotate": the Body tab is in Rotate mode, and the trapezoid is tinted amber like the IK targets.
        internal static void DrawTrapezoidButton(Rect place, string label, bool hover, bool held, bool on, bool usable, bool rotate)
        {
            Texture2D texture = held ? rootHeldTexture : on ? rootOnTexture : hover ? rootHoverTexture : rootIdleTexture;
            Color previous = GUI.color;
            if (rotate)
            {
                GUI.color = RotateRootTint;
            }

            if (!usable)
            {
                GUI.color = new Color(GUI.color.r, GUI.color.g, GUI.color.b, 0.35f);
            }

            GUI.DrawTexture(place, texture);
            RootText.normal.textColor = held || on ? new Color(0.10f, 0.10f, 0.10f, 1f) : Color.white;
            GUI.Label(place, label, RootText);
            GUI.color = previous;
        }

        // A trapezoid of one colour on a transparent rectangle: the bottom edge is the whole width,
        // the top edge TrapezoidTop of it. Large enough for UiScale 2; the slanted sides are smoothed.
        static Texture2D Trapezoid(Color color)
        {
            const int Width = 104, Height = 44;
            Texture2D texture = new Texture2D(Width, Height, TextureFormat.ARGB32, false, true);
            for (int y = 0; y < Height; y++)
            {
                // Texture rows run upwards: row 0 is the bottom edge.
                float down = 1f - (y + 0.5f) / Height;
                float halfWidth = Width * 0.5f * (TrapezoidTop + (1f - TrapezoidTop) * down);
                for (int x = 0; x < Width; x++)
                {
                    float coverage = Mathf.Clamp01(halfWidth - Mathf.Abs(x + 0.5f - Width * 0.5f) + 0.5f);
                    texture.SetPixel(x, y, new Color(color.r, color.g, color.b, coverage));
                }
            }

            texture.Apply();
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.hideFlags = HideFlags.HideAndDontSave;
            return texture;
        }

        // A white disc on a transparent square (the resize knob).
        static Texture2D MakeDisc()
        {
            const int Size = 32;
            Texture2D texture = new Texture2D(Size, Size, TextureFormat.ARGB32, false, true);
            float radius = Size * 0.5f;
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    // Soft one-pixel edge, so the small disc is not jagged.
                    float distance = new Vector2(x + 0.5f - radius, y + 0.5f - radius).magnitude;
                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(radius - distance)));
                }
            }

            texture.Apply();
            texture.hideFlags = HideFlags.HideAndDontSave;
            return texture;
        }

        // A filled white triangle on a transparent square, pointing down or to the right.
        static Texture2D Triangle(bool pointsRight)
        {
            const int Size = 16;
            Texture2D texture = new Texture2D(Size, Size, TextureFormat.ARGB32, false, true);
            for (int along = 0; along < Size; along++)
            {
                // "along" runs from the triangle's base (0) to its tip; the half width shrinks on the way.
                float halfWidth = (Size - 1 - along) * 0.5f;
                for (int across = 0; across < Size; across++)
                {
                    bool inside = Mathf.Abs(across - (Size - 1) * 0.5f) <= halfWidth;
                    Color color = inside ? Color.white : new Color(1f, 1f, 1f, 0f);
                    if (pointsRight)
                    {
                        texture.SetPixel(along, across, color);
                    }
                    else
                    {
                        texture.SetPixel(across, Size - 1 - along, color);   // texture rows run upwards
                    }
                }
            }

            texture.Apply();
            texture.hideFlags = HideFlags.HideAndDontSave;
            return texture;
        }

        static Texture2D Solid(Color color)
        {
            // "linear": the colour is shown as given. Studio renders in linear colour space and would
            // otherwise darken an sRGB texture until the button is almost invisible.
            Texture2D texture = new Texture2D(1, 1, TextureFormat.ARGB32, false, true);
            texture.SetPixel(0, 0, color);
            texture.Apply();
            // Keep it through Studio's scene loads, which unload "unused" assets.
            texture.hideFlags = HideFlags.HideAndDontSave;
            return texture;
        }

        internal static void Fill(Rect rect, Color color)
        {
            Color previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = previous;
        }

        // Button that stays lit while "on". Returns the new state.
        internal static bool Toggle(bool on, string text, params GUILayoutOption[] options)
        {
            return GUILayout.Toggle(on, text, Button, options);
        }

        // A button without text that tells which mouse button clicked it: 0 left, 1 right, -1 none.
        internal static int ClickButton(Rect rect, GUIStyle style)
        {
            int id = GUIUtility.GetControlID(ClickHint, FocusType.Passive, rect);
            Event current = Event.current;
            switch (current.GetTypeForControl(id))
            {
                case EventType.MouseDown:
                    if ((current.button == 0 || current.button == 1) && rect.Contains(current.mousePosition))
                    {
                        GUIUtility.hotControl = id;
                        clickButton = current.button;
                        current.Use();
                    }

                    break;
                case EventType.MouseUp:
                    if (GUIUtility.hotControl == id)
                    {
                        GUIUtility.hotControl = 0;
                        current.Use();
                        if (rect.Contains(current.mousePosition))
                        {
                            return clickButton;
                        }
                    }

                    break;
                case EventType.Repaint:
                    style.Draw(rect, GUIContent.none, rect.Contains(current.mousePosition), GUIUtility.hotControl == id, false, false);
                    break;
            }

            return -1;
        }

        // ---- scrolling ------------------------------------------------------------------------
        //
        // The Pad draws its own scroll bar instead of IMGUI's: it is handled exactly like the drag
        // bars (one hot control, held until release), which Studio's camera never reacts to.
        // A list scrolls with the mouse wheel too.

        internal const float ScrollBarWidth = 10f;   // at UiScale 1

        // A scrolling area of a fixed height without IMGUI's scroll bars; the bar comes from EndScroll.
        internal static Vector2 BeginScroll(Vector2 scroll, float width, float height)
        {
            GUILayout.BeginHorizontal();
            return GUILayout.BeginScrollView(scroll, GUIStyle.none, GUIStyle.none, GUILayout.Width(width), GUILayout.Height(height));
        }

        // contentHeight: the height of everything inside the area. Returns the new scroll position.
        internal static Vector2 EndScroll(Vector2 scroll, float contentHeight, float height)
        {
            GUILayout.EndScrollView();
            scroll.y = ScrollBar(scroll.y, contentHeight, height);
            GUILayout.EndHorizontal();
            return scroll;
        }

        // Press on the bar: the thumb jumps under the pointer and follows it while held.
        static float ScrollBar(float value, float contentHeight, float height)
        {
            Rect rect = GUILayoutUtility.GetRect(Px(ScrollBarWidth), height, GUILayout.Width(Px(ScrollBarWidth)), GUILayout.Height(height));
            float range = Mathf.Max(0f, contentHeight - height);
            float thumb = ThumbSize(contentHeight, height);
            float travel = Mathf.Max(1f, rect.height - thumb);
            value = Mathf.Clamp(value, 0f, range);
            int id = GUIUtility.GetControlID(ScrollHint, FocusType.Passive, rect);
            Event current = Event.current;
            switch (current.GetTypeForControl(id))
            {
                case EventType.MouseDown:
                    if (current.button == 0 && rect.Contains(current.mousePosition))
                    {
                        GUIUtility.hotControl = id;
                        value = ScrollAfterPress(rect, value, contentHeight, current.mousePosition.y);
                        lastMouseY = current.mousePosition.y;
                        current.Use();
                    }

                    break;
                case EventType.MouseDrag:
                    if (GUIUtility.hotControl == id)
                    {
                        value = ScrollAfterDrag(rect, value, contentHeight, current.mousePosition.y - lastMouseY);
                        lastMouseY = current.mousePosition.y;
                        current.Use();
                    }

                    break;
                case EventType.MouseUp:
                    if (GUIUtility.hotControl == id)
                    {
                        GUIUtility.hotControl = 0;
                        current.Use();
                    }

                    break;
                case EventType.Repaint:
                    Fill(rect, Track);
                    float y = rect.y + travel * (range > 0f ? value / range : 0f);
                    Fill(new Rect(rect.x + 1f, y, rect.width - 2f, thumb), GUIUtility.hotControl == id ? FillHeld : Hover);
                    break;
            }

            return value;
        }

        static float ThumbSize(float contentHeight, float height)
        {
            return Mathf.Clamp(height * height / Mathf.Max(height, contentHeight), Px(18f), height);
        }

        // A press outside the thumb puts the thumb's centre under the pointer; on the thumb nothing jumps.
        internal static float ScrollAfterPress(Rect bar, float value, float contentHeight, float mouseY)
        {
            float range = Mathf.Max(0f, contentHeight - bar.height), thumb = ThumbSize(contentHeight, bar.height);
            float travel = Mathf.Max(1f, bar.height - thumb);
            float top = bar.y + travel * (range > 0f ? value / range : 0f);
            if (mouseY >= top && mouseY <= top + thumb)
            {
                return value;
            }

            return Mathf.Clamp((mouseY - bar.y - thumb / 2f) / travel * range, 0f, range);
        }

        // The thumb follows the pointer: moving it over its whole travel scrolls over the whole range.
        internal static float ScrollAfterDrag(Rect bar, float value, float contentHeight, float pixels)
        {
            float range = Mathf.Max(0f, contentHeight - bar.height);
            float travel = Mathf.Max(1f, bar.height - ThumbSize(contentHeight, bar.height));
            return Mathf.Clamp(value + pixels / travel * range, 0f, range);
        }

        // ---- held drags: one step per frame ---------------------------------------------------
        //
        // Until EndlessDrag holds the cursor, a held control moves by each drag event's own
        // movement. Once it holds (the cursor hidden and parked in the middle of the screen), the
        // movement is the mouse's own and is taken once per frame, in the Repaint pass: the value
        // then changes in every frame, as Move Controller's do. Before, it changed only in frames
        // in which Windows sent a drag event, and with the cursor parked and the mouse moving
        // steadily it often sent none: the value stood still and then jumped. Drag events are only
        // used up while the cursor is held.

        // The movement the control "id" takes in this event, in GUI pixels (y down); false when it
        // takes none. "eventMovement": what this drag event itself reports.
        internal static bool TakeHeldMovement(int id, Vector2 eventMovement, out Vector2 moved)
        {
            moved = Vector2.zero;
            if (GUIUtility.hotControl != id)
            {
                return false;
            }

            Event current = Event.current;
            EventType type = current.GetTypeForControl(id);
            if (type == EventType.MouseDrag)
            {
                if (!EndlessDrag.Holding)
                {
                    moved = EndlessDrag.Moved(eventMovement);   // the first real movement starts holding
                }

                current.Use();
                return moved != Vector2.zero;
            }

            if (type == EventType.Repaint && EndlessDrag.Holding)
            {
                moved = EndlessDrag.Moved(Vector2.zero);
                return moved != Vector2.zero;
            }

            return false;
        }

        // Shift for fine steps; read from the keyboard too, because a Repaint pass may not carry it.
        internal static bool Fine(Event current)
        {
            return current.shift || Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        }

        // ---- drag bars -----------------------------------------------------------------------

        // Bar for a value with a fixed range. Dragging across one bar width covers the whole range
        // at sensitivity 1. Returns the new value.
        internal static float Bar(string label, float value, float min, float max, float resetTo, string format, params GUILayoutOption[] options)
        {
            Rect rect = GUILayoutUtility.GetRect(GUIContent.none, BarBox, options);
            float pixels;
            bool reset;
            bool held = Drag(rect, out pixels, out reset);
            if (reset)
            {
                value = resetTo;
            }
            else if (pixels != 0f)
            {
                value = Mathf.Clamp(value + pixels * Sensitivity / rect.width * (max - min), min, max);
            }

            if (Event.current.type == EventType.Repaint)
            {
                // A range around zero fills from zero; otherwise from the left edge.
                float from = min < 0f && max > 0f ? Mathf.InverseLerp(min, max, 0f) : 0f;
                float to = Mathf.InverseLerp(min, max, value);
                Fill(rect, Track);
                Fill(new Rect(rect.x + Mathf.Min(from, to) * rect.width, rect.y, Mathf.Abs(to - from) * rect.width, rect.height), held ? FillHeld : FillColor);
                BarLabel.Draw(rect, label, false, false, false, false);
                BarValue.Draw(rect, value.ToString(format), false, false, false, false);
            }

            return value;
        }

        // The sensitivity bar itself: the same control, but it does not scale its own movement.
        internal static float SensitivityBar(float value, float min, float max, float resetTo, params GUILayoutOption[] options)
        {
            float current = Sensitivity;
            Sensitivity = 1f;
            value = Bar("Sens", value, min, max, resetTo, "0.00", options);
            Sensitivity = current;
            return value;
        }

        // Tall bar for a value without a range (a position, an angle), in the given colour. Returns how
        // much to add for this event: the mouse movement times unitsPerPixel. "reset" is a right click.
        internal static float DeltaBar(string label, string valueText, float unitsPerPixel, Color color, out bool reset, params GUILayoutOption[] options)
        {
            Rect rect = GUILayoutUtility.GetRect(GUIContent.none, AxisBarBox, options);
            float pixels;
            bool held = Drag(rect, out pixels, out reset);
            if (Event.current.type == EventType.Repaint)
            {
                Fill(rect, held ? Color.Lerp(color, Color.white, 0.25f) : Color.Lerp(color, Track, 0.35f));
                BarLabel.Draw(rect, label, false, false, false, false);
                BarValue.Draw(rect, valueText, false, false, false, false);
            }

            return pixels * Sensitivity * unitsPerPixel;
        }

        // Shared mouse handling. "pixels" is the horizontal movement of this event while the bar is
        // held (Shift: a quarter of it); it goes on beyond the screen edge (EndlessDrag). Returns
        // true while the bar is held.
        static bool Drag(Rect rect, out float pixels, out bool reset)
        {
            pixels = 0f;
            reset = false;
            int id = GUIUtility.GetControlID(BarHint, FocusType.Passive, rect);
            Event current = Event.current;
            bool dragEvent = current.GetTypeForControl(id) == EventType.MouseDrag && GUIUtility.hotControl == id;
            Vector2 moved;
            if (TakeHeldMovement(id, new Vector2(current.mousePosition.x - lastMouseX, 0f), out moved))
            {
                pixels = moved.x * (Fine(current) ? FineFactor : 1f);
            }

            if (dragEvent)
            {
                lastMouseX = current.mousePosition.x;
            }

            switch (current.GetTypeForControl(id))
            {
                case EventType.MouseDown:
                    if (!rect.Contains(current.mousePosition))
                    {
                        break;
                    }

                    if (current.button == 0)
                    {
                        GUIUtility.hotControl = id;
                        lastMouseX = current.mousePosition.x;
                        current.Use();
                    }
                    else if (current.button == 1)
                    {
                        reset = true;
                        current.Use();
                    }
                    else if (current.button == 2)
                    {
                        RequestKey();
                        current.Use();
                    }

                    break;
                case EventType.MouseUp:
                    if (GUIUtility.hotControl == id)
                    {
                        GUIUtility.hotControl = 0;
                        EndlessDrag.End();
                        current.Use();
                    }

                    break;
            }

            return GUIUtility.hotControl == id;
        }

        // ---- drag buttons (the item Move tab) ---------------------------------------------------

        // A coloured button that is pressed with the left or right mouse button and dragged.
        // "pixels" is the mouse movement while held (Shift: finer; beyond the screen edge too; once
        // per frame, see TakeHeldMovement), "button" the mouse button that holds it. A press
        // released without moving is a click.
        internal static Press DragButton(Rect rect, string label, Color color, out Vector2 pixels, out int button)
        {
            pixels = Vector2.zero;
            button = dragButton;
            int id = GUIUtility.GetControlID(DragButtonHint, FocusType.Passive, rect);
            Event current = Event.current;
            Press result = Press.None;
            Vector2 moved;
            if (TakeHeldMovement(id, current.delta, out moved))
            {
                pixels = moved * (Fine(current) ? FineFactor : 1f) * Sensitivity;
                dragMoved = true;
                result = Press.Dragged;
            }

            switch (current.GetTypeForControl(id))
            {
                case EventType.MouseDown:
                    if ((current.button == 0 || current.button == 1) && rect.Contains(current.mousePosition))
                    {
                        GUIUtility.hotControl = id;
                        button = dragButton = current.button;
                        dragMoved = false;
                        current.Use();
                        return Press.Down;
                    }

                    break;
                case EventType.MouseUp:
                    if (GUIUtility.hotControl == id)
                    {
                        GUIUtility.hotControl = 0;
                        EndlessDrag.End();
                        current.Use();
                        return dragMoved ? Press.Released : Press.Clicked;
                    }

                    break;
                case EventType.Repaint:
                    bool held = GUIUtility.hotControl == id;
                    bool hover = rect.Contains(current.mousePosition);
                    Fill(rect, held ? Color.Lerp(color, Color.white, 0.3f) : hover ? Color.Lerp(color, Color.white, 0.12f) : color);
                    CenterLabel.Draw(rect, label, false, false, false, false);
                    break;
            }

            return result;
        }

        internal static Press DragButton(string label, Color color, float width, float height, out Vector2 pixels, out int button)
        {
            Rect rect = GUILayoutUtility.GetRect(Px(width), Px(height), DragBox, GUILayout.Width(Px(width)), GUILayout.Height(Px(height)));
            return DragButton(rect, label, color, out pixels, out button);
        }
    }
}
