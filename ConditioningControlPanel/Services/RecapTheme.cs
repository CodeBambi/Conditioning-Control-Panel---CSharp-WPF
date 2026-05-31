using System;
using System.Windows;
using System.Windows.Media;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// Recolors the Season Recap card palette from the active mod's accent. Overrides the
    /// mod-driven color keys in Application.Resources; the recap brushes (DynamicResource-bound,
    /// see Resources/Theme/SeasonRecapCard.xaml) recolor automatically — the same pattern the app
    /// uses for PinkColor (MainWindow.RefreshThemeAwareElements). Constant keys (void/panel darks,
    /// gold, ink, foil cyan) are left untouched for legibility and the holographic edge.
    /// Call at startup (after App.Mods is initialized) and on ModChanged.
    /// </summary>
    public static class RecapTheme
    {
        public static void ApplyForActiveMod()
        {
            var res = Application.Current?.Resources;
            if (res == null) return;
            try
            {
                // Derive light/dark FROM the accent — the mod's own light/dark fields fall back to
                // the base mod when undefined (most mods, incl. drone, only set AccentColor), which
                // would mismatch. Computing keeps the palette coherently mono-accent with depth.
                var (ar, ag, ab) = App.Mods?.GetAccentColorRgb() ?? ((byte)0xFF, (byte)0x69, (byte)0xB4);
                var accent = Color.FromRgb(ar, ag, ab);
                var light  = Lighten(accent, 0.45);
                var dark   = Darken(accent, 0.45);

                res["RecapViolet"]            = dark;    // deep primary accent
                res["RecapVioletLite"]        = light;   // light accent (text/glow)
                res["RecapMagenta"]           = accent;  // punchy accent
                res["RecapAvatarInner"]       = dark;
                // Card background gradient + window void: very dark accent tints so the whole
                // surface (not just the accents) reads as the mod color.
                res["RecapPanelMid"]          = Darken(accent, 0.80);
                res["RecapPanel"]             = Darken(accent, 0.88);
                res["RecapVoid"]              = Darken(accent, 0.94);
                res["RecapLine"]              = WithA(light,  0x2E);
                res["RecapStatFill"]          = WithA(light,  0x0F);
                res["RecapHeroTint"]          = WithA(accent, 0x3D);
                res["RecapVerdictTintTop"]    = WithA(accent, 0x24);
                res["RecapVerdictTintBottom"] = WithA(dark,   0x14);
                res["RecapEdgeTint"]          = WithA(accent, 0x45);
                res["RecapOgPillBg"]          = WithA(accent, 0x2E);
                res["RecapOgPillBorder"]      = WithA(accent, 0x73);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "RecapTheme: failed to apply mod palette");
            }
        }

        private static Color WithA(Color c, byte a) => Color.FromArgb(a, c.R, c.G, c.B);

        // Blend toward white / black by factor t (0..1).
        private static Color Lighten(Color c, double t) => Color.FromRgb(
            (byte)(c.R + (255 - c.R) * t), (byte)(c.G + (255 - c.G) * t), (byte)(c.B + (255 - c.B) * t));

        private static Color Darken(Color c, double t) => Color.FromRgb(
            (byte)(c.R * (1 - t)), (byte)(c.G * (1 - t)), (byte)(c.B * (1 - t)));
    }
}
