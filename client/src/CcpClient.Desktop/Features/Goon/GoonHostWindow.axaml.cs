using System.Reflection;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using CcpClient.Desktop.Capabilities;
using CcpClient.Desktop.Features.Dtrh;
using CcpClient.Desktop.Lifecycle;

namespace CcpClient.Desktop.Features.Goon;

/// <summary>
/// SP-130: the Goon Game practice host shell. The <see cref="Intake.IntakeHostWindow"/> pattern,
/// over the payload the port now ships (<see cref="GoonServingRoots"/>).
///
/// <para><b>This is a HOST, not a game.</b> The duel runs entirely in the page on an in-process
/// loopback pair (<c>ui/soloDriver.js:1-18</c>, <c>net/loopbackTransport.js:19-23</c>); none of
/// upstream's 25 C# files is ported (goon-game-census.md §7.1, "Upstream code to port: none of
/// the 25 as code"). What this window owes the page is the handshake and nothing more.</para>
///
/// <para><b>Boot contract</b> (<c>GoonHostService.cs:296-380</c>, mirrored): the page announces
/// <c>ready</c> (<c>bridge.js:106</c>), then the host posts <c>init</c>, then <c>manifest</c>,
/// then the <c>fullscreen</c> echo. <c>boot.js</c> settles only when BOTH init and manifest have
/// landed (<c>boot.js:415-416</c>) and gives up at 45 s (<c>:112-113</c>), so the manifest is
/// mandatory, not optional.</para>
///
/// <para><b>Window behaviours, each cited:</b> the dashboard is ducked with a plain minimize —
/// upstream is explicit that this surface has no tray tuck (<c>GoonHostService.cs:20-23</c>);
/// fullscreen is host-owned and echoed as the ACTUAL state (catalogue <c>:31</c>,
/// <c>boot.js:2504-2513</c>); the exit handshake is <c>exit</c> → <c>end-run</c> →
/// <c>exit-done</c> with a bounded wait (<c>boot.js:2436-2465</c>).</para>
///
/// <para><b>Named limits, not silences.</b> (1) There is no relaunch coordinator for this
/// surface, so a watchdog recovery demand closes the window honestly with a diagnostic rather
/// than being silently left on a black page. (2) This host grants the page NO permissions; the
/// shipping app grants the microphone unprompted (<c>GoonHostService.cs:489</c>, D244) and this
/// one does not — but Avalonia exposes <c>CoreWebView2</c> only as a raw pointer, so no
/// <c>PermissionRequested</c> deny hook exists either, and WebView2's own prompt is the residual
/// (D250). (3) Linux is unproven and gets the typed unsupported surface; the gate that would
/// confirm it is a WSLg/X11 run observing that surface, which this packet did not perform.</para>
/// </summary>
public partial class GoonHostWindow : Window
{
    private readonly ApplicationHost _host;
    private readonly GoonParticipant _participant;
    private readonly Window _owner;
    private readonly DtrhWatchdog _watchdog;
    private NativeWebView? _web;
    private bool _sentBootMessages;
    private int _heartbeats;
    private DispatcherTimer? _watchTimer;
    private DispatcherTimer? _exitTimer;
    private DtrhProcessFailed.DtrhProcessFailedSignal? _processFailedSignal;
    private readonly DtrhExitFlow _exitFlow = new();
    private WindowState? _ownerStateBeforeDuck;
    private bool _profileRetryUsed;
    private bool _recoveryClosing;

    /// <summary>Boot-matrix facts (headed harness + diagnostics). Content-free.</summary>
    public int Heartbeats => _heartbeats;

    /// <summary>Which surface was selected: embedded / payload-missing / unsupported.</summary>
    public string Surface { get; private set; } = "pending";

    /// <summary>True once init + manifest + the fullscreen echo have been posted.</summary>
    public bool BootMessagesSent => _sentBootMessages;

