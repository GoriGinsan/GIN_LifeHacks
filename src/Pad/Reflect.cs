using System;
using System.Collections.Generic;
using System.Reflection;

namespace GinPadHack
{
    // Cached reflection. KKPE keeps the members this plugin needs private, and they are read
    // for every bar on every GUI event, so each lookup is done once.
    internal static class Reflect
    {
        const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

        static readonly Dictionary<string, Type> types = new Dictionary<string, Type>();
        static readonly Dictionary<Type, Dictionary<string, MemberInfo>> members = new Dictionary<Type, Dictionary<string, MemberInfo>>();
        static readonly Dictionary<string, MethodInfo> methods = new Dictionary<string, MethodInfo>();

        // A loaded type by full name; null (and searched again next time) while it is not loaded.
        internal static Type Type(string fullName)
        {
            Type found;
            if (types.TryGetValue(fullName, out found))
            {
                return found;
            }

            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                found = assembly.GetType(fullName, false);
                if (found != null)
                {
                    types[fullName] = found;
                    return found;
                }
            }

            return null;
        }

        // Read a field or property, also a private one of a base class.
        // Pass a Type to read a static member. A missing member reads as null.
        internal static object Get(object target, string name)
        {
            if (target == null)
            {
                return null;
            }

            Type type = target as Type ?? target.GetType();
            object instance = target is Type ? null : target;
            MemberInfo member = Member(type, name);
            FieldInfo field = member as FieldInfo;
            if (field != null)
            {
                return field.GetValue(instance);
            }

            PropertyInfo property = member as PropertyInfo;
            return property == null ? null : property.GetValue(instance, null);
        }

        internal static T Get<T>(object target, string name)
        {
            object value = Get(target, name);
            return value is T ? (T)value : default(T);
        }

        static MemberInfo Member(Type type, string name)
        {
            Dictionary<string, MemberInfo> known;
            if (!members.TryGetValue(type, out known))
            {
                known = new Dictionary<string, MemberInfo>();
                members[type] = known;
            }

            MemberInfo member;
            if (known.TryGetValue(name, out member))
            {
                return member;
            }

            for (Type at = type; at != null && member == null; at = at.BaseType)
            {
                member = at.GetField(name, Any | BindingFlags.DeclaredOnly);
                if (member == null)
                {
                    // A class may override only the setter; its getter then lives on the base class.
                    PropertyInfo property = at.GetProperty(name, Any | BindingFlags.DeclaredOnly);
                    if (property != null && property.GetGetMethod(true) != null)
                    {
                        member = property;
                    }
                }
            }

            known[name] = member;
            return member;
        }

        // A method by exact parameter types, also a private one of a base class; null when missing.
        internal static MethodInfo Method(Type type, string name, params Type[] parameters)
        {
            string key = type.FullName + "." + name + "/" + parameters.Length;
            foreach (Type parameter in parameters)
            {
                key += "," + parameter.FullName;
            }

            MethodInfo method;
            if (methods.TryGetValue(key, out method))
            {
                return method;
            }

            for (Type at = type; at != null && method == null; at = at.BaseType)
            {
                method = at.GetMethod(name, Any | BindingFlags.DeclaredOnly, null, parameters, null);
            }

            methods[key] = method;
            return method;
        }

        internal static object Call(object target, string name, Type[] parameters, params object[] arguments)
        {
            MethodInfo method = Method(target.GetType(), name, parameters);
            if (method == null)
            {
                throw new MissingMethodException(target.GetType().FullName, name);
            }

            return method.Invoke(target, arguments);
        }
    }
}
