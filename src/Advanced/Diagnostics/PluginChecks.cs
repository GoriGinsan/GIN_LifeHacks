using System;
using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace GinAdvanceHack
{
    public sealed partial class Plugin
    {
        public string RunIsolatedTests()
        {
            if (!IsIsolatedTest())
            {
                return "SKIP Advanced tests: isolated test-game only";
            }

            if (ui == null)
            {
                return "FAIL Advanced UI adapter not attached";
            }

            return ui.RunIsolatedTests() + "\n" + (Library == null ? "FAIL Expression library not attached" : Library.RunIsolatedTests());
        }

        public string PreparePreview(int view)
        {
            if (!IsIsolatedTest() || ui == null)
            {
                return "SKIP Advanced preview: isolated adapter required";
            }

            object main = ReflectionAccess.Get(ReflectionAccess.Type("HSPE.MainWindow"), "_self");
            object pose = ReflectionAccess.Get(main, "_poseTarget");
            if (pose == null)
            {
                return "FAIL Advanced preview: select the fixture character first";
            }

            Behaviour component = pose as Behaviour;
            if (component != null)
            {
                component.enabled = true;
            }

            ReflectionAccess.Set(ReflectionAccess.Type("HSPE.PoseController"), "_drawAdvancedMode", true);
            ReflectionAccess.Set(main, "_advancedModeRect", new Rect(160, 110, 1000, 700));
            object module = ReflectionAccess.Get(pose, view == 1 ? "_bonesEditor" : "_blendShapesEditor");
            if (module == null)
            {
                return "FAIL Advanced preview: target module missing";
            }

            ReflectionAccess.Call(pose, "EnableModule", module);
            if (view == 1)
            {
                Component target = pose as Component;
                foreach (Transform child in target.GetComponentsInChildren<Transform>(true))
                {
                    if (child.name == "cf_J_hitomi_tx_L")
                    {
                        ReflectionAccess.Call(module, "ChangeBoneTarget", child);
                        break;
                    }
                }
            }
            else
            {
                object face = ReflectionAccess.Get(module, "_faceRenderer");
                if (face != null)
                {
                    ReflectionAccess.Set(module, "_skinnedMeshTarget", face);
                }
            }

            if (Library != null)
            {
                // Isolated preview only. Production opening is the adjacent native-style button.
                FieldInfo visible = typeof(ExpressionLibrary).GetField("visible", BindingFlags.NonPublic | BindingFlags.Instance);
                if (visible != null)
                {
                    visible.SetValue(Library, view == 2);
                }

                if (view == 2)
                {
                    string preview = Library.PrepareIsolatedPreview();
                    Report(preview);
                    Library.Show();
                }
            }

            return "PASS Advanced preview prepared: " + view;
        }

        public string SetEnabledForIsolatedTest(bool value)
        {
            if (!IsIsolatedTest())
            {
                return "SKIP Advanced enable test: marked isolated game only";
            }

            Enabled.Value = value;
            if (!value)
            {
                foreach (MethodBase method in Harmony.GetAllPatchedMethods())
                {
                    Patches patches = Harmony.GetPatchInfo(method);
                    if (patches != null && patches.Owners.Contains(Guid + ".ui"))
                    {
                        return "FAIL Advanced OFF: owned patch retained on " + method.Name;
                    }
                }

                return ui == null && Library == null && inputShield == null ? "PASS Advanced OFF: all owned patches, cloned style and input shields removed; native data/UI retained" : "FAIL Advanced OFF: extension instance retained";
            }

            retry = 0;
            failed = false;
            return "WAIT Advanced ON: await Ready before verifying";
        }

        internal static bool IsIsolatedTest()
        {
            return Array.IndexOf(Environment.GetCommandLineArgs(), "-ginLifeHacksVerify") >= 0 && new DirectoryInfo(Paths.GameRootPath).Name.Equals("test-game", StringComparison.OrdinalIgnoreCase) && File.Exists(Path.Combine(Paths.GameRootPath, ".kk-workshop-test"));
        }
    }
}
