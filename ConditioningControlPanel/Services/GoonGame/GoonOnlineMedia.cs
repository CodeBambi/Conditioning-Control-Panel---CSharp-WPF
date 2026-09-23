using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.Fyp;
using ConditioningControlPanel.Services.Fyp.Online;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.GoonGame
{
    /// <summary>
    /// The pure half of the Goon Game's online pictures: what a niche name may be, how many a
    /// pick may carry, what a stored custom blob may look like, which state the page is told
    /// and how a materialised temp file becomes a page url. No I/O, no App statics, so every
    /// rule here is a unit test.
    /// </summary>
    internal static class GoonOnlineMediaRules
    {
        /// <summary>Same grammar as the page's <c>cleanNiche</c> (and Breakout's picker).</summary>
        private static readonly Regex NicheRx = new("^[a-zA-Z0-9_]{2,40}$", RegexOptions.CultureInvariant);

        public const int MaxSubs = 8;

        /// <summary>About what one fetch wave aims to put in the deck. Small enough that the
        /// first pictures land in seconds, big enough that a match does not repeat at once.</summary>
        public const int StillTarget = 24;
        public const int ClipTarget = 12;

        /// <summary>Downloads in flight at once, per kind.</summary>
        public const int Concurrency = 3;

        /// <summary>Batches in a row that add nothing before a kind gives up for this wave.</summary>
        public const int MaxDryBatches = 3;

        /// <summary>The page's custom blob is opaque to us; this only stops a runaway write.</summary>
        public const int MaxCustomChars = 16 * 1024;

        public static readonly IReadOnlyList<string> Flavours =
            new[] { "trance", "pink", "frills", "shiny", "censored", "mine" };

        public static bool IsNiche(string? s) => s != null && NicheRx.IsMatch(s);

        /// <summary>Valid names only, first spelling wins on a case-insensitive repeat, capped.</summary>
        public static List<string> CleanSubs(IEnumerable<string?>? raw)
        {
            var list = new List<string>();
            if (raw == null) return list;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in raw)
            {
                var s = r?.Trim();
                if (s != null && s.StartsWith("r/", StringComparison.OrdinalIgnoreCase)) s = s.Substring(2);
                if (!IsNiche(s) || !seen.Add(s!)) continue;
                list.Add(s!);
                if (list.Count >= MaxSubs) break;
            }
            return list;
        }

        /// <summary>A known flavour id, or "" (never picked). Anything else collapses to "".</summary>
        public static string CleanFlavour(string? raw)
        {
            var s = (raw ?? "").Trim().ToLowerInvariant();
            return Flavours.Contains(s) ? s : "";
        }

        /// <summary>The custom blob as a JSON object string, or "" when it is missing, not an
        /// object, or too big. Re-serialised compactly so the stored form never carries junk
        /// whitespace from the page.</summary>
        public static string CleanCustom(JToken? raw)
        {
            if (raw is not JObject o) return "";
            var s = o.ToString(Newtonsoft.Json.Formatting.None);
            return s.Length <= MaxCustomChars ? s : "";
        }

        /// <summary>The stored custom blob back to an object for init; {} on anything odd.</summary>
        public static JObject ParseCustom(string? stored)
        {
            if (string.IsNullOrWhiteSpace(stored)) return new JObject();
            try { return JToken.Parse(stored) as JObject ?? new JObject(); }
            catch { return new JObject(); }
        }

        public static string JoinSubs(IEnumerable<string> subs) => string.Join(",", subs);

        public static List<string> SplitSubs(string? stored)
            => CleanSubs((stored ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries));

        /// <summary>Should anything be fetched at all.</summary>
        public static bool ShouldFetch(bool online, string flavour, IReadOnlyCollection<string> subs)
            => online && !string.IsNullOrEmpty(flavour) && subs.Count > 0;

        /// <summary>The state the page is told. <paramref name="running"/> = a wave is still
        /// fetching; <paramref name="failed"/> = the feed itself was unreachable.</summary>
        public static string StateFor(bool online, int subs, int have, bool running, bool failed)
        {
            if (!online) return "off";
            if (subs == 0) return "empty";
            if (have > 0) return running ? "loading" : "ready";
            if (running) return "loading";
            return failed ? "error" : "empty";
        }

        /// <summary><c>https://ccp.assets/...</c> for a file under the assets root, or null when
        /// the file is not under it (the system temp fallback is not page-reachable).</summary>
        public static string? UrlFor(string? path, string? assetsRoot)
        {
            if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(assetsRoot)) return null;
            string full, root;
            try
            {
                full = Path.GetFullPath(path);
                root = Path.GetFullPath(assetsRoot).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
            }
            catch { return null; }
            if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return null;
            var rel = full.Substring(root.Length).Replace('\\', '/');
            var parts = rel.Split('/').Select(Uri.EscapeDataString);
            return "https://ccp.assets/" + string.Join("/", parts);
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

        private const string StillTenant = "goon-stills";
        private const string ClipTenant = "goon-clips";

        // The coordinator registry keeps the FIRST channel provider forever, so the provider
        // reads a static, not an instance: a second window (relaunch) must steer the same tenant.
        private static volatile IReadOnlyList<string> _channels = Array.Empty<string>();
        private static IReadOnlyList<string> Channels() => _channels;

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
            catch (Exception ex) { App.Logger?.Debug("GoonOnlineMedia: reset channels: {E}", ex.Message); }

            Emit(gen);
            if (clean.Count == 0) return;

            _ = Task.Run(async () =>
            {
                try
                {
                    var stills = FillAsync(gen, StillTenant, FeedMediaKind.GifStill, FeedMediaKind.Image,
                        GoonOnlineMediaRules.StillTarget, isImage: true, ct);
                    var clips = FillAsync(gen, ClipTenant, FeedMediaKind.GifClip, FeedMediaKind.GifClip,
                        GoonOnlineMediaRules.ClipTarget, isImage: false, ct);
                    await Task.WhenAll(stills, clips).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { }
                catch (Exception ex) { App.Logger?.Warning("GoonOnlineMedia: wave threw: {E}", ex.Message); }
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
            var seen = new HashSet<string>(StringComparer.Ordinal);
            while (!ct.IsCancellationRequested && Count(isImage) < target && dry < GoonOnlineMediaRules.MaxDryBatches)
            {
                var (entries, error) = await coord.FetchBatchAsync(fetchKind, ct).ConfigureAwait(false);
                if (error != null)
                {
                    lock (_gate) if (gen == _gen) _failed = true;
                    dry++;
                    await Task.Delay(1500, ct).ConfigureAwait(false);
                    continue;
                }
                var usable = entries
                    .Where(e => RemoteMediaFormats.Validate(e, checkKind, out _) && seen.Add(e.Id))
                    .Take(Math.Max(0, target - Count(isImage)))
                    .ToList();
                if (usable.Count == 0) { dry++; continue; }
                int before = Count(isImage);
                var jobs = usable.Select(async e =>
                {
                    await slots.WaitAsync(ct).ConfigureAwait(false);
                    try { await MaterialiseOneAsync(gen, e, isImage, target, ct).ConfigureAwait(false); }
                    finally { slots.Release(); }
                });
                await Task.WhenAll(jobs).ConfigureAwait(false);
                dry = Count(isImage) > before ? 0 : dry + 1;
            }
        }

        private async Task MaterialiseOneAsync(int gen, FypAssetManifest.Entry e, bool isImage, int target,
            CancellationToken ct)
        {
            if (ct.IsCancellationRequested || Count(isImage) >= target) return;
            var path = await RemoteMediaCache.MaterializeAsync(e.Url, ct).ConfigureAwait(false);
            if (path == null) return;
            var url = GoonOnlineMediaRules.UrlFor(path, App.EffectiveAssetsPath);
            if (url == null)
            {
                RemoteMediaCache.ReleaseTempFile(path);
                return;
            }
            bool kept = false;
            lock (_gate)
            {
                if (gen == _gen && !_disposed && !ct.IsCancellationRequested)
                {
                    var list = isImage ? _images : _videos;
                    if (list.Count < target)
                    {
                        list.Add(new Item(NameFor(e, isImage), url, path));
                        kept = true;
                    }
                }
            }
            if (!kept) { RemoteMediaCache.ReleaseTempFile(path); return; }
            Emit(gen);
        }

        /// <summary>"online:&lt;sub&gt;/&lt;post&gt;" - identity only, never a path.</summary>
        private static string NameFor(FypAssetManifest.Entry e, bool isImage)
        {
            var id = e.Id ?? "";
            if (id.StartsWith("scrolller/", StringComparison.Ordinal)) id = id.Substring("scrolller/".Length);
            return (isImage ? "online:" : "online-clip:") + id;
        }

        private int Count(bool isImage)
        {
            lock (_gate) return isImage ? _images.Count : _videos.Count;
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
            catch (Exception ex) { App.Logger?.Debug("GoonOnlineMedia: snapshot sink threw: {E}", ex.Message); }
        }

        private Snapshot SnapshotLocked()
        {
            int have = _images.Count + _videos.Count;
            int want = _subs.Count == 0 ? 0 : GoonOnlineMediaRules.StillTarget + GoonOnlineMediaRules.ClipTarget;
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
                try { RemoteMediaCache.ReleaseTempFile(i.Path); } catch { }
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
