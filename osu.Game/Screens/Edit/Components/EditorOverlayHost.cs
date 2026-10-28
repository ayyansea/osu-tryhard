// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;

namespace osu.Game.Screens.Edit.Components
{
    /// <summary>
    /// Covers the whole editor, above its menu and bottom bar. Holds <see cref="EditorToolOverlay"/>s, including ones that belong to a screen
    /// (which adds them on load and removes them on disposal), so they dim the whole editor rather than just their screen.
    /// </summary>
    public partial class EditorOverlayHost : Container
    {
        public EditorOverlayHost()
        {
            RelativeSizeAxes = Axes.Both;
        }
    }
}
