using System;
using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace GinAdvanceHack
{
    public sealed partial class Plugin
    {
        private sealed class CameraGuardLife
        {
            public bool Active = true;
        }

        private void InstallCameraGuard()
        {
            if (camera || Studio.Studio.Instance == null || !Studio.Studio.Instance.cameraCtrl)
            {
                return;
            }

            camera = Studio.Studio.Instance.cameraCtrl;
            oldMouse = camera.noCtrlCondition;
            oldKeys = camera.keyCondition;
            oldZoom = camera.zoomCondition;
            Studio.CameraControl.NoCtrlFunc m = oldMouse, k = oldKeys, z = oldZoom;
            CameraGuardLife life = new CameraGuardLife();
            guardLife = life;
            FieldInfo dragging = AccessTools.Field(typeof(Studio.CameraControl), "dragging");
            mouseGuard = delegate
            {
                bool blocked = life.Active && (IsPointerOverExtensionUI || IsTextInputActive);
                if (blocked && !Input.GetMouseButton(0) && !Input.GetMouseButton(1) && !Input.GetMouseButton(2) && dragging != null && camera)
                {
                    dragging.SetValue(camera, false);
                }

                return (m != null && m()) || blocked;
            };
            keysGuard = delegate
            {
                return (k == null || k()) && !(life.Active && IsTextInputActive);
            };
            zoomGuard = delegate
            {
                return (z == null || z()) && !(life.Active && (IsPointerOverExtensionUI || IsTextInputActive));
            };
            camera.noCtrlCondition = mouseGuard;
            camera.keyCondition = keysGuard;
            camera.zoomCondition = zoomGuard;
        }
    }
}
