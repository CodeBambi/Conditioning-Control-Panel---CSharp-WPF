// PORTED from WPF 7.1.5 Services/GoonGame/GoonOnlineMedia.cs (the fetcher half; the rules are in
// CCP.Core/Services/GoonGame/GoonOnlineMediaRules.cs). Deviations: RemoteMediaCache (not ported) becomes
// GoonRemoteFiles below, same folder ({assets}/.temp) and ownership rule; App.* becomes Core statics.
// Item.Url stays the WPF https://ccp.assets/ form; the window rewrites it onto the loopback asset server.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.Fyp;
using ConditioningControlPanel.Services.Fyp.Online;
using Serilog;

namespace ConditioningControlPanel.Services.GoonGame
{
    /// <summary>One https picture or clip as a file under {assets}/.temp (WPF RemoteMediaCache.
    /// MaterializeAsync with ownerReleases: the pool that asked releases it). Media extensions only,
    /// 40 MB cap, null on any failure.</summary>
    internal static class GoonRemoteFiles
    {
        internal const string Prefix = "ccp_remote_";
        private const long MaxBytes = 40L * 1024 * 1024;
        private static readonly string[] Exts = { ".mp4", ".webm", ".webp", ".jpg", ".jpeg", ".png" };
        private static readonly System.Net.Http.HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

        internal static async Task<string?> MaterializeAsync(string url, CancellationToken ct)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps) return null;
            var ext = Path.GetExtension(uri.AbsolutePath).ToLowerInvariant();
            if (Array.IndexOf(Exts, ext) < 0) return null;
            var root = CorePaths.EffectiveAssets;
            if (string.IsNullOrEmpty(root)) return null;
            var dir = Path.Combine(root, ".temp");
            string path = Path.Combine(dir, $"{Prefix}{Guid.NewGuid():N}{ext}");
            try
            {
                Directory.CreateDirectory(dir);
                using var resp = await Http.GetAsync(uri, System.Net.Http.HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode || resp.Content.Headers.ContentLength is > MaxBytes) return null;
                await using (var src = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
                await using (var dst = File.Create(path))
                {
                    var buf = new byte[81920];
                    long total = 0;
                    int n;
                    while ((n = await src.ReadAsync(buf, ct).ConfigureAwait(false)) > 0)
                    {
                        total += n;
                        if (total > MaxBytes) throw new IOException("over the cap");
                        await dst.WriteAsync(buf.AsMemory(0, n), ct).ConfigureAwait(false);
                    }
                }
                return path;
            }
            catch (Exception ex)
            {
                Log.Debug("GoonOnlineMedia: download failed ({Type})", ex.GetType().Name);
                Release(path);
                return null;
            }
        }

