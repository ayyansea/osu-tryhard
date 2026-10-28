// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Overlays;
using osu.Game.Screens.Edit.Compose.Components.Timeline;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Screens.Edit.Hitsounds
{
    /// <summary>
    /// An editor screen for hitsounding: a sample picker / previewer, and a pattern grid aligned to the timeline.
    /// The pattern grid edits a hitsound track (see <see cref="EditorHitsounds"/>) which exists independently of the hit objects.
    /// </summary>
    public partial class HitsoundsScreen : EditorScreen
    {
        /// <summary>
        /// The bank applied to newly painted additions and to the bank row. Null follows the normal bank (and makes the bank row cycle).
        /// </summary>
        private readonly Bindable<string?> brushBank = new Bindable<string?>();

        private readonly Bindable<int> brushSampleSet = new Bindable<int>(1);

        private readonly EditorHitsounds hitsounds;

        public HitsoundsScreen(EditorHitsounds hitsounds)
            : base(EditorScreenMode.Hitsounds)
        {
            this.hitsounds = hitsounds;
        }

        public override EditorChangeHandler ChangeHandler => hitsounds.ChangeHandler;

        [BackgroundDependencyLoader]
        private void load()
        {
            OsuTextFlowContainer legend;

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
                                    new HitsoundPicker(brushBank, brushSampleSet) { RelativeSizeAxes = Axes.X },
                                    new FillFlowContainer
                                    {
                                        RelativeSizeAxes = Axes.X,
                                        AutoSizeAxes = Axes.Y,
                                        Direction = FillDirection.Vertical,
                                        Spacing = new Vector2(8),
                                        Padding = new MarginPadding(10),
                                        Children = new Drawable[]
                                        {
                                            legend = new OsuTextFlowContainer(s => s.Font = OsuFont.GetFont(size: 12))
                                            {
                                                RelativeSizeAxes = Axes.X,
                                                AutoSizeAxes = Axes.Y,
                                            },
                                            new FillFlowContainer
                                            {
                                                AutoSizeAxes = Axes.Both,
                                                Direction = FillDirection.Horizontal,
                                                Spacing = new Vector2(8),
                                                Children = new Drawable[]
                                                {
                                                    new RoundedButton { Width = 200, Height = 28, Text = "apply to hit objects", Action = hitsounds.ApplyToHitObjects },
                                                    new RoundedButton { Width = 200, Height = 28, Text = "import from hit objects", Action = hitsounds.ImportFromHitObjects },
                                                },
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
                            Child = new TimelineArea(new HitsoundPatternEditor(hitsounds, brushBank, brushSampleSet) { RelativeSizeAxes = Axes.Both }, HitsoundPatternEditor.HEIGHT, false)
                            {
                                Anchor = Anchor.Centre,
                                Origin = Anchor.Centre,
                            },
                        },
                    },
                }
            };

            addLegend(legend, "bank row", "click to cycle normal / soft / drum, or to set the brush bank. ", CatppuccinMochaColourProvider.SAPPHIRE);
            addLegend(legend, "whistle row", "toggle a whistle. ", CatppuccinMochaColourProvider.YELLOW);
            addLegend(legend, "finish row", "toggle a finish. ", CatppuccinMochaColourProvider.RED);
            addLegend(legend, "clap row", "toggle a clap.\n", CatppuccinMochaColourProvider.MAUVE);
            legend.AddText("click anywhere in the grid (snaps to the beat divisor) to place a hitsound, drag to paint, right click to delete a step. play the track to audition.\n" +
                           "steps are independent of hit objects. \"apply to hit objects\" assigns them to objects at the same time (steps with no object are dropped); \"import\" replaces the steps with the objects' current samples.");
        }

        private static void addLegend(OsuTextFlowContainer legend, string name, string description, Color4 colour)
        {
            legend.AddText(name, s =>
            {
                s.Colour = colour;
                s.Font = OsuFont.GetFont(size: 12, weight: FontWeight.SemiBold);
            });
            legend.AddText(": " + description);
        }
    }
}
