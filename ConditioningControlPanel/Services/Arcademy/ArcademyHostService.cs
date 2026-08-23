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

    // ============================== the gate ==============================

    /// <summary>
    /// Single source of truth for "is there an Arcademy door". Everything that shows, hides or
    /// opens the Arcademy asks this - the Play card's visibility in
    /// <c>MainWindow.RefreshPlayCards</c> and the refusal at the top of <see cref="Launch"/>.
    ///
    /// <para><c>false</c> because the Arcademy is BUILT but not launched: Semester 1 landed on
    /// main (PR #241) ahead of its public reveal, and 6.8.4 is an auth/stability patch that must
    /// ship those fixes without also shipping an unannounced feature. A HIDE, not a lockband, for
    /// the same reason Just Drop hides: a lockband advertises something the account could buy,
    /// and a door we have not opened yet is not for sale.</para>
    ///
    /// <para>Flip to <c>true</c> to reveal it - that is the whole reveal. The T2 bar and the
    /// AudioOnlySession rule below are untouched and still apply underneath it.</para>
    /// </summary>
    /// <remarks>static readonly, not const: a const would make the guard in <see cref="Launch"/>
    /// compile-time unreachable (CS0162), exactly as JustDropService.Withheld documents.</remarks>
    public static readonly bool DoorAvailable = false;
    /// <summary>Whether the live instance was opened through the dev switch (recovery relaunch keeps it).</summary>
    private static bool _devDoor;

    // ============================ launch / close ============================

    /// <summary>
    /// Open the Arcademy window. Idempotency FIRST (a live instance is only ever re-focused, so
    /// a window that is already open can always be brought back - even after AudioOnlySession was
    /// switched on mid-class, which suspends the class rather than closing the window), then the
    /// gates for a FRESH launch, each failing closed: the door, T2, then AudioOnlySession.
    /// </summary>
    /// <summary>
    /// The <c>--arcademy</c> dev switch. Bypasses ONLY the door (rule 2) so an unreleased build can be
    /// play-tested from the command line; T2 and AudioOnlySession still apply underneath. Not
    /// reachable from any UI - the switch is parsed once in App.OnStartup.
    /// </summary>
    public static void LaunchDev() => Launch(devDoor: true);

    public static void Launch() => Launch(devDoor: false);

    private static void Launch(bool devDoor)
    {
        // 1. Idempotent: never re-launch a live class, and never strand an open window behind a
        //    gate that only applies to opening a NEW one (the mid-class AudioOnlySession flip
        //    arrives as a `suspend` push - see OnSettingChangedInApp - and the window stays up).
        if (_host != null) { _host.FocusWeb(); return; }

        // 2. The door itself. The card is collapsed while this is false so nothing in the UI can
        //    reach here - but the handler is internal and the card is one XAML edit away from
        //    visible, and the house rule is that the code path which actually opens the door has
        //    to be the one that can say no (see the BtnStartArcademy_Click docs). Silent: there is
        //    no announced feature to explain a refusal about yet.
        if (!DoorAvailable && !devDoor)
        {
            App.Logger?.Information("ArcademyHost.Launch refused: the Arcademy door is not open yet (unreleased)");
            return;
        }

        // 3. The T2 bar. TierGate is the one truth for "may this account open that?" and it
        //    fails closed while App.Patreon is null; DemandLab also raises the standard refusal
        //    toast with its "See tiers" action, which is the whole gate UX.
        if (!TierGate.DemandLab(ProductName)) return;

        // 4. AudioOnlySession. Owner ruling: v1 SKIPS the Arcademy on audio-only days rather than
        //    substituting audio-capable classes; the attendance streak is preserved (frozen, not
        //    broken) because nothing was missed - the day simply had no visual classes in it.
        if (App.Settings?.Current?.AudioOnlySession == true)
        {
            RefuseForAudioOnly();
            return;
        }

        _devDoor = devDoor;
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
                // THE LOOM: the player's own saved spirals (the same folder DTRH exposes). The
                // shell's spiral pool mixes them in via settings.loomSpirals; a missing folder
                // is simply an empty list, so create it the way DtrhHostService does.
                ("ccp.spirals", Chaos.DtrhLoomStore.SpiralsFolder, CoreWebView2HostResourceAccessKind.Allow),
            };
            try { Directory.CreateDirectory(Chaos.DtrhLoomStore.SpiralsFolder); }
            catch (Exception ex) { App.Logger?.Debug("ArcademyHost: spirals dir create failed: {E}", ex.Message); }
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
        // The Loom's saved spirals, as ccp.spirals urls (the DTRH shape, verbatim). The page's
        // spiral pool appends them to the bundled set; an empty list changes nothing.
        try
        {
            bag["loomSpirals"] = new JArray(Chaos.DtrhLoomStore.List()
                .Select(sp => (object)$"https://ccp.spirals/loom_{sp.Slug}.gif").ToArray());
        }
        catch (Exception ex) { App.Logger?.Debug("ArcademyHost.BuildSettingsBag loom: {E}", ex.Message); }
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
        ["game_the_deep_end"] = "The Deep End",
        // Semester II ghost labels on the campus plan (behind the tape). Same
        // game_<key> convention the registry will use once those classes ship.
        ["game_misdirection"] = "Misdirection",
        ["game_instant_recall"] = "Instant Recall",
        ["game_echo"] = "Echo",
        // ---- campus (the Direction A hub, shell/campus.js) ------------------------------
        // Room names are diegetic and FIXED to their game - a game always lives in its
        // room. Every value must stay under MergeModTable's 96-char skin cap.
        ["student"] = "Student",
        ["campus_room_daily_trigger"] = "Homeroom",
        ["campus_room_deja_vu"] = "Memory Lab",
        ["campus_room_impulse_control"] = "Discipline Hall",
        ["campus_room_lost_and_found"] = "Lost & Found",
        ["campus_room_the_deep_end"] = "The Pool",
        ["campus_desc_daily_trigger"] = "One word, six chances. The whole school sits the same word today.",
        ["campus_desc_deja_vu"] = "Pairs that move when you blink. The board settles only when you stop looking.",
        ["campus_desc_impulse_control"] = "Hands on the desk. Move only when told - the room will lie to you.",
        ["campus_desc_lost_and_found"] = "Things went missing in a wall of moving pictures. Find them before they move again.",
        ["campus_desc_the_deep_end"] = "Sink tile into tile. The deeper you go, the harder the board is to read.",
        ["campus_records"] = "Records",
        ["campus_desc_records"] = "Report card, attendance ledger, grades. Your whole term, in ink.",
        ["campus_registrar"] = "Registrar",
        ["campus_desc_registrar"] = "Every setting is a form. Every consent, a waiver with a stamp.",
        ["campus_entrance_hall"] = "Entrance Hall",
        ["campus_desc_entrance"] = "The notice board carries announcements. The trophy case waits for your diplomas.",
        ["campus_notice_board"] = "Notice Board",
        ["campus_trophy_case"] = "Trophy Case",
        ["campus_admissions"] = "Admissions",
        ["campus_bell_tower"] = "Bell Tower",
        ["campus_main_gate"] = "Main Gate",
        ["campus_main_hall"] = "Main Hall",
        ["campus_the_quad"] = "The Quad",
        ["campus_front_path"] = "Front Path",
        ["campus_east_wing"] = "East Wing",
        ["campus_west_wing"] = "West Wing",
        ["campus_desc_east"] = "You can hear hammering behind the tape.",
        ["campus_desc_west"] = "The boards are older here.",
        ["campus_sealed"] = "Sealed",
        ["campus_opens_semester_2"] = "Opens Semester II",
        ["campus_semester_3"] = "Semester III",
        ["campus_in_session"] = "In Session",
        ["campus_not_tonight"] = "Not tonight",
        ["campus_next_bell"] = "Next Bell",
        ["campus_step_inside"] = "Step inside",
        ["campus_xp_first"] = "First pass of the day pays XP.",
        ["campus_xp_retake"] = "Retakes pay no XP - pride only.",
        ["campus_hint"] = "Hover a room - click to step inside.",
        ["campus_night_sessions"] = "Night Sessions",
        ["campus_rm"] = "RM",
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
        ["dt_lesson_header"] = "Today's Lesson",
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
        // the chalk whispers (Unreliable Label): the TEXT lies, the glyphs are the truth
        ["dt_whisper_1"] = "Forget the pink ones.",
        ["dt_whisper_2"] = "It was never in row two.",
        ["dt_whisper_3"] = "You already typed it.",
        ["dt_whisper_4"] = "The stars are lying, not me.",
        // ---- Deja Vu (games/deja-vu) --------------------------------------------------
        ["dv_bell"] = "The bell. Time is up.",
        ["dv_called_it"] = "You called the lie.",
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
        ["dv_rack_label"] = "Specimens",
        // the deja re-deal (House Rules Deck III, the native signature)
        ["dv_redeal_stamp"] = "DEJA VU",
        ["dv_redeal_hint"] = "One of those was a lie.",
        ["dv_redeal_gift"] = "The machine blinked.",
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
        ["lf_density"] = "Board density",
        ["lf_density_hint"] = "How crowded the wall deals: easy, medium, or hard (near-impossible).",
        ["lf_found"] = "Found her",
        ["lf_howto_title"] = "Class rules",
        ["lf_howto_find"] = "She hides on a wall that never sits still. Spot the tile that matches her picture.",
        ["lf_howto_five"] = "Every find, she relocates. Catch her five times.",
        ["lf_howto_go"] = "Start the hunt",
        ["lf_jackpot"] = "Jackpot",
        ["lf_melt"] = "The wall runs like wax",
        ["lf_misclick"] = "Wrong one",
        ["lf_misclick_streak"] = "Focus",
        ["lf_misses"] = "Misses",
        ["lf_modifier"] = "The board wakes up",
        ["lf_peek_input"] = "Peek input",
        ["lf_peek_input_hint"] = "Hold the key, tap to toggle, or let the pointer decide.",
        ["lf_peek_key"] = "Peek key",
        ["lf_relocate"] = "It moved - the same glitch hides the churn",
        ["lf_royal"] = "ROYAL PAYOUT",
        ["lf_timeout"] = "Time",
        ["lf_trick_seen"] = "Did you see that?",
        ["lf_warm"] = "Warm",
        ["lf_zen"] = "Zen mode",
        ["lf_zen_clock"] = "--:--",
        ["lf_zen_hint"] = "No clock and no grade. The class still counts for attendance.",
        // ---- Impulse Control (games/impulse-control, mirrors lex.js IC_LEX) ------------
        ["ic_baseline"] = "baseline",
        ["ic_baseline_new"] = "Baseline established. Later classes are scored against it.",
        ["ic_best_rt"] = "best pop",
        ["ic_bg_fade"] = "Backdrop visibility",
        ["ic_bubble_n"] = "Bubble",
        ["ic_debrief"] = "Debrief",
        ["ic_denied_hit"] = "THAT WAS THE X",
        ["ic_denied_pass"] = "Withheld",
        ["ic_drifted"] = "drifted",
        ["ic_gate_hint"] = "S needs an untouched X row AND real speed.",
        ["ic_go_hint"] = "Click the bubble or press {key}. An X means hold still.",
        ["ic_go_key"] = "POP key",
        ["ic_hold"] = "HOLD",
        ["ic_incoming"] = "INCOMING",
        ["ic_loading"] = "Priming the tube - hold still, subject.",
        ["ic_median_rt"] = "median pop",
        ["ic_missed"] = "It drifted away",
        ["ic_new_best"] = "NEW BEST",
        ["ic_personal_record"] = "personal record",
        ["ic_pop"] = "POP",
        ["ic_pop_fast"] = "Quick",
        ["ic_pop_ok"] = "Popped",
        ["ic_pop_perfect"] = "PERFECT",
        ["ic_popped"] = "popped",
        ["ic_recalibrate"] = "Recalibrate baseline",
        ["ic_recalibrate_confirm"] = "Tap again to confirm",
        ["ic_recalibrated"] = "Baseline cleared - the next class recalibrates.",
        ["ic_restraint"] = "restraint",
        ["ic_score"] = "Score",
        ["ic_show_rt"] = "Show reaction time",
        ["ic_slip_both"] = "Both axes slipped. Reassessment recommended.",
        ["ic_slip_none"] = "Speed and restraint both held. Filed.",
        ["ic_slip_restraint"] = "Pops on record. The X got you - reassessment recommended.",
        ["ic_slip_speed"] = "Restraint held. Pops off your record - reassessment recommended.",
        ["ic_streak"] = "streak",
        ["ic_subject"] = "Subject",
        ["ic_submit"] = "Submit report",
        ["ic_tube_rules"] = "Pop every bubble the instant it surfaces. NEVER touch the X.",
        ["ic_tube_title"] = "The Drop Tube",
        ["ic_x_held"] = "X held",
        ["ic_x_popped"] = "X popped",
        // ---- The Deep End (games/the-deep-end, mirrors lex.js DE_LEX) ------------------
        ["de_bell_line"] = "The bell. Up you come.",
        ["de_bell_warn"] = "Twenty seconds.",
        ["de_board_size"] = "Board size",
        ["de_board_size_hint"] = "5x5 slows the pressure for a longer soak. Only 4x4 can earn an S.",
        ["de_brief"] = "Swipe. Equal tiles sink together. Every depth makes the room heavier.",
        ["de_ceiling_line"] = "Tier eleven. The ladder ends here, warm.",
        ["de_chip_chain"] = "Chain",
        ["de_chip_clock"] = "Time left",
        ["de_chip_depth"] = "Depth",
        ["de_chip_score"] = "Score",
        ["de_end_best"] = "Best dive",
        ["de_end_ceiling"] = "Reached the end of the ladder",
        ["de_end_chains"] = "Chains",
        ["de_end_dare"] = "Lifetime deepest",
        ["de_end_dare_first"] = "Your first mark on the ladder. Beat it next class.",
        ["de_end_dare_line"] = "Your standing dare. Beat it next class.",
        ["de_end_efficiency"] = "Merges per swipe",
        ["de_end_no"] = "No",
        ["de_end_resurfaces"] = "Resurfaces",
        ["de_end_score"] = "Score",
        ["de_end_survival"] = "Survived the bell",
        ["de_end_title"] = "Dive report",
        ["de_end_yes"] = "Yes",
        ["de_exhale_line"] = "Exhale. The room eases for ten seconds, and the next tile will fit.",
        ["de_jackpot"] = "JACKPOT",
        ["de_lifetime_new"] = "A new lifetime depth.",
        ["de_near_miss"] = "SO CLOSE",
        ["de_new_depth"] = "New depth.",
        ["de_play_hint"] = "Arrows, WASD or swipe.",
        ["de_resurface_line"] = "The board locked. The depth is banked. Fresh water.",
        ["de_retake"] = "Retake",
        ["de_silt_line"] = "Silt. It slides, it never sinks, it never leaves.",
        ["de_stamp_bell"] = "BELL",
        ["de_stamp_ceiling"] = "ALL THE WAY DOWN",
        ["de_stamp_depth"] = "NEW DEPTH",
        ["de_stamp_resurface"] = "RESURFACE",
        ["de_strain"] = "Almost. They strain toward each other.",
        ["de_tier_1"] = "Awake",
        ["de_tier_10"] = "Trench",
        ["de_tier_11"] = "Blackout",
        ["de_tier_2"] = "Fuzzy",
        ["de_tier_3"] = "Drowsy",
        ["de_tier_4"] = "Heavy",
        ["de_tier_5"] = "Drifting",
        ["de_tier_6"] = "Sinking",
        ["de_tier_7"] = "Sunken",
        ["de_tier_8"] = "Submerged",
        ["de_tier_9"] = "Fathoms",
        ["de_tier_silt"] = "Silt",
        ["de_trick_melt"] = "The shallows run like wax",
        ["de_trick_seen"] = "Did you see that?",
        // ---- Free Swim (shell, mirrors core/lexicon.js)
        ["free_swim"] = "Free Swim",
        ["free_swim_hint"] = "Untimed practice. No grade, no XP, no attendance.",
        ["de_stuck_hint"] = "Nothing is locked. The lit edges still move.",
        ["de_tile_faces"] = "Tile faces",
        ["de_tile_faces_hint"] = "Your own media on every tile, tinted by depth. Still = no loops. Plain = colour only.",
        ["de_tile_faces_media"] = "Media",
        ["de_tile_faces_plain"] = "Plain",
        ["de_tile_faces_still"] = "Still",
        ["de_brief_free"] = "No bell tonight. Sink as far as you like - tap Surface when you are done.",
        ["de_end_dives"] = "Dives",
        ["de_end_time"] = "Time",
        ["de_end_title_free"] = "Free swim over",
        ["de_free_swim"] = "Free Swim",
        ["de_free_swim_hint"] = "No bell, no grade - swim until you surface.",
        ["de_surface"] = "Surface",
        // ---- Impulse Control - House Rules wave (casino words + class-rules sheet)
        ["ic_almost"] = "ALMOST",
        ["ic_howto_drift"] = "A bubble you miss just drifts off the dish. Nothing is taken from you.",
        ["ic_howto_go"] = "Start the drop",
        ["ic_howto_pop"] = "A bubble lands in the dish. Pop it at once. The faster you are, the more it pays.",
        ["ic_howto_title"] = "Class rules",
        ["ic_howto_x"] = "A bubble wearing an X is a trap. Touch nothing until its ring runs out.",
        ["ic_jackpot"] = "JACKPOT",
        ["ic_just"] = "JUST",
        ["ic_perfect_class"] = "Perfect class",
        ["ic_record_ping"] = "record",
        ["ic_royal"] = "ROYAL",
        ["ic_streak_n"] = "chain {n}",
        ["ic_tonight"] = "tonight only",
        // ---- Semester II/III game + family rows (2026-08-23)
        ["family_puzzle"] = "puzzle",
        ["family_recall"] = "recall",
        ["game_anomaly"] = "Anomaly",
        ["game_composure"] = "Composure",
        // ---- campus wings: Semester II/III rooms (2026-08-23)
        ["campus_desc_anomaly"] = "Everything in here matches. One thing does not. Find it before it moves.",
        ["campus_desc_composure"] = "Slide the picture back together while the room does its best to blur it.",
        ["campus_desc_east_open"] = "The tape is down. Wet paint, three new doors, nobody at the desk.",
        ["campus_desc_echo"] = "It plays a line, you play it back. Then it adds one more, every time.",
        ["campus_desc_instant_recall"] = "Watch the whole hour, then answer for it. You never hear it coming.",
        ["campus_desc_misdirection"] = "Keep your eyes on the one that matters. It will not make that easy.",
        ["campus_desc_west_open"] = "Older boards, deeper rooms. Nobody in here is in any hurry.",
        ["campus_room_anomaly"] = "Darkroom",
        ["campus_room_composure"] = "The Studio",
        ["campus_room_echo"] = "Music Room",
        ["campus_room_instant_recall"] = "Lecture Hall",
        ["campus_room_misdirection"] = "The Parlour",
        // ---- shell Deck V THE RAKE (2026-08-23)
        ["rake_back_to_campus"] = "Back to campus",
        ["rake_class_dismissed"] = "Class dismissed",
        ["rake_drop_gold_seal"] = "Gold Seal",
        ["rake_drop_gold_star"] = "Gold Star",
        ["rake_drop_hall_pass"] = "Hall Pass",
        ["rake_drop_line_gold_seal"] = "Pressed while the wax was still soft. It kept the shape.",
        ["rake_drop_line_gold_star"] = "Pinned to the board where everyone can see it.",
        ["rake_drop_line_hall_pass"] = "Signed and dated. Good for exactly one wandering.",
        ["rake_drop_line_merit_mark"] = "Someone wrote your name in the good column tonight.",
        ["rake_drop_merit_mark"] = "Merit Mark",
        ["rake_promo_progress"] = "{have} of {need} to {tier}",
        ["rake_retake_chip"] = "Free replay. It pays nothing, and today keeps your first grade.",
        ["rake_streak_cold"] = "Attendance x{n} goes cold if today ends here.",
        ["rake_streak_credited"] = "Attendance x{n} is banked for today already.",
        ["rake_top_of_class"] = "Top of the class",
        // ---- Daily Trigger class-rules sheet (2026-08-23)
        ["dt_howto_go"] = "Start homeroom",
        ["dt_howto_marks"] = "Star: right letter, right place. Half: right letter, elsewhere. Cross: not in it.",
        ["dt_howto_rows"] = "Six rows is the whole budget. Every wrong row turns the room up one notch.",
        ["dt_howto_title"] = "Class rules",
        ["dt_howto_type"] = "Type a word into the row, then Enter. One answer a day, the same for everyone.",
        // ---- Deja Vu class-rules sheet (2026-08-23)
        ["dv_howto_flip"] = "Turn two slides. A matching pair stays lit. Anything else turns back over.",
        ["dv_howto_go"] = "Deal the board",
        ["dv_howto_redeal"] = "Sometimes the whole board re-deals. Same pairs - only the seats change.",
        ["dv_howto_swap"] = "The board only moves while nothing is face up, and it always shudders first.",
        ["dv_howto_title"] = "Class rules",
        // ---- The Deep End class-rules sheet + perf ladder (2026-08-23)
        ["de_howto_ceiling"] = "The ladder ends at the eleventh depth. Reach it and the class holds you there.",
        ["de_howto_go"] = "Into the water",
        ["de_howto_merge"] = "Two matching tiles meet and sink one depth. The room gets heavier as you go.",
        ["de_howto_resurface"] = "A locked board is not a loss. Your depth is banked and the water turns fresh.",
        ["de_howto_swipe"] = "Swipe, or use the arrows. Every tile on the board slides that way at once.",
        ["de_howto_title"] = "Class rules",
        ["de_perf"] = "Performance",
        ["de_perf_auto"] = "Auto",
        ["de_perf_full"] = "Full",
        ["de_perf_hint"] = "Auto watches the frame rate and drops to Lite. Lite: fewer live loops, calmer water.",
        ["de_perf_lite"] = "Lite",
        // ---- MISDIRECTION (md_) - games/misdirection/lex.js MD_LEX
        ["md_almost"] = "ONE OFF",
        ["md_almost_line"] = "One off. She was next door the whole time.",
        ["md_auto_bank_line"] = "Banked for you.",
        ["md_auto_ride_line"] = "Riding for you.",
        ["md_bank"] = "Bank",
        ["md_banked_line"] = "Banked. Nothing takes that back.",
        ["md_bell_line"] = "The bell. Hands off the table.",
        ["md_bell_warn"] = "Twenty seconds.",
        ["md_blind_line"] = "The hand comes over the table.",
        ["md_brief"] = "Watch the shell. Keep watching it. Then point at it.",
        ["md_bust_line"] = "The pot goes back to the house. Your bank is untouched.",
        ["md_chip_clock"] = "Time left",
        ["md_chip_pot"] = "Pot",
        ["md_chip_round"] = "Round",
        ["md_chip_streak"] = "Streak",
        ["md_end_banked"] = "Banked",
        ["md_end_blind"] = "Called through a blackout",
        ["md_end_clean"] = "You banked a round before your first miss.",
        ["md_end_deepest"] = "Deepest ride banked",
        ["md_end_latency"] = "Average pick",
        ["md_end_no"] = "No",
        ["md_end_picks"] = "Picks",
        ["md_end_rounds"] = "Rounds",
        ["md_end_streak"] = "Best streak",
        ["md_end_title"] = "Table report",
        ["md_end_yes"] = "Yes",
        ["md_hit_line"] = "Right where you said she was.",
        ["md_howto_go"] = "Open the table",
        ["md_howto_keys"] = "Keys {keys} pick a shell.",
        ["md_howto_pick"] = "Point at the shell you followed. Four seconds, every round.",
        ["md_howto_shuffle"] = "They slide and trade places. The room will do its best to blind you.",
        ["md_howto_stake"] = "Right? Bank the pot, or ride it double into a dirtier shuffle.",
        ["md_howto_title"] = "Class rules",
        ["md_howto_watch"] = "One shell lifts. What is under it is the only thing you are tracking.",
        ["md_jackpot"] = "JACKPOT",
        ["md_key_pick1"] = "Pick the first shell",
        ["md_key_pick2"] = "Pick the second shell",
        ["md_key_pick3"] = "Pick the third shell",
        ["md_key_pick4"] = "Pick the fourth shell",
        ["md_key_pick5"] = "Pick the fifth shell",
        ["md_miss_line"] = "Empty. The true lid comes up.",
        ["md_near_miss"] = "SO CLOSE",
        ["md_pick_line"] = "Where is she?",
        ["md_remedial_line"] = "Slow round. Clean shuffle, full pot.",
        ["md_retake"] = "Retake",
        ["md_reveal_line"] = "There she is.",
        ["md_ride"] = "Ride",
        ["md_ride_cap_line"] = "Five deep. The house pays out and the table resets.",
        ["md_ride_line"] = "Riding. The table gets dirtier.",
        ["md_royal"] = "ROYAL",
        ["md_scholarship"] = "SCHOLARSHIP ROUND",
        ["md_shell_aria"] = "Shell {n}",
        ["md_shell_noun"] = "Shell",
        ["md_shell_skin"] = "Shell skin",
        ["md_shell_skin_contrast"] = "High contrast",
        ["md_shell_skin_hint"] = "Themed shells, plain shapes, or high-contrast rims that stay readable.",
        ["md_shell_skin_minimal"] = "Minimal",
        ["md_shell_skin_themed"] = "Themed",
        ["md_shuffle_line"] = "Eyes on her.",
        ["md_stake_line"] = "Bank it, or ride it double or nothing?",
        ["md_stake_mode"] = "Stake prompt",
        ["md_stake_mode_ask"] = "Ask",
        ["md_stake_mode_bank"] = "Always bank",
        ["md_stake_mode_hint"] = "Ask after every win, or always bank / always ride without the prompt.",
        ["md_stake_mode_ride"] = "Always ride",
        ["md_stamp_bank"] = "BANKED",
        ["md_stamp_bell"] = "BELL",
        ["md_stamp_blind"] = "EYES OPEN",
        ["md_timeout_line"] = "Too slow. The lid comes up anyway.",
        ["md_trick_feint"] = "Nothing moved that time",
        ["md_trick_hint"] = "This one. Surely.",
        ["md_trick_melt"] = "The lids run like wax",
        ["md_trick_seen"] = "Did you see that?",
        ["md_voided_line"] = "That round is off the books. Your bank is safe.",
        // ---- ECHO (ec_) - games/echo/lex.js EC_LEX
        ["ec_brief"] = "Sit down. Listen first.",
        ["ec_chip_best"] = "Longest echo",
        ["ec_chip_clock"] = "Time left",
        ["ec_chip_len"] = "Sequence length",
        ["ec_chip_streak"] = "Streak",
        ["ec_decoy_tell"] = "Not this one",
        ["ec_end_accuracy"] = "Accuracy",
        ["ec_end_best"] = "Longest echo",
        ["ec_end_decoys"] = "Decoys resisted",
        ["ec_end_encore"] = "Encore used",
        ["ec_end_line"] = "The room keeps the length. You keep the tune.",
        ["ec_end_no"] = "No",
        ["ec_end_record"] = "A new personal best",
        ["ec_end_sequences"] = "Sequences held",
        ["ec_end_streak"] = "Best run of pads",
        ["ec_end_tempo"] = "Tempo held",
        ["ec_end_title"] = "Class dismissed",
        ["ec_end_yes"] = "Yes",
        ["ec_howto_decoy"] = "A pad may light out of turn. Leave that one alone.",
        ["ec_howto_go"] = "Begin",
        ["ec_howto_repeat"] = "Then repeat it back, in order, by tap or by key.",
        ["ec_howto_title"] = "Class rules",
        ["ec_howto_watch"] = "The pads play a sequence. Watch it. Listen to it.",
        ["ec_jackpot"] = "Perfect pitch",
        ["ec_key_pad1"] = "Pad 1",
        ["ec_key_pad2"] = "Pad 2",
        ["ec_key_pad3"] = "Pad 3",
        ["ec_key_pad4"] = "Pad 4",
        ["ec_key_pad5"] = "Pad 5",
        ["ec_key_pad6"] = "Pad 6",
        ["ec_msg_bell_warn"] = "Last of the class.",
        ["ec_msg_clear"] = "Clean. One longer now.",
        ["ec_msg_decoy_warn"] = "One of them lies tonight. Do not echo it.",
        ["ec_msg_encore"] = "Again. Slower this time.",
        ["ec_msg_encore_clear"] = "Held. Keep going.",
        ["ec_msg_encore_fail"] = "Gone. A new one, then.",
        ["ec_msg_fail"] = "Broken. Listen again.",
        ["ec_msg_input"] = "Your turn.",
        ["ec_msg_near"] = "One short of it.",
        ["ec_msg_new"] = "A new one, then.",
        ["ec_msg_resisted"] = "You let it pass. Good.",
        ["ec_msg_silent"] = "No sound tonight. Watch the light.",
        ["ec_msg_timeout"] = "Too slow. Listen again.",
        ["ec_msg_watch"] = "Watch.",
        ["ec_near_miss"] = "So nearly",
        ["ec_pad_aria"] = "Pad {n}",
        ["ec_pad_words"] = "Pad faces",
        ["ec_pad_words_hint"] = "Pads wear a word from your pool, or a plain glyph only.",
        ["ec_retake"] = "Retake",
        ["ec_ring_aria"] = "The pads",
        ["ec_royal"] = "The whole melody",
        ["ec_taunt_ghost"] = "This one. Surely this one.",
        ["ec_taunt_label"] = "Read it again. Or do not.",
        ["ec_taunt_slow"] = "Slower than you were.",
        ["ec_taunt_stall"] = "Still there?",
        // ---- INSTANT RECALL (ir_) - games/instant-recall/lex.js IR_LEX
        ["ir_almost"] = "ALMOST",
        ["ir_answer_hint"] = "Tap an answer, or press 1-4.",
        ["ir_answer_hint3"] = "Tap an answer, or press 1-3.",
        ["ir_bell_warn"] = "Last stretch.",
        ["ir_brief"] = "Watch. It stops without warning and asks what you just saw.",
        ["ir_brief_bell"] = "Watch. A bell warns you before every stop.",
        ["ir_chip_clock"] = "Time left",
        ["ir_chip_density"] = "Density",
        ["ir_chip_stops"] = "Stops",
        ["ir_correct"] = "VERIFIED",
        ["ir_corrected"] = "Corrected memory.",
        ["ir_density"] = "Montage density",
        ["ir_density_hint"] = "How thick the stream gets between stops. Calm eases the ceiling, dense rides it.",
        ["ir_end_accuracy"] = "Accuracy",
        ["ir_end_latency"] = "Average answer",
        ["ir_end_line"] = "The stream never stopped. Only you did.",
        ["ir_end_none"] = "None",
        ["ir_end_plants"] = "Baits dodged",
        ["ir_end_stops"] = "Stops answered",
        ["ir_end_streak"] = "Best run",
        ["ir_end_timeouts"] = "Blanked",
        ["ir_end_title"] = "Vigil over",
        ["ir_fx_ambient_field"] = "Grain",
        ["ir_fx_bubble_field"] = "Bubbles",
        ["ir_fx_crt"] = "Scanlines",
        ["ir_fx_flash_burst"] = "Flash",
        ["ir_fx_gif_burst"] = "Burst",
        ["ir_fx_gif_rain"] = "Rain",
        ["ir_fx_glitch_swap"] = "Glitch",
        ["ir_fx_row_drift"] = "Drift",
        ["ir_fx_wash"] = "Wash",
        ["ir_gotcha"] = "That one flashed while the screen was FROZEN.",
        ["ir_hear"] = "Hear it",
        ["ir_howto_1"] = "A montage plays. Triggers fire over it.",
        ["ir_howto_2"] = "Without warning, everything freezes.",
        ["ir_howto_3"] = "Answer what just happened.",
        ["ir_howto_bell"] = "A bell warns you first. For now.",
        ["ir_howto_go"] = "GO",
        ["ir_howto_nobell"] = "No bell. It just stops.",
        ["ir_howto_title"] = "The vigil",
        ["ir_jackpot"] = "Photographic Memory",
        ["ir_layout_mosaic"] = "Mosaic",
        ["ir_layout_rows"] = "Rows",
        ["ir_layout_swirl"] = "Swirl",
        ["ir_near"] = "So close. That one flashed, but earlier.",
        ["ir_nobell_debrief"] = "That one had no bell. From Year 3, none of them do.",
        ["ir_q_last_effect"] = "What was the last effect?",
        ["ir_q_last_sting"] = "Which sting just played?",
        ["ir_q_last_two"] = "The last two words, in order?",
        ["ir_q_last_word"] = "What was the last word to flash?",
        ["ir_q_mode"] = "Which layout were you watching?",
        ["ir_resisted"] = "RESISTED",
        ["ir_resume"] = "Resume. Denser now.",
        ["ir_royal"] = "ROYAL",
        ["ir_sting_blip"] = "Tick",
        ["ir_sting_bump"] = "Thud",
        ["ir_sting_glitch"] = "Static",
        ["ir_sting_pop"] = "Pop",
        ["ir_sting_sting"] = "Chime",
        ["ir_stop_incoming"] = "Stop incoming.",
        ["ir_stop_now"] = "FREEZE.",
        ["ir_timeout"] = "BLANKED",
        ["ir_truth"] = "It really did.",
        ["ir_vigil_hint"] = "Eyes up. Nothing to click until it freezes.",
        ["ir_voided"] = "Stop voided. The vigil goes on.",
        ["ir_wrong"] = "MISSED",
        // ---- ANOMALY (an_) - games/anomaly/lex.js AN_LEX
        ["an_almost"] = "ALMOST",
        ["an_bell"] = "Time.",
        ["an_breather"] = "Breathe. This one is easy.",
        ["an_brief"] = "One tile is not like the others. Find it before the round runs out.",
        ["an_chip_clock"] = "Time left",
        ["an_chip_round"] = "Round",
        ["an_chip_streak"] = "Streak",
        ["an_end_accuracy"] = "First-tap accuracy",
        ["an_end_found"] = "Found",
        ["an_end_kind"] = "Hardest to see",
        ["an_end_line"] = "Global changes are noise. Only local difference is true.",
        ["an_end_median"] = "Median find",
        ["an_end_none"] = "None",
        ["an_end_rounds"] = "Rounds offered",
        ["an_end_streak"] = "Longest streak",
        ["an_end_title"] = "Eyes up",
        ["an_end_tracked"] = "Tracked after a shift",
        ["an_fast"] = "FAST",
        ["an_found"] = "Found.",
        ["an_found_fast"] = "Fast.",
        ["an_howto_find"] = "One is not. Tap it. The first tap is the one that counts.",
        ["an_howto_go"] = "Open your eyes",
        ["an_howto_lie"] = "The room tints, drifts and glitches every tile at once. That is noise.",
        ["an_howto_same"] = "Every tile is the same loop, playing in step.",
        ["an_howto_title"] = "Class rules",
        ["an_jackpot"] = "Sharp eyes.",
        ["an_kind_blur"] = "focus",
        ["an_kind_bright"] = "light",
        ["an_kind_frame"] = "timing",
        ["an_kind_hue"] = "colour",
        ["an_kind_mirror"] = "mirrored",
        ["an_kind_rotate"] = "tilt",
        ["an_kind_scale"] = "size",
        ["an_kind_speed"] = "speed",
        ["an_kinds"] = "Difference kinds",
        ["an_kinds_hint"] = "Gentle keeps colour, mirror and size only. Mirror is always in the pool.",
        ["an_moved"] = "It moved.",
        ["an_play_hint"] = "Tap the odd tile.",
        ["an_refund"] = "+1s",
        ["an_reveal"] = "It was here.",
        ["an_royal"] = "ROYAL",
        ["an_stamp_bell"] = "Bell",
        ["an_stamp_found"] = "Found",
        ["an_streak_lit"] = "Five straight. The frame is lit.",
        ["an_timeout"] = "Gone. Next grid.",
        ["an_trick_melt"] = "The frame runs like wax",
        ["an_trick_seen"] = "Did you see that?",
        ["an_wrong"] = "Not that one. That tile is out.",
        // ---- COMPOSURE (cp_) - games/composure/lex.js CP_LEX
        ["cp_backtrack_line"] = "Back where it was. Breathe.",
        ["cp_bell_line"] = "The bell. Hands off the board.",
        ["cp_bell_warn"] = "Twenty seconds.",
        ["cp_brief"] = "One picture, cut apart and still moving. Put it back together.",
        ["cp_brief_zen"] = "No clock tonight. Slide until it is whole again.",
        ["cp_chip_calm"] = "Composure",
        ["cp_chip_clock"] = "Time left",
        ["cp_chip_locked"] = "Pieces home",
        ["cp_chip_moves"] = "Moves",
        ["cp_end_assists"] = "Assists",
        ["cp_end_backtracks"] = "Backtracks",
        ["cp_end_best"] = "Best solve",
        ["cp_end_best_first"] = "Your first finished picture on this board.",
        ["cp_end_best_line"] = "Your standing mark on this board. Beat it next class.",
        ["cp_end_locked"] = "Pieces home",
        ["cp_end_moves"] = "Moves",
        ["cp_end_no"] = "No",
        ["cp_end_par"] = "Baseline",
        ["cp_end_solved"] = "Solved",
        ["cp_end_thrash"] = "Panic moves",
        ["cp_end_time"] = "Time",
        ["cp_end_title"] = "Composure report",
        ["cp_end_title_zen"] = "Zen board",
        ["cp_end_yes"] = "Yes",
        ["cp_finish"] = "Finish",
        ["cp_howto_go"] = "Start the picture",
        ["cp_howto_lock"] = "A piece that reaches its own place locks with a snap. It can still be slid.",
        ["cp_howto_slide"] = "Tap a piece beside the gap and it slides in. Arrows, WASD and swipes do the same.",
        ["cp_howto_title"] = "Class rules",
        ["cp_howto_wash"] = "The room will bury the board. Keep sliding - the picture underneath never moved.",
        ["cp_jackpot"] = "JACKPOT",
        ["cp_lock_line"] = "That one is home.",
        ["cp_mode"] = "Mode",
        ["cp_mode_hint"] = "Timed is one graded class. Zen is untimed, gentle, and always a pass.",
        ["cp_near_miss"] = "SO CLOSE",
        ["cp_peek_ref"] = "The finished picture",
        ["cp_play_hint"] = "Tap a piece beside the gap. Arrows, WASD or swipe.",
        ["cp_rescue_line"] = "Take the lit piece. The grade eases; the class does not end.",
        ["cp_retake"] = "Retake",
        ["cp_solved_line"] = "Whole. Watch it play.",
        ["cp_stamp_assist"] = "ASSIST",
        ["cp_stamp_bell"] = "BELL",
        ["cp_stamp_lock"] = "HOME",
        ["cp_stamp_solved"] = "COMPOSED",
        ["cp_trick_melt"] = "One of them is running.",
        ["cp_trick_preview"] = "Did it move?",
        ["cp_trick_seen"] = "That is not where that piece is.",
        ["cp_wash_line"] = "Keep sliding. The board is still exactly where you left it.",
        ["cp_zen_done"] = "Whole, in your own time.",
        ["cp_zen_grid"] = "Zen board",
        ["cp_zen_grid_hint"] = "Zen only. A timed class plays the board your year has earned.",
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
                Launch(devDoor: _devDoor);
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
