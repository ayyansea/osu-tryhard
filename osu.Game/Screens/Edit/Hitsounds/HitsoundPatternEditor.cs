// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Extensions.ObjectExtensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Input.Events;
using osu.Game.Audio;
using osu.Game.Overlays;
using osu.Game.Rulesets.Edit;
using osu.Game.Screens.Edit.Components.Timelines.Summary.Parts;
using osu.Game.Skinning;
using osuTK;
using osuTK.Graphics;
using osuTK.Input;

namespace osu.Game.Screens.Edit.Hitsounds
{
    /// <summary>
    /// A step-sequencer style grid, displayed as the timeline's content so that it stays aligned with the beatmap's time.
    /// Every <see cref="HitsoundStep"/> is a column; rows are the bank and the whistle / finish / clap additions.
    /// Click or drag anywhere (snapped to the beat divisor) to paint, right click to delete a step. Playing the track auditions the samples.
    /// </summary>
    public partial class HitsoundPatternEditor : TimelinePart
    {
        public const float ROW_HEIGHT = 32;
        public const float CELL_WIDTH = 14;
        public const float HEIGHT = 4 * ROW_HEIGHT;

        // row 0 is the bank, the rest are additions.
        private static readonly string?[] row_samples = { null, HitSampleInfo.HIT_WHISTLE, HitSampleInfo.HIT_FINISH, HitSampleInfo.HIT_CLAP };

        private readonly EditorHitsounds hitsounds;
        private readonly Bindable<string?> brushBank;
        private readonly Bindable<int> brushSampleSet;

        [Resolved]
        private EditorClock clock { get; set; } = null!;

        [Resolved]
        private IBeatSnapProvider beatSnap { get; set; } = null!;

        private readonly Dictionary<HitsoundStep, MomentColumn> columns = new Dictionary<HitsoundStep, MomentColumn>();
        private List<MomentColumn>? sortedCache;

        private Box ghost = null!;

        private SkinnableSound[] pool = Array.Empty<SkinnableSound>();
        private int poolIndex;
        private double lastTime;

        private bool painting;
        private bool targetOn;
        private string targetBank = HitSampleInfo.BANK_NORMAL;
        private int targetRow;
        private double lastPaintTime;

        public HitsoundPatternEditor(EditorHitsounds hitsounds, Bindable<string?> brushBank, Bindable<int> brushSampleSet)
        {
            this.hitsounds = hitsounds;
            this.brushBank = brushBank;
            this.brushSampleSet = brushSampleSet;
        }

        private string? brushSuffix => brushSampleSet.Value >= 2 ? brushSampleSet.Value.ToString() : null;

        private List<MomentColumn> sorted => sortedCache ??= columns.Values.OrderBy(c => c.Moment.Time).ToList();

        [BackgroundDependencyLoader]
        private void load()
        {
            pool = Enumerable.Range(0, 24).Select(_ => new SkinnableSound()).ToArray();

            AddInternal(new EditorSkinProvidingContainer(EditorBeatmap) { Children = pool });
        }

        protected override void LoadBeatmap(EditorBeatmap beatmap)
        {
            base.LoadBeatmap(beatmap);

            Content.Add(ghost = new Box
            {
                RelativePositionAxes = Axes.X,
                Origin = Anchor.TopCentre,
                Size = new Vector2(CELL_WIDTH, HEIGHT),
                Colour = Color4.White,
                Alpha = 0,
            });

            foreach (var step in hitsounds.Steps)
                addColumn(step);

            hitsounds.StepAdded += addColumn;
            hitsounds.StepRemoved += removeColumn;
            hitsounds.StepUpdated += updateColumn;
        }

        private void addColumn(HitsoundStep step)
        {
            var column = new MomentColumn(HitsoundEditing.GetMoments(step).Single());
            columns[step] = column;
            Content.Add(column);
            sortedCache = null;
        }

        private void removeColumn(HitsoundStep step)
        {
            if (!columns.Remove(step, out var column))
                return;

            Content.Remove(column, true);
            sortedCache = null;
        }

        private void updateColumn(HitsoundStep step)
        {
            if (columns.TryGetValue(step, out var column))
                column.UpdateMoment(HitsoundEditing.GetMoments(step).Single());
        }

