// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.IO;
using System.IO.Compression;
using System.Linq;
using NUnit.Framework;
using osu.Game.Screens.EditorSelect;

namespace osu.Game.Tests.Database
{
    /// <summary>
    /// Runs against the osu!lazer installation of the machine, and is inconclusive without one.
    /// </summary>
    [TestFixture]
    public class LazerLibraryTest
    {
        [Test]
        public void TestReadAndArchive()
        {
            if (!File.Exists(Path.Combine(LazerLibrary.DataDirectory, "client.realm")))
                Assert.Inconclusive("osu!lazer isn't installed");

            var watch = System.Diagnostics.Stopwatch.StartNew();
            var sets = LazerLibrary.Read(LazerLibrary.DataDirectory, new[] { "osu" });
            TestContext.Progress.WriteLine($"read {sets.Count} sets in {watch.ElapsedMilliseconds} ms");

            Assert.That(sets, Is.Not.Empty);
            Assert.That(sets.All(s => s.Difficulties > 0 && s.Files.Any(f => f.Name.EndsWith(".osu", System.StringComparison.OrdinalIgnoreCase))));

            string path = Path.Combine(Path.GetTempPath(), $"lazer-library-{Path.GetRandomFileName()}.osz");

            try
            {
                // the smallest set to keep the test quick.
                var set = sets.OrderBy(s => s.Files.Sum(f => new FileInfo(Path.Combine(LazerLibrary.DataDirectory, "files", f.Hash[..1], f.Hash[..2], f.Hash)).Length)).First();
                LazerLibrary.WriteArchive(set, LazerLibrary.DataDirectory, path);

                using (var zip = ZipFile.OpenRead(path))
                    Assert.That(zip.Entries.Select(e => e.FullName), Is.EquivalentTo(set.Files.Select(f => f.Name)));
            }
            finally
            {
                File.Delete(path);
            }
        }
    }
}
