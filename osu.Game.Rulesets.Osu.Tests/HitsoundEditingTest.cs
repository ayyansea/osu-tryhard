// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using osu.Game.Audio;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.ControlPoints;
using osu.Game.Beatmaps.Formats;
using osu.Game.IO;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Objects.Types;
using osu.Game.Rulesets.Osu.Objects;
using osu.Game.Screens.Edit.Hitsounds;
using osuTK;

namespace osu.Game.Rulesets.Osu.Tests
{
    [TestFixture]
    public class HitsoundEditingTest
    {
        private static readonly HitsoundLane soft_normal = new HitsoundLane(HitSampleInfo.BANK_SOFT, 1, HitSampleInfo.HIT_NORMAL);
        private static readonly HitsoundLane drum_clap = new HitsoundLane(HitSampleInfo.BANK_DRUM, 1, HitSampleInfo.HIT_CLAP);
        private static readonly HitsoundLane drum_clap_custom = new HitsoundLane(HitSampleInfo.BANK_DRUM, 3, HitSampleInfo.HIT_CLAP);

        [Test]
        public void TestLaneToggle()
        {
            var circle = new HitCircle { StartTime = 1000 };
            var moment = HitsoundEditing.GetMoments(circle).Single();

            Assert.That(HitsoundEditing.SetLane(moment, soft_normal, true), Is.True);
            Assert.That(HitsoundEditing.SetLane(moment, soft_normal, true), Is.False);

            
            // another normal replaces the soft one, whatever the bank.
            var normal_normal = new HitsoundLane(HitSampleInfo.BANK_NORMAL, 1, HitSampleInfo.HIT_NORMAL);
            Assert.That(HitsoundEditing.SetLane(moment, normal_normal, true), Is.True);
            Assert.That(circle.Samples.Select(s => s.Bank + s.Name), Is.EqualTo(new[] { "normalhitnormal" }));

            // additions can use another bank than the normal sample.
            var soft_whistle = new HitsoundLane(HitSampleInfo.BANK_SOFT, 1, HitSampleInfo.HIT_WHISTLE);
            Assert.That(HitsoundEditing.SetLane(moment, soft_whistle, true), Is.True);
            Assert.That(HitsoundEditing.SetLane(moment, drum_clap, true), Is.True);

            // ... but all additions share a bank, so the last one placed wins.
            Assert.That(circle.Samples.Select(s => s.Bank + s.Name), Is.EquivalentTo(new[] { "normalhitnormal", "drumhitclap" }));

            // and the same addition in another bank is replaced too.
            var drum_whistle = new HitsoundLane(HitSampleInfo.BANK_DRUM, 1, HitSampleInfo.HIT_WHISTLE);
            Assert.That(HitsoundEditing.SetLane(moment, drum_whistle, true), Is.True);
            Assert.That(circle.Samples.Select(s => s.Bank + s.Name), Is.EquivalentTo(new[] { "normalhitnormal", "drumhitclap", "drumhitwhistle" }));

            // there is one sample set per object, so a custom set replaces everything else.
            Assert.That(HitsoundEditing.SetLane(moment, drum_clap_custom, true), Is.True);
            Assert.That(circle.Samples.Select(s => s.Bank + s.Name + s.Suffix), Is.EqualTo(new[] { "drumhitclap3" }));
            Assert.That(HitsoundEditing.IsRepresentable(circle.Samples), Is.True);

            Assert.That(HitsoundEditing.SetLane(moment, drum_clap_custom, false), Is.True);
            Assert.That(circle.Samples, Is.Empty);
            Assert.That(HitsoundEditing.SetLane(moment, drum_clap_custom, false), Is.False);
        }

        [Test]
        public void TestSliderNodesAreIndependent()
        {
            var slider = createSlider();
            var moments = HitsoundEditing.GetMoments(slider).ToList();

            Assert.That(moments, Has.Count.EqualTo(3));
            Assert.That(moments[2].Time, Is.EqualTo(slider.EndTime).Within(1));

            Assert.That(HitsoundEditing.SetLane(moments[1], drum_clap, true), Is.True);

            Assert.That(HitsoundEditing.Has(moments[0], drum_clap), Is.False);
            Assert.That(HitsoundEditing.Has(moments[1], drum_clap), Is.True);
            Assert.That(HitsoundEditing.Has(moments[2], drum_clap), Is.False);
        }

