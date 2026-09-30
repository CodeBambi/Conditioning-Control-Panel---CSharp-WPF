using System;
using System.Collections.Generic;
using System.IO;
using ConditioningControlPanel.Lab.GazeMinigame;

namespace ConditioningControlPanel.Services;

/// <summary>
/// Flat shuffled-on-demand pool of media paths drawn from a user-selected
/// list of folders. Single source of truth for asset selection across the
/// full BlinkTrainerService session AND the Exclusives tab's stage preview,
/// extracted in v5.9.8 so both surfaces draw from the same logic.
///
/// Mix-mode bucketing (per-aspect random pick) lives in BlinkTrainerService
/// alongside the overlay rendering; this pool is the raw flat list.
///
/// AssetPack still lives under Lab.GazeMinigame — it's used by Gaze Minigame
/// and Focus Gaze too, so moving the whole pair would have widened the
/// migration surface unnecessarily.
/// </summary>
public sealed class BlinkTrainerAssetPool
{
    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".webm", ".mov", ".avi", ".mkv",
    };

    private readonly List<string> _paths;

    public int ImageCount { get; }
    public int VideoCount { get; }
    public bool IsEmpty => _paths.Count == 0;
    public IReadOnlyList<string> Paths => _paths;

    private BlinkTrainerAssetPool(List<string> paths, int imageCount, int videoCount)
    {
        _paths = paths;
        ImageCount = imageCount;
        VideoCount = videoCount;
    }

    /// <summary>
    /// Builds a pool from the given folder list. Each folder is enumerated
    /// via <see cref="AssetPack.FromFolder"/>, so the same two-level scan
    /// rules apply (folder itself plus one layer of subfolders). Folders
    /// that don't exist or contain no recognised media are skipped silently.
    /// </summary>
    public static BlinkTrainerAssetPool Build(IReadOnlyList<string>? folders, bool includeVideos)
    {
        var paths = new List<string>();
        int images = 0;
        int videos = 0;

        if (folders != null)
        {
            foreach (var folder in folders)
            {
                if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) continue;
                var pack = AssetPack.FromFolder(folder);
                if (pack == null) continue;

                paths.AddRange(pack.ImagePaths);
                images += pack.ImagePaths.Count;

                if (includeVideos)
                {
                    paths.AddRange(pack.VideoPaths);
                    videos += pack.VideoPaths.Count;
                }
            }
        }

        return new BlinkTrainerAssetPool(paths, images, videos);
    }

    /// <summary>
    /// Returns a random path from the pool, avoiding the same path as
    /// <paramref name="lastPickedPath"/> when the pool has more than one
    /// entry. Returns null when the pool is empty.
    /// </summary>
    public string? PickRandom(string? lastPickedPath = null)
    {
        if (_paths.Count == 0) return null;
        if (_paths.Count == 1) return _paths[0];

        int idx = Random.Shared.Next(_paths.Count);
        var chosen = _paths[idx];
        if (!string.IsNullOrEmpty(lastPickedPath)
            && string.Equals(chosen, lastPickedPath, StringComparison.OrdinalIgnoreCase))
        {
            idx = (idx + 1) % _paths.Count;
            chosen = _paths[idx];
        }
        return chosen;
    }

    /// <summary>True if the given path's extension marks it as a supported video.</summary>
    public static bool IsVideo(string? path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        var ext = Path.GetExtension(path);
        return !string.IsNullOrEmpty(ext) && VideoExtensions.Contains(ext);
    }
}
