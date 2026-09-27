using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services;

/// <summary>
/// Folder-level exclusions for the asset blacklist (ccp-bugs #1231). Unticking a folder used to
/// write only the files it held at that moment into <c>DisabledAssetPaths</c>, so a file added
/// later was enabled in every preset. The folder is now remembered too
/// (<c>DisabledAssetFolders</c>, relative to EffectiveAssetsPath, forward-slashed) and anything
/// under it inherits the folder's state. <c>DisabledAssetPaths</c> stays the one set every
/// consumer reads: <see cref="ExpandFromDisk"/> writes the inherited files into it, and the hot
/// pools also check <see cref="IsUnderAny"/> so a new file is excluded before the next expand.
/// </summary>
public static class AssetFolderExclusion
{
    public static string Norm(string p) => (p ?? string.Empty).Replace('\\', '/').Trim('/');

    /// <summary>True when <paramref name="relativePath"/> sits inside <paramref name="folder"/> (any depth).</summary>
    public static bool IsUnder(string relativePath, string folder)
    {
        var path = Norm(relativePath);
        var f = Norm(folder);
        if (f.Length == 0 || path.Length <= f.Length) return false;
        return path.StartsWith(f, StringComparison.OrdinalIgnoreCase) && path[f.Length] == '/';
    }

    public static bool IsUnderAny(string relativePath, IEnumerable<string>? folders)
    {
        if (folders == null) return false;
        foreach (var f in folders)
            if (IsUnder(relativePath, f)) return true;
        return false;
    }

    /// <summary>
    /// A folder was ticked or unticked as a whole. Unticked: remember it (its own sub-markers are
    /// then redundant). Ticked: forget it, every marker under it, and every marker above it,
    /// because an ancestor that now holds an enabled folder is no longer fully excluded.
    /// </summary>
    public static void MarkFolder(HashSet<string> folders, string folder, bool isChecked)
    {
        var f = Norm(folder);
        if (f.Length == 0) return;
        folders.RemoveWhere(m => IsUnder(m, f));
        if (isChecked)
        {
            folders.RemoveWhere(m => string.Equals(Norm(m), f, StringComparison.OrdinalIgnoreCase) || IsUnder(f, m));
        }
        else
        {
            folders.Add(f);
        }
    }

    /// <summary>A single file was ticked: no folder above it is fully excluded any more.</summary>
    public static void FileEnabled(HashSet<string> folders, string fileRelativePath)
        => folders.RemoveWhere(m => IsUnder(fileRelativePath, m));

    /// <summary>
    /// Writes every file under a remembered folder into <paramref name="disabledPaths"/>. Returns how
    /// many were added. Pure over the listing it is handed.
    /// </summary>
    public static int Expand(HashSet<string> disabledPaths, IEnumerable<string>? folders, IEnumerable<string> fileRelativePaths)
    {
        var marks = folders?.Select(Norm).Where(f => f.Length > 0).ToArray() ?? Array.Empty<string>();
        if (marks.Length == 0) return 0;
        int added = 0;
        foreach (var rel in fileRelativePaths)
        {
            var p = Norm(rel);
            if (IsUnderAny(p, marks) && disabledPaths.Add(p)) added++;
        }
        return added;
    }

    private static readonly string[] MediaExtensions =
    {
        ".png", ".jpg", ".jpeg", ".jpe", ".jfif", ".gif", ".webp", ".bmp", ".tif", ".tiff", ".heic", ".avif",
        ".mp4", ".avi", ".mkv", ".mov", ".wmv", ".webm"
    };

    /// <summary>
    /// Brings the active selection up to date with the disk: files added to an excluded folder
    /// since it was unticked are written into the blacklist. UI thread (it mutates settings).
    /// </summary>
    public static int ExpandFromDisk(AppSettings? s, string? assetsRoot)
        => s == null ? 0 : ExpandFromDisk(s.DisabledAssetPaths, s.DisabledAssetFolders, assetsRoot);

    /// <summary>The same for any blacklist, a saved preset's included.</summary>
    public static int ExpandFromDisk(HashSet<string>? disabledPaths, HashSet<string>? folders, string? assetsRoot)
    {
        if (disabledPaths == null || folders == null || folders.Count == 0 || string.IsNullOrEmpty(assetsRoot)) return 0;
        var listing = new List<string>();
        foreach (var folder in folders.ToArray())
        {
            try
            {
                var full = Path.Combine(assetsRoot, Norm(folder));
                if (!Directory.Exists(full)) continue;
                foreach (var file in Directory.EnumerateFiles(full, "*", SearchOption.AllDirectories))
                {
                    if (!MediaExtensions.Contains(Path.GetExtension(file).ToLowerInvariant())) continue;
                    listing.Add(Path.GetRelativePath(assetsRoot, file));
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("AssetFolderExclusion: could not list {Folder}: {Error}", folder, ex.Message);
            }
        }
        return Expand(disabledPaths, folders, listing);
    }
}