        [Test]
        public void TestLayoutLanesAndPaging()
        {
            var layout = new HitsoundLayout();
            layout.SetSampleSets(new[] { 1, 3 });

            // soft, normal, drum of the default set, then the same for set 3.
            Assert.That(layout.SectionCount, Is.EqualTo(6));
            Assert.That(layout.Rows, Has.Count.EqualTo(12));
            Assert.That(layout.Rows[0].Lane, Is.EqualTo(new HitsoundLane(HitSampleInfo.BANK_SOFT, 1, HitSampleInfo.HIT_NORMAL)));
            Assert.That(layout.Rows[4].Lane, Is.EqualTo(new HitsoundLane(HitSampleInfo.BANK_NORMAL, 1, HitSampleInfo.HIT_NORMAL)));

            // the gap between two sections belongs to no lane.
            Assert.That(layout.TryGetLane(HitsoundLayout.SECTION_HEIGHT - 1, out var last), Is.True);
            Assert.That(last, Is.EqualTo(new HitsoundLane(HitSampleInfo.BANK_SOFT, 1, HitSampleInfo.HIT_CLAP)));
            Assert.That(layout.TryGetLane(HitsoundLayout.SECTION_HEIGHT + 1, out _), Is.False);
            Assert.That(layout.TryGetLane(HitsoundLayout.SECTION_HEIGHT + HitsoundLayout.SEPARATOR_HEIGHT + 1, out var first), Is.True);
            Assert.That(first.Bank, Is.EqualTo(HitSampleInfo.BANK_NORMAL));

            layout.ShowSampleSet(3);
            Assert.That(layout.Rows[0].Lane, Is.EqualTo(new HitsoundLane(HitSampleInfo.BANK_SOFT, 3, HitSampleInfo.HIT_NORMAL)));

            // can't scroll past the end.
            layout.SectionOffset.Value = 100;
            Assert.That(layout.SectionOffset.Value, Is.EqualTo(3));
        }

        [Test]
        public void TestSampleSetsFollowSteps()
        {
            var hitsounds = new EditorHitsounds(null!);
            Assert.That(hitsounds.SampleSets, Is.EqualTo(new[] { 1 }));

            var step = new HitsoundStep { StartTime = 100, Samples = { drum_clap_custom.Create() } };
            hitsounds.Add(step);
            Assert.That(hitsounds.SampleSets, Is.EqualTo(new[] { 1, 3 }));

            hitsounds.RegisterSampleSets(new[] { 2 });
            Assert.That(hitsounds.SampleSets, Is.EqualTo(new[] { 1, 2, 3 }));

            // sets the UI registered stay, sets only used by steps go away with them.
            hitsounds.Remove(step);
            Assert.That(hitsounds.SampleSets, Is.EqualTo(new[] { 1, 2 }));
        }

        [Test]
        public void TestSerializerRoundTrip()
        {
            var step = new HitsoundStep { StartTime = 1234.5, Samples = { new HitSampleInfo(HitSampleInfo.HIT_NORMAL, HitSampleInfo.BANK_SOFT, "2"), new HitSampleInfo(HitSampleInfo.HIT_CLAP, HitSampleInfo.BANK_DRUM, null, 80, false) } };

            var read = HitsoundSerializer.Read(HitsoundSerializer.Write(new[] { step })).Single();

            Assert.That(read.StartTime, Is.EqualTo(1234.5));
            Assert.That(read.Samples, Is.EqualTo(step.Samples));
        }

