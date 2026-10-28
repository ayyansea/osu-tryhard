// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using osu.Game.Screens.Edit.Hitsounds;

namespace osu.Game.Rulesets.Osu.Tests
{
    [TestFixture]
    public class HitsoundSampleProcessorTest
    {
        private const int rate = 44100;

        // a mono "hit": silence, then a decaying burst.
        private static float[] createHit(int silentFrames, int hitFrames, float amplitude)
            => new float[silentFrames].Concat(Enumerable.Range(0, hitFrames).Select(i => amplitude * (1 - (float)i / hitFrames) * MathF.Sin(i * 0.3f + 1))).ToArray();

        [Test]
        public void TestTrimsSilenceAndNormalises()
        {
            var (result, report) = HitsoundSampleProcessor.Process(createHit(441, 2000, 0.25f), 1, rate);

            Assert.That(report.TrimmedMs, Is.EqualTo(10).Within(1));
            Assert.That(Math.Abs(result[0]), Is.GreaterThanOrEqualTo(HitsoundSampleProcessor.SILENCE_THRESHOLD));
            Assert.That(result.Max(x => Math.Abs(x)), Is.EqualTo(Math.Pow(10, HitsoundSampleProcessor.TARGET_PEAK_DB / 20)).Within(0.001));
            Assert.That(report.PeakDelayed, Is.False);
        }

        [Test]
        public void TestPadsShortFilesAndFlagsDelayedPeaks()
        {
            // 10 ms long, and the loudest point comes late.
            var shortHit = Enumerable.Range(0, 441).Select(i => i / 441f * 0.5f).ToArray();
            var (result, report) = HitsoundSampleProcessor.Process(shortHit, 1, rate);

            Assert.That(report.Padded, Is.True);
            Assert.That(result.Length, Is.GreaterThanOrEqualTo(rate * 25 / 1000));
            Assert.That(report.PeakDelayed, Is.True);
        }

        [Test]
        public void TestSilentFileIsLeftAlone()
        {
            var silent = new float[1000];
            var (result, report) = HitsoundSampleProcessor.Process(silent, 1, rate);

            Assert.That(report.Silent, Is.True);
            Assert.That(result, Is.SameAs(silent));
        }

        [Test]
        public void TestStereoStaysInSync()
        {
            // only the right channel makes sound at first.
            float[] samples = new float[2 * 600];
            for (int i = 100; i < 600; i++)
                samples[i * 2 + 1] = 0.1f;

            var (result, _) = HitsoundSampleProcessor.Process(samples, 2, rate);

            Assert.That(result[0], Is.EqualTo(0)); // left
            Assert.That(result[1], Is.GreaterThan(0)); // right, first audible frame
        }

        [Test]
        public void TestWavCanBeDecodedAgain()
        {
            var (processed, _) = HitsoundSampleProcessor.Process(createHit(300, 3000, 0.3f), 1, rate);
            byte[] wav = HitsoundSampleProcessor.ToWav(processed, 1, rate);

            Assert.That(System.Text.Encoding.ASCII.GetString(wav, 0, 4), Is.EqualTo("RIFF"));
            Assert.That(wav.Length, Is.EqualTo(44 + processed.Length * 2));

            string path = Path.Combine(Path.GetTempPath(), $"hitsound-test-{Guid.NewGuid():N}.wav");

            try
            {
                File.WriteAllBytes(path, wav);

                if (!HitsoundSampleProcessor.TryDecode(new FileInfo(path), out float[] decoded, out int channels, out int decodedRate))
                    Assert.Inconclusive("BASS is not available here.");

                Assert.That(channels, Is.EqualTo(1));
                Assert.That(decodedRate, Is.EqualTo(rate));
                Assert.That(decoded.Length, Is.EqualTo(processed.Length));
                Assert.That(decoded.Max(x => Math.Abs(x)), Is.EqualTo(processed.Max(x => Math.Abs(x))).Within(0.001));
            }
            finally
            {
                File.Delete(path);
            }
        }
    }
}
