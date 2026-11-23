using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;

namespace GinPadHack
{
    // The Pad window: a header with tab buttons and below it every tab that is switched on, side by
    // side in that order, so that for example the Map and the Body can be used together. The window
    // always shrinks to its contents and only its background is translucent.
    //
    // Which tabs depends on Studio's selection, never on a switch:
    //   a character               Map, Face, Body, Shapes          (setting Window / OpenTabs)
    //   anything else selected    Move, and Shapes when the item has Blend Shapes
    //   (items, several objects)  (setting Window / OpenItemTabs, PadWindow.Items.cs)
    //
    // Middle click on any bar or on the Map keys it in the Timeline at the playhead (an existing key
    // there is updated). With the Timeline's Auto Key on, every edit is keyed by the Timeline itself.
    // Studio's Undo (Ctrl + Z) takes back the Body tab's pose edits and the Move tab's moves and turns.
    //
    // Outside the window's top corners: play / pause and the time drag (top left), and the knob
    // that resizes the whole window (top right); see PadWindow.Corners.cs.
    public sealed partial class PadWindow
    {
        const int WindowId = 0x47494E35;
        const float MessageSeconds = 5f;
        const float ContentWidth = 400f;            // the Map tab, at UiScale 1
        const float PanelWidth = 340f;              // the other tabs: 15 % narrower
        const float SensitivityMin = 0.05f, SensitivityMax = 3f;

        enum Tab
        {
            Map,
            Face,
            Body,
            Shapes
        }

        static readonly string[] TabNames = { "Map", "Face", "Body", "Shapes" };
        static readonly bool[] AllTabsUsable = { true, true, true, true };
        static readonly Color Backdrop = new Color(0.09f, 0.09f, 0.11f);

        readonly Plugin host;
        readonly string storage;

        // "window" is what GUILayout.Window works with: during every layout pass it is shrunk to
        // 10 x 10 so that the window fits its contents again, and it only has its real size after the
        // pass that follows. Any question "is the pointer on the window?" therefore uses "drawn": the
        // window as it was last painted on screen. (Testing against "window" made a press sometimes
        // count as outside the window, and Studio's camera then followed the drag.)
        Rect window, drawn;
        readonly bool[] tabs = new bool[TabNames.Length];
        readonly bool[] itemTabs = new bool[ItemTabNames.Length];

        // What the current GUI pass shows. IMGUI needs the same controls in the Layout pass and in the
        // event that follows it, so a click only requests a change and the next Layout pass adopts it.
        readonly bool[] shownTabs = new bool[TabNames.Length];
        readonly bool[] shownItemTabs = new bool[ItemTabNames.Length];
        ItemSelection shownItems;
        BodyPart shownPart;
        readonly List<BodyPart> shownSelection = new List<BodyPart>();
        Making shownMaking;
        string shownMessage = "";

        float sensitivity;
        bool editPending;
        bool anyButtonHeld, pressBeganOnWindow;
        string message = "";
        float messageUntil;
        int lastDrawFrame = -100;

        internal bool Visible;

        internal PadWindow(Plugin plugin, string storageDirectory)
        {
            host = plugin;
            storage = storageDirectory;
            window = new Rect(plugin.WindowX.Value, plugin.WindowY.Value, 10f, 10f);
            drawn = window;
            sensitivity = plugin.BarSensitivity.Value;
            ReadOpenTabs(host.OpenTabs.Value, TabNames, tabs);
            ReadOpenTabs(host.OpenItemTabs.Value, ItemTabNames, itemTabs);
            LoadPinned();
            LoadMap();
            LoadDelays();
        }

        // A press that began on the window is still held.
        internal bool HoldsPress
        {
            get
            {
                return Visible && anyButtonHeld && pressBeganOnWindow;
            }
        }

        // For the log: what the window knew about the pointer.
        internal string PointerState()
        {
            return "pointer " + Pointer() + ", window " + drawn + ", held " + anyButtonHeld + ", began on window " + pressBeganOnWindow;
        }

        // True when the EventSystem must not see what is behind the window at this screen position
        // (pixels from the bottom left): the window is shown and the pointer is on it, or a press
        // that began on the window is still held.
        internal bool Covers(Vector2 screenPosition)
        {
            if (!Visible || Time.frameCount - lastDrawFrame > 1)
            {
                return false;
            }

            return (anyButtonHeld && pressBeganOnWindow) || OnWindow(new Vector2(screenPosition.x, Screen.height - screenPosition.y));
        }

