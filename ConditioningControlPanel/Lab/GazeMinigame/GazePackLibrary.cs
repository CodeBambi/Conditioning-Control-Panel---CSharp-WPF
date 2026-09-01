using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ConditioningControlPanel.Lab.GazeMinigame
{
    /// <summary>
    /// Discovers candidate <see cref="AssetPack"/>s for the gaze minigame instead of
    /// making the user folder-browse to each one by hand. Scans where a user is
    /// likely to have organised content into named sets:
    ///   • subfolders of {assets}/images and {assets}/videos (the natural home for
    ///     categorised flash/video content),
    ///   • subfolders of {assets}/gaze-packs (a convention for mixed-media packs),
    ///   • other immediate subfolders of the assets root,
    ///   • plus any custom folders the user added previously (remembered in settings).
    /// Each folder becomes a pack only if it actually contains media. Results are
    /// de-duplicated by full path and sorted by name for a stable gallery order.
    /// </summary>
    public static class GazePackLibrary
    {
        public const string ConventionFolder = "gaze-packs";
        private static readonly string[] DrilledRoots = { "images", "videos", ConventionFolder };

        public static List<AssetPack> Discover(IEnumerable<string>? extraPaths = null)
        {
            var found = new Dictionary<string, AssetPack>(StringComparer.OrdinalIgnoreCase);

            void TryAdd(string folder)
            {
                if (string.IsNullOrWhiteSpace(folder)) return;
                string full;
                try { full = Path.GetFullPath(folder); } catch { return; }
                if (found.ContainsKey(full)) return;
                var pack = AssetPack.FromFolder(full);
                if (pack != null) found[full] = pack;
            }

            var assets = CorePaths.EffectiveAssets;
            if (!string.IsNullOrWhiteSpace(assets))
            {
                // Per-category packs inside the conventional roots.
                foreach (var root in DrilledRoots)
                    ScanSubfolders(Path.Combine(assets, root), TryAdd);

                // Any other top-level category folders the user made directly under
                // the assets root (skip the roots we already drilled into above).
                ScanSubfolders(assets, TryAdd, skip: DrilledRoots);
            }

            // Remembered custom folders (may live outside the assets tree).
            if (extraPaths != null)
                foreach (var p in extraPaths) TryAdd(p);

            return found.Values
                        .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                        .ToList();
        }

        /// <summary>
        /// First usable still image in a pack, for a gallery thumbnail. Returns null
        /// for video-only packs (caller shows a video glyph instead) — decoding a
        /// frame at discovery time would be far too heavy for a strip of cards.
        /// </summary>
        public static string? ThumbnailPath(AssetPack pack)
            => pack.ImagePaths.Count > 0 ? pack.ImagePaths[0] : null;

        private static void ScanSubfolders(string root, Action<string> add, string[]? skip = null)
        {
            try
            {
                if (!Directory.Exists(root)) return;
                foreach (var sub in Directory.EnumerateDirectories(root))
                {
                    var name = new DirectoryInfo(sub).Name;
                    if (skip != null && skip.Contains(name, StringComparer.OrdinalIgnoreCase)) continue;
                    add(sub);
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "GazePackLibrary: subfolder scan failed for {Root}", root);
            }
        }
    }
}
