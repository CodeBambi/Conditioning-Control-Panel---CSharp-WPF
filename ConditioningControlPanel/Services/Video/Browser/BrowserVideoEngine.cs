using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Newtonsoft.Json.Linq;
using Screen = System.Windows.Forms.Screen;

namespace ConditioningControlPanel.Services.Video.Browser
{
    /// <summary>What the host wants played, and how the host wants its windows dressed.</summary>
    internal sealed class BrowserVideoRequest
    {
        /// <summary>Absolute path of the clip. Must resolve to a mapped virtual host - see
        /// <see cref="BrowserVideoEngine.BuildPageUrl"/>.</summary>
        public required string Path { get; init; }

        /// <summary>The audio-bearing monitor.</summary>
        public required Screen PrimaryScreen { get; init; }

        /// <summary>Extra monitors to fill (already filtered by the caller's monitor-count cap, #389).
        /// These load muted.</summary>
        public IReadOnlyList<Screen> SecondaryScreens { get; init; } = Array.Empty<Screen>();

        /// <summary>Master x video volume, 0..1. External mute folds in through <see cref="Muted"/>.</summary>
        public double Volume { get; init; } = 1.0;

        public bool Muted { get; init; }

        /// <summary>Blurred-background (TikTok fill) composite instead of black bars. Page-side.</summary>
        public bool BlurBackground { get; init; }

        /// <summary>Hide the mouse pointer over the surface. Default FALSE, which is what the LibVLC
        /// video windows do - a surface that eats the cursor while the rest of the app still has one
        /// reads as a frozen machine, and mouse-driven hosts (BubbleCount) need it visible.</summary>
        public bool HideCursor { get; init; }

        /// <summary>Start position for the chaos random-segment mode. 0 = from the top.</summary>
        public long StartAtMs { get; init; }

        /// <summary>
        /// This clip runs under a strict lock, as a FIXED fact of the session rather than the live
        /// probe <see cref="IsHostStrict"/> answers. It rides the page's <c>load</c> message, where
        /// it arms the Media Session guard: a focused WebView2 owns the OS media session, so without
        /// it a Bluetooth headset's play/pause tap pauses the element behind our backs.
        /// </summary>
        public bool Strict { get; init; }

        /// <summary>Runs on each window after construction and BEFORE Show() - the host's hook for
        /// strict handlers and anything that must be wired before the HWND is realized.</summary>
        public Action<Window, Screen, bool>? ConfigureBeforeShow { get; init; }

        /// <summary>Runs on each window right after Show() - z-order guards and the like.</summary>
        public Action<Window, Screen, bool>? ConfigureAfterShow { get; init; }

        /// <summary>True while the host is deliberately holding playback (grace pause). The
        /// first-frame watchdog must not count that time, or a pause taken before the first frame
        /// would fake a decode failure and force a pointless LibVLC fallback.</summary>
        public Func<bool>? IsHostPaused { get; init; }

        /// <summary>True while this clip is running under a STRICT lock (Lock Card / Lockdown /
        /// Possession), where every screen is covered on purpose and Alt+F4 is refused. A dead mirror
        /// is then left on screen as an opaque black window rather than hidden - see
        /// <c>VideoSurfaceHealth.ShouldUncoverDeadSurface</c>. Absent = never strict.</summary>
        public Func<bool>? IsHostStrict { get; init; }
    }

    /// <summary>
    /// Out-of-process video playback for mandatory videos: one WebView2 per monitor running the
    /// player page (<c>Resources/web/player/index.html</c>), driven over the JSON bridge in
    /// docs/BROWSER_VIDEO_ENGINE_PLAN.md §3.
    ///
    /// Why it exists: LibVLC renders IN-PROCESS and can wedge the WPF dispatcher (the whole
    /// quarantine/retire/wedge-ladder museum in VideoService exists to survive that). WebView2 media
    /// is out-of-process - a dead renderer is a <c>ProcessFailed</c> event, not a frozen desktop.
    ///
    /// This class owns ONLY the surfaces and the bridge. Scheduling, selection, XP, attention checks,
    /// ducking, safety timers, strict mode and every event consumers see stay in VideoService, which
    /// is what makes a browser session indistinguishable from a LibVLC one.
    ///
    /// It NEVER throws to callers: every failure is a logged event plus <see cref="Failed"/>, which
    /// the host answers by replaying the same file through LibVLC.
    /// </summary>
    internal sealed class BrowserVideoEngine
    {
        // ---- virtual hosts (folders are created before mapping: a missing folder makes WebView2
        // silently SKIP the mapping, which surfaces much later as a 404 on the video) ----
        private const string GameHost = "ccp.game";
        private const string AssetsHost = "ccp.assets";
        private const string PacksHost = "ccp.packs";
        private const string StartUrl = "https://" + GameHost + "/player/index.html";

        /// <summary>How long a session may go without a <c>playing</c> report ONCE THE PAGE IS UP.
        /// The page runs the same clock from its own load and reports error 101; whichever lands
        /// first wins and the host's fallback is idempotent.</summary>
        private const int NoPlayingTimeoutMs = 10000;

        /// <summary>Budget from session start to the page's <c>ready</c> handshake. A cold WebView2
        /// (environment build, browser process spawn, navigation) can eat well over ten seconds on a
        /// slow disk, and NONE of that is the clip's fault - charging it to the clip is how a healthy
        /// file ends up looking undecodable. The clock restarts at <c>ready</c>.</summary>
        private const int PreReadyTimeoutMs = 20000;
        private const int WatchTickMs = 500;

        /// <summary>Two dead browser processes in one app session and the engine stands down for
        /// good - a flapping renderer must not turn every video into a fallback.</summary>
        private const int MaxProcessFailures = 2;

        private static readonly Lazy<BrowserVideoEngine> _instance =
            new(() => new BrowserVideoEngine(), System.Threading.LazyThreadSafetyMode.ExecutionAndPublication);
        private static Task<CoreWebView2Environment?>? _envTask;
        private static readonly object _envLock = new();
        private static int _processFailures;
        private static bool _environmentDead;

