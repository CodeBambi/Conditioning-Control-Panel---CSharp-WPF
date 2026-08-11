using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Threading;
using CcpClient.Desktop.Capabilities;
using CcpClient.Desktop.Features.Dtrh;
using CcpClient.Desktop.Lifecycle;

namespace CcpClient.Desktop.Features.Intake;

/// <summary>
/// SP-054: the Graded Intake host shell (ChaosWebViewHost parity on the landed b-series
/// pattern; WPF IntakeHostService.cs + ChaosWebViewHost.cs archaeology in record.md Step 1).
///
/// Surface: capability-driven (SP-006, never an OS guess) — embedded WebView2 on Windows;
/// the NativeWebDialog path is NOT ADMITTED for intake (the page's web-shim has no §3.3
/// inbox transport — a dialog would run the page standalone and silently lose results);
/// anything else = the honest unsupported surface. Boot contract (IntakeHostService.cs
/// :236-292 + ChaosWebViewHost Post :242-254): host→page messages queue until the page's
/// `ready`, then init flushes (the shim's preBuffer replays anything early,
/// web-shim.js:56-62) followed by the fullscreen echo (:~295). Host→page = synthetic
/// MessageEvent dispatch on window.chrome.webview (SP-011 W4, byte-identical to DTRH).
///
/// Window behaviors: per-instance profile (browser_data_intake parity →
/// DtrhProfileLock.WebView2ProfileDir("intake")); autoplay-without-gesture (:123);
/// navigation lockdown is the §4 server's (the page origin only serves the two trees);
/// fullscreen persist (set → echo ACTUAL → persist-if-changed, :352-366); BUILT in the
/// remembered mode (:115 — recovery relaunch always windowed); dashboard duck/restore
/// (:74, :133, :163-224 — plain minimize, NO audio ducking exists in WPF); 20s-heartbeat
/// relaunch-once watchdog (DtrhWatchdog with runActive:false — intake has no 10s mid-run
/// tier, :1000-1004); 1200ms exit watchdog (DtrhExitFlow, :1045-1051).
///
/// The ABORT path (consult correction 7a): `intake-close` closes IMMEDIATELY through the
/// _abortClosing latch — no end-run, no bounded wait, NO result, NO spend (:531-543).
/// </summary>
public partial class IntakeHostWindow : Window
{
    private readonly ApplicationHost _host;
    private readonly IntakeHostContext _context;
    private readonly Window _owner;
    private readonly DtrhWatchdog _watchdog;
    private readonly bool _killRenderers;
    private readonly string? _drive;
    private NativeWebView? _web;
    private bool _sentBootMessages;
    private int _heartbeats;
    private readonly CancellationTokenSource _closing = new();
    private DispatcherTimer? _watchTimer;
    private DtrhProcessFailed.DtrhProcessFailedSignal? _processFailedSignal;
    private readonly DtrhExitFlow _exitFlow = new();
    private DispatcherTimer? _exitTimer;
    private bool _recoveryClosing;
    // 7a: the jumpscare-abort latch — skips the graceful wind-down AND the spend.
    private bool _abortClosing;
    private bool _profileRetryUsed;
    private DtrhLoomDispatch? _loomDispatch;
    private WindowState? _ownerStateBeforeDuck;

    /// <summary>Boot-matrix facts (headed harness + diagnostics). Content-free.</summary>
    public int Heartbeats => _heartbeats;

    public string Surface { get; private set; } = "pending";

    /// <summary>Raised (UI thread) when the watchdog demands recovery: Relaunch-once or
    /// Exhausted-honest-close. The coordinator owns the acting (window recreation).</summary>
    public event Action<DtrhWatchdog.DtrhRecoveryOutcome>? RecoveryRequested;

