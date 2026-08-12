using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.Fyp.Online;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.Chaos;

/// <summary>
/// Enumerates the user's active preset (App.EffectiveAssetsPath images/ + videos/) into the
/// manifest the DtRH browser game consumes over the bridge (hostMedia.js). Entries become
/// https://ccp.assets/ URLs; the page never sees a disk path.
///
/// Only browser-decodable formats are listed - LibVLC-only containers (wmv/avi/mkv/mov...)
/// are counted as skipped so the game can be honest about what it can't show. Native video
/// payloads keep playing those through VideoService untouched.
///
/// Since the app-wide remote-media work the manifest can also carry a bounded tail of
/// REMOTE entries (absolute CDN urls, no ccp.assets path behind them) when the user has
/// turned the app-wide source away from "local". Those are CORS-tainted and are therefore
/// usable only by the page's DOM layer, never by its WebGL one - see the remote-media
/// section at the foot of this file before touching them.
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

            // Remote entries ride ON TOP of the local pool, AFTER the downsample: they are a
            // bounded handful (MaxRemoteEntries) and sampling them against a 5000-file library
            // would leave "online" mode looking broken for exactly the users who asked for it.
            int remote = AppendRemote(m);

            App.Logger?.Information("DtrhAssetManifest: {I} images, {V} videos, {S} skipped{T}{R}",
                m.Images.Count, m.Videos.Count, m.Skipped, m.Truncated ? " (truncated)" : "",
                remote > 0 ? $" (+{remote} remote)" : "");
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
    ///
    /// LOCAL DISK ONLY, deliberately: this yields full paths and byte lengths, which a remote
    /// entry has neither of. The transfer compression planner is the consumer, and remote
    /// media must not become something the app offers to compress or hand to a duel partner.
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

    // ========================= remote media (Phase 2, Contract 3) =========================
    //
    // Remote entries carry an ABSOLUTE CDN url and deliberately never go through
    // ToAssetUrl - there is no ccp.assets path behind them. They ride the SAME manifest
    // frame the page already consumes, because the frame's shape ({name,url} per entry) is
    // owned by DtrhHostService/GoonHostService and neither is ours to widen. The name
    // therefore carries the marker:
    //
    //     "online<pct>:<sub>-<postId>.<ext>"      pct = the remote share, 0..100
    //
    // hostMedia.js parses that prefix for the mix ratio, but decides remote-vs-local from
    // the URL's ORIGIN (anything not https://ccp.*) - a marker is a hint, an origin is a
    // fact, and only the fact can be trusted to keep tainted media out of WebGL.
    //
    // B3 / CORS: scrolller's CDN sends no Access-Control-Allow-Origin, so these URLs are
    // tainted. Every three.js consumer in the page (spawner wall cards, wallPosters, the
    // Mirror biomes) either fetch()es the bytes or sets crossOrigin='anonymous' before a
    // texture upload, and BOTH of those hard-fail on a tainted source. hostMedia.js keeps
    // remote entries out of draw()/drawKind()/favorite() for that reason and hands them
    // only to the DOM payload layer (game/payloadFx.js). See the header there.
    //
    // BRIGHT LINE: the fetch below goes from this machine straight to scrolller. No CC Labs
    // server is involved, and nothing here writes third-party BYTES to disk - the cache is
    // a list of URLs, which is the same class of data as the niche selection in settings.

    /// <summary>Marker prefix on a remote entry's name; the page mirrors this regex.</summary>
    private const string RemoteNamePrefix = "online";
    private const string RemoteConsumerId = "dtrh";
    private const int MaxRemoteEntries = 60;
    private const int RemoteLowWater = 24;               // refill when the cache drops under this
    private static readonly TimeSpan RemoteEntryTtl = TimeSpan.FromDays(3);

    private static readonly object RemoteLock = new();
    private static List<RemoteEntry>? _remoteCache;      // null = not loaded from disk yet
    private static int _remoteFetchInFlight;             // 0/1 via Interlocked

    private sealed record RemoteEntry(string Id, string Url, bool IsImage, long AtUnix);

    private static string RemoteCachePath => Path.Combine(App.UserDataPath, "dtrh_remote_media.json");

    /// <summary>
    /// Append the cached remote pool to <paramref name="m"/> and top the cache up in the
    /// background. Returns how many entries were added. Never throws.
    ///
    /// SYNCHRONOUS BY NECESSITY, and therefore ONE LAUNCH BEHIND on a cold cache. Build() is
    /// called from the host's OnPageReady on the UI thread and the manifest is posted exactly
    /// once per page (there is no re-post path), so a network round trip here would either
    /// block the UI thread or arrive after the only frame that could carry it. The cache is
    /// persisted instead: the first ever DTRH launch with remote media enabled shows none and
    /// warms the file; every launch after that is instant.
    /// </summary>
    private static int AppendRemote(Manifest m)
    {
        try
        {
            var s = App.Settings?.Current;
            // Two gates, both required: the app-wide source must have left "local" AND the
            // user must have accepted a consent card (either this one or the FYP feed's -
            // HasRemoteMediaConsent is the property that knows about both).
            if (s == null || s.MediaSource == "local" || !s.HasRemoteMediaConsent) return 0;

            var pool = LoadRemoteCache();
            int pct = s.MediaSource == "online" ? 100 : Math.Clamp(s.RemoteMediaRatio, 5, 95);
            int added = 0;
            // `pool` is a snapshot, so no lock is needed to walk it.
            foreach (var e in pool)
            {
                var name = $"{RemoteNamePrefix}{pct}:{NameFor(e)}";
                (e.IsImage ? m.Images : m.Videos).Add(new Entry(name, e.Url));
                added++;
            }
            KickRemoteRefill();
            return added;
        }
        catch (Exception ex)
        {
            App.Logger?.Debug("DtrhAssetManifest: remote append failed: {E}", ex.Message);
            return 0;
        }
    }

    /// <summary>"scrolller/EroticHypnosis/12345" + ".webm" -> "EroticHypnosis-12345.webm".
    /// The page never shows these, but DtrhAssetStatsStore keys engagement by name, so they
    /// must not be able to collide with a preset filename (same reason mod entries carry a
    /// "mod:" prefix). The "online&lt;pct&gt;:" prefix the caller adds does that job.</summary>
    private static string NameFor(RemoteEntry e)
    {
        var parts = e.Id.Split('/');
        var stem = parts.Length >= 3 ? $"{parts[1]}-{parts[2]}" : e.Id.Replace('/', '-');
        var ext = "";
        int cut = e.Url.IndexOfAny(new[] { '?', '#' });
        var clean = cut >= 0 ? e.Url[..cut] : e.Url;
        int dot = clean.LastIndexOf('.');
        if (dot > 0 && clean.Length - dot <= 6) ext = clean[dot..];
        return stem + ext;
    }

    /// <summary>The cache, loaded from disk on first use and pruned of stale entries.
    /// Returns a snapshot the caller can enumerate without holding the lock.</summary>
    private static List<RemoteEntry> LoadRemoteCache()
    {
        lock (RemoteLock)
        {
            if (_remoteCache == null)
            {
                _remoteCache = new List<RemoteEntry>();
                try
                {
                    if (File.Exists(RemoteCachePath))
                    {
                        var o = JObject.Parse(File.ReadAllText(RemoteCachePath));
                        if (o["entries"] is JArray arr)
                        {
                            foreach (var t in arr)
                            {
                                var id = (string?)t["id"];
                                var url = (string?)t["url"];
                                if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(url)) continue;
                                _remoteCache.Add(new RemoteEntry(id!, url!, (bool?)t["image"] ?? false,
                                    (long?)t["at"] ?? 0));
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    App.Logger?.Debug("DtrhAssetManifest: remote cache load failed: {E}", ex.Message);
                }
            }
            PruneRemoteLocked();
            return new List<RemoteEntry>(_remoteCache);
        }
    }

    /// <summary>
    /// Drop entries older than the TTL (a CDN url is not forever) and anything the user has
    /// blocked SINCE it was cached. The coordinator's blocklist runs before entries ever
    /// reach the cache, but this pool outlives the session that filled it, so a block made
    /// today has to reach yesterday's cache too - otherwise "block this subreddit" would be
    /// visibly ignored for up to the TTL. Caller holds <see cref="RemoteLock"/>.
    /// </summary>
    private static void PruneRemoteLocked()
    {
        if (_remoteCache == null) return;
        long cutoff = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - (long)RemoteEntryTtl.TotalSeconds;
        _remoteCache.RemoveAll(e => e.AtUnix < cutoff);

        var s = App.Settings?.Current;
        var subs = new HashSet<string>(s?.RemoteMediaBlockedSubs ?? new List<string>(),
            StringComparer.OrdinalIgnoreCase);
        var ids = new HashSet<string>(s?.RemoteMediaBlockedIds ?? new List<string>(), StringComparer.Ordinal);
        if (subs.Count > 0 || ids.Count > 0)
        {
            _remoteCache.RemoveAll(e =>
            {
                if (ids.Contains(e.Id)) return true;
                var parts = e.Id.Split('/');
                return parts.Length >= 3 && subs.Contains(parts[1]);
            });
        }

        if (_remoteCache.Count > MaxRemoteEntries)
            _remoteCache.RemoveRange(0, _remoteCache.Count - MaxRemoteEntries);
    }

    /// <summary>Top the cache up off the UI thread, single-flight. Fire-and-forget by design:
    /// whatever lands is for the NEXT manifest, so nothing waits on it.</summary>
    private static void KickRemoteRefill()
    {
        lock (RemoteLock)
        {
            if (_remoteCache != null && _remoteCache.Count >= RemoteLowWater) return;
        }
        if (Interlocked.CompareExchange(ref _remoteFetchInFlight, 1, 0) != 0) return;
        _ = Task.Run(async () =>
        {
            try
            {
                var coord = FypOnlineCoordinator.For(RemoteConsumerId, RemoteChannels, FeedMediaKind.Any);
                var (entries, error) = await coord.FetchBatchAsync(CancellationToken.None).ConfigureAwait(false);
                if (error != null)
                {
                    App.Logger?.Debug("DtrhAssetManifest: remote refill failed ({E})", error);
                    return;
                }
                int kept = 0;
                long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                lock (RemoteLock)
                {
                    _remoteCache ??= new List<RemoteEntry>();
                    var known = new HashSet<string>(_remoteCache.Select(e => e.Id), StringComparer.Ordinal);
                    foreach (var e in entries)
                    {
                        // RemoteMediaFormats is the ONE authority on what a remote entry may
                        // be (blocker B7). The local extension lists at the top of this file
                        // describe a disk the user controls; they do not describe a CDN.
                        if (!RemoteMediaFormats.Validate(e, FeedMediaKind.Any, out var reason))
                        {
                            App.Logger?.Debug("DtrhAssetManifest: rejected remote entry {Id}: {Reason}", e.Id, reason);
                            continue;
                        }
                        if (!known.Add(e.Id)) continue;
                        _remoteCache.Add(new RemoteEntry(e.Id, e.Url,
                            e.Type == RemoteMediaFormats.TypeImage, now));
                        kept++;
                    }
                    PruneRemoteLocked();
                    SaveRemoteCacheLocked();
                }
                if (kept > 0)
                    App.Logger?.Information("DtrhAssetManifest: remote pool +{N} (next launch shows them)", kept);
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("DtrhAssetManifest: remote refill threw: {E}", ex.Message);
            }
            finally { Interlocked.Exchange(ref _remoteFetchInFlight, 0); }
        });
    }

    /// <summary>DTRH's channel set. NICHE SELECTION IS SHARED APP-WIDE on purpose (see the
    /// AppSettings remarks next to MediaSource): one taxonomy, one selection, many surfaces.
    /// Only the rotation/dwell state is per-consumer, which is what the tenant id buys.</summary>
    private static IReadOnlyList<string> RemoteChannels()
    {
        var s = App.Settings?.Current;
        return FypOnlineCoordinator.ResolveChannels(s?.FypOnlineNiches, s?.FypOnlineCustomSubs);
    }

    /// <summary>Caller holds <see cref="RemoteLock"/>. Never throws.</summary>
    private static void SaveRemoteCacheLocked()
    {
        try
        {
            var arr = new JArray();
            foreach (var e in _remoteCache ?? new List<RemoteEntry>())
                arr.Add(new JObject { ["id"] = e.Id, ["url"] = e.Url, ["image"] = e.IsImage, ["at"] = e.AtUnix });
            Directory.CreateDirectory(App.UserDataPath);
            File.WriteAllText(RemoteCachePath,
                new JObject { ["entries"] = arr }.ToString(Newtonsoft.Json.Formatting.None));
        }
        catch (Exception ex)
        {
            App.Logger?.Debug("DtrhAssetManifest: remote cache save failed: {E}", ex.Message);
        }
    }
}
