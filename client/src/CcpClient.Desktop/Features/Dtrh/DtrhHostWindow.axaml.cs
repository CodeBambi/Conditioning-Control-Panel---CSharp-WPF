using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Threading;
using CcpClient.Desktop.Capabilities;
using CcpClient.Desktop.Lifecycle;

namespace CcpClient.Desktop.Features.Dtrh;

/// <summary>
/// The DTRH host shell (slice b1; dtrh-admission.md §3/§5). Surface selection is driven
/// by the PROBED capability states (SP-006 — never an OS guess): Windows embedded
/// WebView2 vs Linux NativeWebDialog vs honest unsupported. Boot contract (WPF
/// archaeology, DtrhHostService.cs:166-211 + ChaosWebViewHost.cs:301-305): the host
/// queues host→page messages until the page's `ready`, then flushes init + manifest in
/// order and claims keyboard focus (DtrhHostService.cs:169-172). Host→page: Windows =
/// synthetic MessageEvent dispatch on window.chrome.webview (SP-011 W4, byte-identical);
/// Linux = §3.3 inbox enqueue (retained delivery = the pre-ready queue, replay-equivalent).
/// Page→host: WebMessageReceived on both (FIRST GATE proven on the dialog path).
/// </summary>
public partial class DtrhHostWindow : Window
{
    private readonly ApplicationHost _host;
    private readonly DtrhParticipant _dtrh;
    private readonly string _page;
    private readonly string? _fxDrive;
    private NativeWebView? _web;   // created programmatically, embedded path only (see axaml note)
    private NativeWebDialog? _dialog;
    private bool _sentBootMessages;
    private bool _engineLive;
    private int _heartbeats;
    private readonly CancellationTokenSource _closing = new();
    // SP-025 slice b3: the native effects owner + its router/backends (window-scoped
    // lifetime — constructed at Opened, torn down at Closing; DisposeAll :896 parity).
    private DtrhNativeEffects? _fx;
    private DtrhFxRouter? _router;
    private SoundFlowDtrhAudio? _audio;
    private DtrhVideoWindow? _videoWindow;

    /// <summary>Boot-matrix facts (headed harness + diagnostics). Content-free.</summary>
    public bool EngineLive => _engineLive;

    public int Heartbeats => _heartbeats;

    public string Surface { get; private set; } = "pending";

    public DtrhHostWindow(ApplicationHost host, string page = "index.html", int? slot = null, string? fxDrive = null)
    {
        _host = host;
        _dtrh = host.Participants.OfType<DtrhParticipant>().Single();
        _page = page;
        _fxDrive = fxDrive;
        InitializeComponent();
        if (slot is not null)
        {
            SetStatus($"dtrh: descending into slot {slot}");
            _host.LogDiagnostic($"dtrh: host window opening on slot {slot}");
        }

        Opened += (_, _) =>
        {
            InitNativeEffects();
            Begin();
            ScheduleFxDrive();
        };
        Closing += (_, _) =>
        {
            _closing.Cancel();
            TeardownNativeEffects();
            TryCloseDialog();
        };
    }

    private void Begin()
    {
        var capabilities = _host.Capabilities;
        var embedded = capabilities?.GetState(DtrhCapabilityProbes.EmbeddedCapability);
        var dialog = capabilities?.GetState(DtrhCapabilityProbes.DialogCapability);

        if (embedded is CapabilityState.Available)
        {
            Surface = "embedded";
            BeginEmbedded();
        }
        else if (dialog is CapabilityState.Available)
        {
            Surface = "dialog";
            BeginDialog();
        }
        else
        {
            // Honest unsupported (admission §5: no classic fallback, never a silent substitute).
            Surface = "unsupported";
            UnsupportedPanel.IsVisible = true;
            UnsupportedDetail.Text =
                $"capability {DtrhCapabilityProbes.EmbeddedCapability}: {Describe(embedded)}\n"
                + $"capability {DtrhCapabilityProbes.DialogCapability}: {Describe(dialog)}";
            SetStatus("dtrh: honest unsupported (no classic fallback)");
            _host.LogDiagnostic("dtrh: no admitted web surface available — unsupported surface shown");
            _host.LogDiagnostic("dtrh: " + UnsupportedDetail.Text.Replace("\n", " | "));
        }
    }

