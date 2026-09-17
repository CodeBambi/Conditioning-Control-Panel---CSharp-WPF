using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.Fyp;
using ConditioningControlPanel.Services.Fyp.Online;
using Serilog;

namespace ConditioningControlPanel.Services.BackRoom;

/// <summary>One warm remote still. <see cref="Url"/> is a <c>ccp.assets</c> url over the file the
/// bytes were written to, so it is the same kind of thing a local pick is and the page cannot tell
/// them apart. The temp path itself stays inside <see cref="BackRoomRemotePool"/> (PII rule).</summary>
/// <param name="Id">The provider's entry id. The dedupe key: one id is materialized ONCE.</param>
internal readonly record struct BackRoomRemoteStill(string Id, string Url, int W, int H);

/// <summary>One warm remote CLIP: a materialized webm/mp4 on a <c>ccp.assets</c> url, which the room's
/// page plays rather than decodes (<c>room\clip-source.js</c>, routed from <c>room\gif.js</c> on the
/// extension). Same shape as a still on purpose, because everything downstream of the deal treats it
/// as one dealt picture; what makes it a clip is the extension in <see cref="Url"/> and nothing else.
/// Only the WALL is ever dealt one - see <see cref="BackRoomMedia.IsWall"/> for why.</summary>
/// <param name="Id">The provider's entry id, same dedupe key, in its own set.</param>
internal readonly record struct BackRoomRemoteClip(string Id, string Url, int W, int H);

/// <summary>The warm remote pool as the deal sees it. A seam so the suite can drive every source
/// without a network, and so the deal can ask "what is ready" without knowing what a fetch is.</summary>
internal interface IBackRoomRemotePool
{
    /// <summary>True when the room's effective source actually wants remote pictures. Everything
    /// else on this interface is a no-op when it is false.</summary>
    bool Wanted { get; }

    /// <summary>Start filling in the background and return at once. Cheap enough to call on every
    /// room open and every deal.</summary>
    void EnsureWarm();

    /// <summary>Start filling and wait, but only up to <see cref="BackRoomRemotePool.WarmWaitMs"/>.
    /// Returns a completed task whenever every set is already at target, a warm is in flight and
    /// nothing is empty, or the fetch gap has not elapsed. One cap covers BOTH sets, so the clip set
    /// existing cannot make a sit-down wait longer than the stills alone already could.</summary>
    Task WarmAsync(CancellationToken ct);

    /// <summary>The stills whose bytes are on disk RIGHT NOW. Never a network call.</summary>
    IReadOnlyList<BackRoomRemoteStill> Ready();

    /// <summary>The clips whose bytes are on disk RIGHT NOW. Never a network call. Defaulted to none
    /// so a stand-in written before the clip lane keeps compiling and reads as "stills only", which is
    /// the one direction a pool is allowed to degrade in.</summary>
    IReadOnlyList<BackRoomRemoteClip> ReadyClips() => Array.Empty<BackRoomRemoteClip>();

    /// <summary>Room closed: hand every materialized file back.</summary>
    void Drain();
}

