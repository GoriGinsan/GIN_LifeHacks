using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
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
        void InstallCameraGuard()
        {
            if (Studio.Studio.Instance == null || Studio.Studio.Instance.cameraCtrl == null)
            {
                return;
            }

            guardedCamera = Studio.Studio.Instance.cameraCtrl;
            previousKeys = guardedCamera.keyCondition;
            previousMouse = guardedCamera.noCtrlCondition;
            previousZoom = guardedCamera.zoomCondition;
            cameraDragging = typeof(Studio.CameraControl).GetField("dragging", Flags);
            int epoch = ++guardEpoch;
            Studio.CameraControl.NoCtrlFunc before = previousKeys;
            keyGuard = delegate
            {
                if (before != null && !before())
                {
                    return false;
                }

                if (!attached || epoch != guardEpoch)
                {
                    return true;
                }

                bool commitHeld = CommitShortcutPressed(false) && CanUseShortcut(true);
                bool moveHeld = (Input.GetKey(upShortcut.Value.MainKey) || Input.GetKey(downShortcut.Value.MainKey)) && Shift && CanUseShortcut(false);
                return !(commitHeld || moveHeld || TextFocused());
            };
            Studio.CameraControl.NoCtrlFunc mouseBefore = previousMouse, zoomBefore = previousZoom;
            mouseGuard = delegate
            {
                bool blocked = attached && epoch == guardEpoch && PointerOverOwnedUi();
                ClearReleasedDrag(blocked, Input.GetMouseButton(0) || Input.GetMouseButton(1) || Input.GetMouseButton(2));
                return (mouseBefore != null && mouseBefore()) || blocked;
            };
            zoomGuard = delegate
            {
                return (zoomBefore == null || zoomBefore()) && !(attached && epoch == guardEpoch && PointerOverOwnedUi());
            };
            guardedCamera.keyCondition = keyGuard;
            guardedCamera.noCtrlCondition = mouseGuard;
            guardedCamera.zoomCondition = zoomGuard;
        }

        bool PointerOverOwnedUi()
        {
            if (!attached || !Native.InterfaceVisible)
            {
                return false;
            }

            Vector2 point = Input.mousePosition;
            return (sidebar != null && sidebar.gameObject.activeInHierarchy && RectTransformUtility.RectangleContainsScreenPoint(sidebar, point, null)) || (toolbar != null && toolbar.gameObject.activeInHierarchy && RectTransformUtility.RectangleContainsScreenPoint(toolbar, point, null));
        }

        void ClearReleasedDrag(bool blocked, bool held)
        {
            if (blocked && !held && guardedCamera != null && cameraDragging != null && cameraDragging.FieldType == typeof(bool))
            {
                cameraDragging.SetValue(guardedCamera, false);
            }
        }
    }
}
