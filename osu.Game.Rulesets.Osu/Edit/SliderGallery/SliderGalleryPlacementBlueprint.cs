// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Input.Bindings;
using osu.Framework.Input.Events;
using osu.Game.Configuration;
using osu.Game.Input.Bindings;
using osu.Game.Rulesets.Edit;
using osu.Game.Rulesets.Osu.Edit.Blueprints.HitCircles.Components;
using osu.Game.Rulesets.Osu.Edit.Blueprints.Sliders.Components;
using osu.Game.Rulesets.Osu.Objects;
using osu.Game.Screens.Edit;
using osuTK;
using osuTK.Input;

namespace osu.Game.Rulesets.Osu.Edit.SliderGallery
{
    /// <summary>
    /// Places the session's active shape: the head follows the cursor (snapped like a circle), the duration is fitted at the placement time.
    /// </summary>
    public partial class SliderGalleryPlacementBlueprint : HitObjectPlacementBlueprint
    {
        public new Slider HitObject => (Slider)base.HitObject;

        private readonly SliderGallerySession session;

        private readonly IBindable<SliderShape?> activeShape;
        private readonly IBindable<SliderFitMode> fitMode;
        private readonly IBindable<SliderDurationSource> durationSource;
        private readonly IBindable<double> beats;
        private readonly IBindable<SliderShapeTransform> transform;

        private SliderBodyPiece bodyPiece = null!;
        private HitCirclePiece headCirclePiece = null!;
        private HitCirclePiece tailCirclePiece = null!;

        [Resolved]
        private OsuHitObjectComposer? composer { get; set; }

        [Resolved]
        private EditorClock? editorClock { get; set; }

        [Resolved]
        private EditorBeatmap editorBeatmap { get; set; } = null!;

        [Resolved]
        private OsuSliderVelocityToolboxGroup? sliderVelocityToolbox { get; set; }

        private Bindable<bool> limitedDistanceSnap = null!;

        private IBindable<double>? sliderVelocity;

        private double? fittedStartTime;

        public SliderGalleryPlacementBlueprint(SliderGallerySession session)
            : base(new Slider())
        {
            this.session = session;

            activeShape = session.ActiveShape.GetBoundCopy();
            fitMode = session.FitMode.GetBoundCopy();
            durationSource = session.DurationSource.GetBoundCopy();
            beats = session.Beats.GetBoundCopy();
            transform = session.Transform.GetBoundCopy();
        }

        [BackgroundDependencyLoader]
        private void load(OsuConfigManager config)
        {
            Children = new Drawable[]
            {
                bodyPiece = new SliderBodyPiece(),
                headCirclePiece = new HitCirclePiece(),
                tailCirclePiece = new HitCirclePiece(),
                new FlipHandler(this),
            };

            limitedDistanceSnap = config.GetBindable<bool>(OsuSetting.EditorLimitedDistanceSnap);
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            activeShape.BindValueChanged(_ => refit());
            fitMode.BindValueChanged(_ => refit());
            durationSource.BindValueChanged(_ => refit());
            beats.BindValueChanged(_ => refit());
            transform.BindValueChanged(_ => refit());
            sliderVelocity = sliderVelocityToolbox?.SliderVelocity.GetBoundCopy();
            sliderVelocity?.BindValueChanged(_ => refit());

            BeginPlacement();
            refit();
        }

        protected override void Update()
        {
            base.Update();

            bodyPiece.UpdateFrom(HitObject);
            headCirclePiece.UpdateFrom(HitObject.HeadCircle);
            tailCirclePiece.UpdateFrom(HitObject.TailCircle);
        }

        public override SnapResult UpdateTimeAndPosition(Vector2 screenSpacePosition, double fallbackTime)
        {
            var result = composer?.TrySnapToNearbyObjects(screenSpacePosition, fallbackTime);
            result ??= composer?.TrySnapToDistanceGrid(screenSpacePosition, limitedDistanceSnap.Value && editorClock != null ? editorClock.CurrentTime : null);
            if (composer?.TrySnapToPositionGrid(result?.ScreenSpacePosition ?? screenSpacePosition, result?.Time ?? fallbackTime) is SnapResult gridSnapResult)
                result = gridSnapResult;
            result ??= new SnapResult(screenSpacePosition, fallbackTime);

            base.UpdateTimeAndPosition(result.ScreenSpacePosition, result.Time ?? fallbackTime);
            HitObject.Position = ToLocalSpace(result.ScreenSpacePosition);

            // the fit depends on the timing (and rhythm) at the start time.
            if (fittedStartTime != HitObject.StartTime)
                refit();

            return result;
        }

        protected override bool OnMouseDown(MouseDownEvent e)
        {
            if (e.Button == MouseButton.Left)
            {
                SliderShapeFitting.ResetNodeSamples(HitObject);
                EndPlacement(true);
                return true;
            }

            return base.OnMouseDown(e);
        }

        protected override bool OnKeyDown(KeyDownEvent e)
        {
            if (!e.ControlPressed || e.AltPressed)
                return base.OnKeyDown(e);

            float step = e.ShiftPressed ? 15 : 90;

            switch (e.Key)
            {
                case Key.Comma:
                    rotate(-step);
                    return true;

                case Key.Period:
                    rotate(step);
                    return true;
            }

            return base.OnKeyDown(e);
        }

        private void rotate(float degrees)
        {
            var current = session.Transform.Value;
            session.Transform.Value = current with { Rotation = (current.Rotation + degrees) % 360 };
        }

        private void flip(bool horizontally)
        {
            var current = session.Transform.Value;

            // mirroring is applied before rotating; mirroring after a rotation equals mirroring first and rotating the other way.
            session.Transform.Value = horizontally
                ? current with { FlipX = !current.FlipX, Rotation = -current.Rotation }
                : current with { FlipY = !current.FlipY, Rotation = -current.Rotation };
        }

        private void refit()
        {
            var shape = activeShape.Value;

            if (shape == null)
                return;

            fittedStartTime = HitObject.StartTime;

            HitObject.RepeatCount = shape.RepeatCount;

            SliderShapeFitting.Fit(HitObject, shape,
                session.GetDuration(shape, HitObject.StartTime, editorBeatmap.ControlPointInfo),
                fitMode.Value,
                sliderVelocity?.Value ?? 1,
                transform.Value,
                editorBeatmap.ControlPointInfo,
                editorBeatmap.Difficulty);
        }

        /// <summary>
        /// Takes the flip shortcuts while placing; nothing is selected then, so the selection handler doesn't want them.
        /// </summary>
        private partial class FlipHandler : Component, IKeyBindingHandler<GlobalAction>
        {
            private readonly SliderGalleryPlacementBlueprint blueprint;

            public FlipHandler(SliderGalleryPlacementBlueprint blueprint)
            {
                this.blueprint = blueprint;
            }

            public bool OnPressed(KeyBindingPressEvent<GlobalAction> e)
            {
                if (e.Repeat)
                    return false;

                switch (e.Action)
                {
                    case GlobalAction.EditorFlipHorizontally:
                        blueprint.flip(true);
                        return true;

                    case GlobalAction.EditorFlipVertically:
                        blueprint.flip(false);
                        return true;
                }

                return false;
            }

            public void OnReleased(KeyBindingReleaseEvent<GlobalAction> e)
            {
            }
        }
    }
}
