// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Linq;
using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Lines;
using osu.Framework.Testing;
using osu.Game.Rulesets.Objects.Types;
using osu.Game.Rulesets.Osu.Edit.SliderGallery;
using osu.Game.Tests.Visual;
using osuTK;

namespace osu.Game.Rulesets.Osu.Tests.Editor
{
    public partial class TestSceneSliderShapeThumbnail : OsuTestScene
    {
        private SliderShapeThumbnail thumbnail = null!;

        [Test]
        public void TestHeadMarkerOnPathStart()
        {
            AddStep("create thumbnail", () => Child = thumbnail = new SliderShapeThumbnail(new SliderShape
            {
                ControlPoints = { (Vector2.Zero, PathType.PERFECT_CURVE), (new Vector2(60, -60), null), (new Vector2(120, 0), null) },
                Length = 180,
            })
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Size = new Vector2(170, 110),
            });

            AddUntilStep("path laid out", () => thumbnail.ChildrenOfType<SmoothPath>().SingleOrDefault()?.Vertices.Count > 0);

            AddUntilStep("head marker centred on the path start", () =>
            {
                var path = thumbnail.ChildrenOfType<SmoothPath>().Single();
                var start = path.ToScreenSpace(path.PositionInBoundingBox(path.Vertices[0]));
                var head = thumbnail.ChildrenOfType<CircularContainer>().Single().ScreenSpaceDrawQuad.Centre;
                return Vector2.Distance(start, head);
            }, () => Is.LessThan(0.5f));
        }
    }
}
