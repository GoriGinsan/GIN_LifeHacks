using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Timeline;
using UnityEngine;
using UnityEngine.UI;
using Native = Timeline.Timeline;

namespace GinTimelineHack
{
    // Auto Key (like Blender's auto keying).
    //
    // While it is on and playback is stopped, moving something that already has a Timeline
    // row writes that row's key at the playhead: an existing key there is updated, otherwise
    // a key is added. Rows are created only with "Add missing rows" (AutoKeyRows.cs), which also
    // watches every row type instead of the AutoKey / Rows list. The S key keeps working as before.
    // GIN Pad's Delay Key writes the rows of the body parts it edits a little later (KeyDelay.cs).
    // The toolbar control and the curve of added keys are in AutoKeyButton.cs.
    //
    // How a manual edit is recognised:
    //   1. Timeline only writes values while playing or seeking. After either, the current
    //      values are remembered as the baseline and nothing is keyed.
    //   2. Later differences from the baseline count only shortly after mouse or keyboard
    //      input. Other differences are folded into the baseline without a key.
    //   3. KKPE rows compare KKPE's own override values, not the live transform or mesh, so
    //      body animation and blinking never look like an edit.
    //   4. The key is written one frame after the change: KKPE applies an edit to the bone or
    //      mesh in LateUpdate, and the native "current value" is only correct after that.
    //   5. Everything is compared at most 20 times a second during input and 10 times without;
    //      between those only the rows waiting for their key are compared, every frame.
    public sealed partial class Plugin
    {
        const float AutoKeyIdleInterval = 0.1f;   // seconds between checks without user input
        const float AutoKeyInputGrace = 0.5f;     // how long after input a change counts as an edit
        const string AutoKeyDefaultRows = "guideObjectPos,guideObjectRot,guideObjectScale,bonePos,boneRot,boneScale,lastBlendShape,groupBlendShape,groupBlendShapeLink,groupBlendShapeNameLink";

        static readonly Color AutoKeyOnColor = new Color(.86f, .14f, .14f, 1f);
        static readonly object NotWatched = new object();      // row is ignored by Auto Key
        static readonly object NotOverridden = new object();   // KKPE has no override: animation owns the value
        static int nativeWroteValuesFrame = -10;

        bool autoKey, autoKeySupported, autoKeyBaselineStale = true;
        float autoKeyTime = -1f, autoKeyNextCheck, autoKeyLastInput = -10f;
        readonly Dictionary<Interpolable, object> autoKeyBaseline = new Dictionary<Interpolable, object>();
        readonly Dictionary<Interpolable, int> autoKeyChangedFrame = new Dictionary<Interpolable, int>();
        readonly List<Interpolable> autoKeyDue = new List<Interpolable>();
        readonly HashSet<string> autoKeyRows = new HashSet<string>();
        Image autoKeyButton, autoKeyStripe;
        MethodInfo blendShapeName;

        public bool AutoKeyEnabled
        {
            get
            {
                return autoKey;
            }
        }

        void InstallAutoKey()
        {
            MethodInfo interpolate = typeof(Native).GetMethod("Interpolate", Flags, null, new Type[] { typeof(bool) }, null);
            autoKeySupported = interpolate != null;
            if (autoKeySupported)
            {
                harmony.Patch(interpolate, null, H("NativeWroteValues"), null, null, null);
            }

            ReadAutoKeyRows();
            InstallMaterialEditorHook();
        }

        void ResetAutoKey()
        {
            autoKey = false;
            autoKeyBaselineStale = true;
            autoKeyBaseline.Clear();
            autoKeyChangedFrame.Clear();
            neverAddedRows.Clear();
            autoKeyButton = autoKeyStripe = autoKeyDot = null;
            autoKeyArrow = autoKeyMenu = null;
            addRowsBox = addRowsCheck = null;
            ResetAutoRows();
            curveRows.Clear();
            curveButtons.Clear();
        }

