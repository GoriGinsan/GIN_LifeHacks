using System;
using System.Collections.Generic;
using System.Reflection;
using Studio;
using UnityEngine;

namespace GinPadHack
{
    // GIN Timeline life hack is optional and never referenced at compile time.
    // It keys rows at the playhead (the whole face, or the rows of named bones and renderers) and
    // tells which Blend Shapes the Timeline keys use. A key at the playhead replaces a key that is
    // already there.
    //
    // Playback (play / pause, the playhead, the length) is Timeline's own and only needs Timeline.
    internal static class TimelineBridge
    {
        const string NeedsTimelineHack = "Keying needs GIN Timeline life hack 1.1.0 or later";

        static MethodInfo keyFace, keyNamed, usage;
        static bool found;

        static void Find()
        {
            if (found)
            {
                return;
            }

            Type timeline = Reflect.Type("GinTimelineHack.Plugin");
            if (timeline == null)
            {
                return;   // not loaded (yet): look again next time
            }

            found = true;
            BindingFlags flags = BindingFlags.Public | BindingFlags.Static;
            keyFace = timeline.GetMethod("KeyFaceRows", flags, null, new Type[] { typeof(ObjectCtrlInfo), typeof(bool) }, null);
            keyNamed = timeline.GetMethod("KeyNamedRows", flags, null, new Type[] { typeof(ObjectCtrlInfo), typeof(string) }, null);
            usage = timeline.GetMethod("FaceShapeUsage", flags, null, new Type[] { typeof(ObjectCtrlInfo) }, null);
        }

        // Key the face rows (face, eyebrow and teeth Blend Shapes plus the pupils) at the playhead.
        // Returns a short status line.
        internal static string KeyFace(ObjectCtrlInfo character)
        {
            Find();
            if (keyFace == null)
            {
                return NeedsTimelineHack;
            }

            return (string)keyFace.Invoke(null, new object[] { character, false });
        }

        // Key every row of the object whose bone, guide object or renderer has one of these
        // transform names. Returns a short status line.
        internal static string KeyNamed(ObjectCtrlInfo owner, ICollection<string> names)
        {
            Find();
            if (keyNamed == null)
            {
                return NeedsTimelineHack;
            }

            if (names.Count == 0)
            {
                return "Nothing selected to key";
            }

            string[] list = new string[names.Count];
            names.CopyTo(list, 0);
            return (string)keyNamed.Invoke(null, new object[] { owner, string.Join(",", list) });
        }

        // ---- Timeline's own playback (Timeline.Timeline; GIN Timeline life hack is not needed) ----

        static PropertyInfo playing, playhead, length;
        static MethodInfo play, pause, seek;
        static FieldInfo self, frameRate;
        static bool playbackFound;

        // True when Timeline is installed and its playback can be driven.
        internal static bool HasPlayback
        {
            get
            {
                if (!playbackFound)
                {
                    Type native = Reflect.Type("Timeline.Timeline");
                    if (native == null)
                    {
                        return false;   // not loaded (yet): look again next time
                    }

                    playbackFound = true;
                    BindingFlags flags = BindingFlags.Public | BindingFlags.Static;
                    playing = native.GetProperty("isPlaying", flags);
                    playhead = native.GetProperty("playbackTime", flags);
                    length = native.GetProperty("duration", flags);
                    play = native.GetMethod("Play", flags, null, Type.EmptyTypes, null);
                    pause = native.GetMethod("Pause", flags, null, Type.EmptyTypes, null);
                    seek = native.GetMethod("Seek", flags, null, new Type[] { typeof(float) }, null);
                    self = native.GetField("_self", BindingFlags.NonPublic | BindingFlags.Static);
                    frameRate = native.GetField("_desiredFrameRate", BindingFlags.NonPublic | BindingFlags.Instance);
                }

                return playing != null && playhead != null && length != null && play != null && pause != null && seek != null && self != null && self.GetValue(null) != null;
            }
        }

        internal static bool IsPlaying
        {
            get
            {
                return (bool)playing.GetValue(null, null);
            }
        }

        internal static float PlaybackTime
        {
            get
            {
                return (float)playhead.GetValue(null, null);
            }
        }

        internal static float Duration
        {
            get
            {
                return (float)length.GetValue(null, null);
            }
        }

        // Timeline's frame rate (its "60" field); 60 when it cannot be read.
        internal static int FrameRate
        {
            get
            {
                object rate = frameRate == null ? null : frameRate.GetValue(self.GetValue(null));
                return rate is int && (int)rate > 0 ? (int)rate : 60;
            }
        }

        internal static void TogglePlay()
        {
            (IsPlaying ? pause : play).Invoke(null, null);
        }

        internal static void Pause()
        {
            if (IsPlaying)
            {
                pause.Invoke(null, null);
            }
        }

        internal static void Seek(float time)
        {
            seek.Invoke(null, new object[] { Mathf.Clamp(time, 0f, Duration) });
        }

        // Renderer name -> used flag per Blend Shape index; empty when Timeline data is unavailable.
        internal static Dictionary<string, bool[]> Usage(ObjectCtrlInfo character)
        {
            Find();
            if (usage == null)
            {
                return new Dictionary<string, bool[]>();
            }

            return (Dictionary<string, bool[]>)usage.Invoke(null, new object[] { character });
        }
    }
}
