// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using osu.Framework.Allocation;
using osu.Framework.Graphics.Containers;
using osu.Framework.Logging;
using osu.Framework.Platform;
using osu.Game.Audio;
using osu.Game.Rulesets.Objects;

namespace osu.Game.Screens.Edit.Hitsounds
{
    /// <summary>
    /// A hitsound at a point in time, independent of whether a hit object exists there.
    /// Its <see cref="HitObject.Samples"/> are the samples that play (normal + additions).
    /// </summary>
    public class HitsoundStep : HitObject
    {
    }

    /// <summary>
    /// The hitsound track of the beatmap being edited: a list of <see cref="HitsoundStep"/>s which exist regardless of the hit objects.
    /// They can be applied to hit objects at matching times (steps without an object are dropped), which is the basis for copying hitsounds between difficulties.
    /// </summary>
    /// <remarks>
    /// Like the rhythm blueprint, this is stored outside of the beatmap's files, keyed by the difficulty, and has its own undo history.
    /// </remarks>
    public partial class EditorHitsounds : CompositeDrawable
    {
        public IReadOnlyList<HitsoundStep> Steps => steps;

        public event Action<HitsoundStep>? StepAdded;
        public event Action<HitsoundStep>? StepRemoved;
        public event Action<HitsoundStep>? StepUpdated;

        public EditorChangeHandler ChangeHandler { get; }

        public bool HasUnsavedChanges => lastSavedHash != null && ChangeHandler.CurrentStateHash != lastSavedHash;

        private readonly List<HitsoundStep> steps = new List<HitsoundStep>();
        private readonly EditorBeatmap editorBeatmap;

        private Storage storage = null!;
        private string? lastSavedHash;

        public EditorHitsounds(EditorBeatmap editorBeatmap)
        {
            this.editorBeatmap = editorBeatmap;
            ChangeHandler = new HitsoundChangeHandler(this);
        }

        private string filename => $"{editorBeatmap.BeatmapInfo.ID}.txt";

        [BackgroundDependencyLoader]
        private void load(Storage gameStorage)
        {
            storage = gameStorage.GetStorageForDirectory("hitsounds");

            steps.AddRange(readFromStorage());
            MarkSaved();
        }

        public void BeginChange() => ChangeHandler.BeginChange();

        public void EndChange() => ChangeHandler.EndChange();

        // ponytail: linear scans, fine for a few thousand steps.
        public HitsoundStep? FindStep(double time) => steps.FirstOrDefault(s => Math.Abs(s.StartTime - time) < 1);

        public void Add(HitsoundStep step)
        {
            int index = steps.FindIndex(s => s.StartTime > step.StartTime);
            steps.Insert(index < 0 ? steps.Count : index, step);

            StepAdded?.Invoke(step);
            ChangeHandler.SaveState();
        }

        public void Remove(HitsoundStep step)
        {
            if (!steps.Remove(step))
                return;

            StepRemoved?.Invoke(step);
            ChangeHandler.SaveState();
        }

        /// <summary>
        /// Notifies that the samples of a step were changed.
        /// </summary>
        public void Update(HitsoundStep step)
        {
            StepUpdated?.Invoke(step);
            ChangeHandler.SaveState();
        }

        public void Replace(IEnumerable<HitsoundStep> newSteps)
        {
            foreach (var step in steps.ToArray())
                StepRemoved?.Invoke(step);

            steps.Clear();
            steps.AddRange(newSteps.OrderBy(s => s.StartTime));

            foreach (var step in steps.ToArray())
                StepAdded?.Invoke(step);

            ChangeHandler.SaveState();
        }

        /// <summary>
        /// Replaces the steps with the samples currently on the beatmap's hit objects (and slider nodes).
        /// </summary>
        public void ImportFromHitObjects()
        {
            var byTime = new Dictionary<long, HitsoundStep>();

            foreach (var hitObject in editorBeatmap.HitObjects)
            {
                foreach (var moment in HitsoundEditing.GetMoments(hitObject))
                {
                    if (moment.Samples.Count == 0)
                        continue;

                    byTime[(long)Math.Round(moment.Time)] = new HitsoundStep
                    {
                        StartTime = moment.Time,
                        Samples = moment.Samples.Select(s => s.With()).ToList(),
                    };
                }
            }

            Replace(byTime.Values);
        }

