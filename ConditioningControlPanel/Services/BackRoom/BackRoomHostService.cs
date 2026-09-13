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

    /// <summary>Room lexicon rows the page reads through <c>init.lex</c>. Stations may ask for more
    /// with a fallback; every row here has one in en.json.</summary>
    internal static readonly string[] LexKeys =
    {
        "br_room_title", "br_back", "br_balance", "br_walk_hint", "br_soon_title", "br_soon_body",
        "br_station_slot", "br_station_wheel", "br_station_scratcher", "br_station_cards", "br_station_counter",
        "br_station_failed", "br_station_closed", "br_model_missing", "br_suspended",
        "br_preset_drop", "br_preset_relax", "br_preset_let_go", "br_preset_sink",
    };

    private static readonly TimeSpan PanicDoublePressWindow = TimeSpan.FromSeconds(2);

    private static ChaosWebViewHost? _host;
    private static BackRoomBridge? _bridge;
    private static Models.AppSettings? _hookedSettings;
    private static bool _replaceHooked;
    private static bool _disposing;
    private static bool _panicSuspended;
    private static bool _minimised;
    private static DateTime _lastPanicPressUtc;

    /// <summary>C3 and C4 replace these at integration; until then the null objects answer.</summary>
    internal static IBackRoomFx Fx { get; set; } = new NullBackRoomFx();
    internal static IBackRoomMedia Media { get; set; } = new NullBackRoomMedia(Loc.Get);

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
                Log = msg => App.Logger?.Debug("BackRoom: {Msg}", msg),
            });

            var webRoot = Path.Combine(AppContext.BaseDirectory, "Resources", "web");
            var mappings = new List<(string, string, CoreWebView2HostResourceAccessKind)>
            {
                ("ccp.game", webRoot, CoreWebView2HostResourceAccessKind.Deny),
                // The player's own media (the C4 feed deals ccp.assets urls). Allow, because the
                // slot paints dealt GIFs into reel textures and a tainted image cannot reach WebGL.
                ("ccp.assets", App.EffectiveAssetsPath, CoreWebView2HostResourceAccessKind.Allow),
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
                ExtraBrowserArguments = BrowserArguments,
                OnReady = () => _bridge?.OnReady(),
                OnMessage = m => _bridge?.Handle(m),
                OnProcessFailed = kind => { App.Logger?.Warning("BackRoom: process failed ({Kind}), closing", kind); OnUi(DisposeAll); },
            });
            HookSettings(true);
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
            lex = LexKeys.ToDictionary(k => k, Loc.Get),
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
        return new { type = "settings", motion = MotionWire(motion), intensity = IntensityWire(s, motion), reduced = motion != Models.MotionLevel.Full };
    }

    internal static string MotionWire(Models.MotionLevel m) => m.ToString().ToLowerInvariant();

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
        switch (e.PropertyName)
        {
            case nameof(Models.AppSettings.SkillPoints):
                _bridge.OnSpChanged(s.SkillPoints, "earn");
                break;
            case nameof(Models.AppSettings.MotionLevel):
            case "BackRoomFxIntensity":
                _bridge.PushSettings(SettingsMessage());
                break;
        }
    }

    private static void DisposeAll()
    {
        if (_disposing) return;
        _disposing = true;
        try
        {
            HookSettings(false);
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
