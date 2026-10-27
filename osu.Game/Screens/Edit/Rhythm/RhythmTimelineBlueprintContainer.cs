// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Input.Events;
using osu.Game.Rulesets.Edit;
using osu.Game.Screens.Edit.Compose.Components.Timeline;
using osuTK;
using osuTK.Input;

namespace osu.Game.Screens.Edit.Rhythm
{
    /// <summary>
    /// The timeline content of the <see cref="RhythmScreen"/>.
    /// Selection works exactly like in the compose screen's timeline, with the addition of placing clicks and holds.
    /// </summary>
    internal partial class RhythmTimelineBlueprintContainer : TimelineBlueprintContainer
    {
        public readonly Bindable<RhythmTool> Tool = new Bindable<RhythmTool>();

        [Resolved]
        private Timeline timeline { get; set; } = null!;

        [Resolved]
        private IBeatSnapProvider beatSnapProvider { get; set; } = null!;

        private RhythmHold? holdPlacement;

        protected override bool ShowSamplePoints => false;

        public RhythmTimelineBlueprintContainer()
            : base(null!)
        {
        }

        protected override bool OnMouseDown(MouseDownEvent e)
        {
            if (Tool.Value == RhythmTool.Select || e.Button != MouseButton.Left)
                return base.OnMouseDown(e);

            if (getSnappedTime(e.ScreenSpaceMousePosition) is not double time)
                return true;

            Beatmap.SelectedHitObjects.Clear();

            switch (Tool.Value)
            {
                case RhythmTool.Click:
                    RhythmPlacement.AddClick(Beatmap, time);
                    break;

                case RhythmTool.Hold:
                    holdPlacement = new RhythmHold { StartTime = time };
                    Beatmap.PlacementObject.Value = holdPlacement;
                    break;
            }

            return true;
        }

        protected override bool OnDragStart(DragStartEvent e)
        {
            // placement tools don't make drag selections.
            if (Tool.Value != RhythmTool.Select)
                return holdPlacement != null;

            return base.OnDragStart(e);
        }

        protected override void OnDrag(DragEvent e)
        {
            if (holdPlacement != null)
            {
                updateHoldPlacement(e.ScreenSpaceMousePosition);
                return;
            }

            base.OnDrag(e);
        }

        protected override void OnDragEnd(DragEndEvent e)
        {
            if (holdPlacement != null)
                return;

            base.OnDragEnd(e);
        }

        protected override void OnMouseUp(MouseUpEvent e)
        {
            if (holdPlacement != null && e.Button == MouseButton.Left)
            {
                updateHoldPlacement(e.ScreenSpaceMousePosition);
                commitHoldPlacement();
                return;
            }

            base.OnMouseUp(e);
        }

        private void updateHoldPlacement(Vector2 screenSpacePosition)
        {
            if (holdPlacement == null || getSnappedTime(screenSpacePosition) is not double time)
                return;

            holdPlacement.Duration = Math.Max(0, time - holdPlacement.StartTime);
        }

        private void commitHoldPlacement()
        {
            var hold = holdPlacement!;

            holdPlacement = null;
            Beatmap.PlacementObject.Value = null;

            // releasing without dragging out a duration places a click instead.
            if (hold.Duration <= 0)
                RhythmPlacement.AddClick(Beatmap, hold.StartTime);
            else
                RhythmPlacement.AddHold(Beatmap, hold.StartTime, hold.Duration);
        }

        private double? getSnappedTime(Vector2 screenSpacePosition) => timeline.FindSnappedPositionAndTime(screenSpacePosition).Time;
    }

    /// <summary>
    /// Helpers for adding rhythm objects without creating duplicates.
    /// </summary>
    public static class RhythmPlacement
    {
        /// <summary>
        /// Objects starting within this many milliseconds of each other are considered to be at the same time.
        /// </summary>
        private const double same_time_leniency = 1;

        public static RhythmHitObject? AddClick(EditorBeatmap rhythmBeatmap, double time)
        {
            if (hasObjectAt(rhythmBeatmap, time))
                return null;

            var click = new RhythmHitObject { StartTime = time };
            rhythmBeatmap.Add(click);
            return click;
        }

        public static RhythmHold? AddHold(EditorBeatmap rhythmBeatmap, double time, double duration)
        {
            if (hasObjectAt(rhythmBeatmap, time))
                return null;

            var hold = new RhythmHold { StartTime = time, Duration = duration };
            rhythmBeatmap.Add(hold);
            return hold;
        }

        private static bool hasObjectAt(EditorBeatmap rhythmBeatmap, double time)
            => rhythmBeatmap.HitObjects.Any(h => h != rhythmBeatmap.PlacementObject.Value && Math.Abs(h.StartTime - time) < same_time_leniency);
    }
}