        void ReadAutoKeyRows()
        {
            autoKeyRows.Clear();
            foreach (string id in autoKeyRowsSetting.Value.Split(','))
            {
                if (id.Trim().Length > 0)
                {
                    autoKeyRows.Add(id.Trim());
                }
            }
        }

        static void NativeWroteValues()
        {
            nativeWroteValuesFrame = Time.frameCount;
        }

        public void SetAutoKey(bool on)
        {
            if (on && !autoKeySupported)
            {
                Note("Auto Key needs Timeline 1.5.6");
                return;
            }

            autoKey = on;
            autoKeyBaselineStale = true;
            autoKeyChangedFrame.Clear();
            ShowAutoKeyState();
            ResetAutoRows();
            Note(!on ? "Auto Key off - S applies current values to selected keys"
                : AutoKeyAddRows ? "AUTO KEY ON - an edit writes its key at the playhead, adding the row when there is none"
                : "AUTO KEY ON - moving a registered row writes its key at the playhead");
        }

        void ShowAutoKeyState()
        {
            ShowRecordState();
            if (autoKeyStripe != null)
            {
                autoKeyStripe.gameObject.SetActive(autoKey);
            }
        }

        // Called every frame from Update.
        void PollAutoKey()
        {
            if (!autoKey)
            {
                return;
            }

            if (Input.anyKey || Input.GetMouseButtonUp(0) || Input.GetMouseButtonUp(1) || Input.GetMouseButtonUp(2))
            {
                autoKeyLastInput = Time.unscaledTime;
            }

            if (Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1) || Input.GetMouseButtonDown(2))
            {
                autoRowsRebuildDue = true;   // a press may select what is edited next
            }

            bool valuesSettled = !Native.isPlaying && Time.frameCount > nativeWroteValuesFrame + 1 && Native.playbackTime == autoKeyTime;
            if (!valuesSettled)
            {
                autoKeyTime = Native.playbackTime;
                autoKeyBaselineStale = true;
                autoKeyChangedFrame.Clear();
                return;
            }

            bool userActive = Time.unscaledTime - autoKeyLastInput <= AutoKeyInputGrace;
            if (autoKeyBaselineStale || Time.unscaledTime >= autoKeyNextCheck)
            {
                autoKeyNextCheck = Time.unscaledTime + (userActive ? AutoKeyActiveInterval : AutoKeyIdleInterval);
                if (autoKeyBaselineStale)
                {
                    // Timeline wrote values (playback, seeking): all of them are the new starting point.
                    autoKeyBaselineStale = false;
                    autoKeyBaseline.Clear();
                    ResetAutoRows();
                }

                CompareWithBaseline(userActive);
                PollMissingRows(userActive);
            }
            else if (autoKeyChangedFrame.Count > 0)
            {
                ComparePending(userActive);
            }
            else
            {
                return;
            }

            WriteDueKeys();
        }

        // Between full checks: only the rows that wait for their key, so a drag that pauses is
        // keyed on the next frame without comparing every row every frame.
        void ComparePending(bool userActive)
        {
            autoKeyDue.Clear();
            autoKeyDue.AddRange(autoKeyChangedFrame.Keys);
            foreach (Interpolable row in autoKeyDue)
            {
                object now = EditSignature(row);
                object before;
                if (autoKeyBaseline.TryGetValue(row, out before) && !SameValue(before, now))
                {
                    autoKeyBaseline[row] = now;
                    if (userActive && now != NotWatched && now != NotOverridden)
                    {
                        autoKeyChangedFrame[row] = Time.frameCount;
                    }
                }
            }
        }

