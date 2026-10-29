// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using osu.Game.Beatmaps;
using osu.Game.Extensions;

namespace osu.Game.Screens.Edit
{
    /// <summary>
    /// Sends the beatmap set being edited to a running (or starting) osu!lazer, so that it can be tested there.
    /// </summary>
    /// <remarks>
    /// The set is exported as an archive and passed to the osu!lazer executable as an argument, which a running osu!lazer picks up and imports.
    /// Every export of the same set carries the same online ID (the real one if the set has one, otherwise a made up one), so that osu!lazer replaces the previous copy instead of adding another one.
    /// The ID is only written to the exported copy, never to the set in this client.
    /// </remarks>
    public static class LazerTestLauncher
    {
        private static readonly string[] default_locations =
        {
            "~/Downloads/osu.AppImage",
            "~/Applications/osu.AppImage",
            "~/osu.AppImage",
        };

        /// <summary>
        /// An ID which is the same for every export of a set, and stays well clear of the IDs of sets which really exist.
        /// </summary>
        public static int GetFakeOnlineID(Guid setId) => 1_000_000_000 + (int)(BitConverter.ToUInt32(setId.ToByteArray(), 0) % 1_000_000_000);

        /// <summary>
        /// Finds the osu!lazer executable: the configured one, or one in a few usual places.
        /// </summary>
        public static string? FindExecutable(string? configured)
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            return new[] { configured }.Concat(default_locations.Select(l => l.Replace("~", home))).FirstOrDefault(p => !string.IsNullOrEmpty(p) && File.Exists(p));
        }

        /// <summary>
        /// Sets the online ID of the set in a beatmap file. Beatmaps which were never submitted don't have a line for it (it is only written for IDs above 0), so it is added then.
        /// A real ID (above 0, eg. of a set imported from osu!lazer) is kept, so that osu!lazer replaces its own copy of that set.
        /// </summary>
        public static string SetOnlineID(string osuFile, int onlineId)
        {
            if (Regex.IsMatch(osuFile, @"^BeatmapSetID:", RegexOptions.Multiline))
                return Regex.Replace(osuFile, @"^(BeatmapSetID:\s*)(-?\d+)", m => int.Parse(m.Groups[2].Value) > 0 ? m.Value : $"{m.Groups[1].Value}{onlineId}", RegexOptions.Multiline);

            var header = Regex.Match(osuFile, @"^\[Metadata\][ \t]*\r?\n", RegexOptions.Multiline);

            // the order of the lines in a section doesn't matter.
            return header.Success ? osuFile.Insert(header.Index + header.Length, $"BeatmapSetID:{onlineId}{(header.Value.EndsWith("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n")}") : osuFile;
        }

        /// <summary>
        /// Writes an archive of the files with the online ID of the set changed in the beatmap files.
        /// </summary>
        public static void WriteArchive(IEnumerable<(string Name, Func<Stream> Open)> files, Stream output, int onlineId)
        {
            using (var archive = new ZipArchive(output, ZipArchiveMode.Create, true))
            {
                foreach (var (name, open) in files)
                {
                    var entry = archive.CreateEntry(name);

                    using (var source = open())
                    using (var target = entry.Open())
                    {
                        if (name.EndsWith(".osu", StringComparison.OrdinalIgnoreCase))
                        {
                            using (var reader = new StreamReader(source, Encoding.UTF8))
                            using (var writer = new StreamWriter(target, new UTF8Encoding(false)))
                                writer.Write(SetOnlineID(reader.ReadToEnd(), onlineId));
                        }
                        else
                            source.CopyTo(target);
                    }
                }
            }
        }

        /// <summary>
        /// Exports the set and starts the executable with it.
        /// </summary>
        /// <returns>A message describing what went wrong, or <c>null</c> if the set was sent.</returns>
        public static string? Launch(WorkingBeatmap working, string? configuredExecutable)
        {
            string? executable = FindExecutable(configuredExecutable);

            if (executable == null)
                return "couldn't find osu!lazer. Set LazerExecutable in game.ini to the path of the osu!lazer executable (or AppImage).";

            var set = working.BeatmapSetInfo;

            try
            {
                string directory = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "osu-tryhard-lazer-test")).FullName;
                string archivePath = Path.Combine(directory, $"{set.ID}.osz");

                using (var output = File.Create(archivePath))
                    WriteArchive(set.Files.Select(f => (f.Filename, (Func<Stream>)(() => working.GetStream(f.File.GetStoragePath())))), output, GetFakeOnlineID(set.ID));

                Process.Start(new ProcessStartInfo(executable)
                {
                    UseShellExecute = false,
                    ArgumentList = { archivePath },
                })?.Dispose();

                return null;
            }
            catch (Exception e)
            {
                return $"couldn't send the beatmap to osu!lazer: {e.Message}";
            }
        }
    }
}
