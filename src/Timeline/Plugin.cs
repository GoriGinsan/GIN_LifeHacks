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

[assembly: AssemblyVersion("1.0.3.0")]
[assembly: AssemblyFileVersion("1.0.3.0")]
namespace GinTimelineHack
{
    [BepInPlugin(Guid, "GIN Timeline life hack", "1.0.3")]
    [BepInProcess("CharaStudio.exe")]
    [BepInDependency("com.joan6694.illusionplugins.timeline", BepInDependency.DependencyFlags.HardDependency)]
    public sealed partial class Plugin : BaseUnityPlugin
    {
        public const string Guid = "local.kkworkshop.0a52b20541f5.gin-timeline-life-hack";
        internal static Plugin Self;
        internal const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        internal const BindingFlags StaticFlags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        ConfigEntry<bool> enabledSetting, rightCandidates, summaries;
        ConfigEntry<KeyboardShortcut> addShortcut, commitShortcut, upShortcut, downShortcut;
        ConfigEntry<int> candidateWidth;
        Harmony harmony;
        internal Native native;
        internal Canvas canvas;
        internal RectTransform window, grid, content;
        RectTransform toolbar, sidebar, candidateContent;
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
        readonly HashSet<object> selectedGroups = new HashSet<object>();
        readonly List<RectTransform> toolbarNeighbors = new List<RectTransform>();
        Key? anchor;
        List<Key> range, lastPhase;
        Studio.CameraControl guardedCamera;
        Studio.CameraControl.NoCtrlFunc previousKeys, keyGuard;
        Studio.CameraControl.NoCtrlFunc previousMouse, mouseGuard, previousZoom, zoomGuard;
        FieldInfo cameraDragging;
        int guardEpoch;
        public bool IsAttached { get { return attached; } }
        public string Status { get { return message; } }

        void Awake()
        {
            Self = this;
            enabledSetting = Config.Bind("General", "Enabled", true, "Restore original Timeline when false. A restart is not required.");
            addShortcut = Config.Bind("Input", "AddKeyframe", new KeyboardShortcut(KeyCode.Mouse1, KeyCode.LeftControl), "Add at the pointer time on a track/group, or at the playhead on an Interpolables candidate. Configure this shortcut in F1 Plugin/Mod Settings. Native middle click remains available; existing native key context gestures are unchanged.");
            commitShortcut = Config.Bind("Input", "UseCurrentValue", new KeyboardShortcut(KeyCode.S), "Apply current values to every selected Timeline key, including during viewport, Move Controller or KKPE edits. Default S also works while Shift/Alt is held; Ctrl+S remains scene save. Custom bindings keep their configured modifiers. Typing and the F1 settings window are protected.");
            upShortcut = Config.Bind("Input", "MoveTracksUp", new KeyboardShortcut(KeyCode.UpArrow, KeyCode.LeftShift), "Move selected tracks or groups up one position within their parent group.");
            downShortcut = Config.Bind("Input", "MoveTracksDown", new KeyboardShortcut(KeyCode.DownArrow, KeyCode.LeftShift), "Move selected tracks or groups down one position within their parent group.");
            rightCandidates = Config.Bind("Layout", "CandidatesOnRight", true, "Place available Interpolables in a separate right hand scroll panel. The AddKeyframe shortcut or native middle click adds at the playhead.");
            candidateWidth = Config.Bind("Layout", "CandidateWidth", 250, new ConfigDescription("Right panel width in native UI units.", new AcceptableValueRange<int>(180, 420)));
            summaries = Config.Bind("Layout", "GroupSummaryKeys", true, "Group diamonds summarize descendant keys: red when unselected, green when any represented key is selected. Supports box selection, Alt drag and bulk insertion while folded.");
            enabledSetting.SettingChanged += delegate { if (!enabledSetting.Value) Detach(); else retryAt = 0; };
            rightCandidates.SettingChanged += delegate { RebuildCandidates(); };
            candidateWidth.SettingChanged += delegate { PositionPanels(); };
            summaries.SettingChanged += delegate { RefreshGroups(); };
            Logger.LogInfo("GIN Timeline life hack 1.0.3 ready; original Studio layout retained.");
        }

