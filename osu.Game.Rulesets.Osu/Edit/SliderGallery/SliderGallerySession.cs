// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using osu.Framework.Bindables;
using osu.Framework.Utils;
using osu.Game.Beatmaps.ControlPoints;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Objects.Types;

namespace osu.Game.Rulesets.Osu.Edit.SliderGallery
{
    public enum SliderDurationSource
    {
        [Description("The shape's own length in beats")]
        Shape,

        [Description("The rhythm blueprint hold at the placement time")]
        Rhythm,

        [Description("A fixed number of beats")]
        Beats,
    }

    /// <summary>
    /// The gallery state of one compose screen: the shapes, the shape being placed and the fitting settings.
    /// Shared by the overlay, the placement tool and its blueprints.
    /// </summary>
    public class SliderGallerySession
    {
        /// <summary>
        /// How close a rhythm hold has to start to the placement time to be used, in ms.
        /// </summary>
        public const double RHYTHM_LENIENCY = 2;

        public readonly SliderGalleryStore Store;

        public readonly Bindable<SliderShape?> ActiveShape = new Bindable<SliderShape?>();

        public readonly Bindable<SliderFitMode> FitMode = new Bindable<SliderFitMode>();

        public readonly Bindable<SliderDurationSource> DurationSource = new Bindable<SliderDurationSource>();

        public readonly BindableDouble Beats = new BindableDouble(2)
        {
            MinValue = 0.25,
            MaxValue = 16,
            Precision = 0.25,
        };

        public readonly Bindable<SliderShapeTransform> Transform = new Bindable<SliderShapeTransform>();

        /// <summary>
        /// Shows the gallery.
        /// </summary>
        public Action? OpenRequested { get; set; }

        /// <summary>
        /// Starts placing the given shape.
        /// </summary>
        public Action<SliderShape>? PlaceRequested { get; set; }

        /// <summary>
        /// The objects of the rhythm blueprint, if there is one.
        /// </summary>
        private readonly Func<IEnumerable<HitObject>> rhythmObjects;

        public SliderGallerySession(SliderGalleryStore store, Func<IEnumerable<HitObject>> rhythmObjects)
        {
            Store = store;
            this.rhythmObjects = rhythmObjects;
        }

        /// <summary>
        /// The duration a shape placed at <paramref name="startTime"/> gets, according to <see cref="DurationSource"/>.
        /// </summary>
        public double GetDuration(SliderShape shape, double startTime, ControlPointInfo controlPoints)
        {
            double beatLength = controlPoints.TimingPointAt(startTime).BeatLength;

            switch (DurationSource.Value)
            {
                case SliderDurationSource.Rhythm:
                    if (FindRhythmHold(startTime) is IHasDuration hold)
                        return hold.Duration;

                    // no hold here: the shape's own length is the least surprising fallback.
                    return shape.Beats * beatLength;

                case SliderDurationSource.Beats:
                    return Beats.Value * beatLength;

                default:
                    return shape.Beats * beatLength;
            }
        }

        public IHasDuration? FindRhythmHold(double startTime) =>
            rhythmObjects().OfType<IHasDuration>()
                           .FirstOrDefault(h => h.Duration > 0 && Precision.AlmostEquals(((HitObject)h).StartTime, startTime, RHYTHM_LENIENCY));
    }
}
