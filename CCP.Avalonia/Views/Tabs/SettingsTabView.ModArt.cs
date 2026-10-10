// PORTED from ConditioningControlPanel/MainWindow/MainWindow.xaml.cs LoadFeatureImages (:2706) and
// ModTileVariant (:2667), the Home half: the mosaic's tile art follows the active mod. The paths are the
// mod compatibility surface and are never renamed. A null resolve leaves the tile's art alone.
// The description images on other tabs repaint themselves (their own ApplyFeatureArt / ModChanged hooks).

using System;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Models;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Tabs
{
    public partial class SettingsTabView
    {
        // WPF TileDecodeWidth / WideTileDecodeWidth: the tile art is 1376 px wide and a card paints a
        // fraction of that.
        private const int TileDecodeWidth = 768;
        private const int WideTileDecodeWidth = 1024;

        /// <summary>WPF ModTileVariant's naming: built-in mods get app-shipped themed faces by filename
        /// (features/mysterybox_bambi.png); every other mod, and CCP Default, reads the base path.</summary>
        internal static string? TileVariantPath(string baseName, string? modId)
        {
            var suffix = modId switch
            {
                BuiltInMods.BambiSleepId => "_bambi",
                BuiltInMods.SissyHypnoId => "_sissy",
                BuiltInMods.DronificationId => "_drone",
                BuiltInMods.LockedId => "_locked",
                _ => null,
            };
            return suffix == null ? null : $"features/{baseName}{suffix}.png";
        }

        private static global::Avalonia.Media.Imaging.Bitmap? ModTileVariant(string baseName, int decodeWidth)
        {
            if (TileVariantPath(baseName, CoreMods.ActiveModId) is { } themedPath && ModArt.TryLoad(themedPath, decodeWidth) is { } themed)
                return themed;
            return ModArt.TryLoad($"features/{baseName}.png", decodeWidth);
        }

        /// <summary>Repaints every Home tile from the active mod. Called by the shell on a mod switch.</summary>
        internal void LoadFeatureImages()
        {
            try
            {
                var cards = new (string Path, Features.FeatureCard? Card)[]
                {
                    ("features/flash.png", CardFlash),
                    ("features/subliminal.png", CardSubliminal),
                    ("features/bouncing_text.png", CardBouncingText),
                    ("features/Bubble_pop.png", CardBubblePop),
                    ("features/Phrase_Lock.png", CardLockCard),
                    ("features/deeper_editor.png", CardDeeperEditor),
                };
                foreach (var (path, card) in cards)
                {
                    if (card == null) continue;
                    if (ModArt.TryLoad(path, TileDecodeWidth) is { } art) card.Icon = art;
                }

                if (CardMystery != null && ModTileVariant("mysterybox", TileDecodeWidth) is { } box) CardMystery.Icon = box;
                if (CardVault != null && ModTileVariant("vault", WideTileDecodeWidth) is { } vault) CardVault.Icon = vault;

                // The three diagonal tiles: per-half art, so mods reskin each half.
                var splits = new (Features.SplitFeatureCard? Card, string A, string B)[]
                {
                    (ComboVideoBubble, "features/mandatory_videos.png", "features/Bubble_count.png"),
                    (ComboSpiralPink, "features/spiral_overlay.png", "features/Pink_filter.png"),
                    (ComboMindDrain, "features/Mind_Wipers.png", "features/brain_drain.png"),
                };
                foreach (var (card, a, b) in splits)
                {
                    if (card == null) continue;
                    if (ModArt.TryLoad(a, TileDecodeWidth) is { } artA) card.IconA = artA;
                    if (ModArt.TryLoad(b, TileDecodeWidth) is { } artB) card.IconB = artB;
                }
            }
            catch (Exception ex) { Log.Warning(ex, "Home: tile art repaint failed"); }
        }
    }
}
