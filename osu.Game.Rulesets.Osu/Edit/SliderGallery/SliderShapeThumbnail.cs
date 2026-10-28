// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Lines;
using osu.Framework.Graphics.Shapes;
using osuTK;

namespace osu.Game.Rulesets.Osu.Edit.SliderGallery
{
    /// <summary>
    /// Draws a shape's path scaled to fit, with a ring on the head (like a hit circle) to show which end the slider starts from.
    /// </summary>
    public partial class SliderShapeThumbnail : CompositeDrawable
    {
        private readonly SliderShape shape;
        private readonly float pathRadius;

        private SmoothPath path = null!;
        private CircularContainer head = null!;

        private Vector2 lastDrawSize;

        public SliderShapeThumbnail(SliderShape shape, float pathRadius = 3)
        {
            this.shape = shape;
            this.pathRadius = pathRadius;
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            InternalChildren = new Drawable[]
            {
                path = new SmoothPath
                {
                    PathRadius = pathRadius,
                },
                head = new CircularContainer
                {
                    Size = new Vector2(pathRadius * 5),
                    Origin = Anchor.Centre,
                    Masking = true,
                    BorderThickness = pathRadius,
                    BorderColour = Colour4.White,
                    // the border only draws with something inside the mask.
                    Child = new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Alpha = 0,
                        AlwaysPresent = true,
                    },
                },
            };
        }

        protected override void Update()
        {
            base.Update();

            if (path == null || DrawWidth <= 0 || DrawHeight <= 0)
                return;

            if (DrawSize != lastDrawSize)
            {
                lastDrawSize = DrawSize;
                layout();
            }

            alignPath();
        }

        /// <summary>
        /// Moves the path so its first vertex is drawn where the head is. Measured rather than derived, as the path's
        /// own vertex-to-local mapping isn't something to second-guess (getting it wrong put the head next to the path).
        /// </summary>
        private void alignPath()
        {
            if (path.Vertices.Count == 0)
                return;

            var drawnStart = path.ToSpaceOfOtherDrawable(path.PositionInBoundingBox(path.Vertices[0]), this);
            var offset = head.Position - drawnStart;

            if (offset.LengthSquared > 0.0001f)
                path.Position += offset;
        }

        private void layout()
        {
            var vertices = new List<Vector2>();
            shape.CreatePath().GetPathToProgress(vertices, 0, 1);

            if (vertices.Count < 2)
                return;

            var min = new Vector2(vertices.Min(v => v.X), vertices.Min(v => v.Y));
            var max = new Vector2(vertices.Max(v => v.X), vertices.Max(v => v.Y));
            var size = Vector2.ComponentMax(max - min, Vector2.One);

            // room for the head ring on the edges.
            float margin = pathRadius * 3;
            float scale = Math.Min((DrawWidth - margin * 2) / size.X, (DrawHeight - margin * 2) / size.Y);
            var offset = (DrawSize - size * scale) / 2 - min * scale;

            var scaled = vertices.Select(v => v * scale + offset).ToList();
            path.Vertices = scaled;
            head.Position = scaled[0];
        }
    }
}
