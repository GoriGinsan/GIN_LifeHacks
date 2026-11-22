using System;
using System.Collections.Generic;
using Studio;
using Timeline;
using UnityEngine;
using Native = Timeline.Timeline;

namespace GinTimelineHack
{
    // Services for GIN Pad life hack (called through reflection): keying rows at the playhead.
    //
    // "Face rows" are a character's KKPE rows that make up one expression:
    // Blend Shape rows of renderers on the head, and the pupil bones.
    // The tongue bones are included only on request, because they are usually keyed on their own.
    // "Named rows" are an object's rows of given bones, guide objects or renderers (by transform
    // name), whatever the row type.
    public sealed partial class Plugin
    {
        const string HeadPathMarker = "ct_head";
        const string PupilBonePrefix = "cf_J_hitomi_tx";
        const string TongueBonePrefix = "cf_j_tang";
        const int FaceKeyDelayFrames = 2;   // KKPE applies an edit to the mesh in the next LateUpdate
        const float ShapeInUse = 0.5f;      // weight above which a keyed Blend Shape counts as used

        // One pending request: which rows of which object, keyed a few frames from now.
        ObjectCtrlInfo faceKeyTarget;
        Predicate<Interpolable> faceKeyRows;
        string faceKeyWhat;
        int faceKeyFrame;

        // Write a key at the playhead on every face row of the target, two frames from now.
        // Returns a short status for the caller's own feedback line.
        public static string KeyFaceRows(ObjectCtrlInfo target, bool includeTongue)
        {
            if (Self == null || !Self.attached)
            {
                return "GIN Timeline life hack is not attached to Timeline";
            }

            if (Native.isPlaying)
            {
                return "Stop playback before keying the face";
            }

            int rows = Self.FaceRows(target, includeTongue).Count;
            if (rows == 0)
            {
#if CHAR_SETUP
                return "No face rows on this character - press Char Setup first";
#else
                return "No face rows on this character - add them to Timeline first";
#endif
            }

            Self.RequestKeys(target, delegate (Interpolable row) { return row.owner == "KKPE" && IsFaceRow(row, includeTongue); }, "Face");
            return "Face keyed: " + rows + " rows at " + Native.playbackTime.ToString("0.###") + " s";
        }

        // Write a key at the playhead on every row of the target whose bone, guide object or
        // renderer has one of these names (comma separated), two frames from now.
        public static string KeyNamedRows(ObjectCtrlInfo target, string names)
        {
            if (Self == null || !Self.attached)
            {
                return "GIN Timeline life hack is not attached to Timeline";
            }

            if (Native.isPlaying)
            {
                return "Stop playback before keying";
            }

            List<string> wanted = new List<string>();
            foreach (string name in names.Split(','))
            {
                if (name.Trim().Length > 0)
                {
                    wanted.Add(name.Trim());
                }
            }

            Predicate<Interpolable> named = delegate (Interpolable row) { return wanted.Contains(RowLeaf(row)); };
            int rows = RowsOf(target, named).Count;
            if (rows == 0)
            {
                return "No Timeline rows for " + names;
            }

            Self.RequestKeys(target, named, "Rows");
            return "Keyed: " + rows + " rows at " + Native.playbackTime.ToString("0.###") + " s";
        }

        void RequestKeys(ObjectCtrlInfo target, Predicate<Interpolable> rows, string what)
        {
            faceKeyTarget = target;
            faceKeyRows = rows;
            faceKeyWhat = what;
            faceKeyFrame = Time.frameCount + FaceKeyDelayFrames;
        }

        // The name a row is known by: its bone, guide object or renderer ("" for the object itself).
        static string RowLeaf(Interpolable row)
        {
            try
            {
                GuideObject guide = row.parameter as GuideObject;
                if (guide != null)
                {
                    return guide.transformTarget.name;
                }

                if (row.owner == "KKPE" && row.id.StartsWith("bone", StringComparison.Ordinal))
                {
                    Transform bone = Get<Transform>(row.parameter, "value");
                    return bone == null ? "" : bone.name;
                }

                if (row.owner == "KKPE" && IsBlendRow(row))
                {
                    string path = Get<string>(row.parameter, "rendererPath");
                    return path.Substring(path.LastIndexOf('/') + 1);
                }
            }
            catch
            {
            }

            return "";
        }