    public GoonHostWindow(ApplicationHost host, GoonParticipant participant, Window owner, DtrhWatchdog watchdog)
    {
        _host = host;
        _participant = participant;
        _owner = owner;
        _watchdog = watchdog;
        InitializeComponent();
        BuildRefusalRail();

        if (owner.Screens.Primary is { } screen)
        {
            Width = screen.WorkingArea.Width * 0.85 / screen.Scaling;
            Height = screen.WorkingArea.Height * 0.85 / screen.Scaling;
        }

        Opened += (_, _) =>
        {
            DuckOwner();
            Begin();
            StartWatchTimer();
        };
        Closing += (_, e) =>
        {
            // Graceful exit for window-X on a LIVE page only: ask the page to wind down and wait
            // a bounded 1200 ms for exit-done (boot.js:2436-2465). Never for a recovery close.
            if (!_recoveryClosing && !_exitFlow.Exiting && _watchdog.IsLive
                && _exitFlow.RequestClose(pageLive: true) == DtrhExitFlow.DtrhExitAction.RequestWindDown)
            {
                e.Cancel = true;
                _watchdog.BeginExit();
                _host.LogDiagnostic("goon: graceful exit — end-run posted; bounded exit-done wait armed (1200ms)");
                SendToPage(GoonProtocol.BuildEndRun("host"));
                ArmExitTimer();
                return;
            }

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

    // ---------- the refusal rail (the packet's sharpest honesty requirement) ----------

    /// <summary>
    /// One row per typed refusal, built from <see cref="GoonDoors.Refused"/> so the rail can
    /// never disagree with the caps computed from the same list. Each row carries the headline,
    /// what is MISSING, and the owner gate that owns the decision — and never a price, a date or
    /// a "coming soon".
    /// </summary>
    private void BuildRefusalRail()
    {
        foreach (var refusal in GoonDoors.Refused)
        {
            var row = new StackPanel
            {
                Name = $"Refusal{refusal.Door}",
                Spacing = 4,
                Orientation = Orientation.Vertical,
            };
            row.Children.Add(new TextBlock
            {
                Text = refusal.Headline,
                Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x9A, 0xD5)),
                FontWeight = FontWeight.Bold,
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap,
            });
            row.Children.Add(new TextBlock
            {
                Text = refusal.Missing,
                Foreground = new SolidColorBrush(Color.FromRgb(0xC8, 0xC8, 0xDD)),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
            });
            row.Children.Add(new TextBlock
            {
                Text = refusal.OwnerGate,
                Foreground = new SolidColorBrush(Color.FromRgb(0x7A, 0x7A, 0x99)),
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
            });
            RefusalPanel.Children.Add(row);
        }
    }

    // ---------- dashboard duck/restore (GoonHostService.cs:20-23 — minimize, no tray tuck) ----------

