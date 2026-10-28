// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Specialized;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using osu.Game.Extensions;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterface;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Overlays;
using osu.Game.Overlays.Dialog;
using osu.Game.Rulesets.Osu.Objects;
using osu.Game.Screens.Edit;
using osu.Game.Screens.Edit.Components;
using osuTK;

namespace osu.Game.Rulesets.Osu.Edit.SliderGallery
{
    /// <summary>
    /// Browses, saves and manages the gallery's shapes, sets how they're fitted, and starts placing one.
    /// </summary>
    public partial class SliderGalleryOverlay : EditorToolOverlay
    {
        protected override string Title => "Slider gallery";

        private readonly SliderGallerySession session;

        private readonly BindableList<SliderShape> shapes = new BindableList<SliderShape>();
        private readonly Bindable<SliderShape?> selected = new Bindable<SliderShape?>();

        [Resolved]
        private EditorBeatmap editorBeatmap { get; set; } = null!;

        [Resolved]
        private OverlayColourProvider colourProvider { get; set; } = null!;

        [Resolved]
        private IDialogOverlay? dialogOverlay { get; set; }

        [Resolved]
        private OsuColour colours { get; set; } = null!;

        private SearchTextBox search = null!;
        private FillFlowContainer<ShapeCard> cards = null!;
        private OsuSpriteText emptyText = null!;
        private Container detail = null!;
        private RoundedButton saveButton = null!;
        private OsuSpriteText status = null!;

        public SliderGalleryOverlay(SliderGallerySession session)
        {
            this.session = session;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            shapes.BindTo(session.Store.Shapes);

            Child = new GridContainer
            {
                RelativeSizeAxes = Axes.Both,
                RowDimensions = new[]
                {
                    new Dimension(GridSizeMode.AutoSize),
                    new Dimension(GridSizeMode.Absolute, PADDING),
                    new Dimension(),
                },
                ColumnDimensions = new[]
                {
                    new Dimension(),
                    new Dimension(GridSizeMode.Absolute, PADDING),
                    new Dimension(GridSizeMode.Absolute, 360),
                },
                Content = new[]
                {
                    new Drawable[]
                    {
                        new FillFlowContainer
                        {
                            RelativeSizeAxes = Axes.X,
                            AutoSizeAxes = Axes.Y,
                            Direction = FillDirection.Horizontal,
                            Spacing = new Vector2(10),
                            Children = new Drawable[]
                            {
                                search = new SearchTextBox
                                {
                                    Width = 300,
                                    PlaceholderText = "Search names and tags",
                                    HoldFocus = false,
                                },
                                saveButton = new RoundedButton
                                {
                                    Width = 220,
                                    Text = "Save selected sliders",
                                    Action = SaveSelection,
                                },
                                new RoundedButton
                                {
                                    Width = 120,
                                    Text = "Open folder",
                                    Action = () => session.Store.PresentExternally(),
                                },
                                new RoundedButton
                                {
                                    Width = 100,
                                    Text = "Reload",
                                    Action = () => session.Store.Reload(),
                                },
                                status = new OsuSpriteText
                                {
                                    Anchor = Anchor.CentreLeft,
                                    Origin = Anchor.CentreLeft,
                                    Font = OsuFont.Default.With(size: 14),
                                },
                            }
                        },
                        Empty(),
                        Empty(),
                    },
                    new Drawable[] { Empty(), Empty(), Empty() },
                    new Drawable[]
                    {
                        new Container
                        {
                            RelativeSizeAxes = Axes.Both,
                            Masking = true,
                            CornerRadius = 5,
                            Children = new Drawable[]
                            {
                                new Box
                                {
                                    RelativeSizeAxes = Axes.Both,
                                    Colour = colourProvider.Background6,
                                },
                                new OsuScrollContainer
                                {
                                    RelativeSizeAxes = Axes.Both,
                                    Child = cards = new FillFlowContainer<ShapeCard>
                                    {
                                        RelativeSizeAxes = Axes.X,
                                        AutoSizeAxes = Axes.Y,
                                        Padding = new MarginPadding(10),
                                        Spacing = new Vector2(10),
                                    }
                                },
                                emptyText = new OsuSpriteText
                                {
                                    Anchor = Anchor.Centre,
                                    Origin = Anchor.Centre,
                                    Text = "No shapes yet: select sliders in compose and press \"Save selected sliders\".",
                                    Font = OsuFont.Default.With(size: 16),
                                },
                            }
                        },
                        Empty(),
                        new OsuScrollContainer
                        {
                            RelativeSizeAxes = Axes.Both,
                            Child = detail = new Container
                            {
                                RelativeSizeAxes = Axes.X,
                                AutoSizeAxes = Axes.Y,
                            }
                        },
                    },
                }
            };
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            emptyText.Colour = colourProvider.Content2;
            status.Colour = colourProvider.Content2;

            shapes.BindCollectionChanged(onShapesChanged, true);
            search.Current.BindValueChanged(_ => updateFilter());
            selected.BindValueChanged(_ => updateDetail(), true);

            editorBeatmap.SelectedHitObjects.BindCollectionChanged((_, _) => updateSelectionState(), true);
        }

