// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

#nullable disable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Platform;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.Formats;
using osu.Game.Configuration;
using osu.Game.Extensions;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Overlays;
using osu.Game.Rulesets.Edit;
using osu.Game.Rulesets.Edit.Checks.Components;
using osuTK;

namespace osu.Game.Screens.Edit.Verify
{
    [Cached]
    public partial class IssueList : CompositeDrawable
    {
        private IssueTable table;

        [Resolved]
        private IBindable<WorkingBeatmap> workingBeatmap { get; set; }

        [Resolved]
        private EditorBeatmap beatmap { get; set; }

        [Resolved]
        private VerifyScreen verify { get; set; }

        [Resolved]
        private BeatmapManager beatmapManager { get; set; }

        [Resolved]
        private OsuConfigManager config { get; set; }

        [Resolved]
        private Storage storage { get; set; }

        /// <summary>
        /// Which checks produced the issues, or what went wrong with them.
        /// </summary>
        public string Source => source?.Text.ToString();

        private OsuSpriteText source;
        private CancellationTokenSource mapsetVerifierRun;
        private bool mapsetVerifierRunning;

        /// <summary>
        /// The issues Mapset Verifier found in the last run, or null if the built-in checks are used (Mapset Verifier isn't installed, or failed).
        /// </summary>
        private List<Issue> mapsetVerifierIssues;

        private IBeatmapVerifier rulesetVerifier;
        private BeatmapVerifier generalVerifier;
        private BeatmapVerifierContext context;

        [BackgroundDependencyLoader]
        private void load(OverlayColourProvider colours)
        {
            generalVerifier = new BeatmapVerifier();
            rulesetVerifier = beatmap.BeatmapInfo.Ruleset.CreateInstance().CreateBeatmapVerifier();

            context = BeatmapVerifierContext.Create(
                beatmap,
                workingBeatmap.Value,
                verify.InterpretedDifficulty.Value,
                beatmapManager
            );

            verify.InterpretedDifficulty.BindValueChanged(difficulty => context.InterpretedDifficulty = difficulty.NewValue);

            RelativeSizeAxes = Axes.Both;

            InternalChildren = new Drawable[]
            {
                new Box
                {
                    Colour = colours.Background3,
                    RelativeSizeAxes = Axes.Both,
                },
                table = new IssueTable
                {
                    RelativeSizeAxes = Axes.Both,
                },
                source = new OsuSpriteText
                {
                    Anchor = Anchor.BottomLeft,
                    Origin = Anchor.BottomLeft,
                    Margin = new MarginPadding(20),
                    Font = OsuFont.GetFont(size: 14),
                },
                new FillFlowContainer
                {
                    AutoSizeAxes = Axes.Both,
                    Anchor = Anchor.BottomRight,
                    Origin = Anchor.BottomRight,
                    Margin = new MarginPadding(20),
                    Children = new Drawable[]
                    {
                        new RoundedButton
                        {
                            Text = "Refresh",
                            Action = Refresh,
                            Size = new Vector2(120, 40),
                            Anchor = Anchor.BottomRight,
                            Origin = Anchor.BottomRight,
                        },
                    }
                },
            };
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            // Mapset Verifier has no interpreted difficulty, and its result doesn't change with the filters, so these only show it differently.
            verify.InterpretedDifficulty.BindValueChanged(_ => refilter());
            verify.VerifyChecksScope.BindValueChanged(_ => refilter());
            verify.HiddenIssueTypes.BindCollectionChanged((_, _) => refilter());

            Refresh();
        }

        /// <summary>
        /// Runs the checks again: with Mapset Verifier if it's installed, otherwise with the built-in checks.
        /// </summary>
        public void Refresh()
        {
            mapsetVerifierRun?.Cancel();
            mapsetVerifierIssues = null;
            mapsetVerifierRunning = false;

            string app = MapsetVerifierServer.FindApp(config.Get<string>(OsuSetting.MapsetVerifierPath));

            if (app == null)
            {
                source.Text = "Built-in checks (Mapset Verifier not found, set MapsetVerifierPath in game.ini)";
                showBuiltIn();
                return;
            }

            runMapsetVerifier(app);
        }

