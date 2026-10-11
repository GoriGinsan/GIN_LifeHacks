using System;
using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

[assembly: AssemblyVersion("1.0.2.0")]
[assembly: AssemblyFileVersion("1.0.2.0")]

namespace GinAdvanceHack
{
    [BepInPlugin(Guid, "GIN Advance mode life hack", "1.0.2")]
    [BepInProcess("CharaStudio.exe")]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "local.kkworkshop.0a52b20541f5.gin-advance-life-hack";
        internal static Plugin Instance;
        internal ConfigEntry<bool> Enabled, LargeControls, CompactSpacing, VisibilityTools, ScrubBones, TwinShortcut, Expressions;
        internal ConfigEntry<int> ButtonHeight, ThumbSize;
        internal ConfigEntry<float> ScrubPixels;
        internal ConfigEntry<string> Language;
        internal ExpressionLibrary Library;
        private AdvancedUi ui;
        private UiInputShield inputShield;
        private float retry;
        private bool failed;
        private Studio.CameraControl camera;
        private Studio.CameraControl.NoCtrlFunc oldMouse, oldKeys, oldZoom, mouseGuard, keysGuard, zoomGuard;
        private CameraGuardLife guardLife;
        private sealed class CameraGuardLife { public bool Active = true; }
        public static bool IsTextInputActive { get { return Instance != null && Instance.Enabled.Value && (AdvancedUi.TextInputActive || (Instance.Library != null && Instance.Library.TextInputActive)); } }
        public static bool IsPointerOverExtensionUI { get { return Instance != null && Instance.Enabled.Value && (AdvancedUi.PointerOverExtension || (Instance.Library != null && Instance.Library.IsPointerOverUI)); } }
        public static bool IsAdvancedVisible { get { return AdvancedUi.NativeVisible; } }
        public static bool IsPointerOverAdvancedUI { get { return AdvancedUi.NativeVisible && AdvancedUi.NativeRect.Contains(new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y)); } }
        public bool Ready { get { return ui != null; } }
        internal void Report(string s) { Logger.LogInfo(s); }
        private void Awake()
        {
            Instance = this;
            Enabled = Config.Bind("General", "Enabled", true, "Enable the additions. False restores native KKPE UI; expression files remain.");
            Language = Config.Bind("General", "Language", "en", "Legacy setting retained for compatibility. GIN additions use English; native KKPE labels and saved names are unchanged.");
            LargeControls = Config.Bind("UI", "LargerControls", true, "Only KKPE Advanced mode receives larger controls.");
            CompactSpacing = Config.Bind("UI", "CompactSpacing", true, "Reduce margins only inside Advanced mode.");
            ButtonHeight = Config.Bind("UI", "ButtonHeight", 26, new ConfigDescription("Button and numeric field height", new AcceptableValueRange<int>(20, 40)));
            ThumbSize = Config.Bind("UI", "SliderThumbSize", 22, new ConfigDescription("Slider thumb / scrollbar hit area", new AcceptableValueRange<int>(14, 36)));
            VisibilityTools = Config.Bind("BlendShapes", "VisibilityTools", true, "Hide rows without changing their values; named display templates.");
            ScrubBones = Config.Bind("Bones", "DragIncrementButtons", true, "Click +/- for one step; hold and drag horizontally to scrub. Shift fine, Ctrl coarse; Escape cancels current scrub.");
            ScrubPixels = Config.Bind("Bones", "PixelsPerStep", 8f, new ConfigDescription("Horizontal distance for one native increment", new AcceptableValueRange<float>(2f, 40f)));
            TwinShortcut = Config.Bind("Bones", "TwinButtonNearCopy", true, "Add L/R immediately before the native Copy Position / Rotation / Scale button.");
            Expressions = Config.Bind("Expressions", "Enabled", true, "Expression library beside the native Advanced mode window.");
            Enabled.SettingChanged += delegate { failed = false; if (!Enabled.Value) StopExtensions(); };
        }
        private void Update()
        {
            if (!Enabled.Value) return;
            if (ui == null && !failed && Time.realtimeSinceStartup >= retry)
            {
                retry = Time.realtimeSinceStartup + 2;
                Type kkpe = R.Type("HSPE.MainWindow");
                if (kkpe == null || R.Get(kkpe, "_self") == null) return;
                try
                {
                    ui = new AdvancedUi(this);
                    Library = new ExpressionLibrary(Path.Combine(Paths.GameRootPath, "UserData/GIN/Expressions"), Report);
                    inputShield = new UiInputShield();
                    Logger.LogInfo("GIN Advanced mode life hack 1.0.2 attached to KKPE " + kkpe.Assembly.GetName().Version + ".");
                }
                catch (Exception ex) { failed = true; StopExtensions(); Logger.LogError("Native KKPE retained: " + ex); }
            }
            if (ui == null) return;
            InstallCameraGuard();
            ui.Tick();
            if (Library != null && Expressions.Value) Library.Tick();
        }
        private void LateUpdate()
        {
            if(inputShield!=null)inputShield.Update(AdvancedUi.ExtensionInputRects,Library!=null&&Expressions.Value?Library.InputRects:new Rect[0]);
        }
        private void InstallCameraGuard()
        {
            if (camera || Studio.Studio.Instance == null || !Studio.Studio.Instance.cameraCtrl) return;
            camera = Studio.Studio.Instance.cameraCtrl;
            oldMouse = camera.noCtrlCondition; oldKeys = camera.keyCondition; oldZoom = camera.zoomCondition;
            Studio.CameraControl.NoCtrlFunc m = oldMouse, k = oldKeys, z = oldZoom;
            CameraGuardLife life = new CameraGuardLife(); guardLife = life;
            FieldInfo dragging = AccessTools.Field(typeof(Studio.CameraControl), "dragging");
            mouseGuard = delegate
            {
                bool blocked = life.Active && (IsPointerOverExtensionUI || IsTextInputActive);
                if (blocked && !Input.GetMouseButton(0) && !Input.GetMouseButton(1) && !Input.GetMouseButton(2) && dragging != null && camera) dragging.SetValue(camera, false);
                return (m != null && m()) || blocked;
            };
            keysGuard = delegate { return (k == null || k()) && !(life.Active && IsTextInputActive); };
            zoomGuard = delegate { return (z == null || z()) && !(life.Active && (IsPointerOverExtensionUI || IsTextInputActive)); };
            camera.noCtrlCondition = mouseGuard; camera.keyCondition = keysGuard; camera.zoomCondition = zoomGuard;
        }
        private void StopExtensions()
        {
            if (ui != null) { ui.Dispose(); ui = null; }
            if (Library != null) { Library.Dispose(); Library = null; }
            if (inputShield != null) { inputShield.Dispose(); inputShield = null; }
            if (guardLife != null) guardLife.Active = false;
            if (camera)
            {
                if (camera.noCtrlCondition == mouseGuard) camera.noCtrlCondition = oldMouse;
                if (camera.keyCondition == keysGuard) camera.keyCondition = oldKeys;
                if (camera.zoomCondition == zoomGuard) camera.zoomCondition = oldZoom;
            }
            camera = null;
        }
        private void OnDestroy() { StopExtensions(); if (Instance == this) Instance = null; }
        public string RunIsolatedTests()
        {
            if (!IsIsolatedTest()) return "SKIP Advanced tests: isolated test-game only";
            if(ui == null)return "FAIL Advanced UI adapter not attached";
            return ui.RunIsolatedTests()+"\n"+(Library==null?"FAIL Expression library not attached":Library.RunIsolatedTests());
        }
        public string PreparePreview(int view)
        {
            if (!IsIsolatedTest() || ui == null) return "SKIP Advanced preview: isolated adapter required";
            object main = R.Get(R.Type("HSPE.MainWindow"), "_self");
            object pose = R.Get(main,"_poseTarget");
            if (pose == null) return "FAIL Advanced preview: select the fixture character first";
            Behaviour component = pose as Behaviour; if(component != null) component.enabled = true;
            R.Set(R.Type("HSPE.PoseController"),"_drawAdvancedMode",true);
            R.Set(main,"_advancedModeRect", new Rect(160,110,1000,700));
            object module = R.Get(pose, view == 1 ? "_bonesEditor" : "_blendShapesEditor");
            if(module == null) return "FAIL Advanced preview: target module missing";
            R.Call(pose,"EnableModule",module);
            if(view==1)
            {
                Component target=pose as Component;
                foreach(Transform child in target.GetComponentsInChildren<Transform>(true))
                    if(child.name=="cf_J_hitomi_tx_L") { R.Call(module,"ChangeBoneTarget",child);break; }
            }
            else
            {
                object face=R.Get(module,"_faceRenderer");if(face!=null)R.Set(module,"_skinnedMeshTarget",face);
            }
            if(Library!=null)
            {
                // Isolated preview only. Production opening is the adjacent native-style button.
                FieldInfo visible=typeof(ExpressionLibrary).GetField("visible",BindingFlags.NonPublic|BindingFlags.Instance);
                if(visible!=null)visible.SetValue(Library,view==2);
                if(view==2) { string preview=Library.PrepareIsolatedPreview();Report(preview);Library.Show(); }
            }
            return "PASS Advanced preview prepared: "+view;
        }
        public string SetEnabledForIsolatedTest(bool value)
        {
            if(!IsIsolatedTest())return "SKIP Advanced enable test: marked isolated game only";
            Enabled.Value=value;
            if(!value)
            {
                foreach(MethodBase method in Harmony.GetAllPatchedMethods())
                {
                    Patches patches=Harmony.GetPatchInfo(method);
                    if(patches!=null&&patches.Owners.Contains(Guid+".ui"))return "FAIL Advanced OFF: owned patch retained on "+method.Name;
                }
                return ui==null&&Library==null&&inputShield==null?"PASS Advanced OFF: all owned patches, cloned style and input shields removed; native data/UI retained":"FAIL Advanced OFF: extension instance retained";
            }
            retry=0;failed=false;return "WAIT Advanced ON: await Ready before verifying";
        }
        internal static bool IsIsolatedTest()
        {
            return Array.IndexOf(Environment.GetCommandLineArgs(), "-ginLifeHacksVerify") >= 0
                && new DirectoryInfo(Paths.GameRootPath).Name.Equals("test-game", StringComparison.OrdinalIgnoreCase)
                && File.Exists(Path.Combine(Paths.GameRootPath, ".kk-workshop-test"));
        }
    }

    internal static class R
    {
        internal const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        internal static Type Type(string name) { foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies()) { Type t = a.GetType(name, false); if (t != null) return t; } return null; }
        internal static object Get(object obj, string name)
        {
            if (obj == null) return null; Type t = obj as Type ?? obj.GetType(); FieldInfo f = AccessTools.Field(t, name);
            if (f != null) return f.GetValue(obj is Type ? null : obj);
            PropertyInfo p = AccessTools.Property(t, name); return p == null ? null : p.GetValue(obj is Type ? null : obj, null);
        }
        internal static void Set(object obj, string name, object value) { Type t = obj as Type ?? obj.GetType(); FieldInfo f = AccessTools.Field(t, name); if (f == null) throw new MissingFieldException(t.FullName, name); f.SetValue(obj is Type ? null : obj, value); }
        internal static object Call(object obj, string name, params object[] args)
        {
            Type t = obj as Type ?? obj.GetType(); MethodInfo found = null;
            foreach (MethodInfo m in t.GetMethods(Flags)) if (m.Name == name && m.GetParameters().Length == args.Length)
            {
                bool valid = true; ParameterInfo[] pp = m.GetParameters(); for (int i = 0; i < pp.Length; i++) if (args[i] != null && !pp[i].ParameterType.IsInstanceOfType(args[i])) valid = false;
                if (valid) { found = m; break; }
            }
            if (found == null) throw new MissingMethodException(t.FullName, name); return found.Invoke(obj is Type ? null : obj, args);
        }
    }
}
