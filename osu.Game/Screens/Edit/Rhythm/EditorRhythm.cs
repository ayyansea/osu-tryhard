// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.IO;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics.Containers;
using osu.Framework.Logging;
using osu.Framework.Platform;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Objects;

namespace osu.Game.Screens.Edit.Rhythm
{
    /// <summary>
    /// The rhythm blueprint of the beatmap being edited: a set of non-positional clicks and holds which can be used to lay out a map's rhythm
    /// before placing actual hit objects.
    /// </summary>
    /// <remarks>
    /// Rhythm objects are kept in their own <see cref="EditorBeatmap"/> (sharing timing with the actual beatmap),
    /// so that the editor's existing timeline selection and editing logic can be used on them.
    /// The blueprint is stored outside of the beatmap's files, keyed by the difficulty, so it never affects the actual beatmap.
    /// </remarks>
    public partial class EditorRhythm : CompositeDrawable
    {
        /// <summary>
        /// The beatmap containing the rhythm objects.
        /// </summary>
        public EditorBeatmap Beatmap { get; private set; } = null!;

        /// <summary>
        /// Tracks changes to <see cref="Beatmap"/> for undo / redo.
        /// </summary>
        public RhythmChangeHandler ChangeHandler { get; private set; } = null!;

        /// <summary>
        /// The serialised contents of the rhythm clipboard, if anything has been copied.
        /// </summary>
        public readonly Bindable<string?> Clipboard = new Bindable<string?>();

        /// <summary>
        /// Whether there are changes to the rhythm blueprint since it was last saved.
        /// </summary>
        public bool HasUnsavedChanges => ChangeHandler.CurrentStateHash != lastSavedHash;

        private readonly EditorBeatmap editorBeatmap;

        private Storage storage = null!;

        private string? lastSavedHash;

        public EditorRhythm(EditorBeatmap editorBeatmap)
        {
            this.editorBeatmap = editorBeatmap;
        }

        private string filename => $"{editorBeatmap.BeatmapInfo.ID}.txt";

        [BackgroundDependencyLoader]
        private void load(Storage gameStorage)
        {
            storage = gameStorage.GetStorageForDirectory("rhythm");

            var beatmap = new Beatmap
            {
                BeatmapInfo = new BeatmapInfo(editorBeatmap.BeatmapInfo.Ruleset, editorBeatmap.Difficulty.Clone(), new BeatmapMetadata()),
                ControlPointInfo = editorBeatmap.ControlPointInfo,
            };

            beatmap.HitObjects.AddRange(readFromStorage());

            AddInternal(Beatmap = new EditorBeatmap(beatmap));
            ChangeHandler = new RhythmChangeHandler(Beatmap);

            MarkSaved();
        }

        protected override void Update()
        {
            base.Update();

            // the actual beatmap's control points may be replaced entirely (e.g. on undo), so keep the rhythm's timing in sync.
            if (Beatmap.ControlPointInfo != editorBeatmap.ControlPointInfo)
                Beatmap.ControlPointInfo = editorBeatmap.ControlPointInfo;
        }

        /// <summary>
        /// Writes the rhythm blueprint to storage.
        /// </summary>
        public void Save()
        {
            try
            {
                if (Beatmap.HitObjects.Count == 0)
                    storage.Delete(filename);
                else
                {
                    using (var stream = storage.CreateFileSafely(filename))
                    using (var writer = new StreamWriter(stream))
                        RhythmSerializer.Write(Beatmap.HitObjects, writer);
                }

                MarkSaved();
            }
            catch (Exception e)
            {
                Logger.Error(e, "Failed to save rhythm blueprint");
            }
        }

        /// <summary>
        /// Marks the current state of the rhythm blueprint as saved.
        /// </summary>
        public void MarkSaved() => lastSavedHash = ChangeHandler.CurrentStateHash;

        private HitObject[] readFromStorage()
        {
            try
            {
                if (!storage.Exists(filename))
                    return Array.Empty<HitObject>();

                using (var stream = storage.GetStream(filename))
                using (var reader = new StreamReader(stream))
                    return RhythmSerializer.Read(reader).Cast<HitObject>().ToArray();
            }
            catch (Exception e)
            {
                Logger.Error(e, "Failed to load rhythm blueprint");
                return Array.Empty<HitObject>();
            }
        }
    }
}
