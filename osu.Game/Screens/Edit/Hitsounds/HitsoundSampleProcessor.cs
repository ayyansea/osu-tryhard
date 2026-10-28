// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ManagedBass;
using osu.Framework.Audio.Callbacks;

namespace osu.Game.Screens.Edit.Hitsounds
{
    /// <summary>
    /// Prepares sound files which are added as custom hitsounds so that they satisfy the ranking criteria (and the checks in the verify screen):
    /// leading silence is removed (the peak may be delayed by 5 ms at most), the peak is normalised to -0.1 dB, files are at least 25 ms long
    /// and the result is always an uncompressed WAV file (MP3 is not allowed for hitsounds as it is inherently delayed).
    /// </summary>
    public static class HitsoundSampleProcessor
    {
        /// <summary>
        /// Amplitude below which audio is considered silent. The same as <see cref="Rulesets.Edit.Checks.CheckDelayedHitsounds"/> uses.
        /// </summary>
        public const float SILENCE_THRESHOLD = 0.001f;

        public const double TARGET_PEAK_DB = -0.1;

        public const double MIN_LENGTH_MS = 25;

        public const double MAX_PEAK_DELAY_MS = 5;

        /// <summary>
        /// The share of the peak amplitude from which on the sample counts as having reached its peak.
        /// </summary>
        private const float peak_reached_factor = 0.95f;

        public record Report(double TrimmedMs, double GainDb, double PeakDelayMs, bool Padded, bool Silent)
        {
            public bool PeakDelayed => PeakDelayMs > MAX_PEAK_DELAY_MS;

            public string Describe(string name)
            {
                if (Silent)
                    return $"{name}: the file is silent, it was converted to wav as is.";

                var parts = new List<string>();

                if (TrimmedMs >= 0.05)
                    parts.Add($"removed {TrimmedMs:0.#} ms of silence at the start");

                parts.Add($"normalised {(GainDb >= 0 ? "+" : "")}{GainDb:0.#} dB to a peak of {TARGET_PEAK_DB} dB");

                if (Padded)
                    parts.Add($"padded to {MIN_LENGTH_MS:0} ms");

                string text = $"{name}: {string.Join(", ", parts)}.";

                if (PeakDelayed)
                    text += $" Warning: the loudest point is {PeakDelayMs:0.#} ms after the start, ranked maps need a clear impact within {MAX_PEAK_DELAY_MS:0} ms.";

                return text;
            }
        }

        /// <summary>
        /// Trims, normalises and pads interleaved samples.
        /// </summary>
        public static (float[] Samples, Report Report) Process(float[] samples, int channels, int sampleRate)
        {
            int frames = samples.Length / channels;

            int first = 0;

            while (first < frames && !isAudible(samples, first * channels, channels))
                first++;

            // nothing to work with.
            if (first == frames)
                return (samples, new Report(0, 0, 0, false, true));

            int minFrames = (int)Math.Ceiling(MIN_LENGTH_MS / 1000 * sampleRate);
            int outFrames = Math.Max(frames - first, minFrames);
            var result = new float[outFrames * channels];

            Array.Copy(samples, first * channels, result, 0, (frames - first) * channels);

            float peak = result.Max(x => Math.Abs(x));
            double gain = Math.Pow(10, TARGET_PEAK_DB / 20) / peak;

            for (int i = 0; i < result.Length; i++)
                result[i] = Math.Clamp((float)(result[i] * gain), -1, 1);

            int peakFrame = 0;

            while (!isAudible(result, peakFrame * channels, channels, (float)(peak * gain * peak_reached_factor)))
                peakFrame++;

            return (result, new Report((double)first / sampleRate * 1000, 20 * Math.Log10(gain), (double)peakFrame / sampleRate * 1000, outFrames > frames - first, false));
        }

        private static bool isAudible(float[] samples, int offset, int channels, float threshold = SILENCE_THRESHOLD)
        {
            for (int c = 0; c < channels; c++)
            {
                if (Math.Abs(samples[offset + c]) >= threshold)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Writes 16 bit PCM wav data.
        /// </summary>
        public static byte[] ToWav(float[] samples, int channels, int sampleRate)
        {
            int dataLength = samples.Length * 2;

            using (var stream = new MemoryStream(44 + dataLength))
            using (var writer = new BinaryWriter(stream, Encoding.ASCII))
            {
                writer.Write(Encoding.ASCII.GetBytes("RIFF"));
                writer.Write(36 + dataLength);
                writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
                writer.Write(16);
                writer.Write((short)1);
                writer.Write((short)channels);
                writer.Write(sampleRate);
                writer.Write(sampleRate * channels * 2);
                writer.Write((short)(channels * 2));
                writer.Write((short)16);
                writer.Write(Encoding.ASCII.GetBytes("data"));
                writer.Write(dataLength);

                foreach (float sample in samples)
                    writer.Write((short)Math.Round(Math.Clamp(sample, -1, 1) * short.MaxValue));

                return stream.ToArray();
            }
        }

        /// <summary>
        /// Decodes any audio file BASS can read into interleaved float samples.
        /// </summary>
        public static bool TryDecode(FileInfo file, out float[] samples, out int channels, out int sampleRate)
        {
            samples = Array.Empty<float>();
            channels = 0;
            sampleRate = 0;

            try
            {
                using (var data = file.OpenRead())
                {
                    var callbacks = new FileCallbacks(new DataStreamFileProcedures(data));
                    int handle = Bass.CreateStream(StreamSystem.NoBuffer, BassFlags.Decode | BassFlags.Float, callbacks.Callbacks, callbacks.Handle);

                    if (handle == 0)
                        return false;

                    try
                    {
                        var info = Bass.ChannelGetInfo(handle);
                        channels = info.Channels;
                        sampleRate = info.Frequency;

                        var decoded = new List<float>();
                        byte[] buffer = new byte[1 << 16];

                        int read;

                        while ((read = Bass.ChannelGetData(handle, buffer, buffer.Length)) > 0)
                        {
                            var chunk = new float[read / sizeof(float)];
                            Buffer.BlockCopy(buffer, 0, chunk, 0, chunk.Length * sizeof(float));
                            decoded.AddRange(chunk);
                        }

                        // only whole frames.
                        decoded.RemoveRange(decoded.Count - decoded.Count % Math.Max(1, channels), decoded.Count % Math.Max(1, channels));
                        samples = decoded.ToArray();

                        return channels > 0 && samples.Length > 0;
                    }
                    finally
                    {
                        Bass.StreamFree(handle);
                    }
                }
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Decodes and processes a sound file into wav data.
        /// </summary>
        /// <returns>Whether the file could be read. If not, nothing was produced.</returns>
        public static bool TryProcess(FileInfo file, out byte[] wav, out Report? report)
        {
            wav = Array.Empty<byte>();
            report = null;

            if (!TryDecode(file, out float[] samples, out int channels, out int sampleRate))
                return false;

            var (processed, processReport) = Process(samples, channels, sampleRate);

            wav = ToWav(processed, channels, sampleRate);
            report = processReport;
            return true;
        }
    }
}
