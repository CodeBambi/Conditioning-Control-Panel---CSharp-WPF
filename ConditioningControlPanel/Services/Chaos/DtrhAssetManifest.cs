using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ConditioningControlPanel.Services.Chaos;

/// <summary>
/// Enumerates the user's active preset (App.EffectiveAssetsPath images/ + videos/) into the
/// manifest the DtRH browser game consumes over the bridge (hostMedia.js). Entries become
/// https://ccp.assets/ URLs; the page never sees a disk path.
///
/// Only browser-decodable formats are listed - LibVLC-only containers (wmv/avi/mkv/mov...)
/// are counted as skipped so the game can be honest about what it can't show. Native video
/// payloads keep playing those through VideoService untouched.
/// </summary>
internal static class DtrhAssetManifest
{
    private static readonly string[] ImageExts = { ".jpg", ".jpeg", ".png", ".webp", ".gif" };
    private static readonly string[] VideoExts = { ".mp4", ".webm", ".m4v" };
    private const long MaxImageBytes = 50L * 1024 * 1024;    // mirrors the engine's own caps
    private const long MaxVideoBytes = 500L * 1024 * 1024;
    private const int MaxEntries = 5000;                     // media.js precedent
    private const int MaxWalkDepth = 8;

    public sealed record Entry(string Name, string Url);

    public sealed class Manifest
    {
        public List<Entry> Images { get; } = new();
        public List<Entry> Videos { get; } = new();
        public int Skipped { get; set; }
        public bool Truncated { get; set; }
    }

    /// <summary>
    /// One file the scan looked at. <c>Skipped == true</c> means "media-looking but not usable"
    /// (unsupported container, zero-length, over the size cap) — the manifest counts those so the
    /// game can be honest; silently-ignored junk and user-deselected files are never yielded.
    /// </summary>
    private readonly record struct ScanItem(string Full, string Rel, long Bytes, bool IsImage, bool Skipped);

    /// <summary>Build the manifest for the current EffectiveAssetsPath. Never throws.</summary>
    public static Manifest Build()
    {
        var m = new Manifest();
        try
        {
            var root = App.EffectiveAssetsPath;
            foreach (var it in Scan(root, BuildDisabledSet()))
            {
                if (it.Skipped) { m.Skipped++; continue; }
                var entry = new Entry(Path.GetFileName(it.Full), ToAssetUrl(root, it.Full));
                (it.IsImage ? m.Images : m.Videos).Add(entry);
            }

            int total = m.Images.Count + m.Videos.Count;
            if (total > MaxEntries)
            {
                // Uniform random sample per kind, preserving the image:video ratio.
                var rng = new Random();
                int imgKeep = (int)Math.Round(MaxEntries * (double)m.Images.Count / total);
                int vidKeep = MaxEntries - imgKeep;
                Downsample(m.Images, imgKeep, rng);
                Downsample(m.Videos, vidKeep, rng);
                m.Truncated = true;
            }
            App.Logger?.Information("DtrhAssetManifest: {I} images, {V} videos, {S} skipped{T}",
                m.Images.Count, m.Videos.Count, m.Skipped, m.Truncated ? " (truncated)" : "");
        }
        catch (Exception ex)
        {
            App.Logger?.Warning("DtrhAssetManifest.Build failed: {E}", ex.Message);
        }
        return m;
    }

    /// <summary>
    /// The user's active pool as flat tuples: full path, forward-slashed path relative to
    /// EffectiveAssetsPath, byte length, and image-vs-video. Same filters the DtRH manifest uses
    /// (browser-decodable extensions, DisabledAssetPaths, the 50MB/500MB caps, depth 8, dot-dirs
    /// skipped, 10k walk bound) — shared so the transfer compression planner and the game can
    /// never disagree about what "the active pool" is. Lazy; never throws.
    /// </summary>
    public static IEnumerable<(string Full, string Rel, long Bytes, bool IsImage)> EnumerateActive()
    {
        string root;
        HashSet<string> disabled;
        try
        {
            root = App.EffectiveAssetsPath;
            disabled = BuildDisabledSet();
        }
        catch (Exception ex)
        {
            App.Logger?.Warning("DtrhAssetManifest.EnumerateActive setup failed: {E}", ex.Message);
            return Array.Empty<(string, string, long, bool)>();
        }
        return Enumerate(root, disabled);
    }

