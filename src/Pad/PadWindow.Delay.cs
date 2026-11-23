using System.Collections.Generic;
using System.Globalization;
using GinShared;
using Studio;
using UnityEngine;

namespace GinPadHack
{
    // Delay Key: the Body tab keys every body part a little after the playhead, each part by its
    // own delay, so that a pose change starts at the hips and runs out through the body (the
    // overlap otherwise made by hand by dragging the shoulder, neck and head keys to the right).
    //
    // The delays are seconds, without frame steps (L.Shoulder +0.052 s after R.Shoulder is fine),
    // and only for keys written from the Pad: Auto Key of the Pad's own edits, Key now and a
    // middle click on the Body tab. Keys written in any other way stay at the playhead.
    // GIN Timeline life hack 1.2.0 writes the keys (KeyDelay.cs there).
    //
    // The panel right of the figure is narrow, so that the window is no wider than without it:
    // a slim bar, Delay Key and Edit. In Edit mode it is about three times as wide as before
    // (the marks then stand apart, with the bar's seconds and Clear).
    //   the bar          a small timeline standing up: the playhead at the top, later downwards.
    //                    Every part is a mark at the height of its delay; the right side's parts
    //                    on the left, as on the figure. Drag a mark: that part's delay.
    //                    Drag between marks: every delay stretches or shrinks by the same factor.
    //                    Wheel: how many seconds the bar shows. Right click a mark: 0,
    //                    right click between marks: the bar fits the delays.
    //                    Double click a mark: type the delay. Ctrl + click a mark: the origin.
    //   Edit             the figure's buttons show and set delays instead of posing:
    //                    drag up / down     the part's delay (later downwards)
    //                    click              show the part's delay in the large readout
    //                    Ctrl + click       the origin (★): delays run out from it
    //                    right click        0
    //                    double click       type the delay
    //   Clear            Ctrl + click: every delay 0
    //   Delay Key        on: keys from the Pad are written later by each part's delay
    //
    // Dragging a part takes along every part further from the origin than it (dragging
    // R.Shoulder moves R.Elbow and R.Hand by the same amount); with Alt only the part itself.
    // Shift is four times finer, "Sens" scales the speed as everywhere in the Pad.
    public sealed partial class PadWindow
    {
        const float CompactPanelWidth = 36f, CompactBarWidth = 14f;   // fits beside the figure under the header
        const float EditPanelWidth = 198f;                            // Edit mode: three times the first panel
        const float DelayMarkWidth = 14f, DelayMarkHeight = 5f, DelayGrab = 7f;
        const float MinDelayRange = 0.1f, MaxDelayRange = 5f, DefaultDelayRange = 0.5f;
        const float DelayReadoutSeconds = 1.5f;
        const string DefaultDelayOrigin = "cf_t_hips";

        static readonly int DelayBarHint = "GinPadDelayBar".GetHashCode();
        static readonly int DelayPartHint = "GinPadDelayPart".GetHashCode();
        static readonly Color DelayTop = new Color(0.20f, 0.36f, 0.95f, 1f), DelayBottom = new Color(0.52f, 0.20f, 0.90f, 1f);
        static readonly Color DelayIkMark = new Color(0.86f, 0.88f, 0.94f, 1f), DelayBoneMark = new Color(0.45f, 0.90f, 0.75f, 1f);
        static readonly Color DelayOriginMark = new Color(1f, 0.80f, 0.25f, 1f), DelayLit = new Color(1f, 0.55f, 0.25f, 1f);
        static readonly Color DelayGrid = new Color(1f, 1f, 1f, 0.22f);

