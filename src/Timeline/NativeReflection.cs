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
        void Patch(string name, string pre, string post, string final)
        {
            HarmonyMethod p = H(pre), q = H(post), f = H(final);
            if (p != null)
            {
                p.priority = Priority.First;
            }

            if (q != null)
            {
                q.priority = Priority.Last;
            }

            harmony.Patch(Method(name), p, q, null, f, null);
        }

        static HarmonyMethod H(string name)
        {
            return name == null ? null : new HarmonyMethod(typeof(Plugin).GetMethod(name, StaticFlags));
        }

        internal static MethodInfo Method(string name, params Type[] types)
        {
            MethodInfo result = types.Length == 0 ? typeof(Native).GetMethod(name, Flags) : typeof(Native).GetMethod(name, Flags, null, types, null);
            if (result == null)
            {
                throw new MissingMethodException("Timeline", name);
            }

            return result;
        }

        internal object Call(string name, params object[] args)
        {
            MethodInfo chosen = null;
            foreach (MethodInfo m in typeof(Native).GetMethods(Flags))
            {
                if (m.Name != name || m.GetParameters().Length != args.Length)
                {
                    continue;
                }

                bool ok = true;
                ParameterInfo[] p = m.GetParameters();
                for (int i = 0; i < args.Length; i++)
                {
                    if (args[i] != null && !p[i].ParameterType.IsInstanceOfType(args[i]))
                    {
                        ok = false;
                    }
                }

                if (ok)
                {
                    chosen = m;
                    break;
                }
            }

            if (chosen == null)
            {
                throw new MissingMethodException("Timeline", name);
            }

            return chosen.Invoke(native, args);
        }

        internal static T Get<T>(object target, string name)
        {
            FieldInfo f = target.GetType().GetField(name, Flags);
            if (f != null)
            {
                return (T)f.GetValue(target);
            }

            PropertyInfo p = target.GetType().GetProperty(name, Flags);
            if (p == null)
            {
                throw new MissingMemberException(target.GetType().Name, name);
            }

            return (T)p.GetValue(target, null);
        }

        internal static void Set(object target, string name, object value)
        {
            FieldInfo f = target.GetType().GetField(name, Flags);
            if (f != null)
            {
                f.SetValue(target, value);
            }
            else
            {
                target.GetType().GetProperty(name, Flags).SetValue(target, value, null);
            }
        }

        internal static string Error(Exception e)
        {
            while (e.InnerException != null)
            {
                e = e.InnerException;
            }

            return e.Message;
        }
    }
}
