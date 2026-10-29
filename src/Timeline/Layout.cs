using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Timeline;
using Key = System.Collections.Generic.KeyValuePair<float, Timeline.Keyframe>;
using Native = Timeline.Timeline;
using Object = System.Object;

namespace GinTimelineHack
{
    public sealed partial class Plugin
    {
        readonly Vector3[] panelCorners = new Vector3[4];

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
            bg.color = new Color(.16f, .29f, .39f, 1);
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
