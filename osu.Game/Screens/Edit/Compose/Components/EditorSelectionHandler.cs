// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Input.Bindings;
using osu.Framework.Input.Events;
using osu.Game.Audio;
using osu.Game.Graphics.UserInterface;
using osu.Game.Localisation;
using osu.Game.Rulesets.Edit;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Objects.Types;
using osuTK.Input;

namespace osu.Game.Screens.Edit.Compose.Components
{
    public partial class EditorSelectionHandler : SelectionHandler<HitObject>
    {
        /// <summary>
        /// Whether right click should delete even when shift is not held.
        /// </summary>
        public bool RightClickAlwaysQuickDeletes { get; set; }

        [Resolved]
        protected EditorBeatmap EditorBeatmap { get; private set; } = null!;

        /// <remarks>
        /// May be null when editing objects which don't belong to a ruleset (e.g. the rhythm blueprint).
        /// </remarks>
        [Resolved(CanBeNull = true)]
        private HitObjectComposer? composer { get; set; }

        protected override bool ShouldQuickDelete(MouseButtonEvent e)
        {
            if (RightClickAlwaysQuickDeletes && e.Button == MouseButton.Right)
                return true;

            return base.ShouldQuickDelete(e);
        }

        protected override void DeleteItems(IEnumerable<HitObject> items) => EditorBeatmap.RemoveRange(items);

        #region Context Menu

        /// <summary>
        /// Provide context menu items relevant to current selection. Calling base is not required.
        /// </summary>
        /// <param name="selection">The current selection.</param>
        /// <returns>The relevant menu items.</returns>
        protected override IEnumerable<MenuItem> GetContextMenuItemsForSelection(IEnumerable<SelectionBlueprint<HitObject>> selection)
        {
            // Hitsound and combo options are provided by the composer.
            if (composer == null)
                yield break;

            if (SelectedBlueprints.All(b => b.Item is IHasComboInformation) && composer.SelectionNewComboState != null)
            {
                yield return new TernaryStateToggleMenuItem(EditorStrings.NewCombo)
                {
                    State = { BindTarget = composer.SelectionNewComboState },
                    Hotkey = new Hotkey(new KeyCombination(InputKey.Q))
                };
            }

            yield return new OsuMenuItem(EditorStrings.Sample) { Items = getSampleSubmenuItems(composer).ToArray(), };
            yield return new OsuMenuItem(EditorStrings.Bank) { Items = getBankSubmenuItems(composer).ToArray(), };
        }

        private IEnumerable<MenuItem> getSampleSubmenuItems(HitObjectComposer composer)
        {
            var whistle = composer.SelectionSampleStates[HitSampleInfo.HIT_WHISTLE];
            yield return new TernaryStateToggleMenuItem(whistle.Description)
            {
                State = { BindTarget = whistle },
                Hotkey = new Hotkey(new KeyCombination(InputKey.W))
            };

            var finish = composer.SelectionSampleStates[HitSampleInfo.HIT_FINISH];
            yield return new TernaryStateToggleMenuItem(finish.Description)
            {
                State = { BindTarget = finish },
                Hotkey = new Hotkey(new KeyCombination(InputKey.E))
            };

            var clap = composer.SelectionSampleStates[HitSampleInfo.HIT_CLAP];
            yield return new TernaryStateToggleMenuItem(clap.Description)
            {
                State = { BindTarget = clap },
                Hotkey = new Hotkey(new KeyCombination(InputKey.R))
            };
        }

        private IEnumerable<MenuItem> getBankSubmenuItems(HitObjectComposer composer)
        {
            var auto = composer.SelectionBankStates[HitObjectComposer.HIT_BANK_AUTO];
            yield return new TernaryStateToggleMenuItem(auto.Description)
            {
                State = { BindTarget = auto },
                Hotkey = new Hotkey(new KeyCombination(InputKey.Shift, InputKey.Q))
            };

            var normal = composer.SelectionBankStates[HitSampleInfo.BANK_NORMAL];
            yield return new TernaryStateToggleMenuItem(normal.Description)
            {
                State = { BindTarget = normal },
                Hotkey = new Hotkey(new KeyCombination(InputKey.Shift, InputKey.W))
            };

            var soft = composer.SelectionBankStates[HitSampleInfo.BANK_SOFT];
            yield return new TernaryStateToggleMenuItem(soft.Description)
            {
                State = { BindTarget = soft },
                Hotkey = new Hotkey(new KeyCombination(InputKey.Shift, InputKey.E))
            };

            var drum = composer.SelectionBankStates[HitSampleInfo.BANK_DRUM];
            yield return new TernaryStateToggleMenuItem(drum.Description)
            {
                State = { BindTarget = drum },
                Hotkey = new Hotkey(new KeyCombination(InputKey.Shift, InputKey.R))
            };

            yield return new OsuMenuItem(EditorStrings.AdditionBank)
            {
                Items = composer.SelectionAdditionBankStates.Select(kvp =>
                    new TernaryStateToggleMenuItem(kvp.Value.Description) { State = { BindTarget = kvp.Value } }).ToArray()
            };
        }

        #endregion
    }
}
