// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Extensions;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Extensions.ObjectExtensions;
using osu.Framework.Graphics;
using osu.Game.Beatmaps;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Cursor;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Input;
using osu.Framework.Input.Bindings;
using osu.Framework.Input.Events;
using osu.Game.Audio;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Input.Bindings;
using osu.Game.Overlays;
using osu.Game.Rulesets.Edit;
using osu.Game.Screens.Edit.Compose.Components.Timeline;
using osu.Game.Screens.Edit.Setup;
using osuTK;
using osuTK.Input;

namespace osu.Game.Screens.Edit.Hitsounds
{
    /// <summary>
    /// An editor screen for hitsounding: a grid of swimlanes (every sample of every bank of every sample set) aligned to the timeline,
    /// and a panel to create custom sample sets and assign sound files to them.
    /// The grid edits a hitsound track (see <see cref="EditorHitsounds"/>) which exists independently of the hit objects.
    /// </summary>
    public partial class HitsoundsScreen : EditorScreen, IKeyBindingHandler<GlobalAction>, IKeyBindingHandler<PlatformAction>
    {
        /// <summary>
        /// Whether clicking selects / moves steps instead of painting.
        /// </summary>
        private readonly BindableBool selectMode = new BindableBool();

        private readonly HitsoundLayout layout = new HitsoundLayout();
        private readonly Bindable<EditorBeatmapSkin.SampleSet?> currentSampleSet = new Bindable<EditorBeatmapSkin.SampleSet?>();

        private readonly EditorHitsounds hitsounds;

        private readonly BindableInt selectionVolume = createVolume();
        private readonly BindableInt allVolume = createVolume();
        private HitsoundPatternEditor patternEditor = null!;
        private TimelineArea timelineArea = null!;
        private Container rhythmStripSpacer = null!;
        private IBindable<bool>? rhythmStripShown;
        private bool refreshingVolumes;

        [Resolved]
        private EditorClock clock { get; set; } = null!;

        [Resolved]
        private IBeatSnapProvider beatSnap { get; set; } = null!;

        private RoundedButton paintButton = null!;
        private RoundedButton selectButton = null!;

        [Resolved]
        private BeatmapManager beatmaps { get; set; } = null!;
        private FormSampleSetChooser sampleSetChooser = null!;
        private OsuTextFlowContainer status = null!;

        public HitsoundsScreen(EditorHitsounds hitsounds)
            : base(EditorScreenMode.Hitsounds)
        {
            this.hitsounds = hitsounds;
        }

        public override EditorChangeHandler ChangeHandler => hitsounds.ChangeHandler;