        // Each part's neighbour towards the hips: the body as a tree. Delays run out from the origin
        // along it, in every direction (with the waist as origin, the hips come after the waist too).
        static readonly string[,] DelayLinks =
        {
            { "cf_j_waist02", "cf_t_hips" },
            { "cf_j_neck", "cf_j_waist02" },
            { "cf_j_head", "cf_j_neck" },
            { "cf_d_shoulder_R", "cf_j_waist02" },
            { "cf_d_shoulder_L", "cf_j_waist02" },
            { "cf_t_shoulder_R", "cf_d_shoulder_R" },
            { "cf_t_shoulder_L", "cf_d_shoulder_L" },
            { "cf_t_elbo_R", "cf_t_shoulder_R" },
            { "cf_t_elbo_L", "cf_t_shoulder_L" },
            { "cf_t_hand_R", "cf_t_elbo_R" },
            { "cf_t_hand_L", "cf_t_elbo_L" },
            { "cf_d_bust01_R", "cf_j_waist02" },
            { "cf_d_bust01_L", "cf_j_waist02" },
            { "cf_d_bust03_R", "cf_d_bust01_R" },
            { "cf_d_bust03_L", "cf_d_bust01_L" },
            { "cf_t_waist_R", "cf_t_hips" },
            { "cf_t_waist_L", "cf_t_hips" },
            { "cf_d_siri01_R", "cf_t_hips" },
            { "cf_d_siri01_L", "cf_t_hips" },
            { "cf_d_kokan", "cf_t_hips" },
            { "cf_t_knee_R", "cf_t_waist_R" },
            { "cf_t_knee_L", "cf_t_waist_L" },
            { "cf_t_leg_R", "cf_t_knee_R" },
            { "cf_t_leg_L", "cf_t_knee_L" },
            { "cf_j_toes_R", "cf_t_leg_R" },
            { "cf_j_toes_L", "cf_t_leg_L" }
        };

        readonly Dictionary<string, float> delays = new Dictionary<string, float>();   // part name -> seconds
        string delayOrigin = DefaultDelayOrigin;
        float delayRange = DefaultDelayRange;   // seconds from the top of the bar to its bottom
        bool delayKeyOn, delayEdit;
        bool delayEditShown;                    // Edit as the current GUI pass lays it out (adopted in Layout)
        bool delaysChanged;                     // written to the settings on release

        // The drag in progress: a part (with or without the parts beyond it), or the whole bar.
        BodyPart delayDragPart;
        bool delayDragChain, delayDragMoved, delayStretching, delayPressCtrl;
        float delaySecondsPerPixel;
        Dictionary<string, float> delaysAtPress;
        float delayDragTotal;                   // pixels moved since the press (Shift and Sens applied)

        BodyPart delayReadoutPart;              // the large readout shows this part's delay ...
        float delayReadoutUntil;                // ... while dragged and this long after (realtime)
        BodyPart delayHoverFigure, delayHoverBar;
        BodyPart delayTyping;                   // the part whose delay is being typed, or null
        string delayTypingText = "";
        bool delayTypingFocus;
        Rect delayBar;                          // the bar as last laid out, window coordinates

        // ---- settings ------------------------------------------------------------------------

        void LoadDelays()
        {
            delays.Clear();
            foreach (string item in host.KeyDelays.Value.Split(','))
            {
                int equals = item.IndexOf('=');
                float seconds;
                if (equals > 0 && float.TryParse(item.Substring(equals + 1).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out seconds) && FindPart(item.Substring(0, equals).Trim()) != null)
                {
                    delays[item.Substring(0, equals).Trim()] = Mathf.Max(0f, seconds);
                }
            }

            delayOrigin = FindPart(host.DelayOrigin.Value) != null ? host.DelayOrigin.Value : DefaultDelayOrigin;
            delayRange = Mathf.Clamp(host.DelayRange.Value, MinDelayRange, MaxDelayRange);
        }

        // From Tick when no button is held.
        void RememberDelays()
        {
            if (!delaysChanged)
            {
                return;
            }

            delaysChanged = false;
            List<string> items = new List<string>();
            foreach (BodyPart part in BodyParts)
            {
                float seconds = Delay(part);
                if (seconds > 0f)
                {
                    items.Add(part.Name + "=" + seconds.ToString("0.####", CultureInfo.InvariantCulture));
                }
            }

            host.KeyDelays.Value = string.Join(",", items.ToArray());
            host.DelayOrigin.Value = delayOrigin;
            host.DelayRange.Value = delayRange;
        }

        static BodyPart FindPart(string name)
        {
            foreach (BodyPart part in BodyParts)
            {
                if (part.Name == name)
                {
                    return part;
                }
            }

            return null;
        }

        float Delay(BodyPart part)
        {
            float seconds;
            return delays.TryGetValue(part.Name, out seconds) ? seconds : 0f;
        }

        void SetDelay(BodyPart part, float seconds)
        {
            delays[part.Name] = Mathf.Max(0f, seconds);
            delaysChanged = true;
        }

        // ---- the body as a tree, seen from the origin ------------------------------------------

