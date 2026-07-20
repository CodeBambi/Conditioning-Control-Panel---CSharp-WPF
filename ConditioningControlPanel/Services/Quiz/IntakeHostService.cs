using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Newtonsoft.Json.Linq;
using ConditioningControlPanel.Models;

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
    ///                  exit                           -&gt; graceful teardown
    ///
    /// Unlike DtRH this is a windowed Lab tool, not a screen-owning game: it does NOT minimise
    /// the main window, hosts nothing native (the effect layer is fully self-contained in-page),
    /// and needs no meta/loom/haptics plumbing. It keeps the same hardening the DtRH host has:
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

        public static bool IsActive => _host != null;

        /// <summary>The page reported boot-error this app session (a genuine load/init failure).
        /// The Lab entry point can check this to route to the classic quiz instead.</summary>
        public static bool BootFailedThisSession { get; private set; }

        /// <summary>Launch the intake window (idempotent). A running instance is just re-focused.</summary>
        public static void Launch(bool testMode = false)
        {
            if (_host != null) { _host.FocusWeb(); return; }
            try
            {
                _exiting = false;
                _testMode = testMode;

                var webRoot = Path.Combine(AppContext.BaseDirectory, "Resources", "web");
                var mappings = new List<(string, string, CoreWebView2HostResourceAccessKind)>
                {
                    // The page + its banks/<niche>.json + the shared ../dtrh/vendor three.js all
                    // live under this one origin (Deny = same-origin only, matches the DtRH host).
                    ("ccp.game", webRoot, CoreWebView2HostResourceAccessKind.Deny),
                    // Optional media the in-page effect layer may pull (gif bursts / bubble art).
                    // Allow (CORS-clean) so anything uploaded to WebGL/canvas resolves cleanly.
                    ("ccp.assets", App.EffectiveAssetsPath, CoreWebView2HostResourceAccessKind.Allow),
                };

                _host = new ChaosWebViewHost(new ChaosWebViewHost.Options
                {
                    StartUrl = "https://ccp.game/intake/index.html",
                    PrimaryHost = "ccp.game",
                    Mappings = mappings,
                    UserDataFolderName = "browser_data_intake",
                    InputEnabled = true,
                    // A normal titled, resizable window — the page's dock button can go borderless.
                    StartFullscreen = false,
                    WindowTitle = ProductName,
                    LogTag = "IntakeHost",
                    // The binaural audio bed must start without a user gesture.
                    ExtraBrowserArguments = "--autoplay-policy=no-user-gesture-required",
                    OnReady = OnPageReady,
                    OnMessage = OnPageMessage,
                    OnProcessFailed = OnProcessFailed,
                });
                _host.Show();
                // Windowed Lab tool: the user closes it via the title-bar X. Tear down cleanly so
                // IsActive resets and the heartbeat watchdog can't relaunch a window the user shut.
                if (_host.Window != null) _host.Window.Closed += (_, _) => DisposeAll();
                StartHeartbeatWatch();
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
                case "exit":       // page-initiated wind-down (its own exit affordance)
                    _exiting = true;
                    ArmExitWatchdog();
                    break;
                case "exit-done":
                    DisposeAll();
                    break;
            }
        }

        /// <summary>quiz-result { result: QuizRunResult } -&gt; deserialise + draft a themed CCP
        /// session via <see cref="QuizSessionGenerator"/>, then offer to save it (mirrors the
        /// classic quiz's "save session" flow). The window stays open — the page continues into
        /// its Recovery band / summary and the user closes it when done.</summary>
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
                try
                {
                    var session = QuizSessionGenerator.GenerateSession(run);
                    var fileService = new SessionFileService();
                    var dialog = new Microsoft.Win32.SaveFileDialog
                    {
                        Title = $"Save {ProductName} Session",
                        Filter = "Session files (*.session.json)|*.session.json",
                        FileName = SessionFileService.GetExportFileName(session),
                        DefaultExt = ".session.json",
                        InitialDirectory = SessionFileService.CustomSessionsFolder,
                    };
                    var owner = _host?.Window;
                    var shown = (owner != null && owner.IsLoaded) ? dialog.ShowDialog(owner) : dialog.ShowDialog();

                    if (shown == true)
                    {
                        fileService.ExportSession(session, dialog.FileName);
                        App.Logger?.Information("IntakeHostService: drafted session '{Name}' -> {Path}",
                            session.Name, dialog.FileName);
                    }
                }
                catch (Exception ex) { App.Logger?.Warning(ex, "IntakeHostService: session draft/save failed"); }
            });
        }

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

        /// <summary>Chosen niche for this run (Niche.* — bambi/drone/sissy). Best-effort mapped
        /// from the app's active content mode; defaults to bambi. A dedicated picker is a Phase-3
        /// UX concern.</summary>
        private static string SafeNiche()
        {
            try
            {
                return App.Settings?.Current?.ContentMode == ContentMode.SissyHypno ? "sissy" : "bambi";
            }
            catch { return "bambi"; }
        }

        /// <summary>Patreon access token for the /intake/ai bearer gate (same source every other
        /// AI feature uses). Empty string when unavailable — the page degrades to its local stub.</summary>
        private static string SafeAuthToken()
        {
            try { return App.Patreon?.GetAccessToken() ?? string.Empty; }
            catch { return string.Empty; }
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
                    Launch(wasTest);
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
                App.Logger?.Information("IntakeHostService: closed");
            }
            finally { _disposing = false; }
        }
    }
}