    // ---------- b3 native effects (SP-025) ----------

    /// <summary>The media roots the effects resolve against: the served payload assets
    /// (overlay-first, §4 mirrored host-side). The overlay assets dir is product-owned
    /// (harness staging for evidence lands there at RUN time — never in the read-only
    /// payload, never a Z:\ reference).</summary>
    private static string PayloadAssets => DtrhParticipant.MediaRoot;

    private static string OverlayAssets => Path.Combine(DtrhParticipant.OverlayRoot, "assets");

    private void InitNativeEffects()
    {
        _audio = new SoundFlowDtrhAudio(_host.LogDiagnostic);
        if (!_audio.TryInit(null, out var audioError))
        {
            // WPF parity (AudioService device-missing outcome): no device = audio disabled
            // for the session, never a crash. CreatePlayer guards log-and-drop downstream.
            _host.LogDiagnostic($"dtrh: audio backend init failed ({audioError}) — audio disabled this session");
        }

        var video = new LibVlcDtrhVideo(_host.LogDiagnostic, action => Dispatcher.UIThread.Post(action));
        _fx = new DtrhNativeEffects(_audio, video, new DtrhNativeEffectsOptions
        {
            SfxRoots = [Path.Combine(PayloadAssets, "bubbles", "sfx"), Path.Combine(OverlayAssets, "bubbles", "sfx")],
            WhisperRoots = [Path.Combine(PayloadAssets, "bubbles", "voices"), Path.Combine(OverlayAssets, "bubbles", "voices")],
            VideoRoots = [PayloadAssets, OverlayAssets],
            MasterVolume = 80, // the init literal this window sends (b2); the settings seam is b4.
        }, _host.LogDiagnostic);
        _fx.VideoStarted += OnFxVideoStarted;
        _fx.VideoEnded += OnFxVideoEnded;
        _fx.NotifySessionStart(); // Launch :71 parity — every session begins unfrozen/unducked
        _router = new DtrhFxRouter(_fx, _host.LogDiagnostic);
        _host.LogDiagnostic("dtrh: native effects up (SFX pool 8/drop, voice, vmem video, freeze)");
    }

    private void TeardownNativeEffects()
    {
        try { _videoWindow?.Close(); } catch { /* best-effort */ }
        _videoWindow = null;
        // DisposeAll :896 parity: NEVER leave a clip wedged paused if the window dies
        // mid-freeze — Teardown force-resumes before stopping.
        try { _fx?.Teardown(); } catch { /* best-effort */ }
        try { _fx?.Dispose(); } catch { /* best-effort */ }
        _fx = null;
        _router = null;
        // SoundFlow teardown is proven Δ0 handles/Δ0 threads (SP-017 A8); libvlc release
        // at exit stays SKIPPED (V3 — OS reclaims; unified-video row owns clean teardown).
        try { _audio?.Dispose(); } catch { /* best-effort */ }
        _audio = null;
    }

