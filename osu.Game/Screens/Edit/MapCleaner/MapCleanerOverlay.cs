// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Extensions.ObjectExtensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Game.Extensions;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Overlays;
using osu.Game.Rulesets.Objects;
using osu.Game.Screens.Edit.Components;
using osuTK;

namespace osu.Game.Screens.Edit.MapCleaner
{
    /// <summary>
    /// Previews and applies <see cref="MapCleaning"/> clean-ups of the open difficulty.
    /// </summary>
    public partial class MapCleanerOverlay : EditorToolOverlay
    {
        protected override string Title => "Clean up difficulty";

        protected override Vector2 PanelSize => new Vector2(0.7f, 0.75f);

        private readonly BindableBool resnapObjects = new BindableBool(true);
        private readonly BindableBool resnapOther = new BindableBool(true);
        private readonly BindableBool removeRedundantTimingPoints = new BindableBool(true);
        private readonly BindableBool tidyComboColourSkips = new BindableBool(true);

        private readonly BindableInt maxResnapDistance = new BindableInt(5)
        {
            MinValue = 1,
            MaxValue = 50,
        };

        [Resolved]
        private EditorBeatmap beatmap { get; set; } = null!;

        [Resolved]
        private EditorClock clock { get; set; } = null!;

        [Resolved]
        private OverlayColourProvider colourProvider { get; set; } = null!;

        private FillFlowContainer changeList = null!;
        private OsuSpriteText summary = null!;
        private RoundedButton applyButton = null!;

        private List<CleanupChange> plan = new List<CleanupChange>();

        [BackgroundDependencyLoader]
        private void load()
        {
            Child = new GridContainer
            {
                RelativeSizeAxes = Axes.Both,
                ColumnDimensions = new[]
                {
                    new Dimension(GridSizeMode.Absolute, 380),
                    new Dimension(GridSizeMode.Absolute, PADDING),
                    new Dimension(),
                },
                Content = new[]
                {
                    new Drawable[]
                    {
                        new OsuScrollContainer
                        {
                            RelativeSizeAxes = Axes.Both,
                            Child = new FillFlowContainer
                            {
                                RelativeSizeAxes = Axes.X,
                                AutoSizeAxes = Axes.Y,
                                Spacing = new Vector2(5),
                                Direction = FillDirection.Vertical,
                                Children = new Drawable[]
                                {
                                    new FormCheckBox
                                    {
                                        Caption = "Resnap hit objects",
                                        HintText = "Moves starts and ends onto the closest snap. Slider ends are fixed by changing the length, not the velocity.",
                                        Current = resnapObjects,
                                    },
                                    new FormCheckBox
                                    {
                                        Caption = "Resnap effect points and bookmarks",
                                        Current = resnapOther,
                                    },
                                    new FormSliderBar<int>
                                    {
                                        Caption = "Max resnap distance (ms)",
                                        HintText = "Times further from a snap than this are left alone: they're probably intended, or need a look.",
                                        Current = maxResnapDistance,
                                        TransferValueOnCommit = true,
                                    },
                                    new FormCheckBox
                                    {
                                        Caption = "Remove redundant timing points",
                                        HintText = "Timing points with the same BPM and meter as the previous one, placed on one of its bar lines.",
                                        Current = removeRedundantTimingPoints,
                                    },
                                    new FormCheckBox
                                    {
                                        Caption = "Tidy combo colour skips",
                                        HintText = "Clears skips on objects that don't start a combo, and reduces skips of a whole palette or more. Colours don't change.",
                                        Current = tidyComboColourSkips,
                                    },
                                    applyButton = new RoundedButton
                                    {
                                        RelativeSizeAxes = Axes.X,
                                        Text = "Apply",
                                        Action = apply,
                                        Margin = new MarginPadding { Top = 10 },
                                    },
                                    summary = new OsuSpriteText
                                    {
                                        Font = OsuFont.Default.With(size: 14),
                                    },
                                }
                            }
                        },
                        Empty(),
                        new Container
                        {
                            RelativeSizeAxes = Axes.Both,
                            Masking = true,
                            CornerRadius = 5,
                            Children = new Drawable[]
                            {
                                new Box
                                {
                                    RelativeSizeAxes = Axes.Both,
                                    Colour = colourProvider.Background6,
                                },
                                new OsuScrollContainer
                                {
                                    RelativeSizeAxes = Axes.Both,
                                    Child = changeList = new FillFlowContainer
                                    {
                                        RelativeSizeAxes = Axes.X,
                                        AutoSizeAxes = Axes.Y,
                                        Padding = new MarginPadding(10),
                                        Spacing = new Vector2(2),
                                        Direction = FillDirection.Vertical,
                                    }
                                },
                            }
                        },
                    },
                }
            };
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            summary.Colour = colourProvider.Content2;

            resnapObjects.BindValueChanged(_ => refresh());
            resnapOther.BindValueChanged(_ => refresh());
            removeRedundantTimingPoints.BindValueChanged(_ => refresh());
            tidyComboColourSkips.BindValueChanged(_ => refresh());
            maxResnapDistance.BindValueChanged(_ => refresh());

            // undo/redo still reach the editor while this is open; a plan made before them would apply to stale objects.
            beatmap.HitObjectAdded += onBeatmapChanged;
            beatmap.HitObjectRemoved += onBeatmapChanged;
            beatmap.BeatmapReprocessed += onBeatmapReprocessed;
        }