        /// <summary>
        /// Assigns the samples of the steps to the beatmap's hit objects (and slider nodes) at matching times.
        /// Steps without an object are dropped, objects without a step are left untouched.
        /// </summary>
        public void ApplyToHitObjects()
        {
            var byTime = new Dictionary<long, HitsoundStep>();

            foreach (var step in steps)
                byTime[(long)Math.Round(step.StartTime)] = step;

            HitsoundStep? find(double time)
            {
                long key = (long)Math.Round(time);
                return byTime.GetValueOrDefault(key) ?? byTime.GetValueOrDefault(key - 1) ?? byTime.GetValueOrDefault(key + 1);
            }

            editorBeatmap.BeginChange();

            foreach (var hitObject in editorBeatmap.HitObjects.ToArray())
            {
                bool changed = false;

                foreach (var moment in HitsoundEditing.GetMoments(hitObject))
                {
                    var step = find(moment.Time);

                    if (step == null)
                        continue;

                    HitsoundEditing.SetSamples(moment, step.Samples.Select(s => s.With()).ToList());
                    changed = true;
                }

                if (changed)
                    editorBeatmap.Update(hitObject);
            }

            editorBeatmap.EndChange();
        }

        /// <summary>
        /// Writes the hitsound track to storage.
        /// </summary>
        public void Save()
        {
            try
            {
                if (steps.Count == 0)
                    storage.Delete(filename);
                else
                {
                    using (var stream = storage.CreateFileSafely(filename))
                    using (var writer = new StreamWriter(stream))
                        writer.Write(HitsoundSerializer.Write(steps));
                }

                MarkSaved();
            }
            catch (Exception e)
            {
                Logger.Error(e, "Failed to save hitsound track");
            }
        }

        public void MarkSaved() => lastSavedHash = ChangeHandler.CurrentStateHash;

        private IEnumerable<HitsoundStep> readFromStorage()
        {
            try
            {
                if (!storage.Exists(filename))
                    return Array.Empty<HitsoundStep>();

                using (var stream = storage.GetStream(filename))
                using (var reader = new StreamReader(stream))
                    return HitsoundSerializer.Read(reader.ReadToEnd());
            }
            catch (Exception e)
            {
                Logger.Error(e, "Failed to load hitsound track");
                return Array.Empty<HitsoundStep>();
            }
        }

        private partial class HitsoundChangeHandler : EditorChangeHandler
        {
            private readonly EditorHitsounds hitsounds;

            public HitsoundChangeHandler(EditorHitsounds hitsounds)
            {
                this.hitsounds = hitsounds;
            }

            protected override void WriteCurrentStateToStream(MemoryStream stream)
            {
                using (var writer = new StreamWriter(stream, Encoding.UTF8, 1024, true))
                    writer.Write(HitsoundSerializer.Write(hitsounds.steps));
            }

            protected override void ApplyStateChange(byte[] previousState, byte[] newState) =>
                hitsounds.Replace(HitsoundSerializer.Read(Encoding.UTF8.GetString(newState)));
        }
    }

    /// <summary>
    /// Reads and writes hitsound tracks in a simple line-based text format.
    /// Each line is "time|name,bank,suffix,volume,autobank|..." with one entry per sample.
    /// </summary>
    public static class HitsoundSerializer
    {
        private const string header = @"osu!tryhard hitsounds v1";

        public static string Write(IEnumerable<HitsoundStep> steps)
        {
            var sb = new StringBuilder();
            sb.Append(header).Append('\n');

            foreach (var step in steps.OrderBy(s => s.StartTime))
            {
                sb.Append(step.StartTime.ToString(CultureInfo.InvariantCulture));

                foreach (var s in step.Samples)
                    sb.Append('|').Append(string.Join(',', s.Name, s.Bank, s.Suffix, s.Volume, s.EditorAutoBank ? 1 : 0));

                sb.Append('\n');
            }

            return sb.ToString();
        }

        public static List<HitsoundStep> Read(string text)
        {
            var steps = new List<HitsoundStep>();

            foreach (string line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                string[] parts = line.Split('|');

                if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double time))
                    continue;

                var step = new HitsoundStep { StartTime = time };

                foreach (string part in parts.Skip(1))
                {
                    string[] f = part.Split(',');

                    if (f.Length == 5 && int.TryParse(f[3], out int volume))
                        step.Samples.Add(new HitSampleInfo(f[0], f[1], f[2].Length == 0 ? null : f[2], volume, f[4] == "1"));
                }

                steps.Add(step);
            }

            return steps;
        }
    }
}
