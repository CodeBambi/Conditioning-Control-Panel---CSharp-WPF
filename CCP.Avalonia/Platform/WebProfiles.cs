using System;
using System.Collections.Generic;
using System.IO;

namespace ConditioningControlPanel.Avalonia.Platform
{
    /// <summary>
    /// Where each web view keeps its WebView2 profile on Windows: a folder under UserData with the
    /// SAME name WPF 7.1.5 gives that surface, so cookies and sign-ins (HypnoTube, BambiCloud, the
    /// age checks) survive the upgrade from WPF, and an update or a mirrored deploy of the install
    /// folder never wipes them (WebView2's default is "&lt;exe&gt;.WebView2" beside the exe).
    /// One browser-argument string serves every folder (<c>WebHost.WindowsBrowserArguments</c>):
    /// WebView2 refuses a second environment on a folder started with different switches.
    /// </summary>
    internal static class WebProfiles
    {
        /// <summary>WPF BrowserService.cs:128: the panel browser. Also the Leash video window
        /// (LeashPunishWindow.cs:278 shares it for the HypnoTube age-check cookie) and the default.</summary>
        public const string Browser = "browser_data";
        /// <summary>WPF DeeperEditorWindow.xaml.cs:501.</summary>
        public const string DeeperEditor = "browser_data_deeper_editor";
        /// <summary>WPF EnhancementPlayerWindow.xaml.cs:1467.</summary>
        public const string DeeperPlayer = "browser_data_deeper_player";
        /// <summary>WPF RaceCloudWindow.cs:202.</summary>
        public const string BambiCloud = "browser_data_bambicloud";
        /// <summary>WPF SpiralEmbedView.cs:63.</summary>
        public const string Spiral = "browser_data_spiral";

        /// <summary>WPF's per-game folders (each host service's UserDataFolderName).</summary>
        private static readonly Dictionary<string, string> Games = new(StringComparer.OrdinalIgnoreCase)
        {
            ["arcademy"] = "arcademy",                        // ArcademyHostService.cs:283
            ["backroom"] = "backroom",                        // BackRoomHostService.cs:360
            ["breakout"] = "backroom",                        // a Back Room station: the room's profile
            ["breakoutdemo"] = "backroom",
            ["dtrh"] = "browser_data_dtrh",                   // DtrhHostService.cs:153
            ["goon"] = "browser_data_goon",                   // GoonHostService.cs:284
            ["piecebypiece"] = "browser_data_piecebypiece",   // PieceByPieceHostService.cs:226
            ["race"] = "browser_data_race",                   // CaucusHostService.cs:194
            ["intake"] = "browser_data_intake",               // IntakeHostService.cs:136
            ["justdrop"] = "browser_data_justdrop",           // JustDropHostService.cs:118
            ["fyp"] = "browser_data_fyp",                     // FypHostService.cs:101
            ["bureau"] = "browser_data_bureau",               // BureauHostService.cs:74
            ["loom"] = "browser_data_loom",                   // LoomHostService.cs:59
            ["emergency-exit"] = "emergency-exit",            // EmergencyExitHostService.cs:152
        };

        /// <summary>The profile folder name for a game id; an unknown id gets a folder of its own.</summary>
        public static string ForGame(string? id)
        {
            if (!string.IsNullOrWhiteSpace(id) && Games.TryGetValue(id!, out var name)) return name;
            var safe = new string(Array.FindAll((id ?? "").ToLowerInvariant().ToCharArray(),
                c => (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '-' || c == '_'));
            return safe.Length == 0 ? Browser : "browser_data_" + safe;
        }

        /// <summary>The folder for a profile name, always inside UserData (CCP_USERDATA_DIR moves it
        /// with the rest of the profile). A name that tries to leave the folder falls back to the default.</summary>
        public static string FolderFor(string? profile) => FolderFor(CorePaths.UserData, profile);

        internal static string FolderFor(string userData, string? profile)
        {
            var name = string.IsNullOrWhiteSpace(profile) ? Browser : profile!.Trim();
            if (name.IndexOfAny(new[] { '/', '\\', ':' }) >= 0 || name.Contains("..", StringComparison.Ordinal)) name = Browser;
            return Path.Combine(userData, name);
        }
    }
}
