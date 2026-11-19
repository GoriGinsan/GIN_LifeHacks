using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace GinPadHack
{
    // Map tab, pose editor: making your own map.
    //
    // Every pose of the map has a small triangle: above the map for the mouth columns, to its
    // left for the eye rows, above the Brows bar for the eyebrow poses.
    //
    //   click a triangle         go straight to that pose: the map point moves onto it and the
    //                            character shows it (an open editor of that part closes)
    //   right click a triangle   open the editor: the character shows the pose and its Blend Shapes
    //                            open as bars to the right of the map - the same bars, pins and
    //                            Fav / Used / All choice as on the Face tab. Whatever the bars change
    //                            IS the pose.
    //   right click another      while a pose of the same part is open: the face the character has
    //   triangle of that part    now becomes that pose, which opens (quick way to fill a map)
    //   right click it again,
    //   or Done                  close the editor
    //
    // While a pose is open its direction of the map is locked, so dragging on the map tries the
    // pose with every pose of the other direction. The buttons above the bars move the pose
    // earlier / later, add a copy after it, delete it, undo this visit's changes, or restore the
    // learned defaults. Everything is written to expression-map.txt when the mouse is released.
    public sealed partial class PadWindow
    {
        const int MaxStops = 60;
        const string MapBackupFile = "expression-map.bak.txt";

        static readonly string[] PartNames = { "Eye pose", "Mouth pose", "Brow pose" };
        static readonly List<ShapeRow> NoShapes = new List<ShapeRow>();

        // The pose that is open in the editor.
        sealed class Making
        {
            internal FacePart Part;
            internal int Stop;
            internal Dictionary<string, float> Original;   // the pose as it was when it was opened, for Undo
        }

        // Changes to the map are requested by GUI events and carried out in Update (TickMap),
        // so that the number of poses never changes in the middle of a GUI pass.
        enum MapEdit
        {
            None,
            Select,
            Open,
            StoreCurrentFace,
            Close,
            Earlier,
            Later,
            AddCopy,
            Delete,
            Undo,
            Defaults
        }

        Making making;
        MapEdit requestedEdit;
        FacePart requestedPart;
        int requestedStop;
        bool makingEdited, drawingMaking, mapUnsaved, mapPreviewOnly;
        Vector2 makingScroll;

        bool IsMaking(FacePart part)
        {
            return making != null && making.Part == part;
        }

        void Request(MapEdit edit, FacePart part, int stop)
        {
            requestedEdit = edit;
            requestedPart = part;
            requestedStop = stop;
        }

        // ---- the triangles ---------------------------------------------------------------------

        // One triangle per pose along a strip: a row of them (mouth, brows) or a column (eyes),
        // each centred on its pose's line of the map.
        void StopButtons(Rect strip, FacePart part)
        {
            int count = Stops(part).Count;
            bool column = part == FacePart.Eyes;
            float pitch = (column ? strip.height : strip.width) / (count - 1);
            float thickness = (column ? strip.width : strip.height) - 2f;
            float size = Mathf.Clamp(pitch - 1f, 3f, thickness);
            for (int stop = 0; stop < count; stop++)
            {
                float start = pitch * stop - size / 2f;
                Rect button = column
                    ? new Rect(strip.x, strip.y + start, thickness, size)
                    : new Rect(strip.x + start, strip.y, size, thickness);
                bool open = making != null && making.Part == part && making.Stop == stop;
                int clicked = PadUi.ClickButton(button, open ? PadUi.MarkerOn : PadUi.Marker);
                if (clicked == 0)
                {
                    Request(MapEdit.Select, part, stop);
                }
                else if (clicked == 1)
                {
                    Request(open ? MapEdit.Close : IsMaking(part) ? MapEdit.StoreCurrentFace : MapEdit.Open, part, stop);
                }

                if (Event.current.type == EventType.Repaint)
                {
                    Color previous = GUI.color;
                    GUI.color = open ? new Color(0.1f, 0.1f, 0.1f, 1f) : new Color(1f, 1f, 1f, 0.75f);
                    GUI.DrawTexture(button, column ? PadUi.TriangleRight : PadUi.TriangleDown, ScaleMode.ScaleToFit);
                    GUI.color = previous;
                }
            }
        }

        // ---- the editor to the right of the map --------------------------------------------------

        void DrawMaking(PadTarget target)
        {
            Making shown = shownMaking;
            UpdateFaceLists(target);
            bool rows = shown.Part == FacePart.Eyes;

            GUILayout.BeginVertical(GUILayout.Width(PadUi.Px(ColumnWidth + PadUi.ScrollBarWidth) + 8f));
            GUILayout.BeginHorizontal();
            GUILayout.Label(PartNames[(int)shown.Part] + " " + (shown.Stop + 1) + " / " + Stops(shown.Part).Count, PadUi.Small);
            GUILayout.FlexibleSpace();
            EditButton("Done", 40f, MapEdit.Close);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            EditButton(rows ? "↑" : "←", 22f, MapEdit.Earlier);
            EditButton(rows ? "↓" : "→", 22f, MapEdit.Later);
            EditButton("+", 22f, MapEdit.AddCopy);
            EditButton("-", 22f, MapEdit.Delete);
            EditButton("Undo", 40f, MapEdit.Undo);
            EditButton("Defaults", 58f, MapEdit.Defaults);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            ListButtons();
            GUILayout.EndHorizontal();

            List<ShapeRow> shapes = shown.Part == FacePart.Eyes ? eyeShapes : shown.Part == FacePart.Mouth ? mouthShapes : browShapes;
            List<ShapeRow> teeth = shown.Part == FacePart.Mouth ? mapTeeth : NoShapes;
            float room = PadUi.Px(MapHeight - 10f);   // the editor is no taller than the map column
            float listHeight = ColumnHeight(shapes, teeth);
            BeginColumn(ref makingScroll, listHeight, room);
            drawingMaking = true;
            try
            {
                Section(shown.Part == FacePart.Eyes ? "Eyes" : shown.Part == FacePart.Mouth ? "Mouth" : "Brows", shapes, target);
                Section("Teeth", teeth, target);
            }
            finally
            {
                drawingMaking = false;
            }

            if (shapes.Count == 0 && teeth.Count == 0)
            {
                GUILayout.Label("Nothing listed: choose All", PadUi.Small);   // the short form of NothingListed fits the column
            }

            EndColumn(ref makingScroll, listHeight, room);
            GUILayout.EndVertical();
        }

        void EditButton(string text, float width, MapEdit edit)
        {
            if (GUILayout.Button(text, PadUi.Button, PadUi.Width(width)))
            {
                Request(edit, shownMaking.Part, shownMaking.Stop);
            }
        }

        // ---- carried out in Update ---------------------------------------------------------------

        void TickMap(bool mouseHeld)
        {
            PadTarget target = Kkpe.Target();
            if (requestedEdit != MapEdit.None)
            {
                MapEdit edit = requestedEdit;
                requestedEdit = MapEdit.None;
                PerformMapEdit(target, edit, requestedPart, requestedStop);
            }

            ApplyPendingMap();

            // A bar of the editor moved. The open pose is under the marker, so what the character
            // shows in that part now is the pose.
            if (makingEdited)
            {
                makingEdited = false;
                if (making != null && target != null)
                {
                    Stops(making.Part)[making.Stop] = CurrentPose(target, making.Part);
                    mapUnsaved = true;
                }
            }

            if (mapUnsaved && !mouseHeld)
            {
                mapUnsaved = false;
                SaveMap();
            }
        }

        void PerformMapEdit(PadTarget target, MapEdit edit, FacePart part, int stop)
        {
            if (edit == MapEdit.Select)
            {
                if (IsMaking(part))
                {
                    making = null;
                    faceListStale = true;
                }

                MovePointTo(part, Mathf.Clamp(stop, 0, Stops(part).Count - 1));
                Edited();
                return;
            }

            if (edit == MapEdit.Open || edit == MapEdit.StoreCurrentFace)
            {
                OpenPose(target, part, stop, edit == MapEdit.StoreCurrentFace);
                return;
            }

            if (edit == MapEdit.Defaults)
            {
                RestoreDefaultMap();
                return;
            }

            if (making == null)
            {
                return;
            }

            // A pose is replaced as a whole, never changed in place, so "Original" stays what it was.
            List<Dictionary<string, float>> stops = Stops(making.Part);
            string name = PartNames[(int)making.Part];
            switch (edit)
            {
                case MapEdit.Close:
                    making = null;
                    faceListStale = true;
                    return;
                case MapEdit.Earlier:
                case MapEdit.Later:
                    int other = making.Stop + (edit == MapEdit.Earlier ? -1 : 1);
                    if (other < 0 || other >= stops.Count)
                    {
                        return;
                    }

                    Dictionary<string, float> moved = stops[making.Stop];
                    stops[making.Stop] = stops[other];
                    stops[other] = moved;
                    making.Stop = other;
                    break;
                case MapEdit.AddCopy:
                    if (stops.Count >= MaxStops)
                    {
                        Status("The map holds at most " + MaxStops + " poses in one direction");
                        return;
                    }

                    stops.Insert(making.Stop + 1, new Dictionary<string, float>(stops[making.Stop]));
                    making.Stop++;
                    making.Original = stops[making.Stop];
                    Status(name + " " + (making.Stop + 1) + " added as a copy; change it with the bars");
                    break;
                case MapEdit.Delete:
                    if (stops.Count <= 2)
                    {
                        Status("The map needs at least 2 poses in each direction");
                        return;
                    }

                    stops.RemoveAt(making.Stop);
                    making.Stop = Mathf.Min(making.Stop, stops.Count - 1);
                    making.Original = stops[making.Stop];
                    Status(name + " deleted; " + stops.Count + " left");
                    break;
                case MapEdit.Undo:
                    stops[making.Stop] = making.Original;
                    Status(name + " " + (making.Stop + 1) + " is as it was when it was opened");
                    break;
            }

            mapUnsaved = true;
            faceListStale = true;
            KeepPointInside();
            MovePointTo(making.Part, making.Stop);
        }

        void OpenPose(PadTarget target, FacePart part, int stop, bool storeCurrentFace)
        {
            if (target == null)
            {
                return;
            }

            List<Dictionary<string, float>> stops = Stops(part);
            Making opened = new Making();
            opened.Part = part;
            opened.Stop = Mathf.Clamp(stop, 0, stops.Count - 1);
            opened.Original = stops[opened.Stop];
            string caption = PartNames[(int)part] + " " + (opened.Stop + 1);
            if (storeCurrentFace)
            {
                stops[opened.Stop] = CurrentPose(target, part);
                mapUnsaved = true;
                Status(caption + " is now the face shown here (Undo brings the old pose back)");
            }
            else
            {
                Status(caption + ": the bars change this pose; it is saved by itself");
            }

            making = opened;
            faceListStale = true;
            MovePointTo(part, opened.Stop);
        }

        void SaveMap()
        {
            if (mapPreviewOnly)
            {
                return;   // the isolated test's screenshot setup must not leave a file behind
            }

            try
            {
                map.Save(MapPath);
            }
            catch (Exception error)
            {
                Status("Expression map not saved: " + Root(error).Message);
            }
        }

        // Back to the learned poses. The user's own map is kept beside it as a backup file.
        void RestoreDefaultMap()
        {
            try
            {
                bool hadOwnMap = File.Exists(MapPath);
                if (hadOwnMap)
                {
                    File.Copy(MapPath, Path.Combine(storage, MapBackupFile), true);
                    File.Delete(MapPath);
                }

                map = ExpressionMap.Default();
                making = null;
                mapUnsaved = false;
                faceListStale = true;
                KeepPointInside();
                Status("Map: learned poses restored" + (hadOwnMap ? "; your map is kept as " + MapBackupFile : ""));
            }
            catch (Exception error)
            {
                Status("Expression map not reset: " + Root(error).Message);
            }
        }
    }
}
