// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Bindables;
using osu.Game.Audio;
using osu.Game.Overlays;
using osuTK.Graphics;

namespace osu.Game.Screens.Edit.Hitsounds
{
    /// <summary>
    /// Which swimlanes the hitsound grid currently shows, and where.
    /// Sections (soft / normal / drum of every sample set) are stacked top to bottom with a small gap between them;
    /// only a window of <see cref="SECTIONS_PER_PAGE"/> sections is visible, scrolled by <see cref="SectionOffset"/>.
    /// </summary>
    public class HitsoundLayout
    {
        public const int SECTIONS_PER_PAGE = 3;
        public const float ROW_HEIGHT = 22;
        public const float SEPARATOR_HEIGHT = 12;

        public static readonly string[] BANKS = { HitSampleInfo.BANK_SOFT, HitSampleInfo.BANK_NORMAL, HitSampleInfo.BANK_DRUM };
        public static readonly string[] NAMES = { HitSampleInfo.HIT_NORMAL, HitSampleInfo.HIT_WHISTLE, HitSampleInfo.HIT_FINISH, HitSampleInfo.HIT_CLAP };

        public const float SECTION_HEIGHT = 4 * ROW_HEIGHT;
        public const float HEIGHT = SECTIONS_PER_PAGE * SECTION_HEIGHT + (SECTIONS_PER_PAGE - 1) * SEPARATOR_HEIGHT;
        public const int MAX_ROWS = SECTIONS_PER_PAGE * 4;

        public static Color4 ColourOf(HitsoundLane lane) => lane.Name switch
        {
            HitSampleInfo.HIT_WHISTLE => CatppuccinMochaColourProvider.YELLOW,
            HitSampleInfo.HIT_FINISH => CatppuccinMochaColourProvider.RED,
            HitSampleInfo.HIT_CLAP => CatppuccinMochaColourProvider.MAUVE,
            _ => lane.Bank switch
            {
                HitSampleInfo.BANK_SOFT => CatppuccinMochaColourProvider.GREEN,
                HitSampleInfo.BANK_DRUM => CatppuccinMochaColourProvider.PEACH,
                _ => CatppuccinMochaColourProvider.SAPPHIRE,
            },
        };

        /// <summary>
        /// The index of the first visible section.
        /// </summary>
        public readonly Bindable<int> SectionOffset = new Bindable<int>();

        /// <summary>
        /// The currently visible lanes with their y position.
        /// </summary>
        public IReadOnlyList<(float Y, HitsoundLane Lane)> Rows { get; private set; } = Array.Empty<(float, HitsoundLane)>();

        public event Action? Changed;

        private IReadOnlyList<int> sampleSets = new[] { 1 };

        public int SectionCount => sampleSets.Count * BANKS.Length;

        public HitsoundLayout()
        {
            SectionOffset.BindValueChanged(_ => rebuild(), true);
        }

        public void SetSampleSets(IReadOnlyList<int> sets)
        {
            sampleSets = sets;
            rebuild();
        }

        /// <summary>
        /// Scrolls so that the given sample set is the first visible one.
        /// </summary>
        public void ShowSampleSet(int sampleSet)
        {
            int index = sampleSets.ToList().IndexOf(sampleSet);

            if (index >= 0)
                SectionOffset.Value = index * BANKS.Length;
        }

        private void rebuild()
        {
            int clamped = Math.Clamp(SectionOffset.Value, 0, Math.Max(0, SectionCount - SECTIONS_PER_PAGE));

            if (clamped != SectionOffset.Value)
            {
                // triggers a rebuild through the bindable.
                SectionOffset.Value = clamped;
                return;
            }

            var rows = new List<(float, HitsoundLane)>();

            for (int i = 0; i < SECTIONS_PER_PAGE; i++)
            {
                int section = SectionOffset.Value + i;

                if (section >= SectionCount)
                    break;

                int sampleSet = sampleSets[section / BANKS.Length];
                string bank = BANKS[section % BANKS.Length];

                for (int j = 0; j < NAMES.Length; j++)
                    rows.Add((i * (SECTION_HEIGHT + SEPARATOR_HEIGHT) + j * ROW_HEIGHT, new HitsoundLane(bank, sampleSet, NAMES[j])));
            }

            Rows = rows;
            Changed?.Invoke();
        }

        /// <summary>
        /// Finds the lane at a y position within the grid. Separators between sections have no lane.
        /// </summary>
        public bool TryGetLane(float y, out HitsoundLane lane)
        {
            foreach (var (rowY, rowLane) in Rows)
            {
                if (y >= rowY && y < rowY + ROW_HEIGHT)
                {
                    lane = rowLane;
                    return true;
                }
            }

            lane = default;
            return false;
        }
    }
}
