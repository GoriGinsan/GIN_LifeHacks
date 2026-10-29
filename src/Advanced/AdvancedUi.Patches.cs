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
        private static IEnumerable<CodeInstruction> BonesTranspiler(IEnumerable<CodeInstruction> input)
        {
            if (restoringNative)
            {
                return input;
            }

            int buttons = 0, text = 0;
            List<CodeInstruction> output = new List<CodeInstruction>(input);
            foreach (CodeInstruction c in output)
            {
                MethodInfo m = c.operand as MethodInfo;
                if (m != null && m.DeclaringType == typeof(GUILayout) && m.Name == "Button" && TwoArgsString(m))
                {
                    c.operand = Method("BoneButton");
                    buttons++;
                }

                if (m != null && m.DeclaringType == typeof(GUILayout) && m.Name == "TextField" && TwoArgsString(m))
                {
                    c.operand = Method("TextField");
                    text++;
                }
            }

            if (buttons < 3 || text < 1)
            {
                throw new NotSupportedException("Bones UI patch locations differ");
            }

            return output;
        }

        private static bool TwoArgsString(MethodInfo m)
        {
            ParameterInfo[] pp = m.GetParameters();
            return pp.Length == 2 && pp[0].ParameterType == typeof(string) && pp[1].ParameterType == typeof(GUILayoutOption[]);
        }

        private static IEnumerable<CodeInstruction> BlendTranspiler(IEnumerable<CodeInstruction> input, ILGenerator generator)
        {
            if (restoringNative)
            {
                return input;
            }

            List<CodeInstruction> code = new List<CodeInstruction>(input);
            int alias = -1, rowLocal = -1, loop = -1, sectionLabels = 0;
            for (int i = 0; i < code.Count; i++)
            {
                FieldInfo f = code[i].operand as FieldInfo;
                if (alias < 0 && f != null && f.Name == "_blendShapeAliases")
                {
                    alias = i;
                }

                MethodInfo m = code[i].operand as MethodInfo;
                if (m != null && m.Name == "get_Current" && m.ReturnType == typeof(KeyValuePair<string, int>))
                {
                    rowLocal = LocalIndex(code[i + 1]);
                }

                if (alias >= 0 && i > alias && m != null && m.Name == "MoveNext")
                {
                    loop = i - 1;
                    break;
                }
            }

            if (alias < 0 || rowLocal < 0 || loop <= alias)
            {
                throw new NotSupportedException("BlendShapes row boundaries differ");
            }

            CodeInstruction heading = null;
            for (int i = alias - 1; i >= 0; i--)
            {
                MethodInfo m = code[i].operand as MethodInfo;
                if (m == null || m.DeclaringType != typeof(GUILayout) || m.Name != "Label")
                {
                    continue;
                }

                ParameterInfo[] p = m.GetParameters();
                if (p.Length == 3 && p[0].ParameterType == typeof(string) && p[1].ParameterType == typeof(GUIStyle))
                {
                    heading = code[i];
                    break;
                }
            }

            if (heading == null)
            {
                throw new NotSupportedException("BlendShapes face heading boundary differs");
            }

            Label next = generator.DefineLabel();
            code[loop].labels.Add(next);
            List<CodeInstruction> inject = new List<CodeInstruction>();
            CodeInstruction first = new CodeInstruction(OpCodes.Ldarg_0, null);
            first.labels.AddRange(code[alias].labels);
            code[alias].labels.Clear();
            inject.Add(first);
            inject.Add(new CodeInstruction(OpCodes.Ldloc, rowLocal));
            inject.Add(new CodeInstruction(OpCodes.Call, Method("RowVisible")));
            inject.Add(new CodeInstruction(OpCodes.Brfalse, next));
            code.InsertRange(alias, inject);
            foreach (CodeInstruction c in code)
            {
                MethodInfo m = c.operand as MethodInfo;
                if (m == null || m.DeclaringType != typeof(GUILayout))
                {
                    continue;
                }

                if (m.Name == "Button" && TwoArgsString(m))
                {
                    c.operand = Method("BlendButton");
                }

                if (m.Name == "TextField" && TwoArgsString(m))
                {
                    c.operand = Method("TextField");
                }

                ParameterInfo[] p = m.GetParameters();
                if (c == heading && m.Name == "Label" && p.Length == 3 && p[0].ParameterType == typeof(string) && p[1].ParameterType == typeof(GUIStyle) && p[2].ParameterType == typeof(GUILayoutOption[]))
                {
                    c.operand = Method("SectionHeading");
                    sectionLabels++;
                }
            }

            if (sectionLabels != 1)
            {
                throw new NotSupportedException("BlendShapes section headings differ");
            }

            return code;
        }

        private static int LocalIndex(CodeInstruction c)
        {
            if (c.opcode == OpCodes.Stloc_0)
            {
                return 0;
            }

            if (c.opcode == OpCodes.Stloc_1)
            {
                return 1;
            }

            if (c.opcode == OpCodes.Stloc_2)
            {
                return 2;
            }

            if (c.opcode == OpCodes.Stloc_3)
            {
                return 3;
            }

            LocalBuilder b = c.operand as LocalBuilder;
            if (b != null)
            {
                return b.LocalIndex;
            }

            if (c.operand is int)
            {
                return (int)c.operand;
            }

            if (c.operand is byte)
            {
                return (byte)c.operand;
            }

            return -1;
        }
    }
}