        // Each part's neighbour towards the given origin (the origin itself: none).
        static Dictionary<string, string> TowardsOrigin(string origin)
        {
            Dictionary<string, List<string>> neighbours = new Dictionary<string, List<string>>();
            for (int i = 0; i < DelayLinks.GetLength(0); i++)
            {
                Link(neighbours, DelayLinks[i, 0], DelayLinks[i, 1]);
                Link(neighbours, DelayLinks[i, 1], DelayLinks[i, 0]);
            }

            Dictionary<string, string> towards = new Dictionary<string, string>();
            towards[origin] = null;
            Queue<string> open = new Queue<string>();
            open.Enqueue(origin);
            while (open.Count > 0)
            {
                string part = open.Dequeue();
                List<string> next;
                if (!neighbours.TryGetValue(part, out next))
                {
                    continue;
                }

                foreach (string neighbour in next)
                {
                    if (!towards.ContainsKey(neighbour))
                    {
                        towards[neighbour] = part;
                        open.Enqueue(neighbour);
                    }
                }
            }

            return towards;
        }

        static void Link(Dictionary<string, List<string>> neighbours, string from, string to)
        {
            List<string> list;
            if (!neighbours.TryGetValue(from, out list))
            {
                list = new List<string>();
                neighbours[from] = list;
            }

            list.Add(to);
        }

        // The part and every part further from the origin than it (whose way to the origin passes it).
        internal static List<string> PartsBeyond(string part, string origin)
        {
            Dictionary<string, string> towards = TowardsOrigin(origin);
            List<string> beyond = new List<string>();
            foreach (string candidate in towards.Keys)
            {
                for (string step = candidate; step != null; step = towards[step])
                {
                    if (step == part)
                    {
                        beyond.Add(candidate);
                        break;
                    }
                }
            }

            return beyond;
        }

        // The part a delay is best compared with: the other side's part, otherwise the neighbour
        // towards the origin.
        BodyPart DelayPartner(BodyPart part)
        {
            string name = part.Name;
            string other = name.EndsWith("_L") ? name.Substring(0, name.Length - 2) + "_R" : name.EndsWith("_R") ? name.Substring(0, name.Length - 2) + "_L" : null;
            if (other == null)
            {
                string towards;
                TowardsOrigin(delayOrigin).TryGetValue(name, out towards);
                other = towards;
            }

            return other == null ? null : FindPart(other);
        }

        // ---- changing delays ---------------------------------------------------------------------

        void BeginDelayDrag(BodyPart part, bool alone, bool ctrl)
        {
            delayPressCtrl = ctrl;
            delayDragPart = part;
            delayDragChain = !alone;
            delayStretching = part == null;
            delayDragMoved = false;
            delayDragTotal = 0f;
            delaysAtPress = new Dictionary<string, float>(delays);
            // Fixed for the whole drag: the bar may grow on the way, the speed does not change.
            delaySecondsPerPixel = delayRange / Mathf.Max(1f, delayBar.height > 0f ? delayBar.height : PadUi.Px(FigureRows * Pitch));
            delayReadoutPart = part;
        }

        // "pixels": downwards movement of this event, Shift and Sens applied.
        void DragDelay(float pixels)
        {
            if (pixels == 0f || delaysAtPress == null)
            {
                return;
            }

            delayDragMoved = true;
            delayDragTotal += pixels;
            delays.Clear();
            foreach (KeyValuePair<string, float> pair in delaysAtPress)
            {
                delays[pair.Key] = pair.Value;
            }

            if (delayStretching)
            {
                // Down: every delay grows; the bar's height once doubles them.
                float factor = Mathf.Exp(delayDragTotal / Mathf.Max(1f, delayBar.height) * Mathf.Log(2f));
                foreach (BodyPart part in BodyParts)
                {
                    delays[part.Name] = Delay(part) * factor;
                }
            }
            else
            {
                ShiftDelays(delays, delayDragPart.Name, delayOrigin, delayDragTotal * delaySecondsPerPixel, delayDragChain);
            }

            delaysChanged = true;
            delayReadoutUntil = Time.realtimeSinceStartup + DelayReadoutSeconds;
            GrowRangeToDelays();
        }

