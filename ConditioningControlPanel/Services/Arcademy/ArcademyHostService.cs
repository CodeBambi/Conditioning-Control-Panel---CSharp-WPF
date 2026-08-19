using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ConditioningControlPanel.Services.Chaos;
using ConditioningControlPanel.Services.Fyp.Online;

namespace ConditioningControlPanel.Services.Arcademy;

/// <summary>
/// Host service for THE ARCADEMY (<c>Resources/web/arcademy</c>): the T2-gated collection of
/// webview mini-games whose difficulty slider is the app's own effect stack. Same shape as
/// <see cref="DtrhHostService"/> / <see cref="Quiz.IntakeHostService"/> — one
/// <see cref="ChaosWebViewHost"/>, virtual-host mappings, a queue-until-ready bridge, a heartbeat
/// watchdog, a progress-aware boot deadline and a relaunch-once recovery ladder.
///
/// Protocol v1 (planning/arcademy/BUILD-CONTRACT.md §4). C# stays the settings owner: the page gets
/// ONE already-resolved camelCase projection at <c>init</c> and posts typed messages back; every
/// gated field arriving from the page is re-clamped here before it reaches AppSettings, so a stale
/// or hand-edited page cannot raise its own ceiling.
///
/// Launch gates, in order and failing closed: idempotent re-focus of a live window FIRST, then
/// the T2 bar (<see cref="TierGate"/>), then <c>AudioOnlySession</c> — owner ruling 2026-08-19:
/// the Arcademy does NOT open during an audio-only session, and one starting mid-class pushes
/// <c>suspend</c> instead (the window stays open, so it must stay re-focusable).
/// </summary>
internal static class ArcademyHostService
{
    /// <summary>Display name for gates, dialogs and the window title. The mod-skinnable
    /// name is a page-side lexicon row; this is the product's own.</summary>
    public const string ProductName = "The Arcademy";

    private const int Protocol = 1;

    /// <summary>Progress-aware boot deadline: 45s since the last sign of life from the page
    /// (launch, any message). The page arms its own deadline too and reports
    /// <c>boot-error</c> first in the normal case — this one covers a page that never runs a
    /// line of script at all (a missing bundle, a blocked navigation), where nothing page-side
    /// is alive to complain.</summary>
    private static readonly TimeSpan BootDeadline = TimeSpan.FromSeconds(45);

    /// <summary>Rotation/dwell tenant for remote media, so the Arcademy's browsing does not
    /// collide with flashes ("flashes"), the feed ("fyp") or the intake ("intake").</summary>
    private const string RemoteConsumerId = "arcademy";

    private const int RemoteBatchCap = 24;   // per reply; the page asks again if it wants more

    private static ChaosWebViewHost? _host;
    private static ArcademyMetaStore? _meta;
    private static DispatcherTimer? _heartbeatWatch;
    private static DispatcherTimer? _exitWatchdog;
    private static DispatcherTimer? _bootWatch;
    private static DateTime _lastHeartbeatUtc;
    private static DateTime _lastProgressUtc;
    private static bool _exiting;
    private static bool _relaunchedOnce;
    private static bool _disposing;          // reentrancy guard: Dispose closes the window -> Closed -> DisposeAll
    private static bool _initPosted;         // init is posted exactly once per boot
    private static bool _videoHooked;
    private static bool _browserVideoHooked;
    private static bool _settingsReplaceHooked;
    private static Models.AppSettings? _hookedSettings;   // the instance we are actually subscribed to
    private static bool _minimizedMainWindow;
    private static bool _suppressSettingEcho; // we are mid set-setting: the reply is the echo
    private static bool _classActive;         // between class-started and class-left/class-ended
    private static int _remoteFetchInFlight;  // 0/1 via Interlocked
    private static bool _panicSuspended;      // press 1 of the panic ladder froze the page
    private static DateTime _lastPanicPressUtc;

    /// <summary>Bumped by <see cref="DisposeAll"/>. Every in-flight async continuation captures the
    /// value it started with and drops itself when the two disagree, so a batch that comes back
    /// after a relaunch cannot post into (or prewarm a buffer for) the NEW window.</summary>
    private static int _generation;

    /// <summary>The app-wide double-press convention (MainWindow's panic ladder): two presses inside
    /// this window are a deliberate double-tap, anything slower re-arms rung 1.</summary>
    private static readonly TimeSpan PanicDoublePressWindow = TimeSpan.FromSeconds(2);

    /// <summary>Prewarmed remote URLs per requested kind ("loop"/"still"), so an
    /// <c>assets-request</c> can be answered from memory instead of on the network's schedule.</summary>
    private static readonly Dictionary<string, List<AssetUrl>> RemoteBuffer = new(StringComparer.Ordinal);

    public static bool IsActive => _host != null;

    /// <summary>The page reported <c>boot-error</c> this app session (or the host's own deadline
    /// fired). Entry points can read this to stop sending someone back through a door that has
    /// already failed on this machine.</summary>
    public static bool BootFailedThisSession { get; private set; }

    // ============================ launch / close ============================

    /// <summary>
    /// Open the Arcademy window. Idempotency FIRST (a live instance is only ever re-focused, so
    /// a window that is already open can always be brought back - even after AudioOnlySession was
    /// switched on mid-class, which suspends the class rather than closing the window), then the
    /// gates for a FRESH launch, each failing closed: T2, then AudioOnlySession.
    /// </summary>
    public static void Launch()
    {
        // 1. Idempotent: never re-launch a live class, and never strand an open window behind a
        //    gate that only applies to opening a NEW one (the mid-class AudioOnlySession flip
        //    arrives as a `suspend` push - see OnSettingChangedInApp - and the window stays up).
        if (_host != null) { _host.FocusWeb(); return; }

        // 2. The T2 bar. TierGate is the one truth for "may this account open that?" and it
        //    fails closed while App.Patreon is null; DemandLab also raises the standard refusal
        //    toast with its "See tiers" action, which is the whole gate UX.
        if (!TierGate.DemandLab(ProductName)) return;

        // 3. AudioOnlySession. Owner ruling: v1 SKIPS the Arcademy on audio-only days rather than
        //    substituting audio-capable classes; the attendance streak is preserved (frozen, not
        //    broken) because nothing was missed - the day simply had no visual classes in it.
        if (App.Settings?.Current?.AudioOnlySession == true)
        {
            RefuseForAudioOnly();
            return;
        }

        try
        {
            _exiting = false;
            _initPosted = false;
            _classActive = false;
            _panicSuspended = false;
            _lastPanicPressUtc = DateTime.MinValue;
            _meta = new ArcademyMetaStore(msg => _host?.Post(msg));

            var webRoot = Path.Combine(AppContext.BaseDirectory, "Resources", "web");
            var mappings = new List<(string, string, CoreWebView2HostResourceAccessKind)>
            {
                // The shell + games + the shared vendor code live under one origin.
                ("ccp.game", webRoot, CoreWebView2HostResourceAccessKind.Deny),
                // The user's own media. Allow (CORS-clean) because canvas-safe pools draw local
                // media into canvas - the two-pool law (GROUND-RULES §8) is that ONLY these
                // ccp.* URLs may reach a canvas consumer; remote stays DOM-layer.
                ("ccp.assets", App.EffectiveAssetsPath, CoreWebView2HostResourceAccessKind.Allow),
                // Downloaded audio packs (sfx/vo) mirror the ccp.game tree under their own origin.
                ChaosWebViewHost.ContentMapping(),
            };
            // Creator mods: only the mod's arcademy subfolder is mapped, keeping the rest of its
            // resources off the page. Launch-time snapshot - switching the active mod needs a
            // relaunch, exactly like DTRH.
            var modRoot = ModArcademyRoot();
            if (modRoot != null)
                mappings.Add(("ccp.mod", modRoot, CoreWebView2HostResourceAccessKind.Allow));

            _host = new ChaosWebViewHost(new ChaosWebViewHost.Options
            {
                StartUrl = "https://ccp.game/arcademy/index.html",
                PrimaryHost = "ccp.game",
                Mappings = mappings,
                UserDataFolderName = "arcademy",
                InputEnabled = true,
                StartFullscreen = false,
                // Native ownership rather than Topmost: plenty of things raise MainWindow (a bark,
                // a video window closing, a tray restore) and would otherwise bury the class.
                OwnedByMainWindow = true,
                WindowTitle = ProductName,
                LogTag = "Arcademy",
                // The shell's audio bed / stingers must start without a click.
                ExtraBrowserArguments = "--autoplay-policy=no-user-gesture-required",
                OnReady = OnPageReady,
                OnMessage = OnPageMessage,
                OnProcessFailed = OnProcessFailed,
            });
            // HOOK BEFORE SHOW. The AudioOnlySession gate above ran at the top of this method and
            // Show() + navigation is the slowest thing here; a flip landing in that window used to
            // be missed entirely (the watch did not exist yet) and the page opened classes during
            // an audio-only session. Hooked first, the flip is caught, and OnPageReady re-reads the
            // flag once more when init goes out so a flip DURING the navigation is seeded too.
            HookVideoEvents(true);
            HookSettingsWatch(true);
            HookBrowserVideoEvents(true);

            _host.Show();
            // Title-bar X: tear down cleanly so the heartbeat watchdog cannot misread the
            // resulting silence as a wedge and relaunch a window the user just shut.
            if (_host.Window != null) _host.Window.Closed += (_, _) => DisposeAll();

            StartHeartbeatWatch();
            ArmBootDeadline();

            // Tuck the control panel away while the Arcademy owns the screen; DisposeAll puts it
            // back on every close path (DTRH's minimize/restore behaviour).
            try
            {
                if (Application.Current?.MainWindow is MainWindow mw)
                {
                    mw.MinimizeToTrayForChaos();
                    _minimizedMainWindow = true;
                    _host.FocusWeb();
                }
            }
            catch (Exception ex) { App.Logger?.Debug("ArcademyHost: minimize main window failed: {E}", ex.Message); }

            App.Logger?.Information("ArcademyHostService: launched");
        }
        catch (Exception ex)
        {
            App.Logger?.Error(ex, "ArcademyHostService.Launch failed");
            DisposeAll();
        }
    }

    /// <summary>Graceful close: ask the page to wind down, watchdog-force after 1200ms. Also the
    /// app-exit and panic path. Idempotent.</summary>
    public static void CloseActive()
    {
        try
        {
            if (_host == null) return;
            if (_host.IsReady && !_exiting)
            {
                _exiting = true;
                _host.Post(new { type = "end-run", reason = "host" });
                ArmExitWatchdog();
            }
            else
            {
                DisposeAll();
            }
        }
        catch (Exception ex) { App.Logger?.Debug("ArcademyHostService.CloseActive: {E}", ex.Message); DisposeAll(); }
    }

