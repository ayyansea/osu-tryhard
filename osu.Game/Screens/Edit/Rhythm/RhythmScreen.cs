// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Extensions.ObjectExtensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Bindings;
using osu.Framework.Input.Events;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterface;
using osu.Game.Input.Bindings;
using osu.Game.Overlays;
using osu.Game.Rulesets.Edit;
using osu.Game.Screens.Edit.Components.RadioButtons;
using osuTK;
using osuTK.Input;

namespace osu.Game.Screens.Edit.Rhythm
{
    /// <summary>
    /// An editor screen for laying out a non-positional rhythm blueprint of clicks and holds.
    /// </summary>
    public partial class RhythmScreen : EditorScreenWithTimeline, IKeyBindingHandler<GlobalAction>
    {
        [Resolved]
        private EditorClock clock { get; set; } = null!;

        [Resolved]
        private IBeatSnapProvider beatSnapProvider { get; set; } = null!;

        public readonly Bindable<RhythmTool> Tool = new Bindable<RhythmTool>();

        private EditorBeatmap rhythmBeatmap => rhythm.Beatmap;

        private RhythmTapPad tapPad = null!;

        private EditorRadioButton selectButton = null!;
        private EditorRadioButton clickButton = null!;
        private EditorRadioButton holdButton = null!;

        private readonly EditorRhythm rhythm;

        public RhythmScreen(EditorRhythm rhythm)
            : base(EditorScreenMode.Rhythm)
        {
            this.rhythm = rhythm;
        }

        protected override IReadOnlyDependencyContainer CreateChildDependencies(IReadOnlyDependencyContainer parent)
        {
            var dependencies = new DependencyContainer(base.CreateChildDependencies(parent));

            // Everything within this screen (including the timeline) edits the rhythm blueprint rather than the actual beatmap.
            dependencies.CacheAs(rhythm.Beatmap);
            dependencies.CacheAs<IEditorChangeHandler>(rhythm.ChangeHandler);

            return dependencies;
        }

        public override EditorChangeHandler ChangeHandler => rhythm.ChangeHandler;

        protected override Drawable CreateTimelineContent() => new RhythmTimelineBlueprintContainer
        {
            Tool = { BindTarget = Tool },
        };

        protected override Drawable CreateMainContent() => new GridContainer
        {
            RelativeSizeAxes = Axes.Both,
            ColumnDimensions = new[]
            {
                new Dimension(GridSizeMode.Absolute, 200),
                new Dimension(),
            },
            Content = new[]
            {
                new Drawable[]
                {
                    new RhythmToolbox(this),
                    new Container
                    {
                        RelativeSizeAxes = Axes.Both,
                        Padding = new MarginPadding(40),
                        Child = tapPad = new RhythmTapPad { RelativeSizeAxes = Axes.Both },
                    },
                }
            }
        };

        protected override void LoadComplete()
        {
            base.LoadComplete();

            rhythmBeatmap.SelectedHitObjects.BindCollectionChanged((_, _) => updateClipboardActionAvailability());
            rhythm.Clipboard.BindValueChanged(_ => updateClipboardActionAvailability(), true);
        }

        protected override void PopOut()
        {
            base.PopOut();

            if (tapPad.IsNotNull())
                tapPad.EndAllPresses();
        }

        public bool OnPressed(KeyBindingPressEvent<GlobalAction> e)
        {
            if (e.Repeat || e.Action != GlobalAction.EditorSelectTool)
                return false;

            Tool.Value = RhythmTool.Select;
            return true;
        }

        public void OnReleased(KeyBindingReleaseEvent<GlobalAction> e)
        {
        }

        protected override bool OnKeyDown(KeyDownEvent e)
        {
            if (e.Repeat || e.ControlPressed || e.AltPressed || e.SuperPressed || e.ShiftPressed)
                return false;

            switch (e.Key)
            {
                case Key.Number2:
                    Tool.Value = RhythmTool.Click;
                    return true;

                case Key.Number3:
                    Tool.Value = RhythmTool.Hold;
                    return true;
            }

            if (RhythmTapPad.TAP_KEYS.Contains(e.Key) && tapPad.IsNotNull())
            {
                tapPad.BeginPress(e.Key);
                return true;
            }

            return false;
        }

        protected override void OnKeyUp(KeyUpEvent e)
        {
            if (RhythmTapPad.TAP_KEYS.Contains(e.Key) && tapPad.IsNotNull())
                tapPad.EndPress(e.Key);

            base.OnKeyUp(e);
        }

        #region Clipboard operations

        public override void Cut()
        {
            if (!CanCut.Value)
                return;

            Copy();
            rhythmBeatmap.RemoveRange(rhythmBeatmap.SelectedHitObjects.ToArray());
        }

        public override void Copy()
        {
            if (!CanCopy.Value)
                return;

            rhythm.Clipboard.Value = RhythmSerializer.Write(rhythmBeatmap.SelectedHitObjects);
        }

