using System;
using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using GinShared;
using UnityEngine;

[assembly: AssemblyVersion(GinPadHack.Plugin.Version + ".0")]
[assembly: AssemblyFileVersion(GinPadHack.Plugin.Version + ".0")]
namespace GinPadHack
{
    // GIN Pad life hack ("IK QOL" on the toolbar): one small window, opened from the Studio toolbar.
    //
    // With a character selected:
    //   Map     one graph that blends eye and mouth poses, the brows and the pupils  (PadWindow.Map.cs)
    //   Face    the Blend Shapes this scene uses as drag bars                       (PadWindow.Face.cs)
    //   Body    IK targets and the bones KKPE's small window lacks                  (PadWindow.Body.cs)
    //           with Delay Key: keys of each part a little after the playhead       (PadWindow.Delay.cs)
    //   Shapes  the Blend Shapes of the body mesh
    // With items (or several objects) selected:
    //   Move    Move Controller's moves and turns, FK bone stepping with the wheel  (PadWindow.Items.cs)
    //   Shapes  the item's Blend Shapes
    //
    // Nothing here is stored by this plugin in a scene: every value is written through KKPE or
    // Studio, so a scene plays back the same without this plugin installed.

    // Which Blend Shapes the Face tab (and the Map's pose editor) lists.
    public enum ShapeList
    {
        Favorites,   // only the pinned ones
        Used,        // pinned, keyed by the Timeline or not zero right now
        All
    }

    [BepInPlugin(Guid, "GIN Pad life hack", Version)]
    [BepInProcess("CharaStudio.exe")]
    [BepInDependency("marco.kkapi", BepInDependency.DependencyFlags.SoftDependency)]
    public sealed partial class Plugin : BaseUnityPlugin
    {
        public const string Guid = "local.kkworkshop.0a52b20541f5.gin-pad-life-hack";
        public const string Version = "1.1.0";
        const string ToolbarIconResource = "ToolbarIcon.png";

        internal const float DefaultSensitivity = 0.6f;
        const string DefaultHiddenShapes = "ha00_def_cl,f00_def_cl,f00_def_op,mayu00_def_cl,mayu00_def_op";
        internal const float DefaultUiScale = 1.045f;
        internal const float MinUiScale = 0.6f, MaxUiScale = 2f;   // also the resize knob's range
        const int SettingsRevision = 3;   // raise when a default changes and old saved defaults must follow

        internal static Plugin Instance;
        internal ConfigEntry<float> Opacity, UiScale, BarSensitivity, WindowX, WindowY;
        internal ConfigEntry<KeyboardShortcut> ToggleKey;
        internal ConfigEntry<string> OpenTabs, OpenItemTabs;
        internal ConfigEntry<string> HiddenShapes;
        internal ConfigEntry<ShapeList> FaceList;
        internal ConfigEntry<bool> LimitPupils;
        internal ConfigEntry<float> PupilLeftMinX, PupilLeftMaxX, PupilNoseDepth, PupilOuterDepth;
        internal ConfigEntry<string> KeyDelays, DelayOrigin;
        internal ConfigEntry<float> DelayRange;

        PadWindow window;
        PointerCatcher catcher;
        object toolbarToggle;

        // Other plugins ask this before reacting to the mouse.
        public static bool IsPointerOverUI
        {
            get
            {
                return Instance != null && Instance.window != null && Instance.window.OwnsPointer;
            }
        }

        void Awake()
        {
            Instance = this;
            Opacity = Config.Bind(
                "Window",
                "BackgroundOpacity",
                0.5f,
                new ConfigDescription("Opacity of the window background. Buttons and bars stay solid.", new AcceptableValueRange<float>(0.1f, 1f)));
            UiScale = Config.Bind(
                "Window",
                "UiScale",
                DefaultUiScale,
                new ConfigDescription("Size of the whole window, text included. Also set by dragging the round knob at the window's top right corner.", new AcceptableValueRange<float>(MinUiScale, MaxUiScale)));
            BarSensitivity = Config.Bind(
                "Window",
                "BarSensitivity",
                DefaultSensitivity,
                new ConfigDescription("How fast bars and body drags follow the mouse. 1: one bar width is the whole range. Also on the window as \"Sens\".", new AcceptableValueRange<float>(0.05f, 3f)));
            ToggleKey = Config.Bind("Window", "ToggleKey", KeyboardShortcut.Empty, "Optional key that opens and closes the window, besides the toolbar button.");
            WindowX = Config.Bind("Window", "X", 60f, "Remembered window position.");
            OpenTabs = Config.Bind("Window", "OpenTabs", "Face", "Remembered open tabs for a character (Map, Face, Body, Shapes), shown side by side.");
            OpenItemTabs = Config.Bind("Window", "OpenItemTabs", "Move", "Remembered open tabs for items (Move, Shapes), shown side by side.");
            WindowY = Config.Bind("Window", "Y", 120f, "Remembered window position.");

            HiddenShapes = Config.Bind(
                "Face",
                "HiddenShapes",
                DefaultHiddenShapes,
                "Blend Shapes the Face tab lists only with \"All\" (names without the mesh prefix, comma separated).");
            FaceList = Config.Bind(
                "Face",
                "ListedShapes",
                ShapeList.Used,
                "Which Blend Shapes are listed. Favorites: only the pinned ones. Used: pinned, keyed by the Timeline or not zero. Also on the window as Fav / Used / All.");
            LimitPupils = Config.Bind("Face", "LimitPupils", true, "Keep the pupils inside the eyes: clamp their X and set their depth (Z) from X.");
            PupilLeftMinX = Config.Bind("Face", "PupilLeftMinX", -0.017f, "Lowest local X of cf_J_hitomi_tx_L. The right pupil uses the mirrored range.");
            PupilLeftMaxX = Config.Bind("Face", "PupilLeftMaxX", 0.013f, "Highest local X of cf_J_hitomi_tx_L. The right pupil uses the mirrored range.");
            PupilNoseDepth = Config.Bind("Face", "PupilNoseSideDepth", -0.0015f, "Local Z of a pupil at its limit towards the nose.");
            PupilOuterDepth = Config.Bind("Face", "PupilOuterSideDepth", -0.0030f, "Local Z of a pupil at its outer limit. Z changes evenly in between.");
            KeyDelays = Config.Bind("Body", "KeyDelays", "", "Delay Key: seconds after the playhead per body part (part=seconds, comma separated). Set on the Body tab's delay bar.");
            DelayOrigin = Config.Bind("Body", "DelayOrigin", "cf_t_hips", "Delay Key: the part delays run out from. Dragging a part's delay takes along the parts further from it.");
            DelayRange = Config.Bind("Body", "DelayBarSeconds", 0.5f, new ConfigDescription("Delay Key: how many seconds the delay bar shows (mouse wheel on the bar).", new AcceptableValueRange<float>(0.1f, 5f)));
            FollowChangedDefaults();
        }

