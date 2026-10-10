using System;
using System.IO;

namespace ConditioningControlPanel.Services.Fyp;

/// <summary>
/// WPF 7.1.5 Services/Fyp/FypFileMenu.cs, the rule half: which library file a page-supplied id may
/// name for "Open file" / "Show in folder". The menu itself is the head's.
/// </summary>
internal static class FypFileMenu
{
    // Same list as FypAssetManifest.VideoExts.
    private static readonly string[] ServedVideoExts = { ".mp4", ".webm", ".m4v" };

    /// <summary>
    /// The full path for a manifest id, or null. Only the shape the manifest itself serves: a clip
    /// under videos/ or a gif under images/, never a dot folder (the remote cache lives in .temp),
    /// never another extension (the shell opens the file, so an .exe or .lnk in the assets folder
    /// must not be reachable from the page), never a remote id, never outside the root.
    /// </summary>
    internal static string? ResolveLocal(string? root, string? id)
    {
        if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(id)) return null;
        if (id.StartsWith("scrolller/", StringComparison.Ordinal)) return null;
        if (id.IndexOf('"') >= 0 || id.IndexOf(':') >= 0) return null;
        var parts = id.Replace('\\', '/').Split('/');
        if (parts.Length < 2) return null;
        bool isVideo = string.Equals(parts[0], "videos", StringComparison.OrdinalIgnoreCase);
        bool isGif = string.Equals(parts[0], "images", StringComparison.OrdinalIgnoreCase);
        if (!isVideo && !isGif) return null;
        foreach (var p in parts)
            if (p.Length == 0 || p.StartsWith('.')) return null;
        var ext = Path.GetExtension(id).ToLowerInvariant();
        if (isVideo ? Array.IndexOf(ServedVideoExts, ext) < 0 : ext != ".gif") return null;
        try
        {
            var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                           + Path.DirectorySeparatorChar;
            var full = Path.GetFullPath(Path.Combine(fullRoot, id.Replace('/', Path.DirectorySeparatorChar)));
            var cmp = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (!full.StartsWith(fullRoot, cmp)) return null;
            return full;
        }
        catch
        {
            return null;
        }
    }
}
