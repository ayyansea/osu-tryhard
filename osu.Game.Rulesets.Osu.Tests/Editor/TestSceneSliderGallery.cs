// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Linq;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics;
using osu.Framework.Platform;
using osu.Framework.Screens;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.ControlPoints;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Rulesets.Edit;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Objects.Types;
using osu.Game.Rulesets.Osu.Edit;
using osu.Game.Rulesets.Osu.Edit.SliderGallery;
using osu.Game.Rulesets.Osu.Objects;
using osu.Game.Rulesets.Osu.UI;
using osu.Game.Tests.Beatmaps;
using osu.Game.Tests.Visual;
using osuTK;
using osuTK.Input;

namespace osu.Game.Rulesets.Osu.Tests.Editor
{
    public partial class TestSceneSliderGallery : EditorTestScene
    {
        private const double beat_length = 500;

        protected override Ruleset CreateEditorRuleset() => new OsuRuleset();

        [Resolved]
        private Storage storage { get; set; } = null!;

        protected override IBeatmap CreateBeatmap(RulesetInfo ruleset)
        {
            var beatmap = new TestBeatmap(ruleset, false);
            beatmap.ControlPointInfo.Clear();
            beatmap.ControlPointInfo.Add(0, new TimingControlPoint { BeatLength = beat_length });
            beatmap.HitObjects.Clear();
            beatmap.HitObjects.Add(new Slider
            {
                StartTime = 1000,
                Position = new Vector2(100, 100),
                Path = new SliderPath(new[]
                {
                    new PathControlPoint(Vector2.Zero, PathType.PERFECT_CURVE),
                    new PathControlPoint(new Vector2(60, -60)),
                    new PathControlPoint(new Vector2(120, 0)),
                }),
            });

            // the test track's length comes from the last end time, which needs the slider's velocity.
            beatmap.HitObjects[0].ApplyDefaults(beatmap.ControlPointInfo, beatmap.Difficulty);
            return beatmap;
        }

        private SliderGalleryOverlay gallery => Editor.ChildrenOfType<SliderGalleryOverlay>().Single();

        private OsuHitObjectComposer composer => Editor.ChildrenOfType<OsuHitObjectComposer>().Single();

        private Slider originalSlider => EditorBeatmap.HitObjects.OfType<Slider>().First();

        public override void SetUpSteps()
        {
            AddStep("clear gallery", () => storage.DeleteDirectory(SliderGalleryStore.DIRECTORY));
            base.SetUpSteps();
        }

        [Test]
        public void TestSaveAndPlace()
        {
            double originalDuration = 0;

            AddStep("store duration", () => originalDuration = originalSlider.Duration);
            AddStep("select slider", () => EditorBeatmap.SelectedHitObjects.Add(originalSlider));

            openGallery();

            clickButton("Save selected slider");
            AddAssert("shape stored", () => storage.GetStorageForDirectory(SliderGalleryStore.DIRECTORY).GetFiles(string.Empty).Count(), () => Is.EqualTo(1));

            clickButton("Place");
            AddUntilStep("gallery hidden", () => gallery.State.Value, () => Is.EqualTo(Visibility.Hidden));
            AddAssert("gallery tool active", () => composer.BlueprintContainer.CurrentTool, () => Is.InstanceOf<SliderGalleryCompositionTool>());

            placeAt(3000);
            AddAssert("slider placed at 3000", () => EditorBeatmap.HitObjects.OfType<Slider>().Any(s => s.StartTime == 3000));
            AddAssert("placed slider has the shape's duration", () => EditorBeatmap.HitObjects.OfType<Slider>().Single(s => s.StartTime == 3000).Duration,
                () => Is.EqualTo(originalDuration).Within(1));
            AddAssert("placed slider has node samples", () => EditorBeatmap.HitObjects.OfType<Slider>().Single(s => s.StartTime == 3000).NodeSamples, () => Has.Count.EqualTo(2));
        }