        [Test]
        public void TestMovePasteDelete()
        {
            var hitsounds = new EditorHitsounds(null!);
            var a = new HitsoundStep { StartTime = 100, Samples = { new HitSampleInfo(HitSampleInfo.HIT_NORMAL) } };
            var b = new HitsoundStep { StartTime = 300, Samples = { new HitSampleInfo(HitSampleInfo.HIT_NORMAL, HitSampleInfo.BANK_DRUM) } };
            hitsounds.Add(a);
            hitsounds.Add(b);

            hitsounds.SelectAll();
            Assert.That(hitsounds.MoveSelected(-500), Is.EqualTo(-100)); // clamped at 0
            Assert.That(new[] { a.StartTime, b.StartTime }, Is.EqualTo(new[] { 0.0, 200.0 }));

            hitsounds.CopySelected();
            hitsounds.Paste(1000);
            Assert.That(hitsounds.Steps.Select(s => s.StartTime), Is.EqualTo(new[] { 0.0, 200.0, 1000.0, 1200.0 }));
            Assert.That(hitsounds.Selected, Has.Count.EqualTo(2));

            hitsounds.DeleteSelected();
            Assert.That(hitsounds.Steps, Has.Count.EqualTo(2));
        }

        /// <summary>
        /// Steps can hold any mix of lanes, but .osu files only have one normal bank, one addition bank and one sample set per hit object.
        /// This documents what survives saving.
        /// </summary>
        [Test]
        public void TestMixedSamplesSaveAndLoad()
        {
            var circle = new HitCircle { StartTime = 1000 };
            HitsoundEditing.SetSamples(HitsoundEditing.GetMoments(circle).Single(), new List<HitSampleInfo>
            {
                soft_normal.Create(),
                drum_clap.Create(),
                drum_clap_custom.Create(),
            });

            var slider = createSlider();
            var nodes = HitsoundEditing.GetMoments(slider).ToList();
            HitsoundEditing.SetSamples(nodes[0], new List<HitSampleInfo> { soft_normal.Create(), drum_clap.Create() });
            HitsoundEditing.SetSamples(nodes[1], new List<HitSampleInfo> { new HitsoundLane(HitSampleInfo.BANK_DRUM, 1, HitSampleInfo.HIT_NORMAL).Create() });
            HitsoundEditing.SetSamples(nodes[2], new List<HitSampleInfo> { soft_normal.Create(), drum_clap_custom.Create() });

            var beatmap = new Beatmap { BeatmapInfo = { Ruleset = new OsuRuleset().RulesetInfo } };
            beatmap.ControlPointInfo.Add(0, new TimingControlPoint());
            beatmap.HitObjects.Add(circle);
            beatmap.HitObjects.Add(slider);

            using var stream = new MemoryStream();

            using (var writer = new StreamWriter(stream, Encoding.UTF8, 1024, true))
                new LegacyBeatmapEncoder(beatmap, null, null).Encode(writer);

            stream.Position = 0;

            var decoded = new LegacyBeatmapDecoder().Decode(new LineBufferedReader(stream));

            // per hit object / slider node, the set index is shared and a mix of banks collapses.
            var decodedCircle = decoded.HitObjects[1];
            var circleSamples = HitsoundEditing.GetMoments(decodedCircle).Single().Samples;
            Assert.That(circleSamples.Select(s => s.Suffix).Distinct().Count(), Is.EqualTo(1));
            Assert.That(circleSamples, Has.Count.LessThan(3));

            var decodedNodes = HitsoundEditing.GetMoments(decoded.HitObjects[0]).ToList();
            Assert.That(decodedNodes[0].Samples.Select(s => s.Bank + s.Name), Is.EquivalentTo(new[] { "softhitnormal", "drumhitclap" }));
            Assert.That(decodedNodes[1].Samples.Select(s => s.Bank + s.Name), Is.EquivalentTo(new[] { "drumhitnormal" }));

            // and the editor knows which steps are affected.
            Assert.That(HitsoundEditing.IsRepresentable(slider.NodeSamples[0]), Is.True);
            Assert.That(HitsoundEditing.IsRepresentable(slider.NodeSamples[2]), Is.False);
            Assert.That(HitsoundEditing.IsRepresentable(circle.Samples), Is.False);
        }

        private static Slider createSlider()
        {
            var slider = new Slider
            {
                StartTime = 0,
                RepeatCount = 1,
                Path = new SliderPath(PathType.LINEAR, new[] { Vector2.Zero, new Vector2(100, 0) }),
                Samples = { new HitSampleInfo(HitSampleInfo.HIT_NORMAL) },
            };

            slider.ApplyDefaults(new ControlPointInfo(), new BeatmapDifficulty());
            return slider;
        }
    }
}