    private static IEnumerable<(string Full, string Rel, long Bytes, bool IsImage)> Enumerate(
        string root, HashSet<string> disabled)
    {
        foreach (var it in Scan(root, disabled))
            if (!it.Skipped) yield return (it.Full, it.Rel, it.Bytes, it.IsImage);
    }

    /// <summary>
    /// Honor the user's asset selection: items unchecked in the Assets tree land in
    /// DisabledAssetPaths (relative to EffectiveAssetsPath, forward-slashed). The DtRH game
    /// consumed the raw folder before this, so deselected images/videos still fell through the
    /// tube. Matches FlashService.GetMediaFiles' normalization exactly (case-insensitive,
    /// separator-agnostic) so the same unchecks that hide a flash hide it here too.
    /// </summary>
    private static HashSet<string> BuildDisabledSet() => new(
        (App.Settings?.Current?.DisabledAssetPaths ?? new()).Select(p => p.Replace('\\', '/')),
        StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// images/ then videos/, in walk order. The accepted-count bound spans BOTH folders (it used to
    /// be a shared running total across the two Collect calls) — keep it that way, the manifest's
    /// downsampling assumes a bounded but unbiased-by-folder input.
    /// </summary>
    private static IEnumerable<ScanItem> Scan(string root, HashSet<string> disabled)
    {
        int accepted = 0;
        foreach (var isImage in new[] { true, false })
        {
            var dir = Path.Combine(root, isImage ? "images" : "videos");
            if (!Directory.Exists(dir)) continue;
            var exts = isImage ? ImageExts : VideoExts;
            var otherExts = isImage ? VideoExts : ImageExts;
            long cap = isImage ? MaxImageBytes : MaxVideoBytes;
            foreach (var f in Walk(dir, 0))
            {
                if (accepted >= MaxEntries * 2) yield break; // sample later, but bound the walk
                var ext = Path.GetExtension(f).ToLowerInvariant();
                if (!exts.Contains(ext))
                {
                    // A media-looking file the browser can't decode counts as skipped;
                    // random other junk (txt/db) is silently ignored.
                    if (!otherExts.Contains(ext) && IsMediaLike(ext))
                        yield return new ScanItem(f, "", 0, isImage, true);
                    continue;
                }
                string rel;
                try { rel = Path.GetRelativePath(root, f).Replace('\\', '/'); }
                catch { rel = Path.GetFileName(f); }
                // Deselected in the Assets tree -> skip (silently, not "skipped"):
                // it's user intent, not an unsupported file.
                if (disabled.Count > 0 && disabled.Contains(rel)) continue;
                long len;
                try { len = new FileInfo(f).Length; } catch { continue; }
                if (len <= 0 || len > cap) { yield return new ScanItem(f, rel, len, isImage, true); continue; }
                accepted++;
                yield return new ScanItem(f, rel, len, isImage, false);
            }
        }
    }

    private static IEnumerable<string> Walk(string dir, int depth)
    {
        if (depth > MaxWalkDepth) yield break;
        IEnumerable<string> files;
        try { files = Directory.EnumerateFiles(dir); }
        catch { yield break; }
        foreach (var f in files) yield return f;
        IEnumerable<string> subs;
        try { subs = Directory.EnumerateDirectories(dir); }
        catch { yield break; }
        foreach (var d in subs)
        {
            if (Path.GetFileName(d).StartsWith('.')) continue;
            foreach (var f in Walk(d, depth + 1)) yield return f;
        }
    }

    private static bool IsMediaLike(string ext) =>
        ext is ".wmv" or ".avi" or ".mkv" or ".mov" or ".flv" or ".mpg" or ".mpeg" or ".bmp" or ".tiff" or ".heic";

    private static void Downsample<T>(List<T> list, int keep, Random rng)
    {
        if (list.Count <= keep) return;
        // partial Fisher-Yates: shuffle the first `keep` slots, then truncate
        for (int i = 0; i < keep; i++)
        {
            int j = i + rng.Next(list.Count - i);
            (list[i], list[j]) = (list[j], list[i]);
        }
        list.RemoveRange(keep, list.Count - keep);
    }

    private static string ToAssetUrl(string root, string fullPath)
    {
        var rel = Path.GetRelativePath(root, fullPath).Replace('\\', '/');
        var escaped = string.Join('/', rel.Split('/').Select(Uri.EscapeDataString));
        return "https://ccp.assets/" + escaped;
    }
}
