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
        void PositionPanels()
        {
            if (sidebar == null || window == null)
            {
                return;
            }

            if (candidateTitle != null)
            {
                candidateTitle.text = "Interpolables\n" + AddShortcutLabel();
            }

            sidebar.anchorMin = new Vector2(1, 0);
            sidebar.anchorMax = Vector2.one;
            sidebar.offsetMin = new Vector2(4, 0);
            sidebar.offsetMax = new Vector2(candidateWidth.Value + 4, 0);
            sidebar.gameObject.SetActive(rightCandidates.Value);
            if (toolbar != null)
            {
                float x = 330, y = -26;
                float right = 0, top = 0;
                bool found = false;
                foreach (RectTransform rt in toolbarNeighbors)
                {
                    if (rt == null || !rt.gameObject.activeInHierarchy)
                    {
                        continue;
                    }

                    rt.GetWorldCorners(panelCorners);
                    Vector3 tr = window.InverseTransformPoint(panelCorners[2]);
                    if (!found || tr.x > right)
                    {
                        right = tr.x;
                        top = tr.y;
                        found = true;
                    }
                }

                if (found)
                {
                    x = right - window.rect.xMin + 3;
                    y = window.rect.yMax - top;
                }

                // Always test the preferred placement, including the feedback even while it is blank.
                // Testing the previous lower placement would alternate sides every frame.
                Put(toolbar, x, y, 338, 24);
                if (statusText != null)
                {
                    Put(statusText.rectTransform, 0, -23, 650, 21);
                }

                Camera camera = PanelCamera();
                Rect bounds = ToolScreenBounds(camera);
                bool below = bounds.yMax > Screen.height - 2;
                // Screen-edge clamping can move the tools toward the handle too.
                // Evaluate the preferred placement afresh each frame so widening restores it.
                if (!below)
                {
                    KeepToolsOnScreen(camera);
                    if (resizeHandle != null && resizeHandle.gameObject.activeInHierarchy)
                    {
                        Rect handle = PanelScreenBounds(resizeHandle, camera);
                        handle.xMin -= 4;
                        handle.xMax += 4;
                        handle.yMin -= 4;
                        handle.yMax += 4;
                        below = PanelScreenBounds(toolbar, camera).Overlaps(handle);
                    }
                }

                if (below)
                {
                    Put(toolbar, x, window.rect.height + 3, 338, 24);
                    if (statusText != null)
                    {
                        Put(statusText.rectTransform, 0, 26, 650, 21);
                    }
                }

                KeepToolsOnScreen(camera);
            }
        }

        void KeepToolsOnScreen(Camera camera)
        {
            Rect bounds = ToolScreenBounds(camera);
            Vector2 shift = new Vector2(ScreenShift(bounds.xMin, bounds.xMax, Screen.width), ScreenShift(bounds.yMin, bounds.yMax, Screen.height));
            if (shift.sqrMagnitude <= .0001f)
            {
                return;
            }

            Vector2 before, after;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(window, bounds.center, camera, out before) && RectTransformUtility.ScreenPointToLocalPointInRectangle(window, bounds.center + shift, camera, out after))
            {
                toolbar.anchoredPosition += after - before;
            }
        }

        Camera PanelCamera()
        {
            Canvas root = canvas == null ? null : canvas.rootCanvas;
            return root == null || root.renderMode == RenderMode.ScreenSpaceOverlay ? null : root.worldCamera;
        }

        Rect PanelScreenBounds(RectTransform rt, Camera camera)
        {
            rt.GetWorldCorners(panelCorners);
            Vector2 first = RectTransformUtility.WorldToScreenPoint(camera, panelCorners[0]);
            Rect bounds = new Rect(first.x, first.y, 0, 0);
            for (int i = 1; i < panelCorners.Length; i++)
            {
                Vector2 point = RectTransformUtility.WorldToScreenPoint(camera, panelCorners[i]);
                bounds.xMin = Math.Min(bounds.xMin, point.x);
                bounds.xMax = Math.Max(bounds.xMax, point.x);
                bounds.yMin = Math.Min(bounds.yMin, point.y);
                bounds.yMax = Math.Max(bounds.yMax, point.y);
            }

            return bounds;
        }

        Rect ToolScreenBounds(Camera camera)
        {
            Rect bounds = PanelScreenBounds(toolbar, camera);
            if (statusText != null)
            {
                Rect feedback = PanelScreenBounds(statusText.rectTransform, camera);
                bounds.xMin = Math.Min(bounds.xMin, feedback.xMin);
                bounds.xMax = Math.Max(bounds.xMax, feedback.xMax);
                bounds.yMin = Math.Min(bounds.yMin, feedback.yMin);
                bounds.yMax = Math.Max(bounds.yMax, feedback.yMax);
            }

            return bounds;
        }

        static float ScreenShift(float min, float max, float screenSize)
        {
            if (max - min > screenSize - 4)
            {
                return 2 - min;
            }

            if (min < 2)
            {
                return 2 - min;
            }

            if (max > screenSize - 2)
            {
                return screenSize - 2 - max;
            }

            return 0;
        }
    }
}
