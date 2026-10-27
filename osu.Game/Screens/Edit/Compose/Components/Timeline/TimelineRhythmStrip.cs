// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Allocation;
using osu.Framework.Extensions.ObjectExtensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Shapes;
using osu.Game.Overlays;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Objects.Types;
using osu.Game.Screens.Edit.Components.Timelines.Summary.Parts;
using osu.Game.Screens.Edit.Rhythm;
using osuTK;

namespace osu.Game.Screens.Edit.Compose.Components.Timeline
{
    /// <summary>
    /// A thin strip displaying the rhythm blueprint (see <see cref="EditorRhythm"/>) in line with the timeline.
    /// </summary>
    public partial class TimelineRhythmStrip : TimelinePart
    {
        private const float click_size = 7;
        private const float hold_height = 5;

        [Resolved]
        private EditorRhythm rhythm { get; set; } = null!;

        [BackgroundDependencyLoader]
        private void load(OverlayColourProvider colourProvider)
        {
            AddRangeInternal(new Drawable[]
            {
                new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = colourProvider.Background6,
                    Alpha = 0.9f,
                    Depth = float.MaxValue,
                },
                new Box
                {
                    RelativeSizeAxes = Axes.X,
                    Height = 1,
                    Colour = colourProvider.Background2,
                    Depth = float.MaxValue / 2,
                },
            });
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            rhythm.Beatmap.HitObjectAdded += scheduleRefresh;
            rhythm.Beatmap.HitObjectRemoved += scheduleRefresh;
            rhythm.Beatmap.HitObjectUpdated += scheduleRefresh;

            refresh();
        }

        private void scheduleRefresh(HitObject _) => Scheduler.AddOnce(refresh);

        private void refresh()
        {
            Clear();

            foreach (var hitObject in rhythm.Beatmap.HitObjects)
            {
                if (hitObject is IHasDuration hold)
                {
                    Add(new Circle
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        RelativePositionAxes = Axes.X,
                        RelativeSizeAxes = Axes.X,
                        X = (float)hitObject.StartTime,
                        Width = (float)hold.Duration,
                        Height = hold_height,
                        Colour = CatppuccinMochaColourProvider.TEAL,
                    });
                }

                Add(new Circle
                {
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.Centre,
                    RelativePositionAxes = Axes.X,
                    X = (float)hitObject.StartTime,
                    Size = new Vector2(click_size),
                    Colour = hitObject is IHasDuration ? CatppuccinMochaColourProvider.TEAL : CatppuccinMochaColourProvider.MAUVE,
                });
            }
        }

        protected override void Dispose(bool isDisposing)
        {
            base.Dispose(isDisposing);

            if (rhythm.IsNotNull())
            {
                rhythm.Beatmap.HitObjectAdded -= scheduleRefresh;
                rhythm.Beatmap.HitObjectRemoved -= scheduleRefresh;
                rhythm.Beatmap.HitObjectUpdated -= scheduleRefresh;
            }
        }
    }
}