        [Test]
        public void TestFitToBeats()
        {
            AddStep("select slider", () => EditorBeatmap.SelectedHitObjects.Add(originalSlider));
            openGallery();
            clickButton("Save selected slider");

            AddStep("fit to a fixed number of beats", () => gallery.ChildrenOfType<FormEnumDropdown<SliderDurationSource>>().Single().Current.Value = SliderDurationSource.Beats);
            AddStep("set 3 beats", () => gallery.ChildrenOfType<FormSliderBar<double>>().Single().Current.Value = 3);
            AddStep("keep size", () => gallery.ChildrenOfType<FormEnumDropdown<SliderFitMode>>().Single().Current.Value = SliderFitMode.KeepSize);

            clickButton("Place");
            placeAt(5000);

            AddAssert("placed slider lasts 3 beats", () => EditorBeatmap.HitObjects.OfType<Slider>().Single(s => s.StartTime == 5000).Duration,
                () => Is.EqualTo(3 * beat_length).Within(1));
            AddAssert("placed slider kept the shape's size", () => EditorBeatmap.HitObjects.OfType<Slider>().Single(s => s.StartTime == 5000).Path.Distance,
                () => Is.EqualTo(originalSlider.Path.Distance).Within(0.5));
        }

        [Test]
        public void TestApplyToSelectionKeepsDuration()
        {
            double originalDuration = 0;

            AddStep("store duration", () => originalDuration = originalSlider.Duration);
            AddStep("save a straight shape", () =>
            {
                var store = new SliderGalleryStore(storage);
                store.Save(new SliderShape
                {
                    Name = "line",
                    ControlPoints = { (Vector2.Zero, PathType.LINEAR), (new Vector2(300, 0), null) },
                    Length = 300,
                    Beats = 1,
                });
            });

            AddStep("select slider", () => EditorBeatmap.SelectedHitObjects.Add(originalSlider));
            openGallery();
            AddStep("reload", () => gallery.ChildrenOfType<RoundedButton>().Single(b => b.Text.ToString() == "Reload").TriggerClick());
            AddStep("pick the line", () => gallery.ChildrenOfType<SliderShapeThumbnail>().First().FindClosestParent<OsuClickableContainer>()!.TriggerClick());

            clickButton("Apply to selected slider");

            AddAssert("slider is a line now", () => originalSlider.Path.ControlPoints.Last().Position.Y, () => Is.EqualTo(0).Within(0.01));
            AddAssert("duration kept", () => originalSlider.Duration, () => Is.EqualTo(originalDuration).Within(0.5));
        }

        [Test]
        public void TestEscapeClosesGalleryOnly()
        {
            openGallery();
            AddStep("press escape", () => InputManager.Key(Key.Escape));
            AddUntilStep("gallery hidden", () => gallery.State.Value, () => Is.EqualTo(Visibility.Hidden));
            AddAssert("still in editor", () => Editor.IsCurrentScreen());
        }

        private void openGallery()
        {
            AddStep("open gallery", () =>
            {
                InputManager.PressKey(Key.ControlLeft);
                InputManager.PressKey(Key.ShiftLeft);
                InputManager.Key(Key.G);
                InputManager.ReleaseKey(Key.ShiftLeft);
                InputManager.ReleaseKey(Key.ControlLeft);
            });
            AddUntilStep("gallery visible", () => gallery.State.Value, () => Is.EqualTo(Visibility.Visible));
        }

        private void clickButton(string text) =>
            AddStep($"click \"{text}\"", () => gallery.ChildrenOfType<RoundedButton>().Single(b => b.Text.ToString() == text).TriggerClick());

        private void placeAt(double time)
        {
            AddStep($"seek to {time}", () => EditorClock.Seek(time));
            AddStep("move to playfield", () => InputManager.MoveMouseTo(Editor.ChildrenOfType<OsuPlayfield>().First()));
            AddUntilStep("placement ready", () => composer.BlueprintContainer.CurrentPlacement is HitObjectPlacementBlueprint { HitObject.StartTime: var t } && t == time);
            AddStep("click", () => InputManager.Click(MouseButton.Left));
        }
    }
}
