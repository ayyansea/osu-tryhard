// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Game.Screens.Edit.ComboColours;

namespace osu.Game.Tests.Editing
{
    [TestFixture]
    public class ComboColourEditingTest
    {
        private static ComboStart[] combos(params double[] times) => times.Select(t => new ComboStart(t, 0)).ToArray();

        private static int[] coloursAfterApply(ComboStart[] starts, IReadOnlyList<ComboColourSection> sections, int colourCount)
            => ComboColourEditing.ColourIndices(ComboColourEditing.ComputeOffsets(starts, sections, colourCount), colourCount);

        [Test]
        public void TestNoSectionsKeepsOffsets()
        {
            var starts = new[] { new ComboStart(0, 0), new ComboStart(100, 2), new ComboStart(200, 1) };

            Assert.That(ComboColourEditing.ComputeOffsets(starts, new List<ComboColourSection>(), 4), Is.EqualTo(new[] { 0, 2, 1 }));
        }

        [Test]
        public void TestNaturalCycle()
        {
            Assert.That(ComboColourEditing.ColourIndices(new[] { 0, 0, 0, 0 }, 3), Is.EqualTo(new[] { 0, 1, 2, 0 }));
        }

        [Test]
        public void TestCycleSection()
        {
            var sections = new List<ComboColourSection> { new ComboColourSection { Time = 0, Colours = { 2, 0 } } };

            var starts = combos(0, 100, 200, 300);
            int[] offsets = ComboColourEditing.ComputeOffsets(starts, sections, 3);

            Assert.That(ComboColourEditing.ColourIndices(offsets, 3), Is.EqualTo(new[] { 2, 0, 2, 0 }));
            Assert.That(offsets, Has.All.InRange(0, ComboColourEditing.MAX_OFFSET));
        }

        [Test]
        public void TestRepeatedColour()
        {
            var sections = new List<ComboColourSection> { new ComboColourSection { Time = 0, Colours = { 1 } } };

            Assert.That(coloursAfterApply(combos(0, 100, 200), sections, 4), Is.EqualTo(new[] { 1, 1, 1 }));
        }

        [Test]
        public void TestCombosBeforeFirstSectionUntouched()
        {
            var starts = new[] { new ComboStart(0, 0), new ComboStart(100, 1), new ComboStart(200, 0), new ComboStart(300, 0) };
            var sections = new List<ComboColourSection> { new ComboColourSection { Time = 200, Colours = { 3 } } };

            int[] offsets = ComboColourEditing.ComputeOffsets(starts, sections, 4);

            Assert.That(offsets.Take(2), Is.EqualTo(new[] { 0, 1 }));
            Assert.That(ComboColourEditing.ColourIndices(offsets, 4).Skip(2), Is.EqualTo(new[] { 3, 3 }));
        }

        [Test]
        public void TestNewSectionRestartsCycle()
        {
            var sections = new List<ComboColourSection>
            {
                new ComboColourSection { Time = 0, Colours = { 0, 1, 2 } },
                new ComboColourSection { Time = 200, Colours = { 2, 1 } },
            };

            Assert.That(coloursAfterApply(combos(0, 100, 200, 300, 400), sections, 3), Is.EqualTo(new[] { 0, 1, 2, 1, 2 }));
        }

        [Test]
        public void TestBurstRecoloursOneCombo()
        {
            var sections = new List<ComboColourSection>
            {
                new ComboColourSection { Time = 0, Colours = { 0, 1 } },
                // between combos: applies to the next one.
                new ComboColourSection { Time = 150, Burst = true, Colours = { 3 } },
            };

            // the cycle carries on after the burst as if it wasn't there.
            Assert.That(coloursAfterApply(combos(0, 100, 200, 300, 400), sections, 4), Is.EqualTo(new[] { 0, 1, 3, 0, 1 }));
        }

        [Test]
        public void TestSectionTimeLeniency()
        {
            var sections = new List<ComboColourSection> { new ComboColourSection { Time = 100.6, Colours = { 2 } } };

            // a combo stored at a rounded time just before the section still gets it.
            Assert.That(coloursAfterApply(combos(0, 100, 200), sections, 3), Is.EqualTo(new[] { 0, 2, 2 }));
        }

        [Test]
        public void TestInvalidColourIgnored()
        {
            var starts = new[] { new ComboStart(0, 1), new ComboStart(100, 1) };
            var sections = new List<ComboColourSection> { new ComboColourSection { Time = 0, Colours = { 5 } } };

            Assert.That(ComboColourEditing.ComputeOffsets(starts, sections, 3), Is.EqualTo(new[] { 1, 1 }));
        }

        [Test]
        public void TestSerializerRoundTrip()
        {
            var sections = new List<ComboColourSection>
            {
                new ComboColourSection { Time = 2500.5, Burst = true, Colours = { 3 } },
                new ComboColourSection { Time = 1000, Colours = { 0, 2, 1 } },
                new ComboColourSection { Time = 4000 },
            };

            var result = ComboColourSerializer.Read(ComboColourSerializer.Write(sections));

            Assert.That(result.Select(s => s.Time), Is.EqualTo(new[] { 1000, 2500.5, 4000 }));
            Assert.That(result[0].Colours, Is.EqualTo(new[] { 0, 2, 1 }));
            Assert.That(result[1].Burst, Is.True);
            Assert.That(result[1].Colours, Is.EqualTo(new[] { 3 }));
            Assert.That(result[2].Colours, Is.Empty);
        }

        [Test]
        public void TestSerializerIgnoresInvalidLines()
        {
            var result = ComboColourSerializer.Read("osu!tryhard combo colours v1\n\nabc|0|1\n500|0\n1000|1|2,x,-1,3\n");

            Assert.That(result, Has.Count.EqualTo(1));
            Assert.That(result[0].Colours, Is.EqualTo(new[] { 2, 3 }));
        }

        [Test]
        public void TestHsvRoundTrip()
        {
            foreach (var colour in new[] { Colour4.Red, Colour4.Lime, Colour4.Blue, new Colour4(0.2f, 0.6f, 0.9f, 1), Colour4.Gray })
            {
                var (h, s, v) = ComboColourEditing.ToHsv(colour);
                var back = ComboColourEditing.FromHsv(h, s, v);

                Assert.That(back.R, Is.EqualTo(colour.R).Within(0.001f));
                Assert.That(back.G, Is.EqualTo(colour.G).Within(0.001f));
                Assert.That(back.B, Is.EqualTo(colour.B).Within(0.001f));
            }
        }

        [Test]
        public void TestGradientEndsOnInputs()
        {
            var from = new Colour4(1, 0.2f, 0.2f, 1);
            var to = new Colour4(0.2f, 0.2f, 1, 1);

            var gradient = ComboColourEditing.Gradient(from, to, 5);

            Assert.That(gradient, Has.Count.EqualTo(5));
            Assert.That(gradient[0].R, Is.EqualTo(from.R).Within(0.001f));
            Assert.That(gradient[4].B, Is.EqualTo(to.B).Within(0.001f));
            // red -> blue the short way goes through magenta, never green.
            Assert.That(gradient.All(c => c.G <= 0.21f), Is.True);
        }

        [Test]
        public void TestHueSpread()
        {
            var spread = ComboColourEditing.HueSpread(Colour4.Red, 3);

            Assert.That(spread.Select(c => ComboColourEditing.ToHsv(c).h), Is.EqualTo(new[] { 0f, 120f, 240f }).Within(0.01f));
        }
    }
}