/// <summary>
/// THE ROOM'S REMOTE MEDIA (CONTRACT section 5, 10.13.C). Two warm pools of Scrolller content that are
/// already downloaded AND already written to a real file, so a sit-down is a memory read and a
/// directory listing, never a GraphQL call: <c>ScrolllerSource</c> is throttled to one request per
/// 1.1 s process-wide, and a player dropping into a chair cannot be made to wait behind that gate.
///
/// <para>THE UNLOCK is where the bytes land. <c>RemoteMediaCache.MaterializeAsync</c> writes them
/// under <c>App.GetMediaTempPath()</c>, which is <c>{App.EffectiveAssetsPath}\.temp</c>, and the host
/// maps <c>ccp.assets</c> to <c>App.EffectiveAssetsPath</c>. So a materialized file already has a
/// legal <c>https://ccp.assets/.temp/&lt;file&gt;</c> url: CORS-clean, WebGL-safe, it passes the page's
/// own <c>allowed()</c> checks, and it still resolves back to a real file for the host's fullscreen
/// <c>gif_from</c> / <c>wash</c> effects. No proxy, no new host, no new mapping. It also keeps the
/// source's extension (<c>MaterializeAsync</c> takes it from the url, because consumers sniff the
/// codec off it), which is precisely what lets the page route a <c>.mp4</c> to the clip player.</para>
///
/// <para>THE DEDUPE TRAP, which has bitten this codebase before with content-pack decrypts: every
/// <c>MaterializeAsync</c> call mints a NEW guid filename, so two materializes of one picture are two
/// urls and the page's url-keyed dedupe (<c>room\screens.js</c>, <c>stations\slot\media.js</c>) cannot
/// collapse them - the same picture would appear twice on the wall. That is why each set is keyed on
/// the PROVIDER'S ENTRY ID and materializes each id exactly once, keeping the path for the life of the
/// pool.</para>
///
/// <para>STILLS AND CLIPS ARE TWO SETS, FETCHED TWICE (2026-09-17). The provider's "GIF" filter means
/// "animated content" and delivers webm/mp4 renditions with STATIC webp/jpg posters; the posters are
/// plain VP8 with no ANIM chunk, byte-verified in <c>RemoteMediaFormats</c>. So a still set really is
/// static, and the ANIMATED half is the clip itself, asked for separately with
/// <see cref="FeedMediaKind.Video"/> (the coordinator's explicit-kind overload exists for exactly this:
/// "a surface showing both stills and clips asks twice, not once with Any"). The owner's call was to
/// play the clip rather than transcode it: WebView2 is Chromium and decodes VP9 and H.264 natively, so
/// the desktop needs no ffmpeg hop and no installer bytes - one representative clip at the 384 px rung
/// costs 339 KB played, against 225-400 KB plus 10-20 MB of installer for a WebP transcode and 2.96 MB
/// for a bundled animated GIF. The page half is <c>room\clip-source.js</c>, which hands back the exact
/// shape <c>room\gif-decode.js</c> does, so nothing below <c>room\gif.js</c> learned a new kind of
/// picture. Clips go ONLY to the wall (<see cref="BackRoomMedia.IsWall"/>); the stations keep stills.</para>
///
/// <para>The bright line (<c>IFeedSource.cs</c>): the fetch happens on the user's device, direct from
/// the provider. Nothing here routes through CC Labs infrastructure.</para>
/// </summary>
internal sealed class BackRoomRemotePool : IBackRoomRemotePool
{
    /// <summary>The room's own tenant in the coordinator registry. Its own rotation state and dwell
    /// store, so the casino and the flashes cannot fight over one set of channel iterators. ONE tenant
    /// for both kinds: the rotation and the dwell weights describe the room's channels, not its
    /// containers, and a second id would deal the same subreddit twice as often.</summary>
    internal const string ConsumerId = "backroom";

    /// <summary>Fill to here. Two full wall deals (the wall asks for 8) plus a 13-card deck's worth of
    /// re-deal, which is as much as one sit-down can consume.</summary>
    internal const int ReadyTarget = 16;

    /// <summary>Hard ceiling. Deliberately far under <c>RemoteMediaCache</c>'s 50-file temp tracker,
    /// which deletes OLDEST-FIRST once it is over: a bigger pool and the room's later materializes
    /// would sweep the room's own earlier wall pictures out from under live urls.</summary>
    internal const int ReadyMax = 24;

    /// <summary>Clips fill to here: exactly ONE FULL WALL. <c>room\screens.js</c> caps the wall at
    /// <c>MAX_PICTURES = 8</c> and <c>room\main.js</c> asks for <c>count: 8</c>, so eight is the most
    /// that can be on screen at once and a ninth warm clip buys nothing that a re-deal cannot get by
    /// reshuffling the same eight. Sized for the wall, NOT copied from the stills' 16: the stills also
    /// feed the card table's 13-per-sit-down, and clips never go near a station.</summary>
    internal const int ClipReadyTarget = 8;

    /// <summary>Hard ceiling, only two over the target rather than the stills' half-again, because a
    /// clip costs more than a still in both places that matter. On DISK it competes for
    /// <c>RemoteMediaCache.MaxTrackedTempFiles = 50</c>, which is shared with the flash pool and the
    /// For You feed and deletes oldest-first once over: 24 stills + 10 clips = 34 leaves those two room
    /// to materialize without the room sweeping its own live wall urls out from under the page (the
    /// failure <see cref="Ready"/>'s existence check exists to survive). At PLAY TIME it costs a video
    /// decoder, not a texture upload, so slack past a full wall is for rotating faces on a re-deal and
    /// nothing else.</summary>
    internal const int ClipReadyMax = 10;