        private void refilter()
        {
            if (mapsetVerifierIssues != null)
                show(mapsetVerifierIssues);
            else if (!mapsetVerifierRunning)
                showBuiltIn();
        }

        private void showBuiltIn()
        {
            var issues = generalVerifier.Run(context);

            if (rulesetVerifier != null)
                issues = issues.Concat(rulesetVerifier.Run(context));

            show(issues);
        }

        private void show(IEnumerable<Issue> issues)
        {
            table.Issues.Clear();
            table.Issues.AddRange(filter(issues)
                                  .OrderBy(issue => issue.Template.Type)
                                  .ThenBy(issue => issue.Check.Metadata.Category)
                                  .ThenBy(issue => issue.Check.Metadata.Description));
        }

        private void runMapsetVerifier(string app)
        {
            var cancellation = mapsetVerifierRun = new CancellationTokenSource();
            mapsetVerifierRunning = true;
            source.Text = "Running Mapset Verifier...";

            // Mapset Verifier reads a folder, so the set is written to one. The difficulty being edited comes from memory so that it includes unsaved changes.
            var working = workingBeatmap.Value;
            string current = beatmap.BeatmapInfo.Path ?? $"{beatmap.BeatmapInfo.DifficultyName}.osu";

            var stream = new MemoryStream();

            using (var writer = new StreamWriter(stream, Encoding.UTF8, 1024, true))
                new LegacyBeatmapEncoder(beatmap.PlayableBeatmap, beatmap.BeatmapSkin, beatmap.Storyboard).Encode(writer);

            byte[] currentFile = stream.ToArray();

            var files = working.BeatmapSetInfo.Files
                               .Where(f => f.Filename != current)
                               .Select(f => (f.Filename, (Func<Stream>)(() => working.GetStream(f.File.GetStoragePath()))))
                               .Append((current, () => new MemoryStream(currentFile)))
                               .ToList();

            string directory = Path.Combine(Path.GetTempPath(), "osu-tryhard-verify", working.BeatmapSetInfo.ID.ToString());
            string cacheDirectory = storage.GetFullPath("mapset-verifier");

            Task.Run(() =>
            {
                MapsetVerifierServer.WriteFolder(directory, files);
                return MapsetVerifierServer.RunChecksAsync(app, cacheDirectory, directory, cancellation.Token);
            }, cancellation.Token).ContinueWith(task => Schedule(() =>
            {
                if (cancellation.IsCancellationRequested)
                    return;

                mapsetVerifierRunning = false;

                try
                {
                    if (task.IsFaulted || task.IsCanceled)
                        throw task.Exception?.GetBaseException() ?? new OperationCanceledException();

                    mapsetVerifierIssues = MapsetVerifierIssues.Parse(task.GetResultSafely(), beatmap.BeatmapInfo.DifficultyName, beatmap.HitObjects.ToList());
                    source.Text = "Checked with Mapset Verifier";
                    show(mapsetVerifierIssues);
                }
                catch (Exception e)
                {
                    source.Text = $"Mapset Verifier failed ({e.Message}), showing the built-in checks";
                    showBuiltIn();
                }
            }), CancellationToken.None);
        }

        protected override void Dispose(bool isDisposing)
        {
            base.Dispose(isDisposing);
            mapsetVerifierRun?.Cancel();
        }

        private IEnumerable<Issue> filter(IEnumerable<Issue> issues)
        {
            return issues.Where(issue =>
                !verify.HiddenIssueTypes.Contains(issue.Template.Type) &&
                issue.Check.Metadata.Scope == verify.VerifyChecksScope.Value);
        }
    }
}
