// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.Formats;
using osu.Game.IO;
using osu.Game.Database;
using osu.Game.Extensions;
using osu.Game.Rulesets;
using osu.Game.Screens.Edit;
using osu.Game.Tests.Resources;

namespace osu.Game.Tests.Database
{
    /// <summary>
    /// Sending a set to osu!lazer repeatedly (see <see cref="LazerTestLauncher"/>) should replace the previous copy.
    /// </summary>
    [TestFixture]
    public class LazerTestImportTest : RealmTest
    {
        [Test]
        public void TestSendingTheSameSetAgainReplacesIt()
        {
            RunTestWithRealmAsync(async (realm, storage) =>
            {
                var importer = new BeatmapImporter(storage, realm);
                using var store = new RealmRulesetStore(realm, storage);

                string source = TestResources.GetQuickTestBeatmapForImport();
                int onlineId = LazerTestLauncher.GetFakeOnlineID(Guid.NewGuid());

                for (int i = 0; i < 3; i++)
                {
                    string path = Path.Combine(Path.GetTempPath(), $"send-{Guid.NewGuid():N}.osz");

                    using (var zip = ZipFile.OpenRead(source))
                    using (var output = File.Create(path))
                    {
                        int send = i;

                        // the files change between sends, like the edits between two tests would.
                        LazerTestLauncher.WriteArchive(zip.Entries.Select(e => (e.FullName, (Func<Stream>)(() =>
                        {
                            var copy = new MemoryStream();
                            using (var entry = e.Open())
                                entry.CopyTo(copy);

                            if (e.FullName.EndsWith(".osu", StringComparison.OrdinalIgnoreCase))
                            {
                                copy.Write(Encoding.UTF8.GetBytes($"\n// send {send}\n"));
                                // difficulties which were never submitted have no online IDs at all.
                                string text = System.Text.RegularExpressions.Regex.Replace(Encoding.UTF8.GetString(copy.ToArray()), @"^Beatmap(Set)?ID:.*\r?\n", string.Empty, System.Text.RegularExpressions.RegexOptions.Multiline);
                                copy = new MemoryStream(Encoding.UTF8.GetBytes(text));
                            }

                            copy.Position = 0;
                            return copy;
                        }))), output, onlineId);
                    }

                    await importer.Import(new ImportTask(path));
                }

                var sets = realm.Realm.All<BeatmapSetInfo>().ToList();

                TestContext.WriteLine(string.Join("\n", sets.Select(s => $"{s.ID} online={s.OnlineID} pending={s.DeletePending} beatmaps={string.Join(",", s.Beatmaps.Select(b => b.OnlineID))}")));

                Assert.That(sets.Count(s => !s.DeletePending), Is.EqualTo(1));
            });
        }

        [Test]
        public void TestRealOnlineIDOfAnImportedSetSurvivesSavingAndSending()
        {
            RunTestWithRealmAsync(async (realm, storage) =>
            {
                var importer = new BeatmapImporter(storage, realm);
                using var store = new RealmRulesetStore(realm, storage);

                // the test archive is a real submitted set (ID 241526), like one imported from osu!lazer.
                var imported = await importer.Import(new ImportTask(TestResources.GetQuickTestBeatmapForImport()));
                Assert.That(imported, Is.Not.Null);

                var set = imported!.PerformRead(s => s.Detach());
                Assert.That(set.OnlineID, Is.EqualTo(241526));

                string storedPath = imported.PerformRead(s => s.Files.First(f => f.Filename.EndsWith(".osu", StringComparison.OrdinalIgnoreCase)).File.GetStoragePath());

                // what saving in the editor does: decode, encode again with the set the beatmap belongs to.
                string saved;

                using (var stream = storage.GetStream(Path.Combine("files", storedPath)))
                using (var reader = new LineBufferedReader(stream))
                {
                    var beatmap = new LegacyBeatmapDecoder().Decode(reader);
                    beatmap.BeatmapInfo.BeatmapSet = set;

                    using var encoded = new MemoryStream();
                    using (var writer = new StreamWriter(encoded, Encoding.UTF8, 1024, true))
                        new LegacyBeatmapEncoder(beatmap, null, null).Encode(writer);

                    saved = Encoding.UTF8.GetString(encoded.ToArray());
                }

                Assert.That(saved, Does.Contain("BeatmapSetID: 241526"));

                // what F8 sends: the saved file, with the fake ID for sets which have none.
                using var output = new MemoryStream();
                int fakeId = LazerTestLauncher.GetFakeOnlineID(set.ID);
                LazerTestLauncher.WriteArchive(new[] { ("map.osu", (Func<Stream>)(() => new MemoryStream(Encoding.UTF8.GetBytes(saved)))) }, output, fakeId);

                output.Position = 0;
                using var archive = new ZipArchive(output);
                using var sent = new StreamReader(archive.GetEntry("map.osu")!.Open());
                string sentText = sent.ReadToEnd();

                Assert.That(sentText, Does.Contain("BeatmapSetID: 241526"));
                Assert.That(sentText, Does.Not.Contain($"BeatmapSetID:{fakeId}"));
            });
        }
    }
}
