using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Newtonsoft.Json.Linq;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Chaos;

namespace ConditioningControlPanel.Services.Quiz
{
    /// <summary>
    /// Host service for the "Graded Intake" web core (Resources/web/intake). Mirrors
    /// <see cref="ConditioningControlPanel.Services.Chaos.DtrhHostService"/>: it owns the
    /// input-receiving WebView2 window (via the reused <see cref="ChaosWebViewHost"/>), the
    /// virtual-host mappings (ccp.game -&gt; Resources/web, ccp.assets -&gt; the active preset),
    /// and speaks the intake bridge protocol defined in web-shim.js:
    ///
    ///   Host -&gt; Page:  init { config, ai }            (config = BootConfig fields;
    ///                                                    ai = { serverBase, authToken })
    ///   Page -&gt; Host:  ready · log                    (consumed inside ChaosWebViewHost)
    ///                  boot-error · heartbeat · pong
    ///                  quiz-result { result }         -&gt; C# QuizSessionGenerator drafts a session
    ///                  session-drafted { ok, name, path } &lt;- host's reply to quiz-result
    ///                  exit                           -&gt; graceful teardown
    ///
    /// Unlike DtRH this is a windowed Lab tool, not a screen-owning game: it hosts nothing native
    /// (the effect layer is fully self-contained in-page) and needs no meta/loom/haptics plumbing.
    /// It DOES duck the control panel out of the way at launch, but with a plain minimize rather
    /// than DtRH's tray tuck (see <see cref="DuckMainWindow"/>), and puts it back on every close
    /// path. It keeps the same hardening the DtRH host has:
    /// per-instance user-data folder, hardened settings (no devtools), navigation lockdown,
    /// queue-until-ready bridge, a heartbeat watchdog and a relaunch-once recovery ladder.
    /// </summary>
    internal static class IntakeHostService
    {
        /// <summary>The fiction name — mirrors contracts.PRODUCT_NAME's single-constant intent
        /// so a rename is a one-line change on each side.</summary>
        public const string ProductName = "Graded Intake";

        private const int Protocol = 1;

        /// <summary>AI server proxy base. MUST match <c>AiService.ProxyBaseUrl</c> — the server's
        /// <c>POST /intake/ai</c> gate (Agent H) expects the same Patreon bearer the app already
        /// uses for <c>/ai/chat</c>. Kept as a local constant to avoid taking a dependency on the
        /// AI service just for a URL.</summary>
        private const string ProxyBaseUrl = "https://codebambi-proxy.vercel.app";

        private static ChaosWebViewHost? _host;
        private static DispatcherTimer? _heartbeatWatch;
        private static DispatcherTimer? _exitWatchdog;
        private static DateTime _lastHeartbeatUtc;
        private static bool _exiting;
        private static bool _relaunchedOnce;
        private static bool _testMode;
        private static bool _disposing;   // reentrancy guard (Dispose closes the window -> Closed -> DisposeAll)
        private static bool _recoveryWindowed;   // this relaunch is a recovery: ignore the remembered fullscreen
        private static bool _duckPreference = true;            // did the caller want main ducked? survives a recovery relaunch
        private static bool _duckedMainWindow;                 // WE minimized main at launch, so WE owe a restore
        private static WindowState _mainStateBeforeDuck = WindowState.Normal;

        public static bool IsActive => _host != null;

        /// <summary>The page reported boot-error this app session (a genuine load/init failure).
        /// The Lab entry point can check this to route to the classic quiz instead.</summary>
        public static bool BootFailedThisSession { get; private set; }

        /// <summary>Launch the intake window (idempotent). A running instance is just re-focused.
        /// <paramref name="duckMainWindow"/> is opt-out for the onboarding case: see
        /// <see cref="DuckMainWindow"/> for why minimizing the control panel is normally right
        /// and why it is wrong on someone's very first run.</summary>
        public static void Launch(bool testMode = false, bool duckMainWindow = true)
        {
            if (_host != null) { _host.FocusWeb(); return; }
            try
            {
                // The intake's vo/sfx/music (plus the dtrh bubble sfx it borrows) ship as the lazy
                // audio-web pack. Fire-and-forget: no-op once installed, on a full install, or offline.
                try { _ = App.ReleaseContent?.RequestPackAsync(ReleaseContentService.PackAudioWeb); }
                catch (Exception ex) { App.Logger?.Debug("IntakeHost: audio-web request failed: {E}", ex.Message); }

                _exiting = false;
                _testMode = testMode;
                _duckPreference = duckMainWindow;

                var webRoot = Path.Combine(AppContext.BaseDirectory, "Resources", "web");
                var mappings = new List<(string, string, CoreWebView2HostResourceAccessKind)>
                {
                    // The page + its banks/<niche>.json + the shared ../dtrh/vendor three.js all
                    // live under this one origin (Deny = same-origin only, matches the DtRH host).
                    ("ccp.game", webRoot, CoreWebView2HostResourceAccessKind.Deny),
                    // Optional media the in-page effect layer may pull (gif bursts / bubble art).
                    // Allow (CORS-clean) so anything uploaded to WebGL/canvas resolves cleanly.
                    ("ccp.assets", App.EffectiveAssetsPath, CoreWebView2HostResourceAccessKind.Allow),
                    // CONTENT PACKS: the intake's vo/sfx/music (and the dtrh bubble sfx it borrows
                    // for chimes/pops) ship as a downloaded pack rather than in the installer. Same
                    // tree, different root - core/audioSrc.js just swaps the origin per file.
                    ChaosWebViewHost.ContentMapping(),
                };

                _host = new ChaosWebViewHost(new ChaosWebViewHost.Options
                {
                    StartUrl = "https://ccp.game/intake/index.html",
                    PrimaryHost = "ccp.game",
                    Mappings = mappings,
                    UserDataFolderName = "browser_data_intake",
                    InputEnabled = true,
                    // Window mode is REMEMBERED (AppSettings.IntakeFullscreen), so the window is
                    // BUILT in the mode the player left it in rather than flipping a beat after
                    // boot. A recovery relaunch always comes back windowed: if the page wedged
                    // once it may wedge again, and a titled window is the state every ordinary
                    // Windows exit still works from.
                    StartFullscreen = !_recoveryWindowed && App.Settings?.Current?.IntakeFullscreen == true,
                    // Keep the intake above MainWindow (native ownership, not Topmost) — main is
                    // raised by things the run doesn't control and used to bury the page.
                    OwnedByMainWindow = true,
                    WindowTitle = ProductName,
                    LogTag = "IntakeHost",
                    // The binaural audio bed must start without a user gesture.
                    ExtraBrowserArguments = "--autoplay-policy=no-user-gesture-required",
                    OnReady = OnPageReady,
                    OnMessage = OnPageMessage,
                    OnProcessFailed = OnProcessFailed,
                });
                _recoveryWindowed = false;   // consumed by the Options above; the next launch is normal again
                _host.Show();
                // Windowed Lab tool: the user closes it via the title-bar X. Tear down cleanly so
                // IsActive resets and the heartbeat watchdog can't relaunch a window the user shut.
                if (_host.Window != null) _host.Window.Closed += (_, _) => DisposeAll();
                StartHeartbeatWatch();
                if (duckMainWindow) DuckMainWindow();
                App.Logger?.Information("IntakeHostService: launched{T}", testMode ? " (test)" : "");
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "IntakeHostService.Launch failed");
                DisposeAll();
            }
        }

