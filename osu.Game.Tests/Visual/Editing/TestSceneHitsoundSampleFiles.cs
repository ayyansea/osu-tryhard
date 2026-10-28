// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Testing;
using osu.Game.Configuration;
using osu.Game.Graphics.UserInterface;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Screens.Edit.Hitsounds;
using osu.Game.Tests.Visual;
using osuTK.Input;

namespace osu.Game.Tests.Visual.Editing
{
    /// <summary>
    /// Goes through adding, removing and adding sound files to a sample set again, with a beatmap set which is stored in the database.
    /// </summary>
    public partial class TestSceneHitsoundSampleFiles : EditorSavingTestScene
    {
        private string firstPath = null!;
        private string secondPath = null!;

        private HitsoundSampleSetPanel panel => Editor.ChildrenOfType<HitsoundSampleSetPanel>().Single();

        private bool setHasFile(string filename)
        {
            var set = Game.BeatmapManager.QueryBeatmapSet(s => s.ID == EditorBeatmap.BeatmapInfo.BeatmapSet!.ID)!.Value;
            return set.Files.Any(f => f.Filename == filename);
        }

        [Test]
        public void TestRemoveAndAddAnotherSound()
        {
            AddStep("create sound files", () =>
            {
                float[] tone = Enumerable.Range(0, 4410).Select(i => 0.2f * MathF.Sin(i * 0.2f + 1)).ToArray();

                File.WriteAllBytes(firstPath = Path.Combine(Path.GetTempPath(), $"kick-{Guid.NewGuid():N}.wav"), HitsoundSampleProcessor.ToWav(tone, 1, 44100));
                File.WriteAllBytes(secondPath = Path.Combine(Path.GetTempPath(), $"snare-{Guid.NewGuid():N}.wav"), HitsoundSampleProcessor.ToWav(tone, 1, 44100));
            });

            SaveEditor();
            AddStep("switch to hitsounds screen", () => InputManager.Key(Key.F7));
            AddUntilStep("panel loaded", () => Editor.ChildrenOfType<HitsoundSampleSetPanel>().SingleOrDefault()?.IsLoaded == true);

            addSound(() => firstPath);
            AddUntilStep("first file is in the set", () => setHasFile("soft-hitnormal.wav"));

            AddStep("right click the sound", () =>
            {
                InputManager.MoveMouseTo(panel.ChildrenOfType<OsuButton>().First(b => b.Text.ToString() is "+" or "play"));
                InputManager.Click(MouseButton.Right);
            });
            AddStep("remove it", () =>
            {
                InputManager.MoveMouseTo(Game.ChildrenOfType<DrawableOsuMenuItem>().First(i => i.Item.Text.Value.ToString() == "Remove"));
                InputManager.Click(MouseButton.Left);
            });
            AddUntilStep("file is gone from the set", () => !setHasFile("soft-hitnormal.wav"));

            addSound(() => secondPath);
            AddUntilStep("chooser closed", () => !Game.ChildrenOfType<FormFileSelector.FileChooserPopover>().Any(p => p.IsLoaded && p.IsPresent));
            AddAssert("slot shows play", () => panel.ChildrenOfType<OsuButton>().First(b => b.Text.ToString() is "+" or "play").Text.ToString(), () => Is.EqualTo("play"));
            AddUntilStep("second file is in the set", () => setHasFile("soft-hitnormal.wav"));
            AddUntilStep("the slot shows it", () => panel.ChildrenOfType<OsuButton>().First(b => b.Text.ToString() is "+" or "play").Text.ToString() == "play");

            AddStep("clean up", () =>
            {
                File.Delete(firstPath);
                File.Delete(secondPath);
            });
        }

        [Test]
        public void TestFileChooserStartsInConfiguredFolder()
        {
            string folder = null!;

            AddStep("set the sound folder", () =>
            {
                folder = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"hitsound-folder-{Guid.NewGuid():N}")).FullName;
                ((OsuConfigManager)Game.Dependencies.Get(typeof(OsuConfigManager))).SetValue(OsuSetting.HitsoundSamplesDirectory, folder);
            });

            SaveEditor();
            AddStep("switch to hitsounds screen", () => InputManager.Key(Key.F7));
            AddUntilStep("panel loaded", () => Editor.ChildrenOfType<HitsoundSampleSetPanel>().SingleOrDefault()?.IsLoaded == true);

            AddStep("click the first slot", () =>
            {
                InputManager.MoveMouseTo(panel.ChildrenOfType<OsuButton>().First(b => b.Text.ToString() is "+" or "play"));
                InputManager.Click(MouseButton.Left);
            });
            AddUntilStep("file chooser shown", () => Game.ChildrenOfType<OsuFileSelector>().SingleOrDefault()?.IsLoaded == true);
            AddAssert("it starts in the folder", () => Game.ChildrenOfType<OsuFileSelector>().Single().CurrentPath.Value.FullName, () => Is.EqualTo(folder));
            AddStep("clean up", () => Directory.Delete(folder));
        }

        private void addSound(Func<string> path)
        {
            AddStep("click the first slot", () =>
            {
                InputManager.MoveMouseTo(panel.ChildrenOfType<OsuButton>().First(b => b.Text.ToString() is "+" or "play"));
                InputManager.Click(MouseButton.Left);
            });
            AddUntilStep("file chooser shown", () => Game.ChildrenOfType<FormFileSelector.FileChooserPopover>().Any(p => p.IsLoaded));
            AddStep("pick the file", () => Game.ChildrenOfType<OsuFileSelector>().Single().CurrentFile.Value = new FileInfo(path()));
            AddUntilStep("confirm button shown", () => getUseButton()?.IsPresent == true && getUseButton()!.Parent!.Alpha > 0.9f);
            AddStep("use this file", () =>
            {
                InputManager.MoveMouseTo(getUseButton()!);
                InputManager.Click(MouseButton.Left);
            });
        }

        private RoundedButton? getUseButton() => Game.ChildrenOfType<FormFileSelector.FileChooserPopover>().FirstOrDefault()?.ChildrenOfType<RoundedButton>().FirstOrDefault(b => b.Text.ToString() == "use this file");
    }
}
