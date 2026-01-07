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
using System.Windows.Threading;
using System.Windows.Forms;
using NAudio.Wave;
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

        private readonly string _videosPath;

        public event EventHandler? VideoAboutToStart; // Fires 1.3s before video
        public event EventHandler? VideoStarted;
        public event EventHandler? VideoEnded;

        public VideoService()
        {
            _videosPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "startle_videos");
            Directory.CreateDirectory(_videosPath);
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
            // Force close any stuck/existing video windows first
            if (_videoPlaying || _windows.Count > 0)
            {
                App.Logger?.Warning("VideoService: Forcing cleanup of existing video before triggering new one");
                ForceCleanup();
            }
            
            var path = GetNextVideo();
            if (string.IsNullOrEmpty(path))
            {
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
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        PlayVideo(path, App.Settings.Current.StrictLockEnabled);
                    });
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
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        PlayVideo(videoPath, strictMode);
                    });
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
            App.Audio?.ResumeBackgroundMusic();
            _penalties = 0;
            App.Logger?.Information("VideoService: Force cleanup completed");
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

            // Duck other apps AND pause our background music
            if (App.Settings.Current.AudioDuckingEnabled)
                App.Audio?.Duck(App.Settings.Current.DuckingLevel);
            App.Audio?.PauseBackgroundMusic();

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
            Application.Current.Dispatcher.Invoke(() =>
            {
                var allScreens = Screen.AllScreens.ToList();
                var primary = allScreens.FirstOrDefault(s => s.Primary) ?? allScreens[0];
                var secondaries = allScreens.Where(s => !s.Primary).ToList();

                // Create primary screen with the actual MediaElement
                var (primaryWin, primaryMedia) = CreatePrimaryVideoWindow(path, primary, strict);
                _windows.Add(primaryWin);

                // Create secondary screens that mirror the primary MediaElement
                if (App.Settings.Current.DualMonitorEnabled)
                {
                    foreach (var scr in secondaries)
                    {
                        var win = CreateMirrorVideoWindow(primaryMedia, scr, strict);
                        _windows.Add(win);
                    }
                }

                // Now play
                primaryMedia.Play();

                if (App.Settings.Current.AttentionChecksEnabled)
                    SetupAttention();
            });

            VideoStarted?.Invoke(this, EventArgs.Empty);
            App.Logger.Information("Playing: {File}", Path.GetFileName(path));
        }

        /// <summary>
        /// Creates the primary video window with the actual MediaElement.
        /// </summary>
        private (Window win, MediaElement media) CreatePrimaryVideoWindow(string path, Screen screen, bool strict)
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
                Volume = App.Settings.Current.MasterVolume / 100.0
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
                App.Logger.Error("Media failed: {Error}", e.ErrorException?.Message);
                Application.Current.Dispatcher.BeginInvoke(OnEnded);
            };

            var grid = new Grid { Background = Brushes.Black };
            grid.Children.Add(mediaElement);
            win.Content = grid;

            SetupStrictHandlers(win, strict);

            // When video is clicked, bring targets back to front
            win.PreviewMouseDown += (s, e) => BringTargetsToFront();

            win.Show();
            win.WindowState = WindowState.Maximized;
            win.Activate();

            // Load source
            mediaElement.Source = new Uri(path);

            App.Logger.Debug("Primary video window on: {Screen}", screen.DeviceName);
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

            // When video is clicked, bring targets back to front
            win.PreviewMouseDown += (s, e) => BringTargetsToFront();

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
            var (win, media) = CreatePrimaryVideoWindow(path, screen, strict);
            if (!withAudio)
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
                        Cleanup();
                };
            }
        }

        #region Attention Checks

        private void SetupAttention()
        {
            Task.Delay(2000).ContinueWith(_ => Application.Current.Dispatcher.BeginInvoke(() =>
            {
                if (!_videoPlaying) return;

                _spawned = 0; // Reset spawned counter
                var dur = _duration > 0 ? _duration : 60;
                // Use setting directly as total count (not density)
                _total = Math.Max(1, App.Settings.Current.AttentionDensity);
                
                for (int i = 0; i < _total; i++)
                    _spawnTimes.Add(3 + _random.NextDouble() * Math.Max(1, dur - 6));
                _spawnTimes.Sort();

                _attentionTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
                _attentionTimer.Tick += CheckSpawnTargets;
                _attentionTimer.Start();

                App.Logger.Information("Attention: {Count} targets over {Duration}s", _total, (int)dur);
            }));
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

                var screens = settings.DualMonitorEnabled ? Screen.AllScreens : new[] { Screen.PrimaryScreen! };
                var screen = screens[_random.Next(screens.Length)];

                _spawned++; // Track actually spawned targets
                App.Logger?.Debug("Spawning attention target: '{Text}' on screen {Screen} ({Spawned}/{Total})", text, screen.DeviceName, _spawned, _total);

                FloatingText? target = null;
                target = new FloatingText(text, screen, settings.AttentionSize, () =>
                {
                    _hits++;
                    App.Progression?.AddXP(10);
                    App.Logger?.Debug("Target hit: {Hits}/{Spawned}", _hits, _spawned);

                    // Remove from targets list immediately on click
                    lock (_targets)
                    {
                        if (target != null) _targets.Remove(target);
                    }
                });

                lock (_targets)
                {
                    _targets.Add(target);
                }

                // Auto-expire with safety check
                var lifespan = settings.AttentionLifespan * 1000;
                Task.Delay(lifespan).ContinueWith(_ =>
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
                    }));
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
            lock (_targets)
            {
                foreach (var t in _targets)
                {
                    t.BringToFront();
                }
            }
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

            Cleanup();
        }

        private void ShowMessage(string text, int ms, Action then)
        {
            // CRITICAL: Set _videoPlaying to false BEFORE CloseAll() so strict mode
            // handlers don't cancel window closing (they check _videoPlaying in Closing event)
            _videoPlaying = false;
            CloseAll();

            var screens = App.Settings.Current.DualMonitorEnabled ? Screen.AllScreens : new[] { Screen.PrimaryScreen! };

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

            Task.Delay(ms).ContinueWith(_ => Application.Current.Dispatcher.BeginInvoke(() =>
            {
                CloseMessageWindows();
                then();
            }));
        }

        private void CloseMessageWindows()
        {
            foreach (var w in _messageWindows.ToList())
            {
                try { w.Close(); } catch { }
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
                App.Logger?.Debug("CloseAll: Destroying {Count} targets", _targets.Count);
                foreach (var t in _targets.ToList()) t.Destroy();
                _targets.Clear();
            }

            App.Logger?.Debug("CloseAll: Closing {Count} video windows, {MsgCount} message windows",
                _windows.Count, _messageWindows.Count);

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
            App.Audio?.ResumeBackgroundMusic();
            _strictActive = false;
            _penalties = 0;

            // Trigger deferred Bambi Reset now that video has ended
            App.Subliminal?.TriggerDeferredBambiReset();

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
                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                var validExtensions = new[] { ".mp4", ".mov", ".avi", ".wmv", ".mkv", ".webm" };

                var files = new List<string>();
                if (Directory.Exists(_videosPath))
                {
                    foreach (var file in Directory.GetFiles(_videosPath))
                    {
                        var ext = Path.GetExtension(file).ToLowerInvariant();
                        if (!validExtensions.Contains(ext)) continue;

                        // Security: Validate path is within allowed directory
                        if (!SecurityHelper.IsPathSafe(file, baseDir))
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
        private readonly Window _win;
        private readonly DispatcherTimer _timer;
        private double _x, _y, _vx, _vy;
        private readonly double _minX, _maxX, _minY, _maxY;
        private bool _dead;

        public FloatingText(string text, Screen screen, int size, Action onHit)
        {
            try
            {
                size = Math.Max(40, size);

                // Use WorkingArea (excludes taskbar) with larger margins to prevent edge spawning
                var area = screen.WorkingArea;
                _minX = area.X + 150;  // Larger margin from edges
                _minY = area.Y + 150;
                _maxX = area.X + area.Width - 200;  // Account for target size + margin
                _maxY = area.Y + area.Height - 150;

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
                    // Fallback to purple classic if colors invalid
                    color1 = Color.FromRgb(155, 89, 182);
                    color2 = Color.FromRgb(142, 68, 173);
                    textColor = Colors.White;
                    borderColor = Colors.White;
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

                // Text shadow color - darker version of text color for floating, or primary color otherwise
                var shadowBase = isFloating ? textColor : color1;
                var shadowColor = Color.FromRgb(
                    (byte)(shadowBase.R * 0.4),
                    (byte)(shadowBase.G * 0.4),
                    (byte)(shadowBase.B * 0.4));

                // Text with customizable font and colors
                var textBlock = new TextBlock
                {
                    Text = text,
                    FontFamily = new FontFamily($"{settings.AttentionFont}, Segoe UI, Arial"),
                    FontSize = size,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(textColor),
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Effect = new System.Windows.Media.Effects.DropShadowEffect
                    {
                        Color = shadowColor,
                        BlurRadius = 3,
                        ShadowDepth = 2,
                        Opacity = 0.8
                    }
                };

                border.Child = textBlock;

                // Measure the text to get proper sizing
                textBlock.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
                double w = textBlock.DesiredSize.Width + 60;  // Add padding
                double h = textBlock.DesiredSize.Height + 40;

                // Ensure minimum size
                w = Math.Max(w, 150);
                h = Math.Max(h, 60);

                _win = new Window
                {
                    WindowStyle = WindowStyle.None,
                    AllowsTransparency = true,
                    Background = Brushes.Transparent,
                    Topmost = true,
                    ShowInTaskbar = false,
                    Width = w,
                    Height = h,
                    Content = border,
                    ShowActivated = false  // Don't steal focus
                };

                // Random position - ensure within bounds
                var rnd = new Random();
                var maxXPos = Math.Max(_minX, _maxX - w);
                var maxYPos = Math.Max(_minY, _maxY - h);
                _x = _minX + rnd.NextDouble() * Math.Max(1, maxXPos - _minX);
                _y = _minY + rnd.NextDouble() * Math.Max(1, maxYPos - _minY);
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
                    if (clicked) return;
                    clicked = true;
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
                };

                _win.Loaded += (s, e) =>
                {
                    _timer.Start();
                    App.Logger?.Debug("Attention target window loaded and visible at ({X}, {Y})", _x, _y);
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
                            audioFile.Volume = 0.6f; // 60% volume for attention target pop
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
            }
            catch { }
        }

        private void FadeOut()
        {
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
            try { _win.Close(); } catch { }
        }

        public void BringToFront()
        {
            if (_dead) return;
            try
            {
                _win.Topmost = false;
                _win.Topmost = true;
                _win.Activate();
            }
            catch { }
        }
    }
}