    /// <summary>
    /// APP EXIT ONLY. <see cref="CloseActive"/>'s graceful path posts <c>end-run</c> and waits on a
    /// 1200ms <see cref="DispatcherTimer"/> for the page's <c>exit-done</c> — and inside
    /// <c>App.OnExit</c> that timer NEVER TICKS (the dispatcher is already shutting down and OnExit
    /// ends in TerminateProcess), so the meta flush and the WebView2 disposal it guards simply never
    /// ran: the last class's grades, streak and XP ledger died with the process.
    ///
    /// <para>This is the synchronous path: flush the debounced meta write, dispose the host, no
    /// watchdog and no round trip to the page. Idempotent, and it never throws.</para>
    /// </summary>
    public static void ShutdownFlush()
    {
        try
        {
            if (_meta == null && _host == null) return;
            _exiting = true;
            try { _meta?.FlushSave(); } catch (Exception ex) { App.Logger?.Warning("ArcademyHost.ShutdownFlush meta: {E}", ex.Message); }
            DisposeAll();
            App.Logger?.Information("ArcademyHostService: shutdown flush complete");
        }
        catch (Exception ex) { App.Logger?.Debug("ArcademyHostService.ShutdownFlush: {E}", ex.Message); }
    }

    /// <summary>
    /// Push a suspend/resume to the page: the engine drops every effect NOW and the class pauses.
    /// Public so the panic path can reach it - a modal Arcademy surface must honour the emergency
    /// stop like every other conditioning surface (GROUND-RULES §5).
    /// </summary>
    /// <param name="reason">"video" | "audio-only" | "panic" (protocol vocabulary).</param>
    public static void Suspend(bool on, string reason)
    {
        var disp = Application.Current?.Dispatcher;
        if (disp == null || disp.HasShutdownStarted || _host == null) return;
        disp.BeginInvoke(() =>
        {
            try { _host?.Post(new { type = "suspend", on, reason }); }
            catch (Exception ex) { App.Logger?.Debug("ArcademyHost.Suspend: {E}", ex.Message); }
        });
    }

    /// <summary>
    /// THE PANIC LADDER, page-side. MainWindow's global hook hands the panic key here while the
    /// Arcademy window is up (the same hand-off DtRH and the feed get), because the app-wide ladder
    /// below it EXITS THE WHOLE APP on press 2 when no session is running — and a modal game window
    /// must never be two taps from killing the app.
    ///
    /// <para>Rung 1 freezes everything: <c>suspend</c> drops every effect, pauses the class and
    /// shows the class_suspended treatment with a Resume button (the page asks for the un-freeze
    /// with <c>resume-request</c>, which only this host may grant). Rung 2, inside
    /// <see cref="PanicDoublePressWindow"/>, closes the Arcademy gracefully and restores the control
    /// panel. A slower second press is treated as a fresh rung 1, which is the forgiving reading:
    /// the emergency stop must not become an accidental exit.</para>
    ///
    /// <para>Attendance is safe either way — the streak is written on <c>class-ended</c>, and a class
    /// abandoned mid-panic simply never ended.</para>
    /// </summary>
    public static void HandlePanicPress()
    {
        if (_host == null) return;
        var now = DateTime.UtcNow;
        bool doubleTap = _panicSuspended && (now - _lastPanicPressUtc) <= PanicDoublePressWindow;
        _lastPanicPressUtc = now;

        if (doubleTap)
        {
            App.Logger?.Information("ArcademyHost: panic press 2 - closing the Arcademy");
            _panicSuspended = false;
            CloseActive();
            return;
        }

        _panicSuspended = true;
        App.Logger?.Information("ArcademyHost: panic press 1 - suspending{Mid} (press again to leave)",
            _classActive ? " mid-class" : "");
        Suspend(true, "panic");
    }

    /// <summary>The page asking to come back from a PANIC suspend (the only suspend with no natural
    /// end — a video un-suspends when it ends, an audio-only session when it does). The host stays
    /// the only thing that may un-freeze a class, which is why this is a request and not a page-side
    /// resume.</summary>
    private static void OnResumeRequest(JObject o)
    {
        var reason = ((string?)o["reason"] ?? "panic").Trim();
        if (reason != "panic")
        {
            App.Logger?.Debug("ArcademyHost: resume-request for '{Reason}' refused - only panic resumes on request", reason);
            return;
        }
        if (!_panicSuspended)
        {
            App.Logger?.Debug("ArcademyHost: resume-request with no panic suspend outstanding - ignored");
            return;
        }
        // A mandatory video or an audio-only session outranks the panic resume: un-freezing here
        // would drop a class back on top of a video the user is supposed to be watching.
        if (App.Video?.IsPlaying == true || App.Settings?.Current?.AudioOnlySession == true)
        {
            App.Logger?.Information("ArcademyHost: resume-request held - a video / audio-only session still owns the screen");
            return;
        }
        _panicSuspended = false;
        _lastPanicPressUtc = DateTime.MinValue;   // the ladder re-arms at rung 1
        App.Logger?.Information("ArcademyHost: panic resume granted");
        Suspend(false, "panic");
    }

    /// <summary>The friendly refusal for an audio-only day. A toast, not a modal: the click that
    /// got here was a launch, and a dialog would fight the session for focus.</summary>
    private static void RefuseForAudioOnly()
    {
        App.Logger?.Information("ArcademyHostService: launch refused - AudioOnlySession is active");
        try
        {
            App.Notifications?.Show(
                "Audio-only session is running - the Arcademy stays shut until it ends. Your attendance streak is safe.",
                NotificationType.Info, TimeSpan.FromSeconds(7));
        }
        catch (Exception ex) { App.Logger?.Debug("ArcademyHost: audio-only refusal toast failed: {E}", ex.Message); }
    }

    // ============================ boot ============================

    private static void OnPageReady()
    {
        try
        {
            _lastHeartbeatUtc = DateTime.UtcNow;
            CancelBootDeadline();
            // Keyboard focus does not land in the WebView2 child until a click on a fresh launch -
            // claim it now so the Esc ladder works from the first frame.
            _host?.FocusWeb();
            if (_initPosted) return;   // exactly one init per boot (contract §4)
            _initPosted = true;
            _host?.Post(BuildInit());
            if (_host != null) _host.Post(new { type = "fullscreen", on = _host.IsFullscreen });
            SeedNativeState();
            App.Logger?.Information("ArcademyHostService: sent init (protocol {P})", Protocol);
        }
        catch (Exception ex) { App.Logger?.Warning("ArcademyHostService.OnPageReady: {E}", ex.Message); }
    }

    /// <summary>
    /// Seed the CURRENT native state onto a freshly-booted page. `init` is a snapshot of settings,
    /// not of what is happening right now, and every suspend producer here is EDGE-driven (a video
    /// STARTING, AudioOnlySession FLIPPING) — so a page that opened while a mandatory video was
    /// already on screen never heard about it and dealt a board over the video. The page buffers a
    /// suspend that lands before its shell exists (boot.js) and replays it, so posting this
    /// immediately after init is safe.
    /// </summary>
    private static void SeedNativeState()
    {
        try
        {
            if (App.Video?.IsPlaying == true)
            {
                App.Logger?.Information("ArcademyHost: seeding suspend - a mandatory video is already playing");
                Suspend(true, "video");
                return;
            }
            // Re-read the gate's flag AT THIS MOMENT: Launch() checked it before Show(), and a flip
            // during the navigation would otherwise be lost between the gate and the settings watch.
            if (App.Settings?.Current?.AudioOnlySession == true)
            {
                App.Logger?.Information("ArcademyHost: seeding suspend - AudioOnlySession turned on during boot");
                Suspend(true, "audio-only");
                return;
            }
            if (App.Settings?.Current?.ProtectBrowserVideoPlayback == true && App.BrowserMedia?.IsPlaying == true)
            {
                App.Logger?.Information("ArcademyHost: seeding suspend - browser video is already playing");
                Suspend(true, "video");
            }
        }
        catch (Exception ex) { App.Logger?.Debug("ArcademyHost.SeedNativeState: {E}", ex.Message); }
    }

    // ============================ page messages ============================

    private static void OnPageMessage(JObject o)
    {
        // Any message is a sign of life for the progress-aware boot deadline.
        _lastProgressUtc = DateTime.UtcNow;
        switch ((string?)o["type"])
        {
            case "heartbeat":
            case "pong":
                _lastHeartbeatUtc = DateTime.UtcNow;
                break;
            case "boot-error":
                OnBootError((string?)o["msg"]);
                break;
            case "fullscreen-request":
                ApplyHostFullscreen((bool?)o["on"] ?? false);
                break;
            case "set-setting":
                OnSetSetting(o);
                break;
            case "meta-command":
                _meta?.Handle(o);
                break;
            case "class-started":
                _classActive = true;
                App.Logger?.Information("ArcademyHost: class started ({Game}, tier {Tier})",
                    (string?)o["gameKey"], (int?)o["gradeTier"] ?? 0);
                break;
            case "class-ended":
                OnClassEnded(o);
                break;
            case "class-left":
                // The closing bracket for `class-started`. Leaving a class with Esc ends no class,
                // so without this the mid-class heartbeat limit (12s vs 20s) stayed armed for the
                // rest of the session and every log line claimed we were still in one.
                _classActive = false;
                App.Logger?.Debug("ArcademyHost: class left ({Game})", (string?)o["gameKey"]);
                break;
            case "resume-request":
                OnResumeRequest(o);
                break;
            case "assets-request":
                OnAssetsRequest(o);
                break;
            case "exit":       // page-initiated (Esc held): it winds itself down, then exit-done
                _exiting = true;
                App.Logger?.Information("ArcademyHost: page exit ({Reason})", (string?)o["reason"]);
                ArmExitWatchdog();
                break;
            case "exit-done":
                DisposeAll();
                break;
            default:
                App.Logger?.Debug("ArcademyHost: unhandled message '{T}'", (string?)o["type"]);
                break;
        }
    }

    /// <summary>Page-driven fullscreen: C# owns the borderless toggle (the browser Fullscreen API
    /// would hijack Esc, which the exit ladder needs). The resulting window state is echoed so the
    /// page's own affordances never assume.</summary>
    private static void ApplyHostFullscreen(bool on)
    {
        var disp = Application.Current?.Dispatcher;
        if (disp == null || disp.HasShutdownStarted) return;
        disp.BeginInvoke(() =>
        {
            try
            {
                _host?.SetFullscreen(on);
                if (_host != null) _host.Post(new { type = "fullscreen", on = _host.IsFullscreen });
            }
            catch (Exception ex) { App.Logger?.Debug("ArcademyHost.fullscreen: {E}", ex.Message); }
        });
    }

    // ============================ init projection ============================

