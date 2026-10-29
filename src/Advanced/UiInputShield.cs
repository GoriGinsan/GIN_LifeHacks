using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace GinAdvanceHack
{
    // IMGUI windows do not stop uGUI raycasts by themselves. These transparent images
    // shield only our visible extension rectangles; no Studio canvas is disabled.
    internal sealed class UiInputShield : IDisposable
    {
        private GameObject root;
        private readonly List<RectTransform> blockers = new List<RectTransform>();

        internal void Update(Rect[] first, Rect[] second)
        {
            int count = first.Length + second.Length;
            if (root == null && count > 0)
            {
                root = new GameObject("GIN Advanced input shield", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
                UnityEngine.Object.DontDestroyOnLoad(root);
                Canvas canvas = root.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 32760;
            }
            if (root == null)
            {
                return;
            }

            while (blockers.Count < count)
            {
                GameObject item = new GameObject("Extension input rectangle", typeof(RectTransform), typeof(Image));
                item.transform.SetParent(root.transform, false);
                RectTransform rt = (RectTransform)item.transform;
                rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0, 1);
                Image image = item.GetComponent<Image>();
                image.color = new Color(0, 0, 0, 0);
                image.raycastTarget = true;
                blockers.Add(rt);
            }
            for (int i = 0; i < blockers.Count; i++)
            {
                bool active = i < count;
                blockers[i].gameObject.SetActive(active);
                if (!active)
                {
                    continue;
                }

                Rect r = i < first.Length ? first[i] : second[i - first.Length];
                blockers[i].anchoredPosition = new Vector2(r.x, -r.y);
                blockers[i].sizeDelta = new Vector2(r.width, r.height);
            }
        }

        public void Dispose()
        {
            if (root != null)
            {
                UnityEngine.Object.Destroy(root);
            }

            root = null;
            blockers.Clear();
        }
    }
}
