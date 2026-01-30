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
        private static LibVLC? _libVLC;
        private static List<LibVLCSharp.Shared.MediaPlayer> _allMediaPlayers = new();

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
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = _screen.Bounds.X + 100;
            Top = _screen.Bounds.Y + 100;
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
        /// Get shared LibVLC from VideoService (avoid double initialization)
        /// Always get fresh reference to avoid stale cached values
        /// </summary>
        private static void EnsureLibVLCInitialized()
        {
            // Always get fresh reference from VideoService
            _libVLC = VideoService.SharedLibVLC;
            App.Logger?.Information("BubbleCountWindow: Got LibVLC from VideoService: {IsNull}", _libVLC == null ? "null" : "valid");

            if (_libVLC == null)
            {
                App.Logger?.Information("BubbleCountWindow: LibVLC not ready, triggering initialization...");

                // Try to trigger initialization via App.Video (should have been done at startup)
                App.Video?.PreloadLibVLC();

                // Wait for initialization to complete (up to 5 seconds)
                App.Logger?.Information("BubbleCountWindow: Waiting for LibVLC initialization...");
                if (VideoService.WaitForLibVLC(5000))
                {
                    _libVLC = VideoService.SharedLibVLC;
                    App.Logger?.Information("BubbleCountWindow: LibVLC became available after wait");
                }
                else
                {
                    App.Logger?.Error("BubbleCountWindow: Timed out waiting for LibVLC");
                }
            }

            if (_libVLC != null)
            {
                App.Logger?.Information("BubbleCountWindow: Using shared LibVLC (version {Version})", _libVLC.Version);
            }
            else
            {
                App.Logger?.Error("BubbleCountWindow: LibVLC not available from VideoService");
            }
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
            _allWindows.Clear();
            _allMediaPlayers.Clear();
            _sharedBubbleCount = 0;
            _sharedTargetCount = 0;

            // Ensure LibVLC is ready
            EnsureLibVLCInitialized();

            if (_libVLC == null)
            {
                App.Logger?.Error("BubbleCountWindow: LibVLC not available, cannot show game");
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

                // Create secondary windows (muted)
                foreach (var screen in screens.Where(s => s != primary))
                {
                    App.Logger?.Information("BubbleCountWindow: Creating secondary window on {Screen}...", screen.DeviceName);
                    var secondaryWindow = new BubbleCountWindow(videoPath, difficulty, strictMode, onComplete, screen, false);
                    secondaryWindow.Show();
                    secondaryWindow.WindowState = WindowState.Maximized;
                    ForceTopmost(secondaryWindow);
                }

                // Activate primary last so it has focus for keyboard input
                primaryWindow.Activate();
                App.Logger?.Information("BubbleCountWindow: All windows created and shown successfully");
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "BubbleCountWindow: Failed to create/show windows");
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

                // Stop all media players FIRST (like VideoService)
                var playersCopy = _allMediaPlayers.ToList();
                _allMediaPlayers.Clear();

                foreach (var player in playersCopy)
                {
                    try
                    {
                        player.Stop();
                    }
                    catch (Exception ex)
                    {
                        App.Logger?.Debug("BubbleCountWindow: Error stopping player - {Error}", ex.Message);
                    }
                }

                // Wait for LibVLC to stop rendering
                if (playersCopy.Count > 0)
                {
                    Thread.Sleep(100);
                }

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

                // Small delay after detaching
                if (windowsCopy.Count > 0)
                {
                    Thread.Sleep(50);
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

                // Dispose players asynchronously after windows closed
                if (playersCopy.Count > 0)
                {
                    Task.Run(async () =>
                    {
                        await Task.Delay(750);
                        foreach (var player in playersCopy)
                        {
                            try
                            {
                                player.Dispose();
                            }
                            catch (Exception ex)
                            {
                                App.Logger?.Debug("BubbleCountWindow: Error disposing player - {Error}", ex.Message);
                            }
                        }
                    });
                }

                // Reset the service busy state
                App.BubbleCount?.ResetBusyState();

                App.Logger?.Information("BubbleCountWindow.ForceCloseAll: Complete");
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

            try
            {
                // Verify video file exists
                if (!File.Exists(_videoPath))
                {
                    App.Logger?.Error("BubbleCountWindow.OnLoaded: Video file not found: {Path}", _videoPath);
                    if (_isPrimary)
                    {
                        CloseAllWindows(false);
                    }
                    return;
                }

                if (_libVLC == null)
                {
                    App.Logger?.Error("BubbleCountWindow.OnLoaded: LibVLC is null!");
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

                _mediaPlayer = new LibVLCSharp.Shared.MediaPlayer(_libVLC);
                _allMediaPlayers.Add(_mediaPlayer);

                // Add VideoView to container
                VideoContainer.Children.Add(_videoView);

                if (_isPrimary)
                {
                    // Get video duration
                    _videoDurationSeconds = GetVideoDuration(_videoPath);

                    // Calculate target bubbles
                    CalculateTargetBubbles();
                    _sharedTargetCount = _targetBubbleCount;

                    // Setup primary player events
                    _mediaPlayer.LengthChanged += (s, args) =>
                    {
                        var duration = args.Length / 1000.0;
                        App.Logger?.Debug("BubbleCountWindow: LengthChanged - duration={Duration}s", duration);
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
                }
                else
                {
                    // Secondary window - sync target count
                    _targetBubbleCount = _sharedTargetCount;

                    // Mute secondary
                    _mediaPlayer.Mute = true;
                    _mediaPlayer.Volume = 0;
                }

                // Attach player to view and start playback
                App.Logger?.Information("BubbleCountWindow.OnLoaded: Attaching MediaPlayer to VideoView...");
                _videoView.MediaPlayer = _mediaPlayer;

                App.Logger?.Information("BubbleCountWindow.OnLoaded: Creating Media for path: {Path}", _videoPath);
                var media = new Media(_libVLC, _videoPath, FromType.FromPath);

                App.Logger?.Information("BubbleCountWindow.OnLoaded: Calling Play()...");
                var playResult = _mediaPlayer.Play(media);

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
                if (_isPrimary)
                {
                    CloseAllWindows(false);
                }
            }
        }

        private void OnMediaEndReached(object? sender, EventArgs e)
        {
            // CRITICAL: Detach from LibVLC thread immediately (same pattern as VideoService)
            App.Logger?.Information("BubbleCountWindow: EndReached fired, _isCleaningUp={Cleaning}", _isCleaningUp);

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
                BubbleCountService.Difficulty.Easy => 6,
                BubbleCountService.Difficulty.Medium => 10,
                BubbleCountService.Difficulty.Hard => 16,
                _ => 10
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
                var resourceUri = new Uri("pack://application:,,,/Resources/bubble.png", UriKind.Absolute);
                _bubbleImage = new BitmapImage();
                _bubbleImage.BeginInit();
                _bubbleImage.UriSource = resourceUri;
                _bubbleImage.CacheOption = BitmapCacheOption.OnLoad;
                _bubbleImage.EndInit();
                _bubbleImage.Freeze();
            }
            catch (Exception ex)
            {
                App.Logger?.Warning("Failed to load bubble image: {Error}", ex.Message);
            }
        }

        private double GetVideoDuration(string path)
        {
            try
            {
                using var reader = new MediaFoundationReader(path);
                return reader.TotalTime.TotalSeconds;
            }
            catch
            {
                return 30;
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
                // Convert relative position to screen coordinates
                var screenX = _screen.Bounds.X + (relX * _screen.Bounds.Width) - size / 2;
                var screenY = _screen.Bounds.Y + (relY * _screen.Bounds.Height) - size / 2;

                if (_isPrimary) PlayPopSound();

                // Bubble is now a separate window (doesn't block video rendering)
                var bubble = new CountBubble(_bubbleImage, size, screenX, screenY, _random,
                    _isPrimary ? PlayPopSound : null, OnBubblePopped);
                _activeBubbles.Add(bubble);
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("Failed to spawn bubble: {Error}", ex.Message);
            }
        }

        private void OnBubblePopped(CountBubble bubble)
        {
            _activeBubbles.Remove(bubble);
            bubble.Dispose(); // Close the bubble window
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

            // Clear remaining bubbles on all windows (bubbles are separate windows now)
            foreach (var window in _allWindows.ToList())
            {
                foreach (var bubble in window._activeBubbles.ToArray())
                {
                    bubble.ForcePop();
                }
                window._activeBubbles.Clear();
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
                // Stop all players first
                var playersCopy = _allMediaPlayers.ToList();
                _allMediaPlayers.Clear();

                foreach (var player in playersCopy)
                {
                    try { player.Stop(); } catch { }
                }

                if (playersCopy.Count > 0) Thread.Sleep(100);

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

                if (windowsCopy.Count > 0) Thread.Sleep(50);

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

                // Dispose players async
                if (playersCopy.Count > 0)
                {
                    Task.Run(async () =>
                    {
                        await Task.Delay(750);
                        foreach (var player in playersCopy)
                        {
                            try { player.Dispose(); } catch { }
                        }
                    });
                }

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

            foreach (var bubble in _activeBubbles)
            {
                bubble.Dispose();
            }
            _activeBubbles.Clear();

            _allWindows.Remove(this);

            base.OnClosed(e);
        }
    }

    /// <summary>
    /// Individual bubble for counting game - uses separate window to avoid blocking LibVLC
    /// Same pattern as VideoService's FloatingText
    /// </summary>
    internal class CountBubble : IDisposable
    {
        private readonly Window _window;
        private readonly Image _imageElement;

        private readonly DispatcherTimer _lifeTimer;
        private readonly DispatcherTimer _animTimer;
        private readonly Action? _playSound;
        private readonly Action<CountBubble> _onPopped;

        private double _scale = 0.1;
        private double _targetScale = 1.0;
        private double _opacity = 1.0;
        private double _rotation = 0;
        private bool _isPopping = false;
        private bool _isDisposed = false;
        private readonly int _size;

        // Win32 for topmost
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOACTIVATE = 0x0010;
        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        public CountBubble(BitmapImage? image, int size, double screenX, double screenY,
            Random random, Action? playSound, Action<CountBubble> onPopped)
        {
            _playSound = playSound;
            _onPopped = onPopped;
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

            // Animation timer - use background priority
            _animTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(30) };
            _animTimer.Tick += Animate;
            _animTimer.Start();

            // Life timer - bubble stays for 1-1.5 seconds then pops
            var lifespan = 1000 + random.Next(500);
            _lifeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(lifespan) };
            _lifeTimer.Tick += (s, e) =>
            {
                _lifeTimer.Stop();
                StartPopping();
            };
            _lifeTimer.Start();
        }

        private void Animate(object? sender, EventArgs e)
        {
            if (_isDisposed) return;

            try
            {
                if (_isPopping)
                {
                    _scale += 0.08;
                    _opacity -= 0.12;
                    _rotation += 5;

                    if (_opacity <= 0)
                    {
                        _animTimer.Stop();
                        _onPopped?.Invoke(this);
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
            _lifeTimer.Stop();
            StartPopping();
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;
            _lifeTimer.Stop();
            _animTimer.Stop();
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