        // Remember the frame in which each watched row last changed under the user's hands.
        void CompareWithBaseline(bool userActive)
        {
            bool everyRow = AutoKeyAddRows;
            foreach (Interpolable row in Native.GetAllInterpolables(true))
            {
                if (row.oci == null)
                {
                    continue;
                }

                // With "Add missing rows" every row type is watched, except those that change by themselves.
                bool listed = autoKeyRows.Contains(row.id);
                if (!listed && (!everyRow || IsDrifting(row.owner, row.id) || IsNeverAdded(row)))
                {
                    continue;
                }

                object now = EditSignature(row);
                object before;
                bool known = autoKeyBaseline.TryGetValue(row, out before);
                autoKeyBaseline[row] = now;
                if (!known)
                {
                    // A value that is a new object on every read can never look unchanged.
                    if (!listed && (now == NotWatched || !SameValue(now, EditSignature(row))))
                    {
                        MarkDrifting(row.owner, row.id);
                    }

                    continue;
                }

                if (SameValue(before, now))
                {
                    continue;
                }

                if (!listed && !userActive)
                {
                    MarkDrifting(row.owner, row.id);   // changed with nobody touching anything
                    continue;
                }

                bool keyable = now != NotWatched && now != NotOverridden && before != NotWatched;
                if (userActive && keyable)
                {
                    autoKeyChangedFrame[row] = Time.frameCount;
                }
            }
        }

        // Write the key of every row whose last change is at least one frame old.
        // A row that is still being dragged is written when the drag pauses or ends.
        void WriteDueKeys()
        {
            if (autoKeyChangedFrame.Count == 0)
            {
                return;
            }

            autoKeyDue.Clear();
            foreach (KeyValuePair<Interpolable, int> changed in autoKeyChangedFrame)
            {
                if (changed.Value < Time.frameCount)
                {
                    autoKeyDue.Add(changed.Key);
                }
            }

            int written = 0;
            float time = Native.playbackTime;
            float smallestDelay = float.MaxValue, largestDelay = 0f;
            foreach (Interpolable row in autoKeyDue)
            {
                autoKeyChangedFrame.Remove(row);
                try
                {
                    float at = AutoKeyTime(row, time);   // later for a row the Pad delays (KeyDelay.cs)
                    WriteKey(row, at);
                    written++;
                    smallestDelay = Mathf.Min(smallestDelay, at - time);
                    largestDelay = Mathf.Max(largestDelay, at - time);
                }
                catch (Exception e)
                {
                    Logger.LogWarning("Auto Key: " + Error(e));
                }
            }

            if (written > 0)
            {
                nextRefresh = 0;
                string added = autoRowsAdded > 0 ? " (" + autoRowsAdded + " new)" : "";
                autoRowsAdded = 0;
                Note("Auto Key: " + written + " row(s)" + added + " at " + time.ToString("0.###") + " s" + DelaySpread(smallestDelay, largestDelay));
            }
        }

        void WriteKey(Interpolable row, float time)
        {
            Timeline.Keyframe key = KeyAt(row, time);
            if (key != null)
            {
                key.value = row.GetValue();   // an existing key keeps its curve
                return;
            }

            if (row.keyframes.Count == 0)
            {
                // A row that Auto Key has just added: its first key, as the scene loader makes one.
                AnimationCurve first = AutoKeyCurve();
                row.keyframes.Add(time, new Timeline.Keyframe(row.GetValue(), row, first == null ? AnimationCurve.Linear(0f, 0f, 1f, 1f) : new AnimationCurve(first.keys)));
                return;
            }

            // Native insertion: current value, curve copied from the previous key;
            // then the curve chosen in the Auto Key menu, if any.
            Call("AddKeyframe", row, time);
            AnimationCurve curve = AutoKeyCurve();
            Timeline.Keyframe added = curve == null ? null : KeyAt(row, time);
            if (added != null)
            {
                added.curve = new AnimationCurve(curve.keys);
            }
        }

        // ---- what counts as "the value the user edits" ----------------------------------------