        private void onBeatmapChanged(HitObject _) => onBeatmapReprocessed();

        private void onBeatmapReprocessed()
        {
            if (State.Value == Visibility.Visible)
                Scheduler.AddOnce(refresh);
        }

        protected override void Dispose(bool isDisposing)
        {
            base.Dispose(isDisposing);

            if (beatmap.IsNotNull())
            {
                beatmap.HitObjectAdded -= onBeatmapChanged;
                beatmap.HitObjectRemoved -= onBeatmapChanged;
                beatmap.BeatmapReprocessed -= onBeatmapReprocessed;
            }
        }

        protected override void PopIn()
        {
            base.PopIn();

            // the beatmap may have changed since the overlay was last open.
            summary.Text = string.Empty;
            refresh();
        }

        private MapCleanerOptions options => new MapCleanerOptions
        {
            ResnapObjects = resnapObjects.Value,
            ResnapOther = resnapOther.Value,
            MaxResnapDistance = maxResnapDistance.Value,
            RemoveRedundantTimingPoints = removeRedundantTimingPoints.Value,
            TidyComboColourSkips = tidyComboColourSkips.Value,
        };

        private void refresh()
        {
            plan = MapCleaning.Plan(beatmap, options);

            changeList.Clear();

            if (plan.Count == 0)
            {
                changeList.Add(new OsuSpriteText
                {
                    Text = "Nothing to clean up.",
                    Colour = colourProvider.Content2,
                });
            }

            foreach (var change in plan)
            {
                changeList.Add(new OsuClickableContainer
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    TooltipText = "Seek here",
                    Action = () => clock.SeekSmoothlyTo(change.Time),
                    Child = new FillFlowContainer
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        Direction = FillDirection.Horizontal,
                        Spacing = new Vector2(10),
                        Children = new Drawable[]
                        {
                            new OsuSpriteText
                            {
                                Width = 80,
                                Text = change.Time.ToEditorFormattedString(),
                                Font = OsuFont.Default.With(size: 14, weight: FontWeight.SemiBold),
                                Colour = colourProvider.Highlight1,
                            },
                            new OsuSpriteText
                            {
                                Text = change.Description,
                                Font = OsuFont.Default.With(size: 14),
                            },
                        }
                    }
                });
            }

            applyButton.Enabled.Value = plan.Count > 0;
            applyButton.Text = plan.Count > 0 ? $"Apply {plan.Count} change{(plan.Count == 1 ? "" : "s")}" : "Apply";

            // with nothing left to do, the summary keeps saying what was applied last.
            if (plan.Count > 0)
                summary.Text = string.Join(", ", plan.GroupBy(c => c.Kind).Select(g => $"{g.Count()} {describe(g.Key)}"));
        }

        private void apply()
        {
            int count = plan.Count;

            MapCleaning.Apply(beatmap, plan);
            refresh();

            summary.Text = $"Applied {count} change{(count == 1 ? "" : "s")} (undo with Ctrl+Z).";
        }

        private static string describe(CleanupKind kind) => kind switch
        {
            CleanupKind.ResnapObject => "objects",
            CleanupKind.ResnapEffectPoint => "effect points",
            CleanupKind.ResnapBookmark => "bookmarks",
            CleanupKind.RedundantTimingPoint => "timing points",
            _ => "combo colour skips",
        };
    }
}
