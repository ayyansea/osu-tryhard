// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Globalization;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Input;
using osu.Game.Configuration;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Overlays;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Osu.Objects;
using osuTK;

namespace osu.Game.Rulesets.Osu.Edit.Blueprints.Sliders.Components
{
    /// <summary>
    /// Displays the angle of slider skeleton sections (the straight lines between consecutive control points).
    /// </summary>
    public partial class SliderSectionAngleOverlay : CompositeDrawable
    {
        /// <summary>
        /// Distance between a section and its label, when the label fits next to the section.
        /// </summary>
        private const float label_spacing = 6;

        /// <summary>
        /// Distance between a section and its label, when the section is too short for the label to fit next to it.
        /// </summary>
        private const float callout_distance = 40;

        private readonly Slider slider;
        private readonly SliderSectionAngleMode mode;

        private readonly Bindable<bool> enabled = new Bindable<bool>();

        private InputManager inputManager = null!;

        private readonly List<SectionAngleIndicator> indicators = new List<SectionAngleIndicator>();
        private readonly List<(Vector2 start, Vector2 end)> visibleSections = new List<(Vector2, Vector2)>();

        /// <summary>
        /// Whether sections near the cursor should be displayed. Only applies to <see cref="SliderSectionAngleMode.HoveredSections"/>.
        /// </summary>
        public Func<bool> ShouldDisplayHoveredSections { get; init; } = () => true;

        /// <summary>
        /// The control point which is currently following the cursor, if any. Only applies to <see cref="SliderSectionAngleMode.ActiveSection"/>.
        /// </summary>
        public Func<PathControlPoint?> CursorControlPoint { get; init; } = () => null;

        public SliderSectionAngleOverlay(Slider slider, SliderSectionAngleMode mode)
        {
            this.slider = slider;
            this.mode = mode;

            RelativeSizeAxes = Axes.Both;

            // Always display above control point pieces and the slider body.
            Depth = float.MinValue;
        }

        [BackgroundDependencyLoader]
        private void load(OsuConfigManager config)
        {
            config.BindWith(OsuSetting.EditorShowSliderSectionAngles, enabled);
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();
            inputManager = GetContainingInputManager()!;
        }

        protected override void Update()
        {
            base.Update();

            visibleSections.Clear();

            if (enabled.Value)
                findVisibleSections();

            while (indicators.Count < visibleSections.Count)
            {
                var indicator = new SectionAngleIndicator();
                indicators.Add(indicator);
                AddInternal(indicator);
            }

            Vector2 cursor = ToLocalSpace(inputManager.CurrentState.Mouse.Position);

            for (int i = 0; i < indicators.Count; i++)
            {
                if (i < visibleSections.Count)
                {
                    indicators[i].Show();
                    indicators[i].UpdateSection(visibleSections[i].start, visibleSections[i].end, cursor);
                }
                else
                    indicators[i].Hide();
            }
        }

        private void findVisibleSections()
        {
            var controlPoints = slider.Path.ControlPoints;

            if (controlPoints.Count < 2)
                return;

            Vector2 origin = slider.StackedPosition;

            switch (mode)
            {
                case SliderSectionAngleMode.ActiveSection:
                {
                    var cursorPoint = CursorControlPoint();

                    if (cursorPoint == null || controlPoints[^1] != cursorPoint)
                        return;

                    addSection(origin + controlPoints[^2].Position, origin + cursorPoint.Position);
                    break;
                }

                case SliderSectionAngleMode.HoveredSections:
                {
                    if (!ShouldDisplayHoveredSections())
                        return;

                    Vector2 cursor = ToLocalSpace(inputManager.CurrentState.Mouse.Position);

                    // Consider the cursor to be hovering a section when it is within the body of a linear slider following that section.
                    float hoverDistance = (float)slider.Radius;

                    for (int i = 0; i < controlPoints.Count - 1; i++)
                    {
                        Vector2 start = origin + controlPoints[i].Position;
                        Vector2 end = origin + controlPoints[i + 1].Position;

                        if (distanceToSegment(cursor, start, end) <= hoverDistance)
                            addSection(start, end);
                    }

                    break;
                }
            }
        }

        private void addSection(Vector2 start, Vector2 end)
        {
            // Sections with no length (e.g. "red" anchors made of two control points at the same position) have no angle.
            if (Vector2.DistanceSquared(start, end) < 1)
                return;

            visibleSections.Add((start, end));
        }

        private static float distanceToSegment(Vector2 point, Vector2 start, Vector2 end)
        {
            Vector2 segment = end - start;
            float lengthSquared = segment.LengthSquared;

            if (lengthSquared == 0)
                return Vector2.Distance(point, start);

            float t = Math.Clamp(Vector2.Dot(point - start, segment) / lengthSquared, 0, 1);
            return Vector2.Distance(point, start + t * segment);
        }

