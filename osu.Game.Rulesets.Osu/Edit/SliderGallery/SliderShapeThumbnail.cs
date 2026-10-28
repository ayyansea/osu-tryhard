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
    /// Draws a shape's path scaled to fit, with a dot on the head.
    /// </summary>
    public partial class SliderShapeThumbnail : CompositeDrawable
    {
        private readonly SliderShape shape;
        private readonly float pathRadius;

        private SmoothPath path = null!;
        private Circle head = null!;

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
                head = new Circle
                {
                    Size = new Vector2(pathRadius * 4),
                    Origin = Anchor.Centre,
                },
            };
        }

        protected override void Update()
        {
            base.Update();

            if (path == null || DrawSize == lastDrawSize || DrawWidth <= 0 || DrawHeight <= 0)
                return;

            lastDrawSize = DrawSize;
            layout();
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

            float margin = pathRadius * 2;
            float scale = Math.Min((DrawWidth - margin * 2) / size.X, (DrawHeight - margin * 2) / size.Y);
            var offset = (DrawSize - size * scale) / 2 - min * scale;

            // the path drawable positions itself at the top left of its vertices (minus its radius), so position it there to keep the vertices where computed.
            var scaled = vertices.Select(v => v * scale + offset).ToList();
            path.Vertices = scaled;
            path.Position = new Vector2(scaled.Min(v => v.X), scaled.Min(v => v.Y)) - new Vector2(pathRadius);
            head.Position = scaled[0];
        }
    }
}
