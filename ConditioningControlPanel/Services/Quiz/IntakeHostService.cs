using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
                case "intake-close":   // "are you sure? -> Yes" jumpscare ABORT
                    OnIntakeClose();
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
                // Completing an intake earns XP (mirrors PopQuiz's 25-base): deeper descent and
                // affirmed mantras pay more, capped so endless laps can't farm it.
                try
                {
                    var xp = 25
                        + (int)Math.Round(Math.Clamp(run.PeakDepth, 0, 1) * 50)
                        + Math.Min(run.AffirmedMantras?.Count ?? 0, 5) * 5;
                    App.Progression?.AddXP(Math.Min(xp, 100), XPSource.Other);
                }
                catch (Exception ex) { App.Logger?.Debug("IntakeHostService: XP grant failed: {E}", ex.Message); }

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
                    gifs = Sample(gifs, rng, 10).Select(ToUrl).ToArray(),
                    images = Sample(stills, rng, 10).Select(ToUrl).ToArray(),
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
                // every whisper playback on the same SubAudioEnabled flag.
                bool audioEnabled = App.Settings?.Current?.SubAudioEnabled == true;

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
