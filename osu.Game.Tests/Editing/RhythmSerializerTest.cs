// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Linq;
using NUnit.Framework;
using osu.Game.Rulesets.Objects;
using osu.Game.Screens.Edit.Rhythm;

namespace osu.Game.Tests.Editing
{
    [TestFixture]
    public class RhythmSerializerTest
    {
        [Test]
        public void TestRoundTrip()
        {
            HitObject[] original =
            {
                new RhythmHold { StartTime = 1250.5, Duration = 375.25 },
                new RhythmHitObject { StartTime = 1000 },
            };

            var result = RhythmSerializer.Read(RhythmSerializer.Write(original));

            Assert.That(result, Has.Count.EqualTo(2));

            // output is ordered by time.
            Assert.That(result[0], Is.Not.InstanceOf<RhythmHold>());
            Assert.That(result[0].StartTime, Is.EqualTo(1000));

            var hold = (RhythmHold)result[1];
            Assert.That(hold.StartTime, Is.EqualTo(1250.5));
            Assert.That(hold.Duration, Is.EqualTo(375.25));
        }

        [Test]
        public void TestInvalidLinesIgnored()
        {
            var result = RhythmSerializer.Read("osu!tryhard rhythm v1\n\nnot a number\n500\n750,abc\n1000,0\n1500,250\n");

            Assert.That(result.Select(h => h.StartTime), Is.EqualTo(new[] { 500d, 750d, 1000d, 1500d }));
            Assert.That(result.Select(h => h is RhythmHold), Is.EqualTo(new[] { false, false, false, true }));
        }
    }
}
