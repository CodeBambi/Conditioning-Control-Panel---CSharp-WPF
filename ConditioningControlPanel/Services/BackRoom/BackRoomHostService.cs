using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Newtonsoft.Json.Linq;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Services.BackRoom;

/// <summary>
/// Host for THE BACK ROOM (<c>Resources/web/backroom</c>): the SP room with a game station in each
/// corner. Its own <see cref="ChaosWebViewHost"/>, windowed, owned by MainWindow, free for every
/// account (CONTRACT section 1). Everything the page and the host say to each other is
/// <see cref="BackRoomBridge"/>; this class is only the window, the settings watch and the three
/// ways a room is left (the page's Back, the panic ladder, app exit).
/// </summary>
internal static class BackRoomHostService
{
    public const string ProductName = "The Back Room";
    public const string StartUrl = "https://ccp.game/backroom/index.html";

    /// <summary>Constant on purpose (AGENTS.md: the argument string must not vary per launch, or
    /// WebView2 refuses to share the browser process between hosts).</summary>
    public const string BrowserArguments = "--autoplay-policy=no-user-gesture-required";

    /// <summary>The room lexicon's prefix: <c>init.lex</c> carries every en.json key that starts with it
    /// (10.13), so a station's rows reach the page the moment they are in en.json, with no list here.</summary>
    internal const string LexPrefix = "br_";

    /// <summary><c>init.lex</c>: each key once, in the current language (English where it has no row).</summary>
    internal static Dictionary<string, string> Lex(IEnumerable<string> keys, Func<string, string> get)
        => keys.Distinct(StringComparer.Ordinal).ToDictionary(k => k, get, StringComparer.Ordinal);

    /// <summary><c>init.gates</c> / <c>settings.gates</c> (10.13.A): the four feature toggles, sent in full
    /// every time. For dressing only; the dispatcher still enforces each toggle.</summary>
    internal static object GatesWire(Models.AppSettings? s) => new
    {
        flash = s?.FlashEnabled ?? false,
        subliminal = s?.SubliminalEnabled ?? false,
        spiral = s?.SpiralEnabled ?? false,
        brainDrain = s?.BrainDrainEnabled ?? false,
    };

    /// <summary>Settings whose change pushes a full <c>settings</c> frame.</summary>
    internal static readonly HashSet<string> SettingsFrameProperties = new(StringComparer.Ordinal)
    {
        nameof(Models.AppSettings.MotionLevel), nameof(Models.AppSettings.BackRoomFxIntensity),
        nameof(Models.AppSettings.FlashEnabled), nameof(Models.AppSettings.SubliminalEnabled),
        nameof(Models.AppSettings.SpiralEnabled), nameof(Models.AppSettings.BrainDrainEnabled),
    };

    /// <summary>DEBUG only: <c>CCP_BACKROOM_CDP_PORT</c> opens a remote debugging port on the room's
    /// own browser process (its own user data folder, so no other host shares these arguments) for
    /// desk-run screenshots. Release builds add nothing.</summary>
    private static string DebugBrowserArguments()
    {
#if DEBUG
        var port = Environment.GetEnvironmentVariable("CCP_BACKROOM_CDP_PORT");
        if (int.TryParse(port, out var p) && p is > 1024 and < 65536) return " --remote-debugging-port=" + p;
#endif
        return string.Empty;
    }

    private static readonly TimeSpan PanicDoublePressWindow = TimeSpan.FromSeconds(2);

    private static ChaosWebViewHost? _host;
    private static BackRoomBridge? _bridge;
    private static Models.AppSettings? _hookedSettings;
    private static bool _replaceHooked;
    private static bool _disposing;
    private static bool _panicSuspended;
    private static bool _minimised;
    private static DateTime _lastPanicPressUtc;

    /// <summary>The real dispatcher (C3, shared with the dev rig so they share one hero gate) and the
    /// real media feed (C4). Tests may swap either for a null object.</summary>
    internal static IBackRoomFx Fx { get; set; } = BackRoomFxServices.Shared;
    internal static IBackRoomMedia Media { get; set; } = new BackRoomMedia(LexOrFallback);

