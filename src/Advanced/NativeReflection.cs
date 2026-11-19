using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace GinAdvanceHack
{
    internal static class ReflectionAccess
    {
        internal const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

        // Lookups are cached: the UI patches read native state for every row on every GUI event.
        static readonly Dictionary<string, Type> types = new Dictionary<string, Type>();
        static readonly Dictionary<Type, Dictionary<string, MemberInfo>> members = new Dictionary<Type, Dictionary<string, MemberInfo>>();

        internal static Type Type(string name)
        {
            Type found;
            if (types.TryGetValue(name, out found))
            {
                return found;
            }

            foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
            {
                found = a.GetType(name, false);
                if (found != null)
                {
                    types[name] = found;
                    return found;
                }
            }

            return null;
        }

        // Field or property by name, including inherited ones; null when the member does not exist.
        static MemberInfo Member(Type type, string name)
        {
            Dictionary<string, MemberInfo> known;
            if (!members.TryGetValue(type, out known))
            {
                known = new Dictionary<string, MemberInfo>();
                members[type] = known;
            }

            MemberInfo member;
            if (!known.TryGetValue(name, out member))
            {
                member = (MemberInfo)AccessTools.Field(type, name) ?? AccessTools.Property(type, name);
                known[name] = member;
            }

            return member;
        }

        // Read an instance member, or a static member when a Type is passed. Missing members read as null.
        internal static object Get(object obj, string name)
        {
            if (obj == null)
            {
                return null;
            }

            Type t = obj as Type ?? obj.GetType();
            object instance = obj is Type ? null : obj;
            MemberInfo member = Member(t, name);
            FieldInfo f = member as FieldInfo;
            if (f != null)
            {
                return f.GetValue(instance);
            }

            PropertyInfo p = member as PropertyInfo;
            return p == null ? null : p.GetValue(instance, null);
        }

        internal static void Set(object obj, string name, object value)
        {
            Type t = obj as Type ?? obj.GetType();
            FieldInfo f = Member(t, name) as FieldInfo;
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
