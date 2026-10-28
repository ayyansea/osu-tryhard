// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Linq;
using NUnit.Framework;
using osu.Framework.Graphics.Containers;
using osu.Framework.Screens;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.ControlPoints;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Osu;
using osu.Game.Rulesets.Osu.Objects;
using osu.Game.Screens.Edit.MapCleaner;
using osu.Game.Tests.Beatmaps;
using osuTK;
using osuTK.Input;

namespace osu.Game.Tests.Visual.Editing
{
    public partial class TestSceneMapCleaner : EditorTestScene
    {
        protected override Ruleset CreateEditorRuleset() => new OsuRuleset();

        protected override IBeatmap CreateBeatmap(RulesetInfo ruleset)
        {
            var beatmap = new TestBeatmap(ruleset, false);
            beatmap.ControlPointInfo.Clear();
            beatmap.ControlPointInfo.Add(0, new TimingControlPoint { BeatLength = 500 });
            beatmap.HitObjects.Clear();
            beatmap.HitObjects.Add(new HitCircle { StartTime = 1002, Position = new Vector2(256, 192) });
            beatmap.HitObjects.Add(new HitCircle { StartTime = 1500, Position = new Vector2(256, 192) });
            return beatmap;
        }

        private MapCleanerOverlay cleaner => Editor.ChildrenOfType<MapCleanerOverlay>().Single();

        [Test]
        public void TestCleanUpWithShortcut()
        {
            AddStep("open with shortcut", () =>
            {
                InputManager.PressKey(Key.ControlLeft);
                InputManager.PressKey(Key.ShiftLeft);
                InputManager.Key(Key.C);
                InputManager.ReleaseKey(Key.ShiftLeft);
                InputManager.ReleaseKey(Key.ControlLeft);
            });
            AddUntilStep("cleaner visible", () => cleaner.State.Value, () => Is.EqualTo(Visibility.Visible));

            AddStep("apply", () => cleaner.ChildrenOfType<RoundedButton>().Single(b => b.Text.ToString().StartsWith("Apply", System.StringComparison.Ordinal)).TriggerClick());
            AddAssert("circle resnapped", () => EditorBeatmap.HitObjects[0].StartTime, () => Is.EqualTo(1000));

            AddStep("press escape", () => InputManager.Key(Key.Escape));
            AddUntilStep("cleaner hidden", () => cleaner.State.Value, () => Is.EqualTo(Visibility.Hidden));
            AddAssert("still in editor", () => Editor.IsCurrentScreen());

            AddStep("undo", () =>
            {
                InputManager.PressKey(Key.ControlLeft);
                InputManager.Key(Key.Z);
                InputManager.ReleaseKey(Key.ControlLeft);
            });
            AddAssert("circle back", () => EditorBeatmap.HitObjects[0].StartTime, () => Is.EqualTo(1002));
        }
    }
}
