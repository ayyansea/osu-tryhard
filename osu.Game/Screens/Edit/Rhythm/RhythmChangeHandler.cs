// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.IO;
using System.Linq;
using System.Text;

namespace osu.Game.Screens.Edit.Rhythm
{
    /// <summary>
    /// Tracks changes to a rhythm blueprint's <see cref="EditorBeatmap"/>, allowing undo and redo independently of the actual beatmap.
    /// </summary>
    public partial class RhythmChangeHandler : EditorChangeHandler
    {
        private readonly EditorBeatmap rhythmBeatmap;

        public RhythmChangeHandler(EditorBeatmap rhythmBeatmap)
        {
            this.rhythmBeatmap = rhythmBeatmap;

            rhythmBeatmap.TransactionBegan += BeginChange;
            rhythmBeatmap.TransactionEnded += EndChange;
            rhythmBeatmap.SaveStateTriggered += SaveState;
        }

        protected override void WriteCurrentStateToStream(MemoryStream stream)
        {
            using (var writer = new StreamWriter(stream, Encoding.UTF8, 1024, true))
                RhythmSerializer.Write(rhythmBeatmap.HitObjects, writer);
        }

        protected override void ApplyStateChange(byte[] previousState, byte[] newState)
        {
            var hitObjects = RhythmSerializer.Read(Encoding.UTF8.GetString(newState));

            rhythmBeatmap.BeginChange();
            rhythmBeatmap.SelectedHitObjects.Clear();
            rhythmBeatmap.RemoveRange(rhythmBeatmap.HitObjects.ToArray());
            rhythmBeatmap.AddRange(hitObjects);
            rhythmBeatmap.EndChange();
        }
    }
}