        // Move a part's delay (and with "chain" the parts beyond it) by "seconds". The part stops at
        // 0 and the parts beyond move as far as it did; none goes below 0.
        internal static void ShiftDelays(Dictionary<string, float> values, string part, string origin, float seconds, bool chain)
        {
            float own;
            values.TryGetValue(part, out own);
            seconds = Mathf.Max(seconds, -own);
            List<string> moved = chain ? PartsBeyond(part, origin) : new List<string>(new string[] { part });
            foreach (string name in moved)
            {
                float value;
                values.TryGetValue(name, out value);
                values[name] = Mathf.Max(0f, value + seconds);
            }
        }

        // A press on a part that did not move: Ctrl makes it the origin; the readout shows it a moment.
        void EndDelayDrag()
        {
            if (delayDragPart != null && !delayDragMoved && delayPressCtrl)
            {
                SetOrigin(delayDragPart);
            }

            if (delayDragPart != null)
            {
                delayReadoutUntil = Time.realtimeSinceStartup + DelayReadoutSeconds;
            }

            delayDragPart = null;
            delayStretching = false;
            delaysAtPress = null;
        }

        // A delay below the bar's bottom makes the bar longer.
        void GrowRangeToDelays()
        {
            float largest = 0f;
            foreach (float seconds in delays.Values)
            {
                largest = Mathf.Max(largest, seconds);
            }

            if (largest > delayRange * 0.95f)
            {
                delayRange = Mathf.Min(MaxDelayRange, largest / 0.9f);
                delaysChanged = true;
            }
        }

        void SetOrigin(BodyPart part)
        {
            delayOrigin = part.Name;
            delaysChanged = true;
            Status("Delay origin: " + part.Label + " - dragging a part takes along the parts further from " + part.Label);
        }

        void BeginTyping(BodyPart part)
        {
            delayTyping = part;
            delayTypingText = Delay(part).ToString("0.000", CultureInfo.InvariantCulture);
            delayTypingFocus = true;
        }

        // ---- keys ------------------------------------------------------------------------------

        // The Timeline row names of these parts, each with its delay ("cf_t_shoulder_L(work)=0.105").
        List<string> DelayedNames(PadTarget target, List<BodyPart> parts)
        {
            List<string> names = new List<string>();
            foreach (BodyPart part in parts)
            {
                string name = RowName(target, part);
                if (name != null)
                {
                    names.Add(name + "=" + Delay(part).ToString("0.####", CultureInfo.InvariantCulture));
                }
            }

            return names;
        }

        // The transform a part's Timeline rows are known by: the IK target's, or the bone's.
        static string RowName(PadTarget target, BodyPart part)
        {
            if (part.Via == Via.Ik)
            {
                OCIChar.IKInfo ik = target.IkTarget(part.Name);
                return ik == null || ik.guideObject == null ? null : ik.guideObject.transformTarget.name;
            }

            Transform bone = target.Bone(part.Name);
            return bone == null ? null : bone.name;
        }

        // The Pad edited these parts: with Delay Key on, Auto Key writes their keys later.
        void BodyEdited(PadTarget target, List<BodyPart> parts)
        {
            if (delayKeyOn && parts.Count > 0)
            {
                TimelineBridge.NotePadEdit(target.Character, DelayedNames(target, parts));
            }

            Edited();
        }

        // KKPE's limb copies and Swap pose change several parts at once.
        void AllPartsEdited(PadTarget target)
        {
            BodyEdited(target, new List<BodyPart>(BodyParts));
        }

        // ---- the figure in Edit mode -------------------------------------------------------------

