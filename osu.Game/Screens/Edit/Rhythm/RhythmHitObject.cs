// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Bindables;
using osu.Game.Overlays;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Objects.Types;
using osuTK.Graphics;

namespace osu.Game.Screens.Edit.Rhythm
{
    /// <summary>
    /// A non-positional "click" in a beatmap's rhythm blueprint.
    /// Rhythm blueprints are a mapping aid for laying out the rhythm of a map before placing actual hit objects.
    /// </summary>
    public class RhythmHitObject : HitObject, IHasDisplayColour
    {
        public Bindable<Color4> DisplayColour { get; } = new Bindable<Color4>(CatppuccinMochaColourProvider.MAUVE);
    }
}