        /// <summary>
        /// Process-wide singleton, built exactly once however many threads race for it (the gate is
        /// consulted from selection code that does not always run on the UI thread).
        ///
        /// Touching it also KICKS OFF the shared environment build - it does not wait for it. A
        /// first video that arrives before the build finishes still sees <see cref="IsAvailable"/>
        /// true (it only turns false once a build has actually FAILED) and its session waits on the
        /// same task in <see cref="InitWindowsAsync"/>. App startup warms this so the first video
        /// does not pay for <c>CreateEnvironmentAsync</c>.
        /// </summary>
        public static BrowserVideoEngine Instance
        {
            get
            {
                var engine = _instance.Value;
                _ = EnvironmentAsync();   // fire-and-forget warm-up
                return engine;
            }
        }

        /// <summary>
        /// Startup warm-up: build the singleton and begin the WebView2 environment creation now, so
        /// the first mandatory video is not the thing that pays for a cold browser start (and does
        /// not spend that time against its own first-frame budget). Fire-and-forget, never throws.
        /// </summary>
        public static void WarmUp()
        {
            try
            {
                _ = Instance;
                App.Logger?.Debug("BrowserVideo: warm-up started (environment building in the background)");
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("BrowserVideo: warm-up failed ({E}) - the first video will build the environment", ex.Message);
            }
        }

        private readonly List<BrowserVideoWindow> _windows = new();

        // Mirrors dropped mid-session (DropSecondaryWindow). They are out of _windows so nothing
        // broadcasts to them, but the host's strict Closing veto can CANCEL their Close(), which
        // would otherwise strand a hidden window nobody owns. StopSession closes these too, by which
        // point the veto is down.
        private readonly List<BrowserVideoWindow> _dropped = new();
        private BrowserVideoWindow? _primary;
        // ONE timer sweeps EVERY window; the deadline itself lives per window
        // (BrowserVideoWindow.FirstFrameDeadlineUtc). A session-wide deadline could only judge the
        // primary, which is exactly how a black mirror stayed invisible - see ArmFirstFrameWatch.
        private DispatcherTimer? _firstFrameWatch;
        private Func<bool>? _isHostPaused;
        private Func<bool>? _isHostStrict;
        private bool _failedRaised;
        private int _sessionId;

        /// <summary>A mirror was taken off screen mid-clip (dropped and hidden). The host keeps its own
        /// window list and a cache of video-window HWNDs for z-order anchoring, and neither can learn
        /// about the drop from <see cref="Windows"/> - the window is out of that list by then. Carries
        /// the HWND because it is read BEFORE the Close(), after which it is gone. NOT raised for a
        /// mirror that strict mode keeps on screen: that one is still a live fullscreen cover and must
        /// stay in the host's bookkeeping.</summary>
        public event Action<Window, IntPtr>? WindowDropped;

        private BrowserVideoEngine() { }

        /// <summary>False once the WebView2 environment has proven unbuildable (runtime missing) or
        /// the browser process has died twice this app session. Every video then routes to LibVLC.</summary>
        public bool IsAvailable => !_environmentDead && _processFailures < MaxProcessFailures;

        /// <summary>True between <see cref="StartSession"/> and <see cref="StopSession"/>.</summary>
        public bool IsSessionActive => _windows.Count > 0;

        /// <summary>The windows of the live session, for the host to add to its own teardown list.</summary>
        public IReadOnlyList<Window> Windows => _windows;

        /// <summary>Mirrors dropped mid-clip (<see cref="DropSecondaryWindow"/>), which are NOT in
        /// <see cref="Windows"/> any more. The host has its own window list and its own cache of
        /// video-window HWNDs, and both would otherwise keep a dropped mirror forever on the paths
        /// that do not run CloseAll (the browser -> LibVLC handoff), so teardown reads this too.</summary>
        public IReadOnlyList<Window> DroppedWindows => _dropped.ToArray();

        /// <summary>The audio-bearing window, or null when no session is live.</summary>
        public Window? PrimaryWindow => _primary;

        /// <summary>The session window covering <paramref name="screen"/>, or null when this monitor
        /// has none (secondaries are capped on high monitor counts, #389). The attention-check
        /// spawner asks per screen and falls back to a floating window when the answer is null, which
        /// is what keeps multi-monitor placement identical to the LibVLC path.</summary>
        public BrowserVideoWindow? WindowForScreen(Screen screen)
        {
            if (screen == null) return null;
            foreach (var w in _windows)
                if (string.Equals(w.Screen.DeviceName, screen.DeviceName, StringComparison.Ordinal)) return w;
            return null;
        }

        // ---- events (all raised on the UI thread, from the WebView2 message callback) ----

        /// <summary>durationMs (0 = unknowable), width, height. Can fire MORE THAN ONCE per clip:
        /// webm / fragmented mp4 report Infinity at loadedmetadata and settle at durationchange.
        /// Last one wins.</summary>
        public event Action<long, int, int>? Meta;

        /// <summary>First real <c>playing</c> event of the clip.</summary>
        public event Action? Playing;

        /// <summary>~10 Hz playback position in ms, plus the page's own paused flag for that position.
        /// The steady 10 Hz stream only runs while playing, but pause / seek / end FORCE a post, so the
        /// flag is what tells those apart — never infer "playing" from the arrival of a message (#874).
        /// null = the page did not report it (older cached page HTML); leave the host's own pause
        /// bookkeeping alone in that case.</summary>
        public event Action<long, bool?>? Time;

        /// <summary>Natural end of the clip.</summary>
        public event Action? Ended;

        /// <summary>reason, blameFile. blameFile=true means the FILE is undecodable (media error or
        /// first-frame timeout) and belongs in <see cref="BrowserUnsafeVideoCache"/>; false means the
        /// session/environment failed and the file deserves another chance later.</summary>
        public event Action<string, bool>? Failed;

