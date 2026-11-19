using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GinPadHack
{
    // An IMGUI window does not stop Studio from reacting to the mouse underneath it.
    // Two guards keep clicks and drags on the Pad window away from Studio (see Plugin below):
    //
    //   * pointer catch - everything in Studio that reacts to the mouse through Unity's EventSystem
    //     (its UI, the move / rotate / scale gizmos of guide objects, clickable objects) asks the
    //     EventSystem what is under the pointer. While the pointer is on the window, or a press began
    //     there, the answer is one inert object of the Pad's own, so nothing behind the window reacts.
    //     An invisible blocker image would not do: a gizmo can still win that raycast.
    //   * camera block  - Studio's camera reads the mouse itself and skips its mouse and wheel handling.
    //   * camera pin    - and whatever else might turn the camera (other plugins read the mouse too):
    //     while a press that began on the window is held, Studio's camera keeps the place it had when
    //     the press began. Not while Timeline plays, so that animated cameras keep moving.
    internal sealed class PointerCatcher : IDisposable
    {
        GameObject root, catcher;
        GraphicRaycaster raycaster;

        // The single raycast result that stands for "the Pad window".
        internal RaycastResult Hit(Vector2 position)
        {
            if (root == null)
            {
                root = new GameObject("GIN Pad pointer catcher", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
                UnityEngine.Object.DontDestroyOnLoad(root);
                Canvas canvas = root.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 32759;
                raycaster = root.GetComponent<GraphicRaycaster>();
                catcher = new GameObject("GIN Pad window", typeof(RectTransform));
                catcher.transform.SetParent(root.transform, false);
            }

            RaycastResult hit = new RaycastResult();
            hit.gameObject = catcher;
            hit.module = raycaster;
            hit.distance = 0f;
            hit.index = 0;
            hit.depth = int.MaxValue;
            hit.sortingOrder = 32759;
            hit.screenPosition = position;
            return hit;
        }

        internal bool IsCatcher(GameObject target)
        {
            return target != null && target == catcher;
        }

        public void Dispose()
        {
            if (root != null)
            {
                UnityEngine.Object.Destroy(root);
            }

            root = catcher = null;
            raycaster = null;
        }
    }

    public sealed partial class Plugin
    {
        Harmony inputBlock;
        Studio.CameraControl.CameraData pinnedCamera;
        int cameraCorrections;
        static PropertyInfo timelinePlaying;

        void InstallInputBlock()
        {
            inputBlock = new Harmony(Guid);

            // Studio's camera asks its "noCtrlCondition" delegate whether to ignore the mouse. Studio
            // itself appends its own condition to that delegate while it starts, and a delegate with
            // several entries returns only the LAST entry's answer. A condition installed early is
            // therefore called but ignored. Instead, the two camera methods that read the mouse are
            // skipped directly while the Pad window owns the pointer.
            HarmonyMethod skip = new HarmonyMethod(typeof(Plugin).GetMethod("SkipCameraInput", BindingFlags.Static | BindingFlags.NonPublic));
            foreach (string name in new string[] { "InputMouseProc", "InputMouseWheelZoomProc" })
            {
                MethodInfo method = typeof(Studio.CameraControl).GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (method == null)
                {
                    Logger.LogWarning("Studio camera method not found: " + name + ". The camera may move while dragging on the Pad window.");
                    continue;
                }

                inputBlock.Patch(method, skip, method.Name == "InputMouseProc" ? new HarmonyMethod(typeof(Plugin).GetMethod("ReportCameraMouse", BindingFlags.Static | BindingFlags.NonPublic)) : null, null, null, null);
            }

            MethodInfo lateUpdate = typeof(Studio.CameraControl).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic);
            if (lateUpdate != null)
            {
                inputBlock.Patch(lateUpdate, new HarmonyMethod(typeof(Plugin).GetMethod("PinCamera", BindingFlags.Static | BindingFlags.NonPublic)), null, null, null, null);
            }

            Type timeline = Reflect.Type("Timeline.Timeline");
            timelinePlaying = timeline == null ? null : timeline.GetProperty("isPlaying", BindingFlags.Static | BindingFlags.Public);

            MethodInfo raycastAll = typeof(EventSystem).GetMethod("RaycastAll", new Type[] { typeof(PointerEventData), typeof(List<RaycastResult>) });
            inputBlock.Patch(raycastAll, null, new HarmonyMethod(typeof(Plugin).GetMethod("CatchPointer", BindingFlags.Static | BindingFlags.NonPublic)), null, null, null);
        }

        // Harmony prefix: answer "no camera input this frame" without running Studio's method.
        static bool SkipCameraInput(ref bool __result)
        {
            if (Instance == null || Instance.window == null || !Instance.window.OwnsPointer)
            {
                return true;
            }

            __result = false;
            return false;
        }

        // Harmony postfix of Studio's mouse camera: if Studio turned the camera although the pointer is
        // on the window, write one line to the log, so that a remaining gap can be found.
        static void ReportCameraMouse(bool __result)
        {
            Plugin self = Instance;
            if (!__result || self == null || self.window == null || !self.window.Covers(Input.mousePosition) || self.cameraCorrections >= 10)
            {
                return;
            }

            self.cameraCorrections++;
            self.Logger.LogInfo("Studio's camera followed the mouse on the Pad window (" + self.window.PointerState() + ")");
        }

        // Harmony prefix of Studio's camera update: while a press on the window is held, put back the
        // place the camera had when the press began, before the camera applies it to the view.
        static void PinCamera(Studio.CameraControl __instance)
        {
            Plugin self = Instance;
            if (self == null || self.window == null || !Singleton<Studio.Studio>.IsInstance() || __instance != Singleton<Studio.Studio>.Instance.cameraCtrl)
            {
                return;
            }

            bool playing = timelinePlaying != null && (bool)timelinePlaying.GetValue(null, null);
            if (!self.window.HoldsPress || playing)
            {
                self.pinnedCamera = null;
                return;
            }

            if (self.pinnedCamera == null)
            {
                self.pinnedCamera = __instance.Export();
                return;
            }

            Studio.CameraControl.CameraData now = __instance.Export();
            if (now.pos != self.pinnedCamera.pos || now.rotate != self.pinnedCamera.rotate || now.distance != self.pinnedCamera.distance || now.parse != self.pinnedCamera.parse)
            {
                __instance.Import(self.pinnedCamera);
                if (self.cameraCorrections++ < 10)
                {
                    self.Logger.LogInfo("Camera change undone while a press on the Pad window was held (" + self.window.PointerState() + ")");
                }
            }
        }

        // Harmony postfix of EventSystem.RaycastAll: on the window, the window is all there is.
        static void CatchPointer(PointerEventData eventData, List<RaycastResult> raycastResults)
        {
            if (Instance == null || Instance.window == null || Instance.catcher == null || !Instance.window.Covers(eventData.position))
            {
                return;
            }

            raycastResults.Clear();
            raycastResults.Add(Instance.catcher.Hit(eventData.position));
        }

        void RemoveInputBlock()
        {
            if (inputBlock != null)
            {
                inputBlock.UnpatchSelf();
                inputBlock = null;
            }
        }
    }
}