        // True while mouse input belongs to this window and Studio's camera must ignore it:
        // a press that began on the window (until every button is released, wherever the pointer
        // goes), or the pointer resting on the window with no button held (the wheel).
        // A camera drag that began outside keeps working when it crosses the window.
        internal bool OwnsPointer
        {
            get
            {
                if (!Visible)
                {
                    return false;
                }

                return anyButtonHeld ? pressBeganOnWindow : OnWindow(Pointer());
            }
        }

        static Vector2 Pointer()
        {
            return new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
        }

        // Called from OnGUI while the window is visible.
        internal void Draw()
        {
            PadUi.EnsureStyles(CurrentScale);
            PadUi.Sensitivity = sensitivity;
            lastDrawFrame = Time.frameCount;

            ClaimPress(Event.current);

            // Start every layout pass from a tiny rectangle: the window then grows to exactly fit its contents.
            if (Event.current.type == EventType.Layout)
            {
                window.width = window.height = 10f;
            }

            window = GUILayout.Window(WindowId, window, DrawContents, GUIContent.none, PadUi.Window);
            KeepOnScreen();
            if (Event.current.type == EventType.Repaint)
            {
                drawn = window;
            }

            DrawCorners();
        }

        void DrawContents(int id)
        {
            if (Event.current.type == EventType.Repaint)
            {
                PadUi.Fill(new Rect(0, 0, window.width, window.height), new Color(Backdrop.r, Backdrop.g, Backdrop.b, host.Opacity.Value));
            }

            PadTarget target = Kkpe.Target();
            if (Event.current.type == EventType.Layout)
            {
                Array.Copy(tabs, shownTabs, tabs.Length);
                Array.Copy(itemTabs, shownItemTabs, itemTabs.Length);
                shownItems = ItemSelection.Current();
                shownMaking = making;
                shownMessage = Time.realtimeSinceStartup < messageUntil ? message : "";
                AdoptBodySelection(target);
            }

            GUILayout.BeginHorizontal();
            if (shownItems != null)
            {
                TabButtons(ItemTabNames, itemTabs, shownItems.TabsUsable, host.OpenItemTabs);
            }
            else
            {
                TabButtons(TabNames, tabs, AllTabsUsable, host.OpenTabs);
            }

            string caption = shownItems != null ? shownItems.Caption : target == null ? "" : target.Name;
            GUILayout.Label(caption, PadUi.Small, GUILayout.MinWidth(PadUi.Px(20)), GUILayout.MaxWidth(PadUi.Px(74)));
            GUILayout.FlexibleSpace();
            sensitivity = PadUi.SensitivityBar(sensitivity, SensitivityMin, SensitivityMax, Plugin.DefaultSensitivity, PadUi.Width(120));
            PadUi.TakeKeyRequest();   // a middle click on the Sens bar keys nothing
            if (GUILayout.Button("×", PadUi.Button, PadUi.Width(22)))
            {
                host.ShowWindow(false);
            }

            GUILayout.EndHorizontal();

            if (shownItems != null)
            {
                DrawItemTabs(shownItems);
            }
            else if (target == null)
            {
                GUILayout.Label(Reflect.Type("HSPE.PoseController") == null ? "KKPE is not installed" : "Select a character or an item", PadUi.Small);
            }
            else
            {
                DrawOpenTabs(target);
            }

            if (shownMessage.Length > 0)
            {
                GUILayout.Label(shownMessage, PadUi.Small, PadUi.Width(PanelWidth));
            }

            DrawTimeReadout();   // over the contents while the time is dragged
            DrawDelayReadout();  // ... or while a Delay Key delay is dragged
            GUI.DragWindow(new Rect(0, 0, 10000, PadUi.Px(22)));
        }

