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

/// <summary>One warm remote CLIP: a materialized webm/mp4 on a <c>ccp.assets</c> url over the file the
/// bytes were written to, so it is the same kind of thing a local pick is and the page cannot tell them
/// apart. The page plays it rather than decodes it (<c>room\clip-source.js</c>, routed on the url's
/// extension from <c>room\gif.js</c>, <c>stations\slot\media.js</c> and <c>shared\hypno\media.js</c>);
/// what makes it a clip is the extension in <see cref="Url"/> and nothing else. The temp path itself
/// stays inside <see cref="BackRoomRemotePool"/> (PII rule).</summary>
/// <param name="Id">The provider's entry id. The dedupe key: one id is materialized ONCE.</param>
/// <param name="W">The provider's size for the post. Aspect only: the file on disk is the small
/// rendition, and the page paints at its own edge anyway.</param>
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
    /// Returns a completed task whenever the set is already at target, a warm is in flight, or the
    /// fetch gap has not elapsed.</summary>
    Task WarmAsync(CancellationToken ct);

    /// <summary>The clips whose bytes are on disk RIGHT NOW. Never a network call.</summary>
    IReadOnlyList<BackRoomRemoteClip> Ready();

    /// <summary>Room closed: hand every materialized file back.</summary>
    void Drain();
}

/// <summary>
/// THE ROOM'S REMOTE MEDIA (CONTRACT section 5, 10.13.C). One warm pool of Scrolller CLIPS that are
/// already downloaded AND already written to a real file, so a sit-down is a memory read and a
/// directory listing, never a GraphQL call: <c>ScrolllerSource</c> is throttled to one request per
/// 1.1 s process-wide, and a player dropping into a chair cannot be made to wait behind that gate.
///
/// <para>THE UNLOCK is where the bytes land. <c>RemoteMediaCache.MaterializeAsync</c> writes them
/// under <c>App.GetMediaTempPath()</c>, which is <c>{App.EffectiveAssetsPath}\.temp</c>, and the host
/// maps <c>ccp.assets</c> to <c>App.EffectiveAssetsPath</c>. So a materialized file already has a
/// legal <c>https://ccp.assets/.temp/&lt;file&gt;</c> url: CORS-clean, WebGL-safe, it passes the page's
/// own <c>allowed()</c> checks, and it still resolves back to a real file for the host's fullscreen
/// effects. No proxy, no new host, no new mapping. It also keeps the source's extension
/// (<c>MaterializeAsync</c> takes it from the url, because consumers sniff the codec off it), which is
/// precisely what lets the page route a <c>.mp4</c> to the clip player.</para>
///
/// <para>THE DEDUPE TRAP, which has bitten this codebase before with content-pack decrypts: every
/// <c>MaterializeAsync</c> call mints a NEW guid filename, so two materializes of one picture are two
/// urls and the page's url-keyed dedupe (<c>room\screens.js</c>, <c>stations\slot\media.js</c>) cannot
/// collapse them - the same picture would appear twice on the wall. That is why the set is keyed on
/// the PROVIDER'S ENTRY ID and materializes each id exactly once, keeping the path for the life of the
/// pool.</para>
///
/// <para>CLIPS ONLY, AND THE SMALL RENDITION (2026-09-17, "discard stills"). The pool used to keep two
/// sets: the provider's static posters for the stations and its webm/mp4 for the wall. The owner's
/// call was to drop the poster as a media class altogether, so every surface animates. What the
/// measurement behind that change actually showed is worth keeping: the posters were never the reason
/// clips took minutes to arrive. A clip cost ~12 s to materialize and they were materialized one at a
/// time, because the entry's <c>Url</c> is the largest rendition up to 1920 px wide and the room paints
/// at 384 px. So this pool fetches <see cref="FeedMediaKind.GifClip"/> (the GIF filter's clip half,
/// never a full-length VIDEO upload), materializes the entry's <c>SmallUrl</c> (the rendition capped at
/// 640 px, falling back to <c>Url</c> when a post has no smaller one), and keeps
/// <see cref="MaterializeConcurrency"/> downloads in flight. WebView2 is Chromium and decodes VP9 and
/// H.264 natively, so there is still no transcode hop and no installer bytes.</para>
///
/// <para>The bright line (<c>IFeedSource.cs</c>): the fetch happens on the user's device, direct from
/// the provider. Nothing here routes through CC Labs infrastructure.</para>
/// </summary>
internal sealed class BackRoomRemotePool : IBackRoomRemotePool
{
    /// <summary>The room's own tenant in the coordinator registry. Its own rotation state and dwell
    /// store, so the casino and the flashes cannot fight over one set of channel iterators.</summary>
    internal const string ConsumerId = "backroom";

