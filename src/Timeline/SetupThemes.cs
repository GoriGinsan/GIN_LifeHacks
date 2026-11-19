using System.Collections.Generic;
using Studio;
using Timeline;
using UnityEngine;
using Native = Timeline.Timeline;

namespace GinTimelineHack
{
    // Char Setup, part 3: one colour theme per character, so that the rows of two characters
    // never look alike. The first character keeps the setup scene's blue rows; the second to
    // fifth get rose, green, amber and violet; the sixth starts over with blue.
    //
    // A theme keeps each colour's saturation and brightness and only turns its hue, so the
    // gradation of the setup scene (pale finger rows to the deep character row) stays. White,
    // grey and black rows are the same in every theme.
    //
    // A character that already has Char Setup rows keeps its theme: it is read back from the
    // colour of the character's own position row.
    public sealed partial class Plugin
    {
        internal const int ThemeCount = 5;
        static readonly string[] ThemeNames = { "blue", "rose", "green", "amber", "violet" };
        static readonly float[] ThemeHues = { -1f, 0.96f, 0.36f, 0.10f, 0.77f };   // -1: the scene's own colours

        internal static Color Themed(Color color, int theme)
        {
            float hue, saturation, value;
            Color.RGBToHSV(color, out hue, out saturation, out value);
            if (theme % ThemeCount == 0 || saturation < 0.02f)
            {
                return color;
            }

            Color themed = Color.HSVToRGB(ThemeHues[theme % ThemeCount], saturation, value);
            themed.a = color.a;
            return themed;
        }

        // The character's theme: its own if it has Char Setup rows, otherwise the next one after
        // the characters that are set up already.
        int ThemeFor(OCIChar character)
        {
            Color reference = CharacterRowColor();
            int others = 0;
            Dictionary<ObjectCtrlInfo, bool> counted = new Dictionary<ObjectCtrlInfo, bool>();
            foreach (Interpolable row in Native.GetAllInterpolables(false))
            {
                OCIChar owner = row.oci as OCIChar;
                if (owner == null || row.id != "guideObjectPos" || !ReferenceEquals(row.parameter, owner.guideObject) || counted.ContainsKey(owner))
                {
                    continue;
                }

                int theme = ThemeOf(row.color, reference);
                if (theme < 0)
                {
                    continue;   // a position row that Char Setup did not colour
                }

                if (owner == character)
                {
                    return theme;
                }

                counted[owner] = true;
                others++;
            }

            return others % ThemeCount;
        }

        static int ThemeOf(Color color, Color reference)
        {
            for (int theme = 0; theme < ThemeCount; theme++)
            {
                Color themed = Themed(reference, theme);
                if (Mathf.Abs(themed.r - color.r) < 0.01f && Mathf.Abs(themed.g - color.g) < 0.01f && Mathf.Abs(themed.b - color.b) < 0.01f)
                {
                    return theme;
                }
            }

            return -1;
        }

        // The template's colour of the character's own position row (the deepest blue).
        static Color CharacterRowColor()
        {
            foreach (System.Xml.XmlElement row in SetupTemplate().DocumentElement.SelectNodes("rows/interpolable"))
            {
                if (row.GetAttribute("id") == "guideObjectPos" && row.GetAttribute("bone").Length == 0 && !row.HasAttribute("object"))
                {
                    return ReadVector(row.GetAttribute("color"), Color.white);
                }
            }

            return Color.white;
        }
    }
}
