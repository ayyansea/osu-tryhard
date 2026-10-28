// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Linq;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Platform;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Objects.Types;
using osu.Game.Rulesets.Osu;
using osu.Game.Rulesets.Osu.Objects;
using osu.Game.Screens.Edit.ComboColours;
using osu.Game.Tests.Beatmaps;
using osuTK;
using osuTK.Input;

namespace osu.Game.Tests.Visual.Editing
{
    public partial class TestSceneComboColourSections : EditorTestScene
    {
        protected override Ruleset CreateEditorRuleset() => new OsuRuleset();

        [Resolved]
        private Storage storage { get; set; } = null!;

        protected override IBeatmap CreateBeatmap(RulesetInfo ruleset)
        {
            var beatmap = new TestBeatmap(ruleset, false);
            beatmap.HitObjects.Clear();

            for (int i = 0; i < 8; i++)
                beatmap.HitObjects.Add(new HitCircle { StartTime = i * 500, Position = new Vector2(256, 192), NewCombo = true });

            return beatmap;
        }

        private ComboColourSectionList sectionList => Editor.ChildrenOfType<ComboColourSectionList>().Single();

        private OsuClickableContainer[] chips(string tooltip) => sectionList.ChildrenOfType<OsuClickableContainer>().Where(c => c.TooltipText == tooltip).ToArray();

        public override void SetUpSteps()
        {
            base.SetUpSteps();

            AddStep("seek to start", () => EditorClock.Seek(0));
            AddStep("switch to setup screen", () => InputManager.Key(Key.F4));
            AddUntilStep("section list loaded", () => Editor.ChildrenOfType<ComboColourSectionList>().SingleOrDefault()?.IsLoaded == true);
        }

        [Test]
        public void TestApplySingleColour()
        {
            AddAssert("palette has colours", () => sectionList.Palette.Count, () => Is.GreaterThan(2));

            AddStep("add section", () => clickButton("Add section here"));
            AddAssert("one section", () => sectionList.Sections, () => Has.Count.EqualTo(1));

            AddUntilStep("remove all colours", () =>
            {
                chips("Remove").FirstOrDefault()?.TriggerClick();
                return sectionList.Sections.Single().Colours.Count == 0;
            });

            AddStep("add second colour", () => chips("Add to the cycle")[1].TriggerClick());
            AddAssert("section uses second colour", () => sectionList.Sections.Single().Colours, () => Is.EqualTo(new[] { 1 }));

            AddStep("apply", () => clickButton("Apply to combos"));
            AddAssert("every combo has the second colour", () => EditorBeatmap.HitObjects.OfType<IHasComboInformation>()
                                                                               .Select(h => (h.ComboIndexWithOffsets - 1) % sectionList.Palette.Count),
                () => Has.All.EqualTo(1));
            AddAssert("sections stored", () => storage.Exists($"combocolours/{EditorBeatmap.BeatmapInfo.ID}.txt"));

            AddStep("undo", () =>
            {
                InputManager.PressKey(Key.ControlLeft);
                InputManager.Key(Key.Z);
                InputManager.ReleaseKey(Key.ControlLeft);
            });
            AddAssert("offsets restored", () => EditorBeatmap.HitObjects.OfType<IHasComboInformation>().Select(h => h.ComboOffset), () => Has.All.EqualTo(0));

            AddStep("delete section", () => chips("Delete section").Single().TriggerClick());
            AddAssert("storage cleared", () => !storage.Exists($"combocolours/{EditorBeatmap.BeatmapInfo.ID}.txt"));
        }

        private void clickButton(string text) => sectionList.ChildrenOfType<RoundedButton>().Single(b => b.Text.ToString() == text).TriggerClick();
    }
}