    /// <summary>Loc.Get returns the key itself on a miss; the feed wants the fallback then.</summary>
    private static string LexOrFallback(string key, string fallback)
    {
        var s = Loc.Get(key);
        return string.IsNullOrWhiteSpace(s) || s == key ? fallback : s;
    }

    public static bool IsActive => _host != null;

    public static void Launch()
    {
        if (_host != null) { _host.FocusWeb(); return; }
        try { App.EmiDesk?.NoteOpen("backroom"); } catch (Exception ex) { Diag.Swallowed(ex); }

        try
        {
            _panicSuspended = _minimised = false;
            _lastPanicPressUtc = DateTime.MinValue;

            var api = new BackRoomApi(null, BackRoomApi.AppIdentity, sp => _bridge?.AdoptSp(sp));
            _bridge = new BackRoomBridge(new BackRoomBridge.Deps
            {
                Post = Post,
                Relay = api,
                Fx = Fx,
                Media = Media,
                BuildInit = BuildInit,
                CloseWindow = DisposeAll,
                Schedule = Schedule,
                NoteEvent = key => App.FeatureDayLog?.Note(key),
                SetSp = sp => { if (App.Settings?.Current is { } s) s.SkillPoints = sp; },
                OnUi = OnUi,
                OffUi = work => System.Threading.Tasks.Task.Run(work),
                Log = msg => App.Logger?.Debug("BackRoom: {Msg}", msg),
            });

            var webRoot = Path.Combine(AppContext.BaseDirectory, "Resources", "web");
            var mappings = new List<(string, string, CoreWebView2HostResourceAccessKind)>
            {
                ("ccp.game", webRoot, CoreWebView2HostResourceAccessKind.Deny),
                // The player's own media (the C4 feed deals ccp.assets urls). Allow, because the
                // slot paints dealt GIFs into reel textures and a tainted image cannot reach WebGL.
                ("ccp.assets", App.EffectiveAssetsPath, CoreWebView2HostResourceAccessKind.Allow),
                // Downloaded audio packs mirror the ccp.game tree. The slot's chimes and thud are the
                // race's own sfx (dtrh/shared/audioSrc.js); without this host CCP_CONTENT_READY never
                // arrives and a pack-only clip is silent. Same helper as the Arcademy and DTRH hosts.
                ChaosWebViewHost.ContentMapping(),
            };

            _host = new ChaosWebViewHost(new ChaosWebViewHost.Options
            {
                StartUrl = StartUrl,
                PrimaryHost = "ccp.game",
                Mappings = mappings,
                UserDataFolderName = "backroom",
                InputEnabled = true,
                StartFullscreen = false,
                OwnedByMainWindow = true,
                CenterOnMainWindow = true,
                WindowTitle = ProductName,
                LogTag = "BackRoom",
                ExtraBrowserArguments = BrowserArguments + DebugBrowserArguments(),
                OnReady = () => _bridge?.OnReady(),
                OnMessage = m => _bridge?.Handle(m),
                OnProcessFailed = kind => { App.Logger?.Warning("BackRoom: process failed ({Kind}), closing", kind); OnUi(DisposeAll); },
            });
            HookSettings(true);
            BackRoomFxServices.Viewport = ReadViewport;
            _host.Show();
            if (_host.Window is { } w)
            {
                w.Closed += (_, _) => _bridge?.CloseNow();
                w.StateChanged += (_, _) => OnWindowStateChanged(w);
                w.Activated += (_, _) => OnWindowActivated();
            }
            App.Logger?.Information("BackRoomHostService: launched");
        }
        catch (Exception ex)
        {
            App.Logger?.Error(ex, "BackRoomHostService.Launch failed");
            DisposeAll();
        }
    }

    /// <summary>Graceful close (the panic stop pass, an entitlement or mod change): the page gets
    /// <c>close</c> and 800 ms. Idempotent.</summary>
    public static void CloseActive(string reason = "panic")
    {
        try
        {
            if (_host == null) return;
            if (_host.IsReady && _bridge != null) _bridge.RequestClose(reason);
            else DisposeAll();
        }
        catch (Exception ex) { App.Logger?.Debug("BackRoom.CloseActive: {E}", ex.Message); DisposeAll(); }
    }

