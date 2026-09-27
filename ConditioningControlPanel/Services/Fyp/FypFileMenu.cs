using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using ConditioningControlPanel.Helpers;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Services.Fyp;

/// <summary>
/// Right-click a library item in the For You feed: "Open file" / "Show in folder" (ccp-bugs #1290).
/// The page only sends the manifest id (a path relative to the assets folder), so the host decides
/// what that id points at and refuses anything that leaves the folder.
/// </summary>
internal static class FypFileMenu
{
    // Same list as FypAssetManifest.VideoExts.
    private static readonly string[] ServedVideoExts = { ".mp4", ".webm", ".m4v" };

    /// <summary>
    /// Full path for a library id, or null for an online id, an empty id, a path that escapes
    /// <paramref name="root"/>, or one carrying a quote (it could not be put on a command line
    /// safely, and a real Windows path never holds one).
    /// </summary>
    internal static string? ResolveLocal(string? root, string? id)
    {
        if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(id)) return null;
        if (id.StartsWith("scrolller/", StringComparison.Ordinal)) return null;
        if (id.IndexOf('"') >= 0 || id.IndexOf(':') >= 0) return null;
        // Only the shape the manifest itself serves (FypAssetManifest.Collect): a clip under
        // videos/ or a gif under images/, never a dot folder (the remote cache lives in .temp),
        // never any other extension. Process.Start opens with the shell, so an .exe or .lnk that
        // happens to sit in the assets folder must not be reachable from the page.
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
            if (!full.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase)) return null;
            return full;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Shows the menu at the pointer. UI thread only.</summary>
    internal static void Show(string? id)
    {
        var path = ResolveLocal(App.EffectiveAssetsPath, id);
        if (path == null || !File.Exists(path)) return;

        var menu = new ContextMenu { Placement = PlacementMode.MousePoint };
        var open = new MenuItem { Header = Loc.Get("fyp_menu_open_file") };
        open.Click += (_, _) => OpenFile(path);
        var reveal = new MenuItem { Header = Loc.Get("fyp_menu_show_in_folder") };
        reveal.Click += (_, _) => ExplorerLauncher.RevealInExplorer(path);
        menu.Items.Add(open);
        menu.Items.Add(reveal);
        menu.IsOpen = true;
    }

    private static void OpenFile(string path)
    {
        try
        {
            if (!File.Exists(path)) return;
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            App.Logger?.Warning("FypFileMenu: open failed for {Path}: {E}", path, ex.Message);
        }
    }
}
