// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Audio.Sample;
using osu.Framework.Extensions;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Cursor;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.IO.Stores;
using osu.Framework.Platform;
using osu.Game.Audio;
using osu.Game.Beatmaps;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterface;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Overlays;
using osu.Game.Utils;
using osuTK;

namespace osu.Game.Screens.Edit.Hitsounds
{
    /// <summary>
    /// Shows the sample files of the selected sample set (per bank and sample), and lets the user add, preview and remove custom files.
    /// Files are stored in the beatmap set under their legacy names (e.g. <c>soft-hitwhistle2.wav</c>), so they apply to every difficulty and survive saving.
    /// </summary>
    public partial class HitsoundSampleSetPanel : CompositeDrawable
    {
        public readonly Bindable<EditorBeatmapSkin.SampleSet?> Current = new Bindable<EditorBeatmapSkin.SampleSet?>();

        [Resolved]
        private EditorBeatmap editorBeatmap { get; set; } = null!;

        [Resolved]
        private BeatmapManager beatmaps { get; set; } = null!;

        [Resolved]
        private IBindable<WorkingBeatmap> working { get; set; } = null!;

        private readonly Dictionary<(string bank, string name), SampleCell> cells = new Dictionary<(string, string), SampleCell>();
        private readonly Bindable<DirectoryInfo?> lastDirectory = new Bindable<DirectoryInfo?>();

        private OsuSpriteText caption = null!;

        public HitsoundSampleSetPanel()
        {
            AutoSizeAxes = Axes.Y;
        }

        [BackgroundDependencyLoader]
        private void load(OverlayColourProvider colourProvider)
        {
            var rows = new FillFlowContainer
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Direction = FillDirection.Vertical,
                Spacing = new Vector2(4),
                Child = caption = new OsuSpriteText
                {
                    Font = OsuFont.GetFont(size: 12, weight: FontWeight.SemiBold),
                    Colour = colourProvider.Content2,
                },
            };

            var header = new FillFlowContainer { AutoSizeAxes = Axes.Both, Direction = FillDirection.Horizontal, Spacing = new Vector2(4) };
            header.Add(new Container { Size = new Vector2(46, 14) });

            foreach (string name in HitsoundLayout.NAMES)
                header.Add(new OsuSpriteText { Width = 70, Text = name.Replace("hit", string.Empty), Font = OsuFont.GetFont(size: 11), Colour = colourProvider.Content2 });

            rows.Add(header);

            foreach (string bank in HitsoundLayout.BANKS)
            {
                var row = new FillFlowContainer { AutoSizeAxes = Axes.Both, Direction = FillDirection.Horizontal, Spacing = new Vector2(4) };
                row.Add(new OsuSpriteText { Width = 46, Text = bank, Font = OsuFont.GetFont(size: 11), Colour = colourProvider.Content2, Anchor = Anchor.CentreLeft, Origin = Anchor.CentreLeft });

                foreach (string name in HitsoundLayout.NAMES)
                {
                    row.Add(cells[(bank, name)] = new SampleCell(this)
                    {
                        Width = 70,
                        Height = 26,
                        LastDirectory = { BindTarget = lastDirectory },
                    });
                }

                rows.Add(row);
            }

            InternalChild = rows;

            Current.BindValueChanged(_ => refresh(), true);

            if (editorBeatmap.BeatmapSkin != null)
                editorBeatmap.BeatmapSkin.BeatmapSkinChanged += scheduleRefresh;
        }

        private void scheduleRefresh() => Schedule(refresh);

        private void refresh()
        {
            var set = Current.Value;

            caption.Text = set == null ? string.Empty : $"{set.Name} - click + to add a sound file, click a sound to play it, right click to remove it";

            // the set's own file list can be stale, so look it up again.
            var files = editorBeatmap.BeatmapSkin?.GetAvailableSampleSets().FirstOrDefault(s => s.SampleSetIndex == set?.SampleSetIndex)?.Filenames ?? new HashSet<string>();

            foreach (((string bank, string name), var cell) in cells)
            {
                cell.ExpectedFilename = $"{bank}-{name}{HitsoundLane.SuffixOf(set?.SampleSetIndex ?? 1)}";
                cell.ActualFilename.Value = files.FirstOrDefault(f => Path.GetFileNameWithoutExtension(f) == cell.ExpectedFilename);
            }
        }

        private string addFile(FileInfo file, string expectedFilename)
        {
            string actual = string.Concat(expectedFilename, file.Extension.ToLowerInvariant());

            using (var stream = file.OpenRead())
                beatmaps.AddFile(working.Value.BeatmapSetInfo, stream, actual);

            return actual;
        }

        private void removeFile(string filename)
        {
            var file = working.Value.BeatmapSetInfo.GetFile(filename);

            if (file != null)
                beatmaps.DeleteFile(working.Value.BeatmapSetInfo, file);
        }

