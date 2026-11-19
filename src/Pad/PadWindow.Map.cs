using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace GinPadHack
{
    // Map tab: one graph for the whole face, and below it the eyebrows and the pupils.
    //
    //   left drag     horizontally through the mouth poses (columns), vertically through the eye
    //                 poses (rows); between two lines the neighbouring poses are blended
    //   right drag    the gaze (both pupils), kept inside the eyes when "Limit" is on
    //   Shift + drag  only horizontally or only vertically, whichever way the mouse moved further
    //                 (to try mouths without losing the eyes, and the other way round)
    //   Ctrl          snap to the nearest pose
    //   wheel         pupil size
    //   middle click  key the face in the Timeline at the playhead
    //
    //   Brows | Spread           the eyebrow poses | how far apart the pupils are
    //   L/R height | Size        the left pupil above the right one | pupil size
    //   Center, Limit            gaze, spread and height back to zero | keep the pupils in the eyes
    //
    // The small triangles above the map, to its left and above the Brows bar stand for the poses:
    // a click goes straight to a pose, a right click opens the pose editor (PadWindow.MapMaking.cs).
    public sealed partial class PadWindow
    {
        // At UiScale 1. Strip + map + margin fill the tab's ContentWidth; the margin leaves room
        // for the last triangle, which is centred on the map's right edge.
        const float StripSize = 11f, MapWidth = 381f, MapHeight = 400f;
        const float LowerBarWidth = 165f, LowerButtonWidth = 44f;   // two bars and a button per row below the map

        // Shift + drag on the Map: the marker follows the mouse only horizontally or only
        // vertically, whichever way the mouse has moved further since Shift was pressed. From then
        // on until the button is released the marker follows the mouse's movement instead of
        // jumping to the pointer, so that letting go of Shift does not throw it across the map.
        sealed class AxisLock
        {
            bool relative;
            Vector2 mouseFrom, markerFrom;

            internal void Release()
            {
                relative = false;
            }

            // The point the pad acts on for this event; "marker" is where the marker is on screen.
            internal Vector2 Pointer(Event current, Vector2 marker)
            {
                if (!relative)
                {
                    if (!current.shift)
                    {
                        return current.mousePosition;
                    }

                    relative = true;
                    mouseFrom = current.mousePosition;
                    markerFrom = marker;
                }

                Vector2 moved = current.mousePosition - mouseFrom;
                if (current.shift)
                {
                    if (Mathf.Abs(moved.x) >= Mathf.Abs(moved.y))
                    {
                        moved.y = 0f;
                    }
                    else
                    {
                        moved.x = 0f;
                    }
                }

                return markerFrom + moved;
            }
        }

        enum FacePart
        {
            Eyes,
            Mouth,
            Brows
        }

        static readonly int MapHint = "GinPadMap".GetHashCode();
        static readonly Color MapBack = new Color(0.13f, 0.13f, 0.15f, 1f);
        static readonly Color MapLine = new Color(1f, 1f, 1f, 0.10f);
        static readonly Color MapMarker = new Color(1f, 0.72f, 0.2f, 1f);

        ExpressionMap map;
        Vector2 mapPoint;                    // x: mouth column, y: eye row; fractions blend neighbours
        float browPoint;
        bool mouthDirty, eyesDirty, browsDirty;
        readonly AxisLock mapLock = new AxisLock(), gazeLock = new AxisLock();

        string MapPath
        {
            get
            {
                return Path.Combine(storage, "expression-map.txt");
            }
        }

        void LoadMap()
        {
            try
            {
                map = ExpressionMap.Load(MapPath);
            }
            catch (Exception error)
            {
                map = ExpressionMap.Default();
                Status("Expression map file ignored: " + Root(error).Message);
            }

            mapPoint = new Vector2((map.Mouths.Count - 1) / 2f, (map.Eyes.Count - 1) / 2f);
            browPoint = 0f;
        }

        List<Dictionary<string, float>> Stops(FacePart part)
        {
            return part == FacePart.Eyes ? map.Eyes : part == FacePart.Mouth ? map.Mouths : map.Brows;
        }

        void DrawMap(PadTarget target)
        {
            if (Event.current.type == EventType.Layout)
            {
                if (pupilOwner != target.Pose)
                {
                    FindPupils(target);
                }

                if (!Input.GetMouseButton(0) && !Input.GetMouseButton(1))
                {
                    ReadPupils(target);
                }
            }

            GUILayout.BeginHorizontal();
            GUILayout.BeginVertical(PadUi.Width(ContentWidth));
            float strip = PadUi.Px(StripSize);
            Rect area = GUILayoutUtility.GetRect(PadUi.Px(ContentWidth), strip + PadUi.Px(MapHeight), GUILayout.ExpandWidth(false));
            Rect pad = new Rect(area.x + strip, area.y + strip, PadUi.Px(MapWidth), PadUi.Px(MapHeight));
            StopButtons(new Rect(pad.x, area.y, pad.width, strip), FacePart.Mouth);
            StopButtons(new Rect(area.x, pad.y, strip, pad.height), FacePart.Eyes);
            MapPad(pad);
            if (HasPupils)
            {
                GazePad(pad, target);
            }

            DrawBrowsAndPupils(target, strip);
            GUILayout.EndVertical();
            if (shownMaking != null)
            {
                DrawMaking(target);
            }

            GUILayout.EndHorizontal();
        }

        // The eyebrow poses (their triangles above the Brows bar) and the pupil bars, two rows of
        // two bars and a button, so that the Map is not taller than the map itself needs.
        void DrawBrowsAndPupils(PadTarget target, float strip)
        {
            Rect browStrip = GUILayoutUtility.GetRect(PadUi.Px(ContentWidth), strip, GUILayout.ExpandWidth(false));
            StopButtons(new Rect(browStrip.x + strip, browStrip.y, PadUi.Px(LowerBarWidth), strip), FacePart.Brows);

            GUILayout.BeginHorizontal();
            GUILayout.Space(strip - 1f);
            float brows = PadUi.Bar("Brows", browPoint, 0f, map.Brows.Count - 1, 0f, "0.0", PadUi.Width(LowerBarWidth));
            float newSpread = spread, newHeight = pupilHeight, newSize = pupilSize;
            bool center = false;
            if (HasPupils)
            {
                newSpread = PadUi.Bar("Spread", spread, -SpreadRange, SpreadRange, 0f, "0.000", PadUi.Width(LowerBarWidth));
                center = GUILayout.Button("Center", PadUi.Button, PadUi.Width(LowerButtonWidth));
            }

            GUILayout.EndHorizontal();

            if (HasPupils)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Space(strip - 1f);
                newHeight = PadUi.Bar("L / R height", pupilHeight, -HeightRange, HeightRange, 0f, "0.000", PadUi.Width(LowerBarWidth));
                newSize = PadUi.Bar("Size", pupilSize, SizeMin, SizeMax, 1f, "0.00", PadUi.Width(LowerBarWidth));
                bool limit = PadUi.Toggle(host.LimitPupils.Value, "Limit", PadUi.Width(LowerButtonWidth));
                if (limit != host.LimitPupils.Value)
                {
                    host.LimitPupils.Value = limit;
                }

                GUILayout.EndHorizontal();
            }

            if (brows != browPoint && !IsMaking(FacePart.Brows))
            {
                browPoint = brows;
                browsDirty = true;
                Edited();
            }

            if (center)
            {
                gaze = Vector2.zero;
                newSpread = 0f;
                newHeight = 0f;
            }

            if (center || newSpread != spread || newHeight != pupilHeight)
            {
                spread = newSpread;
                pupilHeight = newHeight;
                WritePupils(target);
            }

            if (newSize != pupilSize)
            {
                pupilSize = newSize;
                WritePupilSize(target);
            }
        }

        void MapPad(Rect pad)
        {
            int id = GUIUtility.GetControlID(MapHint, FocusType.Passive, pad);
            Event current = Event.current;
            switch (current.GetTypeForControl(id))
            {
                case EventType.MouseDown:
                    if (current.button == 0 && pad.Contains(current.mousePosition))
                    {
                        GUIUtility.hotControl = id;
                        mapLock.Release();
                        MoveMapPoint(pad, mapLock.Pointer(current, MarkerOnScreen(pad)), current.control);
                        current.Use();
                    }
                    else if (current.button == 2 && pad.Contains(current.mousePosition))
                    {
                        PadUi.RequestKey();
                        current.Use();
                    }

                    break;
                case EventType.MouseDrag:
                    if (GUIUtility.hotControl == id)
                    {
                        MoveMapPoint(pad, mapLock.Pointer(current, MarkerOnScreen(pad)), current.control);
                        current.Use();
                    }

                    break;
                case EventType.MouseUp:
                    if (GUIUtility.hotControl == id)
                    {
                        GUIUtility.hotControl = 0;
                        mapLock.Release();
                        current.Use();
                    }

                    break;
                case EventType.Repaint:
                    PadUi.Fill(pad, MapBack);
                    for (int column = 0; column < map.Mouths.Count; column++)
                    {
                        PadUi.Fill(new Rect(pad.x + pad.width * column / (map.Mouths.Count - 1), pad.y, 1, pad.height), MapLine);
                    }

                    for (int row = 0; row < map.Eyes.Count; row++)
                    {
                        PadUi.Fill(new Rect(pad.x, pad.y + pad.height * row / (map.Eyes.Count - 1), pad.width, 1), MapLine);
                    }

                    Vector2 marker = MarkerOnScreen(pad);

                    // The line of the pose that is being made is solid; the other one is faint.
                    Color faint = new Color(MapMarker.r, MapMarker.g, MapMarker.b, 0.45f);
                    bool columnOpen = IsMaking(FacePart.Mouth), rowOpen = IsMaking(FacePart.Eyes);
                    PadUi.Fill(new Rect(marker.x - (columnOpen ? 1f : 0f), pad.y, columnOpen ? 3f : 1f, pad.height), columnOpen ? MapMarker : faint);
                    PadUi.Fill(new Rect(pad.x, marker.y - (rowOpen ? 1f : 0f), pad.width, rowOpen ? 3f : 1f), rowOpen ? MapMarker : faint);
                    float half = PadUi.Px(7f);
                    PadUi.Fill(new Rect(marker.x - half, marker.y - half, half * 2f, half * 2f), MapMarker);
                    break;
            }
        }

        Vector2 MarkerOnScreen(Rect pad)
        {
            return new Vector2(
                pad.x + pad.width * mapPoint.x / (map.Mouths.Count - 1),
                pad.y + pad.height * mapPoint.y / (map.Eyes.Count - 1));
        }

        // Put the map point under "pointer". The pose that is being made stays under the marker:
        // its direction is locked, so a drag tries that pose with every pose of the other direction.
        void MoveMapPoint(Rect pad, Vector2 pointer, bool snap)
        {
            float column = Mathf.Clamp01((pointer.x - pad.x) / pad.width) * (map.Mouths.Count - 1);
            float row = Mathf.Clamp01((pointer.y - pad.y) / pad.height) * (map.Eyes.Count - 1);
            if (snap)
            {
                column = Mathf.Round(column);
                row = Mathf.Round(row);
            }

            if (!IsMaking(FacePart.Mouth) && column != mapPoint.x)
            {
                mapPoint.x = column;
                mouthDirty = true;
            }

            if (!IsMaking(FacePart.Eyes) && row != mapPoint.y)
            {
                mapPoint.y = row;
                eyesDirty = true;
            }

            Edited();
        }

        // Put the map point on one pose and show it on the character.
        void MovePointTo(FacePart part, int stop)
        {
            if (part == FacePart.Eyes)
            {
                mapPoint.y = stop;
                eyesDirty = true;
            }
            else if (part == FacePart.Mouth)
            {
                mapPoint.x = stop;
                mouthDirty = true;
            }
            else
            {
                browPoint = stop;
                browsDirty = true;
            }
        }

        // After poses were added or removed: keep the point inside the map.
        void KeepPointInside()
        {
            mapPoint.x = Mathf.Clamp(mapPoint.x, 0f, map.Mouths.Count - 1);
            mapPoint.y = Mathf.Clamp(mapPoint.y, 0f, map.Eyes.Count - 1);
            browPoint = Mathf.Clamp(browPoint, 0f, map.Brows.Count - 1);
        }

        // Runs in Update, at most once per frame, however many drag events arrived.
        void ApplyPendingMap()
        {
            if (!mouthDirty && !eyesDirty && !browsDirty)
            {
                return;
            }

            bool mouth = mouthDirty, eyes = eyesDirty, brows = browsDirty;
            mouthDirty = eyesDirty = browsDirty = false;
            PadTarget target = Kkpe.Target();
            if (target != null)
            {
                ApplyMap(target, eyes, mouth, brows);
            }
        }

        // Write the chosen parts of the map point to the character. A part sets every Blend Shape
        // it owns, so nothing of the previous expression remains in that part.
        internal void ApplyMap(PadTarget target, bool eyes, bool mouth, bool brows)
        {
            int mouthStart = Kkpe.MouthStart(target);
            foreach (BlendMesh mesh in Kkpe.Meshes(target))
            {
                Mesh shapes = mesh.Renderer.sharedMesh;
                if (mesh.Role == Kkpe.FaceRole)
                {
                    for (int index = 0; index < shapes.blendShapeCount; index++)
                    {
                        bool isMouth = index >= mouthStart;
                        if (isMouth ? mouth : eyes)
                        {
                            string shape = shapes.GetBlendShapeName(index);
                            float weight = isMouth
                                ? ExpressionMap.Blend(map.Mouths, mapPoint.x, shape)
                                : ExpressionMap.Blend(map.Eyes, mapPoint.y, shape);
                            Kkpe.SetWeight(mesh, shape, weight);
                        }
                    }
                }
                else if (mesh.Role == Kkpe.BrowRole && brows)
                {
                    for (int index = 0; index < shapes.blendShapeCount; index++)
                    {
                        string shape = shapes.GetBlendShapeName(index);
                        Kkpe.SetWeight(mesh, shape, ExpressionMap.Blend(map.Brows, browPoint, shape));
                    }
                }
                else if (mesh.Role == Kkpe.TeethRole && mouth && ExpressionMap.TeethShapeIndex < shapes.blendShapeCount)
                {
                    string shape = shapes.GetBlendShapeName(ExpressionMap.TeethShapeIndex);
                    Kkpe.SetWeight(mesh, shape, ExpressionMap.Blend(map.Mouths, mapPoint.x, ExpressionMap.TeethKey));
                }
            }
        }

        // The non-zero weights one part of the face has right now, by Blend Shape name.
        static Dictionary<string, float> CurrentPose(PadTarget target, FacePart part)
        {
            Dictionary<string, float> pose = new Dictionary<string, float>();
            int mouthStart = Kkpe.MouthStart(target);
            foreach (BlendMesh mesh in Kkpe.Meshes(target))
            {
                Mesh shapes = mesh.Renderer.sharedMesh;
                bool wholeMesh = mesh.Role == Kkpe.BrowRole && part == FacePart.Brows;
                bool faceMesh = mesh.Role == Kkpe.FaceRole && part != FacePart.Brows;
                for (int index = 0; (wholeMesh || faceMesh) && index < shapes.blendShapeCount; index++)
                {
                    float weight = Kkpe.Weight(mesh, index);
                    if (weight > NonZero && (wholeMesh || (index >= mouthStart) == (part == FacePart.Mouth)))
                    {
                        pose[shapes.GetBlendShapeName(index)] = weight;
                    }
                }

                if (mesh.Role == Kkpe.TeethRole && part == FacePart.Mouth && ExpressionMap.TeethShapeIndex < shapes.blendShapeCount)
                {
                    float teeth = Kkpe.Weight(mesh, ExpressionMap.TeethShapeIndex);
                    if (teeth > NonZero)
                    {
                        pose[ExpressionMap.TeethKey] = teeth;
                    }
                }
            }

            return pose;
        }
    }
}
