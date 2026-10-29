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

namespace GinTimelineHack
{
    public sealed partial class Plugin
    {
        public bool CommitCurrentValues()
        {
            if (!attached)
            {
                return false;
            }

            List<Key> keys = ReadSelection();
            if (keys.Count == 0)
            {
                Note("Select keyframes first");
                return false;
            }

            Call("UseCurrentValue");
            Note("Current values applied: " + keys.Count + " keys");
            return true;
        }

        internal List<Key> ReadSelection()
        {
            return new List<Key>(Native.GetSelectedKeyframes());
        }

        internal void Select(List<Key> keys)
        {
            Call("SelectKeyframes", (IEnumerable<Key>)keys);
        }

        public bool CaptureAnchor()
        {
            List<Key> selected = ReadSelection();
            if (selected.Count == 0)
            {
                Note("Select a starting key, then click Anchor");
                return false;
            }

            Key first = selected[0];
            foreach (Key k in selected)
            {
                if (k.Key < first.Key)
                {
                    first = k;
                }
            }

            anchor = first;
            range = lastPhase = null;
            Note("Anchor: " + first.Key.ToString("0.###") + " s");
            return true;
        }

        static bool Exists(Key k)
        {
            Timeline.Keyframe v;
            return k.Value != null && k.Value.parent.keyframes.TryGetValue(k.Key, out v) && Object.ReferenceEquals(v, k.Value);
        }

        static bool Same(List<Key> a, List<Key> b)
        {
            if (a == null || b == null || a.Count != b.Count)
            {
                return false;
            }

            foreach (Key k in a)
            {
                if (!b.Contains(k))
                {
                    return false;
                }
            }

            return true;
        }

        public bool SelectAlternating(bool odd)
        {
            List<Key> now = ReadSelection();
            if (now.Count == 0)
            {
                Note("Box-select keys first");
                return false;
            }

            if (!anchor.HasValue)
            {
                Key first = now[0];
                foreach (Key k in now)
                {
                    if (k.Key < first.Key)
                    {
                        first = k;
                    }
                }

                anchor = first;
            }

            if (!Exists(anchor.Value))
            {
                Note("Anchor moved or deleted; select it and press Anchor again");
                return false;
            }

            if (range == null || !Same(now, lastPhase))
            {
                range = new List<Key>(now);
            }

            HashSet<Interpolable> tracks = new HashSet<Interpolable>();
            foreach (Key k in range)
            {
                if (Exists(k))
                {
                    tracks.Add(k.Value.parent);
                }
            }

            List<Key> selected = new List<Key>();
            foreach (Interpolable track in tracks)
            {
                int n = 0;
                foreach (Key k in track.keyframes)
                {
                    if (k.Key < anchor.Value.Key)
                    {
                        continue;
                    }

                    n++;
                    if (((n % 2 == 1) == odd) && range.Contains(k))
                    {
                        selected.Add(k);
                    }
                }
            }

            if (selected.Count == 0)
            {
                Note("No matching keys; selection unchanged");
                return false;
            }

            Select(selected);
            lastPhase = ReadSelection();
            Note((odd ? "1 / 3 / 5: " : "2 / 4 / 6: ") + selected.Count + " keys");
            return true;
        }
    }
}