        // A saved setting keeps its old value when a default changes. Values that still equal the
        // previous default follow the new default once; anything the user chose is left alone.
        void FollowChangedDefaults()
        {
            ConfigEntry<int> revision = Config.Bind("Window", "SettingsRevision", 0, "Internal: which defaults this file has already followed.");
            if (revision.Value >= SettingsRevision)
            {
                return;
            }

            // Revision 2: slower bars, one more "def" shape hidden.
            if (revision.Value < 2 && Mathf.Approximately(BarSensitivity.Value, 1f))
            {
                BarSensitivity.Value = DefaultSensitivity;
            }

            if (revision.Value < 2 && HiddenShapes.Value == "ha00_def_cl,f00_def_cl,f00_def_op,mayu00_def_cl")
            {
                HiddenShapes.Value = DefaultHiddenShapes;
            }

            // Revision 3: the window became 5 % smaller.
            if (revision.Value < 3 && Mathf.Approximately(UiScale.Value, 1.1f))
            {
                UiScale.Value = DefaultUiScale;
            }

            revision.Value = SettingsRevision;
        }

        void Start()
        {
            window = new PadWindow(this, Path.Combine(Paths.GameRootPath, "UserData/GIN/Pad"));
            catcher = new PointerCatcher();
            InstallInputBlock();
            AddToolbarButton();
            Logger.LogInfo("GIN Pad life hack " + Version + " ready" + (toolbarToggle == null ? " (no toolbar button: set Window / ToggleKey)" : "") + ".");
        }

        // KKAPI's left toolbar. Found by reflection so that a KKAPI update cannot stop this plugin from loading.
        void AddToolbarButton()
        {
            try
            {
                Type buttons = Reflect.Type("KKAPI.Studio.UI.CustomToolbarButtons");
                MethodInfo add = buttons == null ? null : buttons.GetMethod("AddLeftToolbarToggle", new Type[] { typeof(Texture2D), typeof(bool), typeof(Action<bool>) });
                if (add == null)
                {
                    Logger.LogWarning("KKAPI toolbar not found; use the ToggleKey setting to open the window.");
                    return;
                }

                toolbarToggle = add.Invoke(null, new object[] { LoadToolbarIcon(), false, new Action<bool>(ShowWindow) });
            }
            catch (Exception error)
            {
                Logger.LogWarning("Toolbar button not added: " + error.Message);
            }
        }

        static Texture2D LoadToolbarIcon()
        {
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ToolbarIconResource))
            {
                byte[] bytes = new byte[stream.Length];
                stream.Read(bytes, 0, bytes.Length);
                Texture2D icon = new Texture2D(32, 32, TextureFormat.ARGB32, false);
                icon.LoadImage(bytes);
                return icon;
            }
        }

        // Called by the toolbar button, the key and the window's own close button.
        internal void ShowWindow(bool show)
        {
            if (window == null || window.Visible == show)
            {
                return;
            }

            window.Visible = show;
            if (toolbarToggle != null)
            {
                // Keep the toolbar button's lit state in step when the window closed itself.
                PropertyInfo value = toolbarToggle.GetType().GetProperty("Value");
                if (value != null && value.CanWrite && !show.Equals(value.GetValue(toolbarToggle, null)))
                {
                    value.SetValue(toolbarToggle, show, null);
                }
            }
        }

        void Update()
        {
            EndlessDrag.Update();
            if (window == null)
            {
                return;
            }

            if (ToggleKey.Value.IsDown())
            {
                ShowWindow(!window.Visible);
            }

            window.Tick();
        }

        void OnGUI()
        {
            if (window != null && window.Visible)
            {
                window.Draw();
            }
        }

        void OnDestroy()
        {
            RemoveInputBlock();
            if (catcher != null)
            {
                catcher.Dispose();
            }

            if (Instance == this)
            {
                Instance = null;
            }
        }
    }
}
