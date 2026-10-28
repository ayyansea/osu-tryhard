// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using osu.Game.Beatmaps.ControlPoints;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Objects.Types;
using osu.Game.Rulesets.Osu.Objects;
using osuTK;

namespace osu.Game.Rulesets.Osu.Edit.SliderGallery
{
    /// <summary>
    /// A slider shape saved to the gallery: its path relative to the head at its actual size, and what it was made for (length in beats, repeats).
    /// </summary>
    public class SliderShape
    {
        /// <summary>
        /// Identifies the shape's file in the gallery.
        /// </summary>
        public Guid ID { get; set; } = Guid.NewGuid();

        public string Name { get; set; } = string.Empty;

        public List<string> Tags { get; set; } = new List<string>();

        /// <summary>
        /// The map the shape was saved from ("Artist - Title (Mapper) [Difficulty]"), editable.
        /// </summary>
        public string OriginalMap { get; set; } = string.Empty;

        /// <summary>
        /// Favourites are listed in their own section at the top of the gallery.
        /// </summary>
        public bool Favourite { get; set; }

        /// <summary>
        /// Control points relative to the head (the first one is at the origin).
        /// </summary>
        public List<(Vector2 Position, PathType? Type)> ControlPoints { get; set; } = new List<(Vector2, PathType?)>();

        /// <summary>
        /// The path length in osu! pixels.
        /// </summary>
        public double Length { get; set; }

        public int RepeatCount { get; set; }

        /// <summary>
        /// The duration of the slider it was saved from, in beats of the timing at its start.
        /// </summary>
        public double Beats { get; set; }

        public static SliderShape FromSlider(Slider slider, ControlPointInfo controlPoints, string name, string originalMap = "")
        {
            var head = slider.Path.ControlPoints[0].Position;

            return new SliderShape
            {
                Name = name,
                OriginalMap = originalMap,
                ControlPoints = slider.Path.ControlPoints.Select(p => (p.Position - head, p.Type)).ToList(),
                Length = slider.Path.Distance,
                RepeatCount = slider.RepeatCount,
                Beats = slider.Duration / controlPoints.TimingPointAt(slider.StartTime).BeatLength,
            };
        }

        /// <summary>
        /// Creates the path of this shape, transformed about the head.
        /// </summary>
        /// <param name="scale">Uniform scale, which also scales the length.</param>
        /// <param name="rotation">Rotation in degrees, clockwise on screen.</param>
        /// <param name="flipX">Mirrors the shape horizontally (before rotating).</param>
        /// <param name="flipY">Mirrors the shape vertically (before rotating).</param>
        public SliderPath CreatePath(float scale = 1, float rotation = 0, bool flipX = false, bool flipY = false)
        {
            float radians = MathHelper.DegreesToRadians(rotation);
            float cos = MathF.Cos(radians);
            float sin = MathF.Sin(radians);

            var points = ControlPoints.Select(p =>
            {
                var position = p.Position * scale;
                if (flipX) position.X = -position.X;
                if (flipY) position.Y = -position.Y;

                // osu! space has y pointing down, so this rotates clockwise on screen.
                position = new Vector2(position.X * cos - position.Y * sin, position.X * sin + position.Y * cos);
                return new PathControlPoint(position, p.Type);
            });

            return new SliderPath(points.ToArray(), Length * scale);
        }
    }

    /// <summary>
    /// Reads and writes slider shapes as small "key=value" text files, so they can be shared and edited by hand.
    /// </summary>
    public static class SliderShapeSerializer
    {
        private const string header = @"osu!tryhard slider v1";

        public static void Write(SliderShape shape, TextWriter writer)
        {
            writer.WriteLine(header);
            writer.WriteLine($"id={shape.ID}");
            writer.WriteLine($"name={shape.Name.Replace('\n', ' ')}");
            writer.WriteLine($"tags={string.Join(',', shape.Tags)}");
            writer.WriteLine($"map={shape.OriginalMap.Replace('\n', ' ')}");
            writer.WriteLine($"favourite={(shape.Favourite ? "1" : "0")}");
            writer.WriteLine($"repeats={shape.RepeatCount.ToString(CultureInfo.InvariantCulture)}");
            writer.WriteLine($"length={shape.Length.ToString(CultureInfo.InvariantCulture)}");
            writer.WriteLine($"beats={shape.Beats.ToString(CultureInfo.InvariantCulture)}");
            writer.WriteLine($"points={string.Join('|', shape.ControlPoints.Select(writePoint))}");
        }

        public static string Write(SliderShape shape)
        {
            using (var writer = new StringWriter())
            {
                Write(shape, writer);
                return writer.ToString();
            }
        }

        /// <returns>The shape, or null if the text isn't a valid shape.</returns>
        public static SliderShape? Read(TextReader reader)
        {
            var shape = new SliderShape();

            if (reader.ReadLine()?.Trim() != header)
                return null;

            while (reader.ReadLine() is string line)
            {
                int separator = line.IndexOf('=');

                if (separator < 0)
                    continue;

                string key = line[..separator].Trim();
                string value = line[(separator + 1)..].Trim();

                switch (key)
                {
                    case "id":
                        if (Guid.TryParse(value, out var id))
                            shape.ID = id;
                        break;

                    case "name":
                        shape.Name = value;
                        break;

                    case "tags":
                        shape.Tags = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
                        break;

                    case "map":
                        shape.OriginalMap = value;
                        break;

                    case "favourite":
                        shape.Favourite = value == "1";
                        break;

                    case "repeats":
                        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int repeats))
                            shape.RepeatCount = Math.Max(0, repeats);
                        break;

                    case "length":
                        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double length))
                            shape.Length = length;
                        break;

                    case "beats":
                        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double beats))
                            shape.Beats = beats;
                        break;

                    case "points":
                        foreach (string point in value.Split('|', StringSplitOptions.RemoveEmptyEntries))
                        {
                            if (readPoint(point) is { } parsed)
                                shape.ControlPoints.Add(parsed);
                        }

                        break;
                }
            }

            if (shape.ControlPoints.Count < 2 || shape.Length <= 0)
                return null;

            return shape;
        }

        public static SliderShape? Read(string text)
        {
            using (var reader = new StringReader(text))
                return Read(reader);
        }

        private static string writePoint((Vector2 Position, PathType? Type) point)
        {
            string position = $"{point.Position.X.ToString(CultureInfo.InvariantCulture)},{point.Position.Y.ToString(CultureInfo.InvariantCulture)}";
            return point.Type is PathType type ? $"{position},{writeType(type)}" : position;
        }

        private static (Vector2, PathType?)? readPoint(string text)
        {
            string[] parts = text.Split(',');

            if (parts.Length < 2
                || !float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x)
                || !float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y))
                return null;

            return (new Vector2(x, y), parts.Length > 2 ? readType(parts[2]) : null);
        }

        private static string writeType(PathType type) => type.Type switch
        {
            SplineType.Linear => "L",
            SplineType.PerfectCurve => "P",
            SplineType.Catmull => "C",
            _ => type.Degree is int degree ? $"B{degree.ToString(CultureInfo.InvariantCulture)}" : "B",
        };

        private static PathType? readType(string text) => text switch
        {
            "L" => PathType.LINEAR,
            "P" => PathType.PERFECT_CURVE,
            "C" => PathType.CATMULL,
            "B" => PathType.BEZIER,
            _ when text.StartsWith('B') && int.TryParse(text[1..], NumberStyles.Integer, CultureInfo.InvariantCulture, out int degree) && degree > 0 => PathType.BSpline(degree),
            _ => null,
        };
    }
}