        void DelayPartButton(Rect place, PadTarget target, BodyPart part)
        {
            int id = GUIUtility.GetControlID(DelayPartHint, FocusType.Passive, place);
            Event current = Event.current;
            Vector2 moved;
            if (PadUi.TakeHeldMovement(id, current.delta, out moved))
            {
                DragDelay(moved.y * (PadUi.Fine(current) ? PadUi.FineFactor : 1f) * PadUi.Sensitivity);
            }

            switch (current.GetTypeForControl(id))
            {
                case EventType.MouseDown:
                    if (!place.Contains(current.mousePosition))
                    {
                        break;
                    }

                    if (current.button == 0 && current.clickCount == 2)
                    {
                        BeginTyping(part);
                        current.Use();
                    }
                    else if (current.button == 0)
                    {
                        GUIUtility.hotControl = id;
                        BeginDelayDrag(part, current.alt, current.control);
                        current.Use();
                    }
                    else if (current.button == 1)
                    {
                        SetDelay(part, 0f);
                        Status("Delay of " + part.Label + ": 0");
                        current.Use();
                    }

                    break;
                case EventType.MouseUp:
                    if (GUIUtility.hotControl == id)
                    {
                        GUIUtility.hotControl = 0;
                        EndlessDrag.End();
                        EndDelayDrag();
                        current.Use();
                    }

                    break;
                case EventType.Repaint:
                    bool hover = place.Contains(current.mousePosition);
                    if (hover)
                    {
                        delayHoverFigure = part;
                    }

                    Color previous = GUI.color;
                    if (!Usable(target, part))
                    {
                        GUI.color = new Color(1f, 1f, 1f, 0.45f);
                    }

                    GUIStyle style = part.Via == Via.Ik ? PadUi.PartButton : part.Small ? PadUi.SmallBoneButton : PadUi.BoneButton;
                    bool lit = GUIUtility.hotControl == id || delayHoverBar == part || delayTyping == part;
                    style.Draw(place, DelayCaption(part), hover, GUIUtility.hotControl == id, lit, false);
                    GUI.color = previous;
                    break;
            }
        }

        // "★ 0", "+0.052", "0": what a figure button shows in Edit mode.
        string DelayCaption(BodyPart part)
        {
            float seconds = Delay(part);
            string value = seconds <= 0f ? "0" : "+" + seconds.ToString(seconds < 1f ? ".000" : "0.00", CultureInfo.InvariantCulture);
            return (part.Name == delayOrigin ? "★ " : "") + value;
        }

        // ---- the panel right of the figure -------------------------------------------------------

        // Layout pass, before the figure: whether this pass draws the Edit layout.
        void AdoptDelayEdit()
        {
            if (Event.current.type == EventType.Layout)
            {
                delayEditShown = delayEdit;
            }
        }

        void DrawDelayPanel(PadTarget target)
        {
            float width = delayEditShown ? EditPanelWidth : CompactPanelWidth;
            GUILayout.BeginVertical(PadUi.Width(width));
            Rect column = GUILayoutUtility.GetRect(PadUi.Px(width), PadUi.Px(FigureRows * Pitch), GUILayout.ExpandWidth(false));
            Rect bar = column;
            if (!delayEditShown)
            {
                // The slim bar in the middle of the narrow column.
                bar = new Rect(Mathf.Round(column.center.x - PadUi.Px(CompactBarWidth) * 0.5f), column.y, PadUi.Px(CompactBarWidth), column.height);
            }

            if (Event.current.type == EventType.Repaint)
            {
                delayBar = bar;
            }

            DelayBar(bar);

            Rect typing = GUILayoutUtility.GetRect(PadUi.Px(width), PadUi.Px(18f), GUILayout.ExpandWidth(false));
            DelayTypingField(typing);

            bool on = GUILayout.Toggle(delayKeyOn, "Delay\nKey", PadUi.DelayKeyButton, PadUi.Width(width));
            if (on != delayKeyOn)
            {
                delayKeyOn = on;
                if (on && !TimelineBridge.CanDelayKeys)
                {
                    Status("Delay Key needs GIN Timeline life hack 1.2.0 or later");
                }
                else
                {
                    Status(on ? "Delay Key on: the Pad's body keys are written later by each part's delay" : "Delay Key off: body keys at the playhead");
                }
            }

            bool edit = PadUi.Toggle(delayEdit, "Edit", PadUi.Width(width));
            if (edit != delayEdit)
            {
                delayEdit = edit;
                delayTyping = null;
                Status(edit
                    ? "Delay Edit: drag a part up / down (Alt: only that part), Ctrl + click: origin ★, right click: 0, double click: type"
                    : "Delay Edit off: the figure poses again");
            }

            if (delayEditShown && GUILayout.Button("Clear", PadUi.Button, PadUi.Width(width)))
            {
                if (Event.current.control)
                {
                    delays.Clear();
                    delaysChanged = true;
                    Status("Every delay is 0");
                }
                else
                {
                    Status("Ctrl + click Clear sets every delay to 0");
                }
            }

            GUILayout.EndVertical();
        }

