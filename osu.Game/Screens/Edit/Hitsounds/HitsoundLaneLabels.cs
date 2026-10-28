// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using osu.Game.Audio;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Skinning;
using osuTK;

namespace osu.Game.Screens.Edit.Hitsounds
{
    /// <summary>
    /// Names the visible swimlanes next to the grid. Clicking a label previews its sample, double clicking selects every sound in the lane (ctrl / shift adds to the selection),
    /// and scrolling over the labels scrolls through the sections.
    /// </summary>
    public partial class HitsoundLaneLabels : CompositeDrawable
    {
        public const float WIDTH = 150;

        private readonly HitsoundLayout layout;
        private readonly EditorHitsounds hitsounds;

        [Resolved]
        private EditorBeatmap editorBeatmap { get; set; } = null!;

        private SkinnableSound preview = null!;
        private Container rows = null!;

        public HitsoundLaneLabels(HitsoundLayout layout, EditorHitsounds hitsounds)
        {
            this.layout = layout;
            this.hitsounds = hitsounds;

            Size = new Vector2(WIDTH, HitsoundLayout.HEIGHT);
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            InternalChildren = new Drawable[]
            {
                new EditorSkinProvidingContainer(editorBeatmap) { Child = preview = new SkinnableSound() },
                rows = new Container { RelativeSizeAxes = Axes.Both },
            };

            layout.Changed += rebuild;
            rebuild();
        }

        private void rebuild()
        {
            rows.Clear();

            foreach (var (y, lane) in layout.Rows)
            {
                var captured = lane;

                rows.Add(new LaneLabel(e => hitsounds.SelectLane(captured, e.ControlPressed || e.ShiftPressed))
                {
                    RelativeSizeAxes = Axes.X,
                    Height = HitsoundLayout.ROW_HEIGHT,
                    Y = y,
                    Action = () =>
                    {
                        preview.Samples = new ISampleInfo[] { captured.Create() };
                        preview.Play();
                    },
                    Child = new OsuSpriteText
                    {
                        Anchor = Anchor.CentreRight,
                        Origin = Anchor.CentreRight,
                        X = -8,
                        Text = lane.Label,
                        Font = OsuFont.GetFont(size: 12, weight: FontWeight.SemiBold),
                        Colour = HitsoundLayout.ColourOf(lane),
                    },
                });
            }
        }

        protected override bool OnScroll(ScrollEvent e)
        {
            layout.SectionOffset.Value -= e.ScrollDelta.Y > 0 ? 1 : -1;
            return true;
        }

        private partial class LaneLabel : ClickableContainer
        {
            private readonly System.Action<DoubleClickEvent> onDoubleClick;

            public LaneLabel(System.Action<DoubleClickEvent> onDoubleClick)
            {
                this.onDoubleClick = onDoubleClick;
            }

            protected override bool OnDoubleClick(DoubleClickEvent e)
            {
                onDoubleClick(e);
                return true;
            }
        }

        protected override void Dispose(bool isDisposing)
        {
            base.Dispose(isDisposing);
            layout.Changed -= rebuild;
        }
    }
}
