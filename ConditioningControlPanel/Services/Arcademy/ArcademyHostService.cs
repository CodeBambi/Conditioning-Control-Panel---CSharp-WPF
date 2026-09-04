using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ConditioningControlPanel.Localization;
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

    /// <summary>When this visit to the Arcademy started, for EMI's <c>arcademyClosed</c> duration.
    /// Nothing else in the service was timing a visit. MinValue means "not open", which is also
    /// what keeps the idempotent close paths from announcing the same visit twice.</summary>
    private static DateTime _emiOpenedUtc = DateTime.MinValue;
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

    /// <summary>The page's LAST boot attempt failed: it reported <c>boot-error</c>, or the host's
    /// own progress deadline fired. Entry points read this to warn before sending someone back
    /// through a door that just failed on this machine.
    ///
    /// <para>It is a LAST-attempt flag, not a session tombstone. <see cref="OnPageReady"/> clears
    /// it, because most of what sets it is transient - a cold WebView2 runtime, a machine under
    /// load, a stalled GPU driver, all of which the 45s <c>BootDeadline</c> reads as failure. The
    /// entry point offers a retry rather than refusing outright; only a session where the campus
    /// has never once come up stays latched.</para></summary>
    public static bool BootFailedThisSession { get; private set; }

    // ============================== the gate ==============================

    /// <summary>
    /// Single source of truth for "is there an Arcademy door". Everything that shows, hides or
    /// opens the Arcademy asks this - the Play card's visibility in
    /// <c>MainWindow.RefreshPlayCards</c> and the refusal at the top of <see cref="Launch"/>.
    ///
    /// <para><c>true</c> since v6.8.5 "First Bell": the Arcademy is open. Semester 1 landed on
    /// main (PR #241) ahead of its public reveal and stayed hidden through 6.8.4; this is the
    /// release that reveals it. Flipping this back to <c>false</c> is still the whole hide - a
    /// HIDE, not a lockband, for the same reason Just Drop hides: a lockband advertises something
    /// the account could buy, and a door we have not opened is not for sale.</para>
    ///
    /// <para>The T2 bar and the AudioOnlySession rule below are untouched and still apply
    /// underneath it.</para>
    /// </summary>
    /// <remarks>static readonly, not const: a const would make the guard in <see cref="Launch"/>
    /// compile-time unreachable (CS0162), exactly as JustDropService.Withheld documents.</remarks>
    public static readonly bool DoorAvailable = true;
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
            App.Logger?.Information("ArcademyHost.Launch refused: the Arcademy door is closed in this build");
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

        // EMI Desk: the ring learns from every open, not just its own cards.
        try { App.EmiDesk?.NoteOpen("arcademy"); } catch { }
        // She does not follow you to school. If she is out, she says goodbye here and winks
        // herself off screen a couple of seconds later, BEFORE `arcademyOpened` and before the
        // ring's own `arcademyFromRing` can land: the farewell claims her voice for that window,
        // so whichever of the three paths opened the Arcademy, the last thing you get is the bye.
        try { App.EmiDesk?.FarewellForArcademy(); } catch { }
        try { App.EmiDesk?.Fire("arcademyOpened", null); } catch { }
        _emiOpenedUtc = DateTime.UtcNow;

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

            // THE SHARED WALLET (wallet contract, "Client behaviour"). Same posture, one shelf
            // over: fetch the ACCOUNT's wallet, carry this machine's up if it never has been, and
            // drain anything a night offline left unpaid. Signed out, this does nothing at all and
            // the money path below stays exactly the local-authority one it has always been.
            ArcademyWalletSyncService.Attach(_meta, OnWalletBanked);

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
        // An open Discord link-up is part of what the emergency stop stops: the browser is sitting
        // on a page this window asked for, and the chip must not be left saying "Waiting...".
        CancelPendingLink("panic", tellPage: true);
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
            // The campus is up, so whatever failed last time did not stick. Clearing here (and only
            // here) is what keeps BootFailedThisSession a statement about the LAST attempt: a cold
            // runtime start that blew the 45s deadline must not cost the user the feature for the
            // rest of the app's life.
            BootFailedThisSession = false;
            // Keyboard focus does not land in the WebView2 child until a click on a fresh launch -
            // claim it now so the Esc ladder works from the first frame.
            _host?.FocusWeb();
            if (_initPosted) return;   // exactly one init per boot (contract §4)
            _initPosted = true;
            _host?.Post(BuildInit());
            if (_host != null) _host.Post(new { type = "fullscreen", on = _host.IsFullscreen });
            SeedNativeState();
            // THE STUDENT ID PHOTO. `init` carried whatever was already on disk; this asks the CDN
            // whether that is still the player's avatar. Fire-and-forget, never awaited, and it
            // pushes its own `profile` frame only if the bytes actually changed.
            KickAvatarRefresh();
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
                // THE LOCKER DRESSES THE DESKTOP TOO. Every equip the player makes is one of these
                // (locker.js `metaSet(OUTFIT_KEY, ...)`), so this is where the desk hears about it
                // without a poll and without the Arcademy having to close first. Read back through
                // EquippedEmiOutfit, never off `o`: the store may have clamped or refused the write,
                // and the wallet still has the last word on whether she may wear it.
                if (string.Equals((string?)o["key"], EmiOutfitKey, StringComparison.Ordinal))
                    PushEmiOutfitToDesk();
                break;
            case "class-started":
                _classActive = true;
                // THE EXTRA CREDIT LEVER, remembered rather than believed. The page says which
                // notch it thinks it pulled; the host clamps it against what is actually unlocked
                // and holds the answer until class-ended asks. The page never echoes a multiplier.
                NotePendingLever((string?)o["gameKey"], (string?)o["lever"]);
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
            case "prize-buy":
                OnPrizeBuy(o);
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
            case "link-discord":
                // THE STUDENT ID's photo chip, in its "Link Discord" state. Posted ONLY when
                // discordLinked is false - a linked account moves the rung with an ordinary
                // set-setting instead (contract trap 1).
                OnLinkDiscord();
                break;
            case "annex-stats":
                // The registry link downstairs. Fire-and-forget: exactly one reply comes back,
                // and a failure is a reply with body = null, never a missing one.
                OnAnnexStats();
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
            // THE HOUSE VOCABULARY'S clips: one row per word of the school's own 24-word list
            // that has an mp3 beside the page. `words` above may legally be EMPTY - nothing
            // enabled, or a creator mod that ships no pool - and on that day the page deals the
            // house list instead (core/vocab.js) and reads its triggers from HERE. Listed always,
            // audible only under SubAudioAudible, exactly like `triggers`; the page filters these
            // to the words it actually dealt so ctx.triggers never describes a pool nobody sees.
            houseTriggers = BuildHouseTriggers(),
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
            // THE SUBJECT FILE (ANNEX-OS.md): the numbers the annex terminal prints about
            // this player, resolved here so the page downstairs counts nothing itself.
            subject = BuildSubject(s),
            // THE STUDENT ID (STUDENT ID contract, "Wire contract"). Who the card is made out to,
            // whether there is a photo on it, and the one consent rung that governs both.
            profile = BuildProfile(s),
            // THE PRIZE COUNTER. The shelf, tonight's payday and the two lever rungs, all resolved
            // here - the page renders the catalog it is handed and never prices anything. The
            // WALLET itself is not here: it rides `meta` above under its own host-owned key, so
            // there is exactly one copy of the balances on the wire.
            economy = BuildEconomy(),
        };
    }

    /// <summary>
    /// THE PRIZE COUNTER's projection. Wrapped like every other optional block here: a throw would
    /// kill <c>init</c>, and a page that never gets <c>init</c> never boots. A failure costs the
    /// shelf its stock for the session, never the Arcademy.
    ///
    /// <para><c>payday</c> is the SEEDED nightly draw, computed once here off the UTC date and the
    /// enrolled roster (<see cref="ArcademyEconomy.PickPayday"/>) so every machine on the same day
    /// agrees. The page displays it; it never rolls anything.</para>
    /// </summary>
    private static object BuildEconomy()
    {
        try
        {
            // THE SERVER'S DRAW WINS WHEN THERE IS ONE. Both sides run the same seeded pick over
            // the same roster, so they normally agree to the letter; where they can differ is a
            // roster that has not finished mirroring, and on that night the account's answer is the
            // one every OTHER device is also showing. The local pick is the fallback, and it is
            // what a signed-out player (and an early `init` that beat the reply home) gets.
            var payday = ArcademyWalletSyncService.ServerPayday ?? ArcademyEconomy.PickPayday(
                DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                _meta?.EnrolledGameKeys());
            var (extra, honors) = _meta?.LeverUnlocks() ?? (false, false);
            return new
            {
                catalog = ArcademyEconomy.CatalogJson(),
                payday = new { gameKey = payday.GameKey, mult = payday.Mult },
                leverUnlocks = new { extra, honors },
                // Which per-game setting VALUES the counter holds the door on. One row today
                // (The Deep End's wide board); the settings page draws a locked value from this
                // rather than guessing which enum entry a sku is talking about. The host refuses
                // the write regardless (PrizeGateAllows), so this is dressing, never the gate.
                settingUnlocks = new JArray
                {
                    new JObject
                    {
                        ["key"] = DeepEndBoardSizeKey,
                        ["value"] = DeepEndWideBoard,
                        ["sku"] = ArcademyEconomy.SkuDeepEndWideBoard,
                        ["owned"] = _meta?.WalletOwns(ArcademyEconomy.SkuDeepEndWideBoard) == true,
                    },
                },
            };
        }
        catch (Exception ex)
        {
            App.Logger?.Debug("ArcademyHost.BuildEconomy: {E}", ex.Message);
            return new
            {
                catalog = new JArray(),
                payday = new { gameKey = (string?)null, mult = 1 },
                leverUnlocks = new { extra = false, honors = false },
                settingUnlocks = new JArray(),
            };
        }
    }

    // ============================ the wallet, read from outside ============================

    /// <summary>
    /// DOES THE PLAYER HOLD THIS PRIZE? The one door out of the Arcademy's wallet for the rest of
    /// the app, and it exists for exactly one row on the shelf: TUBE GLASS: MIDNIGHT is bought at
    /// the Prize Counter and worn by <c>AvatarTubeWindow</c>, which is up and running long before
    /// (and long after) anyone opens the school.
    ///
    /// <para>Two sources, in order. The LIVE store first — while a class is on, it is the only
    /// copy that has tonight's purchase in it. When the Arcademy is closed <see cref="_meta"/> is
    /// null (it is minted at launch and dropped at teardown, after a flush), so the answer comes
    /// off the same <c>arcademy_meta.json</c> the store would have read, cached against the file's
    /// stamp so a per-frame caller cannot turn a cosmetic into a disk read.</para>
    ///
    /// <para>NEVER THROWS, and a wallet it cannot read is answered "no". A cosmetic that fails
    /// closed is a plain tube; one that failed open would be a promise the counter never sold.</para>
    /// </summary>
    public static bool WalletOwnsSku(string sku)
    {
        if (string.IsNullOrWhiteSpace(sku)) return false;
        try
        {
            var live = _meta;
            if (live != null) return live.WalletOwns(sku);
            return WalletOwnsOnDisk(sku);
        }
        catch (Exception ex)
        {
            App.Logger?.Debug("ArcademyHost.WalletOwnsSku({Sku}): {E}", sku, ex.Message);
            return false;
        }
    }

    private static readonly object _walletDiskLock = new();
    private static HashSet<string>? _walletDiskInv;
    private static long _walletDiskStamp;   // ticks ^ length; any write moves it

    /// <summary>The owned-sku set out of the persisted blob, re-read only when the file's stamp
    /// moves. A missing or unparseable file caches an EMPTY set rather than nothing, so a player
    /// who has never opened the Arcademy costs one File.Exists per stamp, not one per call.</summary>
    private static bool WalletOwnsOnDisk(string sku)
    {
        var path = Path.Combine(App.UserDataPath, "arcademy_meta.json");
        long stamp;
        try
        {
            var info = new FileInfo(path);
            stamp = info.Exists ? (info.LastWriteTimeUtc.Ticks ^ info.Length) : 0L;
        }
        catch { stamp = 0L; }

        lock (_walletDiskLock)
        {
            if (_walletDiskInv == null || _walletDiskStamp != stamp)
            {
                _walletDiskInv = ReadOwnedSkus(path);
                _walletDiskStamp = stamp;
            }
            return _walletDiskInv.Contains(sku);
        }
    }

    private static HashSet<string> ReadOwnedSkus(string path)
    {
        var owned = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            if (!File.Exists(path)) return owned;
            var blob = JObject.Parse(File.ReadAllText(path));
            if (blob[ArcademyMetaStore.WalletKey] is not JObject wallet) return owned;
            if (wallet["inv"] is not JObject inv) return owned;
            foreach (var p in inv.Properties())
            {
                // Same witness the counter uses: a row with a positive count is held.
                if (p.Value is JObject row && (int?)row["n"] > 0) owned.Add(p.Name);
            }
        }
        catch (Exception ex)
        {
            App.Logger?.Debug("ArcademyHost.ReadOwnedSkus: {E}", ex.Message);
        }
        return owned;
    }

    // ============================ the Locker's outfit, read from outside ============================

    /// <summary>The meta key the Locker arms EMI's outfit in (<c>OUTFIT_KEY</c>,
    /// <c>Resources/web/arcademy/shell/locker.js</c>). Page-owned and free-form, like every other
    /// non host-owned key in the blob.</summary>
    public const string EmiOutfitKey = "lockerOutfit";

    /// <summary>Which prize each garment is: the gate the Locker itself applies
    /// (<c>OUTFIT_SKU</c>, locker.js) repeated verbatim so BOTH sides refuse the same thing.</summary>
    private static readonly IReadOnlyDictionary<string, string> EmiOutfitSku =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["varsity"] = "emi_varsity",
            ["labcoat"] = "emi_labcoat",
            ["cheer"] = "emi_cheer",
            ["swim"] = "emi_swim",
        };

    /// <summary>
    /// WHAT IS EMI WEARING? The second door out of the Arcademy's state, and the one the EMI Desk
    /// widget dresses off: the Locker arms an outfit on the campus, and the girl on the user's
    /// desktop is the same girl, so she wears it there too (community ask, 2026-09-01).
    ///
    /// <para>Same two sources as <see cref="WalletOwnsSku"/> and for the same reason: the LIVE store
    /// first, because while the Arcademy is open it is the only copy holding a pick made ten seconds
    /// ago, and the persisted blob when it is closed (the store is minted at launch and dropped at
    /// teardown, after a flush).</para>
    ///
    /// <para><b>Ownership is enforced HERE, not only page-side.</b> locker.js already clamps its own
    /// read against the wallet (<c>readOutfit</c>), but that clamp lives in the same file that writes
    /// the key, so a blob carrying a garment nobody bought - an older build, a hand-edited save, a
    /// wallet that got rolled back by a sync - would dress her anyway. The desk asks the wallet
    /// itself and answers null when the prize is not held.</para>
    ///
    /// <para>NEVER THROWS. Anything it cannot read, parse, recognise or verify is null, which is
    /// "the standard art" - the sheet that has always been there.</para>
    /// </summary>
    /// <returns>An <see cref="EmiDesk.EmiChains.Outfits"/> name, or null for the standard art.</returns>
    public static string? EquippedEmiOutfit()
    {
        try
        {
            var live = _meta;
            var raw = live != null ? (string?)live.Get(EmiOutfitKey) : EmiOutfitOnDisk();

            var name = EmiDesk.EmiChains.OutfitName(raw);
            if (name == null) return null;

            // Bought, or she is not wearing it. `varsity` has been gated since the restock and the
            // other three got skus of their own in the same wave, so every name in the list has one.
            if (!EmiOutfitSku.TryGetValue(name, out var sku)) return null;
            if (!WalletOwnsSku(sku))
            {
                App.Logger?.Debug("ArcademyHost.EquippedEmiOutfit: '{Outfit}' is armed but {Sku} is not owned - standard art", name, sku);
                return null;
            }
            return name;
        }
        catch (Exception ex)
        {
            App.Logger?.Debug("ArcademyHost.EquippedEmiOutfit: {E}", ex.Message);
            return null;
        }
    }

    private static readonly object _outfitDiskLock = new();
    private static string? _outfitDiskValue;
    private static long _outfitDiskStamp = -1L;   // -1 = never read; any write to the file moves it

    /// <summary>The armed outfit out of the persisted blob, re-read only when the file's stamp
    /// moves - the same cache shape as <see cref="WalletOwnsOnDisk"/>, so a desk that asks on every
    /// summon costs one <c>FileInfo</c> and not one parse.</summary>
    private static string? EmiOutfitOnDisk()
    {
        var path = Path.Combine(App.UserDataPath, "arcademy_meta.json");
        long stamp;
        try
        {
            var info = new FileInfo(path);
            stamp = info.Exists ? (info.LastWriteTimeUtc.Ticks ^ info.Length) : 0L;
        }
        catch { stamp = 0L; }

        lock (_outfitDiskLock)
        {
            if (_outfitDiskStamp != stamp)
            {
                _outfitDiskValue = ReadArmedOutfit(path);
                _outfitDiskStamp = stamp;
            }
            return _outfitDiskValue;
        }
    }

    private static string? ReadArmedOutfit(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            return (string?)JObject.Parse(File.ReadAllText(path))[EmiOutfitKey];
        }
        catch (Exception ex)
        {
            App.Logger?.Debug("ArcademyHost.ReadArmedOutfit: {E}", ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Tell the desk widget to re-read what the Locker armed. Cheap and safe from anywhere: it is a
    /// no-op when she has never been summoned (the window is built on the first summon and lives
    /// until the app closes), and the window itself re-reads on the way in, so this is only ever the
    /// LIVE half - the swap that lands while she is already out.
    /// </summary>
    private static void PushEmiOutfitToDesk()
    {
        try { App.EmiDesk?.Window?.RefreshOutfit(); }
        catch (Exception ex) { App.Logger?.Debug("ArcademyHost.PushEmiOutfitToDesk: {E}", ex.Message); }
    }

    /// <summary>
    /// THE STUDENT ID's identity block. Four fields, and the whole body is wrapped for the same
    /// reason <see cref="BuildSubject"/> is: a throw here would kill <c>init</c>, and a page that
    /// never gets <c>init</c> never boots. A failure costs the card its name and its photo, never
    /// the Arcademy.
    ///
    /// <para>THE NAME IS THE CCP NICKNAME, never the Discord handle (owner ruling 5).
    /// <see cref="App.UserDisplayName"/> already resolves the offline username, the unified display
    /// name and the provider names in the app's own order; blank reads as null and the page draws
    /// its own "Student".</para>
    ///
    /// <para>THE PHOTO IS THE <c>discord</c> RUNG (owner ruling 1). One switch: the picture on the
    /// card is the picture the campus ghost wears, so <c>avatarUrl</c> is non-null only at that rung
    /// AND with Discord actually linked AND with bytes already cached. Anything less is null and the
    /// page draws its PHOTO PENDING plate. The cache is read here synchronously and never fetches -
    /// the fetch is <see cref="KickAvatarRefresh"/>, and when it lands it pushes a <c>profile</c>
    /// frame of its own.</para>
    /// </summary>
    private static object BuildProfile(Models.AppSettings? s)
    {
        try
        {
            var linked = App.Discord?.IsAuthenticated == true;
            var share = PresenceShare(s);
            var name = App.UserDisplayName;
            return new
            {
                name = string.IsNullOrWhiteSpace(name) ? null : name!.Trim(),
                avatarUrl = (linked && share == "discord") ? ArcademyAvatarCache.ReadDataUri() : null,
                discordLinked = linked,
                presenceShare = share,
            };
        }
        catch (Exception ex)
        {
            App.Logger?.Debug("ArcademyHost.BuildProfile: {E}", ex.Message);
            return new
            {
                name = (string?)null,
                avatarUrl = (string?)null,
                discordLinked = false,
                presenceShare = "off",
            };
        }
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

    /// <summary>
    /// THE SUBJECT'S OWN FILE (ANNEX-OS.md §5). Everything the annex terminal prints about the
    /// player, projected once at init so the OS downstairs never counts anything itself - the page
    /// only ever RENDERS a number this method already resolved.
    ///
    /// <para>THE WHOLE BODY IS WRAPPED. A throw inside <see cref="BuildInit"/> kills the entire init
    /// message, and a page that never gets init never boots - so a missing counter costs a row here,
    /// never the Arcademy. Every read is null-safe with a literal fallback for the same reason.</para>
    /// </summary>
    private static object BuildSubject(Models.AppSettings? s)
    {
        try
        {
            var (code, password) = SubjectCredentials(s);
            return new
            {
                // Deterministic theatre, not security: the code and the password are a stable
                // derivation of this install's identity so the paper in the binder and the
                // terminal agree. Nothing is protected by them - the note with the password is
                // pinned next to the screen on purpose.
                code,
                password,
                // "yyyy-MM-dd" or null on installs that predate the field; null passes through and
                // the OS drops the row rather than inventing a date.
                date = s?.InstallDate,
                level = s?.PlayerLevel ?? 0,
                // The MONOTONIC lifetime ledger. GetTotalXP() is season/curve dependent and would
                // make "experience, lifetime" fall when a season rolls.
                xp = App.Achievements?.Progress?.TotalXPEarned ?? 0,
                minutes = s?.TotalConditioningMinutes ?? 0,
                videoMinutes = App.Achievements?.Progress?.TotalVideoMinutes ?? 0,
                spiralMinutes = App.Achievements?.Progress?.TotalSpiralMinutes ?? 0,
                // The NO-ARGUMENT overload only. The free (false) and patron (true) overloads are
                // deliberately separate counts and must never be summed into one number
                // (AchievementService.cs:1074) - this one is already the whole shelf.
                achievements = App.Achievements?.GetUnlockedCount() ?? 0,
                appStreak = s?.CurrentStreak ?? 0,
                appStreakBest = s?.HighestStreak ?? 0,
                // AppSettings.TotalSessions is a SECOND, independent counter of the same idea.
                // The progress ledger is the one chosen here; do not project both, a file that
                // prints two different session counts reads as a bug in the file.
                sessionsStarted = App.Achievements?.Progress?.TotalSessionsStarted ?? 0,
                flashes = App.Achievements?.Progress?.TotalFlashImages ?? 0,
                bubbles = App.Achievements?.Progress?.TotalBubblesPopped ?? 0,
                lockCards = App.Achievements?.Progress?.TotalLockCardsCompleted ?? 0,
                keywordTriggers = App.Achievements?.Progress?.KeywordTriggersFired ?? 0,
            };
        }
        catch (Exception ex)
        {
            App.Logger?.Debug("ArcademyHost.BuildSubject: {E}", ex.Message);
            return new { };
        }
    }

    /// <summary>The salt behind the subject code and the terminal password. DETERMINISTIC THEATRE,
    /// NOT SECURITY: it exists so one install always sees one code, and so two installs do not see
    /// the same one. Nothing is guarded by either string.</summary>
    private const string SubjectSalt = "ccp-annex-subject-2026";

    /// <summary>Sixteen dry desk nouns, indexed by two hash bytes. Sixteen exactly - the index is a
    /// nibble (<c>&amp; 0x0F</c>), so a seventeenth word would simply never be drawn.</summary>
    private static readonly string[] SubjectWords =
    {
        "paper", "drawer", "folder", "carbon", "staple", "filing", "copier", "binder",
        "archive", "cabinet", "printer", "lamp", "stamp", "memo", "index", "teal",
    };

    /// <summary>
    /// Derive this install's <c>XXXX-XXXX-XXXX</c> subject code and its <c>word-word-NN</c>
    /// terminal password from one HMAC. The identity is the account's unified id, or - offline, or
    /// never signed in - a stable string built from the install date and the offline name, so the
    /// code does not change under a player who never logged in.
    /// </summary>
    private static (string Code, string Password) SubjectCredentials(Models.AppSettings? s)
    {
        var identity = s?.UnifiedId;
        if (string.IsNullOrWhiteSpace(identity))
            identity = "offline:" + (s?.InstallDate ?? "") + ":" + (s?.OfflineUsername ?? "");
        using var mac = new HMACSHA256(Encoding.UTF8.GetBytes(SubjectSalt));
        var h = mac.ComputeHash(Encoding.UTF8.GetBytes(identity));
        var hex = Convert.ToHexString(h, 0, 6);   // 12 uppercase hex chars
        var code = hex.Substring(0, 4) + "-" + hex.Substring(4, 4) + "-" + hex.Substring(8, 4);
        var password = SubjectWords[h[6] & 0x0F] + "-" + SubjectWords[h[7] & 0x0F] + "-"
            + (h[8] % 100).ToString("00", CultureInfo.InvariantCulture);
        return (code, password);
    }

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
    /// THE SCHOOL'S OWN VOCABULARY, mirrored from <c>Resources/web/arcademy/core/vocab.js</c>
    /// (<c>HOUSE_WORDS</c>). The page owns the list and only ever falls back to it when the
    /// player's <c>SubliminalPool</c> is empty; this copy exists so the host can answer WHICH of
    /// those words has a clip on disk without the page probing for files. The spelling is LOCKED
    /// on both sides: the filename is the word lowercased with spaces turned into underscores
    /// ("LET GO" -> <c>let_go.mp3</c>), so renaming a word here or there orphans a clip.
    /// </summary>
    private static readonly string[] HouseWords =
    {
        "FOCUS", "RELAX", "BREATHE", "LET GO", "SINK", "DEEPER", "DRIFT", "BLANK",
        "EMPTY", "LISTEN", "OBEY", "GOOD", "AGAIN", "STAY", "SMILE", "SOFTER",
        "MELT", "CALM", "DROP", "TRUST", "QUIET", "OPEN", "GIVE IN", "FLOAT",
    };

    /// <summary>The file stem a house word's clip ships under: lowercase, spaces to underscores.</summary>
    private static string HouseSlug(string word)
        => (word ?? string.Empty).Trim().ToLowerInvariant().Replace(' ', '_');

    /// <summary>
    /// Which house-word clips are on disk. Cached for the process for the same reason
    /// <see cref="BuildSfxSamples"/> is: the folder ships with the build and cannot change under
    /// a running app. <c>null</c> = not scanned yet, never "none found".
    /// </summary>
    private static string[]? _sublimStems;

    private static string[] SublimStems()
    {
        if (_sublimStems != null) return _sublimStems;
        try
        {
            var dir = Path.Combine(AppContext.BaseDirectory, "Resources", "web", "arcademy", "assets", "sublim");
            if (!Directory.Exists(dir)) return _sublimStems = Array.Empty<string>();
            _sublimStems = Directory.EnumerateFiles(dir, "*.mp3", SearchOption.TopDirectoryOnly)
                .Select(f => Path.GetFileNameWithoutExtension(f) ?? string.Empty)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToArray();
            return _sublimStems;
        }
        catch (Exception ex)
        {
            App.Logger?.Debug("ArcademyHost.SublimStems: {E}", ex.Message);
            return _sublimStems = Array.Empty<string>();
        }
    }

    /// <summary>
    /// The house vocabulary WITH its whisper clips: <c>[{text, audio}]</c>, one row per house word
    /// that actually has an mp3 beside the page. The page uses these only on a day it fell back to
    /// the house list (an empty <c>SubliminalPool</c>), and it filters them down to the words it
    /// dealt, so <c>ctx.triggers</c> always describes <c>ctx.words</c>.
    /// <para>
    /// The url is RELATIVE to the page (<c>./assets/sublim/&lt;slug&gt;.mp3</c>), exactly like the
    /// sample door's cues: the whole arcademy folder is served off one origin, so the element that
    /// plays it stays same-origin and can feed the mixer's bus graph instead of slipping it.
    /// </para>
    /// <para>
    /// GATED ON <see cref="Models.AppSettings.SubAudioAudible"/> the same way
    /// <see cref="BuildTriggers"/> is: with the app-wide whisper mute on, every row is text-only
    /// and the page has nothing it could play. The rows are still listed - the host says what
    /// EXISTS (trap 86); the flag says whether it may be heard. An empty or missing folder is an
    /// empty list and the school simply flashes its words in silence.
    /// </para>
    /// </summary>
    private static object[] BuildHouseTriggers()
    {
        try
        {
            var stems = SublimStems();
            if (stems.Length == 0) return Array.Empty<object>();
            var have = new HashSet<string>(stems, StringComparer.OrdinalIgnoreCase);
            var audible = App.Settings?.Current?.SubAudioAudible == true;
            var rows = new List<object>(HouseWords.Length);
            foreach (var word in HouseWords)
            {
                var slug = HouseSlug(word);
                if (slug.Length == 0 || !have.Contains(slug)) continue;
                rows.Add(audible
                    ? (object)new { text = word, audio = "./assets/sublim/" + slug + ".mp3" }
                    : new { text = word, audio = (string?)null });
            }
            return rows.ToArray();
        }
        catch (Exception ex)
        {
            App.Logger?.Debug("ArcademyHost.BuildHouseTriggers: {E}", ex.Message);
            return Array.Empty<object>();
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

        // An ANIMATED webp is a loop wearing a still's extension (ccp-bugs#1086): it is sampled
        // out of `stills` above and moved over here, so the page's budgets meet it as what it
        // costs. Only the SAMPLED files are probed - a header read per file across a whole
        // library is exactly the boot cost this manifest exists to avoid.
        var gifUrls = Sample(gifs, rng, LocalAssetSample).Select(ToAssetsUrl).ToList();
        var stillUrls = new List<string>();
        foreach (var file in Sample(stills, rng, LocalAssetSample))
        {
            // The hint travels with the URL, not with the bucket: provider/index.js
            // resolveManifest() FLATTENS gifs+stills into one list and re-derives every entry's
            // kind from its url, so a bucket move on its own would be silently undone.
            if (IsAnimatedLocalImage(file)) gifUrls.Add(ToAssetsUrl(file) + AnimatedImageHint);
            else stillUrls.Add(ToAssetsUrl(file));
        }

        return new JObject
        {
            ["gifs"] = new JArray(gifUrls.Cast<object>().ToArray()),
            ["stills"] = new JArray(stillUrls.Cast<object>().ToArray()),
        };
    }

    /// <summary>
    /// THE ANIMATED-WEBP HINT (ccp-bugs#1086). Appended to a <c>ccp.assets</c> url whose file the
    /// header probe says ANIMATES, and read by every page-side budget that decides "does this url
    /// cost a decoder and an animation clock" with <c>/\.gif(\?|#|$)/</c> - Lost &amp; Found's live
    /// window, its frame governor, and the same test in anomaly / deja-vu / instant-recall.
    ///
    /// <para>Why a fragment and not a query: the URL Standard drops the fragment before the fetch,
    /// so <c>a.webp#.gif</c> loads byte-for-byte the same file out of the virtual-host mapping while
    /// every one of those regexes reads it. That is not a new convention - it is the one
    /// <c>provider/index.js hintedPileUrl()</c> already uses to tell an extension-less <c>blob:</c>
    /// row's kind (<c>#.mp4</c>). The MIME the host puts on the wire stays the true one.</para>
    ///
    /// <para>WHY IT IS NEEDED AT ALL: the page cannot answer this question. Animation lives in a
    /// webp's VP8X container flag, not in its name, so a url alone can only guess - which is why
    /// the pre-fix code classed every webp as a still and a library of animated ones dealt ~170
    /// simultaneous main-thread decoders onto one wall (the reported symptom: Lost &amp; Found
    /// lagging so hard that a click landed on the tile that had drifted into place). Only the
    /// desktop host has the bytes, so only the desktop host can say.</para>
    /// </summary>
    internal const string AnimatedImageHint = "#.gif";

    /// <summary>
    /// Does this local image ANIMATE despite carrying a still's extension? True only for a
    /// <c>.webp</c> whose container header sets the animation flag; every other extension already
    /// tells the truth (a <c>.gif</c> is dealt as a loop by name, a <c>.png</c>/<c>.jpg</c> cannot
    /// animate). Cheap - <see cref="AnimatedWebp.IsAnimated"/> is a 21-byte header read - but still
    /// a file open, so call it on a SAMPLED slice, never over a whole library walk.
    /// </summary>
    internal static bool IsAnimatedLocalImage(string file)
        => Path.GetExtension(file).Equals(".webp", StringComparison.OrdinalIgnoreCase)
           && AnimatedWebp.IsAnimated(file);

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
        // The one letter with punctuation in it. 'grade_s+' is not a key shape a lexicon row
        // (or a mod table) can carry, so core/lexicon.js spells it out and so does this.
        ["grade_splus"] = "S+",
        ["attendance"] = "Attendance",
        ["perfect_attendance"] = "Perfect Attendance",
        // ---- the Prize Counter (economy, 2026-08-26) -----------------------------------
        // Every row here is the front desk talking: warm, a bit scruffy, never a form letter.
        // These keys are the ONES THE PAGE ACTUALLY ASKS FOR: core/lexicon.js carries the same
        // set as its offline fallback and shell/lever.js + shell/prizecounter.js look them up
        // by exactly these names. A row nothing calls is a row a mod would re-voice for
        // nothing, so the list is kept to what is consumed and the harness checks both ways.
        // ---- the door on the west wall
        ["campus_room_prizes"] = "Prize Counter",
        ["campus_prizes_status"] = "Open late",
        ["campus_desc_prizes"] = "Tickets on the shelf, tokens in the case. Somebody is always restocking.",
        // ---- the two currencies, as they read on a chip
        ["wallet_tickets"] = "Tickets",
        ["wallet_tokens"] = "Tokens",
        // ---- the counter itself
        ["prize_counter_title"] = "Prize Counter",
        ["prize_counter_sub"] = "Tickets on the shelf, tokens in the case",
        ["prize_shelf"] = "Ticket Shelf",
        ["prize_shelf_hint"] = "Every graded class pays tickets. This is where they go.",
        ["prize_case"] = "Token Case",
        ["prize_case_hint"] = "Tokens only. Your first S of the day drops one in the tray.",
        ["prize_you_have"] = "On you",
        ["prize_owned"] = "Yours",
        ["prize_held"] = "Holding",
        ["prize_buy"] = "Trade",
        ["prize_soon"] = "Arriving soon",
        ["prize_wait"] = "Asking the counter",
        // What the counter says back. The refusals line up one for one with the reason strings on
        // `wallet-result` (unknown / poor / owned / full / locked, plus `offline` and `busy` since
        // the wallet moved to the account); the last two are what the page says on its own when no
        // answer came back at all.
        ["prize_bought"] = "Wrapped up and yours.",
        ["prize_poor"] = "Not quite enough on you for that one yet.",
        ["prize_owned_msg"] = "You have that one already.",
        ["prize_full"] = "Your pockets are full of those. Use one first.",
        ["prize_locked_msg"] = "That one stays in the case for now.",
        ["prize_unknown"] = "The counter does not know that one. Odd.",
        // The shared wallet's two new refusals. The till is on the account now, so a purchase with
        // no line out is a thing the counter cannot do rather than a thing you cannot afford, and
        // the account lock is another of your own devices at the same drawer. Both are worded so
        // they never sound like a scolding and never mention a network.
        ["prize_offline"] = "The counter cannot reach the bank right now. Nothing was charged.",
        ["prize_busy"] = "Somebody is already at the drawer. Give it a second and ask again.",
        ["prize_quiet"] = "The counter went quiet on that one. Try again in a moment.",
        ["prize_empty"] = "Shelf is bare tonight. Come back when the truck has been.",
        // THE ALMOST and THE CHARGE-HOLD (shell/prizecounter.js, wave 0828). `prize_short` is a
        // bare word: the page builds "Almost, 20 short" by concatenation the way it already builds
        // "Holding 2/3", so no translated string has a number baked into it.
        ["pc_verb_almost"] = "Almost",
        ["prize_short"] = "short",
        ["prize_hold_hint"] = "Hold it down to trade that one.",
        ["prize_hold_aria"] = "Hold to trade",
        // Tonight's hot room, painted from the seeded draw `init` already handed down.
        ["prize_payday_label"] = "Hot room tonight",
        ["prize_payday_2"] = "is paying double",
        ["prize_payday_5"] = "is paying five times over",
        // THE ANTECHAMBER (shell/prizebooth.js). The painted booth you walk up to before
        // the shelf opens: the lit window, the tray on the sill, and what a counter with
        // its shutter down says. `prize_closed` is one word because it is stencilled on
        // the shutter, not spoken.
        ["prize_booth_window"] = "The service window",
        ["prize_booth_tray"] = "The ticket tray",
        ["prize_closed"] = "Closed",
        ["prize_closed_line"] = "The shutter is down and the sign above it has been switched off at the wall.",
        ["prize_no_payday"] = "No room is paying over the odds tonight. Every graded class still pays tickets.",
        ["settings_classes_head"] = "Classes",
        ["campus_desc_prizes_shut"] = "Shutter down over the window, parcels still stacked behind it. Back another night.",
        // The eight rows on the shelf. Names and blurbs both, so a page with a partial mod
        // table still reads (the catalog also ships the neutral English on the wire). Keyed
        // EXACTLY as ArcademyEconomy.Catalog's NameKey/BlurbKey - a missing row here is a blank
        // label on the shelf, so the harness checks the two lists against each other.
        ["prize_id_frame_gold"] = "Gold Pinstripe Frame",
        ["prize_id_frame_gold_blurb"] = "A thin gold pinstripe around your ID photo, for being seen.",
        ["prize_id_frame_navy"] = "Navy Varsity Frame",
        ["prize_id_frame_navy_blurb"] = "Deep navy with a varsity edge, like the old team photos.",
        ["prize_confetti_stamp"] = "Confetti Stamp",
        ["prize_confetti_stamp_blurb"] = "Your stamp lands in a little burst of paper now, every time.",
        ["prize_late_slip"] = "Tardy Slip",
        ["prize_late_slip_blurb"] = "Hand one in and the night you missed is filed as excused. Two on the desk, no more.",
        ["prize_honors_lever"] = "Honors Lever",
        ["prize_honors_lever_blurb"] = "Unbolts the third notch, which is where the S+ nights live.",
        ["prize_free_swim_key"] = "Free Swim Key",
        ["prize_free_swim_key_blurb"] = "Opens Free Swim on every room you are in, card or no card.",
        ["prize_de_5x5"] = "The Wide Board",
        ["prize_de_5x5_blurb"] = "Adds the roomy 5x5 board to The Deep End, for a gentler soak.",
        ["prize_jukebox"] = "Jukebox",
        ["prize_jukebox_blurb"] = "The slot is dressed and the case is empty. It is on the truck.",
        // THE RESTOCK (2026-08-26) plus THE LOCKER's three outfits (2026-08-28). Fourteen more
        // rows over three waves - the shelf projection hides anything above
        // ArcademyEconomy.CurrentWave, but the LEXICON carries all fourteen regardless: a lexicon
        // row costs nothing until something asks for it, and shipping the next wave should be one
        // const bump, not a second trip through nine language files.
        // Copy is the contract's, verbatim. The restock's own rows all sit inside the 96-char
        // bar; the lab coat's blurb runs to 106 because that is the line the owner signed off,
        // and nothing measures a blurb - the card wraps it.
        ["prize_away_colors"] = "AWAY COLORS",
        ["prize_away_colors_blurb"] = "Alternate kit for your little walker. Same you, sharper stripes.",
        ["prize_sparkler_steps"] = "SPARKLER STEPS",
        ["prize_sparkler_steps_blurb"] = "A trail of little sparks wherever you walk. The janitor has given up complaining.",
        ["prize_brass_bell"] = "THE BRASS BELL",
        ["prize_brass_bell_blurb"] = "The old bell from the storage room takes over. Rings a little warmer than the new one.",
        ["prize_emi_desk_toy"] = "EMI'S DESK TOY",
        ["prize_emi_desk_toy_blurb"] = "A little something for her desk. She'll fidget with it and pretend she doesn't love it.",
        ["prize_poster_drop_1"] = "POSTER DROP NO 1",
        ["prize_poster_drop_1_blurb"] = "Fresh prints for the corkboard, motivational in a way we can't quite explain.",
        ["prize_pa_pack"] = "PA ANNOUNCER",
        ["prize_pa_pack_blurb"] = "The morning announcements get a voice. She mostly reads the schedule, mostly.",
        ["prize_theme_drone"] = "DRONE PROTOCOL",
        ["prize_theme_drone_blurb"] = "Somebody left a strange cartridge in the AV room and now the campus runs green. We like it.",
        ["prize_emi_labcoat"] = "LAB COAT",
        ["prize_emi_labcoat_blurb"] = "White coat, pocket protector, the clipboard she never writes on. She looks like she is about to grade you.",
        ["prize_emi_cheer"] = "CHEER UNIFORM",
        ["prize_emi_cheer_blurb"] = "Navy and pink, pleats and all. The pom-poms are not optional and neither is the chant.",
        ["prize_emi_swim"] = "SWIM TEAM",
        ["prize_emi_swim_blurb"] = "Lane four, goggles up. Free Swim was always going to end up here.",
        ["prize_ghost_walk"] = "GHOST WALK",
        ["prize_ghost_walk_blurb"] = "Your walker goes see-through with a soft afterimage. Spooky in a fun way, we checked.",
        ["prize_theme_snowday"] = "SNOW DAY",
        ["prize_theme_snowday_blurb"] = "Frost on the windows, snow in the courtyard, everything soft and blue. Classes run anyway.",
        ["prize_emi_varsity"] = "EMI: VARSITY JACKET",
        ["prize_emi_varsity_blurb"] = "She found it in lost and found and it fits perfectly. Every one of her poses, re-dressed.",
        ["prize_tube_midnight"] = "TUBE GLASS: MIDNIGHT",
        ["prize_tube_midnight_blurb"] = "A darker glass for the tube back home. It ships to the whole app, not just the school.",
        // ---- the Extra Credit lever ----------------------------------------------------
        // shell/lever.js owns the words on BOTH class-start surfaces (the door card and the
        // painted room's apron), so every rung is one key and one locked line, no more.
        ["lever_title"] = "Extra Credit",
        ["lever_standard"] = "Standard",
        ["lever_extra"] = "Extra Credit",
        ["lever_honors"] = "Honors",
        ["lever_standard_hint"] = "Play it straight. Tickets pay the usual.",
        ["lever_extra_hint"] = "Half again the tickets, and it asks more of you.",
        ["lever_honors_hint"] = "Double tickets, and the only road to an S plus.",
        ["lever_extra_locked"] = "Earn an A on anything and this one wakes up.",
        ["lever_honors_locked"] = "The counter sells this one for a token.",
        // ---- the till, as it reads after a class ---------------------------------------
        // The payday's own words live on the counter (prize_payday_*); what lands here is the
        // report card's payout beat and the one purchase a player never watches being spent.
        ["free_swim_key_hint"] = "Your key opens this one for a practice run. Nothing counts, nothing costs.",
        ["payout_tickets"] = "Tickets",
        ["payout_token_minted"] = "A token dropped in the tray. That is your one for today.",
        ["late_slip_used"] = "A tardy slip was handed in for you. Your streak never noticed.",
        // THE ONE SMALL BUTTON under the jeopardy line (Deck V, the Rake). `{name}` is filled
        // from the catalog row itself, so a mod that renames the slip renames the offer too.
        ["rake_slip_offer"] = "The counter sells a {name}.",
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
        // THE TIME CAPSULE (Resources/web/arcademy/shell/capsule.js). The plaque
        // line is TWO clause rows joined with one space in the page: the whole
        // sentence is 102 characters and MergeModTable drops any mod string over
        // 96, so a single row could never be re-voiced (trap 26).
        ["campus_desc_trophy"] = "One exhibit under glass. The school keeps its own first night in here.",
        ["capsule_on_view"] = "On view",
        ["capsule_title"] = "Time Capsule",
        ["capsule_line_2026_02_a"] = "The first dashboard. February 2026.",
        ["capsule_line_2026_02_b"] = "Everything was pink and the DROP button was the size of a doormat.",
        ["capsule_footer"] = "Sealed by the Registrar. Opened at thirty nights.",
        ["capsule_sealed_tag"] = "opens at 30 nights",
        ["capsule_sealed_hint"] = "The case is wrapped and taped. The tag has a number on it.",
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
        // THE DISCORD LINE (the Activity wave, 2026-08-28). `{cmd}` is filled by
        // the page from games/registry.js DISCORD_COMMAND, never by the host.
        ["punchcard_unlocked_discord"] = "Even in Discord: type {cmd} in the CCP server to play it anytime.",
        ["launch_card_locked"] = "That card is not complete yet. Fill it first.",
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
        // ---- the office as a ROOM (shell/recordsroom.js, 0825) --------------------------
        // Four things you can touch in the painted office, plus the chrome of its two
        // close-ups. "records_book" is deliberate and so is its value: the other word for
        // that volume is a register word, and the register is barred from every
        // user-facing string in this school.
        ["records_tray"] = "The card tray",
        ["records_board"] = "The noticeboard",
        ["records_book"] = "The book",
        ["records_storeroom"] = "The storeroom",
        ["records_fresh"] = "New",
        ["records_close_panel"] = "Put the cards back",
        ["records_book_next"] = "Next page",
        ["records_book_prev"] = "Back a page",
        ["records_book_ch_school"] = "The Arcademy",
        ["records_book_ch_rules"] = "House rules",
        ["records_book_ch_tips"] = "Tips",
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
        ["lf_rebrief"] = "New target. Memorize her.",
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
        ["ic_file_tab"] = "field notes",
        ["ic_file_stamp"] = "for you",
        ["ic_file_stamp_after"] = "subject",
        ["ic_file_note_head"] = "note to file",
        ["ic_file_note_1"] = "i'm not supposed to be in the tube so if anyone asks this note fell out of a bubble on its own",
        ["ic_file_note_2"] = "hasta la vista, baby. i wrote it down so i'd get it right this time. did i get it right",
        ["ic_file_note_3"] = "saved you the slow bubble again, the one that takes its time, so don't tell the others",
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
        // ---- shell/room.js: the proctor's rail along the bottom of a room scene
        ["room_options"] = "Class options",
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
        ["ec_msg_silent"] = "No whispers tonight - listen for the tones.",
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
        ["ir_payout"] = "LOCKED IN",
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
        ["sort_vet_more"] = "Fetching more cards",
        ["sort_vetting"] = "Checking your cards",
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

        // EMI ASKS: the Send button on the one question with a keyboard (a14,
        // "what do i call you?"). The ONLY display string EMI renders - her
        // questions, chips and reactions are verbatim content and never pass
        // through the lexicon. shell/shell.js resolves this one and hands the
        // answer to mountEmi. Her voice is lowercase, so this row is too.
        ["emi_ask_send"] = "send",
        // EMI'S DESK TOY (COUNTER STOCK). The three lines she has about it, resolved by
        // the shell (same road as emi_ask_send - she has never imported the lexicon).
        ["emi_toy_1"] = "Don't wind it too far. It gets ideas.",
        ["emi_toy_2"] = "It's not a toy, it's office equipment. Okay, it's a toy.",
        ["emi_toy_3"] = "She spins when I do good work. We have a system.",
        // CAMPUS LOOK (COUNTER STOCK). The settings row's own two words; the themes
        // themselves are named by their prize rows (prize_theme_drone, prize_theme_snowday).
        ["opt_theme_head"] = "Campus look",
        ["opt_theme_standard"] = "The usual",

        // THE PHANTOM POST chrome (shell/mail.js, mailbox.js, corkboard.js,
        // bugle.js). Copy of core/lexicon.js's block - copy the values, do not
        // re-word them (the IC_LEX rule). Letter bodies, notices and newspaper
        // copy are content, never lexicon, and are deliberately absent here.
        ["mail_kicker"] = "Mail",
        ["mail_title"] = "The Mail Box",
        ["mail_chip_label"] = "Mail",
        ["mail_unread"] = "unread",
        ["mail_all_read"] = "read",
        ["mail_empty"] = "Nothing in the box yet.",
        ["mail_pick"] = "Pick an envelope to read it.",
        ["mail_delivered"] = "Delivered",
        ["mail_new"] = "New",
        ["mail_close"] = "Close",
        ["board_kicker"] = "Pinned up",
        ["board_title"] = "Noticeboard",
        ["board_prop_label"] = "Noticeboard",
        ["board_lede"] = "What is up on the wall tonight. Some of it stays. Most of it does not.",
        ["board_empty"] = "Nothing pinned up tonight.",
        ["board_rotates"] = "The wall gets sorted through most days. What is pinned flat stays put.",
        ["board_kind_notice"] = "Notice",
        ["board_kind_flyer"] = "Flyer",
        ["board_kind_minutes"] = "Minutes",
        ["board_kind_poster"] = "Poster",
        ["board_note_open"] = "Take this one down and read it",
        ["board_note_close"] = "Put it back on the wall",
        ["bugle_issue"] = "Issue",
        ["bugle_page"] = "Page",
        ["bugle_pages"] = "Pages",
        ["bugle_prev"] = "Previous page",
        ["bugle_next"] = "Next page",
        ["bugle_comics"] = "Comics",
        ["bugle_comics_held"] = "Picture held at the printer. Described below.",
        ["bugle_empty"] = "Nothing set for this page.",
        ["bugle_prop_label"] = "The paper",
        // ---- wet ink (THE SEEP, tell 09) -----------------------------------------------
        // The one COPY tell: a warm maintenance note on the noticeboard that reads as a
        // shrug the first time and as a confession after the reveal. Stored as CLAUSE ROWS
        // joined with one space (the PAPERS pattern) so every row clears MergeModTable's
        // 96-char cap and a mod can still re-voice the whole note. Do not merge the rows.
        // Rows 3 and 4 carry the seed clause - CANON, the school's own cover story.
        ["seep_wetink_title"] = "FROM THE FRONT DESK",
        ["seep_wetink_1"] = "Couple of things this week: the water fountain by 103 is fixed, you're welcome,",
        ["seep_wetink_2"] = "and whoever keeps winning the gate raffle please come collect your pencils.",
        ["seep_wetink_3"] = "Also if you see light under the Records door after closing,",
        ["seep_wetink_4"] = "that's just the old wiring acting up again, Marco says he'll swap the breaker",
        ["seep_wetink_5"] = "when the part shows up. Be good.",
        ["seep_wetink_sig"] = "The front desk.",
        // ---- the annex (ANNEX-OS.md) - fence words legal on these rows only ----
        ["annex_cam"] = "CAM",
        ["annex_rec"] = "REC",
        ["annex_cam_gate"] = "MAIN GATE",
        ["annex_lap_title"] = "RECORDS ANNEX",
        ["annex_lap_locked"] = "TERMINAL LOCKED",
        ["annex_lap_prompt"] = "AWAITING KEY",
        ["annex_door"] = "A wall panel, ajar",
        ["annex_room_label"] = "The Records Annex",
        ["annex_back"] = "step back",
        ["annex_hot_monitors"] = "the monitors",
        ["annex_hot_shelf"] = "the shelf",
        ["annex_hot_desk"] = "the desk",
        ["annex_hot_door"] = "the stairs",
        ["annex_hot_folder"] = "the folder",
        ["annex_hot_binder"] = "FIELD DATA",
        ["annex_hot_laptop"] = "the laptop",
        ["annex_paper_close"] = "put it down",
        ["annex_stamp_ongoing"] = "ONGOING",
        ["annex_page_prev"] = "previous page",
        ["annex_page_next"] = "next page",
        ["annex_os_label"] = "Annex terminal",
        ["annex_os_boot_1"] = "RECORDS ANNEX / UNIT TERMINAL",
        ["annex_os_boot_2"] = "memory check: fine, thanks for asking",
        ["annex_os_boot_3"] = "feed wall link: up",
        ["annex_os_boot_4"] = "archive index: 26 files, 5 drawers",
        ["annex_os_login_sub"] = "authorised staff. there is no other kind of staff.",
        ["annex_os_pass"] = "password",
        ["annex_os_enter"] = "log in",
        ["annex_os_wrong"] = "no. the note is right there.",
        ["annex_os_note"] = "PW: CYBER-PUNK",
        ["annex_os_files"] = "FILES",
        ["annex_os_registry"] = "REGISTRY",
        ["annex_os_search"] = "SUBJECT SEARCH",
        ["annex_os_term"] = "TERMINAL",
        ["annex_os_close"] = "close",
        ["annex_os_live"] = "LIVE",
        ["annex_os_archive"] = "ARCHIVE",
        ["annex_os_linkdown"] = "LINK DOWN",
        ["annex_os_linkwait"] = "link…",
        ["annex_os_retry"] = "retry",
        ["annex_os_room"] = "room",
        ["annex_os_enrolled"] = "enrolled",
        ["annex_os_completed"] = "completed",
        ["annex_os_all"] = "all subjects",
        ["annex_os_redacted"] = "withheld",
        ["annex_os_code"] = "subject code",
        ["annex_os_open_file"] = "open file",
        ["annex_os_notfound"] = "that code is not on file. check the paper in the binder.",
        ["annex_os_search_slip"] = "codes are issued at intake. your sheet is in the FIELD DATA binder, on the shelf. the reader on the side takes student cards too, if you have yours on you.",
        ["annex_os_badge"] = "insert student ID",
        ["annex_os_reader"] = "card reader",
        ["annex_os_reading"] = "reading card...",
        ["annex_os_file_title"] = "SUBJECT FILE",
        ["annex_os_ongoing"] = "ONGOING",
        // ---- the reading room (waves 1-3, 2026-08-25): explorer, papers, drawer ----
        ["annex_os_bin"] = "RECYCLE",
        ["annex_os_bin_empty"] = "nothing in here.",
        ["annex_os_skim_on"] = "skim: on",
        ["annex_os_skim_off"] = "skim: off",
        ["annex_os_read_n"] = "read {n}/{total}",
        ["annex_os_withheld_n"] = "withheld {n}",
        ["annex_os_new"] = "new",
        ["annex_os_newtag"] = "NEW",
        ["annex_os_tldr"] = "TL;DR",
        ["annex_os_prev"] = "prev",
        ["annex_os_next"] = "next",
        ["annex_os_pos"] = "{i} of {n}",
        ["annex_os_chart"] = "chart",
        ["annex_os_archivefig"] = "archive figure",
        ["annex_os_audio"] = "audio log",
        ["annex_os_play"] = "play",
        ["annex_os_scan"] = "scan",
        ["annex_os_pick"] = "pick a file.",
        ["annex_os_back"] = "back",
        ["annex_os_closed"] = "CLOSED",
        ["annex_tab_projects"] = "PROJECTS",
        ["annex_tab_fielddata"] = "FIELD DATA",
        ["annex_tab_misc"] = "MISC",
        ["annex_tab_unread"] = "unread",
        ["annex_row_terminal"] = "on the terminal",
        ["annex_row_withheld"] = "withheld",
        ["annex_turn_over"] = "turn over",
        ["annex_turn_back"] = "turn back",
        ["annex_page_of"] = "{i} / {n}",
        ["annex_drawer_label"] = "the binder drawer",
        ["annex_drawer_close"] = "put it back",
        ["annex_f_general"] = "GENERAL",
        ["annex_f_since"] = "on record since",
        ["annex_f_level"] = "level",
        ["annex_f_xp"] = "experience, lifetime",
        ["annex_f_minutes"] = "supervised minutes",
        ["annex_f_video"] = "screening minutes",
        ["annex_f_spiral"] = "focus minutes",
        ["annex_f_ach"] = "citations on file",
        ["annex_f_attend"] = "ATTENDANCE",
        ["annex_f_streak"] = "attendance streak",
        ["annex_f_perfect"] = "perfect nights",
        ["annex_f_cards"] = "cards mastered",
        ["annex_f_appstreak"] = "reporting streak",
        ["annex_f_appbest"] = "reporting streak, best",
        ["annex_f_sessions"] = "sessions opened",
        ["annex_f_devices"] = "DEVICES",
        ["annex_f_flashes"] = "exposures delivered",
        ["annex_f_bubbles"] = "targets cleared",
        ["annex_f_lockcards"] = "sentences typed",
        ["annex_f_triggers"] = "cue firings",
        ["annex_f_unit"] = "UNIT OBSERVATION",
        ["annex_f_pets"] = "pets received",
        ["annex_f_drags"] = "relocations",
        ["annex_f_flings"] = "ejections",
        ["annex_f_hides"] = "dismissals",
        ["annex_f_restores"] = "recalls from dock",
        ["annex_f_lines"] = "lines delivered",
        ["annex_f_emisessions"] = "sessions observed",
        ["annex_f_emidays"] = "days observed",
        ["annex_f_hours"] = "hours observed",
        ["campus_annex"] = "Records Annex",
        ["campus_annex_status"] = "Stairs down",
        ["campus_desc_annex"] = "Under the office. The lights are off down there. The screens are not.",
        // ---- THE STUDENT ID (2026-08-25): the furniture card, its photo chip and the spotlight.
        // The chip rows are the SHORT face (under the photo); the id_photo_* rows are the long
        // ones the spotlight prints beside it. One switch behind both - the `discord` rung.
        ["student_id_title"] = "Student ID",
        ["id_photo_pending"] = "Photo pending",
        ["id_photo_on"] = "Discord photo on",
        ["id_photo_use"] = "Use my Discord photo",
        ["id_photo_link"] = "Link Discord for my photo",
        ["id_photo_waiting"] = "Waiting on Discord...",
        ["id_chip_on"] = "Photo on",
        ["id_chip_use"] = "Use Discord photo",
        ["id_chip_link"] = "Link Discord",
        ["id_chip_wait"] = "Waiting...",
        ["id_photo_hint_app"] = "Opens the Discord link-up in the app, then your photo goes on the card and on campus.",
        ["id_photo_hint_web"] = "Sends you to Connections to link Discord, then straight back here with the photo on.",
        ["id_photo_hint_off"] = "Your ghost on campus wears this photo too. Tap to take it down (your name stays).",
        ["id_photo_failed"] = "Discord did not pick up. Try again in a minute.",
        ["id_photo_day"] = "Photo day",
        ["id_no"] = "Student no.",
        ["id_no_temp"] = "temp",
        ["id_enrolled"] = "Enrolled",
        ["id_homeroom"] = "Homeroom",
        ["id_issued_at"] = "Issued at",
        ["id_front_desk"] = "Front desk",
        ["id_stat_semester"] = "Term",
        ["id_stat_streak"] = "Attendance streak",
        ["id_stat_perfect"] = "Perfect days",
        ["id_stat_stamps"] = "Stamps of 100",
        ["id_stat_best"] = "S days",
        ["id_year"] = "Year",
        ["id_grade_tier"] = "Grade tier",
        ["id_to_go"] = "{n} to go",
        ["id_reinked"] = "Re-inked",
        ["id_flip"] = "Tap the card to turn it over. Esc to put it back.",
        ["id_back_lost"] = "Lost it? Ask at the front desk. The second one costs you a stamp.",
        ["id_back_valid"] = "Good for as long as the lights are on.",
        ["id_records_line"] = "Records: {n} of {m} cards mastered",
        ["id_open_records"] = "Open Records",
        ["id_spot_close"] = "Close",
        // ---- the front office sheet (shell/settings.js): section titles, per-host blurbs, folds --
        ["settings_ceilings_head"] = "App ceilings",
        ["settings_ceilings_note_app"] = "Set in the app and shown here so you know what the school has to work with.",
        ["settings_device_head"] = "This device",
        ["settings_device_note"] = "Sound and motion for this browser, on this phone or PC. Nothing here leaves the device.",
        ["settings_master_volume"] = "Master volume",
        ["settings_master_volume_hint"] = "One dial over every sound the school makes.",
        ["settings_motion"] = "Motion",
        ["settings_motion_hint"] = "Reduced keeps the room still. Off cuts every animation the school can cut.",
        ["settings_motion_off"] = "Off",
        ["settings_motion_reduced"] = "Reduced",
        ["settings_motion_full"] = "Full",
        ["settings_distraction_head"] = "Distraction",
        ["settings_channels_head"] = "Channel ceilings",
        ["settings_channels_note"] = "A class may use less than these. Never more.",
        ["settings_sound_head"] = "Sound",
        ["settings_lessons_head"] = "Lessons",
        ["settings_mascot_head"] = "Mascot",
        ["emi_bubble_hold_label"] = "Speech bubble time",
        ["emi_bubble_hold_hint"] = "How long her lines stay up before she lets them go. Questions always wait for an answer.",
        ["emi_bubble_hold_quick"] = "Quick",
        ["emi_bubble_hold_normal"] = "Normal",
        ["emi_bubble_hold_long"] = "Long",
        ["emi_bubble_hold_extra"] = "Extra long",
        ["settings_game_nothing"] = "Nothing to configure - this class runs on the globals.",
        ["settings_sum_volume"] = "Volume {v}",
        ["settings_sum_motion"] = "Motion {v}",
        ["settings_sum_online_on"] = "Online on",
        ["settings_sum_online_off"] = "Online off",
        ["settings_sum_intensity"] = "Intensity {v}",
        ["settings_sum_guard"] = "Guard {v}",
        ["settings_sum_caps_all"] = "All at 100%",
        ["settings_sum_caps_low"] = "Lowest: {name} {v}",
        ["settings_sum_muted"] = "Muted",
        ["settings_sum_sound"] = "On{sep}Music {v}",
        ["settings_sum_tutorials_on"] = "Tutorials on",
        ["settings_sum_tutorials_off"] = "Tutorials skipped",
        ["settings_sum_board"] = "Board {v}",
        ["settings_sum_keys"] = "{n} keys",
        ["settings_sum_key_one"] = "1 key",
        ["settings_sum_nothing"] = "Nothing to set",
        // ---- THE ACCOUNT CHIP (2026-08-25): a host slot the web build fills (init.account); the desktop
        // never sends it, so these rows are here for the mirror law and mod skins only.
        ["account_menu"] = "Account",
        ["account_signed_in_as"] = "Signed in as",
        ["account_open_card"] = "Open my card",
        ["account_profile"] = "Profile",
        // THE FRONT GATE (2026-09-03): the third account verb, the way back to the CC Labs site.
        // Web/activity hosts only (they alone list "dashboard" in account.actions); mirrored here
        // so a mod can re-voice it, same as the other four rows.
        ["account_dashboard"] = "Front Gate",
        ["account_dashboard_hint"] = "back to CC Labs",
        ["account_sign_out"] = "Sign out",

        // THE LOCKER (2026-08-28): RM 004 + the booth arrival beat and the purchase toast verbs.
        // Mirrors of the page EN strings in core/lexicon.js so a mod lexicon.json can override them.
        ["booth_alley_hint"] = "The lit window is down at the end of the row.",
        ["booth_put_it_on"] = "Put it on",
        ["booth_hang_it"] = "Hang it up",
        // THE HOLDINGS TRAY (counter shortcut wave, 2026-08-30). The tray on the booth's sill
        // lists the consumables the player is carrying. There is no "2 of 3" row: the count is
        // built as "2/3" by concatenation, the way prize_held and prize_short already are.
        // The two passive lines are the honest half of the feature - the one consumable on the
        // shelf, late_slip, has no manual verb at all (ArcademyEconomy.ConsumeLateSlip burns it
        // inside the attendance credit), so the row says so instead of growing a dead button.
        // All four are well inside MergeModTable's 96-char skin cap (trap 26).
        ["booth_holdings"] = "What you are holding",
        ["booth_hold_none"] = "Nothing in your pockets tonight. The shelf is through the window.",
        ["booth_hold_late_slip"] = "It files itself the night you miss one. Nothing to press.",
        ["booth_hold_passive"] = "It spends itself the moment it is needed.",
        // The two wayfinding plates in the alley (shell/alleysign.js): the booth's
        // right-hand wall points at RM 004 and the Locker's left wall points back.
        // Rows of their own, not a re-use of the room cards, because a sign names a
        // DIRECTION and a card names a room. The sheet sets both in block caps.
        ["alley_sign_locker"] = "Locker room",
        ["alley_sign_locker_aria"] = "Go to the Locker room",
        ["alley_sign_counter"] = "Prize counter",
        ["alley_sign_counter_aria"] = "Go back to the Prize Counter",
        ["campus_room_locker"] = "The Locker",
        ["locker_sign"] = "Locker",
        ["locker_status"] = "Yours",
        ["locker_tip"] = "Your own door in the row. Everything you have won is behind it.",
        ["locker_kicker"] = "The Locker",
        ["locker_hot"] = "Your locker",
        ["locker_title"] = "The Locker",
        ["locker_sub"] = "Room 004. Nobody else has the combination.",
        ["locker_wear"] = "Wear",
        ["locker_card"] = "Card",
        ["locker_campus"] = "Campus",
        ["locker_desk"] = "Desk",
        ["locker_bag"] = "In your bag",
        ["locker_always"] = "Always on",
        ["locker_outfit_standard"] = "The usual",
        ["locker_outfit_varsity"] = "Varsity jacket",
        ["locker_outfit_labcoat"] = "Lab coat",
        ["locker_outfit_cheer"] = "Cheer uniform",
        ["locker_outfit_swim"] = "Swim team",
        ["locker_frame_plain"] = "Plain",
        ["locker_frame_gold"] = "Gold",
        ["locker_frame_navy"] = "Navy",
        ["locker_toy_auto"] = "Let the desk choose",
        ["locker_toy_spinner"] = "Spinner",
        ["locker_toy_globe"] = "Snow globe",
        ["locker_toy_lamp"] = "Lava lamp",
        ["locker_toy_beads"] = "Beads",
        ["locker_selected"] = "On",
        ["locker_held"] = "x{n}",
        ["locker_empty"] = "Nothing in here yet. The counter is one window up.",
        ["locker_more_at_counter"] = "{n} more at the counter",
        ["locker_ring_bell"] = "Ring it",
        ["locker_signpost"] = "Outfits, frames and campus looks live in The Locker now. RM 004.",
        ["locker_signpost_go"] = "Open The Locker",
        ["locker_unlock_hint"] = "{tok}2 at the counter",
        ["locker_open"] = "Open Locker",
        // ---- THE BACK ROOM (casino wing W1: the door and the chips) ------------------
        // The fifth window in the service alley. The desktop host ships these so a page
        // running against it says the same words the web build does: a key the host does
        // not carry falls back to English in silence and nobody ever sees the gap.
        // Every value is under the 96-character mod-skin cap and none of them prices
        // anything - chip costs ride the catalog, like every other row on that shelf.
        ["campus_room_backroom"] = "The Back Room",
        ["campus_desc_backroom"] = "Cash only. Chips only. The house always has time for you.",
        ["backroom_sign"] = "Back Room",
        ["campus_backroom_status"] = "Always open",
        ["backroom_dust"] = "Not open yet.",
        ["backroom_dust_line"] = "Sheets over the tables and the lights off at the wall. Another night.",
        ["wallet_chips"] = "Chips",
        ["prize_shelf_chips"] = "The Back Room shelf",
        ["prize_shelf_chips_hint"] = "Chips only. What you carried out of the Back Room buys these.",
        // ---- EMI's stuck-hints (Daily Trigger, 2026-08-30) ----------------------------
        // The owner amended the "no mid-class mascot speech" law (arcademy/CLAUDE.md traps
        // 90 and 97) for one narrow channel: when the board says the player is beaten, EMI
        // may ASK whether they want a hand. Two offers a class, never a hint she was not
        // invited to give. Offer 1 names the band today's answer came out of (free); offer
        // 2 places one letter and caps the class at A via the existing `stuck_rescue`
        // assist, which the report card already explains without a row of its own.
        //
        // These are ordinary call-site keys. The CLASS resolves them and hands finished
        // sentences to emi/asks.js, which has no lexicon - so `{cat}` below is substituted
        // page-side with one of the dt_cat_* rows, never here.
        ["dt_help_ask_cat"] = "psst. i might know this one.",
        ["dt_help_chip_cat_yes"] = "spill",
        ["dt_help_chip_no"] = "nah",
        ["dt_help_yes_cat"] = "smells like a {cat} word to me.",
        ["dt_help_no_cat"] = "respect. i'll just sit here knowing it.",
        ["dt_help_ask_letter"] = "i could hold one letter for you.",
        ["dt_help_chip_letter_yes"] = "ok",
        ["dt_help_yes_letter"] = "boop. that one's yours now.",
        ["dt_help_no_letter"] = "ok. my letter and i will practice waiting.",
        // The nine band names. The KEY is the contract: it is `dt_cat_` plus the `cat` of a
        // THEME_GROUPS band in games/daily-trigger/words-answers.js (plus `common` for the
        // tiny ordinary-English band), so renaming a band there orphans its row here.
        ["dt_cat_trance"] = "spirally",
        ["dt_cat_training"] = "training arc",
        ["dt_cat_submission"] = "yes ma'am",
        ["dt_cat_denial"] = "not yet",
        ["dt_cat_bimbo"] = "glittery",
        ["dt_cat_arcade"] = "hometown",
        ["dt_cat_school"] = "classroom",
        ["dt_cat_melt"] = "melty",
        ["dt_cat_common"] = "civilian",
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

        if (!PrizeGateAllows(s, key, value))
        {
            // Refused, never wiped: echo what is STORED so the page's pending paint converges on
            // the truth rather than on the value it asked for (the keybinds precedent above).
            return (ParseJsonObject(s.ArcademySettingsJson) ?? new JObject())[key];
        }
        return SetGameSetting(s, key, value);
    }

    /// <summary>
    /// THE ONE PER-GAME KNOB THE PRIZE COUNTER GUARDS: The Deep End's wide board. The enum itself
    /// is declared page-side in the game's manifest (a game file, which the economy does not
    /// touch), so the door is held HERE — a value the player has not bought is refused on the way
    /// in, and the counter's projection is what tells the page to draw the row as locked.
    ///
    /// <para>GRANDFATHERED, deliberately. 5x5 shipped free, so a save that already sits on it keeps
    /// it: the gate only ever refuses a CHANGE onto the wide board, never yanks a player off one
    /// they were already using. Nothing anyone already had is taken away.</para>
    /// </summary>
    private static bool PrizeGateAllows(Models.AppSettings s, string key, JToken? value)
    {
        if (!string.Equals(key, DeepEndBoardSizeKey, StringComparison.Ordinal)) return true;
        if (!string.Equals((value as JValue)?.Value as string, DeepEndWideBoard, StringComparison.Ordinal))
            return true;
        try
        {
            var stored = (ParseJsonObject(s.ArcademySettingsJson) ?? new JObject())[key] as JValue;
            if (string.Equals(stored?.Value as string, DeepEndWideBoard, StringComparison.Ordinal))
                return true;   // already there before the counter existed - leave them on it
            if (_meta?.WalletOwns(ArcademyEconomy.SkuDeepEndWideBoard) == true) return true;
            App.Logger?.Information("ArcademyHost: the wide board is not bought yet - '{Key}' refused", key);
            return false;
        }
        catch (Exception ex)
        {
            App.Logger?.Debug("ArcademyHost.PrizeGateAllows: {E}", ex.Message);
            return true;   // a wallet we cannot read never costs the player a setting
        }
    }

    /// <summary>The Deep End's board-size knob and its wide value. Mirrors the game manifest's
    /// <c>de_board_size</c> enum (<c>games/the-deep-end/index.js</c>) - the two must agree.</summary>
    private const string DeepEndBoardSizeKey = "de_board_size";
    private const string DeepEndWideBoard = "5x5";

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
        // S+ is the Honors-only rung (economy, 2026-08-26). It sits in the SAME table so the
        // existing "unknown grade degrades to C" clamp below recognises it instead of eating it.
        ["S+"] = 1.6, ["S"] = 1.5, ["A"] = 1.25, ["B"] = 1.0, ["C"] = 0.6, ["pass"] = 1.0,
    };

    /// <summary>Ceiling on the per-game flavour bonus, so a game can season its payout without
    /// re-inventing the table (SYNTHESIS-NOTES #4).</summary>
    private const double FlavorXpCap = 15;

    /// <summary>EMI's dare bonus (EMI ASKS, owner call 2026-08-25): "bet you can't S this one",
    /// won. A small FIXED award, host-side like every other number on this page - the frame says
    /// only WHICH dare was won, never what it is worth.</summary>
    private const double DareBonusXp = 15;

    /// <summary>The only three dare kinds a page may name. Anything else is dropped in silence:
    /// the field is free text off a postMessage and it must never be able to widen the table.</summary>
    private static readonly HashSet<string> DareKinds =
        new(StringComparer.Ordinal) { "S", "streak", "fast" };

    // ============================ the Extra Credit lever ============================

    /// <summary>
    /// THE PENDING LEVER, per game key: what <c>class-started</c> claimed, already clamped against
    /// what this save has unlocked. Process-lifetime on purpose — a crash mid-class simply means
    /// the finish pays the standard rate, which is the safe way for this to fail. Persisting it
    /// would buy nothing and would give a stale file a way to claim a multiplier.
    /// </summary>
    private static readonly Dictionary<string, string> _pendingLever = new(StringComparer.Ordinal);

    /// <summary>Remember the clamped notch for <paramref name="gameKey"/>. Unknown text, and any
    /// notch the player has not unlocked, become "standard".</summary>
    private static void NotePendingLever(string? gameKey, string? lever)
    {
        try
        {
            var key = (gameKey ?? "").Trim();
            if (key.Length == 0 || key.Length > 64) return;
            var (extra, honors) = _meta?.LeverUnlocks() ?? (false, false);
            var clamped = ArcademyEconomy.ClampLever(lever?.Trim(), extra, honors);
            lock (_pendingLever)
            {
                // Bounded for the same reason every other page-fed map here is: there are a dozen
                // rooms, and a frame loop must not be able to grow this without end.
                if (clamped == "standard") _pendingLever.Remove(key);
                else if (_pendingLever.Count < 64 || _pendingLever.ContainsKey(key))
                    _pendingLever[key] = clamped;
            }
            if (clamped != "standard")
                App.Logger?.Information("ArcademyHost: lever '{Lever}' armed for {Game}", clamped, key);
        }
        catch (Exception ex) { App.Logger?.Debug("ArcademyHost.NotePendingLever: {E}", ex.Message); }
    }

    /// <summary>Read and CLEAR the pending notch. Once spent it is gone, so a second
    /// <c>class-ended</c> for the same room pays the standard rate.</summary>
    private static string TakePendingLever(string gameKey)
    {
        lock (_pendingLever)
        {
            if (!_pendingLever.TryGetValue(gameKey, out var lever)) return "standard";
            _pendingLever.Remove(gameKey);
            return lever;
        }
    }

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

            // THE LEVER, read back and spent. Taken here rather than at the payout below because
            // it also decides whether the S+ the page is claiming is even reachable.
            var lever = TakePendingLever(gameKey);

            // S+ IS UNREACHABLE OUTSIDE HONORS, so a claimed one that did not start on the Honors
            // notch is degraded to a plain S rather than refused: the run really did grade at the
            // top, it simply cannot be worth the Honors row. Never trust the page for a number.
            if (string.Equals(grade, "S+", StringComparison.OrdinalIgnoreCase) && lever != "honors")
            {
                App.Logger?.Warning("ArcademyHost: S+ claimed for {Game} without the Honors lever - graded S",
                    gameKey);
                grade = "S";
            }
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
            /* EMI'S DARE (EMI ASKS, 2026-08-25). The page flags the run when the player takes the
               bet and reports the WIN on this same frame; the host owns the number and the farm
               guard. Gated on `firstToday` for exactly the reason the payout above is: a retake is
               a free replay of the same script, and a dare that paid on every replay would be the
               one way to grind XP out of a class that is deliberately free. It is its own AddXP
               call, tagged XPSource.Quest, so THE BANK flies (BankAccumulator.IsBankable) the way
               a quest payout does - the class XP itself stays XPSource.Other and is unchanged. */
            var dareWon = (ReadString(o, "dareWon") ?? "").Trim();
            if (dareWon.Length > 16) dareWon = dareWon[..16];
            bool darePaid = firstToday && DareKinds.Contains(dareWon);
            if (darePaid)
            {
                try { App.Progression?.AddXP(DareBonusXp, XPSource.Quest); }
                catch (Exception ex) { App.Logger?.Debug("ArcademyHost dare AddXP: {E}", ex.Message); }
            }

            int levelAfter = App.Settings?.Current?.PlayerLevel ?? levelBefore;

            // LOCAL date rolls attendance; the page's dayUtc only ever seeded the content (#978),
            // so it is deliberately NOT what gets written here. RecordAttendance is idempotent per
            // (day, gameKey), so a retake cannot inflate todayClasses or perfect attendance - and
            // running it unconditionally is what keeps a retake on a NEW local day (same UTC day,
            // player east of UTC) crediting the streak it has earned.
            var localDate = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var (streak, perfect, classesToday, slipsSpent) =
                _meta?.RecordAttendance(localDate, gameKey) ?? (0, 0, 0, 0);
            // The page and the debrief only ever needed "did one cover me"; the COUNT is the
            // server's business, because it holds the bag the slips come out of.
            var lateSlipUsed = slipsSpent > 0;

            // ============================ THE TILL ============================
            // Tickets and tokens, wrapped on their own: the attendance credit above is the thing we
            // must not lose, and the money must never become a second way to lose it. Everything
            // here is host-decided - the frame carries a grade and a room, and nothing else.
            //
            // AND SINCE THE SHARED WALLET, WHO DECIDES DEPENDS ON THE DOOR. With an account
            // attached the money is the SERVER's to mint (one wallet, every device), so this frame
            // goes up and the answer comes back on its own beat. Signed out, nothing has changed:
            // MintCurrency runs here, in this order, exactly as it always has.
            var mintFrame = ArcademyWalletSyncService.DoorOpen
                ? ArcademyWalletSyncService.BuildMintFrame(
                    gameKey, grade, zen, streak, localDate, lever, slipsSpent, dayUtc)
                : null;
            var till = mintFrame == null
                ? MintCurrency(gameKey, grade, zen, streak, localDate, lever)
                : default;

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
                // S+ counts as an S day here (economy, 2026-08-26): it is the same night's work
                // with the lever pushed, and the double hole is the reward for the letter.
                bool gradedS = ArcademyEconomy.IsTokenGrade(grade, zen: false);
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

            // Everything the debrief needs that is NOT money, gathered once so both endings below
            // send the same frame and there is only one place to change the wording of a payout.
            var report = new PayoutReport(gameKey, tier, xp, levelAfter > levelBefore, streak,
                perfect, classesToday, !firstToday, darePaid ? DareBonusXp : 0, grade,
                darePaid ? dareWon : "", dayUtc);

            if (mintFrame == null)
            {
                // SIGNED OUT. Byte for byte the old ending: the snapshot, the payout, the card.
                if (_meta != null) _host?.Post(_meta.SnapshotMessage());
                PostPayout(report, till, lateSlipUsed);
                PostPunchCard(gameKey, "daily", punch);
                LogClassComplete(report, till, lever, banked: false);
                return;
            }

            // BANKED. The punch card goes out NOW rather than behind the request: the ceremony is
            // the page's own beat and it must never sit waiting on a network that might be down.
            // The money follows on `payout-result` whenever the bank answers, which is exactly the
            // fill-in-later the report card was already built for (it repaints on arrival).
            if (_meta != null) _host?.Post(_meta.SnapshotMessage());
            PostPunchCard(gameKey, "daily", punch);

            int epoch = Volatile.Read(ref _generation);
            ArcademyWalletSyncService.Bank(mintFrame,
                outcome => SettleMint(epoch, report, mintFrame, lever, lateSlipUsed, zen, localDate, outcome));
        }
        catch (Exception ex) { App.Logger?.Warning("ArcademyHost.OnClassEnded: {E}", ex.Message); }
    }

    // ============================ the till ============================

    /// <summary>What one finish put in the drawer, ready to ride <c>payout-result</c>.</summary>
    private readonly record struct TillResult(
        int Tickets, int Base, double Mult, bool TokenMinted,
        int Payday, string Lever, JObject Wallet);

    /// <summary>Everything on <c>payout-result</c> that is NOT money. Gathered once in
    /// <see cref="OnClassEnded"/> so the local ending and the banked one send the same frame.</summary>
    private readonly record struct PayoutReport(
        string GameKey, int Tier, double Xp, bool LevelUp, int Streak, int Perfect,
        int ClassesToday, bool Retake, double DareXp, string Grade, string DareWon, string DayUtc);

    /// <summary>
    /// The <c>payout-result</c> frame. Every field is what it always was; what changed is that the
    /// money half can now arrive from the account's wallet instead of this machine's, and the page
    /// cannot tell (and must not be able to tell) which.
    /// </summary>
    private static void PostPayout(in PayoutReport r, in TillResult till, bool lateSlipUsed)
    {
        _host?.Post(new
        {
            type = "payout-result",
            gameKey = r.GameKey,
            xp = r.Xp,
            levelUp = r.LevelUp,
            streak = r.Streak,
            perfectAttendance = r.Perfect,
            classesToday = r.ClassesToday,
            // Additive: the report card reads it to explain a 0 XP line. Older pages ignore it.
            retake = r.Retake,
            // Additive (EMI ASKS): what the dare bonus actually paid, so the page never has
            // to guess. 0 on every class that carried no dare, and on a retake.
            dareXp = r.DareXp,
            // Additive (economy): the till, already counted. `tickets` is what was minted,
            // `ticketBase`/`ticketMult` are its working so the debrief can show the lever and
            // the payday doing their jobs, and `wallet` is the POST-mint balance, so the page
            // never adds anything up itself.
            grade = r.Grade,
            tickets = till.Tickets,
            ticketBase = till.Base,
            ticketMult = till.Mult,
            tokenMinted = till.TokenMinted,
            payday = till.Payday,
            lever = till.Lever,
            lateSlipUsed,
            wallet = till.Wallet ?? new JObject { ["t"] = 0, ["k"] = 0 },
        });
    }

    /// <summary>The owner's one line per class. <paramref name="banked"/> is the only new word in
    /// it, and it is the word that tells a support thread apart: money on the account, or money
    /// still sitting on this desk waiting for a wire.</summary>
    private static void LogClassComplete(in PayoutReport r, in TillResult till, string lever, bool banked)
    {
        App.Logger?.Information(
            "ArcademyHost: class complete ({Game}, tier {Tier}, grade {Grade}) = {Xp:0} XP{Dare}{Retake}, {Tickets} tickets (x{Mult}{Lever}){Token}, streak {Streak}, {Today}/4 today{Banked}",
            r.GameKey, r.Tier, r.Grade, r.Xp,
            r.DareWon.Length > 0
                ? " +" + r.DareXp.ToString("0", CultureInfo.InvariantCulture) + " dare (" + r.DareWon + ")"
                : "",
            r.Retake ? " (retake - already paid for " + r.DayUtc + ")" : "",
            till.Tickets, till.Mult.ToString("0.##", CultureInfo.InvariantCulture),
            lever == "standard" ? "" : ", " + lever,
            till.TokenMinted ? " +1 TOKEN" : "",
            r.Streak, r.ClassesToday,
            banked ? " (banked)" : "");
    }

    /// <summary>
    /// The bank answered (or did not). Runs on a background thread, so it hops the dispatcher and
    /// checks the window generation the way every other async continuation here does - a reply that
    /// arrives after the Arcademy closed must not paint a payout into the NEXT session's report.
    ///
    /// <para>THE THREE ENDINGS. Banked: adopt what came back and report it. Nobody answered YET -
    /// the wire, or the TIER GATE, which on this desk is a 14-day cached-entitlement grace the
    /// server does not keep, so an account can legitimately be playing here and not yet bankable
    /// there: mint locally so the debrief still has a number, park the frame under the same
    /// <c>mintId</c>, and let the next launch carry it up - the server's answer will REPLACE this
    /// preview, so nothing is ever counted twice. Refused: mint locally and park nothing, because a
    /// frame this account can never bank is a queue that can never drain.</para>
    /// </summary>
    private static void SettleMint(int epoch, PayoutReport report, JObject frame, string lever,
        bool lateSlipUsed, bool zen, string localDate, ArcademyWalletSyncService.MintOutcome outcome)
    {
        try
        {
            var disp = Application.Current?.Dispatcher;
            if (disp == null || disp.HasShutdownStarted) return;
            if (Volatile.Read(ref _generation) != epoch) return;
            disp.BeginInvoke(() =>
            {
                try
                {
                    if (_meta == null || _host == null) return;
                    if (Volatile.Read(ref _generation) != epoch) return;

                    TillResult till;
                    bool slip = lateSlipUsed;
                    if (outcome.Verdict == ArcademyWalletSyncService.MintVerdict.Banked
                        && outcome.Economy != null)
                    {
                        till = TillFromEconomy(outcome.Economy, lever);
                        // The server consumes the late slip itself and echoes what actually
                        // happened, so its word beats the local read that went up on the frame.
                        slip = (bool?)outcome.Economy["lateSlipUsed"] ?? lateSlipUsed;
                    }
                    else
                    {
                        // THE PREVIEW. Worth saying plainly: this writes the local wallet, and the
                        // next successful pull or replay overwrites it with the account's copy.
                        till = MintCurrency(report.GameKey, report.Grade, zen, report.Streak,
                            localDate, lever);
                        if (outcome.Verdict == ArcademyWalletSyncService.MintVerdict.Queue)
                            ArcademyWalletSyncService.Park(frame);
                    }

                    _host.Post(_meta.SnapshotMessage());
                    PostPayout(report, till, slip);
                    LogClassComplete(report, till, lever,
                        banked: outcome.Verdict == ArcademyWalletSyncService.MintVerdict.Banked);
                }
                catch (Exception ex) { App.Logger?.Warning("ArcademyHost.SettleMint: {E}", ex.Message); }
            });
        }
        catch (Exception ex) { App.Logger?.Debug("ArcademyHost.SettleMint dispatch: {E}", ex.Message); }
    }

    /// <summary>
    /// The server's <c>economy</c> block, read into the same shape the local till has. The page's
    /// <c>payday</c> field is the MULTIPLIER (an int) and always has been; the wire carries the
    /// draw as an object, so the room name is dropped here rather than changing a frame the shell
    /// already reads.
    /// </summary>
    private static TillResult TillFromEconomy(JObject economy, string fallbackLever)
    {
        int tickets = Math.Max(0, (int?)economy["tickets"] ?? 0);
        int b = Math.Max(0, (int?)economy["ticketBase"] ?? 0);
        double mult = (double?)economy["ticketMult"] ?? 1.0;
        if (!(mult > 0)) mult = 1.0;
        bool token = (bool?)economy["tokenMinted"] ?? false;
        int payday = Math.Max(1, (int?)economy["payday"]?["mult"] ?? 1);
        var lever = (string?)economy["lever"];
        if (string.IsNullOrWhiteSpace(lever)) lever = fallbackLever;
        var wallet = economy["wallet"] as JObject;
        return new TillResult(tickets, b, mult, token, payday, lever,
            ArcademyEconomy.BalanceJson(ArcademyEconomy.EnsureShape(wallet)));
    }

    /// <summary>
    /// The account's wallet landed (or a parked mint drained into it). Same shape as
    /// <see cref="OnMirrorCardsChanged"/>: hop the dispatcher, push the whole-blob meta snapshot,
    /// and let the shell repaint its chips off it. Nothing here decides anything.
    /// </summary>
    private static void OnWalletBanked()
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
                }
                catch (Exception ex) { App.Logger?.Debug("ArcademyHost.OnWalletBanked: {E}", ex.Message); }
            });
        }
        catch (Exception ex) { App.Logger?.Debug("ArcademyHost.OnWalletBanked dispatch: {E}", ex.Message); }
    }

    /// <summary>
    /// TICKETS AND TOKENS for one graded finish. The whole sum lives in
    /// <see cref="ArcademyEconomy"/>; this only gathers the inputs the store owns (the replay
    /// counter, tonight's roster) and writes the result back.
    ///
    /// <para>Deliberately NOT gated on the XP farm guard. A retake is free XP-wise because the day's
    /// seed makes it the same script, but it is still a night at the machine — the replay ladder is
    /// what bounds it, dropping the second run to 40% and everything after to 15%.</para>
    ///
    /// <para>Wrapped whole: a wallet that threw would otherwise cost the frame that carries the
    /// attendance credit, and no amount of money is worth a streak.</para>
    /// </summary>
    private static TillResult MintCurrency(string gameKey, string grade, bool zen, int streak,
        string localDate, string lever)
    {
        var empty = new JObject { ["t"] = 0, ["k"] = 0 };
        if (_meta == null || gameKey.Length == 0)
            return new TillResult(0, 0, 1.0, false, 1, lever, empty);
        try
        {
            // A first A-or-better is what opens the Extra Credit notch, so it is checked before
            // anything is paid - the night that earns it does not get to use it, which is right.
            _meta.TryUnlockExtraCredit(grade);

            var prior = _meta.NoteWalletPlay(localDate, gameKey);
            var payday = ArcademyEconomy.PickPayday(
                DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                _meta.EnrolledGameKeys());
            var paydayMult = ArcademyEconomy.PaydayMultFor(payday, gameKey);

            var sum = ArcademyEconomy.ComputeTickets(grade, prior, streak, paydayMult, lever);
            _meta.EarnTickets(sum.Tickets);

            // ONE TOKEN A DAY, on the first S-rank of the LOCAL day, never from a zen pass.
            // Independent of the tickets above: a token night still pays its tickets.
            bool token = ArcademyEconomy.IsTokenGrade(grade, zen) && _meta.TryClaimTokenDay(localDate);

            var wallet = _meta.WalletSnapshot();
            return new TillResult(sum.Tickets, sum.Base, sum.Mult, token, paydayMult, lever,
                ArcademyEconomy.BalanceJson(wallet));
        }
        catch (Exception ex)
        {
            App.Logger?.Warning("ArcademyHost.MintCurrency: {E}", ex.Message);
            return new TillResult(0, 0, 1.0, false, 1, lever, empty);
        }
    }

    /// <summary>
    /// <c>prize-buy {sku}</c> -> the counter. Validation is entirely host-side
    /// (<see cref="ArcademyEconomy.Buy"/>): the frame carries a sku and nothing else, so there is
    /// no price, no currency and no quantity on it to argue with.
    ///
    /// <para>The reply is posted on EVERY attempt, refusals included, because the counter settles
    /// its UI only on the echo (nothing optimistic) — a silent refusal would leave the page holding
    /// a spinner it could never put down.</para>
    /// </summary>
    private static void OnPrizeBuy(JObject o)
    {
        try
        {
            var sku = (ReadString(o, "sku") ?? "").Trim();
            if (sku.Length > 64) sku = sku[..64];
            if (_meta == null)
            {
                PostWalletResult(sku, false, "unknown", null);
                return;
            }

            // WITH AN ACCOUNT ATTACHED, THE COUNTER IS ON THE SERVER. It has to be: the wallet is
            // shared, so a spend settled here would be overwritten by the next pull and the player
            // would watch a prize they bought turn back into tickets. ONLINE ONLY by contract - a
            // press that cannot reach the till is a refusal, and the room says so.
            if (ArcademyWalletSyncService.DoorOpen)
            {
                int epoch = Volatile.Read(ref _generation);
                ArcademyWalletSyncService.Buy(sku, outcome => SettleBuy(epoch, sku, outcome));
                return;
            }

            var localDate = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var result = _meta.Buy(sku, localDate);
            if (result.Ok) _host?.Post(_meta.SnapshotMessage());
            PostWalletResult(sku, result.Ok, result.Reason, _meta.WalletSnapshot());
        }
        catch (Exception ex) { App.Logger?.Warning("ArcademyHost.OnPrizeBuy: {E}", ex.Message); }
    }

    /// <summary>
    /// The counter answered (or did not). Same dispatcher-and-generation hop <see cref="SettleMint"/>
    /// takes, and the same rule about a stale reply.
    ///
    /// <para>A refusal the SERVER named rides back verbatim - the room already has a line for each
    /// of them. Nobody answering is <c>offline</c>, which is the one new word on this frame, and it
    /// carries the wallet UNCHANGED: the page treats a missing bag as "unchanged" but there is no
    /// reason to make it guess, and no reason for a failed purchase to move a number.</para>
    /// </summary>
    private static void SettleBuy(int epoch, string sku, ArcademyWalletSyncService.BuyOutcome outcome)
    {
        try
        {
            var disp = Application.Current?.Dispatcher;
            if (disp == null || disp.HasShutdownStarted) return;
            if (Volatile.Read(ref _generation) != epoch) return;
            disp.BeginInvoke(() =>
            {
                try
                {
                    if (_meta == null || _host == null) return;
                    if (Volatile.Read(ref _generation) != epoch) return;

                    if (!outcome.Answered)
                    {
                        PostWalletResult(sku, false, "offline", _meta.WalletSnapshot());
                        return;
                    }
                    if (outcome.Ok) _host.Post(_meta.SnapshotMessage());
                    PostWalletResult(sku, outcome.Ok, outcome.Reason, _meta.WalletSnapshot());
                }
                catch (Exception ex) { App.Logger?.Warning("ArcademyHost.SettleBuy: {E}", ex.Message); }
            });
        }
        catch (Exception ex) { App.Logger?.Debug("ArcademyHost.SettleBuy dispatch: {E}", ex.Message); }
    }

    /// <summary>The <c>wallet-result</c> frame: same-frame truth for the counter, on the same
    /// pattern <see cref="PostPunchCard"/> uses. Carries the post-trade balances, the whole
    /// inventory and the lever rungs, so the shelf can repaint from one message.</summary>
    private static void PostWalletResult(string sku, bool ok, string? reason, JObject? wallet)
    {
        try
        {
            var w = ArcademyEconomy.EnsureShape(wallet);
            _host?.Post(new
            {
                type = "wallet-result",
                ok,
                sku,
                reason,
                wallet = ArcademyEconomy.BalanceJson(w),
                inv = ArcademyEconomy.InvJson(w),
                unlocks = ArcademyEconomy.UnlocksJson(w),
            });
        }
        catch (Exception ex) { App.Logger?.Debug("ArcademyHost.PostWalletResult: {E}", ex.Message); }
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

    // ============================ the student id ============================

    /// <summary>One link at a time. A second <c>link-discord</c> while a browser flow is open is
    /// IGNORED, not queued: two OAuth listeners want the same local port, and the player is already
    /// looking at the page the first one opened.</summary>
    private static int _linkInFlight;   // 0/1 via Interlocked

    /// <summary>Set when THIS host tore the link-up down (panic, teardown, our own deadline).
    /// Cancelling the flow makes it throw, and without this the throw would push a second, wrong
    /// <c>failed</c> frame straight after the <c>cancelled</c> we already sent.</summary>
    private static bool _linkCancelled;

    /// <summary>Our own ceiling on a link. <see cref="Services.Account.DiscordService"/> gives up
    /// after five minutes, which is five minutes of the chip saying "Waiting on Discord..." to
    /// somebody who closed the tab. Two minutes and we call it cancelled, cancel the flow underneath
    /// and let them press it again.</summary>
    private static readonly TimeSpan LinkDeadline = TimeSpan.FromSeconds(120);

    /// <summary>
    /// Push the identity block. <paramref name="result"/> is the outcome of a link attempt
    /// (<c>linked</c> / <c>cancelled</c> / <c>failed</c>) and is absent on the ordinary pushes -
    /// a rung that moved, or a photo that finished downloading. The page repaints the card and the
    /// chip from this frame only.
    /// </summary>
    private static void PushProfile(string? result = null)
    {
        var disp = Application.Current?.Dispatcher;
        if (disp == null || disp.HasShutdownStarted) return;
        disp.BeginInvoke(() =>
        {
            try
            {
                if (_host == null) return;
                _host.Post(new { type = "profile", profile = BuildProfile(App.Settings?.Current), result });
            }
            catch (Exception ex) { App.Logger?.Debug("ArcademyHost.PushProfile: {E}", ex.Message); }
        });
    }

    /// <summary>
    /// Bring the cached student photo up to date off the UI thread, and push a <c>profile</c> frame
    /// if - and only if - the bytes changed. Called at window open (after <c>init</c> has already
    /// shipped whatever was on disk) and again after a link completes.
    ///
    /// <para>Generation-guarded like every other continuation here: a fetch that comes back after
    /// the window closed and relaunched must not paint the NEW page's card.</para>
    /// </summary>
    private static void KickAvatarRefresh()
    {
        int epoch = Volatile.Read(ref _generation);
        _ = Task.Run(async () =>
        {
            try
            {
                if (!await ArcademyAvatarCache.EnsureCachedAsync().ConfigureAwait(false)) return;
                if (Volatile.Read(ref _generation) != epoch) return;
                PushProfile();
            }
            catch (Exception ex) { App.Logger?.Debug("ArcademyHost.KickAvatarRefresh: {E}", ex.Message); }
        });
    }

    /// <summary>
    /// ONE CLICK IS ENOUGH (owner ruling 2). The chip asked for a Discord link; when the OAuth
    /// succeeds the HOST applies <c>presenceShare = discord</c> itself and pushes the finished
    /// profile, so the player never has to come back and flip a second switch.
    ///
    /// <para>An account that is ALREADY linked takes the short path - apply the rung, refresh the
    /// photo, answer <c>linked</c>. The page is not supposed to send this frame in that state
    /// (contract trap 1), but a stale page must not be able to strand its own chip.</para>
    ///
    /// <para>The rung is written THROUGH AppSettings, never into the projection: that is what fires
    /// <see cref="ArcademyPresenceService"/>'s consent hook and the ordinary <c>setting</c> echo.
    /// Writing it any other way would move the card without moving the ghost, which is exactly the
    /// one switch the owner ruled these are.</para>
    /// </summary>
    private static async void OnLinkDiscord()
    {
        if (Interlocked.CompareExchange(ref _linkInFlight, 1, 0) != 0)
        {
            App.Logger?.Debug("ArcademyHost: link-discord ignored - a link is already open");
            return;
        }
        _linkCancelled = false;
        var d = App.Discord;
        try
        {
            if (d == null)
            {
                PushProfile("failed");
                return;
            }

            if (d.IsAuthenticated)
            {
                ApplyDiscordRung();
                await ArcademyAvatarCache.EnsureCachedAsync().ConfigureAwait(true);
                PushProfile("linked");
                return;
            }

            App.Logger?.Information("ArcademyHost: student ID chip is opening the Discord link-up");
            // StartOAuthFlowAsync IS the completion signal: it returns when the flow has finished
            // end to end (tokens exchanged, user validated) and throws on cancel/timeout/failure.
            // No polling, and no listening to AuthenticationChanged - that event also fires for a
            // link the player started somewhere else in the app.
            var flow = Application.Current?.Dispatcher?.InvokeAsync(() => d.StartOAuthFlowAsync())
                .Task.Unwrap() ?? d.StartOAuthFlowAsync();
            // Observe any future fault whichever way this race lands, so a flow that throws after
            // our deadline cannot reach the finalizer as an UnobservedTaskException.
            _ = flow.ContinueWith(t => { _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);

            var done = await Task.WhenAny(flow, Task.Delay(LinkDeadline)).ConfigureAwait(true);
            if (done != flow)
            {
                App.Logger?.Information("ArcademyHost: Discord link timed out after {S}s - cancelled",
                    LinkDeadline.TotalSeconds);
                _linkCancelled = true;
                try { d.CancelOAuthFlow(); } catch { }
                PushProfile("cancelled");
                return;
            }
            await flow.ConfigureAwait(true);   // rethrows whatever the flow threw

            if (_linkCancelled) return;        // panic/teardown already answered the page
            if (!d.IsAuthenticated)
            {
                // The flow returned without linking: the service refuses a second concurrent run
                // (IsVerifying) and returns immediately, which reads as "nothing happened".
                PushProfile("cancelled");
                return;
            }

            ApplyDiscordRung();
            await ArcademyAvatarCache.EnsureCachedAsync().ConfigureAwait(true);
            App.Logger?.Information("ArcademyHost: Discord linked from the student ID - photo rung applied");
            PushProfile("linked");
        }
        catch (OperationCanceledException)
        {
            App.Logger?.Information("ArcademyHost: Discord link cancelled");
            if (!_linkCancelled) PushProfile("cancelled");
        }
        catch (Exception ex)
        {
            // A flow WE cancelled surfaces as a throw too (a dead listener, or the service's own
            // TimeoutException off the cancelled delay). That is not a failure to report twice.
            if (_linkCancelled) App.Logger?.Debug("ArcademyHost: link-up threw after we cancelled it: {E}", ex.Message);
            else
            {
                App.Logger?.Warning("ArcademyHost: Discord link failed: {E}", ex.Message);
                PushProfile("failed");
            }
        }
        finally { Interlocked.Exchange(ref _linkInFlight, 0); }
    }

    /// <summary>Raise the consent rung to <c>discord</c> the ordinary way - through AppSettings, so
    /// the presence service's hook and the page's <c>setting</c> echo both fire - and persist it.
    /// The property clamps unknown values itself, so this can only ever write the rung it names.</summary>
    private static void ApplyDiscordRung()
    {
        try
        {
            var s = App.Settings?.Current;
            if (s == null) return;
            if (!string.Equals(PresenceShare(s), "discord", StringComparison.Ordinal))
            {
                s.ArcademyPresenceShare = "discord";
                App.Settings?.Save();
            }
        }
        catch (Exception ex) { App.Logger?.Debug("ArcademyHost.ApplyDiscordRung: {E}", ex.Message); }
    }

    /// <summary>The panic key (and the teardown) close an open link-up: a browser tab left waiting
    /// on a listener nobody is going to read is worse than a chip the player can press again.
    /// Pushes <c>cancelled</c> only when there is still a page to hear it.</summary>
    private static void CancelPendingLink(string why, bool tellPage)
    {
        if (Volatile.Read(ref _linkInFlight) == 0) return;
        App.Logger?.Information("ArcademyHost: cancelling the open Discord link-up ({Why})", why);
        _linkCancelled = true;
        try { App.Discord?.CancelOAuthFlow(); } catch { }
        if (tellPage) PushProfile("cancelled");
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
    private sealed record AssetUrl(string Url, string Kind, string Mime, string? Tag = null, string? Src = null, string? Poster = null);

    /// <summary>A remote loop's own still (<see cref="FypAssetManifest.Entry.PosterUrl"/>): the
    /// page paints it while the clip buffers and over its decoder ceiling, instead of the striped
    /// back the owner kept seeing (0827). Stills and library files carry none.</summary>
    private static string? PosterFor(Fyp.FypAssetManifest.Entry e, string kind)
        => kind == "loop" && !string.IsNullOrEmpty(e.PosterUrl) ? e.PosterUrl : null;

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
            urls = served.Select(u => new { url = u.Url, kind = u.Kind, mime = u.Mime, poster = u.Poster }).ToArray(),
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
                // A card is not a feed tile: the page paints it at a few hundred px, so the
                // smaller rendition (ScrolllerSource.SmallUrl, <= 640 clip / <= 1280 still,
                // the web port's numbers) loads in a fraction of the time the 1920px one did.
                var url = e.SmallUrl ?? e.Url;
                fresh.Add(new AssetUrl(url, kind, MimeFor(url, kind), Poster: PosterFor(e, kind)));
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
                    urls = send.Select(u => new { url = u.Url, kind = u.Kind, mime = u.Mime, poster = u.Poster }).ToArray(),
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

    /// <summary>Asks that arrived while <see cref="TaggedFetchesInFlight"/> held their pile's key.
    /// They used to be answered EMPTY on the spot - and the page, which asks again after every
    /// reply, burned its whole ask budget on empties in under four seconds and fell back to a
    /// QUICK SORT (a different game) while the fetch was still on its way (0827, a Retake). Now
    /// they queue behind that fetch and are served off its buffer the moment it lands. Guarded
    /// by <see cref="TaggedFetchesInFlight"/>'s lock; a waiter from a closed Arcademy (epoch
    /// moved on) is dropped, never posted.</summary>
    private static readonly Dictionary<string, List<(string ReqId, string Tag, int Want, int Epoch)>> TaggedWaiters
        = new(StringComparer.Ordinal);

    private static void DrainTaggedWaiters(string key)
    {
        List<(string ReqId, string Tag, int Want, int Epoch)>? list;
        lock (TaggedFetchesInFlight)
        {
            if (!TaggedWaiters.Remove(key, out list) || list == null) return;
        }
        int epoch = Volatile.Read(ref _generation);
        foreach (var w in list)
        {
            if (w.Epoch != epoch) continue;
            try { PostTaggedAssets(w.ReqId, w.Tag, TakeBuffered(key, w.Want), true); } catch { }
        }
    }

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
                poster = u.Poster,
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
                // The fetch this ask wants is already on its way: queue behind it and let the
                // landing serve it (see TaggedWaiters) - an empty reply here was the Retake's
                // road to a QUICK SORT.
                if (!TaggedWaiters.TryGetValue(key, out var waiters)) TaggedWaiters[key] = waiters = new();
                waiters.Add((reqId, tag, want, epoch));
                return;
            }
        }
        try
        {
            var allowed = new HashSet<string>(TaggedChannelsFor(tag), StringComparer.OrdinalIgnoreCase);
            if (allowed.Count == 0)
            {
                PostTaggedAssets(reqId, tag, Array.Empty<AssetUrl>(), true);
                DrainTaggedWaiters(key);
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
                // The card rendition, not the feed one (see ServeRemoteBatch): SORT's ring is
                // 0.75-2.4s and a 1920px 15MB mp4 was the striped back the owner kept seeing.
                var url = e.SmallUrl ?? e.Url;
                fresh.Add(new AssetUrl(url, kind, MimeFor(url, kind), tag, "r/" + bare, PosterFor(e, kind)));
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
                DrainTaggedWaiters(key);
            });
        }
        catch (Exception ex)
        {
            App.Logger?.Warning("ArcademyHost: tagged batch failed: {E}", ex.Message);
            if (Volatile.Read(ref _generation) == epoch)
            {
                try { PostTaggedAssets(reqId, tag, Array.Empty<AssetUrl>(), true); } catch { }
            }
            DrainTaggedWaiters(key);
        }
        finally { lock (TaggedFetchesInFlight) TaggedFetchesInFlight.Remove(key); }
    }

    // ======================= local sample (the other kind of pile) =======================
    //
    // The page cannot enumerate a virtual host, so a local pile is sampled here: a folder list (or
    // one asset preset) in, `assets` rows out on the same envelope the remote path uses. Same
    // deselection blacklist the flash pool honours, and the same ccp.assets urls BuildLocalAssets
    // hands out - a row's src is the folder it really came from.

    /// <summary>`.webp` is in BOTH lists on purpose (ccp-bugs#1086): the extension does not say
    /// which one it belongs to, so it is admitted to either ask and the header probe in
    /// <see cref="SampleLocalAssets"/> settles it - a still one is dropped from a `loop` ask, an
    /// animated one is stamped with <see cref="AnimatedImageHint"/> and served as the loop it is.</summary>
    private static readonly string[] LocalLoopExts = { ".gif", ".mp4", ".webm", ".webp" };
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
        // Fisher-Yates, the same random-slice trick BuildLocalAssets uses - except the swap runs
        // as the slice is CONSUMED, because a .webp only says whether it animates once its header
        // is read (ccp-bugs#1086) and a `loop` ask may have to walk past a few still ones.
        var rng = new Random(StableSeed(reqId));
        int probed = 0;
        for (int i = 0; i < pool.Count && rows.Count < count; i++)
        {
            int j = rng.Next(i, pool.Count);
            (pool[i], pool[j]) = (pool[j], pool[i]);

            var file = pool[i];
            bool isWebp = Path.GetExtension(file).Equals(".webp", StringComparison.OrdinalIgnoreCase);
            // Bounded: a library of nothing but STILL webps must not turn one `loop` ask into a
            // header read per file. Past the budget an unprobed webp reads as "still" - the safe
            // direction, since the failure this fixes is dealing animation nothing has counted.
            bool animated = isWebp && probed++ < AnimatedProbeBudget && AnimatedWebp.IsAnimated(file);
            // A still webp has no business in the loop lane - it is the one candidate here that
            // was admitted on a maybe (see LocalLoopExts).
            if (kind == "loop" && isWebp && !animated) continue;

            var rowKind = animated ? "loop" : kind;
            var url = ToAssetsUrl(file) + (animated ? AnimatedImageHint : "");
            rows.Add(new AssetUrl(url, rowKind, MimeFor(url, rowKind), tag, presetSrc ?? RelativeFolder(root, file)));
        }
        return rows;
    }

    /// <summary>Header probes one local-sample ask may spend (ccp-bugs#1086). Generous next to the
    /// 24-row batch cap, so an honest ask never runs short, and small enough that a still-webp
    /// library cannot turn a `loop` ask into a walk of the whole tree with a file open per entry.</summary>
    private const int AnimatedProbeBudget = 200;

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

    /// <summary>One line a session about the probe frame's optional scope, the way
    /// <c>_nichesIgnoredLogged</c> guards the assets-request one.</summary>
    private static bool _probeScopeLogged;

    /// <summary>
    /// The SORT door's search box: is r/&lt;name&gt; real, and how much video does it hold. Same
    /// upstream question the two shipped pickers ask (<c>FypHostService.ProbeCustomSub</c>), with
    /// two differences that matter here: the answer is keyed by <c>reqId</c> (the door awaits a
    /// promise, so every path MUST reply), and a verified name lands in the LIBRARY ONLY.
    ///
    /// <para>It never touches <c>FypOnlineCustomSubs</c>: noise the player picked to sort against
    /// must not start flashing on their desktop. That split is the whole point of the library.</para>
    ///
    /// <para>THE SCOPE (2026-08-28). The frame may carry <c>scope</c> (the surface making the add -
    /// SORT's door sends <c>"sort"</c>) and <c>pile</c> (<c>"noise"</c> | <c>"target"</c>). Both are
    /// OPTIONAL and this host acts on NEITHER, deliberately: the law above already holds for every
    /// add, whatever surface made it, so honouring a scope here would be a second way of saying the
    /// same thing and a second place for it to drift. They are read and logged so the contract is
    /// visible on both sides - the web host shim, whose library row IS its feed selection, is the
    /// one that has to act on them (a decoy pile enrolled there followed the player into every
    /// other class, tester report 2026-08-28).</para>
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

        // Read, logged once, acted on nowhere - see the scope paragraph above.
        var scope = ((string?)o["scope"] ?? "").Trim();
        var pile = ((string?)o["pile"] ?? "").Trim();
        if (scope.Length > 0 && !_probeScopeLogged)
        {
            _probeScopeLogged = true;
            App.Logger?.Information(
                "ArcademyHost: probe-sub carried scope '{Scope}' / pile '{Pile}' - the library add never "
                + "enrols a sub in the app-wide feed on this host, so there is nothing to honour", scope, pile);
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

    // ============================ the annex registry link ============================

    /// <summary>The proxy, spelled the way every other service here spells it (there is no shared
    /// constant in this codebase - ArcademyPresenceService, ArcademySyncService, V2AuthService and
    /// ProfileSyncService each carry their own copy).</summary>
    private const string ProxyBaseUrl = "https://codebambi-proxy.vercel.app";

    /// <summary>The public aggregate the annex terminal's REGISTRY pane draws. Unauthenticated by
    /// design (head counts, nothing personal) - NO token is ever attached to this request.</summary>
    private const string AnnexStatsPath = "/v2/arcademy/annex/stats";

    /// <summary>Short of the page's own 8s deadline, so a slow link resolves as LINK DOWN here
    /// rather than being abandoned there.</summary>
    private static readonly TimeSpan AnnexStatsTimeout = TimeSpan.FromSeconds(6);

    /// <summary>A public aggregate is a few KB. A body this size is not the feed we asked for.</summary>
    private const int MaxAnnexStatsChars = 400_000;

    private static readonly HttpClient AnnexHttp = new() { Timeout = AnnexStatsTimeout };

    /// <summary>
    /// <c>annex-stats</c>: the page cannot reach the server itself (CORS is a wall, and the wall is
    /// right), so the host fetches the public aggregate and posts it straight back. EXACTLY ONE
    /// reply per ask, and <c>body</c> is either the parsed object or null - the OS renders LINK DOWN
    /// on null and never fabricates a number. A missing reply is the same thing eight seconds later,
    /// so nothing here may throw out of the handler.
    ///
    /// <para>OFFLINE MODE NEVER LEAVES THE MACHINE: the same flag <see cref="BuildInit"/> projects
    /// as <c>offlineMode</c> short-circuits to a null reply with no request at all.</para>
    /// </summary>
    private static async void OnAnnexStats()
    {
        // The window this ask belongs to: a relaunch mid-flight must not answer the NEW page.
        int epoch = Volatile.Read(ref _generation);
        if (App.Settings?.Current?.OfflineMode == true)
        {
            App.Logger?.Debug("ArcademyHost: annex-stats declined - offline mode");
            PostAnnexStats(null, epoch);
            return;
        }

        JObject? body = null;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, ProxyBaseUrl + AnnexStatsPath);
            using var response = await AnnexHttp.SendAsync(request).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                App.Logger?.Debug("ArcademyHost: annex-stats {Status}", (int)response.StatusCode);
            }
            else
            {
                var text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (text.Length > MaxAnnexStatsChars)
                    App.Logger?.Information("[ArcademyHost] annex-stats is {N} chars - ignored", text.Length);
                else
                    body = JObject.Parse(text);
            }
        }
        catch (Exception ex)
        {
            // Non-200, unparseable, timed out, no network: one line, and the pane says LINK DOWN.
            App.Logger?.Debug("ArcademyHost: annex-stats failed: {E}", ex.Message);
            body = null;
        }
        PostAnnexStats(body, epoch);
    }

    /// <summary>Post the one reply on the UI thread, dropping it if the window it was asked for is
    /// gone or has been relaunched underneath us. Never throws.</summary>
    private static void PostAnnexStats(JObject? body, int epoch)
    {
        try
        {
            var win = _host?.Window;
            if (win == null || Volatile.Read(ref _generation) != epoch) return;
            win.Dispatcher.BeginInvoke(() =>
            {
                try
                {
                    if (_host == null || Volatile.Read(ref _generation) != epoch) return;
                    _host.Post(new { type = "annex-stats", body });
                }
                catch (Exception ex) { App.Logger?.Debug("ArcademyHost.annex-stats post: {E}", ex.Message); }
            });
        }
        catch (Exception ex) { App.Logger?.Debug("ArcademyHost.PostAnnexStats: {E}", ex.Message); }
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

            // THE STUDENT ID follows the rung from EVERY surface - this page's chip, the app's own
            // Settings tab, the host applying it after an OAuth. The photo is the `discord` rung
            // (owner ruling 1), so the card's picture can only appear or vanish on this frame; the
            // `setting` echo below carries the rung but never the bytes. Pushed even when the page
            // wrote it itself, which is the case where the avatar has to arrive.
            if (e.PropertyName == nameof(Models.AppSettings.ArcademyPresenceShare))
            {
                PushProfile();
                // Climbing to `discord` on a machine that has never cached the picture: ask for it
                // now rather than at the next window open. A no-op when the cache is already current.
                if (PresenceShare(s) == "discord") KickAvatarRefresh();
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
                    Loc.GetF("arcademy_boot_error_body", ProductName, msg ?? string.Empty),
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

        // EMI Desk (MOMENTS `arcademyClosed`), the partner of the `arcademyOpened` fire in Launch.
        // Here rather than in CloseActive because this is the ONE funnel every exit reaches - the
        // graceful close, the watchdog, the window's own Closed event and app shutdown all land
        // here, and CloseActive is only one of the four. The stamp is cleared as it is read, so the
        // reentrant second pass says nothing.
        if (_emiOpenedUtc != DateTime.MinValue)
        {
            int emiMinutes = Math.Max(0, (int)(DateTime.UtcNow - _emiOpenedUtc).TotalMinutes);
            _emiOpenedUtc = DateTime.MinValue;
            try { App.EmiDesk?.Fire("arcademyClosed", new { minutes = emiMinutes }); } catch { }

            // ...and she comes home in whatever the Locker put her in. The live hook on
            // `meta-command` has normally already done this; this is the backstop for the session
            // that changed an outfit and never sent the message we expected (a page error, a
            // watchdog kill), so at worst the swap lands one Arcademy visit late instead of never.
            PushEmiOutfitToDesk();
        }

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
            // And the wallet with it. Nothing to flush here: a frame the server never took is
            // already on disk in `pendingMints`, and the next launch is what carries it up.
            try { ArcademyWalletSyncService.Detach(); } catch { }
            // Stop the presence poll BEFORE the host goes: the timer must never outlive the window
            // that armed it, and Detach also sends this session's one best-effort `campus_leave`.
            try { ArcademyPresenceService.Detach(); } catch { }
            // A link-up the player started from the student ID belongs to THIS window. No frame:
            // there is nothing left to paint it.
            try { CancelPendingLink("teardown", tellPage: false); } catch { }
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
