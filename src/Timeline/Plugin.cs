using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using Key = System.Collections.Generic.KeyValuePair<float, Timeline.Keyframe>;
using Native = Timeline.Timeline;

[assembly: AssemblyVersion(GinTimelineHack.Plugin.Version + ".0")]
[assembly: AssemblyFileVersion(GinTimelineHack.Plugin.Version + ".0")]
namespace GinTimelineHack
{
    [BepInPlugin(Guid, "GIN Timeline life hack", Version)]
    [BepInProcess("CharaStudio.exe")]
    [BepInDependency("com.joan6694.illusionplugins.timeline", BepInDependency.DependencyFlags.HardDependency)]
    public sealed partial class Plugin : BaseUnityPlugin
    {
        public const string Guid = "local.kkworkshop.0a52b20541f5.gin-timeline-life-hack";
        public const string Version = "1.2.0";
        internal static Plugin Self;
        internal const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        internal const BindingFlags StaticFlags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        ConfigEntry<bool> enabledSetting, rightCandidates, summaries, arrowDefaultsApplied;
        ConfigEntry<KeyboardShortcut> addShortcut, commitShortcut, upShortcut, downShortcut, autoKeyShortcut;
        ConfigEntry<string> autoKeyRowsSetting, autoKeyCurveSetting;
        ConfigEntry<int> candidateWidth;
        Harmony harmony;
        internal Native native;
        internal Canvas canvas;
        internal RectTransform window, grid, content;
        RectTransform toolbar, sidebar, candidateContent, resizeHandle;
        Text statusText, candidateTitle;
        ScrollRect candidateScroll;
        Font font;
        bool attached, rebuilding, inView, trackFocus, advancedFocus;
        float retryAt, nextRefresh, statusUntil;
        string message = "";
        string lastAttachError;
        readonly List<GameObject> owned = new List<GameObject>();
        readonly Dictionary<GameObject, RectSnapshot> relocated = new Dictionary<GameObject, RectSnapshot>();
        readonly Dictionary<object, GroupUi> groupRows = new Dictionary<object, GroupUi>();
        readonly Dictionary<GameObject, PointerAction> modelActions = new Dictionary<GameObject, PointerAction>();
        readonly Dictionary<GameObject, PointerAction> keyActions = new Dictionary<GameObject, PointerAction>();
        readonly Dictionary<GameObject, PointerAction> headerActions = new Dictionary<GameObject, PointerAction>();
        readonly HashSet<object> selectedGroups = new HashSet<object>();
        readonly List<RectTransform> toolbarNeighbors = new List<RectTransform>();
        Key? anchor;
        List<Key> range, lastPhase;
        Studio.CameraControl guardedCamera;
        Studio.CameraControl.NoCtrlFunc previousKeys, keyGuard;
        Studio.CameraControl.NoCtrlFunc previousMouse, mouseGuard, previousZoom, zoomGuard;
        FieldInfo cameraDragging;
        int guardEpoch;

        public bool IsAttached
        {
            get
            {
                return attached;
            }
        }

        public string Status
        {
            get
            {
                return message;
            }
        }