        /// <summary>Any pointer-down on the surface. Window-level PreviewMouseDown is unreliable over
        /// a WebView2 child HWND, so this message is the only signal the host gets.</summary>
        public event Action? Clicked;

        /// <summary>key, alt, ctrl, shift - every non-repeat keydown the page saw. Keyboard over a
        /// focused WebView2 goes to Chromium, so this is how strict-mode/ESC decisions still reach C#.</summary>
        public event Action<string, bool, bool, bool>? KeyPressed;

        /// <summary>An attention-check target was pressed (target id). Never accompanied by
        /// <see cref="Clicked"/> for the same press - the page routes one or the other.</summary>
        public event Action<string>? AttentionClicked;

        /// <summary>Target id + its viewport-fraction rectangle, ~10 Hz while the target bounces and
        /// only when the show message asked for it (gaze click enabled). Pure position bookkeeping.</summary>
        public event Action<string, double, double, double, double>? AttentionMoved;

        // ===================== environment =====================

        private static Task<CoreWebView2Environment?> EnvironmentAsync()
        {
            // Locked for the same reason Instance is Lazy: two threads racing here would each start
            // a CoreWebView2Environment.CreateAsync against the same user-data folder.
            lock (_envLock) { return _envTask ??= CreateEnvironmentAsync(); }
        }

        /// <summary>
        /// The ONE shared WebView2 environment, for hosts that own their own surfaces instead of a
        /// session here (BubbleCountWindow puts a <see cref="BrowserVideoSurface"/> inside the game
        /// window so its HUD, strict lock and ESC handling keep working). Null once it has proven
        /// unbuildable - the caller must then use LibVLC. Never throws.
        /// </summary>
        public static Task<CoreWebView2Environment?> SharedEnvironmentAsync() => EnvironmentAsync();

        /// <summary>Virtual-host mappings for a foreign surface, with the folders created first (a
        /// missing folder makes WebView2 skip the mapping silently).</summary>
        public static IReadOnlyList<(string, string, CoreWebView2HostResourceAccessKind)> SharedMappings()
            => BuildMappings();

        /// <summary>Where a foreign surface must navigate, and the only host it may navigate to.</summary>
        public static string PlayerStartUrl => StartUrl;

        public static string PlayerHost => GameHost;

        /// <summary>
        /// A host driving its own surface reports a dead browser process here, so the two-strikes
        /// stand-down is shared app-wide: a flapping renderer must not turn every video in every
        /// feature into a fallback. Never blames the file (plan §4).
        /// </summary>
        public static void ReportProcessFailure(CoreWebView2ProcessFailedKind kind)
        {
            _processFailures++;
            if (_processFailures >= MaxProcessFailures)
                App.Logger?.Warning("BrowserVideo: {Count} WebView2 process failures this session ({Kind}) - engine standing down, everything routes to LibVLC",
                    _processFailures, kind);
        }

