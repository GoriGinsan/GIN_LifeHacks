using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GinTimelineHack
{
    public sealed partial class Plugin
    {
        const float ToolbarHeight = 24f, ToolbarGap = 2f;
        const float FeedbackWidth = 650f, FeedbackHeight = 21f;
        internal static readonly Color ButtonColor = new Color(.16f, .29f, .39f, 1f);

        readonly Vector3[] panelCorners = new Vector3[4];
        float toolbarWidth;

        // The GIN button row above (or, when space is short, below) the Timeline window.
        void BuildToolbar()
        {
            toolbar = MakeRect("GIN Timeline tools", window, true);
            float x = 0;
            AddToolbarButton("Anchor", ref x, 68, delegate
            {
                CaptureAnchor();
            });
            AddToolbarButton("1 / 3 / 5", ref x, 88, delegate
            {
                SelectAlternating(true);
            });
            AddToolbarButton("2 / 4 / 6", ref x, 88, delegate
            {
                SelectAlternating(false);
            });
            AddToolbarButton("+ Tracks", ref x, 86, delegate
            {
                rightCandidates.Value = !rightCandidates.Value;
                Config.Save();
            });
            AddToolbarButton("Char Setup", ref x, 92, delegate
            {
                SetupSelectedCharacters();
            });
            AddAutoKeyControl(ref x);
            toolbarWidth = x;
            Put(toolbar, 330, -26, toolbarWidth, ToolbarHeight);

            RectTransform status = MakeRect("GIN Timeline feedback", toolbar, false);
            Put(status, 0, -23, FeedbackWidth, FeedbackHeight);
            statusText = status.gameObject.AddComponent<Text>();
            statusText.font = font;
            statusText.fontSize = 14;
            statusText.color = Color.white;
            statusText.raycastTarget = false;

            // Red line along the top edge of the Timeline window while Auto Key is on.
            RectTransform stripe = MakeRect("GIN Auto Key indicator", window, true);
            stripe.anchorMin = new Vector2(0, 1);
            stripe.anchorMax = Vector2.one;
            stripe.offsetMin = new Vector2(0, -4);
            stripe.offsetMax = Vector2.zero;
            autoKeyStripe = stripe.gameObject.AddComponent<Image>();
            autoKeyStripe.color = AutoKeyOnColor;
            autoKeyStripe.raycastTarget = false;
            ShowAutoKeyState();
        }

        Button AddToolbarButton(string label, ref float x, float width, Action action)
        {
            Button button = MakeButton(toolbar, label, x, 0, width, ToolbarHeight, action);
            x += width + ToolbarGap;
            return button;
        }

        internal RectTransform MakeRect(string name, Transform parent, bool own)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            if (own)
            {
                owned.Add(go);
            }

            return (RectTransform)go.transform;
        }

        internal static void Put(RectTransform rt, float x, float y, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(w, h);
        }

        internal static void Fill(RectTransform rt, float l, float b, float r, float t)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(l, b);
            rt.offsetMax = new Vector2(-r, -t);
            rt.localScale = Vector3.one;
        }

        Text Label(Transform parent, string value, int size)
        {
            RectTransform rt = MakeRect("Label", parent, false);
            Fill(rt, 3, 1, 3, 1);
            Text text = rt.gameObject.AddComponent<Text>();
            text.font = font;
            text.fontSize = size;
            text.text = value;
            text.color = Color.white;
            text.alignment = TextAnchor.MiddleCenter;
            text.raycastTarget = false;
            return text;
        }

        Button MakeButton(Transform parent, string value, float x, float y, float w, float h, Action action)
        {
            RectTransform rt = MakeRect(value, parent, false);
            Put(rt, x, y, w, h);
            Image bg = rt.gameObject.AddComponent<Image>();
            bg.color = ButtonColor;
            Button button = rt.gameObject.AddComponent<Button>();
            button.targetGraphic = bg;
            ColorBlock colors = button.colors;
            colors.highlightedColor = new Color(.7f, .85f, 1);
            button.colors = colors;
            Label(rt, value, 14);
            button.onClick.AddListener(delegate
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
            });
            return button;
        }
    }

    public sealed class PointerAction : MonoBehaviour, IPointerDownHandler
    {
        public Action<PointerEventData> action;

        public void OnPointerDown(PointerEventData e)
        {
            if (action != null)
            {
                action(e);
            }
        }
    }
}
