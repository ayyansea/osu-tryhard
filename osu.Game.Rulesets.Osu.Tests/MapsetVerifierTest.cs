// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using NUnit.Framework;
using osu.Framework.Extensions;
using osu.Game.Rulesets.Edit.Checks.Components;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Osu.Objects;
using osu.Game.Screens.Edit.Verify;

namespace osu.Game.Rulesets.Osu.Tests
{
    [TestFixture]
    public class MapsetVerifierTest
    {
        private const string result_json = @"{
            ""general"": { ""category"": ""General"", ""checkResults"": [
                { ""id"": 77, ""message"": ""Insane is missing its background file, \""bg.jpg\""."", ""level"": ""Problem"" } ] },
            ""difficulties"": [
                { ""category"": ""Insane"", ""checkResults"": [
                    { ""id"": 64, ""message"": ""01:17:379 - Uninherited line {changes} nothing."", ""level"": ""Warning"" },
                    { ""id"": 64, ""message"": ""01:17:379 (1,2) - Second one."", ""level"": ""Minor"" },
                    { ""id"": 25, ""message"": ""No timestamp, 12:34 is a time of day."", ""level"": ""Check"" } ] },
                { ""category"": ""Extra"", ""checkResults"": [ { ""id"": 25, ""message"": ""Not mine."", ""level"": ""Error"" } ] } ],
            ""checks"": { ""77"": { ""id"": 77, ""name"": ""Missing background."" }, ""64"": { ""id"": 64, ""name"": ""Unused timing lines."" } }
        }";

        [Test]
        public void TestParse()
        {
            var objects = new HitObject[] { new HitCircle { StartTime = 77_379 }, new HitCircle { StartTime = 90_000 } };

            var issues = MapsetVerifierIssues.Parse(result_json, "Insane", objects);

            Assert.That(issues, Has.Count.EqualTo(4), "the issues of other difficulties aren't included");

            var general = issues[0];
            Assert.That(general.Template.Type, Is.EqualTo(IssueType.Problem));
            Assert.That(general.Check.Metadata.Scope, Is.EqualTo(CheckScope.BeatmapSet));
            Assert.That(general.Check.Metadata.Description, Is.EqualTo("Missing background."));
            Assert.That(general.Time, Is.Null);
            Assert.That(general.ToString(), Is.EqualTo("Insane is missing its background file, \"bg.jpg\"."));

            var timed = issues[1];
            Assert.That(timed.Template.Type, Is.EqualTo(IssueType.Warning));
            Assert.That(timed.Check.Metadata.Scope, Is.EqualTo(CheckScope.Difficulty));
            Assert.That(timed.Time, Is.EqualTo(77_379));
            Assert.That(timed.HitObjects, Is.EquivalentTo(new[] { objects[0] }));
            Assert.That(timed.ToString(), Is.EqualTo("Uninherited line {changes} nothing."), "braces survive being used as a format string");

            Assert.That(issues[2].Template.Type, Is.EqualTo(IssueType.Negligible));
            Assert.That(issues[2].Time, Is.EqualTo(77_379));
            Assert.That(issues[2].ToString(), Is.EqualTo("Second one."));

            Assert.That(issues[3].Template.Type, Is.EqualTo(IssueType.Negligible));
            Assert.That(issues[3].Time, Is.Null);
            Assert.That(issues[3].Check.Metadata.Description, Is.EqualTo("Check 25"), "checks without a name are still shown");
        }

        [Test]
        public void TestWriteFolderEmptiesIt()
        {
            string directory = Path.Combine(Path.GetTempPath(), "osu-tryhard-verify-test", Guid.NewGuid().ToString());

            try
            {
                Directory.CreateDirectory(directory);
                File.WriteAllText(Path.Combine(directory, "stale.png"), "x");

                MapsetVerifierServer.WriteFolder(directory, new (string, Func<Stream>)[]
                {
                    ("a.osu", () => new MemoryStream(Encoding.UTF8.GetBytes("a"))),
                    ("sb/b.png", () => new MemoryStream(Encoding.UTF8.GetBytes("b"))),
                    ("../escape.txt", () => new MemoryStream(Encoding.UTF8.GetBytes("c"))),
                });

                Assert.That(File.Exists(Path.Combine(directory, "stale.png")), Is.False);
                Assert.That(File.ReadAllText(Path.Combine(directory, "a.osu")), Is.EqualTo("a"));
                Assert.That(File.ReadAllText(Path.Combine(directory, "sb", "b.png")), Is.EqualTo("b"));
                Assert.That(File.Exists(Path.Combine(directory, "..", "escape.txt")), Is.False, "names can't leave the folder");
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        /// <summary>
        /// Starts the real backend, so it only runs on machines which have Mapset Verifier.
        /// </summary>
        [Test]
        public void TestRunRealBackend()
        {
            string? app = MapsetVerifierServer.FindApp(null);

            if (app == null)
                Assert.Ignore("Mapset Verifier isn't installed");

            string root = Path.Combine(Path.GetTempPath(), "osu-tryhard-verify-test", Guid.NewGuid().ToString());
            string folder = Path.Combine(root, "set");

            try
            {
                const string osu = "osu file format v14\n\n[General]\nAudioFilename: audio.mp3\nMode: 0\n\n[Metadata]\nTitle:t\nArtist:a\nCreator:c\nVersion:Easy\n\n[Difficulty]\nHPDrainRate:5\nCircleSize:4\nOverallDifficulty:5\nApproachRate:5\nSliderMultiplier:1.4\nSliderTickRate:1\n\n[TimingPoints]\n0,500,4,2,0,100,1,0\n\n[HitObjects]\n256,192,1000,1,0,0:0:0:0:\n";

                MapsetVerifierServer.WriteFolder(folder, new (string, Func<Stream>)[] { ("a - t (c) [Easy].osu", () => new MemoryStream(Encoding.UTF8.GetBytes(osu))) });

                using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(3));
                string json = MapsetVerifierServer.RunChecksAsync(app, Path.Combine(root, "cache"), folder, cancellation.Token).GetResultSafely();

                var issues = MapsetVerifierIssues.Parse(json, "Easy", Array.Empty<HitObject>());

                // there is no audio file and no background, whatever else it finds.
                Assert.That(issues.Select(i => i.Check.Metadata.Scope), Does.Contain(CheckScope.BeatmapSet));
            }
            finally
            {
                MapsetVerifierServer.Stop();
                Directory.Delete(root, true);
            }
        }
    }
}
