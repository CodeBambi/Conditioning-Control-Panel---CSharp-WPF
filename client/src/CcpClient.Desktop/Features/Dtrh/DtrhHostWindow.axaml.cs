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
    // SP-026 slice b4: the meta-progression engine (ops/payout/request-run/asset-stats)
    // bound to the descended slot; test mode (--dtrh-m2test) clones in memory.
    private readonly int _slot;
    private readonly bool _m2Test;
    private DtrhMeta? _meta;
    private DtrhAssetStats? _assetStats;
    private DtrhLoom? _loom;
    // SP-027 slice b5: the session watchdog (coordinator-owned — survives relaunch),
    // the native ProcessFailed subscription (Windows embedded only — W17 immediate
    // route), the 5s heartbeat watch, and the run-active gate for the 10s/20s limits.
    private readonly DtrhWatchdog? _watchdog;
    private DispatcherTimer? _watchTimer;
    private DtrhProcessFailed.DtrhProcessFailedSignal? _processFailedSignal;
    private bool _runActive;
    // b5 graceful exit (window-scoped flow) + stale-profile retry latch (retry ONCE,
    // never a crash loop).
    private readonly DtrhExitFlow _exitFlow = new();
    private DispatcherTimer? _exitTimer;
    private bool _recoveryClosing;
    private bool _profileRetryUsed;
    // SP-027 b5 HARNESS-ONLY: --dtrh-kill-renderers arms the W17 renderer-kill injection
    // at engine-live (and again on the relaunched instance → exhaustion evidence).
    private readonly bool _killRenderers;

    /// <summary>Boot-matrix facts (headed harness + diagnostics). Content-free.</summary>
    public bool EngineLive => _engineLive;

    public int Heartbeats => _heartbeats;

    public string Surface { get; private set; } = "pending";

    /// <summary>Raised (UI thread) when the watchdog demands recovery: Relaunch-once or
    /// Exhausted-honest-close. The coordinator owns the acting (window recreation).</summary>
    public event Action<DtrhWatchdog.DtrhRecoveryOutcome>? RecoveryRequested;

    public DtrhHostWindow(ApplicationHost host, string page = "index.html", int? slot = null, string? fxDrive = null, bool m2Test = false,
        DtrhWatchdog? watchdog = null, bool killRenderers = false)
    {
        _host = host;
        _watchdog = watchdog;
        _killRenderers = killRenderers;
        _dtrh = host.Participants.OfType<DtrhParticipant>().Single();
        _page = page;
        _fxDrive = fxDrive;
        _slot = slot ?? host.Participants.OfType<DtrhSaveSlots>().Single().ActiveSlot;
        _m2Test = m2Test;
        InitializeComponent();
        if (slot is not null)
        {
            SetStatus($"dtrh: descending into slot {slot}");
            _host.LogDiagnostic($"dtrh: host window opening on slot {slot}");
        }

        if (m2Test)
        {
            _host.LogDiagnostic("dtrh: M2 TEST MODE — meta engine clones in memory, the real save is never touched (HARNESS-ONLY)");
        }

        Opened += (_, _) =>
        {
            InitMetaEngine();
            InitNativeEffects();
            Begin();
            ScheduleFxDrive();
            StartWatchTimer();
        };
        Closing += (_, e) =>
        {
            // b5 graceful exit (WPF CloseActive :149-160): a LIVE page gets the wind-down
            // request + bounded exit-done wait; everything else tears down now. Recovery
            // closes skip the wind-down (the page is dead — WPF Recover :858 calls
            // DisposeAll directly, no end-run).
            if (!_recoveryClosing && !_exitFlow.Exiting && _watchdog?.IsLive == true
                && _exitFlow.RequestClose(pageLive: true) == DtrhExitFlow.DtrhExitAction.RequestWindDown)
            {
                e.Cancel = true; // stay open until exit-done / the 1200ms force-close
                _watchdog?.BeginExit();
                _host.LogDiagnostic("dtrh: graceful exit — end-run posted to the page; bounded exit-done wait armed (1200ms, WPF :880)");
                SendToPage(new { type = "end-run", reason = "host" });
                ArmExitTimer();
                return;
            }

            _closing.Cancel();
            try { _watchTimer?.Stop(); } catch { /* best-effort */ }
            _watchTimer = null;
            try { _exitTimer?.Stop(); } catch { /* best-effort */ }
            _exitTimer = null;
            // Detach BEFORE the adapter dies (detach on a zombie is tolerated anyway);
            // MarkDead parks the watch without spending the relaunch.
            try { _processFailedSignal?.Dispose(); } catch { /* best-effort */ }
            _processFailedSignal = null;
            _watchdog?.MarkDead();
            try { _meta?.FlushSave(); } catch { /* best-effort — teardown is idempotent */ }
            TeardownNativeEffects();
            TryCloseDialog();
        };
    }

    /// <summary>b4: bind the meta engine to the descended slot's store (the b2 slot
    /// document rides SP-005 machinery — no parallel save file, no schema bump).
    /// Broadcast = SendToPage (meta snapshots answer applied ops).</summary>
    private void InitMetaEngine()
    {
        var slots = _host.Participants.OfType<DtrhSaveSlots>().Single();
        _assetStats = new DtrhAssetStats(slots.AssetStatsStore, _host.LogDiagnostic);
        _meta = new DtrhMeta(
            slots.StoreFor(_slot),
            slots.IndexStore,
            _assetStats,
            SendToPage,
            _host.LogDiagnostic,
            _m2Test,
            slots.SlotFilePath(_slot));
        // b4: the Loom store (<dataDir>/Spirals — DtrhLoomStore.cs:28 parity).
        _loom = new DtrhLoom(_dtrh.SpiralsRoot, _host.LogDiagnostic);
        _host.LogDiagnostic($"dtrh: meta engine bound to slot {_slot}{(_m2Test ? " (TEST clone)" : "")}");
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
            // b4: the user-media videos dir joins the native pool (WPF parity: native
            // payloads play the user's pool; DtrhHostService EffectPayloadFactory).
            VideoRoots = [PayloadAssets, OverlayAssets, DtrhUserMedia.VideosFolder(_dtrh.UserMediaRoot)],
            // Media-logging rule (packet framing c): anything under the user-media root
            // logs presence+shape ONLY — never a filename (SP-018 V5 class).
            PresenceOnlyRoots = [_dtrh.UserMediaRoot],
            MasterVolume = 80, // the init literal this window sends (b2); the settings seam is a named limit.
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

            if (json is null && bare.StartsWith("loom-file:", StringComparison.Ordinal))
            {
                // HARNESS-ONLY non-protocol step: read a staged GIF (overlay staging,
                // product code never references Z:\) and send the REAL loom-save message
                // with its base64 through the REAL parse+dispatch path (the video-file:/
                // whisper-file: precedent). The GIF bytes + message shape are real; only
                // the encoder-worker origin is harnessed (headed evidence can't drive the
                // Loom pane's encoder deterministically).
                var fileName = bare["loom-file:".Length..];
                _ = Task.Delay(TimeSpan.FromSeconds(seconds), _closing.Token).ContinueWith(
                    t =>
                    {
                        if (t.IsCanceled) return;
                        try
                        {
                            var staged = Path.Combine(DtrhParticipant.OverlayRoot, "loom", fileName);
                            var gifBytes = File.ReadAllBytes(staged);
                            var loomName = Path.GetFileNameWithoutExtension(fileName);
                            var loomJson = "{\"type\":\"loom-save\",\"name\":" + JsonSerializer.Serialize(loomName)
                                + ",\"overwrite\":true,\"params\":{\"arms\":4,\"turns\":2,\"duty\":0.5,\"speed\":1,\"style\":\"classic\",\"direction\":1,\"colors\":[\"#ff5fa2\",\"#8a5cff\"]},\"gifBase64\":\""
                                + Convert.ToBase64String(gifBytes) + "\"}";
                            _host.LogDiagnostic($"dtrh: fx-drive loom-file staged ({gifBytes.Length} bytes) — real loom-save through the real dispatch path (HARNESS-ONLY)");
                            Dispatcher.UIThread.Post(() => HandleWebMessageBody(loomJson));
                        }
                        catch (Exception ex)
                        {
                            _host.LogDiagnostic($"dtrh: fx-drive loom-file failed ({ex.GetType().Name}) — harness no-op");
                        }
                    }, TaskScheduler.Default);
                continue;
            }

            if (json is null && bare == "probe-missing-media")
            {
                // SP-027 b5 HARNESS-ONLY missing-media injection (W19 class): point the
                // probe at a media URL that does not exist — the server answers a typed
                // 404 (CORS-on-errors), the probe logs the LOAD ERROR, the page survives.
                _ = Task.Delay(TimeSpan.FromSeconds(seconds), _closing.Token).ContinueWith(
                    t =>
                    {
                        if (t.IsCanceled) return;
                        _host.LogDiagnostic("dtrh: fx-drive probe-missing-media (HARNESS-ONLY missing-media injection, W19 class)");
                        Dispatcher.UIThread.Post(() => SendToPage(new
                        {
                            type = "probe-img",
                            kind = "image",
                            url = $"{_dtrh.Server.MediaOrigin}/umedia/images/__b5_missing__.png",
                        }));
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
        // SP-027 b5: a page-exit message through the real dispatch path WITHOUT a real
        // wind-down behind it — the bounded exit-done wait expires & the watchdog forces
        // the close (the timeout cell of the exit matrix; the fast path is real ESC-hold).
        "exit" => "{\"type\":\"exit\"}",
        // SP-026 b4: a REAL request-run through the real dispatch path (the gating message
        // for runs — freeze bubbles only exist in-run). 150s, drafts OFF (a legit setup
        // toggle — draft holds park the field up to 60s/run and eat the catch window);
        // effect share ramps with intensity (chaosRun.js:3178-3181), the deep chambers
        // deal the freeze bubbles.
        "request-run" => "{\"type\":\"request-run\",\"setup\":{\"difficulty\":\"Easy\",\"durationSec\":150,\"waveCount\":4,\"motion\":\"Mixed\",\"effectIntensity\":0.85,\"colorFlashes\":true,\"boonDraftEnabled\":false,\"allowCurses\":true,\"dartersEnabled\":true,\"key1\":\"Q\",\"key2\":\"E\",\"enabledVariants\":null}}",
        // SP-026 b4: a deterministic full-field run-ended for the payout banking proof
        // (the page's own run-ended is the gameplay path; this pins the math evidence).
        "run-ended-full" => "{\"type\":\"run-ended\",\"score\":5200,\"durationSec\":180,\"elapsedSec\":172,\"difficulty\":\"Gentle\",\"difficultyMult\":1.0,\"sparkGainMult\":1.0,\"bestCombo\":11,\"defused\":7,\"trickleDrops\":3,\"dripFeedMaxed\":false,\"sessionStats\":{\"bubblesPopped\":57}}",
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
        _web.AdapterCreated += (_, _) =>
        {
            _host.LogDiagnostic($"dtrh: AdapterCreated info='{SafeAdapterInfo()}'");
            AttachProcessFailedSignal();
        };
        _web.AdapterDestroyed += (_, _) => _host.LogDiagnostic("dtrh: AdapterDestroyed");
        _web.WebMessageReceived += OnWebMessage;
        _web.NavigationCompleted += OnNavigationCompleted;
        WebHost.Children.Add(_web);
        var url = _dtrh.PageUrl(_page);
        SetStatus("dtrh: navigating (embedded)");
        _host.LogDiagnostic($"dtrh: navigating embedded surface (page {_page})");
        try
        {
            _web.Source = new Uri(url);
        }
        catch (Exception ex) when (DtrhProfileLock.IsStaleProfileLock(ex) && !_profileRetryUsed)
        {
            // b5: the 0x800700AA stale-profile-lock class (SP-023 surprise #7) — recover
            // honestly (kill the stale children holding OUR profile) and retry ONCE.
            _profileRetryUsed = true;
            _host.LogDiagnostic($"dtrh: navigation threw the stale-profile-lock class (0x800700AA) — recovering (typed, never silent)");
            LogProfileRecovery(DtrhProfileLock.TryRecover(DtrhProfileLock.WebView2ProfileDir()));
            _web.Source = new Uri(url); // a second failure stands typed (never a crash loop)
        }
    }

    private void LogProfileRecovery(DtrhProfileLock.DtrhProfileRecovery recovery)
    {
        switch (recovery)
        {
            case DtrhProfileLock.DtrhProfileRecovery.Recovered recovered:
                _host.LogDiagnostic($"dtrh: stale-profile recovery — killed {recovered.KilledCount} stale msedgewebview2 child(ren) holding our profile");
                break;
            case DtrhProfileLock.DtrhProfileRecovery.NothingToKill:
                _host.LogDiagnostic("dtrh: stale-profile recovery — no profile-matched children found (lock class without stale children; typed)");
                break;
            case DtrhProfileLock.DtrhProfileRecovery.Unsupported unsupported:
                _host.LogDiagnostic($"dtrh: stale-profile recovery unavailable — {unsupported.Detail}");
                break;
            case DtrhProfileLock.DtrhProfileRecovery.Failed failed:
                _host.LogDiagnostic($"dtrh: stale-profile recovery FAILED — {failed.Detail}");
                break;
        }
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
            wv2.UserDataFolder = DtrhProfileLock.WebView2ProfileDir();
            // WPF parity (DtrhHostService.cs:119-120): the game's audio bed / drift voice
            // must start without a click; SP-011 W10 verified the flag end-to-end.
            wv2.AdditionalBrowserArguments = "--autoplay-policy=no-user-gesture-required";
            _host.LogDiagnostic("dtrh: WebView2 UserDataFolder set; autoplay-policy=no-user-gesture-required");
        }
        else if (args is Avalonia.Platform.GtkWebViewEnvironmentRequestedEventArgs gtk)
        {
            var dataRoot = DtrhProfileLock.DtrhDataRoot();
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
        _host.LogDiagnostic(
            "dtrh: native ProcessFailed signal UNAVAILABLE (unsupported-platform) on the WebKitGTK dialog path — "
            + "heartbeat watchdog is the only net (named limit; W17-class detection lands after the black-but-beating window + threshold, never claimed fast)");
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
                SendProbeMedia();
            }
            catch (OperationCanceledException) { /* window closed mid-probe */ }
        });
    }

    /// <summary>SP-026 b4 HARNESS-ONLY: on the probe page, send every SERVED media URL
    /// (Loom spirals + the user-media manifest) as probe-img messages so the probe renders
    /// them in the same engine — the pixel-verifiable serve→display proof for media that
    /// the pane-gated Loom rack can't reach deterministically in a scripted drive.
    /// Presence+shape only in logs (the URLs are never logged).</summary>
    private void SendProbeMedia()
    {
        var sent = 0;
        if (_loom is not null)
        {
            foreach (var spiral in _loom.List())
            {
                SendToPage(new { type = "probe-img", kind = "image", url = $"{_dtrh.Server.MediaOrigin}/spirals/loom_{spiral.Slug}.gif" });
                sent++;
            }
        }

        var manifest = DtrhUserMedia.Build(_dtrh.UserMediaRoot, _dtrh.Server.MediaOrigin, _host.LogDiagnostic);
        foreach (var image in manifest.Images.Take(2))
        {
            SendToPage(new { type = "probe-img", kind = "image", url = image.Url });
            sent++;
        }

        foreach (var video in manifest.Videos.Take(1))
        {
            SendToPage(new { type = "probe-img", kind = "video", url = video.Url });
            sent++;
        }

        _host.LogDiagnostic($"dtrh: probe-img sent ({sent} served url(s) — loom + user media, HARNESS-ONLY)");
    }

    /// <summary>Recovery teardown (watchdog relaunch / exhaustion): skip the graceful
    /// wind-down — the page is dead or wedged; WPF Recover :858-876 calls DisposeAll
    /// directly.</summary>
    public void CloseForRecovery()
    {
        _recoveryClosing = true;
        Close();
    }

    /// <summary>The 1200ms bounded exit-done wait (WPF ArmExitWatchdog :877-882,
    /// "watchdog-force after 1200ms"). One-shot; re-armed if the page re-sends exit.</summary>
    private void ArmExitTimer()
    {
        try { _exitTimer?.Stop(); } catch { /* best-effort */ }
        _exitTimer = new DispatcherTimer { Interval = DtrhWatchdog.ExitDoneWait };
        _exitTimer.Tick += (_, _) =>
        {
            try { _exitTimer?.Stop(); } catch { /* best-effort */ }
            if (_exitFlow.Timeout() == DtrhExitFlow.DtrhExitAction.ForceClose)
            {
                _host.LogDiagnostic("dtrh: exit-done wait elapsed (1200ms) — watchdog-FORCED close (page wedged mid-shutdown; WPF :881)");
                Close();
            }
        };
        _exitTimer.Start();
    }

    // ---------- b5 watchdog + native process-failure signal (SP-027) ----------

    /// <summary>The 5s heartbeat watch (WPF :819-841). Ticks no-op until the page reports
    /// ready (WPF IsReady guard :830) — a still-loading page can't false-trip.</summary>
    private void StartWatchTimer()
    {
        if (_watchdog is null)
        {
            return;
        }

        _watchTimer = new DispatcherTimer { Interval = DtrhWatchdog.WatchInterval };
        _watchTimer.Tick += (_, _) =>
        {
            var outcome = _watchdog.Tick(DateTimeOffset.UtcNow, _runActive);
            if (outcome is not null)
            {
                HandleRecoveryOutcome(outcome);
            }
        };
        _watchTimer.Start();
    }

    /// <summary>Subscribe the native ProcessFailed signal at AdapterCreated (consult trap:
    /// the platform handle is useless before the adapter exists; re-subscribe happens
    /// naturally — a relaunch recreates the window + adapter). The pointer chain is
    /// verified (IWindowsWebView2PlatformHandle, Avalonia.Controls.WebView 12.0.1); where
    /// the platform cannot deliver the signal the typed Unavailable is logged — the
    /// heartbeat watchdog is the only net there, never a faked signal.</summary>
    private void AttachProcessFailedSignal()
    {
        if (_watchdog is null)
        {
            return;
        }

        if (_web?.TryGetPlatformHandle() is Avalonia.Platform.IWindowsWebView2PlatformHandle wv2)
        {
            switch (DtrhProcessFailed.TryAttach(wv2.CoreWebView2, OnNativeProcessFailed))
            {
                case DtrhProcessFailed.AttachOutcome.Attached attached:
                    _processFailedSignal = attached.Signal;
                    _host.LogDiagnostic("dtrh: native ProcessFailed signal ATTACHED (W17 immediate route, WebView2 slot-25 subscription)");
                    break;
                case DtrhProcessFailed.AttachOutcome.Unavailable unavailable:
                    _host.LogDiagnostic(
                        $"dtrh: native ProcessFailed signal UNAVAILABLE ({unavailable.Code}) — {unavailable.Detail}; heartbeat watchdog is the only net");
                    break;
            }
        }
        else
        {
            _host.LogDiagnostic(
                "dtrh: native ProcessFailed signal UNAVAILABLE (unsupported-platform) — the platform handle is not "
                + "IWindowsWebView2PlatformHandle; heartbeat watchdog is the only net (named limit, never faked)");
        }
    }

    /// <summary>Native ProcessFailed callback (arrives on the UI thread — COM apartment;
    /// posted defensively anyway). WPF OnProcessFailed :850-852 parity.</summary>
    private void OnNativeProcessFailed(int kind)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => OnNativeProcessFailed(kind));
            return;
        }

        _host.LogDiagnostic($"dtrh: native ProcessFailed — {DtrhWatchdog.KindName(kind)} (immediate detection; AdapterDestroyed never fires — W17)");
        if (_watchdog is null)
        {
            return;
        }

        var outcome = _watchdog.OnProcessFailed(kind);
        if (outcome is null)
        {
            _host.LogDiagnostic("dtrh: ProcessFailed dropped by the recovery-episode latch / guards (same episode — never a double-relaunch)");
            return;
        }

        HandleRecoveryOutcome(outcome);
    }

    /// <summary>HARNESS-ONLY (--dtrh-kill-renderers; the W17 injection): kill every
    /// msedgewebview2 child holding OUR profile — the exact SP-011 kill-renderer.ps1
    /// shape — once the engine is live (first kill), and again on the RELAUNCHED
    /// instance (second kill → the one relaunch is spent → typed exhaustion → honest
    /// close). The watchdog/ProcessFailed detection + relaunch-once machine is the
    /// product code under test; this flag only pulls the trigger.</summary>
    private void ScheduleKillRenderersInjection()
    {
        if (!_killRenderers)
        {
            return;
        }

        var relaunched = _watchdog?.RelaunchSpent == true;
        var delay = relaunched ? 8 : 12;
        _host.LogDiagnostic(
            $"dtrh: HARNESS kill-renderers armed at +{delay}s ({(relaunched ? "SECOND kill — exhaustion expected" : "first kill — relaunch-once expected")})");
        _ = Task.Delay(TimeSpan.FromSeconds(delay), _closing.Token).ContinueWith(
            t =>
            {
                if (t.IsCanceled) return;
                _host.LogDiagnostic("dtrh: HARNESS kill-renderers FIRING (killing profile-matched msedgewebview2 children — W17 injection)");
                var recovery = DtrhProfileLock.TryRecover(DtrhProfileLock.WebView2ProfileDir());
                _host.LogDiagnostic($"dtrh: HARNESS kill-renderers outcome: {recovery}");
            }, TaskScheduler.Default);
    }

    private void HandleRecoveryOutcome(DtrhWatchdog.DtrhRecoveryOutcome outcome)
    {
        switch (outcome)
        {
            case DtrhWatchdog.DtrhRecoveryOutcome.Relaunch relaunch:
                _host.LogDiagnostic($"dtrh: watchdog demands RELAUNCH ({relaunch.Reason}) — generation {relaunch.Generation}");
                break;
            case DtrhWatchdog.DtrhRecoveryOutcome.Exhausted exhausted:
                _host.LogDiagnostic($"dtrh: watchdog demands EXHAUSTED close ({exhausted.Reason}) — never a restart loop");
                break;
        }

        RecoveryRequested?.Invoke(outcome);
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
            _host.LogDiagnostic($"dtrh: '{message.GetType().Name}' deferred to slice {deferred.Slice} (typed, not dropped)");
            return;
        }

        switch (message)
        {
            case DtrhProtocol.DtrhPageMessage.Heartbeat:
                _heartbeats++;
                _watchdog?.Heartbeat(DateTimeOffset.UtcNow);
                if (_heartbeats % 15 == 1) _host.LogDiagnostic($"dtrh: heartbeat #{_heartbeats}");
                return;
            case DtrhProtocol.DtrhPageMessage.Log log:
                _host.LogDiagnostic($"dtrh page log: {log.Msg}");
                if (log.Msg?.Contains("engine live") == true)
                {
                    _engineLive = true;
                    _host.LogDiagnostic("dtrh: ENGINE LIVE");
                    SetStatus("dtrh: ENGINE LIVE");
                    ScheduleKillRenderersInjection();
                }

                return;
            case DtrhProtocol.DtrhPageMessage.Ready:
                _host.LogDiagnostic("dtrh: ready received — flushing init+manifest");
                _watchdog?.MarkLive(DateTimeOffset.UtcNow);
                SendBootMessages();
                return;
            case DtrhProtocol.DtrhPageMessage.Exit:
                // b5 (WPF :312-314): the page winds itself down (boot.js:197-198 sends
                // exit then exit-done back-to-back) — arm the bounded wait, never close
                // before exit-done or the 1200ms force.
                _host.LogDiagnostic("dtrh: exit received — page winding down; bounded exit-done wait armed (1200ms)");
                _watchdog?.BeginExit();
                if (_exitFlow.PageExit() == DtrhExitFlow.DtrhExitAction.ArmBoundedWait)
                {
                    ArmExitTimer();
                }

                return;
            case DtrhProtocol.DtrhPageMessage.ExitDone:
                // b5 (WPF :316): the page finished winding down — close now (fast path).
                _host.LogDiagnostic("dtrh: exit-done received — closing (graceful fast path)");
                if (_exitFlow.ExitDone() == DtrhExitFlow.DtrhExitAction.CloseNow && IsVisible)
                {
                    Close();
                }

                return;
            case DtrhProtocol.DtrhPageMessage.Pong:
                // b5 (WPF :318-320): pong counts as a heartbeat stamp.
                _watchdog?.Heartbeat(DateTimeOffset.UtcNow);
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
            // SP-026 slice b4: progression/payout + media stats route to the meta engine.
            case DtrhProtocol.DtrhPageMessage.MetaCommand metaCommand:
                if (_meta is null)
                {
                    _host.LogDiagnostic("dtrh: 'MetaCommand' arrived before meta engine init — logged, not acted on");
                    return;
                }

                _meta.HandleMetaCommand(metaCommand.Raw);
                return;
            case DtrhProtocol.DtrhPageMessage.RequestRun requestRun:
                if (_meta is null)
                {
                    _host.LogDiagnostic("dtrh: 'RequestRun' arrived before meta engine init — logged, not acted on");
                    return;
                }

                var runConfig = _meta.OnRequestRun(requestRun.Setup);
                SendToPage(DtrhProtocol.BuildRunConfig(
                    JsonSerializer.SerializeToElement(runConfig, PageJsonOptions)));
                return;
            case DtrhProtocol.DtrhPageMessage.RunStarted runStarted:
                // WPF order (:252/:258-259): stale-duck/freeze hygiene FIRST, then the
                // run boundary — the SAME b3 router entry, now inside the real handler.
                _router?.TryRunBoundaryHygiene(runStarted);
                _runActive = true;
                _meta?.OnRunStarted(runStarted.Difficulty, runStarted.Mode);
                return;
            case DtrhProtocol.DtrhPageMessage.RunEnded runEnded:
                // WPF order (:513): ApplyWorldFreeze(false) is the FIRST statement of
                // OnRunEnded — a run ending mid-freeze never wedges native video/voice.
                _router?.TryRunBoundaryHygiene(runEnded);
                _runActive = false;
                if (_meta is null)
                {
                    _host.LogDiagnostic("dtrh: 'RunEnded' arrived before meta engine init — logged, not acted on");
                    return;
                }

                var payout = _meta.OnRunEnded(runEnded.Raw);
                SendToPage(DtrhProtocol.BuildPayoutResult(payout));
                return;
            case DtrhProtocol.DtrhPageMessage.AssetStats assetStats:
                _meta?.OnAssetStats(assetStats.Raw);
                return;
            case DtrhProtocol.DtrhPageMessage.LoomSave loomSave:
            {
                if (_loom is null)
                {
                    _host.LogDiagnostic("dtrh: 'LoomSave' arrived before loom init — logged, not acted on");
                    return;
                }

                // loom-save {name, gifBase64, params, overwrite} (loomStudio.js:84-91) →
                // store validates + writes, page gets the verdict + a fresh list
                // (DtrhHostService.cs:285-293).
                string? gifBase64 = loomSave.Raw.TryGetProperty("gifBase64", out var b64) && b64.ValueKind == JsonValueKind.String
                    ? b64.GetString()
                    : null;
                JsonElement? loomParams = loomSave.Raw.TryGetProperty("params", out var lp) && lp.ValueKind == JsonValueKind.Object
                    ? lp.Clone()
                    : null;
                var saveResult = _loom.Save(loomSave.Name, gifBase64, loomParams, loomSave.Overwrite);
                SendToPage(DtrhProtocol.BuildLoomResult("save", saveResult.Ok, saveResult.Slug, saveResult.Error));
                if (saveResult.Ok) PostLoomList();
                return;
            }
            case DtrhProtocol.DtrhPageMessage.LoomDelete loomDelete:
            {
                if (_loom is null)
                {
                    _host.LogDiagnostic("dtrh: 'LoomDelete' arrived before loom init — logged, not acted on");
                    return;
                }

                var deleteResult = _loom.Delete(loomDelete.Slug);
                SendToPage(DtrhProtocol.BuildLoomResult("delete", deleteResult.Ok, deleteResult.Slug, deleteResult.Error));
                if (deleteResult.Ok) PostLoomList();
                return;
            }
        }
    }

    /// <summary>PostLoomList (DtrhHostService.cs:327-343): the saved-spiral library —
    /// {slug, url, params} per entry, served through the §4 media origin (the
    /// ccp.spirals-class route; Step-1 decision). Params sidecars ride along so the pane
    /// can re-edit a kept spiral; unparseable sidecars post null (WPF TryParseJson).</summary>
    private void PostLoomList()
    {
        if (_loom is null)
        {
            return;
        }

        try
        {
            var spirals = _loom.List().Select(s =>
            {
                JsonElement? parsed = null;
                if (s.ParamsJson is { Length: > 0 } json)
                {
                    try { parsed = JsonDocument.Parse(json).RootElement.Clone(); }
                    catch { /* unparseable sidecar → null params (WPF TryParseJson parity) */ }
                }

                return new DtrhProtocol.DtrhLoomSpiral(
                    s.Slug,
                    $"{_dtrh.Server.MediaOrigin}/spirals/loom_{s.Slug}.gif",
                    parsed);
            }).ToList();
            SendToPage(DtrhProtocol.BuildLoomList(spirals));
            _host.LogDiagnostic($"dtrh: loom-list posted ({spirals.Count} spiral(s))"); // presence+shape only
        }
        catch (Exception ex)
        {
            _host.LogDiagnostic($"dtrh: loom-list failed ({ex.GetType().Name})");
        }
    }

    /// <summary>The serialize options for wrapping engine payloads into protocol messages
    /// (camelCase — the payload's handlers read camelCase).</summary>
    private static readonly JsonSerializerOptions PageJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

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
            runSetup: DtrhMeta.BuildRunSetupPayload(
                _host.Participants.OfType<DtrhSaveSlots>().Single().IndexStore.Current),
            m2Test: _m2Test));
        // WPF boot order (DtrhHostService.cs:175-216): init → meta snapshot → manifest →
        // loom-list (Step 3 wires it) → favorites. The meta snapshot seeds the page's
        // Warren/Cheshire state (the b4-gated unlock: cheshireGuide.ensureInit needs it).
        if (_meta is not null)
        {
            SendToPage(_meta.SnapshotMessage());
        }

        // b4: the manifest is the REAL user-media enumeration (WPF DtrhAssetManifest
        // parity; consult item 5 — the b2 hardcoded payload entries are dropped: WPF's
        // manifest is user-pool-only, and shipped payload art must not read as user
        // media). An empty user pool → an empty manifest → the page renders its shipped
        // in-page art (fresh-WPF-install parity, hostMedia.hasUserMedia() false).
        var manifest = DtrhUserMedia.Build(_dtrh.UserMediaRoot, _dtrh.Server.MediaOrigin, _host.LogDiagnostic);
        SendToPage(DtrhProtocol.BuildManifest(
            images: manifest.Images,
            videos: manifest.Videos,
            skipped: manifest.Skipped,
            truncated: manifest.Truncated));
        // THE LOOM (DtrhHostService.cs:209): seed the page's saved-spiral pool at ready.
        PostLoomList();
        // Favorites seed (DtrhHostService.cs:215-216): posted only when the cumulative
        // store ranks anything (WPF posts only when Count > 0).
        var favorites = _meta?.FavoritesSeed();
        if (favorites is { Count: > 0 })
        {
            SendToPage(DtrhProtocol.BuildFavorites(favorites));
        }

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
