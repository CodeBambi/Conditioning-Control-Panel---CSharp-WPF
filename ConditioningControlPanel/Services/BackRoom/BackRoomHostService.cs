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
using ConditioningControlPanel.Services.Race;
using ConditioningControlPanel.Services.Chaos;

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

    /// <summary><c>init.gates</c> / <c>settings.gates</c> (10.13.A, 10.14): the four feature toggles plus the room's own
    /// <c>tunnel</c> and <c>melt</c> switches, sent in full every time. For dressing only: since the authored show
    /// (2026-09-15) nothing on the host gates a Back Room effect on any of them.</summary>
    internal static object GatesWire(Models.AppSettings? s) => new
    {
        flash = s?.FlashEnabled ?? false,
        subliminal = s?.SubliminalEnabled ?? false,
        // Not AppSettings.SpiralEnabled. That is the panel's FULLSCREEN Spiral Overlay feature, and
        // MainWindow.StartStop.cs RandomizeAndStart coin-flips it, so "jump right in" was silently
        // deciding whether the Back Room's wheel hub is a Loom spiral or a brass crosshatch star -
        // which is what the owner saw as "the wheel has no spiral in the middle on desktop". The web
        // playtest has no settings frame, so readGates(null) sends every gate true and it looked fine
        // there. The room's hypno dressing wants its own switch (BackRoomTunnel / BackRoomMelt are the
        // pattern); until it has one it is simply on.
        spiral = true,
        brainDrain = s?.BrainDrainEnabled ?? false,
        tunnel = s?.BackRoomTunnel ?? false,
        melt = s?.BackRoomMelt ?? false,
    };

    /// <summary><c>intensityChoice</c> (10.14): the player's own Calm / Normal / Full, which the room's Options
    /// shows even while <c>intensity</c> is forced to <c>calm</c> below MotionLevel Full.</summary>
    internal static string IntensityChoiceWire(Models.AppSettings? s)
        => (s?.BackRoomFxIntensity ?? BackRoomFxIntensity.Normal).ToString().ToLowerInvariant();

    /// <summary>A <c>room-option</c> the bridge already validated, written to the settings and saved (10.14).
    /// UI thread: the settings listeners run there.</summary>
    internal static void ApplyRoomOption(Models.AppSettings s, BackRoomBridge.RoomOption option)
    {
        switch (option.Key)
        {
            case BackRoomBridge.OptionTunnel: s.BackRoomTunnel = option.On; break;
            case BackRoomBridge.OptionMelt: s.BackRoomMelt = option.On; break;
            case BackRoomBridge.OptionIntensity when option.Intensity is { } i: s.BackRoomFxIntensity = i; break;
            case BackRoomBridge.OptionMediaSource when option.Text is { } src: s.BackRoomMediaSource = src; break;
            case BackRoomBridge.OptionSubVolume when option.Level is { } sub: s.BackRoomSubVolume = sub; break;
            case BackRoomBridge.OptionSfxVolume when option.Level is { } sfx: s.BackRoomSfxVolume = sfx; break;
            case BackRoomBridge.OptionMusicVolume when option.Level is { } mus: s.BackRoomMusicVolume = mus; break;
            case BackRoomBridge.OptionSubAdd when option.Text is { } add: AddNiche(s, add); break;
            case BackRoomBridge.OptionSubRemove when option.Text is { } drop: RemoveNiche(s, drop); break;
            case BackRoomBridge.OptionSubToggle when option.Text is { } flip: ToggleNiche(s, flip); break;
            default: return;
        }
    }

    /// <summary>Settings whose change pushes a full <c>settings</c> frame.</summary>
    internal static readonly HashSet<string> SettingsFrameProperties = new(StringComparer.Ordinal)
    {
        nameof(Models.AppSettings.MotionLevel), nameof(Models.AppSettings.BackRoomFxIntensity),
        nameof(Models.AppSettings.FlashEnabled), nameof(Models.AppSettings.SubliminalEnabled),
        nameof(Models.AppSettings.SpiralEnabled), nameof(Models.AppSettings.BrainDrainEnabled),
        nameof(Models.AppSettings.BackRoomTunnel), nameof(Models.AppSettings.BackRoomMelt),
        // The room's picture source and its three levels, so a room that is already open repaints its own
        // Options when something else changes them. MediaSource and RemoteMediaRatio are the APP's, and they
        // are here because BackRoomMediaSource defaults to "auto" and follows them: changing the source in
        // the Assets tab has to reach the room.
        nameof(Models.AppSettings.BackRoomMediaSource), nameof(Models.AppSettings.BackRoomMediaSubs),
        nameof(Models.AppSettings.BackRoomMediaSubsOff),
        nameof(Models.AppSettings.BackRoomSubVolume), nameof(Models.AppSettings.BackRoomSfxVolume),
        nameof(Models.AppSettings.BackRoomMusicVolume),
        nameof(Models.AppSettings.MediaSource), nameof(Models.AppSettings.RemoteMediaRatio),
    };

    /* ---------------------------------------------------------------- the room's niche list (10.13.C)
     * The host is the only writer. The page presses once per niche and reads the answer back off the
     * next settings frame, so the two can never disagree about what is selected. Case-insensitive
     * throughout, because r/EroticHypnosis and r/erotichypnosis are one community, and the cap is
     * enforced here as well as in the room: a page is not a gatekeeper.
     *
     * A removed niche loses its disabled flag too. A niche switched off KEEPS its place in the list,
     * which is the owner's ruling on this control: a saved disabled niche stays visible so it can be
     * switched back on, and the editor is never a comma-separated field. */
    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static void AddNiche(Models.AppSettings s, string name)
    {
        var subs = new List<string>(s.BackRoomMediaSubs);
        if (subs.Exists(x => Same(x, name)) || subs.Count >= Models.AppSettings.BackRoomMediaSubCap) return;
        subs.Add(name);
        s.BackRoomMediaSubs = subs;
    }

    private static void RemoveNiche(Models.AppSettings s, string name)
    {
        var subs = new List<string>(s.BackRoomMediaSubs);
        if (subs.RemoveAll(x => Same(x, name)) == 0) return;
        s.BackRoomMediaSubs = subs;
        var off = new List<string>(s.BackRoomMediaSubsOff);
        if (off.RemoveAll(x => Same(x, name)) > 0) s.BackRoomMediaSubsOff = off;
    }

    private static void ToggleNiche(Models.AppSettings s, string name)
    {
        // Only a niche that is actually in the list can be switched off: otherwise a stale press would
        // park a name in the disabled list that nothing ever shows or clears.
        if (!s.BackRoomMediaSubs.Exists(x => Same(x, name))) return;
        var off = new List<string>(s.BackRoomMediaSubsOff);
        if (off.RemoveAll(x => Same(x, name)) == 0) off.Add(name);
        s.BackRoomMediaSubsOff = off;
    }

    /// <summary><c>init.media</c> / <c>settings.media</c>: what the room's picture picker shows. <c>effective</c>
    /// is what the deal will actually do once "auto" is resolved against the app, so the page never has to know
    /// the app's own setting. <c>consented</c> false means the online rows paint disabled rather than vanish:
    /// the player needs to see why Scrolller is not on offer.</summary>
    internal static object MediaWire(Models.AppSettings? s) => new
    {
        source = s?.BackRoomMediaSource ?? "auto",
        effective = EffectiveMediaSource(s),
        subs = (s?.BackRoomMediaSubs ?? new List<string>()).ToArray(),
        off = (s?.BackRoomMediaSubsOff ?? new List<string>()).ToArray(),
        cap = Models.AppSettings.BackRoomMediaSubCap,
        consented = s?.HasRemoteMediaConsent ?? false,
        ratio = s?.RemoteMediaRatio ?? 30,
    };

    /// <summary>
    /// "auto" resolved: follow the app-wide <c>MediaSource</c>. Anything that would deal remote media without
    /// consent collapses to <c>local</c> here rather than at the deal, so one place decides it.
    /// </summary>
    internal static string EffectiveMediaSource(Models.AppSettings? s)
    {
        var chosen = s?.BackRoomMediaSource ?? "auto";
        var resolved = chosen == "auto" ? (s?.MediaSource ?? "local") : chosen;
        if (resolved is "online" or "mixed" && !(s?.HasRemoteMediaConsent ?? false)) return "local";
        return resolved;
    }

    /// <summary><c>init.audio</c> / <c>settings.audio</c>: the room's own three levels as 0..1, which is what the
    /// kit's bus setters and the music element take. Not the app's volumes (10.21).</summary>
    internal static object AudioWire(Models.AppSettings? s) => new
    {
        sub = (s?.BackRoomSubVolume ?? 100) / 100.0,
        sfx = (s?.BackRoomSfxVolume ?? 100) / 100.0,
        music = (s?.BackRoomMusicVolume ?? 15) / 100.0,
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
    private static bool _openingRace;
    private static bool _racePage;
    private static int _roomGeneration;
    private static bool _panicSuspended;
    private static bool _minimised;
    private static DateTime _lastPanicPressUtc;

    /// <summary>The real dispatcher (C3, shared with the dev rig so they share one hero gate) and the
    /// real media feed (C4). Tests may swap either for a null object.</summary>
    internal static IBackRoomFx Fx { get; set; } = BackRoomFxServices.Shared;
    internal static IBackRoomMedia Media { get; set; } = new BackRoomMedia(LexOrFallback);

    /// <summary>The spoken subliminal word (10.21). Tests swap it for the null object.</summary>
    internal static IBackRoomVoice Voice { get; set; } = new BackRoomVoice();

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

            // Start filling the remote picture pool now. A local-only room makes this a no-op, and for an
            // online one it means the first wall the player sees is the one they asked for: the pool's own
            // bounded wait otherwise lands on the page's boot media-request.
            try { Media.WarmForRoomOpen(); } catch (Exception ex) { Diag.Swallowed(ex, "backroom warm"); }
            // The bridge is no longer built inline here: the casino's race handoff tears this one down and
            // builds a fresh one on the way back (ReturnToRoom), so there has to be exactly one writer of
            // it. Everything this worktree had inline moved into CreateBridge() unchanged, identity pin
            // included - see the note there about why the pin is captured per bridge rather than per launch.
            CreateBridge();

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
                OnMessage = OnRoomMessage,
                OnProcessFailed = kind => { App.Logger?.Warning("BackRoom: process failed ({Kind}), closing", kind); OnUi(DisposeAll); },
            });
            HookSettings(true);
            BackRoomFxServices.Viewport = ReadViewport;
            _host.Show();
            if (_host.Window is { } w)
            {
                w.Closed += (_, _) => { _openingRace = false; if (_bridge != null) _bridge.CloseNow(); else DisposeAll(); };
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

    private static void CreateBridge()
    {
            var generation = ++_roomGeneration;
            // THE IDENTITY PIN, kept from this worktree's inline bridge rather than lost to the
            // extraction. The room's relay refuses to speak for anyone but the account that opened
            // it: if the app's identity changes under a live room (a sign-out, a switch), RoomIdentity()
            // returns null and the relay goes quiet instead of banking one player's SP into another
            // player's ledger. The pin is captured per BRIDGE, not per launch, and that is deliberate:
            // the casino's race handoff rebuilds the bridge in the same window (ReturnToRoom), and
            // re-reading the identity there is right, because the run that just finished banked
            // against whoever is signed in now.
            var roomAccount = BackRoomApi.AppIdentity()?.UnifiedId;
            (string UnifiedId, string Token)? RoomIdentity()
            {
                var current = BackRoomApi.AppIdentity();
                return current?.UnifiedId == roomAccount ? current : null;
            }
            BackRoomBridge? bridge = null;
            var api = new BackRoomApi(null, RoomIdentity,
                sp => bridge?.AdoptSp(sp, () => RoomIdentity() != null));
            _bridge = bridge = new BackRoomBridge(new BackRoomBridge.Deps
            {
                Post = message => OnUi(() =>
                {
                    if (generation == _roomGeneration && !_racePage) _host?.Post(message);
                }),
                Relay = api,
                Fx = Fx,
                Media = Media,
                Voice = Voice,
                BuildInit = BuildInit,
                CloseWindow = OnRoomClosed,
                Schedule = Schedule,
                NoteEvent = key => App.FeatureDayLog?.Note(key),
                SetOption = option => OnUi(() =>
                {
                    if (App.Settings?.Current is not { } s) return;
                    ApplyRoomOption(s, option);
                    App.Settings.Save();
                }),
                SetSp = sp => { if (generation == _roomGeneration && !_racePage && App.Settings?.Current is { } s) s.SkillPoints = sp; },
                OnUi = OnUi,
                OffUi = work => System.Threading.Tasks.Task.Run(work),
                Log = msg => App.Logger?.Debug("BackRoom: {Msg}", msg),
            });

    }

    private static void OnRoomMessage(JObject message)
    {
        if ((string?)message["type"] != "game-open") { _bridge?.Handle(message); return; }
        if ((string?)message["game"] != "race" || _openingRace) return;
        string? refusal = !RacingAccess.CanLaunch ? "locked" : CaucusHostService.IsActive ? "busy" : null;
        Post(new { type = "game-open-result", game = "race", ok = refusal == null, reason = refusal });
        if (refusal != null) return;
        _openingRace = true;
        _bridge?.RequestClose("race");
    }

    private static void OnRoomClosed()
    {
        if (!_openingRace || _disposing) { DisposeAll(); return; }
        _openingRace = false;
        ++_roomGeneration;
        _bridge = null;
        HookSettings(false);
        BackRoomFxServices.Viewport = null;
        if (_host == null) return;
        // Revalidate after the room's asynchronous exit, including concurrent launches.
        if (!RacingAccess.CanLaunch || CaucusHostService.IsActive) { ReturnToRoom(); return; }
        if (_host.Window != null) _host.Window.Title = "Racing Thoughts";
        _racePage = true;
        CaucusHostService.Launch(sharedHost: _host, returnToRoom: ReturnToRoom);
    }

    private static void ReturnToRoom()
    {
        _racePage = false;
        if (_disposing || _host == null) return;
        try
        {
            CreateBridge();
            if (_host.Window != null) _host.Window.Title = ProductName;
            _panicSuspended = _minimised = false;
            HookSettings(true);
            BackRoomFxServices.Viewport = ReadViewport;
            _host.NavigatePage(StartUrl + "?raceReturn=1", () => _bridge?.OnReady(), OnRoomMessage);
            _host.FocusWeb();
        }
        catch (Exception ex) { App.Logger?.Warning(ex, "BackRoom: return failed"); DisposeAll(); }
    }

    /// <summary>Graceful close (the panic stop pass, an entitlement or mod change): the page gets
    /// <c>close</c> and 800 ms. Idempotent.</summary>
    public static void CloseActive(string reason = "panic")
    {
        _openingRace = false;
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
        _openingRace = false;
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
        if (_racePage) { CloseActive("panic"); return; }
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
            racingTracks = RacingAccess.OwnedTracks,
            sp = s?.SkillPoints ?? 0,
            reduced = motion != Models.MotionLevel.Full,
            motion = MotionWire(motion),
            intensity = IntensityWire(s, motion),
            lang = LocalizationManager.Instance.CurrentLanguage,
            gates = GatesWire(s), media = MediaWire(s), audio = AudioWire(s),
            intensityChoice = IntensityChoiceWire(s),
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
            reduced = motion != Models.MotionLevel.Full, gates = GatesWire(s), intensityChoice = IntensityChoiceWire(s),
            media = MediaWire(s), audio = AudioWire(s),
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
        if (e.PropertyName != null && SettingsFrameProperties.Contains(e.PropertyName)) _bridge.PushSettings(SettingsMessage());
        // The pool is keyed to a niche selection, so a selection change makes everything in it stale. The
        // frame above already tells the page to re-deal; this makes sure the re-deal gets new pictures
        // rather than the old ones over again.
        if (e.PropertyName is nameof(Models.AppSettings.BackRoomMediaSubs)
            or nameof(Models.AppSettings.BackRoomMediaSubsOff)
            or nameof(Models.AppSettings.BackRoomMediaSource)
            or nameof(Models.AppSettings.MediaSource))
        {
            try { Media.ReleaseWarmPool(); Media.WarmForRoomOpen(); }
            catch (Exception ex) { Diag.Swallowed(ex, "backroom repool"); }
        }
    }

    private static void DisposeAll()
    {
        if (_disposing) return;
        _disposing = true;
        try
        {
            _openingRace = false;
            ++_roomGeneration;
            _bridge?.CloseNow();
            if (_racePage) CaucusHostService.DetachFromRoom();
            _racePage = false;
            HookSettings(false);
            BackRoomFxServices.Viewport = null;
            var host = _host;
            _host = null;
            _bridge = null;
            try { host?.Dispose(); } catch (Exception ex) { Diag.Swallowed(ex); }
            // The pool's materialized pictures are temp files under the assets folder. Let them go with
            // the room: otherwise up to a poolful survive until RemoteMediaCache's next startup sweep.
            try { Media.ReleaseWarmPool(); } catch (Exception ex) { Diag.Swallowed(ex, "backroom pool release"); }
            _panicSuspended = _minimised = false;
            App.Logger?.Information("BackRoomHostService: closed");
        }
        finally { _disposing = false; }
    }
}
