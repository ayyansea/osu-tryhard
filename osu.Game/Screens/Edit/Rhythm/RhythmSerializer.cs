// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Objects.Types;

namespace osu.Game.Screens.Edit.Rhythm
{
    /// <summary>
    /// Reads and writes rhythm blueprints in a simple line-based text format.
    /// Each line is either "start" for a click, or "start,duration" for a hold (in milliseconds).
    /// </summary>
    public static class RhythmSerializer
    {
        private const string header = @"osu!tryhard rhythm v1";

        public static void Write(IEnumerable<HitObject> hitObjects, TextWriter writer)
        {
            writer.WriteLine(header);

            foreach (var hitObject in hitObjects.OrderBy(h => h.StartTime))
            {
                string startTime = hitObject.StartTime.ToString(CultureInfo.InvariantCulture);

                if (hitObject is IHasDuration hold)
                    writer.WriteLine($"{startTime},{hold.Duration.ToString(CultureInfo.InvariantCulture)}");
                else
                    writer.WriteLine(startTime);
            }
        }

        public static string Write(IEnumerable<HitObject> hitObjects)
        {
            using (var writer = new StringWriter())
            {
                Write(hitObjects, writer);
                return writer.ToString();
            }
        }

        public static List<RhythmHitObject> Read(TextReader reader)
        {
            var hitObjects = new List<RhythmHitObject>();

            while (reader.ReadLine() is string line)
            {
                line = line.Trim();

                if (line.Length == 0 || line == header)
                    continue;

                string[] parts = line.Split(',');

                if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double startTime))
                    continue;

                if (parts.Length > 1 && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double duration) && duration > 0)
                    hitObjects.Add(new RhythmHold { StartTime = startTime, Duration = duration });
                else
                    hitObjects.Add(new RhythmHitObject { StartTime = startTime });
            }

            return hitObjects;
        }

        public static List<RhythmHitObject> Read(string text)
        {
            using (var reader = new StringReader(text))
                return Read(reader);
        }
    }
}