        // The standing timeline: top = playhead, the bottom = delayRange seconds later.
        void DelayBar(Rect bar)
        {
            int id = GUIUtility.GetControlID(DelayBarHint, FocusType.Passive, bar);
            Event current = Event.current;
            Vector2 moved;
            if (PadUi.TakeHeldMovement(id, current.delta, out moved))
            {
                DragDelay(moved.y * (PadUi.Fine(current) ? PadUi.FineFactor : 1f) * PadUi.Sensitivity);
            }

            switch (current.GetTypeForControl(id))
            {
                case EventType.MouseDown:
                    if (!bar.Contains(current.mousePosition))
                    {
                        break;
                    }

                    BodyPart mark = MarkAt(bar, current.mousePosition);
                    if (current.button == 0 && current.clickCount == 2 && mark != null)
                    {
                        BeginTyping(mark);
                    }
                    else if (current.button == 0)
                    {
                        GUIUtility.hotControl = id;
                        BeginDelayDrag(mark, current.alt, current.control);
                    }
                    else if (current.button == 1 && mark != null)
                    {
                        SetDelay(mark, 0f);
                        Status("Delay of " + mark.Label + ": 0");
                    }
                    else if (current.button == 1)
                    {
                        FitDelayRange();
                    }

                    current.Use();
                    break;
                case EventType.MouseUp:
                    if (GUIUtility.hotControl == id)
                    {
                        GUIUtility.hotControl = 0;
                        EndlessDrag.End();
                        EndDelayDrag();
                        current.Use();
                    }

                    break;
                case EventType.ScrollWheel:
                    if (bar.Contains(current.mousePosition))
                    {
                        delayRange = Mathf.Clamp(delayRange * (current.delta.y > 0f ? 1.25f : 0.8f), MinDelayRange, MaxDelayRange);
                        delaysChanged = true;
                        current.Use();
                    }

                    break;
                case EventType.Repaint:
                    delayHoverBar = GUIUtility.hotControl == 0 && bar.Contains(current.mousePosition) ? MarkAt(bar, current.mousePosition) : null;
                    PaintDelayBar(bar, GUIUtility.hotControl == id);
                    break;
            }
        }

        // Wheel shortcut's sibling: the bar shows the largest delay with a little room below it.
        void FitDelayRange()
        {
            float largest = 0f;
            foreach (float seconds in delays.Values)
            {
                largest = Mathf.Max(largest, seconds);
            }

            delayRange = largest > 0f ? Mathf.Clamp(largest / 0.85f, MinDelayRange, MaxDelayRange) : DefaultDelayRange;
            delaysChanged = true;
            Status("Delay bar: 0 .. " + delayRange.ToString("0.00") + " s");
        }

        // Where a part's mark is on the bar.
        // On the slim bar every mark spans the bar; on the wide one each part has its own lane,
        // placed as on the figure.
        Rect MarkRect(Rect bar, BodyPart part)
        {
            float y = bar.y + Mathf.Clamp01(Delay(part) / delayRange) * (bar.height - PadUi.Px(DelayMarkHeight));
            if (!delayEditShown)
            {
                return new Rect(bar.x + 1f, y, bar.width - 2f, PadUi.Px(DelayMarkHeight));
            }

            float lane = (part.Place.x + part.Place.width * 0.5f) / FigureWidth;
            float inset = PadUi.Px(DelayMarkWidth * 0.5f + 2f);
            float x = bar.x + inset + lane * (bar.width - inset * 2f);
            return new Rect(x - PadUi.Px(DelayMarkWidth) * 0.5f, y, PadUi.Px(DelayMarkWidth), PadUi.Px(DelayMarkHeight));
        }

        // The mark nearest the pointer within reach; null when none is. Big parts win a tie, so the
        // often used ones are easy to take.
        BodyPart MarkAt(Rect bar, Vector2 point)
        {
            BodyPart best = null;
            float bestDistance = PadUi.Px(DelayGrab);
            foreach (BodyPart part in BodyParts)
            {
                Rect mark = MarkRect(bar, part);
                float dx = Mathf.Max(0f, Mathf.Abs(point.x - mark.center.x) - mark.width * 0.5f);
                float dy = Mathf.Max(0f, Mathf.Abs(point.y - mark.center.y) - mark.height * 0.5f);
                float distance = Mathf.Sqrt(dx * dx + dy * dy) + (part.Small ? 0.5f : 0f);
                if (distance <= bestDistance)
                {
                    best = part;
                    bestDistance = distance;
                }
            }

            return best;
        }

