// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.IO;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Input;
using osu.Framework.Platform;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.ControlPoints;
using osu.Game.Configuration;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Osu;
using osu.Game.Screens.Edit;
using osu.Game.Screens.Edit.Compose;
using osu.Game.Screens.Edit.Compose.Components.Timeline;
using osu.Game.Screens.Edit.Rhythm;
using osu.Game.Tests.Beatmaps;
using osuTK;
using osuTK.Input;

namespace osu.Game.Tests.Visual.Editing
{
    public partial class TestSceneRhythmScreen : EditorTestScene
    {
        private const double beat_length = 500;

        protected override Ruleset CreateEditorRuleset() => new OsuRuleset();

        [Resolved]
        private OsuConfigManager config { get; set; } = null!;

        [Resolved]
        private Storage storage { get; set; } = null!;

        protected override IBeatmap CreateBeatmap(RulesetInfo ruleset)
        {
            var beatmap = new TestBeatmap(ruleset, false);
            beatmap.ControlPointInfo.Clear();
            beatmap.ControlPointInfo.Add(0, new TimingControlPoint { BeatLength = beat_length });
            return beatmap;
        }

        private EditorRhythm rhythm => Editor.ChildrenOfType<EditorRhythm>().Single();

        private EditorBeatmap rhythmBeatmap => rhythm.Beatmap;

        private RhythmScreen rhythmScreen => Editor.ChildrenOfType<RhythmScreen>().Single();

        private Timeline rhythmTimeline => rhythmScreen.ChildrenOfType<Timeline>().Single();

        public override void SetUpSteps()
        {
            base.SetUpSteps();

            AddStep("enable rhythm strip", () => config.SetValue(OsuSetting.EditorTimelineShowRhythmStrip, true));
            AddStep("seek to start", () => EditorClock.Seek(0));
            AddStep("switch to rhythm screen", () => InputManager.Key(Key.F5));
            AddUntilStep("rhythm timeline loaded", () => Editor.ChildrenOfType<RhythmScreen>().SingleOrDefault()?.ChildrenOfType<RhythmTimelineBlueprintContainer>().SingleOrDefault()?.IsLoaded == true);
        }

        [Test]
        public void TestPlaceClicksAndHolds()
        {
            int realObjectCount = 0;

            AddStep("store actual object count", () => realObjectCount = EditorBeatmap.HitObjects.Count);

            AddStep("select click tool", () => InputManager.Key(Key.Number2));
            placeClickAt(1000);
            AddAssert("click placed", () => rhythmBeatmap.HitObjects.Single(), () => Is.Not.InstanceOf<RhythmHold>());
            AddAssert("click at 1000ms", () => rhythmBeatmap.HitObjects.Single().StartTime, () => Is.EqualTo(1000));

            AddStep("select hold tool", () => InputManager.Key(Key.Number3));
            AddStep("drag out hold", () =>
            {
                InputManager.MoveMouseTo(timelinePositionAt(1500));
                InputManager.PressButton(MouseButton.Left);
                InputManager.MoveMouseTo(timelinePositionAt(2000));
                InputManager.MoveMouseTo(timelinePositionAt(2500));
            });
            AddStep("release", () => InputManager.ReleaseButton(MouseButton.Left));

            AddAssert("hold placed", () => rhythmBeatmap.HitObjects.OfType<RhythmHold>().Single().StartTime, () => Is.EqualTo(1500));
            AddAssert("hold duration correct", () => rhythmBeatmap.HitObjects.OfType<RhythmHold>().Single().Duration, () => Is.EqualTo(1000));

            AddAssert("actual beatmap unchanged", () => EditorBeatmap.HitObjects.Count, () => Is.EqualTo(realObjectCount));
            AddAssert("editor has unsaved changes", () => Editor.HasUnsavedChanges);
        }

        [Test]
        public void TestSelectCopyPasteCutDeleteUndo()
        {
            AddStep("add rhythm", () =>
            {
                rhythmBeatmap.Add(new RhythmHitObject { StartTime = 0 });
                rhythmBeatmap.Add(new RhythmHitObject { StartTime = 500 });
                rhythmBeatmap.Add(new RhythmHold { StartTime = 1000, Duration = 500 });
            });

            AddStep("select tool", () => InputManager.Key(Key.Number1));

            AddStep("select all", () => InputManager.Keys(PlatformAction.SelectAll));
            AddAssert("all rhythm selected", () => rhythmBeatmap.SelectedHitObjects.Count, () => Is.EqualTo(3));
            AddAssert("actual beatmap selection unaffected", () => EditorBeatmap.SelectedHitObjects, () => Is.Empty);

            AddStep("copy", () => InputManager.Keys(PlatformAction.Copy));
            AddStep("seek to 4000", () => EditorClock.Seek(4000));
            AddStep("paste", () => InputManager.Keys(PlatformAction.Paste));

            AddAssert("pattern pasted", () => rhythmBeatmap.HitObjects.Count, () => Is.EqualTo(6));
            AddAssert("pasted at current time", () => rhythmBeatmap.SelectedHitObjects.Min(h => h.StartTime), () => Is.EqualTo(4000));
            AddAssert("pasted hold kept duration", () => rhythmBeatmap.SelectedHitObjects.OfType<RhythmHold>().Single().Duration, () => Is.EqualTo(500));

            AddStep("delete pasted", () => InputManager.Key(Key.Delete));
            AddAssert("pasted objects deleted", () => rhythmBeatmap.HitObjects.Count, () => Is.EqualTo(3));

            AddStep("undo", () => InputManager.Keys(PlatformAction.Undo));
            AddAssert("deletion undone", () => rhythmBeatmap.HitObjects.Count, () => Is.EqualTo(6));

            AddStep("redo", () => InputManager.Keys(PlatformAction.Redo));
            AddAssert("deletion redone", () => rhythmBeatmap.HitObjects.Count, () => Is.EqualTo(3));

            AddStep("select first click", () =>
            {
                rhythmBeatmap.SelectedHitObjects.Clear();
                rhythmBeatmap.SelectedHitObjects.Add(rhythmBeatmap.HitObjects.First());
            });
            AddStep("cut", () => InputManager.Keys(PlatformAction.Cut));
            AddAssert("click cut", () => rhythmBeatmap.HitObjects.Count, () => Is.EqualTo(2));

            AddStep("seek to 6000", () => EditorClock.Seek(6000));
            AddStep("paste", () => InputManager.Keys(PlatformAction.Paste));
            AddAssert("cut click pasted", () => rhythmBeatmap.HitObjects.Any(h => h.StartTime == 6000));
        }

