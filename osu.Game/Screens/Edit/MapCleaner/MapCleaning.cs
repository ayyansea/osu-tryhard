// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Utils;
using osu.Game.Beatmaps.ControlPoints;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Objects.Types;
using osu.Game.Screens.Edit.ComboColours;

namespace osu.Game.Screens.Edit.MapCleaner
{
    public class MapCleanerOptions
    {
        /// <summary>
        /// Move hit object starts and ends (slider length, spinner duration) onto the closest snap.
        /// </summary>
        public bool ResnapObjects { get; set; } = true;

        /// <summary>
        /// Move kiai / effect points and bookmarks onto the closest snap.
        /// </summary>
        public bool ResnapOther { get; set; } = true;

        /// <summary>
        /// Only times at most this far (in ms) from a snap are moved; anything further is probably intended (or needs a human).
        /// </summary>
        public double MaxResnapDistance { get; set; } = 5;

        /// <summary>
        /// Remove timing points that repeat the previous one (same BPM and meter, on one of its bar lines).
        /// </summary>
        public bool RemoveRedundantTimingPoints { get; set; } = true;

        /// <summary>
        /// Clear combo colour skips that do nothing (objects not starting a combo), and reduce skips of a full palette cycle or more.
        /// </summary>
        public bool TidyComboColourSkips { get; set; } = true;
    }

    public enum CleanupKind
    {
        ResnapObject,
        ResnapEffectPoint,
        ResnapBookmark,
        RedundantTimingPoint,
        ComboColourSkip,
    }

    /// <summary>
    /// One planned change: what it does, for the preview, and how to do it.
    /// </summary>
    public class CleanupChange
    {
        public CleanupKind Kind { get; }
        public double Time { get; }
        public string Description { get; }

        internal readonly Action Apply;

        public CleanupChange(CleanupKind kind, double time, string description, Action apply)
        {
            Kind = kind;
            Time = time;
            Description = description;
            Apply = apply;
        }
    }

    /// <summary>
    /// Plans and applies clean-ups of the open difficulty. Planning doesn't change anything, so the result can be previewed first.
    /// </summary>
    public static class MapCleaning
    {
        /// <summary>
        /// Times closer than this to a snap are left alone: stored times are whole milliseconds, so a snap is usually a fraction away.
        /// </summary>
        public const double MIN_RESNAP_DISTANCE = 1;

        public static List<CleanupChange> Plan(EditorBeatmap beatmap, MapCleanerOptions options)
        {
            var changes = new List<CleanupChange>();
            var controlPoints = beatmap.ControlPointInfo;

            // the redundancy check reads the timing points as they are now, so it runs before anything else and the resnaps use the timing that stays.
            var removedTimingPoints = options.RemoveRedundantTimingPoints ? findRedundantTimingPoints(controlPoints) : new List<TimingControlPoint>();

            foreach (var point in removedTimingPoints)
            {
                changes.Add(new CleanupChange(CleanupKind.RedundantTimingPoint, point.Time, $"Remove timing point repeating the previous one ({60000 / point.BeatLength:0.##} BPM)", () =>
                {
                    var group = controlPoints.GroupAt(point.Time);
                    group.Remove(point);

                    if (group.ControlPoints.Count == 0)
                        controlPoints.RemoveGroup(group);
                }));
            }

            if (options.ResnapObjects)
            {
                foreach (var hitObject in beatmap.HitObjects)
                {
                    if (planObjectResnap(controlPoints, hitObject, options.MaxResnapDistance) is CleanupChange change)
                        changes.Add(change);
                }
            }

            if (options.ResnapOther)
            {
                foreach (var point in controlPoints.EffectPoints.ToArray())
                {
                    if (!tryResnap(controlPoints, point.Time, options.MaxResnapDistance, out double snapped))
                        continue;

                    double originalTime = point.Time;

                    changes.Add(new CleanupChange(CleanupKind.ResnapEffectPoint, originalTime, $"Resnap effect point ({describeOffset(originalTime, snapped)})", () =>
                    {
                        var group = controlPoints.GroupAt(originalTime);
                        group.Remove(point);

                        if (group.ControlPoints.Count == 0)
                            controlPoints.RemoveGroup(group);

                        controlPoints.Add(snapped, point);
                    }));
                }

                foreach (int bookmark in beatmap.Bookmarks.ToArray())
                {
                    if (!tryResnap(controlPoints, bookmark, options.MaxResnapDistance, out double snapped))
                        continue;

                    int snappedBookmark = (int)Math.Round(snapped);

                    if (snappedBookmark == bookmark)
                        continue;

                    changes.Add(new CleanupChange(CleanupKind.ResnapBookmark, bookmark, $"Resnap bookmark ({describeOffset(bookmark, snappedBookmark)})", () =>
                    {
                        beatmap.Bookmarks.Remove(bookmark);

                        if (!beatmap.Bookmarks.Contains(snappedBookmark))
                            beatmap.Bookmarks.Add(snappedBookmark);
                    }));
                }
            }

            if (options.TidyComboColourSkips)
                changes.AddRange(planComboColourSkips(beatmap));

            return changes.OrderBy(c => c.Time).ToList();
        }

