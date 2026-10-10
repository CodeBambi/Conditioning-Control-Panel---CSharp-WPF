// PORTED from ConditioningControlPanel/MainWindow/MainWindow.xaml.cs ApplyActiveModChange (:3127), the art
// half: LoadFeatureImages (:2706, the Home tiles, see SettingsTabView.ModArt.cs) and
// MainWindow.NavRail.cs ApplyDoorArt (:825), plus Services/ModResourceResolver.EmbeddedModTwin (:296).
//
// The colours (title bar rule, rank title, level chip, XP bar, banner lines) already follow the palette:
// every one of them is a {DynamicResource PinkBrush} here and RefreshThemeAwareElements rewrites that
// brush. What a mod switch still left on the old mod until a restart was PICTURES and the level readout.
//
// Not ported: the Home logo. WPF LoadLogo hands the dial a per-mod wordmark (SetArtwork); this head's
// AnimatedLogoDial draws one fixed artwork and has no such door yet.

using System;
using System.IO;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using ConditioningControlPanel.Avalonia.Helpers;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        /// <summary>WPF NavDoorArtDecodeWidth: 64 px native art in a 40 px icon, 2x headroom.</summary>
        private const int NavDoorArtDecodeWidth = 128;

        private static readonly (string Name, string Path)[] NavDoors =
        {
            ("ImgDoorHome", "nav/door_home.png"), ("ImgDoorStudio", "nav/door_studio.png"),
            ("ImgDoorCompanion", "nav/door_companion.png"), ("ImgDoorPlay", "nav/door_play.png"),
            ("ImgDoorSocial", "nav/door_social.png"), ("ImgDoorYou", "nav/door_you.png"),
            ("ImgDoorLibrary", "nav/door_library.png"), ("ImgDoorSettings", "nav/door_settings.png"),
        };

        /// <summary>True once a mod other than the one the XAML art was authored for has painted.</summary>
        private bool _modArtPainted;

        /// <summary>The pictures and readouts a mod switch repaints without a restart. At attach the XAML
        /// art already is CCP Default's, so a default profile decodes nothing.</summary>
        internal void RefreshModArt(bool atAttach = false)
        {
            try
            {
                if (atAttach && !_modArtPainted && (global::ConditioningControlPanel.Avalonia.App.Mods?.IsCCPDefault ?? true)) return;
                _modArtPainted = true;
                Named<Tabs.SettingsTabView>("SettingsTab")?.LoadFeatureImages();
                ApplyDoorArt();
                UpdateLevelDisplay();
                RefreshXPBarBonuses();
            }
            catch (Exception ex) { Log.Warning(ex, "Mod switch: art repaint failed"); }
        }

        /// <summary>WPF EmbeddedModTwin: nav/door_social.png under drone-mode is
        /// nav/mods/drone-mode/door_social.png. Null when the path is not nav art, there is no mod, or
        /// the mod id is not one safe path segment.</summary>
        internal static string? EmbeddedModTwin(string resourcePath, string? modId)
        {
            if (string.IsNullOrEmpty(modId) || string.IsNullOrEmpty(resourcePath)) return null;
            if (!resourcePath.StartsWith("nav/", StringComparison.Ordinal)) return null;
            if (resourcePath.StartsWith("nav/mods/", StringComparison.Ordinal)) return null;
            if (modId.Contains("..") || modId.Contains('/') || modId.Contains('\\') || Path.IsPathRooted(modId)) return null;
            return "nav/mods/" + modId + "/" + resourcePath.Substring("nav/".Length);
        }

        /// <summary>The pack's own file wins, the embedded per-mod twin is next, our default last.</summary>
        internal static Bitmap? ResolveDoorArt(string path, string? modId)
        {
            bool packHasIt = false;
            try { packHasIt = CoreModArt.OverridePath(path) is { } p && File.Exists(p); } catch { }
            if (!packHasIt && EmbeddedModTwin(path, modId) is { } twin && ModArt.TryLoad(twin, NavDoorArtDecodeWidth) is { } themed)
                return themed;
            return ModArt.TryLoad(path, NavDoorArtDecodeWidth);
        }

        /// <summary>WPF ApplyDoorArt: points the medallions at the active mod's art. A null resolve leaves
        /// the authored Source alone: an empty rail is the worst failure this file can produce.</summary>
        internal void ApplyDoorArt()
        {
            try
            {
                var modId = CoreMods.ActiveModId;
                foreach (var (name, path) in NavDoors)
                {
                    if (Named<Image>(name) is not { } img) continue;
                    if (ResolveDoorArt(path, modId) is { } art) img.Source = art;
                }
            }
            catch (Exception ex) { Log.Warning(ex, "ApplyDoorArt failed; nav rail keeps its embedded medallions"); }
        }
    }
}
