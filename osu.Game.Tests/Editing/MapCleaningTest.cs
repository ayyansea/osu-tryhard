// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Linq;
using NUnit.Framework;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.ControlPoints;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Objects.Types;
using osu.Game.Rulesets.Osu;
using osu.Game.Rulesets.Osu.Objects;
using osu.Game.Screens.Edit;
using osu.Game.Screens.Edit.MapCleaner;
using osuTK;

namespace osu.Game.Tests.Editing
{
    [TestFixture]
    public class MapCleaningTest
    {
        private const double beat_length = 500;

        private static EditorBeatmap createBeatmap(ControlPointInfo? controlPoints = null, params HitObject[] hitObjects)
        {
            if (controlPoints == null)
            {
                controlPoints = new ControlPointInfo();
                controlPoints.Add(0, new TimingControlPoint { BeatLength = beat_length });
            }

            var beatmap = new Beatmap
            {
                ControlPointInfo = controlPoints,
                BeatmapInfo = { Ruleset = new OsuRuleset().RulesetInfo },
            };

            foreach (var hitObject in hitObjects)
            {
                hitObject.ApplyDefaults(controlPoints, beatmap.Difficulty);
                beatmap.HitObjects.Add(hitObject);
            }

            var editorBeatmap = new EditorBeatmap(beatmap);

            var processor = new EditorBeatmapProcessor(editorBeatmap, new OsuRuleset());
            processor.PreProcess();
            processor.PostProcess();

            return editorBeatmap;
        }

        private static MapCleanerOptions only(CleanupKind kind) => new MapCleanerOptions
        {
            ResnapObjects = kind == CleanupKind.ResnapObject,
            ResnapOther = kind is CleanupKind.ResnapEffectPoint or CleanupKind.ResnapBookmark,
            RemoveRedundantTimingPoints = kind == CleanupKind.RedundantTimingPoint,
            TidyComboColourSkips = kind == CleanupKind.ComboColourSkip,
        };

        private static void planAndApply(EditorBeatmap beatmap, MapCleanerOptions options) => MapCleaning.Apply(beatmap, MapCleaning.Plan(beatmap, options));

        [Test]
        public void TestResnapCircles()
        {
            var slightlyOff = new HitCircle { StartTime = 1002 };
            var snapped = new HitCircle { StartTime = 1250 };
            // 11 ms from the closest snap (1/16): past the default max distance, so probably intended.
            var farOff = new HitCircle { StartTime = 1520 };
            var beatmap = createBeatmap(null, slightlyOff, snapped, farOff);

            var plan = MapCleaning.Plan(beatmap, only(CleanupKind.ResnapObject));
            Assert.That(plan.Select(c => c.Time), Is.EqualTo(new[] { 1002d }));

            MapCleaning.Apply(beatmap, plan);

            Assert.That(slightlyOff.StartTime, Is.EqualTo(1000));
            Assert.That(snapped.StartTime, Is.EqualTo(1250));
            Assert.That(farOff.StartTime, Is.EqualTo(1520));
        }

        [Test]
        public void TestMaxDistance()
        {
            var farOff = new HitCircle { StartTime = 1520 };
            var beatmap = createBeatmap(null, farOff);

            planAndApply(beatmap, new MapCleanerOptions { MaxResnapDistance = 15 });

            Assert.That(farOff.StartTime, Is.EqualTo(1531.25).Within(0.01));
        }

        [Test]
        public void TestResnapSliderEndKeepsVelocity()
        {
            var slider = new Slider
            {
                StartTime = 0,
                Path = new SliderPath(new[] { new PathControlPoint(Vector2.Zero), new PathControlPoint(new Vector2(300, 0)) }),
            };

            var beatmap = createBeatmap(null, slider);

            // make the slider end 3 ms after a beat, whatever the default velocity is.
            double velocity = slider.Path.Distance / slider.Duration;
            slider.Path.ExpectedDistance.Value = velocity * 503;
            beatmap.Update(slider);
            Assume.That(slider.EndTime, Is.EqualTo(503).Within(0.01));

            double sliderVelocity = slider.SliderVelocityMultiplier;

            planAndApply(beatmap, only(CleanupKind.ResnapObject));

            Assert.That(slider.EndTime, Is.EqualTo(500).Within(0.01));
            Assert.That(slider.SliderVelocityMultiplier, Is.EqualTo(sliderVelocity));
        }