    /// <summary>Fill to here: one full wall (<c>room\screens.js</c> caps it at <c>MAX_PICTURES = 8</c>
    /// and <c>room\main.js</c> asks for 8) plus a station's deal drawn from the same set, with enough
    /// left that a re-deal reshuffles rather than repeats. The card table asks for 13, the wheel 8,
    /// the slot and roulette 4; each deal is a seeded draw over the whole set, so 16 gives a 13-card
    /// deck and an 8-screen wall that only partly overlap.</summary>
    internal const int ReadyTarget = 16;

    /// <summary>Hard ceiling. <c>RemoteMediaCache.MaxTrackedTempFiles = 50</c> deletes OLDEST-FIRST
    /// across every consumer once it is over, and the pool's own earlier clips are the oldest files
    /// there are, so a pool that grew past the tracker would sweep its own live wall urls out from
    /// under the page (the failure <see cref="Ready"/>'s existence check exists to survive). The only
    /// other tenant that materializes is the desktop wallpaper (<c>WallpaperService.RemotePoolTarget
    /// = 4</c>); the flash pool, the For You feed and the video service never touch disk. 24 + 4 is
    /// 28 of 50, which is more headroom than the room left itself when it kept 34.</summary>
    internal const int ReadyMax = 24;

    /// <summary>Downloads in flight at once during one batch. Four, because the batch is bandwidth-
    /// bound (a clip is a few hundred KB, a still was a few tens), a single CDN is on the other end and
    /// the wallpaper shares the pipe, and a browser's own per-host politeness is six. More lanes would
    /// only fight each other for the same link; one lane was the 12-seconds-a-clip the room had.</summary>
    internal const int MaterializeConcurrency = 4;

    /// <summary>Minimum gap between batch fetches. The source is already throttled ~1 req/1.1 s
    /// process-wide; this stops a room that re-deals often from queueing behind that gate faster than
    /// it drains.</summary>
    internal const int WarmGapSeconds = 8;

    /// <summary>The MOST a deal will ever wait on a warm, and only when the set is empty. The page
    /// gives the host 6000 ms for a <c>media</c> reply (<c>room\main.js</c>), so this leaves room to
    /// spare; the batch carries on filling in the background past the cap, the WAIT is what stops.
    /// A cold batch can land after the cap, which is not a bug: that deal is the player's own files
    /// (or the bundled loops) and the next one is clips.</summary>
    internal const int WarmWaitMs = 2000;

    /// <summary>The app's live pool. A property rather than a field so nothing forces this type's
    /// static init while <c>BackRoomHostService</c> is still building its own statics.</summary>
    private static BackRoomRemotePool? _shared;
    internal static BackRoomRemotePool Shared => _shared ??= new BackRoomRemotePool();

    private readonly Func<Models.AppSettings?> _settings;
    private readonly Func<CancellationToken, Task<(List<FypAssetManifest.Entry> Entries, string? Error)>> _fetch;
    private readonly Func<string, CancellationToken, Task<string?>> _materialize;
    private readonly Action<string> _release;
    private readonly Func<string?> _assetsRoot;
    private readonly Func<ILogger?> _log;

    /// <summary>What the warm set holds per entry. The path is in here rather than in the deal's view of
    /// it, which is how "no path ever leaves this class" is kept while still being able to re-check
    /// that the file exists and to hand it back on <see cref="Drain"/>.</summary>
    private readonly record struct Warm(string Id, string Url, int W, int H, string Path);

    private readonly object _gate = new();
    /// <summary>Entry id -> what the deal gets, plus the file behind it. THE dedupe key.</summary>
    private readonly Dictionary<string, Warm> _byId = new(StringComparer.Ordinal);
    /// <summary>Insertion order, so the oldest entry is the one a full set refuses to grow past.</summary>
    private readonly List<string> _order = new();
    /// <summary>Downloads started and not yet added or released. Counted against the ceiling so four
    /// lanes cannot overshoot it by three files that then have to be thrown away.</summary>
    private int _inFlight;
    /// <summary>The ids those downloads are for. With one lane a repeated id in a batch was "known" by
    /// the time the loop reached it; with four, the first copy is still on the wire, and without this
    /// set the second copy is downloaded too and thrown away on landing.</summary>
    private readonly HashSet<string> _inFlightIds = new(StringComparer.Ordinal);
    private Task? _warming;
    private DateTime _lastWarmUtc = DateTime.MinValue;