        void Awake()
        {
            Self = this;
            enabledSetting = Config.Bind("General", "Enabled", true, "Restore original Timeline when false. A restart is not required.");
            addShortcut = Config.Bind(
                "Input",
                "AddKeyframe",
                new KeyboardShortcut(KeyCode.Mouse1, KeyCode.LeftControl),
                "Add at the pointer time on a track/group, or at the playhead on an Interpolables candidate. Configure this shortcut in F1 Plugin/Mod Settings. Native middle click remains available; existing native key context gestures are unchanged.");
            commitShortcut = Config.Bind(
                "Input",
                "UseCurrentValue",
                new KeyboardShortcut(KeyCode.S),
                "Apply current values to every selected Timeline key, including during viewport, Move Controller or KKPE edits. Default S also works while Shift/Alt is held; Ctrl+S remains scene save. Custom bindings keep their configured modifiers. Typing and the F1 settings window are protected.");
            upShortcut = Config.Bind(
                "Input",
                "MoveTracksUp",
                new KeyboardShortcut(KeyCode.UpArrow),
                "Move selected tracks or groups up one position within their parent group. Works after clicking inside Timeline.");
            downShortcut = Config.Bind(
                "Input",
                "MoveTracksDown",
                new KeyboardShortcut(KeyCode.DownArrow),
                "Move selected tracks or groups down one position within their parent group. Works after clicking inside Timeline.");
            arrowDefaultsApplied = Config.Bind(
                "Input",
                "PlainArrowDefaultsApplied",
                false,
                "Internal. Version 1.1.0 changes the default reorder keys from Shift + Up / Down to plain Up / Down once; later custom bindings are kept.");
            autoKeyShortcut = Config.Bind(
                "Input",
                "ToggleAutoKey",
                new KeyboardShortcut(KeyCode.None),
                "Optional key that switches Auto Key on and off, in addition to the toolbar button.");
            autoKeyRowsSetting = Config.Bind(
                "AutoKey",
                "Rows",
                AutoKeyDefaultRows,
                "Comma separated Timeline row types that Auto Key writes. Default: guide objects (FK / IK / object transforms), KKPE bones and KKPE Blend Shapes.");
            autoKeyCurveSetting = Config.Bind(
                "AutoKey",
                "Curve",
                PreviousCurve,
                new ConfigDescription(
                    "Curve of the keys Auto Key adds: one of Timeline's keyframe presets, or Previous (copy the previous key's curve, as Timeline does). Also in the menu next to the Auto Key button.",
                    new AcceptableValueList<string>(CurveNames)));
            BindAutoKeyRowsSetting();
            rightCandidates = Config.Bind(
                "Layout",
                "CandidatesOnRight",
                true,
                "Place available Interpolables in a separate right hand scroll panel. The AddKeyframe shortcut or native middle click adds at the playhead.");
            candidateWidth = Config.Bind(
                "Layout",
                "CandidateWidth",
                250,
                new ConfigDescription("Right panel width in native UI units.", new AcceptableValueRange<int>(180, 420)));
            summaries = Config.Bind(
                "Layout",
                "GroupSummaryKeys",
                true,
                "Group diamonds summarize descendant keys: red when unselected, green when any represented key is selected. Supports box selection, Alt drag and bulk insertion while folded.");
            enabledSetting.SettingChanged += delegate
            {
                if (!enabledSetting.Value)
                {
                    Detach();
                }
                else
                {
                    retryAt = 0;
                }
            };
            rightCandidates.SettingChanged += delegate
            {
                RebuildCandidates();
            };
            candidateWidth.SettingChanged += delegate
            {
                PositionPanels();
            };
            summaries.SettingChanged += delegate
            {
                RefreshGroups();
            };
            autoKeyRowsSetting.SettingChanged += delegate
            {
                ReadAutoKeyRows();
            };
            ApplyPlainArrowDefaults();
            Logger.LogInfo("GIN Timeline life hack " + Version + " ready; original Studio layout retained.");
        }

        // Up to 1.0.6 the reorder keys defaulted to Shift + Up / Down. Replace only those
        // untouched old defaults, once.
        void ApplyPlainArrowDefaults()
        {
            if (arrowDefaultsApplied.Value)
            {
                return;
            }

            if (IsShiftPlus(upShortcut.Value, KeyCode.UpArrow))
            {
                upShortcut.Value = new KeyboardShortcut(KeyCode.UpArrow);
            }

            if (IsShiftPlus(downShortcut.Value, KeyCode.DownArrow))
            {
                downShortcut.Value = new KeyboardShortcut(KeyCode.DownArrow);
            }

            arrowDefaultsApplied.Value = true;
        }

        void Update()
        {
            if (!enabledSetting.Value)
            {
                return;
            }

            if (!attached)
            {
                if (Time.unscaledTime < retryAt)
                {
                    return;
                }

                retryAt = Time.unscaledTime + 2;
                try
                {
                    Attach();
                    if (attached)
                    {
                        lastAttachError = null;
                    }
                }
                catch (Exception e)
                {
                    Detach();
                    string error = Error(e);
                    if (!String.Equals(lastAttachError, error, StringComparison.Ordinal))
                    {
                        Logger.LogError("Timeline attach: " + error);
                        lastAttachError = error;
                    }
                }

                return;
            }

            if (native == null || canvas == null || window == null)
            {
                Detach();
                return;
            }

            PollGroupInteraction();
            if (Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1) || Input.GetMouseButtonDown(2))
            {
                UpdateFocus();
            }

            if (CanUseShortcut(false))
            {
                if (ShortcutDown(upShortcut.Value))
                {
                    MoveSelected(-1);
                }

                if (ShortcutDown(downShortcut.Value))
                {
                    MoveSelected(1);
                }
            }

            if (CommitShortcutPressed(true) && CanUseShortcut(true))
            {
                CommitCurrentValues();
            }

            if (autoKeyShortcut.Value.IsDown() && !TextFocused())
            {
                SetAutoKey(!autoKey);
            }

            PollAddShortcut();
            PollCurveMenu();
            PollAutoKey();
            PollFaceKey();
        }

        void LateUpdate()
        {
            if (!attached || !Native.InterfaceVisible)
            {
                return;
            }

            PositionPanels();
            if (Time.unscaledTime >= nextRefresh)
            {
                nextRefresh = Time.unscaledTime + .15f;
                RefreshGroups();
                HookKeys();
            }

            if (statusText != null && Time.unscaledTime > statusUntil && statusText.text.Length > 0)
            {
                statusText.text = "";
            }
        }

        internal void Note(string text)
        {
            message = text;
            statusUntil = Time.unscaledTime + 5;
            if (statusText != null)
            {
                statusText.text = text;
            }
        }

        void OnDestroy()
        {
            Detach();
            if (Self == this)
            {
                Self = null;
            }
        }
    }
}
