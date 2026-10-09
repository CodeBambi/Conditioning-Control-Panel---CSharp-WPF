using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Fyp;
using ConditioningControlPanel.Services.Fyp.Online;
using Serilog;

namespace ConditioningControlPanel.Services.Flash
{
    /// <summary>
    /// The ready pool of online clips (WPF Services/Flash/RemoteFlashPool.cs, unchanged). The
    /// caller holds its lock. A selection generation rejects stale downloads; every drawn clip
    /// moves to the SHOWN list, and once fresh runs dry a burst reuses the least recently shown.
    /// </summary>
    internal sealed class RemoteFlashPool
    {
        internal const int ShownMax = 256;

        private readonly List<string> _ready = new();
        private readonly LinkedList<string> _shown = new();
        private string _selection = "";
        internal int Generation { get; private set; }
        internal int Count => _ready.Count;
        internal int Available => _ready.Count + _shown.Count;
        internal bool Select(IEnumerable<string> channels)
        {
            var key = string.Join("|", channels.Select(x => x.ToLowerInvariant()).Distinct().OrderBy(x => x));
            if (key == _selection) return false;
            _selection = key;
            Clear();
            return true;
        }
        internal bool Add(int generation, string url)
        {
            if (generation != Generation || _ready.Contains(url)) return false;
            _shown.Remove(url);
            _ready.Add(url);
            return true;
        }
        internal string? Take(Random random, Func<string, bool> usable)
        {
            while (_ready.Count > 0)
            {
                int index = random.Next(_ready.Count);
                var url = _ready[index];
                _ready.RemoveAt(index);
                if (!usable(url)) continue;
                Remember(url);
                return url;
            }
            return null;
        }
        internal string? TakeShown(Func<string, bool> usable)
        {
            while (_shown.First is { } node)
            {
                _shown.RemoveFirst();
                if (!usable(node.Value)) continue;
                _shown.AddLast(node);
                return node.Value;
            }
            return null;
        }
        internal void Clear() { Generation++; _ready.Clear(); _shown.Clear(); }

        private void Remember(string url)
        {
            _shown.Remove(url);
            _shown.AddLast(url);
            while (_shown.Count > ShownMax) _shown.RemoveFirst();
        }
    }

    /// <summary>One remote flash ready to show: the URL is its identity (history, session log),
    /// the poster and clip are files already on disk.</summary>
    internal readonly record struct RemoteFlashItem(string Url, string PosterPath, string ClipPath);

    /// <summary>
    /// The source rules of WPF FlashService.GetNextImages / ShouldDrawRemote / RemoteFlashesEnabled
    /// (Services/Flash/FlashService.cs:3714, :4021, :4188), pure so both heads and the tests share them.
    /// </summary>
    internal static class FlashSourceRules
    {
        /// <summary>WPF RemoteFlashesEnabled: the source is not "local" AND the user consented
        /// (HasRemoteMediaConsent, so a For You consent counts).</summary>
        internal static bool RemoteEnabled(AppSettings? s)
            => s != null
               && !string.Equals(s.MediaSource, "local", StringComparison.OrdinalIgnoreCase)
               && s.HasRemoteMediaConsent;

        /// <summary>WPF ShouldDrawRemote: no local pool = all remote; "online" = all remote;
        /// "mixed" = RemoteMediaRatio percent remote. Re-reads the gate every pick (#1037).</summary>
        internal static bool ShouldDrawRemote(AppSettings? s, bool haveLocal, Random rng)
        {
            if (!RemoteEnabled(s)) return false;
            if (!haveLocal) return true;
            if (string.Equals(s!.MediaSource, "online", StringComparison.OrdinalIgnoreCase)) return true;
            return rng.Next(100) < Math.Clamp(s.RemoteMediaRatio, 0, 100);
        }

