// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Linq;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.ControlPoints;
using osu.Game.Configuration;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Objects.Types;
using osu.Game.Rulesets.Osu.Edit.Blueprints.Sliders;
using osu.Game.Rulesets.Osu.Edit.Blueprints.Sliders.Components;
using osu.Game.Rulesets.Osu.Objects;
using osu.Game.Rulesets.Osu.Objects.Drawables;
using osu.Game.Tests.Visual;
using osuTK;

namespace osu.Game.Rulesets.Osu.Tests.Editor
{
    public partial class TestSceneSliderSectionAngles : SelectionBlueprintTestScene
    {
        private Slider slider = null!;
        private SliderSelectionBlueprint blueprint = null!;

        [Resolved]
        private OsuConfigManager config { get; set; } = null!;

        [SetUp]
        public void Setup() => Schedule(() =>
        {
            Clear();

            config.SetValue(OsuSetting.EditorShowSliderSectionAngles, true);

            slider = new Slider
            {
                Position = new Vector2(100, 192),
                Path = new SliderPath(PathType.LINEAR, new[]
                {
                    Vector2.Zero,
                    new Vector2(200, 0),
                    // A section too short to fit its label next to it.
                    new Vector2(205, -5),
                })
            };

            slider.ApplyDefaults(new ControlPointInfo(), new BeatmapDifficulty { CircleSize = 4 });

            var drawableObject = new DrawableSlider(slider);
            Add(drawableObject);
            AddBlueprint(blueprint = new SliderSelectionBlueprint(slider), drawableObject);
        });

        [Test]
        public void TestHoverSection()
        {
            moveMouseToSliderPosition(new Vector2(100, 0));

            AddUntilStep("one indicator shown", () => visibleIndicators().Length, () => Is.EqualTo(1));
            AddAssert("indicator shows 0°", () => visibleIndicators().Single().Text, () => Is.EqualTo("0°"));
            AddAssert("label next to section", () => visibleIndicators().Single().IsCallout, () => Is.False);
        }

        [Test]
        public void TestShortSectionUsesCallout()
        {
            moveMouseToSliderPosition(new Vector2(205, -5));

            AddUntilStep("short section indicator shown", () => visibleIndicators().Any(i => i.Text == "45°"));
            AddAssert("short section label is a callout", () => visibleIndicators().Single(i => i.Text == "45°").IsCallout);
        }

        [Test]
        public void TestNotShownAwayFromSlider()
        {
            moveMouseToSliderPosition(new Vector2(100, 0));
            AddUntilStep("indicator shown", () => visibleIndicators().Length, () => Is.EqualTo(1));

            moveMouseToSliderPosition(new Vector2(100, 150));
            AddUntilStep("no indicators shown", () => visibleIndicators(), () => Is.Empty);
        }

        [Test]
        public void TestToggle()
        {
            moveMouseToSliderPosition(new Vector2(100, 0));
            AddUntilStep("indicator shown", () => visibleIndicators().Length, () => Is.EqualTo(1));

            AddStep("disable section angles", () => config.SetValue(OsuSetting.EditorShowSliderSectionAngles, false));
            AddUntilStep("no indicators shown", () => visibleIndicators(), () => Is.Empty);

            AddStep("enable section angles", () => config.SetValue(OsuSetting.EditorShowSliderSectionAngles, true));
            AddUntilStep("indicator shown again", () => visibleIndicators().Length, () => Is.EqualTo(1));
        }

        [TestCase(1, 0, 0)]
        [TestCase(1, -1, 45)]
        [TestCase(0, -1, 90)]
        [TestCase(-1, 0, 180)]
        [TestCase(0, 1, 270)]
        [TestCase(1, 1, 315)]
        public void TestSectionAngle(float x, float y, float expected)
        {
            Assert.That(SliderSectionAngleOverlay.GetSectionAngle(Vector2.Zero, new Vector2(x, y)), Is.EqualTo(expected).Within(0.01));
        }

        private void moveMouseToSliderPosition(Vector2 position) => AddStep($"move mouse to {position}", () =>
        {
            var overlay = blueprint.ChildrenOfType<SliderSectionAngleOverlay>().Single();
            InputManager.MoveMouseTo(overlay.ToScreenSpace(slider.StackedPosition + position));
        });

        private SliderSectionAngleOverlay.SectionAngleIndicator[] visibleIndicators()
            => blueprint.ChildrenOfType<SliderSectionAngleOverlay.SectionAngleIndicator>().Where(i => i.IsPresent).ToArray();
    }
}