    public IntakeHostWindow(ApplicationHost host, IntakeHostContext context, Window owner,
        DtrhWatchdog watchdog, bool recoveryWindowed, string? drive = null, bool killRenderers = false)
    {
        _host = host;
        _context = context;
        _owner = owner;
        _watchdog = watchdog;
        _drive = drive;
        _killRenderers = killRenderers;
        InitializeComponent();

        // BUILT in the remembered mode (:115): fullscreen when persisted — the recovery
        // relaunch is always windowed (:110-115).
        if (!recoveryWindowed && context.SettingsStore.Current.Fullscreen)
        {
            WindowState = WindowState.FullScreen;
            _host.LogDiagnostic("intake: built fullscreen (persisted IntakeFullscreen, :115)");
        }

        // Windowed default = 85% of the primary screen, centered (ChaosWebViewHost.cs
        // :167-218 parity) — measured from the already-shown owner's screen.
        if (WindowState != WindowState.FullScreen && owner.Screens.Primary is { } screen)
        {
            Width = screen.WorkingArea.Width * 0.85 / screen.Scaling;
            Height = screen.WorkingArea.Height * 0.85 / screen.Scaling;
        }

        Opened += (_, _) =>
        {
            DuckOwner();
            Begin();
            ScheduleDrive();
            StartWatchTimer();
        };
        Closing += (_, e) =>
        {
            // Graceful exit (WPF CloseActive :146-160) — ONLY for window-X / auto-close on a
            // LIVE page, never for recovery (dead page, DisposeAll directly :858) and NEVER
            // for the abort (7a: immediate teardown, no end-run).
            if (!_recoveryClosing && !_abortClosing && !_exitFlow.Exiting && _watchdog.IsLive
                && _exitFlow.RequestClose(pageLive: true) == DtrhExitFlow.DtrhExitAction.RequestWindDown)
            {
                e.Cancel = true;
                _watchdog.BeginExit();
                _host.LogDiagnostic("intake: graceful exit — end-run posted; bounded exit-done wait armed (1200ms, :1045-1051)");
                SendToPage(IntakeProtocol.BuildEndRun("host"));
                ArmExitTimer();
                return;
            }

            _closing.Cancel();
            try { _watchTimer?.Stop(); } catch { /* best-effort */ }
            _watchTimer = null;
            try { _exitTimer?.Stop(); } catch { /* best-effort */ }
            _exitTimer = null;
            try { _processFailedSignal?.Dispose(); } catch { /* best-effort */ }
            _processFailedSignal = null;
            _watchdog.MarkDead();
            RestoreOwner();
        };
    }

    // ---------- dashboard duck/restore (:74, :133, :163-224 — window minimize ONLY) ----------

    /// <summary>Plain MainWindow minimize (explicitly NOT tray tuck; records the prior
    /// state so a maximized panel comes back maximized). NO audio ducking exists in WPF —
    /// the word is window-only (:165-194).</summary>
    private void DuckOwner()
    {
        if (_owner.WindowState == WindowState.Minimized)
        {
            return;
        }

        _ownerStateBeforeDuck = _owner.WindowState;
        _owner.WindowState = WindowState.Minimized;
        _host.LogDiagnostic("intake: dashboard ducked (minimized; prior state recorded)");
    }

    /// <summary>The single restore funnel (every close path runs through Closing):
    /// Maximized comes back Maximized, else Normal + Activate (:205-225).</summary>
    private void RestoreOwner()
    {
        if (_ownerStateBeforeDuck is not { } prior)
        {
            return;
        }

        _ownerStateBeforeDuck = null;
        if (_owner.WindowState == WindowState.Minimized)
        {
            _owner.WindowState = prior == WindowState.Maximized ? WindowState.Maximized : WindowState.Normal;
            _owner.Activate();
            _host.LogDiagnostic($"intake: dashboard restored ({_owner.WindowState})");
        }
    }

    // ---------- surface selection (capability-driven; dialog NOT admitted) ----------

    private void Begin()
    {
        var probe = _context.PayloadProbe;
        if (probe.State != IntakeServingRoots.IntakePayloadState.Present)
        {
            // Typed honest surface (SP-048 discipline): never a substitute tree.
            Surface = "payload-missing";
            UnsupportedPanel.IsVisible = true;
            UnsupportedDetail.Text =
                $"intake payload root '{probe.Root}' -> {probe.State} ({probe.FileCount} files).\n"
                + "The intake tree serves from payload/intake beside the exe (the SP-023 copied-asset convention). "
                + "Never a silent substitute.";
            SetStatus($"intake: payload {probe.State} — honest surface");
            _host.LogDiagnostic($"intake: payload {probe.State} — honest surface shown (no substitute)");
            return;
        }

        var embedded = _host.Capabilities?.GetState(DtrhCapabilityProbes.EmbeddedCapability);
        if (embedded is CapabilityState.Available)
        {
            Surface = "embedded";
            BeginEmbedded();
            return;
        }

        // The dialog path is NOT ADMITTED for intake (the class comment): web-shim has no
        // §3.3 inbox transport, so a dialog page runs standalone and quiz results would
        // silently die in localStorage. Linux = the named limit (WSL zero distros), typed.
        Surface = "unsupported";
        UnsupportedPanel.IsVisible = true;
        UnsupportedDetail.Text =
            $"capability {DtrhCapabilityProbes.EmbeddedCapability}: {DescribeState(embedded)}\n"
            + "The intake page's web-shim speaks WebView2 chrome.webview only (web-shim.js:28-52) — the "
            + "NativeWebDialog path is not admitted for intake (a standalone page would silently lose "
            + "quiz results). Linux is the packet's named limit, never faked.";
        SetStatus("intake: honest unsupported (no silent standalone)");
        _host.LogDiagnostic("intake: embedded webview unavailable — honest unsupported surface (dialog not admitted for intake; named Linux limit)");
    }

