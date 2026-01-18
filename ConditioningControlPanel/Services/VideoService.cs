using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Forms;
using NAudio.Wave;
using LibVLCSharp.Shared;
using LibVLCSharp.WPF;
using Application = System.Windows.Application;
using Screen = System.Windows.Forms.Screen;

namespace ConditioningControlPanel.Services
{
    public class VideoService : IDisposable
    {
        private readonly Random _random = new();
        private Queue<string> _videoQueue = new();  // Performance: Changed to Queue for O(1) dequeue
        private readonly List<Window> _windows = new();
        private readonly List<FloatingText> _targets = new();

        private DispatcherTimer? _scheduler;
        private DispatcherTimer? _attentionTimer;
        private DispatcherTimer? _safetyTimer;

        private bool _isRunning;
        private bool _videoPlaying;
        private bool _strictActive;
        private string? _retryPath;
        private DateTime _startTime;
        private double _duration;
        
        private List<double> _spawnTimes = new();
        private int _hits, _total, _spawned, _penalties;
        private List<Window> _messageWindows = new();  // Track message windows for cleanup
        private bool _codecWarningShown;  // Only show codec warning once per session

        private string _videosPath = "";

        // LibVLC for codec-independent video playback
        private static LibVLC? _libVLC;
        private static readonly object _libVLCLock = new();
        private static bool _libVLCInitialized;
        private static bool _libVLCInitializing;
        private readonly List<LibVLCSharp.Shared.MediaPlayer> _mediaPlayers = new();

        public event EventHandler? VideoAboutToStart; // Fires 1.3s before video
        public event EventHandler? VideoStarted;
        public event EventHandler? VideoEnded;

        /// <summary>
        /// Whether a video is currently playing
        /// </summary>
        public bool IsPlaying => _videoPlaying;

        public VideoService()
        {
            RefreshVideosPath();
            // LibVLC initialization is deferred to first video playback for faster startup
        }

        /// <summary>
        /// Initialize LibVLC for codec-independent video playback.
        /// Uses VLC's bundled codecs instead of Windows Media Foundation.
        /// Called lazily on first video playback to improve startup time.
        /// </summary>
        private void EnsureLibVLCInitialized()
        {
            lock (_libVLCLock)
            {
                if (_libVLCInitialized || _libVLCInitializing) return;
                _libVLCInitializing = true;
            }

            InitializeLibVLCCore();
        }

        private void InitializeLibVLCCore()
        {
            lock (_libVLCLock)
            {
                if (_libVLC != null)
                {
                    _libVLCInitialized = true;
                    _libVLCInitializing = false;
                    return;
                }

                try
                {
                    // Find the libvlc folder - it's in a subdirectory of the app
                    var appDir = AppDomain.CurrentDomain.BaseDirectory;
                    var libvlcPath = Path.Combine(appDir, "libvlc");

                    App.Logger?.Information("Looking for LibVLC in: {Path}", libvlcPath);

                    if (!Directory.Exists(libvlcPath))
                    {
                        // Try alternate location (development)
                        libvlcPath = Path.Combine(appDir, "libvlc", "win-x64");
                        App.Logger?.Information("Trying alternate LibVLC path: {Path}", libvlcPath);
                    }

                    if (Directory.Exists(libvlcPath))
                    {
                        // Initialize LibVLCSharp core with explicit path
                        Core.Initialize(libvlcPath);
                        App.Logger?.Information("LibVLC core initialized from: {Path}", libvlcPath);
                    }
                    else
                    {
                        // Try default initialization
                        Core.Initialize();
                        App.Logger?.Information("LibVLC core initialized from default location");
                    }

                    // Create LibVLC instance with audio and video options
                    _libVLC = new LibVLC(
                        "--no-video-title-show",  // Don't show filename
                        "--no-osd",               // No on-screen display
                        "--aout=directsound",     // Use DirectSound for audio (most compatible on Windows)
                        "--directx-volume=1.0",   // Full volume for DirectX audio
                        "--gain=1.0",             // Audio gain
                        "--no-disable-screensaver", // Don't interfere with screensaver
                        "--verbose=-1"            // Reduce logging
                    );

                    App.Logger?.Information("LibVLC initialized successfully (version {Version})", _libVLC.Version);
                }
                catch (Exception ex)
                {
                    App.Logger?.Error(ex, "Failed to initialize LibVLC - falling back to MediaElement");
                    _libVLC = null;
                }
                finally
                {
                    _libVLCInitialized = true;
                    _libVLCInitializing = false;
                }
            }
        }

        /// <summary>
        /// Refresh the videos path based on current settings.
        /// Call this after changing the custom assets path.
        /// </summary>
        public void RefreshVideosPath()
        {
            _videosPath = Path.Combine(App.EffectiveAssetsPath, "videos");
            Directory.CreateDirectory(_videosPath);
            _videoQueue.Clear();
            App.Logger?.Information("VideoService: Videos path refreshed to {Path}", _videosPath);
        }

        /// <summary>
        /// Update volume on all currently playing videos (for live master volume changes).
        /// </summary>
        public void UpdateMasterVolume(int volume)
        {
            UpdatePlayingVideosVolume();
        }

        /// <summary>
        /// Update video-specific volume (separate from master volume).
        /// </summary>
        public void UpdateVideoVolume(int volume)
        {
            UpdatePlayingVideosVolume();
        }

        /// <summary>
        /// Calculate effective volume combining master and video volume.
        /// </summary>
        private int GetEffectiveVolume()
        {
            var master = App.Settings.Current.MasterVolume;
            var video = App.Settings.Current.VideoVolume;
            return (int)((master / 100.0) * (video / 100.0) * 100);
        }

        /// <summary>
        /// Apply current volume settings to all playing videos.
        /// </summary>
        private void UpdatePlayingVideosVolume()
        {
            var effectiveVolume = GetEffectiveVolume();

            // Update LibVLC media players
            foreach (var player in _mediaPlayers.ToList())
            {
                try
                {
                    if (!player.Mute) // Only update unmuted players (primary audio)
                    {
                        player.Volume = effectiveVolume;
                    }
                }
                catch (Exception ex)
                {
                    App.Logger?.Debug("Failed to update LibVLC player volume: {Error}", ex.Message);
                }
            }

            // Update MediaElement players in active windows
            foreach (var win in _windows.ToList())
            {
                try
                {
                    if (win.Content is Grid g && g.Children.Count > 0 && g.Children[0] is MediaElement me)
                    {
                        me.Volume = effectiveVolume / 100.0;
                    }
                }
                catch (Exception ex)
                {
                    App.Logger?.Debug("Failed to update MediaElement volume: {Error}", ex.Message);
                }
            }
        }

        public void Start()
        {
            if (_isRunning) return;
            _isRunning = true;
            ScheduleNext();
            App.Logger.Information("VideoService started");
        }

        public void Stop()
        {
            _isRunning = false;
            _scheduler?.Stop();
            _attentionTimer?.Stop();
            _safetyTimer?.Stop();

            // Force cleanup of any playing video
            _videoPlaying = false;
            _strictActive = false;
            Cleanup();

            App.Logger?.Information("VideoService stopped");
        }