        [BackgroundDependencyLoader]
        private void load()
        {
            Child = new GridContainer
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
                        new GridContainer
                        {
                            RelativeSizeAxes = Axes.X,
                            AutoSizeAxes = Axes.Y,
                            RowDimensions = new[] { new Dimension(GridSizeMode.AutoSize) },
                            ColumnDimensions = new[]
                            {
                                new Dimension(GridSizeMode.Absolute, 440),
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
                                        Direction = FillDirection.Vertical,
                                        Spacing = new Vector2(6),
                                        Padding = new MarginPadding(10),
                                        Children = new Drawable[]
                                        {
                                            sampleSetChooser = new FormSampleSetChooser
                                            {
                                                Current = { BindTarget = currentSampleSet },
                                            },
                                            new HitsoundSampleSetPanel
                                            {
                                                RelativeSizeAxes = Axes.X,
                                                Current = { BindTarget = currentSampleSet },
                                            },
                                        }
                                    },
                                    new FillFlowContainer
                                    {
                                        RelativeSizeAxes = Axes.X,
                                        AutoSizeAxes = Axes.Y,
                                        Direction = FillDirection.Vertical,
                                        Spacing = new Vector2(8),
                                        Padding = new MarginPadding(10),
                                        Children = new Drawable[]
                                        {
                                            new FillFlowContainer
                                            {
                                                AutoSizeAxes = Axes.Both,
                                                Direction = FillDirection.Horizontal,
                                                Spacing = new Vector2(8),
                                                Children = new Drawable[]
                                                {
                                                    paintButton = new RoundedButton { Width = 96, Height = 28, Text = "paint (2)", Action = () => selectMode.Value = false },
                                                    selectButton = new RoundedButton { Width = 96, Height = 28, Text = "select (1)", Action = () => selectMode.Value = true },
                                                    new RoundedButton { Width = 110, Height = 28, Text = "lanes up", Action = () => layout.SectionOffset.Value-- },
                                                    new RoundedButton { Width = 110, Height = 28, Text = "lanes down", Action = () => layout.SectionOffset.Value++ },
                                                },
                                            },
                                            new FillFlowContainer
                                            {
                                                AutoSizeAxes = Axes.Both,
                                                Direction = FillDirection.Horizontal,
                                                Spacing = new Vector2(8),
                                                Children = new Drawable[]
                                                {
                                                    new RoundedButton { Width = 160, Height = 28, Text = "apply to this difficulty", Action = apply },
                                                    new RoundedButton { Width = 160, Height = 28, Text = "apply to all difficulties", Action = applyToAll },
                                                    new RoundedButton { Width = 180, Height = 28, Text = "import from this difficulty", Action = hitsounds.ImportFromHitObjects },
                                                },
                                            },
                                            new FillFlowContainer
                                            {
                                                RelativeSizeAxes = Axes.X,
                                                AutoSizeAxes = Axes.Y,
                                                Direction = FillDirection.Full,
                                                Spacing = new Vector2(8),
                                                Children = new Drawable[]
                                                {
                                                    createVolumeSlider("paint volume", "The volume of the sounds you paint.", hitsounds.PaintVolume),
                                                    createVolumeSlider("selected volume", "Sets the volume of every selected sound. Shows the average when they differ.", selectionVolume),
                                                    createVolumeSlider("all volume", "Sets the volume of every sound of the track. Shows the average when they differ.", allVolume),
                                                },
                                            },
                                            new HelpButton { Width = 70, Height = 28, Text = "help" },
                                            status = new OsuTextFlowContainer(s => s.Font = OsuFont.GetFont(size: 12)) { RelativeSizeAxes = Axes.X, AutoSizeAxes = Axes.Y },
                                        }
                                    },
                                }
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
                                new Dimension(GridSizeMode.Absolute, HitsoundLaneLabels.WIDTH),
                                new Dimension(),
                            },
                            Content = new[]
                            {
                                new Drawable[]
                                {
                                    new Container
                                    {
                                        RelativeSizeAxes = Axes.Both,
                                        Child = new FillFlowContainer
                                        {
                                            Anchor = Anchor.Centre,
                                            Origin = Anchor.Centre,
                                            Width = HitsoundLaneLabels.WIDTH,
                                            AutoSizeAxes = Axes.Y,
                                            Direction = FillDirection.Vertical,
                                            Children = new Drawable[]
                                            {
                                                // matches the strip Timeline inserts above the grid, so the names stay on their lanes.
                                                rhythmStripSpacer = new Container { RelativeSizeAxes = Axes.X },
                                                new HitsoundLaneLabels(layout, hitsounds),
                                            },
                                        },
                                    },
                                    new Container
                                    {
                                        RelativeSizeAxes = Axes.Both,
                                        Child = timelineArea = new TimelineArea(patternEditor = new HitsoundPatternEditor(hitsounds, layout, selectMode) { RelativeSizeAxes = Axes.Both }, HitsoundLayout.HEIGHT, false, true, true)
                                        {
                                            Anchor = Anchor.Centre,
                                            Origin = Anchor.Centre,
                                        },
                                    },
                                }
                            }
                        },
                    },
                }
            };

            selectMode.BindValueChanged(_ =>
            {
                paintButton.BackgroundColour = selectMode.Value ? CatppuccinMochaColourProvider.SURFACE1 : CatppuccinMochaColourProvider.MAUVE.Darken(0.4f);
                selectButton.BackgroundColour = selectMode.Value ? CatppuccinMochaColourProvider.MAUVE.Darken(0.4f) : CatppuccinMochaColourProvider.SURFACE1;
            }, true);

            hitsounds.SelectionChanged += updateClipboardActionAvailability;
            updateClipboardActionAvailability();

            // the sliders only transfer their value when committed, which is when the volume is actually edited.
            hitsounds.PaintVolume.BindValueChanged(v => patternEditor.Preview(new HitsoundLane(HitSampleInfo.BANK_NORMAL, 1, HitSampleInfo.HIT_NORMAL).Create(v.NewValue)));

            selectionVolume.BindValueChanged(v =>
            {
                if (refreshingVolumes)
                    return;

                hitsounds.SetSelectedVolume(v.NewValue);

                var sample = hitsounds.Selected.FirstOrDefault();

                if (sample != null)
                    patternEditor.Preview(sample);
            });

            allVolume.BindValueChanged(v =>
            {
                if (!refreshingVolumes)
                    hitsounds.SetAllVolume(v.NewValue);
            });

            hitsounds.SelectionChanged += scheduleVolumeRefresh;
            hitsounds.StepAdded += onStepChanged;
            hitsounds.StepRemoved += onStepChanged;
            hitsounds.StepUpdated += onStepChanged;
            refreshVolumes();

            hitsounds.SampleSetsChanged += updateLayoutSampleSets;
            updateLayoutSampleSets();

            currentSampleSet.BindValueChanged(set =>
            {
                registerSampleSets();

                if (set.NewValue != null && set.NewValue.SampleSetIndex > 0)
                    layout.ShowSampleSet(set.NewValue.SampleSetIndex);
            });

            if (EditorBeatmap.BeatmapSkin != null)
                EditorBeatmap.BeatmapSkin.BeatmapSkinChanged += scheduleRegisterSampleSets;


        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            // the chooser has to be loaded before its items can be read.
            registerSampleSets();

            rhythmStripShown = timelineArea.Timeline.RhythmStripShown.GetBoundCopy();
            rhythmStripShown.BindValueChanged(shown => rhythmStripSpacer.Height = shown.NewValue ? Timeline.RHYTHM_STRIP_HEIGHT : 0, true);
        }

        private void applyToAll()
        {
            var (difficulties, unrepresentable) = hitsounds.ApplyToAllDifficulties(beatmaps);

            status.Text = $"applied to {difficulties} difficult{(difficulties == 1 ? "y" : "ies")}.{describeUnrepresentable(unrepresentable)}";
        }

        private static string describeUnrepresentable(int unrepresentable) => unrepresentable == 0
            ? string.Empty
            : $" {unrepresentable} step(s) mix banks, sample sets or volumes (red mark); a .osu file keeps only one of each.";

        private void apply()
        {
            int unrepresentable = hitsounds.ApplyToHitObjects();

            status.Text = $"applied.{describeUnrepresentable(unrepresentable)}";
        }

        private void updateLayoutSampleSets() => layout.SetSampleSets(hitsounds.SampleSets);

        private void scheduleRegisterSampleSets() => Schedule(registerSampleSets);

        /// <summary>
        /// Makes sure lanes are shown for every sample set which has files or was just created.
        /// </summary>
        private void registerSampleSets()
        {
            var sets = EditorBeatmap.BeatmapSkin?.GetAvailableSampleSets().Select(s => s.SampleSetIndex) ?? Enumerable.Empty<int>();
            var created = sampleSetChooser.Items.Where(i => i != null && i.SampleSetIndex > 0).Select(i => i!.SampleSetIndex);

            hitsounds.RegisterSampleSets(sets.Concat(created));
        }

        #region Volume

        private static BindableInt createVolume() => new BindableInt(HitsoundEditing.MAX_VOLUME)
        {
            MinValue = HitsoundEditing.MIN_VOLUME,
            MaxValue = HitsoundEditing.MAX_VOLUME,
        };

        private static FormSliderBar<int> createVolumeSlider(string caption, string hint, BindableInt volume) => new FormSliderBar<int>
        {
            RelativeSizeAxes = Axes.None,
            Width = 200,
            Caption = caption,
            HintText = hint,
            KeyboardStep = 5,
            TransferValueOnCommit = true,
            Current = { BindTarget = volume },
        };

        private void onStepChanged(HitsoundStep _) => scheduleVolumeRefresh();

        private void scheduleVolumeRefresh() => Scheduler.AddOnce(refreshVolumes);

        /// <summary>
        /// Shows the volume of the selection and of the whole track (their average if they differ), after the sounds changed.
        /// </summary>
        private void refreshVolumes()
        {
            refreshingVolumes = true;

            setDisplayedVolume(selectionVolume, hitsounds.SelectedVolume);
            setDisplayedVolume(allVolume, hitsounds.OverallVolume);

            refreshingVolumes = false;
        }

        // nothing to set the volume of -> disabled.
        private static void setDisplayedVolume(BindableInt bindable, int? volume)
        {
            bindable.Disabled = false;
            bindable.Value = volume ?? HitsoundEditing.MAX_VOLUME;
            bindable.Disabled = volume == null;
        }

        #endregion

        #region Input

        public bool OnPressed(KeyBindingPressEvent<GlobalAction> e)
        {
            if (e.Repeat || e.Action != GlobalAction.EditorSelectTool)
                return false;

            selectMode.Value = true;
            return true;
        }

        public void OnReleased(KeyBindingReleaseEvent<GlobalAction> e)
        {
        }

        public bool OnPressed(KeyBindingPressEvent<PlatformAction> e)
        {
            switch (e.Action)
            {
                case PlatformAction.SelectAll:
                    hitsounds.SelectAll();
                    return true;

                case PlatformAction.Delete:
                    hitsounds.DeleteSelected();
                    return true;
            }

            return false;
        }

        public void OnReleased(KeyBindingReleaseEvent<PlatformAction> e)
        {
        }

        protected override bool OnKeyDown(KeyDownEvent e)
        {
            if (e.Repeat || e.ControlPressed || e.AltPressed || e.SuperPressed || e.ShiftPressed || e.Key != Key.Number2)
                return false;

            selectMode.Value = false;
            return true;
        }

        #endregion

        #region Clipboard operations

        public override void Cut()
        {
            if (!CanCut.Value)
                return;

            Copy();
            hitsounds.DeleteSelected();
        }

        public override void Copy()
        {
            if (!CanCopy.Value)
                return;

            hitsounds.CopySelected();
            updateClipboardActionAvailability();
        }

        public override void Paste()
        {
            if (!CanPaste.Value)
                return;

            hitsounds.Paste(beatSnap.SnapTime(clock.CurrentTime, null));
        }

        private void updateClipboardActionAvailability()
        {
            CanCut.Value = CanCopy.Value = hitsounds.Selected.Count > 0;
            CanPaste.Value = hitsounds.Clipboard != null;
        }

        #endregion

        protected override void Dispose(bool isDisposing)
        {
            hitsounds.SampleSetsChanged -= updateLayoutSampleSets;
            hitsounds.SelectionChanged -= updateClipboardActionAvailability;
            hitsounds.SelectionChanged -= scheduleVolumeRefresh;
            hitsounds.StepAdded -= onStepChanged;
            hitsounds.StepRemoved -= onStepChanged;
            hitsounds.StepUpdated -= onStepChanged;

            if (EditorBeatmap.IsNotNull() && EditorBeatmap.BeatmapSkin != null)
                EditorBeatmap.BeatmapSkin.BeatmapSkinChanged -= scheduleRegisterSampleSets;

            rhythmStripShown?.UnbindAll();

            base.Dispose(isDisposing);
        }

        private partial class HelpButton : RoundedButton, IHasPopover
        {
            public HelpButton()
            {
                Action = () => this.ShowPopover();
            }

            public Popover GetPopover() => new HelpPopover();
        }

        private partial class HelpPopover : OsuPopover
        {
            public HelpPopover()
            {
                OsuTextFlowContainer text;

                Child = text = new OsuTextFlowContainer(s => s.Font = OsuFont.GetFont(size: 14))
                {
                    Width = 420,
                    AutoSizeAxes = Axes.Y,
                };

                text.AddParagraph("Paint (2): click or drag in a lane to place or erase that sound. It snaps to the beat divisor. Right click deletes a step.");
                text.AddParagraph("Select (1): click a sound, ctrl or shift click to add, drag to box-select. Double click a lane name to select the whole lane. Drag a selected sound to move the selection. Delete removes it. Ctrl+A / X / C / V.");
                text.AddParagraph("Scroll the lane names, or use lanes up / down, to see other banks and sample sets. Click a lane name to hear it. Play the track to audition.");
                text.AddParagraph("The height of a sound is its volume. Paint volume is used for new sounds. Selected volume and all volume edit the selection or everything, and show the average when sounds differ. A .osu file stores one volume per object, so a step with mixed volumes gets a red mark.");
                text.AddParagraph("Hitsounds belong to the whole set, not to hit objects. Apply copies them onto objects at the same time (steps with no object are dropped), for this difficulty or every difficulty (which saves them). Import replaces the track with this difficulty's samples.");
                text.AddParagraph("Sample files: click + to add a file, click a sound to play it, right click to remove it. Sound folder chooses where the file chooser starts.");
            }
        }
    }
}
