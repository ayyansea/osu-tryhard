// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Extensions.ObjectExtensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using osu.Framework.Logging;
using osu.Framework.Platform;
using osu.Game.Extensions;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterface;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Overlays;
using osu.Game.Rulesets.Objects;
using osuTK;

namespace osu.Game.Screens.Edit.ComboColours
{
    /// <summary>
    /// Edits the combo colour sections of the open difficulty and applies them to its combo colour offsets.
    /// Sections are tool state, stored per difficulty in <c>combocolours/&lt;BeatmapInfo.ID&gt;.txt</c> and saved on every change.
    /// </summary>
    public partial class ComboColourSectionList : CompositeDrawable
    {
        private const float row_height = 30;

        /// <summary>
        /// The combo colour palette, bound to the beatmap's.
        /// </summary>
        public BindableList<Colour4> Palette { get; } = new BindableList<Colour4>();

        public IReadOnlyList<ComboColourSection> Sections => sections;

        private readonly List<ComboColourSection> sections = new List<ComboColourSection>();

        [Resolved]
        private EditorBeatmap beatmap { get; set; } = null!;

        // optional so the section can be shown outside an editor (tests).
        [Resolved]
        private EditorClock? clock { get; set; }

        [Resolved]
        private OverlayColourProvider colourProvider { get; set; } = null!;

        private Storage storage = null!;

        private FillFlowContainer<SectionRow> rows = null!;
        private PreviewStrip preview = null!;
        private OsuSpriteText status = null!;
        private RoundedButton applyButton = null!;

        private string filename => $"{beatmap.BeatmapInfo.ID}.txt";

        [BackgroundDependencyLoader]
        private void load(Storage gameStorage)
        {
            storage = gameStorage.GetStorageForDirectory("combocolours");

            RelativeSizeAxes = Axes.X;
            AutoSizeAxes = Axes.Y;
            Masking = true;
            CornerRadius = 5;
            CornerExponent = 2.5f;

            InternalChildren = new Drawable[]
            {
                new FormControlBackground(),
                new FillFlowContainer
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Padding = new MarginPadding(9),
                    Spacing = new Vector2(7),
                    Direction = FillDirection.Vertical,
                    Children = new Drawable[]
                    {
                        new FormFieldCaption
                        {
                            Caption = "Colour sections",
                            TooltipText = "From a section's time on, new combos cycle through its colours. A burst recolours only the next combo. "
                                          + "Click a palette colour to add it to a section, click a section colour to remove it. Click a time to seek there.",
                        },
                        preview = new PreviewStrip
                        {
                            RelativeSizeAxes = Axes.X,
                            Height = 16,
                        },
                        rows = new FillFlowContainer<SectionRow>
                        {
                            RelativeSizeAxes = Axes.X,
                            AutoSizeAxes = Axes.Y,
                            Spacing = new Vector2(4),
                            Direction = FillDirection.Vertical,
                        },
                        new FillFlowContainer
                        {
                            RelativeSizeAxes = Axes.X,
                            AutoSizeAxes = Axes.Y,
                            Spacing = new Vector2(7),
                            Direction = FillDirection.Horizontal,
                            Children = new Drawable[]
                            {
                                new RoundedButton
                                {
                                    Width = 170,
                                    Text = "Add section here",
                                    Action = () => addSection(false),
                                },
                                new RoundedButton
                                {
                                    Width = 150,
                                    Text = "Add burst here",
                                    Action = () => addSection(true),
                                },
                                applyButton = new RoundedButton
                                {
                                    Width = 170,
                                    Text = "Apply to combos",
                                    Action = apply,
                                },
                                status = new OsuSpriteText
                                {
                                    Anchor = Anchor.CentreLeft,
                                    Origin = Anchor.CentreLeft,
                                    Font = OsuFont.Default.With(size: 14),
                                },
                            }
                        },
                    }
                }
            };

            sections.AddRange(readFromStorage());
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            status.Colour = colourProvider.Content2;

            Palette.BindCollectionChanged((_, _) => sectionsChanged(false));
            beatmap.HitObjectAdded += onBeatmapChanged;
            beatmap.HitObjectRemoved += onBeatmapChanged;
            beatmap.BeatmapReprocessed += refreshPreview;

            sectionsChanged(false);
        }

        private void onBeatmapChanged(HitObject _) => Scheduler.AddOnce(refreshPreview);

        private void addSection(bool burst)
        {
            // not snapped: seeking onto a combo puts the clock exactly on it, and snapping could move past an unsnapped one.
            double time = Math.Round(clock?.CurrentTime ?? 0);

            // one section per time and kind: adding again at the same place just keeps the existing one.
            if (sections.Any(s => s.Burst == burst && Math.Abs(s.Time - time) <= ComboColourEditing.TIME_LENIENCY))
                return;

            var previous = sections.Where(s => !s.Burst && s.Time <= time).MaxBy(s => s.Time);

            sections.Add(new ComboColourSection
            {
                Time = time,
                Burst = burst,
                // start from the colours in effect, which is usually what gets tweaked.
                Colours = burst ? new List<int>() : previous?.Colours.ToList() ?? Enumerable.Range(0, Palette.Count).ToList(),
            });

            sectionsChanged(true);
        }