    /// <summary>payload-state {kind:'video', on} + covering window (DtrhHostService.cs:744
    /// parity): the page pauses/ducks while a native video covers it.</summary>
    private void OnFxVideoStarted(object? sender, EventArgs e)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => OnFxVideoStarted(sender, e));
            return;
        }

        SendToPage(DtrhProtocol.BuildPayloadState("video", true));
        if (_videoWindow is null && _fx is not null)
        {
            _videoWindow = new DtrhVideoWindow(_fx.Video, _host.LogDiagnostic);
            _videoWindow.Closed += (_, _) => _videoWindow = null;
            _videoWindow.Show(this);
        }

        _host.LogDiagnostic("dtrh: payload-state video on (covering window shown)");
    }

    /// <summary>Video ended/capped/stopped (DtrhHostService.cs:764-775 parity): tell the
    /// page, close the covering window, reclaim keyboard focus for the game.</summary>
    private void OnFxVideoEnded(object? sender, EventArgs e)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => OnFxVideoEnded(sender, e));
            return;
        }

        SendToPage(DtrhProtocol.BuildPayloadState("video", false));
        try { _videoWindow?.Close(); } catch { /* best-effort */ }
        _videoWindow = null;
        Activate();
        _web?.Focus();
        _host.LogDiagnostic("dtrh: payload-state video off (focus reclaimed)");
    }

    // ---------- b3 harness drive (--dtrh-fx-drive; harness-only) ----------

    /// <summary>HARNESS-ONLY (headed/WX evidence without gameplay — runs are b4-gated so
    /// the page cannot originate these messages in-slice): a timed script of RAW page
    /// JSON fed through the REAL parse+dispatch path (pre-approach consult item 7).
    /// Steps: <code>sfx:name[:scale]@t; payload:video|audio@t; freeze:on|off@t;
    /// vn:on|off@t; run-started@t; run-ended@t</code> (@t seconds, default spacing 4s).</summary>
    private void ScheduleFxDrive()
    {
        if (string.IsNullOrWhiteSpace(_fxDrive)) return;
        _host.LogDiagnostic($"dtrh: fx-drive armed (HARNESS-ONLY): {_fxDrive}");
        var steps = _fxDrive.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var index = 0;
        foreach (var step in steps)
        {
            index++;
            var at = step.IndexOf('@');
            var seconds = at >= 0 && double.TryParse(step[(at + 1)..], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var s) ? s : index * 4.0;
            var json = FxDriveStepToJson(at >= 0 ? step[..at] : step);
            var bare = at >= 0 ? step[..at] : step;
            if (json is null && bare.StartsWith("video-file:", StringComparison.Ordinal))
            {
                // HARNESS-ONLY non-protocol step: pin one NAMED pool file (staged real
                // media) through the same covering-video path — never a page message.
                var fileName = bare["video-file:".Length..];
                _ = Task.Delay(TimeSpan.FromSeconds(seconds), _closing.Token).ContinueWith(
                    t =>
                    {
                        if (t.IsCanceled) return;
                        _host.LogDiagnostic($"dtrh: fx-drive video-file '{fileName}' (HARNESS-ONLY, pool-resolved)");
                        Dispatcher.UIThread.Post(() => _fx?.FireVideoFromPool(fileName));
                    }, TaskScheduler.Default);
                continue;
            }

            if (json is null && bare.StartsWith("whisper-file:", StringComparison.Ordinal))
            {
                // HARNESS-ONLY non-protocol step: pin one NAMED pool whisper (staged long
                // clip for freeze evidence) through the same voice channel.
                var fileName = bare["whisper-file:".Length..];
                _ = Task.Delay(TimeSpan.FromSeconds(seconds), _closing.Token).ContinueWith(
                    t =>
                    {
                        if (t.IsCanceled) return;
                        _host.LogDiagnostic($"dtrh: fx-drive whisper-file '{fileName}' (HARNESS-ONLY, pool-resolved)");
                        Dispatcher.UIThread.Post(() => _fx?.PlayWhisperFromPool(fileName));
                    }, TaskScheduler.Default);
                continue;
            }

            if (json is null)
            {
                _host.LogDiagnostic($"dtrh: fx-drive step '{step}' unknown — skipped (harness)");
                continue;
            }

            _ = Task.Delay(TimeSpan.FromSeconds(seconds), _closing.Token).ContinueWith(
                t =>
                {
                    if (t.IsCanceled) return;
                    _host.LogDiagnostic($"dtrh: fx-drive injecting '{step}' (raw JSON through the real dispatch path)");
                    Dispatcher.UIThread.Post(() => HandleWebMessageBody(json));
                }, TaskScheduler.Default);
        }
    }

    private static string? FxDriveStepToJson(string step) => step switch
    {
        "freeze:on" => "{\"type\":\"freeze-state\",\"on\":true}",
        "freeze:off" => "{\"type\":\"freeze-state\",\"on\":false}",
        "vn:on" => "{\"type\":\"vn-speaking\",\"on\":true}",
        "vn:off" => "{\"type\":\"vn-speaking\",\"on\":false}",
        "payload:video" => "{\"type\":\"fire-payload\",\"kind\":\"video\",\"strength\":60,\"durationMult\":1.0}",
        "payload:audio" => "{\"type\":\"fire-payload\",\"kind\":\"audio\",\"strength\":60,\"durationMult\":1.0}",
        "run-started" => "{\"type\":\"run-started\",\"difficulty\":\"Gentle\",\"mode\":\"dtrh-web\"}",
        "run-ended" => "{\"type\":\"run-ended\",\"score\":0,\"durationSec\":1,\"difficulty\":\"Gentle\"}",
        _ when step.StartsWith("sfx:", StringComparison.Ordinal) => SfxDriveJson(step[4..]),
        _ => null,
    };

    private static string SfxDriveJson(string rest)
    {
        var parts = rest.Split(':');
        var name = System.Text.Json.JsonSerializer.Serialize(parts[0]);
        var scale = parts.Length > 1 && double.TryParse(parts[1], out var s) ? s : 0.6;
        // Invariant culture: a decimal-comma session culture would emit 0,6 → malformed
        // JSON (observed in run A; the SP-024 {0:N0} culture lesson's class).
        return $"{{\"type\":\"sfx\",\"name\":{name},\"scale\":{scale.ToString(System.Globalization.CultureInfo.InvariantCulture)}}}";
    }

    // ---------- Windows: embedded WebView2 ----------

    private void BeginEmbedded()
    {
        _web = new NativeWebView();
        _web.EnvironmentRequested += OnEnvironmentRequested;
        _web.AdapterCreated += (_, _) => _host.LogDiagnostic($"dtrh: AdapterCreated info='{SafeAdapterInfo()}'");
        _web.AdapterDestroyed += (_, _) => _host.LogDiagnostic("dtrh: AdapterDestroyed");
        _web.WebMessageReceived += OnWebMessage;
        _web.NavigationCompleted += OnNavigationCompleted;
        WebHost.Children.Add(_web);
        var url = _dtrh.PageUrl(_page);
        SetStatus("dtrh: navigating (embedded)");
        _host.LogDiagnostic($"dtrh: navigating embedded surface (page {_page})");
        _web.Source = new Uri(url);
    }

    private string SafeAdapterInfo()
    {
        try { return _web?.AdapterInfo?.ToString() ?? "(null)"; }
        catch (Exception ex) { return $"(AdapterInfo threw {ex.GetType().Name})"; }
    }

    private void OnEnvironmentRequested(object? sender, WebViewEnvironmentRequestedEventArgs args)
    {
        args.EnableDevTools = false;
        if (args is Avalonia.Platform.WindowsWebView2EnvironmentRequestedEventArgs wv2)
        {
            var dataRoot = Path.Combine(
                Path.GetDirectoryName(CompositionRoot.DefaultSettingsPath())!, "dtrh");
            wv2.UserDataFolder = Path.Combine(dataRoot, "wv2-profile");
            // WPF parity (DtrhHostService.cs:119-120): the game's audio bed / drift voice
            // must start without a click; SP-011 W10 verified the flag end-to-end.
            wv2.AdditionalBrowserArguments = "--autoplay-policy=no-user-gesture-required";
            _host.LogDiagnostic("dtrh: WebView2 UserDataFolder set; autoplay-policy=no-user-gesture-required");
        }
        else if (args is Avalonia.Platform.GtkWebViewEnvironmentRequestedEventArgs gtk)
        {
            var dataRoot = Path.Combine(
                Path.GetDirectoryName(CompositionRoot.DefaultSettingsPath())!, "dtrh");
            gtk.BaseDataDirectory = Path.Combine(dataRoot, "gtk-data");
            gtk.BaseCacheDirectory = Path.Combine(dataRoot, "gtk-cache");
            _host.LogDiagnostic("dtrh: GTK WebKit base dirs set");
        }
    }

    // ---------- Linux: NativeWebDialog (WebKitGTK toplevel) ----------

    private void BeginDialog()
    {
        var url = _dtrh.PageUrl(_page);
        _dialog = new NativeWebDialog { Title = "CCP — DTRH" };
        _dialog.EnvironmentRequested += OnEnvironmentRequested;
        _dialog.AdapterCreated += (_, _) => _host.LogDiagnostic("dtrh(dialog): AdapterCreated");
        _dialog.AdapterDestroyed += (_, _) => _host.LogDiagnostic("dtrh(dialog): AdapterDestroyed");
        _dialog.WebMessageReceived += OnWebMessage;
        _dialog.NavigationCompleted += OnNavigationCompleted;
        _dialog.Closing += (_, _) =>
        {
            _host.LogDiagnostic("dtrh(dialog): closing");
            Dispatcher.UIThread.Post(() =>
            {
                if (IsVisible) Close();
            });
        };
        _dialog.Source = new Uri(url);
        SetStatus("dtrh: dialog surface shown (NativeWebDialog)");
        _host.LogDiagnostic($"dtrh: showing NativeWebDialog surface (page {_page})");
        _dialog.Show(this);
    }

    private void TryCloseDialog()
    {
        try { _dialog?.Close(); } catch { /* best effort — teardown is idempotent */ }
        try { _dialog?.Dispose(); } catch { /* best effort */ }
        _dialog = null;
    }

    // ---------- probe page drive (boot-matrix transport checks) ----------

    private void OnNavigationCompleted(object? sender, WebViewNavigationCompletedEventArgs e)
    {
        _host.LogDiagnostic($"dtrh: NavigationCompleted success={e.IsSuccess} (surface {Surface})");
        if (_page != "probe.html" || !e.IsSuccess)
        {
            return;
        }

        // Ported spike probe sequence (SP-011 W4/W6), driven identically on both surfaces:
        // probe-h2p AFTER the page's module registered its handler; probe-buffered BEFORE
        // the +4s late registration — bridge.js must buffer and replay it. Results mirror
        // back via bridge.log (page→host), so the no-InvokeScript dialog path evidences
        // both directions.
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(2000, _closing.Token);
                // SendToPage marshals to the UI thread (WebView2 ExecuteScriptAsync is
                // apartment-bound; from a pool thread the call silently never lands).
                SendToPage(new { type = "probe-h2p", via = Surface == "embedded" ? "synthetic-dispatch" : "inbox" });
                SendToPage(new { type = "probe-buffered", via = "pre-handler-send" });
            }
            catch (OperationCanceledException) { /* window closed mid-probe */ }
        });
    }

    // ---------- boot contract ----------

    private void OnWebMessage(object? sender, WebMessageReceivedEventArgs e)
    {
        HandleWebMessageBody(e.Body ?? "");
    }

    /// <summary>The real parse+dispatch path for one page→host frame (also what the
    /// harness-only fx-drive feeds — consult item 7).</summary>
    private void HandleWebMessageBody(string body)
    {
        // Protocol v1 dispatcher (SP-024 slice b2): every frame parses to a TYPED outcome —
        // Handled, Deferred(slice), UnknownType, ForwardVersion, Malformed. Never silent,
        // never crashes. Presence+shape logging only (§4.8 sensitive-logging ban).
        switch (DtrhProtocol.ParsePageMessage(body))
        {
            case DtrhProtocol.DtrhPageParseResult.Parsed parsed:
                DispatchPageMessage(parsed.Message);
                return;
            case DtrhProtocol.DtrhPageParseResult.UnknownType unknown:
                // Transport-probe messages (boot-matrix harness, not product protocol)
                // surface verbatim as both-directions evidence.
                if (unknown.Type.StartsWith("probe-", StringComparison.Ordinal))
                {
                    _host.LogDiagnostic($"dtrh: {body}");
                }
                else
                {
                    _host.LogDiagnostic($"dtrh: unknown page message type '{unknown.Type}' — tolerated (typed, not dropped)");
                }

                return;
            case DtrhProtocol.DtrhPageParseResult.ForwardVersion forward:
                _host.LogDiagnostic(
                    $"dtrh: page message '{forward.Type}' declares protocol {forward.Protocol} > {DtrhProtocol.Version} — tolerated (forward-version, not acted on)");
                return;
            case DtrhProtocol.DtrhPageParseResult.Malformed malformed:
                _host.LogDiagnostic($"dtrh: malformed page message ({malformed.Reason}) — tolerated");
                return;
        }
    }

    private void DispatchPageMessage(DtrhProtocol.DtrhPageMessage message)
    {
        if (DtrhProtocol.Classify(message) is DtrhProtocol.DtrhDispatchClass.Deferred deferred)
        {
            // b3 run-boundary hygiene (pre-approach consult item 4): run-started/run-ended
            // STAY Deferred(b4), but the stale-freeze/stale-duck cleanup (WPF run-started
            // :252/:259, run-ended :513) is a b3 safety invariant — invoked BEFORE the
            // typed-deferral log.
            if (_router?.TryRunBoundaryHygiene(message) == true)
            {
                _host.LogDiagnostic("dtrh: run-boundary freeze/duck hygiene applied (message stays Deferred(b4))");
            }

            _host.LogDiagnostic($"dtrh: '{message.GetType().Name}' deferred to slice {deferred.Slice} (typed, not dropped)");
            return;
        }

        switch (message)
        {
            case DtrhProtocol.DtrhPageMessage.Heartbeat:
                _heartbeats++;
                if (_heartbeats % 15 == 1) _host.LogDiagnostic($"dtrh: heartbeat #{_heartbeats}");
                return;
            case DtrhProtocol.DtrhPageMessage.Log log:
                _host.LogDiagnostic($"dtrh page log: {log.Msg}");
                if (log.Msg?.Contains("engine live") == true)
                {
                    _engineLive = true;
                    _host.LogDiagnostic("dtrh: ENGINE LIVE");
                    SetStatus("dtrh: ENGINE LIVE");
                }

                return;
            case DtrhProtocol.DtrhPageMessage.Ready:
                _host.LogDiagnostic("dtrh: ready received — flushing init+manifest");
                SendBootMessages();
                return;
            case DtrhProtocol.DtrhPageMessage.Exit:
                _host.LogDiagnostic("dtrh: exit received — closing");
                Close();
                return;
            case DtrhProtocol.DtrhPageMessage.FullscreenSet fullscreenSet:
                WindowState = fullscreenSet.On ? WindowState.FullScreen : WindowState.Normal;
                _host.LogDiagnostic($"dtrh: fullscreen-set on={fullscreenSet.On} -> WindowState={WindowState}");
                // WPF parity (DtrhHostService.cs:430): echo the resulting state so the
                // page's dock button + Esc ladder stay in sync.
                SendToPage(DtrhProtocol.BuildFullscreen(WindowState == WindowState.FullScreen));
                return;
            case DtrhProtocol.DtrhPageMessage.BootError bootError:
                // Typed non-silent outcome (Step 1 consult): the WPF reaction (classic-game
                // fallback) is BANNED in greenfield (admission §5); the page already shows
                // its own honest no-WebGL surface (boot.js:82-101). Host closes with a
                // diagnostic — never a silent black window.
                _host.LogDiagnostic($"dtrh: boot-error from page ({(bootError.Msg is { Length: > 0 } m ? m : "no detail")}) — closing honestly (no classic fallback)");
                SetStatus("dtrh: page reported boot-error — closed honestly");
                Close();
                return;
            // SP-025 slice b3: the native-effects messages route to REAL effects.
            case DtrhProtocol.DtrhPageMessage.Sfx:
            case DtrhProtocol.DtrhPageMessage.FirePayload:
            case DtrhProtocol.DtrhPageMessage.FreezeState:
            case DtrhProtocol.DtrhPageMessage.VnSpeaking:
                if (_router is null)
                {
                    _host.LogDiagnostic($"dtrh: '{message.GetType().Name}' arrived before native effects init — logged, not acted on");
                    return;
                }

                _router.Handle(message);
                return;
        }
    }

    /// <summary>
    /// The WPF boot contract (archaeology): init first, manifest second, in order, after
    /// ready. b1 sends ONLY these two (meta/loom-list/favorites are b2…b4).
    /// </summary>
    private void SendBootMessages()
    {
        if (_sentBootMessages) return;
        _sentBootMessages = true;

        // Focus claim at ready (DtrhHostService.cs:169-172): keyboard focus does not land
        // in the web child on a fresh launch until a click — claim it now so ESC works.
        Activate();
        _web?.Focus();
        if (_web is not null)
        {
            // Behavioral evidence of the claim (SP-011 W14 class): the page reports whether
            // keyboard focus actually reached the document.
            _ = _web.InvokeScript("document.hasFocus()")
                .ContinueWith(
                    t => _host.LogDiagnostic(t.IsFaulted
                        ? $"dtrh: focus check faulted: {t.Exception?.GetBaseException().Message}"
                        : $"dtrh: focus claimed at ready; document.hasFocus()={t.Result}"),
                    TaskScheduler.Default);
        }

        SendToPage(DtrhProtocol.BuildInit(
            masterVolume: 80,
            modId: "builtin-sissyhypno",
            modContent: null,
            runSetup: new DtrhProtocol.DtrhRunSetup(
                Difficulty: "Easy",
                DurationSec: 180,
                WaveCount: 5,
                Motion: "Mixed",
                EnabledVariants: null,
                EffectIntensity: 0.85,
                ColorFlashes: true,
                BoonDraftEnabled: true,
                AllowCurses: true,
                DartersEnabled: true,
                Key1: "Q",
                Key2: "E"),
            m2Test: false));
        SendToPage(DtrhProtocol.BuildManifest(
            images: [new DtrhProtocol.DtrhManifestEntry("bubble.png", $"{_dtrh.Server.MediaOrigin}/media/bubbles/bubble.png")],
            videos: [new DtrhProtocol.DtrhManifestEntry("spiral.webm", $"{_dtrh.Server.MediaOrigin}/media/bubbles/spiral.webm")],
            skipped: 0,
            truncated: false));
        SetStatus("dtrh: init+manifest sent");
    }

    /// <summary>
    /// Host→page (admission §3.2): Windows embedded = synthetic MessageEvent dispatch on
    /// window.chrome.webview (SP-011 W4/W6 proven, byte-identical — never unified onto
    /// polling); Linux dialog = §3.3 inbox (retained seq delivery, replay-equivalent).
    /// </summary>
    public void SendToPage(object msg)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => SendToPage(msg));
            return;
        }

        var json = DtrhProtocol.SerializeForPage(msg);
        if (Surface == "embedded")
        {
            _ = _web!.InvokeScript($"window.chrome.webview.dispatchEvent(new MessageEvent('message',{{data:{json}}}))")
                .ContinueWith(
                    t => _host.LogDiagnostic($"dtrh: host->page dispatch faulted: {t.Exception?.GetBaseException().Message}"),
                    TaskContinuationOptions.OnlyOnFaulted);
        }
        else
        {
            _dtrh.Inbox.Enqueue(json);
        }
    }

    private void SetStatus(string s) => Dispatcher.UIThread.Post(() => Status.Text = s);

    private static string Describe(CapabilityState? state) => state switch
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
}
