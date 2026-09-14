using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ConditioningControlPanel.Services.Remix;

/// <summary>
/// The pure half of the Jackpot Remix builder: which gifs go to the page, in what order, as
/// what url, and how many prebuilt files the cache keeps. No WebView2, no App, so every rule
/// here is unit tested (JackpotRemixPlanTests) and the host in <see cref="JackpotRemixBuilder"/>
/// is only plumbing.
/// </summary>
public static class JackpotRemixPlan
{
    /// <summary>The Remix Room's tile ceiling (engine/rects.js MAX_TILES). A build always sends
    /// exactly this many entries; fewer sources cycle.</summary>
    public const int Tiles = 8;

    /// <summary>
    /// Per-source file size cap. Chosen from the spike measurements (evidence/fx/remix/measurements.md):
    /// the page reads the whole file into memory, hands the worker a copy, and composes every source
    /// frame on a full-size canvas before cover-fitting to the strip, so the cost of a source scales
    /// with its bytes and its pixel area, not with the output. 12 MB keeps eight worst-case sources
    /// under the renderer's comfortable ceiling; a 20 MB gif is skipped, never trimmed.
    /// </summary>
    public const long MaxSourceBytes = 12L * 1024 * 1024;

    /// <summary>How many finished remix files stay on disk. Two: the one a flash may be playing
    /// and the one prebuilt for the next roll.</summary>
    public const int KeepFiles = 2;

    /// <summary>The virtual host the page reads the user's gifs from (mapped to the assets root).</summary>
    public const string AssetsHost = "ccp.assets";

    /// <summary>
    /// Exactly <see cref="Tiles"/> entries: the sources in order, then round again until eight
    /// (a b c -> a b c a b c a b). Empty in, empty out: a remix with no media is no remix.
    /// </summary>
    public static IReadOnlyList<string> Cycle(IReadOnlyList<string> sources, int tiles = Tiles)
    {
        if (sources == null || sources.Count == 0 || tiles <= 0) return Array.Empty<string>();
        var list = new List<string>(tiles);
        for (int i = 0; i < tiles; i++) list.Add(sources[i % sources.Count]);
        return list;
    }

    /// <summary>
    /// Drop sources over the byte cap or missing on disk. <paramref name="sizeOf"/> returns the
    /// file length or a negative number when the file cannot be read.
    /// </summary>
    public static IReadOnlyList<string> FilterBySize(IEnumerable<string> sources, Func<string, long> sizeOf, long capBytes = MaxSourceBytes)
    {
        var keep = new List<string>();
        foreach (var s in sources ?? Enumerable.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(s)) continue;
            long n;
            try { n = sizeOf(s); } catch { n = -1; }
            if (n < 0 || n > capBytes) continue;
            keep.Add(s);
        }
        return keep;
    }

    /// <summary>
    /// The url the page fetches a local gif from: <c>https://ccp.assets/&lt;relative path&gt;</c>,
    /// forward slashes, each segment escaped. Null when the file is not under the assets root,
    /// because the virtual host cannot see it (mods and remote media are J2's problem).
    /// </summary>
    public static string? ToAssetUrl(string assetsRoot, string path)
    {
        if (string.IsNullOrWhiteSpace(assetsRoot) || string.IsNullOrWhiteSpace(path)) return null;
        string root, full;
        try
        {
            root = Path.GetFullPath(assetsRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            full = Path.GetFullPath(path);
        }
        catch { return null; }
        if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return null;
        var rel = full.Substring(root.Length + 1).Replace('\\', '/');
        var parts = rel.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Uri.EscapeDataString);
        return "https://" + AssetsHost + "/" + string.Join("/", parts);
    }

    /// <summary>The output file name for a build: the room's code, so a log line and a file name agree.</summary>
    public static string OutputName(string code, DateTime utc)
        => "remix-" + Sanitize(code) + "-" + utc.ToString("yyyyMMdd-HHmmss") + ".gif";

    private static string Sanitize(string s)
    {
        if (string.IsNullOrEmpty(s)) return "0";
        var bad = Path.GetInvalidFileNameChars();
        return new string(s.Select(c => bad.Contains(c) ? '_' : c).ToArray());
    }

    /// <summary>
    /// Which of the cache's remix files to delete so at most <paramref name="keep"/> remain: the
    /// oldest by write time. Pure over (path, writeTimeUtc) pairs so the rule is testable without
    /// a disk.
    /// </summary>
    public static IReadOnlyList<string> Excess(IEnumerable<(string Path, DateTime WriteUtc)> files, int keep = KeepFiles)
    {
        var ordered = (files ?? Enumerable.Empty<(string, DateTime)>())
            .OrderByDescending(f => f.WriteUtc)
            .ThenBy(f => f.Path, StringComparer.Ordinal)
            .ToList();
        if (keep < 0) keep = 0;
        return ordered.Skip(keep).Select(f => f.Path).ToList();
    }

    /// <summary>Apply <see cref="Excess"/> to a folder. Never throws: a file a flash still has open
    /// simply survives until the next bound.</summary>
    public static void Bound(string folder, int keep = KeepFiles)
    {
        try
        {
            if (!Directory.Exists(folder)) return;
            var files = new DirectoryInfo(folder).GetFiles("remix-*.gif")
                .Select(f => (f.FullName, f.LastWriteTimeUtc));
            foreach (var p in Excess(files, keep))
            {
                try { File.Delete(p); } catch (Exception ex) { Diag.Swallowed(ex); }
            }
        }
        catch (Exception ex) { Diag.Swallowed(ex); }
    }
}
