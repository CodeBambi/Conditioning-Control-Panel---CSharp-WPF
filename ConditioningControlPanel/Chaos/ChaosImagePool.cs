using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ConditioningControlPanel;

/// <summary>
/// TTL-cached listing of the flash image pool (<c>EffectiveAssetsPath/images</c>), shared by the
/// chaos flash wash (<see cref="ChaosFlashOverlay"/>) and the GIF cascade
/// (<see cref="ChaosGifCascadeOverlay"/>). Both used to re-enumerate the whole folder on the UI
/// thread on every show — against a big asset library (multi-GB image folders are common) that's
/// a burst of directory I/O per ~10s wash, mid-run. One listing is reused for <see cref="TTL"/>;
/// files dropped into the folder simply join the pool on the next refresh. The returned list is
/// replaced wholesale on refresh (never mutated), so callers may hold a reference. UI thread only.
/// </summary>
internal static class ChaosImagePool
{
    // Keep this in lockstep with FlashService's flash-pool extension list so glitch/cascade
    // draw from exactly the images the user sees flashed (FlashService.cs ~1967).
    private static readonly string[] Extensions =
        { ".png", ".jpg", ".jpeg", ".jpe", ".jfif", ".gif", ".webp", ".bmp", ".tif", ".tiff", ".heic", ".avif", ".ico" };
    private static readonly TimeSpan TTL = TimeSpan.FromSeconds(60);

    private static List<string> _files = new();
    private static string? _dir;
    private static DateTime _stamp = DateTime.MinValue;

    /// <summary>The image files (up to a minute stale). Never null; may be empty.</summary>
    public static List<string> GetFiles()
    {
        try
        {
            var dir = Path.Combine(App.EffectiveAssetsPath ?? "", "images");
            var now = DateTime.UtcNow;
            if (dir == _dir && now - _stamp < TTL) return _files;
            // Stamp BEFORE the walk so a missing/throwing folder isn't re-walked every call.
            _stamp = now;
            _dir = dir;
            // Recurse subfolders — FlashService scans AllDirectories to support user-organized
            // category folders, so top-level-only here meant glitch/cascade silently found nothing
            // for anyone who filed their images into subfolders even though flashes worked.
            _files = Directory.Exists(dir)
                ? Directory.EnumerateFiles(dir, "*.*", SearchOption.AllDirectories)
                    .Where(f => Extensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                    .ToList()
                : new List<string>();
        }
        catch { _files = new List<string>(); }
        return _files;
    }
}
