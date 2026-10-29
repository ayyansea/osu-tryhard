// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Pooling;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Input.Events;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterface;
using osu.Game.Overlays;
using osu.Game.Rulesets;
using osu.Game.Screens.Edit.Components;
using osuTK;

namespace osu.Game.Screens.EditorSelect
{
    /// <summary>
    /// Lists the beatmap sets of the osu!lazer installation on this machine. Clicking one imports it.
    /// </summary>
    [Cached]
    public partial class LazerImportOverlay : EditorToolOverlay
    {
        protected override string Title => "Import from osu!lazer";

        protected override Vector2 PanelSize => new Vector2(0.6f, 0.85f);

        private readonly Action<LazerSet> import;

        private readonly BindableList<LazerSet> shownSets = new BindableList<LazerSet>();

        private List<LazerSet> allSets = new List<LazerSet>();

        private SearchTextBox search = null!;
        private OsuSpriteText status = null!;

        [Resolved]
        private OverlayColourProvider colourProvider { get; set; } = null!;

        [Resolved]
        private RulesetStore rulesets { get; set; } = null!;

        public LazerImportOverlay(Action<LazerSet> import)
        {
            this.import = import;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            Child = new GridContainer
            {
                RelativeSizeAxes = Axes.Both,
                RowDimensions = new[]
                {
                    new Dimension(GridSizeMode.AutoSize),
                    new Dimension(GridSizeMode.Absolute, PADDING),
                    new Dimension(),
                },
                Content = new[]
                {
                    new Drawable[]
                    {
                        new FillFlowContainer
                        {
                            RelativeSizeAxes = Axes.X,
                            AutoSizeAxes = Axes.Y,
                            Direction = FillDirection.Horizontal,
                            Spacing = new Vector2(PADDING),
                            Children = new Drawable[]
                            {
                                search = new SearchTextBox
                                {
                                    Width = 400,
                                    PlaceholderText = "search by artist, title or mapper",
                                    HoldFocus = false,
                                },
                                status = new OsuSpriteText
                                {
                                    Anchor = Anchor.CentreLeft,
                                    Origin = Anchor.CentreLeft,
                                    Font = OsuFont.GetFont(size: 14),
                                    Colour = colourProvider.Content2,
                                },
                            }
                        },
                    },
                    new Drawable[] { Empty() },
                    new Drawable[]
                    {
                        new SetList
                        {
                            RelativeSizeAxes = Axes.Both,
                            RowData = { BindTarget = shownSets },
                        },
                    },
                }
            };

            search.Current.BindValueChanged(_ => Scheduler.AddOnce(updateFilter));
        }

        protected override void PopIn()
        {
            base.PopIn();

            // the library is read every time, as lazer may have changed since.
            ScheduleAfterChildren(() => GetContainingFocusManager()!.ChangeFocus(search));
            search.Current.Value = string.Empty;
            allSets = new List<LazerSet>();
            shownSets.Clear();
            status.Text = "reading the osu!lazer library...";

            string[] rulesetNames = rulesets.AvailableRulesets.Select(r => r.ShortName).ToArray();

            Task.Run(() => LazerLibrary.Read(LazerLibrary.DataDirectory, rulesetNames)).ContinueWith(t => Schedule(() =>
            {
                if (t.IsCompletedSuccessfully)
                {
                    allSets = t.GetResultSafely();
                    updateFilter();
                }
                else
                    status.Text = $"couldn't read the osu!lazer library: {t.Exception?.GetBaseException().Message}";
            }));
        }

        private void updateFilter()
        {
            string[] terms = search.Current.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            var matching = allSets.Where(s => terms.All(t => contains(s.Artist, t) || contains(s.Title, t) || contains(s.Author, t))).ToArray();

            shownSets.Clear();
            shownSets.AddRange(matching);

            status.Text = $"{matching.Length:#,0} of {allSets.Count:#,0} beatmap sets in osu!lazer";

            static bool contains(string value, string term) => value.Contains(term, StringComparison.OrdinalIgnoreCase);
        }

        private void importSet(LazerSet set)
        {
            Hide();
            import(set);
        }

        private partial class SetList : VirtualisedListContainer<LazerSet, SetRow>
        {
            public SetList()
                : base(EditorBeatmapSelect.ROW_HEIGHT, 50)
            {
            }

            protected override ScrollContainer<Drawable> CreateScrollContainer() => new OsuScrollContainer();
        }

        private partial class SetRow : PoolableDrawable, IHasCurrentValue<LazerSet>
        {
            private readonly BindableWithCurrent<LazerSet> current = new BindableWithCurrent<LazerSet>();

            private Box background = null!;
            private OsuSpriteText titleText = null!;
            private OsuSpriteText detailsText = null!;

            [Resolved]
            private OverlayColourProvider colourProvider { get; set; } = null!;

            [Resolved]
            private LazerImportOverlay overlay { get; set; } = null!;

            public Bindable<LazerSet> Current
            {
                get => current.Current;
                set => current.Current = value;
            }

            [BackgroundDependencyLoader]
            private void load()
            {
                RelativeSizeAxes = Axes.X;
                Height = EditorBeatmapSelect.ROW_HEIGHT;

                InternalChildren = new Drawable[]
                {
                    background = new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = colourProvider.Background4,
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
            }

            protected override void LoadComplete()
            {
                base.LoadComplete();

                Current.BindValueChanged(_ => updateText(), true);
            }

            private void updateText()
            {
                var set = Current.Value;

                if (set == null)
                    return;

                titleText.Text = $"{set.Artist} - {set.Title}";
                detailsText.Text = $"mapped by {set.Author} · {set.Difficulties} {(set.Difficulties == 1 ? "difficulty" : "difficulties")} · added {set.DateAdded.LocalDateTime:d}";
            }

            protected override bool OnHover(HoverEvent e)
            {
                background.FadeIn(100, Easing.OutQuint);
                return true;
            }

            protected override void OnHoverLost(HoverLostEvent e)
            {
                background.FadeOut(100, Easing.OutQuint);
                base.OnHoverLost(e);
            }

            protected override bool OnClick(ClickEvent e)
            {
                overlay.importSet(Current.Value);
                return true;
            }
        }
    }
}
