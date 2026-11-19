using System;
using System.Collections.Generic;
using Timeline;
using UnityEngine;
using UnityEngine.UI;

namespace GinTimelineHack
{
    // The Auto Key control on the toolbar, drawn like Blender's: a small record button (a dot on
    // a square, blue with a red dot while on) and a narrow arrow that opens the curve menu.
    //
    // The menu chooses the curve of every key that Auto Key ADDS: one of the first five presets
    // of Timeline's keyframe window (Line, Top, Bottom, Hermite, Stairs), or "Previous key",
    // Timeline's own behaviour of copying the curve of the key before. A key that Auto Key only
    // updates keeps its curve. The curves are read from Timeline itself, so they are exactly the
    // ones its preset buttons apply. The choice is saved in the AutoKey / Curve setting.
    public sealed partial class Plugin
    {
        const float RecordWidth = 26f, ArrowWidth = 16f;
        const float MenuWidth = 136f, MenuRowHeight = 22f, MenuPadding = 2f;
        const int IconWidth = 40, IconHeight = 18;

        internal const string PreviousCurve = "Previous";
        static readonly string[] CurveNames = { PreviousCurve, "Line", "Top", "Bottom", "Hermite", "Stairs" };
        static readonly string[] CurveLabels = { "Previous key", "Line", "Top", "Bottom", "Hermite", "Stairs" };
        static readonly string[] CurveFields = { null, "_linePreset", "_topPreset", "_bottomPreset", "_hermitePreset", "_stairsPreset" };

        static readonly Color RecordOnColor = new Color(.28f, .45f, .78f, 1f);
        static readonly Color DotOnColor = new Color(.92f, .2f, .2f, 1f);
        static readonly Color DotOffColor = new Color(.78f, .78f, .8f, 1f);
        static readonly Color MenuColor = new Color(.11f, .11f, .13f, .97f);
        static readonly Color CurveLineColor = new Color(.3f, .85f, .8f, 1f);

        static Sprite dotSprite, arrowSprite;

        Image autoKeyDot;
        RectTransform autoKeyArrow, autoKeyMenu;
        readonly List<Image> curveRows = new List<Image>();
        readonly List<Button> curveButtons = new List<Button>();

        // ---- toolbar -------------------------------------------------------------------------

        void AddAutoKeyControl(ref float x)
        {
            Button record = MakeIconButton("GIN Auto Key", x, RecordWidth, Dot(), 6f, 5f, delegate
            {
                SetAutoKey(!autoKey);
            });
            autoKeyButton = (Image)record.targetGraphic;
            autoKeyDot = record.transform.GetChild(0).GetComponent<Image>();
            x += RecordWidth;

            Button arrow = MakeIconButton("GIN Auto Key curve", x, ArrowWidth, Arrow(), 3f, 8f, delegate
            {
                ShowCurveMenu(autoKeyMenu == null || !autoKeyMenu.gameObject.activeSelf);
            });
            autoKeyArrow = (RectTransform)arrow.transform;
            x += ArrowWidth + ToolbarGap;
            BuildCurveMenu();
        }

        Button MakeIconButton(string name, float x, float width, Sprite icon, float insetX, float insetY, Action action)
        {
            RectTransform rt = MakeRect(name, toolbar, false);
            Put(rt, x, 0, width, ToolbarHeight);
            Image back = rt.gameObject.AddComponent<Image>();
            back.color = ButtonColor;
            Button button = rt.gameObject.AddComponent<Button>();
            button.targetGraphic = back;
            ColorBlock colors = button.colors;
            colors.highlightedColor = new Color(.7f, .85f, 1);
            button.colors = colors;

            RectTransform iconRect = MakeRect("Icon", rt, false);
            Fill(iconRect, insetX, insetY, insetX, insetY);
            Image image = iconRect.gameObject.AddComponent<Image>();
            image.sprite = icon;
            image.preserveAspect = true;
            image.raycastTarget = false;
            button.onClick.AddListener(delegate
            {
                Guarded(action);
            });
            return button;
        }

