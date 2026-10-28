// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Game.Audio;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterface;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Overlays;
using osu.Game.Skinning;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Screens.Edit.Hitsounds
{
    /// <summary>
    /// Lists every sample (per bank) and plays it on click. Clicking also selects the bank as the brush used by the pattern editor.
    /// </summary>
    public partial class HitsoundPicker : CompositeDrawable
    {
        private static readonly string[] sample_names = { HitSampleInfo.HIT_NORMAL, HitSampleInfo.HIT_WHISTLE, HitSampleInfo.HIT_FINISH, HitSampleInfo.HIT_CLAP };

        private readonly Bindable<string?> brushBank;
        private readonly Bindable<int> brushSampleSet;

        [Resolved]
        private EditorBeatmap editorBeatmap { get; set; } = null!;

        private SkinnableSound preview = null!;

        private readonly Dictionary<string, List<RoundedButton>> bankButtons = new Dictionary<string, List<RoundedButton>>();
        private RoundedButton autoButton = null!;

        public HitsoundPicker(Bindable<string?> brushBank, Bindable<int> brushSampleSet)
        {
            this.brushBank = brushBank;
            this.brushSampleSet = brushSampleSet;
            AutoSizeAxes = Axes.Y;
        }

        [BackgroundDependencyLoader]
        private void load(OverlayColourProvider colourProvider)
        {
            var sampleSets = editorBeatmap.BeatmapSkin?.GetAvailableSampleSets().ToList() ?? new List<EditorBeatmapSkin.SampleSet> { new EditorBeatmapSkin.SampleSet(1) };

            FillFlowContainer banks;
            OsuDropdown<EditorBeatmapSkin.SampleSet> sampleSetDropdown;

            InternalChildren = new Drawable[]
            {
                new EditorSkinProvidingContainer(editorBeatmap) { Child = preview = new SkinnableSound() },
                new FillFlowContainer
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Direction = FillDirection.Vertical,
                    Spacing = new Vector2(8),
                    Padding = new MarginPadding(10),
                    Children = new Drawable[]
                    {
                        new OsuSpriteText { Text = "sample picker", Font = OsuFont.GetFont(size: 16, weight: FontWeight.SemiBold) },
                        new OsuTextFlowContainer(s => { s.Font = OsuFont.GetFont(size: 12); s.Colour = colourProvider.Content2; })
                        {
                            RelativeSizeAxes = Axes.X,
                            AutoSizeAxes = Axes.Y,
                            Text = "click a sample to preview it and use its bank as the brush. the brush bank is applied to newly painted additions and to the bank row.",
                        },
                        autoButton = new RoundedButton
                        {
                            RelativeSizeAxes = Axes.X,
                            Height = 28,
                            Text = "auto bank (follow normal bank; bank row cycles)",
                            Action = () => brushBank.Value = null,
                        },
                        sampleSetDropdown = new OsuDropdown<EditorBeatmapSkin.SampleSet>
                        {
                            RelativeSizeAxes = Axes.X,
                            Items = sampleSets,
                        },
                        banks = new FillFlowContainer
                        {
                            RelativeSizeAxes = Axes.X,
                            AutoSizeAxes = Axes.Y,
                            Direction = FillDirection.Horizontal,
                            Spacing = new Vector2(8),
                        },
                    }
                },
            };

            foreach (string bank in HitSampleInfo.ALL_BANKS)
            {
                var buttons = new List<RoundedButton>();
                bankButtons[bank] = buttons;

                var column = new FillFlowContainer
                {
                    Width = 120,
                    AutoSizeAxes = Axes.Y,
                    Direction = FillDirection.Vertical,
                    Spacing = new Vector2(4),
                    Child = new OsuSpriteText { Text = bank, Font = OsuFont.GetFont(size: 14, weight: FontWeight.SemiBold) },
                };

                foreach (string name in sample_names)
                {
                    string sampleName = name;
                    string bankName = bank;

                    var button = new RoundedButton
                    {
                        RelativeSizeAxes = Axes.X,
                        Height = 28,
                        Text = sampleName.Replace("hit", string.Empty),
                        Action = () =>
                        {
                            brushBank.Value = bankName;
                            play(sampleName, bankName);
                        },
                    };

                    buttons.Add(button);
                    column.Add(button);
                }

                banks.Add(column);
            }

            sampleSetDropdown.Current.BindValueChanged(set => brushSampleSet.Value = set.NewValue.SampleSetIndex);
            sampleSetDropdown.Current.Value = sampleSets.First();

            brushBank.BindValueChanged(_ => updateSelection(colourProvider), true);
        }

        private void updateSelection(OverlayColourProvider colourProvider)
        {
            autoButton.BackgroundColour = brushBank.Value == null ? CatppuccinMochaColourProvider.MAUVE.Darken(0.4f) : colourProvider.Background3;

            foreach ((string bank, var buttons) in bankButtons)
            {
                foreach (var button in buttons)
                    button.BackgroundColour = brushBank.Value == bank ? CatppuccinMochaColourProvider.MAUVE.Darken(0.4f) : colourProvider.Background3;
            }
        }

        private void play(string sampleName, string bank)
        {
            int set = brushSampleSet.Value;

            preview.Samples = new ISampleInfo[] { new HitSampleInfo(sampleName, bank, set >= 2 ? set.ToString() : null) };
            preview.Play();
        }
    }
}