        /// <summary>WPF IsRemotePath: an absolute http(s) URL, which no local path can look like.</summary>
        internal static bool IsRemotePath(string? path)
            => !string.IsNullOrEmpty(path)
               && (path!.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                   || path.StartsWith("https://", StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// The burst plan of WPF GetNextImages: for each of <paramref name="count"/> picks, true =
        /// draw remote, false = draw local. Remote goes first because it is the pool that can decline;
        /// <paramref name="takeRemote"/> returning false (pool went cold) falls through to local, and
        /// with no local pool the burst stops there. Empty when every pool is empty.
        /// </summary>
        internal static List<bool> Plan(int count, AppSettings? s, bool haveLocal, int remoteReady, Random rng, Func<bool> takeRemote)
        {
            var plan = new List<bool>(Math.Max(0, count));
            if (!haveLocal && remoteReady == 0) return plan;
            for (var i = 0; i < count; i++)
            {
                if (remoteReady > 0 && ShouldDrawRemote(s, haveLocal, rng))
                {
                    if (takeRemote()) { plan.Add(true); continue; }
                    remoteReady = 0;
                    if (!haveLocal) break;
                }
                if (haveLocal) plan.Add(false);
            }
            return plan;
        }
    }

    /// <summary>
    /// The third flash pool, online clips (WPF FlashService "Remote media (Phase 3 - Contract 2)").
    /// Scrolller's GIF feed through the "flashes" tenant of <see cref="FypOnlineCoordinator"/>,
    /// the same niche selection the For You feed uses. Each entry is materialized (clip + poster
    /// on disk) before it enters the ready pool, so a draw never waits on the network. Off unless
    /// <see cref="FlashSourceRules.RemoteEnabled"/>; a source that is down reads as "no remote
    /// content", never as broken flashes.
    /// </summary>
    internal static class RemoteFlashSource
    {
        private const string ConsumerId = "flashes";
        private const int ReadyTarget = 12;
        private const int ReadyMax = 24;
        private const int PrefetchGapSeconds = 1;
        private const int ClipMaxBytes = 40 * 1024 * 1024;

        private static readonly RemoteFlashPool Pool = new();
        private static readonly object Lock = new();
        private static readonly ConcurrentDictionary<string, RemoteFlashItem> Items = new();
        private static readonly CancellationTokenSource Cts = new();
        private static bool _inFlight;
        private static DateTime _lastFetchUtc = DateTime.MinValue;
        private static HttpClient? _http;

        /// <summary>Settings reader (tests swap it).</summary>
        internal static Func<AppSettings?> Settings = () => CoreSettings.Current;

        /// <summary>Batch fetcher (tests swap it). Default = the coordinator tenant.</summary>
        internal static Func<CancellationToken, Task<(List<FypAssetManifest.Entry> Entries, string? Error)>> Fetch = DefaultFetch;

        /// <summary>Where clips and posters land.</summary>
        internal static string CacheDir => Path.Combine(Path.GetTempPath(), "ccp-flash-remote");

        private static IReadOnlyList<string> Channels()
        {
            var s = Settings();
            return FypOnlineCoordinator.ResolveChannels(s?.FypOnlineNiches, s?.FypOnlineCustomSubs);
        }

        private static Task<(List<FypAssetManifest.Entry> Entries, string? Error)> DefaultFetch(CancellationToken ct)
            => FypOnlineCoordinator.For(ConsumerId, Channels, FeedMediaKind.GifClip).FetchBatchAsync(ct);

        private static void RefreshSelection()
        {
            lock (Lock)
                if (Pool.Select(Channels())) _lastFetchUtc = DateTime.MinValue;
        }

        /// <summary>Clips ready to draw (fresh + reusable shown).</summary>
        internal static int ReadyCount
        {
            get { lock (Lock) return Pool.Available; }
        }

        /// <summary>Kick a background top-up; returns at once. Cheap to call on every draw.</summary>
        internal static void EnsurePrefetch()
        {
            RefreshSelection();
            if (!FlashSourceRules.RemoteEnabled(Settings())) return;
            lock (Lock)
            {
                if (_inFlight || Pool.Count >= ReadyTarget) return;
                if ((DateTime.UtcNow - _lastFetchUtc).TotalSeconds < PrefetchGapSeconds) return;
                _inFlight = true;
                _lastFetchUtc = DateTime.UtcNow;
            }
            _ = Task.Run(PrefetchBatchAsync);
        }

        /// <summary>Consume one ready clip (fresh first, then the least recently shown).</summary>
        internal static RemoteFlashItem? TryTake(Random rng)
        {
            RefreshSelection();
            lock (Lock)
            {
                Func<string, bool> usable = url => Items.TryGetValue(url, out var it) && File.Exists(it.PosterPath);
                var url = Pool.Take(rng, usable) ?? Pool.TakeShown(usable);
                return url != null && Items.TryGetValue(url, out var item) ? item : null;
            }
        }

        /// <summary>Test seam: put a materialized item straight into the ready pool.</summary>
        internal static bool AddReady(RemoteFlashItem item)
        {
            RefreshSelection();
            lock (Lock)
            {
                Items[item.Url] = item;
                return Pool.Add(Pool.Generation, item.Url);
            }
        }

        /// <summary>Test seam: forget everything.</summary>
        internal static void ResetForTests()
        {
            lock (Lock) { Pool.Clear(); _inFlight = false; _lastFetchUtc = DateTime.MinValue; }
            Items.Clear();
        }

        /// <summary>WPF ClearFileCache: drop the warm clips and refetch for the current selection.</summary>
        internal static void ClearReady()
        {
            lock (Lock) { Pool.Clear(); _lastFetchUtc = DateTime.MinValue; }
        }

        /// <summary>Shutdown: stop the prefetch and delete every clip and poster in
        /// <see cref="CacheDir"/> (WPF RemoteMediaCache's temp sweep). Never throws.</summary>
        internal static void CleanupCache()
        {
            try { Cts.Cancel(); } catch { }
            lock (Lock) Pool.Clear();
            Items.Clear();
            try
            {
                if (!Directory.Exists(CacheDir)) return;
                foreach (var f in Directory.EnumerateFiles(CacheDir)) TryDelete(f);
                Directory.Delete(CacheDir, false);
            }
            catch (Exception ex) { Log.Debug("Flash: remote cache cleanup incomplete: {Error}", ex.Message); }
        }

        private static async Task PrefetchBatchAsync()
        {
            try
            {
                var ct = Cts.Token;
                int generation;
                lock (Lock) generation = Pool.Generation;
                var entries = new List<FypAssetManifest.Entry>();
                string? error = null;
                // Small portions from several channels prevent one page dominating the pool.
                for (var batch = 0; batch < 3; batch++)
                {
                    var result = await Fetch(ct).ConfigureAwait(false);
                    entries.AddRange(result.Entries.Take(4));
                    error = result.Error;
                    if (ct.IsCancellationRequested) return;
                }
                if (error != null && entries.Count == 0)
                {
                    Log.Debug("Flash: remote fetch failed ({Error}); staying on the local pool", error);
                    return;
                }

                var warmed = 0;
                foreach (var entry in entries)
                {
                    if (ct.IsCancellationRequested) break;
                    lock (Lock) if (Pool.Count >= ReadyMax) break;
                    if (!RemoteMediaFormats.Validate(entry, FeedMediaKind.GifClip, out var reason))
                    {
                        Log.Debug("Flash: dropped remote entry {Id}: {Reason}", entry?.Id, reason);
                        continue;
                    }
                    if (string.IsNullOrEmpty(entry.PosterUrl)) continue;
                    var url = entry.SmallUrl ?? entry.Url;
                    var poster = await DownloadAsync(entry.PosterUrl!, ct).ConfigureAwait(false);
                    if (poster == null) continue;
                    var clip = await DownloadAsync(url, ct, ClipMaxBytes).ConfigureAwait(false) ?? "";
                    RefreshSelection();
                    lock (Lock)
                    {
                        if (generation != Pool.Generation) return;
                        if (Items.Count >= 512)
                            foreach (var key in Items.Keys.Take(128).ToList())
                                if (Items.TryRemove(key, out var old)) { TryDelete(old.PosterPath); TryDelete(old.ClipPath); }
                        Items[url] = new RemoteFlashItem(url, poster, clip);
                        if (Pool.Add(generation, url)) warmed++;
                    }
                }
                if (warmed > 0) Log.Information("Flash: warmed {Warmed} remote clip(s), {Ready} ready", warmed, ReadyCount);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { Log.Debug("Flash: remote prefetch failed (non-fatal): {Error}", ex.Message); }
            finally { lock (Lock) _inFlight = false; }
        }

        private static HttpClient Http => _http ??= new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

        /// <summary>Bytes of <paramref name="url"/> into the cache dir, keyed by a hash of the whole
        /// URL. Null on any failure or when the body exceeds <paramref name="maxBytes"/>.</summary>
        private static async Task<string?> DownloadAsync(string url, CancellationToken ct, int maxBytes = 8 * 1024 * 1024)
        {
            try
            {
                if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
                    || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)) return null;
                Directory.CreateDirectory(CacheDir);
                var ext = Path.GetExtension(uri.AbsolutePath).ToLowerInvariant();
                if (ext.Length is 0 or > 5) ext = ".bin";
                var name = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(url)))[..20] + ext;
                var path = Path.Combine(CacheDir, name);
                if (File.Exists(path) && new FileInfo(path).Length > 0) return path;

                using var resp = await Http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode) return null;
                if (resp.Content.Headers.ContentLength is long len && len > maxBytes) return null;
                var tmp = path + ".part";
                await using (var src = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
                await using (var dst = File.Create(tmp))
                {
                    var buf = new byte[81920];
                    long total = 0;
                    int n;
                    while ((n = await src.ReadAsync(buf, ct).ConfigureAwait(false)) > 0)
                    {
                        total += n;
                        if (total > maxBytes) { dst.Close(); TryDelete(tmp); return null; }
                        await dst.WriteAsync(buf.AsMemory(0, n), ct).ConfigureAwait(false);
                    }
                }
                File.Move(tmp, path, overwrite: true);
                return path;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                Log.Debug("Flash: remote download from {Host} failed: {Error}", SafeHost(url), ex.Message);
                return null;
            }
        }

        private static string SafeHost(string url) => Uri.TryCreate(url, UriKind.Absolute, out var u) ? u.Host : "?";

        private static void TryDelete(string? path)
        {
            if (string.IsNullOrEmpty(path)) return;
            try { File.Delete(path); } catch { }
        }
    }
}
