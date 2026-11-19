using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace GinPadHack
{
    // The data behind the Map tab: three ordered lists of poses ("stops").
    //
    //   Eyes    rows of the map     - eye Blend Shapes of the face renderer
    //   Mouths  columns of the map  - mouth Blend Shapes of the face renderer, and the teeth row
    //   Brows   the strip below     - Blend Shapes of the eyebrow renderer
    //
    // A stop holds only its non-zero weights by Blend Shape name; a shape that a stop does not
    // name is 0 there. A position between two stops blends them evenly, so a point of the map
    // is always "eyes between two neighbouring rows" plus "mouth between two neighbouring columns".
    //
    // The default stops were learned from finished scenes (DefaultExpressionMap.cs). A stop that
    // the user replaces is kept in expression-map.txt, one stop per line:
    //   eyes<TAB>shape=weight<TAB>shape=weight ...
    internal sealed class ExpressionMap
    {
        internal const string TeethKey = "#teeth";   // weight of the single teeth row
        internal const int TeethShapeIndex = 29;     // its Blend Shape index on cf_O_tooth

        internal readonly List<Dictionary<string, float>> Eyes = new List<Dictionary<string, float>>();
        internal readonly List<Dictionary<string, float>> Mouths = new List<Dictionary<string, float>>();
        internal readonly List<Dictionary<string, float>> Brows = new List<Dictionary<string, float>>();

        internal static ExpressionMap Default()
        {
            ExpressionMap map = new ExpressionMap();
            AddStops(map.Eyes, DefaultExpressionMap.EyeShapes, DefaultExpressionMap.Eyes);
            AddStops(map.Mouths, DefaultExpressionMap.MouthShapes, DefaultExpressionMap.Mouths);
            AddStops(map.Brows, DefaultExpressionMap.BrowShapes, DefaultExpressionMap.Brows);
            for (int i = 0; i < map.Mouths.Count; i++)
            {
                if (DefaultExpressionMap.Teeth[i] != 0f)
                {
                    map.Mouths[i][TeethKey] = DefaultExpressionMap.Teeth[i];
                }
            }

            return map;
        }

        static void AddStops(List<Dictionary<string, float>> stops, string[] shapes, float[][] weights)
        {
            foreach (float[] row in weights)
            {
                Dictionary<string, float> stop = new Dictionary<string, float>();
                for (int i = 0; i < shapes.Length; i++)
                {
                    if (row[i] != 0f)
                    {
                        stop[shapes[i]] = row[i];
                    }
                }

                stops.Add(stop);
            }
        }

        // Weight of one Blend Shape at a position along a list of stops (0 = first stop).
        internal static float Blend(List<Dictionary<string, float>> stops, float position, string shape)
        {
            int low = Mathf.Clamp(Mathf.FloorToInt(position), 0, stops.Count - 1);
            int high = Math.Min(low + 1, stops.Count - 1);
            float from, to;
            stops[low].TryGetValue(shape, out from);
            stops[high].TryGetValue(shape, out to);
            return Mathf.Lerp(from, to, position - low);
        }

        // ---- file ----------------------------------------------------------------------------

        // The saved map, or the default one when the file is missing, unreadable or incomplete.
        internal static ExpressionMap Load(string path)
        {
            if (!File.Exists(path))
            {
                return Default();
            }

            ExpressionMap map = new ExpressionMap();
            foreach (string line in File.ReadAllLines(path))
            {
                string[] parts = line.Split('\t');
                List<Dictionary<string, float>> stops = parts[0] == "eyes" ? map.Eyes : parts[0] == "mouth" ? map.Mouths : parts[0] == "brows" ? map.Brows : null;
                if (stops == null)
                {
                    continue;   // comment or blank line
                }

                Dictionary<string, float> stop = new Dictionary<string, float>();
                for (int i = 1; i < parts.Length; i++)
                {
                    int equals = parts[i].LastIndexOf('=');
                    float weight;
                    if (equals > 0 && float.TryParse(parts[i].Substring(equals + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out weight))
                    {
                        stop[parts[i].Substring(0, equals)] = weight;
                    }
                }

                stops.Add(stop);
            }

            return map.Eyes.Count >= 2 && map.Mouths.Count >= 2 && map.Brows.Count >= 2 ? map : Default();
        }

        internal void Save(string path)
        {
            List<string> lines = new List<string>();
            lines.Add("# GIN Pad life hack expression map. One stop per line, in map order. Delete this file to return to the defaults.");
            AddLines(lines, "eyes", Eyes);
            AddLines(lines, "mouth", Mouths);
            AddLines(lines, "brows", Brows);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllLines(path, lines.ToArray());
        }

        static void AddLines(List<string> lines, string kind, List<Dictionary<string, float>> stops)
        {
            foreach (Dictionary<string, float> stop in stops)
            {
                string line = kind;
                foreach (KeyValuePair<string, float> weight in stop)
                {
                    line += "\t" + weight.Key + "=" + weight.Value.ToString("0.##", CultureInfo.InvariantCulture);
                }

                lines.Add(line);
            }
        }
    }
}
