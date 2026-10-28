// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Bindables;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterface;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Overlays.Volume
{
    /// <summary>
    /// Always visible master / audio (track) / hitsound (sample) volume sliders, which sit at the top centre of the game on every screen.
    /// These are the same bindables the <see cref="VolumeOverlay"/> adjusts, so both stay in sync.
    /// </summary>
    public partial class GlobalVolumeControl : CompositeDrawable
    {
        private const float slider_width = 90;

        [BackgroundDependencyLoader]
        private void load(AudioManager audio)
        {
            Anchor = Anchor.TopCentre;
            Origin = Anchor.TopCentre;
            AutoSizeAxes = Axes.Both;

            Masking = true;
            CornerRadius = 6;

            InternalChildren = new Drawable[]
            {
                new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = Color4.Black.Opacity(0.4f),
                },
                new FillFlowContainer
                {
                    AutoSizeAxes = Axes.Both,
                    Direction = FillDirection.Horizontal,
                    Spacing = new Vector2(14, 0),
                    Padding = new MarginPadding { Horizontal = 10, Vertical = 3 },
                    Children = new Drawable[]
                    {
                        createSlider("master", audio.Volume),
                        createSlider("audio", audio.VolumeTrack),
                        createSlider("hitsounds", audio.VolumeSample),
                    },
                },
            };
        }

        private static Drawable createSlider(string name, BindableNumber<double> volume) => new FillFlowContainer
        {
            AutoSizeAxes = Axes.Both,
            Direction = FillDirection.Horizontal,
            Spacing = new Vector2(6, 0),
            Children = new Drawable[]
            {
                new OsuSpriteText
                {
                    Text = name,
                    Font = OsuFont.GetFont(size: 12),
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft,
                },
                new RoundedSliderBar<double>
                {
                    Width = slider_width,
                    Height = 14,
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft,
                    DisplayAsPercentage = true,
                    Current = { BindTarget = volume },
                },
            },
        };
    }
}