        /// <summary>
        /// Applies the changes as one undo step.
        /// </summary>
        public static void Apply(EditorBeatmap beatmap, IEnumerable<CleanupChange> changes)
        {
            beatmap.BeginChange();

            foreach (var change in changes)
                change.Apply();

            beatmap.UpdateAllHitObjects();
            beatmap.EndChange();
        }

        private static List<TimingControlPoint> findRedundantTimingPoints(ControlPointInfo controlPoints)
        {
            var redundant = new List<TimingControlPoint>();
            TimingControlPoint? previous = null;

            foreach (var point in controlPoints.TimingPoints)
            {
                if (previous != null && repeats(previous, point))
                {
                    // the next one is compared to the one that stays, which has the same bar lines.
                    redundant.Add(point);
                    continue;
                }

                previous = point;
            }

            return redundant;
        }

        private static bool repeats(TimingControlPoint previous, TimingControlPoint point)
        {
            if (point.OmitFirstBarLine || !Precision.AlmostEquals(previous.BeatLength, point.BeatLength) || !point.TimeSignature.Equals(previous.TimeSignature))
                return false;

            // a red line off the previous one's bar lines resets where bars (and the snap grid) fall, so it isn't redundant.
            double barLength = previous.BeatLength * previous.TimeSignature.Numerator;
            double bars = (point.Time - previous.Time) / barLength;

            return Math.Abs(bars - Math.Round(bars)) * barLength < MIN_RESNAP_DISTANCE;
        }

        private static CleanupChange? planObjectResnap(ControlPointInfo controlPoints, HitObject hitObject, double maxDistance)
        {
            double start = hitObject.StartTime;
            bool moveStart = tryResnap(controlPoints, start, maxDistance, out double newStart);

            if (!moveStart)
                newStart = start;

            double duration = hitObject.GetEndTime() - start;
            double? newDuration = null;

            if (hitObject is IHasDuration && duration > 0)
            {
                double end = newStart + duration;

                if (tryResnap(controlPoints, end, maxDistance, out double newEnd) && newEnd > newStart)
                    newDuration = newEnd - newStart;
            }

            if (!moveStart && newDuration == null)
                return null;

            var parts = new List<string>();
            if (moveStart) parts.Add($"start {describeOffset(start, newStart)}");
            if (newDuration is double d) parts.Add($"end {describeOffset(newStart + duration, newStart + d)}");

            return new CleanupChange(CleanupKind.ResnapObject, start, $"Resnap {objectName(hitObject)} ({string.Join(", ", parts)})", () =>
            {
                hitObject.StartTime = newStart;

                if (newDuration is not double target)
                    return;

                switch (hitObject)
                {
                    // the duration of a slider follows from its length: scale the length rather than the velocity, so no extra velocity changes appear.
                    case IHasPath hasPath:
                        hasPath.Path.ExpectedDistance.Value = hasPath.Path.Distance * target / duration;
                        break;

                    case IHasDuration hasDuration:
                        hasDuration.Duration = target;
                        break;
                }
            });
        }

        private static IEnumerable<CleanupChange> planComboColourSkips(EditorBeatmap beatmap)
        {
            int colourCount = beatmap.BeatmapSkin?.ComboColours.Count ?? 0;
            var starts = ComboColourEditing.GetComboStarts(beatmap.HitObjects).ToHashSet();

            foreach (var combo in beatmap.HitObjects.OfType<IHasComboInformation>())
            {
                int offset = combo.ComboOffset;
                int tidied;

                if (!starts.Contains(combo))
                    tidied = 0;
                else if (colourCount > 0)
                    tidied = offset % colourCount;
                else
                    continue;

                if (tidied == offset)
                    continue;

                var hitObject = (HitObject)combo;

                yield return new CleanupChange(CleanupKind.ComboColourSkip, hitObject.StartTime,
                    starts.Contains(combo)
                        ? $"Reduce combo colour skip of {objectName(hitObject)} from {offset} to {tidied} (same colour)"
                        : $"Clear combo colour skip of {objectName(hitObject)} (doesn't start a combo)",
                    () => combo.ComboOffset = tidied);
            }
        }

        private static bool tryResnap(ControlPointInfo controlPoints, double time, double maxDistance, out double snapped)
        {
            snapped = controlPoints.GetClosestSnappedTime(time);
            double distance = Math.Abs(snapped - time);
            return distance >= MIN_RESNAP_DISTANCE && distance <= maxDistance;
        }

        private static string describeOffset(double from, double to)
        {
            double delta = to - from;
            return $"{(delta > 0 ? "+" : "")}{delta:0.#} ms";
        }

        private static string objectName(HitObject hitObject) => hitObject switch
        {
            IHasPath => "slider",
            IHasDuration => "spinner",
            _ => "circle",
        };
    }
}
