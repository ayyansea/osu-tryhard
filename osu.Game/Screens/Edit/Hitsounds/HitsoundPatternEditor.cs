// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Collections.Specialized;
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
    /// Every <see cref="HitsoundStep"/> is a column; every row is a swimlane (see <see cref="HitsoundLayout"/>).
    /// Click or drag anywhere (snapped to the beat divisor) to paint a lane, right click to delete a step. Playing the track auditions the samples.
    /// </summary>
    public partial class HitsoundPatternEditor : TimelinePart
    {
        public const float CELL_WIDTH = 14;

        /// <summary>
        /// Minimum time between two edit previews, so that painting a long run doesn't pile up sounds.
        /// </summary>
        private const double preview_interval = 100;

        private readonly EditorHitsounds hitsounds;
        private readonly BindableBool selectMode;
        private readonly HitsoundLayout layout;

        [Resolved]
        private EditorClock clock { get; set; } = null!;

        [Resolved]
        private IBeatSnapProvider beatSnap { get; set; } = null!;

        private readonly Dictionary<HitsoundStep, MomentColumn> columns = new Dictionary<HitsoundStep, MomentColumn>();
        private List<MomentColumn>? sortedCache;

        // created up front as LoadBeatmap (which fills it) runs from the base class's loader, before this class's.
        private readonly Container stripes = new Container
        {
            RelativeSizeAxes = Axes.X,
            Height = HitsoundLayout.HEIGHT,
            Depth = float.MaxValue,
        };
        private Box ghost = null!;
        private Box selectionBox = null!;

        private SkinnableSound previewSound = null!;
        private double lastPreviewTime;

        private SkinnableSound[] pool = Array.Empty<SkinnableSound>();
        private int poolIndex;
        private double lastTime;

        private bool painting;
        private bool targetOn;
        private HitsoundLane targetLane;
        private double lastPaintTime;
        private readonly HashSet<long> paintedTimes = new HashSet<long>();

        private bool moving;
        private double moveAnchor;
        private bool boxing;
        private double boxStart;
        private HashSet<HitsoundStep> boxBase = new HashSet<HitsoundStep>();

        public HitsoundPatternEditor(EditorHitsounds hitsounds, HitsoundLayout layout, BindableBool selectMode)
        {
            this.hitsounds = hitsounds;
            this.layout = layout;
            this.selectMode = selectMode;
        }

        private List<MomentColumn> sorted => sortedCache ??= columns.Values.OrderBy(c => c.Moment.Time).ToList();

        [BackgroundDependencyLoader]
        private void load()
        {
            pool = Enumerable.Range(0, 24).Select(_ => new SkinnableSound()).ToArray();

            AddInternal(new EditorSkinProvidingContainer(EditorBeatmap)
            {
                Children = pool.Append(previewSound = new SkinnableSound()).ToArray(),
            });

            AddInternal(stripes);
        }

        protected override void LoadBeatmap(EditorBeatmap beatmap)
        {
            base.LoadBeatmap(beatmap);

            Content.AddRange(new Drawable[]
            {
                ghost = new Box
                {
                    RelativePositionAxes = Axes.X,
                    Origin = Anchor.TopCentre,
                    Size = new Vector2(CELL_WIDTH, HitsoundLayout.HEIGHT),
                    Colour = Color4.White,
                    Alpha = 0,
                },
                selectionBox = new Box
                {
                    RelativePositionAxes = Axes.X,
                    RelativeSizeAxes = Axes.X,
                    Height = HitsoundLayout.HEIGHT,
                    Colour = Color4.White,
                    Alpha = 0,
                },
            });

            foreach (var step in hitsounds.Steps)
                addColumn(step);

            hitsounds.StepAdded += addColumn;
            hitsounds.StepRemoved += removeColumn;
            hitsounds.StepUpdated += updateColumn;
            hitsounds.Selected.CollectionChanged += onSelectionChanged;
            layout.Changed += onLayoutChanged;

            onLayoutChanged();
        }

        private void onLayoutChanged()
        {
            stripes.Clear();

            foreach (var (y, lane) in layout.Rows)
            {
                stripes.Add(new Box
                {
                    RelativeSizeAxes = Axes.X,
                    Height = HitsoundLayout.ROW_HEIGHT - 1,
                    Y = y,
                    Colour = HitsoundLayout.ColourOf(lane),
                    Alpha = 0.08f,
                });
            }

            foreach (var column in columns.Values)
                column.Refresh();
        }

        private void addColumn(HitsoundStep step)
        {
            var column = new MomentColumn(HitsoundEditing.GetMoments(step).Single(), layout);
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
            // steps may have moved in time.
            sortedCache = null;

            if (columns.TryGetValue(step, out var column))
                column.UpdateMoment(HitsoundEditing.GetMoments(step).Single());
        }

        private void onSelectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            var selected = hitsounds.Selected.ToHashSet();

            foreach ((var step, var column) in columns)
                column.SetSelected(selected.Contains(step));
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
            bool over = ReceivePositionalInputAt(mouse) && isWithinGrid(mouse);

            ghost.Alpha = over && !selectMode.Value ? 0.2f : 0;

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

                var samples = sorted[i].Moment.Samples;

                if (samples.Count == 0)
                    continue;

                // round robin so that fast streams don't cut each other off.
                var sound = pool[poolIndex++ % pool.Length];
                sound.Samples = samples.Cast<ISampleInfo>().ToArray();
                sound.Play();
                played++;
            }
        }

        /// <summary>
        /// Plays the sample of a lane as an edit preview.
        /// This uses a single sound which is throttled, so that painting a long run doesn't stack up into something very loud.
        /// </summary>
        public void Preview(HitsoundLane lane)
        {
            if (Time.Current - lastPreviewTime < preview_interval)
                return;

            lastPreviewTime = Time.Current;

            previewSound.Samples = new ISampleInfo[] { lane.Create() };
            previewSound.Play();
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

        private bool isWithinGrid(Vector2 screenSpacePosition)
        {
            float y = Content.ToLocalSpace(screenSpacePosition).Y;
            return y >= 0 && y < HitsoundLayout.HEIGHT;
        }

        private bool tryGetLane(Vector2 screenSpacePosition, out HitsoundLane lane) => layout.TryGetLane(Content.ToLocalSpace(screenSpacePosition).Y, out lane);

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
            if (!isWithinGrid(e.ScreenSpaceMouseDownPosition))
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

            if (selectMode.Value)
                return beginSelect(e);

            // separators between sections have no lane.
            if (!tryGetLane(e.ScreenSpaceMouseDownPosition, out var lane))
                return true;

            var existing = columnNear(e.ScreenSpaceMousePosition.X);
            double time = existing?.Moment.Time ?? snappedTimeAt(e.ScreenSpaceMousePosition.X);

            hitsounds.BeginChange();
            painting = true;
            targetLane = lane;
            lastPaintTime = time;
            paintedTimes.Clear();

            // the first cell decides whether the stroke adds or removes.
            targetOn = existing == null || !HitsoundEditing.Has(existing.Moment, lane);

            paint(time);
            return true;
        }

        private bool beginSelect(MouseDownEvent e)
        {
            var column = columnNear(e.ScreenSpaceMousePosition.X);
            bool additive = e.ControlPressed || e.ShiftPressed;
            var selected = hitsounds.Selected;

            if (column != null)
            {
                var step = (HitsoundStep)column.Moment.Object;

                if (selected.Contains(step) && additive)
                {
                    selected.Remove(step);
                    return true;
                }

                if (!selected.Contains(step))
                {
                    if (!additive)
                        selected.Clear();

                    selected.Add(step);
                }

                hitsounds.BeginChange();
                moving = true;
                moveAnchor = column.Moment.Time;
                return true;
            }

            if (!additive)
                selected.Clear();

            boxing = true;
            boxStart = timeAtScreenX(e.ScreenSpaceMousePosition.X);
            boxBase = selected.ToHashSet();
            updateBox(boxStart);
            selectionBox.Alpha = 0.2f;
            return true;
        }

        private void updateBox(double time)
        {
            double lo = Math.Min(boxStart, time), hi = Math.Max(boxStart, time);

            selectionBox.X = (float)lo;
            selectionBox.Width = (float)(hi - lo);

            hitsounds.Selected.Clear();
            hitsounds.Selected.AddRange(hitsounds.Steps.Where(s => boxBase.Contains(s) || (s.StartTime >= lo && s.StartTime <= hi)));
        }

        protected override bool OnDragStart(DragStartEvent e) => painting || moving || boxing;

        protected override void OnDrag(DragEvent e)
        {
            if (moving)
            {
                moveAnchor += hitsounds.MoveSelected(snappedTimeAt(e.ScreenSpaceMousePosition.X) - moveAnchor);
                return;
            }

            if (boxing)
            {
                updateBox(timeAtScreenX(e.ScreenSpaceMousePosition.X));
                return;
            }

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

            if (moving && e.Button == MouseButton.Left)
            {
                moving = false;
                hitsounds.ResolveCollisions();
                hitsounds.EndChange();
            }

            if (boxing && e.Button == MouseButton.Left)
            {
                boxing = false;
                selectionBox.Alpha = 0;
            }

            base.OnMouseUp(e);
        }

        private void paint(double time)
        {
            // every time is only painted once per stroke, as the mouse moving back and forth would otherwise repaint (and replay) the same cell.
            if (!paintedTimes.Add((long)Math.Round(time)))
                return;

            var step = hitsounds.FindStep(time);

            if (step == null)
            {
                // erasing on empty space does nothing.
                if (!targetOn)
                    return;

                step = new HitsoundStep { StartTime = time };
                hitsounds.Add(step);
            }

            if (!HitsoundEditing.SetLane(HitsoundEditing.GetMoments(step).Single(), targetLane, targetOn))
                return;

            // steps without any sample left are not worth keeping.
            if (step.Samples.Count == 0)
                hitsounds.Remove(step);
            else
                hitsounds.Update(step);

            if (targetOn)
                Preview(targetLane);
        }

        protected override void Dispose(bool isDisposing)
        {
            base.Dispose(isDisposing);

            layout.Changed -= onLayoutChanged;

            if (hitsounds.IsNotNull())
            {
                hitsounds.StepAdded -= addColumn;
                hitsounds.StepRemoved -= removeColumn;
                hitsounds.StepUpdated -= updateColumn;
                hitsounds.Selected.CollectionChanged -= onSelectionChanged;
            }
        }

        private partial class MomentColumn : CompositeDrawable
        {
            private static readonly Color4 off_colour = CatppuccinMochaColourProvider.SURFACE1;

            public HitMoment Moment { get; private set; }

            private readonly HitsoundLayout layout;
            private readonly Box[] cells;
            private readonly Box outline;
            private readonly Box warning;

            public MomentColumn(HitMoment moment, HitsoundLayout layout)
            {
                Moment = moment;
                this.layout = layout;

                RelativePositionAxes = Axes.X;
                X = (float)moment.Time;
                Origin = Anchor.TopCentre;
                Size = new Vector2(CELL_WIDTH, HitsoundLayout.HEIGHT);

                AddInternal(outline = new Box
                {
                    Position = new Vector2(-2),
                    Size = new Vector2(CELL_WIDTH + 4, HitsoundLayout.HEIGHT + 4),
                    Colour = Color4.White,
                    Alpha = 0,
                });

                // marks steps which can't be saved to a .osu file exactly.
                AddInternal(warning = new Box
                {
                    Y = -4,
                    RelativeSizeAxes = Axes.X,
                    Height = 3,
                    Colour = CatppuccinMochaColourProvider.RED,
                    Alpha = 0,
                });

                cells = new Box[HitsoundLayout.MAX_ROWS];

                for (int i = 0; i < cells.Length; i++)
                {
                    AddInternal(cells[i] = new Box
                    {
                        RelativeSizeAxes = Axes.X,
                        Height = HitsoundLayout.ROW_HEIGHT - 2,
                    });
                }

                Refresh();
            }

            public void SetSelected(bool selected) => outline.Alpha = selected ? 0.9f : 0;

            public void UpdateMoment(HitMoment moment)
            {
                Moment = moment;
                X = (float)moment.Time;
                Refresh();
            }

            /// <summary>
            /// Updates the cells to match the visible lanes and the samples of the step.
            /// </summary>
            public void Refresh()
            {
                var rows = layout.Rows;

                warning.Alpha = HitsoundEditing.IsRepresentable(Moment.Samples) ? 0 : 1;

                for (int i = 0; i < cells.Length; i++)
                {
                    if (i >= rows.Count)
                    {
                        cells[i].Alpha = 0;
                        continue;
                    }

                    bool on = HitsoundEditing.Has(Moment, rows[i].Lane);

                    cells[i].Y = rows[i].Y + 1;
                    cells[i].Colour = on ? HitsoundLayout.ColourOf(rows[i].Lane) : off_colour;
                    cells[i].Alpha = on ? 0.95f : 0.35f;
                }
            }
        }
    }
}