        private void apply()
        {
            int changed = ComboColourEditing.Apply(beatmap, sections, Palette.Count);
            status.Text = changed == 0 ? "Nothing to change" : $"Changed {changed} combo{(changed == 1 ? "" : "s")}";
        }

        private void sectionsChanged(bool save)
        {
            sections.Sort((a, b) => a.Time.CompareTo(b.Time));

            if (save)
                writeToStorage();

            rows.Clear();

            foreach (var section in sections)
            {
                rows.Add(new SectionRow(section, Palette.ToArray())
                {
                    Changed = () => sectionsChanged(true),
                    Deleted = () =>
                    {
                        sections.Remove(section);
                        sectionsChanged(true);
                    },
                    Seek = () => clock?.SeekSmoothlyTo(section.Time),
                });
            }

            refreshPreview();
        }

        private void refreshPreview()
        {
            var starts = ComboColourEditing.GetComboStarts(beatmap.HitObjects);
            var combos = starts.Select(s => new ComboStart(((HitObject)s).StartTime, s.ComboOffset)).ToArray();
            int[] offsets = ComboColourEditing.ComputeOffsets(combos, sections, Palette.Count);
            int[] colours = ComboColourEditing.ColourIndices(offsets, Palette.Count);

            int pending = combos.Where((c, i) => c.Offset != offsets[i]).Count();
            applyButton.Enabled.Value = pending > 0;

            if (sections.Count == 0)
                status.Text = "No sections";
            else
                status.Text = pending == 0 ? "All combos match" : $"{pending} combo{(pending == 1 ? "" : "s")} to change";

            double endTime = beatmap.HitObjects.Count > 0 ? beatmap.HitObjects.Max(h => h.GetEndTime()) : 0;
            preview.SetData(combos.Select(c => c.Time).ToArray(), colours.Select(i => Palette.Count > 0 ? Palette[i] : Colour4.White).ToArray(),
                sections.Select(s => s.Time).ToArray(), endTime);
        }

        private void writeToStorage()
        {
            try
            {
                if (sections.Count == 0)
                    storage.Delete(filename);
                else
                {
                    using (var stream = storage.CreateFileSafely(filename))
                    using (var writer = new StreamWriter(stream))
                        ComboColourSerializer.Write(sections, writer);
                }
            }
            catch (Exception e)
            {
                Logger.Error(e, "Failed to save combo colour sections");
            }
        }

        private List<ComboColourSection> readFromStorage()
        {
            try
            {
                if (!storage.Exists(filename))
                    return new List<ComboColourSection>();

                using (var stream = storage.GetStream(filename))
                using (var reader = new StreamReader(stream))
                    return ComboColourSerializer.Read(reader);
            }
            catch (Exception e)
            {
                Logger.Error(e, "Failed to load combo colour sections");
                return new List<ComboColourSection>();
            }
        }

        protected override void Dispose(bool isDisposing)
        {
            base.Dispose(isDisposing);

            if (beatmap.IsNotNull())
            {
                beatmap.HitObjectAdded -= onBeatmapChanged;
                beatmap.HitObjectRemoved -= onBeatmapChanged;
                beatmap.BeatmapReprocessed -= refreshPreview;
            }
        }

        /// <summary>
        /// The resulting colour of every combo across the map, with ticks where sections start.
        /// </summary>
        private partial class PreviewStrip : CompositeDrawable
        {
            private readonly Container segments;
            private readonly Container ticks;

            public PreviewStrip()
            {
                Masking = true;
                CornerRadius = 3;

                InternalChildren = new Drawable[]
                {
                    segments = new Container { RelativeSizeAxes = Axes.Both },
                    ticks = new Container { RelativeSizeAxes = Axes.Both },
                };
            }

            public void SetData(double[] comboTimes, Colour4[] colours, double[] sectionTimes, double endTime)
            {
                segments.Clear();
                ticks.Clear();

                if (comboTimes.Length == 0 || endTime <= comboTimes[0])
                    return;

                double start = comboTimes[0];
                double length = endTime - start;

                for (int i = 0; i < comboTimes.Length; i++)
                {
                    double segmentEnd = i + 1 < comboTimes.Length ? comboTimes[i + 1] : endTime;

                    segments.Add(new Box
                    {
                        RelativePositionAxes = Axes.X,
                        RelativeSizeAxes = Axes.Both,
                        X = (float)((comboTimes[i] - start) / length),
                        Width = (float)Math.Max((segmentEnd - comboTimes[i]) / length, 0.001),
                        Colour = colours[i],
                    });
                }

                foreach (double time in sectionTimes)
                {
                    ticks.Add(new Box
                    {
                        RelativePositionAxes = Axes.X,
                        RelativeSizeAxes = Axes.Y,
                        X = (float)Math.Clamp((time - start) / length, 0, 1),
                        Width = 2,
                        Origin = Anchor.TopCentre,
                        Colour = Colour4.White,
                    });
                }
            }
        }

