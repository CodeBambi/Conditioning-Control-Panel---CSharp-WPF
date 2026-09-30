using System;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Lab.GazeMinigame;

namespace ConditioningControlPanel.Services
{
    /// <summary>What the Blink Trainer's status row says, in priority order.</summary>
    public enum BlinkTrainerStatusState
    {
        IdleReady,
        Running,
        NeedsConsent,
        NeedsFolders,
        NeedsCalibration,
        Error,
    }

    /// <summary>
    /// The Blink Trainer page's head-free decisions. Lifted out of the WPF head's
    /// MainWindow.BlinkTrainer.cs (DetermineBlinkTrainerStatusState and the folder card's count
    /// line), which now delegates here, so every head answers "why won't it start?" the same way.
    /// The inputs that need a camera or a screen list are the caller's to supply.
    /// </summary>
    public static class BlinkTrainerState
    {
        public static BlinkTrainerStatusState Status(bool running, string? lastError, bool consentCurrent,
            int folderCount, bool multiMonitor, bool calibrationUsable)
        {
            if (running) return BlinkTrainerStatusState.Running;
            // The service exposes LastError as a non-empty string after a failure.
            if (!string.IsNullOrEmpty(lastError)) return BlinkTrainerStatusState.Error;
            if (!consentCurrent) return BlinkTrainerStatusState.NeedsConsent;
            if (folderCount == 0) return BlinkTrainerStatusState.NeedsFolders;
            if (multiMonitor && !calibrationUsable) return BlinkTrainerStatusState.NeedsCalibration;
            return BlinkTrainerStatusState.IdleReady;
        }

        /// <summary>A folder card's count summary, or null for an empty/invalid folder
        /// (the caller shows <c>blink_trainer_folder_empty_or_invalid</c>).</summary>
        public static string? FolderCountLine(AssetPack? pack, bool includeVideos)
        {
            if (pack == null) return null;
            if (includeVideos) return $"{pack.ImagePaths.Count} images, {pack.VideoPaths.Count} videos";
            int gifs = pack.ImagePaths.Count(p => Path.GetExtension(p).Equals(".gif", StringComparison.OrdinalIgnoreCase));
            return gifs > 0 ? $"{pack.ImagePaths.Count - gifs} images, {gifs} GIFs" : $"{pack.ImagePaths.Count} images";
        }

        /// <summary>Cap on how many tiles an overlay lays out (keeps decode cost sane on extreme aspects).</summary>
        public const int MaxTiles = 6;

        /// <summary>
        /// The session overlay's tile grid for an image on a screen: a portrait image on a wide screen
        /// tiles across, a panorama tiles down, and aspects within 5% show once (Uniform fit). Lifted
        /// out of WPF BlinkTrainerService.ApplyAsset/ApplyAssetMixed, which delegate here.
        /// </summary>
        public static (int Cols, int Rows) TileGrid(double imageAspect, double screenAspect)
        {
            int cols = 1, rows = 1;
            if (imageAspect > 0)
            {
                if (imageAspect < screenAspect * 0.95)
                    cols = Math.Min(MaxTiles, Math.Max(1, (int)Math.Ceiling(screenAspect / imageAspect)));
                else if (imageAspect > screenAspect * 1.05)
                    rows = Math.Min(MaxTiles, Math.Max(1, (int)Math.Ceiling(imageAspect / screenAspect)));
            }
            return (cols, rows);
        }

        /// <summary>A folder card's title: the basename, else the directory name, else the path.</summary>
        public static string FolderDisplayName(string folder)
        {
            string name;
            try
            {
                name = Path.GetFileName(folder);
                if (string.IsNullOrWhiteSpace(name)) name = new DirectoryInfo(folder).Name;
            }
            catch { name = folder; }
            return string.IsNullOrWhiteSpace(name) ? folder : name;
        }
    }
}