        public override void Paste()
        {
            if (!CanPaste.Value)
                return;

            var objects = RhythmSerializer.Read(rhythm.Clipboard.Value!);

            if (objects.Count == 0)
                return;

            double timeOffset = beatSnapProvider.SnapTime(clock.CurrentTime) - objects.Min(o => o.StartTime);

            foreach (var h in objects)
                h.StartTime += timeOffset;

            rhythmBeatmap.BeginChange();

            rhythmBeatmap.SelectedHitObjects.Clear();
            rhythmBeatmap.AddRange(objects);
            rhythmBeatmap.SelectedHitObjects.AddRange(objects);

            rhythmBeatmap.EndChange();
        }

        private void updateClipboardActionAvailability()
        {
            CanCut.Value = CanCopy.Value = rhythmBeatmap.SelectedHitObjects.Any();
            CanPaste.Value = !string.IsNullOrEmpty(rhythm.Clipboard.Value);
        }

        #endregion

        private partial class RhythmToolbox : CompositeDrawable
        {
            private readonly RhythmScreen screen;

            public RhythmToolbox(RhythmScreen screen)
            {
                this.screen = screen;
                RelativeSizeAxes = Axes.Both;
            }

            [BackgroundDependencyLoader]
            private void load(OverlayColourProvider colourProvider)
            {
                EditorRadioButtonCollection tools;
                OsuSpriteText countText;

                InternalChildren = new Drawable[]
                {
                    new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = colourProvider.Background5,
                    },
                    new OsuScrollContainer
                    {
                        RelativeSizeAxes = Axes.Both,
                        Child = new FillFlowContainer
                        {
                            RelativeSizeAxes = Axes.X,
                            AutoSizeAxes = Axes.Y,
                            Direction = FillDirection.Vertical,
                            Children = new Drawable[]
                            {
                                new EditorToolboxGroup("tools")
                                {
                                    Child = tools = new EditorRadioButtonCollection { RelativeSizeAxes = Axes.X }
                                },
                                new EditorToolboxGroup("rhythm")
                                {
                                    Child = new FillFlowContainer
                                    {
                                        RelativeSizeAxes = Axes.X,
                                        AutoSizeAxes = Axes.Y,
                                        Direction = FillDirection.Vertical,
                                        Spacing = new Vector2(0, 5),
                                        Children = new Drawable[]
                                        {
                                            countText = new OsuSpriteText
                                            {
                                                Font = OsuFont.GetFont(size: 14, weight: FontWeight.SemiBold),
                                            },
                                            new OsuTextFlowContainer(s => s.Font = OsuFont.GetFont(size: 12))
                                            {
                                                RelativeSizeAxes = Axes.X,
                                                AutoSizeAxes = Axes.Y,
                                                Colour = colourProvider.Content2,
                                                Text = "a blueprint of the map's rhythm. it is saved with the beatmap, but never ends up in the actual map.",
                                            },
                                        }
                                    }
                                },
                            }
                        }
                    },
                };

                tools.AddButton(screen.selectButton = new EditorRadioButton("Select", () => screen.Tool.Value = RhythmTool.Select,
                    () => new SpriteIcon { Icon = FontAwesome.Solid.MousePointer })
                {
                    Hotkey = new Hotkey(GlobalAction.EditorSelectTool),
                });

                tools.AddButton(screen.clickButton = new EditorRadioButton("Click", () => screen.Tool.Value = RhythmTool.Click,
                    () => new SpriteIcon { Icon = FontAwesome.Regular.Circle })
                {
                    Hotkey = new Hotkey(new KeyCombination(InputKey.Number2)),
                });

                tools.AddButton(screen.holdButton = new EditorRadioButton("Hold", () => screen.Tool.Value = RhythmTool.Hold,
                    () => new SpriteIcon { Icon = FontAwesome.Solid.GripLines })
                {
                    Hotkey = new Hotkey(new KeyCombination(InputKey.Number3)),
                });

                screen.Tool.BindValueChanged(tool =>
                {
                    switch (tool.NewValue)
                    {
                        case RhythmTool.Select:
                            screen.selectButton.Select();
                            break;

                        case RhythmTool.Click:
                            screen.clickButton.Select();
                            break;

                        case RhythmTool.Hold:
                            screen.holdButton.Select();
                            break;
                    }
                }, true);

                var rhythmBeatmap = screen.rhythmBeatmap;

                void updateCount()
                {
                    int holds = rhythmBeatmap.HitObjects.Count(h => h is RhythmHold);
                    int clicks = rhythmBeatmap.HitObjects.Count - holds;
                    countText.Text = $"{clicks} clicks, {holds} holds";
                }

                rhythmBeatmap.HitObjectAdded += _ => Scheduler.AddOnce(updateCount);
                rhythmBeatmap.HitObjectRemoved += _ => Scheduler.AddOnce(updateCount);
                updateCount();
            }
        }
    }
}