    /// <summary>APP EXIT ONLY. No round trip and no timer (a DispatcherTimer never ticks inside
    /// OnExit): send the last cursor and dispose now.</summary>
    public static void ShutdownFlush()
    {
        try
        {
            if (_host == null) return;
            try { _host.Post(new { type = "close", reason = "app-exit" }); } catch (Exception ex) { Diag.Swallowed(ex); }
            if (_bridge != null) _bridge.CloseNow(); else DisposeAll();
        }
        catch (Exception ex) { App.Logger?.Debug("BackRoom.ShutdownFlush: {E}", ex.Message); }
    }

    /// <summary>
    /// THE PANIC LADDER while the room is up (MainWindow hands the key here, as it does for the
    /// Arcademy, so two taps inside a game window never exit the app). Press 1 suspends: every
    /// effect the room started is cancelled and the page goes quiet. Press 2 inside 2 s closes the
    /// room. A suspended room wakes the next time its window is activated after the double-press
    /// window has passed, because protocol 1 has no page-side resume request.
    /// </summary>
    public static void HandlePanicPress()
    {
        if (_host == null || _bridge == null) return;
        var now = DateTime.UtcNow;
        bool second = _panicSuspended && (now - _lastPanicPressUtc) <= PanicDoublePressWindow;
        _lastPanicPressUtc = now;
        if (second)
        {
            App.Logger?.Information("BackRoom: panic press 2 - closing the room");
            _panicSuspended = false;
            CloseActive("panic");
            return;
        }
        App.Logger?.Information("BackRoom: panic press 1 - suspending (press again to leave)");
        _panicSuspended = true;
        _bridge.Suspend(true, "panic");
    }

    private static void OnWindowStateChanged(Window w)
    {
        bool min = w.WindowState == WindowState.Minimized;
        if (min == _minimised) return;
        _minimised = min;
        _bridge?.Suspend(min, "minimise");
    }

    private static void OnWindowActivated()
    {
        if (!_panicSuspended || DateTime.UtcNow - _lastPanicPressUtc <= PanicDoublePressWindow) return;
        _panicSuspended = false;
        _bridge?.Suspend(false, "panic");
    }

    // ============================ projection ============================

    private static object BuildInit()
    {
        var s = App.Settings?.Current;
        var motion = s?.MotionLevel ?? Models.MotionLevel.Full;
        return new
        {
            type = "init",
            protocol = BackRoomBridge.Protocol,
            sp = s?.SkillPoints ?? 0,
            reduced = motion != Models.MotionLevel.Full,
            motion = MotionWire(motion),
            intensity = IntensityWire(s, motion),
            lang = LocalizationManager.Instance.CurrentLanguage,
            gates = GatesWire(s),
            lex = Lex(LocalizationManager.Instance.KeysWithPrefix(LexPrefix), Loc.Get),
            stations = BackRoomApi.Ops.Keys.ToArray(),
            // No door endpoint exists yet: null = unknown, and the page learns `closed` from its
            // first station-request (CONTRACT section 3, 403 closed).
            open = (bool?)null,
        };
    }

    private static object SettingsMessage()
    {
        var s = App.Settings?.Current;
        var motion = s?.MotionLevel ?? Models.MotionLevel.Full;
        return new
        {
            type = "settings", motion = MotionWire(motion), intensity = IntensityWire(s, motion),
            reduced = motion != Models.MotionLevel.Full, gates = GatesWire(s),
        };
    }

    internal static string MotionWire(Models.MotionLevel m) => m.ToString().ToLowerInvariant();

