using System;
using System.Windows;
using System.Windows.Media;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// Single source of truth for ambient FX colour. Writes the Fx* Color + Brush keys into
    /// Application.Resources from the active mod's palette chain (fxPalette slot →
    /// theme.filterColor → theme.accentColor → #FF69B4) — the same "overwrite the app resource,
    /// every DynamicResource repaints" pattern as RecapTheme / MainWindow.RefreshThemeAwareElements.
    /// Skia surfaces read the Color getters below instead of binding.
    /// Call at startup (after App.Mods is initialized) and on ModChanged.
    /// </summary>
    public static class FxTheme
    {
        private static readonly Color Fallback = Color.FromRgb(0xFF, 0x69, 0xB4);

        public static void ApplyForActiveMod()
        {
            var res = Application.Current?.Resources;
            if (res == null) return;
            try
            {
                var mods = App.Mods;
                Set(res, "FxMist", FromRgb(mods?.GetMistColorRgb()));
                Set(res, "FxParticle", FromRgb(mods?.GetParticleColorRgb()));
                Set(res, "FxGlow", FromRgb(mods?.GetGlowColorRgb()));
                Set(res, "FxFlashTint", FromRgb(mods?.GetFlashTintRgb()));
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "FxTheme: failed to apply mod palette");
            }
        }

        /// <summary>Fog / aurora wash colour.</summary>
        public static Color MistColor => Read("FxMistColor");

        /// <summary>Dust / burst particle colour.</summary>
        public static Color ParticleColor => Read("FxParticleColor");

        /// <summary>Glow-breath and rim-light colour.</summary>
        public static Color GlowColor => Read("FxGlowColor");

        /// <summary>One-shot flash / sheen tint.</summary>
        public static Color FlashTintColor => Read("FxFlashTintColor");

        /// <summary>Mod-authored opacity multiplier for the mist layers (0-1, default 1).</summary>
        public static double MistOpacity
        {
            get
            {
                try { return App.Mods?.GetMistOpacity() ?? 1.0; } catch { return 1.0; }
            }
        }

        private static void Set(ResourceDictionary res, string stem, Color c)
        {
            res[stem + "Color"] = c;
            res[stem + "Brush"] = Freeze(new SolidColorBrush(c));
        }

        private static SolidColorBrush Freeze(SolidColorBrush b)
        {
            b.Freeze();
            return b;
        }

        private static Color FromRgb((byte R, byte G, byte B)? rgb) =>
            rgb is { } v ? Color.FromRgb(v.R, v.G, v.B) : Fallback;

        private static Color Read(string key)
        {
            try
            {
                if (Application.Current?.Resources[key] is Color c) return c;
            }
            catch { }
            return Fallback;
        }
    }
}