        [Test]
        public void TestDragSelect()
        {
            AddStep("add rhythm", () =>
            {
                rhythmBeatmap.Add(new RhythmHitObject { StartTime = 500 });
                rhythmBeatmap.Add(new RhythmHitObject { StartTime = 1000 });
                rhythmBeatmap.Add(new RhythmHitObject { StartTime = 3000 });
            });

            AddStep("select tool", () => InputManager.Key(Key.Number1));
            AddStep("drag select first two", () =>
            {
                InputManager.MoveMouseTo(timelinePositionAt(250) + new Vector2(0, -20));
                InputManager.PressButton(MouseButton.Left);
                InputManager.MoveMouseTo(timelinePositionAt(800) + new Vector2(0, 20));
                InputManager.MoveMouseTo(timelinePositionAt(1250) + new Vector2(0, 20));
            });
            AddStep("release", () => InputManager.ReleaseButton(MouseButton.Left));

            AddAssert("two objects selected", () => rhythmBeatmap.SelectedHitObjects.Select(h => h.StartTime), () => Is.EquivalentTo(new[] { 500d, 1000d }));
        }

        [Test]
        public void TestTapAlong()
        {
            AddStep("seek to 1500", () => EditorClock.Seek(1500));
            AddStep("tap A", () => InputManager.Key(Key.A));
            AddAssert("click recorded at current time", () => rhythmBeatmap.HitObjects.Single().StartTime, () => Is.EqualTo(1500));
        }

        [Test]
        public void TestSaveWritesRhythm()
        {
            AddStep("add rhythm", () =>
            {
                rhythmBeatmap.Add(new RhythmHitObject { StartTime = 500 });
                rhythmBeatmap.Add(new RhythmHold { StartTime = 1000, Duration = 1000 });
            });

            AddAssert("has unsaved changes", () => Editor.HasUnsavedChanges);
            AddStep("save", () => Editor.Save());
            AddAssert("no unsaved changes", () => !Editor.HasUnsavedChanges);

            AddAssert("rhythm written to storage", () =>
            {
                using (var stream = storage.GetStorageForDirectory("rhythm").GetStream($"{EditorBeatmap.BeatmapInfo.ID}.txt"))
                using (var reader = new StreamReader(stream))
                    return RhythmSerializer.Write(RhythmSerializer.Read(reader));
            }, () => Is.EqualTo(RhythmSerializer.Write(rhythmBeatmap.HitObjects)));
        }

        [Test]
        public void TestRhythmStripInCompose()
        {
            AddStep("add rhythm", () =>
            {
                rhythmBeatmap.Add(new RhythmHitObject { StartTime = 500 });
                rhythmBeatmap.Add(new RhythmHold { StartTime = 1000, Duration = 1000 });
            });

            AddStep("switch to compose", () => InputManager.Key(Key.F1));
            AddUntilStep("strip visible", () => composeStrip()?.Alpha == 1);
            AddUntilStep("strip shows rhythm", () => composeStrip()!.Children.Count, () => Is.EqualTo(3));

            AddStep("disable rhythm strip", () => config.SetValue(OsuSetting.EditorTimelineShowRhythmStrip, false));
            AddAssert("strip hidden", () => composeStrip()!.Alpha, () => Is.Zero);

            TimelineRhythmStrip? composeStrip() => Editor.ChildrenOfType<ComposeScreen>().SingleOrDefault()?.ChildrenOfType<TimelineRhythmStrip>().SingleOrDefault();
        }

        private void placeClickAt(double time) => AddStep($"click at {time}ms", () =>
        {
            InputManager.MoveMouseTo(timelinePositionAt(time));
            InputManager.Click(MouseButton.Left);
        });

        private Vector2 timelinePositionAt(double time) => rhythmTimeline.ScreenSpacePositionAtTime(time);
    }
}
