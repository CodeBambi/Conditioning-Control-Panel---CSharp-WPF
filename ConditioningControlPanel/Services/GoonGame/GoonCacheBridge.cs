using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using ConditioningControlPanel.Services.Transfer;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.GoonGame
{
    /// <summary>
    /// The goon page's window onto <see cref="TransferCompressionService"/>: the assets screen's
    /// data feed, the queue's remote control, and the lane-B (WebCodecs) dispatch loop.
    ///
    /// THE WHOLE FILE IS A THROTTLE. The compression service raises Changed/Progress on worker
    /// threads at whatever rate the encoders finish, and a five-thousand-item pool re-serialised at
    /// that rate would bury <c>PostWebMessageAsJson</c> under megabytes of JSON while a duel is
    /// running in the same renderer. So:
    ///   - <c>cache-state</c> (small, fixed shape) goes out at most 2 Hz, and only when something
    ///     actually changed;
    ///   - <c>cache-progress</c> goes out at most 4 Hz and carries ONLY items whose state or
    ///     percentage moved. Past <see cref="MaxDeltaItems"/> changed items it degenerates to
    ///     <c>{items:[], reload:true}</c> — "re-ask me for the list" is cheaper than the diff;
    ///   - <c>cache-list</c> is NEVER pushed. It is a reply, paged at
    ///     <see cref="ListPageItems"/> items per frame.
    ///
    /// WRITE GUARD. The page can hand bytes back (lane B encodes GIFs the host has no decoder for),
    /// and this is the only path by which it can. It is safe because the page cannot name anything:
    /// a <c>cache-put</c> quotes a <c>jobId</c> the HOST dispatched, the assembled bytes are
    /// magic-checked as ISO-BMFF, they land in the tmp dir OUTSIDE the mapped vhost, and
    /// <see cref="TransferCompressionService.CompletePageJob"/> hashes them host-side to mint the
    /// filename. An unknown jobId is refused before a single byte touches disk.
    ///
    /// Threading: subscribe callbacks arrive on service worker threads and page messages arrive on
    /// the WebView2 UI thread, so every outbound frame goes through <see cref="Post"/>, which
    /// marshals onto the Dispatcher exactly like <c>GoonHostService.ReplyNetPost</c>.
    /// <c>ChaosWebViewHost.Post</c> touches <c>CoreWebView2</c> directly and is NOT thread-safe.
    /// </summary>
    internal static class GoonCacheBridge
    {
        /// <summary>Timer granularity. cache-progress rides every tick, cache-state every other.</summary>
        private const int TickMs = 250;
        private const int StateMinIntervalMs = 500;

        /// <summary>Items per <c>cache-list</c> frame. 500 keeps a frame comfortably under a MB.</summary>
        public const int ListPageItems = 500;
        /// <summary>Ids accepted per compress/delete request.</summary>
        public const int MaxIdsPerRequest = 500;
        /// <summary>Beyond this many changed items, tell the page to re-list instead of diffing.</summary>
        private const int MaxDeltaItems = 500;

        /// <summary>Base64 chars per <c>cache-put</c> part (~3 MB of bytes).</summary>
        public const int MaxPutB64Chars = 4 * 1024 * 1024;
        /// <summary>Parts per lane-B artifact. 16 x 3 MB is far past the 24 MiB wire cap.</summary>
        public const int MaxPutParts = 16;
        /// <summary>A half-uploaded lane-B artifact is dropped after this. The service has its own,
        /// shorter (3 min) no-progress staleness rule that requeues the job.</summary>
        private static readonly TimeSpan PutTimeout = TimeSpan.FromMinutes(5);

        /// <summary>Lane-B encode targets, sent verbatim in <c>encode-request</c>.</summary>
        private const int EncodeMaxBox = 720;
        private const int EncodeMaxFps = 30;
        private const int PreviewHeight = 240;
        private const int PreviewMs = 2000;
        private const int PreviewBitrate = 350_000;

        private static readonly object Lock = new();

        private static ChaosWebViewHost? _host;
        private static Timer? _timer;

        private static bool _stateDirty;
        private static DateTime _lastStateUtc = DateTime.MinValue;

        /// <summary>srcKey -&gt; the delta the page has not seen yet. Drained
        /// <see cref="MaxDeltaItems"/> per tick, never dropped: a delete-all storm takes a couple of
        /// seconds to flow out instead of arriving as one multi-megabyte frame.</summary>
        private static readonly Dictionary<string, ItemDelta> _deltas = new(StringComparer.Ordinal);
        /// <summary>An item left the pool (delete / preset shrank). There is no "gone" item state on
        /// the page, so the honest answer is a fresh paged list rather than a delta.</summary>
        private static bool _relistNeeded;

        /// <summary>srcKey -&gt; "state|pct|sha", the signature the last rebuild published.</summary>
        private static Dictionary<string, string> _lastSig = new(StringComparer.Ordinal);
        /// <summary>No diffing (and no lane counting) until the page has asked for a list — before
        /// that it has nothing to update and the pass would be pure waste.</summary>
        private static bool _listedOnce;

        private static int _laneVideo, _laneStill, _lanePage;

        private static long _lastUsedBytes = -1;
        private static DateTime _lastThroughputUtc = DateTime.MinValue;
        private static double _throughputBps;

        private static string? _lastPresetId;
        private static bool _presetChanged;

        /// <summary>Set by <c>cache-req {op:'hello'}</c>. No page WebCodecs support = no lane-B
        /// dispatch, and the jobs sit in the queue until a capable page shows up.</summary>
        private static bool _pageCanEncode;

        private static string? _dispatchedJobId;

        // lane-B upload buffer (one job at a time, by construction). Two independent part
        // streams per job: 'art' (the artifact, mandatory) and 'prv' (the micro-preview,
        // optional, small) — each with its own gapless seq from 0.
        private static string? _putJobId;
        private static readonly List<byte[]> _putParts = new();
        private static int _putNextSeq;
        private static long _putBytes;
        private static readonly List<byte[]> _putPrvParts = new();
        private static int _putPrvNextSeq;
        private static long _putPrvBytes;
        /// <summary>A refused preview stream drops ONLY the preview — never the artifact.</summary>
        private static bool _putPrvRefused;
        private static DateTime _putStartedUtc;

        /// <summary>Preview parts ceiling — a 240p 2 s clip is ~100 KB; 4 x 3 MB is generous.</summary>
        public const int MaxPrvParts = 4;
        /// <summary>Preview byte ceiling; anything bigger than 2 MB is not a micro-preview.</summary>
        public const long MaxPrvBytes = 2 * 1024 * 1024;

        private sealed class ItemDelta
        {
            public required string Id { get; init; }
            public string State { get; set; } = "";
            public int Pct { get; set; }
            public string? ArtUrl { get; set; }
            public string? PrevUrl { get; set; }
            public long Bytes { get; set; }
            public string? Fail { get; set; }
        }

        /// <summary>
        /// The service's state words are not quite the page's: it says <c>running</c>, the assets
        /// screen's <c>ITEM_STATES</c> says <c>working</c>. Translate here, once, rather than
        /// teaching either side the other's vocabulary.
        /// </summary>
        private static string WireState(string state) => state == "running" ? "working" : state;

        // ============================ lifecycle ============================

        /// <summary>Bind to a live page. Idempotent; re-attaching to the same host is a no-op.</summary>
        public static void Attach(ChaosWebViewHost? host)
        {
            if (host == null) return;
            lock (Lock)
            {
                if (ReferenceEquals(_host, host)) return;
                DetachLocked();
                _host = host;
                _lastSig = new Dictionary<string, string>(StringComparer.Ordinal);
                _listedOnce = false;
                _pageCanEncode = false;
                _dispatchedJobId = null;
                _stateDirty = true;
                _lastStateUtc = DateTime.MinValue;
                _lastUsedBytes = -1;
                _throughputBps = 0;
                _timer = new Timer(OnTick, null, TickMs, TickMs);
            }
            var svc = TransferCompressionService.Instance;
            svc.Changed += OnServiceChanged;
            svc.Progress += OnServiceProgress;
            App.Logger?.Information("GoonCacheBridge: attached");
            // The page's assets screen may mount before it says hello; give it a state to render.
            PostState();
        }

        /// <summary>Unbind. Safe to call when never attached, and safe to call twice.</summary>
        public static void Detach()
        {
            var svc = TransferCompressionService.Instance;
            try { svc.Changed -= OnServiceChanged; } catch { }
            try { svc.Progress -= OnServiceProgress; } catch { }
            lock (Lock)
            {
                if (_host == null && _timer == null) return;
                DetachLocked();
            }
            App.Logger?.Information("GoonCacheBridge: detached");
        }

        private static void DetachLocked()
        {
            try { _timer?.Dispose(); } catch { }
            _timer = null;
            _host = null;
            _deltas.Clear();
            _relistNeeded = false;
            _lastSig.Clear();
            _listedOnce = false;
            _pageCanEncode = false;
            _dispatchedJobId = null;
            AbandonPutLocked("detach");
        }

        // ============================ service events ============================

        private static void OnServiceChanged()
        {
            lock (Lock) { if (_host != null) _stateDirty = true; }
        }

        private static void OnServiceProgress(JobProgress p)
        {
            if (p == null) return;
            lock (Lock)
            {
                if (_host == null) return;
                // Progress also refreshes the header (etaMs / throughput tick down while work runs).
                _stateDirty = true;
                if (!_listedOnce) return;
                var d = Delta(p.SrcKey);
                d.State = "working";
                d.Pct = (int)Math.Clamp(p.Percent, 0, 100);
            }
        }

        /// <summary>Caller holds <see cref="Lock"/>.</summary>
        private static ItemDelta Delta(string id)
        {
            if (!_deltas.TryGetValue(id, out var d)) _deltas[id] = d = new ItemDelta { Id = id };
            return d;
        }

        // ============================ the pump ============================

        private static void OnTick(object? _)
        {
            try
            {
                bool doState;
                lock (Lock)
                {
                    if (_host == null) return;
                    ExpirePutLocked();
                    doState = _stateDirty
                        && (DateTime.UtcNow - _lastStateUtc).TotalMilliseconds >= StateMinIntervalMs;
                    if (doState) { _stateDirty = false; _lastStateUtc = DateTime.UtcNow; }
                }
                if (doState) { Rebuild(); PostState(); }
                FlushDeltas();
                bool relist;
                lock (Lock) { relist = _relistNeeded; _relistNeeded = false; }
                if (relist) SendList();
                DispatchPageJob();
            }
            catch (Exception ex) { App.Logger?.Debug("GoonCacheBridge.OnTick: {E}", ex.Message); }
        }

        /// <summary>
        /// One pass over the pool to (a) count outstanding work per lane for the header and (b) diff
        /// item states into <see cref="_deltas"/>. Runs at most 2 Hz, only when something changed,
        /// and only once the page has actually asked for a list.
        /// </summary>
        private static void Rebuild()
        {
            try
            {
                var items = TransferCompressionService.Instance.ListItems();
                var snap = new Dictionary<string, TransferCacheEntry>(StringComparer.Ordinal);
                foreach (var kv in TransferCacheStore.Instance.Snapshot()) snap[kv.Key] = kv.Value;

                int v = 0, s = 0, p = 0;
                var sig = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var it in items)
                {
                    if (it.State is "pending" or "queued" or "running")
                    {
                        switch (it.Lane)
                        {
                            case TransferLanes.HostMt: v++; break;
                            case TransferLanes.HostSkia: s++; break;
                            case TransferLanes.PageWc: p++; break;
                        }
                    }
                    sig[it.SrcKey] = it.State + "|" + it.Percent + "|" + it.Sha;
                }

                lock (Lock)
                {
                    _laneVideo = v; _laneStill = s; _lanePage = p;
                    if (!_listedOnce) { _lastSig = sig; return; }

                    foreach (var it in items)
                    {
                        if (_lastSig.TryGetValue(it.SrcKey, out var was) && was == sig[it.SrcKey]) continue;
                        snap.TryGetValue(it.SrcKey, out var e);
                        var d = Delta(it.SrcKey);
                        d.State = WireState(it.State);
                        d.Pct = it.Percent;
                        d.Bytes = it.Bytes;
                        d.Fail = it.Fail ?? "";
                        d.ArtUrl = ArtUrlFor(it);
                        d.PrevUrl = e != null && e.PrevBytes > 0 && e.Sha.Length > 0
                            ? "https://ccp.cache/prv/" + e.Sha + ".mp4"
                            : null;
                    }
                    // Something LEFT the pool. The page's item table has no removal delta, so the
                    // truthful update is a fresh paged list (seq 0 restarts its assembly).
                    if (_lastSig.Keys.Any(k => !sig.ContainsKey(k))) _relistNeeded = true;
                    _lastSig = sig;
                }
            }
            catch (Exception ex) { App.Logger?.Debug("GoonCacheBridge.Rebuild: {E}", ex.Message); }
        }

        private static void FlushDeltas()
        {
            object[] payload;
            lock (Lock)
            {
                if (_host == null || _deltas.Count == 0) return;
                var take = _deltas.Values.Take(MaxDeltaItems).ToList();
                payload = take.Select(d => (object)new
                {
                    id = d.Id,
                    state = d.State,
                    pct = d.Pct,
                    artUrl = d.ArtUrl ?? "",
                    prevUrl = d.PrevUrl ?? "",
                    bytes = d.Bytes,
                    fail = d.Fail ?? "",
                }).ToArray();
                foreach (var d in take) _deltas.Remove(d.Id);
            }
            Post(new { type = "cache-progress", items = payload });
        }

        private static void PostState()
        {
            try
            {
                var svc = TransferCompressionService.Instance;
                var st = svc.GetState();

                int working = st.Running + st.PageQueued;
                int pending = Math.Max(0, st.NotReady - working);
                int total = Math.Max(st.Pool, st.Ready + st.Exempt + st.Failed + pending + working);

                double bps;
                bool presetChanged;
                int lv, ls, lp;
                lock (Lock)
                {
                    // Throughput as ARTIFACT BYTES LANDING PER SECOND (the only rate observable from
                    // out here - the service reports no source-byte counter). Idle reads as 0.
                    var now = DateTime.UtcNow;
                    if (_lastUsedBytes >= 0)
                    {
                        var dt = (now - _lastThroughputUtc).TotalSeconds;
                        var db = st.UsedBytes - _lastUsedBytes;
                        var sample = dt > 0.05 && db > 0 ? db / dt : 0;
                        _throughputBps = _throughputBps <= 0 ? sample : _throughputBps * 0.7 + sample * 0.3;
                    }
                    _lastUsedBytes = st.UsedBytes;
                    _lastThroughputUtc = now;
                    bps = working > 0 ? _throughputBps : 0;

                    // Sticky "your preset changed - N assets need compressing" nudge: raised when the
                    // active preset id moves with work outstanding, cleared when the page reacts
                    // (asks for the list, or starts compressing).
                    if (_lastPresetId != st.PresetId)
                    {
                        if (_lastPresetId != null && pending + working > 0) _presetChanged = true;
                        _lastPresetId = st.PresetId;
                    }
                    presetChanged = _presetChanged;
                    lv = _laneVideo; ls = _laneStill; lp = _lanePage;
                }

                Post(new
                {
                    type = "cache-state",
                    capBytes = st.CapBytes,
                    usedBytes = st.UsedBytes,
                    overCap = st.OverCapProtected,
                    ready = st.Ready,
                    pending,
                    queued = st.Queued,
                    working,
                    failed = st.Failed,
                    exempt = st.Exempt,
                    total,
                    paused = st.Paused,
                    pausedBy = st.PausedBy,
                    etaMs = st.EtaMs,
                    throughputBps = (long)bps,
                    hw = st.Hardware,
                    presetId = st.PresetId,
                    presetName = PresetNameFor(st.PresetId),
                    presetChanged,
                    lanes = new { video = lv, still = ls, page = lp },
                });
            }
            catch (Exception ex) { App.Logger?.Debug("GoonCacheBridge.PostState: {E}", ex.Message); }
        }

        // ============================ page -> host ============================

        /// <summary>Route one page frame. Called on the WebView2 UI thread by GoonHostService.</summary>
        public static void OnMessage(JObject o)
        {
            switch ((string?)o["type"])
            {
                case "cache-req": OnRequest(o); break;
                case "cache-put": OnPut(o); break;
                case "encode-done": OnEncodeDone(o); break;
            }
        }

        private static void OnRequest(JObject o)
        {
            var svc = TransferCompressionService.Instance;
            var op = (string?)o["op"] ?? "";
            try
            {
                switch (op)
                {
                    case "hello":
                        // The page's own probeEncode() result, verbatim from assetsStore.js:
                        // { videoEncoder, hw, gif, awebp, recorderMp4 }. Lane B needs an ENCODER
                        // plus a DECODER for at least one animated container; the MediaRecorder
                        // fallback covers a runtime with neither WebCodecs nor ImageDecoder.
                        // (`webcodecs`/`mp4` are tolerated aliases so an older page still works.)
                        var caps = o["caps"] as JObject;
                        bool enc = (bool?)caps?["videoEncoder"] == true || (bool?)caps?["webcodecs"] == true;
                        bool dec = (bool?)caps?["gif"] == true || (bool?)caps?["awebp"] == true
                                   || (bool?)caps?["mp4"] == true;
                        _pageCanEncode = (enc && dec) || (bool?)caps?["recorderMp4"] == true;
                        App.Logger?.Information("GoonCacheBridge: page hello (lane B available: {Can})", _pageCanEncode);
                        _ = Task.Run(async () => { try { await svc.RefreshAsync().ConfigureAwait(false); } catch { } });
                        break;

                    case "list":
                        lock (Lock) { _listedOnce = true; _presetChanged = false; }
                        Rebuild();
                        SendList();
                        break;

                    case "compress-all":
                        lock (Lock) _presetChanged = false;
                        svc.CompressAll((bool?)o["retryFailed"] == true);
                        break;

                    case "compress":
                        foreach (var id in Ids(o)) svc.CompressOne(id);
                        break;

                    case "cancel":
                        var cancelIds = Ids(o);
                        if (cancelIds.Count == 0) svc.CancelAll();
                        else foreach (var id in cancelIds) svc.Cancel(id);
                        break;

                    case "pause":
                        if ((string?)o["reason"] == "match") svc.PauseForMatch(); else svc.PauseUser();
                        break;

                    case "resume":
                        if ((string?)o["reason"] == "match") svc.ResumeAfterMatch(); else svc.ResumeUser();
                        break;

                    case "delete":
                        foreach (var id in Ids(o)) svc.DeleteOne(id);
                        break;

                    case "delete-all":
                        svc.DeleteAll();
                        break;

                    case "set-cap":
                        // Clamped HOST-side regardless of what the slider sent.
                        var raw = (long?)o["capBytes"] ?? TransferCacheStore.DefaultCapBytes;
                        svc.SetCapBytes(Math.Clamp(raw, TransferCacheStore.MinCapBytes, TransferCacheStore.MaxCapBytes));
                        break;

                    case "encode-progress":
                        // Keeps a long lane-B encode off the service's stale-job timer.
                        var pid = (string?)o["jobId"] ?? "";
                        if (pid.Length > 0) svc.ReportPageProgress(pid, (double?)o["pct"] ?? 0);
                        break;

                    default:
                        App.Logger?.Debug("GoonCacheBridge: unknown cache-req op '{Op}'", op);
                        return;
                }
            }
            catch (Exception ex) { App.Logger?.Warning("GoonCacheBridge.cache-req({Op}): {E}", op, ex.Message); }
            lock (Lock) _stateDirty = true;
        }

        /// <summary>Ids from a request body, deduped and capped. Never trusts the count.</summary>
        private static List<string> Ids(JObject o)
        {
            var outp = new List<string>();
            if (o["ids"] is not JArray arr) return outp;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var t in arr)
            {
                if (outp.Count >= MaxIdsPerRequest) break;
                var s = (string?)t;
                if (!string.IsNullOrEmpty(s) && seen.Add(s!)) outp.Add(s!);
            }
            return outp;
        }

        private static void SendList()
        {
            try
            {
                var items = TransferCompressionService.Instance.ListItems();
                var snap = new Dictionary<string, TransferCacheEntry>(StringComparer.Ordinal);
                foreach (var kv in TransferCacheStore.Instance.Snapshot()) snap[kv.Key] = kv.Value;

                int total = items.Count;
                int frames = Math.Max(1, (total + ListPageItems - 1) / ListPageItems);
                for (int f = 0; f < frames; f++)
                {
                    var page = items.Skip(f * ListPageItems).Take(ListPageItems).Select(it =>
                    {
                        snap.TryGetValue(it.SrcKey, out var e);
                        return (object)new
                        {
                            id = it.SrcKey,
                            rel = it.Rel,
                            name = NameOf(it.Rel),
                            kind = it.Kind,
                            state = WireState(it.State),
                            lane = it.Lane,
                            sha = it.Sha,
                            ext = it.Ext,
                            // srcBytes is the name the assets screen reads for "what compressing
                            // this would have to chew through"; `size` is the same number kept for
                            // anything that grew up on the C# field name.
                            srcBytes = it.Size,
                            size = it.Size,
                            bytes = it.Bytes,
                            w = it.W,
                            h = it.H,
                            durMs = it.DurMs,
                            fail = it.Fail ?? "",
                            pct = it.Percent,
                            srcUrl = AssetUrl(it.Rel),
                            artUrl = ArtUrlFor(it) ?? "",
                            prevUrl = e != null && e.PrevBytes > 0 && e.Sha.Length > 0
                                ? "https://ccp.cache/prv/" + e.Sha + ".mp4"
                                : "",
                        };
                    }).ToArray();
                    Post(new { type = "cache-list", seq = f, last = f == frames - 1, total, items = page });
                }
            }
            catch (Exception ex) { App.Logger?.Warning("GoonCacheBridge.SendList: {E}", ex.Message); }
        }

        // ============================ lane-B dispatch ============================

        /// <summary>
        /// Hand the page the next WebCodecs job, if it can do them and one is waiting. The service
        /// keeps returning the SAME outstanding job until it is completed or goes stale, so this
        /// only posts when the id actually changes.
        /// </summary>
        private static void DispatchPageJob()
        {
            try
            {
                bool can;
                lock (Lock) can = _host != null && _pageCanEncode;
                if (!can) return;
                if (!TransferCompressionService.Instance.TryDequeuePageJob(out var job) || job == null) return;

                lock (Lock)
                {
                    if (_dispatchedJobId == job.JobId) return;
                    _dispatchedJobId = job.JobId;
                    AbandonPutLocked("new job");
                }
                Post(new
                {
                    type = "encode-request",
                    jobId = job.JobId,
                    id = job.JobId,
                    srcUrl = job.Url,
                    kind = job.Kind,
                    maxBox = EncodeMaxBox,
                    bitrate = VideoTranscodeLane.TargetBitrateFor(0),
                    maxFps = EncodeMaxFps,
                    prevHeight = PreviewHeight,
                    prevMs = PreviewMs,
                    prevBitrate = PreviewBitrate,
                });
                App.Logger?.Information("GoonCacheBridge: encode-request {Rel}", job.Rel);
            }
            catch (Exception ex) { App.Logger?.Debug("GoonCacheBridge.DispatchPageJob: {E}", ex.Message); }
        }

        /// <summary>
        /// <c>cache-put {jobId, seq, b64, last?}</c> — one part of a lane-B artifact. Buffered in
        /// memory (16 x 3 MB ceiling) and never written until <c>encode-done</c> commits, so a page
        /// that starts an upload and vanishes leaves nothing behind.
        /// </summary>
        private static void OnPut(JObject o)
        {
            var jobId = (string?)o["jobId"] ?? "";
            var seq = (int?)o["seq"] ?? -1;
            var b64 = (string?)o["b64"] ?? "";
            var part = (string?)o["part"] ?? "art";   // absent = 'art' (pre-preview pages)
            string? error = null;

            lock (Lock)
            {
                ExpirePutLocked();
                if (jobId.Length == 0 || _dispatchedJobId == null || jobId != _dispatchedJobId)
                    error = "unknown-job";
                else if (part != "art" && part != "prv")
                    error = "bad-seq";
                else if (b64.Length == 0 || b64.Length > MaxPutB64Chars)
                    error = "too-big";
                else
                {
                    if (_putJobId != jobId)
                    {
                        _putJobId = jobId;
                        _putParts.Clear();
                        _putNextSeq = 0;
                        _putBytes = 0;
                        _putPrvParts.Clear();
                        _putPrvNextSeq = 0;
                        _putPrvBytes = 0;
                        _putPrvRefused = false;
                        _putStartedUtc = DateTime.UtcNow;
                    }
                    var isArt = part == "art";
                    var nextSeq = isArt ? _putNextSeq : _putPrvNextSeq;
                    if (seq != nextSeq) error = "bad-seq";
                    else if (isArt && _putParts.Count >= MaxPutParts) error = "too-big";
                    else if (!isArt && (_putPrvParts.Count >= MaxPrvParts || _putPrvBytes >= MaxPrvBytes)) error = "too-big";
                    else
                    {
                        try
                        {
                            var bytes = Convert.FromBase64String(b64);
                            if (bytes.Length == 0) error = "bad-seq";
                            else if (isArt)
                            {
                                _putParts.Add(bytes);
                                _putBytes += bytes.Length;
                                _putNextSeq = seq + 1;
                            }
                            else
                            {
                                _putPrvParts.Add(bytes);
                                _putPrvBytes += bytes.Length;
                                _putPrvNextSeq = seq + 1;
                            }
                        }
                        catch { error = "bad-seq"; }
                    }
                }
                if (error != null && error != "unknown-job" && _putJobId == jobId)
                {
                    if (part == "prv")
                    {
                        // Cosmetic stream: drop the preview, keep the artifact upload alive.
                        _putPrvParts.Clear();
                        _putPrvBytes = 0;
                        _putPrvNextSeq = 0;
                        _putPrvRefused = true;
                    }
                    else
                    {
                        AbandonPutLocked(error);
                    }
                }
            }

            if (error != null)
                App.Logger?.Warning("GoonCacheBridge: cache-put {Job} {Part} seq {Seq} refused ({E})", jobId, part, seq, error);
            Post(new { type = "cache-put-result", jobId, part, seq, ok = error == null, error });
        }

        /// <summary>
        /// <c>encode-done {jobId, ok, parts, ext, w, h, durMs, fail?}</c> — the COMMIT. Everything
        /// the page claimed about the bytes is checked or ignored: the part count must match what
        /// arrived, the bytes must sniff as ISO-BMFF, and the sha is minted by the service from the
        /// file it writes. A jobId we did not dispatch never reaches the disk.
        /// </summary>
        private static void OnEncodeDone(JObject o)
        {
            var jobId = (string?)o["jobId"] ?? "";
            var ok = (bool?)o["ok"] ?? false;
            var fail = (string?)o["fail"];
            var ext = (string?)o["ext"] ?? "mp4";
            int w = (int?)o["w"] ?? 0, h = (int?)o["h"] ?? 0, durMs = (int?)o["durMs"] ?? 0;
            int? claimedParts = (int?)o["parts"];
            int? claimedPrvParts = (int?)o["prvParts"];

            byte[]? blob = null;
            byte[]? prvBlob = null;
            string? error = null;
            lock (Lock)
            {
                if (jobId.Length == 0 || _dispatchedJobId == null || jobId != _dispatchedJobId)
                {
                    error = "unknown-job";
                }
                else if (!ok)
                {
                    AbandonPutLocked("page reported failure");
                    _dispatchedJobId = null;
                }
                else if (_putJobId != jobId || _putParts.Count == 0)
                {
                    error = "unknown-job";
                }
                else if (claimedParts != null && claimedParts.Value != _putParts.Count)
                {
                    error = "bad-seq";
                    AbandonPutLocked(error);
                }
                else
                {
                    blob = new byte[_putBytes];
                    int off = 0;
                    foreach (var part in _putParts) { Buffer.BlockCopy(part, 0, blob, off, part.Length); off += part.Length; }

                    // Preview is cosmetic end to end: a claim mismatch or a refused stream just
                    // means no preview — the artifact commit is never held hostage by it.
                    if (!_putPrvRefused && _putPrvParts.Count > 0 &&
                        (claimedPrvParts == null || claimedPrvParts.Value == _putPrvParts.Count))
                    {
                        prvBlob = new byte[_putPrvBytes];
                        int poff = 0;
                        foreach (var part in _putPrvParts) { Buffer.BlockCopy(part, 0, prvBlob, poff, part.Length); poff += part.Length; }
                    }
                    AbandonPutLocked("committed");
                    _dispatchedJobId = null;
                }
            }

            if (prvBlob != null && !TransferInboxStore.LooksLikeMp4(prvBlob))
            {
                App.Logger?.Debug("GoonCacheBridge: encode-done {Job} preview is not an mp4 - dropped", jobId);
                prvBlob = null;
            }

            if (error != null)
            {
                App.Logger?.Warning("GoonCacheBridge: encode-done {Job} refused ({E})", jobId, error);
                Post(new { type = "cache-put-result", jobId, seq = -1, ok = false, error });
                return;
            }

            if (!ok || blob == null)
            {
                TransferCompressionService.Instance.CompletePageJob(jobId, false, null, null, 0, 0, 0,
                    fail ?? TransferFailReasons.EncodeFailed);
                Post(new { type = "cache-put-result", jobId, seq = -1, ok = false, error = fail ?? "encode-failed" });
                lock (Lock) _stateDirty = true;
                return;
            }

            // Magic check BEFORE anything is written: the lane's whole contract is "an mp4 came back".
            if (!TransferInboxStore.LooksLikeMp4(blob))
            {
                App.Logger?.Warning("GoonCacheBridge: encode-done {Job} is not an mp4 - rejected", jobId);
                TransferCompressionService.Instance.CompletePageJob(jobId, false, null, null, 0, 0, 0,
                    TransferFailReasons.EncodeFailed);
                Post(new { type = "cache-put-result", jobId, seq = -1, ok = false, error = "bad-format" });
                lock (Lock) _stateDirty = true;
                return;
            }

            var payload = blob;
            var preview = prvBlob;
            _ = Task.Run(() =>
            {
                try
                {
                    // Land it in the tmp dir (SIBLING of the mapped root - a half-written artifact is
                    // never page-reachable) and let the service hash + name + index it.
                    Directory.CreateDirectory(TransferCacheStore.Instance.TmpDir);
                    TransferCompressionService.Instance.CompletePageJob(jobId, true, payload,
                        ext, w, h, durMs, null, previewBytes: preview);
                    Post(new { type = "cache-put-result", jobId, seq = -1, ok = true, done = true });
                }
                catch (Exception ex)
                {
                    App.Logger?.Warning("GoonCacheBridge.encode-done({Job}): {E}", jobId, ex.Message);
                    Post(new { type = "cache-put-result", jobId, seq = -1, ok = false, error = "io-failed" });
                }
                lock (Lock) _stateDirty = true;
            });
        }

        /// <summary>Caller holds <see cref="Lock"/>.</summary>
        private static void AbandonPutLocked(string why)
        {
            if (_putJobId != null && _putParts.Count > 0)
                App.Logger?.Debug("GoonCacheBridge: dropped {N} buffered parts for {Job} ({Why})",
                    _putParts.Count, _putJobId, why);
            _putJobId = null;
            _putParts.Clear();
            _putNextSeq = 0;
            _putBytes = 0;
            _putPrvParts.Clear();
            _putPrvNextSeq = 0;
            _putPrvBytes = 0;
            _putPrvRefused = false;
        }

        private static void ExpirePutLocked()
        {
            if (_putJobId != null && DateTime.UtcNow - _putStartedUtc > PutTimeout)
                AbandonPutLocked("upload timed out");
        }

        // ============================ helpers ============================

        /// <summary>Marshal onto the Dispatcher — every caller here may be on a worker thread, and
        /// <c>ChaosWebViewHost.Post</c> touches CoreWebView2 directly.</summary>
        private static void Post(object msg)
        {
            try
            {
                var disp = Application.Current?.Dispatcher;
                if (disp == null || disp.HasShutdownStarted) return;
                disp.BeginInvoke(() =>
                {
                    try
                    {
                        ChaosWebViewHost? h;
                        lock (Lock) h = _host;
                        h?.Post(msg);
                    }
                    catch (Exception ex) { App.Logger?.Debug("GoonCacheBridge.Post: {E}", ex.Message); }
                });
            }
            catch (Exception ex) { App.Logger?.Debug("GoonCacheBridge.Post dispatch: {E}", ex.Message); }
        }

        /// <summary>Where the page can actually fetch this item's sendable bytes. Ready entries live
        /// in the cache; exempt ones ARE the original, so the page uses <c>srcUrl</c> instead.</summary>
        private static string? ArtUrlFor(CacheItem it) =>
            it.State == TransferStates.Ready && it.Sha.Length > 0 && it.Ext.Length > 0
                ? "https://ccp.cache/art/" + it.Sha + "." + it.Ext
                : null;

        /// <summary>Percent-escape per SEGMENT, exactly as <c>DtrhAssetManifest.ToAssetUrl</c> does —
        /// escaping the whole path would eat the slashes and 404 every nested asset.</summary>
        private static string AssetUrl(string rel)
        {
            var norm = (rel ?? "").Replace('\\', '/');
            return "https://ccp.assets/" + string.Join('/', norm.Split('/').Select(Uri.EscapeDataString));
        }

        private static string NameOf(string rel)
        {
            var norm = (rel ?? "").Replace('\\', '/');
            var i = norm.LastIndexOf('/');
            return i >= 0 ? norm[(i + 1)..] : norm;
        }

        private static string? PresetNameFor(string? id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            try { return App.Settings?.Current?.AssetPresets?.FirstOrDefault(p => p.Id == id)?.Name; }
            catch { return null; }
        }
    }
}