        protected override void Update()
        {
            base.Update();

            double now = clock.CurrentTime;

            // only audition on smooth forward playback, not on seeks.
            if (clock.IsRunning && now > lastTime && now - lastTime < 200)
                audition(lastTime, now);

            lastTime = now;

            var mouse = GetContainingInputManager().CurrentState.Mouse.Position;
            bool over = ReceivePositionalInputAt(mouse) && tryGetRow(mouse, out _);

            ghost.Alpha = over ? 0.2f : 0;

            if (over)
                ghost.X = (float)snappedTimeAt(mouse.X);
        }

        private void audition(double from, double to)
        {
            int played = 0;

            for (int i = lowerBound(from); i < sorted.Count && sorted[i].Moment.Time <= to && played < 8; i++)
            {
                if (sorted[i].Moment.Time <= from)
                    continue;

                play(sorted[i].Moment);
                played++;
            }
        }

        private void play(HitMoment moment)
        {
            if (moment.Samples.Count == 0 || pool.Length == 0)
                return;

            // round robin so that fast streams don't cut each other off.
            var sound = pool[poolIndex++ % pool.Length];
            sound.Samples = moment.Samples.Cast<ISampleInfo>().ToArray();
            sound.Play();
        }

        /// <summary>
        /// Index of the first column with a time greater than or equal to <paramref name="time"/>.
        /// </summary>
        private int lowerBound(double time)
        {
            int lo = 0, hi = sorted.Count;

            while (lo < hi)
            {
                int mid = (lo + hi) / 2;
                if (sorted[mid].Moment.Time < time)
                    lo = mid + 1;
                else
                    hi = mid;
            }

            return lo;
        }

        private double timeAtScreenX(float screenX) => Math.Max(0, Content.ToLocalSpace(new Vector2(screenX, 0)).X / Content.DrawWidth * Content.RelativeChildSize.X);

        private double snappedTimeAt(float screenX) => Math.Max(0, beatSnap.SnapTime(timeAtScreenX(screenX), null));

        private bool tryGetRow(Vector2 screenSpacePosition, out int row)
        {
            row = (int)Math.Floor(Content.ToLocalSpace(screenSpacePosition).Y / ROW_HEIGHT);
            return row >= 0 && row < row_samples.Length;
        }

        private MomentColumn? columnNear(float screenX) =>
            columns.Values.Where(c => Math.Abs(c.ScreenSpaceDrawQuad.Centre.X - screenX) <= CELL_WIDTH / 2)
                   .OrderBy(c => Math.Abs(c.ScreenSpaceDrawQuad.Centre.X - screenX))
                   .FirstOrDefault();

        /// <summary>
        /// The times to paint between two times: all existing steps, and every snapped tick.
        /// </summary>
        private IEnumerable<double> paintTimesBetween(double a, double b)
        {
            double from = Math.Min(a, b), to = Math.Max(a, b);

            foreach (var column in sorted.Skip(lowerBound(from - 1)).TakeWhile(c => c.Moment.Time <= to + 1))
                yield return column.Moment.Time;

            double t = beatSnap.SnapTime(from, null);

            for (int i = 0; i < 4096 && t <= to + 0.5; i++)
            {
                yield return Math.Max(0, t);

                double next = beatSnap.SnapTime(t + beatSnap.GetBeatLengthAtTime(t), null);
                t = next > t ? next : t + beatSnap.GetBeatLengthAtTime(t);
            }
        }

        protected override bool OnMouseDown(MouseDownEvent e)
        {
            if (!tryGetRow(e.ScreenSpaceMouseDownPosition, out int row))
                return false;

            if (e.Button == MouseButton.Right)
            {
                var column = columnNear(e.ScreenSpaceMousePosition.X);

                if (column == null)
                    return false;

                hitsounds.BeginChange();
                hitsounds.Remove((HitsoundStep)column.Moment.Object);
                hitsounds.EndChange();
                return true;
            }

            if (e.Button != MouseButton.Left)
                return false;

            var existing = columnNear(e.ScreenSpaceMousePosition.X);
            double time = existing?.Moment.Time ?? snappedTimeAt(e.ScreenSpaceMousePosition.X);

            hitsounds.BeginChange();
            painting = true;
            targetRow = row;
            lastPaintTime = time;

            // the first cell decides whether the stroke adds or removes.
            if (row == 0)
                targetBank = brushBank.Value ?? HitsoundEditing.NextBank(existing == null ? null : HitsoundEditing.GetBank(existing.Moment));
            else
                targetOn = existing == null || !HitsoundEditing.Has(existing.Moment, row_samples[row]!);

            paint(time);
            return true;
        }