    /// <summary>The app's live sources.</summary>
    internal BackRoomRemotePool()
        : this(() => App.Settings?.Current, null, null, null, null, null)
    {
    }

    /// <summary>Seams for tests: the settings, the batch fetch, the materialize and its release, the
    /// assets root and the log. A null seam wires to the real stack.</summary>
    internal BackRoomRemotePool(
        Func<Models.AppSettings?>? settings,
        Func<CancellationToken, Task<(List<FypAssetManifest.Entry> Entries, string? Error)>>? fetch,
        Func<string, CancellationToken, Task<string?>>? materialize,
        Action<string>? release,
        Func<string?>? assetsRoot,
        Func<ILogger?>? log)
    {
        _settings = settings ?? (() => App.Settings?.Current);
        _fetch = fetch ?? DefaultFetchAsync;
        _materialize = materialize ?? RemoteMediaCache.MaterializeAsync;
        _release = release ?? RemoteMediaCache.ReleaseTempFile;
        _assetsRoot = assetsRoot ?? (() => App.EffectiveAssetsPath);
        _log = log ?? (() => App.Logger);
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

    /// <summary>The room's coordinator: one tenant, one kind. <see cref="FeedMediaKind.GifClip"/> is
    /// the GIF filter only, so a gif-only sub with an empty VIDEO page can no longer exhaust the
    /// channel, and the flashes' own GifStill tenant is untouched because this is a different one.</summary>
    private Task<(List<FypAssetManifest.Entry> Entries, string? Error)> DefaultFetchAsync(CancellationToken ct)
        => FypOnlineCoordinator.For(ConsumerId, () => RoomChannels(_settings()), FeedMediaKind.GifClip)
            .FetchBatchAsync(FeedMediaKind.GifClip, ct);

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
        _ = StartWarm();
    }

    public Task WarmAsync(CancellationToken ct)
    {
        var warm = StartWarm();
        return warm == null ? Task.CompletedTask : WaitBounded(warm, ct);
    }

