using System;
using System.Collections.Generic;
using System.Globalization;
using Studio;
using Timeline;
using UnityEngine;
using Native = Timeline.Timeline;

namespace GinTimelineHack
{
    // Delay Key for GIN Pad life hack (called through reflection).
    //
    // The Pad gives every body part its own delay: how many seconds after the playhead that
    // part's key is written. Hips first, then the shoulders, the head a little later, the left
    // shoulder not quite with the right one: the overlap that keeps a pose change from moving
    // every part in the same instant.
    //
    // Only edits made in the Pad are delayed. While it edits, the Pad tells which transforms it
    // changes and by how much their keys are delayed (NotePadEdit); Auto Key then writes a row
    // of such a transform at playhead + delay instead of at the playhead. The note is only
    // trusted for a short moment, so an edit made elsewhere (Studio's gizmo, KKPE) a little later
    // is keyed at the playhead as before.
    //
    // A key already at the delayed time is updated (as at the playhead). Keys between the
    // playhead and the delayed time are left alone. A delayed time beyond the animation's end
    // is moved back to the end. Writing a key does not make Timeline apply its values, so the
    // pose on screen stays as the user made it until the playhead moves.
    public sealed partial class Plugin
    {
        const float PadNoteSeconds = 1f;   // how long a note of the Pad counts for Auto Key

        sealed class PadNote
        {
            internal ObjectCtrlInfo Target;
            internal float Delay, Time;
        }

        readonly Dictionary<string, PadNote> padNotes = new Dictionary<string, PadNote>();

        // The Pad edited these transforms of the target. "delays": "name=seconds" pairs, comma
        // separated (the names Timeline rows are known by, see RowLeaf).
        public static void NotePadEdit(ObjectCtrlInfo target, string delays)
        {
            if (Self == null || target == null)
            {
                return;
            }

            float now = Time.unscaledTime;
            foreach (KeyValuePair<string, float> pair in ParseDelays(delays))
            {
                PadNote note;
                if (!Self.padNotes.TryGetValue(pair.Key, out note))
                {
                    note = new PadNote();
                    Self.padNotes[pair.Key] = note;
                }

                note.Target = target;
                note.Delay = pair.Value;
                note.Time = now;
            }
        }

        // "a=0.1, b, c=0.25" -> a: 0.1, b: 0, c: 0.25. Delays below 0 count as 0.
        internal static Dictionary<string, float> ParseDelays(string text)
        {
            Dictionary<string, float> delays = new Dictionary<string, float>();
            if (string.IsNullOrEmpty(text))
            {
                return delays;
            }

            foreach (string item in text.Split(','))
            {
                string entry = item.Trim();
                if (entry.Length == 0)
                {
                    continue;
                }

                float delay = 0f;
                int equals = entry.IndexOf('=');
                if (equals >= 0)
                {
                    float.TryParse(entry.Substring(equals + 1).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out delay);
                    entry = entry.Substring(0, equals).Trim();
                }

                if (entry.Length > 0)
                {
                    delays[entry] = Mathf.Max(0f, delay);
                }
            }

            return delays;
        }

        // Where Auto Key writes this row's key: playhead + the Pad's delay for it, if the Pad
        // edited the row's transform a moment ago; otherwise the playhead.
        float AutoKeyTime(Interpolable row, float playhead)
        {
            if (padNotes.Count == 0)
            {
                return playhead;
            }

            PadNote note;
            if (!padNotes.TryGetValue(RowLeaf(row), out note) || note.Target != row.oci || Time.unscaledTime - note.Time > PadNoteSeconds)
            {
                return playhead;
            }

            return DelayedTime(playhead, note.Delay);
        }

        // playhead + delay, kept inside the animation.
        internal static float DelayedTime(float playhead, float delay)
        {
            if (delay <= 0f)
            {
                return playhead;
            }

            float end = Native.duration;
            float time = playhead + delay;
            return end > 0f ? Mathf.Min(time, Mathf.Max(playhead, end)) : time;
        }

        // " (+0.052 .. +0.105 s)": the spread of the delays used, for the status line; "" without delays.
        static string DelaySpread(float smallest, float largest)
        {
            if (largest <= 0f)
            {
                return "";
            }

            return smallest == largest
                ? " +" + largest.ToString("0.000", CultureInfo.InvariantCulture) + " s"
                : " +" + smallest.ToString("0.000", CultureInfo.InvariantCulture) + " .. +" + largest.ToString("0.000", CultureInfo.InvariantCulture) + " s";
        }
    }
}