        /// <summary>
        /// Returns the angle of the section going from <paramref name="start"/> to <paramref name="end"/>, in degrees.
        /// The angle is measured counter-clockwise from the positive X axis, as displayed on screen, and is in the range [0, 360).
        /// </summary>
        public static float GetSectionAngle(Vector2 start, Vector2 end)
        {
            Vector2 direction = end - start;

            // Playfield Y coordinates point downwards, so flip them to get the angle as it appears on screen.
            float angle = MathHelper.RadiansToDegrees(MathF.Atan2(-direction.Y, direction.X));

            // Normalise to [0, 360). This also turns negative zero (from a section pointing right) into positive zero.
            angle = (angle % 360 + 360) % 360;

            // Avoid displaying 360° for angles which round up to it.
            return MathF.Round(angle, 1) >= 360 ? 0 : angle;
        }

        public static string FormatAngle(float angle) => $"{angle.ToString("0.#", CultureInfo.InvariantCulture)}°";

        public partial class SectionAngleIndicator : CompositeDrawable
        {
            /// <summary>
            /// Whether the label is currently displayed away from the section with a pointer, due to the section being too short.
            /// </summary>
            public bool IsCallout { get; private set; }

            public string Text => text.Text.ToString();

            private readonly Box sectionHighlight;
            private readonly Box pointer;
            private readonly Container label;
            private readonly OsuSpriteText text;

            public SectionAngleIndicator()
            {
                RelativeSizeAxes = Axes.Both;
                AlwaysPresent = false;

                InternalChildren = new Drawable[]
                {
                    sectionHighlight = new Box
                    {
                        Origin = Anchor.CentreLeft,
                        Height = 2,
                        Colour = CatppuccinMochaColourProvider.MAUVE,
                        Alpha = 0.6f,
                        EdgeSmoothness = new Vector2(1),
                    },
                    pointer = new Box
                    {
                        Origin = Anchor.CentreLeft,
                        Height = 1,
                        Colour = CatppuccinMochaColourProvider.SUBTEXT0,
                        Alpha = 0.45f,
                        EdgeSmoothness = new Vector2(1),
                    },
                    label = new Container
                    {
                        Origin = Anchor.Centre,
                        AutoSizeAxes = Axes.Both,
                        Masking = true,
                        CornerRadius = 3,
                        Children = new Drawable[]
                        {
                            new Box
                            {
                                RelativeSizeAxes = Axes.Both,
                                Colour = CatppuccinMochaColourProvider.MANTLE,
                                Alpha = 0.8f,
                            },
                            text = new OsuSpriteText
                            {
                                Font = OsuFont.Default.With(size: 11, weight: FontWeight.SemiBold),
                                Colour = CatppuccinMochaColourProvider.TEXT,
                                Padding = new MarginPadding { Horizontal = 4, Vertical = 1 },
                            },
                        }
                    },
                };
            }

            public void UpdateSection(Vector2 start, Vector2 end, Vector2 cursor)
            {
                Vector2 section = end - start;
                float length = section.Length;
                Vector2 direction = section / length;
                Vector2 midpoint = (start + end) / 2;

                text.Text = FormatAngle(GetSectionAngle(start, end));

                sectionHighlight.Position = start;
                sectionHighlight.Width = length;
                sectionHighlight.Rotation = MathHelper.RadiansToDegrees(MathF.Atan2(section.Y, section.X));

                // Place the label on the side of the section facing away from the cursor, so that it doesn't get in the way.
                Vector2 normal = new Vector2(-direction.Y, direction.X);
                if (Vector2.Dot(cursor - midpoint, normal) > 0)
                    normal = -normal;

                Vector2 labelSize = label.DrawSize;

                // The distance from the label's centre to its edge, in the direction of the normal.
                float labelExtent = Math.Abs(normal.X) * labelSize.X / 2 + Math.Abs(normal.Y) * labelSize.Y / 2;

                // The label fits next to the section if the section is longer than the label's extent along the section.
                float labelExtentAlongSection = Math.Abs(direction.X) * labelSize.X + Math.Abs(direction.Y) * labelSize.Y;
                IsCallout = length < labelExtentAlongSection + label_spacing * 2;

                if (!IsCallout)
                {
                    label.Position = midpoint + normal * (label_spacing + labelExtent);
                    pointer.Hide();
                }
                else
                {
                    // Like annotations of small countries on maps: move the label away from the section, and point to it.
                    Vector2 labelPosition = midpoint + normal * (callout_distance + labelExtent);
                    Vector2 pointerStart = midpoint + normal * callout_distance;
                    Vector2 pointerVector = midpoint - pointerStart;

                    label.Position = labelPosition;

                    pointer.Show();
                    pointer.Position = pointerStart;
                    pointer.Width = pointerVector.Length;
                    pointer.Rotation = MathHelper.RadiansToDegrees(MathF.Atan2(pointerVector.Y, pointerVector.X));
                }
            }
        }
    }

    public enum SliderSectionAngleMode
    {
        /// <summary>
        /// Display the angles of sections near the cursor.
        /// </summary>
        HoveredSections,

        /// <summary>
        /// Display the angle of the section currently being placed (from the last placed control point to the cursor).
        /// </summary>
        ActiveSection,
    }
}
