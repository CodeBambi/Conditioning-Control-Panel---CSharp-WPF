using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ConditioningControlPanel.Lab.GazeMinigame
{
    /// <summary>
    /// A folder of conditioning assets the user has opted into for the minigame.
    /// Names match the folder basename; paths are absolute. Image and video
    /// lists are pre-resolved at construction time so per-round selection is a
    /// trivial random index.
    /// </summary>
    public sealed class AssetPack
    {
        public string Name { get; init; } = "";
        public string Path { get; init; } = "";
        public List<string> ImagePaths { get; init; } = new();
        public List<string> VideoPaths { get; init; } = new();

        public int ImageCount => ImagePaths.Count;
        public int VideoCount => VideoPaths.Count;
        public int TotalCount => ImageCount + VideoCount;

        private static readonly string[] ImageExts = { ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp" };
        private static readonly string[] VideoExts = { ".mp4", ".webm", ".mov", ".avi", ".mkv" };

        /// <summary>
        /// Build an AssetPack from a folder. Scans the folder itself plus one
        /// level of immediate subfolders (so a top-level "outfits" folder with
        /// an "images" + "videos" pair both get picked up). Returns null if
        /// the folder doesn't exist or contains no recognised media.
        /// </summary>
        public static AssetPack? FromFolder(string folderPath)
        {
            if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
                return null;

            var images = new List<string>();
            var videos = new List<string>();

            try
            {
                CollectMediaIn(folderPath, images, videos);

                // Scan one layer of subfolders too — a pack might be organised
                // as `pack-name/{images,videos}` rather than flat.
                foreach (var sub in Directory.EnumerateDirectories(folderPath))
                {
                    CollectMediaIn(sub, images, videos);
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "AssetPack.FromFolder: enumeration failed for {Path}", folderPath);
                return null;
            }

            if (images.Count == 0 && videos.Count == 0) return null;

            return new AssetPack
            {
                Name = new DirectoryInfo(folderPath).Name,
                Path = folderPath,
                ImagePaths = images,
                VideoPaths = videos,
            };
        }

        private static void CollectMediaIn(string dir, List<string> images, List<string> videos)
        {
            foreach (var file in Directory.EnumerateFiles(dir))
            {
                var ext = System.IO.Path.GetExtension(file).ToLowerInvariant();
                if (ImageExts.Contains(ext)) images.Add(file);
                else if (VideoExts.Contains(ext)) videos.Add(file);
            }
        }
    }
}
