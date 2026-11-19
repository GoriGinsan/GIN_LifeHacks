using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace GinPadHack
{
    // Face tab: Blend Shapes as drag bars. (The pupils are on the Map tab.)
    //
    // The small square in front of a bar pins the shape as a favourite. Which shapes are listed:
    //   Fav    only the pinned ones
    //   Used   pinned, used by the character's Timeline keys, or not zero right now. The shapes
    //          named in the HiddenShapes setting (the "def" shapes that are never edited) are
    //          left out.
    //   All    everything
    //
    // The Map tab shows the same bars for the pose that is being made (PadWindow.MapMaking.cs).
    public sealed partial class PadWindow
    {
        const float FaceRefreshSeconds = 2f;
        const float NonZero = 0.5f;
        const float ColumnWidth = 167f;     // at UiScale 1 (15 % narrower than before, so that tabs fit side by side)
        const float ShapeBarWidth = ColumnWidth - 11f;   // a bar beside its pin square; the Shapes tabs use the same length
        const string LegacyPinnedFile = "../FacePad/pinned-shapes.txt";   // GIN Advance mode life hack 1.1.0 trial builds

        const string NothingListed = "Nothing listed: choose All, then pin shapes with the square";
        static readonly string[] ListNames = { "Fav", "Used", "All" };
        static readonly float[] ListWidths = { 32f, 40f, 30f };

        // One Blend Shape shown as a bar.
        sealed class ShapeRow
        {
            internal BlendMesh Mesh;
            internal int Index;
            internal string Name, Label, PinKey;
            internal float Weight;
        }

        readonly List<ShapeRow> eyeShapes = new List<ShapeRow>(), browShapes = new List<ShapeRow>();
        readonly List<ShapeRow> mouthShapes = new List<ShapeRow>(), toothShapes = new List<ShapeRow>();
        readonly List<ShapeRow> mapTeeth = new List<ShapeRow>();   // the one teeth shape a mouth pose of the Map holds
        readonly HashSet<string> pinned = new HashSet<string>(StringComparer.Ordinal);
        readonly HashSet<string> hiddenByDefault = new HashSet<string>(StringComparer.Ordinal);
        string hiddenSetting;
        bool faceListStale = true;
        float faceRefreshAt;
        int hiddenShapes;
        Component faceTarget;
        Vector2 leftScroll, rightScroll;

        void DrawFace(PadTarget target)
        {
            UpdateFaceLists(target);
            GUILayout.BeginHorizontal();
            ListButtons();
            GUILayout.EndHorizontal();

            // A column scrolls only when it would not fit on the screen.
            float room = Mathf.Max(160f, Screen.height - 260f);
            GUILayout.BeginHorizontal();
            float left = ColumnHeight(eyeShapes, browShapes), right = ColumnHeight(mouthShapes, toothShapes);
            BeginColumn(ref leftScroll, left, room);
            Section("Eyes", eyeShapes, target);
            Section("Brows", browShapes, target);
            EndColumn(ref leftScroll, left, room);
            BeginColumn(ref rightScroll, right, room);
            Section("Mouth", mouthShapes, target);
            Section("Teeth", toothShapes, target);
            EndColumn(ref rightScroll, right, room);
            GUILayout.EndHorizontal();
            if (eyeShapes.Count + browShapes.Count + mouthShapes.Count + toothShapes.Count == 0)
            {
                GUILayout.Label(NothingListed, PadUi.Small);
            }
        }

        // Lists may only change in the Layout pass: IMGUI needs the same controls in Layout and Repaint.
        void UpdateFaceLists(PadTarget target)
        {
            if (Event.current.type != EventType.Layout)
            {
                return;
            }

            bool idle = !Input.GetMouseButton(0) && !Input.GetMouseButton(1);
            if (faceListStale || faceTarget != target.Pose || (idle && Time.realtimeSinceStartup >= faceRefreshAt))
            {
                RefreshFaceShapes(target);
            }
            else
            {
                ReadFaceValues(target, idle);
            }
        }

        // "Fav / Used / All" and how many shapes the choice leaves out.
        void ListButtons()
        {
            for (int i = 0; i < ListNames.Length; i++)
            {
                ShapeList choice = (ShapeList)i;
                if (PadUi.Toggle(host.FaceList.Value == choice, ListNames[i], PadUi.Width(ListWidths[i])) && host.FaceList.Value != choice)
                {
                    host.FaceList.Value = choice;
                    faceListStale = true;
                }
            }

            GUILayout.Label(hiddenShapes == 0 ? "" : "+" + hiddenShapes, PadUi.Small, PadUi.Width(34));
        }

        static float ColumnHeight(List<ShapeRow> first, List<ShapeRow> second)
        {
            int headings = (first.Count > 0 ? 1 : 0) + (second.Count > 0 ? 1 : 0);
            return (first.Count + second.Count) * (PadUi.BarHeight + 2f) + headings * PadUi.Px(15f);
        }

        // A column of bars that scrolls (with the Pad's own scroll bar) only when it is taller than "room".
        static void BeginColumn(ref Vector2 scroll, float contentHeight, float room)
        {
            if (contentHeight > room)
            {
                scroll = PadUi.BeginScroll(scroll, PadUi.Px(ColumnWidth) + 4f, room);
            }

            GUILayout.BeginVertical(PadUi.Width(ColumnWidth));
        }

        static void EndColumn(ref Vector2 scroll, float contentHeight, float room)
        {
            GUILayout.EndVertical();
            if (contentHeight > room)
            {
                scroll = PadUi.EndScroll(scroll, contentHeight + 8f, room);
            }
        }

        void Section(string title, List<ShapeRow> shapes, PadTarget target)
        {
            if (shapes.Count == 0)
            {
                return;
            }

            GUILayout.Label(title, PadUi.Heading);
            foreach (ShapeRow shape in shapes)
            {
                GUILayout.BeginHorizontal();
                bool isPinned = pinned.Contains(shape.PinKey);
                if (GUILayout.Button(GUIContent.none, isPinned ? PadUi.PinOn : PadUi.PinOff, PadUi.Width(8)))
                {
                    if (isPinned)
                    {
                        pinned.Remove(shape.PinKey);
                    }
                    else
                    {
                        pinned.Add(shape.PinKey);
                    }

                    SavePinned();
                }

                float weight = PadUi.Bar(shape.Label, shape.Weight, 0f, 100f, 0f, "0", PadUi.Width(ShapeBarWidth));
                GUILayout.EndHorizontal();
                if (weight != shape.Weight)
                {
                    if (Kkpe.SetWeight(shape.Mesh, shape.Name, weight))
                    {
                        shape.Weight = weight;
                        makingEdited |= drawingMaking;
                        Edited();
                    }
                    else
                    {
                        Status("KKPE did not accept " + shape.Name);
                    }
                }
            }
        }

        // ---- which shapes are listed -----------------------------------------------------------

        void RefreshFaceShapes(PadTarget target)
        {
            faceListStale = false;
            faceTarget = target.Pose;
            faceRefreshAt = Time.realtimeSinceStartup + FaceRefreshSeconds;
            eyeShapes.Clear();
            browShapes.Clear();
            mouthShapes.Clear();
            toothShapes.Clear();
            mapTeeth.Clear();
            hiddenShapes = 0;
            ReadHiddenSetting();
            ShapeList list = host.FaceList.Value;

            // While a pose of the Map is being made, what it consists of must be visible, favourite or not.
            bool showNonZero = tabs[(int)Tab.Map] && making != null;

            Dictionary<string, bool[]> usage = TimelineBridge.Usage(target.Character);
            int mouthStart = Kkpe.MouthStart(target);
            foreach (BlendMesh mesh in Kkpe.Meshes(target))
            {
                if (mesh.Role != Kkpe.FaceRole && mesh.Role != Kkpe.BrowRole && mesh.Role != Kkpe.TeethRole)
                {
                    continue;
                }

                bool[] used;
                bool usageKnown = usage.TryGetValue(mesh.Renderer.name, out used);
                Mesh shapes = mesh.Renderer.sharedMesh;
                for (int index = 0; index < shapes.blendShapeCount; index++)
                {
                    ShapeRow shape = new ShapeRow();
                    shape.Mesh = mesh;
                    shape.Index = index;
                    shape.Name = shapes.GetBlendShapeName(index);
                    shape.PinKey = mesh.Renderer.name + "\n" + shape.Name;
                    shape.Weight = Kkpe.Weight(mesh, index);
                    shape.Label = Kkpe.ShapeLabel(target, shape.Name);
                    if (mesh.Role == Kkpe.TeethRole && index == ExpressionMap.TeethShapeIndex)
                    {
                        mapTeeth.Add(shape);
                    }

                    bool nonZero = shape.Weight > NonZero;
                    bool inUse = (usageKnown && index < used.Length && used[index]) || nonZero;
                    bool listed = list == ShapeList.All
                        || pinned.Contains(shape.PinKey)
                        || (list == ShapeList.Used && inUse && !hiddenByDefault.Contains(Kkpe.ShortName(shape.Name)))
                        || (showNonZero && nonZero);
                    if (!listed)
                    {
                        hiddenShapes++;
                        continue;
                    }

                    if (mesh.Role == Kkpe.BrowRole)
                    {
                        browShapes.Add(shape);
                    }
                    else if (mesh.Role == Kkpe.TeethRole)
                    {
                        toothShapes.Add(shape);
                    }
                    else if (index < mouthStart)
                    {
                        eyeShapes.Add(shape);
                    }
                    else
                    {
                        mouthShapes.Add(shape);
                    }
                }
            }

            FindPupils(target);
            ReadPupils(target);
        }

        void ReadFaceValues(PadTarget target, bool idle)
        {
            foreach (List<ShapeRow> section in new List<ShapeRow>[] { eyeShapes, browShapes, mouthShapes, toothShapes, mapTeeth })
            {
                foreach (ShapeRow shape in section)
                {
                    shape.Weight = Kkpe.Weight(shape.Mesh, shape.Index);
                }
            }

            // While a pupil control is held it owns the values; see WritePupils.
            if (idle)
            {
                ReadPupils(target);
            }
        }

        void ReadHiddenSetting()
        {
            if (hiddenSetting == host.HiddenShapes.Value)
            {
                return;
            }

            hiddenSetting = host.HiddenShapes.Value;
            hiddenByDefault.Clear();
            foreach (string name in hiddenSetting.Split(','))
            {
                if (name.Trim().Length > 0)
                {
                    hiddenByDefault.Add(name.Trim());
                }
            }
        }

        // ---- pinned shapes: one "renderer<TAB>shape" per line ------------------------------------

        string PinnedPath
        {
            get
            {
                return Path.Combine(storage, "pinned-shapes.txt");
            }
        }

        void LoadPinned()
        {
            try
            {
                string path = File.Exists(PinnedPath) ? PinnedPath : Path.Combine(storage, LegacyPinnedFile);
                if (!File.Exists(path))
                {
                    return;
                }

                foreach (string line in File.ReadAllLines(path))
                {
                    string[] parts = line.Split('\t');
                    if (parts.Length == 2)
                    {
                        pinned.Add(parts[0] + "\n" + parts[1]);
                    }
                }
            }
            catch (Exception error)
            {
                Status("Pinned shapes ignored: " + Root(error).Message);
            }
        }

        void SavePinned()
        {
            faceListStale = true;
            try
            {
                List<string> lines = new List<string>();
                foreach (string key in pinned)
                {
                    lines.Add(key.Replace('\n', '\t'));
                }

                Directory.CreateDirectory(storage);
                File.WriteAllLines(PinnedPath, lines.ToArray());
            }
            catch (Exception error)
            {
                Status("Pinned shapes not saved: " + Root(error).Message);
            }
        }
    }
}
