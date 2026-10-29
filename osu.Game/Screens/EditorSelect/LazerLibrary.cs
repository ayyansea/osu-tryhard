// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Realms;

namespace osu.Game.Screens.EditorSelect
{
    /// <summary>
    /// A beatmap set in an osu!lazer installation.
    /// </summary>
    /// <param name="Files">The files of the set: the name inside the set, and the hash which names the file in lazer's file store.</param>
    public record LazerSet(string Artist, string Title, string Author, int Difficulties, DateTimeOffset DateAdded, IReadOnlyList<(string Name, string Hash)> Files);

    /// <summary>
    /// Reads the beatmap sets of an osu!lazer installation, so that they can be imported without exporting them from osu!lazer first.
    /// </summary>
    /// <remarks>
    /// Lazer's database is only ever read, from a copy, and without a schema: osu!lazer may be running and may be a newer version than this client.
    /// Only the few properties used here have to keep their name.
    /// </remarks>
    public static class LazerLibrary
    {
        // ponytail: the default location only. osu!lazer can be moved with a storage.ini, which isn't read here.
        public static string DataDirectory => Path.Combine(
            Environment.GetFolderPath(OperatingSystem.IsWindows() ? Environment.SpecialFolder.ApplicationData : Environment.SpecialFolder.LocalApplicationData),
            "osu");

        /// <summary>
        /// Lists the sets which have a difficulty for one of the given rulesets, newest first.
        /// </summary>
        public static List<LazerSet> Read(string dataDirectory, ICollection<string> rulesetShortNames)
        {
            string source = Path.Combine(dataDirectory, "client.realm");

            if (!File.Exists(source))
                throw new FileNotFoundException($"no osu!lazer data found in {dataDirectory}");

            var temp = Directory.CreateTempSubdirectory("osu-tryhard-lazer-library-");

            try
            {
                string copy = Path.Combine(temp.FullName, "client.realm");
                File.Copy(source, copy);

                using (var realm = Realm.GetInstance(new RealmConfiguration(copy) { IsReadOnly = true, IsDynamic = true }))
                {
                    var sets = new List<LazerSet>();

                    foreach (var set in realm.DynamicApi.All("BeatmapSet"))
                    {
                        if (set.DynamicApi.Get<bool>("DeletePending") || set.DynamicApi.Get<bool>("Protected"))
                            continue;

                        var beatmaps = set.DynamicApi.GetList<IRealmObjectBase>("Beatmaps")
                                          .Where(b => rulesetShortNames.Contains(b.DynamicApi.Get<IRealmObjectBase>("Ruleset").DynamicApi.Get<string>("ShortName")))
                                          .ToArray();

                        if (beatmaps.Length == 0)
                            continue;

                        var metadata = beatmaps[0].DynamicApi.Get<IRealmObjectBase>("Metadata").DynamicApi;

                        sets.Add(new LazerSet(
                            metadata.Get<string>("Artist"),
                            metadata.Get<string>("Title"),
                            metadata.Get<IRealmObjectBase>("Author").DynamicApi.Get<string>("Username"),
                            beatmaps.Length,
                            set.DynamicApi.Get<DateTimeOffset>("DateAdded"),
                            set.DynamicApi.GetList<IRealmObjectBase>("Files")
                               .Select(f => (f.DynamicApi.Get<string>("Filename"), f.DynamicApi.Get<IRealmObjectBase>("File").DynamicApi.Get<string>("Hash")))
                               .ToArray()));
                    }

                    return sets.OrderByDescending(s => s.DateAdded).ToList();
                }
            }
            finally
            {
                try
                {
                    temp.Delete(true);
                }
                catch
                {
                    // a left over temp copy isn't worth failing for.
                }
            }
        }

        /// <summary>
        /// Writes the files of the set, from lazer's file store, as an archive that can be imported.
        /// </summary>
        public static void WriteArchive(LazerSet set, string dataDirectory, string archivePath)
        {
            using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
            {
                foreach (var (name, hash) in set.Files)
                    archive.CreateEntryFromFile(Path.Combine(dataDirectory, "files", hash[..1], hash[..2], hash), name, CompressionLevel.Fastest);
            }
        }
    }
}