    private void DuckOwner()
    {
        if (_owner.WindowState == WindowState.Minimized)
        {
            return;
        }

        _ownerStateBeforeDuck = _owner.WindowState;
        _owner.WindowState = WindowState.Minimized;
        _host.LogDiagnostic("goon: dashboard ducked (minimized; prior state recorded)");
    }

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
            _host.LogDiagnostic($"goon: dashboard restored ({_owner.WindowState})");
        }
    }

    // ---------- surface selection (capability-driven; the dialog path is NOT admitted) ----------

    private void Begin()
    {
        var probe = GoonServingRoots.Probe();
        if (probe.State != GoonServingRoots.GoonPayloadState.Present)
        {
            Surface = "payload-missing";
            UnsupportedPanel.IsVisible = true;
            UnsupportedDetail.Text =
                $"goon payload root '{probe.Root}' -> {probe.State} ({probe.FileCount} files)"
                + (probe.MissingFile is null ? "" : $"; required file '{probe.MissingFile}' absent")
                + ".\nThe goon tree serves from payload/goon beside the exe (the SP-023 copied-asset "
                + "convention; the bytes stay owned by the legacy tree). Never a silent substitute.";
            SetStatus($"goon: payload {probe.State} — honest surface");
            _host.LogDiagnostic($"goon: payload {probe.State} — honest surface shown (no substitute)");
            return;
        }

        var embedded = _host.Capabilities?.GetState(DtrhCapabilityProbes.EmbeddedCapability);
        if (embedded is CapabilityState.Available)
        {
            Surface = "embedded";
            BeginEmbedded();
            return;
        }

        Surface = "unsupported";
        UnsupportedPanel.IsVisible = true;
        UnsupportedDetail.Text =
            $"capability {DtrhCapabilityProbes.EmbeddedCapability}: {Intake.IntakeHostWindow.DescribeState(embedded)}\n"
            + "The goon page's bridge speaks WebView2 chrome.webview only (bridge.js:45-47, :65-67, "
            + ":93-95) — the NativeWebDialog path is not admitted here, because a standalone page "
            + "synthesizes its own init (bridge.js:473) and would ignore every door refusal this "
            + "host declares. Linux is unproven and is a named gate, never faked.";
        SetStatus("goon: honest unsupported (no silent standalone)");
        _host.LogDiagnostic(
            "goon: embedded webview unavailable — honest unsupported surface (dialog not admitted; named Linux limit)");
    }

    private void BeginEmbedded()
    {
        _web = new NativeWebView();
        _web.EnvironmentRequested += OnEnvironmentRequested;
        _web.AdapterCreated += (_, _) =>
        {
            _host.LogDiagnostic($"goon: AdapterCreated info='{SafeAdapterInfo()}'");
            AttachProcessFailedSignal();
        };
        _web.AdapterDestroyed += (_, _) => _host.LogDiagnostic("goon: AdapterDestroyed");
        _web.WebMessageReceived += OnWebMessage;
        _web.NavigationCompleted += (_, e) =>
            _host.LogDiagnostic($"goon: NavigationCompleted success={e.IsSuccess} (surface {Surface})");
        WebHost.Children.Add(_web);
        var url = _participant.PageUrl();
        SetStatus("goon: navigating (embedded)");
        _host.LogDiagnostic("goon: navigating embedded surface");
        try
        {
            _web.Source = new Uri(url);
        }
        catch (Exception ex) when (DtrhProfileLock.IsStaleProfileLock(ex) && !_profileRetryUsed)
        {
            // The 0x800700AA stale-profile-lock class (SP-023 surprise #7): recover honestly and
            // retry ONCE — never a crash loop.
            _profileRetryUsed = true;
            _host.LogDiagnostic("goon: navigation threw the stale-profile-lock class (0x800700AA) — recovering (typed)");
            var recovery = DtrhProfileLock.TryRecover(DtrhProfileLock.WebView2ProfileDir("goon"));
            _host.LogDiagnostic($"goon: stale-profile recovery: {recovery}");
            _web.Source = new Uri(url);
        }
    }

    private void OnEnvironmentRequested(object? sender, WebViewEnvironmentRequestedEventArgs args)
    {
        args.EnableDevTools = false;
        if (args is Avalonia.Platform.WindowsWebView2EnvironmentRequestedEventArgs wv2)
        {
            // A NAMED per-surface profile: the DTRH game's and the intake's WebView2 state stay
            // untouched, and two surfaces on one profile would re-arm the stale-profile-lock class.
            wv2.UserDataFolder = DtrhProfileLock.WebView2ProfileDir("goon");
            // The page builds its audio graph on the first screen rather than the first cue and
            // expects a running context under the host (ui/screens/title.js:161-165).
            wv2.AdditionalBrowserArguments = "--autoplay-policy=no-user-gesture-required";
            _host.LogDiagnostic("goon: WebView2 UserDataFolder set (per-surface); autoplay-policy=no-user-gesture-required");
        }
    }

    private string SafeAdapterInfo()
    {
        try { return _web?.AdapterInfo?.ToString() ?? "(null)"; }
        catch (Exception ex) { return $"(AdapterInfo threw {ex.GetType().Name})"; }
    }

    // ---------- watchdog ----------

    private void StartWatchTimer()
    {
        _watchTimer = new DispatcherTimer { Interval = DtrhWatchdog.WatchInterval };
        _watchTimer.Tick += (_, _) =>
        {
            // runActive is always false here: this host does not model a mid-run tier. Upstream's
            // extra net is a paint-stall rule (GoonHostService.cs:74-80) this unit does not port.
            var outcome = _watchdog.Tick(DateTimeOffset.UtcNow, runActive: false);
            if (outcome is not null)
            {
                HandleRecoveryOutcome(outcome);
            }
        };
        _watchTimer.Start();
    }

    /// <summary>No relaunch coordinator exists for this surface, so BOTH outcomes close the
    /// window honestly with the reason named. A wedged page is never left painted-and-dead.</summary>
    private void HandleRecoveryOutcome(DtrhWatchdog.DtrhRecoveryOutcome outcome)
    {
        var reason = outcome switch
        {
            DtrhWatchdog.DtrhRecoveryOutcome.Relaunch relaunch => relaunch.Reason,
            DtrhWatchdog.DtrhRecoveryOutcome.Exhausted exhausted => exhausted.Reason,
            _ => outcome.GetType().Name,
        };
        _host.LogDiagnostic(
            $"goon: watchdog demanded recovery ({reason}) — no relaunch coordinator exists for this surface, closing honestly (named limit)");
        SetStatus($"goon: page stopped responding ({reason}) — closed honestly");
        _recoveryClosing = true;
        _watchdog.MarkDead();
        Close();
    }

    private void AttachProcessFailedSignal()
    {
        if (_web?.TryGetPlatformHandle() is Avalonia.Platform.IWindowsWebView2PlatformHandle wv2)
        {
            switch (DtrhProcessFailed.TryAttach(wv2.CoreWebView2, OnNativeProcessFailed))
            {
                case DtrhProcessFailed.AttachOutcome.Attached attached:
                    _processFailedSignal = attached.Signal;
                    _host.LogDiagnostic("goon: native ProcessFailed signal ATTACHED (W17 immediate route)");
                    break;
                case DtrhProcessFailed.AttachOutcome.Unavailable unavailable:
                    _host.LogDiagnostic(
                        $"goon: native ProcessFailed signal UNAVAILABLE ({unavailable.Code}) — {unavailable.Detail}; heartbeat watchdog is the only net");
                    break;
            }
        }
        else
        {
            _host.LogDiagnostic(
                "goon: native ProcessFailed signal UNAVAILABLE (unsupported-platform) — heartbeat watchdog is the only net (named limit, never faked)");
        }
    }

    private void OnNativeProcessFailed(int kind)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => OnNativeProcessFailed(kind));
            return;
        }

        var outcome = _watchdog.OnProcessFailed(kind);
        if (outcome is not null)
        {
            HandleRecoveryOutcome(outcome);
        }
    }

    private void ArmExitTimer()
    {
        _exitTimer?.Stop();
        _exitTimer = new DispatcherTimer { Interval = DtrhWatchdog.ExitDoneWait };
        _exitTimer.Tick += (_, _) =>
        {
            _exitTimer?.Stop();
            _exitTimer = null;
            if (_exitFlow.Timeout() == DtrhExitFlow.DtrhExitAction.ForceClose)
            {
                _host.LogDiagnostic("goon: exit-done never arrived within 1200ms — closing anyway");
                Close();
            }
        };
        _exitTimer.Start();
    }

    // ---------- bridge: parse + dispatch (tolerance discipline) ----------

    private void OnWebMessage(object? sender, WebMessageReceivedEventArgs e) => HandleWebMessageBody(e.Body ?? "");

    /// <summary>The real parse+dispatch path for one page→host frame.</summary>
    internal void HandleWebMessageBody(string body)
    {
        switch (GoonProtocol.ParsePageMessage(body))
        {
            case GoonProtocol.GoonPageParseResult.Parsed parsed:
                DispatchPageMessage(parsed.Message);
                return;
            case GoonProtocol.GoonPageParseResult.UnknownType unknown:
                // The transfer-cache, received-inbox, Discord and haptics families all land here:
                // out of THIS host's vocabulary, tolerated, logged by name, never acted on.
                _host.LogDiagnostic($"goon: page message type '{unknown.Type}' is out of this host's vocabulary — tolerated (typed, not dropped)");
                return;
            case GoonProtocol.GoonPageParseResult.ForwardVersion forward:
                _host.LogDiagnostic(
                    $"goon: page message '{forward.Type}' declares protocol {forward.Protocol} > {GoonProtocol.Version} — tolerated (forward-version, not acted on)");
                return;
            case GoonProtocol.GoonPageParseResult.Malformed malformed:
                _host.LogDiagnostic($"goon: malformed page message ({malformed.Reason}) — tolerated");
                return;
        }
    }

    private void DispatchPageMessage(GoonProtocol.GoonPageMessage message)
    {
        switch (message)
        {
            case GoonProtocol.GoonPageMessage.Heartbeat:
                _heartbeats++;
                _watchdog.Heartbeat(DateTimeOffset.UtcNow);
                if (_heartbeats % 15 == 1) _host.LogDiagnostic($"goon: heartbeat #{_heartbeats}");
                return;
            case GoonProtocol.GoonPageMessage.Pong:
                _watchdog.Heartbeat(DateTimeOffset.UtcNow);
                return;
            case GoonProtocol.GoonPageMessage.Log log:
                _host.LogDiagnostic($"goon page log: {log.Msg}");
                return;
            case GoonProtocol.GoonPageMessage.Ready:
                _host.LogDiagnostic("goon: ready received — flushing init + manifest");
                _watchdog.MarkLive(DateTimeOffset.UtcNow);
                SendBootMessages();
                return;
            case GoonProtocol.GoonPageMessage.BootError bootError:
                _host.LogDiagnostic(
                    $"goon: boot-error from page ({(bootError.Msg is { Length: > 0 } m ? m : "no detail")}) — closing honestly (no fallback surface exists)");
                SetStatus("goon: page reported boot-error — closed honestly");
                Close();
                return;
            case GoonProtocol.GoonPageMessage.FullscreenSet fullscreenSet:
                WindowState = fullscreenSet.On ? WindowState.FullScreen : WindowState.Normal;
                var isFull = WindowState == WindowState.FullScreen;
                _host.LogDiagnostic($"goon: fullscreen-set on={fullscreenSet.On} -> WindowState={WindowState}");
                // Echo the ACTUAL state, never the requested one (catalogue :31).
                SendToPage(GoonProtocol.BuildFullscreen(isFull));
                return;
            case GoonProtocol.GoonPageMessage.NetPost netPost:
                OnNetPost(netPost);
                return;
            case GoonProtocol.GoonPageMessage.Exit:
                _host.LogDiagnostic("goon: exit received — page winding down; bounded exit-done wait armed (1200ms)");
                _watchdog.BeginExit();
                if (_exitFlow.PageExit() == DtrhExitFlow.DtrhExitAction.ArmBoundedWait)
                {
                    ArmExitTimer();
                }

                return;
            case GoonProtocol.GoonPageMessage.ExitDone:
                _host.LogDiagnostic("goon: exit-done received — closing");
                if (_exitFlow.ExitDone() == DtrhExitFlow.DtrhExitAction.CloseNow)
                {
                    Close();
                }

                return;
        }
    }

    /// <summary>
    /// <b>The refusal, in-process.</b> The page's every server call arrives here (init sets
    /// <c>viaHost:true</c>), and it is answered locally and immediately: no socket, no proxy, no
    /// header, no token. The requested path is logged as a ROUTE, never followed.
    /// </summary>
    private void OnNetPost(GoonProtocol.GoonPageMessage.NetPost netPost)
    {
        var refusal = GoonDoors.NetRefusal;
        if (refusal is null)
        {
            // Unreachable while any door is refused. Typed rather than assumed: with no refusal
            // to quote this host has no honest sentence, so it says so instead of inventing one.
            _host.LogDiagnostic("goon: net-post arrived with NO door refusal to answer from — refusing without a reason (typed)");
            return;
        }

        _host.LogDiagnostic(
            $"goon: net-post '{netPost.Path}' refused in-process ({GoonProtocol.NetPostRefusedStatus}) — this build has no outbound network boundary");
        SendToPage(GoonProtocol.BuildNetPostResult(netPost.Id, refusal));
    }

    /// <summary>
    /// The boot flush: <c>init</c>, then <c>manifest</c>, then the fullscreen echo — the order
    /// upstream uses (<c>GoonHostService.cs:311</c>, <c>:372</c>, <c>:387</c>), and the order
    /// <c>boot.js</c> needs, since it settles only once both have landed (<c>boot.js:415-416</c>).
    /// </summary>
    private void SendBootMessages()
    {
        if (_sentBootMessages)
        {
            return;
        }

        _sentBootMessages = true;

        var appVersion = VersionSelfCheck.GetInformationalVersion(Assembly.GetExecutingAssembly()) ?? "0.0.0";
        SendToPage(GoonProtocol.BuildInit(
            displayName: DefaultDisplayName,
            appVersion: appVersion,
            caps: GoonDoors.Caps,
            fullscreen: WindowState == WindowState.FullScreen));
        _host.LogDiagnostic(
            "goon: init sent (no server, no token, no discord; caps computed from the door refusals; "
            + "consent 720s/0.7/30000ms transcribed from GoonContracts.cs:97,:297,:108)");

        // The user's own images/videos as loopback /umedia/ URLs, over the SAME enumerator DTRH
        // and intake use (census §7.1.1 — "one enumerator for every web core"). Counts only in
        // the log: never a name, never a path.
        var media = DtrhUserMedia.Build(
            _participant.UserMediaRoot, _participant.Server.MediaOrigin, _host.LogDiagnostic);
        SendToPage(GoonProtocol.BuildManifest(media.Images, media.Videos, media.Skipped, media.Truncated));
        _host.LogDiagnostic(
            $"goon: manifest sent ({media.Images.Count} image(s), {media.Videos.Count} video(s), {media.Skipped} skipped; "
            + "received is an empty frame-shape stub — this build has no media channel to fill it)");

        SendToPage(GoonProtocol.BuildFullscreen(WindowState == WindowState.FullScreen));
        SetStatus("goon: init + manifest sent — practice is playable; four doors refuse (see the rail)");
    }

    /// <summary>Upstream's own fallback when no display name is set
    /// (<c>GoonHostService.cs:860-868</c>); this build has no display-name setting at all.</summary>
    internal const string DefaultDisplayName = "Player";

    /// <summary>Host→page: the synthetic <c>MessageEvent</c> dispatch on
    /// <c>window.chrome.webview</c> that DTRH and intake already use (SP-011 W4). The goon page's
    /// own listener is <c>bridge.js:66</c> and needs no shadow — which is why zero payload bytes
    /// are forked for this surface.</summary>
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

        var json = GoonProtocol.SerializeForPage(msg);
        _ = _web.InvokeScript($"window.chrome.webview.dispatchEvent(new MessageEvent('message',{{data:{json}}}))")
            .ContinueWith(
                t => _host.LogDiagnostic($"goon: host->page dispatch faulted: {t.Exception?.GetBaseException().Message}"),
                TaskContinuationOptions.OnlyOnFaulted);
    }

    private void SetStatus(string s) => Dispatcher.UIThread.Post(() => Status.Text = s);
}
