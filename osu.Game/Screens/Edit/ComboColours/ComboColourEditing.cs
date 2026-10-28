// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using osu.Framework.Graphics;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Objects.Types;

namespace osu.Game.Screens.Edit.ComboColours
{
    /// <summary>
    /// From <see cref="Time"/> on, new combos cycle through <see cref="Colours"/> (indices into the combo colour palette).
    /// A burst section only recolours the first combo at or after its time; the surrounding cycle then carries on.
    /// </summary>
    public class ComboColourSection
    {
        public double Time { get; set; }

        public List<int> Colours { get; set; } = new List<int>();

        public bool Burst { get; set; }
    }

    /// <summary>
    /// A combo start as far as colours are concerned: its time and its current combo colour offset ("colour skip").
    /// </summary>
    public readonly record struct ComboStart(double Time, int Offset);

    public static class ComboColourEditing
    {
        /// <summary>
        /// Allowed distance (in ms) between a section and the first combo it applies to, for rounding in stored times.
        /// </summary>
        public const double TIME_LENIENCY = 1;

        /// <summary>
        /// The .osu format stores the combo colour skip in 3 bits.
        /// </summary>
        public const int MAX_OFFSET = 7;

        /// <summary>
        /// Computes the combo offsets that give every combo the colour its section asks for.
        /// Combos not covered by any section keep their offset.
        /// </summary>
        public static int[] ComputeOffsets(IReadOnlyList<ComboStart> combos, IReadOnlyList<ComboColourSection> sections, int colourCount)
        {
            int[] offsets = combos.Select(c => c.Offset).ToArray();

            if (colourCount <= 0)
                return offsets;

            int?[] desired = DesiredColours(combos, sections, colourCount);
            int previousIndexWithOffsets = 0;

            for (int i = 0; i < combos.Count; i++)
            {
                // compare `IHasComboInformation.UpdateComboInformation` and `EditorBeatmapSkin`: a combo shows palette entry (index with offsets - 1).
                if (desired[i] is int colour)
                    offsets[i] = mod(colour - previousIndexWithOffsets, colourCount);

                previousIndexWithOffsets += offsets[i] + 1;
            }

            return offsets;
        }

        /// <summary>
        /// The palette index each combo is shown with, given its offsets.
        /// </summary>
        public static int[] ColourIndices(IReadOnlyList<int> offsets, int colourCount)
        {
            int[] indices = new int[offsets.Count];

            if (colourCount <= 0)
                return indices;

            int indexWithOffsets = 0;

            for (int i = 0; i < offsets.Count; i++)
            {
                indexWithOffsets += offsets[i] + 1;
                indices[i] = mod(indexWithOffsets - 1, colourCount);
            }

            return indices;
        }

        /// <summary>
        /// The palette index each combo should get according to the sections, or null where no section applies.
        /// </summary>
        public static int?[] DesiredColours(IReadOnlyList<ComboStart> combos, IReadOnlyList<ComboColourSection> sections, int colourCount)
        {
            int?[] desired = new int?[combos.Count];
            bool[] isBurst = new bool[combos.Count];

            foreach (var burst in sections.Where(s => s.Burst))
            {
                int index = firstComboAtOrAfter(combos, burst.Time);

                if (index < 0 || burst.Colours.Count == 0)
                    continue;

                isBurst[index] = true;
                desired[index] = valid(burst.Colours[0], colourCount);
            }

            var cycles = sections.Where(s => !s.Burst).OrderBy(s => s.Time).ToList();
            int cycleIndex = -1;
            int step = 0;

            for (int i = 0; i < combos.Count; i++)
            {
                int previousCycle = cycleIndex;

                while (cycleIndex + 1 < cycles.Count && cycles[cycleIndex + 1].Time <= combos[i].Time + TIME_LENIENCY)
                    cycleIndex++;

                if (cycleIndex != previousCycle)
                    step = 0;

                if (isBurst[i] || cycleIndex < 0)
                    continue;

                var colours = cycles[cycleIndex].Colours;

                if (colours.Count == 0)
                    continue;

                desired[i] = valid(colours[step % colours.Count], colourCount);
                step++;
            }

            return desired;
        }

        /// <summary>
        /// Lists the combo starts of a beatmap, in order, as computed by its ruleset (spinners and breaks included).
        /// </summary>
        public static List<IHasComboInformation> GetComboStarts(IEnumerable<HitObject> hitObjects)
        {
            var starts = new List<IHasComboInformation>();
            // combo indices start at 1; an object that doesn't start a combo (a leading spinner) keeps 0.
            int lastComboIndex = 0;

            foreach (var combo in hitObjects.OfType<IHasComboInformation>())
            {
                if (combo.ComboIndex != lastComboIndex)
                    starts.Add(combo);

                lastComboIndex = combo.ComboIndex;
            }

            return starts;
        }

        /// <summary>
        /// Applies the sections to the combo offsets of the beatmap, as one undo step.
        /// </summary>
        /// <returns>The number of combos that changed.</returns>
        public static int Apply(EditorBeatmap beatmap, IReadOnlyList<ComboColourSection> sections, int colourCount)
        {
            var starts = GetComboStarts(beatmap.HitObjects);
            int[] offsets = ComputeOffsets(starts.Select(s => new ComboStart(((HitObject)s).StartTime, s.ComboOffset)).ToArray(), sections, colourCount);

            int changed = 0;

            beatmap.BeginChange();

            for (int i = 0; i < starts.Count; i++)
            {
                if (starts[i].ComboOffset == offsets[i])
                    continue;

                starts[i].ComboOffset = offsets[i];
                beatmap.Update((HitObject)starts[i]);
                changed++;
            }

            beatmap.EndChange();

            return changed;
        }