        void PaintDelayBar(Rect bar, bool held)
        {
            // Blue at the playhead, violet further down; dimmed while Delay Key is off.
            const int Strips = 24;
            float alpha = delayKeyOn ? 1f : 0.45f;
            for (int i = 0; i < Strips; i++)
            {
                Color color = Color.Lerp(DelayTop, DelayBottom, i / (Strips - 1f));
                color.a = alpha;
                float top = bar.y + bar.height * i / Strips;
                PadUi.Fill(new Rect(bar.x, top, bar.width, bar.y + bar.height * (i + 1) / Strips - top), color);
            }

            // Grid: a line every "step" seconds, labelled.
            float step = GridStep(delayRange);
            for (float seconds = step; seconds < delayRange - step * 0.25f; seconds += step)
            {
                float y = Mathf.Round(bar.y + seconds / delayRange * (bar.height - PadUi.Px(DelayMarkHeight)) + PadUi.Px(DelayMarkHeight) * 0.5f);
                PadUi.Fill(new Rect(bar.x, y, bar.width, 1f), DelayGrid);
                if (delayEditShown)
                {
                    GUI.Label(new Rect(bar.x + 1f, y - PadUi.Px(11f), bar.width, PadUi.Px(11f)), seconds.ToString(step < 0.1f ? "0.00" : "0.0"), PadUi.DelayGridLabel);
                }
            }

            if (delayEditShown)
            {
                GUI.Label(new Rect(bar.x, bar.yMax - PadUi.Px(12f), bar.width - 2f, PadUi.Px(12f)), delayRange.ToString("0.00") + " s", PadUi.DelayRangeLabel);
            }
            PadUi.Fill(new Rect(bar.x, bar.y, bar.width, 2f), Color.white);   // the playhead

            // Marks: small ones first, so the big parts are drawn on top; the lit one last.
            BodyPart lit = delayDragPart ?? delayTyping ?? delayHoverBar ?? delayHoverFigure;
            for (int pass = 0; pass < 2; pass++)
            {
                foreach (BodyPart part in BodyParts)
                {
                    if (part.Small != (pass == 0) || part == lit)
                    {
                        continue;
                    }

                    PaintMark(bar, part, false);
                }
            }

            if (lit != null)
            {
                PaintMark(bar, lit, true);
            }

            if (held && delayStretching)
            {
                PadUi.Fill(new Rect(bar.x, bar.y, 2f, bar.height), Color.white);
                PadUi.Fill(new Rect(bar.xMax - 2f, bar.y, 2f, bar.height), Color.white);
            }
        }

        void PaintMark(Rect bar, BodyPart part, bool lit)
        {
            Rect mark = MarkRect(bar, part);
            Color color = lit ? DelayLit : part.Name == delayOrigin ? DelayOriginMark : part.Via == Via.Ik ? DelayIkMark : DelayBoneMark;
            PadUi.Fill(new Rect(mark.x - 1f, mark.y - 1f, mark.width + 2f, mark.height + 2f), new Color(0f, 0f, 0f, 0.55f));
            PadUi.Fill(mark, color);
            if (lit)
            {
                // The name beside the mark, on the side with room.
                float width = PadUi.Px(70f);
                bool left = !delayEditShown || mark.center.x > bar.center.x;   // the slim bar: over the figure
                Rect label = new Rect(left ? mark.x - width - 2f : mark.xMax + 2f, mark.center.y - PadUi.Px(7f), width, PadUi.Px(14f));
                PadUi.Fill(label, new Color(0.05f, 0.05f, 0.07f, 0.85f));
                GUI.Label(label, part.Label, left ? PadUi.DelayMarkLabelRight : PadUi.DelayMarkLabel);
            }
        }

        // A round step for the grid: about four to eight lines on the bar.
        internal static float GridStep(float range)
        {
            float[] steps = { 0.01f, 0.02f, 0.05f, 0.1f, 0.2f, 0.25f, 0.5f, 1f };
            foreach (float step in steps)
            {
                if (range / step <= 8f)
                {
                    return step;
                }
            }

            return 1f;
        }