        protected override bool OnDragStart(DragStartEvent e) => painting;

        protected override void OnDrag(DragEvent e)
        {
            if (!painting)
                return;

            double time = snappedTimeAt(e.ScreenSpaceMousePosition.X);

            foreach (double t in paintTimesBetween(lastPaintTime, time).ToList())
                paint(t);

            lastPaintTime = time;
        }

        protected override void OnMouseUp(MouseUpEvent e)
        {
            if (painting && e.Button == MouseButton.Left)
            {
                painting = false;
                hitsounds.EndChange();
            }

            base.OnMouseUp(e);
        }

        private void paint(double time)
        {
            var step = hitsounds.FindStep(time);

            if (step == null)
            {
                // erasing on empty space does nothing.
                if (targetRow != 0 && !targetOn)
                    return;

                step = new HitsoundStep { StartTime = time };
                step.Samples.Add(new HitSampleInfo(HitSampleInfo.HIT_NORMAL, brushBank.Value ?? HitSampleInfo.BANK_NORMAL, brushSuffix));
                hitsounds.Add(step);
            }

            var moment = HitsoundEditing.GetMoments(step).Single();

            bool changed = targetRow == 0
                ? HitsoundEditing.SetBank(moment, targetBank, brushSuffix)
                : HitsoundEditing.SetAddition(moment, row_samples[targetRow]!, targetOn, brushBank.Value, brushSuffix);

            if (changed)
                hitsounds.Update(step);

            if (changed || targetRow == 0 || targetOn)
                play(moment);
        }

        protected override void Dispose(bool isDisposing)
        {
            base.Dispose(isDisposing);

            if (hitsounds.IsNotNull())
            {
                hitsounds.StepAdded -= addColumn;
                hitsounds.StepRemoved -= removeColumn;
                hitsounds.StepUpdated -= updateColumn;
            }
        }

        private partial class MomentColumn : CompositeDrawable
        {
            private static readonly Color4 off_colour = CatppuccinMochaColourProvider.SURFACE1;

            public HitMoment Moment { get; private set; }

            private readonly Box[] cells;

            public MomentColumn(HitMoment moment)
            {
                Moment = moment;

                RelativePositionAxes = Axes.X;
                Origin = Anchor.TopCentre;
                Size = new Vector2(CELL_WIDTH, HEIGHT);

                cells = new Box[row_samples.Length];

                for (int i = 0; i < cells.Length; i++)
                {
                    AddInternal(cells[i] = new Box
                    {
                        RelativeSizeAxes = Axes.X,
                        Height = ROW_HEIGHT - 2,
                        Y = i * ROW_HEIGHT + 1,
                    });
                }

                UpdateMoment(moment);
            }

            public void UpdateMoment(HitMoment moment)
            {
                Moment = moment;
                X = (float)moment.Time;

                cells[0].Colour = bankColour(HitsoundEditing.GetBank(moment));
                cells[0].Alpha = 0.9f;

                for (int i = 1; i < cells.Length; i++)
                {
                    bool on = HitsoundEditing.Has(moment, row_samples[i]!);
                    cells[i].Colour = on ? additionColour(row_samples[i]!) : off_colour;
                    cells[i].Alpha = on ? 0.95f : 0.5f;
                }
            }

            private static Color4 bankColour(string? bank) => bank switch
            {
                HitSampleInfo.BANK_NORMAL => CatppuccinMochaColourProvider.SAPPHIRE,
                HitSampleInfo.BANK_SOFT => CatppuccinMochaColourProvider.GREEN,
                HitSampleInfo.BANK_DRUM => CatppuccinMochaColourProvider.PEACH,
                _ => off_colour,
            };

            private static Color4 additionColour(string sample) => sample switch
            {
                HitSampleInfo.HIT_WHISTLE => CatppuccinMochaColourProvider.YELLOW,
                HitSampleInfo.HIT_FINISH => CatppuccinMochaColourProvider.RED,
                _ => CatppuccinMochaColourProvider.MAUVE,
            };
        }
    }
}
