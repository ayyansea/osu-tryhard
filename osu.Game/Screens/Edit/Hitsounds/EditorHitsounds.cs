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

        /// <summary>
        /// The serialised steps last copied, if any.
        /// </summary>
        public string? Clipboard { get; private set; }

        public event Action<HitsoundStep>? StepAdded;
        public event Action<HitsoundStep>? StepRemoved;
        public event Action<HitsoundStep>? StepUpdated;

        /// <summary>
        /// The sample sets to show lanes for: the default set, sets registered by the UI (custom sets with files), and sets used by any step.
        /// </summary>
        public IReadOnlyList<int> SampleSets { get; private set; } = new[] { 1 };

        public event Action? SampleSetsChanged;

        public EditorChangeHandler ChangeHandler { get; }

        public bool HasUnsavedChanges => lastSavedHash != null && ChangeHandler.CurrentStateHash != lastSavedHash;

        private readonly List<HitsoundStep> steps = new List<HitsoundStep>();
        private readonly HashSet<int> registeredSampleSets = new HashSet<int> { 1 };
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
            refreshSampleSets();
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

            if (step.Samples.Any(s => !SampleSets.Contains(HitsoundLane.SampleSetOf(s.Suffix))))
                refreshSampleSets();

            StepAdded?.Invoke(step);
            ChangeHandler.SaveState();
        }

        public void Remove(HitsoundStep step)
        {
            if (!steps.Remove(step))
                return;

            if (step.Samples.Aggregate(false, (any, s) => selected.Remove(s) | any))
                SelectionChanged?.Invoke();

            refreshSampleSets();
            StepRemoved?.Invoke(step);
            ChangeHandler.SaveState();
        }

        /// <summary>
        /// Makes sure lanes are shown for the given sample sets (e.g. custom sets which have sample files), even if no step uses them.
        /// </summary>
        public void RegisterSampleSets(IEnumerable<int> sampleSets)
        {
            int before = registeredSampleSets.Count;
            registeredSampleSets.UnionWith(sampleSets);

            if (registeredSampleSets.Count != before)
                refreshSampleSets();
        }

        private void refreshSampleSets()
        {
            var all = registeredSampleSets.Union(steps.SelectMany(s => s.Samples).Select(s => HitsoundLane.SampleSetOf(s.Suffix))).OrderBy(i => i).ToList();

            if (all.SequenceEqual(SampleSets))
                return;

            SampleSets = all;
            SampleSetsChanged?.Invoke();
        }

        #region Selection

        // The selection consists of individual sounds (a sample of a step), compared by reference since equal samples in different steps are different sounds.
        private readonly HashSet<HitSampleInfo> selected = new HashSet<HitSampleInfo>(ReferenceEqualityComparer.Instance);

        public IReadOnlyCollection<HitSampleInfo> Selected => selected;

        public event Action? SelectionChanged;

        public bool IsSelected(HitSampleInfo sample) => selected.Contains(sample);

        public void SetSelection(IEnumerable<HitSampleInfo> samples)
        {
            var newSelection = samples.ToList();

            if (newSelection.Count == selected.Count && newSelection.All(selected.Contains))
                return;

            selected.Clear();
            selected.UnionWith(newSelection);
            SelectionChanged?.Invoke();
        }

        public void SelectAll() => SetSelection(steps.SelectMany(s => s.Samples));

        /// <summary>
        /// Selects every sound in a lane.
        /// </summary>
        public void SelectLane(HitsoundLane lane, bool additive)
        {
            var inLane = steps.SelectMany(s => s.Samples).Where(lane.Matches);
            SetSelection(additive ? selected.Concat(inLane) : inLane);
        }

        /// <summary>
        /// Maps each sound to the step it is in, forgetting selected sounds which are gone.
        /// </summary>
        private Dictionary<HitSampleInfo, HitsoundStep> ownersOf()
        {
            var owners = new Dictionary<HitSampleInfo, HitsoundStep>(ReferenceEqualityComparer.Instance);

            foreach (var step in steps)
            {
                foreach (var sample in step.Samples)
                    owners[sample] = step;
            }

            if (selected.RemoveWhere(s => !owners.ContainsKey(s)) > 0)
                SelectionChanged?.Invoke();

            return owners;
        }

        private static void removeSample(HitsoundStep step, HitSampleInfo sample)
        {
            for (int i = 0; i < step.Samples.Count; i++)
            {
                if (ReferenceEquals(step.Samples[i], sample))
                {
                    step.Samples.RemoveAt(i);
                    return;
                }
            }
        }

        private static HitsoundLane laneOf(HitSampleInfo sample) => new HitsoundLane(sample.Bank, HitsoundLane.SampleSetOf(sample.Suffix), sample.Name);

        /// <summary>
        /// Notifies about steps which were changed, removing the ones which have no sounds left.
        /// </summary>
        private void finishEdit(IEnumerable<HitsoundStep> touched)
        {
            foreach (var step in touched)
            {
                if (step.Samples.Count == 0)
                    Remove(step);
                else
                    Update(step);
            }
        }

        public void DeleteSelected()
        {
            var owners = ownersOf();

            BeginChange();

            var touched = new HashSet<HitsoundStep>();

            foreach (var sample in selected.ToArray())
            {
                removeSample(owners[sample], sample);
                touched.Add(owners[sample]);
            }

            SetSelection(Enumerable.Empty<HitSampleInfo>());
            finishEdit(touched);

            EndChange();
        }

        /// <summary>
        /// Moves the selected sounds in time, never before 0. Sounds which land on a step with a sound they conflict with don't replace it until <see cref="ResolveConflicts"/>,
        /// so moving back and forth is not destructive.
        /// </summary>
        /// <returns>The delta that was actually applied.</returns>
        public double MoveSelected(double delta)
        {
            var owners = ownersOf();

            if (selected.Count == 0)
                return 0;

            delta = Math.Max(delta, -selected.Min(s => owners[s].StartTime));

            if (delta == 0)
                return 0;

            var touched = new HashSet<HitsoundStep>();

            foreach (var sample in selected.ToArray())
            {
                var from = owners[sample];
                double time = from.StartTime + delta;

                var to = FindStep(time);

                if (to == null)
                {
                    to = new HitsoundStep { StartTime = time };
                    Add(to);
                }

                removeSample(from, sample);
                to.Samples.Add(sample);

                touched.Add(from);
                touched.Add(to);
            }

            finishEdit(touched);
            return delta;
        }

        /// <summary>
        /// Removes unselected sounds which the selected sounds can't be saved together with (or duplicate).
        /// </summary>
        public void ResolveConflicts()
        {
            var owners = ownersOf();
            var touched = new HashSet<HitsoundStep>();

            foreach (var sample in selected)
            {
                var step = owners[sample];
                var lane = laneOf(sample);

                foreach (var other in step.Samples.Where(s => !selected.Contains(s) && (lane.Matches(s) || lane.ConflictsWith(s))).ToArray())
                {
                    removeSample(step, other);
                    touched.Add(step);
                }
            }

            finishEdit(touched);
        }

        public void CopySelected()
        {
            var owners = ownersOf();

            Clipboard = selected.Count == 0
                ? null
                : HitsoundSerializer.Write(selected.GroupBy(s => owners[s]).Select(g => new HitsoundStep { StartTime = g.Key.StartTime, Samples = g.Select(s => s.With()).ToList() }));
        }

        /// <summary>
        /// Pastes the clipboard with its first step at <paramref name="time"/>, adding to the steps there (and replacing conflicting sounds).
        /// </summary>
        public void Paste(double time)
        {
            if (Clipboard == null)
                return;

            var pasted = HitsoundSerializer.Read(Clipboard);

            if (pasted.Count == 0)
                return;

            double offset = time - pasted.Min(s => s.StartTime);
            var added = new List<HitSampleInfo>();

            BeginChange();

            foreach (var source in pasted)
            {
                double at = source.StartTime + offset;
                var target = FindStep(at);

                if (target == null)
                {
                    target = new HitsoundStep { StartTime = at };
                    Add(target);
                }

                foreach (var sample in source.Samples)
                {
                    var lane = laneOf(sample);

                    foreach (var other in target.Samples.Where(s => !added.Contains(s) && (lane.Matches(s) || lane.ConflictsWith(s))).ToArray())
                        removeSample(target, other);

                    target.Samples.Add(sample);
                    added.Add(sample);
                }

                Update(target);
            }

            SetSelection(added);

            EndChange();
        }

        #endregion

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
            SetSelection(Enumerable.Empty<HitSampleInfo>());

            foreach (var step in steps.ToArray())
                StepRemoved?.Invoke(step);

            steps.Clear();
            steps.AddRange(newSteps.OrderBy(s => s.StartTime));
            refreshSampleSets();

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
        /// <returns>The number of hit objects / slider nodes which got samples that can't be saved exactly (see <see cref="HitsoundEditing.IsRepresentable"/>).</returns>
        public int ApplyToHitObjects()
        {
            int unrepresentable = 0;

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

                    if (!HitsoundEditing.IsRepresentable(step.Samples))
                        unrepresentable++;
                }

                if (changed)
                    editorBeatmap.Update(hitObject);
            }

            editorBeatmap.EndChange();

            return unrepresentable;
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
    /// Each line is "time|name,bank,suffix,volume,autobank,usebeatmapsamples|..." with one entry per sample.
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
                    sb.Append('|').Append(string.Join(',', s.Name, s.Bank, s.Suffix, s.Volume, s.EditorAutoBank ? 1 : 0, s.UseBeatmapSamples ? 1 : 0));

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

                    if (f.Length >= 5 && int.TryParse(f[3], out int volume))
                        // older files don't have the beatmap samples flag, and always meant to use them.
                        step.Samples.Add(new HitSampleInfo(f[0], f[1], f[2].Length == 0 ? null : f[2], volume, f[4] == "1", f.Length < 6 || f[5] == "1"));
                }

                steps.Add(step);
            }

            return steps;
        }
    }
}
