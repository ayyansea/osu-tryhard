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
    /// Helpers for reading and mutating the samples of a <see cref="HitMoment"/>.
    /// Mutations do not notify the editor beatmap; callers are expected to wrap them in a change and call <see cref="EditorBeatmap.Update"/>.
    /// </summary>
    public static class HitsoundEditing
    {
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

        public static bool Has(HitMoment moment, string sampleName) => moment.Samples.Any(s => s.Name == sampleName);

        public static string? GetBank(HitMoment moment) => (moment.Samples.FirstOrDefault(s => s.Name == HitSampleInfo.HIT_NORMAL) ?? moment.Samples.FirstOrDefault())?.Bank;

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
        /// Adds or removes a sample addition (whistle / finish / clap).
        /// </summary>
        /// <param name="moment">The moment to change.</param>
        /// <param name="sampleName">The addition to add or remove.</param>
        /// <param name="on">Whether the addition should be present.</param>
        /// <param name="bank">The bank for a newly added addition if the moment has no additions yet. If null, the moment's normal bank is followed.</param>
        /// <param name="suffix">The sample set suffix used together with <paramref name="bank"/>.</param>
        /// <returns>Whether anything changed.</returns>
        public static bool SetAddition(HitMoment moment, string sampleName, bool on, string? bank = null, string? suffix = null)
        {
            var samples = moment.Samples.ToList();

            if (!on)
            {
                if (samples.RemoveAll(s => s.Name == sampleName) == 0)
                    return false;

                SetSamples(moment, samples);
                return true;
            }

            if (samples.Any(s => s.Name == sampleName))
                return false;

            var existingAddition = samples.FirstOrDefault(s => s.Name != HitSampleInfo.HIT_NORMAL);
            var normal = samples.FirstOrDefault(s => s.Name == HitSampleInfo.HIT_NORMAL);

            HitSampleInfo created;

            // as per stable, all additions at a moment share the same bank.
            if (existingAddition != null)
                created = existingAddition.With(newName: sampleName);
            else if (normal != null)
                created = normal.With(newName: sampleName, newEditorAutoBank: true);
            else
                created = new HitSampleInfo(sampleName);

            if (existingAddition == null && bank != null)
                created = created.With(newBank: bank, newSuffix: suffix, newEditorAutoBank: false);

            samples.Add(created);
            SetSamples(moment, samples);
            return true;
        }

        /// <summary>
        /// Sets the bank of the normal sample (and of any additions following it automatically).
        /// </summary>
        /// <returns>Whether anything changed.</returns>
        public static bool SetBank(HitMoment moment, string bank, string? suffix = null)
        {
            var samples = moment.Samples.ToList();

            if (!samples.Any(s => s.Name == HitSampleInfo.HIT_NORMAL))
                samples.Insert(0, new HitSampleInfo(HitSampleInfo.HIT_NORMAL, bank, suffix));

            var updated = samples.Select(s => s.Name == HitSampleInfo.HIT_NORMAL || s.EditorAutoBank ? s.With(newBank: bank, newSuffix: suffix) : s).ToList();

            if (updated.SequenceEqual(moment.Samples))
                return false;

            SetSamples(moment, updated);
            return true;
        }

        /// <summary>
        /// Returns the bank after <paramref name="bank"/> when cycling normal -> soft -> drum.
        /// </summary>
        public static string NextBank(string? bank)
        {
            int index = bank == null ? -1 : System.Array.IndexOf(HitSampleInfo.ALL_BANKS, bank);
            return HitSampleInfo.ALL_BANKS[(index + 1) % HitSampleInfo.ALL_BANKS.Length];
        }
    }
}
