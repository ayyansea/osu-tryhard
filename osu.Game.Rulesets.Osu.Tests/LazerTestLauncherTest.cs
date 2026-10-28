// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using NUnit.Framework;
using osu.Game.Screens.Edit;

namespace osu.Game.Rulesets.Osu.Tests
{
    [TestFixture]
    public class LazerTestLauncherTest
    {
        [Test]
        public void TestFakeOnlineIDIsStableAndOutOfTheWay()
        {
            var id = Guid.NewGuid();

            Assert.That(LazerTestLauncher.GetFakeOnlineID(id), Is.EqualTo(LazerTestLauncher.GetFakeOnlineID(id)));
            Assert.That(LazerTestLauncher.GetFakeOnlineID(id), Is.InRange(1_000_000_000, 2_000_000_000));
            Assert.That(LazerTestLauncher.GetFakeOnlineID(Guid.NewGuid()), Is.Not.EqualTo(LazerTestLauncher.GetFakeOnlineID(id)));
        }

        [Test]
        public void TestOnlyTheSetIDIsReplaced()
        {
            string file = "[Metadata]\r\nTitle:x\r\nBeatmapID:5\r\nBeatmapSetID:-1\r\nTags:BeatmapSetID:7\r\n";

            Assert.That(LazerTestLauncher.SetOnlineID(file, 123), Is.EqualTo("[Metadata]\r\nTitle:x\r\nBeatmapID:5\r\nBeatmapSetID:123\r\nTags:BeatmapSetID:7\r\n"));
        }

        [Test]
        public void TestSetIDIsAddedWhenTheBeatmapHasNone()
        {
            // beatmaps which were never submitted have no ID lines at all.
            Assert.That(LazerTestLauncher.SetOnlineID("[General]\r\nMode:0\r\n\r\n[Metadata]\r\nTitle:x\r\n", 123), Is.EqualTo("[General]\r\nMode:0\r\n\r\n[Metadata]\r\nBeatmapSetID:123\r\nTitle:x\r\n"));
            Assert.That(LazerTestLauncher.SetOnlineID("[Metadata]\nTitle:x\n", 7), Is.EqualTo("[Metadata]\nBeatmapSetID:7\nTitle:x\n"));
        }

        [Test]
        public void TestArchiveHasAllFilesWithTheSetIDChangedInBeatmaps()
        {
            byte[] audio = { 1, 2, 3, 4 };

            using (var output = new MemoryStream())
            {
                LazerTestLauncher.WriteArchive(new (string, Func<Stream>)[]
                {
                    ("a.osu", () => new MemoryStream(Encoding.UTF8.GetBytes("BeatmapSetID:-1\n"))),
                    ("audio.mp3", () => new MemoryStream(audio)),
                }, output, 42);

                output.Position = 0;

                using (var archive = new ZipArchive(output))
                {
                    Assert.That(archive.Entries.Select(e => e.FullName), Is.EquivalentTo(new[] { "a.osu", "audio.mp3" }));

                    using (var reader = new StreamReader(archive.GetEntry("a.osu")!.Open()))
                        Assert.That(reader.ReadToEnd(), Is.EqualTo("BeatmapSetID:42\n"));

                    using (var data = new MemoryStream())
                    {
                        archive.GetEntry("audio.mp3")!.Open().CopyTo(data);
                        Assert.That(data.ToArray(), Is.EqualTo(audio));
                    }
                }
            }
        }
    }
}
