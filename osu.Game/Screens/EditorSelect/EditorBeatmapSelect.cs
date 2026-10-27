// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Screens;
using osu.Game.Beatmaps;
using osu.Game.Database;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterface;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Overlays;
using osu.Game.Rulesets;
using osu.Game.Screens.Backgrounds;
using osu.Game.Screens.Edit;
using osu.Game.Screens.Select;
using osuTK;

namespace osu.Game.Screens.EditorSelect
{
    /// <summary>
    /// A simple list-based screen for choosing a beatmap set and difficulty to open in the editor.
    /// </summary>
    [Cached]
    public partial class EditorBeatmapSelect : OsuScreen
    {
        public const float ROW_HEIGHT = 44;

        private const float header_height = 60;
        private const float footer_height = 30;
        private const float difficulty_list_width = 460;

        public override bool AllowUserExit => false;

        public override bool HideOverlaysOnEnter => false;

        public override bool? AllowGlobalTrackControl => false;

        /// <summary>
        /// The currently selected beatmap set, whose difficulties are displayed for selection.
        /// </summary>
        public readonly Bindable<BeatmapSetInfo?> SelectedSet = new Bindable<BeatmapSetInfo?>();

        [Cached(typeof(OverlayColourProvider))]
        private readonly OverlayColourProvider colourProvider = new CatppuccinMochaColourProvider();

        [Resolved]
        private BeatmapManager beatmapManager { get; set; } = null!;

        [Resolved]
        private RulesetStore rulesets { get; set; } = null!;

        [Resolved]
        private MusicController musicController { get; set; } = null!;

        [Resolved]
        private IDialogOverlay? dialogOverlay { get; set; }

        private IBindableList<BeatmapSetInfo> allSets = null!;

        private readonly BindableList<BeatmapSetInfo> filteredSets = new BindableList<BeatmapSetInfo>();

        private SearchTextBox searchTextBox = null!;
        private OsuSpriteText countText = null!;
        private OsuSpriteText selectedSetText = null!;
        private FillFlowContainer<DifficultyRow> difficultyFlow = null!;
        private RoundedButton deleteButton = null!;

        protected override BackgroundScreen CreateBackground() => new BackgroundScreenBlack();

