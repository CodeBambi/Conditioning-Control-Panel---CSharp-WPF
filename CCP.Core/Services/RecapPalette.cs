using System.Collections.Generic;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// The Season Recap card's mod-driven palette, derived from one accent colour. Both heads'
    /// RecapTheme write these keys into their resources; the math lives here once.
    /// Light/dark are computed FROM the accent (most mods only set AccentColor, and their own
    /// light/dark fields fall back to the base mod, which would mismatch). Constant keys (void
    /// gold, ink, foil cyan) are not in this list.
    /// </summary>
    public static class RecapPalette
    {
        public static IReadOnlyList<(string Key, byte A, byte R, byte G, byte B)> For(byte r, byte g, byte b)
        {
            var accent = (r, g, b);
            var light = Lighten(accent, 0.45);
            var dark = Darken(accent, 0.45);
            return new[]
            {
                Solid("RecapViolet", dark),
                Solid("RecapVioletLite", light),
                Solid("RecapMagenta", accent),
                Solid("RecapAvatarInner", dark),
                Solid("RecapPanelMid", Darken(accent, 0.80)),
                Solid("RecapPanel", Darken(accent, 0.88)),
                Solid("RecapVoid", Darken(accent, 0.94)),
                Tint("RecapLine", light, 0x2E),
                Tint("RecapStatFill", light, 0x0F),
                Tint("RecapHeroTint", accent, 0x3D),
                Tint("RecapVerdictTintTop", accent, 0x24),
                Tint("RecapVerdictTintBottom", dark, 0x14),
                Tint("RecapEdgeTint", accent, 0x45),
                Tint("RecapOgPillBg", accent, 0x2E),
                Tint("RecapOgPillBorder", accent, 0x73),
            };
        }

        private static (string, byte, byte, byte, byte) Solid(string key, (byte R, byte G, byte B) c) => (key, 0xFF, c.R, c.G, c.B);
        private static (string, byte, byte, byte, byte) Tint(string key, (byte R, byte G, byte B) c, byte a) => (key, a, c.R, c.G, c.B);

        // Blend toward white / black by factor t (0..1).
        private static (byte, byte, byte) Lighten((byte R, byte G, byte B) c, double t) =>
            ((byte)(c.R + (255 - c.R) * t), (byte)(c.G + (255 - c.G) * t), (byte)(c.B + (255 - c.B) * t));

        private static (byte, byte, byte) Darken((byte R, byte G, byte B) c, double t) =>
            ((byte)(c.R * (1 - t)), (byte)(c.G * (1 - t)), (byte)(c.B * (1 - t)));
    }
}