    /// <summary>The one gate. Returns the warm in flight, a new one, or null when the set does not
    /// need topping up right now.</summary>
    private Task? StartWarm()
    {
        if (!Wanted) return null;
        lock (_gate)
        {
            if (_warming != null) return _warming;
            if (_order.Count >= ReadyTarget) return null;
            if ((DateTime.UtcNow - _lastWarmUtc).TotalSeconds < WarmGapSeconds) return null;
            _lastWarmUtc = DateTime.UtcNow;
            return _warming = Task.Run(WarmBatchAsync);
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

    /// <summary>The url one entry is downloaded from: the card-sized rendition when the post has one
    /// with a playable extension, else the post's best. The page paints a clip at 384 px or less, so
    /// the 1920 px rendition the entry's <c>Url</c> names is bytes nobody sees.</summary>
    internal static string DownloadUrl(FypAssetManifest.Entry entry)
        => !string.IsNullOrEmpty(entry.SmallUrl) && RemoteMediaFormats.IsRemoteVideo(entry.SmallUrl)
            ? entry.SmallUrl!
            : entry.Url;

    /// <summary>One coordinator batch -> entries validated as clips -> bytes in a real file, up to
    /// <see cref="MaterializeConcurrency"/> at a time -> ccp.assets urls in the set. Never throws,
    /// never touches the UI, logs counts only.</summary>
    private async Task WarmBatchAsync()
    {
        int warmed = 0, dropped = 0;
        var lanes = new List<Task>();
        using var lane = new SemaphoreSlim(MaterializeConcurrency);
        try
        {
            var (entries, error) = await _fetch(CancellationToken.None).ConfigureAwait(false);
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
                lock (_gate)
                {
                    // In-flight downloads count against the ceiling, so four lanes cannot carry the
                    // set three files past it and then release them.
                    full = _order.Count + _inFlight >= ReadyMax;
                    known = entry != null && !string.IsNullOrEmpty(entry.Id)
                        && (_byId.ContainsKey(entry.Id) || _inFlightIds.Contains(entry.Id));
                }
                if (full) break;
                if (known) continue;   // the trap: one entry id is materialized exactly once
                if (entry == null || string.IsNullOrEmpty(entry.Id)) { dropped++; continue; }

                if (!RemoteMediaFormats.Validate(entry, FeedMediaKind.GifClip, out _)) { dropped++; continue; }

                // Download AND write the file now, off the UI thread, so the deal later is a pure
                // lookup. The lane is taken here, on the loop, so the loop itself paces the batch:
                // it cannot run ahead and queue thirty downloads behind four lanes.
                await lane.WaitAsync().ConfigureAwait(false);
                lock (_gate) { _inFlight++; _inFlightIds.Add(entry.Id); }
                lanes.Add(Task.Run(async () =>
                {
                    try
                    {
                        if (await MaterializeOneAsync(entry).ConfigureAwait(false)) Interlocked.Increment(ref warmed);
                        else Interlocked.Increment(ref dropped);
                    }
                    finally
                    {
                        lock (_gate) { _inFlight--; _inFlightIds.Remove(entry.Id); }
                        lane.Release();
                    }
                }));
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
            // Every lane finishes before the batch is declared over, or a late add would land in a
            // set the next warm believes is idle. A lane never throws; its failures are counted.
            try { await Task.WhenAll(lanes).ConfigureAwait(false); } catch { /* counted inside the lane */ }
            int ready;
            lock (_gate) { _warming = null; ready = _order.Count; }
            // Counts only, never a path and never a url (PII rule). A host, when one is ever worth
            // naming here, goes through Logging.UrlLog.Host and nothing else.
            if (warmed > 0 || dropped > 0)
                _log()?.Information("BackRoomRemotePool: warmed {Warmed} clip(s), dropped {Dropped}, {Ready} ready",
                    warmed, dropped, ready);
        }
    }

    /// <summary>One lane: download, land, add. True when the clip joined the set. A failure here just
    /// means this one entry never joins; nothing escapes to the batch.</summary>
    private async Task<bool> MaterializeOneAsync(FypAssetManifest.Entry entry)
    {
        string? path;
        try { path = await _materialize(DownloadUrl(entry), CancellationToken.None).ConfigureAwait(false); }
        catch (Exception ex)
        {
            _log()?.Debug("BackRoomRemotePool: materialize failed ({Type})", ex.GetType().Name);
            return false;
        }
        if (string.IsNullOrEmpty(path)) return false;

        var url = AssetsUrlFor(path!);
        if (url == null)
        {
            // GetMediaTempPath falls back to the SYSTEM temp when the assets folder is unusable, and
            // nothing outside the assets root has a ccp.assets url the page would load. Hand the file
            // straight back rather than hold one the room cannot show.
            Release(path!);
            return false;
        }

        bool added;
        lock (_gate)
        {
            // Re-checked under the gate: another lane may have landed the same id, or filled the set,
            // while this download was on the wire.
            added = !_byId.ContainsKey(entry.Id) && _order.Count < ReadyMax;
            if (added)
            {
                _byId[entry.Id] = new Warm(entry.Id, url, entry.Width ?? 0, entry.Height ?? 0, path!);
                _order.Add(entry.Id);
            }
        }
        if (!added) Release(path!);
        return added;
    }

    public IReadOnlyList<BackRoomRemoteClip> Ready()
    {
        var live = new List<BackRoomRemoteClip>();
        List<string>? lost = null;
        lock (_gate)
        {
            foreach (var id in _order)
            {
                if (!_byId.TryGetValue(id, out var warm)) continue;
                // The bytes have to still be there. RemoteMediaCache's over-50 rule deletes oldest
                // first across every consumer, so a file CAN go out from under a warm url; dropping
                // it here (and forgetting the id, so a later warm may re-deal it) is what keeps the
                // promise that a dealt url always loads.
                if (Exists(warm.Path)) { live.Add(new BackRoomRemoteClip(warm.Id, warm.Url, warm.W, warm.H)); continue; }
                (lost ??= new List<string>()).Add(id);
            }
            if (lost != null)
                foreach (var id in lost)
                {
                    _byId.Remove(id);
                    _order.Remove(id);
                }
        }
        return live;
    }

    public void Drain()
    {
        List<string> paths;
        lock (_gate)
        {
            paths = _byId.Values.Select(w => w.Path).ToList();
            _byId.Clear();
            _order.Clear();
            _lastWarmUtc = DateTime.MinValue;
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
