// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Linq;
using NUnit.Framework;
using osu.Framework.Input;
using osu.Framework.Testing;
using osu.Game.Audio;
using osu.Game.Graphics.UserInterface;
using osu.Game.Graphics.UserInterfaceV2;
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
        public void TestAddSampleOpensFileChooser()
        {
            AddStep("click the first + button", () =>
            {
                InputManager.MoveMouseTo(Editor.ChildrenOfType<HitsoundSampleSetPanel>().Single().ChildrenOfType<OsuButton>().First());
                InputManager.Click(MouseButton.Left);
            });

            AddUntilStep("file chooser with preview shown", () => Editor.ChildrenOfType<FormFileSelector.FileChooserPopover>().Any(p => p.IsLoaded));
        }

        [Test]
        public void TestBoxSelectScrollsTimeline()
        {
            double startTime = 0;

            AddStep("select tool", () => InputManager.Key(Key.Number1));
            AddStep("remember time", () => startTime = EditorClock.CurrentTime);
            AddStep("start a box in the middle of the timeline", () =>
            {
                var timeline = Editor.ChildrenOfType<HitsoundsScreen>().Single().ChildrenOfType<Timeline>().Single();
                InputManager.MoveMouseTo(new Vector2(timeline.ScreenSpaceDrawQuad.Centre.X, pattern.ToScreenSpace(Vector2.Zero).Y + HitsoundLayout.ROW_HEIGHT / 2));
                InputManager.PressButton(MouseButton.Left);
            });
            AddStep("drag to the right edge", () =>
            {
                var timeline = Editor.ChildrenOfType<HitsoundsScreen>().Single().ChildrenOfType<Timeline>().Single();
                InputManager.MoveMouseTo(new Vector2(timeline.ScreenSpaceDrawQuad.TopRight.X - 5, pattern.ToScreenSpace(Vector2.Zero).Y + HitsoundLayout.ROW_HEIGHT / 2));
            });
            AddUntilStep("timeline scrolled along", () => EditorClock.CurrentTime, () => Is.GreaterThan(startTime + 500));
            AddStep("release", () => InputManager.ReleaseButton(MouseButton.Left));
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