        [BackgroundDependencyLoader]
        private void load(BeatmapStore beatmapStore, OsuColour colours)
        {
            allSets = beatmapStore.GetBeatmapSets(null);

            RelativeSizeAxes = Axes.Both;

            FillFlowContainer newBeatmapButtons;

            InternalChildren = new Drawable[]
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
                        new Dimension(GridSizeMode.Absolute, header_height),
                        new Dimension(),
                        new Dimension(GridSizeMode.Absolute, footer_height),
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
                                    new GridContainer
                                    {
                                        RelativeSizeAxes = Axes.Both,
                                        Padding = new MarginPadding { Horizontal = 20 },
                                        ColumnDimensions = new[]
                                        {
                                            new Dimension(GridSizeMode.AutoSize),
                                            new Dimension(),
                                            new Dimension(GridSizeMode.AutoSize),
                                        },
                                        Content = new[]
                                        {
                                            new Drawable[]
                                            {
                                                new OsuSpriteText
                                                {
                                                    Anchor = Anchor.CentreLeft,
                                                    Origin = Anchor.CentreLeft,
                                                    Text = "beatmap editor",
                                                    Font = OsuFont.GetFont(size: 24, weight: FontWeight.Light),
                                                    Margin = new MarginPadding { Right = 20 },
                                                },
                                                searchTextBox = new SearchTextBox
                                                {
                                                    Anchor = Anchor.CentreLeft,
                                                    Origin = Anchor.CentreLeft,
                                                    RelativeSizeAxes = Axes.X,
                                                    HoldFocus = true,
                                                    PlaceholderText = "search by artist, title, creator, tags or difficulty name",
                                                },
                                                newBeatmapButtons = new FillFlowContainer
                                                {
                                                    Anchor = Anchor.CentreRight,
                                                    Origin = Anchor.CentreRight,
                                                    AutoSizeAxes = Axes.Both,
                                                    Direction = FillDirection.Horizontal,
                                                    Spacing = new Vector2(5),
                                                    Margin = new MarginPadding { Left = 20 },
                                                    Child = new OsuSpriteText
                                                    {
                                                        Anchor = Anchor.CentreLeft,
                                                        Origin = Anchor.CentreLeft,
                                                        Text = "new beatmap:",
                                                        Font = OsuFont.GetFont(size: 14, weight: FontWeight.SemiBold),
                                                        Colour = colourProvider.Content2,
                                                        Margin = new MarginPadding { Right = 5 },
                                                    },
                                                },
                                            }
                                        }
                                    },
                                }
                            },
                        },
                        new Drawable[]
                        {
                            new GridContainer
                            {
                                RelativeSizeAxes = Axes.Both,
                                ColumnDimensions = new[]
                                {
                                    new Dimension(),
                                    new Dimension(GridSizeMode.Absolute, difficulty_list_width),
                                },
                                Content = new[]
                                {
                                    new Drawable[]
                                    {
                                        new BeatmapSetList
                                        {
                                            RelativeSizeAxes = Axes.Both,
                                            RowData = { BindTarget = filteredSets },
                                        },
                                        new Container
                                        {
                                            RelativeSizeAxes = Axes.Both,
                                            Children = new Drawable[]
                                            {
                                                new Box
                                                {
                                                    RelativeSizeAxes = Axes.Both,
                                                    Colour = colourProvider.Background3,
                                                },
                                                new GridContainer
                                                {
                                                    RelativeSizeAxes = Axes.Both,
                                                    RowDimensions = new[]
                                                    {
                                                        new Dimension(GridSizeMode.AutoSize),
                                                        new Dimension(),
                                                    },
                                                    Content = new[]
                                                    {
                                                        new Drawable[]
                                                        {
                                                            new Container
                                                            {
                                                                RelativeSizeAxes = Axes.X,
                                                                AutoSizeAxes = Axes.Y,
                                                                Padding = new MarginPadding(15),
                                                                Children = new Drawable[]
                                                                {
                                                                    selectedSetText = new TruncatingSpriteText
                                                                    {
                                                                        Anchor = Anchor.CentreLeft,
                                                                        Origin = Anchor.CentreLeft,
                                                                        RelativeSizeAxes = Axes.X,
                                                                        Padding = new MarginPadding { Right = 110 },
                                                                        Font = OsuFont.GetFont(size: 16, weight: FontWeight.SemiBold),
                                                                    },
                                                                    deleteButton = new RoundedButton
                                                                    {
                                                                        Anchor = Anchor.CentreRight,
                                                                        Origin = Anchor.CentreRight,
                                                                        Width = 100,
                                                                        Height = 30,
                                                                        Text = "delete set",
                                                                        BackgroundColour = colours.DangerousButtonColour,
                                                                        Action = deleteSelectedSet,
                                                                    },
                                                                }
                                                            },
                                                        },
                                                        new Drawable[]
                                                        {
                                                            new OsuScrollContainer
                                                            {
                                                                RelativeSizeAxes = Axes.Both,
                                                                Child = difficultyFlow = new FillFlowContainer<DifficultyRow>
                                                                {
                                                                    RelativeSizeAxes = Axes.X,
                                                                    AutoSizeAxes = Axes.Y,
                                                                    Direction = FillDirection.Vertical,
                                                                },
                                                            },
                                                        },
                                                    }
                                                },
                                            }
                                        },
                                    }
                                }
                            },
                        },
                        new Drawable[]
                        {
                            new Container
                            {
                                RelativeSizeAxes = Axes.Both,
                                Padding = new MarginPadding { Horizontal = 20 },
                                Children = new Drawable[]
                                {
                                    new OsuSpriteText
                                    {
                                        Anchor = Anchor.CentreLeft,
                                        Origin = Anchor.CentreLeft,
                                        Text = "drag and drop .osz or .olz files onto this window to import beatmaps",
                                        Font = OsuFont.GetFont(size: 12),
                                        Colour = colourProvider.Content2,
                                    },
                                    countText = new OsuSpriteText
                                    {
                                        Anchor = Anchor.CentreRight,
                                        Origin = Anchor.CentreRight,
                                        Font = OsuFont.GetFont(size: 12),
                                        Colour = colourProvider.Content2,
                                    },
                                }
                            },
                        },
                    }
                },
            };

            foreach (var ruleset in rulesets.AvailableRulesets)
            {
                var instance = ruleset.CreateInstance();

                newBeatmapButtons.Add(new OsuHoverContainer
                {
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft,
                    Size = new Vector2(30),
                    IdleColour = colourProvider.Content2,
                    HoverColour = colourProvider.Highlight1,
                    TooltipText = $"Create a new {instance.Description} beatmap",
                    Action = () => CreateNewBeatmap(ruleset),
                    Child = instance.CreateIcon().With(i =>
                    {
                        i.RelativeSizeAxes = Axes.Both;
                        i.Anchor = Anchor.Centre;
                        i.Origin = Anchor.Centre;
                    }),
                });
            }
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            allSets.BindCollectionChanged((_, _) => Scheduler.AddOnce(updateFilter));
            searchTextBox.Current.BindValueChanged(_ => Scheduler.AddOnce(updateFilter));
            SelectedSet.BindValueChanged(_ => updateDifficulties());

            updateFilter();
            updateDifficulties();
        }

        public override void OnResuming(ScreenTransitionEvent e)
        {
            base.OnResuming(e);

            // don't keep playing the track of the beatmap which was being edited.
            musicController.Stop();
        }

        private void updateFilter()
        {
            string[] terms = searchTextBox.Current.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            var matching = allSets
                           .Where(s => s.Beatmaps.Any(IsEditable) && terms.All(t => matches(s, t)))
                           .OrderBy(s => s.Metadata.Artist, StringComparer.OrdinalIgnoreCase)
                           .ThenBy(s => s.Metadata.Title, StringComparer.OrdinalIgnoreCase)
                           .ToArray();

            filteredSets.Clear();
            filteredSets.AddRange(matching);

            countText.Text = matching.Length == allSets.Count
                ? $"{allSets.Count:#,0} beatmap sets"
                : $"{matching.Length:#,0} of {allSets.Count:#,0} beatmap sets";

            // ensure the selection references the most recent (detached) model, as beatmaps may have been modified in the editor.
            if (SelectedSet.Value != null)
                SelectedSet.Value = allSets.FirstOrDefault(s => s.ID == SelectedSet.Value.ID);
        }

        /// <summary>
        /// Whether the given difficulty can be opened in the editor.
        /// Difficulties for rulesets which aren't available in this client (eg. other game modes) are hidden.
        /// </summary>
        public static bool IsEditable(BeatmapInfo beatmapInfo) => beatmapInfo.Ruleset.Available;

        private static bool matches(BeatmapSetInfo set, string term)
        {
            var metadata = set.Metadata;

            return contains(metadata.Artist)
                   || contains(metadata.ArtistUnicode)
                   || contains(metadata.Title)
                   || contains(metadata.TitleUnicode)
                   || contains(metadata.Author.Username)
                   || contains(metadata.Source)
                   || contains(metadata.Tags)
                   || set.Beatmaps.Where(IsEditable).Any(b => contains(b.DifficultyName));

            bool contains(string? value) => value?.Contains(term, StringComparison.OrdinalIgnoreCase) == true;
        }

        private void updateDifficulties()
        {
            difficultyFlow.Clear();

            var set = SelectedSet.Value;

            deleteButton.Enabled.Value = set != null;

            if (set == null)
            {
                selectedSetText.Text = "select a beatmap set";
                return;
            }

            selectedSetText.Text = $"{set.Metadata.Artist} - {set.Metadata.Title}";

            difficultyFlow.ChildrenEnumerable = set.Beatmaps
                                                   .Where(IsEditable)
                                                   .OrderBy(b => b.Ruleset.OnlineID < 0 ? int.MaxValue : b.Ruleset.OnlineID)
                                                   .ThenBy(b => b.StarRating)
                                                   .Select(b => new DifficultyRow(b) { Action = () => OpenDifficulty(b) });
        }

        /// <summary>
        /// Opens the given difficulty in the editor.
        /// </summary>
        public void OpenDifficulty(BeatmapInfo beatmapInfo)
        {
            if (!this.IsCurrentScreen())
                return;

            Ruleset.Value = beatmapInfo.Ruleset;
            Beatmap.Value = beatmapManager.GetWorkingBeatmap(beatmapInfo);

            this.Push(new EditorLoader());
        }

        /// <summary>
        /// Opens the editor with a new, empty beatmap for the given ruleset.
        /// </summary>
        public void CreateNewBeatmap(RulesetInfo ruleset)
        {
            if (!this.IsCurrentScreen())
                return;

            // the editor creates a new beatmap when it is entered with the default (dummy) beatmap.
            Ruleset.Value = ruleset;
            Beatmap.SetDefault();

            this.Push(new EditorLoader());
        }

        private void deleteSelectedSet()
        {
            if (SelectedSet.Value == null)
                return;

            dialogOverlay?.Push(new BeatmapDeleteDialog(SelectedSet.Value));
        }

        private partial class BeatmapSetList : VirtualisedListContainer<BeatmapSetInfo, BeatmapSetRow>
        {
            public BeatmapSetList()
                : base(ROW_HEIGHT, 50)
            {
            }

            protected override ScrollContainer<Drawable> CreateScrollContainer() => new OsuScrollContainer();
        }
    }
}