        void Update()
        {
            if (!enabledSetting.Value) return;
            if (!attached)
            {
                if (Time.unscaledTime < retryAt) return;
                retryAt = Time.unscaledTime + 2;
                try
                {
                    Attach();
                    if (attached) lastAttachError = null;
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
            if (native == null || canvas == null || window == null) { Detach(); return; }
            PollGroupInteraction();
            if (Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1) || Input.GetMouseButtonDown(2)) UpdateFocus();
            if (CanUseShortcut(false))
            {
                if (ShortcutDown(upShortcut.Value)) MoveSelected(-1);
                if (ShortcutDown(downShortcut.Value)) MoveSelected(1);
            }
            if (CommitShortcutPressed(true) && CanUseShortcut(true)) CommitCurrentValues();
            PollAddShortcut();
        }

        void LateUpdate()
        {
            if (!attached || !Native.InterfaceVisible) return;
            PositionPanels();
            if (Time.unscaledTime >= nextRefresh)
            {
                nextRefresh = Time.unscaledTime + .15f;
                RefreshGroups();
                HookKeys();
            }
            if (statusText != null && Time.unscaledTime > statusUntil && statusText.text.Length > 0) statusText.text = "";
        }

        void Attach()
        {
            if (!ValidateNativeContract()) return;
            native = typeof(Native).GetField("_self", StaticFlags).GetValue(null) as Native;
            if (native == null) return;
            canvas = Get<Canvas>(native, "_ui");
            window = Get<RectTransform>(native, "_timelineWindow");
            grid = Get<RectTransform>(native, "_keyframesContainer");
            ScrollRect vertical = Get<ScrollRect>(native, "_verticalScrollView");
            if (canvas == null || window == null || grid == null || vertical == null) return;
            content = vertical.content;
            Text original = window.GetComponentInChildren<Text>();
            font = original == null ? Resources.GetBuiltinResource<Font>("Arial.ttf") : original.font;
            // Method contract validation occurs before modifying any native hierarchy.
            Method("UpdateInterpolablesView"); Method("OnKeyframeContainerMouseDown"); Method("UseCurrentValue");
            Method("AddKeyframe", typeof(Interpolable), typeof(float)); Method("AddInterpolable", typeof(InterpolableModel));
            harmony = new Harmony(Guid);
            Patch("UpdateInterpolablesView", "ViewPrefix", "ViewPostfix", null);
            Patch("OnKeyframeContainerMouseDown", "GridPrefix", "GridPostfix", "GridFinalizer");
            Patch("UpdateGrid", null, "GridUpdated", null);
            Patch("UpdateInterpolableSelection", null, "TrackSelectionChanged", null);
            InstallInputHooks();
            InstallGroupInteraction();
            toolbar = MakeRect("GIN Timeline tools", window, true);
            Put(toolbar, 330, -26, 338, 24);
            MakeButton(toolbar, "Anchor", 0, 0, 68, 24, delegate { CaptureAnchor(); });
            MakeButton(toolbar, "1 / 3 / 5", 70, 0, 88, 24, delegate { SelectAlternating(true); });
            MakeButton(toolbar, "2 / 4 / 6", 160, 0, 88, 24, delegate { SelectAlternating(false); });
            MakeButton(toolbar, "+ Tracks", 250, 0, 86, 24, delegate { rightCandidates.Value = !rightCandidates.Value; Config.Save(); });
            foreach (Button b in window.GetComponentsInChildren<Button>(true))
            {
                if (b.transform.IsChildOf(toolbar)) continue;
                Text label = b.GetComponentInChildren<Text>();
                string text = label == null ? "" : label.text.Trim();
                if (text == "▲" || text == "▼" || text == "↑" || text == "↓") toolbarNeighbors.Add((RectTransform)b.transform);
            }
            RectTransform status = MakeRect("GIN Timeline feedback", toolbar, false);
            Put(status, 0, -23, 650, 21);
            statusText = status.gameObject.AddComponent<Text>(); statusText.font = font; statusText.fontSize = 14; statusText.color = Color.white; statusText.raycastTarget = false;
            CreateSidebar();
            attached = true;
            InstallCameraGuard();
            Call("UpdateInterpolablesView");
            Note("GIN Timeline life hack enabled");
            Logger.LogInfo("Native Timeline " + nativeVersion + " attached without replacing window or data.");
        }

        void Patch(string name, string pre, string post, string final)
        {
            HarmonyMethod p = H(pre), q = H(post), f = H(final);
            if (p != null) p.priority = Priority.First;
            if (q != null) q.priority = Priority.Last;
            harmony.Patch(Method(name), p, q, null, f, null);
        }
        static HarmonyMethod H(string name) { return name == null ? null : new HarmonyMethod(typeof(Plugin).GetMethod(name, StaticFlags)); }
        internal static MethodInfo Method(string name, params Type[] types)
        {
            MethodInfo result = types.Length == 0 ? typeof(Native).GetMethod(name, Flags) : typeof(Native).GetMethod(name, Flags, null, types, null);
            if (result == null) throw new MissingMethodException("Timeline", name);
            return result;
        }
        internal object Call(string name, params object[] args)
        {
            MethodInfo chosen = null;
            foreach (MethodInfo m in typeof(Native).GetMethods(Flags))
            {
                if (m.Name != name || m.GetParameters().Length != args.Length) continue;
                bool ok = true; ParameterInfo[] p = m.GetParameters();
                for (int i = 0; i < args.Length; i++) if (args[i] != null && !p[i].ParameterType.IsInstanceOfType(args[i])) ok = false;
                if (ok) { chosen = m; break; }
            }
            if (chosen == null) throw new MissingMethodException("Timeline", name);
            return chosen.Invoke(native, args);
        }
        internal static T Get<T>(object target, string name)
        {
            FieldInfo f = target.GetType().GetField(name, Flags);
            if (f != null) return (T)f.GetValue(target);
            PropertyInfo p = target.GetType().GetProperty(name, Flags);
            if (p == null) throw new MissingMemberException(target.GetType().Name, name);
            return (T)p.GetValue(target, null);
        }
        internal static void Set(object target, string name, object value)
        {
            FieldInfo f = target.GetType().GetField(name, Flags);
            if (f != null) f.SetValue(target, value); else target.GetType().GetProperty(name, Flags).SetValue(target, value, null);
        }
        internal void Note(string text) { message = text; statusUntil = Time.unscaledTime + 5; if (statusText != null) statusText.text = text; }
        internal static string Error(Exception e) { while (e.InnerException != null) e = e.InnerException; return e.Message; }
        internal static bool Ctrl { get { return Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl); } }
        internal static bool Shift { get { return Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift); } }

