using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Native = Timeline.Timeline;

namespace GinTimelineHack
{
    // Access to private members of the native Timeline plugin.
    // Lookups are cached: several of them run every frame.
    public sealed partial class Plugin
    {
        static readonly Dictionary<Type, Dictionary<string, MemberInfo>> memberCache = new Dictionary<Type, Dictionary<string, MemberInfo>>();
        static readonly Dictionary<string, MethodInfo[]> nativeMethodCache = new Dictionary<string, MethodInfo[]>();

        void Patch(string name, string pre, string post, string final)
        {
            HarmonyMethod prefix = H(pre), postfix = H(post), finalizer = H(final);
            if (prefix != null)
            {
                prefix.priority = Priority.First;
            }

            if (postfix != null)
            {
                postfix.priority = Priority.Last;
            }

            harmony.Patch(Method(name), prefix, postfix, null, finalizer, null);
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

        // Invoke a native instance method by name; the overload is chosen from the argument types.
        internal object Call(string name, params object[] args)
        {
            foreach (MethodInfo candidate in NativeMethods(name))
            {
                if (AcceptsArguments(candidate, args))
                {
                    return candidate.Invoke(native, args);
                }
            }

            throw new MissingMethodException("Timeline", name);
        }

        static MethodInfo[] NativeMethods(string name)
        {
            MethodInfo[] methods;
            if (nativeMethodCache.TryGetValue(name, out methods))
            {
                return methods;
            }

            List<MethodInfo> found = new List<MethodInfo>();
            foreach (MethodInfo method in typeof(Native).GetMethods(Flags))
            {
                if (method.Name == name)
                {
                    found.Add(method);
                }
            }

            methods = found.ToArray();
            nativeMethodCache[name] = methods;
            return methods;
        }

        static bool AcceptsArguments(MethodInfo method, object[] args)
        {
            ParameterInfo[] parameters = method.GetParameters();
            if (parameters.Length != args.Length)
            {
                return false;
            }

            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] != null && !parameters[i].ParameterType.IsInstanceOfType(args[i]))
                {
                    return false;
                }
            }

            return true;
        }

        // Read a field or property (public or private) by name.
        internal static T Get<T>(object target, string name)
        {
            MemberInfo member = FindMember(target.GetType(), name);
            FieldInfo field = member as FieldInfo;
            if (field != null)
            {
                return (T)field.GetValue(target);
            }

            return (T)((PropertyInfo)member).GetValue(target, null);
        }

        internal static void Set(object target, string name, object value)
        {
            MemberInfo member = FindMember(target.GetType(), name);
            FieldInfo field = member as FieldInfo;
            if (field != null)
            {
                field.SetValue(target, value);
            }
            else
            {
                ((PropertyInfo)member).SetValue(target, value, null);
            }
        }

        static MemberInfo FindMember(Type type, string name)
        {
            Dictionary<string, MemberInfo> members;
            if (!memberCache.TryGetValue(type, out members))
            {
                members = new Dictionary<string, MemberInfo>();
                memberCache[type] = members;
            }

            MemberInfo member;
            if (members.TryGetValue(name, out member))
            {
                return member;
            }

            member = type.GetField(name, Flags);
            if (member == null)
            {
                member = type.GetProperty(name, Flags);
            }

            if (member == null)
            {
                throw new MissingMemberException(type.Name, name);
            }

            members[name] = member;
            return member;
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
