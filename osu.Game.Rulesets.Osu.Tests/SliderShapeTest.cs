// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Linq;
using NUnit.Framework;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.ControlPoints;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Objects.Types;
using osu.Game.Rulesets.Osu.Edit.SliderGallery;
using osu.Game.Rulesets.Osu.Objects;
using osuTK;

namespace osu.Game.Rulesets.Osu.Tests
{
    [TestFixture]
    public class SliderShapeTest
    {
        private const double beat_length = 500;

        private ControlPointInfo controlPoints = null!;
        private BeatmapDifficulty difficulty = null!;

        [SetUp]
        public void SetUp()
        {
            controlPoints = new ControlPointInfo();
            controlPoints.Add(0, new TimingControlPoint { BeatLength = beat_length });
            controlPoints.Add(10000, new TimingControlPoint { BeatLength = 250 });
            difficulty = new BeatmapDifficulty();
        }

        private SliderShape createShape(int repeats = 0) => new SliderShape
        {
            Name = "wave",
            Tags = { "curvy", "long" },
            OriginalMap = "Artist - Title (Mapper) [Insane]",
            Favourite = true,
            ControlPoints =
            {
                (Vector2.Zero, PathType.BEZIER),
                (new Vector2(50, -40), null),
                (new Vector2(100, 0), PathType.PERFECT_CURVE),
                (new Vector2(150, 30), null),
                (new Vector2(200, 0), null),
            },
            Length = 250,
            RepeatCount = repeats,
            Beats = 2,
        };

        private Slider createSlider(double startTime, int repeats = 0)
        {
            var slider = new Slider { StartTime = startTime, Position = new Vector2(100, 100), RepeatCount = repeats };
            slider.ApplyDefaults(controlPoints, difficulty);
            return slider;
        }

        [Test]
        public void TestSerializerRoundTrip()
        {
            var shape = createShape(2);
            shape.ControlPoints.Add((new Vector2(250, 10), PathType.BSpline(3)));
            shape.ControlPoints.Add((new Vector2(300, 0), PathType.LINEAR));
            shape.ControlPoints.Add((new Vector2(350, 5), PathType.CATMULL));

            var result = SliderShapeSerializer.Read(SliderShapeSerializer.Write(shape));

            Assert.That(result, Is.Not.Null);
            Assert.That(result!.ID, Is.EqualTo(shape.ID));
            Assert.That(result.Name, Is.EqualTo("wave"));
            Assert.That(result.Tags, Is.EqualTo(new[] { "curvy", "long" }));
            Assert.That(result.OriginalMap, Is.EqualTo("Artist - Title (Mapper) [Insane]"));
            Assert.That(result.Favourite, Is.True);
            Assert.That(result.RepeatCount, Is.EqualTo(2));
            Assert.That(result.Length, Is.EqualTo(250));
            Assert.That(result.Beats, Is.EqualTo(2));
            Assert.That(result.ControlPoints, Is.EqualTo(shape.ControlPoints));
        }

        [Test]
        public void TestSerializerRejectsInvalid()
        {
            Assert.That(SliderShapeSerializer.Read("not a shape"), Is.Null);
            Assert.That(SliderShapeSerializer.Read("osu!tryhard slider v1\nname=x\nlength=100\npoints=0,0"), Is.Null, "a single point isn't a path");
            Assert.That(SliderShapeSerializer.Read("osu!tryhard slider v1\nname=x\npoints=0,0|10,0"), Is.Null, "no length");
        }

        [Test]
        public void TestFromSlider()
        {
            var slider = new Slider
            {
                StartTime = 1000,
                RepeatCount = 1,
                Path = new SliderPath(new[]
                {
                    new PathControlPoint(new Vector2(0, 0), PathType.LINEAR),
                    new PathControlPoint(new Vector2(100, 0)),
                }),
            };
            slider.ApplyDefaults(controlPoints, difficulty);

            var shape = SliderShape.FromSlider(slider, controlPoints, "line");

            Assert.That(shape.ControlPoints.Select(p => p.Position), Is.EqualTo(new[] { Vector2.Zero, new Vector2(100, 0) }));
            Assert.That(shape.ControlPoints[0].Type, Is.EqualTo(PathType.LINEAR));
            Assert.That(shape.Length, Is.EqualTo(100).Within(0.01));
            Assert.That(shape.RepeatCount, Is.EqualTo(1));
            Assert.That(shape.Beats, Is.EqualTo(slider.Duration / beat_length).Within(0.0001));
        }