        // The line under the bar: typing a delay (Enter applies, Esc cancels), otherwise the hint.
        void DelayTypingField(Rect rect)
        {
            if (delayTyping == null)
            {
                BodyPart shown = delayHoverBar ?? delayHoverFigure;
                string text = !delayEditShown ? delayRange.ToString("0.0#") + "s" : shown != null ? shown.Label : "Edit";
                GUI.Label(rect, text, PadUi.DelayHint);
                return;
            }

            Event current = Event.current;
            if (current.type == EventType.KeyDown && (current.keyCode == KeyCode.Return || current.keyCode == KeyCode.KeypadEnter))
            {
                float seconds;
                if (float.TryParse(delayTypingText.Trim().TrimStart('+'), NumberStyles.Float, CultureInfo.InvariantCulture, out seconds))
                {
                    SetDelay(delayTyping, seconds);
                    GrowRangeToDelays();
                    Status("Delay of " + delayTyping.Label + ": +" + Delay(delayTyping).ToString("0.000", CultureInfo.InvariantCulture) + " s");
                }

                delayTyping = null;
                GUIUtility.keyboardControl = 0;
                current.Use();
                return;
            }

            if (current.type == EventType.KeyDown && current.keyCode == KeyCode.Escape)
            {
                delayTyping = null;
                GUIUtility.keyboardControl = 0;
                current.Use();
                return;
            }

            GUI.SetNextControlName("GinPadDelayTyping");
            delayTypingText = GUI.TextField(rect, delayTypingText, PadUi.DelayTyping);
            if (delayTypingFocus && current.type == EventType.Repaint)
            {
                delayTypingFocus = false;
                GUI.FocusControl("GinPadDelayTyping");
            }
        }

        // ---- the large readout while a delay is dragged --------------------------------------------
        //
        //   L.Shoulder   +0.105 s
        //   vs R.Shoulder  +0.052 s          key 1.193 s

        bool DelayReadoutShown
        {
            get
            {
                return delayReadoutPart != null && (delayDragPart != null || Time.realtimeSinceStartup < delayReadoutUntil);
            }
        }

        void DrawDelayReadout()
        {
            if (Event.current.type != EventType.Repaint || !DelayReadoutShown || ReadoutShown)
            {
                return;
            }

            BodyPart part = delayReadoutPart;
            float seconds = Delay(part);
            float pad = PadUi.Px(6f);
            float width = Mathf.Min(PadUi.Px(ReadoutWidth + 30f), window.width - PadUi.Px(8f));
            Rect panel = new Rect(PadUi.Px(4f), PadUi.Px(26f), width, PadUi.Px(ReadoutBigRow + ReadoutSmallRow) + pad * 2f);
            PadUi.Fill(panel, ReadoutBack);

            Rect big = new Rect(panel.x + pad, panel.y + pad, panel.width - pad * 2f, PadUi.Px(ReadoutBigRow));
            GUI.Label(big, "+" + seconds.ToString("0.000", CultureInfo.InvariantCulture) + " s", PadUi.BigTime);
            GUI.Label(big, part.Label, PadUi.TimeDetail);

            Rect change = new Rect(big.x, big.yMax, big.width, PadUi.Px(ReadoutSmallRow));
            BodyPart partner = DelayPartner(part);
            if (partner != null)
            {
                float difference = seconds - Delay(partner);
                Color previous = GUI.contentColor;
                GUI.contentColor = difference < 0f ? Earlier : Later;
                GUI.Label(change, (difference < 0f ? "-" : "+") + Mathf.Abs(difference).ToString("0.000", CultureInfo.InvariantCulture) + " s", PadUi.TimeChange);
                GUI.contentColor = previous;
                GUI.Label(new Rect(change.x + PadUi.Px(110f), change.y, change.width - PadUi.Px(110f), change.height), "vs " + partner.Label, PadUi.DelayVersus);
            }

            if (TimelineBridge.HasPlayback)
            {
                GUI.Label(change, "key " + (TimelineBridge.PlaybackTime + seconds).ToString("0.000", CultureInfo.InvariantCulture) + " s", PadUi.TimeDetail);
            }
        }

        // From Tick: a release that no control saw still ends the drag.
        void TickDelay(bool mouseHeld)
        {
            if (!mouseHeld && (delayDragPart != null || delayStretching))
            {
                EndDelayDrag();
            }

            if (!mouseHeld)
            {
                RememberDelays();
            }
        }
    }
}