        void OnDestroy() { Detach(); if (Self == this) Self = null; }
        void Detach()
        {
            attached = false;
            guardEpoch++;
            if (guardedCamera != null && guardedCamera.keyCondition == keyGuard) guardedCamera.keyCondition = previousKeys;
            if (guardedCamera != null && guardedCamera.noCtrlCondition == mouseGuard) guardedCamera.noCtrlCondition = previousMouse;
            if (guardedCamera != null && guardedCamera.zoomCondition == zoomGuard) guardedCamera.zoomCondition = previousZoom;
            guardedCamera = null; keyGuard = previousKeys = null;
            mouseGuard = previousMouse = zoomGuard = previousZoom = null;
            ResetGroupInteraction();
            ResetInputHooks();
            if (harmony != null) { harmony.UnpatchSelf(); harmony = null; }
            RestoreCandidates();
            foreach (KeyValuePair<GameObject, PointerAction> a in modelActions) if (a.Value != null) Destroy(a.Value);
            foreach (KeyValuePair<GameObject, PointerAction> a in keyActions) if (a.Value != null) Destroy(a.Value);
            modelActions.Clear(); keyActions.Clear();
            foreach (GroupUi row in groupRows.Values) row.Destroy();
            groupRows.Clear(); selectedGroups.Clear(); toolbarNeighbors.Clear();
            foreach (GameObject go in owned) if (go != null) Destroy(go);
            owned.Clear(); toolbar = sidebar = candidateContent = null;
            if (native != null && content != null) try { Call("UpdateInterpolablesView"); } catch { }
            native = null; canvas = null; window = grid = content = null;
        }

        void UpdateFocus()
        {
            bool? overAdvanced = AdvanceProperty("IsPointerOverAdvancedUI");
            bool? overExtension = AdvanceProperty("IsPointerOverExtensionUI");
            advancedFocus = (overAdvanced.HasValue ? overAdvanced.Value : PointerOverNativeAdvanced()) || (overExtension.HasValue && overExtension.Value);
            if (advancedFocus) { trackFocus = false; return; }
            if (EventSystem.current == null) { trackFocus = false; return; }
            PointerEventData p = new PointerEventData(EventSystem.current); p.position = Input.mousePosition;
            List<RaycastResult> results = new List<RaycastResult>(); EventSystem.current.RaycastAll(p, results);
            if (results.Count == 0) { trackFocus = false; return; }
            Transform t = results[0].gameObject.transform;
            trackFocus = t.IsChildOf(canvas.transform);
        }

