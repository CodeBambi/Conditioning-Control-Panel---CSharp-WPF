using System;
using System.IO;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Controls.ApplicationLifetimes;
using global::Avalonia.Media;
using global::Avalonia.Threading;
using Microsoft.Extensions.Logging;
using ConditioningControlPanel;                       // IBarkService, IModService, IProgressionService, ISkillTreeService
using ConditioningControlPanel.Avalonia.Chaos;        // DtrhNativeEffects, ChaosMetaStoreAdapter, IRevealService, IChaosMetaService
using ConditioningControlPanel.Core.Platform;         // IAppEnvironment, BrowserHostResourceAccess
using ConditioningControlPanel.Core.Services.Chaos;   // orchestrator, stores, sentinel, IChaosMetaStore, IChaosWebGameService
using ConditioningControlPanel.Core.Services.Settings;// ISettingsService
using ConditioningControlPanel.Core.Services.Video;   // IVideoService (world-freeze + watch-credit)
using ConditioningControlPanel.Avalonia.Desktop.Windows.Platform; // WebView2BrowserHost

namespace ConditioningControlPanel.Avalonia.Desktop.Windows.Services.Chaos;

/// <summary>
/// Windows-head implementation of the Core <see cref="IChaosWebGameService"/> launch seam: owns the
/// dedicated DTRH web-game <see cref="Window"/>, its <see cref="WebView2BrowserHost"/>, and the portable
/// <see cref="DtrhHostOrchestrator"/> that routes the game's JS↔C# bridge into real desktop conditioning.
/// Mirrors <c>ChaosTunnelService</c>'s window/host lifecycle, inverted for an interactive foreground game
/// (focusable, activated, taskbar, normal decorations, windowed with a page-driven fullscreen toggle —
/// NOT the tunnel's click-through sink-to-bottom ambient background).
///
/// Owner ruling 2026-07-10: web-only, dedicated Window, NEVER Topmost, launches windowed, borderless-
/// fullscreen ONLY on the page Fullscreen API toggle. Not DI-registered until S2c-2c (Program.cs + Lab
/// launch hook) — until then this type is inert and cannot affect the smoke test.
/// </summary>
public sealed class DtrhGameHostService : IChaosWebGameService
{
    // Virtual-host mappings (WPF DtrhHostService.cs:82-94 parity): page root Deny, asset roots Allow so
    // WebGL texture/media uploads from ccp.assets/ccp.art are CORS-clean.
    private const string GameHost = "ccp.game";
    private const string AssetsHost = "ccp.assets";
    private const string ArtHost = "ccp.art";
    private const string StartUrl = "https://ccp.game/dtrh/index.html";

    // WebView2 env args: keep the WebGL swapchain off the DirectComposition video-overlay / occlusion path
    // (anti-MPO, ChaosTunnelService precedent) + let the game's audio bed autoplay without a user gesture.
    private const string BrowserArgs =
        "--disable-direct-composition-video-overlays --disable-features=CalculateNativeWinOcclusion " +
        "--autoplay-policy=no-user-gesture-required";

    private readonly ISettingsService _settings;
    private readonly IVideoService? _video;
    private readonly IAppEnvironment _env;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<DtrhGameHostService>? _logger;
    private readonly IBarkService? _bark;
    private readonly IModService? _mods;
    private readonly IRevealService? _reveal;
    private readonly IChaosMetaService? _meta;
    private readonly IProgressionService? _progression;
    private readonly ISkillTreeService? _skillTree;

    private Window? _window;
    private WebView2BrowserHost? _host;
    private DtrhHostOrchestrator? _orchestrator;
    // WPF minimized the main window when launching from tray (DtrhHostService.cs:790); the Avalonia game is
    // a separate foreground window and does NOT minimize the main window, so this stays false and
    // RestoreMainWindow self-guards to a no-op (auditor carry-forward N2).
    private bool _minimizedMainWindow;
    // Recover is honoured at most once per session (DtrhHostService.cs:745 _relaunchedOnce, carry-forward N3).
    private bool _relaunchedThisSession;

