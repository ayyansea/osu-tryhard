// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using System.Linq;
using osu.Game.Audio;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Objects.Types;

namespace osu.Game.Screens.Edit.Hitsounds
{
    /// <summary>
    /// A moment at which a hit object plays samples: the object itself, or one node (head / repeat / tail) of a slider.
    /// </summary>
    public readonly record struct HitMoment(HitObject Object, int NodeIndex, double Time)
    {
        public IList<HitSampleInfo> Samples => Object is IHasRepeats repeats && NodeIndex < repeats.NodeSamples.Count ? repeats.NodeSamples[NodeIndex] : Object.Samples;
    }

    /// <summary>
    /// A swimlane of the hitsound grid: one sample (normal / whistle / finish / clap) of one bank in one sample set.
    /// </summary>
    /// <param name="Bank">The bank (soft / normal / drum).</param>
    /// <param name="SampleSet">The sample set index. 1 is the default set, 2 and above are custom sets.</param>
    /// <param name="Name">The sample name (<see cref="HitSampleInfo.HIT_NORMAL"/> and friends).</param>
    public readonly record struct HitsoundLane(string Bank, int SampleSet, string Name)
    {
        public static string? SuffixOf(int sampleSet) => sampleSet >= 2 ? sampleSet.ToString() : null;

        public static int SampleSetOf(string? suffix) => int.TryParse(suffix, out int set) && set >= 2 ? set : 1;

        public bool Matches(HitSampleInfo sample) => sample.Name == Name && sample.Bank == Bank && SampleSetOf(sample.Suffix) == SampleSet;

        /// <summary>
        /// Whether the sample can't be saved to a .osu file together with this lane: there is one sample of each name, one addition bank and one sample set per hit object.
        /// </summary>
        public bool ConflictsWith(HitSampleInfo sample) => !Matches(sample)
                                                          && (sample.Name == Name
                                                              || SampleSetOf(sample.Suffix) != SampleSet
                                                              || (Name != HitSampleInfo.HIT_NORMAL && sample.Name != HitSampleInfo.HIT_NORMAL && sample.Bank != Bank));

        // sets are looked up in the beatmap's own files first, which is what makes custom sample files play.
        public HitSampleInfo Create(int volume = HitsoundEditing.MAX_VOLUME) => new HitSampleInfo(Name, Bank, SuffixOf(SampleSet), volume, editorAutoBank: false, useBeatmapSamples: true);

        public string Label => $"{Bank} {Name.Replace("hit", string.Empty)}{(SampleSet >= 2 ? $" #{SampleSet}" : string.Empty)}";
    }

    /// <summary>
    /// Helpers for reading and mutating the samples of a <see cref="HitMoment"/>.
    /// Mutations do not notify anything; callers are expected to do so.
    /// </summary>
    public static class HitsoundEditing
    {
        public const int MIN_VOLUME = 1;
        public const int MAX_VOLUME = 100;

        public static IEnumerable<HitMoment> GetMoments(HitObject hitObject)
        {
            if (hitObject is IHasRepeats repeats)
            {
                double spanDuration = repeats.Duration / repeats.SpanCount();

                for (int i = 0; i < repeats.RepeatCount + 2; i++)
                    yield return new HitMoment(hitObject, i, hitObject.StartTime + i * spanDuration);
            }
            else
                yield return new HitMoment(hitObject, 0, hitObject.GetEndTime());
        }

        public static bool Has(HitMoment moment, HitsoundLane lane) => moment.Samples.Any(lane.Matches);

        /// <summary>
        /// Whether the samples survive saving as a .osu file, which only has one normal bank, one addition bank, one sample set and one volume per hit object (or slider node).
        /// </summary>
        public static bool IsRepresentable(IEnumerable<HitSampleInfo> samples)
        {
            var list = samples.ToList();
            var additions = list.Where(s => s.Name != HitSampleInfo.HIT_NORMAL).ToList();

            return list.Count(s => s.Name == HitSampleInfo.HIT_NORMAL) <= 1
                   && additions.Select(s => s.Name).Distinct().Count() == additions.Count
                   && additions.Select(s => s.Bank).Distinct().Count() <= 1
                   && list.Select(s => HitsoundLane.SampleSetOf(s.Suffix)).Distinct().Count() <= 1
                   && list.Select(s => s.Volume).Distinct().Count() <= 1;
        }

        public static void SetSamples(HitMoment moment, IList<HitSampleInfo> samples)
        {
            if (moment.Object is IHasRepeats repeats)
            {
                while (repeats.NodeSamples.Count <= moment.NodeIndex)
                    repeats.NodeSamples.Add(moment.Object.Samples.Select(s => s.With()).ToList());

                repeats.NodeSamples[moment.NodeIndex] = samples;
            }
            else
                moment.Object.Samples = samples;
        }

        /// <summary>
        /// Adds or removes the sample of a lane. Adding replaces any samples which can't be saved together with it (see <see cref="HitsoundLane.ConflictsWith"/>), so that a step always stays stable compatible.
        /// </summary>
        /// <returns>Whether anything changed.</returns>
        public static bool SetLane(HitMoment moment, HitsoundLane lane, bool on, int volume = MAX_VOLUME)
        {
            var samples = moment.Samples.ToList();

            if (on)
            {
                if (samples.Any(lane.Matches))
                    return false;

                samples.RemoveAll(lane.ConflictsWith);
                samples.Add(lane.Create(volume));
            }
            else if (samples.RemoveAll(lane.Matches) == 0)
                return false;

            SetSamples(moment, samples);
            return true;
        }
    }
}
