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
    // This adapter never disables a renderer, bone, module, or native expression.
    internal sealed class AdvancedUi : IDisposable
    {
        private static AdvancedUi current;
        private readonly Plugin host;
        private readonly Harmony harmony;
        private readonly Type mainType, poseType, bonesType, blendsType;
        private readonly string displayDirectory;
        private readonly List<MethodBase> patched = new List<MethodBase>();
        private GUISkin sourceSkin, localSkin;
        private static object bonesContext, blendContext;
        private static string currentBlendKey;
        private static int textIndex;
        private bool disposed, dirty, showHidden, templatesOpen;
        private float saveAt;
        private string templateName = "Default", message = "";
        private Vector2 templatesScroll;
        private Rect templateRect = new Rect(50, 90, 480, 380);
        private readonly HashSet<string> hidden = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> collapsed = new HashSet<string>(StringComparer.Ordinal);
        private string[] templateFiles = new string[0];
        private static bool textInput;
        private static bool restoringNative;
        private bool clearTextFocus;
        private static int textFrame;
        private static readonly Dictionary<int, ScrubGesture> scrubs = new Dictionary<int, ScrubGesture>();
        private sealed class ScrubGesture { internal float StartX, LastX, Total, Started, RepeatAt; internal bool Dragged, Repeated; }

        internal static bool TextInputActive { get { return current != null && textInput && Time.frameCount - textFrame < 3; } }
        internal static bool PointerOverExtension { get { return current != null && current.templatesOpen && current.templateRect.Contains(Mouse()); } }
        internal static Rect[] ExtensionInputRects { get { return current != null && NativeVisible && current.templatesOpen ? new Rect[]{current.templateRect} : new Rect[0]; } }
        internal static Rect NativeRect { get { object o = R.Get(R.Type("HSPE.MainWindow"), "_self"); object r = R.Get(o, "_advancedModeRect"); return r is Rect ? (Rect)r : new Rect(); } }
        internal static bool NativeVisible
        {
            get { Type p = R.Type("HSPE.PoseController"); object v = R.Get(p, "_drawAdvancedMode"); object m = R.Get(R.Type("HSPE.MainWindow"), "_self"); return v is bool && (bool)v && R.Get(m, "_poseTarget") is UnityEngine.Object && (UnityEngine.Object)R.Get(m, "_poseTarget") != null; }
        }
        internal AdvancedUi(Plugin plugin)
        {
            host = plugin; mainType = R.Type("HSPE.MainWindow"); poseType = R.Type("HSPE.PoseController");
            bonesType = R.Type("HSPE.AMModules.BonesEditor"); blendsType = R.Type("HSPE.AMModules.BlendShapesEditor");
            Version nativeVersion = mainType == null ? null : mainType.Assembly.GetName().Version;
            if (nativeVersion != new Version(2,18,1,0) && nativeVersion != new Version(2,21,5,0))
                throw new NotSupportedException("Inspected KKPE APIs: 2.18.1.0 and 2.21.5.0; found " + (nativeVersion == null ? "none" : nativeVersion.ToString()) + ". Native KKPE UI is retained.");
            displayDirectory = Path.Combine(Paths.GameRootPath, "UserData/GIN/AdvancedDisplay");
            Directory.CreateDirectory(displayDirectory);
            if (File.Exists(Path.Combine(displayDirectory, "active.xml")))
                try { LoadDisplay(Path.Combine(displayDirectory, "active.xml")); }
                catch(Exception e) { host.Report("Display preferences ignored; native values retained: "+e.Message); }
            RefreshTemplates();
            harmony = new Harmony(Plugin.Guid + ".ui"); current = this; restoringNative = false;
            try
            {
                Patch(AccessTools.Method(poseType, "AdvancedModeWindow", null, null), "WindowPrefix", null, null, "WindowFinalizer");
                Patch(AccessTools.Method(mainType, "OnGUI", null, null), null, "AfterNativeGUI", null, null);
                Patch(AccessTools.Method(blendsType, "GUILogic", null, null), "BlendPrefix", null, "BlendTranspiler", "BlendFinalizer");
                Patch(AccessTools.Method(bonesType, "GUILogic", null, null), "BonesPrefix", null, "BonesTranspiler", "BonesFinalizer");
                Type module = R.Type("HSPE.AMModules.AdvancedModeModule");
                Patch(AccessTools.Method(module, "Vector3Editor", new Type[] { typeof(Vector3),typeof(Color),typeof(Color),typeof(Color),typeof(float),typeof(string),typeof(string),typeof(string),typeof(Action),typeof(bool) }, null), "VectorPrefix", null, null, null);
                Patch(AccessTools.Method(module, "QuaternionEditor", new Type[] { typeof(Quaternion),typeof(Color),typeof(Color),typeof(Color),typeof(float),typeof(string),typeof(string),typeof(string),typeof(Action) }, null), "QuaternionPrefix", null, null, null);
                Patch(AccessTools.Method(typeof(Studio.WorkspaceCtrl),"OnClickDelete",null,null),"BeforeSceneDelete",null,null,null);
            }
            catch { Dispose(); throw; }
        }
        private void Patch(MethodInfo original, string prefix, string postfix, string transpiler, string finalizer)
        {
            if (original == null) throw new MissingMethodException("KKPE API signature differs");
            harmony.Patch(original, HM(prefix), HM(postfix), HM(transpiler), HM(finalizer), null); patched.Add(original);
        }
        private static HarmonyMethod HM(string name) { return name == null ? null : new HarmonyMethod(typeof(AdvancedUi).GetMethod(name, R.Flags)); }
        private static MethodInfo Method(string name) { return typeof(AdvancedUi).GetMethod(name, R.Flags); }
        internal void Tick()
        {
            if (dirty && Time.realtimeSinceStartup > saveAt) { SaveDisplay(Path.Combine(displayDirectory,"active.xml"), "Current"); dirty = false; }
            if (!NativeVisible) { textInput = false; templatesOpen = false; ReleaseScrub(); }
            else if(Input.GetMouseButtonDown(0) && !NativeRect.Contains(Mouse()) && !PointerOverExtension && (host.Library==null||!host.Library.IsPointerOverUI))clearTextFocus=true;
        }
        private static bool BeforeSceneDelete() { return current==null||(!Plugin.IsTextInputActive&&!Plugin.IsPointerOverExtensionUI); }
        private void Changed() { dirty = true; saveAt = Time.realtimeSinceStartup + .6f; }
        private static Vector2 Mouse() { return new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y); }
        private GUISkin GetSkin(GUISkin original)
        {
            if (localSkin == null || sourceSkin != original)
            {
                if (localSkin != null) UnityEngine.Object.Destroy(localSkin);
                sourceSkin = original; localSkin = UnityEngine.Object.Instantiate(original) as GUISkin;
                localSkin.name = "GIN Advanced mode scoped skin";
            }
            // All styles live in a cloned skin. Other IMGUI windows retain their own skin.
            if (host.LargeControls.Value)
            {
                localSkin.button.fixedHeight = host.ButtonHeight.Value;
                localSkin.textField.fixedHeight = host.ButtonHeight.Value;
                localSkin.horizontalSlider.fixedHeight = host.ThumbSize.Value + 4;
                localSkin.horizontalSliderThumb.fixedWidth = host.ThumbSize.Value;
                localSkin.horizontalSliderThumb.fixedHeight = host.ThumbSize.Value;
                localSkin.verticalScrollbar.fixedWidth = host.ThumbSize.Value;
                localSkin.verticalScrollbarThumb.fixedWidth = host.ThumbSize.Value;
                localSkin.horizontalScrollbar.fixedHeight = host.ThumbSize.Value;
                localSkin.horizontalScrollbarThumb.fixedHeight = host.ThumbSize.Value;
            }
            else
            {
                localSkin.button = new GUIStyle(original.button); localSkin.textField = new GUIStyle(original.textField);
                localSkin.horizontalSlider = new GUIStyle(original.horizontalSlider); localSkin.horizontalSliderThumb = new GUIStyle(original.horizontalSliderThumb);
                localSkin.verticalScrollbar = new GUIStyle(original.verticalScrollbar); localSkin.verticalScrollbarThumb = new GUIStyle(original.verticalScrollbarThumb);
                localSkin.horizontalScrollbar = new GUIStyle(original.horizontalScrollbar); localSkin.horizontalScrollbarThumb = new GUIStyle(original.horizontalScrollbarThumb);
            }
            int gap = host.CompactSpacing.Value ? 1 : original.button.margin.top;
            localSkin.button.margin = new RectOffset(2,2,gap,gap);
            localSkin.textField.margin = new RectOffset(2,2,gap,gap);
            localSkin.label.margin = host.CompactSpacing.Value ? new RectOffset(2,2,0,0) : new RectOffset(original.label.margin.left,original.label.margin.right,original.label.margin.top,original.label.margin.bottom);
            localSkin.horizontalSlider.margin = host.CompactSpacing.Value ? new RectOffset(4,4,0,0) : new RectOffset(original.horizontalSlider.margin.left,original.horizontalSlider.margin.right,original.horizontalSlider.margin.top,original.horizontalSlider.margin.bottom);
            return localSkin;
        }
        private sealed class SkinState { internal GUISkin Previous; }
        private static void WindowPrefix(out SkinState __state)
        {
            __state = new SkinState(); __state.Previous = GUI.skin; textIndex = 0;
            if (current != null) GUI.skin = current.GetSkin(GUI.skin);
        }
        private static Exception WindowFinalizer(Exception __exception, SkinState __state)
        {
            if (__state != null && __state.Previous != null) GUI.skin = __state.Previous;
            return __exception;
        }
        private static void AfterNativeGUI()
        {
            AdvancedUi a = current; if (a == null || !NativeVisible) return;
            string focus = GUI.GetNameOfFocusedControl();
            if(a.clearTextFocus) { if(!String.IsNullOrEmpty(focus)&&focus.StartsWith("GIN.Adv.",StringComparison.Ordinal)){GUI.FocusControl(null);focus="";}a.clearTextFocus=false; }
            textInput = !String.IsNullOrEmpty(focus) && focus.StartsWith("GIN.Adv.", StringComparison.Ordinal); textFrame = Time.frameCount;
            if (a.host.Library != null && a.host.Expressions.Value) a.host.Library.Draw(NativeRect);
            if (a.templatesOpen)
            {
                Color previous=GUI.color;GUI.color=new Color(.17f,.17f,.17f,1);GUI.DrawTexture(a.templateRect,Texture2D.whiteTexture);GUI.color=previous;
                a.templateRect = GUILayout.Window(0x47494131, a.templateRect, a.DrawTemplates, "Display templates — GIN", GUILayout.Width(480), GUILayout.Height(380));
            }
        }
        private static void BonesPrefix(object __instance) { bonesContext = __instance; }
        private static Exception BonesFinalizer(Exception __exception) { bonesContext = null; return __exception; }
        private static void BlendPrefix(object __instance)
        {
            blendContext = __instance; currentBlendKey = null;
            if (current == null || !current.host.VisibilityTools.Value) return;
            current.DrawVisibilityToolbar(__instance);
        }
        private static Exception BlendFinalizer(Exception __exception) { blendContext = null; currentBlendKey = null; return __exception; }
        private void DrawVisibilityToolbar(object editor)
        {
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Display templates", GUILayout.ExpandWidth(false))) { templatesOpen = !templatesOpen; if (templatesOpen) { templateRect.x = Mathf.Clamp(NativeRect.xMax + 8, 0, Screen.width - 480); templateRect.y = Mathf.Clamp(NativeRect.y,0,Screen.height - 380); RefreshTemplates(); } }
            showHidden = GUILayout.Toggle(showHidden, "Show hidden rows", GUILayout.ExpandWidth(false));
            if (GUILayout.Button("Reveal all", GUILayout.ExpandWidth(false))) { hidden.Clear(); Changed(); }
            if (R.Get(editor,"_faceRenderer") == R.Get(editor,"_skinnedMeshTarget") && R.Get(editor,"_faceRenderer") != null)
            {
                if (GUILayout.Button("Expand Eyes / Mouth", GUILayout.ExpandWidth(false))) { SetFaceSections(editor,false); }
                if (GUILayout.Button("Collapse both", GUILayout.ExpandWidth(false))) { SetFaceSections(editor,true); }
            }
            GUILayout.EndHorizontal();
        }
        private static string RendererKey(object editor) { return Convert.ToString(R.Get(R.Get(editor,"_skinnedMeshTarget"),"_fullPath"), CultureInfo.InvariantCulture); }
        private static IDictionary Separators(object editor)
        {
            object target = R.Get(editor,"_target"); bool female = (bool)R.Get(target,"isFemale");
            return (IDictionary)R.Get(editor.GetType(), female ? "_femaleSeparators" : "_maleSeparators");
        }
        private static string Section(object editor, int index)
        {
            if (R.Get(editor,"_faceRenderer") != R.Get(editor,"_skinnedMeshTarget")) return "";
            IDictionary separators = Separators(editor); string section = ""; int nearest = Int32.MinValue;
            foreach (DictionaryEntry e in separators) { int start = Convert.ToInt32(e.Key); if (start <= index && start > nearest) { nearest=start; section=Convert.ToString(e.Value); } }
            return section;
        }
        private void SetFaceSections(object editor, bool close)
        {
            foreach (DictionaryEntry e in Separators(editor)) { string key=RendererKey(editor)+"\n"+e.Value; if(close)collapsed.Add(key);else collapsed.Remove(key); } Changed();
        }
        // Inserted just after the native section heading and before any row layout.
        private static bool RowVisible(object editor, KeyValuePair<string,int> row)
        {
            currentBlendKey=RendererKey(editor)+"\n"+row.Key;
            if (current == null || !current.host.VisibilityTools.Value) return true;
            if (current.collapsed.Contains(RendererKey(editor)+"\n"+Section(editor,row.Value))) return false;
            return current.showHidden || !current.hidden.Contains(currentBlendKey);
        }
        private static void SectionHeading(string text, GUIStyle style, GUILayoutOption[] options)
        {
            if (current == null || !current.host.VisibilityTools.Value || blendContext == null) { GUILayout.Label(text,style,options); return; }
            string key=RendererKey(blendContext)+"\n"+text; bool closed=current.collapsed.Contains(key);
            if (GUILayout.Button((closed ? "▶ " : "▼ ")+text, style, options)) { if(closed)current.collapsed.Remove(key);else current.collapsed.Add(key);current.Changed(); }
        }
        private static bool BlendButton(string text, GUILayoutOption[] options)
        {
            if (current != null && current.host.VisibilityTools.Value && currentBlendKey != null && (text=="Rename"||text=="Save"))
            {
                Color color=GUI.color; GUI.color=Color.white;
                bool isHidden=current.hidden.Contains(currentBlendKey);
                if(GUILayout.Button(isHidden?"Show":"Hide",GUILayout.Width(54)))
                { if(isHidden)current.hidden.Remove(currentBlendKey);else current.hidden.Add(currentBlendKey);current.Changed(); }
                GUI.color=color;
            }
            if(current!=null&&current.host.LargeControls.Value&&(text=="-1"||text=="+1"))
            {
                List<GUILayoutOption> larger=new List<GUILayoutOption>(options);larger.Add(GUILayout.MinWidth(32));return GUILayout.Button(text,larger.ToArray());
            }
            return GUILayout.Button(text,options);
        }
        private static string TextField(string text, GUILayoutOption[] options)
        {
            GUI.SetNextControlName("GIN.Adv.NativeText."+(textIndex++));
            return GUILayout.TextField(text,options);
        }
        private static bool BoneButton(string text, GUILayoutOption[] options)
        {
            if(current != null && current.host.TwinShortcut.Value && bonesContext != null && (text=="Copy Position"||text=="Copy Rotation"||text=="Copy Scale"))
            {
                bool enabled=GUI.enabled; Transform twin=R.Get(bonesContext,"_twinBoneTarget") as Transform;
                GUI.enabled=enabled&&twin!=null;
                if(GUILayout.Button(new GUIContent("L/R","Select the opposite bone"),GUILayout.Width(48))) SwitchTwin(bonesContext);
                GUI.enabled=enabled;
            }
            return GUILayout.Button(text,options);
        }
        private static bool SwitchTwin(object editor)
        {
            Transform twin=R.Get(editor,"_twinBoneTarget") as Transform;if(twin==null)return false;
            R.Call(editor,"ChangeBoneTarget",twin);return (R.Get(editor,"_boneTarget") as Transform)==twin;
        }
        private static IEnumerable<CodeInstruction> BonesTranspiler(IEnumerable<CodeInstruction> input)
        {
            if(restoringNative)return input;
            int buttons=0, text=0; List<CodeInstruction> output=new List<CodeInstruction>(input);
            foreach(CodeInstruction c in output)
            {
                MethodInfo m=c.operand as MethodInfo;
                if(m!=null&&m.DeclaringType==typeof(GUILayout)&&m.Name=="Button"&&TwoArgsString(m)) { c.operand=Method("BoneButton");buttons++; }
                if(m!=null&&m.DeclaringType==typeof(GUILayout)&&m.Name=="TextField"&&TwoArgsString(m)) { c.operand=Method("TextField");text++; }
            }
            if(buttons<3||text<1)throw new NotSupportedException("Bones UI patch locations differ");
            return output;
        }
        private static bool TwoArgsString(MethodInfo m) { ParameterInfo[] pp=m.GetParameters();return pp.Length==2&&pp[0].ParameterType==typeof(string)&&pp[1].ParameterType==typeof(GUILayoutOption[]); }
        private static IEnumerable<CodeInstruction> BlendTranspiler(IEnumerable<CodeInstruction> input, ILGenerator generator)
        {
            if(restoringNative)return input;
            List<CodeInstruction> code=new List<CodeInstruction>(input);int alias=-1, rowLocal=-1, loop=-1, sectionLabels=0;
            for(int i=0;i<code.Count;i++)
            {
                FieldInfo f=code[i].operand as FieldInfo;
                if(alias<0&&f!=null&&f.Name=="_blendShapeAliases")alias=i;
                MethodInfo m=code[i].operand as MethodInfo;
                if(m!=null&&m.Name=="get_Current"&&m.ReturnType==typeof(KeyValuePair<string,int>)) rowLocal=LocalIndex(code[i+1]);
                if(alias>=0&&i>alias&&m!=null&&m.Name=="MoveNext") { loop=i-1;break; }
            }
            if(alias<0||rowLocal<0||loop<=alias)throw new NotSupportedException("BlendShapes row boundaries differ");
            CodeInstruction heading=null;
            for(int i=alias-1;i>=0;i--)
            {
                MethodInfo m=code[i].operand as MethodInfo;if(m==null||m.DeclaringType!=typeof(GUILayout)||m.Name!="Label")continue;
                ParameterInfo[] p=m.GetParameters();
                if(p.Length==3&&p[0].ParameterType==typeof(string)&&p[1].ParameterType==typeof(GUIStyle)) { heading=code[i];break; }
            }
            if(heading==null)throw new NotSupportedException("BlendShapes face heading boundary differs");
            Label next=generator.DefineLabel();code[loop].labels.Add(next);
            List<CodeInstruction> inject=new List<CodeInstruction>();
            CodeInstruction first=new CodeInstruction(OpCodes.Ldarg_0, null);first.labels.AddRange(code[alias].labels);code[alias].labels.Clear();
            inject.Add(first);inject.Add(new CodeInstruction(OpCodes.Ldloc,rowLocal));inject.Add(new CodeInstruction(OpCodes.Call,Method("RowVisible")));inject.Add(new CodeInstruction(OpCodes.Brfalse,next));
            code.InsertRange(alias,inject);
            foreach(CodeInstruction c in code)
            {
                MethodInfo m=c.operand as MethodInfo;if(m==null||m.DeclaringType!=typeof(GUILayout))continue;
                if(m.Name=="Button"&&TwoArgsString(m))c.operand=Method("BlendButton");
                if(m.Name=="TextField"&&TwoArgsString(m))c.operand=Method("TextField");
                ParameterInfo[] p=m.GetParameters();
                if(c==heading&&m.Name=="Label"&&p.Length==3&&p[0].ParameterType==typeof(string)&&p[1].ParameterType==typeof(GUIStyle)&&p[2].ParameterType==typeof(GUILayoutOption[])) { c.operand=Method("SectionHeading");sectionLabels++; }
            }
            if(sectionLabels!=1)throw new NotSupportedException("BlendShapes section headings differ");
            return code;
        }
        private static int LocalIndex(CodeInstruction c)
        {
            if(c.opcode==OpCodes.Stloc_0)return 0;if(c.opcode==OpCodes.Stloc_1)return 1;if(c.opcode==OpCodes.Stloc_2)return 2;if(c.opcode==OpCodes.Stloc_3)return 3;
            LocalBuilder b=c.operand as LocalBuilder;if(b!=null)return b.LocalIndex;
            if(c.operand is int)return (int)c.operand;if(c.operand is byte)return (byte)c.operand;
            return -1;
        }

        private static bool VectorPrefix(object __instance, Vector3 value, Color xColor, Color yColor, Color zColor, float customInc, string xLabel, string yLabel, string zLabel, Action onValueChanged, bool scaleEditor, ref Vector3 __result)
        {
            if(current==null||!current.host.ScrubBones.Value||__instance!=bonesContext)return true;
            Vector3 result=value;Color color=GUI.color;GUILayout.BeginVertical();
            string[] labels={xLabel,yLabel,zLabel};Color[] colors={xColor,yColor,zColor};
            for(int i=0;i<3;i++)
            {
                GUI.color=colors[i];float typed;bool set;float delta=DrawAxis(result[i],labels[i],customInc,scaleEditor?1f:0f,out typed,out set);
                if(set||delta!=0f) { result[i]=(set?typed:result[i])+delta;if(onValueChanged!=null)onValueChanged(); }
            }
            GUILayout.EndVertical();GUI.color=color;__result=result;return false;
        }
        private static bool QuaternionPrefix(object __instance, Quaternion value, Color xColor, Color yColor, Color zColor, float customInc, string xLabel, string yLabel, string zLabel, Action onValueChanged, ref Quaternion __result)
        {
            if(current==null||!current.host.ScrubBones.Value||__instance!=bonesContext)return true;
            Quaternion result=value;Color color=GUI.color;GUILayout.BeginVertical();
            string[] labels={xLabel,yLabel,zLabel};Color[] colors={xColor,yColor,zColor};Vector3[] axes={Vector3.right,Vector3.up,Vector3.forward};
            for(int i=0;i<3;i++)
            {
                GUI.color=colors[i];Vector3 euler=result.eulerAngles;float typed;bool set;
                float delta=DrawAxis(euler[i],labels[i],customInc,0f,out typed,out set);
                if(set) { euler[i]=typed;result=Quaternion.Euler(euler); }
                // Native KKPE increment buttons post-multiply local-axis rotations.
                if(delta!=0f)result=result*Quaternion.AngleAxis(delta,axes[i]);
                if((set||delta!=0f)&&onValueChanged!=null)onValueChanged();
            }
            GUILayout.EndVertical();GUI.color=color;__result=result;return false;
        }
        private static float DrawAxis(float value,string label,float step,float reset,out float typed,out bool set)
        {
            GUILayout.BeginHorizontal();GUILayout.Label(label,GUILayout.Width(Mathf.Clamp(GUI.skin.label.CalcSize(new GUIContent(label)).x+4,26,100)));
            string before=value.ToString("0.#####",CultureInfo.InvariantCulture);
            GUI.SetNextControlName("GIN.Adv.Axis."+(textIndex++));
            string after=GUILayout.TextField(before,GUILayout.MinWidth(72));
            typed=value;set=after!=before&&Single.TryParse(after,NumberStyles.Float,CultureInfo.InvariantCulture,out typed)&&!Single.IsNaN(typed)&&!Single.IsInfinity(typed);
            if(GUILayout.Button(reset.ToString(CultureInfo.InvariantCulture),GUILayout.Width(28))) { typed=reset;set=true; }
            float delta=ScrubButton(-Mathf.Abs(step))+ScrubButton(Mathf.Abs(step));GUILayout.EndHorizontal();return delta;
        }
        internal static float ScrubDelta(float pixels,float step,float pixelsPerStep,bool fine,bool coarse)
        { return pixels*Mathf.Abs(step)/Mathf.Max(1f,pixelsPerStep)*(fine?.1f:coarse?10f:1f); }
        private static float ScrubButton(float signedStep)
        {
            string text=signedStep.ToString("+0.#####;-0.#####",CultureInfo.InvariantCulture);
            GUIContent content=new GUIContent(text,"Click: one step / hold and drag horizontally / Shift: fine / Ctrl: coarse / Esc: cancel");
            Rect rect=GUILayoutUtility.GetRect(content,GUI.skin.button,GUILayout.MinWidth(64),GUILayout.Height(current.host.ButtonHeight.Value));
            int id=GUIUtility.GetControlID(0x47494e53,FocusType.Passive,rect);Event e=Event.current;ScrubGesture gesture;float delta=0;
            if(e.type==EventType.Repaint)GUI.skin.button.Draw(rect,content,rect.Contains(e.mousePosition),GUIUtility.hotControl==id,false,false);
            if(!GUI.enabled)return 0;
            if(e.type==EventType.MouseDown&&e.button==0&&rect.Contains(e.mousePosition))
            {
                GUIUtility.hotControl=id;GUIUtility.keyboardControl=0;
                gesture=new ScrubGesture();gesture.StartX=gesture.LastX=e.mousePosition.x;gesture.Started=Time.realtimeSinceStartup;gesture.RepeatAt=gesture.Started+.45f;scrubs[id]=gesture;e.Use();
            }
            if(GUIUtility.hotControl!=id||!scrubs.TryGetValue(id,out gesture))return 0;
            if(e.type==EventType.KeyDown&&e.keyCode==KeyCode.Escape) { delta=-gesture.Total;GUIUtility.hotControl=0;scrubs.Remove(id);e.Use();return delta; }
            if(e.type==EventType.MouseDrag)
            {
                if(!gesture.Dragged&&Mathf.Abs(e.mousePosition.x-gesture.StartX)>=3)gesture.Dragged=true;
                if(gesture.Dragged)
                {
                    delta=ScrubDelta(e.mousePosition.x-gesture.LastX,signedStep,current.host.ScrubPixels.Value,e.shift,e.control);
                    gesture.Total+=delta;gesture.LastX=e.mousePosition.x;
                }
                e.Use();
            }
            else if(e.type==EventType.MouseUp&&e.button==0)
            {
                if(!gesture.Dragged&&!gesture.Repeated&&rect.Contains(e.mousePosition))delta=signedStep;
                GUIUtility.hotControl=0;scrubs.Remove(id);e.Use();
            }
            else if(e.type==EventType.Repaint&&!gesture.Dragged&&Time.realtimeSinceStartup>=gesture.RepeatAt)
            { delta=signedStep;gesture.Total+=delta;gesture.Repeated=true;gesture.RepeatAt=Time.realtimeSinceStartup+.09f; }
            return delta;
        }
        private static void ReleaseScrub() { if(scrubs.ContainsKey(GUIUtility.hotControl))GUIUtility.hotControl=0;scrubs.Clear(); }

        private void RefreshTemplates() { templateFiles=Directory.GetFiles(displayDirectory,"template-*.xml");Array.Sort(templateFiles,StringComparer.OrdinalIgnoreCase); }
        private void SaveDisplay(string path,string name)
        {
            string temporary=path+".tmp";
            XmlWriterSettings settings=new XmlWriterSettings();settings.Indent=true;settings.Encoding=new UTF8Encoding(false);
            using(XmlWriter w=XmlWriter.Create(temporary,settings))
            {
                w.WriteStartElement("GINAdvancedDisplay");w.WriteAttributeString("version","1");w.WriteAttributeString("name",name);
                foreach(string key in hidden) { w.WriteStartElement("hidden");w.WriteString(key);w.WriteEndElement(); }
                foreach(string key in collapsed) { w.WriteStartElement("collapsed");w.WriteString(key);w.WriteEndElement(); }
                w.WriteEndElement();
            }
            if(File.Exists(path))File.Delete(path);File.Move(temporary,path);
        }
        private void LoadDisplay(string path)
        {
            XmlDocument doc=new XmlDocument();doc.XmlResolver=null;doc.Load(path);
            if(doc.DocumentElement.Name!="GINAdvancedDisplay"||doc.DocumentElement.GetAttribute("version")!="1")throw new IOException("Unrecognized display template");
            HashSet<string> nextHidden=new HashSet<string>(StringComparer.Ordinal),nextCollapsed=new HashSet<string>(StringComparer.Ordinal);
            foreach(XmlNode n in doc.DocumentElement.ChildNodes) { if(n.Name=="hidden")nextHidden.Add(n.InnerText);if(n.Name=="collapsed")nextCollapsed.Add(n.InnerText); }
            hidden.Clear();collapsed.Clear();foreach(string s in nextHidden)hidden.Add(s);foreach(string s in nextCollapsed)collapsed.Add(s);
        }
        private static string DisplayName(string path)
        {
            try { XmlDocument d=new XmlDocument();d.XmlResolver=null;d.Load(path);return d.DocumentElement.GetAttribute("name"); }
            catch { return Path.GetFileNameWithoutExtension(path)+" (!)"; }
        }
        private void DrawTemplates(int id)
        {
            GUI.SetNextControlName("GIN.Adv.Template.Name");templateName=GUILayout.TextField(templateName,128);
            GUILayout.BeginHorizontal();
            if(GUILayout.Button("Save current display"))
            {
                if(String.IsNullOrEmpty(templateName.Trim()))message="Enter a name";
                else try { string path=Path.Combine(displayDirectory,"template-"+System.Guid.NewGuid().ToString("N")+".xml");SaveDisplay(path,templateName.Trim());RefreshTemplates();message="Saved visibility only; values are unchanged"; } catch(Exception ex) { message=ex.Message; }
            }
            if(GUILayout.Button("Close",GUILayout.Width(64)))templatesOpen=false;
            GUILayout.EndHorizontal();
            GUILayout.Label("Hidden rows keep working. Templates store visibility only.");
            templatesScroll=GUILayout.BeginScrollView(templatesScroll);
            foreach(string file in templateFiles)
            {
                if(GUILayout.Button(DisplayName(file)))try { LoadDisplay(file);Changed();message="Display template applied"; } catch(Exception ex){message=ex.Message;}
            }
            GUILayout.EndScrollView();GUILayout.Label(message);GUI.DragWindow(new Rect(0,0,10000,20));
            string focus=GUI.GetNameOfFocusedControl();if(!String.IsNullOrEmpty(focus)&&focus.StartsWith("GIN.Adv.",StringComparison.Ordinal)){textInput=true;textFrame=Time.frameCount;}
        }
        public void Dispose()
        {
            if(disposed)return;disposed=true;
            if(dirty)try{SaveDisplay(Path.Combine(displayDirectory,"active.xml"),"Current");}catch(Exception e){host.Report("Display state save: "+e.Message);}
            restoringNative=true;
            if(harmony!=null)harmony.UnpatchSelf();
            if(localSkin!=null)UnityEngine.Object.Destroy(localSkin);
            if(current==this)current=null;bonesContext=null;blendContext=null;textInput=false;ReleaseScrub();
        }
        internal string RunIsolatedTests()
        {
            if(!Plugin.IsIsolatedTest())return "SKIP Advanced tests: isolation required";
            StringBuilder log=new StringBuilder();
            foreach(MethodBase m in patched) { Patches p=Harmony.GetPatchInfo(m);if(p==null||!p.Owners.Contains(harmony.Id))return "FAIL Advanced patch ownership: "+m.Name; }
            log.AppendLine("PASS Advanced: "+patched.Count+" native method patches owned by this plugin; KKPE "+mainType.Assembly.GetName().Version+" version guard");
            if(Mathf.Abs(ScrubDelta(8,.1f,8,false,false)-.1f)>.00001f||Mathf.Abs(ScrubDelta(-16,.1f,8,false,false)+.2f)>.00001f||Mathf.Abs(ScrubDelta(8,.1f,8,true,false)-.01f)>.00001f)return "FAIL Bone scrub direction / precision";
            log.AppendLine("PASS Bone scrub arithmetic: right increases, left decreases, Shift fine; native increment size retained");
            object main=R.Get(mainType,"_self"),pose=R.Get(main,"_poseTarget");
            object bones=R.Get(pose,"_bonesEditor");Component character=pose as Component;
            if(bones!=null&&character!=null)
            {
                Transform left=null;foreach(Transform t in character.GetComponentsInChildren<Transform>(true))if(t.name=="cf_J_hitomi_tx_L"){left=t;break;}
                if(left!=null)
                {
                    object selected=R.Get(bones,"_boneTarget"),twin=R.Get(bones,"_twinBoneTarget"),alias=R.Get(bones,"_currentAlias");
                    try
                    {
                        R.Call(bones,"ChangeBoneTarget",left);Transform right=R.Get(bones,"_twinBoneTarget") as Transform;
                        if(right==null||right.name!="cf_J_hitomi_tx_R"||!SwitchTwin(bones)||(R.Get(bones,"_boneTarget") as Transform)!=right)throw new InvalidOperationException("L/R did not use native twin selection");
                        log.AppendLine("PASS L/R command: native left eye -> right eye selection; no transform values changed");
                    }
                    finally { R.Call(bones,"ChangeBoneTarget",new object[]{selected});R.Set(bones,"_twinBoneTarget",twin);R.Set(bones,"_currentAlias",alias); }
                }
            }
            string path=Path.Combine(displayDirectory,"verify-"+System.Guid.NewGuid().ToString("N")+".xml");
            HashSet<string> oldHidden=new HashSet<string>(hidden),oldCollapsed=new HashSet<string>(collapsed);
            try
            {
                hidden.Clear();collapsed.Clear();hidden.Add("renderer/path\nshape&name_日本語");collapsed.Add("renderer/path\nEyes");
                SaveDisplay(path,"検証 <template>");hidden.Clear();collapsed.Clear();LoadDisplay(path);
                if(!hidden.Contains("renderer/path\nshape&name_日本語")||!collapsed.Contains("renderer/path\nEyes")||DisplayName(path)!="検証 <template>")return "FAIL Visibility template round trip";
                log.AppendLine("PASS Visibility template disk round trip: Unicode names, renderer-qualified rows and independent collapsed sections; no mesh/bone data written");
            }
            finally { hidden.Clear();collapsed.Clear();foreach(string s in oldHidden)hidden.Add(s);foreach(string s in oldCollapsed)collapsed.Add(s);if(File.Exists(path))File.Delete(path); }
            log.AppendLine("SKIP physical pointer scrub / visual sizing: requires interactive UI verification");return log.ToString();
        }
    }
}