        // Every open tab of a character, side by side, top aligned. A middle click in a tab keys
        // what that tab edits.
        void DrawOpenTabs(PadTarget target)
        {
            GUILayout.BeginHorizontal();
            for (int i = 0; i < shownTabs.Length; i++)
            {
                if (!shownTabs[i])
                {
                    continue;
                }

                GUILayout.BeginVertical(GUILayout.ExpandWidth(false));
                switch ((Tab)i)
                {
                    case Tab.Map:
                        DrawMap(target);
                        KeyFaceIfRequested(target);
                        break;
                    case Tab.Face:
                        DrawFace(target);
                        KeyFaceIfRequested(target);
                        break;
                    case Tab.Body:
                        DrawBody(target);
                        if (PadUi.TakeKeyRequest())
                        {
                            KeyBodySelection(target);
                        }

                        break;
                    default:
                        DrawBodyShapes(target);
                        if (PadUi.TakeKeyRequest())
                        {
                            Status(TimelineBridge.KeyNamed(target.Character, new string[] { bodyShapesRenderer }));
                        }

                        break;
                }

                GUILayout.EndVertical();
                GUILayout.Space(PadUi.Px(4f));
            }

            GUILayout.EndHorizontal();
        }

        void KeyFaceIfRequested(PadTarget target)
        {
            if (PadUi.TakeKeyRequest())
            {
                Status(TimelineBridge.KeyFace(target.Character));
            }
        }

        bool TabOpen(Tab which)
        {
            return shownTabs[(int)which];
        }

        // One toggle per tab; "usable" leaves a tab's button out (an item without Blend Shapes).
        // The open tabs are remembered in the given setting.
        void TabButtons(string[] names, bool[] states, bool[] usable, ConfigEntry<string> remembered)
        {
            for (int i = 0; i < names.Length; i++)
            {
                if (!usable[i])
                {
                    continue;
                }

                bool open = PadUi.Toggle(states[i], names[i], PadUi.Width(46));
                if (open != states[i])
                {
                    states[i] = open;
                    faceListStale = true;
                    remembered.Value = OpenTabNames(names, states);
                }
            }
        }

        static void ReadOpenTabs(string setting, string[] names, bool[] states)
        {
            foreach (string name in setting.Split(','))
            {
                int i = Array.IndexOf(names, name.Trim());
                if (i >= 0)
                {
                    states[i] = true;
                }
            }
        }

        static string OpenTabNames(string[] names, bool[] states)
        {
            List<string> open = new List<string>();
            for (int i = 0; i < states.Length; i++)
            {
                if (states[i])
                {
                    open.Add(names[i]);
                }
            }

            return string.Join(",", open.ToArray());
        }

        // A press on the window belongs to the window, whatever Tick saw at the start of the frame
        // (a window that has just grown, a press shorter than a frame). Studio's camera reads the
        // mouse before OnGUI, so this guards every later frame of the drag.
        void ClaimPress(Event e)
        {
            if (e.type == EventType.MouseDown && Visible && OnWindow(e.mousePosition))
            {
                anyButtonHeld = true;
                pressBeganOnWindow = true;
            }
        }

        // Called every frame from Update, before Studio's camera reads the mouse in LateUpdate.
        internal void Tick()
        {
            bool held = Input.GetMouseButton(0) || Input.GetMouseButton(1) || Input.GetMouseButton(2);
            if (held && !anyButtonHeld)
            {
                pressBeganOnWindow = Visible && OnWindow(Pointer());
            }

            anyButtonHeld = held;

            try
            {
                TickMap(held);
                TickItems(held);
                TickBody(held);
                TickDelay(held);
                TickCorners(held);
                if (editPending && !held)
                {
                    // The edit is finished: the lists of used shapes may have changed.
                    editPending = false;
                    faceListStale = true;
                }
            }
            catch (Exception error)
            {
                Status(Root(error).Message);
            }

            if (!held)
            {
                RememberSettings();
            }
        }

        // Settings are written when the mouse is released, not on every drag event.
        void RememberSettings()
        {
            if (Mathf.Abs(host.BarSensitivity.Value - sensitivity) > 0.001f)
            {
                host.BarSensitivity.Value = sensitivity;
            }

            if (Visible && (Mathf.Abs(host.WindowX.Value - window.x) > 0.5f || Mathf.Abs(host.WindowY.Value - window.y) > 0.5f))
            {
                host.WindowX.Value = window.x;
                host.WindowY.Value = window.y;
            }
        }

        // An edit was made; the Face lists are refreshed once the mouse button is released.
        // (Keys are written by the Timeline's Auto Key, or by a middle click.)
        void Edited()
        {
            editPending = true;
        }

        void Status(string text)
        {
            message = text ?? "";
            messageUntil = Time.realtimeSinceStartup + MessageSeconds;
        }

        static Exception Root(Exception error)
        {
            while (error.InnerException != null)
            {
                error = error.InnerException;
            }

            return error;
        }
    }
}
