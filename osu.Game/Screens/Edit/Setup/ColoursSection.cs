// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Localisation;
using osu.Framework.Logging;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.Formats;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Localisation;
using osu.Game.Screens.Edit.ComboColours;
using osu.Game.Skinning;
using osu.Game.Utils;

namespace osu.Game.Screens.Edit.Setup
{
    public partial class ColoursSection : SetupSection
    {
        public override LocalisableString Title => EditorSetupStrings.ColoursHeader;

        private FormColourPalette comboColours = null!;
        private FormEnumDropdown<PaletteStyle> paletteStyle = null!;
        private FormSliderBar<int> paletteSize = null!;
        private ComboColourSectionList sectionList = null!;

        private CancellationTokenSource? cancellationSource;

        [Resolved]
        private IBindable<WorkingBeatmap> working { get; set; } = null!;

        [BackgroundDependencyLoader]
        private void load(SetupScreen? setupScreen)
        {
            Children = new Drawable[]
            {
                comboColours = new FormColourPalette
                {
                    PaletteHeaderText = EditorSetupStrings.AccentColoursInBeatmapBackground,
                    Caption = EditorSetupStrings.HitCircleSliderCombos,
                },
                paletteStyle = new FormEnumDropdown<PaletteStyle>
                {
                    Caption = "Palette style",
                    HintText = "How \"Generate palette\" builds the colours from the current palette.",
                },
                paletteSize = new FormSliderBar<int>
                {
                    Caption = "Palette size",
                    Current = new BindableInt(4)
                    {
                        MinValue = 2,
                        MaxValue = LegacyBeatmapDecoder.MAX_COMBO_COLOUR_COUNT,
                    },
                },
                new FormButton
                {
                    Caption = "Generate palette",
                    ButtonText = "Generate",
                    Action = generatePalette,
                },
                sectionList = new ComboColourSectionList(),
            };

            sectionList.Palette.BindTo(comboColours.Colours);

            setupScreen?.BackgroundChanged += refreshSuggestions;

            refreshSuggestions();
        }

        private bool syncingColours;

        protected override void LoadComplete()
        {
            base.LoadComplete();

            if (Beatmap.BeatmapSkin != null)
                comboColours.Colours.AddRange(Beatmap.BeatmapSkin.ComboColours);

            if (comboColours.Colours.Count == 0)
            {
                // compare ctor of `EditorBeatmapSkin`
                for (int i = 0; i < SkinConfiguration.DefaultComboColours.Count; ++i)
                    comboColours.Colours.Add(SkinConfiguration.DefaultComboColours[(i + 1) % SkinConfiguration.DefaultComboColours.Count]);
            }

            comboColours.Colours.BindCollectionChanged((_, _) =>
            {
                if (Beatmap.BeatmapSkin != null)
                {
                    if (syncingColours)
                        return;

                    syncingColours = true;

                    Beatmap.BeatmapSkin.ComboColours.Clear();
                    Beatmap.BeatmapSkin.ComboColours.AddRange(comboColours.Colours);

                    updateAddButtonVisibility();

                    syncingColours = false;
                }
            });

            Beatmap.BeatmapSkin?.ComboColours.BindCollectionChanged((_, _) =>
            {
                if (syncingColours)
                    return;

                syncingColours = true;

                comboColours.Colours.Clear();
                comboColours.Colours.AddRange(Beatmap.BeatmapSkin?.ComboColours);

                updateAddButtonVisibility();

                syncingColours = false;
            });

            updateAddButtonVisibility();

            void updateAddButtonVisibility() => comboColours.CanAdd.Value = comboColours.Colours.Count < LegacyBeatmapDecoder.MAX_COMBO_COLOUR_COUNT;
        }

        private void generatePalette()
        {
            if (comboColours.Colours.Count == 0)
                return;

            var first = comboColours.Colours.First();
            var last = comboColours.Colours.Last();
            int count = paletteSize.Current.Value;

            var generated = paletteStyle.Current.Value switch
            {
                PaletteStyle.Gradient => ComboColourEditing.Gradient(first, last, count),
                PaletteStyle.HueSpread => ComboColourEditing.HueSpread(first, count),
                _ => ComboColourEditing.Shades(first, count),
            };

            comboColours.Colours.Clear();
            comboColours.Colours.AddRange(generated);
        }

        private enum PaletteStyle
        {
            [Description("Gradient from the first to the last colour")]
            Gradient,

            [Description("Hues spread evenly from the first colour")]
            HueSpread,

            [Description("Shades of the first colour")]
            Shades,
        }

        private void refreshSuggestions()
        {
            cancellationSource?.Cancel();
            cancellationSource = new CancellationTokenSource();

            var cancellationToken = cancellationSource.Token;

            string backgroundFile = working.Value.Metadata.BackgroundFile;

            if (string.IsNullOrEmpty(backgroundFile))
            {
                comboColours.Suggestions.Clear();
                return;
            }

            string? storagePath = working.Value.BeatmapSetInfo.GetPathForFile(backgroundFile);

            if (storagePath == null)
            {
                comboColours.Suggestions.Clear();
                return;
            }

            var beatmap = working.Value;

            Task.Run(() =>
            {
                try
                {
                    using var stream = beatmap.GetStream(storagePath);

                    if (stream == null)
                        return;

                    var colours = BackgroundComboColourExtractor.Extract(stream);

                    Schedule(() =>
                    {
                        if (cancellationToken.IsCancellationRequested)
                            return;

                        comboColours.Suggestions.Clear();
                        comboColours.Suggestions.AddRange(colours);
                    });
                }
                catch (Exception e)
                {
                    Logger.Error(e, @"Failed to extract combo colours from background");
                }
            }, cancellationToken);
        }
    }
}
