using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ConditioningControlPanel.Services.Transfer
{
    /// <summary>
    /// A queued/running/failed item as the assets screen wants to render it.
    ///
    /// <c>Kind</c> is the SOURCE's ("gif" for an animated gif/webp) while <c>Ext</c>/<c>Codec</c>
    /// describe the ARTIFACT — the two disagree for exactly the case the goon transfer lane cares
    /// about, a gif that became an mp4. <c>Codec</c> is the compressor's own label ("avc1",
    /// "webp", "jpeg", "orig") and reaches the page so a peer with no decoder for it is never
    /// offered the file; "" / "orig" means unknown, which the page treats as "offer it anyway".
    /// </summary>
    internal sealed record CacheItem(
        string SrcKey, string Rel, string Kind, string State, string Lane,
        string Sha, string Ext, string Codec, long Size, long Bytes, int W, int H, int DurMs,
        string? Fail, int Percent);

    /// <summary>One coalescable progress tick.</summary>
    internal sealed record JobProgress(string SrcKey, string Lane, double Percent);

    /// <summary>Everything the assets screen's header needs in one frame.</summary>
    internal sealed record CacheState(
        int Pool, int Ready, int Exempt, int Failed, int NotReady, int Queued, int Running,
        long UsedBytes, long CapBytes, bool Paused, string? PausedBy,
        long EtaMs, bool Hardware, string? PresetId, bool OverCapProtected, int PageQueued);

    /// <summary>
    /// A lane-B unit of work handed to the goon page. The page fetches <see cref="Url"/> from the
    /// existing <c>https://ccp.assets</c> host, encodes it with WebCodecs, and posts the bytes back
    /// quoting <see cref="JobId"/> — a job the host did not dispatch is refused, so the page can
    /// never write into the cache unprompted.
    /// </summary>
    internal sealed record PageJob(string JobId, string Rel, string Url, long Size, string Kind);

    /// <summary>
    /// App-lifetime owner of the compression queue: survives the goon page closing and is reusable
    /// from anywhere in the app (a future Remote Control "compress my library" button costs nothing).
    ///
    /// Two plain Task workers, NO Dispatcher anywhere — this has to run with the window minimized,
    /// during a match, and in a headless smoke test. At most one host-mt (video) and one page-wc
    /// (WebCodecs) job are in flight at a time; stills fill the other worker.
    ///
    /// The queue is DERIVED, never persisted: <see cref="AssetCompressionPlanner"/> ∩ the index,
    /// recomputed on demand. That is what makes a preset change a one-line call
    /// (<see cref="OnPresetChanged"/>) instead of a migration.
    ///
    /// Lane-B jobs are DISPATCHED here, not executed: <see cref="TryDequeuePageJob"/> /
    /// <see cref="CompletePageJob"/> are the whole surface the Phase-2 bridge needs; this file knows
    /// nothing about bridge messaging.
    /// </summary>
    internal sealed class TransferCompressionService
    {
        public static TransferCompressionService Instance { get; } = new();

        private const int WorkerCount = 2;
        private const int IdlePollMs = 400;
        /// <summary>A dispatched page job whose page went away comes back to the queue after this.</summary>
        private const int PageJobStaleMs = 180_000;
        /// <summary>Throughput samples per lane behind the ETA.</summary>
        private const int RateWindow = 8;
        /// <summary>Above this, the host-mt lane is certainly using the GPU encoder.</summary>
        private const double HardwareBytesPerSec = 8.0 * 1024 * 1024;

        /// <summary>Cold-start guesses, replaced by the first real sample of each lane.</summary>
        private static readonly Dictionary<string, double> DefaultRates = new(StringComparer.Ordinal)
        {
            [TransferLanes.HostMt] = 6.0 * 1024 * 1024,
            [TransferLanes.HostSkia] = 20.0 * 1024 * 1024,
            [TransferLanes.PageWc] = 3.0 * 1024 * 1024,
            [TransferLanes.Exempt] = 200.0 * 1024 * 1024,
        };

        private readonly object _lock = new();
        private readonly TransferCacheStore _store = TransferCacheStore.Instance;
        private readonly SemaphoreSlim _wake = new(0);
        private readonly SemaphoreSlim _planGate = new(1, 1);

        /// <summary>Everything the pool needs that the cache doesn't have yet (counts + ETA source).</summary>
        private List<CompressionJob> _plan = new();
        /// <summary>Actually scheduled work. Empty = idle, even when <see cref="_plan"/> is huge.</summary>
        private readonly List<CompressionJob> _queue = new();
        private readonly Dictionary<string, RunningJob> _running = new(StringComparer.Ordinal);
        private readonly Dictionary<string, DispatchedPageJob> _pageOut = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Queue<double>> _rates = new(StringComparer.Ordinal);

        private CancellationTokenSource? _shutdown;
        private Task[]? _workers;
        private bool _started;
        private bool _pausedByUser;
        private bool _pausedByMatch;
        private int _poolCount;
        private string? _presetId;

        /// <summary>Raised whenever the queue or the cache changed shape. Worker thread — marshal yourself.</summary>
        public event Action? Changed;
        /// <summary>Per-job progress. Fired at the transcoder's own rate; coalesce downstream (4 Hz).</summary>
        public event Action<JobProgress>? Progress;

        private sealed class RunningJob
        {
            public required CompressionJob Job { get; init; }
            public required CancellationTokenSource Cts { get; init; }
            public required DateTime StartedUtc { get; init; }
            public double Percent;
        }

        private sealed class DispatchedPageJob
        {
            public required CompressionJob Job { get; init; }
            public required DateTime DispatchedUtc { get; set; }
            public double Percent;
        }

        // ---- lifecycle ------------------------------------------------------------

        /// <summary>
        /// Create the cache folders, load the index and start the workers. Idempotent, cheap, and
        /// safe to call before any UI exists. MUST run before the ccp.cache virtual-host mapping.
        /// </summary>
        public void Initialize()
        {
            lock (_lock)
            {
                if (_started) return;
                _started = true;
                _shutdown = new CancellationTokenSource();
            }
            try
            {
                _store.EnsureRoot();
                _store.Load();
                _store.SweepTmp(TimeSpan.FromHours(6));
                _presetId = App.Settings?.Current?.CurrentAssetPresetId;
            }
            catch (Exception ex)
            {
                App.Logger?.Warning("TransferCompressionService.Initialize: {E}", ex.Message);
            }

            var token = _shutdown!.Token;
            var workers = new Task[WorkerCount];
            for (int i = 0; i < WorkerCount; i++)
                workers[i] = Task.Run(() => WorkerLoopAsync(token), CancellationToken.None);
            _workers = workers;
            App.Logger?.Information("TransferCompressionService: started ({W} workers, cap {CapGB:F1} GB)",
                WorkerCount, _store.CapBytes / 1073741824.0);
        }

        /// <summary>Cancel everything in flight and flush the index. Safe to call twice.</summary>
        public void Shutdown()
        {
            CancellationTokenSource? cts;
            lock (_lock)
            {
                if (!_started) return;
                _started = false;
                cts = _shutdown;
                _shutdown = null;
                foreach (var r in _running.Values) { try { r.Cts.Cancel(); } catch { } }
                _queue.Clear();
                _pageOut.Clear();
            }
            try { cts?.Cancel(); } catch { }
            try { _store.Save(); } catch { }
        }

        // ---- planning -------------------------------------------------------------

        /// <summary>
        /// Recompute what the pool still needs. Cheap for an already-compressed library (the index
        /// answers most files without touching them); the first pass on a fresh library probes every
        /// big video once. One pass at a time.
        /// </summary>
        public async Task RefreshAsync(bool retryFailed = false, CancellationToken ct = default)
        {
            if (!await _planGate.WaitAsync(0, ct).ConfigureAwait(false)) return;
            try
            {
                _store.Load();
                var plan = await AssetCompressionPlanner.PlanAsync(_store, retryFailed, ct).ConfigureAwait(false);
                lock (_lock)
                {
                    _plan = plan.Jobs;
                    _poolCount = plan.PoolCount;
                    // Drop scheduled work that the new plan says is already handled (a preset shrank,
                    // or another pass committed it), keeping queue order for what survives.
                    var live = new HashSet<string>(plan.Jobs.Select(j => j.SrcKey), StringComparer.Ordinal);
                    _queue.RemoveAll(j =>
                    {
                        if (live.Contains(j.SrcKey) || _running.ContainsKey(j.SrcKey) || _pageOut.ContainsKey(j.SrcKey))
                            return false;
                        _store.SetBusy(j.SrcKey, false);
                        return true;
                    });
                }
                App.Logger?.Information(
                    "TransferCompressionService: plan = {N} to do / {Done} done / {Failed} failed of {Pool} pool files",
                    plan.Jobs.Count, plan.AlreadyDone, plan.PreviouslyFailed, plan.PoolCount);
                RaiseChanged();
                if (App.Settings?.Current?.TransferCacheAutoCompress == true) CompressAll();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                App.Logger?.Warning("TransferCompressionService.RefreshAsync: {E}", ex.Message);
            }
            finally { _planGate.Release(); }
        }

        /// <summary>
        /// The active preset changed. Recompute in the background and tell subscribers, so the UI can
        /// offer "your preset changed - N assets need compressing". Never blocks the caller.
        /// </summary>
        public void OnPresetChanged(string? presetId)
        {
            _presetId = presetId;
            try { if (App.Settings?.Current != null) App.Settings.Current.LastSeenAssetPresetId = presetId; }
            catch { }
            _ = Task.Run(async () =>
            {
                try { await RefreshAsync().ConfigureAwait(false); }
                catch (Exception ex) { App.Logger?.Debug("TransferCompressionService.OnPresetChanged: {E}", ex.Message); }
            });
        }

        // ---- queue control --------------------------------------------------------

        /// <summary>Schedule the whole outstanding plan. Refreshes first if nothing has been planned yet.</summary>
        public void CompressAll(bool retryFailed = false)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    bool needPlan;
                    lock (_lock) needPlan = _plan.Count == 0 && _poolCount == 0;
                    if (needPlan || retryFailed) await RefreshAsync(retryFailed).ConfigureAwait(false);
                    lock (_lock)
                    {
                        var have = new HashSet<string>(_queue.Select(j => j.SrcKey), StringComparer.Ordinal);
                        foreach (var j in _plan)
                        {
                            if (!have.Add(j.SrcKey) || _running.ContainsKey(j.SrcKey) || _pageOut.ContainsKey(j.SrcKey))
                                continue;
                            _queue.Add(j);
                            // Queued counts as busy: a retried failed entry still has an index row,
                            // and the LRU must not evict something the queue is about to rewrite.
                            _store.SetBusy(j.SrcKey, true);
                        }
                    }
                    Kick();
                    RaiseChanged();
                }
                catch (Exception ex) { App.Logger?.Warning("TransferCompressionService.CompressAll: {E}", ex.Message); }
            });
        }

        /// <summary>Jump one asset to the head of the queue (the per-tile "compress this" button).</summary>
        public void CompressOne(string srcKey)
        {
            lock (_lock)
            {
                if (_running.ContainsKey(srcKey) || _pageOut.ContainsKey(srcKey)) return;
                if (_queue.Any(j => j.SrcKey == srcKey)) return;
                var job = _plan.FirstOrDefault(j => j.SrcKey == srcKey);
                if (job == null) return;
                _queue.Insert(0, job);
                _store.SetBusy(srcKey, true);
            }
            Kick();
            RaiseChanged();
        }

        /// <summary>Cancel one job wherever it is: queued, running, or out with the page.</summary>
        public void Cancel(string srcKey)
        {
            RunningJob? running = null;
            lock (_lock)
            {
                if (_queue.RemoveAll(j => j.SrcKey == srcKey) > 0) _store.SetBusy(srcKey, false);
                if (_pageOut.Remove(srcKey)) _store.SetBusy(srcKey, false);
                if (_running.TryGetValue(srcKey, out var r)) running = r;
            }
            if (running != null) { try { running.Cts.Cancel(); } catch { } }
            RaiseChanged();
        }

        /// <summary>Empty the queue and abort everything in flight. The plan (what COULD run) survives.</summary>
        public void CancelAll()
        {
            List<RunningJob> running;
            lock (_lock)
            {
                foreach (var j in _queue) _store.SetBusy(j.SrcKey, false);
                _queue.Clear();
                foreach (var k in _pageOut.Keys.ToList()) _store.SetBusy(k, false);
                _pageOut.Clear();
                running = _running.Values.ToList();
            }
            foreach (var r in running) { try { r.Cts.Cancel(); } catch { } }
            RaiseChanged();
        }

        /// <summary>User pressed pause. Independent of the match pause — either one holds the queue.</summary>
        public void PauseUser() { _pausedByUser = true; RaiseChanged(); }
        public void ResumeUser() { _pausedByUser = false; Kick(); RaiseChanged(); }

        /// <summary>A duel started. Encoding during a match steals the frames the match needs.</summary>
        public void PauseForMatch() { _pausedByMatch = true; RaiseChanged(); }
        public void ResumeAfterMatch() { _pausedByMatch = false; Kick(); RaiseChanged(); }

        public bool IsPaused => _pausedByUser || _pausedByMatch;
        /// <summary>"user" wins the label when both hold — it's the one with a button attached.</summary>
        public string? PausedBy => _pausedByUser ? "user" : _pausedByMatch ? "match" : null;

        /// <summary>Persist a new disk budget and immediately enforce it.</summary>
        public void SetCapBytes(long bytes)
        {
            try
            {
                var clamped = Math.Clamp(bytes, TransferCacheStore.MinCapBytes, TransferCacheStore.MaxCapBytes);
                if (App.Settings?.Current != null) App.Settings.Current.TransferCacheCapBytes = clamped;
                _store.EnforceCap();
            }
            catch (Exception ex) { App.Logger?.Warning("TransferCompressionService.SetCapBytes: {E}", ex.Message); }
            RaiseChanged();
        }

        // ---- deletes --------------------------------------------------------------

        public void DeleteOne(string srcKey)
        {
            Cancel(srcKey);
            _store.DeleteOne(srcKey);
            _ = Task.Run(async () => { try { await RefreshAsync().ConfigureAwait(false); } catch { } });
        }

        public void DeleteAll()
        {
            CancelAll();
            _store.DeleteAll();
            AssetCompressionPlanner.ClearProbeCache();
            _ = Task.Run(async () => { try { await RefreshAsync().ConfigureAwait(false); } catch { } });
        }

        // ---- reads ----------------------------------------------------------------

        public CacheState GetState()
        {
            var (total, ready, exempt, failed) = _store.Counts();
            lock (_lock)
            {
                return new CacheState(
                    Pool: _poolCount, Ready: ready, Exempt: exempt, Failed: failed,
                    NotReady: _plan.Count, Queued: _queue.Count, Running: _running.Count,
                    UsedBytes: _store.UsedBytes, CapBytes: _store.CapBytes,
                    Paused: IsPaused, PausedBy: PausedBy,
                    EtaMs: EstimateEtaMsLocked(), Hardware: IsHardware,
                    PresetId: _presetId, OverCapProtected: _store.OverCapProtected,
                    PageQueued: _pageOut.Count);
            }
        }

        /// <summary>
        /// Every asset the screen can show: what the index knows, plus what is still outstanding
        /// (as "not ready"), with live percentages folded in.
        /// </summary>
        public IReadOnlyList<CacheItem> ListItems()
        {
            var items = new List<CacheItem>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var (key, e) in _store.Snapshot())
            {
                seen.Add(key);
                items.Add(new CacheItem(key, e.Rel, e.Kind, e.State, e.Lane, e.Sha, e.Ext, e.Codec,
                    e.Size, e.Bytes, e.W, e.H, e.DurMs, e.Fail, 100));
            }
            lock (_lock)
            {
                foreach (var j in _plan)
                {
                    if (!seen.Add(j.SrcKey)) continue;
                    int pct = 0;
                    string state = "pending";
                    if (_running.TryGetValue(j.SrcKey, out var r)) { pct = (int)r.Percent; state = "running"; }
                    else if (_pageOut.TryGetValue(j.SrcKey, out var d)) { pct = (int)d.Percent; state = "running"; }
                    else if (_queue.Any(q => q.SrcKey == j.SrcKey)) state = "queued";
                    // Still outstanding: there is no artifact yet, so no ext and no codec.
                    items.Add(new CacheItem(j.SrcKey, j.Rel, j.Kind, state, j.Lane, "", "", "",
                        j.Size, 0, j.SrcW, j.SrcH, j.DurMs, null, pct));
                }
            }
            return items;
        }

        /// <summary>Last host-mt throughput says whether the GPU encoder engaged — honest ETAs need it.</summary>
        public bool IsHardware
        {
            get
            {
                lock (_lock)
                    return _rates.TryGetValue(TransferLanes.HostMt, out var q) && q.Count > 0
                        && q.Last() > HardwareBytesPerSec;
            }
        }

        // ---- lane-B (page WebCodecs) surface --------------------------------------

        /// <summary>Jobs waiting for the page right now. Phase 2 turns these into encode-requests.</summary>
        public IReadOnlyList<PageJob> PendingPageJobs
        {
            get { lock (_lock) return _pageOut.Values.Select(d => ToPageJob(d.Job)).ToList(); }
        }

        /// <summary>
        /// Take the next lane-B job for the page, dispatching one from the queue if none is out.
        /// At most one page job is ever in flight — the page shares a process with the duel.
        /// </summary>
        public bool TryDequeuePageJob(out PageJob job)
        {
            lock (_lock)
            {
                ExpireStalePageJobsLocked();
                if (_pageOut.Count == 0 && !IsPaused)
                {
                    var next = _queue.FirstOrDefault(j => j.Lane == TransferLanes.PageWc);
                    if (next != null)
                    {
                        _queue.Remove(next);
                        _pageOut[next.SrcKey] = new DispatchedPageJob { Job = next, DispatchedUtc = DateTime.UtcNow };
                        _store.SetBusy(next.SrcKey, true);
                    }
                }
                var outJob = _pageOut.Values.FirstOrDefault();
                if (outJob == null) { job = null!; return false; }
                job = ToPageJob(outJob.Job);
                return true;
            }
        }

        /// <summary>Progress report from the page for a dispatched job (0-100).</summary>
        public void ReportPageProgress(string jobId, double percent)
        {
            CompressionJob? job = null;
            lock (_lock)
            {
                if (_pageOut.TryGetValue(jobId, out var d))
                {
                    d.Percent = Math.Clamp(percent, 0, 100);
                    d.DispatchedUtc = DateTime.UtcNow;   // still alive
                    job = d.Job;
                }
            }
            if (job != null) RaiseProgress(new JobProgress(jobId, job.Lane, Math.Clamp(percent, 0, 100)));
        }

        /// <summary>
        /// The page finished (or gave up on) a dispatched lane-B job. <paramref name="bytes"/> is the
        /// encoded artifact; the host hashes it, so the page's own claims about it are irrelevant.
        /// A jobId the host never dispatched is ignored - that is the whole write guard.
        /// </summary>
        public void CompletePageJob(string jobId, bool ok, byte[]? bytes, string? ext,
            int w, int h, int durMs, string? failReason, byte[]? previewBytes = null)
        {
            CompressionJob? job;
            lock (_lock)
            {
                if (!_pageOut.Remove(jobId, out var d)) { job = null; }
                else job = d.Job;
            }
            if (job == null)
            {
                App.Logger?.Warning("TransferCompressionService: page completed unknown job {Id} - ignored", jobId);
                return;
            }

            try
            {
                if (!ok || bytes == null || bytes.Length == 0)
                {
                    RecordFailure(job, failReason ?? TransferFailReasons.EncodeFailed);
                    return;
                }
                var safeExt = (ext ?? "mp4").TrimStart('.').ToLowerInvariant();
                if (safeExt.Length is 0 or > 5 || !safeExt.All(char.IsLetterOrDigit)) safeExt = "mp4";
                var tmp = TempPathFor(job, safeExt);
                Directory.CreateDirectory(_store.TmpDir);
                File.WriteAllBytes(tmp, bytes);
                Commit(job, tmp, safeExt, "avc1", w, h, durMs, previewSource: null,
                    previewBytes: previewBytes);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning("TransferCompressionService.CompletePageJob({Id}): {E}", jobId, ex.Message);
                RecordFailure(job, TransferFailReasons.IoFailed);
            }
            finally
            {
                _store.SetBusy(job.SrcKey, false);
                Kick();
                RaiseChanged();
            }
        }

        private static PageJob ToPageJob(CompressionJob j) => new(
            j.SrcKey, j.Rel,
            "https://ccp.assets/" + string.Join('/', j.Rel.Split('/').Select(Uri.EscapeDataString)),
            j.Size, j.Kind);

        private void ExpireStalePageJobsLocked()
        {
            if (_pageOut.Count == 0) return;
            var now = DateTime.UtcNow;
            foreach (var (key, d) in _pageOut.ToList())
            {
                if ((now - d.DispatchedUtc).TotalMilliseconds < PageJobStaleMs) continue;
                _pageOut.Remove(key);
                _store.SetBusy(key, false);
                _queue.Add(d.Job);   // the page died mid-encode; try again later
                App.Logger?.Information("TransferCompressionService: page job {Id} went stale, requeued", key);
            }
        }

        // ---- workers --------------------------------------------------------------

        private void Kick()
        {
            try { _wake.Release(); } catch (SemaphoreFullException) { }
        }

        private async Task WorkerLoopAsync(CancellationToken shutdown)
        {
            while (!shutdown.IsCancellationRequested)
            {
                CompressionJob? job = null;
                try
                {
                    job = TakeNextJob();
                    if (job == null)
                    {
                        await _wake.WaitAsync(IdlePollMs, shutdown).ConfigureAwait(false);
                        continue;
                    }
                    await RunJobAsync(job, shutdown).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (shutdown.IsCancellationRequested) { return; }
                catch (Exception ex)
                {
                    App.Logger?.Warning("TransferCompressionService worker: {E}", ex.Message);
                    if (job != null) { try { RecordFailure(job, TransferFailReasons.IoFailed); } catch { } }
                    try { await Task.Delay(250, shutdown).ConfigureAwait(false); } catch { return; }
                }
            }
        }

        /// <summary>
        /// Next runnable job under the concurrency rules: never while paused, at most one host-mt in
        /// flight (the GPU encoder serializes anyway and two of them starve the app), page-wc jobs
        /// are never taken here — they belong to the page.
        /// </summary>
        private CompressionJob? TakeNextJob()
        {
            lock (_lock)
            {
                if (IsPaused || _queue.Count == 0) return null;
                bool mtBusy = _running.Values.Any(r => r.Job.Lane == TransferLanes.HostMt);
                for (int i = 0; i < _queue.Count; i++)
                {
                    var j = _queue[i];
                    if (j.Lane == TransferLanes.PageWc) continue;
                    if (j.Lane == TransferLanes.HostMt && mtBusy) continue;
                    if (_running.ContainsKey(j.SrcKey)) continue;
                    _queue.RemoveAt(i);
                    _running[j.SrcKey] = new RunningJob
                    {
                        Job = j,
                        Cts = CancellationTokenSource.CreateLinkedTokenSource(_shutdown?.Token ?? default),
                        StartedUtc = DateTime.UtcNow,
                    };
                    _store.SetBusy(j.SrcKey, true);
                    return j;
                }
                return null;
            }
        }

        private async Task RunJobAsync(CompressionJob job, CancellationToken shutdown)
        {
            CancellationToken ct;
            lock (_lock)
            {
                if (!_running.TryGetValue(job.SrcKey, out var r)) return;
                ct = r.Cts.Token;
            }
            var started = DateTime.UtcNow;
            try
            {
                if (!File.Exists(job.FullPath)) { RecordFailure(job, TransferFailReasons.Missing); return; }

                switch (job.Lane)
                {
                    case TransferLanes.Exempt: await RunExemptAsync(job, ct).ConfigureAwait(false); break;
                    case TransferLanes.HostSkia: RunStill(job, ct); break;
                    case TransferLanes.HostMt: await RunVideoAsync(job, ct).ConfigureAwait(false); break;
                    default:
                        App.Logger?.Warning("TransferCompressionService: job {Key} has lane {Lane}?", job.SrcKey, job.Lane);
                        break;
                }
                RecordRate(job, started);
            }
            catch (OperationCanceledException)
            {
                CleanTemp(job);
                App.Logger?.Debug("TransferCompressionService: {Rel} cancelled", job.Rel);
            }
            catch (TranscodeUnsupportedException ex)
            {
                CleanTemp(job);
                RecordFailure(job, ex.Reason == TransferFailReasons.NoDecoder ? TransferFailReasons.NoDecoder : TransferFailReasons.EncodeFailed);
            }
            catch (Exception ex)
            {
                CleanTemp(job);
                App.Logger?.Warning("TransferCompressionService: {Rel} failed: {E}", job.Rel, ex.Message);
                RecordFailure(job, TransferFailReasons.EncodeFailed);
            }
            finally
            {
                lock (_lock) _running.Remove(job.SrcKey);
                _store.SetBusy(job.SrcKey, false);
                Kick();
                RaiseChanged();
            }
        }

        /// <summary>Small (or already-small) files: the original is the payload, so all we owe it is a
        /// hash — plus a micro-preview when it's a big-but-efficient video.</summary>
        private async Task RunExemptAsync(CompressionJob job, CancellationToken ct)
        {
            var sha = TransferCacheHash.Sha256File(job.FullPath);
            if (sha == null) { RecordFailure(job, TransferFailReasons.IoFailed); return; }

            long prevBytes = 0;
            if (job.WantsPreview)
            {
                var probe = job.SrcW > 0
                    ? new VideoTranscodeLane.VideoProbe(job.SrcW, job.SrcH, job.DurMs, job.SrcBitrate)
                    : await VideoTranscodeLane.ProbeAsync(job.FullPath, ct).ConfigureAwait(false);
                prevBytes = await MakePreviewAsync(job.FullPath, sha, probe, ct).ConfigureAwait(false);
            }

            var ext = Path.GetExtension(job.FullPath).TrimStart('.').ToLowerInvariant();
            _store.Upsert(job.SrcKey, new TransferCacheEntry
            {
                Rel = job.Rel,
                Size = job.Size,
                Mtime = job.MtimeTicks,
                Kind = job.Kind,
                State = TransferStates.Exempt,
                Lane = TransferLanes.Exempt,
                Sha = sha,
                Ext = ext,
                Bytes = job.Size,
                PrevBytes = prevBytes,
                W = job.SrcW,
                H = job.SrcH,
                DurMs = job.DurMs,
                Codec = "orig",
            });
        }

        private void RunStill(CompressionJob job, CancellationToken ct)
        {
            var tmp = TempPathFor(job, "still");
            var result = StillCompressLane.Compress(job.FullPath, tmp, ct);
            if (result == null) { RecordFailure(job, TransferFailReasons.EncodeFailed); return; }
            var final = Path.ChangeExtension(tmp, "." + result.Ext);
            try { if (final != tmp) File.Move(tmp, final, overwrite: true); } catch { final = tmp; }
            Commit(job, final, result.Ext, result.Codec, result.Width, result.Height, 0, previewSource: null);
        }

        private async Task RunVideoAsync(CompressionJob job, CancellationToken ct)
        {
            var probe = job.SrcW > 0 || job.SrcBitrate > 0
                ? new VideoTranscodeLane.VideoProbe(job.SrcW, job.SrcH, job.DurMs, job.SrcBitrate)
                : await VideoTranscodeLane.ProbeAsync(job.FullPath, ct).ConfigureAwait(false);

            var tmp = TempPathFor(job, "mp4");
            var result = await VideoTranscodeLane.TranscodeAsync(job.FullPath, tmp, probe,
                pct =>
                {
                    lock (_lock) { if (_running.TryGetValue(job.SrcKey, out var r)) r.Percent = pct; }
                    RaiseProgress(new JobProgress(job.SrcKey, job.Lane, pct));
                }, ct).ConfigureAwait(false);

            Commit(job, result.TmpPath, result.Ext, result.Codec, result.Width, result.Height, result.DurMs,
                previewSource: job.WantsPreview ? result.TmpPath : null, previewProbe: probe, ct: ct);
        }

        // ---- commit ---------------------------------------------------------------

        /// <summary>
        /// The single path by which bytes become a cache entry: hash them HOST-side (that hash is the
        /// wire identity - never a page's or a peer's claim), move into art/&lt;sha&gt;.&lt;ext&gt;,
        /// mint the preview, write the index, then enforce the cap.
        /// </summary>
        private void Commit(CompressionJob job, string tmpPath, string ext, string codec,
            int w, int h, int durMs, string? previewSource,
            VideoTranscodeLane.VideoProbe? previewProbe = null, CancellationToken ct = default,
            byte[]? previewBytes = null)
        {
            var sha = TransferCacheHash.Sha256File(tmpPath);
            if (sha == null) { RecordFailure(job, TransferFailReasons.IoFailed); return; }

            var dest = _store.ArtifactPathForSha(sha, ext);
            long bytes;
            try
            {
                _store.EnsureRoot();
                if (File.Exists(dest))
                {
                    // Two source files with identical compressed bytes share one artifact.
                    try { File.Delete(tmpPath); } catch { }
                }
                else
                {
                    File.Move(tmpPath, dest, overwrite: true);
                }
                bytes = new FileInfo(dest).Length;
            }
            catch (Exception ex)
            {
                App.Logger?.Warning("TransferCompressionService.Commit({Rel}): {E}", job.Rel, ex.Message);
                RecordFailure(job, TransferFailReasons.IoFailed);
                return;
            }

            long prevBytes = 0;
            if (previewBytes is { Length: > 0 })
            {
                // Lane B: the page encoded the micro-preview itself (same filmstrip, no seek cost).
                // Cosmetic on failure, exactly like MakePreviewAsync below.
                try
                {
                    var pdest = _store.PreviewPathForSha(sha);
                    File.WriteAllBytes(pdest, previewBytes);
                    prevBytes = previewBytes.Length;
                }
                catch (Exception ex)
                {
                    App.Logger?.Debug("TransferCompressionService: page preview for {Sha} failed: {E}",
                        sha, ex.Message);
                }
            }
            else if (previewSource != null)
            {
                // The artifact moved; preview from where it actually is now.
                var src = File.Exists(previewSource) ? previewSource : dest;
                prevBytes = MakePreviewAsync(src, sha, previewProbe, ct).GetAwaiter().GetResult();
            }

            _store.Upsert(job.SrcKey, new TransferCacheEntry
            {
                Rel = job.Rel,
                Size = job.Size,
                Mtime = job.MtimeTicks,
                Kind = job.Kind,
                State = TransferStates.Ready,
                Lane = job.Lane,
                Sha = sha,
                Ext = ext,
                Bytes = bytes,
                PrevBytes = prevBytes,
                W = w,
                H = h,
                DurMs = durMs,
                Codec = codec,
            });
            App.Logger?.Information("TransferCompressionService: {Rel} {SrcKB} KB -> {OutKB} KB via {Lane}",
                job.Rel, job.Size / 1024, bytes / 1024, job.Lane);
            _store.EnforceCap();
        }

        private async Task<long> MakePreviewAsync(string src, string sha,
            VideoTranscodeLane.VideoProbe? probe, CancellationToken ct)
        {
            var tmp = Path.Combine(_store.TmpDir, sha + ".prv.part");
            try
            {
                await VideoTranscodeLane.MakePreviewAsync(src, tmp, probe, ct).ConfigureAwait(false);
                var dest = _store.PreviewPathForSha(sha);
                _store.EnsureRoot();
                File.Move(tmp, dest, overwrite: true);
                return new FileInfo(dest).Length;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                // A missing preview is cosmetic - the artifact itself is already committed.
                App.Logger?.Debug("TransferCompressionService: preview for {Sha} failed: {E}", sha, ex.Message);
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
                return 0;
            }
        }

        /// <summary>Write a failed entry so the planner stops re-queuing it (dropped after 14 days).</summary>
        private void RecordFailure(CompressionJob job, string reason)
        {
            _store.Upsert(job.SrcKey, new TransferCacheEntry
            {
                Rel = job.Rel,
                Size = job.Size,
                Mtime = job.MtimeTicks,
                Kind = job.Kind,
                State = TransferStates.Failed,
                Lane = job.Lane,
                Sha = "",
                Ext = "",
                Fail = reason,
            });
            App.Logger?.Information("TransferCompressionService: {Rel} failed ({Reason})", job.Rel, reason);
        }

        private string TempPathFor(CompressionJob job, string ext) =>
            Path.Combine(_store.TmpDir, job.JobId + "." + ext + ".part");

        private void CleanTemp(CompressionJob job)
        {
            try
            {
                if (!Directory.Exists(_store.TmpDir)) return;
                foreach (var f in Directory.EnumerateFiles(_store.TmpDir, job.JobId + ".*"))
                    try { File.Delete(f); } catch { }
            }
            catch { }
        }

        // ---- throughput / ETA -----------------------------------------------------

        private void RecordRate(CompressionJob job, DateTime started)
        {
            var secs = (DateTime.UtcNow - started).TotalSeconds;
            if (secs <= 0.05 || job.Size <= 0) return;
            var rate = job.Size / secs;
            lock (_lock)
            {
                if (!_rates.TryGetValue(job.Lane, out var q)) _rates[job.Lane] = q = new Queue<double>();
                q.Enqueue(rate);
                while (q.Count > RateWindow) q.Dequeue();
            }
        }

        /// <summary>EWMA (newest weighted 2x) over the last 8 completions of a lane; the cold-start
        /// default until a lane has run once. Caller holds <see cref="_lock"/>.</summary>
        private double RateForLocked(string lane)
        {
            if (_rates.TryGetValue(lane, out var q) && q.Count > 0)
            {
                double acc = 0, weight = 0, w = 1;
                foreach (var v in q) { acc += v * w; weight += w; w *= 2; }
                if (weight > 0 && acc > 0) return acc / weight;
            }
            return DefaultRates.TryGetValue(lane, out var d) ? d : 5.0 * 1024 * 1024;
        }

        private long EstimateEtaMsLocked()
        {
            double ms = 0;
            foreach (var group in _queue.Concat(_running.Values.Select(r => r.Job))
                                        .Concat(_pageOut.Values.Select(d => d.Job))
                                        .GroupBy(j => j.Lane))
            {
                long bytes = group.Sum(j => j.Size);
                var rate = RateForLocked(group.Key);
                if (rate > 0) ms = Math.Max(ms, bytes / rate * 1000.0);   // lanes run in parallel
            }
            return (long)Math.Min(ms, int.MaxValue);
        }

        // ---- events ---------------------------------------------------------------

        private void RaiseChanged()
        {
            try { Changed?.Invoke(); }
            catch (Exception ex) { App.Logger?.Debug("TransferCompressionService.Changed subscriber threw: {E}", ex.Message); }
        }

        private void RaiseProgress(JobProgress p)
        {
            try { Progress?.Invoke(p); }
            catch (Exception ex) { App.Logger?.Debug("TransferCompressionService.Progress subscriber threw: {E}", ex.Message); }
        }

#if DEBUG
        /// <summary>
        /// Headless smoke of the pure logic — no disk, no encoders, no page. Callable from
        /// GoonTestPanel once Phase 2 gives it a button. Returns a human-readable report;
        /// every line starts OK or FAIL.
        /// </summary>
        public static string DebugSelfTest()
        {
            var lines = new List<string>();
            void Check(string name, bool ok, string detail = "") =>
                lines.Add((ok ? "OK   " : "FAIL ") + name + (detail.Length > 0 ? " - " + detail : ""));

            // srcKey: stable, path-case-insensitive, and sensitive to size AND mtime.
            var a = TransferCacheHash.ComputeSrcKey("images/a.png", 100, 555);
            var b = TransferCacheHash.ComputeSrcKey("Images\\A.PNG", 100, 555);
            var c = TransferCacheHash.ComputeSrcKey("images/a.png", 101, 555);
            var d = TransferCacheHash.ComputeSrcKey("images/a.png", 100, 556);
            Check("srcKey deterministic", a == TransferCacheHash.ComputeSrcKey("images/a.png", 100, 555));
            Check("srcKey path-normalized", a == b);
            Check("srcKey size-sensitive", a != c);
            Check("srcKey mtime-sensitive", a != d);
            Check("srcKey is sha hex", TransferCacheHash.IsShaHex(a), a);

            // LRU: coldest unprotected first, pinned/busy never, protected only as a last resort.
            var now = DateTime.UtcNow;
            var cands = new List<TransferCacheStore.EvictCandidate>
            {
                new("cold",      100, now.AddDays(-9), now.AddDays(-9), false, false, false, false),
                new("warm",      100, now.AddDays(-1), now.AddDays(-1), false, false, false, false),
                new("pinned",    100, now.AddDays(-9), now.AddDays(-9), false, true,  false, false),
                new("busy",      100, now.AddDays(-9), now.AddDays(-9), false, false, true,  false),
                new("protected", 100, now.AddDays(-9), now.AddDays(-9), false, false, false, true),
                new("oldfail",     0, now.AddDays(-30), now.AddDays(-30), true, false, false, false),
            };
            var drop = TransferCacheStore.PlanEvictions(cands, 500, 400, now, out var overProt);
            Check("stale failure dropped", drop.Contains("oldfail"));
            Check("coldest unprotected first", drop.Contains("cold"));
            Check("pinned never evicted", !drop.Contains("pinned"));
            Check("busy never evicted", !drop.Contains("busy"));
            Check("protected spared while cap fits", !drop.Contains("protected") && !overProt);

            drop = TransferCacheStore.PlanEvictions(cands, 500, 50, now, out overProt);
            Check("protected evicted only under pressure", drop.Contains("protected") && overProt);
            Check("pinned still safe under pressure", !drop.Contains("pinned") && !drop.Contains("busy"));

            // Encoder maths.
            Check("bitrate clamp low", VideoTranscodeLane.TargetBitrateFor(100_000) == 900_000);
            Check("bitrate 85pct", VideoTranscodeLane.TargetBitrateFor(2_000_000) == 1_700_000);
            Check("bitrate clamp high", VideoTranscodeLane.TargetBitrateFor(50_000_000) == 1_800_000);
            Check("bitrate unknown", VideoTranscodeLane.TargetBitrateFor(0) == 1_800_000);
            var (fw, fh) = VideoTranscodeLane.FitEven(1920, 1080, 1280, 720);
            Check("fit 1080p -> 720p", fw == 1280 && fh == 720, fw + "x" + fh);
            (fw, fh) = VideoTranscodeLane.FitEven(641, 481, 1280, 720);
            Check("never upscales, even dims", fw == 640 && fh == 480, fw + "x" + fh);
            var (sw, sh) = StillCompressLane.ScaledSize(4000, 3000, 1920);
            Check("still long edge 1920", sw == 1920 && sh == 1440, sw + "x" + sh);
            (sw, sh) = StillCompressLane.ScaledSize(800, 600, 1920);
            Check("still small untouched", sw == 800 && sh == 600);

            int fails = lines.Count(l => l.StartsWith("FAIL"));
            lines.Add(fails == 0 ? $"ALL {lines.Count} CHECKS PASSED" : $"{fails} CHECK(S) FAILED");
            return string.Join(Environment.NewLine, lines);
        }
#endif
    }
}