        internal static void Release(string? path)
        {
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                if (Path.GetFileName(path).StartsWith(Prefix, StringComparison.Ordinal) && File.Exists(path)) File.Delete(path);
            }
            catch { /* still open in the page: left for the next sweep */ }
        }
    }

    /// <summary>
    /// Scrolller pictures for the Goon Game, fetched through the shared online stack
    /// (<see cref="FypOnlineCoordinator"/> tenants <c>goon-stills</c> / <c>goon-clips</c>) and
    /// materialised by <see cref="RemoteMediaCache"/> under <c>App.GetMediaTempPath()</c>, which
    /// <c>ccp.assets</c> already maps. One instance per open game window.
    ///
    /// A <see cref="Start"/> with a new niche list cancels the wave in flight and releases every
    /// temp file this instance owned; <see cref="Dispose"/> does the same on close. Snapshots are
    /// raised from worker threads, each one the WHOLE current list; the host marshals.
    /// </summary>
    internal sealed class GoonOnlineMedia : IDisposable
    {
        public sealed record Item(string Name, string Url, string Path);

        public sealed record Snapshot(string State, IReadOnlyList<string> Subs,
            IReadOnlyList<Item> Images, IReadOnlyList<Item> Videos, int Have, int Want);

        private const string OwnStillTenant = "goon-stills";
        private const string OwnClipTenant = "goon-clips";
        // THE PEER POOL (2026-09-24, ForPeer): the opponent's niches, fetched by THIS host for
        // an opponent who throws without sending bytes. Own tenants, so its channel list never
        // steers the player's own deck and a reset of one never resets the other.
        private const string PeerStillTenant = "goon-peer-stills";
        private const string PeerClipTenant = "goon-peer-clips";

        // The coordinator registry keeps the FIRST channel provider forever, so the provider
        // reads a static, not an instance: a second window (relaunch) must steer the same tenant.
        private static volatile IReadOnlyList<string> _ownChannels = Array.Empty<string>();
        private static volatile IReadOnlyList<string> _peerChannels = Array.Empty<string>();
        private readonly bool _peer;
        // A NOISE board (ForNoise): its own tenant per set id and ONE fixed channel, so the
        // registry's first-provider-wins rule is exactly right for it. Stills only.
        private readonly string? _noiseSet;
        private readonly IReadOnlyList<string>? _fixedChannels;
        private readonly int _stillTarget = GoonOnlineMediaRules.StillTarget;
        private readonly int _clipTarget = GoonOnlineMediaRules.ClipTarget;
        // ANOTHER GAME'S OWN DECK (ForGame, 2026-09-28): same fetch, validation and temp-file
        // ownership, on "<prefix>-stills" / "<prefix>-clips". Its channel list lives in a static
        // map keyed by prefix, for the same first-provider-wins reason as _ownChannels.
        private readonly string? _prefix;
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, IReadOnlyList<string>> _namedChannels
            = new(StringComparer.Ordinal);
        private string StillTenant => _prefix != null ? _prefix + "-stills" : _noiseSet != null ? "goon-noise-" + _noiseSet : _peer ? PeerStillTenant : OwnStillTenant;
        private string ClipTenant => _prefix != null ? _prefix + "-clips" : _noiseSet != null ? "goon-noise-clips-" + _noiseSet : _peer ? PeerClipTenant : OwnClipTenant;
        private IReadOnlyList<string> _channels
        {
            get => _fixedChannels
                ?? (_prefix != null
                    ? (_namedChannels.TryGetValue(_prefix, out var named) ? named : Array.Empty<string>())
                    : (_peer ? _peerChannels : _ownChannels));
            set
            {
                if (_fixedChannels != null) return;
                if (_prefix != null) _namedChannels[_prefix] = value;
                else if (_peer) _peerChannels = value;
                else _ownChannels = value;
            }
        }
        private IReadOnlyList<string> Channels() => _channels;

        /// <summary>A pool for the OPPONENT'S niches (their hello's <c>caps.niches</c>): the same
        /// fetch, validation and temp-file ownership as the player's own pool, on its own
        /// tenants. Nothing from the opponent but the niche names ever reaches it.</summary>
        public static GoonOnlineMedia ForPeer(Action<Snapshot> onSnapshot) => new(onSnapshot, peer: true);

        private GoonOnlineMedia(Action<Snapshot> onSnapshot, bool peer) : this(onSnapshot) => _peer = peer;

        /// <summary>Another game's own deck (Piece by Piece: "pbp"): the same stills + clips
        /// waves, refills and temp-file ownership as the Goon Game's, on its own tenants, so the
        /// two games never steer or reset each other's channels.</summary>
        public static GoonOnlineMedia ForGame(string tenantPrefix, Action<Snapshot> onSnapshot)
            => new(onSnapshot, tenantPrefix);

        private GoonOnlineMedia(Action<Snapshot> onSnapshot, string tenantPrefix) : this(onSnapshot)
            => _prefix = string.IsNullOrWhiteSpace(tenantPrefix) ? "game" : tenantPrefix.Trim();

        /// <summary>A Sort duel's NOISE board: one known board (<see cref="GoonNoiseSets"/>),
        /// <see cref="GoonNoiseSets.Stills"/> stills and no clips, on its own tenant. The same
        /// validation, materialisation and temp-file ownership as every other pool.</summary>
        public static GoonOnlineMedia? ForNoise(string setId, Action<Snapshot> onSnapshot)
        {
            var sub = GoonNoiseSets.SubFor(setId);
            return sub == null ? null : new GoonOnlineMedia(onSnapshot, setId, sub);
        }

        private GoonOnlineMedia(Action<Snapshot> onSnapshot, string setId, string sub) : this(onSnapshot)
        {
            _noiseSet = setId;
            _fixedChannels = new[] { sub };
            _stillTarget = GoonNoiseSets.Stills;
            _clipTarget = 0;
        }

        private readonly object _gate = new();
        private readonly Action<Snapshot> _onSnapshot;
        private CancellationTokenSource? _cts;
        private int _gen;
        private bool _disposed;
        private List<string> _subs = new();
        private readonly List<Item> _images = new();
        private readonly List<Item> _videos = new();
        private bool _running;
        private bool _failed;
        // Per wave: how many fresh pictures this wave has landed, per kind. A refill wave
        // (More) adds another StillTarget / ClipTarget on top of what the deck holds.
        private int _stillAdded;
        private int _clipAdded;
        // Every post id this niche list has already shown the deck, so a refill never
        // brings back a picture it already had. Reset by Start (a new list is a new deck).
        private readonly HashSet<string> _seenIds = new(StringComparer.Ordinal);

        public GoonOnlineMedia(Action<Snapshot> onSnapshot) => _onSnapshot = onSnapshot;

        /// <summary>Post the 'off' state and drop anything held.</summary>
        public void Off()
        {
            List<Item> drop;
            lock (_gate)
            {
                _gen++;
                CancelLocked();
                drop = TakeFilesLocked();
                _subs = new List<string>();
                _running = false;
                _failed = false;
            }
            Release(drop);
            _onSnapshot(new Snapshot("off", Array.Empty<string>(), Array.Empty<Item>(), Array.Empty<Item>(), 0, 0));
        }

        /// <summary>Start (or restart) a wave for these niches. Same list while a wave holds
        /// pictures = the current snapshot again, no refetch.</summary>
        public void Start(IReadOnlyList<string> subs)
        {
            var clean = GoonOnlineMediaRules.CleanSubs(subs);
            List<Item>? drop = null;
            Snapshot? same = null;
            int gen = 0;
            CancellationToken ct = default;
            lock (_gate)
            {
                if (_disposed) return;
                if (clean.SequenceEqual(_subs, StringComparer.OrdinalIgnoreCase)
                    && (_running || _images.Count + _videos.Count > 0))
                {
                    same = SnapshotLocked();
                }
                else
                {
                    gen = ++_gen;
                    CancelLocked();
                    drop = TakeFilesLocked();
                    _subs = clean;
                    _failed = false;
                    _running = clean.Count > 0;
                    _stillAdded = 0;
                    _clipAdded = 0;
                    _seenIds.Clear();
                    _cts = new CancellationTokenSource();
                    ct = _cts.Token;
                }
            }
            if (same != null) { Raise(same); return; }
            Release(drop!);
            _channels = clean;
            try
            {
                FypOnlineCoordinator.For(StillTenant, Channels, FeedMediaKind.GifStill).ResetChannels();
                FypOnlineCoordinator.For(ClipTenant, Channels, FeedMediaKind.GifClip).ResetChannels();
            }
            catch (Exception ex) { Log.Debug("GoonOnlineMedia: reset channels: {E}", ex.Message); }

            Emit(gen);
            if (clean.Count == 0) return;
            RunWave(gen, ct);
        }

        /// <summary>The page's deck is running low (page -> host <c>media-more</c>): fetch the
        /// next wave for the SAME niches off the feed's own cursor, never a post this list has
        /// already had. No-op while a wave runs, with no niches, or after Off / Dispose. Once
        /// the deck holds <see cref="GoonOnlineMediaRules.MaxWaves"/> waves, each fresh picture
        /// retires the oldest one, so the files held stay bounded.</summary>
        public bool More()
        {
            int gen;
            CancellationToken ct;
            lock (_gate)
            {
                if (_disposed || _running || _subs.Count == 0 || _cts == null) return false;
                gen = _gen;
                ct = _cts.Token;
                _running = true;
                _failed = false;
                _stillAdded = 0;
                _clipAdded = 0;
            }
            Emit(gen);
            RunWave(gen, ct);
            return true;
        }

        private void RunWave(int gen, CancellationToken ct)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    var stills = FillAsync(gen, StillTenant, FeedMediaKind.GifStill, FeedMediaKind.Image,
                        _stillTarget, isImage: true, ct);
                    var clips = _clipTarget > 0
                        ? FillAsync(gen, ClipTenant, FeedMediaKind.GifClip, FeedMediaKind.GifClip,
                            _clipTarget, isImage: false, ct)
                        : Task.CompletedTask;
                    await Task.WhenAll(stills, clips).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { }
                catch (Exception ex) { Log.Warning("GoonOnlineMedia: wave threw: {E}", ex.Message); }
                finally
                {
                    bool mine;
                    lock (_gate) { mine = gen == _gen; if (mine) _running = false; }
                    if (mine) Emit(gen);
                }
            });
        }

        private async Task FillAsync(int gen, string tenant, FeedMediaKind fetchKind, FeedMediaKind checkKind,
            int target, bool isImage, CancellationToken ct)
        {
            var coord = FypOnlineCoordinator.For(tenant, Channels, fetchKind);
            using var slots = new SemaphoreSlim(GoonOnlineMediaRules.Concurrency);
            int dry = 0;
            while (!ct.IsCancellationRequested && Added(isImage) < target && dry < GoonOnlineMediaRules.MaxDryBatches)
            {
                var (entries, error) = await coord.FetchBatchAsync(fetchKind, ct).ConfigureAwait(false);
                if (error != null)
                {
                    lock (_gate) if (gen == _gen) _failed = true;
                    dry++;
                    await Task.Delay(1500, ct).ConfigureAwait(false);
                    continue;
                }
                List<FypAssetManifest.Entry> usable;
                lock (_gate)
                {
                    usable = entries
                        .Where(e => RemoteMediaFormats.Validate(e, checkKind, out _) && e.Id != null && _seenIds.Add(e.Id))
                        .Take(Math.Max(0, target - (isImage ? _stillAdded : _clipAdded)))
                        .ToList();
                }
                if (usable.Count == 0) { dry++; continue; }
                int before = Added(isImage);
                var jobs = usable.Select(async e =>
                {
                    await slots.WaitAsync(ct).ConfigureAwait(false);
                    try { await MaterialiseOneAsync(gen, e, isImage, target, ct).ConfigureAwait(false); }
                    finally { slots.Release(); }
                });
                await Task.WhenAll(jobs).ConfigureAwait(false);
                dry = Added(isImage) > before ? 0 : dry + 1;
            }
        }

        private async Task MaterialiseOneAsync(int gen, FypAssetManifest.Entry e, bool isImage, int target,
            CancellationToken ct)
        {
            if (ct.IsCancellationRequested || Added(isImage) >= target) return;
            // ownerReleases: the pool holds these for the whole game and releases every one on a
            // niche change or close, so the app-wide 50-file sweep must not take them mid-match.
            var path = await GoonRemoteFiles.MaterializeAsync(e.Url, ct).ConfigureAwait(false);
            if (path == null) return;
            var url = GoonOnlineMediaRules.UrlFor(path, CorePaths.EffectiveAssets);
            if (url == null)
            {
                GoonRemoteFiles.Release(path);
                return;
            }
            bool kept = false;
            Item? retired = null;
            lock (_gate)
            {
                if (gen == _gen && !_disposed && !ct.IsCancellationRequested
                    && (isImage ? _stillAdded : _clipAdded) < target)
                {
                    var list = isImage ? _images : _videos;
                    // Past the cap the oldest picture makes room, and only once a fresh one has
                    // actually landed: an exhausted feed never shrinks the deck.
                    if (list.Count >= GoonOnlineMediaRules.Cap(target))
                    {
                        retired = list[0];
                        list.RemoveAt(0);
                    }
                    list.Add(new Item(NameFor(e, isImage), url, path));
                    if (isImage) _stillAdded++; else _clipAdded++;
                    kept = true;
                }
            }
            if (!kept) { GoonRemoteFiles.Release(path); return; }
            if (retired != null) ReleaseLater(retired);
            Emit(gen);
        }

        /// <summary>"online:&lt;sub&gt;/&lt;post&gt;" - identity only, never a path.</summary>
        private static string NameFor(FypAssetManifest.Entry e, bool isImage)
        {
            var id = e.Id ?? "";
            if (id.StartsWith("scrolller/", StringComparison.Ordinal)) id = id.Substring("scrolller/".Length);
            return (isImage ? "online:" : "online-clip:") + id;
        }

        private int Added(bool isImage)
        {
            lock (_gate) return isImage ? _stillAdded : _clipAdded;
        }

        /// <summary>A retired picture may still be on screen (the page got the new list a
        /// moment ago), so its file goes a little later, not under a playing element.</summary>
        private static void ReleaseLater(Item item)
        {
            _ = Task.Run(async () =>
            {
                try { await Task.Delay(TimeSpan.FromSeconds(30)).ConfigureAwait(false); } catch { }
                Release(new[] { item });
            });
        }

        private void Emit(int gen)
        {
            Snapshot snap;
            lock (_gate)
            {
                if (gen != _gen || _disposed) return;
                snap = SnapshotLocked();
            }
            Raise(snap);
        }

        private void Raise(Snapshot s)
        {
            try { _onSnapshot(s); }
            catch (Exception ex) { Log.Debug("GoonOnlineMedia: snapshot sink threw: {E}", ex.Message); }
        }

        private Snapshot SnapshotLocked()
        {
            int have = _images.Count + _videos.Count;
            int want = _subs.Count == 0 ? 0
                : _running ? have + Math.Max(0, _stillTarget - _stillAdded)
                                 + Math.Max(0, _clipTarget - _clipAdded)
                           : Math.Max(have, _stillTarget + _clipTarget);
            var state = GoonOnlineMediaRules.StateFor(true, _subs.Count, have, _running, _failed);
            return new Snapshot(state, _subs.ToList(), _images.ToList(), _videos.ToList(), have, want);
        }

        private void CancelLocked()
        {
            // Cancel only: a worker may still be registering on the token, and a disposed
            // source throws there. The GC takes the source.
            try { _cts?.Cancel(); } catch { }
            _cts = null;
        }

        private List<Item> TakeFilesLocked()
        {
            var all = _images.Concat(_videos).ToList();
            _images.Clear();
            _videos.Clear();
            return all;
        }

        private static void Release(IEnumerable<Item> items)
        {
            foreach (var i in items)
            {
                try { GoonRemoteFiles.Release(i.Path); } catch { }
            }
        }

        public void Dispose()
        {
            List<Item> drop;
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;
                _gen++;
                CancelLocked();
                drop = TakeFilesLocked();
            }
            Release(drop);
        }
    }
}