        protected override void Dispose(bool isDisposing)
        {
            if (editorBeatmap.BeatmapSkin != null)
                editorBeatmap.BeatmapSkin.BeatmapSkinChanged -= scheduleRefresh;

            base.Dispose(isDisposing);
        }

        private partial class SampleCell : OsuButton, IHasPopover, IHasContextMenu
        {
            public string ExpectedFilename = string.Empty;
            public readonly Bindable<string?> ActualFilename = new Bindable<string?>();
            public readonly Bindable<DirectoryInfo?> LastDirectory = new Bindable<DirectoryInfo?>();

            private readonly Bindable<FileInfo?> selectedFile = new Bindable<FileInfo?>();
            private readonly HitsoundSampleSetPanel panel;

            [Resolved]
            private OverlayColourProvider colourProvider { get; set; } = null!;

            public SampleCell(HitsoundSampleSetPanel panel)
                : base(null)
            {
                this.panel = panel;

                Action = () =>
                {
                    if (ActualFilename.Value == null)
                    {
                        selectedFile.Value = null;
                        this.ShowPopover();
                    }
                    else
                        panel.editorBeatmap.BeatmapSkin?.Skin.Samples?.Get(ActualFilename.Value)?.Play();
                };
            }

            protected override void LoadComplete()
            {
                base.LoadComplete();

                ActualFilename.BindValueChanged(_ =>
                {
                    BackgroundColour = ActualFilename.Value == null ? colourProvider.Background3 : colourProvider.Colour3;
                    Text = ActualFilename.Value == null ? "+" : "play";
                }, true);

                selectedFile.BindValueChanged(file =>
                {
                    if (file.NewValue == null)
                        return;

                    this.HidePopover();
                    ActualFilename.Value = panel.addFile(file.NewValue, ExpectedFilename);
                    LastDirectory.Value = file.NewValue.Directory;
                });
            }

            public Popover? GetPopover() => ActualFilename.Value == null
                ? new PreviewFileChooserPopover(selectedFile, LastDirectory.Value?.FullName)
                : null;

            public MenuItem[]? ContextMenuItems => ActualFilename.Value == null
                ? null
                : new MenuItem[]
                {
                    new OsuMenuItem("Remove", MenuItemType.Destructive, () =>
                    {
                        panel.removeFile(ActualFilename.Value!);
                        ActualFilename.Value = null;
                    }),
                };
        }

        /// <summary>
        /// A file chooser which previews the audio file you pick, and only adds it once confirmed.
        /// </summary>
        private partial class PreviewFileChooserPopover : FormFileSelector.FileChooserPopover
        {
            private readonly Bindable<FileInfo?> target;

            [Resolved]
            private AudioManager audio { get; set; } = null!;

            private FileInfo? pending;
            private ISampleStore? store;
            private Container bar = null!;
            private OsuSpriteText name = null!;

            public PreviewFileChooserPopover(Bindable<FileInfo?> target, string? chooserPath)
                : base(SupportedExtensions.AUDIO_EXTENSIONS, target, chooserPath, false)
            {
                this.target = target;
            }

            [BackgroundDependencyLoader]
            private void load(OverlayColourProvider colourProvider)
            {
                Add(bar = new Container
                {
                    Anchor = Anchor.BottomCentre,
                    Origin = Anchor.BottomCentre,
                    RelativeSizeAxes = Axes.X,
                    Height = 44,
                    Alpha = 0,
                    Children = new Drawable[]
                    {
                        new Box { RelativeSizeAxes = Axes.Both, Colour = colourProvider.Background5 },
                        new FillFlowContainer
                        {
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.CentreLeft,
                            AutoSizeAxes = Axes.Both,
                            Direction = FillDirection.Horizontal,
                            Spacing = new Vector2(8),
                            Padding = new MarginPadding { Left = 10 },
                            Children = new Drawable[]
                            {
                                name = new TruncatingSpriteText { Width = 300, Anchor = Anchor.CentreLeft, Origin = Anchor.CentreLeft },
                                new RoundedButton { Width = 80, Height = 28, Text = "play", Action = play },
                                new RoundedButton { Width = 120, Height = 28, Text = "use this file", Action = () => target.Value = pending },
                            },
                        },
                    },
                });
            }

            // picking a file previews it instead of committing, so that you can listen through the folder.
            protected override void OnFileSelected(FileInfo? file)
            {
                if (file == null)
                    return;

                pending = file;
                name.Text = file.Name;
                bar.FadeIn(100);

                store?.Dispose();
                store = audio.GetSampleStore(new StorageBackedResourceStore(new NativeStorage(file.DirectoryName!)));

                // samples load asynchronously, so give this one a moment before the first play.
                Scheduler.AddDelayed(play, 150);
            }

            private void play() => store?.Get(pending?.Name)?.Play();

            protected override void Dispose(bool isDisposing)
            {
                store?.Dispose();
                base.Dispose(isDisposing);
            }
        }
    }
}