        private partial class SectionRow : CompositeDrawable
        {
            public Action? Changed { get; init; }
            public Action? Deleted { get; init; }
            public Action? Seek { get; init; }

            private readonly ComboColourSection section;
            private readonly Colour4[] palette;

            public SectionRow(ComboColourSection section, Colour4[] palette)
            {
                this.section = section;
                this.palette = palette;
            }

            [BackgroundDependencyLoader]
            private void load(OverlayColourProvider colourProvider)
            {
                RelativeSizeAxes = Axes.X;
                Height = row_height;

                var sequence = new FillFlowContainer
                {
                    AutoSizeAxes = Axes.Both,
                    Spacing = new Vector2(3),
                    Direction = FillDirection.Horizontal,
                };

                for (int i = 0; i < section.Colours.Count; i++)
                {
                    int position = i;
                    int colour = section.Colours[i];

                    sequence.Add(new ColourChip(colour < palette.Length ? palette[colour] : Colour4.Gray, $"{colour + 1}", row_height)
                    {
                        TooltipText = "Remove",
                        Action = () =>
                        {
                            section.Colours.RemoveAt(position);
                            Changed?.Invoke();
                        },
                    });
                }

                if (section.Colours.Count == 0)
                {
                    sequence.Add(new OsuSpriteText
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        Text = "no colours: combos are left alone",
                        Font = OsuFont.Default.With(size: 14),
                        Colour = colourProvider.Content2,
                    });
                }

                var choices = new FillFlowContainer
                {
                    AutoSizeAxes = Axes.Both,
                    Anchor = Anchor.CentreRight,
                    Origin = Anchor.CentreRight,
                    Spacing = new Vector2(3),
                    Direction = FillDirection.Horizontal,
                };

                for (int i = 0; i < palette.Length; i++)
                {
                    int colour = i;

                    choices.Add(new ColourChip(palette[i], $"{i + 1}", row_height * 0.7f)
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        TooltipText = section.Burst ? "Use this colour" : "Add to the cycle",
                        Action = () =>
                        {
                            // a burst recolours a single combo, so it has a single colour.
                            if (section.Burst)
                                section.Colours.Clear();

                            section.Colours.Add(colour);
                            Changed?.Invoke();
                        },
                    });
                }

                choices.Add(new IconButton
                {
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft,
                    Icon = FontAwesome.Solid.Times,
                    TooltipText = "Delete section",
                    Action = () => Deleted?.Invoke(),
                });

                InternalChildren = new Drawable[]
                {
                    new FillFlowContainer
                    {
                        AutoSizeAxes = Axes.X,
                        RelativeSizeAxes = Axes.Y,
                        Spacing = new Vector2(10),
                        Direction = FillDirection.Horizontal,
                        Children = new Drawable[]
                        {
                            new OsuClickableContainer
                            {
                                Width = 90,
                                RelativeSizeAxes = Axes.Y,
                                Action = () => Seek?.Invoke(),
                                TooltipText = "Seek here",
                                Child = new OsuSpriteText
                                {
                                    Anchor = Anchor.CentreLeft,
                                    Origin = Anchor.CentreLeft,
                                    Text = section.Time.ToEditorFormattedString(),
                                    Font = OsuFont.Default.With(size: 16, weight: FontWeight.SemiBold),
                                    Colour = colourProvider.Highlight1,
                                },
                            },
                            new OsuSpriteText
                            {
                                Anchor = Anchor.CentreLeft,
                                Origin = Anchor.CentreLeft,
                                Width = 50,
                                Text = section.Burst ? "burst" : "cycle",
                                Font = OsuFont.Default.With(size: 14),
                                Colour = colourProvider.Content2,
                            },
                            sequence,
                        }
                    },
                    choices,
                };
            }
        }

        private partial class ColourChip : OsuClickableContainer
        {
            public ColourChip(Colour4 colour, string label, float size)
            {
                Size = new Vector2(size);
                Masking = true;
                CornerRadius = 4;

                Children = new Drawable[]
                {
                    new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = colour,
                    },
                    new OsuSpriteText
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Text = label,
                        Font = OsuFont.Default.With(size: size * 0.5f, weight: FontWeight.Bold),
                        Colour = OsuColour.ForegroundTextColourFor(colour),
                    },
                };
            }

            protected override bool OnHover(HoverEvent e)
            {
                this.ScaleTo(1.1f, 100, Easing.OutQuint);
                return base.OnHover(e);
            }

            protected override void OnHoverLost(HoverLostEvent e)
            {
                this.ScaleTo(1, 100, Easing.OutQuint);
                base.OnHoverLost(e);
            }
        }
    }
}
