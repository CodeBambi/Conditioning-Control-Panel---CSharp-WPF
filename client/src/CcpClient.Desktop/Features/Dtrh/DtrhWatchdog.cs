namespace CcpClient.Desktop.Features.Dtrh;

/// <summary>
/// SP-027 slice b5: the DTRH watchdog + relaunch-once recovery state machine (contract-named).
/// WPF archaeology (DtrhHostService.cs): heartbeat watch 5s cadence (:823), silence limits
/// 10s mid-run / 20s hub (:833-836), guarded on page-ready and not-exiting (:830), relaunch
/// once per session then give up cleanly (:858-876) — never a restart loop.
///
/// W17 (webview-dtrh-spike.md): killing the WebView2 renderer processes leaves a BLACK
/// surface whose bridge keeps beating ~28s before going silent, and AdapterDestroyed NEVER
/// fires. The detection stack is therefore TWO signals: the native ProcessFailed signal
/// (DtrhProcessFailed — immediate, Windows-embedded only) and this watchdog's heartbeat
/// silence (the net on both platforms — it also catches a wedged JS main thread, the case
/// ProcessFailed never sees). On the watchdog-only path (Linux dialog) detection lands at
/// the black-but-beating window (~28s) PLUS the silence threshold — a named limit, never
/// claimed as fast.
///
/// Recovery-episode latch (pre-approach consult CORRECTION 1 — a latent WPF bug, not
/// ported): one kill burst fires MULTIPLE ProcessFailed events; without a latch the second
/// event would consume the relaunch AND tear down the replacement. While a relaunch is
/// pending (until the new instance reports ready) every further failure signal is dropped.
///
/// Pure and thread-agnostic: the caller (host window) drives it from the UI thread.
/// </summary>
public sealed class DtrhWatchdog
{
    /// <summary>Heartbeat watch cadence (WPF :823).</summary>
    public static readonly TimeSpan WatchInterval = TimeSpan.FromSeconds(5);

    /// <summary>Silence limit mid-run (WPF :834).</summary>
    public static readonly TimeSpan RunSilenceLimit = TimeSpan.FromSeconds(10);

    /// <summary>Silence limit in the idling Warren hub (WPF :834).</summary>
    public static readonly TimeSpan HubSilenceLimit = TimeSpan.FromSeconds(20);

    /// <summary>Bounded exit-done wait before the watchdog-forced close (WPF :880,
    /// "watchdog-force after 1200ms").</summary>
    public static readonly TimeSpan ExitDoneWait = TimeSpan.FromMilliseconds(1200);

    /// <summary>Recovery outcome for one failure signal.</summary>
    public abstract record DtrhRecoveryOutcome
    {
        /// <summary>Tear the current instance down and relaunch ONCE (generation latched
        /// until the new instance is ready).</summary>
        public sealed record Relaunch(string Reason, int Generation) : DtrhRecoveryOutcome;

        /// <summary>The one relaunch is already spent — give up cleanly (honest close,
        /// never a restart loop; WPF :863 "giving up").</summary>
        public sealed record Exhausted(string Reason) : DtrhRecoveryOutcome;
    }

    private DateTimeOffset _lastHeartbeatUtc;
    private bool _live;                 // page ready (heartbeats only flow after boot)
    private bool _exiting;              // graceful exit in progress — failures must not relaunch
    private bool _relaunchSpent;        // WPF _relaunchedOnce (:39)
    private int _pendingGeneration;     // 0 = no recovery episode in flight
    private int _generationCounter;

    /// <summary>Whether the page is live (ready reported, not since dead). The host
    /// window's graceful-exit branch keys off this (WPF _host.IsReady :154).</summary>
    public bool IsLive => _live;

    /// <summary>The recovery generation of the in-flight relaunch (0 = none). Diagnostics.</summary>
    public int PendingGeneration => _pendingGeneration;

    /// <summary>Whether the one relaunch has been consumed this session. Diagnostics.</summary>
    public bool RelaunchSpent => _relaunchSpent;

    /// <summary>The page reported ready (initial boot or the relaunched instance): arm the
    /// watch, clear the recovery-episode latch, stamp the baseline (WPF :176, :821).</summary>
    public void MarkLive(DateTimeOffset utcNow)
    {
        _pendingGeneration = 0;
        _live = true;
        _lastHeartbeatUtc = utcNow;
    }

    /// <summary>heartbeat / pong stamp (WPF :277/:319 — pong counts as a beat).</summary>
    public void Heartbeat(DateTimeOffset utcNow)
    {
        if (_live)
        {
            _lastHeartbeatUtc = utcNow;
        }
    }

    /// <summary>The graceful-exit flow started: no failure signal may relaunch from here
    /// (a process dying mid-wind-down is expected, not a recovery case).</summary>
    public void BeginExit() => _exiting = true;

    /// <summary>The instance is gone without a live successor (window closed for recovery
    /// or teardown): stop ticking until MarkLive. Does NOT spend the relaunch.</summary>
    public void MarkDead() => _live = false;