        object EditSignature(Interpolable row)
        {
            try
            {
                if (row.owner != "KKPE")
                {
                    return row.GetValue();
                }

                if (row.parameter == null)
                {
                    return NotWatched;
                }

                return row.id.StartsWith("bone", StringComparison.Ordinal) ? KkpeBoneOverride(row) : KkpeBlendOverride(row);
            }
            catch
            {
                return NotWatched;
            }
        }

        // parameter: HashedPair<BonesEditor, Transform>. BonesEditor._dirtyBones holds the overrides.
        static object KkpeBoneOverride(Interpolable row)
        {
            Transform bone = Get<Transform>(row.parameter, "value");
            if (bone == null)
            {
                return NotWatched;
            }

            IDictionary overrides = Get<IDictionary>(Get<object>(row.parameter, "key"), "_dirtyBones");
            object data = overrides[bone.gameObject];
            if (data == null)
            {
                return NotOverridden;
            }

            string component = row.id == "bonePos" ? "position" : row.id == "boneRot" ? "rotation" : "scale";
            object editable = Get<object>(data, component);
            return Get<bool>(editable, "hasValue") ? Get<object>(editable, "value") : NotOverridden;
        }

        // parameter: GroupParameter or IndividualParameter. BlendRenderer._dirtyBlends holds the overrides.
        object KkpeBlendOverride(Interpolable row)
        {
            object renderer = Get<object>(row.parameter, "blendRenderer");
            if (renderer == null)
            {
                return NotWatched;
            }

            IDictionary overrides = Get<IDictionary>(renderer, "_dirtyBlends");
            bool wholeRenderer = row.id == "groupBlendShape" || row.id == "groupBlendShapeLink";
            if (!wholeRenderer)
            {
                if (blendShapeName == null)
                {
                    blendShapeName = renderer.GetType().GetMethod("GetBlendShapeName", Flags);
                }

                string name = (string)blendShapeName.Invoke(renderer, new object[] { Get<int>(row.parameter, "index") });
                object data = name == null ? null : overrides[name];
                return data == null ? NotOverridden : (object)Get<float>(data, "weight");
            }

            if (overrides.Count == 0)
            {
                return NotOverridden;
            }

            // One number that changes whenever any override weight of the renderer changes.
            double signature = overrides.Count;
            int position = 0;
            foreach (DictionaryEntry entry in overrides)
            {
                signature += Get<float>(entry.Value, "weight") * (1 + position % 251);
                position++;
            }

            return signature;
        }

        // Camera and animation rows: never keyed by "Add missing rows" (AutoKeyRows.cs).
        readonly Dictionary<Interpolable, bool> neverAddedRows = new Dictionary<Interpolable, bool>();

        bool IsNeverAdded(Interpolable row)
        {
            bool never;
            if (!neverAddedRows.TryGetValue(row, out never))
            {
                string name = null;
                try
                {
                    name = row.name;
                }
                catch
                {
                }

                never = NeverAdded(row.owner, row.id, name);
                neverAddedRows[row] = never;
            }

            return never;
        }

        static bool SameValue(object a, object b)
        {
            if (ReferenceEquals(a, b))
            {
                return true;
            }

            if (a == null || b == null)
            {
                return false;
            }

            // Unity's == on these types already allows for rounding noise.
            if (a is Vector3 && b is Vector3)
            {
                return (Vector3)a == (Vector3)b;
            }

            if (a is Quaternion && b is Quaternion)
            {
                return (Quaternion)a == (Quaternion)b;
            }

            if (a is float && b is float)
            {
                return Mathf.Approximately((float)a, (float)b);
            }

            // Arrays (all weights of a mesh, ...) are new on every read: compare what they hold.
            Array first = a as Array, second = b as Array;
            if (first != null && second != null)
            {
                if (first.Length != second.Length)
                {
                    return false;
                }

                for (int i = 0; i < first.Length; i++)
                {
                    if (!SameValue(first.GetValue(i), second.GetValue(i)))
                    {
                        return false;
                    }
                }

                return true;
            }

            return a.Equals(b);
        }
    }
}
