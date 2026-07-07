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

    /// <summary>Build the manifest for the current EffectiveAssetsPath. Never throws.</summary>
    public static Manifest Build()
    {
        var m = new Manifest();
        try
        {
            var root = App.EffectiveAssetsPath;
            Collect(Path.Combine(root, "images"), root, isImage: true, m);
            Collect(Path.Combine(root, "videos"), root, isImage: false, m);

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

    private static void Collect(string dir, string root, bool isImage, Manifest m)
    {
        if (!Directory.Exists(dir)) return;
        var exts = isImage ? ImageExts : VideoExts;
        var otherExts = isImage ? VideoExts : ImageExts;
        long cap = isImage ? MaxImageBytes : MaxVideoBytes;
        foreach (var f in Walk(dir, 0))
        {
            if (m.Images.Count + m.Videos.Count >= MaxEntries * 2) return; // sample later, but bound the walk
            var ext = Path.GetExtension(f).ToLowerInvariant();
            if (!exts.Contains(ext))
            {
                // A media-looking file the browser can't decode counts as skipped;
                // random other junk (txt/db) is silently ignored.
                if (!otherExts.Contains(ext) && IsMediaLike(ext)) m.Skipped++;
                continue;
            }
            long len;
            try { len = new FileInfo(f).Length; } catch { continue; }
            if (len <= 0 || len > cap) { m.Skipped++; continue; }
            var entry = new Entry(Path.GetFileName(f), ToAssetUrl(root, f));
            (isImage ? m.Images : m.Videos).Add(entry);
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
