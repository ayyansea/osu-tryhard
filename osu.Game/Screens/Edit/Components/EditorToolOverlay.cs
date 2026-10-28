// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Allocation;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Effects;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterface;
using osu.Game.Input.Bindings;
using osu.Game.Overlays;
using osuTK;

namespace osu.Game.Screens.Edit.Components
{
    /// <summary>
    /// A panel shown over the whole editor, dimming it. Escape or a click on the dimmed part closes it.
    /// While it is open, editor shortcuts don't reach the editor behind it.
    /// </summary>
    public abstract partial class EditorToolOverlay : OsuFocusedOverlayContainer
    {
        protected const float PADDING = 20;

        protected abstract string Title { get; }

        protected override Container<Drawable> Content { get; } = new Container { RelativeSizeAxes = Axes.Both };

        // the editor isn't behind an overlay manager's dim, so this draws its own.
        protected override bool DimMainContent => false;

        protected override string? PopInSampleName => null;
        protected override string? PopOutSampleName => null;

        /// <summary>
        /// The panel's size relative to the editor.
        /// </summary>
        protected virtual Vector2 PanelSize => new Vector2(0.8f, 0.85f);

        [BackgroundDependencyLoader]
        private void load(OverlayColourProvider colourProvider)
        {
            RelativeSizeAxes = Axes.Both;

            InternalChildren = new Drawable[]
            {
                new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = Colour4.Black,
                    Alpha = 0.6f,
                },
                new Panel
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    RelativeSizeAxes = Axes.Both,
                    Size = PanelSize,
                    Masking = true,
                    CornerRadius = 10,
                    EdgeEffect = new EdgeEffectParameters
                    {
                        Type = EdgeEffectType.Shadow,
                        Colour = Colour4.Black.Opacity(0.4f),
                        Radius = 20,
                    },
                    Children = new Drawable[]
                    {
                        new Box
                        {
                            RelativeSizeAxes = Axes.Both,
                            Colour = colourProvider.Background5,
                        },
                        new GridContainer
                        {
                            RelativeSizeAxes = Axes.Both,
                            RowDimensions = new[]
                            {
                                new Dimension(GridSizeMode.Absolute, 50),
                                new Dimension(),
                            },
                            Content = new[]
                            {
                                new Drawable[]
                                {
                                    new Container
                                    {
                                        RelativeSizeAxes = Axes.Both,
                                        Children = new Drawable[]
                                        {
                                            new Box
                                            {
                                                RelativeSizeAxes = Axes.Both,
                                                Colour = colourProvider.Background4,
                                            },
                                            new OsuSpriteText
                                            {
                                                Anchor = Anchor.CentreLeft,
                                                Origin = Anchor.CentreLeft,
                                                X = PADDING,
                                                Text = Title,
                                                Font = OsuFont.Default.With(size: 22, weight: FontWeight.SemiBold),
                                            },
                                            new IconButton
                                            {
                                                Anchor = Anchor.CentreRight,
                                                Origin = Anchor.CentreRight,
                                                X = -10,
                                                Icon = FontAwesome.Solid.Times,
                                                TooltipText = "Close",
                                                Action = Hide,
                                            },
                                        }
                                    },
                                },
                                new Drawable[]
                                {
                                    new Container
                                    {
                                        RelativeSizeAxes = Axes.Both,
                                        Padding = new MarginPadding(PADDING),
                                        Child = Content,
                                    }
                                },
                            }
                        },
                    }
                },
            };
        }

        protected override bool OnClick(ClickEvent e)
        {
            // clicks inside the panel are handled (or swallowed) by it; one reaching here was on the dimmed part.
            Hide();
            return true;
        }

        public override bool OnPressed(KeyBindingPressEvent<GlobalAction> e)
        {
            if (State.Value != Visibility.Visible)
                return false;

            switch (e.Action)
            {
                // the volume keys keep working while the overlay is open.
                case GlobalAction.IncreaseVolume:
                case GlobalAction.DecreaseVolume:
                case GlobalAction.ToggleMute:
                case GlobalAction.NextVolumeMeter:
                case GlobalAction.PreviousVolumeMeter:
                    return false;
            }

            base.OnPressed(e);
            return true;
        }

        private partial class Panel : Container
        {
            // clicks on the panel itself must not reach the overlay, which closes on a click.
            protected override bool OnClick(ClickEvent e) => true;
            protected override bool OnMouseDown(MouseDownEvent e) => true;
        }

        protected override void PopIn()
        {
            this.FadeIn(200, Easing.OutQuint);
        }

        protected override void PopOut()
        {
            base.PopOut();
            this.FadeOut(200, Easing.OutQuint);
        }
    }
}
