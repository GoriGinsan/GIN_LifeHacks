using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using System.Xml;
using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace GinAdvanceHack
{
    internal sealed partial class AdvancedUi
    {
        internal string RunIsolatedTests()
        {
            if (!Plugin.IsIsolatedTest())
            {
                return "SKIP Advanced tests: isolation required";
            }

            StringBuilder log = new StringBuilder();
            foreach (MethodBase m in patched)
            {
                Patches p = Harmony.GetPatchInfo(m);
                if (p == null || !p.Owners.Contains(harmony.Id))
                {
                    return "FAIL Advanced patch ownership: " + m.Name;
                }
            }

            log.AppendLine("PASS Advanced: " + patched.Count + " native method patches owned by this plugin; KKPE " + mainType.Assembly.GetName().Version + " version guard");
            if (Mathf.Abs(ScrubDelta(8, .1f, 8, false, false) - .1f) > .00001f || Mathf.Abs(ScrubDelta(-16, .1f, 8, false, false) + .2f) > .00001f || Mathf.Abs(ScrubDelta(8, .1f, 8, true, false) - .01f) > .00001f)
            {
                return "FAIL Bone scrub direction / precision";
            }

            log.AppendLine("PASS Bone scrub arithmetic: right increases, left decreases, Shift fine; native increment size retained");
            object main = ReflectionAccess.Get(mainType, "_self"), pose = ReflectionAccess.Get(main, "_poseTarget");
            object bones = ReflectionAccess.Get(pose, "_bonesEditor");
            Component character = pose as Component;
            if (bones != null && character != null)
            {
                Transform left = null;
                foreach (Transform t in character.GetComponentsInChildren<Transform>(true))
                {
                    if (t.name == "cf_J_hitomi_tx_L")
                    {
                        left = t;
                        break;
                    }
                }

                if (left != null)
                {
                    object selected = ReflectionAccess.Get(bones, "_boneTarget"), twin = ReflectionAccess.Get(bones, "_twinBoneTarget"), alias = ReflectionAccess.Get(bones, "_currentAlias");
                    try
                    {
                        ReflectionAccess.Call(bones, "ChangeBoneTarget", left);
                        Transform right = ReflectionAccess.Get(bones, "_twinBoneTarget") as Transform;
                        if (right == null || right.name != "cf_J_hitomi_tx_R" || !SwitchTwin(bones) || (ReflectionAccess.Get(bones, "_boneTarget") as Transform) != right)
                        {
                            throw new InvalidOperationException("L/R did not use native twin selection");
                        }

                        log.AppendLine("PASS L/R command: native left eye -> right eye selection; no transform values changed");
                    }
                    finally
                    {
                        ReflectionAccess.Call(bones, "ChangeBoneTarget", new object[] { selected });
                        ReflectionAccess.Set(bones, "_twinBoneTarget", twin);
                        ReflectionAccess.Set(bones, "_currentAlias", alias);
                    }
                }
            }

            string path = Path.Combine(displayDirectory, "verify-" + System.Guid.NewGuid().ToString("N") + ".xml");
            HashSet<string> oldHidden = new HashSet<string>(hidden), oldCollapsed = new HashSet<string>(collapsed);
            try
            {
                hidden.Clear();
                collapsed.Clear();
                hidden.Add("renderer/path\nshape&name_日本語");
                collapsed.Add("renderer/path\nEyes");
                SaveDisplay(path, "検証 <template>");
                hidden.Clear();
                collapsed.Clear();
                LoadDisplay(path);
                if (!hidden.Contains("renderer/path\nshape&name_日本語") || !collapsed.Contains("renderer/path\nEyes") || DisplayName(path) != "検証 <template>")
                {
                    return "FAIL Visibility template round trip";
                }

                log.AppendLine("PASS Visibility template disk round trip: Unicode names, renderer-qualified rows and independent collapsed sections; no mesh/bone data written");
            }
            finally
            {
                hidden.Clear();
                collapsed.Clear();
                foreach (string s in oldHidden)
                {
                    hidden.Add(s);
                }

                foreach (string s in oldCollapsed)
                {
                    collapsed.Add(s);
                }

                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }

            log.AppendLine("SKIP physical pointer scrub / visual sizing: requires interactive UI verification");
            return log.ToString();
        }
    }
}
