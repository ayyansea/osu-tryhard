// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Input.Events;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.Drawables;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Overlays;
using osuTK;

namespace osu.Game.Screens.EditorSelect
{
    /// <summary>
    /// A single difficulty in the difficulty list of <see cref="EditorBeatmapSelect"/>. Clicking opens it in the editor.
    /// </summary>
    public partial class DifficultyRow : OsuClickableContainer
    {
        private readonly BeatmapInfo beatmapInfo;

        private Box background = null!;

        [Resolved]
        private OverlayColourProvider colourProvider { get; set; } = null!;

        public DifficultyRow(BeatmapInfo beatmapInfo)
        {
            this.beatmapInfo = beatmapInfo;
        }

        [BackgroundDependencyLoader]
        private void load(OsuColour colours)
        {
            RelativeSizeAxes = Axes.X;
            Height = EditorBeatmapSelect.ROW_HEIGHT;

            Children = new Drawable[]
            {
                background = new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = colourProvider.Background2,
                    Alpha = 0,
                },
                new Container
                {
                    RelativeSizeAxes = Axes.Both,
                    Padding = new MarginPadding { Horizontal = 15 },
                    Children = new Drawable[]
                    {
                        new DifficultyIcon(beatmapInfo)
                        {
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.CentreLeft,
                            Size = new Vector2(20),
                        },
                        new TruncatingSpriteText
                        {
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.CentreLeft,
                            RelativeSizeAxes = Axes.X,
                            Padding = new MarginPadding { Left = 30, Right = 60 },
                            Text = beatmapInfo.DifficultyName,
                            Font = OsuFont.GetFont(size: 16, weight: FontWeight.SemiBold),
                        },
                        new OsuSpriteText
                        {
                            Anchor = Anchor.CentreRight,
                            Origin = Anchor.CentreRight,
                            Text = $"{beatmapInfo.StarRating:0.00}★",
                            Font = OsuFont.GetFont(size: 14, weight: FontWeight.Bold),
                            Colour = colours.ForStarDifficulty(beatmapInfo.StarRating),
                        },
                    }
                },
            };
        }

        protected override bool OnHover(HoverEvent e)
        {
            background.FadeIn(100, Easing.OutQuint);
            return base.OnHover(e);
        }

        protected override void OnHoverLost(HoverLostEvent e)
        {
            background.FadeOut(100, Easing.OutQuint);
            base.OnHoverLost(e);
        }
    }
}
