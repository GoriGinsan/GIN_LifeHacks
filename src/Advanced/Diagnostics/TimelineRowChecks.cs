using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace GinAdvanceHack
{
    // Guarded regression checks for the Blend Shapes "Timeline 0:00" buttons.
    // Needs the fixture character selected and an empty Timeline; never runs during normal use.
    public sealed partial class Plugin
    {
        public string RunTimelineRowTests()
        {
            if (!IsIsolatedTest())
            {
                return "SKIP Timeline row tests: isolation required";
            }

            return ui == null ? "FAIL Advanced UI adapter not attached" : ui.RunTimelineRowTests();
        }
    }

    internal sealed partial class AdvancedUi
    {
        internal string RunTimelineRowTests()
        {
            StringBuilder log = new StringBuilder();
            object main = ReflectionAccess.Get(ReflectionAccess.Type("HSPE.MainWindow"), "_self");
            object editor = ReflectionAccess.Get(ReflectionAccess.Get(main, "_poseTarget"), "_blendShapesEditor");
            if (editor == null)
            {
                return "FAIL Timeline rows: select the fixture character first";
            }

            object oldTarget = ReflectionAccess.Get(editor, "_skinnedMeshTarget");
            object oldSearch = ReflectionAccess.Get(editor, "_search");
            List<string> oldHidden = new List<string>(hidden);
            try
            {
                object tooth = Renderer(editor, "cf_O_tooth"), face = Renderer(editor, "cf_O_face");
                if (tooth == null || face == null)
                {
                    return "FAIL Timeline rows: fixture renderers missing";
                }

                // One row per Blend Shape for an ordinary renderer.
                ReflectionAccess.Set(editor, "_skinnedMeshTarget", tooth);
                int shapes = ((SkinnedMeshRenderer)ReflectionAccess.Get(tooth, "_renderer")).sharedMesh.blendShapeCount;
                RegisterBlendShapeRows(editor, false);
                Expect(log, timelineMessage, shapes + " added / 0 existing", "Key all registers every Blend Shape of the renderer");

                // Filtered: Search text and hidden rows limit what is offered.
                ReflectionAccess.Set(editor, "_search", "_op");
                List<string> listed = new List<string>();
                foreach (KeyValuePair<string, int> shape in (IEnumerable<KeyValuePair<string, int>>)ReflectionAccess.Get(tooth, "_blendIndics"))
                {
                    if (shape.Key.IndexOf("_op", StringComparison.CurrentCultureIgnoreCase) >= 0)
                    {
                        listed.Add(shape.Key);
                    }
                }

                RegisterBlendShapeRows(editor, true);
                Expect(log, timelineMessage, "0 added / " + listed.Count + " existing", "Key filtered follows the Search box");
                hidden.Add(RendererKey(editor) + "\n" + listed[0]);
                RegisterBlendShapeRows(editor, true);
                Expect(log, timelineMessage, "0 added / " + (listed.Count - 1) + " existing", "Key filtered skips hidden rows");

                // The face renderer becomes one linked group row, with or without a filter.
                ReflectionAccess.Set(editor, "_skinnedMeshTarget", face);
                RegisterBlendShapeRows(editor, true);
                Expect(log, timelineMessage, "1 added / 0 existing", "face renderer registers as one group row");
                RegisterBlendShapeRows(editor, false);
                Expect(log, timelineMessage, "0 added / 1 existing", "repeat registration adds nothing");
            }
            catch (Exception e)
            {
                log.AppendLine("FAIL Timeline rows: " + e);
            }
            finally
            {
                ReflectionAccess.Set(editor, "_skinnedMeshTarget", oldTarget);
                ReflectionAccess.Set(editor, "_search", oldSearch);
                hidden.Clear();
                foreach (string key in oldHidden)
                {
                    hidden.Add(key);
                }
            }

            return log.ToString().TrimEnd();
        }

        private static object Renderer(object editor, string name)
        {
            foreach (KeyValuePair<string, object> entry in ReflectionEntries(ReflectionAccess.Get(editor, "_blendRenderers")))
            {
                if (entry.Key.EndsWith("/" + name, StringComparison.Ordinal))
                {
                    return entry.Value;
                }
            }

            return null;
        }

        // KKPE's renderer dictionary has a private value type; read it without naming that type.
        private static IEnumerable<KeyValuePair<string, object>> ReflectionEntries(object dictionary)
        {
            foreach (System.Collections.DictionaryEntry entry in (System.Collections.IDictionary)dictionary)
            {
                yield return new KeyValuePair<string, object>((string)entry.Key, entry.Value);
            }
        }

        private static void Expect(StringBuilder log, string actual, string expected, string what)
        {
            bool pass = actual != null && actual.IndexOf(expected, StringComparison.Ordinal) >= 0;
            log.AppendLine((pass ? "PASS " : "FAIL ") + "Timeline 0:00: " + what + (pass ? "" : " - expected \"" + expected + "\", got \"" + actual + "\""));
        }
    }
}