        [Test]
        public void TestCreatePathTransforms()
        {
            var shape = new SliderShape
            {
                ControlPoints = { (Vector2.Zero, PathType.LINEAR), (new Vector2(100, 20), null) },
                Length = 100,
            };

            assertSecondPoint(shape.CreatePath(), new Vector2(100, 20));
            assertSecondPoint(shape.CreatePath(scale: 2), new Vector2(200, 40));
            // y points down in osu! space, so clockwise on screen takes +x to +y.
            assertSecondPoint(shape.CreatePath(rotation: 90), new Vector2(-20, 100));
            assertSecondPoint(shape.CreatePath(flipX: true), new Vector2(-100, 20));
            assertSecondPoint(shape.CreatePath(flipY: true), new Vector2(100, -20));

            Assert.That(shape.CreatePath(scale: 2).ExpectedDistance.Value, Is.EqualTo(200));

            static void assertSecondPoint(SliderPath path, Vector2 expected)
            {
                Assert.That(path.ControlPoints[1].Position.X, Is.EqualTo(expected.X).Within(0.001));
                Assert.That(path.ControlPoints[1].Position.Y, Is.EqualTo(expected.Y).Within(0.001));
            }
        }

        [TestCase(0, 1)]
        [TestCase(0, 2)]
        [TestCase(2, 1.5)]
        [TestCase(10000, 3)]
        public void TestFitScaleKeepsVelocity(double startTime, double beats)
        {
            var shape = createShape();
            var slider = createSlider(startTime);
            double duration = beats * controlPoints.TimingPointAt(startTime).BeatLength;

            SliderShapeFitting.Fit(slider, shape, duration, SliderFitMode.Scale, 1.2, default, controlPoints, difficulty);

            Assert.That(slider.Duration, Is.EqualTo(duration).Within(0.01));
            Assert.That(slider.SliderVelocityMultiplier, Is.EqualTo(1.2));
            // the shape is scaled uniformly: the ratio of the points stays.
            Assert.That(slider.Path.ControlPoints[2].Position.X / slider.Path.ControlPoints[4].Position.X, Is.EqualTo(0.5).Within(0.001));
            Assert.That(slider.Path.Distance, Is.EqualTo(slider.Velocity * duration).Within(0.01));
        }

        [Test]
        public void TestFitScaleWithRepeats()
        {
            var shape = createShape();
            var slider = createSlider(0, repeats: 2);

            SliderShapeFitting.Fit(slider, shape, 1500, SliderFitMode.Scale, 1, default, controlPoints, difficulty);

            Assert.That(slider.Duration, Is.EqualTo(1500).Within(0.01));
            Assert.That(slider.SpanDuration, Is.EqualTo(500).Within(0.01));
        }

        [Test]
        public void TestFitKeepSizeChangesVelocity()
        {
            var shape = createShape();
            var slider = createSlider(0);

            SliderShapeFitting.Fit(slider, shape, 1000, SliderFitMode.KeepSize, 1, default, controlPoints, difficulty);

            Assert.That(slider.Duration, Is.EqualTo(1000).Within(0.01));
            Assert.That(slider.Path.Distance, Is.EqualTo(shape.Length).Within(0.01));
            Assert.That(slider.SliderVelocityMultiplier, Is.Not.EqualTo(1));
        }

        [Test]
        public void TestFitKeepSizeOutOfVelocityRange()
        {
            var shape = createShape();
            var slider = createSlider(0);

            // 250 px in 5 ms would need a velocity far past the maximum.
            SliderShapeFitting.Fit(slider, shape, 5, SliderFitMode.KeepSize, 1, default, controlPoints, difficulty);

            Assert.That(slider.SliderVelocityMultiplier, Is.EqualTo(SliderShapeFitting.MAX_VELOCITY));
            Assert.That(slider.Duration, Is.EqualTo(5).Within(0.01), "the path is trimmed to keep the duration");
        }

        [Test]
        public void TestFitAppliesTransform()
        {
            var shape = createShape();
            var slider = createSlider(0);

            SliderShapeFitting.Fit(slider, shape, 1000, SliderFitMode.KeepSize, 1, new SliderShapeTransform(90), controlPoints, difficulty);

            var last = slider.Path.ControlPoints.Last().Position;
            Assert.That(last.X, Is.EqualTo(0).Within(0.001));
            Assert.That(last.Y, Is.EqualTo(200).Within(0.001));
        }
    }
}
