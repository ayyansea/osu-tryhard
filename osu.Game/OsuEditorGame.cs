// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using osu.Framework.Allocation;
using osu.Framework.Configuration;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Input.Bindings;
using osu.Framework.Input.Events;
using osu.Framework.Logging;
using osu.Framework.Platform;
using osu.Framework.Screens;
using osu.Framework.Threading;
using osu.Game.Beatmaps;
using osu.Game.Database;
using osu.Game.Input.Bindings;
using osu.Game.Online.API;
using osu.Game.Overlays;
using osu.Game.Overlays.Volume;
using osu.Game.Screens;
using osu.Game.Screens.EditorSelect;
using osu.Game.Screens.Menu;

namespace osu.Game
{
    /// <summary>
    /// A stripped down game which only provides the beatmap editor.
    /// There is no gameplay, no online connectivity and no overlays beyond what the editor requires to function.
    /// </summary>
    public partial class OsuEditorGame : OsuGameBase, IKeyBindingHandler<GlobalAction>
    {
        public OsuScreenStack ScreenStack { get; private set; } = null!;

        private readonly NotificationOverlay notifications = new NotificationOverlay();

        private OnScreenDisplay onScreenDisplay = null!;

        private VolumeOverlay volume = null!;

        private OsuLogo osuLogo = null!;

        private EditorBeatmapSelect? beatmapSelect;

        private readonly List<string> dragDropFiles = new List<string>();

        private ScheduledDelegate? dragDropImportSchedule;

        [Resolved]
        private FrameworkConfigManager frameworkConfig { get; set; } = null!;

        private DependencyContainer dependencies = null!;

        public OsuEditorGame()
        {
            // All online functionality is removed. A permanently logged-out dummy API ensures that nothing
            // attempts to communicate with osu! servers (including spectator / multiplayer / metadata hubs).
            var api = new DummyAPIAccess();
            api.Logout();
            API = api;
        }

        protected override IReadOnlyDependencyContainer CreateChildDependencies(IReadOnlyDependencyContainer parent) =>
            dependencies = new DependencyContainer(base.CreateChildDependencies(parent));

        protected override IBeatmapUpdater CreateBeatmapUpdater() => new BeatmapUpdater(BeatmapManager, DifficultyCache);

        public override void SetHost(GameHost host)
        {
            base.SetHost(host);

            if (host.Window != null)
            {
                host.Window.CursorState |= CursorState.Hidden;
                host.Window.DragDrop += onWindowDragDrop;
            }
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            if (API is Drawable apiDrawable)
                Add(apiDrawable);

            dependencies.CacheAs(osuLogo = new OsuLogo { Alpha = 0 });
        }

        /// <summary>
        /// Removes stored key bindings for editor features which no longer exist in this client.
        /// Without this, their keys (e.g. F5, which was used for testing gameplay) would conflict with newer default bindings.
        /// </summary>
        private static void removeObsoleteKeyBindings(RealmAccess realm)
        {
            int testGameplay = (int)GlobalAction.EditorTestGameplay;
            int submitBeatmap = (int)GlobalAction.EditorSubmitBeatmap;

            realm.Write(r =>
            {
                var obsolete = r.All<RealmKeyBinding>()
                                .Where(b => b.RulesetName == null && (b.ActionInt == testGameplay || b.ActionInt == submitBeatmap))
                                .ToArray();

                foreach (var binding in obsolete)
                    r.Remove(binding);
            });
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            // RealmAccess is cached by OsuGameBase's loader into the child container, which isn't visible to this class's own loader parameters.
            removeObsoleteKeyBindings(dependencies.Get<RealmAccess>());

            BeatmapManager.PostNotification = n => notifications.Post(n);
            SkinManager.PostNotification = n => notifications.Post(n);
            ScoreManager.PostNotification = n => notifications.Post(n);

            Container logoContainer;
            Container overlayContent;
            Container topMostOverlayContent;

            AddRange(new Drawable[]
            {
                ScreenStack = new OsuScreenStack { RelativeSizeAxes = Axes.Both },
                logoContainer = new Container { RelativeSizeAxes = Axes.Both },
                overlayContent = new Container { RelativeSizeAxes = Axes.Both },
                topMostOverlayContent = new Container { RelativeSizeAxes = Axes.Both },
            });

            ScreenStack.ScreenPushed += (_, next) => updateCursor(next);
            ScreenStack.ScreenExited += (_, next) => updateCursor(next);

            loadComponentSingleFile<BeatmapStore>(new RealmDetachedBeatmapStore(), Add, true);

            loadComponentSingleFile(volume = new VolumeOverlay(), overlayContent.Add, true);
            loadComponentSingleFile(new GlobalVolumeControl(), overlayContent.Add);

            onScreenDisplay = new OnScreenDisplay();
            onScreenDisplay.BeginTracking(this, frameworkConfig);
            onScreenDisplay.BeginTracking(this, LocalConfig);
            loadComponentSingleFile(onScreenDisplay, Add, true);

            loadComponentSingleFile<INotificationOverlay>(notifications.With(d =>
            {
                d.Anchor = Anchor.TopRight;
                d.Origin = Anchor.TopRight;
            }), overlayContent.Add, true);

            loadComponentSingleFile<IDialogOverlay>(new DialogOverlay(), topMostOverlayContent.Add, true);

            loadComponentSingleFile(osuLogo, _ =>
            {
                osuLogo.SetupDefaultContainer(logoContainer);

                // Loader has to be created after the logo has finished loading as Loader performs logo transformations on entering.
                ScreenStack.Push(new EditorOnlyLoader(() => beatmapSelect = new EditorBeatmapSelect()) { RelativeSizeAxes = Axes.Both });
            });
        }

