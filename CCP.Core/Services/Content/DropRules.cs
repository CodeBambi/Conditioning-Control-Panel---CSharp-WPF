using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Services.Deeper;

namespace ConditioningControlPanel.Services
{
    /// <summary>What a window-wide file drop is. Lifted from WPF MainWindow.SessionIO.cs so both
    /// heads classify a drop with one rule set.</summary>
    public enum DropType { None, Session, Preset, Assets, Zip, Folder, Enhancement, Mod, Unrecognised }

    /// <summary>
    /// Pure rules for a file drop on the main window (WPF DetectDropType, SessionIO.cs:1555).
    /// The order matters: session and preset by name/content first, a .ccpmod before the zip
    /// branch (a mod IS a zip but installs), enhancements before folders and assets.
    /// </summary>
    public static class DropRules
    {
        public static readonly IReadOnlySet<string> AssetVideoExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".mp4", ".avi", ".mkv", ".mov", ".wmv", ".webm", ".m4v", ".flv", ".mpeg", ".mpg", ".3gp"
        };

        public static readonly IReadOnlySet<string> AssetImageExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".tiff", ".tif"
        };

        // Deeper-playable subsets - narrower than AssetVideoExtensions because the player's
        // backends only handle these. Used by the single-file drop prompt.
        public static readonly IReadOnlySet<string> DeeperVideoExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".mp4", ".webm", ".mkv", ".mov", ".avi", ".m4v"
        };

        public static readonly IReadOnlySet<string> DeeperAudioExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".mp3", ".wav", ".m4a", ".aac", ".flac", ".ogg"
        };

        public static bool IsDeeperPlayableMedia(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            var ext = Path.GetExtension(path);
            return DeeperVideoExtensions.Contains(ext) || DeeperAudioExtensions.Contains(ext);
        }

        public static DropType Detect(string[] files)
        {
            if (files.Length == 0) return DropType.None;

            if (files.Length == 1 && files[0].EndsWith(".session.json", StringComparison.OrdinalIgnoreCase))
                return DropType.Session;

            // The name is the fast path; any other .json is sniffed (ccp-bugs #1331).
            if (files.Length == 1 && File.Exists(files[0]) && PresetDropRules.FileIsPreset(files[0]))
                return DropType.Preset;

            if (files.Length == 1 && files[0].EndsWith(".ccpmod", StringComparison.OrdinalIgnoreCase))
                return DropType.Mod;

            if (files.All(EnhancementImportRules.IsImportablePath)
                && !files.Any(f => f.EndsWith(".session.json", StringComparison.OrdinalIgnoreCase))
                && !files.Any(f => f.EndsWith(".preset.json", StringComparison.OrdinalIgnoreCase)))
            {
                // A lone plain .json that is neither a preset nor an enhancement gets the
                // generic "not recognised" toast, not the Deeper one.
                if (files.Length == 1
                    && !files[0].EndsWith(".ccpenh.json", StringComparison.OrdinalIgnoreCase)
                    && !EnhancementImportRules.FileLooksLikeEnhancement(files[0]))
                    return DropType.Unrecognised;
                return DropType.Enhancement;
            }

            if (files.Length == 1 && Directory.Exists(files[0]))
                return DropType.Folder;

            var hasZip = false;
            var hasAssets = false;
            foreach (var file in files)
            {
                if (Directory.Exists(file)) { hasAssets = true; continue; }
                var ext = Path.GetExtension(file);
                if (ext.Equals(".zip", StringComparison.OrdinalIgnoreCase)) hasZip = true;
                else if (AssetVideoExtensions.Contains(ext) || AssetImageExtensions.Contains(ext)) hasAssets = true;
            }

            if (hasZip) return DropType.Zip;
            if (hasAssets) return DropType.Assets;

            // Accepted so the drop says so in a toast instead of doing nothing.
            return DropType.Unrecognised;
        }
    }
}
