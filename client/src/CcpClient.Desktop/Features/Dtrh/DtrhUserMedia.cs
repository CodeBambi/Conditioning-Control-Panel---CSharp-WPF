namespace CcpClient.Desktop.Features.Dtrh;

/// <summary>
/// User-media manifest (b4): enumerates the user-media folder contract
/// (<c>&lt;dataDir&gt;/assets/images</c> + <c>&lt;dataDir&gt;/assets/videos</c> — the WPF
/// App.EffectiveAssetsPath default parity; a future assets-picker row owns custom paths)
/// into the manifest the page consumes (hostMedia.js setManifest). Ported from WPF
/// DtrhAssetManifest.cs: image exts jpg/jpeg/png/webp/gif (:19), video mp4/webm/m4v (:20),
/// caps 50MB/500MB (:21-22), MaxEntries 5000 (:23), walk depth 8 with dot-dirs skipped
/// (:24,107-122), browser-undecodable media counted as skipped (:124-125), ratio-preserving
/// downsample when over the cap (:127-137).
///
/// Divergences (recorded): DisabledAssetPaths = empty (no assets tree in the greenfield);
/// URLs are loopback {MediaOrigin}/umedia/<rel> instead of https://ccp.assets/ (the §4
/// media origin is the greenfield's ccp.assets-class host).
/// Media-logging rule (packet framing c): the build logs COUNTS ONLY, never names/paths.
/// </summary>
public static class DtrhUserMedia
{
    private static readonly string[] ImageExts = [".jpg", ".jpeg", ".png", ".webp", ".gif"];
    private static readonly string[] VideoExts = [".mp4", ".webm", ".m4v"];
    private const long MaxImageBytes = 50L * 1024 * 1024;
    private const long MaxVideoBytes = 500L * 1024 * 1024;
    private const int MaxEntries = 5000;
    private const int MaxWalkDepth = 8;

    public sealed class Manifest
    {
        public List<DtrhProtocol.DtrhManifestEntry> Images { get; } = [];
        public List<DtrhProtocol.DtrhManifestEntry> Videos { get; } = [];
        public int Skipped { get; set; }
        public bool Truncated { get; set; }
    }

    /// <summary>The user-media folder contract (Step-1 decision, consult-approved):
    /// images/ + videos/ under the DTRH data directory.</summary>
    public static string ImagesFolder(string userMediaRoot) => Path.Combine(userMediaRoot, "images");

    public static string VideosFolder(string userMediaRoot) => Path.Combine(userMediaRoot, "videos");

    /// <summary>Build the manifest for the user-media root. Never throws; counts-only log
    /// (DtrhAssetManifest.cs:70 parity — WPF logs the same counts, never names).</summary>
    public static Manifest Build(string userMediaRoot, string mediaOrigin, Action<string> log)
    {
        var m = new Manifest();
        try
        {
            var root = Path.GetFullPath(userMediaRoot);
            Collect(ImagesFolder(root), root, isImage: true, mediaOrigin, m);
            Collect(VideosFolder(root), root, isImage: false, mediaOrigin, m);

            var total = m.Images.Count + m.Videos.Count;
            if (total > MaxEntries)
            {
                // Uniform random sample per kind, preserving the image:video ratio
                // (DtrhAssetManifest.cs:54-64).
                var rng = new Random();
                var imgKeep = (int)Math.Round(MaxEntries * (double)m.Images.Count / total);
                Downsample(m.Images, imgKeep, rng);
                Downsample(m.Videos, MaxEntries - imgKeep, rng);
                m.Truncated = true;
            }

            log($"dtrh-media: manifest — {m.Images.Count} image(s), {m.Videos.Count} video(s), {m.Skipped} skipped{(m.Truncated ? " (truncated)" : "")}");
        }
        catch (Exception ex)
        {
            log($"dtrh-media: manifest build failed ({ex.GetType().Name})");
        }

        return m;
    }

    private static void Collect(string dir, string root, bool isImage, string mediaOrigin, Manifest m)
    {
        if (!Directory.Exists(dir)) return;
        var exts = isImage ? ImageExts : VideoExts;
        var otherExts = isImage ? VideoExts : ImageExts;
        var cap = isImage ? MaxImageBytes : MaxVideoBytes;
        foreach (var f in Walk(dir, 0))
        {
            if (m.Images.Count + m.Videos.Count >= MaxEntries * 2) return; // sample later, but bound the walk
            var ext = Path.GetExtension(f).ToLowerInvariant();
            if (!exts.Contains(ext))
            {
                // A media-looking file the browser can't decode counts as skipped;
                // random other junk (txt/db) is silently ignored (:94-96,124-125).
                if (!otherExts.Contains(ext) && IsMediaLike(ext)) m.Skipped++;
                continue;
            }

            long len;
            try { len = new FileInfo(f).Length; } catch { continue; }
            if (len <= 0 || len > cap) { m.Skipped++; continue; }
            var entry = new DtrhProtocol.DtrhManifestEntry(Path.GetFileName(f), ToMediaUrl(mediaOrigin, root, f));
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
        for (var i = 0; i < keep; i++)
        {
            var j = i + rng.Next(list.Count - i);
            (list[i], list[j]) = (list[j], list[i]);
        }

        list.RemoveRange(keep, list.Count - keep);
    }

    /// <summary>The loopback URL for one user-media file (WPF ToAssetUrl :139-144 shape —
    /// root-relative, segment-escaped).</summary>
    private static string ToMediaUrl(string mediaOrigin, string root, string fullPath)
    {
        var rel = Path.GetRelativePath(root, fullPath).Replace('\\', '/');
        var escaped = string.Join('/', rel.Split('/').Select(Uri.EscapeDataString));
        return $"{mediaOrigin}/umedia/{escaped}";
    }
}
