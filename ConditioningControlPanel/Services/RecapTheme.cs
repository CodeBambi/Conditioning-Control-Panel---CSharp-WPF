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
                // The palette math is shared with the Avalonia head (CCP.Core RecapPalette).
                foreach (var (key, a, r, g, b) in RecapPalette.For(ar, ag, ab))
                    res[key] = Color.FromArgb(a, r, g, b);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "RecapTheme: failed to apply mod palette");
            }
        }
    }
}
