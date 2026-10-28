// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Linq;
using NUnit.Framework;
using osu.Framework.Testing;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Edit.Checks.Components;
using osu.Game.Rulesets.Osu;
using osu.Game.Screens.Edit;
using osu.Game.Screens.Edit.Verify;

namespace osu.Game.Tests.Visual.Editing
{
    public partial class TestSceneVerifyScreen : EditorTestScene
    {
        protected override Ruleset CreateEditorRuleset() => new OsuRuleset();

        /// <summary>
        /// Starts the real Mapset Verifier backend, so it only runs on machines which have it.
        /// </summary>
        [Test]
        public void TestMapsetVerifierIssuesAreShown()
        {
            if (MapsetVerifierServer.FindApp(null) == null)
                Assert.Ignore("Mapset Verifier isn't installed");

            AddStep("switch to verify screen", () => Editor.Mode.Value = EditorScreenMode.Verify);
            AddUntilStep("mapset verifier finished", () => status() != null && status() != "Running Mapset Verifier...");
            AddAssert("checked with mapset verifier", status, () => Is.EqualTo("Checked with Mapset Verifier"));

            AddStep("show the issues of the set", () => Editor.ChildrenOfType<VerifyScreen>().Single().VerifyChecksScope.Value = CheckScope.BeatmapSet);
            AddAssert("issues come from mapset verifier", () => Editor.ChildrenOfType<IssueTable>().Single().Issues.Any(i => i.Check is MapsetVerifierCheck));
        }

        private string status() => Editor.ChildrenOfType<IssueList>().SingleOrDefault()?.Source;

        protected override void Dispose(bool isDisposing)
        {
            base.Dispose(isDisposing);
            MapsetVerifierServer.Stop();
        }
    }
}
