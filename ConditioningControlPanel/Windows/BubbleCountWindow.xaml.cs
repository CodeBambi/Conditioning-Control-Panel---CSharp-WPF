using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using NAudio.Wave;
using LibVLCSharp.Shared;
using LibVLCSharp.WPF;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Localization;
using Screen = System.Windows.Forms.Screen;

namespace ConditioningControlPanel
{
    /// <summary>
    /// Bubble Count Challenge - watch video, count bubbles, enter total
    /// Multi-monitor support using LibVLC (same pattern as VideoService)
    /// </summary>
    public partial class BubbleCountWindow : Window
    {
        private readonly string _videoPath;
        private readonly BubbleCountService.Difficulty _difficulty;
        private readonly bool _strictMode;
        private readonly Action<bool> _onComplete;
        private readonly Screen _screen;
        private readonly bool _isPrimary;

        private readonly Random _random = new();
        private readonly List<CountBubble> _activeBubbles = new();
        private DispatcherTimer? _bubbleSpawnTimer;
        private DispatcherTimer? _safetyTimer;
        // Single shared animation timer for ALL count bubbles, replacing the previous
        // two-DispatcherTimers-per-bubble model (up to ~24 concurrent timers).
        private DispatcherTimer? _bubbleAnimTimer;
        private const double BubbleAnimTickMs = 30;

        private int _bubbleCount = 0;
        private int _targetBubbleCount = 0;
        private double _videoDurationSeconds = 30;
        private bool _videoEnded = false;
        private bool _gameCompleted = false;

        private BitmapImage? _bubbleImage;

        // LibVLC - instance fields per window
        private LibVLCSharp.Shared.MediaPlayer? _mediaPlayer;
        private VideoView? _videoView;

        // Multi-monitor support - static shared state
        private static readonly object _cleanupLock = new();
        private static bool _isCleaningUp = false;
        private static List<BubbleCountWindow> _allWindows = new();
        private static int _sharedBubbleCount = 0;
        private static int _sharedTargetCount = 0;
        private static List<LibVLCSharp.Shared.MediaPlayer> _allMediaPlayers = new();

        /// <summary>Owner tag used for every VideoService lease + trace line from this window.</summary>
        private const string LeaseTag = "bubble-count";
        /// <summary>Bounded, pumped budget for the player Stop() batch during teardown. This path
        /// hosts a VideoView (HwndHost), so detaching before the players are stopped is the historic
        /// multi-monitor crash - but the wait must never be unbounded on the dispatcher (#766).
        /// Sized to VideoService.CloseAll's own VideoView budget (500ms + a 4s pumped extension):
        /// a shorter one would start detaching from players that are merely SLOW to stop, and the
        /// wait pumps, so it costs nothing while Stop() is healthy and does not itself look like a
        /// UI stall to the wedge watchdog.</summary>
        private const int StopWaitMs = 4000;
        /// <summary>Fallback duration when the metadata cache has never seen this video. LibVLC's
        /// LengthChanged corrects it within a second of Play() - see AdoptRealDuration.</summary>
        private const double FallbackDurationSeconds = 30;

        /// <summary>Duration of the last played video in seconds (shared for XP scaling)</summary>
        internal static double LastVideoDurationSeconds { get; private set; } = 30;

        public BubbleCountWindow(string videoPath, BubbleCountService.Difficulty difficulty,
            bool strictMode, Action<bool> onComplete,
            Screen? screen = null, bool isPrimary = true)
        {
            InitializeComponent();

            _videoPath = videoPath;
            _difficulty = difficulty;
            _strictMode = strictMode;
            _onComplete = onComplete;
            _screen = screen ?? Screen.PrimaryScreen!;
            _isPrimary = isPrimary;

            // Set difficulty display
            TxtDifficulty.Text = $" ({difficulty})";

            // Handle strict mode
            if (_strictMode)
            {
                TxtStrict.Visibility = Visibility.Visible;
                TxtEscHint.Visibility = Visibility.Collapsed;
            }

            // Initial small position on target screen (will maximize after show)
            // Convert physical pixels to WPF DIPs using per-screen DPI
            var dpiScale = GetDpiForScreen(_screen);
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = (_screen.Bounds.X + 100) / dpiScale;
            Top = (_screen.Bounds.Y + 100) / dpiScale;
            Width = 400;
            Height = 300;

            // Load bubble image
            LoadBubbleImage();

            // Key handler
            KeyDown += OnKeyDown;

            // Hide from Alt+Tab
            SourceInitialized += (s, e) =>
            {
                var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                var exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
                SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_TOOLWINDOW);
                // Cache the handle for VideoService's wedge escape hatch while we still can: these
                // windows are fullscreen + topmost, and once the UI thread wedges nothing can
                // resolve a Window to its HWND any more (#765/#766).
                App.Video?.RegisterManagedWindow(hwnd);
            };

            // Reclaim focus when stolen by other windows (only primary needs focus)
            if (_isPrimary)
            {
                Deactivated += (s, e) =>
                {
                    if (!_gameCompleted && !_videoEnded)
                    {
                        Dispatcher.BeginInvoke(new Action(() =>
                        {
                            if (!_gameCompleted && !_videoEnded)
                            {
                                Activate();
                                Focus();
                            }
                        }), DispatcherPriority.Input);
                    }
                };
            }

            // Register window
            _allWindows.Add(this);

            // Start when loaded
            Loaded += OnLoaded;
        }

        /// <summary>
        /// Make sure the shared LibVLC instance exists before the game windows are built.
        /// Deliberately caches NOTHING: the player is built through
        /// <see cref="VideoService.CreateManagedPlayer"/> under _libVLCLock at the moment it is
        /// needed. The old code copied SharedLibVLC into a static field here and consumed it in
        /// OnLoaded, so a retire landing in that gap built the player on an already-quarantined
        /// instance (#766).
        /// </summary>
        private static bool EnsureLibVLCReady()
        {
            if (VideoService.SharedLibVLC != null) return true;

            App.Logger?.Information("BubbleCountWindow: LibVLC not ready, triggering initialization...");
            App.Video?.PreloadLibVLC();

            if (VideoService.WaitForLibVLC(5000) && VideoService.SharedLibVLC != null)
            {
                App.Logger?.Information("BubbleCountWindow: LibVLC became available after wait");
                return true;
            }

            App.Logger?.Error("BubbleCountWindow: LibVLC not available from VideoService");
            return false;
        }