    /// <summary>The room's WebView2 on screen for a gif-from's rect (UI thread; the fx sink calls it there).
    /// CSS px reach physical px through the page zoom times the monitor scale WebView2 rasterizes at.</summary>
    private static Overlays.RoomViewport? ReadViewport()
    {
        if (_host?.Window is not { } w || _host.WebView is not { } web) return null;
        if (w.WindowState == WindowState.Minimized || !web.IsVisible || web.ActualWidth < 1 || web.ActualHeight < 1)
            return new Overlays.RoomViewport(true, default, 1, 1);
        var tl = web.PointToScreen(new Point(0, 0));
        var br = web.PointToScreen(new Point(web.ActualWidth, web.ActualHeight));
        return new Overlays.RoomViewport(false, new Overlays.PxRect(tl.X, tl.Y, br.X - tl.X, br.Y - tl.Y),
            web.ZoomFactor, System.Windows.Media.VisualTreeHelper.GetDpi(web).DpiScaleX);
    }

    /// <summary>The effective intensity: <c>calm</c> whenever MotionLevel is not Full (CONTRACT
    /// section 4), otherwise <c>AppSettings.BackRoomFxIntensity</c>, which C3 adds. Read by name so
    /// this lane builds before that property exists; absent reads as <c>normal</c>.</summary>
    internal static string IntensityWire(Models.AppSettings? s, Models.MotionLevel motion)
    {
        if (motion != Models.MotionLevel.Full) return "calm";
        try
        {
            var v = s?.GetType().GetProperty("BackRoomFxIntensity")?.GetValue(s)?.ToString();
            if (!string.IsNullOrEmpty(v)) return v.ToLowerInvariant();
        }
        catch (Exception ex) { Diag.Swallowed(ex); }
        return "normal";
    }

    // ============================ plumbing ============================

    private static void Post(object msg) => OnUi(() => _host?.Post(msg));

    private static void OnUi(Action a)
    {
        var d = Application.Current?.Dispatcher;
        if (d == null || d.HasShutdownStarted) return;
        if (d.CheckAccess()) { try { a(); } catch (Exception ex) { App.Logger?.Debug("BackRoom.OnUi: {E}", ex.Message); } }
        else d.BeginInvoke(() => { try { a(); } catch (Exception ex) { App.Logger?.Debug("BackRoom.OnUi: {E}", ex.Message); } });
    }

    private static Action Schedule(TimeSpan delay, Action fn)
    {
        DispatcherTimer? t = null;
        OnUi(() =>
        {
            t = new DispatcherTimer { Interval = delay };
            t.Tick += (_, _) => { t?.Stop(); fn(); };
            t.Start();
        });
        return () => OnUi(() => t?.Stop());
    }

    private static void HookSettings(bool on)
    {
        try
        {
            if (_hookedSettings != null) _hookedSettings.PropertyChanged -= OnSettingChanged;
            _hookedSettings = null;
            if (App.Settings == null) return;
            if (_replaceHooked) { App.Settings.CurrentReplaced -= OnSettingsReplaced; _replaceHooked = false; }
            if (!on) return;
            App.Settings.CurrentReplaced += OnSettingsReplaced;
            _replaceHooked = true;
            if (App.Settings.Current is { } s) { s.PropertyChanged += OnSettingChanged; _hookedSettings = s; }
        }
        catch (Exception ex) { Diag.Swallowed(ex); }
    }

    private static void OnSettingsReplaced()
    {
        if (_host == null) return;
        HookSettings(true);
        _bridge?.PushSettings(SettingsMessage());
        if (App.Settings?.Current is { } s) _bridge?.OnSpChanged(s.SkillPoints, "sync");
    }

    private static void OnSettingChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_bridge == null || sender is not Models.AppSettings s) return;
        if (e.PropertyName == nameof(Models.AppSettings.SkillPoints)) _bridge.OnSpChanged(s.SkillPoints, "earn");
        else if (e.PropertyName != null && SettingsFrameProperties.Contains(e.PropertyName)) _bridge.PushSettings(SettingsMessage());
    }

    private static void DisposeAll()
    {
        if (_disposing) return;
        _disposing = true;
        try
        {
            HookSettings(false);
            BackRoomFxServices.Viewport = null;
            var host = _host;
            _host = null;
            _bridge = null;
            try { host?.Dispose(); } catch (Exception ex) { Diag.Swallowed(ex); }
            _panicSuspended = _minimised = false;
            App.Logger?.Information("BackRoomHostService: closed");
        }
        finally { _disposing = false; }
    }
}