        private static async Task<CoreWebView2Environment?> CreateEnvironmentAsync()
        {
            try
            {
                var userDataFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "ConditioningControlPanel", "browser_data_video");
                Directory.CreateDirectory(userDataFolder);

                // --autoplay-policy: the page must start without a user gesture.
                // --disable-direct-composition-video-overlays: MPO scans video out on its own plane;
                //   on some GPUs that means black video or video ABOVE topmost overlays (#449/#439).
                // The two occlusion flags: a mandatory video window is covered by attention targets,
                //   subliminals and grace cards - Chromium would call the page occluded and stop
                //   rendering it.
                var args = "--autoplay-policy=no-user-gesture-required "
                         + "--disable-direct-composition-video-overlays "
                         + "--disable-features=CalculateNativeWinOcclusion "
                         + "--disable-backgrounding-occluded-windows";
                var options = new CoreWebView2EnvironmentOptions { AdditionalBrowserArguments = args };
                var env = await CoreWebView2Environment
                    .CreateAsync(browserExecutableFolder: null, userDataFolder: userDataFolder, options: options)
                    .ConfigureAwait(true);
                App.Logger?.Information("BrowserVideo: WebView2 environment ready ({Folder})", userDataFolder);
                return env;
            }
            catch (Exception ex)
            {
                // Almost always "the WebView2 runtime is not installed". One clear line, then every
                // video routes to LibVLC for the rest of the session.
                _environmentDead = true;
                App.Logger?.Warning("BrowserVideo: WebView2 environment unavailable ({E}) - browser video engine disabled for this session", ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Virtual-host URL for a clip on disk, or null when the file lives outside every mapped
        /// root (in which case the caller must use LibVLC). Percent-escaped per path segment.
        /// The pack root is checked FIRST: content-pack clips are AES-decrypted into
        /// <c>App.GetMediaTempPath()</c>, which normally sits inside the assets folder.
        ///
        /// A remote clip is already a URL and passes straight through. This engine is the DEFAULT
        /// video path (<c>BrowserVideoEngineEnabled</c> ships true), so without this every remote
        /// clip would force a fallback to LibVLC — the engine most users never touch — and a
        /// stalled network read would land on the WPF dispatcher instead of out-of-process.
        /// Safe because the player page never reads pixels back (no canvas, no drawImage) and
        /// carries no CSP, so a CORS-tainted &lt;video&gt; is a non-issue; the FYP feed already
        /// plays these exact URLs in WebView2.
        /// </summary>
        public static string? BuildPageUrl(string? path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            try
            {
                if (Uri.TryCreate(path, UriKind.Absolute, out var abs)
                    && (abs.Scheme == Uri.UriSchemeHttp || abs.Scheme == Uri.UriSchemeHttps))
                    return abs.AbsoluteUri;

                var full = Path.GetFullPath(path);
                var packRoot = PackTempRoot();
                if (TryRelative(full, packRoot, out var packRel))
                    return "https://" + PacksHost + "/" + EscapeSegments(packRel);
                if (TryRelative(full, App.EffectiveAssetsPath, out var assetRel))
                    return "https://" + AssetsHost + "/" + EscapeSegments(assetRel);
                return null;
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("BrowserVideo: URL build failed for {Path}: {E}", path, ex.Message);
                return null;
            }
        }

        /// <summary>The content-pack decrypt directory, resolved from the same helper
        /// <c>ContentPackService.GetPackFileTempPath</c> writes into - never hardcoded.</summary>
        private static string PackTempRoot()
        {
            try { return App.GetMediaTempPath(); }
            catch { return string.Empty; }
        }

        private static bool TryRelative(string full, string? root, out string relative)
        {
            relative = string.Empty;
            if (string.IsNullOrEmpty(root)) return false;
            try
            {
                var rootFull = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (!full.StartsWith(rootFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    return false;
                relative = full.Substring(rootFull.Length + 1).Replace('\\', '/');
                return relative.Length > 0;
            }
            catch { return false; }
        }

        private static string EscapeSegments(string relative)
            => string.Join('/', Array.ConvertAll(relative.Split('/'), Uri.EscapeDataString));

        private static IReadOnlyList<(string, string, CoreWebView2HostResourceAccessKind)> BuildMappings()
        {
            var webRoot = Path.Combine(AppContext.BaseDirectory, "Resources", "web");
            var assets = App.EffectiveAssetsPath;
            var packs = PackTempRoot();
            // Create BEFORE mapping - a missing folder makes WebView2 skip the mapping silently.
            foreach (var dir in new[] { assets, packs })
            {
                if (string.IsNullOrEmpty(dir)) continue;
                try { Directory.CreateDirectory(dir); }
                catch (Exception ex) { App.Logger?.Debug("BrowserVideo: mapping dir create failed ({Dir}): {E}", dir, ex.Message); }
            }
            return new List<(string, string, CoreWebView2HostResourceAccessKind)>
            {
                (GameHost, webRoot, CoreWebView2HostResourceAccessKind.Deny),
                (AssetsHost, assets, CoreWebView2HostResourceAccessKind.Allow),
                (PacksHost, packs, CoreWebView2HostResourceAccessKind.Allow),
            };
        }

        // ===================== session =====================

        /// <summary>
        /// Bring the windows up and start the clip. Returns false when nothing could be created (the
        /// caller must then use LibVLC); a failure that only surfaces later comes back through
        /// <see cref="Failed"/>. Never throws. UI thread only.
        /// </summary>
        public bool StartSession(BrowserVideoRequest req)
        {
            if (!IsAvailable) return false;
            var url = BuildPageUrl(req.Path);
            if (url == null) return false;

            StopSession();   // a previous session must never outlive its video

            var session = ++_sessionId;
            _failedRaised = false;
            _isHostPaused = req.IsHostPaused;
            _isHostStrict = req.IsHostStrict;
            // Resolved once per session, not per window: the load posts below run in a loop and the
            // settings could change between iterations, splitting one clip across two devices.
            var sinkLabel = BrowserSinkLabel.Resolve();

            try
            {
                _primary = CreateWindow(req, req.PrimaryScreen, primary: true, url, sinkLabel);
                if (_primary == null)
                {
                    App.Logger?.Warning("BrowserVideo: the audio-bearing window on {Screen} could not be created - handing the whole clip to LibVLC",
                        req.PrimaryScreen.DeviceName);
                    return false;
                }
                _windows.Add(_primary);

                foreach (var scr in req.SecondaryScreens)
                {
                    var w = CreateWindow(req, scr, primary: false, url, sinkLabel);
                    if (w != null) _windows.Add(w);
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "BrowserVideo: session start failed");
                StopSession();
                return false;
            }

            _ = InitWindowsAsync(session);
            ArmFirstFrameWatch();

            App.Logger?.Information("BrowserVideo: session {Id} up on {Count} screen(s) - {File} (blur={Blur}, vol={Vol:0.00}, muted={Muted})",
                session, _windows.Count, Path.GetFileName(req.Path), req.BlurBackground, req.Volume, req.Muted);
            return true;
        }

        private BrowserVideoWindow? CreateWindow(BrowserVideoRequest req, Screen screen, bool primary, string url, string? sinkLabel)
        {
            BrowserVideoWindow? win = null;
            try
            {
                win = new BrowserVideoWindow(screen, primary);
                win.SessionStartTick = Environment.TickCount64;
                // Only the PRIMARY is armed here. InitWindowsAsync brings the windows up ONE AT A
                // TIME, so arming a mirror now would charge it for the whole time it spends queued
                // behind the primary's WebView2 - on a cold multi-screen start the last mirror could
                // be past its deadline before its own init had even begun, and the sweep would then
                // hide and close a perfectly healthy screen. Mirrors are armed when their init
                // starts; until then they are UNARMED (DateTime.MaxValue) and cannot be judged.
                //
                // The primary keeps the session-start clock on purpose: it is initialised first so it
                // never queues, and this is the only deadline that can still fire if the WebView2
                // ENVIRONMENT task never completes - in that case the serial init loop never runs, so
                // nothing else would ever end the session and hand the clip to LibVLC.
                if (VideoSurfaceHealth.ArmsFrameDeadlineAtSessionStart(primary))
                {
                    win.FirstFrameBudgetMs = PreReadyTimeoutMs;
                    win.FirstFrameDeadlineUtc = DateTime.UtcNow.AddMilliseconds(PreReadyTimeoutMs);
                }
                win.Message += OnWindowMessage;
                win.ProcessFailed += OnWindowProcessFailed;
                win.Ready += OnWindowReady;
                win.InitFailed += OnWindowInitFailed;

                // Queued until the page's ready handshake, so it is safe to post before the
                // CoreWebView2 even exists.
                win.Post(new
                {
                    type = "load",
                    url,
                    volume = primary ? Math.Clamp(req.Volume, 0, 1) : 0.0,
                    muted = primary ? req.Muted : true,   // secondaries never carry audio
                    blurBackground = req.BlurBackground,
                    hideCursor = req.HideCursor,
                    startAtMs = req.StartAtMs,
                    // Arms the page's Media Session guard (player.js setMediaKeyGuard), which is what
                    // stops an OS/Bluetooth media key from pausing a video the user must not be able
                    // to stop. Every window carries it: a mirror is just as pausable as the primary.
                    strict = req.Strict,
                    // Audio-output routing by device label (#938 plumbing). Null = leave the page on
                    // the Windows default; the page treats every failure the same way and reports it
                    // back as {type:'sink'}.
                    sinkLabel = BrowserSinkLabel.ForWindow(primary, sinkLabel),
                });

                try { req.ConfigureBeforeShow?.Invoke(win, screen, primary); }
                catch (Exception ex) { App.Logger?.Debug("BrowserVideo: ConfigureBeforeShow threw: {E}", ex.Message); }

                win.Show();
                // Focus BEFORE the host stamps WS_EX_NOACTIVATE: the page needs keyboard focus for
                // its {type:'key'} reports, and after the style lands the window can never take it.
                if (primary) win.FocusWeb();

                try { req.ConfigureAfterShow?.Invoke(win, screen, primary); }
                catch (Exception ex) { App.Logger?.Debug("BrowserVideo: ConfigureAfterShow threw: {E}", ex.Message); }

                return win;
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "BrowserVideo: failed to create window on {Screen}", screen.DeviceName);
                VideoSurfaceHealth.Report("browser", screen.DeviceName, primary, -1, "window creation threw: " + ex.Message);
                try { win?.Close(); } catch { }
                return null;
            }
        }

        private async Task InitWindowsAsync(int session)
        {
            var env = await EnvironmentAsync().ConfigureAwait(true);
            if (session != _sessionId) return;   // the session ended while the environment was building
            if (env == null)
            {
                RaiseFailed("WebView2 environment unavailable", blameFile: false);
                return;
            }

            var mappings = BuildMappings();
            foreach (var w in _windows.ToArray())
            {
                if (session != _sessionId) return;
                // Arm a still-UNARMED window (every mirror) as its own init starts - never at session
                // start, because this loop is serial and a window queued behind the primary has not
                // begun coming up yet. A cold WebView2 can eat well over ten seconds by itself (see
                // PreReadyTimeoutMs), which is exactly how a healthy third screen used to be
                // condemned. The primary is already armed from session start and is NOT re-armed
                // here: that clock is the backstop for a hung environment and must not slide.
                if (w.FirstFrameDeadlineUtc == DateTime.MaxValue)
                {
                    w.FirstFrameBudgetMs = PreReadyTimeoutMs;
                    w.FirstFrameDeadlineUtc = DateTime.UtcNow.AddMilliseconds(PreReadyTimeoutMs);
                }
                // EnsureCoreWebView2Async only works once the control is in the visual tree, which
                // is why this runs after Show().
                await w.InitAsync(env, mappings, StartUrl, GameHost).ConfigureAwait(true);
            }
            // The control had no CoreWebView2 to focus when the window was shown, so re-assert it
            // now that one exists - otherwise the page never receives a keydown.
            if (session == _sessionId) _primary?.FocusWeb();
        }

        /// <summary>Tear the session down. Idempotent, never throws, raises nothing.</summary>
        public void StopSession()
        {
            _sessionId++;   // invalidates every in-flight continuation and late page message
            StopFirstFrameWatch();
            _isHostPaused = null;
            _isHostStrict = null;

            var windows = _windows.ToArray();
            _windows.Clear();
            _primary = null;

            // Mirrors dropped mid-clip: their Close() may have been vetoed by the host's strict
            // Closing handler while the video was still playing. The veto is down by the time any
            // teardown reaches here, so this is where they actually go.
            var dropped = _dropped.ToArray();
            _dropped.Clear();
            foreach (var w in dropped)
            {
                try { w.Close(); }
                catch (Exception ex) { App.Logger?.Debug("BrowserVideo: dropped-mirror close failed: {E}", ex.Message); }
            }

            foreach (var w in windows)
            {
                try
                {
                    w.Message -= OnWindowMessage;
                    w.ProcessFailed -= OnWindowProcessFailed;
                    w.Ready -= OnWindowReady;
                    w.InitFailed -= OnWindowInitFailed;
                    // Decoder hygiene: pause -> drop src -> load(). Queued messages are dropped with
                    // the window, so this is best-effort and the Close below is what really frees it.
                    if (w.IsReady) w.Post(new { type = "stop" });
                    w.Close();
                }
                catch (Exception ex) { App.Logger?.Debug("BrowserVideo: window close failed: {E}", ex.Message); }
            }
        }

        // ---- host controls ----

        public void Pause() => Broadcast(new { type = "pause" });

        public void Resume() => Broadcast(new { type = "resume" });

        /// <summary>Master x video volume as 0..1, with external mute folded in.</summary>
        public void SetVolume(double volume, bool muted)
        {
            var v = Math.Clamp(volume, 0, 1);
            foreach (var w in _windows.ToArray())
            {
                // Secondaries stay silent for the whole session (one WASAPI session per clip).
                try { w.Post(new { type = "setVolume", volume = w.IsPrimary ? v : 0.0, muted = w.IsPrimary ? muted : true }); }
                catch (Exception ex) { App.Logger?.Debug("BrowserVideo: setVolume post failed: {E}", ex.Message); }
            }
        }

        /// <summary>Seek every screen so multi-monitor mirrors stay in lockstep (#527).</summary>
        public void Seek(long ms) => Broadcast(new { type = "seek", ms = Math.Max(0, ms) });

        private void Broadcast(object msg)
        {
            foreach (var w in _windows.ToArray())
            {
                try { w.Post(msg); }
                catch (Exception ex) { App.Logger?.Debug("BrowserVideo: post failed: {E}", ex.Message); }
            }
        }

        // ---- page messages ----

        private void OnWindowMessage(BrowserVideoWindow win, JObject o)
        {
            try
            {
                var type = (string?)o["type"];

                // Attention-check traffic is window-scoped, not session-scoped: with dual monitors
                // every screen carries its OWN target and the user may click any of them, so these
                // two must be handled before the primary-only gate below or a secondary's target
                // could never be hit. Each message names the target, so nothing doubles up.
                switch (type)
                {
                    case "attentionClick":
                        AttentionClicked?.Invoke((string?)o["id"] ?? "");
                        return;
                    case "attentionMove":
                        AttentionMoved?.Invoke((string?)o["id"] ?? "",
                            (double?)o["xPct"] ?? 0, (double?)o["yPct"] ?? 0,
                            (double?)o["wPct"] ?? 0, (double?)o["hPct"] ?? 0);
                        return;
                }

                // Per-SURFACE bookkeeping, ahead of the primary-only gate below: every window posts its
                // own `playing`, and until now only the primary's was looked at. This is the HEALTHY
                // half of the per-surface diagnostics; the half that matters for a black mirror - the
                // window that never posts `playing` at all - is ArmFirstFrameWatch's sweep, which
                // reports and drops it on its own deadline. Together they guarantee one line per
                // surface either way, which is what tells "monitor 2 is black" apart from "the clip
                // failed" in a bug report.
                if (type == "playing" && !win.FirstFrameReported)
                {
                    win.FirstFrameReported = true;
                    VideoSurfaceHealth.Report("browser", win.Screen.DeviceName, win.IsPrimary,
                        Math.Max(0, Environment.TickCount64 - win.SessionStartTick), null);
                }

                // Only the audio-bearing window drives the session, exactly like the LibVLC primary
                // player - a secondary's events would double every callback. A secondary's ERROR is
                // still reported (Warning + a surface line): it is never allowed to end the run, but
                // "swallowed entirely" is how a permanently black mirror stayed invisible to triage.
                if (!win.IsPrimary)
                {
                    if (type == "error")
                    {
                        int scode = (int?)o["code"] ?? 0;
                        var smsg = (string?)o["message"] ?? "";
                        App.Logger?.Warning("BrowserVideo[secondary]: page error {Code} {Msg} on {Screen} - that mirror stays black, the primary keeps the session",
                            scode, smsg, win.Screen.DeviceName);
                        VideoSurfaceHealth.Report("browser", win.Screen.DeviceName, false, -1, $"page error {scode}: {smsg}");
                    }
                    return;
                }

                switch (type)
                {
                    case "meta":
                    {
                        long durationMs = (long?)o["durationMs"] ?? 0;
                        int w = (int?)o["width"] ?? 0;
                        int h = (int?)o["height"] ?? 0;
                        Meta?.Invoke(durationMs, w, h);
                        break;
                    }
                    case "playing":
                        // Deliberately does NOT stop the sweep: the primary rendering says nothing
                        // about the mirrors, and disarming here is how a black secondary used to go
                        // unnoticed for the whole clip. The sweep stops itself once every window has
                        // reported (or been dropped).
                        Playing?.Invoke();
                        break;
                    case "time":
                        // `paused` is absent on older cached page HTML — pass null through rather than
                        // defaulting to false, which would be the blind-clear this fixed (#874).
                        Time?.Invoke((long?)o["ms"] ?? 0, (bool?)o["paused"]);
                        break;
                    case "ended":
                        StopFirstFrameWatch();
                        Ended?.Invoke();
                        break;
                    case "error":
                    {
                        int code = (int?)o["code"] ?? 0;
                        var msg = (string?)o["message"] ?? "";
                        App.Logger?.Warning("BrowserVideo: page error {Code}: {Msg}", code, msg);
                        RaiseFailed($"page error {code}: {msg}", blameFile: BlamesFile(code));
                        break;
                    }
                    case "sink":
                    {
                        // The page's verdict on the requested audio-output routing (#938 plumbing).
                        // Exactly one line each way; failure is deliberately only a Warning because
                        // the fail-safe already happened page-side - audio stayed on the default.
                        var label = (string?)o["label"] ?? "";
                        if ((bool?)o["ok"] == true)
                            App.Logger?.Information("BrowserVideo: audio routed to output device \"{Label}\" via setSinkId", label);
                        else
                            App.Logger?.Warning("BrowserVideo: could not route audio to \"{Label}\" ({Detail}) - staying on the Windows default output",
                                label, (string?)o["detail"] ?? "");
                        break;
                    }
                    case "click":
                        Clicked?.Invoke();
                        break;
                    case "key":
                        KeyPressed?.Invoke((string?)o["key"] ?? "",
                            (bool?)o["alt"] ?? false, (bool?)o["ctrl"] ?? false, (bool?)o["shift"] ?? false);
                        break;
                }
            }
            catch (Exception ex) { App.Logger?.Debug("BrowserVideo: message dispatch failed: {E}", ex.Message); }
        }

        /// <summary>
        /// Which page error codes mean "this FILE is undecodable" and belong in the unsafe cache.
        /// 1-4 are MediaError.code verbatim, 100 is a >10s stall after playing and 101 is no first
        /// frame within the window - all properties of the file. 102 (second unrequested pause) and
        /// 103 (malformed load message) are session-level and must not condemn the clip.
        /// </summary>
        private static bool BlamesFile(int code) => (code >= 1 && code <= 4) || code == 100 || code == 101;

        /// <summary>
        /// The page handshake landed, so the queued <c>load</c> only reaches the element NOW - which
        /// means everything before this point was environment start-up, not the clip failing to
        /// decode. Restart THIS window's first-frame clock from here UNCONDITIONALLY (the pre-ready
        /// budget is generous precisely so this always gets to run) and give the file its own full
        /// window.
        ///
        /// Every screen gets this, not just the primary: a mirror's WebView2 comes up on its own
        /// schedule, and the mirror that handshakes and THEN never decodes is exactly the surface the
        /// sweep has to be able to time out (see ArmFirstFrameWatch).
        /// </summary>
        private void OnWindowReady(BrowserVideoWindow win)
        {
            if (win.FirstFrameReported || _firstFrameWatch == null) return;
            win.FirstFrameBudgetMs = NoPlayingTimeoutMs;
            win.FirstFrameDeadlineUtc = DateTime.UtcNow.AddMilliseconds(NoPlayingTimeoutMs);
        }

        /// <summary>
        /// A surface's WebView2 never came up (<see cref="BrowserVideoSurface.InitFailed"/>). Before
        /// this handler existed the failure was a Warning and nothing else, which left an OPAQUE BLACK
        /// fullscreen window on that monitor: on the PRIMARY - the only window carrying audio, and the
        /// one <see cref="InitWindowsAsync"/> initialises first - that is exactly the reported "black
        /// primary, no sound, secondaries fine", and the only thing that ever rescued it was the
        /// <see cref="PreReadyTimeoutMs"/> budget expiring. Fail the session NOW instead so the host's
        /// LibVLC fallback runs at once.
        /// </summary>
        private void OnWindowInitFailed(BrowserVideoWindow win, string reason)
        {
            VideoSurfaceHealth.Report("browser", win.Screen.DeviceName, win.IsPrimary, -1, "surface init failed: " + reason);
            if (!win.IsPrimary)
            {
                App.Logger?.Warning("BrowserVideo[secondary]: surface init failed on {Screen} ({Reason}) - dropping that mirror, the primary keeps playing",
                    win.Screen.DeviceName, reason);
                DropSecondaryWindow(win, "init failed");
                return;
            }

            App.Logger?.Warning("BrowserVideo: the PRIMARY surface never came up ({Reason}) - failing the session now instead of sitting black for the {Ms}ms first-frame budget",
                reason, PreReadyTimeoutMs);
            // Never blames the file: a WebView2 that would not start says nothing about the clip.
            RaiseFailed("primary surface init failed: " + reason, blameFile: false);
        }

        /// <summary>Unhook and drop ONE mirror without touching the session. Shared by the three
        /// secondary-only failure paths so they cannot drift apart.
        ///
        /// The window is always unhooked and taken out of <see cref="_windows"/>, so nothing
        /// broadcasts to it and the sweep stops judging it. What happens to the SCREEN depends on the
        /// lock (VideoSurfaceHealth.ShouldUncoverDeadSurface):
        ///
        ///   * NOT strict - Hide() then Close(). Hide() comes first and is the load-bearing call:
        ///     every window the host builds carries the strict Closing veto
        ///     (VideoService.SetupStrictHandlers), which cancels Close() while <c>_videoPlaying</c> is
        ///     set - and it is already set by the time any surface can fail. A vetoed Close alone
        ///     would leave the dead mirror on that monitor as an opaque black fullscreen window for
        ///     the whole clip with every handler unhooked. Hide() is not vetoable, so the screen is
        ///     freed either way, and the real Close lands here or at StopSession.
        ///   * STRICT (Lock Card / Lockdown / Possession) - the window STAYS on screen, black. Every
        ///     screen being covered is the point of that lock; hiding one would hand the user an
        ///     interactive desktop mid-clip, which is a worse failure than a dead screen and is not
        ///     something the released build ever did. StopSession closes it once the veto is down.</summary>
        private void DropSecondaryWindow(BrowserVideoWindow win, string why)
        {
            try
            {
                win.Message -= OnWindowMessage;
                win.ProcessFailed -= OnWindowProcessFailed;
                win.Ready -= OnWindowReady;
                win.InitFailed -= OnWindowInitFailed;
                // Stops the sweep judging it again even if something re-adds it, and stops a late
                // `playing` from this window double-reporting the surface.
                win.FirstFrameReported = true;
                _windows.Remove(win);
                if (!_dropped.Contains(win)) _dropped.Add(win);

                bool strict = false;
                try { strict = _isHostStrict?.Invoke() == true; }
                catch (Exception ex) { App.Logger?.Debug("BrowserVideo: strict probe threw: {E}", ex.Message); }

                if (!VideoSurfaceHealth.ShouldUncoverDeadSurface(strict))
                {
                    App.Logger?.Warning("BrowserVideo[secondary]: {Screen} dropped after {Why}, but the clip is under a STRICT lock - the screen stays covered (black) instead of being uncovered mid-video",
                        win.Screen.DeviceName, why);
                    return;
                }

                // Read the HWND while the window still has one: the host prunes it from its z-order
                // anchor cache, and after Close() there is nothing left to read.
                IntPtr hwnd = IntPtr.Zero;
                try { hwnd = new System.Windows.Interop.WindowInteropHelper(win).Handle; } catch { }
                try { WindowDropped?.Invoke(win, hwnd); }
                catch (Exception ex) { App.Logger?.Debug("BrowserVideo: WindowDropped handler threw: {E}", ex.Message); }

                try { win.Hide(); }
                catch (Exception ex) { App.Logger?.Debug("BrowserVideo: secondary hide after {Why} failed: {E}", why, ex.Message); }
                win.Close();
            }
            catch (Exception ex) { App.Logger?.Debug("BrowserVideo: secondary close after {Why} failed: {E}", why, ex.Message); }
        }

        private void OnWindowProcessFailed(BrowserVideoWindow win, CoreWebView2ProcessFailedKind kind)
        {
            // ONE dead browser process fires this on EVERY window sharing it, so a dual-monitor
            // session used to spend both strikes of the stand-down budget on a single crash. Only
            // the primary's handler counts - and only the primary's failure ends the session.
            if (!win.IsPrimary)
            {
                App.Logger?.Warning("BrowserVideo[secondary]: WebView2 process failed ({Kind}) - dropping that mirror, the primary keeps playing", kind);
                VideoSurfaceHealth.Report("browser", win.Screen.DeviceName, false, -1, "WebView2 process failed: " + kind);
                DropSecondaryWindow(win, "ProcessFailed");
                return;
            }

            _processFailures++;
            if (_processFailures >= MaxProcessFailures)
                App.Logger?.Warning("BrowserVideo: {Count} WebView2 process failures this session - engine standing down, everything routes to LibVLC",
                    _processFailures);
            // Never blames the file: a dead renderer is an environment failure (plan §4).
            RaiseFailed("WebView2 process failed: " + kind, blameFile: false);
        }

        // ---- first-frame watchdog ----

        /// <summary>
        /// PER-SURFACE first-frame liveness for the browser engine. One timer, but the verdict is
        /// taken per window against that window's own deadline.
        ///
        /// This used to watch the PRIMARY alone and disarm itself the instant the primary posted
        /// <c>playing</c>. The browser engine is the DEFAULT for mp4/webm and it drives EVERY screen,
        /// so that left the reported failure completely unguarded: a mirror whose WebView2 started,
        /// completed the handshake and then never decoded a frame (GPU stall, a fetch the page
        /// swallowed, a decode failure that never became an <c>error</c>) raised nothing, was dropped
        /// by nothing, and sat as an opaque black fullscreen window for the whole clip - with no line
        /// in video-diag.log to tell it apart from a window that was never created.
        ///
        /// Now: a mirror that misses its deadline is reported and dropped, the clip untouched; the
        /// audio-bearing surface that misses its deadline still fails the session so the host replays
        /// the clip through LibVLC. Verdict rule is pure - VideoSurfaceHealth.DecideBrowserFrameSweep.
        /// </summary>
        private void ArmFirstFrameWatch()
        {
            StopFirstFrameWatch();
            _firstFrameWatch = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(WatchTickMs) };
            _firstFrameWatch.Tick += (_, _) =>
            {
                try
                {
                    var wins = _windows.ToArray();
                    if (wins.Length == 0) { StopFirstFrameWatch(); return; }

                    // A grace pause holds playback on purpose, and the page suspends its own clock
                    // for the same reason - slide EVERY pending deadline instead of condemning
                    // healthy surfaces for obeying the host.
                    if (_isHostPaused?.Invoke() == true)
                    {
                        foreach (var w in wins)
                        {
                            if (!w.FirstFrameReported)
                                w.FirstFrameDeadlineUtc = VideoSurfaceHealth.SlidePendingDeadline(w.FirstFrameDeadlineUtc, WatchTickMs);
                        }
                        return;
                    }

                    var now = DateTime.UtcNow;
                    bool anyPending = false;
                    foreach (var w in wins)
                    {
                        // An UNARMED deadline (this window's init has not started - the loop in
                        // InitWindowsAsync is serial) reads as "not passed", so a queued window is
                        // never condemned for the time it spent waiting its turn.
                        switch (VideoSurfaceHealth.DecideBrowserFrameSweep(w.FirstFrameReported,
                                    VideoSurfaceHealth.FrameDeadlinePassed(w.FirstFrameDeadlineUtc, now), w.IsPrimary))
                        {
                            case VideoSurfaceHealth.BrowserFrameSweepAction.Ignore:
                                continue;

                            case VideoSurfaceHealth.BrowserFrameSweepAction.Wait:
                                anyPending = true;
                                continue;

                            case VideoSurfaceHealth.BrowserFrameSweepAction.DropMirror:
                                // Marked BEFORE the drop so a late `playing` from this window cannot
                                // double-report it, and so a re-entrant tick cannot judge it twice.
                                w.FirstFrameReported = true;
                                App.Logger?.Warning("BrowserVideo[secondary]: no first frame within {Ms}ms on {Screen} - dropping that mirror, the primary keeps playing",
                                    w.FirstFrameBudgetMs, w.Screen.DeviceName);
                                VideoSurfaceHealth.Report("browser", w.Screen.DeviceName, false, -1,
                                    "no first frame within " + w.FirstFrameBudgetMs + "ms");
                                DropSecondaryWindow(w, "no first frame");
                                continue;

                            case VideoSurfaceHealth.BrowserFrameSweepAction.FailSession:
                                w.FirstFrameReported = true;
                                StopFirstFrameWatch();
                                // blameFile is ALWAYS false here. This timer cannot tell "undecodable
                                // clip" from "the page never ran / the browser is still starting", and
                                // condemning a file to LibVLC forever on that guess is the worse
                                // error. The page's OWN error 101 - raised by a page that demonstrably
                                // loaded and then got no frame - is the legitimate file-blame path
                                // (BlamesFile), and it still is.
                                RaiseFailed("no playback before the first-frame deadline", blameFile: false);
                                return;
                        }
                    }

                    // Every surface has either presented a frame or been condemned - nothing left to
                    // watch, and leaving the timer running would keep a dead session ticking.
                    if (!anyPending) StopFirstFrameWatch();
                }
                catch (Exception ex) { App.Logger?.Debug("BrowserVideo: first-frame watch tick failed: {E}", ex.Message); }
            };
            _firstFrameWatch.Start();
        }

        private void StopFirstFrameWatch()
        {
            try { _firstFrameWatch?.Stop(); } catch { }
            _firstFrameWatch = null;
        }

        /// <summary>One failure per session reaches the host; the page's own 10s report and this
        /// engine's watchdog can both fire for the same stall.</summary>
        private void RaiseFailed(string reason, bool blameFile)
        {
            if (_failedRaised) return;
            _failedRaised = true;
            StopFirstFrameWatch();
            // The session-level verdict, said in the same per-surface shape as every other line so a
            // report shows the primary's fate next to each mirror's.
            VideoSurfaceHealth.Report("browser", _primary?.Screen.DeviceName, primary: true, firstFrameMs: -1, failureReason: reason);
            try { Failed?.Invoke(reason, blameFile); }
            catch (Exception ex) { App.Logger?.Warning("BrowserVideo: Failed handler threw: {E}", ex.Message); }
        }
    }
}