        private static int firstComboAtOrAfter(IReadOnlyList<ComboStart> combos, double time)
        {
            for (int i = 0; i < combos.Count; i++)
            {
                if (combos[i].Time >= time - TIME_LENIENCY)
                    return i;
            }

            return -1;
        }

        private static int? valid(int colour, int colourCount) => colour >= 0 && colour < colourCount ? colour : null;

        private static int mod(int value, int divisor) => ((value % divisor) + divisor) % divisor;

        #region Palette generation

        /// <summary>
        /// Evenly interpolates between two colours (in HSV, the short way round the hue circle, so it doesn't go through grey).
        /// </summary>
        public static List<Colour4> Gradient(Colour4 from, Colour4 to, int count)
        {
            var (h1, s1, v1) = ToHsv(from);
            var (h2, s2, v2) = ToHsv(to);

            // a grey end has no meaningful hue: take the other end's.
            if (s1 < 0.01f) h1 = h2;
            if (s2 < 0.01f) h2 = h1;

            float hueDelta = h2 - h1;
            if (hueDelta > 180) hueDelta -= 360;
            if (hueDelta < -180) hueDelta += 360;

            var colours = new List<Colour4>();

            for (int i = 0; i < count; i++)
            {
                float t = count == 1 ? 0 : (float)i / (count - 1);
                colours.Add(FromHsv(h1 + hueDelta * t, s1 + (s2 - s1) * t, v1 + (v2 - v1) * t));
            }

            return colours;
        }

        /// <summary>
        /// Spreads <paramref name="count"/> colours evenly around the hue circle, keeping the saturation and value of <paramref name="baseColour"/>.
        /// </summary>
        public static List<Colour4> HueSpread(Colour4 baseColour, int count)
        {
            var (h, s, v) = ToHsv(baseColour);
            return Enumerable.Range(0, count).Select(i => FromHsv(h + 360f * i / count, s, v)).ToList();
        }

        /// <summary>
        /// Steps the brightness of <paramref name="baseColour"/> from full down to a third, keeping its hue.
        /// </summary>
        public static List<Colour4> Shades(Colour4 baseColour, int count)
        {
            var (h, s, _) = ToHsv(baseColour);
            return Enumerable.Range(0, count).Select(i => FromHsv(h, s, 1 - (2 / 3f) * (count == 1 ? 0 : (float)i / (count - 1)))).ToList();
        }

        /// <returns>Hue in degrees [0, 360), saturation and value in [0, 1].</returns>
        public static (float h, float s, float v) ToHsv(Colour4 colour)
        {
            float max = Math.Max(colour.R, Math.Max(colour.G, colour.B));
            float min = Math.Min(colour.R, Math.Min(colour.G, colour.B));
            float delta = max - min;

            float h = 0;

            if (delta > 0)
            {
                if (max == colour.R)
                    h = 60 * (((colour.G - colour.B) / delta) % 6);
                else if (max == colour.G)
                    h = 60 * ((colour.B - colour.R) / delta + 2);
                else
                    h = 60 * ((colour.R - colour.G) / delta + 4);
            }

            if (h < 0) h += 360;

            return (h, max == 0 ? 0 : delta / max, max);
        }

        public static Colour4 FromHsv(float h, float s, float v)
        {
            h = ((h % 360) + 360) % 360;
            s = Math.Clamp(s, 0, 1);
            v = Math.Clamp(v, 0, 1);

            float c = v * s;
            float x = c * (1 - Math.Abs((h / 60) % 2 - 1));
            float m = v - c;

            (float r, float g, float b) = (int)(h / 60) switch
            {
                0 => (c, x, 0f),
                1 => (x, c, 0f),
                2 => (0f, c, x),
                3 => (0f, x, c),
                4 => (x, 0f, c),
                _ => (c, 0f, x),
            };

            return new Colour4(r + m, g + m, b + m, 1);
        }

        #endregion
    }

    /// <summary>
    /// Reads and writes combo colour sections. One section per line: "time|burst|colour,colour,...", colours being 0-based palette indices.
    /// </summary>
    public static class ComboColourSerializer
    {
        private const string header = @"osu!tryhard combo colours v1";

        public static void Write(IEnumerable<ComboColourSection> sections, TextWriter writer)
        {
            writer.WriteLine(header);

            foreach (var section in sections.OrderBy(s => s.Time))
            {
                writer.WriteLine(string.Join('|',
                    section.Time.ToString(CultureInfo.InvariantCulture),
                    section.Burst ? "1" : "0",
                    string.Join(',', section.Colours.Select(c => c.ToString(CultureInfo.InvariantCulture)))));
            }
        }

        public static string Write(IEnumerable<ComboColourSection> sections)
        {
            using (var writer = new StringWriter())
            {
                Write(sections, writer);
                return writer.ToString();
            }
        }

        public static List<ComboColourSection> Read(TextReader reader)
        {
            var sections = new List<ComboColourSection>();

            while (reader.ReadLine() is string line)
            {
                line = line.Trim();

                if (line.Length == 0 || line == header)
                    continue;

                string[] parts = line.Split('|');

                if (parts.Length < 3 || !double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double time))
                    continue;

                var colours = new List<int>();

                foreach (string colour in parts[2].Split(',', StringSplitOptions.RemoveEmptyEntries))
                {
                    if (int.TryParse(colour, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index) && index >= 0)
                        colours.Add(index);
                }

                sections.Add(new ComboColourSection { Time = time, Burst = parts[1] == "1", Colours = colours });
            }

            return sections;
        }

        public static List<ComboColourSection> Read(string text)
        {
            using (var reader = new StringReader(text))
                return Read(reader);
        }
    }
}