        /// <summary>Graceful close: ask the page to wind down, watchdog-force after 1200ms. Idempotent.</summary>
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
            catch (Exception ex) { App.Logger?.Debug("IntakeHostService.CloseActive: {E}", ex.Message); DisposeAll(); }
        }

        // ============================ ducking the control panel ============================

        /// <summary>
        /// Get the control panel out of the way once the intake window is up. Deliberately a plain
        /// MINIMIZE and not DtRH's <c>MinimizeToTrayForChaos</c> tray tuck: DtRH is screen-owning
        /// and takes the desktop for the length of a descent, whereas this is a windowed Lab tool -
        /// making the app's taskbar button disappear for it would read as a crash. Minimized, main
        /// is one Alt-Tab away and the intake keeps its own button beside it.
        ///
        /// This is only safe because <see cref="ChaosWebViewHost"/> now un-glues itself for the
        /// duration of an owner minimize: the intake is natively OWNED by main (so nothing in the
        /// app can bury it), and Windows hides owned windows along with a minimizing owner. Without
        /// that decoupling this call would minimize the game the moment it launched.
        ///
        /// No-op when main is already minimized or tray-hidden - the user's own last word on the
        /// window stands, and we take no restore debt we did not earn.
        /// </summary>
        private static void DuckMainWindow()
        {
            try
            {
                var main = (Window?)App.MainWindowRef ?? Application.Current?.MainWindow;
                if (main == null || !main.IsVisible || main.WindowState == WindowState.Minimized) return;
                _mainStateBeforeDuck = main.WindowState;   // a maximized panel must come back maximized
                main.WindowState = WindowState.Minimized;
                _duckedMainWindow = true;
                // Minimizing main hands activation to whatever is next in the z-order; take it back
                // so the page keeps keyboard focus from the first frame.
                _host?.FocusWeb();
                App.Logger?.Information("IntakeHostService: ducked MainWindow (was {S})", _mainStateBeforeDuck);
            }
            catch (Exception ex) { App.Logger?.Debug("IntakeHostService.DuckMainWindow: {E}", ex.Message); }
        }

        /// <summary>
        /// Undo <see cref="DuckMainWindow"/>. Called from <see cref="DisposeAll"/>, which is the
        /// single funnel every close path runs through - title-bar X (Window.Closed), the page's
        /// "are you sure? -&gt; Yes" abort, exit/exit-done, boot-error, the heartbeat/process-failed
        /// recovery ladder and <see cref="CloseActive"/>'s watchdog - because a tool that minimizes
        /// the app and then leaves it minimized on exit is a worse bug than the one the duck fixes.
        ///
        /// Two deliberate no-ops: main tray-hidden (not visible) or already restored by the user -
        /// in both cases they moved the window after we did, and their choice wins.
        /// </summary>
        private static void RestoreMainWindow()
        {
            if (!_duckedMainWindow) return;
            _duckedMainWindow = false;
            try
            {
                var disp = Application.Current?.Dispatcher;
                if (disp == null || disp.HasShutdownStarted) return;   // app is going away regardless
                var main = (Window?)App.MainWindowRef ?? Application.Current?.MainWindow;
                if (main == null || !main.IsVisible) return;
                if (main.WindowState != WindowState.Minimized) return;
                main.WindowState = _mainStateBeforeDuck == WindowState.Maximized
                    ? WindowState.Maximized
                    : WindowState.Normal;
                try { main.Activate(); } catch { }
            }
            catch (Exception ex) { App.Logger?.Debug("IntakeHostService.RestoreMainWindow: {E}", ex.Message); }
        }

        // ============================ boot ============================

        private static void OnPageReady()
        {
            try
            {
                _lastHeartbeatUtc = DateTime.UtcNow;
                // Claim keyboard focus so the page's inputs work from the first frame.
                _host?.FocusWeb();
                // web-shim.fromHostInit expects { type:'init', config:{...}, ai:{...} }.
                _host?.Post(new
                {
                    type = "init",
                    protocol = Protocol,
                    config = new
                    {
                        niche = SafeNiche(),
                        caps = BuildCaps(),
                        endless = false,          // opt-in only (BUILD_PLAN §0); default terminates via Recovery
                        steerValve = 1.0,         // full steering; a "play it straight" valve can wire here later
                        priorRun = (object?)null, // the page's own stats.js owns feed-forward (IndexedDB)
                        m2Test = _testMode,
                        // MIC GATE. The say-it mantra beat offers a "tap & say it"
                        // button; this is the app-wide consent flag every other mic
                        // consumer already honours (AutonomyService, Deeper's
                        // SpeakPromptSession). Without it the page probed only for the
                        // Web Speech API and offered the affordance regardless of
                        // whether the user had ever allowed the mic — the page cannot
                        // see the WPF setting on its own. False => beats.js renders the
                        // button disabled rather than live (type-it stays available).
                        micEnabled = App.Settings?.Current?.MicConsentGiven == true,
                        // Reward media for the in-page effect layer (gif bursts / jackpot
                        // spotlights). Served through the ccp.assets virtual host mapped above;
                        // a small random sample per launch keeps the init message light.
                        media = BuildMediaManifest(),
                        // Stable per-install fiction id ("Subject #0417" page-side). Host-supplied
                        // so it survives WebView2 user-data clears; page mints its own standalone.
                        subjectId = GetSubjectId(),
                        // The user's ENABLED subliminal pool, replicated in-page by render/subliminals.js
                        // (mirrors the WPF SubliminalService flash). Each entry carries its connected
                        // whisper clip as a data: URI when one exists on disk (the sub_audio / active-mod
                        // flashes_audio folders sit outside both virtual-host roots, so we inline the small
                        // clips the same way the bubble sprite rides this init message rather than add a
                        // new host mapping). Null when the user has no enabled phrases.
                        subliminals = BuildSubliminalPool(),
                    },
                    ai = new
                    {
                        serverBase = ProxyBaseUrl,
                        // The /intake/ai gate wants the Patreon access token — the SAME bearer the
                        // app's other AI features send. Empty when not logged in: the page's ai.js
                        // then falls back to its deterministic local stub (no network).
                        authToken = SafeAuthToken(),
                    },
                });
                // ...and tell the page which window it is actually painted in. ui/fullscreen.js
                // NEVER assumes: its affordances read the echoed state, so a run launched
                // straight into a remembered fullscreen still labels its exit correctly.
                if (_host != null) _host.Post(new { type = "fullscreen", on = _host.IsFullscreen });
                App.Logger?.Information("IntakeHostService: sent init (niche={N})", SafeNiche());
            }
            catch (Exception ex) { App.Logger?.Warning("IntakeHostService.OnPageReady: {E}", ex.Message); }
        }

        // ============================ page messages ============================

        private static void OnPageMessage(JObject o)
        {
            switch ((string?)o["type"])
            {
                case "heartbeat":
                    _lastHeartbeatUtc = DateTime.UtcNow;
                    break;
                case "pong":
                    _lastHeartbeatUtc = DateTime.UtcNow;
                    break;
                case "quiz-result":
                    OnQuizResult(o);
                    break;
                case "boot-error":
                    OnBootError((string?)o["msg"]);
                    break;
                case "fullscreen-set":   // pause menu / Options / F11: C# owns the borderless toggle
                    ApplyHostFullscreen((bool?)o["on"] ?? false);
                    break;
                case "exit":       // page-initiated wind-down (its own exit affordance)
                    _exiting = true;
                    ArmExitWatchdog();
                    break;
                case "intake-close":   // "are you sure? -> Yes" jumpscare ABORT
                    OnIntakeClose();
                    break;
                case "loom-save":      // outro spiral recap: "Keep it" -> the Spirals library
                    OnLoomSave(o);
                    break;
                case "intake-save-image":  // outro spiral recap: "Save PNG" -> intake_spirals/
                    OnSaveSpiralImage(o);
                    break;
                case "exit-done":
                    DisposeAll();
                    break;
            }
        }

        // ============================ window mode ============================

        /// <summary>
        /// Page-driven fullscreen, mirroring <see cref="Services.Chaos.DtrhHostService"/>: the
        /// page asks C# to borderless-toggle its OWN window instead of calling the browser
        /// Fullscreen API. That is not a style preference - the browser API eats the first
        /// Escape to leave fullscreen and a page cannot preventDefault it, and this page's
        /// Escape is already spoken for several rungs deep (steering's hijack disarm, the pause
        /// menu, the Options panel). Owning the window here leaves that ladder untouched.
        ///
        /// The resulting REAL window state is echoed back - never the requested one - so the
        /// page's labels can't claim a mode we failed to enter, and it is persisted so the next
        /// launch builds the window the player left in rather than flipping after boot.
        /// </summary>
        private static void ApplyHostFullscreen(bool on)
        {
            var disp = Application.Current?.Dispatcher;
            if (disp == null || disp.HasShutdownStarted) return;
            disp.BeginInvoke(() =>
            {
                try
                {
                    if (_host == null) return;
                    _host.SetFullscreen(on);
                    _host.Post(new { type = "fullscreen", on = _host.IsFullscreen });
                    var settings = App.Settings?.Current;
                    if (settings != null && settings.IntakeFullscreen != _host.IsFullscreen)
                    {
                        settings.IntakeFullscreen = _host.IsFullscreen;
                        App.Settings?.Save();
                    }
                }
                catch (Exception ex) { App.Logger?.Debug("IntakeHostService.fullscreen: {E}", ex.Message); }
            });
        }

        /// <summary>quiz-result { result: QuizRunResult } -&gt; deserialise + draft a themed CCP
        /// session via <see cref="QuizSessionGenerator"/>, then AUTO-SAVE it into the
        /// CustomSessions folder and announce it with a non-blocking toast. The window stays
        /// open — the page continues into its Recovery band / summary and the user closes it
        /// when done — so nothing here may block: the drafted session is the run's artifact and
        /// must not be destroyable by dismissing a dialog.</summary>
        private static void OnQuizResult(JObject o)
        {
            QuizRunResult? run = null;
            try { run = o["result"]?.ToObject<QuizRunResult>(); }
            catch (Exception ex) { App.Logger?.Warning("IntakeHostService: bad quiz-result: {E}", ex.Message); }
            if (run == null) return;

            App.Logger?.Information(
                "IntakeHostService: quiz-result (niche={N}, peakDepth={D:0.00}, band={B}, mantras={M})",
                run.Niche, run.PeakDepth, run.DeepestBand, run.AffirmedMantras?.Count ?? 0);

            var disp = Application.Current?.Dispatcher;
            if (disp == null || disp.HasShutdownStarted) return;
            disp.BeginInvoke(() =>
            {
                // Completing an intake earns XP (mirrors PopQuiz's 25-base): deeper descent and
                // affirmed mantras pay more, capped so endless laps can't farm it.
                try
                {
                    var xp = 25
                        + (int)Math.Round(Math.Clamp(run.PeakDepth, 0, 1) * 50)
                        + Math.Min(run.AffirmedMantras?.Count ?? 0, 5) * 5;
                    App.Progression?.AddXP(Math.Min(xp, 100), XPSource.Other);

                    // Affirmed mantras also feed the mantra quest/program verifier, same cap
                    // as the XP above so endless laps can't farm program days. XP was already
                    // granted in the sum - this is credit only.
                    var affirmed = Math.Min(run.AffirmedMantras?.Count ?? 0, 5);
                    for (var i = 0; i < affirmed; i++)
                        App.Quests?.TrackMantraCompleted();
                }
                catch (Exception ex) { App.Logger?.Debug("IntakeHostService: XP grant failed: {E}", ex.Message); }

                // SPEND THE WEEKLY PASS - here, and nowhere else. Deliberately NOT at launch:
                // the run is only over once a quiz-result has actually arrived, so a crash, a
                // WebView2 process failure or the "are you sure? -> Yes" abort all cost nothing.
                // A no-op for patrons, who never took a pass to get in.
                //
                // Outside the session-draft try below on purpose: the intake WAS completed even
                // if drafting the session then fails, and the user should not be charged twice
                // for our own error.
                try { App.IntakePass?.ConsumeForCompletedIntake(); }
                catch (Exception ex) { App.Logger?.Debug("IntakeHostService: pass consume failed: {E}", ex.Message); }

                try
                {
                    // AUTO-SAVE, NO DIALOG. This used to put the drafted session behind a modal
                    // SaveFileDialog: a run the user had just spent forty minutes descending
                    // through was destroyed by one Escape key, with no recovery path and no
                    // second chance (the QuizRunResult is not retained). The session is the
                    // artifact of the run, so it is written straight into the CustomSessions
                    // folder - where the Sessions tab already enumerates it - and the user is
                    // told about it with a non-blocking toast instead of being interrupted
                    // mid-Recovery-band by a file picker.
                    var session = QuizSessionGenerator.GenerateSession(run);
                    var fileService = new SessionFileService();
                    fileService.EnsureCustomFolderExists();

                    var path = UniqueSessionPath(SessionFileService.GetExportFileName(session));
                    fileService.ExportSession(session, path);
                    session.SourceFilePath = path;

                    App.Logger?.Information("IntakeHostService: drafted session '{Name}' -> {Path}",
                        session.Name, path);

                    // TELL THE RUNNING APP, NOT JUST THE DISK. BUG #614: writing the file was
                    // the whole fix once, but SessionManager.LoadAllSessions() runs exactly
                    // once per launch (MainWindow.InitializeSessionManager), so a session
                    // dropped into CustomSessions after startup stayed invisible in the
                    // Sessions tab until the app was restarted. The user's reasonable reading
                    // of that was "the intake never made a session".
                    //
                    // This is a SEPARATE try/catch on purpose. The file is already on disk and
                    // is the run's artifact; a UI-refresh failure (no MainWindow yet, torn-down
                    // window, PresetsTab not realised) must never be allowed to escalate into
                    // the "draft failed" path below and tell the user they lost the run. It is
                    // also why the session-drafted reply is not sent from in here.
                    try
                    {
                        var main = App.MainWindowRef;
                        if (main != null) main.RegisterExternallySavedSession(session, path);
                        else App.Logger?.Debug(
                            "IntakeHostService: no MainWindow to register the drafted session with; "
                            + "it will appear in the Sessions tab on next launch.");
                    }
                    catch (Exception ex)
                    {
                        App.Logger?.Warning(ex,
                            "IntakeHostService: drafted session saved but the Sessions list refresh failed");
                    }

                    // HALF A PUNCH. Completing the intake queues the stamp; the hole only lands
                    // once this session has actually been run (IntakePunchCardService). Queued
                    // against the session id so nothing but the session this run produced can
                    // redeem it.
                    try { App.IntakePunchCard?.NotifyIntakeCompleted(session.Id); }
                    catch (Exception ex) { App.Logger?.Debug("IntakeHostService: punch queue failed: {E}", ex.Message); }

                    // The app's standard in-app toast surface (MainWindow's NotificationHost).
                    // It queues internally when the host isn't attached yet, so this is safe
                    // even if the intake window somehow outlives the main window's layout.
                    //
                    // The action used to be "Show folder", which answered a question nobody was
                    // asking: the run's whole point is the session it drafted, and the next step
                    // is to RUN it, not to look at it on disk. RevealSessionInLibrary re-selects
                    // by id (the toast can outlive the selection) and BtnStartSession_Click keeps
                    // the normal confirmation, so nothing starts an hour-long session by surprise.
                    var sessionId = session.Id;
                    App.Notifications?.Show(
                        $"Session drafted: \"{session.Name}\" - run it to stamp your punch card.",
                        NotificationType.Success,
                        TimeSpan.FromSeconds(12),
                        "Run it now",
                        () =>
                        {
                            try
                            {
                                var mw = App.MainWindowRef;
                                if (mw == null) return;
                                if (mw.RevealSessionInLibrary(sessionId))
                                    mw.BtnStartSession_Click(mw, new RoutedEventArgs());
                            }
                            catch (Exception ex) { App.Logger?.Debug("IntakeHostService: run-it-now failed: {E}", ex.Message); }
                        });

                    // TELL THE PAGE. quiz-result used to be one-way, so the certificate could
                    // only ever say "Session drafting..." and then sit there forever - the run's
                    // single most important outcome rendered as a spinner that never resolves.
                    // The page now names the session it earned. This is also the only honest
                    // source for the Records Office's session name: without it the archive has
                    // to re-derive the name by mirroring GetFallbackContent, which silently
                    // drifts the moment that rule changes.
                    _host?.Post(new { type = "session-drafted", ok = true, name = session.Name, path });
                }
                catch (Exception ex)
                {
                    App.Logger?.Warning(ex, "IntakeHostService: session draft/save failed");
                    // Failure is reported too: a certificate stuck on "drafting" is worse than
                    // one that says the draft failed, because only the latter tells the user to
                    // stop waiting. Their answers are still recorded either way.
                    try { _host?.Post(new { type = "session-drafted", ok = false, name = (string?)null, path = (string?)null }); }
                    catch { }
                }
            });
        }

        /// <summary>A free path under CustomSessions for <paramref name="fileName"/> (which always
        /// ends in <c>.session.json</c>). Two intakes on the same niche at the same tier produce
        /// the same name, so collisions are the norm, not the exception: suffix <c>-2</c>,
        /// <c>-3</c>, ... rather than silently overwriting the earlier draft. The counter is
        /// bounded so a pathological folder can't spin here; the last candidate is taken as-is.</summary>
        private static string UniqueSessionPath(string fileName)
        {
            const string ext = ".session.json";
            var stem = fileName.EndsWith(ext, StringComparison.OrdinalIgnoreCase)
                ? fileName[..^ext.Length]
                : Path.GetFileNameWithoutExtension(fileName);
            if (string.IsNullOrWhiteSpace(stem)) stem = "intake-session";

            var folder = SessionFileService.CustomSessionsFolder;
            var candidate = Path.Combine(folder, stem + ext);
            for (var i = 2; i <= 999 && File.Exists(candidate); i++)
                candidate = Path.Combine(folder, $"{stem}-{i}{ext}");
            return candidate;
        }

        /// <summary>The in-page "are you sure? -&gt; Yes" jumpscare asks the window to close as an
        /// ABORT: unlike the natural quiz end (a <c>quiz-result</c> that drafts a session), NOTHING
        /// is reported here — we just tear the host down on the dispatcher, reusing the same
        /// <see cref="DisposeAll"/> path the natural close and the title-bar X use, so no
        /// <see cref="QuizRunResult"/> is generated. <c>_exiting</c> is set first so the heartbeat
        /// watchdog can't relaunch the window the user is being kicked out of.</summary>
        private static void OnIntakeClose()
        {
            _exiting = true;
            var disp = Application.Current?.Dispatcher;
            if (disp == null || disp.HasShutdownStarted) { DisposeAll(); return; }
            disp.BeginInvoke(() => DisposeAll());
        }

        // ============================ outro spiral recap ============================

        /// <summary>Folder the outro's "Save PNG" action writes to:
        /// <c>%APPDATA%/ConditioningControlPanel/intake_spirals</c>. Deliberately NOT the
        /// assets folder — a run's souvenir spirals should not silently join the user's flash
        /// image pool — and deliberately NOT <see cref="DtrhLoomStore.SpiralsFolder"/>, whose
        /// contents the Loom rack enumerates. Its own folder beside the rest of the user data,
        /// the same convention every other intake artifact (intake_subject.txt) follows.</summary>
        private static string SpiralImageFolder => Path.Combine(App.UserDataPath, "intake_spirals");

        private const int MaxSpiralPngBase64Chars = 12 * 1024 * 1024;  // ~9MB decoded ceiling

        /// <summary>
        /// <c>loom-save { name, params, gifBase64, overwrite }</c> — byte-for-byte the message
        /// THE LOOM's own editor sends, so the intake's recap spirals land in the SAME library
        /// (<see cref="DtrhLoomStore"/>: slug whitelist, 12-file cap, GIF magic validation) and
        /// show up in the Loom rack / SpiralFeatureControl with zero extra plumbing. The page's
        /// params are schema-v2 loom params (they came out of <c>loomField.randomParams2</c>),
        /// so the sidecar round-trips and the spiral re-opens in the editor unchanged.
        /// </summary>
        private static void OnLoomSave(JObject o)
        {
            try
            {
                var (ok, slug, error) = DtrhLoomStore.Save(
                    (string?)o["name"], (string?)o["gifBase64"], o["params"], (bool?)o["overwrite"] ?? false);
                _host?.Post(new { type = "loom-result", op = "save", ok, slug, error });
                if (ok) App.Logger?.Information("IntakeHostService: recap spiral kept as {Slug}", slug);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning("IntakeHostService.OnLoomSave: {E}", ex.Message);
                try { _host?.Post(new { type = "loom-result", op = "save", ok = false, slug = (string?)null, error = "io-failed" }); }
                catch { }
            }
        }

        /// <summary>
        /// <c>intake-save-image { pngBase64, index }</c> — write one recap spiral as a PNG under
        /// <see cref="SpiralImageFolder"/> and answer with the full path so the page can tell the
        /// user exactly where it went. Validation is C#-authoritative: base64 length ceiling, PNG
        /// magic, and a filename built entirely here (the page contributes only a clamped index),
        /// so nothing the page sends can steer the write out of the folder.
        /// </summary>
        private static void OnSaveSpiralImage(JObject o)
        {
            string? error = null;
            string? path = null;
            try
            {
                var b64 = (string?)o["pngBase64"];
                if (string.IsNullOrEmpty(b64) || b64.Length > MaxSpiralPngBase64Chars)
                {
                    error = "too-big";
                }
                else
                {
                    byte[] bytes;
                    try { bytes = Convert.FromBase64String(b64); }
                    catch { bytes = Array.Empty<byte>(); }

                    if (!LooksLikePng(bytes))
                    {
                        error = "bad-image";
                    }
                    else
                    {
                        Directory.CreateDirectory(SpiralImageFolder);
                        var index = Math.Clamp((int?)o["index"] ?? 1, 1, 99);
                        var name = $"intake-spiral-{DateTime.Now:yyyyMMdd-HHmmss}-{index:D2}.png";
                        var full = Path.Combine(SpiralImageFolder, name);
                        File.WriteAllBytes(full, bytes);
                        path = full;
                        App.Logger?.Information("IntakeHostService: saved recap spiral -> {Path}", full);
                    }
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning("IntakeHostService.OnSaveSpiralImage: {E}", ex.Message);
                error = "io-failed";
            }

            try { _host?.Post(new { type = "intake-save-image-result", ok = error == null, path, error }); }
            catch { }
        }

        /// <summary>8-byte PNG signature — enough to reject arbitrary bytes.</summary>
        private static bool LooksLikePng(byte[] b) =>
            b.Length > 8 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47
            && b[4] == 0x0D && b[5] == 0x0A && b[6] == 0x1A && b[7] == 0x0A;

        private static void OnBootError(string? msg)
        {
            App.Logger?.Warning("IntakeHostService: page boot-error: {Msg}", msg);
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

        // ============================ init payload helpers ============================

        /// <summary>User effect caps in the DEFAULT_CAPS shape (contracts.js). The web core already
        /// clamps depth/effect intensities in-page against these; v1 ships full (1.0) caps — real
        /// per-channel caps can be wired from settings later without touching the page.</summary>
        private static object BuildCaps() => new
        {
            flashRate = 1.0,
            flashOpacity = 1.0,
            subDensity = 1.0,
            duckDepth = 1.0,
            bubbleRate = 1.0,
            binauralDepth = 1.0,
            bgIntensity = 1.0,
            masterIntensity = 1.0,
        };

        /// <summary>Chosen niche for this run — DesiredNiche() clamped to a niche that actually has
        /// a prompt bank on disk. A niche with no banks/&lt;niche&gt;.json would drop the page onto the
        /// engine's tiny placeholder bank, so an unbacked niche degrades to bambi LOUDLY (one warning
        /// per app session) instead of looking intentional. Drop the bank in and the clamp lifts.</summary>
        private static string SafeNiche()
        {
            var want = DesiredNiche();
            if (BankExists(want)) return want;
            if (!_bankFallbackWarned)
            {
                _bankFallbackWarned = true;
                App.Logger?.Warning(
                    "IntakeHostService: niche '{N}' has no prompt bank (Resources/web/intake/banks/{N}.json) — serving the bambi bank instead. Author that bank to fix.",
                    want, want);
            }
            return "bambi";
        }

        /// <summary>True once the missing-bank warning has been logged this app session.</summary>
        private static bool _bankFallbackWarned;

        /// <summary>Does banks/&lt;niche&gt;.json ship with this build? Errs on the side of "yes" so an
        /// IO hiccup never silently downgrades a niche that does have content.</summary>
        private static bool BankExists(string niche)
        {
            if (niche == "bambi") return true;
            try
            {
                return File.Exists(Path.Combine(
                    AppContext.BaseDirectory, "Resources", "web", "intake", "banks", niche + ".json"));
            }
            catch { return true; }
        }

        /// <summary>Niche the ACTIVE MOD asks for (Niche.* — bambi/drone/sissy/circe), before the
        /// bank-availability clamp. Mapped from the mod, not the legacy two-value ContentMode enum
        /// (which has no drone value and collapses every non-sissy mod to BambiSleep — that mapping
        /// served drone-mode users the whole bambi prompt bank). Built-in ids win; third-party mods
        /// declare theirs via a manifest tag. Defaults to bambi. A dedicated picker is a Phase-3 UX
        /// concern. The mapping itself now lives in <see cref="IntakeNiche"/> so the weekly pass
        /// card and its nudge popup show art for the same niche this picks a prompt bank for.</summary>
        private static string DesiredNiche() => IntakeNiche.Current();

        /// <summary>Patreon access token for the /intake/ai bearer gate (same source every other
        /// AI feature uses). Empty string when unavailable — the page degrades to its local stub.</summary>
        private static string SafeAuthToken()
        {
            try { return App.Patreon?.GetAccessToken() ?? string.Empty; }
            catch { return string.Empty; }
        }

        /// <summary>MediaManifest (contracts.js): a small random sample of the user's flash images,
        /// split gifs/stills, as ccp.assets URLs. Null on any failure — the page's effect layer
        /// falls back to its particle stand-ins.</summary>
        private static object? BuildMediaManifest()
        {
            try
            {
                var gifs = new List<string>();
                var stills = new List<string>();
                var imagesRoot = Path.Combine(App.EffectiveAssetsPath, "images");
                if (Directory.Exists(imagesRoot))
                {
                    foreach (var file in Directory.EnumerateFiles(imagesRoot, "*", SearchOption.AllDirectories))
                    {
                        var ext = Path.GetExtension(file).ToLowerInvariant();
                        if (ext == ".gif") gifs.Add(file);
                        else if (ext is ".png" or ".jpg" or ".jpeg" or ".webp") stills.Add(file);
                    }
                }

                // The bubble sprite + subliminal phrases ride the manifest too, so the page can
                // still get them when the user has no images folder at all.
                var bubbleSprite = BuildBubbleSpriteDataUri();
                var subliminals = SampleActiveSubliminals();
                if (gifs.Count == 0 && stills.Count == 0 && bubbleSprite == null && subliminals == null)
                    return null;

                var rng = new Random();
                static List<string> Sample(List<string> pool, Random r, int take)
                {
                    // partial Fisher-Yates: take random items without shuffling the whole list
                    for (int i = 0; i < Math.Min(take, pool.Count); i++)
                    {
                        int j = r.Next(i, pool.Count);
                        (pool[i], pool[j]) = (pool[j], pool[i]);
                    }
                    return pool.GetRange(0, Math.Min(take, pool.Count));
                }
                string ToUrl(string file)
                {
                    var rel = Path.GetRelativePath(App.EffectiveAssetsPath, file).Replace('\\', '/');
                    var escaped = string.Join('/', rel.Split('/').Select(Uri.EscapeDataString));
                    return "https://ccp.assets/" + escaped;
                }

                return new
                {
                    // 18 samples (was 10): the captcha grid items want 15-20 mounted
                    // assets so tiles don't repeat within a 3x3; gifs/stills split is
                    // unchanged. Host-side only (PROTOCOL 1 + web-shim.js untouched).
                    gifs = Sample(gifs, rng, 18).Select(ToUrl).ToArray(),
                    images = Sample(stills, rng, 18).Select(ToUrl).ToArray(),
                    bubbleSprite,
                    subliminals,
                };
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("IntakeHostService.BuildMediaManifest: {E}", ex.Message);
                return null;
            }
        }

        /// <summary>The app's REAL bubble sprite (mod-aware: an active BS/sissy mod's bubble.png
        /// wins, same resolution order <see cref="BubbleService"/> uses), PNG-encoded as a data:
        /// URI so the page needs no extra virtual host. ~50-150KB riding the init message.</summary>
        private static string? BuildBubbleSpriteDataUri()
        {
            try
            {
                var src = ModResourceResolver.ResolveImage("bubble.png") as System.Windows.Media.Imaging.BitmapSource;
                if (src == null)
                {
                    var uri = new Uri("pack://application:,,,/Resources/bubble.png", UriKind.Absolute);
                    src = new System.Windows.Media.Imaging.BitmapImage(uri);
                }
                var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
                enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(src));
                using var ms = new MemoryStream();
                enc.Save(ms);
                return "data:image/png;base64," + Convert.ToBase64String(ms.ToArray());
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("IntakeHostService.BuildBubbleSpriteDataUri: {E}", ex.Message);
                return null;
            }
        }

        /// <summary>The user's ACTIVE subliminal phrases (SubliminalPool entries toggled on),
        /// shuffled + capped so the init message stays small. Null when none are active.</summary>
        private static string[]? SampleActiveSubliminals()
        {
            try
            {
                var pool = App.Settings.Current.SubliminalPool;
                var active = pool?.Where(kvp => kvp.Value).Select(kvp => kvp.Key)
                    .Where(t => !string.IsNullOrWhiteSpace(t)).ToList();
                if (active == null || active.Count == 0) return null;
                var rng = new Random();
                for (int i = active.Count - 1; i > 0; i--)
                {
                    int j = rng.Next(i + 1);
                    (active[i], active[j]) = (active[j], active[i]);
                }
                return active.Take(40).ToArray();
            }
            catch { return null; }
        }

        // Audio inlined into the init config: only enabled phrases, and only when a matching clip
        // exists. Small per-clip + total budgets keep the message light (the whisper mp3s are
        // ~15-55KB each); the page re-guards clip length (>8s skipped) at play time.
        private const long SubClipMaxBytes = 512 * 1024;          // skip a single clip larger than this
        private const long SubAudioBudgetBytes = 6 * 1024 * 1024; // total raw audio inlined per launch
        private static readonly string[] SubAudioExtsLower = { ".mp3", ".wav", ".ogg" };

        /// <summary>The user's ENABLED subliminal phrases as <c>{ text, audio? }</c> entries for the
        /// web core's render/subliminals.js. <c>audio</c> is a base64 data: URI of the phrase's
        /// connected whisper clip when one exists (resolved with the SAME text→file matching
        /// <see cref="ConditioningControlPanel.Services.SubliminalService"/> uses: active-mod
        /// <c>resources/sounds/flashes_audio</c> first, then the default <c>Resources/sub_audio</c>).
        /// Shuffled + capped so the init message stays light; null when no phrases are enabled.</summary>
        private static object[]? BuildSubliminalPool()
        {
            try
            {
                var pool = App.Settings?.Current?.SubliminalPool;
                var active = pool?.Where(kvp => kvp.Value).Select(kvp => kvp.Key)
                    .Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim()).ToList();
                if (active == null || active.Count == 0) return null;

                var rng = new Random();
                for (int i = active.Count - 1; i > 0; i--)
                {
                    int j = rng.Next(i + 1);
                    (active[i], active[j]) = (active[j], active[i]);
                }
                if (active.Count > 400) active = active.GetRange(0, 400);

                // Resolve the two audio dirs once (both may sit outside the ccp.game/ccp.assets roots,
                // which is exactly why clips are inlined below instead of served by URL).
                var defaultAudioDir = Path.Combine(AppContext.BaseDirectory, "Resources", "sub_audio");
                string? modAudioDir = null;
                try
                {
                    var modPath = App.Mods?.ActiveMod?.InstalledPath;
                    if (!string.IsNullOrEmpty(modPath))
                    {
                        var d = Path.Combine(modPath, "resources", "sounds", "flashes_audio");
                        if (Directory.Exists(d)) modAudioDir = d;
                    }
                }
                catch { /* mod audio is best-effort */ }

                // Respect the WPF global subliminal-audio toggle: when it's OFF, whispers
                // are off everywhere, so the intake ships text-only entries (no audio
                // resolution / inlining at all). Mirrors SubliminalService, which gates
                // every whisper playback on the same SubAudioAudible flag (enabled AND not muted).
                bool audioEnabled = App.Settings?.Current?.SubAudioAudible == true;

                long budget = SubAudioBudgetBytes;
                var list = new List<object>(active.Count);
                foreach (var text in active)
                {
                    string? dataUri = null;
                    if (audioEnabled)
                    {
                        try
                        {
                            var file = ResolveSubliminalAudioFile(text, modAudioDir, defaultAudioDir);
                            if (file != null)
                            {
                                var len = new FileInfo(file).Length;
                                if (len > 0 && len <= SubClipMaxBytes && len <= budget)
                                {
                                    var bytes = File.ReadAllBytes(file);
                                    dataUri = "data:" + MimeForAudio(file) + ";base64," + Convert.ToBase64String(bytes);
                                    budget -= bytes.Length;
                                }
                            }
                        }
                        catch { dataUri = null; } // any read failure = text-only for this phrase
                    }

                    list.Add(dataUri != null ? (object)new { text, audio = dataUri } : new { text });
                }
                return list.ToArray();
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("IntakeHostService.BuildSubliminalPool: {E}", ex.Message);
                return null;
            }
        }

        /// <summary>Find the whisper clip for <paramref name="text"/>, mirroring
        /// <c>SubliminalService.FindLinkedAudio</c>: exact filename match against case/apostrophe
        /// variants, then a case-insensitive directory scan. Mod dir wins over the default dir.</summary>
        private static string? ResolveSubliminalAudioFile(string text, string? modDir, string defaultDir)
        {
            var clean = text.Trim();
            var variants = new[]
            {
                clean,
                clean.ToUpperInvariant(),
                clean.ToLowerInvariant(),
                clean.Replace('’', '\''),
                clean.Replace('\'', '’'),
                clean.ToUpperInvariant().Replace('’', '\''),
            };
            var exts = new[] { ".mp3", ".wav", ".ogg", ".MP3", ".WAV", ".OGG" };

            foreach (var dir in new[] { modDir, defaultDir })
            {
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) continue;

                foreach (var v in variants)
                    foreach (var ext in exts)
                    {
                        var p = Path.Combine(dir, v + ext);
                        if (File.Exists(p)) return p;
                    }

                try
                {
                    var norm = clean.ToUpperInvariant().Replace('’', '\'');
                    foreach (var f in Directory.GetFiles(dir))
                    {
                        if (Array.IndexOf(SubAudioExtsLower, Path.GetExtension(f).ToLowerInvariant()) < 0) continue;
                        var name = Path.GetFileNameWithoutExtension(f).ToUpperInvariant().Replace('’', '\'');
                        if (name == norm) return f;
                    }
                }
                catch { /* scan is best-effort */ }
            }
            return null;
        }

        private static string MimeForAudio(string file) => Path.GetExtension(file).ToLowerInvariant() switch
        {
            ".wav" => "audio/wav",
            ".ogg" => "audio/ogg",
            _ => "audio/mpeg",
        };

        /// <summary>Stable per-install subject number (4 digits, e.g. "0417") persisted beside the
        /// user data. Kept OUT of AppSettings on purpose: it is pure fiction, not a setting.</summary>
        private static string GetSubjectId()
        {
            try
            {
                var path = Path.Combine(App.UserDataPath, "intake_subject.txt");
                if (File.Exists(path))
                {
                    var existing = File.ReadAllText(path).Trim();
                    if (existing.Length is > 0 and <= 8) return existing;
                }
                var id = new Random().Next(1, 10000).ToString("D4");
                Directory.CreateDirectory(App.UserDataPath);
                File.WriteAllText(path, id);
                return id;
            }
            catch { return "0000"; }
        }

        // ============================ watchdogs / recovery ============================

        private static void StartHeartbeatWatch()
        {
            StopHeartbeatWatch();
            _lastHeartbeatUtc = DateTime.UtcNow;
            _heartbeatWatch = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            _heartbeatWatch.Tick += (_, _) =>
            {
                // Only after the page is live (it beats via rAF once booted) so a still-loading
                // page can't false-trip. A wedged main thread also kills the page's own exit path,
                // so the watchdog must exist even though this is only a windowed tool.
                if (_host == null || !_host.IsReady || _exiting) return;
                var silent = (DateTime.UtcNow - _lastHeartbeatUtc).TotalSeconds;
                if (silent > 20)
                {
                    App.Logger?.Warning("IntakeHostService: page heartbeat silent >20s - recovering");
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

        /// <summary>Relaunch once per session; a second failure gives up cleanly.</summary>
        private static void Recover(string reason)
        {
            var disp = Application.Current?.Dispatcher;
            if (disp == null) { DisposeAll(); return; }
            disp.BeginInvoke(() =>
            {
                var retry = !_relaunchedOnce;
                var wasTest = _testMode;
                App.Logger?.Warning("IntakeHostService: recovery ({Reason}) - {Action}",
                    reason, retry ? "relaunching once" : "giving up");
                DisposeAll();
                if (retry)
                {
                    _relaunchedOnce = true;
                    // Come back WINDOWED regardless of the remembered mode. The page just wedged
                    // or died; if the relaunch does the same, a titled window still has a close
                    // button and the remembered preference is untouched for the next real launch.
                    _recoveryWindowed = true;
                    // Carry the original duck choice through: a first-ever run that crashes and
                    // relaunches must not suddenly minimize the control panel out from under it.
                    Launch(wasTest, _duckPreference);
                }
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
                CancelExitWatchdog();
                StopHeartbeatWatch();
                try { _host?.Dispose(); } catch { }
                _host = null;
                _exiting = false;
                // Give the control panel back before anything else can claim the foreground.
                RestoreMainWindow();
                App.Logger?.Information("IntakeHostService: closed");
            }
            finally { _disposing = false; }
        }
    }
}
