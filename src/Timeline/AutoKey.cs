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
    // a key is added. Rows are never created; the S key keeps working as before.
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
        }

        void ResetAutoKey()
        {
            autoKey = false;
            autoKeyBaselineStale = true;
            autoKeyBaseline.Clear();
            autoKeyChangedFrame.Clear();
            autoKeyButton = autoKeyStripe = autoKeyDot = null;
            autoKeyArrow = autoKeyMenu = null;
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
            Note(on ? "AUTO KEY ON - moving a registered row writes its key at the playhead" : "Auto Key off - S applies current values to selected keys");
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

            bool valuesSettled = !Native.isPlaying && Time.frameCount > nativeWroteValuesFrame + 1 && Native.playbackTime == autoKeyTime;
            if (!valuesSettled)
            {
                autoKeyTime = Native.playbackTime;
                autoKeyBaselineStale = true;
                autoKeyChangedFrame.Clear();
                return;
            }

            bool userActive = Time.unscaledTime - autoKeyLastInput <= AutoKeyInputGrace;
            if (!autoKeyBaselineStale && !userActive && autoKeyChangedFrame.Count == 0 && Time.unscaledTime < autoKeyNextCheck)
            {
                return;
            }

            autoKeyNextCheck = Time.unscaledTime + AutoKeyIdleInterval;
            if (autoKeyBaselineStale)
            {
                autoKeyBaselineStale = false;
                autoKeyBaseline.Clear();
            }

            CompareWithBaseline(userActive);
            WriteDueKeys();
        }

        // Remember the frame in which each watched row last changed under the user's hands.
        void CompareWithBaseline(bool userActive)
        {
            foreach (Interpolable row in Native.GetAllInterpolables(true))
            {
                if (row.oci == null || !autoKeyRows.Contains(row.id))
                {
                    continue;
                }

                object now = EditSignature(row);
                object before;
                bool known = autoKeyBaseline.TryGetValue(row, out before);
                autoKeyBaseline[row] = now;
                if (!known || SameValue(before, now))
                {
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
            foreach (Interpolable row in autoKeyDue)
            {
                autoKeyChangedFrame.Remove(row);
                try
                {
                    WriteKey(row, time);
                    written++;
                }
                catch (Exception e)
                {
                    Logger.LogWarning("Auto Key: " + Error(e));
                }
            }

            if (written > 0)
            {
                nextRefresh = 0;
                Note("Auto Key: " + written + " row(s) at " + time.ToString("0.###") + " s");
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

            return a.Equals(b);
        }
    }
}
