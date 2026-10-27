// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Linq;
using NUnit.Framework;
using osu.Framework.Bindables;
using osu.Framework.Development;
using osu.Framework.Extensions;
using osu.Framework.Platform;
using osu.Framework.Screens;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.Online.API;
using osu.Game.Rulesets.Osu;
using osu.Game.Screens.Edit;
using osu.Game.Screens.EditorSelect;
using osu.Game.Tests.Resources;
using osuTK.Input;

namespace osu.Game.Tests.Visual.Navigation
{
    public partial class TestSceneOsuEditorGame : OsuManualInputManagerTestScene
    {
        private TestOsuEditorGame game = null!;

        protected override bool UseFreshStoragePerRun => true;

        protected override bool CreateNestedActionContainer => false;

        protected override bool DisplayCursorForManualInput => false;

        private EditorBeatmapSelect beatmapSelect = null!;

        [SetUpSteps]
        public void SetUpSteps()
        {
            AddStep("create game", () =>
            {
                if (game?.Parent != null)
                    Remove(game, true);

                RecycleLocalStorage(false);

                AddGame(game = new TestOsuEditorGame(LocalStorage));
            });

            AddUntilStep("wait for beatmap select", () => game.IsLoaded && game.ScreenStack?.CurrentScreen is EditorBeatmapSelect select && select.IsLoaded);
            AddStep("store beatmap select", () => beatmapSelect = (EditorBeatmapSelect)game.ScreenStack.CurrentScreen);
        }

        [TearDownSteps]
        public void TearDownSteps()
        {
            if (DebugUtils.IsNUnitRunning)
            {
                AddStep("exit game", () => game.Exit());
                AddUntilStep("wait for game exit", () => game.Parent == null);
                AddStep("dispose game", () => game.Dispose());
            }
        }

        [Test]
        public void TestOnlineServicesDisabled()
        {
            AddAssert("api is offline", () => game.API.State.Value, () => Is.EqualTo(APIState.Offline));
            AddAssert("api is dummy", () => game.API, () => Is.InstanceOf<DummyAPIAccess>());
        }

        [Test]
        public void TestOpenExistingDifficulty()
        {
            AddStep("import beatmap", () => game.BeatmapManager.Import(TestResources.GetQuickTestBeatmapForImport()).WaitSafely());

            AddUntilStep("beatmap set listed", () => game.ChildrenOfType<BeatmapSetRow>().Any(r => r.IsPresent && r.Current.Value != null));
            AddStep("click beatmap set", () =>
            {
                InputManager.MoveMouseTo(game.ChildrenOfType<BeatmapSetRow>().First(r => r.IsPresent && r.Current.Value != null));
                InputManager.Click(MouseButton.Left);
            });

            AddUntilStep("difficulties listed", () => game.ChildrenOfType<DifficultyRow>().Any());
            AddStep("click difficulty", () =>
            {
                InputManager.MoveMouseTo(game.ChildrenOfType<DifficultyRow>().First());
                InputManager.Click(MouseButton.Left);
            });

            AddUntilStep("editor loaded", () => game.ScreenStack.CurrentScreen is Editor editor && editor.ReadyForUse);
            AddAssert("editing selected beatmap", () => game.Beatmap.Value.BeatmapSetInfo.ID, () => Is.EqualTo(beatmapSelect.SelectedSet.Value?.ID));

            AddStep("exit editor", () => game.ScreenStack.CurrentScreen.Exit());
            AddUntilStep("back at beatmap select", () => game.ScreenStack.CurrentScreen is EditorBeatmapSelect);
        }

        [Test]
        public void TestSearchFiltersList()
        {
            AddStep("import beatmap", () => game.BeatmapManager.Import(TestResources.GetQuickTestBeatmapForImport()).WaitSafely());
            AddUntilStep("beatmap set listed", () => game.ChildrenOfType<BeatmapSetRow>().Count(r => r.IsPresent && r.Current.Value != null), () => Is.EqualTo(1));

            AddStep("search for nonsense", () => game.ChildrenOfType<Graphics.UserInterface.SearchTextBox>().Single().Current.Value = "zzzzzzzz");
            AddUntilStep("no beatmap sets listed", () => game.ChildrenOfType<BeatmapSetRow>().Count(r => r.IsPresent && r.Current.Value != null), () => Is.Zero);

            AddStep("clear search", () => game.ChildrenOfType<Graphics.UserInterface.SearchTextBox>().Single().Current.Value = string.Empty);
            AddUntilStep("beatmap set listed again", () => game.ChildrenOfType<BeatmapSetRow>().Count(r => r.IsPresent && r.Current.Value != null), () => Is.EqualTo(1));
        }

        [Test]
        public void TestCreateNewBeatmap()
        {
            AddStep("create new beatmap", () => beatmapSelect.CreateNewBeatmap(new OsuRuleset().RulesetInfo));
            AddUntilStep("editor loaded", () => game.ScreenStack.CurrentScreen is Editor editor && editor.ReadyForUse);
            AddAssert("new beatmap created", () => game.Beatmap.Value, () => Is.Not.InstanceOf<DummyWorkingBeatmap>());

            AddStep("exit editor", () => game.ScreenStack.CurrentScreen.Exit());
            AddUntilStep("prompted to save", () => game.ChildrenOfType<PromptForSaveDialog>().SingleOrDefault()?.IsPresent == true);
            AddStep("discard new beatmap", () => game.ChildrenOfType<PromptForSaveDialog>().Single().PerformOkAction());
            AddUntilStep("back at beatmap select", () => game.ScreenStack.CurrentScreen is EditorBeatmapSelect);
        }

        private partial class TestOsuEditorGame : OsuEditorGame
        {
            public new IAPIProvider API => base.API;

            public new BeatmapManager BeatmapManager => base.BeatmapManager;

            public new Bindable<WorkingBeatmap> Beatmap => base.Beatmap;

            public TestOsuEditorGame(Storage storage)
            {
                Storage = storage;
            }

            protected override void Update()
            {
                base.Update();

                // when running in visual tests and the window loses focus, we generally don't want the game to pause.
                ((Bindable<bool>)IsActive).Value = true;
            }
        }
    }
}
