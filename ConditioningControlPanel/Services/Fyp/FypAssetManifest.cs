using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ConditioningControlPanel.Services.Fyp;

/// <summary>
/// Enumerates the user's active preset into the manifest the For You feed consumes over the
/// bridge. Sibling of <see cref="Chaos.DtrhAssetManifest"/> with a richer per-entry shape:
/// the feed's segment grid needs a stable id plus duration/dimensions where known.
///
/// Ids are the forward-slashed path relative to EffectiveAssetsPath — segIds are
/// "<c>id:k</c>", so ids must never contain '<c>:</c>' (relative paths have no drive colon).
/// Durations come from <see cref="VideoMetadataCache"/> when warm; dimensions (and durations
/// for videos LibVLC never touched) are backfilled by the page's own probes via
/// <see cref="FypMetaStore"/>, so the grid gets better with every session.
/// </summary>
internal static class FypAssetManifest
{
    private static readonly string[] VideoExts = { ".mp4", ".webm", ".m4v" };
    private const long MaxGifBytes = 50L * 1024 * 1024;      // mirrors DtrhAssetManifest caps
    private const long MaxVideoBytes = 500L * 1024 * 1024;
    private const int MaxEntries = 5000;
    private const int MaxWalkDepth = 8;

    public sealed class Entry
    {
        // camelCase on the wire — the page reads these directly.
        [Newtonsoft.Json.JsonProperty("id")] public string Id { get; init; } = "";
        [Newtonsoft.Json.JsonProperty("url")] public string Url { get; init; } = "";
        [Newtonsoft.Json.JsonProperty("type")] public string Type { get; init; } = "video";   // "video" | "gif"
        [Newtonsoft.Json.JsonProperty("filename")] public string Filename { get; init; } = "";
        [Newtonsoft.Json.JsonProperty("folder")] public string Folder { get; init; } = "";
        [Newtonsoft.Json.JsonProperty("durationMs")] public long? DurationMs { get; set; }
        [Newtonsoft.Json.JsonProperty("width")] public int? Width { get; set; }
        [Newtonsoft.Json.JsonProperty("height")] public int? Height { get; set; }
    }

    /// <summary>Build the asset list for the current EffectiveAssetsPath. Never throws.</summary>
    public static List<Entry> Build(FypMetaStore meta)
    {
        var entries = new List<Entry>();
        try
        {
            var root = App.EffectiveAssetsPath;
            var disabled = new HashSet<string>(
                (App.Settings?.Current?.DisabledAssetPaths ?? new()).Select(p => p.Replace('\\', '/')),
                StringComparer.OrdinalIgnoreCase);

            Collect(Path.Combine(root, "videos"), root, isVideo: true, entries, disabled);
            Collect(Path.Combine(root, "images"), root, isVideo: false, entries, disabled);

            if (entries.Count > MaxEntries)
            {
                // partial Fisher-Yates, then truncate (DtrhAssetManifest precedent)
                var rng = new Random();
                for (int i = 0; i < MaxEntries; i++)
                {
                    int j = i + rng.Next(entries.Count - i);
                    (entries[i], entries[j]) = (entries[j], entries[i]);
                }
                entries.RemoveRange(MaxEntries, entries.Count - MaxEntries);
            }

            // Enrich: LibVLC duration cache (videos only) + the page's own probe backfills.
            foreach (var e in entries)
            {
                var m = meta.Get(e.Id);
                if (m != null)
                {
                    e.DurationMs = m.DurationMs;
                    e.Width = m.Width;
                    e.Height = m.Height;
                }
                if (e.DurationMs == null && e.Type == "video")
                {
                    var path = Path.Combine(root, e.Id.Replace('/', Path.DirectorySeparatorChar));
                    var sec = App.Video?.MetadataCache?.TryGetDuration(path);
                    if (sec is > 0) e.DurationMs = (long)(sec.Value * 1000);
                }
            }

            App.Logger?.Information("FypAssetManifest: {V} videos, {G} gifs ({D} with duration)",
                entries.Count(e => e.Type == "video"), entries.Count(e => e.Type == "gif"),
                entries.Count(e => e.DurationMs != null));
        }
        catch (Exception ex)
        {
            App.Logger?.Warning("FypAssetManifest.Build failed: {E}", ex.Message);
        }
        return entries;
    }

    private static void Collect(string dir, string root, bool isVideo, List<Entry> entries, HashSet<string> disabled)
    {
        if (!Directory.Exists(dir)) return;
        long cap = isVideo ? MaxVideoBytes : MaxGifBytes;
        foreach (var f in Walk(dir, 0))
        {
            if (entries.Count >= MaxEntries * 2) return; // sample later, but bound the walk
            var ext = Path.GetExtension(f).ToLowerInvariant();
            if (isVideo ? !VideoExts.Contains(ext) : ext != ".gif") continue;
            var rel = Path.GetRelativePath(root, f).Replace('\\', '/');
            if (disabled.Count > 0 && disabled.Contains(rel)) continue;
            long len;
            try { len = new FileInfo(f).Length; } catch { continue; }
            if (len <= 0 || len > cap) continue;
            entries.Add(new Entry
            {
                Id = rel,
                Url = "https://ccp.assets/" + string.Join('/', rel.Split('/').Select(Uri.EscapeDataString)),
                Type = isVideo ? "video" : "gif",
                Filename = Path.GetFileName(f),
                Folder = Path.GetFileName(Path.GetDirectoryName(f)) ?? "",
            });
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
}