    /// <summary>Minimum gap between batch fetches, per set. The source is already throttled ~1 req/1.1 s
    /// process-wide; this stops a room that re-deals often from queueing behind that gate faster than
    /// it drains.</summary>
    internal const int WarmGapSeconds = 8;

    /// <summary>The MOST a deal will ever wait on a warm, across every set, and only when one is empty.
    /// The page gives the host 6000 ms for a <c>media</c> reply (<c>room\main.js</c>), so this leaves
    /// room to spare; the batches carry on filling in the background past the cap, the WAIT is what
    /// stops. A cold clip batch is the second GraphQL call of the pair and can easily land after the
    /// cap, which is not a bug: the wall shows stills for one deal and clips on the next.</summary>
    internal const int WarmWaitMs = 2000;

    /// <summary>The app's live pool. A property rather than a field so nothing forces this type's
    /// static init while <c>BackRoomHostService</c> is still building its own statics.</summary>
    private static BackRoomRemotePool? _shared;
    internal static BackRoomRemotePool Shared => _shared ??= new BackRoomRemotePool();

    private readonly Func<Models.AppSettings?> _settings;
    private readonly Func<string, CancellationToken, Task<string?>> _materialize;
    private readonly Action<string> _release;
    private readonly Func<string?> _assetsRoot;
    private readonly Func<ILogger?> _log;

    /// <summary>What a warm set holds per entry. The path is in here rather than in the deal's view of
    /// it, which is how "no path ever leaves this class" is kept while still being able to re-check
    /// that the file exists and to hand it back on <see cref="Drain"/>.</summary>
    private readonly record struct Warm(string Id, string Url, int W, int H, string Path);

    /// <summary>
    /// ONE WARM SET. Everything that makes stills and clips the same machine - the entry-id dedupe, the
    /// materialize, the existence re-check, the counts-only log - lives once, in
    /// <see cref="BackRoomRemotePool.WarmBatchAsync"/>; the three things that differ are fields here:
    /// the media kind the entries are validated against, the two sizes, and the batch fetch.
    /// Its own lock and its own in-flight task, so a clip batch grinding through 339 KB downloads never
    /// holds up the stills a station is asking for.
    /// </summary>
    private sealed class WarmSet
    {
        internal WarmSet(FeedMediaKind kind, int target, int max, string noun,
            Func<CancellationToken, Task<(List<FypAssetManifest.Entry> Entries, string? Error)>> fetch)
        {
            Kind = kind; Target = target; Max = max; Noun = noun; Fetch = fetch;
        }

        /// <summary>The kind <c>RemoteMediaFormats.Validate</c> is asked for. The single authority on
        /// what a remote entry may be: <c>Video</c> is <c>.mp4</c>/<c>.webm</c> and nothing else,
        /// <c>Image</c> is the static set, so a clip cannot reach a reel texture and a poster cannot
        /// reach the clip player.</summary>
        internal readonly FeedMediaKind Kind;
        internal readonly int Target;
        internal readonly int Max;
        /// <summary>What one entry is called in the log line. A constant word, so it carries nothing.</summary>
        internal readonly string Noun;
        internal readonly Func<CancellationToken, Task<(List<FypAssetManifest.Entry> Entries, string? Error)>> Fetch;

        internal readonly object Gate = new();
        /// <summary>Entry id -> what the deal gets, plus the file behind it. THE dedupe key.</summary>
        internal readonly Dictionary<string, Warm> ById = new(StringComparer.Ordinal);
        /// <summary>Insertion order, so the oldest entry is the one a full set refuses to grow past.</summary>
        internal readonly List<string> Order = new();
        internal Task? Warming;
        internal DateTime LastWarmUtc = DateTime.MinValue;
    }

    private readonly WarmSet _stills;
    private readonly WarmSet _clips;

    /// <summary>The app's live sources.</summary>
    internal BackRoomRemotePool()
        : this(() => App.Settings?.Current, null, null, null, null, null)
    {
    }