        protected override void PopIn()
        {
            base.PopIn();

            status.Text = string.Empty;
            selected.Value ??= session.ActiveShape.Value;
            updateSelectionState();
        }

        private void onShapesChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            cards.Clear();

            foreach (var shape in shapes.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase))
            {
                cards.Add(new ShapeCard(shape)
                {
                    Selected = () => selected.Value = shape,
                    Activated = () => place(shape),
                });
            }

            if (selected.Value != null && !shapes.Contains(selected.Value))
                selected.Value = null;

            updateFilter();
            updateCardSelection();
        }

        private void updateFilter()
        {
            string[] terms = search.Current.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            foreach (var card in cards)
            {
                string haystack = $"{card.Shape.Name} {string.Join(' ', card.Shape.Tags)}";
                card.Alpha = terms.All(t => haystack.Contains(t, StringComparison.OrdinalIgnoreCase)) ? 1 : 0;
            }

            emptyText.Alpha = shapes.Count == 0 ? 1 : 0;
        }

        private void updateCardSelection()
        {
            foreach (var card in cards)
                card.IsSelected = card.Shape == selected.Value;
        }

        private Slider[] selectedSliders => editorBeatmap.SelectedHitObjects.OfType<Slider>().ToArray();

        private void updateSelectionState()
        {
            int count = selectedSliders.Length;

            saveButton.Enabled.Value = count > 0;
            saveButton.Text = count > 1 ? $"Save {count} selected sliders" : "Save selected slider";

            updateDetail();
        }

        /// <summary>
        /// Saves the selected sliders as new shapes.
        /// </summary>
        public void SaveSelection()
        {
            var sliders = selectedSliders;
            SliderShape? last = null;

            foreach (var slider in sliders)
            {
                string title = editorBeatmap.BeatmapInfo.Metadata.Title;
                last = SliderShape.FromSlider(slider, editorBeatmap.ControlPointInfo, $"{title} {slider.StartTime.ToEditorFormattedString()}".Trim());
                session.Store.Save(last);
            }

            if (last != null)
                selected.Value = last;

            status.Text = $"Saved {sliders.Length} shape{(sliders.Length == 1 ? "" : "s")}";
        }

        private void place(SliderShape shape)
        {
            selected.Value = shape;
            session.PlaceRequested?.Invoke(shape);
        }

        private void applyToSelection(SliderShape shape)
        {
            var sliders = selectedSliders;

            editorBeatmap.BeginChange();

            foreach (var slider in sliders)
            {
                // keeps the rhythm of the slider: its start, duration and repeats. Only the path (and velocity, when keeping the size) changes.
                double duration = slider.Duration;

                SliderShapeFitting.Fit(slider, shape, duration, session.FitMode.Value, slider.SliderVelocityMultiplier, session.Transform.Value,
                    editorBeatmap.ControlPointInfo, editorBeatmap.Difficulty);

                editorBeatmap.Update(slider);
            }

            editorBeatmap.EndChange();

            status.Text = $"Applied to {sliders.Length} slider{(sliders.Length == 1 ? "" : "s")}";
        }

        private void delete(SliderShape shape)
        {
            if (dialogOverlay == null)
            {
                session.Store.Delete(shape);
                return;
            }

            dialogOverlay.Push(new DeleteShapeDialog(shape.Name, () => session.Store.Delete(shape)));
        }

        private void updateDetail()
        {
            if (detail == null)
                return;

            updateCardSelection();

            var shape = selected.Value;

            if (shape == null)
            {
                detail.Child = new FillFlowContainer
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Direction = FillDirection.Vertical,
                    Spacing = new Vector2(5),
                    Children = createFitControls().Prepend(new OsuTextFlowContainer(t => t.Font = OsuFont.Default.With(size: 14))
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        Colour = colourProvider.Content2,
                        Text = "Pick a shape. Double click places it.",
                        Margin = new MarginPadding { Bottom = 10 },
                    }).ToArray(),
                };
                return;
            }

            FormTextBox name;
            FormTextBox tags;

            int sliderCount = selectedSliders.Length;

            detail.Child = new FillFlowContainer
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Direction = FillDirection.Vertical,
                Spacing = new Vector2(5),
                Children = new Drawable[]
                {
                    new Container
                    {
                        RelativeSizeAxes = Axes.X,
                        Height = 200,
                        Masking = true,
                        CornerRadius = 5,
                        Children = new Drawable[]
                        {
                            new Box
                            {
                                RelativeSizeAxes = Axes.Both,
                                Colour = colourProvider.Background6,
                            },
                            new SliderShapeThumbnail(shape, 5)
                            {
                                RelativeSizeAxes = Axes.Both,
                                Colour = colourProvider.Highlight1,
                            },
                        }
                    },
                    new OsuSpriteText
                    {
                        Text = describe(shape),
                        Font = OsuFont.Default.With(size: 14),
                        Colour = colourProvider.Content2,
                        Margin = new MarginPadding { Bottom = 5 },
                    },
                    name = new FormTextBox
                    {
                        Caption = "Name",
                        Current = { Value = shape.Name },
                    },
                    tags = new FormTextBox
                    {
                        Caption = "Tags",
                        HintText = "Separated by commas.",
                        Current = { Value = string.Join(", ", shape.Tags) },
                    },
                    new RoundedButton
                    {
                        RelativeSizeAxes = Axes.X,
                        Text = "Place",
                        Action = () => place(shape),
                        Margin = new MarginPadding { Top = 10 },
                    },
                    new RoundedButton
                    {
                        RelativeSizeAxes = Axes.X,
                        Text = sliderCount > 1 ? $"Apply to {sliderCount} selected sliders" : "Apply to selected slider",
                        Enabled = { Value = sliderCount > 0 },
                        Action = () => applyToSelection(shape),
                    },
                }
            };

            var flow = (FillFlowContainer)detail.Child;
            flow.AddRange(createFitControls());
            flow.Add(new RoundedButton
            {
                RelativeSizeAxes = Axes.X,
                Text = "Delete shape",
                BackgroundColour = colours.Red3,
                Action = () => delete(shape),
                Margin = new MarginPadding { Top = 10 },
            });

            name.OnCommit += (_, _) =>
            {
                shape.Name = name.Current.Value.Trim();
                session.Store.Save(shape);
                onShapesChanged(null, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
            };

            tags.OnCommit += (_, _) =>
            {
                shape.Tags = tags.Current.Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
                session.Store.Save(shape);
                updateFilter();
            };
        }

        private Drawable[] createFitControls()
        {
            var beatsSlider = new FormSliderBar<double>
            {
                Caption = "Beats",
                HintText = "The duration when the duration is a fixed number of beats.",
                Current = session.Beats.GetBoundCopy(),
                KeyboardStep = 0.25f,
            };

            var durationSource = new FormEnumDropdown<SliderDurationSource>
            {
                Caption = "Duration",
                HintText = "What the placed slider's duration is fitted to. Apply to selection always keeps each slider's own duration.",
                Current = session.DurationSource.GetBoundCopy(),
            };

            return new Drawable[]
            {
                new OsuSpriteText
                {
                    Text = "Fitting",
                    Font = OsuFont.Default.With(size: 16, weight: FontWeight.SemiBold),
                    Margin = new MarginPadding { Top = 10 },
                },
                new FormEnumDropdown<SliderFitMode>
                {
                    Caption = "Fit",
                    HintText = "Scale keeps the slider velocity and resizes the shape. Keep size keeps the shape as saved and changes this slider's velocity.",
                    Current = session.FitMode.GetBoundCopy(),
                },
                durationSource,
                beatsSlider,
                new FormButton
                {
                    Caption = "Reset rotation and flip",
                    ButtonText = "Reset",
                    Action = () => session.Transform.Value = default,
                },
            };
        }

        private static string describe(SliderShape shape)
        {
            string repeats = shape.RepeatCount == 0 ? "" : $", {shape.RepeatCount} repeat{(shape.RepeatCount == 1 ? "" : "s")}";
            return $"{shape.Beats:0.##} beats, {shape.Length:0} px{repeats}";
        }

        private partial class ShapeCard : OsuClickableContainer
        {
            public readonly SliderShape Shape;

            public Action? Selected { get; init; }
            public Action? Activated { get; init; }

            private Box background = null!;

            [Resolved]
            private OverlayColourProvider colourProvider { get; set; } = null!;

            private bool isSelected;

            public bool IsSelected
            {
                set
                {
                    isSelected = value;
                    updateState();
                }
            }

            public ShapeCard(SliderShape shape)
            {
                Shape = shape;
            }

            [BackgroundDependencyLoader]
            private void load()
            {
                Size = new Vector2(170, 160);
                Masking = true;
                CornerRadius = 5;
                Action = () => Selected?.Invoke();

                Children = new Drawable[]
                {
                    background = new Box { RelativeSizeAxes = Axes.Both },
                    new SliderShapeThumbnail(Shape)
                    {
                        RelativeSizeAxes = Axes.X,
                        Height = 110,
                        Colour = colourProvider.Highlight1,
                    },
                    new FillFlowContainer
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        Anchor = Anchor.BottomLeft,
                        Origin = Anchor.BottomLeft,
                        Padding = new MarginPadding(8),
                        Direction = FillDirection.Vertical,
                        Children = new Drawable[]
                        {
                            new TruncatingSpriteText
                            {
                                RelativeSizeAxes = Axes.X,
                                Text = Shape.Name,
                                Font = OsuFont.Default.With(size: 14, weight: FontWeight.SemiBold),
                            },
                            new TruncatingSpriteText
                            {
                                RelativeSizeAxes = Axes.X,
                                Text = describe(Shape),
                                Font = OsuFont.Default.With(size: 12),
                                Colour = colourProvider.Content2,
                            },
                        }
                    },
                };

                updateState();
            }

            protected override bool OnDoubleClick(DoubleClickEvent e)
            {
                Activated?.Invoke();
                return true;
            }

            protected override bool OnHover(HoverEvent e)
            {
                updateState();
                return base.OnHover(e);
            }

            protected override void OnHoverLost(HoverLostEvent e)
            {
                updateState();
                base.OnHoverLost(e);
            }

            private void updateState()
            {
                if (background == null)
                    return;

                background.Colour = isSelected ? colourProvider.Background2 : IsHovered ? colourProvider.Background3 : colourProvider.Background4;
            }
        }

        private partial class DeleteShapeDialog : DangerousActionDialog
        {
            public DeleteShapeDialog(string name, Action delete)
            {
                HeaderText = $"Delete \"{name}\"?";
                BodyText = "The shape is removed from the gallery for all maps.";
                Icon = FontAwesome.Regular.TrashAlt;
                DangerousAction = delete;
            }
        }
    }
}