        public void TriggerVideo()
        {
            // Check if another fullscreen interaction is active (bubble count, lock card)
            // If so, queue this video for later
            if (App.InteractionQueue != null && !App.InteractionQueue.CanStart)
            {
                App.InteractionQueue.TryStart(
                    InteractionQueueService.InteractionType.Video,
                    () => TriggerVideo(),
                    queue: true);
                return;
            }

            // Notify queue we're starting
            App.InteractionQueue?.TryStart(
                InteractionQueueService.InteractionType.Video,
                () => { }, // Already executing
                queue: false);

            // Force close any stuck/existing video windows first
            if (_videoPlaying || _windows.Count > 0)
            {
                App.Logger?.Warning("VideoService: Forcing cleanup of existing video before triggering new one");
                ForceCleanup();
            }

            var path = GetNextVideo();
            if (string.IsNullOrEmpty(path))
            {
                // No video to play - release the queue lock
                App.InteractionQueue?.Complete(InteractionQueueService.InteractionType.Video);
                System.Windows.MessageBox.Show($"No videos in:\n{_videosPath}", "No Videos");
                return;
            }

            // Trigger Bambi Freeze subliminal+audio BEFORE video, but only if:
            // - No minigame is active
            // - Attention checks are NOT enabled (user needs to be alert to click targets)
            var skipFreeze = App.Settings.Current.AttentionChecksEnabled ||
                            (App.BubbleCount != null && App.BubbleCount.IsBusy);

            if (!skipFreeze)
            {
                // Defer the reset until video ends (pass deferReset: true)
                App.Subliminal?.TriggerBambiFreeze(deferReset: true);

                // Small delay to let the freeze effect register before video starts
                Task.Delay(800).ContinueWith(_ =>
                {
                    try
                    {
                        if (Application.Current?.Dispatcher == null) return;
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            PlayVideo(path, App.Settings.Current.StrictLockEnabled);
                        });
                    }
                    catch (Exception ex)
                    {
                        App.Logger?.Warning("VideoService: Delayed video play failed - {Error}", ex.Message);
                    }
                });
            }
            else
            {
                // Attention checks or minigame active - play video without freeze
                PlayVideo(path, App.Settings.Current.StrictLockEnabled);
            }
        }

        /// <summary>
        /// Play a specific video file (used for startup video)
        /// </summary>
        public void PlaySpecificVideo(string videoPath, bool strictMode)
        {
            if (string.IsNullOrEmpty(videoPath) || !System.IO.File.Exists(videoPath))
            {
                App.Logger?.Warning("VideoService: Specific video not found: {Path}", videoPath);
                return;
            }

            // Check if another fullscreen interaction is active
            // If so, queue this video for later
            if (App.InteractionQueue != null && !App.InteractionQueue.CanStart)
            {
                App.InteractionQueue.TryStart(
                    InteractionQueueService.InteractionType.Video,
                    () => PlaySpecificVideo(videoPath, strictMode),
                    queue: true);
                return;
            }

            // Notify queue we're starting
            App.InteractionQueue?.TryStart(
                InteractionQueueService.InteractionType.Video,
                () => { }, // Already executing
                queue: false);

            // Force close any stuck/existing video windows first
            if (_videoPlaying || _windows.Count > 0)
            {
                App.Logger?.Warning("VideoService: Forcing cleanup of existing video before playing specific video");
                ForceCleanup();
            }

            // Skip freeze if attention checks are enabled (user needs to click targets)
            if (!App.Settings.Current.AttentionChecksEnabled)
            {
                // Trigger Bambi Freeze subliminal+audio BEFORE video
                App.Subliminal?.TriggerBambiFreeze(deferReset: true);

                // Small delay to let the freeze effect register before video starts
                Task.Delay(800).ContinueWith(_ =>
                {
                    try
                    {
                        if (Application.Current?.Dispatcher == null) return;
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            PlayVideo(videoPath, strictMode);
                        });
                    }
                    catch (Exception ex)
                    {
                        App.Logger?.Warning("VideoService: Delayed specific video play failed - {Error}", ex.Message);
                    }
                });
            }
            else
            {
                // Attention checks enabled - play immediately without freeze
                PlayVideo(videoPath, strictMode);
            }
        }

        /// <summary>
        /// Force cleanup without scheduling next - used for panic key and preventing stacking
        /// </summary>
        public void ForceCleanup()
        {
            _safetyTimer?.Stop();
            _videoPlaying = false;
            _strictActive = false;
            CloseAll();
            App.Audio?.Unduck();
            _penalties = 0;
            App.Logger?.Information("VideoService: Force cleanup completed");
        }

        /// <summary>
        /// Play a video URL on all screens (used for browser fullscreen with dual monitor)
        /// No attention checks, no strict mode - just playback
        /// </summary>
        public void PlayUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return;
            if (_videoPlaying) return;

            EnsureLibVLCInitialized();

            if (_libVLC == null)
            {
                App.Logger?.Warning("Cannot play URL - LibVLC not available");
                return;
            }

            Application.Current?.Dispatcher.Invoke(() =>
            {
                _videoPlaying = true;
                _strictActive = false;

                var allScreens = App.GetAllScreensCached().ToList();
                if (allScreens.Count == 0) return;

                var primary = allScreens.FirstOrDefault(s => s.Primary) ?? allScreens[0];
                var secondaries = allScreens.Where(s => !s.Primary).ToList();

                // Create primary window with audio
                var primaryWin = CreateLibVLCUrlWindow(url, primary, withAudio: true);
                _windows.Add(primaryWin);

                // Create secondary windows (muted)
                if (App.Settings.Current.DualMonitorEnabled)
                {
                    foreach (var screen in secondaries)
                    {
                        var win = CreateLibVLCUrlWindow(url, screen, withAudio: false);
                        _windows.Add(win);
                    }
                }

                App.Logger?.Information("Playing URL via LibVLC on {Count} screen(s): {Url}", _windows.Count, url);
            });
        }

        private Window CreateLibVLCUrlWindow(string url, Screen screen, bool withAudio)
        {
            var win = new Window
            {
                WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false,
                ShowActivated = withAudio,
                Topmost = true,
                Background = Brushes.Black,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = screen.Bounds.X,
                Top = screen.Bounds.Y,
                Width = screen.Bounds.Width,
                Height = screen.Bounds.Height
            };

            var videoView = new VideoView
            {
                HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                Background = Brushes.Black
            };

            var mediaPlayer = new LibVLCSharp.Shared.MediaPlayer(_libVLC!);
            _mediaPlayers.Add(mediaPlayer);

            if (withAudio)
            {
                mediaPlayer.EndReached += (s, e) =>
                {
                    Application.Current?.Dispatcher.BeginInvoke(() =>
                    {
                        _videoPlaying = false;
                        CloseAll();
                    });
                };
            }

            win.Content = videoView;

            // Allow Escape to close
            win.KeyDown += (s, e) =>
            {
                if (e.Key == System.Windows.Input.Key.Escape)
                {
                    _videoPlaying = false;
                    CloseAll();
                }
            };

            win.Show();
            if (withAudio) win.Activate();

            videoView.MediaPlayer = mediaPlayer;

            // Create media from URL
            var media = new Media(_libVLC!, url, FromType.FromLocation);
            mediaPlayer.Play(media);

            if (withAudio)
            {
                mediaPlayer.Mute = false;
                mediaPlayer.Volume = GetEffectiveVolume();
            }
            else
            {
                mediaPlayer.Mute = true;
                mediaPlayer.Volume = 0;
            }

            return win;
        }

        private void ScheduleNext()
        {
            if (!_isRunning || !App.Settings.Current.MandatoryVideosEnabled) return;

            var perHour = Math.Max(1, App.Settings.Current.VideosPerHour);
            var secs = 3600.0 / perHour * (0.8 + _random.NextDouble() * 0.4);

            _scheduler?.Stop();
            _scheduler = new DispatcherTimer { Interval = TimeSpan.FromSeconds(Math.Max(60, secs)) };
            _scheduler.Tick += (s, e) => { _scheduler?.Stop(); if (_isRunning && !_videoPlaying) TriggerVideo(); ScheduleNext(); };
            _scheduler.Start();
        }

        private void PlayVideo(string path, bool strict)
        {
            _videoPlaying = true;
            _strictActive = strict;
            _retryPath = path;
            _startTime = DateTime.Now;
            _hits = _total = 0;
            _spawnTimes.Clear();

            // Fire pre-announcement event 1.3s before video starts
            VideoAboutToStart?.Invoke(this, EventArgs.Empty);

            // Stop flashes during video
            App.Flash?.Stop();

            // Duck other apps
            if (App.Settings.Current.AudioDuckingEnabled)
                App.Audio?.Duck(App.Settings.Current.DuckingLevel);

            // Delay video start by 1.3 seconds to allow avatar to announce
            var delayTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.3) };
            delayTimer.Tick += (s, e) =>
            {
                delayTimer.Stop();
                StartVideoPlayback(path, strict);
            };
            delayTimer.Start();
        }

        private void StartVideoPlayback(string path, bool strict)
        {
            // Safety check: ensure app is still running
            if (Application.Current == null) return;

            // Ensure LibVLC is initialized (deferred from startup for faster launch)
            EnsureLibVLCInitialized();

            Application.Current.Dispatcher.Invoke(() =>
            {
                var allScreens = App.GetAllScreensCached().ToList();
                if (allScreens.Count == 0)
                {
                    App.Logger?.Error("VideoService: No screens available - cannot play video");
                    OnEnded();
                    return;
                }
                var primary = allScreens.FirstOrDefault(s => s.Primary) ?? allScreens[0];
                var secondaries = allScreens.Where(s => !s.Primary).ToList();

                App.Logger?.Information("VideoService: Detected {Total} screens - Primary: {Primary}, Secondary: {SecCount} ({SecNames})",
                    allScreens.Count, primary.DeviceName, secondaries.Count,
                    string.Join(", ", secondaries.Select(s => s.DeviceName)));

                // Use LibVLC if available (codec-independent), otherwise fall back to MediaElement
                if (_libVLC != null)
                {
                    // Create primary screen with LibVLC VideoView (with audio)
                    var primaryWin = CreateLibVLCVideoWindow(path, primary, strict, withAudio: true);
                    _windows.Add(primaryWin);

                    // Create secondary screens with their own LibVLC players (muted)
                    if (App.Settings.Current.DualMonitorEnabled)
                    {
                        foreach (var scr in secondaries)
                        {
                            var win = CreateLibVLCVideoWindow(path, scr, strict, withAudio: false);
                            _windows.Add(win);
                        }
                    }
                }
                else
                {
                    // Fallback to MediaElement (requires Windows codecs)
                    var (primaryWin, primaryMedia) = CreateMediaElementVideoWindow(path, primary, strict);
                    _windows.Add(primaryWin);

                    if (App.Settings.Current.DualMonitorEnabled)
                    {
                        foreach (var scr in secondaries)
                        {
                            var win = CreateMirrorVideoWindow(primaryMedia, scr, strict);
                            _windows.Add(win);
                        }
                    }

                    primaryMedia.Play();
                }

                App.Logger?.Information("VideoService: Created {Count} video windows (DualMonitor={Enabled})",
                    _windows.Count, App.Settings.Current.DualMonitorEnabled);

                if (App.Settings.Current.AttentionChecksEnabled)
                    SetupAttention();
            });

            VideoStarted?.Invoke(this, EventArgs.Empty);
            _ = App.Haptics?.StartVideoBackgroundVibeAsync();
            App.Logger.Information("Playing: {File}", Path.GetFileName(path));
        }

        /// <summary>
        /// Creates a video window using LibVLC (codec-independent).
        /// Works on Windows N/KN editions without additional codecs.
        /// </summary>
        /// <param name="path">Video file path</param>
        /// <param name="screen">Target screen</param>
        /// <param name="strict">Whether strict mode is enabled</param>
        /// <param name="withAudio">Whether to play audio (primary monitor) or mute (secondary monitors)</param>
        private Window CreateLibVLCVideoWindow(string path, Screen screen, bool strict, bool withAudio)
        {
            var win = new Window
            {
                WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false,
                ShowActivated = withAudio, // Only activate primary
                Topmost = true,
                Background = Brushes.Black,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = screen.Bounds.X + 100,
                Top = screen.Bounds.Y + 100,
                Width = 400,
                Height = 300
            };

            // Create LibVLC VideoView
            var videoView = new VideoView
            {
                HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                Background = Brushes.Black
            };

            // Create media player for this video
            var mediaPlayer = new LibVLCSharp.Shared.MediaPlayer(_libVLC!);
            _mediaPlayers.Add(mediaPlayer);

            // Only the primary player handles events (to avoid duplicate triggers)
            if (withAudio)
            {
                mediaPlayer.LengthChanged += (s, e) =>
                {
                    _duration = e.Length / 1000.0; // Convert ms to seconds
                    Application.Current?.Dispatcher.BeginInvoke(() => StartSafetyTimer(_duration));
                };

                mediaPlayer.EndReached += (s, e) =>
                {
                    // Must be invoked on UI thread
                    Application.Current?.Dispatcher.BeginInvoke(OnEnded);
                };

                mediaPlayer.EncounteredError += (s, e) =>
                {
                    App.Logger?.Error("LibVLC playback error");
                    Application.Current?.Dispatcher.BeginInvoke(OnEnded);
                };
            }

            var grid = new Grid { Background = Brushes.Black };
            grid.Children.Add(videoView);

            // Add invisible click overlay - LibVLC uses Win32 child window that bypasses WPF events
            // This overlay catches all clicks before they reach the video surface
            var clickOverlay = new System.Windows.Shapes.Rectangle
            {
                Fill = Brushes.Transparent,
                IsHitTestVisible = true
            };
            clickOverlay.MouseDown += (s, e) =>
            {
                e.Handled = true;
                BringTargetsToFront();
            };
            grid.Children.Add(clickOverlay);

            win.Content = grid;

            SetupStrictHandlers(win, strict);

            // Also handle at window level for any clicks that get through
            win.PreviewMouseDown += (s, e) =>
            {
                // Don't let the video window activate - keeps targets on top
                e.Handled = true;
                BringTargetsToFront();
            };

            win.Show();
            win.WindowState = WindowState.Maximized;
            if (withAudio) win.Activate();

            // Attach media player to view and start playback
            videoView.MediaPlayer = mediaPlayer;

            // Create media - use file path directly for better compatibility
            var media = new Media(_libVLC!, path, FromType.FromPath);

            // Play the media
            mediaPlayer.Play(media);

            // Configure audio AFTER Play() - LibVLC sometimes ignores settings before playback
            if (withAudio)
            {
                // Ensure audio is not muted and volume is set using effective volume
                mediaPlayer.Mute = false;
                mediaPlayer.Volume = GetEffectiveVolume();
                mediaPlayer.SetAudioTrack(1); // Select first audio track
                App.Logger?.Information("LibVLC audio: Volume={Vol}, Mute={Mute}, AudioTrack={Track}",
                    mediaPlayer.Volume, mediaPlayer.Mute, mediaPlayer.AudioTrack);
            }
            else
            {
                // Mute secondary monitors
                mediaPlayer.Mute = true;
                mediaPlayer.Volume = 0;
            }

            // Don't dispose media - let LibVLC manage it
            // media.Dispose(); // Commented out - may cause audio issues

            App.Logger?.Debug("LibVLC video window on: {Screen} (audio: {Audio}, vol: {Vol}, mute: {Mute})",
                screen.DeviceName, withAudio, mediaPlayer.Volume, mediaPlayer.Mute);
            return win;
        }

        /// <summary>
        /// Creates the primary video window with MediaElement (fallback for when LibVLC fails).
        /// Requires Windows Media Foundation codecs.
        /// </summary>
        private (Window win, MediaElement media) CreateMediaElementVideoWindow(string path, Screen screen, bool strict)
        {
            var win = new Window
            {
                WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false,
                ShowActivated = true,
                Topmost = true,
                Background = Brushes.Black,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = screen.Bounds.X + 100,
                Top = screen.Bounds.Y + 100,
                Width = 400,
                Height = 300
            };

            var mediaElement = new MediaElement
            {
                LoadedBehavior = MediaState.Manual,
                UnloadedBehavior = MediaState.Manual,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Volume = GetEffectiveVolume() / 100.0
            };

            mediaElement.MediaOpened += (s, e) =>
            {
                if (mediaElement.NaturalDuration.HasTimeSpan)
                {
                    _duration = mediaElement.NaturalDuration.TimeSpan.TotalSeconds;
                    StartSafetyTimer(_duration);
                }
            };

            mediaElement.MediaEnded += (s, e) =>
                Application.Current.Dispatcher.BeginInvoke(OnEnded);

            mediaElement.MediaFailed += (s, e) =>
            {
                var errorMsg = e.ErrorException?.Message ?? "Unknown error";
                App.Logger.Error("Media failed: {Error}", errorMsg);

                // Check for Windows Media Player / codec issues
                if (errorMsg.Contains("Windows Media Player") ||
                    errorMsg.Contains("MF_E_") ||
                    errorMsg.Contains("0xC00D") ||
                    errorMsg.Contains("codec", StringComparison.OrdinalIgnoreCase))
                {
                    // Show one-time warning about missing codecs
                    if (!_codecWarningShown)
                    {
                        _codecWarningShown = true;
                        Application.Current.Dispatcher.BeginInvoke(() =>
                        {
                            System.Windows.MessageBox.Show(
                                "Video playback requires Windows Media components.\n\n" +
                                "If you're on Windows N/KN edition, please install the Media Feature Pack:\n\n" +
                                "1. Open Settings > Apps > Optional features\n" +
                                "2. Click 'Add a feature'\n" +
                                "3. Search for 'Media Feature Pack'\n" +
                                "4. Install and restart your PC\n\n" +
                                "Alternatively, install K-Lite Codec Pack from codecguide.com",
                                "Video Codec Required",
                                MessageBoxButton.OK,
                                MessageBoxImage.Warning);
                        });
                    }
                }

                Application.Current.Dispatcher.BeginInvoke(OnEnded);
            };

            var grid = new Grid { Background = Brushes.Black };
            grid.Children.Add(mediaElement);
            win.Content = grid;

            SetupStrictHandlers(win, strict);

            // Prevent video window from stealing focus when clicked (keeps attention targets visible)
            win.PreviewMouseDown += (s, e) =>
            {
                e.Handled = true;
                BringTargetsToFront();
            };

            win.Show();
            win.WindowState = WindowState.Maximized;
            win.Activate();

            // Load source
            mediaElement.Source = new Uri(path);

            App.Logger.Debug("MediaElement video window on: {Screen}", screen.DeviceName);
            return (win, mediaElement);
        }

        /// <summary>
        /// Creates a mirror window that displays the same video using VisualBrush.
        /// This avoids the decoder creating a separate decode stream.
        /// </summary>
        private Window CreateMirrorVideoWindow(MediaElement sourceMedia, Screen screen, bool strict)
        {
            var win = new Window
            {
                WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false,
                ShowActivated = false,
                Topmost = true,
                Background = Brushes.Black,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = screen.Bounds.X + 100,
                Top = screen.Bounds.Y + 100,
                Width = 400,
                Height = 300
            };

            // Use VisualBrush to mirror the primary MediaElement
            var visualBrush = new VisualBrush
            {
                Visual = sourceMedia,
                Stretch = Stretch.Uniform,
                AlignmentX = AlignmentX.Center,
                AlignmentY = AlignmentY.Center
            };

            var rectangle = new System.Windows.Shapes.Rectangle
            {
                Fill = visualBrush,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };

            var grid = new Grid { Background = Brushes.Black };
            grid.Children.Add(rectangle);
            win.Content = grid;

            SetupStrictHandlers(win, strict);

            // Prevent video window from stealing focus when clicked (keeps attention targets visible)
            win.PreviewMouseDown += (s, e) =>
            {
                e.Handled = true;
                BringTargetsToFront();
            };

            win.Show();
            win.WindowState = WindowState.Maximized;

            App.Logger.Debug("Mirror video window on: {Screen}", screen.DeviceName);
            return win;
        }

        /// <summary>
        /// Creates a fullscreen video window on the specified screen.
        /// Kept for backward compatibility.
        /// </summary>
        private Window CreateFullscreenVideoWindow(string path, Screen screen, bool strict, bool withAudio)
        {
            var (win, media) = CreateMediaElementVideoWindow(path, screen, strict);
            if (withAudio)
            {
                media.Volume = GetEffectiveVolume() / 100.0;
                media.IsMuted = false;
            }
            else
            {
                media.Volume = 0;
                media.IsMuted = true;
            }
            media.Play();
            return win;
        }

        private void SetupStrictHandlers(Window win, bool strict)
        {
            if (strict)
            {
                win.Closing += (s, e) => { if (_videoPlaying) e.Cancel = true; };
                win.PreviewKeyDown += (s, e) =>
                {
                    if (e.Key == Key.Escape || e.Key == Key.System ||
                        (e.Key == Key.F4 && Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)))
                        e.Handled = true;
                };
                // Don't reactivate if attention targets are active - they need focus for clicks
                win.Deactivated += (s, e) =>
                {
                    if (_videoPlaying && _strictActive && !App.Settings.Current.AttentionChecksEnabled)
                    {
                        win.Activate();
                        win.Focus();
                    }
                };
            }
            else
            {
                win.KeyDown += (s, e) =>
                {
                    if (e.Key == Key.Escape && App.Settings.Current.PanicKeyEnabled)
                    {
                        try
                        {
                            Cleanup();
                        }
                        catch (Exception ex)
                        {
                            App.Logger?.Error(ex, "Error during video cleanup on escape");
                        }
                    }
                };
            }
        }

        #region Attention Checks

        private void SetupAttention()
        {
            Task.Delay(2000).ContinueWith(_ =>
            {
                try
                {
                    if (Application.Current?.Dispatcher == null) return;
                    Application.Current.Dispatcher.BeginInvoke(() =>
                    {
                        if (!_videoPlaying) return;

                        _spawned = 0; // Reset spawned counter
                        var dur = _duration > 0 ? _duration : 60;
                        // Use setting directly as total count (not density)
                        _total = Math.Max(1, App.Settings.Current.AttentionDensity);

                        for (int i = 0; i < _total; i++)
                            _spawnTimes.Add(3 + _random.NextDouble() * Math.Max(1, dur - 8)); // Stop spawning ~5s before end
                        _spawnTimes.Sort();

                        _attentionTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
                        _attentionTimer.Tick += CheckSpawnTargets;
                        _attentionTimer.Start();

                        App.Logger.Information("Attention: {Count} targets over {Duration}s", _total, (int)dur);
                    });
                }
                catch (Exception ex)
                {
                    App.Logger?.Warning("SetupAttention failed: {Error}", ex.Message);
                }
            });
        }

        private void CheckSpawnTargets(object? s, EventArgs e)
        {
            if (!_videoPlaying) return;
            var elapsed = (DateTime.Now - _startTime).TotalSeconds;
            while (_spawnTimes.Count > 0 && elapsed >= _spawnTimes[0])
            {
                _spawnTimes.RemoveAt(0);
                SpawnTarget();
            }
        }

        private void SpawnTarget()
        {
            try
            {
                var settings = App.Settings.Current;
                var pool = settings.AttentionPool.Where(p => p.Value).Select(p => p.Key).ToList();
                var text = pool.Count > 0 ? pool[_random.Next(pool.Count)] : "CLICK ME";

                var screens = settings.DualMonitorEnabled ? App.GetAllScreensCached() : new[] { Screen.PrimaryScreen };
                // Safety check: ensure we have at least one screen
                if (screens == null || screens.Length == 0 || screens[0] == null)
                {
                    App.Logger?.Warning("SpawnTarget: No screens available");
                    return;
                }
                var screen = screens[_random.Next(screens.Length)];

                _spawned++; // Track actually spawned targets
                App.Logger?.Debug("Spawning attention target: '{Text}' on screen {Screen} ({Spawned}/{Total})", text, screen.DeviceName, _spawned, _total);

                FloatingText? target = null;
                target = new FloatingText(text, screen, settings.AttentionSize, () =>
                {
                    _ = App.Haptics?.VideoTargetHitAsync();
                    _hits++;
                    App.Progression?.AddXP(10);

                    // Remove from targets list immediately on click
                    List<FloatingText> remainingTargets;
                    lock (_targets)
                    {
                        if (target != null) _targets.Remove(target);
                        remainingTargets = _targets.ToList();
                    }

                    App.Logger?.Information("ATTENTION: Hit {Hits}/{Spawned}, {Remaining} targets remaining", _hits, _spawned, remainingTargets.Count);

                    // Bring remaining targets to front AFTER the clicked target fully closes
                    // FadeOut takes ~200ms, so wait 300ms to ensure window is closed
                    if (remainingTargets.Count > 0)
                    {
                        Task.Delay(300).ContinueWith(_ =>
                        {
                            try
                            {
                                Application.Current?.Dispatcher.BeginInvoke(() =>
                                {
                                    foreach (var t in remainingTargets)
                                    {
                                        t.BringToFront();
                                    }
                                });
                            }
                            catch (Exception ex)
                            {
                                App.Logger?.Debug("Failed to bring targets to front after hit: {Error}", ex.Message);
                            }
                        });
                    }
                });

                lock (_targets)
                {
                    _targets.Add(target);
                    App.Logger?.Information("ATTENTION: Target added to list, total now: {Count}", _targets.Count);
                }

                // Auto-expire with safety check
                var lifespan = settings.AttentionLifespan * 1000;
                Task.Delay(lifespan).ContinueWith(_ =>
                {
                    try
                    {
                        if (Application.Current?.Dispatcher == null) return;
                        Application.Current.Dispatcher.BeginInvoke(() =>
                        {
                            try
                            {
                                lock (_targets)
                                {
                                    if (_targets.Contains(target))
                                    {
                                        _targets.Remove(target);
                                        target.Destroy();
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                App.Logger?.Warning("Error expiring target: {Error}", ex.Message);
                            }
                        });
                    }
                    catch (Exception ex)
                    {
                        App.Logger?.Debug("Target auto-expire task failed (app may be shutting down): {Error}", ex.Message);
                    }
                });
            }
            catch (Exception ex)
            {
                App.Logger?.Error("Failed to spawn attention target: {Error}", ex.Message);
            }
        }

        /// <summary>
        /// Brings all attention targets back to front when video is clicked
        /// </summary>
        private void BringTargetsToFront()
        {
            // Delay slightly to ensure targets come to front AFTER video window activation
            Task.Delay(50).ContinueWith(_ =>
            {
                try
                {
                    Application.Current?.Dispatcher.BeginInvoke(() =>
                    {
                        lock (_targets)
                        {
                            foreach (var t in _targets)
                            {
                                t.BringToFront();
                            }
                        }
                    });
                }
                catch (Exception ex)
                {
                    App.Logger?.Debug("BringTargetsToFront task failed (app may be shutting down): {Error}", ex.Message);
                }
            });
        }

        #endregion

        #region Video End / Penalty / Mercy

        private void OnEnded()
        {
            if (!_videoPlaying) return;

            var settings = App.Settings.Current;
            bool loop = false, troll = false;

            if (settings.AttentionChecksEnabled && _spawned > 0)
            {
                bool passed = _hits >= _spawned;
                App.Logger.Information("Attention result: {Hits}/{Spawned} (of {Total} scheduled) = {Result}", _hits, _spawned, _total, passed ? "PASS" : "FAIL");

                if (passed)
                {
                    var xpForPlays = (_penalties + 1) * 50;
                    var bonus = 200;
                    App.Progression?.AddXP(xpForPlays + bonus);

                    if (_random.NextDouble() < 0.1)
                    {
                        loop = troll = true;
                    }
                }
                else
                {
                    loop = true;
                    // Track attention check failure for "Mercy Beggar" achievement
                    App.Achievements?.TrackAttentionCheckFailed();
                }
            }

            if (loop && !string.IsNullOrEmpty(_retryPath))
            {
                _penalties++;
                if (_penalties >= 3 && settings.MercySystemEnabled)
                    ShowMessage("BAMBI GETS MERCY", 2500, Cleanup);
                else
                    ShowMessage(troll ? "GOOD GIRL!\nWATCH AGAIN 😜" : "DUMB BAMBI!\nTRY AGAIN", 2000, () =>
                    {
                        // ShowMessage already set _videoPlaying = false and called CloseAll()
                        // Reset attention tracking for retry
                        _hits = 0;
                        _spawnTimes.Clear();
                        PlayVideo(_retryPath!, _strictActive);
                    });
                return;
            }

            // Track video watch time for leaderboard
            // Must be done here in OnEnded, not in Cleanup, because Cleanup is also called during shutdown
            if (_duration > 0)
            {
                App.Logger?.Information("Video ended with duration: {Duration}s, tracking watch time", _duration);
                App.Achievements?.TrackVideoWatched(_duration);
            }

            Cleanup();
        }

        private void ShowMessage(string text, int ms, Action then)
        {
            // CRITICAL: Set _videoPlaying to false BEFORE CloseAll() so strict mode
            // handlers don't cancel window closing (they check _videoPlaying in Closing event)
            _videoPlaying = false;
            CloseAll();

            var screens = App.Settings.Current.DualMonitorEnabled ? App.GetAllScreensCached() : new[] { Screen.PrimaryScreen };
            // Safety check: ensure we have at least one screen
            if (screens == null || screens.Length == 0 || screens[0] == null)
            {
                App.Logger?.Warning("ShowMessage: No screens available, executing callback immediately");
                then();
                return;
            }

            foreach (var screen in screens)
            {
                var win = new Window
                {
                    WindowStyle = WindowStyle.None,
                    Background = Brushes.Black,
                    Topmost = true,
                    ShowInTaskbar = false,
                    WindowStartupLocation = WindowStartupLocation.Manual,
                    Left = screen.Bounds.X + 100,
                    Top = screen.Bounds.Y + 100,
                    Width = 400,
                    Height = 300,
                    Content = new TextBlock
                    {
                        Text = text,
                        Foreground = Brushes.Magenta,
                        FontSize = 64,
                        FontWeight = FontWeights.Bold,
                        FontFamily = new FontFamily("Impact"),
                        TextAlignment = TextAlignment.Center,
                        HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    }
                };
                win.Show();
                win.WindowState = WindowState.Maximized;
                _messageWindows.Add(win);  // Track for cleanup
            }

            Task.Delay(ms).ContinueWith(_ =>
            {
                try
                {
                    if (Application.Current?.Dispatcher == null) return;
                    Application.Current.Dispatcher.BeginInvoke(() =>
                    {
                        CloseMessageWindows();
                        then();
                    });
                }
                catch (Exception ex)
                {
                    App.Logger?.Warning("ShowMessage callback failed: {Error}", ex.Message);
                }
            });
        }

        private void CloseMessageWindows()
        {
            foreach (var w in _messageWindows.ToList())
            {
                try { w.Close(); }
                catch (Exception ex)
                {
                    App.Logger?.Debug("Failed to close message window: {Error}", ex.Message);
                }
            }
            _messageWindows.Clear();
        }

        #endregion

        #region Safety Timeout

        /// <summary>
        /// Starts a safety timer to force cleanup if MediaEnded never fires.
        /// This prevents the video window from getting stuck on fullscreen.
        /// </summary>
        private void StartSafetyTimer(double videoDurationSeconds)
        {
            _safetyTimer?.Stop();

            // Add 5 second buffer beyond video duration
            var timeoutSeconds = videoDurationSeconds + 5;

            _safetyTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(timeoutSeconds) };
            _safetyTimer.Tick += (s, e) =>
            {
                _safetyTimer?.Stop();
                if (_videoPlaying)
                {
                    App.Logger?.Warning("VideoService: Safety timeout triggered - MediaEnded did not fire. Forcing cleanup.");
                    Cleanup();
                }
            };
            _safetyTimer.Start();

            App.Logger?.Debug("VideoService: Safety timer started for {Duration}s", timeoutSeconds);
        }

        #endregion

        #region Cleanup

        private void CloseAll()
        {
            _attentionTimer?.Stop();

            lock (_targets)
            {
                App.Logger?.Information("ATTENTION: CloseAll() called - destroying {Count} targets", _targets.Count);
                foreach (var t in _targets.ToList()) t.Destroy();
                _targets.Clear();
            }

            App.Logger?.Debug("CloseAll: Closing {Count} video windows, {MsgCount} message windows",
                _windows.Count, _messageWindows.Count);

            // First, detach MediaPlayers from VideoViews to prevent crashes
            var windowsCopy = _windows.ToList();
            foreach (var w in windowsCopy)
            {
                try
                {
                    if (w.Content is Grid g && g.Children.Count > 0 && g.Children[0] is VideoView vv)
                    {
                        var mp = vv.MediaPlayer;
                        vv.MediaPlayer = null; // Detach before dispose
                    }
                }
                catch (Exception ex)
                {
                    App.Logger?.Debug("CloseAll: Error detaching VideoView - {Error}", ex.Message);
                }
            }

            // Now stop and dispose all LibVLC media players (on a background thread to avoid blocking)
            var playersCopy = _mediaPlayers.ToList();
            _mediaPlayers.Clear();

            Task.Run(() =>
            {
                foreach (var player in playersCopy)
                {
                    try
                    {
                        player.Stop();
                        player.Dispose();
                    }
                    catch (Exception ex)
                    {
                        App.Logger?.Warning("CloseAll: Failed to stop LibVLC player - {Error}", ex.Message);
                    }
                }
            });

            // Close video windows
            foreach (var w in _windows.ToList())
            {
                try
                {
                    // Stop any MediaElement
                    if (w.Content is Grid g && g.Children.Count > 0 && g.Children[0] is MediaElement me)
                    {
                        me.Stop();
                        me.Source = null; // Release media resources
                    }
                    w.Close();
                }
                catch (Exception ex)
                {
                    App.Logger?.Warning("CloseAll: Failed to close video window - {Error}", ex.Message);
                }
            }
            _windows.Clear();

            // Also close any lingering message windows
            CloseMessageWindows();
        }

        private void Cleanup()
        {
            _safetyTimer?.Stop();
            _videoPlaying = false;
            CloseAll();
            App.Audio?.Unduck();
            _strictActive = false;
            _penalties = 0;

            // Trigger deferred Bambi Reset now that video has ended
            App.Subliminal?.TriggerDeferredBambiReset();

            // Stop haptic background vibe
            _ = App.Haptics?.StopVideoBackgroundVibeAsync();

            // Notify InteractionQueue that video is complete (triggers queued items)
            App.InteractionQueue?.Complete(InteractionQueueService.InteractionType.Video);

            VideoEnded?.Invoke(this, EventArgs.Empty);

            if (_isRunning && App.Settings.Current.FlashEnabled)
                App.Flash?.Start();
            if (_isRunning)
                ScheduleNext();
        }

        #endregion

        private string? GetNextVideo()
        {
            if (_videoQueue.Count == 0)
            {
                var validExtensions = new[] { ".mp4", ".mov", ".avi", ".wmv", ".mkv", ".webm" };

                var files = new List<string>();
                if (Directory.Exists(_videosPath))
                {
                    // Scan subfolders to support user-organized categories
                    foreach (var file in Directory.GetFiles(_videosPath, "*.*", SearchOption.AllDirectories))
                    {
                        var ext = Path.GetExtension(file).ToLowerInvariant();
                        if (!validExtensions.Contains(ext)) continue;

                        // Security: Validate path is within allowed directories (app dir, user assets, or custom path)
                        var isInAppDir = SecurityHelper.IsPathSafe(file, AppDomain.CurrentDomain.BaseDirectory);
                        var isInUserAssets = SecurityHelper.IsPathSafe(file, App.UserDataPath);
                        var isInCustomPath = SecurityHelper.IsPathSafe(file, App.EffectiveAssetsPath);

                        if (!isInAppDir && !isInUserAssets && !isInCustomPath)
                        {
                            App.Logger?.Warning("Blocked video outside allowed directory: {Path}", file);
                            continue;
                        }

                        // Security: Sanitize filename
                        var fileName = SecurityHelper.SanitizeFilename(Path.GetFileName(file));
                        if (string.IsNullOrEmpty(fileName)) continue;

                        files.Add(file);
                    }
                }

                // Filter out disabled assets (blacklist approach)
                if (App.Settings?.Current?.DisabledAssetPaths.Count > 0)
                {
                    var basePath = App.EffectiveAssetsPath;
                    files = files.Where(f =>
                    {
                        var relativePath = Path.GetRelativePath(basePath, f);
                        return !App.Settings.Current.DisabledAssetPaths.Contains(relativePath);
                    }).ToList();
                }

                if (files.Count == 0) return null;

                // Performance: Shuffle and enqueue all at once
                _videoQueue = new Queue<string>(files.OrderBy(_ => _random.Next()));
            }

            return _videoQueue.Count > 0 ? _videoQueue.Dequeue() : null;  // Performance: O(1) instead of O(n)
        }

        public void Dispose() => Stop();
    }

    /// <summary>
    /// Bouncing text target - customizable via settings
    /// </summary>
    internal class FloatingText
    {
        // Win32 for reliable z-order management and tool window style
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_SHOWWINDOW = 0x0040;

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_APPWINDOW = 0x00040000;

        private readonly Window _win;
        private readonly DispatcherTimer _timer;
        private double _x, _y, _vx, _vy;
        private readonly double _minX, _maxX, _minY, _maxY;
        private bool _dead;
        private IntPtr _hwnd;
        private int _tickCount;  // For periodic z-order refresh

        public FloatingText(string text, Screen screen, int size, Action onHit)
        {
            try
            {
                size = Math.Max(40, size);

                // Format multi-word triggers: 2 words = 2 lines, 4+ words = 2 lines with 2 on each
                text = FormatTriggerText(text);

                // Get DPI scale factor (Screen uses physical pixels, WPF uses DIPs)
                double dpiScale = 1.0;
                try
                {
                    var source = PresentationSource.FromVisual(Application.Current.MainWindow);
                    if (source?.CompositionTarget != null)
                    {
                        dpiScale = source.CompositionTarget.TransformToDevice.M11;
                    }
                }
                catch (Exception ex)
                {
                    App.Logger?.Debug("Could not get DPI scale for attention target: {Error}", ex.Message);
                }

                // Use WorkingArea (excludes taskbar) with generous margins
                // Convert physical pixels to WPF DIPs
                var area = screen.WorkingArea;
                double areaX = area.X / dpiScale;
                double areaY = area.Y / dpiScale;
                double areaWidth = area.Width / dpiScale;
                double areaHeight = area.Height / dpiScale;

                // Margins scale with screen size to handle different resolutions
                var marginX = Math.Min(150, areaWidth * 0.08);
                var marginY = Math.Min(100, areaHeight * 0.08);
                _minX = areaX + marginX;
                _minY = areaY + marginY;
                _maxX = areaX + areaWidth - marginX;
                _maxY = areaY + areaHeight - marginY;

                // Load style settings
                var settings = App.Settings.Current;
                Color color1, color2, textColor, borderColor;
                try
                {
                    color1 = (Color)ColorConverter.ConvertFromString(settings.AttentionColor1);
                    color2 = (Color)ColorConverter.ConvertFromString(settings.AttentionColor2);
                    textColor = (Color)ColorConverter.ConvertFromString(settings.AttentionTextColor);
                    borderColor = (Color)ColorConverter.ConvertFromString(settings.AttentionBorderColor);
                }
                catch
                {
                    // Fallback to bright fluo pink if colors invalid
                    color1 = Color.FromRgb(255, 20, 147); // DeepPink
                    color2 = Color.FromRgb(255, 105, 180); // HotPink
                    textColor = Color.FromRgb(255, 20, 147); // DeepPink
                    borderColor = Color.FromRgb(255, 20, 147);
                }

                // Check if floating text mode (no background)
                var isFloating = settings.AttentionFloatingText;

                // Create container with customizable styling
                var border = new Border
                {
                    Background = isFloating
                        ? Brushes.Transparent
                        : new LinearGradientBrush(color1, color2, 90),
                    CornerRadius = isFloating ? new CornerRadius(0) : new CornerRadius(20),
                    BorderBrush = (settings.AttentionShowBorder && !isFloating)
                        ? new SolidColorBrush(borderColor)
                        : Brushes.Transparent,
                    BorderThickness = (settings.AttentionShowBorder && !isFloating)
                        ? new Thickness(3)
                        : new Thickness(0),
                    Padding = isFloating ? new Thickness(0) : new Thickness(20, 10, 20, 10),
                    Effect = isFloating ? null : new System.Windows.Media.Effects.DropShadowEffect
                    {
                        Color = Colors.Black,
                        BlurRadius = 15,
                        ShadowDepth = 5,
                        Opacity = 0.6
                    },
                    Cursor = System.Windows.Input.Cursors.Hand
                };

                // Create outlined text using geometry for crisp 2mm black outline
                var fontFamily = new FontFamily($"{settings.AttentionFont}, Segoe UI, Arial");
                var typeface = new Typeface(fontFamily, FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);

                // Create FormattedText to generate geometry
                var formattedText = new FormattedText(
                    text,
                    System.Globalization.CultureInfo.CurrentCulture,
                    System.Windows.FlowDirection.LeftToRight,
                    typeface,
                    size,
                    Brushes.White, // Placeholder, we'll use geometry
                    VisualTreeHelper.GetDpi(Application.Current.MainWindow).PixelsPerDip);
                formattedText.TextAlignment = TextAlignment.Center;
                formattedText.LineHeight = size * 0.95;

                // Get text geometry for outline
                var textGeometry = formattedText.BuildGeometry(new System.Windows.Point(0, 0));

                // 2mm ≈ 7.5 pixels at 96 DPI
                const double outlineThickness = 7.5;

                // Get the actual bounds of the geometry and offset to ensure nothing is clipped
                var bounds = textGeometry.Bounds;
                double offsetX = -bounds.X + outlineThickness;
                double offsetY = -bounds.Y + outlineThickness;

                // Apply transform to offset the geometry so it starts within the container
                var transformedGeometry = textGeometry.Clone();
                transformedGeometry.Transform = new TranslateTransform(offsetX, offsetY);

                // Create path for outline (black stroke)
                var outlinePath = new System.Windows.Shapes.Path
                {
                    Data = transformedGeometry,
                    Stroke = Brushes.Black,
                    StrokeThickness = outlineThickness,
                    StrokeLineJoin = PenLineJoin.Round,
                    Fill = Brushes.Transparent
                };

                // Create path for fill (text color)
                var fillPath = new System.Windows.Shapes.Path
                {
                    Data = transformedGeometry,
                    Fill = new SolidColorBrush(textColor),
                    Stroke = Brushes.Transparent
                };

                // Stack outline behind fill in a Grid
                var textContainer = new Grid
                {
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                textContainer.Children.Add(outlinePath);
                textContainer.Children.Add(fillPath);

                border.Child = textContainer;

                // Measure the text to get proper sizing (use actual geometry bounds + outline thickness)
                double w = bounds.Width + outlineThickness * 2 + 60;  // Add padding + outline
                double h = bounds.Height + outlineThickness * 2 + 40;

                // Ensure minimum size
                w = Math.Max(w, 150);
                h = Math.Max(h, 60);

                // Create a container grid with an invisible hit zone
                // This ensures clicks register even on transparent pixels (inside "O", etc.)
                var container = new Grid();

                // Invisible hit zone rectangle - nearly transparent but still hit-testable
                var hitZone = new System.Windows.Shapes.Rectangle
                {
                    Fill = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)), // Almost invisible but hit-testable
                    IsHitTestVisible = true
                };
                container.Children.Add(hitZone);
                container.Children.Add(border);

                _win = new Window
                {
                    WindowStyle = WindowStyle.None,
                    AllowsTransparency = true,
                    Background = Brushes.Transparent,
                    Topmost = true,
                    ShowInTaskbar = false,
                    Width = w,
                    Height = h,
                    Content = container,
                    ShowActivated = false  // Don't steal focus
                };

                // Random position - ensure window stays fully within bounds
                var rnd = new Random();
                // Calculate spawn range: from minX to (maxX - windowWidth)
                var spawnRangeX = Math.Max(0, (_maxX - w) - _minX);
                var spawnRangeY = Math.Max(0, (_maxY - h) - _minY);
                _x = _minX + rnd.NextDouble() * spawnRangeX;
                _y = _minY + rnd.NextDouble() * spawnRangeY;
                // Clamp to ensure we're definitely within bounds
                _x = Math.Clamp(_x, _minX, Math.Max(_minX, _maxX - w));
                _y = Math.Clamp(_y, _minY, Math.Max(_minY, _maxY - h));
                _win.Left = _x;
                _win.Top = _y;

                // Random velocity (slightly faster for better visibility)
                var angle = rnd.NextDouble() * Math.PI * 2;
                _vx = Math.Cos(angle) * 3.0;
                _vy = Math.Sin(angle) * 3.0;

                // Click = hit
                bool clicked = false;
                _win.MouseLeftButtonDown += (s, e) =>
                {
                    e.Handled = true;  // Prevent click from propagating to windows behind
                    if (clicked) return;
                    clicked = true;
                    App.Logger?.Information("ATTENTION: Target clicked");
                    PlayPopSound();
                    onHit();
                    FadeOut();
                };

                // Movement
                _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
                _timer.Tick += (s, e) =>
                {
                    if (_dead) return;
                    _x += _vx; _y += _vy;
                    if (_x < _minX) { _x = _minX; _vx = Math.Abs(_vx); }
                    if (_x + w > _maxX) { _x = _maxX - w; _vx = -Math.Abs(_vx); }
                    if (_y < _minY) { _y = _minY; _vy = Math.Abs(_vy); }
                    if (_y + h > _maxY) { _y = _maxY - h; _vy = -Math.Abs(_vy); }
                    _win.Left = _x;
                    _win.Top = _y;

                    // Periodically re-assert topmost z-order (every ~32ms = 2 ticks at 16ms)
                    // This ensures targets stay on top of subliminals and fullscreen video windows
                    _tickCount++;
                    if (_tickCount >= 2 && _hwnd != IntPtr.Zero)
                    {
                        _tickCount = 0;
                        SetWindowPos(_hwnd, HWND_TOPMOST, 0, 0, 0, 0,
                            SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
                    }
                };

                // Set tool window style BEFORE window is shown (SourceInitialized fires after HWND created but before visible)
                _win.SourceInitialized += (s, e) =>
                {
                    _hwnd = new WindowInteropHelper(_win).Handle;
                    if (_hwnd != IntPtr.Zero)
                    {
                        // Set as tool window to hide from Alt+Tab - must be done before window is visible
                        int exStyle = GetWindowLong(_hwnd, GWL_EXSTYLE);
                        exStyle |= WS_EX_TOOLWINDOW;  // Add tool window style
                        exStyle &= ~WS_EX_APPWINDOW;  // Remove app window style
                        SetWindowLong(_hwnd, GWL_EXSTYLE, exStyle);
                        App.Logger?.Debug("Attention target: Set WS_EX_TOOLWINDOW style on hwnd={Hwnd}", _hwnd);
                    }
                };

                _win.Loaded += (s, e) =>
                {
                    _timer.Start();
                    // Ensure hwnd is captured if not already
                    if (_hwnd == IntPtr.Zero)
                        _hwnd = new WindowInteropHelper(_win).Handle;

                    if (_hwnd != IntPtr.Zero)
                    {
                        // Ensure topmost via Win32
                        SetWindowPos(_hwnd, HWND_TOPMOST, 0, 0, 0, 0,
                            SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
                    }
                    App.Logger?.Debug("Attention target window loaded at ({X}, {Y}), hwnd={Hwnd}", _x, _y, _hwnd);
                };

                // Track when window is closing (to debug unexpected closes)
                _win.Closing += (s, e) =>
                {
                    App.Logger?.Information("ATTENTION: Target window Closing event, _dead={Dead}", _dead);
                };

                // Prevent activation from stealing focus from other targets
                _win.Activated += (s, e) =>
                {
                    // Immediately bring all other topmost windows back to front
                    // This is handled by the VideoService's BringTargetsToFront
                };

                _win.Show();
                App.Logger?.Debug("Attention target window created: '{Text}' size {W}x{H}", text, w, h);
            }
            catch (Exception ex)
            {
                App.Logger?.Error("Failed to create FloatingText window: {Error}", ex.Message);
                _timer = new DispatcherTimer(); // Prevent null reference
                _win = new Window { Visibility = Visibility.Collapsed }; // Dummy window
            }
        }

        private void PlayPopSound()
        {
            try
            {
                var soundsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "sounds", "bubbles");
                var popFiles = new[] { "Pop.mp3", "Pop2.mp3", "Pop3.mp3" };
                var rnd = new Random();
                var chosenPop = popFiles[rnd.Next(popFiles.Length)];
                var popPath = Path.Combine(soundsPath, chosenPop);

                if (File.Exists(popPath))
                {
                    Task.Run(() =>
                    {
                        try
                        {
                            using var audioFile = new AudioFileReader(popPath);
                            // Apply master volume to attention target pop sound
                            var masterVolume = App.Settings?.Current?.MasterVolume ?? 100;
                            audioFile.Volume = 0.6f * (masterVolume / 100f);
                            using var outputDevice = new WaveOutEvent();
                            outputDevice.Init(audioFile);
                            outputDevice.Play();
                            while (outputDevice.PlaybackState == PlaybackState.Playing)
                            {
                                Thread.Sleep(50);
                            }
                        }
                        catch (Exception ex)
                        {
                            App.Logger?.Debug("Pop sound playback failed: {Error}", ex.Message);
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("Failed to start pop sound: {Error}", ex.Message);
            }
        }

        private void FadeOut()
        {
            App.Logger?.Debug("FloatingText.FadeOut() starting");
            _timer.Stop();
            var fade = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
            fade.Tick += (s, e) =>
            {
                _win.Opacity -= 0.15;
                if (_win.Opacity <= 0.1) { fade.Stop(); Destroy(); }
            };
            fade.Start();
        }

        public void Destroy()
        {
            if (_dead) return;  // Already destroyed
            _dead = true;
            _timer.Stop();
            App.Logger?.Information("ATTENTION: Target destroyed (window closing)");
            try { _win.Close(); }
            catch (Exception ex)
            {
                App.Logger?.Debug("Failed to close target window: {Error}", ex.Message);
            }
        }

        public void BringToFront()
        {
            if (_dead)
            {
                App.Logger?.Information("ATTENTION: BringToFront skipped - target is dead");
                return;
            }
            if (_hwnd == IntPtr.Zero)
            {
                App.Logger?.Information("ATTENTION: BringToFront skipped - hwnd is zero");
                return;
            }
            try
            {
                // Use Win32 SetWindowPos for reliable z-order without focus stealing
                bool result = SetWindowPos(_hwnd, HWND_TOPMOST, 0, 0, 0, 0,
                    SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
                App.Logger?.Information("ATTENTION: BringToFront hwnd={Hwnd}, success={Result}, visible={Visible}",
                    _hwnd, result, _win.IsVisible);
            }
            catch (Exception ex)
            {
                App.Logger?.Error("ATTENTION: BringToFront failed: {Error}", ex.Message);
            }
        }

        /// <summary>
        /// Formats trigger text for display:
        /// - 2 words: stack vertically (one per line)
        /// - 4+ words: 2 lines with words split evenly
        /// - 1 word or 3 words: keep as-is
        /// </summary>
        private static string FormatTriggerText(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;

            var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            if (words.Length == 2)
            {
                // 2 words: stack vertically
                return $"{words[0]}\n{words[1]}";
            }
            else if (words.Length >= 4)
            {
                // 4+ words: split into 2 lines
                int mid = words.Length / 2;
                var line1 = string.Join(" ", words.Take(mid));
                var line2 = string.Join(" ", words.Skip(mid));
                return $"{line1}\n{line2}";
            }

            // 1 or 3 words: keep as-is
            return text;
        }
    }
}
