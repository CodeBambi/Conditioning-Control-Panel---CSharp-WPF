// PORTED from WPF 7.1.5 Services/Chaos/DtrhAssetManifest.cs "remote media (Phase 2, Contract 3)":
// AppendRemote, NameFor, LoadRemoteCache, PruneRemoteLocked, CountKinds, KickRemoteRefill,
// RemoteChannels, SaveRemoteCacheLocked. Same constants, same cache file, same name marker.
//
// Remote entries carry an ABSOLUTE CDN url and never go through the asset server. They ride the
// same manifest frame, so the name carries the marker "online<pct>:<sub>-<postId>.<ext>" (pct = the
// remote share, 0..100). The page decides remote-vs-local from the URL's origin and keeps remote
// entries out of WebGL (the CDN sends no CORS header), handing them to the DOM layer only.
//
// BRIGHT LINE: the fetch goes from this machine straight to the feed. No CC Labs server is involved
// and no third-party BYTES are written to disk: the cache is a list of URLs.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Fyp.Online;
using Newtonsoft.Json.Linq;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Games
{
    internal static partial class GameMediaManifest
    {
        /// <summary>Marker prefix on a remote entry's name; the page mirrors this regex.</summary>
        internal const string RemoteNamePrefix = "online";
        private const string RemoteConsumerId = "dtrh";
        internal const int MaxRemoteEntries = 60;
        internal const int RemoteKindLowWater = 12;           // refill a kind when it drops under this
        private static readonly TimeSpan RemoteEntryTtl = TimeSpan.FromDays(3);

        private static readonly object RemoteLock = new();
        private static List<RemoteEntry>? _remoteCache;      // null = not loaded from disk yet
        private static int _remoteFetchInFlight;             // 0/1 via Interlocked

        internal sealed record RemoteEntry(string Id, string Url, bool IsImage, long AtUnix);

        /// <summary>Tests: where the URL cache lives. Null = the user-data folder.</summary>
        internal static string? RemoteCachePathOverride { get; set; }

        /// <summary>Tests: stands in for the background refill (no network in a test).</summary>
        internal static Action? RefillOverride { get; set; }

        private static string RemoteCachePath =>
            RemoteCachePathOverride ?? Path.Combine(CorePaths.UserData, "dtrh_remote_media.json");

        /// <summary>Tests: forget the loaded cache so the next build reads the file again.</summary>
        internal static void ResetRemoteCacheForTest()
        {
            lock (RemoteLock) _remoteCache = null;
        }

        /// <summary>The two gates, both required: the app-wide source has left "local" AND a consent
        /// card was accepted (this one or the For You feed's: HasRemoteMediaConsent knows both).</summary>
        internal static bool RemoteAllowed(AppSettings? s) =>
            s != null && s.MediaSource != "local" && s.HasRemoteMediaConsent;

        /// <summary>
        /// Appends the cached remote pool to <paramref name="m"/> and tops the cache up in the
        /// background. Returns how many entries were added. Never throws.
        ///
        /// SYNCHRONOUS BY NECESSITY, and therefore ONE LAUNCH BEHIND on a cold cache: the manifest is
        /// posted exactly once per page, so a network round trip here would block the UI thread or
        /// arrive after the only frame that could carry it. The first launch with remote media on
        /// shows none and warms the file; every launch after that is instant.
        /// </summary>
        internal static int AppendRemote(Manifest m, AppSettings? s)
        {
            try
            {
                if (!RemoteAllowed(s)) return 0;

                var pool = LoadRemoteCache();
                int pct = s!.MediaSource == "online" ? 100 : Math.Clamp(s.RemoteMediaRatio, 5, 95);
                int added = 0;
                foreach (var e in pool)   // a snapshot: no lock needed to walk it
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
                Log.Debug("[Game] manifest: remote append failed: {E}", ex.Message);
                return 0;
            }
        }

        /// <summary>"scrolller/EroticHypnosis/12345" + ".webm" -> "EroticHypnosis-12345.webm". The
        /// "online&lt;pct&gt;:" prefix the caller adds keeps it from colliding with a local file name.</summary>
        internal static string NameFor(RemoteEntry e)
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

        /// <summary>The cache, loaded from disk on first use and pruned of stale entries. Returns a
        /// snapshot the caller can enumerate without holding the lock.</summary>
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
                                    _remoteCache.Add(new RemoteEntry(id!, url!, (bool?)t["image"] ?? false, (long?)t["at"] ?? 0));
                                }
                            }
                        }
                    }
                    catch (Exception ex) { Log.Debug("[Game] manifest: remote cache load failed: {E}", ex.Message); }
                }
                PruneRemoteLocked();
                return new List<RemoteEntry>(_remoteCache);
            }
        }

        /// <summary>Drops entries older than the TTL (a CDN url is not forever). Caller holds the lock.</summary>
        private static void PruneRemoteLocked()
        {
            if (_remoteCache == null) return;
            long cutoff = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - (long)RemoteEntryTtl.TotalSeconds;
            _remoteCache.RemoveAll(e => e.AtUnix < cutoff);

            // Oldest first, per kind, so a run of clips can never push the last stills out.
            foreach (bool image in new[] { true, false })
            {
                int over = _remoteCache.Count(e => e.IsImage == image) - MaxRemoteEntries / 2;
                if (over > 0) _remoteCache.RemoveAll(e => e.IsImage == image && over-- > 0);
            }
        }

        /// <summary>(stills, clips) in the cache.</summary>
        internal static (int Stills, int Clips) CountKinds(IReadOnlyCollection<RemoteEntry>? cache)
        {
            if (cache == null) return (0, 0);
            int stills = cache.Count(e => e.IsImage);
            return (stills, cache.Count - stills);
        }

        /// <summary>Tops the cache up off the UI thread, single-flight. Fire-and-forget by design:
        /// whatever lands is for the NEXT manifest, so nothing waits on it.</summary>
        private static void KickRemoteRefill()
        {
            // Each kind has its own low-water mark: one batch off a video-only sub used to fill the
            // cache with clips and nothing else, and a full cache never refilled (WPF, Sep 23).
            bool wantStills;
            lock (RemoteLock)
            {
                var (stills, clips) = CountKinds(_remoteCache);
                if (stills >= RemoteKindLowWater && clips >= RemoteKindLowWater) return;
                wantStills = stills < RemoteKindLowWater && stills <= clips;
            }
            if (RefillOverride is { } fake) { fake(); return; }
            if (Interlocked.CompareExchange(ref _remoteFetchInFlight, 1, 0) != 0) return;
            _ = Task.Run(async () =>
            {
                try
                {
                    // Stills come from GIF posts' posters, the same kind the flashes draw. Separate
                    // tenants keep the two rotations apart.
                    var coord = wantStills
                        ? FypOnlineCoordinator.For(RemoteConsumerId + "-stills", RemoteChannels, FeedMediaKind.GifStill)
                        : FypOnlineCoordinator.For(RemoteConsumerId, RemoteChannels, FeedMediaKind.Video);
                    var (entries, error) = await coord.FetchBatchAsync(CancellationToken.None).ConfigureAwait(false);
                    if (error != null)
                    {
                        Log.Debug("[Game] manifest: remote refill failed ({E})", error);
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
                            // RemoteMediaFormats is the ONE authority on what a remote entry may be.
                            if (!RemoteMediaFormats.Validate(e, FeedMediaKind.Any, out var reason))
                            {
                                Log.Debug("[Game] manifest: rejected remote entry {Id}: {Reason}", e.Id, reason);
                                continue;
                            }
                            if (!known.Add(e.Id)) continue;
                            _remoteCache.Add(new RemoteEntry(e.Id, e.Url, e.Type == RemoteMediaFormats.TypeImage, now));
                            kept++;
                        }
                        PruneRemoteLocked();
                        SaveRemoteCacheLocked();
                    }
                    if (kept > 0) Log.Information("[Game] manifest: remote pool +{N} (next launch shows them)", kept);
                }
                catch (Exception ex) { Log.Debug("[Game] manifest: remote refill threw: {E}", ex.Message); }
                finally { Interlocked.Exchange(ref _remoteFetchInFlight, 0); }
            });
        }

        /// <summary>The niche selection is shared app-wide on purpose: one taxonomy, one selection,
        /// many surfaces. Only the rotation state is per consumer, which the tenant id buys.</summary>
        private static IReadOnlyList<string> RemoteChannels()
        {
            var s = CoreSettings.Current;
            return FypOnlineCoordinator.ResolveChannels(s.FypOnlineNiches, s.FypOnlineCustomSubs);
        }

        /// <summary>Caller holds the lock. Never throws.</summary>
        private static void SaveRemoteCacheLocked()
        {
            try
            {
                var arr = new JArray();
                foreach (var e in _remoteCache ?? new List<RemoteEntry>())
                    arr.Add(new JObject { ["id"] = e.Id, ["url"] = e.Url, ["image"] = e.IsImage, ["at"] = e.AtUnix });
                var dir = Path.GetDirectoryName(RemoteCachePath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(RemoteCachePath, new JObject { ["entries"] = arr }.ToString(Newtonsoft.Json.Formatting.None));
            }
            catch (Exception ex) { Log.Debug("[Game] manifest: remote cache save failed: {E}", ex.Message); }
        }
    }
}