    /// <summary>Seams for tests: the settings, the batch fetches, the materialize and its release, the
    /// assets root and the log. A null seam wires to the real stack, with ONE deliberate exception
    /// spelled out on <paramref name="clipFetch"/>.</summary>
    /// <param name="fetch">The stills batch.</param>
    /// <param name="clipFetch">The clips batch. When it is null AND <paramref name="fetch"/> was
    /// seamed, this set fetches NOTHING rather than reaching for the real coordinator: a test that
    /// seams the stills fetch and says nothing about clips means "stills only", and the alternative is
    /// a live GraphQL call from the suite. A pool with no seams at all still wires both to the app.</param>
    internal BackRoomRemotePool(
        Func<Models.AppSettings?>? settings,
        Func<CancellationToken, Task<(List<FypAssetManifest.Entry> Entries, string? Error)>>? fetch,
        Func<string, CancellationToken, Task<string?>>? materialize,
        Action<string>? release,
        Func<string?>? assetsRoot,
        Func<ILogger?>? log,
        Func<CancellationToken, Task<(List<FypAssetManifest.Entry> Entries, string? Error)>>? clipFetch = null)
    {
        _settings = settings ?? (() => App.Settings?.Current);
        _materialize = materialize ?? RemoteMediaCache.MaterializeAsync;
        _release = release ?? RemoteMediaCache.ReleaseTempFile;
        _assetsRoot = assetsRoot ?? (() => App.EffectiveAssetsPath);
        _log = log ?? (() => App.Logger);

        // Image, not GifStill: the fetch asks the provider's GIF filter (which is where the posters
        // are), the VALIDATE asks what the bytes have to be, and those are two different questions.
        _stills = new WarmSet(FeedMediaKind.Image, ReadyTarget, ReadyMax, "still",
            fetch ?? DefaultStillFetchAsync);
        _clips = new WarmSet(FeedMediaKind.Video, ClipReadyTarget, ClipReadyMax, "clip",
            clipFetch ?? (fetch == null ? DefaultClipFetchAsync : NoBatchAsync));
    }