        bool CanUseShortcut(bool allowKkpe)
        {
            if (!attached || TextFocused()) return false;
            // Committing a selected key follows the native Keyframe button, not mouse focus.
            if (allowKkpe) return ReadSelection().Count > 0;
            return Native.InterfaceVisible && trackFocus;
        }
        static bool KkpeEditing()
        {
            bool? extension = AdvanceProperty("IsAdvancedVisible");
            if (extension.HasValue) return extension.Value;
            // Fallback to the verified KKPE selected target's advanced state when the companion DLL is absent.
            foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (a.GetName().Name != "KKPE") continue;
                Type t = a.GetType("HSPE.MainWindow", false);
                if (t == null) return false;
                FieldInfo self = t.GetField("_self", StaticFlags); object w = self == null ? null : self.GetValue(null);
                if (w == null) return false;
                FieldInfo target = t.GetField("_poseTarget", StaticFlags | Flags);
                object pose = target == null ? null : target.GetValue(w);
                if (pose == null) return false;
                Type poseType = a.GetType("HSPE.PoseController", false);
                FieldInfo draw = poseType == null ? null : poseType.GetField("_drawAdvancedMode", StaticFlags);
                return draw != null && (bool)draw.GetValue(null);
            }
            return false;
        }
        static bool PointerOverNativeAdvanced()
        {
            if (!KkpeEditing()) return false;
            foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (a.GetName().Name != "KKPE") continue;
                Type t = a.GetType("HSPE.MainWindow", false); if (t == null) return false;
                FieldInfo self = t.GetField("_self", StaticFlags); object main = self == null ? null : self.GetValue(null);
                FieldInfo rect = t.GetField("_advancedModeRect", Flags);
                return main != null && rect != null && ((Rect)rect.GetValue(main)).Contains(new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y));
            }
            return false;
        }
        internal static bool TextFocused()
        {
            bool? advancedText = AdvanceProperty("IsTextInputActive");
            if (SettingsOpen() || IsNativeTextEditing() || (advancedText.HasValue && advancedText.Value) || !String.IsNullOrEmpty(Input.compositionString)) return true;
            EventSystem ev = EventSystem.current;
            InputField field = ev == null || ev.currentSelectedGameObject == null ? null : ev.currentSelectedGameObject.GetComponentInParent<InputField>();
            if (field != null && field.isFocused) return true;
            try { if (Studio.Studio.Instance != null && Studio.Studio.Instance.isInputNow) return true; } catch { }
            return false;
        }
        static bool? AdvanceProperty(string name)
        {
            foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type t = a.GetType("GinAdvanceHack.Plugin", false); if (t == null) continue;
                PropertyInfo p = t.GetProperty(name, StaticFlags); if (p != null && p.PropertyType == typeof(bool)) return (bool)p.GetValue(null, null);
            }
            return null;
        }
        static bool ShortcutDown(KeyboardShortcut key)
        {
            if (key.IsDown()) return true;
            // Accept the right Shift key for the two defaults, without changing custom assignments.
            if (!Input.GetKey(KeyCode.RightShift) || !Input.GetKeyDown(key.MainKey)) return false;
            int count = 0; foreach (KeyCode mod in key.Modifiers) { if (mod != KeyCode.LeftShift) return false; count++; }
            return count == 1 && !Ctrl && !Input.GetKey(KeyCode.LeftAlt) && !Input.GetKey(KeyCode.RightAlt);
        }
        bool CommitShortcutPressed(bool down)
        {
            KeyboardShortcut binding = commitShortcut.Value;
            bool plainS = binding.MainKey == KeyCode.S;
            foreach (KeyCode modifier in binding.Modifiers) { plainS = false; break; }
            if (!plainS) return down ? binding.IsDown() : Input.GetKey(binding.MainKey);
            return (down ? Input.GetKeyDown(KeyCode.S) : Input.GetKey(KeyCode.S)) && DefaultCommitModifiers(ReadPhysicalKey);
        }
        static bool DefaultCommitModifiers(Func<KeyCode, bool> held)
        {
            // Shift/Alt are commonly held during fine movement. Ctrl+S belongs to Studio save.
            return !held(KeyCode.LeftControl) && !held(KeyCode.RightControl) &&
                !held(KeyCode.LeftCommand) && !held(KeyCode.RightCommand) &&
                !held(KeyCode.LeftWindows) && !held(KeyCode.RightWindows);
        }
        void InstallCameraGuard()
        {
            if (Studio.Studio.Instance == null || Studio.Studio.Instance.cameraCtrl == null) return;
            guardedCamera = Studio.Studio.Instance.cameraCtrl; previousKeys = guardedCamera.keyCondition;
            previousMouse = guardedCamera.noCtrlCondition; previousZoom = guardedCamera.zoomCondition;
            cameraDragging = typeof(Studio.CameraControl).GetField("dragging", Flags);
            int epoch = ++guardEpoch;
            Studio.CameraControl.NoCtrlFunc before = previousKeys;
            keyGuard = delegate
            {
                if (before != null && !before()) return false;
                if (!attached || epoch != guardEpoch) return true;
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
            zoomGuard = delegate { return (zoomBefore == null || zoomBefore()) && !(attached && epoch == guardEpoch && PointerOverOwnedUi()); };
            guardedCamera.keyCondition = keyGuard;
            guardedCamera.noCtrlCondition = mouseGuard; guardedCamera.zoomCondition = zoomGuard;
        }
        bool PointerOverOwnedUi()
        {
            if (!attached || !Native.InterfaceVisible) return false;
            Vector2 point = Input.mousePosition;
            return (sidebar != null && sidebar.gameObject.activeInHierarchy && RectTransformUtility.RectangleContainsScreenPoint(sidebar, point, null)) ||
                (toolbar != null && toolbar.gameObject.activeInHierarchy && RectTransformUtility.RectangleContainsScreenPoint(toolbar, point, null));
        }
        void ClearReleasedDrag(bool blocked, bool held)
        { if (blocked && !held && guardedCamera != null && cameraDragging != null && cameraDragging.FieldType == typeof(bool)) cameraDragging.SetValue(guardedCamera, false); }

        public bool CommitCurrentValues()
        {
            if (!attached) return false;
            List<Key> keys = ReadSelection();
            if (keys.Count == 0) { Note("Select keyframes first"); return false; }
            Call("UseCurrentValue"); Note("Current values applied: " + keys.Count + " keys"); return true;
        }
        internal List<Key> ReadSelection() { return new List<Key>(Native.GetSelectedKeyframes()); }
        internal void Select(List<Key> keys) { Call("SelectKeyframes", (IEnumerable<Key>)keys); }
        public bool CaptureAnchor()
        {
            List<Key> selected = ReadSelection();
            if (selected.Count == 0) { Note("Select a starting key, then click Anchor"); return false; }
            Key first = selected[0]; foreach (Key k in selected) if (k.Key < first.Key) first = k;
            anchor = first; range = lastPhase = null; Note("Anchor: " + first.Key.ToString("0.###") + " s"); return true;
        }
        static bool Exists(Key k) { Timeline.Keyframe v; return k.Value != null && k.Value.parent.keyframes.TryGetValue(k.Key, out v) && Object.ReferenceEquals(v, k.Value); }
        static bool Same(List<Key> a, List<Key> b)
        { if (a == null || b == null || a.Count != b.Count) return false; foreach (Key k in a) if (!b.Contains(k)) return false; return true; }
        public bool SelectAlternating(bool odd)
        {
            List<Key> now = ReadSelection();
            if (now.Count == 0) { Note("Box-select keys first"); return false; }
            if (!anchor.HasValue) { Key first = now[0]; foreach (Key k in now) if (k.Key < first.Key) first = k; anchor = first; }
            if (!Exists(anchor.Value)) { Note("Anchor moved or deleted; select it and press Anchor again"); return false; }
            if (range == null || !Same(now, lastPhase)) range = new List<Key>(now);
            HashSet<Interpolable> tracks = new HashSet<Interpolable>(); foreach (Key k in range) if (Exists(k)) tracks.Add(k.Value.parent);
            List<Key> selected = new List<Key>();
            foreach (Interpolable track in tracks)
            {
                int n = 0;
                foreach (Key k in track.keyframes)
                {
                    if (k.Key < anchor.Value.Key) continue;
                    n++; if (((n % 2 == 1) == odd) && range.Contains(k)) selected.Add(k);
                }
            }
            if (selected.Count == 0) { Note("No matching keys; selection unchanged"); return false; }
            Select(selected); lastPhase = ReadSelection(); Note((odd ? "1 / 3 / 5: " : "2 / 4 / 6: ") + selected.Count + " keys"); return true;
        }
    }
}