    public DtrhGameHostService(
        ISettingsService settings,
        IAppEnvironment env,
        ILoggerFactory loggerFactory,
        IBarkService? bark = null,
        IModService? mods = null,
        IRevealService? reveal = null,
        IChaosMetaService? meta = null,
        IProgressionService? progression = null,
        ISkillTreeService? skillTree = null,
        IVideoService? video = null,
        ILogger<DtrhGameHostService>? logger = null)
    {
        _settings = settings;
        _video = video;
        _env = env;
        _loggerFactory = loggerFactory;
        _bark = bark;
        _mods = mods;
        _reveal = reveal;
        _meta = meta;
        _progression = progression;
        _skillTree = skillTree;
        _logger = logger;
    }

    public bool IsRunning => _window != null;

    private static Window? MainWindow =>
        (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

    /// <inheritdoc/>
    public void Launch()
    {
        if (_settings.Current?.ChaosWebGameEnabled != true)
        {
            _logger?.LogDebug("DTRH launch skipped - ChaosWebGameEnabled is off");
            return;
        }
        if (_window != null)
        {
            try { _window.Activate(); } catch { }
            return; // already up
        }
        try { Build(); }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "DTRH game launch failed");
            DisposeAll();
        }
    }

    /// <inheritdoc/>
    public void Close()
    {
        var w = _window;
        if (w == null) { DisposeAll(); return; }
        Dispatcher.UIThread.Post(() => { try { w.Close(); } catch { DisposeAll(); } });
    }

    private void Build()
    {
        _host = new WebView2BrowserHost { AdditionalBrowserArguments = BrowserArgs };
        _host.WebMessageReceived += OnWebMessageReceived;
        _host.FullscreenChanged += OnFullscreenChanged;

        // Page root Deny (WPF :86) + asset roots Allow (WPF :89/:94) — matches SetVirtualHostToFolder's
        // cross-origin access kinds so WebGL uploads are CORS-clean.
        var webRoot = Path.Combine(AppContext.BaseDirectory, "Resources", "web");
        _host.SetVirtualHostToFolder(GameHost, webRoot, BrowserHostResourceAccess.Deny);
        _host.SetVirtualHostToFolder(AssetsHost, _env.EffectiveAssetsPath, BrowserHostResourceAccess.Allow);
        _host.SetVirtualHostToFolder(ArtHost, Path.Combine(AppContext.BaseDirectory, "assets", "Chaos"), BrowserHostResourceAccess.Allow);

        // Window-coupled native-effect callbacks (S2c-2a left these as ctor delegates for 2b to wire):
        //  - reclaimFocus: bring the game window back to front after a native overlay steals focus (WPF FocusWeb).
        //  - restoreMainWindow: N2 self-guard — only acts if we minimized the main window (we don't, so no-op).
        Action reclaimFocus = () => Dispatcher.UIThread.Post(() => { try { _window?.Activate(); } catch { } });
        Action restoreMainWindow = () => Dispatcher.UIThread.Post(() =>
        {
            if (!_minimizedMainWindow) return;
            try { var mw = MainWindow; mw?.Show(); mw?.Activate(); } catch { }
            _minimizedMainWindow = false;
        });

        var effects = new DtrhNativeEffects(
            _bark, _mods, _settings, _reveal, _meta,
            _loggerFactory.CreateLogger<DtrhNativeEffects>(),
            reclaimFocus, restoreMainWindow, _video);

        var store = new ChaosMetaStoreAdapter(_meta!);
        var manifest = new DtrhAssetManifest(_env, _loggerFactory.CreateLogger<DtrhAssetManifest>());
        var assetStats = new DtrhAssetStatsStore(_env, _loggerFactory.CreateLogger<DtrhAssetStatsStore>());
        var sessionStats = new DtrhSessionStatsStore(_env, _loggerFactory.CreateLogger<DtrhSessionStatsStore>());
        var sentinel = new ChaosCrashSentinel(_env, _loggerFactory.CreateLogger<ChaosCrashSentinel>());

        _orchestrator = new DtrhHostOrchestrator(
            _host, effects, store, manifest, assetStats, sessionStats, _env,
            _loggerFactory.CreateLogger<DtrhHostOrchestrator>(),
            _loggerFactory.CreateLogger<DtrhMetaBridge>(),
            _progression, _skillTree, sentinel, _settings, _bark, testMode: false);
        _orchestrator.Closed += OnOrchestratorClosed;
        _orchestrator.RecoverRequested += OnRecoverRequested;
        // N1 watch-credit: feed the orchestrator's telemetry counters from real video teardown credits.
        // AvaloniaVideoService is a long-lived singleton, so this MUST be unsubscribed in DisposeAll
        // (else a leaked handler keeps the closed host alive and ghost-credits future videos).
        if (_video != null) _video.VideoWatchCredited += OnVideoWatchCredited;

        _window = new Window
        {
            Title = "Down the Rabbit Hole",
            Background = Brushes.Black,   // opaque black — no transparency level needed
            Topmost = false,             // owner ruling: NEVER topmost
            ShowActivated = true,        // interactive foreground game — take focus on show
            ShowInTaskbar = true,
            Width = 1280,
            Height = 800,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Content = _host.CreateBrowserControl(),
        };
        _window.Closed += (_, _) => DisposeAll();

        _window.Show();
        _ = _host.NavigateAsync(new Uri(StartUrl));
        _logger?.LogInformation("DTRH web game window up (windowed, non-topmost, focusable)");
    }

    private void OnWebMessageReceived(object? sender, string json)
    {
        if (string.IsNullOrEmpty(json)) return;
        try { _orchestrator?.HandleMessage(json); }
        catch (Exception ex) { _logger?.LogDebug(ex, "DTRH HandleMessage failed"); }
    }

    private void OnFullscreenChanged(object? sender, bool fullscreen)
    {
        // Owner ruling: windowed by default, borderless-fullscreen ONLY when the page toggles the HTML5
        // Fullscreen API. WindowState is set from code-behind (Avalonia v12 cannot set it from styles);
        // WindowState.FullScreen is inherently borderless.
        Dispatcher.UIThread.Post(() =>
        {
            try { if (_window != null) _window.WindowState = fullscreen ? WindowState.FullScreen : WindowState.Normal; }
            catch (Exception ex) { _logger?.LogDebug(ex, "DTRH fullscreen toggle failed"); }
        });
    }

    private void OnOrchestratorClosed()
        => Dispatcher.UIThread.Post(() => { try { _window?.Close(); } catch { DisposeAll(); } });

    // Translate a credited video watch into the orchestrator's run telemetry, mirroring WPF
    // DtrhHostService.OnVideoWatchCredited (DtrhHostService.cs:618-623): accumulate watched seconds, and
    // count the watch as a SKIP when under 90% of duration. The DurationSec>0 guard is behaviour-identical
    // to WPF's Infinity case (division guarded away). Telemetry-only — no XP term.
    private void OnVideoWatchCredited(object? sender, VideoWatchInfoEventArgs e)
    {
        try
        {
            _orchestrator?.OnVideoWatchCredited(e.WatchedSec);
            if (e.DurationSec > 0 && e.WatchedSec / e.DurationSec < 0.90) _orchestrator?.OnVideoSkipped();
        }
        catch (Exception ex) { _logger?.LogDebug(ex, "DTRH OnVideoWatchCredited failed"); }
    }

    private void OnRecoverRequested(string reason)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_relaunchedThisSession)
            {
                _logger?.LogWarning("DTRH recover requested again ({Reason}) - already relaunched once this session; closing", reason);
                Close();
                return;
            }
            _relaunchedThisSession = true;
            _logger?.LogInformation("DTRH recover ({Reason}) - relaunching game window once", reason);
            // Tear down the current graph, then rebuild. Closing nulls _window so Launch() rebuilds cleanly.
            DisposeAll();
            Launch();
        });
    }

    private void DisposeAll()
    {
        try { if (_video != null) _video.VideoWatchCredited -= OnVideoWatchCredited; } catch { }
        try { _orchestrator?.Dispose(); } catch { }
        try { _host?.Dispose(); } catch { }
        _orchestrator = null;
        _host = null;
        _window = null;
    }
}
