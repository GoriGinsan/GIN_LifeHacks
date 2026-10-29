using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using System.Xml;
using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace GinAdvanceHack
{
    // This adapter never disables a renderer, bone, module, or native expression.
    internal sealed partial class AdvancedUi : IDisposable
    {
        private static AdvancedUi current;
        private readonly Plugin host;
        private readonly Harmony harmony;
        private readonly Type mainType, poseType, bonesType, blendsType;
        private readonly string displayDirectory;
        private readonly List<MethodBase> patched = new List<MethodBase>();
        private GUISkin sourceSkin, localSkin;
        private static object bonesContext, blendContext;
        private static string currentBlendKey;
        private static int textIndex;
        private bool disposed, dirty, showHidden, templatesOpen;
        private float saveAt;
        private string templateName = "Default", message = "";
        private Vector2 templatesScroll;
        private Rect templateRect = new Rect(50, 90, 480, 380);
        private readonly HashSet<string> hidden = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> collapsed = new HashSet<string>(StringComparer.Ordinal);
        private string[] templateFiles = new string[0];
        private static bool textInput;
        private static bool restoringNative;
        private bool clearTextFocus;
        private static int textFrame;
        private static readonly Dictionary<int, ScrubGesture> scrubs = new Dictionary<int, ScrubGesture>();

        internal static bool TextInputActive
        {
            get
            {
                return current != null && textInput && Time.frameCount - textFrame < 3;
            }
        }

        internal static bool PointerOverExtension
        {
            get
            {
                return current != null && current.templatesOpen && current.templateRect.Contains(Mouse());
            }
        }

        internal static Rect[] ExtensionInputRects
        {
            get
            {
                return current != null && NativeVisible && current.templatesOpen ? new Rect[]
                {
                    current.templateRect
                }

                : new Rect[0];
            }
        }

        internal static Rect NativeRect
        {
            get
            {
                object o = ReflectionAccess.Get(ReflectionAccess.Type("HSPE.MainWindow"), "_self");
                object r = ReflectionAccess.Get(o, "_advancedModeRect");
                return r is Rect ? (Rect)r : new Rect();
            }
        }

        internal static bool NativeVisible
        {
            get
            {
                Type p = ReflectionAccess.Type("HSPE.PoseController");
                object v = ReflectionAccess.Get(p, "_drawAdvancedMode");
                object m = ReflectionAccess.Get(ReflectionAccess.Type("HSPE.MainWindow"), "_self");
                return v is bool && (bool)v && ReflectionAccess.Get(m, "_poseTarget") is UnityEngine.Object && (UnityEngine.Object)ReflectionAccess.Get(m, "_poseTarget") != null;
            }
        }

        internal AdvancedUi(Plugin plugin)
        {
            host = plugin;
            mainType = ReflectionAccess.Type("HSPE.MainWindow");
            poseType = ReflectionAccess.Type("HSPE.PoseController");
            bonesType = ReflectionAccess.Type("HSPE.AMModules.BonesEditor");
            blendsType = ReflectionAccess.Type("HSPE.AMModules.BlendShapesEditor");
            Version nativeVersion = mainType == null ? null : mainType.Assembly.GetName().Version;
            if (nativeVersion != new Version(2, 18, 1, 0) && nativeVersion != new Version(2, 21, 5, 0))
            {
                throw new NotSupportedException("Inspected KKPE APIs: 2.18.1.0 and 2.21.5.0; found " + (nativeVersion == null ? "none" : nativeVersion.ToString()) + ". Native KKPE UI is retained.");
            }

            displayDirectory = Path.Combine(Paths.GameRootPath, "UserData/GIN/AdvancedDisplay");
            Directory.CreateDirectory(displayDirectory);
            if (File.Exists(Path.Combine(displayDirectory, "active.xml")))
            {
                try
                {
                    LoadDisplay(Path.Combine(displayDirectory, "active.xml"));
                }
                catch (Exception e)
                {
                    host.Report("Display preferences ignored; native values retained: " + e.Message);
                }
            }

            RefreshTemplates();
            harmony = new Harmony(Plugin.Guid + ".ui");
            current = this;
            restoringNative = false;
            try
            {
                Patch(
                    AccessTools.Method(poseType, "AdvancedModeWindow", null, null),
                    "WindowPrefix",
                    null,
                    null,
                    "WindowFinalizer");
                Patch(AccessTools.Method(mainType, "OnGUI", null, null), null, "AfterNativeGUI", null, null);
                Patch(
                    AccessTools.Method(blendsType, "GUILogic", null, null),
                    "BlendPrefix",
                    null,
                    "BlendTranspiler",
                    "BlendFinalizer");
                Patch(
                    AccessTools.Method(bonesType, "GUILogic", null, null),
                    "BonesPrefix",
                    null,
                    "BonesTranspiler",
                    "BonesFinalizer");
                Type module = ReflectionAccess.Type("HSPE.AMModules.AdvancedModeModule");
                Patch(
                    AccessTools.Method(
                        module,
                        "Vector3Editor",
                        new Type[] { typeof(Vector3), typeof(Color), typeof(Color), typeof(Color), typeof(float), typeof(string), typeof(string), typeof(string), typeof(Action), typeof(bool) },
                        null),
                    "VectorPrefix",
                    null,
                    null,
                    null);
                Patch(
                    AccessTools.Method(
                        module,
                        "QuaternionEditor",
                        new Type[] { typeof(Quaternion), typeof(Color), typeof(Color), typeof(Color), typeof(float), typeof(string), typeof(string), typeof(string), typeof(Action) },
                        null),
                    "QuaternionPrefix",
                    null,
                    null,
                    null);
                Patch(
                    AccessTools.Method(typeof(Studio.WorkspaceCtrl), "OnClickDelete", null, null),
                    "BeforeSceneDelete",
                    null,
                    null,
                    null);
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        private void Patch(MethodInfo original, string prefix, string postfix, string transpiler, string finalizer)
        {
            if (original == null)
            {
                throw new MissingMethodException("KKPE API signature differs");
            }

            harmony.Patch(original, HM(prefix), HM(postfix), HM(transpiler), HM(finalizer), null);
            patched.Add(original);
        }

        private static HarmonyMethod HM(string name)
        {
            return name == null ? null : new HarmonyMethod(typeof(AdvancedUi).GetMethod(name, ReflectionAccess.Flags));
        }

        private static MethodInfo Method(string name)
        {
            return typeof(AdvancedUi).GetMethod(name, ReflectionAccess.Flags);
        }

        internal void Tick()
        {
            if (dirty && Time.realtimeSinceStartup > saveAt)
            {
                SaveDisplay(Path.Combine(displayDirectory, "active.xml"), "Current");
                dirty = false;
            }

            if (!NativeVisible)
            {
                textInput = false;
                templatesOpen = false;
                ReleaseScrub();
            }
            else if (Input.GetMouseButtonDown(0) && !NativeRect.Contains(Mouse()) && !PointerOverExtension && (host.Library == null || !host.Library.IsPointerOverUI))
            {
                clearTextFocus = true;
            }
        }

        private static bool BeforeSceneDelete()
        {
            return current == null || (!Plugin.IsTextInputActive && !Plugin.IsPointerOverExtensionUI);
        }

        private void Changed()
        {
            dirty = true;
            saveAt = Time.realtimeSinceStartup + .6f;
        }

        private static Vector2 Mouse()
        {
            return new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            if (dirty)
            {
                try
                {
                    SaveDisplay(Path.Combine(displayDirectory, "active.xml"), "Current");
                }
                catch (Exception e)
                {
                    host.Report("Display state save: " + e.Message);
                }
            }

            restoringNative = true;
            if (harmony != null)
            {
                harmony.UnpatchSelf();
            }

            if (localSkin != null)
            {
                UnityEngine.Object.Destroy(localSkin);
            }

            if (current == this)
            {
                current = null;
            }

            bonesContext = null;
            blendContext = null;
            textInput = false;
            ReleaseScrub();
        }
    }
}