    private void BeginEmbedded()
    {
        _web = new NativeWebView();
        _web.EnvironmentRequested += OnEnvironmentRequested;
        _web.AdapterCreated += (_, _) =>
        {
            _host.LogDiagnostic($"intake: AdapterCreated info='{SafeAdapterInfo()}'");
            AttachProcessFailedSignal();
        };
        _web.AdapterDestroyed += (_, _) => _host.LogDiagnostic("intake: AdapterDestroyed");
        _web.WebMessageReceived += OnWebMessage;
        _web.NavigationCompleted += (_, e) =>
            _host.LogDiagnostic($"intake: NavigationCompleted success={e.IsSuccess} (surface {Surface})");
        WebHost.Children.Add(_web);
        var url = _context.Participant.PageUrl();
        SetStatus("intake: navigating (embedded)");
        _host.LogDiagnostic("intake: navigating embedded surface");
        try
        {
            _web.Source = new Uri(url);
        }
        catch (Exception ex) when (DtrhProfileLock.IsStaleProfileLock(ex) && !_profileRetryUsed)
        {
            // The 0x800700AA stale-profile-lock class (SP-023 surprise #7): recover honestly
            // (kill the stale children holding OUR profile) and retry ONCE — never a crash loop.
            _profileRetryUsed = true;
            _host.LogDiagnostic("intake: navigation threw the stale-profile-lock class (0x800700AA) — recovering (typed, never silent)");
            var recovery = DtrhProfileLock.TryRecover(DtrhProfileLock.WebView2ProfileDir("intake"));
            _host.LogDiagnostic($"intake: stale-profile recovery: {recovery}");
            _web.Source = new Uri(url);
        }
    }

    private void OnEnvironmentRequested(object? sender, WebViewEnvironmentRequestedEventArgs args)
    {
        args.EnableDevTools = false;
        if (args is Avalonia.Platform.WindowsWebView2EnvironmentRequestedEventArgs wv2)
        {
            wv2.UserDataFolder = DtrhProfileLock.WebView2ProfileDir("intake");
            // :123 parity — the binaural bed starts with no gesture (SP-011 W10 verified class).
            wv2.AdditionalBrowserArguments = "--autoplay-policy=no-user-gesture-required";
            _host.LogDiagnostic("intake: WebView2 UserDataFolder set (browser_data_intake parity); autoplay-policy=no-user-gesture-required");
        }
    }

    private string SafeAdapterInfo()
    {
        try { return _web?.AdapterInfo?.ToString() ?? "(null)"; }
        catch (Exception ex) { return $"(AdapterInfo threw {ex.GetType().Name})"; }
    }

    // ---------- b5-pattern watchdog + process-failed (re-verified on this host) ----------

    private void StartWatchTimer()
    {
        _watchTimer = new DispatcherTimer { Interval = DtrhWatchdog.WatchInterval };
        _watchTimer.Tick += (_, _) =>
        {
            // runActive is ALWAYS false on this host: WPF intake has a single 20s silence
            // limit (:1000-1004) — no 10s mid-run tier (consult 7c pin).
            var outcome = _watchdog.Tick(DateTimeOffset.UtcNow, runActive: false);
            if (outcome is not null)
            {
                HandleRecoveryOutcome(outcome);
            }
        };
        _watchTimer.Start();
    }

