// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using osu.Game.Audio;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.ControlPoints;
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
        [Test]
        public void TestCircleAdditionToggle()
        {
            var circle = new HitCircle { StartTime = 1000, Samples = { new HitSampleInfo(HitSampleInfo.HIT_NORMAL, HitSampleInfo.BANK_SOFT) } };
            var moment = HitsoundEditing.GetMoments(circle).Single();

            Assert.That(HitsoundEditing.SetAddition(moment, HitSampleInfo.HIT_CLAP, true), Is.True);
            Assert.That(HitsoundEditing.Has(moment, HitSampleInfo.HIT_CLAP), Is.True);
            Assert.That(circle.Samples.Single(s => s.Name == HitSampleInfo.HIT_CLAP).Bank, Is.EqualTo(HitSampleInfo.BANK_SOFT));

            Assert.That(HitsoundEditing.SetAddition(moment, HitSampleInfo.HIT_CLAP, true), Is.False);

            Assert.That(HitsoundEditing.SetAddition(moment, HitSampleInfo.HIT_CLAP, false), Is.True);
            Assert.That(HitsoundEditing.Has(moment, HitSampleInfo.HIT_CLAP), Is.False);
        }

        [Test]
        public void TestSliderNodeAdditionInheritsNodeBank()
        {
            var slider = new Slider
            {
                StartTime = 0,
                RepeatCount = 1,
                Path = new SliderPath(PathType.LINEAR, new[] { Vector2.Zero, new Vector2(100, 0) }),
                Samples = { new HitSampleInfo(HitSampleInfo.HIT_NORMAL) },
            };

            slider.ApplyDefaults(new ControlPointInfo(), new BeatmapDifficulty());

            var moments = HitsoundEditing.GetMoments(slider).ToList();
            Assert.That(moments, Has.Count.EqualTo(3));
            Assert.That(moments[2].Time, Is.EqualTo(slider.EndTime).Within(1));

            // give the first node a drum whistle, and the second node nothing.
            slider.NodeSamples.Clear();
            slider.NodeSamples.Add(new List<HitSampleInfo>
            {
                new HitSampleInfo(HitSampleInfo.HIT_NORMAL, HitSampleInfo.BANK_DRUM),
                new HitSampleInfo(HitSampleInfo.HIT_WHISTLE, HitSampleInfo.BANK_DRUM, editorAutoBank: false),
            });

            // adding a clap to the first node follows the existing addition's bank.
            Assert.That(HitsoundEditing.SetAddition(moments[0], HitSampleInfo.HIT_CLAP, true), Is.True);
            Assert.That(slider.NodeSamples[0].Single(s => s.Name == HitSampleInfo.HIT_CLAP).Bank, Is.EqualTo(HitSampleInfo.BANK_DRUM));

            // the other nodes are padded and unaffected.
            Assert.That(HitsoundEditing.Has(moments[1], HitSampleInfo.HIT_CLAP), Is.False);
            Assert.That(HitsoundEditing.Has(moments[2], HitSampleInfo.HIT_CLAP), Is.False);

            // brush bank applies when the node has no addition yet.
            Assert.That(HitsoundEditing.SetAddition(moments[1], HitSampleInfo.HIT_FINISH, true, HitSampleInfo.BANK_SOFT, "2"), Is.True);
            var finish = slider.NodeSamples[1].Single(s => s.Name == HitSampleInfo.HIT_FINISH);
            Assert.That(finish.Bank, Is.EqualTo(HitSampleInfo.BANK_SOFT));
            Assert.That(finish.Suffix, Is.EqualTo("2"));
        }

        [Test]
        public void TestSetBankAndCycle()
        {
            var circle = new HitCircle { Samples = { new HitSampleInfo(HitSampleInfo.HIT_NORMAL, HitSampleInfo.BANK_NORMAL) } };
            var moment = HitsoundEditing.GetMoments(circle).Single();

            Assert.That(HitsoundEditing.NextBank(HitsoundEditing.GetBank(moment)), Is.EqualTo(HitSampleInfo.BANK_SOFT));
            Assert.That(HitsoundEditing.SetBank(moment, HitSampleInfo.BANK_DRUM), Is.True);
            Assert.That(HitsoundEditing.GetBank(moment), Is.EqualTo(HitSampleInfo.BANK_DRUM));
            Assert.That(HitsoundEditing.SetBank(moment, HitSampleInfo.BANK_DRUM), Is.False);
            Assert.That(HitsoundEditing.NextBank(HitSampleInfo.BANK_DRUM), Is.EqualTo(HitSampleInfo.BANK_NORMAL));
        }

        [Test]
        public void TestSerializerRoundTrip()
        {
            var step = new HitsoundStep { StartTime = 1234.5, Samples = { new HitSampleInfo(HitSampleInfo.HIT_NORMAL, HitSampleInfo.BANK_SOFT, "2"), new HitSampleInfo(HitSampleInfo.HIT_CLAP, HitSampleInfo.BANK_DRUM, null, 80, false) } };

            var read = HitsoundSerializer.Read(HitsoundSerializer.Write(new[] { step })).Single();

            Assert.That(read.StartTime, Is.EqualTo(1234.5));
            Assert.That(read.Samples, Is.EqualTo(step.Samples));
        }
    }
}
