using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
            // Which rooms a full punch card has unlocked (PUNCHCARD §2.3). The page derives the
            // same list off the `meta` projection; this line is the log the owner reads on a dark
            // build, where the campus is the only other place it shows.
            var unlocked = _meta.UnlockedGameKeys();
            if (unlocked.Count > 0)
                App.Logger?.Information("ArcademyHost: punch cards unlock {N} room(s): {Keys}",
                    unlocked.Count, string.Join(", ", unlocked));

            // THE MIRROR (PUNCHCARD §5). Pull the account's cards now, on a background thread:
            // the reply restores a reinstalled or second machine's drawer, and a restored
            // `enrolledAt` suppresses that class's enrollment tutorial for free (the page derives
            // "enrolled" from the card - §2.2 - so there is no flag to restore separately).
            //
            // It is NOT awaited and the launch does not depend on it. In the ordinary case the
            // reply lands before the page has finished booting and simply rides out in `init`;
            // when it is slower, the callback pushes the same whole-blob `meta` snapshot a mint
            // does and the shell repaints. No identity, no network, no server: the Arcademy opens
            // exactly the same, on the cards this machine already holds.
            ArcademySyncService.Attach(_meta, OnMirrorCardsChanged);

            // CAMPUS PRESENCE (PRESENCE.md §3). Two independent halves behind one Attach: the
            // EMITTER announces `campus_enter` (only if the player has opted in - the rung is read
            // inside), and the SNAPSHOT PUSHER starts polling the public feed and handing it to
            // the page. The pusher is deliberately NOT gated on the share setting: watching is not
            // consenting, so a campus is populated for everyone who is online. Nothing here is
            // awaited and no part of the launch depends on it.
            ArcademyPresenceService.Attach(OnPresenceSnapshot);

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
                // THE WHISPER CLIPS. Echo's pads wear the player's own triggers and play that
                // trigger's clip faintly under the pad's note, so the page needs to REACH the
                // audio, not merely be told a phrase. Allow (CORS-clean) for the same reason
                // ccp.assets is: shell/audio.js routes the media element through the WebAudio
                // bus graph, and a tainted stream cannot feed a MediaElementSource - it would
                // fall back to raw element volume and slip the mixer's laws.
                ("ccp.subaudio", Path.Combine(AppContext.BaseDirectory, "Resources", "sub_audio"),
                    CoreWebView2HostResourceAccessKind.Allow),
            };
            try { Directory.CreateDirectory(Chaos.DtrhLoomStore.SpiralsFolder); }
            catch (Exception ex) { App.Logger?.Debug("ArcademyHost: spirals dir create failed: {E}", ex.Message); }
            // Creator mods: only the mod's arcademy subfolder is mapped, keeping the rest of its
            // resources off the page. Launch-time snapshot - switching the active mod needs a
            // relaunch, exactly like DTRH.
            var modRoot = ModArcademyRoot();
            if (modRoot != null)
                mappings.Add(("ccp.mod", modRoot, CoreWebView2HostResourceAccessKind.Allow));
            // The active mod's own whisper clips get a SECOND origin rather than being copied:
            // KeywordTriggerService/SubliminalService let a mod override a phrase's audio, and
            // BuildTriggers resolves against the mod dir first for exactly that reason.
            var modAudio = ModAudioRoot();
            if (modAudio != null)
                mappings.Add(("ccp.modaudio", modAudio, CoreWebView2HostResourceAccessKind.Allow));

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
                // CAMPUS PRESENCE: a door opened. Best-effort and gated on the share rung inside;
                // at `off`, or with no identity, this line does nothing at all.
                try { ArcademyPresenceService.NoteRoomEnter((string?)o["gameKey"]); } catch { }
                break;
            case "class-ended":
                OnClassEnded(o);
                break;
            case "enrollment-done":
                OnEnrollmentDone(o);
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
            case "local-sample-request":
                OnLocalSampleRequest(o);
                break;
            case "probe-sub":
                OnProbeSub(o);
                break;
            case "library-remove":
                OnLibraryRemove(o);
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
        // ONE shuffled draw of the active pool feeds BOTH projections, so `triggers[i]` and
        // `words[i]` are the same phrase. Two independent shuffles would silently desynchronise
        // a page that reads one and indexes the other.
        var phrases = BuildWords();
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
            // THE AUTOPLAY GRANT (AV CLUB). The view is launched with
            // --autoplay-policy=no-user-gesture-required, so shell/audio.js owes nobody a
            // gesture and may build its graph at boot - which is the only way the opening
            // splash gets a sound at all, since it is over before the player has touched
            // anything. It is a statement about THIS host, not a setting: a page served
            // anywhere else never sees the field and waits for a click, exactly as it does now.
            autoplayOk = true,
            // ...and WHICH recorded cues are actually on disk. A media element answers "no such
            // file" asynchronously, long after the beat it was meant to land on, so a page left
            // to probe would either drop the first cue of every sampled name or lie about what
            // it has. The host serves the folder; it can simply say. Bare names, no paths -
            // shell/audio.js owns the SAMPLES map and intersects this list with it.
            sfxSamples = BuildSfxSamples(),
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
            words = phrases,
            // The SAME phrases with each one's whisper clip resolved to a url (or null). Echo
            // binds one per pad and plays it under the pad's note; `words` stays exactly as it
            // was for every other class. Empty audio everywhere when SubAudioAudible is off.
            triggers = BuildTriggers(phrases),
            // UTC date seeds the content so the day's classes are globally identical (#978)...
            utcDateSeed = DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            // ...and the LOCAL date is what rolls the attendance streak.
            localDate = now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            overrideCalendar = LoadOverrideCalendar(),
            meta = _meta?.Snapshot() ?? new JObject(),
            settings = BuildSettingsBag(s),
            keybinds = ParseJsonObject(s?.ArcademyKeybindsJson),
            hideTutorial = s?.ArcademyHideTutorial ?? false,
            // CAMPUS PRESENCE, the consent rung, projected TOP-LEVEL beside the other global-tier
            // scalars so the settings page can draw the row it owns (PRESENCE.md §3). `off` is the
            // default and the fallback for anything unreadable - a consent flag never degrades to
            // the nearest neighbour. It is the rung this account ASKED for: the server clamps it
            // down silently when the account cannot back it (no linked Discord, no display name).
            presenceShare = PresenceShare(s),
            // The app-wide panic key, projected for ONE reason (SYNTHESIS-NOTES #7):
            // shell/keybinds.js refuses to let a game bind over it. The page never
            // handles the panic key itself - that stays app-side.
            panicKeyEnabled = s?.PanicKeyEnabled ?? true,
            panicKey = s?.PanicKey ?? "Escape",
            // The dev switch (`--arcademy`) is projected so the campus can offer Begin on rooms
            // the seed did not deal tonight (shell: devPass). Always false on a player launch.
            devDoor = _devDoor,
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
    /// The RECORDED cues that exist right now: every <c>.mp3</c> in the page's own
    /// <c>assets/sfx</c> folder, projected as bare names (<c>bell</c>, not <c>bell.mp3</c>).
    /// The page holds the name -> path map and ignores anything it does not know, so this list
    /// is an ANSWER, never an instruction - a stray file cannot make the shell fetch it.
    /// Only the ccp.game tree is scanned: the page asks for these by a RELATIVE url, so a copy
    /// living under any other mapped origin would not be reachable and saying it was there
    /// would make the page drop the cue instead of synthesising it. Missing folder, no
    /// permission, no files: an empty list, and every cue falls to its oscillator recipe
    /// exactly as it did before the sample door existed.
    /// Cached for the process - the folder ships with the build and cannot change under a
    /// running app, and BuildInit is re-projected on every relaunch of the window.
    /// </summary>
    private static string[]? _sfxSamples;   // null = not scanned yet, never "none found"

    private static string[] BuildSfxSamples()
    {
        if (_sfxSamples != null) return _sfxSamples;
        try
        {
            var dir = Path.Combine(AppContext.BaseDirectory, "Resources", "web", "arcademy", "assets", "sfx");
            if (!Directory.Exists(dir)) return _sfxSamples = Array.Empty<string>();
            _sfxSamples = Directory.EnumerateFiles(dir, "*.mp3", SearchOption.TopDirectoryOnly)
                .Select(f => Path.GetFileNameWithoutExtension(f) ?? string.Empty)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToArray();
            return _sfxSamples;
        }
        catch (Exception ex)
        {
            App.Logger?.Debug("ArcademyHost.BuildSfxSamples: {E}", ex.Message);
            return _sfxSamples = Array.Empty<string>();
        }
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

    /// <summary>The active mod's <c>resources/sounds/flashes_audio</c> folder, or null. Same
    /// precedence <c>KeywordTriggerService.FindLinkedAudio</c> applies: a mod's clip for a phrase
    /// beats the bundled one.</summary>
    private static string? ModAudioRoot()
    {
        try
        {
            var installed = App.Mods?.ActiveMod?.InstalledPath;
            if (string.IsNullOrEmpty(installed)) return null;
            var root = Path.Combine(installed, "resources", "sounds", "flashes_audio");
            return Directory.Exists(root) ? root : null;
        }
        catch { return null; }
    }

    private static readonly string[] SubAudioExts = { ".mp3", ".wav", ".ogg" };

    /// <summary>
    /// The day's phrases WITH their whisper clips: <c>[{text, audio}]</c>, <c>audio</c> a
    /// <c>ccp.modaudio</c> / <c>ccp.subaudio</c> url or null. Echo's pads are the only consumer
    /// today (each pad wears one trigger and plays its clip under the pad's note).
    /// <para>
    /// GATED ON <see cref="Models.AppSettings.SubAudioAudible"/>, the app-wide whisper mute, which
    /// is the same flag <c>init.audioAudible</c> projects and <c>SubliminalService</c> gates every
    /// whisper on: with it off every row is text-only and the page has nothing it could play.
    /// The page gates again on <c>ctx.audioAudible</c> - neither side alone opens the tap.
    /// </para>
    /// </summary>
    private static object[] BuildTriggers(string[] phrases)
    {
        if (phrases == null || phrases.Length == 0) return Array.Empty<object>();
        var audible = App.Settings?.Current?.SubAudioAudible == true;
        var modDir = audible ? ModAudioRoot() : null;
        var defaultDir = audible
            ? Path.Combine(AppContext.BaseDirectory, "Resources", "sub_audio")
            : null;
        var rows = new List<object>(phrases.Length);
        foreach (var text in phrases)
        {
            string? url = null;
            if (audible)
            {
                try { url = ResolveTriggerAudioUrl(text, modDir, defaultDir); }
                catch (Exception ex)
                {
                    // A phrase whose clip cannot be resolved is a TEXT row, never a missing row.
                    App.Logger?.Debug("ArcademyHost.BuildTriggers({Text}): {E}", text, ex.Message);
                    url = null;
                }
            }
            rows.Add(url != null ? (object)new { text, audio = url } : new { text, audio = (string?)null });
        }
        return rows.ToArray();
    }

    /// <summary>
    /// Resolve one phrase's whisper clip to a virtual-host url, mirroring
    /// <c>SubliminalService.FindLinkedAudio</c> / <c>KeywordTriggerService.FindLinkedAudio</c>:
    /// exact filename match against the case/apostrophe variants first, then a case-insensitive
    /// directory scan, with the active mod's folder winning over the bundled one.
    /// </summary>
    private static string? ResolveTriggerAudioUrl(string text, string? modDir, string? defaultDir)
    {
        var clean = (text ?? string.Empty).Trim();
        if (clean.Length == 0) return null;
        var variants = new[]
        {
            clean,
            clean.ToUpperInvariant(),
            clean.ToLowerInvariant(),
            clean.Replace('\u2019', '\''),
            clean.Replace('\'', '\u2019'),
            clean.ToUpperInvariant().Replace('\u2019', '\''),
        };
        var exts = new[] { ".mp3", ".wav", ".ogg", ".MP3", ".WAV", ".OGG" };

        foreach (var (dir, host) in new[] { (modDir, "ccp.modaudio"), (defaultDir, "ccp.subaudio") })
        {
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) continue;

            foreach (var v in variants)
                foreach (var ext in exts)
                {
                    var p = Path.Combine(dir, v + ext);
                    if (File.Exists(p)) return ToAudioUrl(host, Path.GetFileName(p));
                }

            try
            {
                var norm = clean.ToUpperInvariant().Replace('\u2019', '\'');
                foreach (var f in Directory.GetFiles(dir))
                {
                    if (Array.IndexOf(SubAudioExts, Path.GetExtension(f).ToLowerInvariant()) < 0) continue;
                    var name = Path.GetFileNameWithoutExtension(f).ToUpperInvariant().Replace('\u2019', '\'');
                    if (name == norm) return ToAudioUrl(host, Path.GetFileName(f));
                }
            }
            catch { /* the scan is best-effort; the exact-match pass already ran */ }
        }
        return null;
    }

    /// <summary>A flat filename on one of the audio origins, escaped the way
    /// <see cref="ToAssetsUrl"/> escapes an assets path.</summary>
    private static string ToAudioUrl(string host, string fileName)
        => "https://" + host + "/" + Uri.EscapeDataString(fileName);

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

        // ---- what a game needs to let the player CHOOSE its media (SORT's setup door) ----
        // Projected once, already resolved, like everything else here: the page never sees a raw
        // consent flag it could recombine into a gate we did not open. Every one of these is
        // wrapped on its own so a single bad folder cannot cost the page its whole settings bag.
        try
        {
            var catalog = new JArray();
            foreach (var n in FypOnlineCoordinator.Catalog)
                catalog.Add(new JObject
                {
                    ["id"] = n.Id,
                    ["label"] = n.Label,
                    ["subs"] = new JArray(n.Subs ?? Array.Empty<string>()),
                });
            bag["remoteCatalog"] = catalog;
        }
        catch (Exception ex) { App.Logger?.Debug("ArcademyHost.BuildSettingsBag catalog: {E}", ex.Message); }

        try { bag["subLibrary"] = JArray.FromObject(BuildSubLibrary()); }
        catch (Exception ex) { App.Logger?.Debug("ArcademyHost.BuildSettingsBag library: {E}", ex.Message); }

        try { bag["localFolders"] = BuildLocalFolders(); }
        catch (Exception ex) { App.Logger?.Debug("ArcademyHost.BuildSettingsBag folders: {E}", ex.Message); }

        try
        {
            var presets = new JArray();
            foreach (var p in s?.AssetPresets ?? new List<Models.AssetPreset>())
            {
                if (p == null || string.IsNullOrWhiteSpace(p.Id)) continue;
                presets.Add(new JObject { ["id"] = p.Id, ["name"] = p.Name ?? "" });
            }
            bag["assetPresets"] = presets;
        }
        catch (Exception ex) { App.Logger?.Debug("ArcademyHost.BuildSettingsBag presets: {E}", ex.Message); }

        try
        {
            // remoteMediaEnabled is projected at the top level too (BUILD-CONTRACT §4.1); it rides
            // here as well so a game reading its own media options finds them in one bag.
            bag["remoteMediaEnabled"] = RemoteMediaEnabled();
            bag["remoteConsent"] = s?.HasRemoteMediaConsent ?? false;
            bag["mediaSource"] = s?.MediaSource ?? "local";
        }
        catch (Exception ex) { App.Logger?.Debug("ArcademyHost.BuildSettingsBag source: {E}", ex.Message); }

        return bag;
    }

    /// <summary>Folders under the assets root that actually hold media, with RECURSIVE counts per
    /// kind, so a picker can say "images/bambi - 412 gifs" without a round trip. Paths are relative
    /// to the assets root with forward slashes; the two roots ("images", "videos") are always
    /// listed when they exist. Honours the same deselection blacklist the samples do, so a count
    /// never promises files the sample would refuse to serve.</summary>
    private const int LocalFolderCap = 400;

    private static JArray BuildLocalFolders()
    {
        var arr = new JArray();
        var root = App.EffectiveAssetsPath;
        if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return arr;

        var rootFull = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
        var disabled = Quiz.IntakeHostService.BuildDisabledAssetSet(App.Settings?.Current?.DisabledAssetPaths);
        var counts = new Dictionary<string, int[]>(StringComparer.OrdinalIgnoreCase);   // [gifs, stills, videos]

        int[] Slot(string rel)
        {
            if (!counts.TryGetValue(rel, out var slot)) counts[rel] = slot = new int[3];
            return slot;
        }

        foreach (var top in new[] { "images", "videos" })
        {
            var topAbs = Path.Combine(rootFull, top);
            if (!Directory.Exists(topAbs)) continue;
            Slot(top);   // a root with nothing in it is still a real choice ("all of my images")

            IEnumerable<string> files;
            try { files = Directory.EnumerateFiles(topAbs, "*", SearchOption.AllDirectories); }
            catch (Exception ex) { App.Logger?.Debug("ArcademyHost.BuildLocalFolders {Top}: {E}", top, ex.Message); continue; }

            foreach (var file in files)
            {
                var ext = Path.GetExtension(file).ToLowerInvariant();
                int bucket = ext switch
                {
                    ".gif" => 0,
                    ".png" or ".jpg" or ".jpeg" or ".webp" => 1,
                    ".mp4" or ".webm" => 2,
                    _ => -1,
                };
                if (bucket < 0) continue;
                if (!Quiz.IntakeHostService.IsAssetActive(disabled, rootFull, file)) continue;

                // Credit the file to its own folder AND to every folder above it up to the root:
                // "recursive counts" is what makes picking "images" a legal, honest choice.
                var dir = Path.GetDirectoryName(file);
                while (!string.IsNullOrEmpty(dir))
                {
                    string rel;
                    try { rel = Path.GetRelativePath(rootFull, dir).Replace('\\', '/'); }
                    catch { break; }
                    if (rel.Length == 0 || rel == "." || rel.StartsWith("..", StringComparison.Ordinal)) break;
                    Slot(rel)[bucket]++;
                    if (string.Equals(rel, top, StringComparison.OrdinalIgnoreCase)) break;
                    dir = Path.GetDirectoryName(dir);
                }
            }
        }

        var keys = new List<string>(counts.Keys);
        keys.Sort(StringComparer.OrdinalIgnoreCase);
        foreach (var rel in keys)
        {
            var slot = counts[rel];
            arr.Add(new JObject
            {
                ["path"] = rel,
                ["gifs"] = slot[0],
                ["stills"] = slot[1],
                ["videos"] = slot[2],
            });
            if (arr.Count >= LocalFolderCap) break;
        }
        return arr;
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
        // The scoped (mid-class) settings page: knobs are a startClass snapshot.
        ["applies_next_class"] = "Class option changes take effect next class.",
        ["back"] = "Back",
        ["begin_class"] = "Begin",
        ["leave_class"] = "Leave class",
        // ---- the exits (shell/exits.js: the campus pill + its confirm) ------------------
        // Every value stays well under MergeModTable's 96-char cap so a mod can re-voice
        // all of them (trap 26 - the long ic_* rows are the cautionary tale).
        ["back_to_campus"] = "Back to campus",
        ["leave_confirm_title"] = "Head back to campus?",
        ["leave_confirm_body"] = "This class is not finished. Nothing from it is saved.",
        ["leave_confirm_stay"] = "Stay in class",
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
        ["campus_registrar"] = "Front Office",
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

        // CAMPUS PRESENCE - "The Student Body" (planning/arcademy/PRESENCE.md,
        // shell/ghosts.js). Six rows and not one more: four BLIPS a ghost may
        // say to another ghost, the busyness chip's label, and the layer's own
        // name. The bubbles are 1-4 characters BY LAW - a mod re-voices the
        // greeting without ever being able to put a sentence over a stranger's
        // head, and every one is far under the 96-char MergeModTable cap.
        ["presence_student_body"] = "Student Body",
        ["presence_bubble_hi"] = "hihi",
        ["presence_bubble_dots"] = "...",
        ["presence_bubble_wave_a"] = "o/",
        ["presence_bubble_wave_b"] = "\\o",
        ["presence_here_tonight"] = "here tonight",
        // ...and the CONSENT ROW (P3, the settings page's global tier). Every option says what
        // it shows PUBLICLY, because the player is agreeing to a specific thing and a rung named
        // only "Anonymous" is not one. All well under the 96-char MergeModTable cap (trap 26), so
        // a mod may re-word the consent copy in its own voice - which is why it lives here at all.
        ["presence_share_label"] = "Show yourself on campus",
        ["presence_share_hint"] = "Your last 24 hours replay as a ghost. Room head counts include you at every rung.",
        ["presence_share_off"] = "Off - room head counts only",
        ["presence_share_anon"] = "Anonymous - a ghost with no name or picture",
        ["presence_share_username"] = "Username - your display name over the ghost",
        ["presence_share_discord"] = "Discord - your display name and profile picture",
        ["presence_share_discord_note"] = "Discord needs a linked account. Without one the school shows your name instead.",
        ["campus_east_wing"] = "East Wing",
        ["campus_west_wing"] = "West Wing",
        ["campus_desc_east"] = "You can hear hammering behind the tape.",
        ["campus_desc_west"] = "The boards are older here.",
        ["campus_sealed"] = "Sealed",
        ["campus_opens_semester_2"] = "Opens Semester II",
        ["campus_semester_3"] = "Semester III",
        ["campus_in_session"] = "In Session",
        ["campus_not_tonight"] = "Not tonight",
        ["campus_dev_pass"] = "Dev pass · Begin",
        ["campus_dev_pass_hint"] = "Dev pass: off tonight's board, graded anyway.",
        ["campus_next_bell"] = "Next Bell",
        ["campus_step_inside"] = "Step inside",
        ["campus_xp_first"] = "First pass of the day pays XP.",
        ["campus_xp_retake"] = "Retakes pay no XP - pride only.",
        ["campus_hint"] = "Hover a room - click to step inside.",
        ["campus_hint_touch"] = "Tap a room to step inside.",
        ["campus_night_sessions"] = "Night Sessions",
        ["campus_rm"] = "RM",
        // ---- punch cards: the campus door (PUNCHCARD.md §2.3) --------------------------
        ["campus_unlocked"] = "Unlocked - open every night",
        ["campus_unlocked_sign"] = "Open",
        ["campus_unlocked_hint"] = "Card complete. This room opens every night, board or no board.",
        // ---- punch cards: the card + its ceremony (PUNCHCARD.md §4) ---------------------
        // Every value stays under MergeModTable's 96-char cap so a mod can re-voice the
        // whole mechanic (trap 26). A line that needs more room becomes a second card.
        ["punchcard"] = "Stamp Card",
        ["punchcard_holes"] = "{have} of {need}",
        // The card face's LIVE TEXT ZONE: the tight count, the MASTERED label, and the
        // eight rotating flavour lines (shell/punchcard.js PHRASE_LEX - copy the values,
        // do not re-word them). One row per line so a mod can re-voice them one at a time.
        ["punchcard_count"] = "{have}/{need}",
        ["punchcard_mastered"] = "Mastered",
        ["punchcard_phrase_1"] = "Attendance is a habit. Habits are the only thing this school grades.",
        ["punchcard_phrase_2"] = "The card does not ask how well you did. Only that you came.",
        ["punchcard_phrase_3"] = "Ten stamps and the room is yours. Nine of them are patience.",
        ["punchcard_phrase_4"] = "Nobody has ever filled a card in one night. You will not be the first.",
        ["punchcard_phrase_5"] = "The register is already open. Your name has been on it a while.",
        ["punchcard_phrase_6"] = "One stamp a night. The school is in no hurry whatsoever.",
        ["punchcard_phrase_7"] = "Progress in here is measured in ink, never in effort.",
        ["punchcard_phrase_8"] = "You will stop noticing that you collect these. That is the intention.",
        ["punchcard_stamped"] = "Stamped for today.",
        // THE S DOUBLE (owner ruling 2026-08-23) - the second hole an S day buys says so.
        ["punchcard_stamped_s"] = "Top marks. The card takes a second stamp.",
        ["punchcard_next_hole"] = "Come back tomorrow for the next stamp.",
        ["punchcard_unlocked_chip"] = "Unlocked",
        ["punchcard_unlocked_title"] = "Assignment complete",
        ["punchcard_unlocked_line"] = "This room is now open even when the course is not in session.",
        ["enroll_kicker"] = "Enrollment",
        ["enroll_next"] = "Next",
        ["enroll_begin"] = "Begin class",
        ["enroll_card_line"] = "Every class carries a stamp card. Ten stamps, one a night.",
        ["enroll_tutorial_line"] = "One stamp for finishing your first class.",
        ["enroll_house_line"] = "And one on the house. Welcome to the class.",
        // DAY ONE IS THREE (owner ruling 2026-08-23) - the third hole says why.
        ["enroll_signon_line"] = "And one for signing on. The card starts warm.",
        // ---- punch cards: the Records Office (PUNCHCARD.md §6) --------------------------
        ["records_kicker"] = "Records Office",
        ["records_lede"] = "Ten cards, ten stamps each. The wall keeps them whether you come back or not.",
        ["records_enrolled"] = "Enrolled",
        ["records_enrolled_on"] = "Enrolled",
        ["records_unlocked_on"] = "Unlocked",
        ["records_holes_punched"] = "Stamps earned",
        ["records_holes_left"] = "Stamps left",
        ["records_stamps"] = "Daily stamps",
        ["records_no_stamps"] = "No daily stamps yet.",
        ["records_not_enrolled"] = "Not enrolled - attend the class",
        ["records_enroll_hint"] = "The first graded finish opens the card and earns three stamps.",
        ["records_house_note"] = "Day one is three stamps: finishing, on the house, signing on.",
        ["records_flip_hint"] = "Pick a card to read its stamps.",
        ["records_spot_close"] = "Close",
        ["records_empty_wall"] = "Nothing on the wall yet. Attend a class and the first card gets pinned.",
        // ---- enrollment flavour, per class (PUNCHCARD.md §4) ----------------------------
        // Mirrors shell/enrollment.js's ENROLL_LEX verbatim - copy the values, do not
        // re-word them (the IC_LEX rule). Three cards per class, in the campus voice:
        // what the room is for, what it makes you do, what it is doing to you.
        ["enroll_daily_trigger_1"] = "Homeroom goes first, and the whole lesson is one word long.",
        ["enroll_daily_trigger_2"] = "Everyone in the school sits the same word tonight. Six chances, no help.",
        ["enroll_daily_trigger_3"] = "Say it enough mornings and you stop deciding what it means.",
        ["enroll_lost_and_found_1"] = "Things go missing here constantly. Nobody files a report.",
        ["enroll_lost_and_found_2"] = "A wall of moving pictures, and one of them is yours. Find it first.",
        ["enroll_lost_and_found_3"] = "This trains the part of you that keeps looking after looking stops working.",
        ["enroll_deja_vu_1"] = "The Memory Lab studies what happens to a board you have already learned.",
        ["enroll_deja_vu_2"] = "Match the pairs. The pairs move when you blink. Both of those are the work.",
        ["enroll_deja_vu_3"] = "You will feel certain and be wrong. We would rather you stopped noticing.",
        ["enroll_impulse_control_1"] = "Discipline Hall exists because you reach for things. Every time.",
        ["enroll_impulse_control_2"] = "Hands on the desk. Pop when told, hold when told. The room may lie.",
        ["enroll_impulse_control_3"] = "A held hand is worth more here than a fast one. Learn which order was real.",
        ["enroll_the_deep_end_1"] = "The Pool has a shallow end that nobody uses. This class is not held there.",
        ["enroll_the_deep_end_2"] = "Sink tile into tile. Every merge takes you further from the surface.",
        ["enroll_the_deep_end_3"] = "The deeper the board, the harder it is to read. That is the subject.",
        ["enroll_misdirection_1"] = "The Parlour teaches what a room does to your attention when it wants it.",
        ["enroll_misdirection_2"] = "Keep your eyes on the one that matters. It will not make that easy.",
        ["enroll_misdirection_3"] = "You will be shown the trick and lose anyway. Then shown it again.",
        ["enroll_echo_1"] = "The Music Room does not teach music. It teaches you to hold a line that somebody else set.",
        ["enroll_echo_2"] = "It plays a phrase. You play it back. Then it adds one and asks again.",
        ["enroll_echo_3"] = "Nobody passes by remembering harder. They pass by stopping the arguing.",
        ["enroll_instant_recall_1"] = "The Lecture Hall never announces the test. That is the design of the room.",
        ["enroll_instant_recall_2"] = "Watch the hour, answer for it after. You will not hear the question coming.",
        ["enroll_instant_recall_3"] = "Attention that only arrives when asked is not attention. This corrects that.",
        ["enroll_anomaly_1"] = "The Darkroom is where the school checks that you still notice a difference.",
        ["enroll_anomaly_2"] = "Everything on the grid matches. One thing does not. Find it before it moves.",
        ["enroll_anomaly_3"] = "The differences get smaller every year. You are expected to keep up.",
        ["enroll_composure_1"] = "The Studio grades one thing: can you finish a picture while interfered with.",
        ["enroll_composure_2"] = "Slide the tiles back into order while the room blurs what order was.",
        ["enroll_composure_3"] = "Nothing in here is fast. Composure is the subject and it cannot be rushed.",
        // ---- FIRST BELL, the once-ever opening (Resources/web/arcademy/vn/lex.js) -------
        // Mirrors VN_LEX row for row. The two PAPERS are stored as CLAUSE rows and joined
        // with a single space by vn/index.js paragraph(): every row therefore stays under
        // the 96-character mod-skin cap, which a whole paragraph could never do (a value
        // over 96 is dropped by MergeModTable and can never be re-voiced - see trap 26).
        // Splitting is a storage decision; the joined text is the owner-vetted paragraph
        // byte for byte and no word of it may be edited here.
        ["vn_skip"] = "Hold to skip",
        ["vn_tap"] = "Tap to continue",
        ["vn_s01_cap1"] = "The gates open at dusk and classes run every night, holidays included.",
        ["vn_s01_cap2"] = "Your enrollment went through last week. First bell rings in the main hall.",
        ["vn_p1_title"] = "WELCOME TO THE ARCADEMY",
        ["vn_p1_a"] = "Hi! You're all set.",
        ["vn_p1_b"] = "Tonight's four classes go up on the big board over this desk at first bell,",
        ["vn_p1_c"] = "homeroom first and then whatever order you feel like.",
        ["vn_p1_d"] = "You don't need to bring anything, every room already has its own machine",
        ["vn_p1_e"] = "and the machine has everything.",
        ["vn_p1_f"] = "Nobody's at the desk after dark, so if a cabinet acts up,",
        ["vn_p1_g"] = "give it one gentle kick and leave us a note in the tray.",
        ["vn_p1_h"] = "Have a great first night!",
        ["vn_s03_cap"] = "Homeroom is room 101, first door on your left, just follow the footprint decals.",
        ["vn_p2_title"] = "NICE ONE!",
        ["vn_p2_a"] = "That's your first stamp of the year.",
        ["vn_p2_b"] = "Three classes are still lit on the board if you're up for another,",
        ["vn_p2_c"] = "and if you're done for tonight that's fine too,",
        ["vn_p2_d"] = "the board deals fresh at dusk either way.",
        ["vn_p2_e"] = "Replay anything as much as you like,",
        ["vn_p2_f"] = "the card just takes one stamp per class a night.",
        ["vn_p2_g"] = "Spare tokens can go in the fountain, it's supposed to be good luck,",
        ["vn_p2_h"] = "or at least that's what everybody writes in the yearbook.",
        ["vn_sign"] = "- the front desk",
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
        ["dv_bell"] = "The bell. Class over.",
        ["dv_boards"] = "boards",
        ["dv_last_call"] = "Last ten seconds.",
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
        ["lf_briefing_n"] = "Memorize her, then find her {n} times.",
        ["lf_clutch"] = "The board relents",
        ["lf_final_bell"] = "Final bell",
        ["lf_find_prompt"] = "Find her",
        ["lf_density"] = "Board density",
        ["lf_density_hint"] = "How crowded the wall deals: easy, medium, or hard (near-impossible).",
        ["lf_found"] = "Found her",
        ["lf_howto_title"] = "Class rules",
        ["lf_howto_find"] = "She hides on a wall that never sits still. Spot the tile that matches her picture.",
        ["lf_howto_finds_n"] = "Every find, she relocates. Catch her {n} times.",
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
        ["campus_desc_east_open"] = "The front office. Two counters, one bell, and a queue that is always you.",
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
        ["dv_howto_boards"] = "Clear the board and a fresh one deals. The bell ends class.",
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
        ["ec_clear"] = "Echo held: {n}",
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
        ["ec_late"] = "Too late",
        ["ec_miss"] = "Miss - again",
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
        ["ec_pad_words_hint"] = "Pads wear one of your triggers, a plain glyph, or your own media.",
        ["ec_phase_aria"] = "Whose turn it is",
        ["ec_phase_listen"] = "Listen...",
        ["ec_phase_over"] = "Class over",
        ["ec_phase_ready"] = "Sit down",
        ["ec_phase_yours"] = "Your turn",
        ["ec_retake"] = "Retake",
        ["ec_ring_aria"] = "The pads",
        ["ec_royal"] = "The whole melody",
        ["ec_stamp_clear"] = "HELD",
        ["ec_stamp_late"] = "LATE",
        ["ec_stamp_miss"] = "MISS",
        ["ec_step_aria"] = "Step {n}",
        ["ec_steps_aria"] = "The sequence, step by step",
        ["ec_taunt_ghost"] = "This one. Surely this one.",
        ["ec_taunt_label"] = "Read it again. Or do not.",
        ["ec_taunt_slow"] = "Slower than you were.",
        ["ec_taunt_stall"] = "Still there?",
        ["ec_this_one"] = "This one",
        // ---- INSTANT RECALL (ir_) - games/instant-recall/lex.js IR_LEX
        ["ir_almost"] = "ALMOST",
        ["ir_answer_hint"] = "Tap an answer, or press 1-4.",
        ["ir_answer_hint2"] = "Tap an answer, or press 1-2.",
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
        ["ir_end_kinds"] = "Question kinds",
        ["ir_end_latency"] = "Average answer",
        ["ir_end_line"] = "The stream never stopped. Only you did.",
        ["ir_end_none"] = "None",
        ["ir_end_plants"] = "Baits dodged",
        ["ir_end_stops"] = "Stops answered",
        ["ir_end_streak"] = "Best run",
        ["ir_end_timeouts"] = "Blanked",
        ["ir_end_title"] = "Vigil over",
        // THE EFFECT POOL (mosaic rework 2026-08-23): CCP's own names for CCP's
        // own effects. The nine rows above these (ir_fx_ambient_field ... _wash)
        // are the retired engine-kind names - the page no longer renders them,
        // but this table is append-only so they stay.
        ["ir_fx_ambient_field"] = "Grain",
        ["ir_fx_brain_drain"] = "Brain Drain",
        ["ir_fx_bubble_field"] = "Bubbles",
        ["ir_fx_bubbles"] = "Bubbles",
        ["ir_fx_cascade"] = "Cascade",
        ["ir_fx_corner_gif"] = "Corner GIF",
        ["ir_fx_crt"] = "Scanlines",
        ["ir_fx_flash"] = "Flash image",
        ["ir_fx_flash_burst"] = "Flash",
        ["ir_fx_fullscreen_gif"] = "Fullscreen GIF",
        ["ir_fx_gif_burst"] = "Burst",
        ["ir_fx_gif_rain"] = "Rain",
        ["ir_fx_glitch_swap"] = "Glitch",
        ["ir_fx_pink"] = "Pink Filter",
        ["ir_fx_row_drift"] = "Drift",
        ["ir_fx_spiral"] = "Spiral",
        ["ir_fx_subliminal"] = "Subliminal",
        ["ir_fx_wash"] = "Wash",
        ["ir_fx_whisper"] = "Whisper",
        ["ir_gotcha"] = "That one flashed while the screen was FROZEN.",
        ["ir_gotcha_heard"] = "That one was whispered while the screen was FROZEN.",
        ["ir_hear"] = "Hear it",
        ["ir_howto_1"] = "A wall of your media keeps changing. Effects fire over it.",
        ["ir_howto_2"] = "Without warning, everything freezes.",
        ["ir_howto_3"] = "Answer what just happened - a word, an effect, a spiral, a face from the wall.",
        ["ir_howto_bell"] = "A bell warns you first. For now.",
        ["ir_howto_go"] = "GO",
        ["ir_howto_nobell"] = "No bell. It just stops.",
        ["ir_howto_title"] = "The vigil",
        ["ir_jackpot"] = "Photographic Memory",
        ["ir_layout_mosaic"] = "Mosaic",
        ["ir_layout_rows"] = "Rows",
        ["ir_layout_swirl"] = "Swirl",
        ["ir_near"] = "So close. That one flashed, but earlier.",
        ["ir_near_heard"] = "So close. That one was whispered, but earlier.",
        ["ir_near_spiral"] = "So close. That spiral played, but earlier.",
        ["ir_no"] = "No",
        ["ir_nobell_debrief"] = "That one had no bell. From Year 3, none of them do.",
        ["ir_opt"] = "Option",
        ["ir_q_heard"] = "What did you just hear?",
        ["ir_q_last_effect"] = "What was the last effect?",
        ["ir_q_last_sting"] = "Which sting just played?",
        ["ir_q_last_two"] = "The last two words, in order?",
        ["ir_q_last_word"] = "What was the last word to flash?",
        ["ir_q_mode"] = "Which layout were you watching?",
        ["ir_q_spiral"] = "Which spiral did you see last?",
        ["ir_q_wall_gone"] = "Which of these was NOT on the wall?",
        ["ir_q_wall_pick"] = "Which of these was on the wall?",
        ["ir_q_wall_seen"] = "Was this on the wall?",
        ["ir_q_wall_twice"] = "Which one was on the wall twice?",
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
        ["ir_truth_heard"] = "That is what it said.",
        ["ir_truth_spiral"] = "That spiral. Exactly that one.",
        ["ir_truth_wall"] = "It was there. Look again.",
        ["ir_truth_wall_gone"] = "That one never showed.",
        ["ir_vigil_hint"] = "Eyes up. Nothing to click until it freezes.",
        ["ir_voided"] = "Stop voided. The vigil goes on.",
        ["ir_wrong"] = "MISSED",
        ["ir_yes"] = "Yes",
        // ---- SORT (sort_) - games/sort/lex.js SORT_LEX + setup-lex.js SETUP_LEX (room 201)
        ["sort_almost"] = "ALMOST",
        ["sort_back"] = "Back",
        ["sort_bg_fade"] = "Background fade",
        ["sort_bg_fade_hint"] = "How brightly the sorted wall burns behind the stack.",
        ["sort_catalog_head"] = "Niches",
        ["sort_change"] = "Change my sort",
        ["sort_chip_chain"] = "Chain",
        ["sort_chip_clock"] = "Time left",
        ["sort_chip_rung"] = "Rung",
        ["sort_chip_sorted"] = "Sorted",
        ["sort_clips"] = "clips",
        ["sort_counts"] = "items",
        ["sort_dealing"] = "Dealing your deck",
        ["sort_door_step"] = "Step",
        ["sort_door_sub"] = "Right is yours. Left is the rest.",
        ["sort_door_title"] = "Set your sort",
        ["sort_folder_taken"] = "on the other pile",
        ["sort_folders_head"] = "Folders",
        ["sort_gate_hint"] = "An S wants near-perfect calls AND a chain that reached the top.",
        ["sort_ghost_head"] = "Like this",
        ["sort_ghost_noise"] = "the rest",
        ["sort_ghost_target"] = "yours",
        ["sort_just"] = "JUST",
        ["sort_leave"] = "Leave",
        ["sort_left_key"] = "Swipe left (not yours)",
        ["sort_lib_empty"] = "Nothing here yet. Search for a sub below.",
        ["sort_lib_head"] = "My library",
        ["sort_missing"] = "gone from the feed",
        ["sort_need_pick"] = "Pick at least one first",
        ["sort_need_split"] = "The two piles cannot be the same",
        ["sort_next"] = "Next",
        ["sort_no_deck"] = "No cards to sort. Your attendance is safe.",
        ["sort_noise_head"] = "What is the rest?",
        ["sort_noise_hint"] = "These go LEFT. Pick one or more.",
        ["sort_overlap_note"] = "Shared subs were dropped from the rest",
        ["sort_pass"] = "PASSED",
        ["sort_perfect"] = "PERFECT",
        ["sort_perfect_class"] = "Clean sort",
        ["sort_play"] = "Deal me in",
        ["sort_preset_none"] = "No preset",
        ["sort_presets_head"] = "Or a whole preset",
        ["sort_probe_bad"] = "That is not a subreddit name",
        ["sort_probe_dupe"] = "Already on your list",
        ["sort_probe_missing"] = "Not found",
        ["sort_probe_ok"] = "Added to your library",
        ["sort_probe_probing"] = "Checking",
        ["sort_quick"] = "QUICK SORT: moving to the right, still to the left.",
        ["sort_quick_head"] = "Quick sort",
        ["sort_quick_nag"] = "Turn on online media or add a second folder for a real sort.",
        ["sort_quick_rule"] = "Moving goes right. Still goes left.",
        ["sort_record"] = "record",
        ["sort_remove"] = "Remove from my library",
        ["sort_right_key"] = "Swipe right (yours)",
        ["sort_ring_label"] = "Time on this card",
        ["sort_royal"] = "ROYAL",
        ["sort_rules_go"] = "Begin",
        ["sort_rules_keys"] = "Arrow keys work too. A key is a swipe.",
        ["sort_rules_left"] = "Left: everything else.",
        ["sort_rules_pass"] = "Let it close and the card comes back. That is not a mistake.",
        ["sort_rules_right"] = "Right: yours.",
        ["sort_rules_ring"] = "The ring closes. Swipe in the gold and the chain grows.",
        ["sort_rules_title"] = "One rule",
        ["sort_rung_down"] = "Rung down",
        ["sort_rung_up"] = "Rung up",
        ["sort_same"] = "Same sort",
        ["sort_search_btn"] = "Add",
        ["sort_search_head"] = "Add a sub",
        ["sort_search_ph"] = "subreddit name",
        ["sort_source_local"] = "My folders",
        ["sort_source_local_hint"] = "Folders and presets from your own assets",
        ["sort_source_local_off"] = "Not enough folders or presets to make two piles",
        ["sort_source_online"] = "Online",
        ["sort_source_online_hint"] = "Niches and subs from the web feed",
        ["sort_source_online_off"] = "Online media is off in your settings",
        ["sort_spice_hot"] = "Hot. These two niches share ground.",
        ["sort_spice_mid"] = "Warm. Your own picks on both sides.",
        ["sort_spice_mild"] = "Mild. These two are easy to tell apart.",
        ["sort_stale"] = "That sort is gone. Pick again.",
        ["sort_stamp_no"] = "NO",
        ["sort_stamp_yes"] = "YES",
        ["sort_starter_head"] = "Easy noise",
        ["sort_starter_hint"] = "One tap. Checked once, then yours forever.",
        ["sort_step_noise"] = "The rest",
        ["sort_step_source"] = "Where from",
        ["sort_step_target"] = "Your pile",
        ["sort_stills_only"] = "stills only",
        ["sort_submit"] = "Submit report",
        ["sort_subtitle"] = "Yours to the right. Everything else to the left.",
        ["sort_target_head"] = "What do you want?",
        ["sort_target_hint"] = "These go RIGHT. Pick one or more.",
        ["sort_thin"] = "Thin pile: expect repeats.",
        ["sort_thin_add"] = "Add another pick",
        ["sort_ticket_chain"] = "Longest chain",
        ["sort_ticket_passed"] = "Passed",
        ["sort_ticket_perfect"] = "Perfect",
        ["sort_ticket_rung"] = "Top rung",
        ["sort_ticket_sorted"] = "Sorted",
        ["sort_ticket_title"] = "The sort",
        ["sort_ticket_wrong"] = "Wrong",
        ["sort_title"] = "Sort",
        ["sort_tut_ghost"] = "Watch two cards sort themselves, then it is your turn.",
        ["sort_tut_pick"] = "You pick both piles now. They do not change once the bell rings.",
        ["sort_tut_rule"] = "One rule all class: yours goes right, the rest goes left.",
        ["sort_unverified"] = "never checked",
        ["sort_verified"] = "verified",
        ["sort_vs"] = "vs",
        ["sort_wall_label"] = "What you sorted",
        ["sort_wrong"] = "WRONG",
        ["campus_desc_sort"] = "Two piles, and you decide what goes in them. Yours to the right.",
        ["campus_room_sort"] = "The Sorting Room",
        ["enroll_sort_1"] = "The Sorting Room does not tell you what matters. You tell it, at the door.",
        ["enroll_sort_2"] = "Yours goes right. Everything else goes left. The ring closes while you decide.",
        ["enroll_sort_3"] = "Sort your own things quickly enough and you stop asking why they are yours.",
        ["game_sort"] = "Sort",
        ["sort_jackpot"] = "JACKPOT",
        ["sort_record_near"] = "ONE OFF YOUR BEST",
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
        ["cp_bank_line"] = "Banked. Here is a fresh one.",
        ["cp_brief"] = "One picture, cut apart and still moving. Put it back together, then again.",
        ["cp_brief_zen"] = "No clock tonight. Slide until it is whole, then again if you like.",
        ["cp_chip_banked"] = "Pictures done",
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
                ["cp_end_par"] = "Baseline",
        ["cp_end_solved"] = "Pictures finished",
        ["cp_howto_bank"] = "Finish a picture and the next one deals. The bell ends the class, not the solve.",
        ["cp_end_thrash"] = "Panic moves",
        ["cp_end_time"] = "Time",
        ["cp_end_title"] = "Composure report",
        ["cp_end_title_zen"] = "Zen board",
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
        // ---- ORIENTATION DAY (2026-08-24, planning/arcademy/ORIENTATION.md §3/§5)
        // The school's once-ever hello: the walk to the front office, the student ID
        // handover, EMI's three lines. Mirrors core/lexicon.js DEFAULT_LEXICON verbatim
        // - copy the values, do not re-word them (the IC_LEX rule); the page resolves
        // them and hands them to emi/moments.js as payload.line. All well under the
        // 96-char cap (trap 26), so a mod re-voices the whole beat.
        ["orientation_kicker"] = "Orientation Day",
        ["emi_orientation_hi"] = "a new student! i did a little spin. you missed it.",
        ["emi_orientation_card"] = "official! now you have to come back. it's the rules.",
        ["emi_orientation_go"] = "go! your first class doesn't know how lucky it is.",
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

            // CAMPUS PRESENCE, the one CONSENT flag the page may write (PRESENCE.md §3). The
            // clamp is an allowlist, not a tolerance: anything that is not one of the four rungs
            // is stored as `off`, so a malformed frame can only ever REDUCE what the account
            // shows. Echoed post-clamp like every other key - trap 1, only the echo moves it.
            case "presenceShare":
                s.ArcademyPresenceShare = (value?.Type == JTokenType.String ? (string?)value : null) ?? "off";
                return s.ArcademyPresenceShare;

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

            // THE PUNCH CARD RIDES THE ATTENDANCE CREDIT (PUNCHCARD §2.1). Same frame, same local
            // date, same idempotence - a graded finish is exactly the event that stamps, so there
            // is nothing here to keep in step with the rule. It is wrapped on its own because the
            // payout frame below must survive anything the card math could do; the credit above is
            // the thing we must not lose, and this must not become a second way to lose it.
            //
            // Dev-door runs stamp like any other graded class (PUNCHCARD §3): it IS graded play,
            // the owner wants it for testing, and `--arcademy` never reaches a player build.
            // Excluding them later is this one condition - `if (!_devDoor)`.
            ArcademyPunchCards.PunchMint? punch = null;
            try
            {
                // THE S DOUBLE (owner ruling 2026-08-23). The grade is only known HERE, so the
                // host decides it and the page is told on the frame - `minted:2`. `grade` has
                // already been clamped to a table key above (zen and anything unrecognised
                // degrade to "pass"/"C"), so this cannot be talked into an S by a junk field, and
                // the mint's own per-day idempotence means a retake that grades S is still worth
                // nothing (trap 23).
                bool gradedS = string.Equals(grade, "S", StringComparison.OrdinalIgnoreCase);
                punch = _meta?.StampPunchCard(gameKey, localDate, gradedS);
                // A real punch is worth mirroring; a same-day retake is not. Debounced, so the
                // enrollment ceremony's second mint rides the same request (PUNCHCARD §5).
                if (punch is { Minted: true }) ArcademySyncService.NotifyMutation();
            }
            catch (Exception ex) { App.Logger?.Warning("ArcademyHost punch card: {E}", ex.Message); }

            // CAMPUS PRESENCE: the class ended, and the letter that rides the wire is the HOST's
            // clamped `grade` - the same value the XP table and the S double already read - so a
            // junk field from the page cannot mint a grade for a stranger's map. A zen `pass` is
            // not one of S/A/B/C and rides as null, which the renderer draws as a finish with no
            // letter. Own try/catch: nothing about company may cost the payout frame below.
            try { ArcademyPresenceService.NoteClassEnd(gameKey, grade); } catch { }

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
            PostPunchCard(gameKey, "daily", punch);
            App.Logger?.Information(
                "ArcademyHost: class complete ({Game}, tier {Tier}, grade {Grade}) = {Xp:0} XP{Retake}, streak {Streak}, {Today}/4 today",
                gameKey, tier, grade, xp, firstToday ? "" : " (retake - already paid for " + dayUtc + ")",
                streak, classesToday);
        }
        catch (Exception ex) { App.Logger?.Warning("ArcademyHost.OnClassEnded: {E}", ex.Message); }
    }

    // ============================ enrollment-done: the first-run punches ============================

    /// <summary>
    /// <c>enrollment-done {gameKey}</c> -> the three first-run punches (PUNCHCARD §4, owner ruling
    /// 2026-08-23). The shell posts this once, at the end of the enrollment ceremony that follows a
    /// class's FIRST graded finish; everything the mint needs beyond the key is derived host-side,
    /// so the frame carries no numbers to forge and a replayed one is a no-op.
    ///
    /// <para>It arrives AFTER that run's <c>class-ended</c>, whose daily stamp it supersedes - the
    /// three punches replace it rather than adding to it, so day one is exactly three either way
    /// round, even when that first night graded S (<see cref="ArcademyPunchCards.Enroll"/>).</para>
    /// </summary>
    private static void OnEnrollmentDone(JObject o)
    {
        try
        {
            var gameKey = (ReadString(o, "gameKey") ?? "").Trim();
            if (gameKey.Length > 64) gameKey = gameKey[..64];
            if (gameKey.Length == 0)
            {
                App.Logger?.Debug("ArcademyHost: enrollment-done with no game key - ignored");
                return;
            }

            // LOCAL date, like every other daily gate here (#978).
            var localDate = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var mint = _meta?.EnrollPunchCard(gameKey, localDate);
            if (mint is { Minted: true })
            {
                if (_meta != null) _host?.Post(_meta.SnapshotMessage());
                ArcademySyncService.NotifyMutation();
            }
            PostPunchCard(gameKey, "enrollment", mint);
        }
        catch (Exception ex) { App.Logger?.Warning("ArcademyHost.OnEnrollmentDone: {E}", ex.Message); }
    }

    /// <summary>
    /// The mirror changed a card (a launch pull that restored something, or a merged push reply
    /// that carried another machine's day). Repaint the page with the same whole-blob snapshot a
    /// local mint sends - the page has no idea the server exists and does not need one.
    ///
    /// <para>Raised from a background thread, so it hops to the dispatcher: <c>Post</c> reaches
    /// WebView2 and WebView2 is UI-thread-only. Every guard from the async rules applies - a null
    /// or shutting-down dispatcher means there is nothing left to repaint anyway.</para>
    /// </summary>
    private static void OnMirrorCardsChanged()
    {
        try
        {
            var disp = Application.Current?.Dispatcher;
            if (disp == null || disp.HasShutdownStarted) return;
            disp.BeginInvoke(() =>
            {
                try
                {
                    if (_meta == null || _host == null) return;
                    _host.Post(_meta.SnapshotMessage());
                    var unlocked = _meta.UnlockedGameKeys();
                    if (unlocked.Count > 0)
                        App.Logger?.Information("ArcademyHost: after sync, punch cards unlock {N} room(s): {Keys}",
                            unlocked.Count, string.Join(", ", unlocked));
                }
                catch (Exception ex) { App.Logger?.Debug("ArcademyHost.OnMirrorCardsChanged post: {E}", ex.Message); }
            });
        }
        catch (Exception ex) { App.Logger?.Debug("ArcademyHost.OnMirrorCardsChanged: {E}", ex.Message); }
    }

    /// <summary>
    /// CAMPUS PRESENCE: one snapshot, straight to the page. THE FRAME IS LOCKED and
    /// <c>shell/ghosts.js</c> consumes exactly it:
    /// <c>{type:'presence', self:&lt;opaque id|null&gt;, snapshot:&lt;the server payload&gt;}</c>.
    ///
    /// <para>THE SNAPSHOT IS PASSED THROUGH UNMODIFIED, and that is a rule rather than laziness.
    /// Its <c>now</c> is the SERVER's clock, which is what lets the page compute the server's ages
    /// for every ghost - reshaping it, or "helpfully" rewriting a timestamp into local time, is the
    /// one edit that would let a skewed machine clock invent a live student.</para>
    ///
    /// <para><c>self</c> is always included, because omitting it leaves the page's previous value
    /// standing; <c>snapshot</c> is null on the frame that only carries a newly-learned id, and the
    /// page leaves the crowd it is drawing exactly where it is.</para>
    /// </summary>
    private static void OnPresenceSnapshot(string? self, JObject? snapshot)
    {
        try
        {
            var disp = Application.Current?.Dispatcher;
            if (disp == null || disp.HasShutdownStarted) return;
            disp.BeginInvoke(() =>
            {
                try
                {
                    if (_host == null) return;
                    _host.Post(new { type = "presence", self, snapshot });
                }
                catch (Exception ex) { App.Logger?.Debug("ArcademyHost.OnPresenceSnapshot post: {E}", ex.Message); }
            });
        }
        catch (Exception ex) { App.Logger?.Debug("ArcademyHost.OnPresenceSnapshot: {E}", ex.Message); }
    }

    /// <summary>
    /// The <c>punchcard-result</c> frame: same-frame truth for the card, on both mint paths. The
    /// whole-blob <c>meta</c> snapshot carries the same state, but the ceremony needs to know
    /// whether THIS finish punched anything and whether it was the tenth hole - which a snapshot
    /// can only answer by diffing. Same reason <c>payout-result</c> carries the streak.
    ///
    /// <para><c>minted</c> is a COUNT, not a flag (owner ruling 2026-08-23): 3 for an enrollment,
    /// 2 for a day the class graded S, 1 for an ordinary day, 0 for a no-op. The ceremony walks
    /// that many beats. Zero is still falsy, so the shell's "no ceremony for a hole that was not
    /// punched" test reads exactly as it did when this was a bool.</para>
    ///
    /// <para>Posted even for a no-op mint (<c>minted:0</c> — a same-day retake, a repeat
    /// enrollment, a full card), so the shell never has to tell "nothing happened" apart from "the
    /// host did not answer". A NULL mint is the other thing entirely: there was no card to touch
    /// (no game key, or no store), and that gets no frame.</para>
    /// </summary>
    private static void PostPunchCard(string gameKey, string reason, ArcademyPunchCards.PunchMint? mint)
    {
        if (mint is not { } m) return;
        try
        {
            _host?.Post(new
            {
                type = "punchcard-result",
                gameKey,
                reason,
                minted = m.Punches,
                justUnlocked = m.JustUnlocked,
                holes = ArcademyPunchCards.Holes,
                card = m.Card,
            });
        }
        catch (Exception ex) { App.Logger?.Debug("ArcademyHost.PostPunchCard: {E}", ex.Message); }
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

    /// <summary>One servable row. <c>Tag</c> and <c>Src</c> are null on the app-wide path (the
    /// reply there is byte-for-byte what it always was) and set on the tagged path, where the tag
    /// IS the answer key the class grades on.</summary>
    private sealed record AssetUrl(string Url, string Kind, string Mime, string? Tag = null, string? Src = null);

    private static void OnAssetsRequest(JObject o)
    {
        var reqId = (string?)o["reqId"] ?? "";
        int count = Math.Clamp((int?)o["count"] ?? 8, 1, RemoteBatchCap);
        var kind = ((string?)o["kind"] ?? "still").Trim();
        if (kind != "loop" && kind != "still") kind = "still";

        // OPT-IN, and it has to be: a request that names its own subs is SORT asking for one of
        // the two piles the player picked, and it is served from that pile alone. Everything
        // without a `subs` array falls through to the app-wide pull below, unchanged.
        var subs = ReadRequestSubs(o);
        if (subs != null) { OnTaggedAssetsRequest(o, reqId, count, kind, subs); return; }

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

    /// <summary>Drain up to <paramref name="count"/> prewarmed rows. The key is the media kind on
    /// the app-wide path and <c>tag|kind</c> on the tagged one - one dictionary, two namespaces,
    /// so a pile can never be answered out of the app-wide buffer.</summary>
    private static List<AssetUrl> TakeBuffered(string key, int count)
    {
        var taken = new List<AssetUrl>();
        lock (RemoteBuffer)
        {
            if (!RemoteBuffer.TryGetValue(key, out var buf)) return taken;
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

    // ======================= tagged assets (SORT: the piles the player picked) =======================
    //
    // SORT (room 201) is the one class whose media has to be TRUE: the right pile is the player's
    // own niches, the left pile is the noise they chose, and the row's `tag` is the answer key the
    // swipe is graded against. So a request that carries `subs` is served from THOSE SUBS ONLY, out
    // of its own buffer, off its own rotation tenant, with every row stamped with the tag it was
    // asked for and the subreddit it really came from.
    //
    // Opt-in per request, deliberately (pitch 11): honouring a pile list unconditionally would move
    // every other class off the app-wide pull, which is exactly the regression this design must not
    // ship. No `subs` field = the code path above, untouched.

    /// <summary>Subs one request may name. Matches the coordinator's own channel ceiling.</summary>
    private const int TaggedSubCap = 64;

    /// <summary>Live sub list per tag, read by that tag's channel provider. The coordinator caches
    /// the FIRST provider it is handed for a consumer id and keeps it forever, so the provider has
    /// to close over THIS table rather than over one request's list - otherwise night two's sort
    /// would quietly deal night one's subs.</summary>
    private static readonly Dictionary<string, List<string>> TaggedChannels = new(StringComparer.Ordinal);

    /// <summary>Buffer keys with a fetch in the air. Per key rather than the single
    /// <see cref="_remoteFetchInFlight"/> latch the app-wide path uses: target and noise are two
    /// different asks, and one must not end the other's exchange with an empty reply.</summary>
    private static readonly HashSet<string> TaggedFetchesInFlight = new(StringComparer.Ordinal);

    private static bool _taggedSubsEmptyLogged;

    /// <summary>The request's sub list, sanitized and de-duplicated; null when the message carries
    /// no <c>subs</c> array at all (which is what keeps every other class on the old path), and an
    /// EMPTY list when it carried one that sanitized away to nothing.</summary>
    internal static List<string>? ReadRequestSubs(JObject o)
    {
        var field = o["subs"];
        if (field == null || field.Type == JTokenType.Null) return null;
        // Present but malformed (a bare string, an object) is REFUSED, not waved through to the
        // app-wide pull: a pile dealt from subs the player never picked is a lie, not a fallback.
        if (field is not JArray arr) return new List<string>();
        var clean = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in arr)
        {
            var name = FypOnlineCoordinator.SanitizeSub((string?)t);
            if (name == null || !seen.Add(name)) continue;
            clean.Add(name);
            if (clean.Count >= TaggedSubCap) break;
        }
        return clean;
    }

    /// <summary>The pile name, normalised to something safe to use as a dictionary key and as a
    /// tenant id suffix. 'target' / 'noise' in practice; anything else is honoured as its own pile
    /// rather than rejected, because the tag is the page's vocabulary, not ours.</summary>
    internal static string ReadTag(JObject o)
    {
        var raw = ((string?)o["tag"] ?? "").Trim().ToLowerInvariant();
        var kept = new string(raw.Where(ch => char.IsLetterOrDigit(ch) || ch == '_' || ch == '-').ToArray());
        if (kept.Length == 0) return "untagged";
        return kept.Length > 24 ? kept[..24] : kept;
    }

    internal static string TaggedBufferKey(string tag, string kind) => "sort:" + tag + "|" + kind;

    private static FypOnlineCoordinator TaggedCoordinator(string tag)
        => FypOnlineCoordinator.For(RemoteConsumerId + ":sort:" + tag, () => TaggedChannelsFor(tag), FeedMediaKind.Any);

    private static IReadOnlyList<string> TaggedChannelsFor(string tag)
    {
        lock (TaggedChannels)
        {
            return TaggedChannels.TryGetValue(tag, out var subs)
                ? new List<string>(subs)
                : (IReadOnlyList<string>)Array.Empty<string>();
        }
    }

    /// <summary>Point a pile at its subs. When the set actually changes, the pile's prewarmed rows
    /// go with it: those rows carry the OLD <c>src</c>, and <c>src</c> is what the class grades on.</summary>
    private static void SetTaggedChannels(string tag, List<string> subs)
    {
        bool changed;
        lock (TaggedChannels)
        {
            changed = !TaggedChannels.TryGetValue(tag, out var current) || !SameChannelSet(current, subs);
            if (changed) TaggedChannels[tag] = new List<string>(subs);
        }
        if (!changed) return;

        lock (RemoteBuffer)
        {
            RemoteBuffer.Remove(TaggedBufferKey(tag, "loop"));
            RemoteBuffer.Remove(TaggedBufferKey(tag, "still"));
        }
        try { TaggedCoordinator(tag).ResetChannels(); }
        catch (Exception ex) { App.Logger?.Debug("ArcademyHost: tagged rotation reset failed: {E}", ex.Message); }
        App.Logger?.Information("ArcademyHost: pile '{Tag}' = {Subs}", tag, string.Join(", ", subs));
    }

    private static bool SameChannelSet(List<string> a, List<string> b)
    {
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++)
            if (!string.Equals(a[i], b[i], StringComparison.OrdinalIgnoreCase)) return false;
        return true;
    }

    private static void OnTaggedAssetsRequest(JObject o, string reqId, int count, string kind, List<string> subs)
    {
        var tag = ReadTag(o);

        if (!RemoteMediaEnabled() || App.Settings?.Current?.OfflineMode == true)
        {
            PostTaggedAssets(reqId, tag, Array.Empty<AssetUrl>(), true);
            return;
        }
        if (subs.Count == 0)
        {
            // An empty pile is answered empty rather than silently falling back to the app-wide
            // pull: a sort dealt from subs the player never picked is a lie, not a fallback.
            if (!_taggedSubsEmptyLogged)
            {
                _taggedSubsEmptyLogged = true;
                App.Logger?.Information("ArcademyHost: tagged assets-request '{Tag}' carried no usable subs", tag);
            }
            PostTaggedAssets(reqId, tag, Array.Empty<AssetUrl>(), true);
            return;
        }

        SetTaggedChannels(tag, subs);

        var key = TaggedBufferKey(tag, kind);
        var served = TakeBuffered(key, count);
        bool satisfied = served.Count >= count;
        PostTaggedAssets(reqId, tag, served, satisfied);
        if (!satisfied) ServeTaggedBatch(reqId, tag, kind, count - served.Count);
    }

    /// <summary>The tagged reply. Same <c>assets</c> envelope the app-wide path posts, with
    /// <c>tag</c> and <c>src</c> on every row (and a top-level tag for the log's sake).</summary>
    private static void PostTaggedAssets(string reqId, string tag, IReadOnlyList<AssetUrl> rows, bool done)
    {
        _host?.Post(new
        {
            type = "assets",
            reqId,
            tag,
            urls = rows.Select(u => new
            {
                url = u.Url,
                kind = u.Kind,
                mime = u.Mime,
                tag = u.Tag ?? tag,
                src = u.Src ?? "",
            }).ToArray(),
            done,
        });
    }

    /// <summary>Fetch one batch for a pile and post it under the original reqId. Mirrors
    /// <see cref="ServeRemoteBatch"/> - same generation guard, same "always terminate the exchange"
    /// posture - with the pile's own tenant, buffer and in-flight latch.</summary>
    private static async void ServeTaggedBatch(string reqId, string tag, string kind, int want)
    {
        int epoch = Volatile.Read(ref _generation);
        var key = TaggedBufferKey(tag, kind);

        lock (TaggedFetchesInFlight)
        {
            if (!TaggedFetchesInFlight.Add(key))
            {
                // End THIS exchange rather than leaving the page's latch open; the pool it wanted
                // is one ask away (the page asks again after every reply).
                PostTaggedAssets(reqId, tag, Array.Empty<AssetUrl>(), true);
                return;
            }
        }
        try
        {
            var allowed = new HashSet<string>(TaggedChannelsFor(tag), StringComparer.OrdinalIgnoreCase);
            if (allowed.Count == 0)
            {
                PostTaggedAssets(reqId, tag, Array.Empty<AssetUrl>(), true);
                return;
            }

            var mediaKind = kind == "loop" ? FeedMediaKind.Video : FeedMediaKind.Image;
            var coord = TaggedCoordinator(tag);
            var (entries, error) = await coord.FetchBatchAsync(mediaKind, CancellationToken.None)
                .ConfigureAwait(false);

            var fresh = new List<AssetUrl>();
            foreach (var e in entries)
            {
                if (!RemoteMediaFormats.Validate(e, mediaKind, out var reason))
                {
                    App.Logger?.Debug("ArcademyHost: rejected tagged entry {Id}: {Reason}", e.Id, reason);
                    continue;
                }
                // ScrolllerSource stamps Folder as "r/<sub>" - that IS the src the page shows and
                // the door de-duplicates on. A row we cannot place in the pile is dropped: the
                // rotation only holds this pile's channels, and this is the belt to that braces.
                var folder = (e.Folder ?? "").Trim();
                var bare = folder.StartsWith("r/", StringComparison.OrdinalIgnoreCase) ? folder[2..] : folder;
                if (bare.Length == 0 || !allowed.Contains(bare)) continue;
                fresh.Add(new AssetUrl(e.Url, kind, MimeFor(e.Url, kind), tag, "r/" + bare));
                if (fresh.Count >= RemoteBatchCap) break;
            }

            var win = _host?.Window;
            if (win == null) return;                                  // the Arcademy closed while fetching
            if (Volatile.Read(ref _generation) != epoch) return;      // ...or closed and relaunched
            await win.Dispatcher.InvokeAsync(() =>
            {
                if (_host == null || Volatile.Read(ref _generation) != epoch) return;
                var send = fresh.Take(want).ToList();
                lock (RemoteBuffer)
                {
                    if (!RemoteBuffer.TryGetValue(key, out var buf)) RemoteBuffer[key] = buf = new List<AssetUrl>();
                    buf.AddRange(fresh.Skip(send.Count));
                    if (buf.Count > 120) buf.RemoveRange(0, buf.Count - 120);
                }
                if (error != null) App.Logger?.Debug("ArcademyHost: tagged batch '{Tag}' error {E}", tag, error);
                PostTaggedAssets(reqId, tag, send, true);
            });
        }
        catch (Exception ex)
        {
            App.Logger?.Warning("ArcademyHost: tagged batch failed: {E}", ex.Message);
            if (Volatile.Read(ref _generation) == epoch)
            {
                try { PostTaggedAssets(reqId, tag, Array.Empty<AssetUrl>(), true); } catch { }
            }
        }
        finally { lock (TaggedFetchesInFlight) TaggedFetchesInFlight.Remove(key); }
    }

    // ======================= local sample (the other kind of pile) =======================
    //
    // The page cannot enumerate a virtual host, so a local pile is sampled here: a folder list (or
    // one asset preset) in, `assets` rows out on the same envelope the remote path uses. Same
    // deselection blacklist the flash pool honours, and the same ccp.assets urls BuildLocalAssets
    // hands out - a row's src is the folder it really came from.

    private static readonly string[] LocalLoopExts = { ".gif", ".mp4", ".webm" };
    private static readonly string[] LocalStillExts = { ".png", ".jpg", ".jpeg", ".webp" };

    private static async void OnLocalSampleRequest(JObject o)
    {
        var reqId = (string?)o["reqId"] ?? "";
        int count = Math.Clamp((int?)o["count"] ?? 8, 1, RemoteBatchCap);
        var kind = ((string?)o["kind"] ?? "still").Trim();
        if (kind != "loop" && kind != "still") kind = "still";
        var tag = ReadTag(o);
        var folders = ReadStringArray(o["folders"]);
        var presetId = ((string?)o["presetId"] ?? "").Trim();
        int epoch = Volatile.Read(ref _generation);

        List<AssetUrl> rows;
        try
        {
            // A big library is a slow walk and the UI thread is holding a webview: enumerate off it.
            rows = await Task.Run(() => SampleLocalAssets(reqId, count, kind, tag, folders, presetId))
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            App.Logger?.Warning("ArcademyHost: local sample failed: {E}", ex.Message);
            rows = new List<AssetUrl>();
        }

        var win = _host?.Window;
        if (win == null) return;
        if (Volatile.Read(ref _generation) != epoch) return;
        await win.Dispatcher.InvokeAsync(() =>
        {
            if (_host == null || Volatile.Read(ref _generation) != epoch) return;
            PostTaggedAssets(reqId, tag, rows, true);
        });
    }

    private static List<string> ReadStringArray(JToken? token)
    {
        var list = new List<string>();
        if (token is not JArray arr) return list;
        foreach (var t in arr)
        {
            var s = ((string?)t ?? "").Trim();
            if (s.Length > 0) list.Add(s);
        }
        return list;
    }

    private static List<AssetUrl> SampleLocalAssets(string reqId, int count, string kind, string tag,
        List<string> folders, string presetId)
    {
        var rows = new List<AssetUrl>();
        var root = App.EffectiveAssetsPath;
        if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return rows;

        var exts = kind == "loop" ? LocalLoopExts : LocalStillExts;

        // Which deselection list applies: a named preset brings its own, otherwise the tree the
        // user is looking at right now (identical to what BuildLocalAssets serves).
        IEnumerable<string>? disabledPaths = App.Settings?.Current?.DisabledAssetPaths;
        string? presetSrc = null;
        if (presetId.Length > 0)
        {
            foreach (var p in App.Settings?.Current?.AssetPresets ?? new List<Models.AssetPreset>())
            {
                if (p == null || !string.Equals(p.Id, presetId, StringComparison.Ordinal)) continue;
                disabledPaths = p.DisabledAssetPaths;
                presetSrc = "preset:" + p.Id;
                break;
            }
            if (presetSrc == null)
                App.Logger?.Debug("ArcademyHost: local sample named unknown preset {Id}", presetId);
        }
        var disabled = Quiz.IntakeHostService.BuildDisabledAssetSet(disabledPaths);

        var searchRoots = new List<string>();
        foreach (var rel in folders)
        {
            var abs = ResolveAssetsFolder(root, rel);
            if (abs != null) searchRoots.Add(abs);
        }
        if (searchRoots.Count == 0)
        {
            foreach (var top in new[] { "images", "videos" })
            {
                var abs = Path.Combine(root, top);
                if (Directory.Exists(abs)) searchRoots.Add(abs);
            }
        }

        var pool = new List<string>();
        var seenFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dir in searchRoots)
        {
            IEnumerable<string> files;
            try { files = Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories); }
            catch (Exception ex) { App.Logger?.Debug("ArcademyHost: local sample walk of {Dir} failed: {E}", dir, ex.Message); continue; }
            foreach (var file in files)
            {
                var ext = Path.GetExtension(file).ToLowerInvariant();
                if (Array.IndexOf(exts, ext) < 0) continue;
                if (!Quiz.IntakeHostService.IsAssetActive(disabled, root, file)) continue;
                if (!seenFiles.Add(file)) continue;   // two picked folders can nest
                pool.Add(file);
            }
        }
        if (pool.Count == 0) return rows;

        // Seeded off the reqId so a retake of the same ask deals the same slice; partial
        // Fisher-Yates, the same random-slice trick BuildLocalAssets uses.
        var rng = new Random(StableSeed(reqId));
        int take = Math.Min(count, pool.Count);
        for (int i = 0; i < take; i++)
        {
            int j = rng.Next(i, pool.Count);
            (pool[i], pool[j]) = (pool[j], pool[i]);
        }
        for (int i = 0; i < take; i++)
        {
            var file = pool[i];
            var url = ToAssetsUrl(file);
            rows.Add(new AssetUrl(url, kind, MimeFor(url, kind), tag, presetSrc ?? RelativeFolder(root, file)));
        }
        return rows;
    }

    /// <summary>A page-supplied folder resolved under the assets root, or null when it does not
    /// exist or tries to climb out of it. Never trust a path that arrived over the bridge.</summary>
    internal static string? ResolveAssetsFolder(string root, string rel)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(rel)) return null;
            var cleaned = rel.Replace('\\', '/').Trim('/');
            if (cleaned.Length == 0) return null;
            var rootFull = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
            var abs = Path.GetFullPath(Path.Combine(rootFull, cleaned));
            if (!abs.StartsWith(rootFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(abs, rootFull, StringComparison.OrdinalIgnoreCase))
            {
                App.Logger?.Warning("ArcademyHost: local sample refused a path outside the assets root: {Rel}", rel);
                return null;
            }
            return Directory.Exists(abs) ? abs : null;
        }
        catch { return null; }
    }

    /// <summary>The file's own folder, relative to the assets root, forward slashes - the same
    /// shape <c>localFolders</c> projects, so a src can be matched back to a folder chip.</summary>
    private static string RelativeFolder(string root, string file)
    {
        try
        {
            var dir = Path.GetDirectoryName(file);
            if (string.IsNullOrEmpty(dir)) return "";
            return Path.GetRelativePath(root, dir).Replace('\\', '/');
        }
        catch { return ""; }
    }

    /// <summary>FNV-1a over the reqId: a stable seed, so the same ask deals the same slice.</summary>
    private static int StableSeed(string s)
    {
        unchecked
        {
            uint h = 2166136261;
            foreach (var ch in s ?? "")
            {
                h ^= ch;
                h *= 16777619;
            }
            return (int)(h & 0x7FFFFFFF);
        }
    }

    // ======================= the sub library (probe / remove / push) =======================

    private static readonly HashSet<string> ProbesInFlight = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The SORT door's search box: is r/&lt;name&gt; real, and how much video does it hold. Same
    /// upstream question the two shipped pickers ask (<c>FypHostService.ProbeCustomSub</c>), with
    /// two differences that matter here: the answer is keyed by <c>reqId</c> (the door awaits a
    /// promise, so every path MUST reply), and a verified name lands in the LIBRARY ONLY.
    ///
    /// <para>It never touches <c>FypOnlineCustomSubs</c>: noise the player picked to sort against
    /// must not start flashing on their desktop. That split is the whole point of the library.</para>
    ///
    /// <para>BRIGHT LINE, as everywhere on this path: the probe goes straight from this machine to
    /// the provider. Nothing routes through CC Labs infrastructure.</para>
    /// </summary>
    private static async void OnProbeSub(JObject o)
    {
        var reqId = (string?)o["reqId"] ?? "";
        var raw = (string?)o["name"] ?? (string?)o["sub"] ?? "";
        var clean = FypOnlineCoordinator.SanitizeSub(raw);
        if (clean == null)
        {
            PostSubProbe(reqId, (raw ?? "").Trim(), false, null, "invalid");
            return;
        }

        var s = App.Settings?.Current;
        // Cached for a week, the same window both pickers trust (AppSettings.SubVerdictMaxAgeDays):
        // a door re-opened every night must not spend a round trip per chip.
        if (s != null && !s.SubVerdictIsStale(clean)
            && s.FypOnlineSubVerdicts.TryGetValue(clean, out var cached) && cached != null)
        {
            if (cached.Ok && s.TryAddLibrarySub(clean)) { App.Settings?.Save(); PushLibrary(); }
            PostSubProbe(reqId, clean, cached.Ok, cached.VideoCount, null);
            return;
        }

        if (!RemoteMediaEnabled() || App.Settings?.Current?.OfflineMode == true)
        {
            PostSubProbe(reqId, clean, false, null, "offline");
            return;
        }

        lock (ProbesInFlight)
        {
            if (!ProbesInFlight.Add(clean))
            {
                // Answer rather than drop: the door is awaiting this reqId, and a silent duplicate
                // is a promise that never settles.
                PostSubProbe(reqId, clean, false, null, "busy");
                return;
            }
        }

        int epoch = Volatile.Read(ref _generation);
        try
        {
            var probe = await Task.Run(() => FypOnlineCoordinator.ProbeSubAsync(clean, CancellationToken.None))
                .ConfigureAwait(false);

            var win = _host?.Window;
            if (win == null) return;
            if (Volatile.Read(ref _generation) != epoch) return;
            await win.Dispatcher.InvokeAsync(() =>
            {
                if (_host == null || Volatile.Read(ref _generation) != epoch) return;
                var st = App.Settings?.Current;
                bool libraryMoved = false;
                // A transport failure taught us nothing about the sub, so no verdict is written.
                if (st != null && probe.Error == null)
                {
                    st.FypOnlineSubVerdicts[clean] = new Models.RemoteSubVerdict
                    {
                        Ok = probe.Ok,
                        VideoCount = probe.VideoCount,
                        CheckedAtUtc = DateTime.UtcNow,
                    };
                    if (probe.Ok)
                    {
                        libraryMoved = st.TryAddLibrarySub(clean);
                        if (libraryMoved)
                            App.Logger?.Information("ArcademyHost: r/{Sub} verified ({N} videos) and kept in the library",
                                clean, probe.VideoCount);
                    }
                    App.Settings?.Save();
                }
                PostSubProbe(reqId, clean, probe.Ok, probe.VideoCount, probe.Error);
                if (libraryMoved) PushLibrary();
            });
        }
        catch (Exception ex)
        {
            App.Logger?.Warning("ArcademyHost: sub probe failed: {E}", ex.Message);
            if (Volatile.Read(ref _generation) == epoch)
            {
                try { PostSubProbe(reqId, clean, false, null, "offline"); } catch { }
            }
        }
        finally { lock (ProbesInFlight) ProbesInFlight.Remove(clean); }
    }

    private static void PostSubProbe(string reqId, string name, bool ok, int? videoCount, string? error)
        => _host?.Post(new
        {
            type = "sub-probe",
            reqId,
            name,
            ok,
            videoCount,
            // videoCount 0 with ok:true is a real answer - the sub exists and has stills only.
            stillOnly = ok && videoCount.GetValueOrDefault() == 0,
            error,
        });

    /// <summary>The X on a library pill: the entry, its verdict and its feed membership go
    /// together (AppSettings.RemoveLibrarySub). One gesture, gone everywhere.</summary>
    private static void OnLibraryRemove(JObject o)
    {
        try
        {
            var name = (string?)o["name"] ?? (string?)o["sub"] ?? "";
            var s = App.Settings?.Current;
            if (s == null || string.IsNullOrWhiteSpace(name)) { PushLibrary(); return; }
            if (s.RemoveLibrarySub(name))
            {
                App.Settings?.Save();
                // The name may have been a live channel for every consumer, not just this page.
                FypOnlineCoordinator.ResetAllChannels();
                App.Logger?.Information("ArcademyHost: r/{Sub} removed from the library", name.Trim());
            }
            PushLibrary();
        }
        catch (Exception ex) { App.Logger?.Warning("ArcademyHost: library remove failed: {E}", ex.Message); }
    }

    /// <summary>Push the whole library after any change. Replace, never patch: the page holds one
    /// list and a diff protocol would be a second source of truth.</summary>
    private static void PushLibrary()
    {
        try { _host?.Post(new { type = "library", subLibrary = BuildSubLibrary() }); }
        catch (Exception ex) { App.Logger?.Debug("ArcademyHost.PushLibrary: {E}", ex.Message); }
    }

    /// <summary>The kept subs joined with their verdicts (and with whether the app-wide feed
    /// currently uses them, which the door renders as a quiet hint, never as a gate).</summary>
    private static object[] BuildSubLibrary()
    {
        var s = App.Settings?.Current;
        if (s == null) return Array.Empty<object>();
        var rows = s.BuildRemoteSubLibraryView();
        var outRows = new List<object>(rows.Count);
        foreach (var r in rows)
            outRows.Add(new { name = r.Name, ok = r.Ok, videoCount = r.VideoCount, stillOnly = r.StillOnly, selected = r.Selected });
        return outRows.ToArray();
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
        // The library is not a `setting` key, and a restored instance can carry a different one.
        PushLibrary();
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
        nameof(Models.AppSettings.ArcademyPresenceShare),
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

            // The library is a LIST, not a projected scalar, so it rides its own message rather
            // than the `setting` echo. Pushed on both properties: the Assets tab can add a name
            // (library) or change what the feed uses (selection), and the page renders both.
            if (e.PropertyName == nameof(Models.AppSettings.RemoteSubLibrary)
                || e.PropertyName == nameof(Models.AppSettings.FypOnlineCustomSubs))
            {
                PushLibrary();
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
        // CAMPUS PRESENCE. Clamped on the way out as well as on the way in: the property already
        // refuses an unknown rung, and reading it through the same allowlist here means a
        // hand-edited settings file can never project a rung the page has no control for.
        nameof(Models.AppSettings.ArcademyPresenceShare) => ("presenceShare", PresenceShare(s)),
        _ => (null, null),
    };

    /// <summary>The presence rung, clamped to the four we know. Anything else is <c>off</c> - a
    /// consent flag degrades to "no consent", never to the nearest neighbour.</summary>
    private static string PresenceShare(Models.AppSettings? s)
    {
        var v = (s?.ArcademyPresenceShare ?? "").Trim().ToLowerInvariant();
        return Array.IndexOf(Models.AppSettings.ArcademyPresenceShares, v) >= 0 ? v : "off";
    }

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
            // Unbind the mirror BEFORE the store goes: a push still sitting in the debounce is
            // sent now (payload taken first, so the request outlives this window without touching
            // anything being disposed) and every reply still in the air is dropped by generation.
            try { ArcademySyncService.Detach(); } catch { }
            // Stop the presence poll BEFORE the host goes: the timer must never outlive the window
            // that armed it, and Detach also sends this session's one best-effort `campus_leave`.
            try { ArcademyPresenceService.Detach(); } catch { }
            try { _meta?.FlushSave(); } catch { }
            _meta = null;
            _classActive = false;
            _panicSuspended = false;
            _lastPanicPressUtc = DateTime.MinValue;
            _initPosted = false;
            lock (RemoteBuffer) RemoteBuffer.Clear();
            // The piles belong to the class that picked them; the next launch names its own.
            lock (TaggedChannels) TaggedChannels.Clear();
            _taggedSubsEmptyLogged = false;
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
