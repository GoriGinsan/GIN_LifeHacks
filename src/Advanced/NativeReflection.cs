using System;
using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace GinAdvanceHack
{
    internal static class ReflectionAccess
    {
        internal const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

        internal static Type Type(string name)
        {
            foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type t = a.GetType(name, false);
                if (t != null)
                {
                    return t;
                }
            }

            return null;
        }

        internal static object Get(object obj, string name)
        {
            if (obj == null)
            {
                return null;
            }

            Type t = obj as Type ?? obj.GetType();
            FieldInfo f = AccessTools.Field(t, name);
            if (f != null)
            {
                return f.GetValue(obj is Type ? null : obj);
            }

            PropertyInfo p = AccessTools.Property(t, name);
            return p == null ? null : p.GetValue(obj is Type ? null : obj, null);
        }

        internal static void Set(object obj, string name, object value)
        {
            Type t = obj as Type ?? obj.GetType();
            FieldInfo f = AccessTools.Field(t, name);
            if (f == null)
            {
                throw new MissingFieldException(t.FullName, name);
            }

            f.SetValue(obj is Type ? null : obj, value);
        }

        internal static object Call(object obj, string name, params object[] args)
        {
            Type t = obj as Type ?? obj.GetType();
            MethodInfo found = null;
            foreach (MethodInfo m in t.GetMethods(Flags))
            {
                if (m.Name == name && m.GetParameters().Length == args.Length)
                {
                    bool valid = true;
                    ParameterInfo[] pp = m.GetParameters();
                    for (int i = 0; i < pp.Length; i++)
                    {
                        if (args[i] != null && !pp[i].ParameterType.IsInstanceOfType(args[i]))
                        {
                            valid = false;
                        }
                    }

                    if (valid)
                    {
                        found = m;
                        break;
                    }
                }
            }

            if (found == null)
            {
                throw new MissingMethodException(t.FullName, name);
            }

            return found.Invoke(obj is Type ? null : obj, args);
        }
    }
}
