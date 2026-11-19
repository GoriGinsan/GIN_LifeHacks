using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using GinShared;
using UnityEngine;

[assembly: AssemblyVersion(GinAdvanceHack.Plugin.Version + ".0")]
[assembly: AssemblyFileVersion(GinAdvanceHack.Plugin.Version + ".0")]
namespace GinAdvanceHack
{
    [BepInPlugin(Guid, "GIN Advance mode life hack", Version)]
    [BepInProcess("CharaStudio.exe")]
    public sealed partial class Plugin : BaseUnityPlugin
    {
        public const string Guid = "local.kkworkshop.0a52b20541f5.gin-advance-life-hack";
        public const string Version = "1.1.0";
        internal static Plugin Instance;
        internal ConfigEntry<bool> Enabled, LargeControls, CompactSpacing, VisibilityTools, ScrubBones, TwinShortcut, Expressions;
        internal ConfigEntry<int> ButtonHeight, ThumbSize;
        internal ConfigEntry<float> ScrubPixels;
        internal ConfigEntry<string> Language, TimelineGroupRenderers;
        internal ExpressionLibrary Library;
        private AdvancedUi ui;
        private UiInputShield inputShield;
        private float retry;
        private bool failed;
        private Studio.CameraControl camera;
        private Studio.CameraControl.NoCtrlFunc oldMouse, oldKeys, oldZoom, mouseGuard, keysGuard, zoomGuard;
        private CameraGuardLife guardLife;

        public static bool IsTextInputActive
        {
            get
            {
                return Instance != null && Instance.Enabled.Value && (AdvancedUi.TextInputActive || (Instance.Library != null && Instance.Library.TextInputActive));
            }
        }

        public static bool IsPointerOverExtensionUI
        {
            get
            {
                return Instance != null && Instance.Enabled.Value && (AdvancedUi.PointerOverExtension || (Instance.Library != null && Instance.Library.IsPointerOverUI));
            }
        }

        public static bool IsAdvancedVisible
        {
            get
            {
                return AdvancedUi.NativeVisible;
            }
        }

        public static bool IsPointerOverAdvancedUI
        {
            get
            {
                return AdvancedUi.NativeVisible && AdvancedUi.NativeRect.Contains(new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y));
            }
        }

        public bool Ready
        {
            get
            {
                return ui != null;
            }
        }

        internal void Report(string s)
        {
            Logger.LogInfo(s);
        }

        private void Awake()
        {
            Instance = this;
            Enabled = Config.Bind("General", "Enabled", true, "Enable the additions. False restores native KKPE UI; expression files remain.");
            Language = Config.Bind(
                "General",
                "Language",
                "en",
                "Legacy setting retained for compatibility. GIN additions use English; native KKPE labels and saved names are unchanged.");
            LargeControls = Config.Bind("UI", "LargerControls", true, "Only KKPE Advanced mode receives larger controls.");
            CompactSpacing = Config.Bind("UI", "CompactSpacing", true, "Reduce margins only inside Advanced mode.");
            ButtonHeight = Config.Bind(
                "UI",
                "ButtonHeight",
                26,
                new ConfigDescription("Button and numeric field height", new AcceptableValueRange<int>(20, 40)));
            ThumbSize = Config.Bind(
                "UI",
                "SliderThumbSize",
                22,
                new ConfigDescription("Slider thumb / scrollbar hit area", new AcceptableValueRange<int>(14, 36)));
            VisibilityTools = Config.Bind("BlendShapes", "VisibilityTools", true, "Hide rows without changing their values; named display templates.");
            TimelineGroupRenderers = Config.Bind(
                "BlendShapes",
                "TimelineGroupRenderers",
                "cf_O_face,cf_O_head,cf_O_mayuge",
                "Renderers that the Timeline 0:00 buttons register as ONE group row (all their Blend Shapes together, linked). Face and eyebrow shapes break when keyed one by one. Other renderers get one row per Blend Shape.");
            ScrubBones = Config.Bind(
                "Bones",
                "DragIncrementButtons",
                true,
                "Click +/- for one step; hold and drag horizontally to scrub. Shift fine, Ctrl coarse; Escape cancels current scrub.");
            ScrubPixels = Config.Bind(
                "Bones",
                "PixelsPerStep",
                8f,
                new ConfigDescription("Horizontal distance for one native increment", new AcceptableValueRange<float>(2f, 40f)));
            TwinShortcut = Config.Bind(
                "Bones",
                "TwinButtonNearCopy",
                true,
                "Add L/R immediately before the native Copy Position / Rotation / Scale button.");
            Expressions = Config.Bind("Expressions", "Enabled", true, "Expression library beside the native Advanced mode window.");
            Enabled.SettingChanged += delegate
            {
                failed = false;
                if (!Enabled.Value)
                {
                    StopExtensions();
                }
            };
        }

        private void Update()
        {
            EndlessDrag.Update();
            if (!Enabled.Value)
            {
                return;
            }

            if (ui == null && !failed && Time.realtimeSinceStartup >= retry)
            {
                retry = Time.realtimeSinceStartup + 2;
                Type kkpe = ReflectionAccess.Type("HSPE.MainWindow");
                if (kkpe == null || ReflectionAccess.Get(kkpe, "_self") == null)
                {
                    return;
                }

                try
                {
                    ui = new AdvancedUi(this);
                    Library = new ExpressionLibrary(Path.Combine(Paths.GameRootPath, "UserData/GIN/Expressions"), Report);
                    inputShield = new UiInputShield();
                    Logger.LogInfo("GIN Advanced mode life hack " + Version + " attached to KKPE " + kkpe.Assembly.GetName().Version + ".");
                }
                catch (Exception ex)
                {
                    failed = true;
                    StopExtensions();
                    Logger.LogError("Native KKPE retained: " + ex);
                }
            }

            if (ui == null)
            {
                return;
            }

            InstallCameraGuard();
            ui.Tick();
            if (Library != null && Expressions.Value)
            {
                Library.Tick();
            }
        }

        private void LateUpdate()
        {
            if (inputShield != null)
            {
                List<Rect> expressionRects = new List<Rect>();
                if (Expressions.Value && Library != null)
                {
                    expressionRects.AddRange(Library.InputRects);
                }

                inputShield.Update(AdvancedUi.ExtensionInputRects, expressionRects.ToArray());
            }
        }

        private void StopExtensions()
        {
            if (ui != null)
            {
                ui.Dispose();
                ui = null;
            }

            if (Library != null)
            {
                Library.Dispose();
                Library = null;
            }

            if (inputShield != null)
            {
                inputShield.Dispose();
                inputShield = null;
            }

            if (guardLife != null)
            {
                guardLife.Active = false;
            }

            if (camera)
            {
                if (camera.noCtrlCondition == mouseGuard)
                {
                    camera.noCtrlCondition = oldMouse;
                }

                if (camera.keyCondition == keysGuard)
                {
                    camera.keyCondition = oldKeys;
                }

                if (camera.zoomCondition == zoomGuard)
                {
                    camera.zoomCondition = oldZoom;
                }
            }

            camera = null;
        }

        private void OnDestroy()
        {
            StopExtensions();
            if (Instance == this)
            {
                Instance = null;
            }
        }
    }
}