        private void updateCursor(IScreen? screen) => GlobalCursorDisplay.ShowCursor = (screen as IOsuScreen)?.CursorVisible ?? false;

        private void onWindowDragDrop(string path)
        {
            lock (dragDropFiles)
            {
                dragDropFiles.Add(path);

                Logger.Log($@"Adding ""{Path.GetFileName(path)}"" for import");

                // File drag drop operations can potentially trigger hundreds or thousands of these calls on some platforms.
                // In order to avoid spawning multiple import tasks for a single drop operation, debounce a touch.
                dragDropImportSchedule?.Cancel();
                dragDropImportSchedule = Scheduler.AddDelayed(handlePendingDragDropImports, 100);
            }

            void handlePendingDragDropImports()
            {
                lock (dragDropFiles)
                {
                    Logger.Log($"Handling batch import of {dragDropFiles.Count} files");

                    string[] paths = dragDropFiles.ToArray();
                    dragDropFiles.Clear();

                    Task.Factory.StartNew(() => Import(paths), TaskCreationOptions.LongRunning);
                }
            }
        }

        private Task asyncLoadStream = Task.CompletedTask;

        private T loadComponentSingleFile<T>(T component, Action<Drawable> loadCompleteAction, bool cache = false)
            where T : class
        {
            if (cache)
                dependencies.CacheAs(component);

            var drawableComponent = component as Drawable ?? throw new ArgumentException($"Component must be a {nameof(Drawable)}", nameof(component));

            // schedule is here to ensure that all component loads are done after LoadComplete is run (and thus all dependencies are cached).
            // with some better organisation of LoadComplete to do construction and dependency caching in one step, followed by calls to loadComponentSingleFile,
            // we could avoid the need for scheduling altogether.
            Schedule(() =>
            {
                var previousLoadStream = asyncLoadStream;

                // chain with existing load stream
                asyncLoadStream = Task.Run(async () =>
                {
                    if (previousLoadStream != null)
                        await previousLoadStream.ConfigureAwait(false);

                    try
                    {
                        Logger.Log($"Loading {component}...");

                        // Since this is running in a separate thread, it is possible for the game to be disposed after LoadComponentAsync has been called
                        // throwing an exception. To avoid this, the call is scheduled on the update thread, which does not run if IsDisposed = true
                        Task? task = null;
                        var del = new ScheduledDelegate(() => task = LoadComponentAsync(drawableComponent, loadCompleteAction));
                        Scheduler.Add(del);

                        // The delegate won't complete if the game has been disposed in the meantime
                        while (!IsDisposed && !del.Completed)
                            await Task.Delay(10).ConfigureAwait(false);

                        // Either we're disposed or the load process has started successfully
                        if (IsDisposed)
                            return;

                        Debug.Assert(task != null);

                        await task.ConfigureAwait(false);

                        Logger.Log($"Loaded {component}!");
                    }
                    catch (OperationCanceledException)
                    {
                    }
                });
            });

            return component;
        }

        public bool OnPressed(KeyBindingPressEvent<GlobalAction> e)
        {
            switch (e.Action)
            {
                case GlobalAction.DecreaseVolume:
                case GlobalAction.IncreaseVolume:
                    return volume.Adjust(e.Action);
            }

            if (e.Repeat)
                return false;

            switch (e.Action)
            {
                case GlobalAction.ToggleMute:
                case GlobalAction.NextVolumeMeter:
                case GlobalAction.PreviousVolumeMeter:
                    return volume.Adjust(e.Action);
            }

            return false;
        }

        public void OnReleased(KeyBindingReleaseEvent<GlobalAction> e)
        {
        }

        protected override bool OnExiting()
        {
            if (ScreenStack.CurrentScreen is Loader)
                return false;

            // Route window close requests through the screen stack so that the editor gets a chance to prompt for unsaved changes.
            if (beatmapSelect != null && ScreenStack.CurrentScreen != beatmapSelect)
            {
                Scheduler.Add(beatmapSelect.MakeCurrent);
                return true;
            }

            return base.OnExiting();
        }

        protected override void Dispose(bool isDisposing)
        {
            base.Dispose(isDisposing);

            if (Host?.Window != null)
                Host.Window.DragDrop -= onWindowDragDrop;
        }

        private partial class EditorOnlyLoader : Loader
        {
            private readonly Func<OsuScreen> createScreen;

            public EditorOnlyLoader(Func<OsuScreen> createScreen)
            {
                this.createScreen = createScreen;
            }

            protected override OsuScreen CreateLoadableScreen() => createScreen();
        }
    }
}