    /// <summary>
    /// THE ROOM'S CHANNELS. Its own niche list minus the ones switched off, capped at
    /// <c>BackRoomMediaSubCap</c>. EMPTY means follow the app's own picker, which is the default and
    /// the point of it: a player who never opens the room's picker gets the niches they already chose
    /// in Assets rather than nothing. Names are sanitized through the coordinator's own sanitizer, so
    /// an "r/Foo" a user pasted resolves the same way everywhere.
    /// </summary>
    internal static IReadOnlyList<string> RoomChannels(Models.AppSettings? s)
    {
        var off = new HashSet<string>(s?.BackRoomMediaSubsOff ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
        var own = (s?.BackRoomMediaSubs ?? new List<string>())
            .Select(FypOnlineCoordinator.SanitizeSub)
            .Where(n => n != null && !off.Contains(n!))
            .Select(n => n!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(Models.AppSettings.BackRoomMediaSubCap)
            .ToList();
        if (own.Count > 0) return own;
        // The app-wide selection is NOT capped here: it is the same list the flashes and the For You
        // feed draw on, and the coordinator caps channels itself.
        return FypOnlineCoordinator.ResolveChannels(s?.FypOnlineNiches, s?.FypOnlineCustomSubs);
    }

    /// <summary>The room's coordinator. One tenant, asked twice for two kinds; the kind it REGISTERS
    /// with is the stills' one because that is what the first caller has always been.</summary>
    private FypOnlineCoordinator Coordinator()
        => FypOnlineCoordinator.For(ConsumerId, () => RoomChannels(_settings()), FeedMediaKind.GifStill);

    /// <summary>GifStill, not Image and not Any: the same kind the flash pool asks for, so this
    /// surface only ever receives renderable stills and a video entry reaching a reel texture (a black
    /// symbol) stays impossible.</summary>
    private Task<(List<FypAssetManifest.Entry> Entries, string? Error)> DefaultStillFetchAsync(CancellationToken ct)
        => Coordinator().FetchBatchAsync(FeedMediaKind.GifStill, ct);

    /// <summary>Video: the webm/mp4 rendition of the same "GIF" posts the stills are posters for. The
    /// explicit-kind overload on the same tenant, which is what it is documented for.</summary>
    private Task<(List<FypAssetManifest.Entry> Entries, string? Error)> DefaultClipFetchAsync(CancellationToken ct)
        => Coordinator().FetchBatchAsync(FeedMediaKind.Video, ct);

    /// <summary>A set with nothing behind it. Reads as "the provider had nothing", which every caller
    /// already degrades through.</summary>
    private static Task<(List<FypAssetManifest.Entry> Entries, string? Error)> NoBatchAsync(CancellationToken ct)
        => Task.FromResult((new List<FypAssetManifest.Entry>(), (string?)null));

    public bool Wanted
    {
        get
        {
            // ONE authority on what the source resolves to (BackRoomHostService.EffectiveMediaSource):
            // it collapses "auto" against the app and withdraws online/mixed when consent is missing.
            try { return BackRoomHostService.EffectiveMediaSource(_settings()) is "online" or "mixed"; }
            catch { return false; }
        }
    }

    public void EnsureWarm()
    {
        // Fire and forget by design, and safe to be: the warm body touches no UI state and swallows
        // everything, so there is no exception to escape into UnobservedTaskException.
        _ = StartWarm(_stills);
        _ = StartWarm(_clips);
    }

    public Task WarmAsync(CancellationToken ct)
    {
        // Stills first, so the kind every surface can use is the one that gets the earlier slot behind
        // the provider's 1.1 s gate; the clip batch follows it and may well land after the wait cap.
        var stills = StartWarm(_stills);
        var clips = StartWarm(_clips);
        if (stills == null && clips == null) return Task.CompletedTask;
        var both = stills == null ? clips! : clips == null ? stills : Task.WhenAll(stills, clips);
        // ONE cap for both: adding the clip set must not be able to make a sit-down wait longer.
        return WaitBounded(both, ct);
    }

    /// <summary>The one gate, per set. Returns the warm in flight, a new one, or null when that set
    /// does not need topping up right now.</summary>
    private Task? StartWarm(WarmSet set)
    {
        if (!Wanted) return null;
        lock (set.Gate)
        {
            if (set.Warming != null) return set.Warming;
            if (set.Order.Count >= set.Target) return null;
            if ((DateTime.UtcNow - set.LastWarmUtc).TotalSeconds < WarmGapSeconds) return null;
            set.LastWarmUtc = DateTime.UtcNow;
            return set.Warming = Task.Run(() => WarmBatchAsync(set));
        }
    }

    /// <summary>Wait on a warm, but never past <see cref="WarmWaitMs"/>. The batch keeps filling
    /// afterwards; only the waiting stops, which is what keeps a sit-down off the network's clock.</summary>
    private static async Task WaitBounded(Task warm, CancellationToken ct)
    {
        using var cap = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var delay = Task.Delay(WarmWaitMs, cap.Token);
        await Task.WhenAny(warm, delay).ConfigureAwait(false);
        cap.Cancel();
        // Observe the loser so a cancelled delay cannot surface as an unobserved fault.
        try { await delay.ConfigureAwait(false); } catch (OperationCanceledException) { }
    }

    /// <summary>One coordinator batch -> entries validated against the set's kind -> bytes in a real
    /// file -> ccp.assets urls in that set. Never throws, never touches the UI, logs counts only.</summary>
    private async Task WarmBatchAsync(WarmSet set)
    {
        int warmed = 0, dropped = 0;
        try
        {
            var (entries, error) = await set.Fetch(CancellationToken.None).ConfigureAwait(false);
            if (error != null)
            {
                // Transport failure. The coordinator is already backing the channel off; all the room
                // has to do is keep what is warm and let the deal degrade (10.13.C). A provider that
                // is down must look like "no remote content", never like a broken room.
                _log()?.Debug("BackRoomRemotePool: warm batch failed ({Error})", error);
                return;
            }

            foreach (var entry in entries ?? new List<FypAssetManifest.Entry>())
            {
                bool full, known;
                lock (set.Gate)
                {
                    full = set.Order.Count >= set.Max;
                    known = entry != null && !string.IsNullOrEmpty(entry.Id) && set.ById.ContainsKey(entry.Id);
                }
                if (full) break;
                if (known) continue;   // the trap: one entry id is materialized exactly once
                if (entry == null || string.IsNullOrEmpty(entry.Id)) { dropped++; continue; }

                if (!RemoteMediaFormats.Validate(entry, set.Kind, out _)) { dropped++; continue; }

                // Download AND write the file now, on this background thread, so the deal later is a
                // pure lookup. A failure here just means this one entry never joins the set.
                var path = await _materialize(entry.Url, CancellationToken.None).ConfigureAwait(false);
                if (string.IsNullOrEmpty(path)) { dropped++; continue; }

                var url = AssetsUrlFor(path!);
                if (url == null)
                {
                    // GetMediaTempPath falls back to the SYSTEM temp when the assets folder is
                    // unusable, and nothing outside the assets root has a ccp.assets url the page
                    // would load. Hand the file straight back rather than hold one the room cannot show.
                    Release(path!);
                    dropped++;
                    continue;
                }

                bool added;
                lock (set.Gate)
                {
                    added = !set.ById.ContainsKey(entry.Id);
                    if (added)
                    {
                        set.ById[entry.Id] = new Warm(entry.Id, url, entry.Width ?? 0, entry.Height ?? 0, path!);
                        set.Order.Add(entry.Id);
                    }
                }
                if (added) warmed++;
                else Release(path!);   // raced with another warm on the same id
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            // Type only: an IO exception message can carry a path.
            _log()?.Debug("BackRoomRemotePool: warm failed ({Type})", ex.GetType().Name);
        }
        finally
        {
            int ready;
            lock (set.Gate) { set.Warming = null; ready = set.Order.Count; }
            // Counts and the set's own noun, never a path and never a url (PII rule). A host, when one
            // is ever worth naming here, goes through Logging.UrlLog.Host and nothing else.
            if (warmed > 0 || dropped > 0)
                _log()?.Information("BackRoomRemotePool: warmed {Warmed} {Kind}(s), dropped {Dropped}, {Ready} ready",
                    warmed, set.Noun, dropped, ready);
        }
    }

    public IReadOnlyList<BackRoomRemoteStill> Ready()
        => ReadyWarm(_stills).Select(w => new BackRoomRemoteStill(w.Id, w.Url, w.W, w.H)).ToList();

    public IReadOnlyList<BackRoomRemoteClip> ReadyClips()
        => ReadyWarm(_clips).Select(w => new BackRoomRemoteClip(w.Id, w.Url, w.W, w.H)).ToList();

    /// <summary>What is on disk in one set, in insertion order, forgetting anything that is not.</summary>
    private List<Warm> ReadyWarm(WarmSet set)
    {
        var live = new List<Warm>();
        List<string>? lost = null;
        lock (set.Gate)
        {
            foreach (var id in set.Order)
            {
                if (!set.ById.TryGetValue(id, out var warm)) continue;
                // The bytes have to still be there. RemoteMediaCache's over-50 rule deletes oldest
                // first across every consumer, so a file CAN go out from under a warm url; dropping
                // it here (and forgetting the id, so a later warm may re-deal it) is what keeps the
                // promise that a dealt url always loads.
                if (Exists(warm.Path)) { live.Add(warm); continue; }
                (lost ??= new List<string>()).Add(id);
            }
            if (lost != null)
                foreach (var id in lost)
                {
                    set.ById.Remove(id);
                    set.Order.Remove(id);
                }
        }
        return live;
    }

    public void Drain()
    {
        var paths = new List<string>();
        foreach (var set in new[] { _stills, _clips })
            lock (set.Gate)
            {
                paths.AddRange(set.ById.Values.Select(w => w.Path));
                set.ById.Clear();
                set.Order.Clear();
                set.LastWarmUtc = DateTime.MinValue;
            }
        foreach (var p in paths) Release(p);
        if (paths.Count > 0) _log()?.Debug("BackRoomRemotePool: released {Count} warm file(s)", paths.Count);
    }

    private static bool Exists(string path)
    {
        try { return File.Exists(path); }
        catch { return false; }
    }

    private void Release(string path)
    {
        try { _release(path); } catch { /* a locked file is left for the startup sweep */ }
    }

    /// <summary>The <c>ccp.assets</c> url for a materialized file, or null when it did not land under
    /// the assets root (and therefore has no url the page is allowed to load).</summary>
    private string? AssetsUrlFor(string path)
    {
        try
        {
            var root = _assetsRoot();
            if (string.IsNullOrEmpty(root)) return null;
            return BackRoomMedia.ToAssetsUrl(Path.GetFullPath(root), Path.GetFullPath(path));
        }
        catch { return null; }
    }
}
