// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Game.Overlays;
using osu.Game.Rulesets.Objects.Types;

namespace osu.Game.Screens.Edit.Rhythm
{
    /// <summary>
    /// A non-positional "hold" in a beatmap's rhythm blueprint.
    /// </summary>
    public class RhythmHold : RhythmHitObject, IHasDuration
    {
        public double EndTime => StartTime + Duration;

        public double Duration { get; set; }

        public RhythmHold()
        {
            DisplayColour.Value = CatppuccinMochaColourProvider.TEAL;
        }
    }
}
