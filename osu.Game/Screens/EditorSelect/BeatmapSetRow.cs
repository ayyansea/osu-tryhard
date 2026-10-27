// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Pooling;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Input.Events;
using osu.Game.Beatmaps;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Overlays;

namespace osu.Game.Screens.EditorSelect
{
    /// <summary>
    /// A single row in the beatmap set list of <see cref="EditorBeatmapSelect"/>.
    /// </summary>
    public partial class BeatmapSetRow : PoolableDrawable, IHasCurrentValue<BeatmapSetInfo>
    {
        private readonly BindableWithCurrent<BeatmapSetInfo> current = new BindableWithCurrent<BeatmapSetInfo>();

        private readonly Bindable<BeatmapSetInfo?> selectedSet = new Bindable<BeatmapSetInfo?>();

        private Box background = null!;
        private OsuSpriteText titleText = null!;
        private OsuSpriteText detailsText = null!;

        [Resolved]
        private OverlayColourProvider colourProvider { get; set; } = null!;

        public Bindable<BeatmapSetInfo> Current
        {
            get => current.Current;
            set => current.Current = value;
        }

        [BackgroundDependencyLoader]
        private void load(EditorBeatmapSelect screen)
        {
            RelativeSizeAxes = Axes.X;
            Height = EditorBeatmapSelect.ROW_HEIGHT;

            InternalChildren = new Drawable[]
            {
                background = new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Alpha = 0,
                },
                new FillFlowContainer
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft,
                    Direction = FillDirection.Vertical,
                    Padding = new MarginPadding { Horizontal = 20 },
                    Children = new Drawable[]
                    {
                        titleText = new TruncatingSpriteText
                        {
                            RelativeSizeAxes = Axes.X,
                            Font = OsuFont.GetFont(size: 16, weight: FontWeight.SemiBold),
                        },
                        detailsText = new TruncatingSpriteText
                        {
                            RelativeSizeAxes = Axes.X,
                            Font = OsuFont.GetFont(size: 12),
                            Colour = colourProvider.Content2,
                        },
                    }
                },
            };

            selectedSet.BindTo(screen.SelectedSet);
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            selectedSet.BindValueChanged(_ => updateState());
            Current.BindValueChanged(_ => updateState(), true);
            FinishTransforms(true);
        }

        protected override bool OnHover(HoverEvent e)
        {
            updateState();
            return true;
        }

        protected override void OnHoverLost(HoverLostEvent e)
        {
            updateState();
            base.OnHoverLost(e);
        }

        protected override bool OnClick(ClickEvent e)
        {
            selectedSet.Value = Current.Value;
            return true;
        }

        private void updateState()
        {
            var set = Current.Value;

            if (set == null)
                return;

            var metadata = set.Metadata;

            titleText.Text = $"{metadata.Artist} - {metadata.Title}";

            int count = set.Beatmaps.Count;
            detailsText.Text = $"mapped by {metadata.Author.Username} · {count} {(count == 1 ? "difficulty" : "difficulties")}";

            bool isSelected = selectedSet.Value?.ID == set.ID;

            if (IsHovered || isSelected)
                background.FadeIn(100, Easing.OutQuint);
            else
                background.FadeOut(100, Easing.OutQuint);

            background.Colour = isSelected ? colourProvider.Background2 : colourProvider.Background4;
            titleText.Colour = isSelected ? colourProvider.Highlight1 : colourProvider.Content1;
        }
    }
}
