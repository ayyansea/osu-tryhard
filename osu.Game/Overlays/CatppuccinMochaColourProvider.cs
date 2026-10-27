// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Extensions.Color4Extensions;
using osuTK.Graphics;

namespace osu.Game.Overlays
{
    /// <summary>
    /// An <see cref="OverlayColourProvider"/> which uses the Catppuccin Mocha palette (https://catppuccin.com/palette)
    /// rather than shades generated from a single hue.
    /// </summary>
    public class CatppuccinMochaColourProvider : OverlayColourProvider
    {
        public static readonly Color4 ROSEWATER = Color4Extensions.FromHex(@"f5e0dc");
        public static readonly Color4 FLAMINGO = Color4Extensions.FromHex(@"f2cdcd");
        public static readonly Color4 PINK = Color4Extensions.FromHex(@"f5c2e7");
        public static readonly Color4 MAUVE = Color4Extensions.FromHex(@"cba6f7");
        public static readonly Color4 RED = Color4Extensions.FromHex(@"f38ba8");
        public static readonly Color4 MAROON = Color4Extensions.FromHex(@"eba0ac");
        public static readonly Color4 PEACH = Color4Extensions.FromHex(@"fab387");
        public static readonly Color4 YELLOW = Color4Extensions.FromHex(@"f9e2af");
        public static readonly Color4 GREEN = Color4Extensions.FromHex(@"a6e3a1");
        public static readonly Color4 TEAL = Color4Extensions.FromHex(@"94e2d5");
        public static readonly Color4 SKY = Color4Extensions.FromHex(@"89dceb");
        public static readonly Color4 SAPPHIRE = Color4Extensions.FromHex(@"74c7ec");
        public static readonly Color4 BLUE = Color4Extensions.FromHex(@"89b4fa");
        public static readonly Color4 LAVENDER = Color4Extensions.FromHex(@"b4befe");
        public static readonly Color4 TEXT = Color4Extensions.FromHex(@"cdd6f4");
        public static readonly Color4 SUBTEXT1 = Color4Extensions.FromHex(@"bac2de");
        public static readonly Color4 SUBTEXT0 = Color4Extensions.FromHex(@"a6adc8");
        public static readonly Color4 OVERLAY2 = Color4Extensions.FromHex(@"9399b2");
        public static readonly Color4 OVERLAY1 = Color4Extensions.FromHex(@"7f849c");
        public static readonly Color4 OVERLAY0 = Color4Extensions.FromHex(@"6c7086");
        public static readonly Color4 SURFACE2 = Color4Extensions.FromHex(@"585b70");
        public static readonly Color4 SURFACE1 = Color4Extensions.FromHex(@"45475a");
        public static readonly Color4 SURFACE0 = Color4Extensions.FromHex(@"313244");
        public static readonly Color4 BASE = Color4Extensions.FromHex(@"1e1e2e");
        public static readonly Color4 MANTLE = Color4Extensions.FromHex(@"181825");
        public static readonly Color4 CRUST = Color4Extensions.FromHex(@"11111b");

        public CatppuccinMochaColourProvider()
            : base(OverlayColourScheme.Purple)
        {
        }

        // Accents, from brightest to darkest. The darker variants are mauve blended towards the base colour,
        // so that they remain readable as backgrounds for light text (e.g. on buttons).
        public override Color4 Colour0 => LAVENDER;
        public override Color4 Colour1 => MAUVE;
        public override Color4 Colour2 => Color4Extensions.FromHex(@"a98ad6");
        public override Color4 Colour3 => Color4Extensions.FromHex(@"7d679f");
        public override Color4 Colour4 => Color4Extensions.FromHex(@"4f4569");

        public override Color4 Highlight1 => MAUVE;

        public override Color4 Content1 => TEXT;
        public override Color4 Content2 => SUBTEXT1;

        public override Color4 Light1 => SUBTEXT0;
        public override Color4 Light2 => OVERLAY2;
        public override Color4 Light3 => OVERLAY1;
        public override Color4 Light4 => OVERLAY0;

        public override Color4 Dark1 => SURFACE2;
        public override Color4 Dark2 => SURFACE1;
        public override Color4 Dark3 => SURFACE0;
        public override Color4 Dark4 => BASE;
        public override Color4 Dark5 => MANTLE;
        public override Color4 Dark6 => CRUST;

        public override Color4 Foreground1 => OVERLAY1;

        public override Color4 Background1 => SURFACE2;
        public override Color4 Background2 => SURFACE1;
        public override Color4 Background3 => SURFACE0;
        public override Color4 Background4 => BASE;
        public override Color4 Background5 => MANTLE;
        public override Color4 Background6 => CRUST;
    }
}