    private void AttachProcessFailedSignal()
    {
        if (_web?.TryGetPlatformHandle() is Avalonia.Platform.IWindowsWebView2PlatformHandle wv2)
        {
            switch (DtrhProcessFailed.TryAttach(wv2.CoreWebView2, OnNativeProcessFailed))
            {
                case DtrhProcessFailed.AttachOutcome.Attached attached:
                    _processFailedSignal = attached.Signal;
                    _host.LogDiagnostic("intake: native ProcessFailed signal ATTACHED (W17 immediate route)");
                    break;
                case DtrhProcessFailed.AttachOutcome.Unavailable unavailable:
                    _host.LogDiagnostic(
                        $"intake: native ProcessFailed signal UNAVAILABLE ({unavailable.Code}) — {unavailable.Detail}; heartbeat watchdog is the only net");
                    break;
            }
        }
        else
        {
            _host.LogDiagnostic(
                "intake: native ProcessFailed signal UNAVAILABLE (unsupported-platform) — heartbeat watchdog is the only net (named limit, never faked)");
        }
    }

    private void OnNativeProcessFailed(int kind)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => OnNativeProcessFailed(kind));
            return;
        }

        _host.LogDiagnostic($"intake: native ProcessFailed — {DtrhWatchdog.KindName(kind)} (immediate detection)");
        var outcome = _watchdog.OnProcessFailed(kind);
        if (outcome is null)
        {
            _host.LogDiagnostic("intake: ProcessFailed dropped by the recovery-episode latch / guards (same episode — never a double-relaunch)");
            return;
        }

        HandleRecoveryOutcome(outcome);
    }

    private void HandleRecoveryOutcome(DtrhWatchdog.DtrhRecoveryOutcome outcome)
    {
        switch (outcome)
        {
            case DtrhWatchdog.DtrhRecoveryOutcome.Relaunch relaunch:
                _host.LogDiagnostic($"intake: watchdog demands RELAUNCH ({relaunch.Reason}) — generation {relaunch.Generation}");
                break;
            case DtrhWatchdog.DtrhRecoveryOutcome.Exhausted exhausted:
                _host.LogDiagnostic($"intake: watchdog demands EXHAUSTED close ({exhausted.Reason}) — never a restart loop");
                break;
        }

        RecoveryRequested?.Invoke(outcome);
    }

    /// <summary>Recovery teardown (watchdog relaunch / exhaustion): skip the graceful
    /// wind-down — the page is dead or wedged (WPF Recover calls DisposeAll directly).</summary>
    public void CloseForRecovery()
    {
        _recoveryClosing = true;
        Close();
    }

    /// <summary>HARNESS-ONLY (--intake-kill-renderers; the W17 injection re-verified on
    /// this host): kill every msedgewebview2 child holding OUR intake profile once the
    /// page is live (first kill → relaunch-once; the coordinator re-arms on the relaunched
    /// instance → second kill → typed exhaustion). The watchdog machine is the product code
    /// under test; this flag only pulls the trigger.</summary>
    private void ScheduleKillRenderersInjection()
    {
        if (!_killRenderers)
        {
            return;
        }

        var relaunched = _watchdog.RelaunchSpent;
        var delay = relaunched ? 8 : 12;
        _host.LogDiagnostic(
            $"intake: HARNESS kill-renderers armed at +{delay}s ({(relaunched ? "SECOND kill — exhaustion expected" : "first kill — relaunch-once expected")})");
        _ = Task.Delay(TimeSpan.FromSeconds(delay), _closing.Token).ContinueWith(
            t =>
            {
                if (t.IsCanceled) return;
                _host.LogDiagnostic("intake: HARNESS kill-renderers FIRING (killing profile-matched msedgewebview2 children — W17 injection)");
                var recovery = DtrhProfileLock.TryRecover(DtrhProfileLock.WebView2ProfileDir("intake"));
                _host.LogDiagnostic($"intake: HARNESS kill-renderers outcome: {recovery}");
            }, TaskScheduler.Default);
    }

    // ---------- the 1200ms bounded exit-done wait (:1045-1051) ----------

    private void ArmExitTimer()
    {
        try { _exitTimer?.Stop(); } catch { /* best-effort */ }
        _exitTimer = new DispatcherTimer { Interval = DtrhWatchdog.ExitDoneWait };
        _exitTimer.Tick += (_, _) =>
        {
            try { _exitTimer?.Stop(); } catch { /* best-effort */ }
            if (_exitFlow.Timeout() == DtrhExitFlow.DtrhExitAction.ForceClose)
            {
                _host.LogDiagnostic("intake: exit-done wait elapsed (1200ms) — watchdog-FORCED close (page wedged mid-shutdown)");
                Close();
            }
        };
        _exitTimer.Start();
    }

    // ---------- bridge: parse + dispatch (tolerance discipline) ----------

    private void OnWebMessage(object? sender, WebMessageReceivedEventArgs e) => HandleWebMessageBody(e.Body ?? "");

    /// <summary>The real parse+dispatch path for one page→host frame (also what the
    /// harness-only drive feeds — the fx-drive precedent).</summary>
    private void HandleWebMessageBody(string body)
    {
        switch (IntakeProtocol.ParsePageMessage(body))
        {
            case IntakeProtocol.IntakePageParseResult.Parsed parsed:
                DispatchPageMessage(parsed.Message);
                return;
            case IntakeProtocol.IntakePageParseResult.UnknownType unknown:
                // Out-of-vocabulary here INCLUDES the DTRH/studio loom types
                // (loom-delete/loom-reveal — intake sends loom-save ONLY, grep-pinned).
                _host.LogDiagnostic($"intake: unknown page message type '{unknown.Type}' — tolerated (typed, not dropped)");
                return;
            case IntakeProtocol.IntakePageParseResult.ForwardVersion forward:
                _host.LogDiagnostic(
                    $"intake: page message '{forward.Type}' declares protocol {forward.Protocol} > {IntakeProtocol.Version} — tolerated (forward-version, not acted on)");
                return;
            case IntakeProtocol.IntakePageParseResult.Malformed malformed:
                _host.LogDiagnostic($"intake: malformed page message ({malformed.Reason}) — tolerated");
                return;
        }
    }

    private void DispatchPageMessage(IntakeProtocol.IntakePageMessage message)
    {
        switch (message)
        {
            case IntakeProtocol.IntakePageMessage.Heartbeat:
                _heartbeats++;
                _watchdog.Heartbeat(DateTimeOffset.UtcNow);
                if (_heartbeats % 15 == 1) _host.LogDiagnostic($"intake: heartbeat #{_heartbeats}");
                return;
            case IntakeProtocol.IntakePageMessage.Pong:
                // pong counts as a beat (:305-308).
                _watchdog.Heartbeat(DateTimeOffset.UtcNow);
                return;
            case IntakeProtocol.IntakePageMessage.Log log:
                _host.LogDiagnostic($"intake page log: {log.Msg}");
                return;
            case IntakeProtocol.IntakePageMessage.Ready:
                _host.LogDiagnostic("intake: ready received — flushing init");
                _watchdog.MarkLive(DateTimeOffset.UtcNow);
                SendBootMessages();
                ScheduleKillRenderersInjection();
                return;
            case IntakeProtocol.IntakePageMessage.QuizResult quizResult:
                OnQuizResult(quizResult.Raw);
                return;
            case IntakeProtocol.IntakePageMessage.BootError bootError:
                // Typed non-silent outcome: the WPF reaction (classic-quiz fallback +
                // MessageBox, :633-654) has NO greenfield counterpart — the Lab/classic
                // quiz rows are unfiled. Host closes with the diagnostic; never a silent
                // black window (recorded deviation).
                _host.LogDiagnostic($"intake: boot-error from page ({(bootError.Msg is { Length: > 0 } m ? m : "no detail")}) — closing honestly (no classic-quiz fallback exists; typed deviation)");
                SetStatus("intake: page reported boot-error — closed honestly");
                Close();
                return;
            case IntakeProtocol.IntakePageMessage.FullscreenSet fullscreenSet:
                WindowState = fullscreenSet.On ? WindowState.FullScreen : WindowState.Normal;
                _host.LogDiagnostic($"intake: fullscreen-set on={fullscreenSet.On} -> WindowState={WindowState}");
                // Echo the ACTUAL state (:354) and persist only if changed (:356-358).
                var isFull = WindowState == WindowState.FullScreen;
                SendToPage(IntakeProtocol.BuildFullscreen(isFull));
                if (_context.SettingsStore.Current.Fullscreen != isFull)
                {
                    _context.SettingsStore.Mutate(d => d.Fullscreen = isFull);
                    _ = _context.SettingsStore.Save();
                    _host.LogDiagnostic($"intake: IntakeFullscreen persisted ({isFull})");
                }

                return;
            case IntakeProtocol.IntakePageMessage.Exit:
                // The page winds itself down; arm the bounded wait (:311-320).
                _host.LogDiagnostic("intake: exit received — page winding down; bounded exit-done wait armed (1200ms)");
                _watchdog.BeginExit();
                if (_exitFlow.PageExit() == DtrhExitFlow.DtrhExitAction.ArmBoundedWait)
                {
                    ArmExitTimer();
                }

                return;
            case IntakeProtocol.IntakePageMessage.ExitDone:
                _host.LogDiagnostic("intake: exit-done received — closing (graceful fast path)");
                if (_exitFlow.ExitDone() == DtrhExitFlow.DtrhExitAction.CloseNow && IsVisible)
                {
                    Close();
                }

                return;
            case IntakeProtocol.IntakePageMessage.IntakeClose:
                // 7a: the jumpscare ABORT (:531-543) — immediate teardown. NO QuizRunResult
                // (no session drafted), NO pass spend, NO end-run wind-down.
                _host.LogDiagnostic("intake: intake-close (jumpscare abort) — immediate teardown; NO result, NO spend (:531-543)");
                _abortClosing = true;
                Close();
                return;
            case IntakeProtocol.IntakePageMessage.LoomSave loomSave:
                // The SHARED b4 store via the ONE write path (consult 7b): loom-result
                // ONLY — the intake 6-out table has no loom-list.
                if (_loomDispatch is null)
                {
                    _loomDispatch = new DtrhLoomDispatch(
                        _context.Loom, () => _context.Participant.Server.MediaOrigin, SendToPage, _host.LogDiagnostic);
                }

                var loomResult = _loomDispatch.HandleSave(loomSave.Name, loomSave.Overwrite, loomSave.Raw);
                _host.LogDiagnostic($"intake: loom-save -> {(loomResult.Ok ? "saved" : $"refused ({loomResult.Error})")} against the SHARED b4 store"); // presence+shape — never the slug
                return;
            case IntakeProtocol.IntakePageMessage.IntakeSaveImage saveImage:
                var imageResult = _context.SaveImageSink.Save(saveImage.PngBase64, saveImage.Index);
                SendToPage(IntakeProtocol.BuildSaveImageResult(imageResult.Ok, imageResult.Path, imageResult.Error));
                return;
        }
    }

    // ---------- the completion loop (:373-508) ----------

    /// <summary>quiz-result → XP (computed, never granted) → pass spend → draft (marked
    /// never-runnable) → punch stamp → session-drafted reply. The window STAYS OPEN.
    /// Order pinned from the WPF evidence: XP :389-397, spend :406, draft :421-427,
    /// punch :459, reply :496/:504.</summary>
    private void OnQuizResult(JsonElement raw)
    {
        IntakeQuizRun? run;
        try
        {
            run = raw.Deserialize<IntakeQuizRun>();
        }
        catch (JsonException ex)
        {
            // Conservative no-spend on an unreadable result (WPF's exact deserialize-failure
            // path is unpinned — recorded ambiguity; the user-favorable direction wins).
            _host.LogDiagnostic($"intake: quiz-result unreadable ({ex.GetType().Name}) — NO spend, NO draft (typed, conservative)");
            SendToPage(IntakeProtocol.BuildSessionDrafted(false, null, null));
            return;
        }

        if (run is null)
        {
            _host.LogDiagnostic("intake: quiz-result deserialized to null — NO spend, NO draft (typed)");
            SendToPage(IntakeProtocol.BuildSessionDrafted(false, null, null));
            return;
        }

        // 1. XP — COMPUTED, never granted (no greenfield XP store — typed seam;
        // IntakeHostService.cs:389-397 formula).
        var xp = IntakeDraft.ComputeCompletionXp(run);
        _host.LogDiagnostic($"intake: completion XP computed, not granted ({xp}; no XP store — typed seam)");

        // 2. The pass spend — HERE and nowhere else (:406; completion-only).
        _context.Pass.ConsumeForCompletedIntake();

        // 3. Draft the session (marked never-runnable — the degraded-delivery contract) and
        // sink it with collision suffixes (:421-427, :515-528).
        try
        {
            var draft = IntakeDraft.Generate(run, DateTimeOffset.UtcNow);
            var path = _context.DraftSink.Write(draft);
            _host.LogDiagnostic($"intake: session drafted ({draft.Difficulty}, profile A1-A5 scored, runnable:false — never-runnable typed)");
            // 4. The punch stamp (first-hole-free on the first completed intake; the draft
            // pends silently — no session engine, the degraded contract).
            _context.PunchCard.EnsureCardStarted();
            _context.PunchCard.NotifyIntakeCompleted(draft.Id);
            // 5. Reply (:496). The 12s "Run it now" toast is the unfiled session-library row.
            SendToPage(IntakeProtocol.BuildSessionDrafted(true, draft.Name, path));
        }
        catch (Exception ex)
        {
            _host.LogDiagnostic($"intake: drafting failed ({ex.GetType().Name}) — session-drafted ok:false (:504); the spend STANDS (the intake completed)");
            SendToPage(IntakeProtocol.BuildSessionDrafted(false, null, null));
        }
    }

    // ---------- boot contract (:236-295) ----------

    private void SendBootMessages()
    {
        if (_sentBootMessages) return;
        _sentBootMessages = true;

        Activate();
        _web?.Focus();

        // init {protocol:1, config, ai} (:241-292). Provisions (the privacy boundaries
        // verbatim): niche = typed seam default bambi, bank-clamped against the SERVED
        // tree; caps all 1.0 (:658-668); endless=false / steerValve=1.0 / priorRun=null;
        // micEnabled=false (no consent subsystem — consent-gated OFF, a TIGHTENING over
        // WPF, never presented as the ported gate); media = 18+18 random sample of the
        // user pool as /umedia/ URLs (null on empty pool); subjectId = local fiction;
        // subliminals=null (typed seam — no subliminal subsystem); ai token EMPTY (WPF's
        // own logged-out branch — the page runs its deterministic local stub, NO network;
        // genuine parity, not a degradation).
        var seamNiche = IntakeNiche.CurrentFromSeam();
        var niche = IntakeNiche.ClampToOnDiskBanks(seamNiche, IntakeServingRoots.PayloadRoot);
        if (niche != seamNiche)
        {
            _host.LogDiagnostic($"intake: niche bank file missing for '{seamNiche}' — clamped to {niche} (bank clamp, :674-702)");
        }
        var media = IntakeMediaManifest.Build(
            _context.Participant.UserMediaRoot, _context.Participant.Server.MediaOrigin, _host.LogDiagnostic,
            disabled: DtrhUserMedia.BuildDisabledSet(_context.AssetSelectionStore.Current.DisabledAssetPaths),
            useWhitelist: _context.AssetSelectionStore.Current.UseAssetWhitelist);
        var config = new
        {
            niche,
            caps = new
            {
                flashRate = 1.0, flashOpacity = 1.0, subDensity = 1.0, duckDepth = 1.0,
                bubbleRate = 1.0, binauralDepth = 1.0, bgIntensity = 1.0, masterIntensity = 1.0,
            },
            endless = false,
            steerValve = 1.0,
            priorRun = (object?)null,
            m2Test = false,
            micEnabled = false,
            media,
            subjectId = _context.SubjectId,
            subliminals = (object?)null,
        };
        SendToPage(IntakeProtocol.BuildInit(config, new IntakeProtocol.IntakeAiProvision(IntakeProtocol.AiProxyBaseUrl, "")));
        _host.LogDiagnostic($"intake: init sent (niche {niche} [seam default + bank clamp]; mic consent-gated OFF; ai EMPTY-token local stub [WPF logged-out parity]; media {(media is null ? "null (empty pool)" : "sampled")}; subject id local-only)");

        // The fullscreen echo rides right after init (:~295).
        SendToPage(IntakeProtocol.BuildFullscreen(WindowState == WindowState.FullScreen));
        SetStatus("intake: init sent");
    }

    /// <summary>Host→page (admission §3.2 parity): synthetic MessageEvent dispatch on
    /// window.chrome.webview (SP-011 W4/W6 proven, byte-identical). No inbox path exists
    /// for intake (the page has no §3.3 transport — the dialog path is not admitted).</summary>
    public void SendToPage(object msg)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => SendToPage(msg));
            return;
        }

        if (_web is null)
        {
            return; // honest-unsupported surface — nothing to post to (typed above)
        }

        var json = IntakeProtocol.SerializeForPage(msg);
        _ = _web.InvokeScript($"window.chrome.webview.dispatchEvent(new MessageEvent('message',{{data:{json}}}))")
            .ContinueWith(
                t => _host.LogDiagnostic($"intake: host->page dispatch faulted: {t.Exception?.GetBaseException().Message}"),
                TaskContinuationOptions.OnlyOnFaulted);
    }

    private void SetStatus(string s) => Dispatcher.UIThread.Post(() => Status.Text = s);

    /// <summary>Shared capability-state description (DtrhHostWindow's formatter).</summary>
    internal static string DescribeState(CapabilityState? state) => state switch
    {
        null => "no capability registry",
        CapabilityState.Available available => $"Available — {available.Detail}",
        CapabilityState.Unavailable unavailable => $"Unavailable ({unavailable.Reason.Code}) — {unavailable.Reason.Detail}",
        CapabilityState.Degraded degraded => $"Degraded ({degraded.Reason.Code}) — {degraded.Reason.Detail}",
        CapabilityState.PermissionRequired permission => $"PermissionRequired ({permission.Reason.Code}) — {permission.Reason.Detail}",
        CapabilityState.DependencyMissing missing => $"DependencyMissing ({missing.Dependency}) — {missing.Reason.Detail}",
        CapabilityState.Faulted faulted => $"Faulted ({faulted.Reason.Code}) — {faulted.Reason.Detail}",
        _ => state.GetType().Name,
    };

    // ---------- HARNESS-ONLY drive (--intake-drive; the fx-drive precedent) ----------

    /// <summary>HARNESS-ONLY: a timed script of RAW page JSON fed through the REAL
    /// parse+dispatch path (headed evidence without input automation — SP-008 named
    /// limit). Steps: <c>quiz-result@t; intake-close@t; fullscreen-set:on|off@t; exit@t;
    /// loom-file:&lt;name&gt;@t</c> (a staged GIF read from the shared overlay staging —
    /// the dtrh loom-file convention — sent as the REAL loom-save).</summary>
    private void ScheduleDrive()
    {
        if (string.IsNullOrWhiteSpace(_drive)) return;
        _host.LogDiagnostic($"intake: drive armed (HARNESS-ONLY): {_drive}");
        var steps = _drive.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var index = 0;
        foreach (var step in steps)
        {
            index++;
            var at = step.IndexOf('@');
            var seconds = at >= 0 && double.TryParse(step[(at + 1)..], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var s) ? s : index * 4.0;
            var bare = at >= 0 ? step[..at] : step;
            var json = IntakeHarness.DriveStepToJson(bare);
            if (json is null && bare.StartsWith("loom-file:", StringComparison.Ordinal))
            {
                var fileName = bare["loom-file:".Length..];
                _ = Task.Delay(TimeSpan.FromSeconds(seconds), _closing.Token).ContinueWith(
                    t =>
                    {
                        if (t.IsCanceled) return;
                        try
                        {
                            // The shared staging convention (dtrh fx-drive loom-file): the
                            // GIF bytes + message shape are real; only the encoder origin
                            // is harnessed.
                            var staged = Path.Combine(DtrhParticipant.OverlayRoot, "loom", fileName);
                            var gifBytes = File.ReadAllBytes(staged);
                            var loomJson = IntakeHarness.LoomFileJson(Path.GetFileNameWithoutExtension(fileName), gifBytes);
                            _host.LogDiagnostic($"intake: drive loom-file staged ({gifBytes.Length} bytes) — real loom-save through the real dispatch path (HARNESS-ONLY)");
                            Dispatcher.UIThread.Post(() => HandleWebMessageBody(loomJson));
                        }
                        catch (Exception ex)
                        {
                            _host.LogDiagnostic($"intake: drive loom-file failed ({ex.GetType().Name}) — harness no-op");
                        }
                    }, TaskScheduler.Default);
                continue;
            }

            if (json is null)
            {
                _host.LogDiagnostic($"intake: drive step '{step}' unknown — skipped (harness)");
                continue;
            }

            _ = Task.Delay(TimeSpan.FromSeconds(seconds), _closing.Token).ContinueWith(
                t =>
                {
                    if (t.IsCanceled) return;
                    _host.LogDiagnostic($"intake: drive injecting '{bare}' (raw JSON through the real dispatch path)");
                    Dispatcher.UIThread.Post(() => HandleWebMessageBody(json));
                }, TaskScheduler.Default);
        }
    }
}