        void Guarded(Action action)
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
                Note(Error(e));
                Logger.LogError(e);
            }
        }

        // ---- the curve menu -------------------------------------------------------------------

        void BuildCurveMenu()
        {
            curveRows.Clear();
            curveButtons.Clear();
            autoKeyMenu = MakeRect("GIN Auto Key curves", toolbar, false);
            Image back = autoKeyMenu.gameObject.AddComponent<Image>();
            back.color = MenuColor;
            for (int i = 0; i < CurveNames.Length; i++)
            {
                int choice = i;
                RectTransform row = MakeRect(CurveNames[i], autoKeyMenu, false);
                Put(row, MenuPadding, MenuPadding + i * MenuRowHeight, MenuWidth - 2 * MenuPadding, MenuRowHeight - 1f);
                Image rowBack = row.gameObject.AddComponent<Image>();
                Button button = row.gameObject.AddComponent<Button>();
                button.targetGraphic = rowBack;
                button.onClick.AddListener(delegate
                {
                    Guarded(delegate
                    {
                        ChooseCurve(choice);
                    });
                });

                AnimationCurve curve = PresetCurve(i);
                float textLeft = 6f;
                if (curve != null)
                {
                    RectTransform iconRect = MakeRect("Curve", row, false);
                    Put(iconRect, 4f, 1f, IconWidth, MenuRowHeight - 3f);
                    Image icon = iconRect.gameObject.AddComponent<Image>();
                    icon.sprite = CurveSprite(curve);
                    icon.raycastTarget = false;
                    textLeft = IconWidth + 10f;
                }

                Text label = Label(row, CurveLabels[i], 12);
                label.alignment = TextAnchor.MiddleLeft;
                label.rectTransform.offsetMin = new Vector2(textLeft, 0f);
                curveRows.Add(rowBack);
                curveButtons.Add(button);
            }

            autoKeyMenu.gameObject.SetActive(false);
            ShowCurveChoice();
        }

        void ShowCurveMenu(bool open)
        {
            if (autoKeyMenu == null)
            {
                return;
            }

            if (open)
            {
                // Under the button, or above it when the toolbar sits low on the screen.
                float height = CurveNames.Length * MenuRowHeight + 2 * MenuPadding;
                Rect bounds = PanelScreenBounds(toolbar, PanelCamera());
                bool upwards = bounds.yMin < height + 8f;
                float x = autoKeyButton.rectTransform.anchoredPosition.x;
                Put(autoKeyMenu, x, upwards ? -(height + 2f) : ToolbarHeight + 2f, MenuWidth, height);
                autoKeyMenu.SetAsLastSibling();
                ShowCurveChoice();
            }

            autoKeyMenu.gameObject.SetActive(open);
        }

        void ChooseCurve(int choice)
        {
            autoKeyCurveSetting.Value = CurveNames[choice];
            Config.Save();
            ShowCurveChoice();
            ShowCurveMenu(false);
            Note(choice == 0
                ? "Auto Key: new keys copy the previous key's curve"
                : "Auto Key: new keys use the " + CurveLabels[choice] + " curve");
        }

        void ShowCurveChoice()
        {
            int chosen = Math.Max(0, Array.IndexOf(CurveNames, autoKeyCurveSetting.Value));
            for (int i = 0; i < curveRows.Count; i++)
            {
                curveRows[i].color = i == chosen ? RecordOnColor : new Color(0f, 0f, 0f, 0f);
            }
        }

        // A click anywhere outside the menu and its arrow closes the menu.
        void PollCurveMenu()
        {
            if (autoKeyMenu == null || !autoKeyMenu.gameObject.activeSelf)
            {
                return;
            }

            if (Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1) || Input.GetMouseButtonDown(2))
            {
                Camera camera = PanelCamera();
                if (!RectTransformUtility.RectangleContainsScreenPoint(autoKeyMenu, Input.mousePosition, camera)
                    && !RectTransformUtility.RectangleContainsScreenPoint(autoKeyArrow, Input.mousePosition, camera))
                {
                    ShowCurveMenu(false);
                }
            }
        }

        // ---- curves ---------------------------------------------------------------------------

        // Timeline's preset by menu index; null for "Previous key" or when this Timeline lacks it.
        AnimationCurve PresetCurve(int index)
        {
            if (index <= 0 || index >= CurveFields.Length)
            {
                return null;
            }

            try
            {
                return Get<AnimationCurve>(native, CurveFields[index]);
            }
            catch
            {
                return null;
            }
        }

        // The curve for keys that Auto Key adds; null keeps Timeline's own choice.
        AnimationCurve AutoKeyCurve()
        {
            return PresetCurve(Array.IndexOf(CurveNames, autoKeyCurveSetting.Value));
        }

        void ShowRecordState()
        {
            if (autoKeyButton != null)
            {
                autoKeyButton.color = autoKey ? RecordOnColor : ButtonColor;
            }

            if (autoKeyDot != null)
            {
                autoKeyDot.color = autoKey ? DotOnColor : DotOffColor;
            }
        }

        // ---- generated icons --------------------------------------------------------------------

        static Sprite Dot()
        {
            if (dotSprite == null)
            {
                const int Size = 32;
                Texture2D texture = NewIcon(Size, Size);
                float centre = (Size - 1) / 2f, radius = Size / 2f - 1f;
                for (int y = 0; y < Size; y++)
                {
                    for (int x = 0; x < Size; x++)
                    {
                        float distance = Mathf.Sqrt((x - centre) * (x - centre) + (y - centre) * (y - centre));
                        texture.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(radius - distance + 0.5f)));
                    }
                }

                dotSprite = ToSprite(texture);
            }

            return dotSprite;
        }

        // A downward chevron, as on Blender's menu buttons.
        static Sprite Arrow()
        {
            if (arrowSprite == null)
            {
                const int Width = 16, Height = 10;
                Texture2D texture = NewIcon(Width, Height);
                for (float t = 0f; t <= 1f; t += 0.01f)
                {
                    float x = t * (Width - 1), y = Height - 2 - (1f - Mathf.Abs(t * 2f - 1f)) * (Height - 4);
                    Plot(texture, x, y, Color.white);
                }

                texture.Apply();
                arrowSprite = ToSprite(texture);
            }

            return arrowSprite;
        }

        static Sprite CurveSprite(AnimationCurve curve)
        {
            Texture2D texture = NewIcon(IconWidth * 2, IconHeight * 2);
            float margin = 3f, width = IconWidth * 2 - 1 - 2 * margin, height = IconHeight * 2 - 1 - 2 * margin;
            Vector2 previous = Vector2.zero;
            const int Samples = 160;
            for (int i = 0; i <= Samples; i++)
            {
                float t = i / (float)Samples;
                Vector2 point = new Vector2(margin + t * width, margin + Mathf.Clamp01(curve.Evaluate(t)) * height);
                if (i > 0)
                {
                    // Join the samples so that a step (Stairs) is drawn as a vertical line.
                    int steps = Mathf.CeilToInt(Vector2.Distance(previous, point) * 2f) + 1;
                    for (int s = 0; s <= steps; s++)
                    {
                        Vector2 at = Vector2.Lerp(previous, point, s / (float)steps);
                        Plot(texture, at.x, at.y, CurveLineColor);
                    }
                }

                previous = point;
            }

            texture.Apply();
            return ToSprite(texture);
        }

        // A 2 x 2 pixel dot: the line stays visible after the icon is scaled down.
        static void Plot(Texture2D texture, float x, float y, Color color)
        {
            int px = Mathf.RoundToInt(x), py = Mathf.RoundToInt(y);
            for (int dx = 0; dx <= 1; dx++)
            {
                for (int dy = 0; dy <= 1; dy++)
                {
                    if (px + dx < texture.width && py + dy < texture.height && px + dx >= 0 && py + dy >= 0)
                    {
                        texture.SetPixel(px + dx, py + dy, color);
                    }
                }
            }
        }

        static Texture2D NewIcon(int width, int height)
        {
            Texture2D texture = new Texture2D(width, height, TextureFormat.ARGB32, false);
            Color clear = new Color(1f, 1f, 1f, 0f);
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    texture.SetPixel(x, y, clear);
                }
            }

            texture.filterMode = FilterMode.Bilinear;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.hideFlags = HideFlags.HideAndDontSave;
            return texture;
        }

        static Sprite ToSprite(Texture2D texture)
        {
            texture.Apply();
            Sprite sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(.5f, .5f));
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }
    }
}
