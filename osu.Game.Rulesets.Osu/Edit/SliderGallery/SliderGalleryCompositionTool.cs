// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Graphics;
using osu.Framework.Graphics.Sprites;
using osu.Game.Rulesets.Edit;
using osu.Game.Rulesets.Edit.Tools;

namespace osu.Game.Rulesets.Osu.Edit.SliderGallery
{
    /// <summary>
    /// Places shapes from the slider gallery. Clicking its button opens the gallery to pick a shape.
    /// </summary>
    public class SliderGalleryCompositionTool : CompositionTool<OsuAction>
    {
        /// <summary>
        /// Set by the composer once it has loaded (tools are created before that).
        /// </summary>
        public SliderGallerySession? Session { get; set; }

        public SliderGalleryCompositionTool()
            : base("Gallery")
        {
            TooltipText = """
                Places a shape from the slider gallery (Ctrl+Shift+G).
                Left click to place.
                Ctrl+, / Ctrl+. to rotate (with Shift: by 15°).
                Ctrl+H / Ctrl+J to flip.
                """;
        }

        public override Drawable CreateIcon() => new SpriteIcon { Icon = FontAwesome.Solid.Shapes };

        public override HitObjectPlacementBlueprint? CreatePlacementBlueprint()
        {
            if (Session?.ActiveShape.Value == null)
                return null;

            return new SliderGalleryPlacementBlueprint(Session);
        }

        // opening on click rather than on selection, so clicking the button again (tool already selected) opens the gallery again.
        public override void OnButtonClicked() => Session?.OpenRequested?.Invoke();
    }
}