        // Called every frame from Update.
        void PollFaceKey()
        {
            if (faceKeyTarget == null || Time.frameCount < faceKeyFrame)
            {
                return;
            }

            ObjectCtrlInfo target = faceKeyTarget;
            faceKeyTarget = null;
            if (Native.isPlaying)
            {
                return;
            }

            int written = 0;
            float time = Native.playbackTime;
            foreach (Interpolable row in RowsOf(target, faceKeyRows))
            {
                try
                {
                    WriteKey(row, time);
                    written++;
                }
                catch (Exception e)
                {
                    Logger.LogWarning("Face key: " + Error(e));
                }
            }

            // These keys are not a new manual edit for Auto Key.
            autoKeyBaselineStale = true;
            nextRefresh = 0;
            Note(faceKeyWhat + " keyed: " + written + " rows at " + time.ToString("0.###") + " s");
        }

        List<Interpolable> FaceRows(ObjectCtrlInfo target, bool includeTongue)
        {
            return RowsOf(target, delegate (Interpolable row) { return row.owner == "KKPE" && IsFaceRow(row, includeTongue); });
        }

        static List<Interpolable> RowsOf(ObjectCtrlInfo target, Predicate<Interpolable> which)
        {
            List<Interpolable> rows = new List<Interpolable>();
            foreach (Interpolable row in Native.GetAllInterpolables(true))
            {
                if (row.oci == target && row.parameter != null && which(row))
                {
                    rows.Add(row);
                }
            }

            return rows;
        }

        static bool IsFaceRow(Interpolable row, bool includeTongue)
        {
            try
            {
                if (row.id.StartsWith("bone", StringComparison.Ordinal))
                {
                    Transform bone = Get<Transform>(row.parameter, "value");
                    if (bone == null)
                    {
                        return false;
                    }

                    return bone.name.StartsWith(PupilBonePrefix, StringComparison.Ordinal)
                        || (includeTongue && bone.name.StartsWith(TongueBonePrefix, StringComparison.Ordinal));
                }

                return IsBlendRow(row) && Get<string>(row.parameter, "rendererPath").IndexOf(HeadPathMarker, StringComparison.Ordinal) >= 0;
            }
            catch
            {
                return false;
            }
        }

        static bool IsBlendRow(Interpolable row)
        {
            return row.id == "lastBlendShape" || row.id == "groupBlendShape" || row.id == "groupBlendShapeLink" || row.id == "groupBlendShapeNameLink";
        }

        // Which Blend Shapes the target's Timeline keys actually use: the shapes that are above
        // ShapeInUse in at least one key. A row that was only registered (all keys 0) uses nothing.
        // Result: renderer name ("cf_O_face") -> one flag per Blend Shape index.
        // A renderer without any Blend Shape row is absent from the result.
        public static Dictionary<string, bool[]> FaceShapeUsage(ObjectCtrlInfo target)
        {
            Dictionary<string, bool[]> usage = new Dictionary<string, bool[]>();
            if (Self == null || !Self.attached)
            {
                return usage;
            }

            foreach (Interpolable row in Native.GetAllInterpolables(false))
            {
                if (row.oci != target || row.owner != "KKPE" || row.parameter == null || !IsBlendRow(row))
                {
                    continue;
                }

                try
                {
                    string path = Get<string>(row.parameter, "rendererPath");
                    string renderer = path.Substring(path.LastIndexOf('/') + 1);
                    bool wholeRenderer = row.id == "groupBlendShape" || row.id == "groupBlendShapeLink";
                    foreach (Timeline.Keyframe key in row.keyframes.Values)
                    {
                        if (wholeRenderer)
                        {
                            float[] weights = (float[])key.value;
                            bool[] flags = UsageFlags(usage, renderer, weights.Length);
                            for (int i = 0; i < weights.Length; i++)
                            {
                                flags[i] |= weights[i] > ShapeInUse;
                            }
                        }
                        else
                        {
                            int index = Get<int>(row.parameter, "index");
                            UsageFlags(usage, renderer, index + 1)[index] |= key.value is float && (float)key.value > ShapeInUse;
                        }
                    }
                }
                catch (Exception e)
                {
                    Self.Logger.LogDebug("Face usage: " + Error(e));
                }
            }

            return usage;
        }

        static bool[] UsageFlags(Dictionary<string, bool[]> usage, string renderer, int length)
        {
            bool[] flags;
            if (!usage.TryGetValue(renderer, out flags))
            {
                flags = new bool[length];
                usage[renderer] = flags;
            }
            else if (flags.Length < length)
            {
                Array.Resize(ref flags, length);
                usage[renderer] = flags;
            }

            return flags;
        }
    }
}
