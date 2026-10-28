// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.ComponentModel;
using System.Linq;
using osu.Game.Audio;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.ControlPoints;
using osu.Game.Rulesets.Objects.Types;
using osu.Game.Rulesets.Osu.Objects;

namespace osu.Game.Rulesets.Osu.Edit.SliderGallery
{
    public enum SliderFitMode
    {
        /// <summary>
        /// Keep the slider velocity and resize the shape until its length gives the duration.
        /// </summary>
        [Description("Scale to fit (keep velocity)")]
        Scale,

        /// <summary>
        /// Keep the shape's size and change the slider velocity of this slider until it gives the duration.
        /// </summary>
        [Description("Keep size (change velocity)")]
        KeepSize,
    }

    /// <summary>
    /// How a shape transformation is applied on placement: rotation and mirroring about the head.
    /// </summary>
    public readonly record struct SliderShapeTransform(float Rotation = 0, bool FlipX = false, bool FlipY = false);

    public static class SliderShapeFitting
    {
        public const double MIN_VELOCITY = 0.1;
        public const double MAX_VELOCITY = 10;

        /// <summary>
        /// Gives <paramref name="slider"/> the shape and makes it last <paramref name="duration"/> ms from its current start time.
        /// </summary>
        /// <param name="slider">The slider, with its start time already set. Its repeat count is kept.</param>
        /// <param name="shape">The shape to use.</param>
        /// <param name="duration">The total duration to fit, including repeats.</param>
        /// <param name="mode">How to make the duration fit.</param>
        /// <param name="velocity">The slider velocity multiplier to start from; <see cref="SliderFitMode.Scale"/> keeps it.</param>
        /// <param name="transform">Rotation and mirroring.</param>
        public static void Fit(Slider slider, SliderShape shape, double duration, SliderFitMode mode, double velocity, SliderShapeTransform transform,
                               ControlPointInfo controlPoints, IBeatmapDifficultyInfo difficulty)
        {
            int spans = slider.RepeatCount + 1;

            slider.SliderVelocityMultiplier = velocity;
            slider.Path = shape.CreatePath(1, transform.Rotation, transform.FlipX, transform.FlipY);
            slider.ApplyDefaults(controlPoints, difficulty);

            if (duration <= 0 || shape.Length <= 0)
                return;

            // path distance travelled per ms at the starting velocity.
            double pixelsPerMs = slider.Velocity;

            switch (mode)
            {
                case SliderFitMode.Scale:
                {
                    double length = pixelsPerMs * duration / spans;
                    slider.Path = shape.CreatePath((float)(length / shape.Length), transform.Rotation, transform.FlipX, transform.FlipY);
                    // the scaled float points don't give the exact length back; the expected distance makes the duration exact.
                    slider.Path.ExpectedDistance.Value = length;
                    break;
                }

                case SliderFitMode.KeepSize:
                {
                    // velocity scales the pixels per ms linearly.
                    double wanted = velocity * (shape.Length * spans / duration) / pixelsPerMs;
                    double clamped = Math.Clamp(wanted, MIN_VELOCITY, MAX_VELOCITY);

                    slider.SliderVelocityMultiplier = clamped;

                    // out of the velocity range: keep the closest velocity and trim or extend the path to get the duration anyway.
                    if (clamped != wanted)
                        slider.Path.ExpectedDistance.Value = pixelsPerMs * clamped / velocity * duration / spans;

                    break;
                }
            }

            slider.ApplyDefaults(controlPoints, difficulty);
        }

        /// <summary>
        /// Gives a new slider one sample list per node (head, repeats, tail), copied from its samples, like a decoded slider has.
        /// </summary>
        public static void ResetNodeSamples(Slider slider)
        {
            slider.NodeSamples = Enumerable.Range(0, slider.RepeatCount + 2)
                                           .Select(_ => (System.Collections.Generic.IList<HitSampleInfo>)slider.Samples.Select(s => s.With()).ToList())
                                           .ToList();
        }
    }
}
