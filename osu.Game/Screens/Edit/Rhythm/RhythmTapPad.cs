// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Input.Events;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Overlays;
using osu.Game.Rulesets.Edit;
using osuTK;
using osuTK.Input;

namespace osu.Game.Screens.Edit.Rhythm
{
    /// <summary>
    /// A pad which records clicks and holds at the current playback time, so rhythm can be mapped out by tapping along with the music.
    /// </summary>
    public partial class RhythmTapPad : CompositeDrawable
    {
        /// <summary>
        /// Keyboard keys which can be used for tapping, in addition to the mouse.
        /// </summary>
        public static readonly Key[] TAP_KEYS = { Key.A, Key.S };

        /// <summary>
        /// Presses held for at least this fraction of a beat become holds rather than clicks.
        /// </summary>
        private const double hold_threshold_beats = 0.5;

        [Resolved]
        private EditorRhythm rhythm { get; set; } = null!;

        [Resolved]
        private EditorClock clock { get; set; } = null!;

        [Resolved]
        private IBeatSnapProvider beatSnapProvider { get; set; } = null!;

        [Resolved]
        private OverlayColourProvider colourProvider { get; set; } = null!;

        private readonly Dictionary<object, double> activePresses = new Dictionary<object, double>();

        private Box background = null!;
        private Box flash = null!;

        [BackgroundDependencyLoader]
        private void load()
        {
            Masking = true;
            CornerRadius = 10;

            InternalChildren = new Drawable[]
            {
                background = new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = colourProvider.Background4,
                },
                flash = new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = colourProvider.Colour1,
                    Alpha = 0,
                },
                new FillFlowContainer
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    AutoSizeAxes = Axes.Both,
                    Direction = FillDirection.Vertical,
                    Spacing = new Vector2(0, 8),
                    Children = new Drawable[]
                    {
                        new OsuSpriteText
                        {
                            Anchor = Anchor.TopCentre,
                            Origin = Anchor.TopCentre,
                            Text = "tap along",
                            Font = OsuFont.GetFont(size: 28, weight: FontWeight.Light),
                            Colour = colourProvider.Content1,
                        },
                        new OsuSpriteText
                        {
                            Anchor = Anchor.TopCentre,
                            Origin = Anchor.TopCentre,
                            Text = "click this pad, or press A / S, while the song plays",
                            Font = OsuFont.GetFont(size: 14),
                            Colour = colourProvider.Content2,
                        },
                        new OsuSpriteText
                        {
                            Anchor = Anchor.TopCentre,
                            Origin = Anchor.TopCentre,
                            Text = "short presses become clicks, long presses become holds. everything snaps to the beat divisor.",
                            Font = OsuFont.GetFont(size: 12),
                            Colour = colourProvider.Light3,
                        },
                    }
                },
            };
        }

        protected override bool OnMouseDown(MouseDownEvent e)
        {
            if (e.Button != MouseButton.Left)
                return false;

            BeginPress(MouseButton.Left);
            return true;
        }

        protected override void OnMouseUp(MouseUpEvent e)
        {
            if (e.Button == MouseButton.Left)
                EndPress(MouseButton.Left);

            base.OnMouseUp(e);
        }

        protected override bool OnHover(HoverEvent e)
        {
            background.FadeColour(colourProvider.Background3, 200, Easing.OutQuint);
            return base.OnHover(e);
        }

        protected override void OnHoverLost(HoverLostEvent e)
        {
            background.FadeColour(colourProvider.Background4, 200, Easing.OutQuint);
            base.OnHoverLost(e);
        }

        /// <summary>
        /// Begins a press from the given source (a key or mouse button) at the current time.
        /// </summary>
        public void BeginPress(object source)
        {
            if (activePresses.ContainsKey(source))
                return;

            activePresses[source] = clock.CurrentTime;
            flash.FadeTo(0.25f, 50, Easing.OutQuint);
        }

        /// <summary>
        /// Ends a press from the given source, recording a click or hold.
        /// </summary>
        public void EndPress(object source)
        {
            if (!activePresses.Remove(source, out double startTime))
                return;

            if (activePresses.Count == 0)
                flash.FadeOut(300, Easing.OutQuint);

            double endTime = clock.CurrentTime;

            double snappedStart = beatSnapProvider.SnapTime(startTime);
            double snappedEnd = beatSnapProvider.SnapTime(endTime);
            double beatLength = rhythm.Beatmap.ControlPointInfo.TimingPointAt(startTime).BeatLength;

            var rhythmBeatmap = rhythm.Beatmap;

            if (endTime - startTime >= beatLength * hold_threshold_beats && snappedEnd > snappedStart)
                RhythmPlacement.AddHold(rhythmBeatmap, snappedStart, snappedEnd - snappedStart);
            else
                RhythmPlacement.AddClick(rhythmBeatmap, snappedStart);
        }

        /// <summary>
        /// Ends all presses, e.g. when the screen is hidden.
        /// </summary>
        public void EndAllPresses()
        {
            foreach (object source in new List<object>(activePresses.Keys))
                EndPress(source);
        }
    }
}
