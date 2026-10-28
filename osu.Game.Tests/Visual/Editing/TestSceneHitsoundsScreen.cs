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
                InputManager.MoveMouseTo(Editor.ChildrenOfType<HitsoundSampleSetPanel>().Single().ChildrenOfType<OsuButton>().First(b => b.Text.ToString() is "+" or "play"));
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
        public void TestProcessSampleFile()
        {
            string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"hitsound-test-{System.Guid.NewGuid():N}.wav");
            HitsoundSampleProcessor.Report? report = null;
            byte[] wav = System.Array.Empty<byte>();

            // 20 ms of silence followed by a quiet tone, as a plain wav file.
            AddStep("process a file with leading silence", () =>
            {
                float[] samples = new float[882].Concat(Enumerable.Range(0, 4410).Select(i => 0.1f * System.MathF.Sin(i * 0.2f + 1))).ToArray();
                System.IO.File.WriteAllBytes(path, HitsoundSampleProcessor.ToWav(samples, 1, 44100));

                Assert.That(HitsoundSampleProcessor.TryProcess(new System.IO.FileInfo(path), out wav, out report), Is.True);
            });

            AddAssert("silence was trimmed", () => report!.TrimmedMs, () => Is.EqualTo(20).Within(1));
            AddAssert("normalised", () => report!.GainDb, () => Is.GreaterThan(15));
            AddAssert("a wav came out", () => System.Text.Encoding.ASCII.GetString(wav, 0, 4), () => Is.EqualTo("RIFF"));
            AddStep("clean up", () => System.IO.File.Delete(path));
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

        [Test]
        public void TestVolumeSliders()
        {
            AddStep("set the paint volume", () => volumeSlider("paint volume").Current.Value = 50);

            AddStep("paint in the first lane at the centre marker", () =>
            {
                float x = Editor.ChildrenOfType<HitsoundsScreen>().Single().ChildrenOfType<Timeline>().Single().ScreenSpaceDrawQuad.Centre.X;
                float y = pattern.ToScreenSpace(Vector2.Zero).Y + HitsoundLayout.ROW_HEIGHT / 2;

                InputManager.MoveMouseTo(new Vector2(x, y));
                InputManager.Click(MouseButton.Left);
            });

            AddAssert("painted with the paint volume", () => hitsounds.Steps.Single().Samples.Single().Volume, () => Is.EqualTo(50));
            AddAssert("nothing to set the selection to", () => volumeSlider("selected volume").Current.Disabled, () => Is.True);

            AddStep("select everything", () => hitsounds.SelectAll());
            AddUntilStep("selection slider shows the volume", () => volumeSlider("selected volume").Current.Disabled == false && volumeSlider("selected volume").Current.Value == 50);

            AddStep("set the selected volume", () => volumeSlider("selected volume").Current.Value = 30);
            AddAssert("sound changed", () => hitsounds.Steps.Single().Samples.Single().Volume, () => Is.EqualTo(30));
            AddAssert("sound is still selected", () => hitsounds.IsSelected(hitsounds.Steps.Single().Samples.Single()));

            AddStep("set all volume", () => volumeSlider("all volume").Current.Value = 80);
            AddAssert("sound changed again", () => hitsounds.Steps.Single().Samples.Single().Volume, () => Is.EqualTo(80));
            AddUntilStep("selection slider follows", () => volumeSlider("selected volume").Current.Value, () => Is.EqualTo(80));

            AddStep("undo", () => InputManager.Keys(PlatformAction.Undo));
            AddAssert("volume restored", () => hitsounds.Steps.Single().Samples.Single().Volume, () => Is.EqualTo(30));
        }

        private FormSliderBar<int> volumeSlider(string caption) => Editor.ChildrenOfType<FormSliderBar<int>>().Single(s => s.Caption.ToString() == caption);
    }
}