        /// <summary>
        /// Show bubble count game on all monitors using LibVLC
        /// </summary>
        public static void ShowOnAllMonitors(string videoPath, BubbleCountService.Difficulty difficulty,
            bool strictMode, Action<bool> onComplete)
        {
            // Reset shared state
            lock (_cleanupLock)
            {
                _isCleaningUp = false;
            }
            var orphanWindows = _allWindows.ToList();
            var orphanPlayers = _allMediaPlayers.ToList();
            _allWindows.Clear();
            _allMediaPlayers.Clear();
            _sharedBubbleCount = 0;
            _sharedTargetCount = 0;

            VideoDiag.Log("BUBBLE", $"BEGIN {Path.GetFileName(videoPath)} (difficulty={difficulty}, strict={strictMode})");

            // Players are LEASED from VideoService now, so dropping the references here would strand
            // them in its lease registry for the life of the process and every later wedge rescue
            // would try to stop players nobody owns. Only reachable when a previous game threw
            // partway through its show/teardown; normally both lists are already empty. Same order
            // as CloseAllWindows - stop, detach, close, release - because releasing a player still
            // attached to a live VideoView is the multi-monitor detach crash.
            if (orphanPlayers.Count > 0 || orphanWindows.Count > 0)
            {
                VideoDiag.Log("BUBBLE", $"leftovers from a previous game: {orphanWindows.Count} window(s), {orphanPlayers.Count} player(s) - clearing before starting");
                var orphanStops = StopPlayersBounded(orphanPlayers, 500, "ORPHAN");
                foreach (var window in orphanWindows)
                {
                    try
                    {
                        if (window._videoView != null) window._videoView.MediaPlayer = null;
                        window._safetyTimer?.Stop();
                        window._bubbleSpawnTimer?.Stop();
                        window.Close();
                    }
                    catch (Exception ex)
                    {
                        App.Logger?.Debug("BubbleCountWindow: Error closing orphaned window - {Error}", ex.Message);
                    }
                }
                ReleaseStoppedPlayers(orphanStops);
            }

            // A wedged native Stop() leaves a LibVLC thread stuck in this process forever, and #766
            // is precisely "poisoning at 22:05, bubble-count video at 22:18, dispatcher dead". Don't
            // stand a fresh player up on the wreckage - the caller treats this like any other
            // can't-show and the next scheduled game gets a clean instance.
            var poisonMs = VideoService.NativePoisonCooldownRemainingMs;
            if (poisonMs > 0)
            {
                App.Logger?.Warning("BubbleCountWindow: shared LibVLC poisoned by a wedged Stop() - not starting ({Sec:F0}s of cooldown left)", poisonMs / 1000.0);
                VideoDiag.Log("BUBBLE", $"ABORT - native poison cooldown, {poisonMs}ms remaining");
                onComplete?.Invoke(false);
                return;
            }

            // Ensure LibVLC is ready
            if (!EnsureLibVLCReady())
            {
                App.Logger?.Error("BubbleCountWindow: LibVLC not available, cannot show game");
                VideoDiag.Log("BUBBLE", "ABORT - no shared LibVLC instance");
                onComplete?.Invoke(false);
                return;
            }

            var settings = App.Settings.Current;
            var allScreens = App.GetAllScreensCached();
            var screens = settings.DualMonitorEnabled
                ? allScreens
                : new[] { allScreens.FirstOrDefault(s => s.Primary) ?? allScreens.FirstOrDefault()! };

            if (screens.Length == 0 || screens[0] == null)
            {
                App.Logger?.Error("BubbleCountWindow: No screens available");
                onComplete?.Invoke(false);
                return;
            }

            var primary = screens.FirstOrDefault(s => s.Primary) ?? screens[0];

            App.Logger?.Information("BubbleCountWindow: Creating windows on {Count} screens, video={Video}",
                screens.Length, videoPath);

            // Armed BEFORE the first window exists: the #766 wedge is inside the native start-up
            // sequence itself (MediaPlayer ctor / HwndHost attach / Play), which all runs on the UI
            // thread from OnLoaded. Disarmed by CloseAllWindows / ForceCloseAll.
            App.Video?.ArmManagedWedgeWatchdog(LeaseTag);

            var showSw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                // Create primary window with audio
                App.Logger?.Information("BubbleCountWindow: Creating primary window...");
                var primaryWindow = new BubbleCountWindow(videoPath, difficulty, strictMode, onComplete, primary, true);

                App.Logger?.Information("BubbleCountWindow: Showing primary window...");
                primaryWindow.Show();
                primaryWindow.WindowState = WindowState.Maximized;
                ForceTopmost(primaryWindow);
                App.Logger?.Information("BubbleCountWindow: Primary window shown and maximized");
                VideoDiag.Log("BUBBLE", $"primary window shown +{showSw.ElapsedMilliseconds}ms");

                // Create secondary windows (muted)
                foreach (var screen in screens.Where(s => s != primary))
                {
                    App.Logger?.Information("BubbleCountWindow: Creating secondary window on {Screen}...", screen.DeviceName);
                    var secondaryWindow = new BubbleCountWindow(videoPath, difficulty, strictMode, onComplete, screen, false);
                    secondaryWindow.Show();
                    secondaryWindow.WindowState = WindowState.Maximized;
                    ForceTopmost(secondaryWindow);
                    VideoDiag.Log("BUBBLE", $"secondary window shown on {screen.DeviceName} +{showSw.ElapsedMilliseconds}ms");
                }

                // Activate primary last so it has focus for keyboard input
                primaryWindow.Activate();
                App.Logger?.Information("BubbleCountWindow: All windows created and shown successfully");
                VideoDiag.Log("BUBBLE", $"show complete - {screens.Length} window(s) on screen +{showSw.ElapsedMilliseconds}ms");
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "BubbleCountWindow: Failed to create/show windows");
                VideoDiag.Log("BUBBLE", $"show THREW +{showSw.ElapsedMilliseconds}ms: {ex.Message}");
                App.Video?.DisarmManagedWedgeWatchdog();
                onComplete?.Invoke(false);
            }
        }

        private static void ForceTopmost(Window window)
        {
            try
            {
                var hwnd = new System.Windows.Interop.WindowInteropHelper(window).Handle;
                SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
            }
            catch { }
        }

        /// <summary>
        /// Force close all bubble count windows (used by panic button)
        /// Follows same cleanup pattern as VideoService
        /// </summary>
        public static void ForceCloseAll()
        {
            lock (_cleanupLock)
            {
                if (_isCleaningUp)
                {
                    App.Logger?.Debug("BubbleCountWindow.ForceCloseAll: Already cleaning up");
                    return;
                }
                _isCleaningUp = true;
            }

            try
            {
                App.Logger?.Information("BubbleCountWindow.ForceCloseAll: Closing {Count} windows, {Players} players",
                    _allWindows.Count, _allMediaPlayers.Count);
                VideoDiag.Log("BUBBLE", $"FORCE CLOSE begin - {_allWindows.Count} window(s), {_allMediaPlayers.Count} player(s)");

                // Stop all media players FIRST (like VideoService)
                var playersCopy = _allMediaPlayers.ToList();
                _allMediaPlayers.Clear();

                // Panic path: a shorter budget than the normal close. The user has already asked for
                // the screen back, so a straggler goes to the quarantine path rather than holding
                // the dispatcher any longer.
                var stopPairs = StopPlayersBounded(playersCopy, 500, "FORCE CLOSE");

                // Detach VideoViews from players
                var windowsCopy = _allWindows.ToList();
                foreach (var window in windowsCopy)
                {
                    try
                    {
                        if (window._videoView != null)
                        {
                            window._videoView.MediaPlayer = null;
                        }
                    }
                    catch (Exception ex)
                    {
                        App.Logger?.Debug("BubbleCountWindow: Error detaching VideoView - {Error}", ex.Message);
                    }
                }

                // Small delay after detaching (pump messages to avoid deadlock)
                if (windowsCopy.Count > 0)
                {
                    WaitWithMessagePump(50);
                }

                // Close all windows
                _allWindows.Clear();
                foreach (var window in windowsCopy)
                {
                    try
                    {
                        window._safetyTimer?.Stop();
                        window._bubbleSpawnTimer?.Stop();
                        window.Close();
                    }
                    catch (Exception ex)
                    {
                        App.Logger?.Debug("BubbleCountWindow: Error closing window - {Error}", ex.Message);
                    }
                }

                // Hand the players back: VideoService disposes the ones that stopped and quarantines
                // (never disposes) any whose Stop() never returned.
                ReleaseStoppedPlayers(stopPairs);

                // Disarmed here, not at the top: the teardown above (stop batch, VideoView detach,
                // window close) is the other place this path can wedge, so it stays guarded to the
                // end - the same reasoning that keeps VideoService's watchdog armed through CloseAll.
                // It must still happen BEFORE the callbacks below, which can start the next game and
                // arm a fresh watchdog.
                App.Video?.DisarmManagedWedgeWatchdog();

                // Reset the service busy state
                App.BubbleCount?.ResetBusyState();

                App.Logger?.Information("BubbleCountWindow.ForceCloseAll: Complete");
                VideoDiag.Log("BUBBLE", "FORCE CLOSE complete");
            }
            finally
            {
                lock (_cleanupLock)
                {
                    _isCleaningUp = false;
                }
            }
        }

        /// <summary>
        /// Check if any bubble count window is currently open
        /// </summary>
        public static bool IsAnyOpen() => _allWindows.Count > 0;

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            App.Logger?.Information("BubbleCountWindow.OnLoaded: Starting (primary={IsPrimary}, video={Video})", _isPrimary, _videoPath);

            // Every step below is a native or HwndHost call on the UI THREAD, and until now not one
            // of them left a trace line - which is why #766's freeze arrived as a silent hang. The
            // +ms stamps mirror VideoService's win[tag] lines: the last one written names the call
            // the dispatcher died inside.
            var tag = _isPrimary ? "primary" : "secondary";
            var sw = System.Diagnostics.Stopwatch.StartNew();
            VideoDiag.Log("BUBBLE", $"win[{tag}]: OnLoaded begin on {_screen.DeviceName}");

            try
            {
                // Verify video file exists
                if (!File.Exists(_videoPath))
                {
                    App.Logger?.Error("BubbleCountWindow.OnLoaded: Video file not found: {Path}", _videoPath);
                    VideoDiag.Log("BUBBLE", $"win[{tag}]: ABORT - video file not found");
                    if (_isPrimary)
                    {
                        CloseAllWindows(false);
                    }
                    return;
                }

                App.Logger?.Information("BubbleCountWindow.OnLoaded: Creating VideoView and MediaPlayer...");
                // Create VideoView and MediaPlayer for this window
                _videoView = new VideoView
                {
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Stretch,
                    Background = Brushes.Black
                };
                VideoDiag.Log("BUBBLE", $"win[{tag}]: VideoView built +{sw.ElapsedMilliseconds}ms");

                // The player is built by VideoService under _libVLCLock, registered for wedge
                // rescue/quarantine, and hands back the exact instance it was built on. The Media
                // below MUST come from that same reference and not from a fresh read of
                // SharedLibVLC - a retire in between would mix instances (#766).
                LibVLC? libVlcOwner = null;
                var video = App.Video;
                if (video != null) _mediaPlayer = video.CreateManagedPlayer($"{LeaseTag}[{tag}]", out libVlcOwner);
                if (_mediaPlayer == null || libVlcOwner == null)
                {
                    App.Logger?.Error("BubbleCountWindow.OnLoaded: could not lease a MediaPlayer from VideoService");
                    VideoDiag.Log("BUBBLE", $"win[{tag}]: ABORT - no managed player +{sw.ElapsedMilliseconds}ms");
                    if (_isPrimary)
                    {
                        CloseAllWindows(false);
                    }
                    return;
                }
                _allMediaPlayers.Add(_mediaPlayer);
                VideoDiag.Log("BUBBLE", $"win[{tag}]: MediaPlayer leased +{sw.ElapsedMilliseconds}ms");

                // NOTE: the user's chosen output device is applied from the Playing handler below,
                // NOT here. LibVLC has no audio output until playback starts, so a call at
                // construction time enumerated an empty device list, matched nothing and silently
                // no-op'd - bubble-count audio has been ignoring the user's output device ever
                // since (the same #707/#708 defect VideoService already fixed).

                // Add VideoView to container
                VideoContainer.Children.Add(_videoView);

                if (_isPrimary)
                {
                    // Duration comes from the shared metadata cache; a miss falls back and LibVLC's
                    // LengthChanged corrects everything a moment later (AdoptRealDuration). The old
                    // synchronous MediaFoundationReader open ran on the UI thread, on a file that
                    // may be a decrypted pack temp on a slow disk.
                    _videoDurationSeconds = ResolveVideoDurationSeconds(_videoPath);
                    LastVideoDurationSeconds = _videoDurationSeconds;
                    VideoDiag.Log("BUBBLE", $"win[{tag}]: duration={_videoDurationSeconds:F1}s +{sw.ElapsedMilliseconds}ms");

                    // Calculate target bubbles
                    CalculateTargetBubbles();
                    _sharedTargetCount = _targetBubbleCount;

                    // Setup primary player events
                    _mediaPlayer.LengthChanged += (s, args) =>
                    {
                        var duration = args.Length / 1000.0;
                        App.Logger?.Debug("BubbleCountWindow: LengthChanged - duration={Duration}s", duration);
                        var dispatcher = Application.Current?.Dispatcher;
                        if (dispatcher == null || dispatcher.HasShutdownStarted) return;
                        dispatcher.BeginInvoke(new Action(() => AdoptRealDuration(duration)));
                    };

                    _mediaPlayer.EndReached += OnMediaEndReached;
                    _mediaPlayer.EncounteredError += OnMediaError;

                    // Start safety timer
                    StartSafetyTimer(_videoDurationSeconds);

                    // Start spawning bubbles (delayed to let layout complete)
                    StartBubbleSpawning();

                    App.Logger?.Information("BubbleCount game started - Target: {Target} bubbles, Duration: {Duration}s, Difficulty: {Diff}",
                        _targetBubbleCount, _videoDurationSeconds, _difficulty);

                    // Configure audio for primary
                    _mediaPlayer.Mute = false;
                    var volume = (int)((App.Settings.Current.MasterVolume / 100.0) * 100);
                    _mediaPlayer.Volume = Math.Clamp(volume, 0, 100);

                    // Everything that needs a LIVE audio output hangs off Playing, subscribed BEFORE
                    // Play(): a fast start can raise Playing inside the Play() call itself.
                    var player = _mediaPlayer;
                    int audioRouted = 0; // Playing can fire again after a restart - route once
                    player.Playing += (s, args) =>
                    {
                        VideoDiag.Log("BUBBLE", "PLAYING (aout is live)");
                        if (Interlocked.Exchange(ref audioRouted, 1) != 0) return;
                        // Anything heavier than a property set goes off the LibVLC event thread.
                        Task.Run(() =>
                        {
                            try
                            {
                                // Same staleness guard RouteAndProbeAudio uses: teardown may have
                                // taken ownership of this player between Playing and this task, and
                                // it stops/releases (and eventually disposes) it off-thread.
                                if (_isCleaningUp || _videoEnded) return;
                                bool applied = App.Audio?.ApplyPreferredDevice(player) ?? false;
                                var routing = App.Audio?.DescribeLibVlcAudioRouting(player) ?? "audio service unavailable";
                                App.Logger?.Information("[VIDEO] aout live for bubble count: deviceApplied={Applied} {Routing}", applied, routing);
                                VideoDiag.Log("AOUT", $"bubble-count deviceApplied={applied} {routing}");
                            }
                            catch (Exception ex)
                            {
                                App.Logger?.Debug("BubbleCountWindow: audio route/probe failed: {Error}", ex.Message);
                            }
                        });
                    };
                }
                else
                {
                    // Secondary window - sync target count
                    _targetBubbleCount = _sharedTargetCount;
                }

                // Attach player to view and start playback
                App.Logger?.Information("BubbleCountWindow.OnLoaded: Attaching MediaPlayer to VideoView...");
                _videoView.MediaPlayer = _mediaPlayer;
                VideoDiag.Log("BUBBLE", $"win[{tag}]: VideoView attached +{sw.ElapsedMilliseconds}ms");

                App.Logger?.Information("BubbleCountWindow.OnLoaded: Creating Media for path: {Path}", _videoPath);
                // Disposed once Play() has returned: libvlc_media_player_set_media retains the media
                // synchronously, so this releases OUR reference and nothing else. It was leaked
                // outright before - one native media per game window, forever.
                using var media = new Media(libVlcOwner, _videoPath, FromType.FromPath);
                VideoDiag.Log("BUBBLE", $"win[{tag}]: Media ctor +{sw.ElapsedMilliseconds}ms");
                // Secondaries skip audio decoding entirely. Setting Mute=true after Play() opened
                // a second WASAPI session on the same MMDevice; Windows collapsed both into one
                // per-app mixer slider and the result was doubled/desynced or zero-volume audio.
                if (!_isPrimary) media.AddOption(":no-audio");

                App.Logger?.Information("BubbleCountWindow.OnLoaded: Calling Play()...");
                VideoDiag.Log("BUBBLE", $"win[{tag}]: Play() issued +{sw.ElapsedMilliseconds}ms");
                var playResult = _mediaPlayer.Play(media);
                VideoDiag.Log("BUBBLE", $"win[{tag}]: Play() returned {playResult} +{sw.ElapsedMilliseconds}ms");

                if (!playResult)
                {
                    App.Logger?.Error("BubbleCountWindow.OnLoaded: Play() returned false! Video will not play.");
                }

                App.Logger?.Information("BubbleCountWindow: Started video on {Screen} (primary={IsPrimary}, playResult={Result}, state={State})",
                    _screen.DeviceName, _isPrimary, playResult, _mediaPlayer.State);
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Failed to initialize bubble count game");
                VideoDiag.Log("BUBBLE", $"win[{tag}]: OnLoaded THREW +{sw.ElapsedMilliseconds}ms: {ex.Message}");
                if (_isPrimary)
                {
                    CloseAllWindows(false);
                }
            }
        }

        /// <summary>
        /// Duration for the game clock: metadata-cache hit, else a fallback that LibVLC's
        /// LengthChanged replaces within a second. A miss also queues the background parse so the
        /// next game on this file is a straight hit.
        /// </summary>
        private static double ResolveVideoDurationSeconds(string path)
        {
            try
            {
                var cache = App.Video?.MetadataCache;
                var cached = cache?.TryGetDuration(path);
                if (cached is > 0) return cached.Value;
                if (cache != null) _ = cache.GetOrComputeDurationAsync(path);
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("BubbleCountWindow: duration lookup failed - {Error}", ex.Message);
            }
            return FallbackDurationSeconds;
        }

        /// <summary>
        /// Adopt the real clip length once LibVLC reports it (UI thread). Re-derives the bubble
        /// target, re-spaces the spawn timer and re-arms both the safety timer and the interaction
        /// queue's stuck-detection window - without this last one a long video started on the
        /// fallback duration would be force-cleaned up mid-game.
        /// </summary>
        private void AdoptRealDuration(double seconds)
        {
            if (!_isPrimary || seconds <= 0 || _videoEnded || _isCleaningUp) return;
            if (Math.Abs(seconds - _videoDurationSeconds) < 1.0) return;

            _videoDurationSeconds = seconds;
            LastVideoDurationSeconds = seconds;

            CalculateTargetBubbles();
            _sharedTargetCount = _targetBubbleCount;
            foreach (var window in _allWindows.ToList())
            {
                window._targetBubbleCount = _targetBubbleCount;
            }

            if (_bubbleSpawnTimer != null)
            {
                var intervalMs = (_videoDurationSeconds * 1000) / Math.Max(1, _targetBubbleCount);
                _bubbleSpawnTimer.Interval = TimeSpan.FromMilliseconds(intervalMs * 0.7);
            }

            StartSafetyTimer(seconds);
            App.InteractionQueue?.ExtendTimeout(seconds + 120);

            App.Logger?.Information("BubbleCountWindow: adopted real duration {Duration:F1}s (target now {Target} bubbles)",
                seconds, _targetBubbleCount);
            VideoDiag.Log("BUBBLE", $"duration adopted from LibVLC: {seconds:F1}s, target={_targetBubbleCount}");
        }

        private void OnMediaEndReached(object? sender, EventArgs e)
        {
            // CRITICAL: Detach from LibVLC thread immediately (same pattern as VideoService)
            App.Logger?.Information("BubbleCountWindow: EndReached fired, _isCleaningUp={Cleaning}", _isCleaningUp);
            VideoDiag.Log("BUBBLE", $"EndReached (cleaningUp={_isCleaningUp})");

            Task.Run(() =>
            {
                try
                {
                    if (_isCleaningUp)
                    {
                        App.Logger?.Debug("BubbleCountWindow: EndReached skipped - cleanup in progress");
                        return;
                    }

                    var dispatcher = Application.Current?.Dispatcher;
                    if (dispatcher == null || dispatcher.HasShutdownStarted)
                    {
                        App.Logger?.Warning("BubbleCountWindow: Dispatcher unavailable in EndReached");
                        return;
                    }

                    dispatcher.BeginInvoke(() =>
                    {
                        if (!_isCleaningUp && _isPrimary)
                        {
                            OnVideoEnded();
                        }
                    });
                }
                catch (Exception ex)
                {
                    App.Logger?.Error(ex, "BubbleCountWindow: Error in EndReached handler");
                }
            });
        }

        private void OnMediaError(object? sender, EventArgs e)
        {
            App.Logger?.Error("BubbleCountWindow: Media playback error");
            VideoDiag.Log("BUBBLE", "EncounteredError - ending the game");

            Task.Run(() =>
            {
                try
                {
                    var dispatcher = Application.Current?.Dispatcher;
                    if (dispatcher == null || dispatcher.HasShutdownStarted) return;

                    dispatcher.BeginInvoke(() =>
                    {
                        if (_isPrimary)
                        {
                            OnVideoEnded();
                        }
                    });
                }
                catch (Exception ex)
                {
                    App.Logger?.Error(ex, "BubbleCountWindow: Error in MediaError handler");
                }
            });
        }

        private void CalculateTargetBubbles()
        {
            double baseRate = _difficulty switch
            {
                BubbleCountService.Difficulty.Easy => 3,
                BubbleCountService.Difficulty.Medium => 5,
                BubbleCountService.Difficulty.Hard => 8,
                _ => 5
            };

            var scaledCount = (baseRate / 30.0) * _videoDurationSeconds;
            var variance = scaledCount * 0.2;
            _targetBubbleCount = (int)Math.Round(scaledCount + (_random.NextDouble() * variance * 2 - variance));
            _targetBubbleCount = Math.Max(3, _targetBubbleCount);
        }

        private void LoadBubbleImage()
        {
            try
            {
                var resolved = Services.ModResourceResolver.ResolveImage("bubble.png");
                if (resolved is BitmapImage bmp)
                {
                    _bubbleImage = bmp.IsFrozen ? bmp : bmp.Clone();
                    if (!_bubbleImage.IsFrozen) _bubbleImage.Freeze();
                }
                else
                {
                    var resourceUri = new Uri("pack://application:,,,/Resources/bubble.png", UriKind.Absolute);
                    _bubbleImage = new BitmapImage();
                    _bubbleImage.BeginInit();
                    _bubbleImage.UriSource = resourceUri;
                    _bubbleImage.CacheOption = BitmapCacheOption.OnLoad;
                    _bubbleImage.EndInit();
                    _bubbleImage.Freeze();
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning("Failed to load bubble image: {Error}", ex.Message);
            }
        }

        private void StartSafetyTimer(double videoDurationSeconds)
        {
            _safetyTimer?.Stop();

            var timeoutSeconds = videoDurationSeconds + 5;

            _safetyTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(timeoutSeconds) };
            _safetyTimer.Tick += (s, e) =>
            {
                _safetyTimer?.Stop();
                if (!_videoEnded && !_isCleaningUp)
                {
                    App.Logger?.Warning("BubbleCountWindow: Safety timeout - forcing video end");
                    OnVideoEnded();
                }
            };
            _safetyTimer.Start();

            App.Logger?.Debug("BubbleCountWindow: Safety timer started for {Duration}s", timeoutSeconds);
        }

        private void StartBubbleSpawning()
        {
            if (!_isPrimary) return;

            var intervalMs = (_videoDurationSeconds * 1000) / Math.Max(1, _targetBubbleCount);

            _bubbleSpawnTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(intervalMs * 0.7)
            };

            _bubbleSpawnTimer.Tick += (s, e) =>
            {
                if (_sharedBubbleCount < _targetBubbleCount && !_videoEnded && !_isCleaningUp)
                {
                    if (_random.NextDouble() < 0.7 || _sharedBubbleCount < _targetBubbleCount / 2)
                    {
                        SpawnBubbleOnAllWindows();
                    }
                }
            };

            // Delay bubble spawning until layout is complete
            Task.Delay(1500).ContinueWith(_ =>
            {
                try
                {
                    Application.Current?.Dispatcher?.Invoke(() =>
                    {
                        if (_videoEnded || _isCleaningUp) return;

                        App.Logger?.Debug("BubbleCount: Starting bubble spawn on screen {Screen}",
                            _screen.DeviceName);

                        _bubbleSpawnTimer?.Start();
                        SpawnBubbleOnAllWindows();
                    });
                }
                catch (Exception ex)
                {
                    App.Logger?.Debug("BubbleCount: Failed to start spawning - {Error}", ex.Message);
                }
            });
        }

        private void SpawnBubbleOnAllWindows()
        {
            if (_sharedBubbleCount >= _targetBubbleCount) return;
            _sharedBubbleCount++;
            _bubbleCount = _sharedBubbleCount;

            // Random position (relative 0-1)
            var relX = _random.NextDouble() * 0.7 + 0.15;
            var relY = _random.NextDouble() * 0.5 + 0.25;
            var size = _random.Next(120, 225);

            // Spawn on ONE random window - use Background priority to not block video rendering
            var windows = _allWindows.ToList();
            if (windows.Count > 0)
            {
                var randomWindow = windows[_random.Next(windows.Count)];
                randomWindow.Dispatcher.BeginInvoke(() =>
                {
                    randomWindow.SpawnBubbleAt(relX, relY, size);
                }, DispatcherPriority.Background);
            }
        }

        private void SpawnBubbleAt(double relX, double relY, int size)
        {
            try
            {
                // Convert relative position to screen coordinates (WPF DIPs)
                // Use WorkingArea (excludes taskbar) and divide each component by DPI separately
                var dpiScale = GetDpiForScreen(_screen);
                var area = _screen.WorkingArea;
                var screenX = area.X / dpiScale + (relX * area.Width / dpiScale) - size / 2.0;
                var screenY = area.Y / dpiScale + (relY * area.Height / dpiScale) - size / 2.0;

                // Sound plays on pop (in StartPopping), not on spawn

                // Bubble is now a separate window (doesn't block video rendering)
                var bubble = new CountBubble(_bubbleImage, size, screenX, screenY, _random,
                    PlayPopSound);
                _activeBubbles.Add(bubble);
                EnsureBubbleAnimTimer();
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("Failed to spawn bubble: {Error}", ex.Message);
            }
        }

        /// <summary>Lazily start the shared animation timer once there are bubbles to animate.</summary>
        private void EnsureBubbleAnimTimer()
        {
            if (_bubbleAnimTimer != null) return;
            _bubbleAnimTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(BubbleAnimTickMs)
            };
            _bubbleAnimTimer.Tick += AnimateAllCountBubbles;
            _bubbleAnimTimer.Start();
        }

        /// <summary>Advances every active count bubble and reaps finished ones. Stops the timer
        /// when no bubbles remain (it restarts on the next spawn).</summary>
        private void AnimateAllCountBubbles(object? sender, EventArgs e)
        {
            for (int i = _activeBubbles.Count - 1; i >= 0; i--)
            {
                if (i >= _activeBubbles.Count) continue;
                var bubble = _activeBubbles[i];
                bubble.Tick(BubbleAnimTickMs);
                if (bubble.IsFinished)
                {
                    _activeBubbles.RemoveAt(i);
                    bubble.Dispose();
                }
            }

            if (_activeBubbles.Count == 0)
            {
                _bubbleAnimTimer?.Stop();
                _bubbleAnimTimer = null;
            }
        }

        private void PlayPopSound()
        {
            // Run everything off UI thread to avoid blocking LibVLC rendering
            var soundIndex = _random.Next(3);
            var masterVolume = (App.Settings?.Current?.MasterVolume ?? 100) / 100f;
            var bubblesVolume = (App.Settings?.Current?.BubblesVolume ?? 50) / 100f;

            Task.Run(() =>
            {
                try
                {
                    var soundsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "sounds", "bubbles");
                    var popFiles = new[] { "Pop.mp3", "Pop2.mp3", "Pop3.mp3" };
                    var popPath = Path.Combine(soundsPath, popFiles[soundIndex]);

                    if (!File.Exists(popPath)) return;

                    var volume = (float)Math.Pow(masterVolume * bubblesVolume, 1.5);

                    using var audioFile = new AudioFileReader(popPath);
                    audioFile.Volume = volume;
                    using var outputDevice = new WaveOutEvent();
                    App.Audio?.ApplyPreferredDevice(outputDevice);
                    outputDevice.Init(audioFile);
                    outputDevice.Play();
                    while (outputDevice.PlaybackState == PlaybackState.Playing)
                    {
                        Thread.Sleep(50);
                    }
                }
                catch { }
            });
        }

        private void OnVideoEnded()
        {
            if (_videoEnded || _isCleaningUp) return;
            _videoEnded = true;

            _safetyTimer?.Stop();
            _bubbleSpawnTimer?.Stop();

            // Mark all windows as ended
            foreach (var window in _allWindows.ToList())
            {
                window._videoEnded = true;
                window._bubbleSpawnTimer?.Stop();
            }

            // Clear remaining bubbles on all windows (bubbles are separate windows now).
            // Dispose directly: the shared animation timer that would otherwise finish their
            // pop-out is about to stop, so close them now to avoid orphaned windows.
            foreach (var window in _allWindows.ToList())
            {
                foreach (var bubble in window._activeBubbles.ToArray())
                {
                    bubble.Dispose();
                }
                window._activeBubbles.Clear();
                window._bubbleAnimTimer?.Stop();
                window._bubbleAnimTimer = null;
            }

            // Track video watch time
            if (_isPrimary && _videoDurationSeconds > 0)
            {
                App.Logger?.Information("BubbleCount video watched: {Duration}s", _videoDurationSeconds);
                App.Achievements?.TrackVideoWatched(_videoDurationSeconds);
            }

            // Show result window (only from primary)
            if (_isPrimary)
            {
                ShowResultWindow();
            }
        }

        private void ShowResultWindow()
        {
            // Hide all game windows
            foreach (var window in _allWindows.ToList())
            {
                try { window.Hide(); } catch { }
            }

            BubbleCountResultWindow.ShowOnAllMonitors(
                _sharedBubbleCount,
                _strictMode,
                (success) =>
                {
                    _gameCompleted = true;
                    CloseAllWindows(success);
                });
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape && !_strictMode && !_gameCompleted && !_isCleaningUp)
            {
                _gameCompleted = true;
                CloseAllWindows(false);
            }
        }

        private void CloseAllWindows(bool success)
        {
            lock (_cleanupLock)
            {
                if (_isCleaningUp) return;
                _isCleaningUp = true;
            }

            try
            {
                VideoDiag.Log("BUBBLE", $"CLOSE begin (success={success}) - {_allWindows.Count} window(s), {_allMediaPlayers.Count} player(s)");

                // Stop all players first
                var playersCopy = _allMediaPlayers.ToList();
                _allMediaPlayers.Clear();

                var stopPairs = StopPlayersBounded(playersCopy, StopWaitMs, "CLOSE");

                // Detach VideoViews
                var windowsCopy = _allWindows.ToList();
                foreach (var window in windowsCopy)
                {
                    try
                    {
                        if (window._videoView != null)
                            window._videoView.MediaPlayer = null;
                    }
                    catch { }
                }

                if (windowsCopy.Count > 0) WaitWithMessagePump(50);

                // Close windows
                _allWindows.Clear();
                foreach (var window in windowsCopy)
                {
                    try
                    {
                        window._safetyTimer?.Stop();
                        window._bubbleSpawnTimer?.Stop();
                        window.Close();
                    }
                    catch { }
                }

                // Hand the players back: disposed if they stopped, quarantined if they wedged.
                ReleaseStoppedPlayers(stopPairs);
                VideoDiag.Log("BUBBLE", "CLOSE complete");

                // Disarmed here, not at the top: the teardown above is itself a place this path can
                // wedge, so it stays guarded to the end - but it must happen BEFORE the completion
                // callback, which on the strict-retry path eventually arms a fresh watchdog.
                App.Video?.DisarmManagedWedgeWatchdog();

                // Invoke completion callback
                _onComplete?.Invoke(success);
            }
            finally
            {
                lock (_cleanupLock)
                {
                    _isCleaningUp = false;
                }
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            _safetyTimer?.Stop();
            _bubbleSpawnTimer?.Stop();
            _bubbleAnimTimer?.Stop();
            _bubbleAnimTimer = null;

            foreach (var bubble in _activeBubbles)
            {
                bubble.Dispose();
            }
            _activeBubbles.Clear();

            _allWindows.Remove(this);

            base.OnClosed(e);
        }

        /// <summary>
        /// Stop every player OFF the dispatcher and wait a bounded, pumping interval for the batch.
        /// Returns the player↔task pairs so a Stop() that never came back can be quarantined instead
        /// of disposed (see <see cref="ReleaseStoppedPlayers"/>).
        ///
        /// The wait itself cannot go away: this path hosts a VideoView, and detaching an HwndHost
        /// from a player that is still presenting is the historic multi-monitor crash. What did have
        /// to go is the old inline <c>player.Stop()</c> on the UI thread — a single wedged player
        /// froze the whole app there, with no trace at all (#766).
        /// </summary>
        private static List<(LibVLCSharp.Shared.MediaPlayer Player, Task StopTask)> StopPlayersBounded(
            List<LibVLCSharp.Shared.MediaPlayer> players, int budgetMs, string phase)
        {
            var pairs = players.Select(p => (Player: p, StopTask: Task.Run(() =>
            {
                try { p.Stop(); }
                catch (Exception ex) { App.Logger?.Debug("BubbleCountWindow: Error stopping player - {Error}", ex.Message); }
            }))).ToList();

            if (pairs.Count == 0) return pairs;

            var sw = System.Diagnostics.Stopwatch.StartNew();
            bool settled = WaitForTasksWithMessagePump(pairs.Select(p => p.StopTask).ToArray(), budgetMs);
            VideoDiag.Log("BUBBLE", settled
                ? $"{phase}: {pairs.Count} player Stop() settled after {sw.ElapsedMilliseconds}ms"
                : $"{phase}: player Stop() WEDGED past the {sw.ElapsedMilliseconds}ms bounded wait - detaching anyway (poisoning signature)");
            return pairs;
        }

        /// <summary>
        /// Give the leased players back to VideoService, which disposes the ones that stopped and
        /// roots the ones that did not. Never dispose a wedged player here: that is what poisons the
        /// shared LibVLC instance for every later video (#559).
        /// </summary>
        private static void ReleaseStoppedPlayers(List<(LibVLCSharp.Shared.MediaPlayer Player, Task StopTask)> pairs)
        {
            foreach (var (player, stopTask) in pairs)
            {
                App.Video?.ReleaseManagedPlayer(player, stopTask, LeaseTag);
            }
        }

        /// <summary>
        /// Pumps WPF messages until every task has completed or the budget expires. Returns true
        /// only if they all completed.
        /// </summary>
        private static bool WaitForTasksWithMessagePump(Task[] tasks, int milliseconds)
        {
            var endTime = DateTime.UtcNow.AddMilliseconds(milliseconds);
            while (DateTime.UtcNow < endTime)
            {
                if (tasks.All(t => t.IsCompleted)) return true;
                try
                {
                    Application.Current?.Dispatcher?.Invoke(
                        DispatcherPriority.Background,
                        new Action(() => { }));
                }
                catch
                {
                    return false;
                }
                Thread.Sleep(10);
            }
            return tasks.All(t => t.IsCompleted);
        }

        /// <summary>
        /// Waits for the specified duration while pumping WPF messages,
        /// preventing UI deadlocks with LibVLC callbacks.
        /// </summary>
        private static void WaitWithMessagePump(int milliseconds)
        {
            var endTime = DateTime.UtcNow.AddMilliseconds(milliseconds);
            while (DateTime.UtcNow < endTime)
            {
                try
                {
                    Application.Current?.Dispatcher?.Invoke(
                        DispatcherPriority.Background,
                        new Action(() => { }));
                }
                catch
                {
                    return;
                }
                Thread.Sleep(10);
            }
        }

        #region Per-Screen DPI

        internal static double GetDpiForScreen(Screen screen)
        {
            try
            {
                uint dpiX = 96, dpiY = 96;
                var hMonitor = MonitorFromPoint(new POINT { X = screen.Bounds.X + 1, Y = screen.Bounds.Y + 1 }, 2);

                if (hMonitor != IntPtr.Zero)
                {
                    var result = GetDpiForMonitor(hMonitor, 0, out dpiX, out dpiY);
                    if (result == 0)
                    {
                        return dpiX / 96.0;
                    }
                }

                using var g = System.Drawing.Graphics.FromHwnd(IntPtr.Zero);
                return g.DpiX / 96.0;
            }
            catch
            {
                return 1.0;
            }
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct POINT { public int X; public int Y; }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

        [System.Runtime.InteropServices.DllImport("shcore.dll")]
        private static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

        #endregion
    }

    /// <summary>
    /// Individual bubble for counting game - uses separate window to avoid blocking LibVLC
    /// Same pattern as VideoService's FloatingText
    /// </summary>
    internal class CountBubble : IDisposable
    {
        private readonly Window _window;
        private readonly Image _imageElement;

        private readonly Action? _playSound;

        private double _scale = 0.1;
        private double _targetScale = 1.0;
        private double _opacity = 1.0;
        private double _rotation = 0;
        private bool _isPopping = false;
        private bool _isDisposed = false;
        private double _lifeRemainingMs;
        private readonly int _size;

        /// <summary>True once the pop animation has fully faded out; the owning window
        /// removes and disposes finished bubbles on its shared animation tick.</summary>
        public bool IsFinished { get; private set; }

        // Win32 for topmost
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOACTIVATE = 0x0010;
        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        public CountBubble(BitmapImage? image, int size, double screenX, double screenY,
            Random random, Action? playSound)
        {
            _playSound = playSound;
            _rotation = random.Next(360);
            _size = size;

            _imageElement = new Image
            {
                Width = size,
                Height = size,
                Stretch = Stretch.Uniform,
                Source = image,
                RenderTransformOrigin = new Point(0.5, 0.5)
            };

            if (image == null)
            {
                var drawing = new DrawingGroup();
                using (var ctx = drawing.Open())
                {
                    var gradientBrush = new RadialGradientBrush(
                        Color.FromArgb(200, 255, 182, 193),
                        Color.FromArgb(100, 255, 105, 180));
                    ctx.DrawEllipse(gradientBrush, new Pen(Brushes.White, 2),
                        new Point(size / 2, size / 2), size / 2 - 5, size / 2 - 5);
                }
                _imageElement.Source = new DrawingImage(drawing);
            }

            var transformGroup = new TransformGroup();
            transformGroup.Children.Add(new ScaleTransform(_scale, _scale));
            transformGroup.Children.Add(new RotateTransform(_rotation));
            _imageElement.RenderTransform = transformGroup;

            // Create separate window for bubble (doesn't share visual tree with video)
            _window = new Window
            {
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = Brushes.Transparent,
                ShowInTaskbar = false,
                ShowActivated = false,
                Topmost = true,
                IsHitTestVisible = false,
                Width = size,
                Height = size,
                Left = screenX,
                Top = screenY,
                Content = _imageElement
            };

            _window.Show();

            // Force topmost
            try
            {
                var hwnd = new System.Windows.Interop.WindowInteropHelper(_window).Handle;
                SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
            }
            catch { }

            // Lifespan - bubble stays for 1-1.5 seconds then pops. Driven by the owning
            // window's single shared animation timer (see BubbleCountWindow.AnimateAllCountBubbles)
            // rather than per-bubble timers, which previously meant ~2 DispatcherTimers per bubble.
            _lifeRemainingMs = 1000 + random.Next(500);
        }

        /// <summary>
        /// Advance this bubble one frame. Called by the owning window's shared timer with the
        /// elapsed milliseconds since the last tick. Sets <see cref="IsFinished"/> when done.
        /// </summary>
        public void Tick(double dtMs)
        {
            if (_isDisposed) return;

            try
            {
                if (!_isPopping)
                {
                    _lifeRemainingMs -= dtMs;
                    if (_lifeRemainingMs <= 0) StartPopping();
                }

                if (_isPopping)
                {
                    _scale += 0.08;
                    _opacity -= 0.12;
                    _rotation += 5;

                    if (_opacity <= 0)
                    {
                        IsFinished = true;
                        return;
                    }
                }
                else
                {
                    if (_scale < _targetScale)
                    {
                        _scale = Math.Min(_targetScale, _scale + 0.1);
                    }
                    _rotation += 0.5;
                }

                _window.Opacity = Math.Max(0, _opacity);

                if (_imageElement.RenderTransform is TransformGroup tg && tg.Children.Count >= 2)
                {
                    if (tg.Children[0] is ScaleTransform st)
                    {
                        st.ScaleX = _scale;
                        st.ScaleY = _scale;
                    }
                    if (tg.Children[1] is RotateTransform rt)
                    {
                        rt.Angle = _rotation;
                    }
                }
            }
            catch { }
        }

        private void StartPopping()
        {
            if (_isPopping || _isDisposed) return;
            _isPopping = true;
            _playSound?.Invoke();
        }

        public void ForcePop()
        {
            if (_isDisposed) return;
            StartPopping();
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;
            try { _window.Close(); } catch { }
        }
    }

    // Win32 for BubbleCountWindow
    public partial class BubbleCountWindow
    {
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOACTIVATE = 0x0010;
        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hwnd, int index);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
    }
}