    /// <summary>
    /// The one <c>init</c> message (BUILD-CONTRACT §4.1 - the field names there are law).
    /// Consent and ceiling flags are projected ALREADY RESOLVED (<c>remoteMediaEnabled</c>,
    /// <c>audioAudible</c>, <c>motionLevel</c>, <c>performanceMode</c>) so the page never sees raw
    /// flags it could recombine into a gate we did not open.
    /// </summary>
    private static object BuildInit()
    {
        var s = App.Settings?.Current;
        var now = DateTime.Now;
        return new
        {
            type = "init",
            protocol = Protocol,
            platform = new
            {
                isTouch = false,
                hasHaptics = SafeHasHaptics(),
                host = "desktop",
            },
            modId = SafeActiveModId(),
            lexicon = BuildLexicon(),
            palette = BuildPalette(),
            masterIntensity = s?.ArcademyMasterIntensity ?? 0.7,
            caps = BuildCaps(s),
            // Reused, never duplicated: the photosensitivity guard is one knob app-wide.
            effectIntensity = s?.ChaosEffectIntensity ?? 0.85,
            audioLevels = BuildAudioLevels(s),
            audioMute = s?.ArcademyAudioMute ?? false,
            masterVolume = Math.Clamp((s?.MasterVolume ?? 32) / 100.0, 0.0, 1.0),
            remoteMediaEnabled = RemoteMediaEnabled(),
            remoteMediaRatio = Math.Clamp((s?.RemoteMediaRatio ?? 30) / 100.0, 0.0, 1.0),
            offlineMode = s?.OfflineMode ?? false,
            audioAudible = s?.SubAudioAudible ?? false,
            // Always false: the launch gate refuses on an audio-only day, so a class can only ever
            // meet one starting mid-run, which arrives as a `suspend` push instead.
            audioOnlySession = false,
            protectBrowserVideo = s?.ProtectBrowserVideoPlayback ?? true,
            motionLevel = ResolvedMotionLevel(),
            performanceMode = PerformanceProfile.CurrentTier != Models.PerformanceTier.Quality,
            reducedMotion = MotionFx.Level != Models.MotionLevel.Full,
            words = BuildWords(),
            // UTC date seeds the content so the day's classes are globally identical (#978)...
            utcDateSeed = DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            // ...and the LOCAL date is what rolls the attendance streak.
            localDate = now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            overrideCalendar = LoadOverrideCalendar(),
            meta = _meta?.Snapshot() ?? new JObject(),
            settings = BuildSettingsBag(s),
            keybinds = ParseJsonObject(s?.ArcademyKeybindsJson),
            hideTutorial = s?.ArcademyHideTutorial ?? false,
            // The app-wide panic key, projected for ONE reason (SYNTHESIS-NOTES #7):
            // shell/keybinds.js refuses to let a game bind over it. The page never
            // handles the panic key itself - that stays app-side.
            panicKeyEnabled = s?.PanicKeyEnabled ?? true,
            panicKey = s?.PanicKey ?? "Escape",
        };
    }

    private static object BuildCaps(Models.AppSettings? s) => new
    {
        flashRate = s?.ArcademyCapFlashRate ?? 1.0,
        flashOpacity = s?.ArcademyCapFlashOpacity ?? 1.0,
        subDensity = s?.ArcademyCapSubDensity ?? 1.0,
        duckDepth = s?.ArcademyCapDuckDepth ?? 1.0,
        bubbleRate = s?.ArcademyCapBubbleRate ?? 1.0,
        // Canon is binauralDepth (SYNTHESIS-NOTES #9). Never audioDepth.
        binauralDepth = s?.ArcademyCapBinauralDepth ?? 1.0,
        bgIntensity = s?.ArcademyCapBgIntensity ?? 1.0,
    };

    private static object BuildAudioLevels(Models.AppSettings? s)
    {
        var levels = s?.ArcademyAudioLevels ?? Models.AppSettings.DefaultArcademyAudioLevels();
        double Level(string group, double fallback)
        {
            if (levels != null && levels.TryGetValue(group, out var v) && double.IsFinite(v))
                return Math.Clamp(v, 0.0, Models.AppSettings.ArcademyAudioCeiling(group));
            return fallback;
        }
        return new
        {
            fx = Level("fx", 0.48),
            voice = Level("voice", 0.85),
            tutorial = Level("tutorial", 0.85),
            drops = Level("drops", 0.4),
            music = Level("music", 1.0),
        };
    }