        [Test]
        public void TestResnapSliderStartMovesEndWithIt()
        {
            var slider = new Slider
            {
                StartTime = 1002,
                Path = new SliderPath(new[] { new PathControlPoint(Vector2.Zero), new PathControlPoint(new Vector2(300, 0)) }),
            };

            var beatmap = createBeatmap(null, slider);

            double velocity = slider.Path.Distance / slider.Duration;
            slider.Path.ExpectedDistance.Value = velocity * 500;
            beatmap.Update(slider);

            planAndApply(beatmap, only(CleanupKind.ResnapObject));

            // moving the start 2 ms back puts the end on a snap as well, so the length stays.
            Assert.That(slider.StartTime, Is.EqualTo(1000));
            Assert.That(slider.EndTime, Is.EqualTo(1500).Within(0.01));
        }

        [Test]
        public void TestResnapSpinnerEnd()
        {
            var spinner = new Spinner { StartTime = 2000, Duration = 998 };
            var beatmap = createBeatmap(null, spinner);

            planAndApply(beatmap, only(CleanupKind.ResnapObject));

            Assert.That(spinner.EndTime, Is.EqualTo(3000));
        }

        [Test]
        public void TestResnapEffectPointsAndBookmarks()
        {
            var controlPoints = new ControlPointInfo();
            controlPoints.Add(0, new TimingControlPoint { BeatLength = beat_length });
            controlPoints.Add(1002, new EffectControlPoint { KiaiMode = true });
            controlPoints.Add(3000, new EffectControlPoint { KiaiMode = false });

            var beatmap = createBeatmap(controlPoints);
            beatmap.Bookmarks.AddRange(new[] { 1500, 2003 });

            planAndApply(beatmap, only(CleanupKind.ResnapEffectPoint));

            Assert.That(beatmap.ControlPointInfo.EffectPoints.Select(p => p.Time), Is.EqualTo(new[] { 1000d, 3000d }));
            Assert.That(beatmap.ControlPointInfo.EffectPoints[0].KiaiMode, Is.True);
            Assert.That(beatmap.Bookmarks, Is.EquivalentTo(new[] { 1500, 2000 }));
        }

        [Test]
        public void TestRedundantTimingPoints()
        {
            var controlPoints = new ControlPointInfo();
            controlPoints.Add(0, new TimingControlPoint { BeatLength = beat_length });
            // on a bar line of the first (4/4 at 500 ms = 2000 ms bars): redundant.
            controlPoints.Add(4000, new TimingControlPoint { BeatLength = beat_length });
            // off the bar lines: moves the grid, so it stays.
            controlPoints.Add(4250, new TimingControlPoint { BeatLength = beat_length });
            // on a bar line of the one before (6250 = 4250 + one bar): redundant.
            controlPoints.Add(6250, new TimingControlPoint { BeatLength = beat_length });
            // other BPM: stays.
            controlPoints.Add(8250, new TimingControlPoint { BeatLength = 400 });
            // omits its first bar line: stays.
            controlPoints.Add(10250, new TimingControlPoint { BeatLength = 400, OmitFirstBarLine = true });

            var beatmap = createBeatmap(controlPoints);

            planAndApply(beatmap, only(CleanupKind.RedundantTimingPoint));

            Assert.That(beatmap.ControlPointInfo.TimingPoints.Select(p => p.Time), Is.EqualTo(new[] { 0d, 4250, 8250, 10250 }));
        }

        [Test]
        public void TestComboColourSkips()
        {
            var first = new HitCircle { StartTime = 0, NewCombo = true, ComboOffset = 2 };
            var noCombo = new HitCircle { StartTime = 500, ComboOffset = 3 };
            var beatmap = createBeatmap(null, first, noCombo);

            planAndApply(beatmap, only(CleanupKind.ComboColourSkip));

            // without a palette there's nothing to reduce against, so only the useless skip goes.
            Assert.That(((IHasComboInformation)first).ComboOffset, Is.EqualTo(2));
            Assert.That(((IHasComboInformation)noCombo).ComboOffset, Is.EqualTo(0));
        }
    }
}
