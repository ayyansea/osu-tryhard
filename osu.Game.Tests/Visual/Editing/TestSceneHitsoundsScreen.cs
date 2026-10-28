// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Linq;
using NUnit.Framework;
using osu.Framework.Input;
using osu.Framework.Testing;
using osu.Game.Audio;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.ControlPoints;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Osu;
using osu.Game.Screens.Edit.Compose.Components.Timeline;
using osu.Game.Screens.Edit.Hitsounds;
using osu.Game.Tests.Beatmaps;
using osuTK;
using osuTK.Input;

namespace osu.Game.Tests.Visual.Editing
{
    public partial class TestSceneHitsoundsScreen : EditorTestScene
    {
        protected override Ruleset CreateEditorRuleset() => new OsuRuleset();

        protected override IBeatmap CreateBeatmap(RulesetInfo ruleset)
        {
            var beatmap = new TestBeatmap(ruleset, false);
            beatmap.ControlPointInfo.Clear();
            beatmap.ControlPointInfo.Add(0, new TimingControlPoint { BeatLength = 500 });
            return beatmap;
        }

        private EditorHitsounds hitsounds => Editor.ChildrenOfType<EditorHitsounds>().Single();

        private HitsoundPatternEditor pattern => Editor.ChildrenOfType<HitsoundPatternEditor>().Single();

        public override void SetUpSteps()
        {
            base.SetUpSteps();

            AddStep("seek to a beat", () => EditorClock.Seek(1000));
            AddStep("switch to hitsounds screen", () => InputManager.Key(Key.F7));
            AddUntilStep("grid and labels loaded", () => Editor.ChildrenOfType<HitsoundPatternEditor>().SingleOrDefault()?.IsLoaded == true
                                                         && Editor.ChildrenOfType<HitsoundLaneLabels>().SingleOrDefault()?.IsLoaded == true);
        }

        [Test]
        public void TestPaintLane()
        {
            // the first lane is soft normal.
            AddStep("click in the first lane at the centre marker", () =>
            {
                float x = Editor.ChildrenOfType<HitsoundsScreen>().Single().ChildrenOfType<Timeline>().Single().ScreenSpaceDrawQuad.Centre.X;
                float y = pattern.ToScreenSpace(Vector2.Zero).Y + HitsoundLayout.ROW_HEIGHT / 2;

                InputManager.MoveMouseTo(new Vector2(x, y));
                InputManager.Click(MouseButton.Left);
            });

            AddAssert("a step was placed", () => hitsounds.Steps.Count, () => Is.EqualTo(1));
            AddAssert("at the current time", () => hitsounds.Steps.Single().StartTime, () => Is.EqualTo(1000).Within(1));
            AddAssert("with soft normal", () => hitsounds.Steps.Single().Samples.Single().Bank, () => Is.EqualTo(HitSampleInfo.BANK_SOFT));

            AddStep("undo", () => InputManager.Keys(PlatformAction.Undo));
        }
    }
}