    /// <summary>One watch tick (5s cadence, WPF :824-840). Returns null when healthy.</summary>
    public DtrhRecoveryOutcome? Tick(DateTimeOffset utcNow, bool runActive)
    {
        if (!_live || _exiting)
        {
            return null;
        }

        var silent = utcNow - _lastHeartbeatUtc;
        var limit = runActive ? RunSilenceLimit : HubSilenceLimit;
        return silent > limit ? Recover($"heartbeat-silent ({(int)silent.TotalSeconds}s > {(int)limit.TotalSeconds}s, {(runActive ? "mid-run" : "hub")})") : null;
    }

    /// <summary>Native process-failure signal (WPF :850-852 — OnProcessFailed(kind)).
    /// kind is the COREWEBVIEW2_PROCESS_FAILED_KIND value, logged by the caller.</summary>
    public DtrhRecoveryOutcome? OnProcessFailed(int kind) => Recover($"process-failed:{KindName(kind)}");

    /// <summary>COREWEBVIEW2_PROCESS_FAILED_KIND names (WebView2.idl 1.0.2535.41; unknown
    /// values stay numeric — forward tolerance).</summary>
    public static string KindName(int kind) => kind switch
    {
        0 => "BrowserProcessExited",
        1 => "RenderProcessExited",
        2 => "RenderProcessUnresponsive",
        3 => "FrameRenderProcessExited",
        4 => "UtilityProcessExited",
        5 => "SandboxHelperProcessExited",
        6 => "GpuProcessExited",
        7 => "PpapiPluginProcessExited",
        8 => "PpapiBrokerProcessExited",
        9 => "UnknownProcessExited",
        _ => $"kind-{kind}",
    };

    private DtrhRecoveryOutcome? Recover(string reason)
    {
        // Guards first (WPF :830 parity + consult CORRECTION 1 latch): never recover a
        // not-live/exiting instance; while a relaunch is in flight, every further signal
        // of the SAME episode is dropped (one kill burst fires N ProcessFailed events).
        if (!_live || _exiting || _pendingGeneration != 0)
        {
            return null;
        }

        if (!_relaunchSpent)
        {
            _relaunchSpent = true;
            _pendingGeneration = ++_generationCounter;
            _live = false; // the watch is parked until the successor reports ready
            return new DtrhRecoveryOutcome.Relaunch(reason, _pendingGeneration);
        }

        _live = false;
        return new DtrhRecoveryOutcome.Exhausted(reason);
    }
}

/// <summary>
/// SP-027 slice b5: the graceful-exit flow (window-scoped — a relaunched window gets a
/// fresh flow). WPF archaeology (DtrhHostService.cs): CloseActive :149-160 — if the page
/// is ready and not already exiting: post end-run {reason:"host"} and arm the 1200ms
/// exit watchdog; otherwise dispose now. Page-initiated exit :312-314 — the page is
/// already winding down (boot.js:197-198 sends exit then exit-done back-to-back), so the
/// host only arms the bounded wait; exit-done :316 closes now. One _exiting latch +
/// one-shot close (consult CORRECTION 3): a late exit-done after the force-close, and a
/// Close() re-entry while Closing is active, are no-ops.
/// </summary>
public sealed class DtrhExitFlow
{
    /// <summary>What the caller must do for one exit event.</summary>
    public enum DtrhExitAction
    {
        /// <summary>Nothing — duplicate/late event (idempotence latch).</summary>
        None,
        /// <summary>Close now (no wind-down): page not live, or already exiting.</summary>
        CloseNow,
        /// <summary>Stay open: post end-run to the page and arm the bounded wait.</summary>
        RequestWindDown,
        /// <summary>Stay open: the page is already winding down — just arm the wait.</summary>
        ArmBoundedWait,
        /// <summary>The 1200ms wait elapsed without exit-done — force the close.</summary>
        ForceClose,
    }

    private bool _closed;

    /// <summary>The wind-down has been requested (WPF _exiting :36).</summary>
    public bool Exiting { get; private set; }

    /// <summary>Host/user close request (window X, auto-close, coordinator close).
    /// pageLive = the watchdog's IsLive at close time.</summary>
    public DtrhExitAction RequestClose(bool pageLive)
    {
        if (_closed)
        {
            return DtrhExitAction.None;
        }

        if (!pageLive || Exiting)
        {
            _closed = true;
            return DtrhExitAction.CloseNow;
        }

        Exiting = true;
        return DtrhExitAction.RequestWindDown;
    }

    /// <summary>Page-initiated exit (Esc held — boot.js:197). The page winds itself down;
    /// exit-done follows within milliseconds on the fast path. CloseNow only when the
    /// close already happened (never re-arm).</summary>
    public DtrhExitAction PageExit()
    {
        if (_closed)
        {
            return DtrhExitAction.None;
        }

        Exiting = true;
        return DtrhExitAction.ArmBoundedWait;
    }

    /// <summary>Page finished winding down (boot.js:123) — the fast path.</summary>
    public DtrhExitAction ExitDone()
    {
        if (_closed)
        {
            return DtrhExitAction.None;
        }

        _closed = true;
        return DtrhExitAction.CloseNow;
    }

    /// <summary>The bounded wait elapsed — the page wedged mid-shutdown; force the close
    /// (WPF exit-watchdog tick :881).</summary>
    public DtrhExitAction Timeout()
    {
        if (_closed)
        {
            return DtrhExitAction.None;
        }

        _closed = true;
        return DtrhExitAction.ForceClose;
    }
}