    /// <summary>
    /// The subliminal vocabulary for the engine's <c>sub_flash</c> channel: the user's ENABLED
    /// <c>SubliminalPool</c> phrases, shuffled and capped so init stays small. MAY BE EMPTY, and
    /// that is a contract, not a failure - every consumer degrades to a word-free look.
    /// </summary>
    private static string[] BuildWords()
    {
        try
        {
            var pool = App.Settings?.Current?.SubliminalPool;
            var active = pool?.Where(kv => kv.Value).Select(kv => kv.Key)
                .Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim()).ToList();
            if (active == null || active.Count == 0) return Array.Empty<string>();
            var rng = new Random();
            for (int i = active.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (active[i], active[j]) = (active[j], active[i]);
            }
            return active.Take(60).ToArray();
        }
        catch (Exception ex)
        {
            App.Logger?.Debug("ArcademyHost.BuildWords: {E}", ex.Message);
            return Array.Empty<string>();
        }
    }

    /// <summary>
    /// The persisted flat settings bag (per-game knobs from game manifests) plus
    /// <c>localAssets</c>: the page cannot enumerate a virtual host, so the asset provider's local
    /// inventory has to be built here (BUILD-CONTRACT §6 explicitly allows the manifest to ride
    /// <c>init.settings</c>). Respects the Assets tree's own deselection blacklist, exactly like
    /// the flash pool - unchecking an image hides it from the Arcademy too.
    /// </summary>
    private static JObject BuildSettingsBag(Models.AppSettings? s)
    {
        var bag = ParseJsonObject(s?.ArcademySettingsJson) ?? new JObject();
        try { bag["localAssets"] = BuildLocalAssets(); }
        catch (Exception ex) { App.Logger?.Debug("ArcademyHost.BuildSettingsBag: {E}", ex.Message); }
        return bag;
    }

    private const int LocalAssetSample = 60;

    private static JObject BuildLocalAssets()
    {
        var gifs = new List<string>();
        var stills = new List<string>();
        var assetsRoot = App.EffectiveAssetsPath;
        var imagesRoot = Path.Combine(assetsRoot, "images");
        var disabled = Quiz.IntakeHostService.BuildDisabledAssetSet(App.Settings?.Current?.DisabledAssetPaths);
        if (Directory.Exists(imagesRoot))
        {
            foreach (var file in Directory.EnumerateFiles(imagesRoot, "*", SearchOption.AllDirectories))
            {
                var ext = Path.GetExtension(file).ToLowerInvariant();
                if (ext != ".gif" && ext is not (".png" or ".jpg" or ".jpeg" or ".webp")) continue;
                if (!Quiz.IntakeHostService.IsAssetActive(disabled, assetsRoot, file)) continue;
                (ext == ".gif" ? gifs : stills).Add(file);
            }
        }

        var rng = new Random();
        static List<string> Sample(List<string> pool, Random r, int take)
        {
            // partial Fisher-Yates: a random slice without shuffling the whole list
            for (int i = 0; i < Math.Min(take, pool.Count); i++)
            {
                int j = r.Next(i, pool.Count);
                (pool[i], pool[j]) = (pool[j], pool[i]);
            }
            return pool.GetRange(0, Math.Min(take, pool.Count));
        }

        return new JObject
        {
            ["gifs"] = new JArray(Sample(gifs, rng, LocalAssetSample).Select(ToAssetsUrl).Cast<object>().ToArray()),
            ["stills"] = new JArray(Sample(stills, rng, LocalAssetSample).Select(ToAssetsUrl).Cast<object>().ToArray()),
        };
    }

    private static string ToAssetsUrl(string file)
    {
        var rel = Path.GetRelativePath(App.EffectiveAssetsPath, file).Replace('\\', '/');
        var escaped = string.Join('/', rel.Split('/').Select(Uri.EscapeDataString));
        return "https://ccp.assets/" + escaped;
    }

    /// <summary>
    /// <see cref="MotionFx.Level"/> projected as the engine's 0..2 scale, where <b>0 means no
    /// motion</b> (BUILD-CONTRACT §5: <c>reducedMotion || motionLevel === 0</c> degrades to
    /// static/dim). The C# enum counts the other way (Full = 0), so this INVERTS it rather than
    /// casting - a cast would tell the engine that Full motion means "strobe nothing".
    /// </summary>
    private static int ResolvedMotionLevel() => MotionFx.Level switch
    {
        Models.MotionLevel.Off => 0,
        Models.MotionLevel.Reduced => 1,
        _ => 2,
    };

    /// <summary>Server override-calendar (holidays / event weeks) if a cached copy exists beside the
    /// user data. Null = run on the seeded timetable, which is the designed offline fallback.</summary>
    private static JObject? LoadOverrideCalendar()
    {
        try
        {
            var path = Path.Combine(App.UserDataPath, "arcademy_calendar.json");
            if (!File.Exists(path)) return null;
            return JObject.Parse(File.ReadAllText(path));
        }
        catch (Exception ex)
        {
            App.Logger?.Debug("ArcademyHost.LoadOverrideCalendar: {E}", ex.Message);
            return null;
        }
    }

    // ============================ lexicon + palette (mod-resolved) ============================

    /// <summary>
    /// The neutral display-string table. Internal keys are FIXED (GROUND-RULES §3) and a mod skins
    /// values only, never keys, by shipping <c>resources/arcademy/lexicon.json</c>. Keys absent from
    /// the mod file keep the neutral default, so a partial mod table is legal.
    /// </summary>
    private static readonly Dictionary<string, string> NeutralLexicon = new(StringComparer.Ordinal)
    {
        // ---- container / the day -------------------------------------------------------
        ["arcademy"] = "The Arcademy",
        ["semester"] = "Semester",
        ["timetable"] = "Timetable",
        ["class"] = "Class",
        ["classes"] = "Classes",
        ["homeroom"] = "Homeroom",
        ["period"] = "Period",
        ["report_card"] = "Report Card",
        // The one internal key for an audio-only-suspended class (SYNTHESIS-NOTES #8).
        ["class_suspended"] = "Class Suspended",
        ["class_placeholder"] = "Class Placeholder",
        // ---- performance ---------------------------------------------------------------
        ["grade"] = "Grade",
        ["grade_s"] = "S",
        ["grade_a"] = "A",
        ["grade_b"] = "B",
        ["grade_c"] = "C",
        ["grade_pass"] = "PASS",
        ["grade_tier"] = "Year",
        // ONE row per tier (SYNTHESIS-NOTES #1) - never invented per game.
        ["grade_tier_1"] = "Year 1",
        ["grade_tier_2"] = "Year 2",
        ["grade_tier_3"] = "Year 3",
        ["grade_tier_4"] = "Year 4",
        ["attendance"] = "Attendance",
        ["perfect_attendance"] = "Perfect Attendance",
        // Reserved vocabulary: designed for, not built in v1 (GROUND-RULES §3).
        ["detention"] = "Detention",
        ["diploma"] = "Diploma",
        ["exam"] = "Exam",
        ["gpa"] = "GPA",
        ["honor_roll"] = "Honor Roll",
        // ---- family chips (timetable) --------------------------------------------------
        ["family_word"] = "word",
        ["family_memory"] = "memory",
        ["family_search"] = "search",
        ["family_tracking"] = "tracking",
        ["family_reflex"] = "reflex",
        ["family_comfort"] = "comfort",
        // ---- verbs / chrome ------------------------------------------------------------
        ["peek"] = "Peek",
        ["peek_hint"] = "Hold to peek. Using it caps this class at A.",
        ["settings"] = "Settings",
        ["back"] = "Back",
        ["begin_class"] = "Begin",
        ["leave_class"] = "Leave class",
        ["replay_board"] = "Flip the board again",
        ["share"] = "Copy share card",
        ["shared"] = "Copied to clipboard",
        ["done"] = "Done",
        ["retake"] = "Retake",
        ["xp"] = "XP",
        ["streak"] = "Streak",
        // ---- share marks (Daily Trigger's emoji grid) ----------------------------------
        // Escaped rather than pasted so the table survives any re-encoding of this file.
        ["share_hit"] = "\U0001F497",   // pink heart
        ["share_near"] = "\U0001F300",  // cyclone
        ["share_miss"] = "\U0001F5A4",  // black heart
        // ---- per-game titles (game_<key>) ----------------------------------------------
        // The shell renders board rows / report rows / settings groups through
        // t('game_' + key, <module title>), so a mod can only re-title a class if the
        // key exists here (MergeModTable only merges keys the default table declares).
        ["game_daily_trigger"] = "Daily Trigger",
        ["game_deja_vu"] = "Deja Vu",
        ["game_impulse_control"] = "Impulse Control",
        ["game_lost_and_found"] = "Lost & Found",
        // ---- per-game rows (Semester 1) -------------------------------------------------
        // Keys that are not prefixed by a game: the shell and more than one class render
        // them (Daily Trigger mints them today).
        ["absorbed"] = "ABSORBED",
        ["detention_so_close"] = "One letter. It kept the last one for itself.",
        ["mark_hit"] = "right letter, right place",
        ["mark_miss"] = "not in the word",
        ["mark_near"] = "right letter, wrong place",
        ["revision_day"] = "Revision",
        ["revision_day_hint"] = "Revision: you have met this one before.",
        // ---- Daily Trigger (games/daily-trigger) --------------------------------------
        ["dt_absorb_jackpot"] = "It sticks. Deeper than usual.",
        ["dt_absorbed_line"] = "Absorbed. It rides with you through the rest of today.",
        ["dt_cell_empty"] = "empty",
        ["dt_commit"] = "COMMIT ROW",
        ["dt_detention_stamp"] = "DETENTION",
        ["dt_enter"] = "ENTER",
        ["dt_gold_chip"] = "GOLD \u2728",
        ["dt_gold_solved"] = "Gilded: solved on a gold letter day.",
        ["dt_hard_chip"] = "HARD",
        ["dt_hard_hit"] = "Hard mode: keep the revealed letters in place",
        ["dt_hard_mode"] = "Hard mode",
        ["dt_hard_mode_hint"] = "Every revealed letter must be reused. Forced on at Year 4.",
        ["dt_hard_near"] = "Hard mode: use every revealed letter",
        ["dt_keyboard_layout"] = "Keyboard layout",
        ["dt_keyboard_layout_hint"] = "Auto follows the layout your system reports.",
        ["dt_ladder"] = "Ladder",
        ["dt_near_miss"] = "One letter away",
        ["dt_not_a_word"] = "Not in the word list",
        ["dt_not_enough"] = "Not enough letters",
        ["dt_phrase_chip"] = "PHRASE",
        ["dt_phrase_hint"] = "Two words today. The gap is free.",
        ["dt_retake"] = "Retake",
        ["dt_skip"] = "Tap to continue",
        ["dt_study_hint"] = "Study hint: one letter is already in place. It costs you nothing.",
        ["dt_theme_word"] = "One of your own words.",
        ["dt_twist_telegraph"] = "The whispers are real words - just not today's.",
        ["dt_type_to_guess"] = "Type to guess",
        ["dt_type_to_guess_hint"] = "Use the physical keyboard instead of tapping the on-screen one.",
        // ---- Deja Vu (games/deja-vu) --------------------------------------------------
        ["dv_bell"] = "The bell. Time is up.",
        ["dv_card"] = "Card",
        ["dv_clear"] = "Board clear.",
        ["dv_cram_assist"] = "Cram Assist",
        ["dv_cram_hint"] = "Hold to re-study the board. Using it caps this class at A.",
        ["dv_cram_key"] = "Cram Assist key",
        ["dv_cram_on"] = "Cramming.",
        ["dv_cram_ready"] = "Cram Assist ready. Hold it - it caps this class at A.",
        ["dv_deal_hint"] = "Dealing the board.",
        ["dv_drift_hint"] = "A whole line is sliding.",
        ["dv_endgame"] = "Last pair.",
        ["dv_jackpot"] = "JACKPOT",
        ["dv_matched_loops"] = "Matched pairs",
        ["dv_near_miss"] = "SO CLOSE",
        ["dv_peek_hold"] = "Card hold length",
        ["dv_peek_hold_hint"] = "How long a mismatched pair stays face up. Above 1.25x counts as a tempo assist.",
        ["dv_play_hint"] = "Find the pairs.",
        ["dv_preview_hint"] = "Memorize the board.",
        ["dv_retake"] = "Retake",
        ["dv_stamp_bell"] = "BELL",
        ["dv_stamp_clear"] = "CLEAR",
        ["dv_stamp_match"] = "PAIR",
        ["dv_swap_hint"] = "The board is moving.",
        ["dv_swap_tell"] = "swap tell",
        ["dv_swaps"] = "swaps",
        ["dv_tracked"] = "Tracked through the static.",
        // ---- Lost & Found (games/lost-and-found) --------------------------------------
        ["lf_briefing"] = "Memorize her, then find her five times.",
        ["lf_clutch"] = "The board relents",
        ["lf_final_bell"] = "Final bell",
        ["lf_find_prompt"] = "Find her",
        ["lf_found"] = "Found her",
        ["lf_jackpot"] = "Jackpot",
        ["lf_misclick"] = "Wrong one",
        ["lf_misclick_streak"] = "Focus",
        ["lf_misses"] = "Misses",
        ["lf_modifier"] = "The board wakes up",
        ["lf_peek_input"] = "Peek input",
        ["lf_peek_input_hint"] = "Hold the key, tap to toggle, or let the pointer decide.",
        ["lf_peek_key"] = "Peek key",
        ["lf_relocate"] = "It moved - the same glitch hides the churn",
        ["lf_timeout"] = "Time",
        ["lf_warm"] = "Warm",
        ["lf_zen"] = "Zen mode",
        ["lf_zen_clock"] = "--:--",
        ["lf_zen_hint"] = "No clock and no grade. The class still counts for attendance.",
        // ---- Impulse Control (games/impulse-control, mirrors lex.js IC_LEX) ------------
        ["ic_almost"] = "Almost had you",
        ["ic_assessment"] = "Reflex & Compliance Assessment",
        ["ic_assessment_block"] = "Block",
        ["ic_baseline"] = "baseline",
        ["ic_baseline_block"] = "Calibration",
        ["ic_baseline_new"] = "Baseline established. Later classes are scored against it.",
        ["ic_block_clear"] = "Block clear",
        ["ic_breather"] = "Breathe. The next block runs hotter.",
        ["ic_calibrating"] = "Calibrating - hold still, subject.",
        ["ic_clean"] = "clean",
        ["ic_commended"] = "COMMENDED",
        ["ic_composure_hold"] = "Composure hold",
        ["ic_debrief"] = "Debrief",
        ["ic_debrief_buzzer_body"] = "A clean GO was answered with the error buzzer to shake your streak. The response was correct. The machine was not.",
        ["ic_debrief_buzzer_lied"] = "That buzzer lied.",
        ["ic_debrief_clean_line"] = "No interference was active. That one's yours.",
        ["ic_debrief_induced_line"] = "You heard it, and you obeyed. Logged as induced, not yours.",
        ["ic_debrief_no_errors"] = "No errors. Nothing to attribute.",
        ["ic_debrief_no_lies"] = "No interference was active this round. An honest test.",
        ["ic_err_commission"] = "Impulse error",
        ["ic_err_isi"] = "Commission during rest",
        ["ic_err_late"] = "Late response",
        ["ic_err_miss"] = "Missed cue",
        ["ic_go_hint"] = "Press {key} or tap the aperture when the GO face shows. Its near-twin means withhold.",
        ["ic_go_key"] = "GO key",
        ["ic_hold_intro"] = "Composure hold. Withhold, mostly.",
        ["ic_induced"] = "induced",
        ["ic_interference_log"] = "Interference log",
        ["ic_inverse_audio"] = "Allow the false error buzzer (Year 4)",
        ["ic_just_made_it"] = "JUST made it",
        ["ic_legend"] = "top row: interference events   bottom row: your errors",
        ["ic_lie_commitment_trap"] = "mid-presentation swap",
        ["ic_lie_false_cue"] = "false go-sting",
        ["ic_lie_inverse_audio"] = "false error buzzer",
        ["ic_lie_peripheral_decoy"] = "peripheral decoys",
        ["ic_lie_priming_flash"] = "subliminal priming",
        ["ic_new_best"] = "NEW BEST",
        ["ic_nogo_share"] = "NO-GO share",
        ["ic_personal_record"] = "personal record",
        ["ic_recalibrate"] = "Recalibrate baseline",
        ["ic_recalibrate_confirm"] = "Tap again to confirm",
        ["ic_recalibrated"] = "Baseline cleared - the next class recalibrates.",
        ["ic_restraint"] = "restraint",
        ["ic_session_median"] = "session median",
        ["ic_show_rt"] = "Show reaction time",
        ["ic_slip_both"] = "Both axes slipped. Reassessment recommended.",
        ["ic_slip_none"] = "Speed and restraint both held. Filed.",
        ["ic_slip_restraint"] = "Reflexes on record. Restraint slipped - reassessment recommended.",
        ["ic_slip_speed"] = "Restraint held. Reflexes off your record - reassessment recommended.",
        ["ic_stimulus_style"] = "Stimulus style",
        ["ic_subject"] = "Subject",
        ["ic_submit"] = "Submit report",
        ["ic_warn_armed"] = "INTERFERENCE ARMED",
        ["ic_withheld"] = "Withheld",
        ["ic_word_go_1"] = "OBEY",
        ["ic_word_go_2"] = "GOOD",
        ["ic_word_go_3"] = "DEEPER",
        ["ic_word_go_4"] = "FOCUS",
        ["ic_word_go_5"] = "HOLD",
        ["ic_word_go_6"] = "YIELD",
        ["ic_word_nogo_1"] = "OBEV",
        ["ic_word_nogo_2"] = "G00D",
        ["ic_word_nogo_3"] = "DEEPEB",
        ["ic_word_nogo_4"] = "FOCVS",
        ["ic_word_nogo_5"] = "H0LD",
        ["ic_word_nogo_6"] = "YEILD",
    };

    /// <summary>The mockup's owner-approved tokens (BUILD-CONTRACT §10). A mod overrides them via
    /// <c>resources/arcademy/palette.json</c>; unknown keys in that file are ignored.</summary>
    private static readonly Dictionary<string, string> NeutralPalette = new(StringComparer.Ordinal)
    {
        ["ground"] = "#14142B",
        ["navy"] = "#1A1A2E",
        ["panel"] = "#252542",
        ["ink"] = "#F2EBDD",
        ["pink"] = "#FF69B4",
        ["lavender"] = "#B8A6E8",
        ["gold"] = "#F0C24B",
    };

    private static JObject BuildLexicon() => MergeModTable(NeutralLexicon, "lexicon.json");

    private static JObject BuildPalette() => MergeModTable(NeutralPalette, "palette.json");

    /// <summary>Neutral defaults overlaid with the active mod's table, if it ships one. Only string
    /// values for keys the default table already declares are taken: a mod may re-skin the display
    /// strings, never add system keys the shell has no meaning for.</summary>
    private static JObject MergeModTable(Dictionary<string, string> defaults, string fileName)
    {
        var result = new JObject();
        foreach (var kv in defaults) result[kv.Key] = kv.Value;
        try
        {
            var root = ModArcademyRoot();
            if (root == null) return result;
            var path = Path.Combine(root, fileName);
            if (!File.Exists(path)) return result;
            var mod = JObject.Parse(File.ReadAllText(path));
            int applied = 0;
            foreach (var p in mod.Properties())
            {
                if (!defaults.ContainsKey(p.Name)) continue;
                if (p.Value.Type != JTokenType.String) continue;
                var v = (string?)p.Value;
                if (string.IsNullOrWhiteSpace(v) || v.Length > 96) continue;
                result[p.Name] = v;
                applied++;
            }
            if (applied > 0)
                App.Logger?.Information("ArcademyHost: mod skinned {N} {File} rows", applied, fileName);
        }
        catch (Exception ex) { App.Logger?.Debug("ArcademyHost.MergeModTable({File}): {E}", fileName, ex.Message); }
        return result;
    }

    /// <summary>The active mod's <c>resources/arcademy</c> folder, or null. Mapping only this
    /// subfolder keeps the rest of the mod's resources off the page (DTRH precedent).</summary>
    private static string? ModArcademyRoot()
    {
        try
        {
            var installed = App.Mods?.ActiveMod?.InstalledPath;
            if (string.IsNullOrEmpty(installed)) return null;
            var root = Path.Combine(installed, "resources", "arcademy");
            return Directory.Exists(root) ? root : null;
        }
        catch { return null; }
    }

    // ============================ set-setting ============================

    /// <summary>
    /// <c>set-setting {key, value}</c>: validate, CLAMP, persist, echo back the post-clamp value.
    /// The host re-clamps every gated field on the way in (GROUND-RULES §10) and the page clamps
    /// again on receipt, so neither side alone can widen a ceiling.
    ///
    /// <para>Keys are the init projection's names, flattened - <c>caps.flashRate</c> or the bare
    /// <c>flashRate</c>, <c>audioLevels.fx</c> or the bare <c>fx</c>. Anything the host does not
    /// recognise is treated as a PER-GAME knob and lands in the flat
    /// <see cref="Models.AppSettings.ArcademySettingsJson"/> bag; global settings are never
    /// writable under a per-game name.</para>
    /// </summary>
    private static void OnSetSetting(JObject o)
    {
        var key = ((string?)o["key"] ?? "").Trim();
        if (key.Length == 0 || key.Length > 64) return;
        var s = App.Settings?.Current;
        if (s == null) return;

        var value = o["value"];
        object? echo;
        _suppressSettingEcho = true;
        try
        {
            echo = ApplySetting(s, key, value);
        }
        catch (Exception ex)
        {
            App.Logger?.Warning("ArcademyHost.OnSetSetting({Key}): {E}", key, ex.Message);
            return;
        }
        finally { _suppressSettingEcho = false; }

        try { App.Settings?.Save(); }
        catch (Exception ex) { App.Logger?.Debug("ArcademyHost: settings save failed: {E}", ex.Message); }
        _host?.Post(new { type = "setting", key, value = echo });
    }

    /// <summary>Write one setting and return the value actually stored. Null echoes back as null
    /// only when the key was refused outright.</summary>
    private static object? ApplySetting(Models.AppSettings s, string key, JToken? value)
    {
        double Num(double fallback)
        {
            var d = (double?)value ?? fallback;
            return double.IsFinite(d) ? d : fallback;
        }
        bool Flag(bool fallback) => (bool?)value ?? fallback;

        switch (Strip(key, "caps."))
        {
            case "masterIntensity": s.ArcademyMasterIntensity = Num(0.7); return s.ArcademyMasterIntensity;
            case "flashRate": s.ArcademyCapFlashRate = Num(1.0); return s.ArcademyCapFlashRate;
            case "flashOpacity": s.ArcademyCapFlashOpacity = Num(1.0); return s.ArcademyCapFlashOpacity;
            case "subDensity": s.ArcademyCapSubDensity = Num(1.0); return s.ArcademyCapSubDensity;
            case "duckDepth": s.ArcademyCapDuckDepth = Num(1.0); return s.ArcademyCapDuckDepth;
            case "bubbleRate": s.ArcademyCapBubbleRate = Num(1.0); return s.ArcademyCapBubbleRate;
            case "binauralDepth": s.ArcademyCapBinauralDepth = Num(1.0); return s.ArcademyCapBinauralDepth;
            case "bgIntensity": s.ArcademyCapBgIntensity = Num(1.0); return s.ArcademyCapBgIntensity;

            // Shared with the descent on purpose: ONE photosensitivity guard app-wide.
            case "effectIntensity": s.ChaosEffectIntensity = Num(0.85); return s.ChaosEffectIntensity;

            case "audioMute": s.ArcademyAudioMute = Flag(false); return s.ArcademyAudioMute;
            case "hideTutorial": s.ArcademyHideTutorial = Flag(false); return s.ArcademyHideTutorial;

            // App-wide comfort volume, stored 0-100 and projected 0..1.
            case "masterVolume":
                s.MasterVolume = (int)Math.Round(Math.Clamp(Num(0.32), 0.0, 1.0) * 100);
                return Math.Clamp(s.MasterVolume / 100.0, 0.0, 1.0);

            // Shared asset-source vocabulary: the ratio is 5..95 in AppSettings.
            case "remoteMediaRatio":
                s.RemoteMediaRatio = (int)Math.Round(Math.Clamp(Num(0.30), 0.0, 1.0) * 100);
                return Math.Clamp(s.RemoteMediaRatio / 100.0, 0.0, 1.0);

            case "keybinds":
                // REFUSE, never wipe. A non-object here used to store "" — one malformed frame
                // (or a page that sent the blob as a JSON *string*, which is trap 3 in the web
                // CLAUDE.md) and every rebind the player had made was gone. Echo what is STORED so
                // the page's pending paint converges on the truth instead of on the refused value.
                if (value is not JObject kb)
                {
                    App.Logger?.Warning("ArcademyHost: keybinds must be an object (got {Type}) - refused, existing binds kept",
                        value?.Type.ToString() ?? "null");
                    return ParseJsonObject(s.ArcademyKeybindsJson);
                }
                var kbJson = kb.ToString(Formatting.None);
                if (kbJson.Length > MaxKeybindsJsonChars)
                {
                    App.Logger?.Warning("ArcademyHost: keybinds blob is {N} chars (cap {Cap}) - refused",
                        kbJson.Length, MaxKeybindsJsonChars);
                    return ParseJsonObject(s.ArcademyKeybindsJson);
                }
                s.ArcademyKeybindsJson = kbJson;
                return ParseJsonObject(s.ArcademyKeybindsJson);
        }

        var group = Strip(key, "audioLevels.");
        if (Models.AppSettings.DefaultArcademyAudioLevels().ContainsKey(group))
            return SetAudioLevel(s, group, Num(0.5));

        return SetGameSetting(s, key, value);
    }

    private static double SetAudioLevel(Models.AppSettings s, string group, double raw)
    {
        var levels = s.ArcademyAudioLevels ?? Models.AppSettings.DefaultArcademyAudioLevels();
        var clamped = Math.Clamp(raw, 0.0, Models.AppSettings.ArcademyAudioCeiling(group));
        levels[group] = clamped;
        // Reassign so INotifyPropertyChanged fires for a mutation inside the dictionary.
        s.ArcademyAudioLevels = levels;
        return clamped;
    }

    /// <summary>Per-game knobs (manifest-declared, e.g. <c>dt_hard_mode</c>) into the one flat bag.
    /// Bounded: 200 keys, and only scalars - a game that wanted to store a blob here is asking for
    /// the meta store instead.</summary>
    private static object? SetGameSetting(Models.AppSettings s, string key, JToken? value)
    {
        if (value == null || value.Type is JTokenType.Object or JTokenType.Array)
        {
            App.Logger?.Debug("ArcademyHost: per-game setting '{Key}' must be a scalar - refused", key);
            return null;
        }
        if (value.Type == JTokenType.String && ((string?)value)?.Length > 256)
        {
            App.Logger?.Debug("ArcademyHost: per-game setting '{Key}' string too long - refused", key);
            return null;
        }

        var bag = ParseJsonObject(s.ArcademySettingsJson) ?? new JObject();
        if (bag[key] == null && bag.Count >= 200)
        {
            App.Logger?.Warning("ArcademyHost: per-game settings bag is full - '{Key}' dropped", key);
            return null;
        }
        var previous = bag[key]?.DeepClone();
        bag[key] = value!.DeepClone();
        // localAssets is a host-built manifest riding this bag at init; it must never be persisted.
        bag.Remove("localAssets");

        // THE ECHO MUST BE WHAT IS STORED. `ArcademySettingsJson`'s setter DISCARDS the whole bag
        // (stores "") past its own 65536 cap, so a write that tipped it over used to answer with the
        // value the page had asked for while every per-game knob on the machine was silently gone.
        // Two guards: the effective budget here is BELOW the property's cap, so the two bounds
        // (200 keys x 256-char strings = ~55KB worst case) can never meet it; and an over-budget
        // write is refused outright and echoes the value that survived.
        var json = bag.ToString(Formatting.None);
        if (json.Length > MaxGameSettingsBagChars)
        {
            App.Logger?.Warning(
                "ArcademyHost: per-game settings bag would be {N} chars (budget {Cap}) - '{Key}' refused, bag left intact",
                json.Length, MaxGameSettingsBagChars, key);
            return previous;   // null when the key was new, which reads page-side as "not stored"
        }
        s.ArcademySettingsJson = json;
        return bag[key];
    }

    /// <summary>Effective budget for the flat per-game bag. Deliberately BELOW
    /// <see cref="Models.AppSettings.ArcademySettingsJson"/>'s own 65536 cap, because that setter
    /// answers an over-long value by throwing the entire bag away — a silent total loss we must
    /// never be able to reach from here.</summary>
    private const int MaxGameSettingsBagChars = 60000;

    /// <summary>Same posture for the keybind blob against the property's 8192 cap.</summary>
    private const int MaxKeybindsJsonChars = 7000;

    private static string Strip(string key, string prefix) =>
        key.StartsWith(prefix, StringComparison.Ordinal) ? key[prefix.Length..] : key;

    private static JObject? ParseJsonObject(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JObject.Parse(json); } catch { return null; }
    }

    // ============================ class-ended: XP + attendance ============================

    /// <summary>Base XP per grade tier (BUILD-CONTRACT §8). Playtest-tunable, and tunable HERE:
    /// the table is C#-owned so a page cannot mint its own payout.</summary>
    private static readonly Dictionary<int, double> XpBase = new()
    {
        [1] = 40, [2] = 60, [3] = 85, [4] = 110,
    };

    /// <summary>Grade multiplier. Zen reports <c>pass</c> and pays the B row (DECISIONS #1).</summary>
    private static readonly Dictionary<string, double> XpGradeMult = new(StringComparer.OrdinalIgnoreCase)
    {
        ["S"] = 1.5, ["A"] = 1.25, ["B"] = 1.0, ["C"] = 0.6, ["pass"] = 1.0,
    };

    /// <summary>Ceiling on the per-game flavour bonus, so a game can season its payout without
    /// re-inventing the table (SYNTHESIS-NOTES #4).</summary>
    private const double FlavorXpCap = 15;

    /// <summary>
    /// <c>class-ended</c> -> the ONE XP table, the attendance/streak write, and the
    /// <c>payout-result</c> reply. Every input is re-clamped: the page reports what happened, the
    /// host decides what it was worth.
    /// </summary>
    private static void OnClassEnded(JObject o)
    {
        _classActive = false;
        try
        {
            // EVERY FIELD READ DEFENSIVELY, AND SEPARATELY. Newtonsoft throws on a cast it cannot
            // make (`(int?)` over a JSON string, `(double?)` over an object), and a single throw here
            // used to abort the whole handler BEFORE RecordAttendance - so one malformed field cost
            // the player the day's attendance and their streak. Attendance is the thing we must not
            // lose: a garbled grade degrades to C, a garbled flavour bonus to 0, and the credit is
            // still written. gameKey is the only field with no sane default, and it is a plain
            // string cast that cannot throw for any scalar.
            var gameKey = (ReadString(o, "gameKey") ?? "").Trim();
            if (gameKey.Length > 64) gameKey = gameKey[..64];
            int tier = Math.Clamp(ReadInt(o, "gradeTier", 1), 1, 4);
            bool zen = ReadBool(o, "zen", false);
            var grade = (ReadString(o, "grade") ?? "").Trim();
            if (zen || !XpGradeMult.ContainsKey(grade)) grade = zen ? "pass" : "C";
            double flavor = Math.Clamp(ReadDouble(o, "flavorXp", 0), 0, FlavorXpCap);

            // THE FARM GUARD: one payout per class per UTC day. Replaying a class is a supported,
            // deliberately free thing to do (the day's seed makes it the same script), so the
            // second run of the day grades and stamps exactly as before and pays nothing. The
            // ledger is host-owned (ArcademyMetaStore.XpPaidKey) and the day is re-derived here
            // when the page's `dayUtc` is missing or malformed - otherwise dropping the field
            // would be the bypass.
            var dayUtc = (ReadString(o, "dayUtc") ?? "").Trim();
            if (!DateTime.TryParseExact(dayUtc, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out _))
            {
                dayUtc = DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            }
            bool firstToday = _meta?.TryClaimXpDay(gameKey, dayUtc) ?? true;

            double xp = firstToday ? XpBase[tier] * XpGradeMult[grade] + flavor : 0;

            int levelBefore = App.Settings?.Current?.PlayerLevel ?? 0;
            // Same ProgressionService path DTRH's run-ended payout takes; XPSource.Other is the
            // hosted-experience precedent (IntakeHostService, JustDrop, Programs) - Chaos would
            // route Arcademy classes through the descent's companion bonuses and barks.
            if (xp > 0)
            {
                try { App.Progression?.AddXP(xp, XPSource.Other); }
                catch (Exception ex) { App.Logger?.Debug("ArcademyHost payout AddXP: {E}", ex.Message); }
            }
            int levelAfter = App.Settings?.Current?.PlayerLevel ?? levelBefore;

            // LOCAL date rolls attendance; the page's dayUtc only ever seeded the content (#978),
            // so it is deliberately NOT what gets written here. RecordAttendance is idempotent per
            // (day, gameKey), so a retake cannot inflate todayClasses or perfect attendance - and
            // running it unconditionally is what keeps a retake on a NEW local day (same UTC day,
            // player east of UTC) crediting the streak it has earned.
            var localDate = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var (streak, perfect, classesToday) = _meta?.RecordAttendance(localDate, gameKey) ?? (0, 0, 0);
            if (_meta != null) _host?.Post(_meta.SnapshotMessage());

            _host?.Post(new
            {
                type = "payout-result",
                gameKey,
                xp,
                levelUp = levelAfter > levelBefore,
                streak,
                perfectAttendance = perfect,
                classesToday,
                // Additive: the report card reads it to explain a 0 XP line. Older pages ignore it.
                retake = !firstToday,
            });
            App.Logger?.Information(
                "ArcademyHost: class complete ({Game}, tier {Tier}, grade {Grade}) = {Xp:0} XP{Retake}, streak {Streak}, {Today}/3 today",
                gameKey, tier, grade, xp, firstToday ? "" : " (retake - already paid for " + dayUtc + ")",
                streak, classesToday);
        }
        catch (Exception ex) { App.Logger?.Warning("ArcademyHost.OnClassEnded: {E}", ex.Message); }
    }

    // Field readers that degrade instead of throwing. Newtonsoft's explicit JToken casts throw an
    // ArgumentException for a type it cannot convert, which is exactly the wrong failure mode inside
    // a handler whose LAST act is the attendance write (see OnClassEnded).
    private static string? ReadString(JObject o, string name) =>
        o[name] is JValue { Type: JTokenType.String } v ? (string?)v.Value : null;

    private static int ReadInt(JObject o, string name, int fallback)
    {
        try
        {
            return o[name] switch
            {
                JValue { Type: JTokenType.Integer or JTokenType.Float } v => Convert.ToInt32(v.Value, CultureInfo.InvariantCulture),
                JValue { Type: JTokenType.String } s when int.TryParse((string?)s.Value, NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out var parsed) => parsed,
                _ => fallback,
            };
        }
        catch { return fallback; }
    }

    private static double ReadDouble(JObject o, string name, double fallback)
    {
        try
        {
            double d = o[name] switch
            {
                JValue { Type: JTokenType.Integer or JTokenType.Float } v => Convert.ToDouble(v.Value, CultureInfo.InvariantCulture),
                JValue { Type: JTokenType.String } s when double.TryParse((string?)s.Value, NumberStyles.Float,
                    CultureInfo.InvariantCulture, out var parsed) => parsed,
                _ => fallback,
            };
            return double.IsFinite(d) ? d : fallback;
        }
        catch { return fallback; }
    }

    private static bool ReadBool(JObject o, string name, bool fallback) => o[name] switch
    {
        JValue { Type: JTokenType.Boolean } v => (bool)(v.Value ?? fallback),
        _ => fallback,
    };

    // ============================ assets-request (remote media) ============================
    //
    // BRIGHT LINE: this machine talks to the provider directly and the page fetches the media
    // itself. No CC Labs server is ever in the media path, and nothing is cached to disk.
    //
    // The reply NEVER blocks on the network (FlashService's posture): whatever is already
    // prewarmed goes out at once, and later batches stream in under the SAME reqId until the
    // request is satisfied or the pool is dry. A closed gate answers with an empty array rather
    // than silence, because silence is what leaves a page spinning.

    private sealed record AssetUrl(string Url, string Kind, string Mime);

    private static void OnAssetsRequest(JObject o)
    {
        var reqId = (string?)o["reqId"] ?? "";
        int count = Math.Clamp((int?)o["count"] ?? 8, 1, RemoteBatchCap);
        var kind = ((string?)o["kind"] ?? "still").Trim();
        if (kind != "loop" && kind != "still") kind = "still";

        // The niche taxonomy is shared app-wide by design (GROUND-RULES §8): a per-request niche
        // list would fork it. The field is accepted and ignored, loudly once.
        if (o["niches"] != null && !_nichesIgnoredLogged)
        {
            _nichesIgnoredLogged = true;
            App.Logger?.Information(
                "ArcademyHost: assets-request carried 'niches' - ignored, the app-wide FypOnlineNiches selection wins");
        }

        if (!RemoteMediaEnabled() || App.Settings?.Current?.OfflineMode == true)
        {
            _host?.Post(new { type = "assets", reqId, urls = Array.Empty<object>(), done = true });
            return;
        }

        var served = TakeBuffered(kind, count);
        bool satisfied = served.Count >= count;
        _host?.Post(new
        {
            type = "assets",
            reqId,
            urls = served.Select(u => new { url = u.Url, kind = u.Kind, mime = u.Mime }).ToArray(),
            done = satisfied,
        });
        if (!satisfied) ServeRemoteBatch(reqId, kind, count - served.Count);
    }

    private static bool _nichesIgnoredLogged;

    private static List<AssetUrl> TakeBuffered(string kind, int count)
    {
        var taken = new List<AssetUrl>();
        lock (RemoteBuffer)
        {
            if (!RemoteBuffer.TryGetValue(kind, out var buf)) return taken;
            while (buf.Count > 0 && taken.Count < count)
            {
                taken.Add(buf[0]);
                buf.RemoveAt(0);
            }
        }
        return taken;
    }

    /// <summary>Fetch one batch and post it under the original reqId. Single-flight; a second ask
    /// while one is in the air is dropped (the page asks again after every reply). Never throws,
    /// and always posts a terminating message so the page's in-flight latch clears.</summary>
    private static async void ServeRemoteBatch(string reqId, string kind, int want)
    {
        // The window this batch was asked for. A fetch can outlive its window (the provider is
        // throttled ~1 req/s process-wide and a relaunch takes far less), and without this the
        // continuation posted the old window's media into the NEW page under a reqId it never sent,
        // and prewarmed RemoteBuffer that DisposeAll had just cleared.
        int epoch = Volatile.Read(ref _generation);

        if (Interlocked.CompareExchange(ref _remoteFetchInFlight, 1, 0) != 0)
        {
            // Another fetch owns the provider (throttled ~1 req/s process-wide). End THIS exchange
            // rather than leaving it open: an unterminated reqId is a page spinning forever, and the
            // pool it would have received is one ask away.
            _host?.Post(new { type = "assets", reqId, urls = Array.Empty<object>(), done = true });
            return;
        }
        try
        {
            var mediaKind = kind == "loop" ? FeedMediaKind.Video : FeedMediaKind.Image;
            var coord = FypOnlineCoordinator.For(RemoteConsumerId, RemoteChannels, FeedMediaKind.Any);
            var (entries, error) = await coord.FetchBatchAsync(mediaKind, CancellationToken.None)
                .ConfigureAwait(false);

            var fresh = new List<AssetUrl>();
            foreach (var e in entries)
            {
                if (!RemoteMediaFormats.Validate(e, mediaKind, out var reason))
                {
                    App.Logger?.Debug("ArcademyHost: rejected remote entry {Id}: {Reason}", e.Id, reason);
                    continue;
                }
                fresh.Add(new AssetUrl(e.Url, kind, MimeFor(e.Url, kind)));
                if (fresh.Count >= RemoteBatchCap) break;
            }

            var win = _host?.Window;
            if (win == null) return;   // the Arcademy closed while fetching
            if (Volatile.Read(ref _generation) != epoch) return;   // ...or closed and relaunched
            await win.Dispatcher.InvokeAsync(() =>
            {
                if (_host == null || Volatile.Read(ref _generation) != epoch) return;
                var send = fresh.Take(want).ToList();
                lock (RemoteBuffer)
                {
                    if (!RemoteBuffer.TryGetValue(kind, out var buf)) RemoteBuffer[kind] = buf = new List<AssetUrl>();
                    buf.AddRange(fresh.Skip(send.Count));
                    if (buf.Count > 120) buf.RemoveRange(0, buf.Count - 120);
                }
                _host.Post(new
                {
                    type = "assets",
                    reqId,
                    urls = send.Select(u => new { url = u.Url, kind = u.Kind, mime = u.Mime }).ToArray(),
                    // Done either way: an empty pool must end the exchange, not restart it.
                    done = true,
                    error,
                });
            });
        }
        catch (Exception ex)
        {
            App.Logger?.Warning("ArcademyHost: remote batch failed: {E}", ex.Message);
            if (Volatile.Read(ref _generation) == epoch)
            {
                try { _host?.Post(new { type = "assets", reqId, urls = Array.Empty<object>(), done = true }); } catch { }
            }
        }
        finally { Interlocked.Exchange(ref _remoteFetchInFlight, 0); }
    }

    private static string MimeFor(string url, string kind)
    {
        var cut = url.IndexOfAny(new[] { '?', '#' });
        var bare = cut < 0 ? url : url[..cut];
        return Path.GetExtension(bare).ToLowerInvariant() switch
        {
            ".mp4" => "video/mp4",
            ".webm" => "video/webm",
            ".gif" => "image/gif",
            ".png" => "image/png",
            ".webp" => "image/webp",
            ".jpg" or ".jpeg" => "image/jpeg",
            _ => kind == "loop" ? "video/mp4" : "image/jpeg",
        };
    }

    /// <summary>True when remote media may appear anywhere in the app. Copied verbatim from
    /// <c>IntakeHostService.RemoteMediaEnabled()</c> (the canonical gate): reads
    /// <c>HasRemoteMediaConsent</c>, never the raw consent flags.</summary>
    private static bool RemoteMediaEnabled()
    {
        var s = App.Settings?.Current;
        return s != null && s.MediaSource != "local" && s.HasRemoteMediaConsent;
    }

    /// <summary>One niche taxonomy app-wide; only the rotation/dwell state is per-consumer, which
    /// is what asking for our own tenant buys.</summary>
    private static IReadOnlyList<string> RemoteChannels()
    {
        var s = App.Settings?.Current;
        return FypOnlineCoordinator.ResolveChannels(s?.FypOnlineNiches, s?.FypOnlineCustomSubs);
    }

    // ============================ native state hooks ============================

    /// <summary>A mandatory video fully covers the class: tell the page to drop every effect and
    /// pause, and hand keyboard focus back when the video closes (video clicks steal activation).
    /// ProtectBrowserVideoPlayback is projected at init as well, so the page knows not to fire
    /// effects over a browser video either.</summary>
    private static void HookVideoEvents(bool on)
    {
        try
        {
            // Flag first, service second. The early return used to run BEFORE `_videoHooked = false`,
            // so a teardown that found App.Video null (shutdown order) left the flag true and the NEXT
            // launch's HookVideoEvents(true) refused to subscribe - the Arcademy then never suspended
            // for a mandatory video for the rest of the app session.
            if (!on) _videoHooked = false;
            if (App.Video == null) return;
            if (on && !_videoHooked)
            {
                App.Video.VideoStarted += OnVideoStarted;
                App.Video.VideoEnded += OnVideoEnded;
                _videoHooked = true;
            }
            else if (!on)
            {
                App.Video.VideoStarted -= OnVideoStarted;
                App.Video.VideoEnded -= OnVideoEnded;
            }
        }
        catch { }
    }

    /// <summary>
    /// BROWSER VIDEO. <c>init.protectBrowserVideo</c> is a real promise, not a label: when the user
    /// has ProtectBrowserVideoPlayback on, a web-video takeover covering the screen must freeze the
    /// class exactly like a mandatory video does. <see cref="Services.Browser.BrowserMediaService"/>
    /// raises <c>PlayingChanged</c> on the UI thread when its playback state flips, which is the one
    /// signal this can honestly hang off — the gate itself
    /// (<see cref="Services.Browser.BrowserMediaService.ResolveDeferInterruptions"/>) is a poll, and
    /// polling a class's freeze state would be worse than not having it.
    /// </summary>
    private static void HookBrowserVideoEvents(bool on)
    {
        try
        {
            if (!on) _browserVideoHooked = false;
            if (App.BrowserMedia == null) return;
            if (on && !_browserVideoHooked)
            {
                App.BrowserMedia.PlayingChanged += OnBrowserVideoPlayingChanged;
                _browserVideoHooked = true;
            }
            else if (!on)
            {
                App.BrowserMedia.PlayingChanged -= OnBrowserVideoPlayingChanged;
            }
        }
        catch { }
    }

    private static void OnBrowserVideoPlayingChanged(object? sender, bool playing)
    {
        // Read the preference LIVE rather than caching the init snapshot: a user who turns the
        // protection off mid-class expects the next clip not to freeze them.
        if (App.Settings?.Current?.ProtectBrowserVideoPlayback != true) return;
        if (playing) { Suspend(true, "video"); return; }
        // Do not un-freeze over a mandatory video / audio-only session that is still running, and
        // never un-freeze a PANIC suspend - that one only lifts on the user's own resume-request.
        if (_panicSuspended || App.Video?.IsPlaying == true
            || App.Settings?.Current?.AudioOnlySession == true) return;
        Suspend(false, "video");
    }

    private static void OnVideoStarted(object? sender, EventArgs e) => Suspend(true, "video");

    private static void OnVideoEnded(object? sender, EventArgs e)
    {
        var disp = Application.Current?.Dispatcher;
        if (disp == null || disp.HasShutdownStarted || _host == null) return;
        // A PANIC suspend outranks the video's own un-freeze: the user pressed the emergency stop,
        // and a video ending is not them asking to be put back in a class. It lifts on their
        // resume-request and nowhere else.
        if (_panicSuspended) { App.Logger?.Debug("ArcademyHost: video ended but a panic suspend still stands"); return; }
        disp.BeginInvoke(() =>
        {
            _host?.Post(new { type = "suspend", on = false, reason = "video" });
            _host?.FocusWeb();
        });
    }

    /// <summary>
    /// Watch the settings the page was told about at init. Two jobs:
    ///   * <c>AudioOnlySession</c> flipping ON mid-class -> <c>suspend</c> (the owner ruling keeps
    ///     the streak; the class simply stops).
    ///   * every other projected key -> a <c>setting</c> echo, so a dial moved in the WPF UI while
    ///     the Arcademy is open lands in the page instead of drifting until the next launch.
    /// </summary>
    private static void HookSettingsWatch(bool on)
    {
        try
        {
            // Unsubscribe from the instance we actually hooked, not from whatever App.Settings.Current
            // happens to be NOW - a cloud restore or a factory Reset SWAPS the object (see
            // CurrentReplaced below) and unhooking the new one would leave the old handler alive
            // forever. Flags are cleared before any early return, same lesson as HookVideoEvents.
            if (!on)
            {
                if (_hookedSettings != null) _hookedSettings.PropertyChanged -= OnSettingChangedInApp;
                _hookedSettings = null;
                if (_settingsReplaceHooked && App.Settings != null)
                    App.Settings.CurrentReplaced -= OnSettingsCurrentReplaced;
                _settingsReplaceHooked = false;
                return;
            }

            var s = App.Settings?.Current;
            if (s != null && !ReferenceEquals(s, _hookedSettings))
            {
                if (_hookedSettings != null) _hookedSettings.PropertyChanged -= OnSettingChangedInApp;
                s.PropertyChanged += OnSettingChangedInApp;
                _hookedSettings = s;
            }
            // ...and FOLLOW the instance. SettingsService.RestoreFrom / Reset do not mutate the
            // settings object, they replace it and raise CurrentReplaced; without this the Arcademy
            // spent the rest of its session listening to a discarded object, which silently killed
            // BOTH jobs of this watch - the audio-only suspend and every live `setting` echo.
            // OverlayService and ModService already follow the same event.
            if (!_settingsReplaceHooked && App.Settings != null)
            {
                App.Settings.CurrentReplaced += OnSettingsCurrentReplaced;
                _settingsReplaceHooked = true;
            }
        }
        catch { }
    }

    private static void OnSettingsCurrentReplaced()
    {
        if (_host == null) return;
        try
        {
            var s = App.Settings?.Current;
            if (s == null || ReferenceEquals(s, _hookedSettings)) return;
            if (_hookedSettings != null) _hookedSettings.PropertyChanged -= OnSettingChangedInApp;
            s.PropertyChanged += OnSettingChangedInApp;
            _hookedSettings = s;
            App.Logger?.Information("ArcademyHost: re-bound to the replaced AppSettings instance");
            // The restored values may differ from what the page is painting; the page's model only
            // ever moves on an echo, so push the whole projection's worth of keys once.
            RepushProjectedSettings(s);
            if (s.AudioOnlySession) Suspend(true, "audio-only");
        }
        catch (Exception ex) { App.Logger?.Warning("ArcademyHost: settings rebind failed: {E}", ex.Message); }
    }

    /// <summary>Re-echo every key the init projection carries. Used after the settings instance is
    /// swapped underneath us, where no PropertyChanged fires for the values that moved.</summary>
    private static void RepushProjectedSettings(Models.AppSettings s)
    {
        foreach (var prop in ProjectedProperties)
        {
            var (key, value) = ProjectedSetting(s, prop);
            if (key == null) continue;
            try { _host?.Post(new { type = "setting", key, value }); }
            catch (Exception ex) { App.Logger?.Debug("ArcademyHost: re-echo {Key} failed: {E}", key, ex.Message); }
        }
    }

    private static readonly string[] ProjectedProperties =
    {
        nameof(Models.AppSettings.ArcademyMasterIntensity),
        nameof(Models.AppSettings.ArcademyCapFlashRate),
        nameof(Models.AppSettings.ArcademyCapFlashOpacity),
        nameof(Models.AppSettings.ArcademyCapSubDensity),
        nameof(Models.AppSettings.ArcademyCapDuckDepth),
        nameof(Models.AppSettings.ArcademyCapBubbleRate),
        nameof(Models.AppSettings.ArcademyCapBinauralDepth),
        nameof(Models.AppSettings.ArcademyCapBgIntensity),
        nameof(Models.AppSettings.ArcademyAudioMute),
        nameof(Models.AppSettings.ArcademyHideTutorial),
        nameof(Models.AppSettings.ArcademyAudioLevels),
        nameof(Models.AppSettings.ChaosEffectIntensity),
        nameof(Models.AppSettings.MasterVolume),
        nameof(Models.AppSettings.RemoteMediaRatio),
        nameof(Models.AppSettings.MediaSource),
        nameof(Models.AppSettings.OfflineMode),
        nameof(Models.AppSettings.MotionLevel),
        nameof(Models.AppSettings.ProtectBrowserVideoPlayback),
    };

    private static void OnSettingChangedInApp(object? sender, PropertyChangedEventArgs e)
    {
        if (_host == null) return;
        var s = App.Settings?.Current;
        if (s == null) return;
        try
        {
            if (e.PropertyName == nameof(Models.AppSettings.AudioOnlySession))
            {
                if (s.AudioOnlySession)
                {
                    App.Logger?.Information("ArcademyHost: audio-only session started{Mid} - suspending",
                        _classActive ? " mid-class" : "");
                    Suspend(true, "audio-only");
                }
                else if (_panicSuspended)
                {
                    App.Logger?.Debug("ArcademyHost: audio-only ended but a panic suspend still stands");
                }
                else Suspend(false, "audio-only");
                return;
            }

            if (_suppressSettingEcho) return;   // the page's own write already gets a reply
            var (key, value) = ProjectedSetting(s, e.PropertyName);
            if (key == null) return;
            _host.Post(new { type = "setting", key, value });
        }
        catch (Exception ex) { App.Logger?.Debug("ArcademyHost.OnSettingChangedInApp: {E}", ex.Message); }
    }

    /// <summary>Map a changed AppSettings property to its init-projection key + resolved value, or
    /// (null, null) for a property the page was never told about.</summary>
    private static (string? Key, object? Value) ProjectedSetting(Models.AppSettings s, string? prop) => prop switch
    {
        nameof(Models.AppSettings.ArcademyMasterIntensity) => ("masterIntensity", s.ArcademyMasterIntensity),
        nameof(Models.AppSettings.ArcademyCapFlashRate) => ("caps.flashRate", s.ArcademyCapFlashRate),
        nameof(Models.AppSettings.ArcademyCapFlashOpacity) => ("caps.flashOpacity", s.ArcademyCapFlashOpacity),
        nameof(Models.AppSettings.ArcademyCapSubDensity) => ("caps.subDensity", s.ArcademyCapSubDensity),
        nameof(Models.AppSettings.ArcademyCapDuckDepth) => ("caps.duckDepth", s.ArcademyCapDuckDepth),
        nameof(Models.AppSettings.ArcademyCapBubbleRate) => ("caps.bubbleRate", s.ArcademyCapBubbleRate),
        nameof(Models.AppSettings.ArcademyCapBinauralDepth) => ("caps.binauralDepth", s.ArcademyCapBinauralDepth),
        nameof(Models.AppSettings.ArcademyCapBgIntensity) => ("caps.bgIntensity", s.ArcademyCapBgIntensity),
        nameof(Models.AppSettings.ArcademyAudioMute) => ("audioMute", s.ArcademyAudioMute),
        nameof(Models.AppSettings.ArcademyHideTutorial) => ("hideTutorial", s.ArcademyHideTutorial),
        nameof(Models.AppSettings.ArcademyAudioLevels) => ("audioLevels", BuildAudioLevels(s)),
        nameof(Models.AppSettings.ChaosEffectIntensity) => ("effectIntensity", s.ChaosEffectIntensity),
        nameof(Models.AppSettings.MasterVolume) => ("masterVolume", Math.Clamp(s.MasterVolume / 100.0, 0.0, 1.0)),
        nameof(Models.AppSettings.RemoteMediaRatio) => ("remoteMediaRatio", Math.Clamp(s.RemoteMediaRatio / 100.0, 0.0, 1.0)),
        nameof(Models.AppSettings.MediaSource) => ("remoteMediaEnabled", RemoteMediaEnabled()),
        nameof(Models.AppSettings.OfflineMode) => ("offlineMode", s.OfflineMode),
        nameof(Models.AppSettings.MotionLevel) => ("motionLevel", ResolvedMotionLevel()),
        nameof(Models.AppSettings.ProtectBrowserVideoPlayback) => ("protectBrowserVideo", s.ProtectBrowserVideoPlayback),
        _ => (null, null),
    };

    // ============================ watchdogs / recovery ============================

    /// <summary>Progress-aware boot deadline. Re-armed by <see cref="OnPageMessage"/>; cancelled
    /// once the page says <c>ready</c>. A page that never reports anything for 45s is a boot that
    /// failed silently, and silence is exactly what the heartbeat watchdog cannot see (it only
    /// runs after IsReady).</summary>
    private static void ArmBootDeadline()
    {
        CancelBootDeadline();
        _lastProgressUtc = DateTime.UtcNow;
        _bootWatch = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _bootWatch.Tick += (_, _) =>
        {
            if (_host == null || _host.IsReady || _exiting) { CancelBootDeadline(); return; }
            if (DateTime.UtcNow - _lastProgressUtc < BootDeadline) return;
            CancelBootDeadline();
            OnBootError($"boot deadline: no progress for {BootDeadline.TotalSeconds:0}s");
        };
        _bootWatch.Start();
    }

    private static void CancelBootDeadline()
    {
        try { _bootWatch?.Stop(); } catch { }
        _bootWatch = null;
    }

    private static void StartHeartbeatWatch()
    {
        StopHeartbeatWatch();
        _lastHeartbeatUtc = DateTime.UtcNow;
        _heartbeatWatch = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _heartbeatWatch.Tick += (_, _) =>
        {
            // Guarded on IsReady: the page only starts beating after boot, so a still-loading page
            // cannot false-trip (that window belongs to the boot deadline above). A wedged page
            // main thread also kills the JS Esc-hold exit, which is why this must exist at all.
            if (_host == null || !_host.IsReady || _exiting) return;
            var silent = (DateTime.UtcNow - _lastHeartbeatUtc).TotalSeconds;
            var limit = _classActive ? 12 : 20;
            if (silent > limit)
            {
                App.Logger?.Warning("ArcademyHost: page heartbeat silent >{Limit}s ({Where}) - recovering",
                    limit, _classActive ? "mid-class" : "shell");
                Recover("heartbeat-silent");
            }
        };
        _heartbeatWatch.Start();
    }

    private static void StopHeartbeatWatch()
    {
        try { _heartbeatWatch?.Stop(); } catch { }
        _heartbeatWatch = null;
    }

    private static void OnProcessFailed(CoreWebView2ProcessFailedKind kind) => Recover($"process-failed:{kind}");

    /// <summary>Relaunch once per session; a second failure gives up cleanly. A relaunch re-runs the
    /// gates, so an entitlement that lapsed mid-session does not get a free second window.</summary>
    private static void Recover(string reason)
    {
        var disp = Application.Current?.Dispatcher;
        if (disp == null) { DisposeAll(); return; }
        disp.BeginInvoke(() =>
        {
            var retry = !_relaunchedOnce;
            App.Logger?.Warning("ArcademyHost: recovery ({Reason}) - {Action}",
                reason, retry ? "relaunching once" : "giving up");
            DisposeAll();
            if (retry)
            {
                _relaunchedOnce = true;
                Launch();
            }
        });
    }

    /// <summary>The page's boot failed (or never started). Tear down and SAY so: a black window
    /// the user has to guess about is the worse failure.</summary>
    private static void OnBootError(string? msg)
    {
        App.Logger?.Warning("ArcademyHost: boot-error: {Msg}", msg);
        BootFailedThisSession = true;
        var disp = Application.Current?.Dispatcher;
        if (disp == null) { DisposeAll(); return; }
        disp.BeginInvoke(() =>
        {
            DisposeAll();
            try
            {
                MessageBox.Show(
                    $"{ProductName} could not start on this machine.\n\n{msg}",
                    ProductName, MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch { }
        });
    }

    // ============================ teardown ============================

    private static void ArmExitWatchdog()
    {
        CancelExitWatchdog();
        _exitWatchdog = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1200) };
        _exitWatchdog.Tick += (_, _) => DisposeAll();
        _exitWatchdog.Start();
    }

    private static void CancelExitWatchdog()
    {
        try { _exitWatchdog?.Stop(); } catch { }
        _exitWatchdog = null;
    }

    private static void DisposeAll()
    {
        if (_disposing) return;   // _host.Dispose() closes the window, re-raising Closed -> here
        _disposing = true;
        try
        {
            // Retire this window's generation FIRST: every async continuation still in the air
            // (ServeRemoteBatch) checks it and drops itself rather than posting into the next window.
            Interlocked.Increment(ref _generation);
            CancelExitWatchdog();
            CancelBootDeadline();
            StopHeartbeatWatch();
            HookVideoEvents(false);
            HookSettingsWatch(false);
            HookBrowserVideoEvents(false);
            try { _meta?.FlushSave(); } catch { }
            _meta = null;
            _classActive = false;
            _panicSuspended = false;
            _lastPanicPressUtc = DateTime.MinValue;
            _initPosted = false;
            lock (RemoteBuffer) RemoteBuffer.Clear();
            try { _host?.Dispose(); } catch { }
            _host = null;
            _exiting = false;
            // Bring the control panel back from the tray if we tucked it away at launch. Every
            // close path funnels through here (title-bar X, page exit, boot-error, the recovery
            // ladder, CloseActive) precisely so this cannot be skipped.
            if (_minimizedMainWindow)
            {
                _minimizedMainWindow = false;
                try { (Application.Current?.MainWindow as MainWindow)?.ShowFromTray(); } catch { }
            }
            App.Logger?.Information("ArcademyHostService: closed");
        }
        finally { _disposing = false; }
    }

    // ============================ small resolvers ============================

    private static bool SafeHasHaptics()
    {
        try { return App.Haptics?.IsConnected == true; }
        catch { return false; }
    }

    private static string SafeActiveModId()
    {
        try { return App.Mods?.ActiveModId ?? "builtin-bambisleep"; }
        catch { return "builtin-bambisleep"; }
    }
}
