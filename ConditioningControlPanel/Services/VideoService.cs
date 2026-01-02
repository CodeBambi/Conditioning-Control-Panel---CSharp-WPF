using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Forms;
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
        
        private bool _isRunning;
        private bool _videoPlaying;
        private bool _strictActive;
        private string? _retryPath;
        private DateTime _startTime;
        private double _duration;
        
        private List<double> _spawnTimes = new();
        private int _hits, _total, _penalties;

        private readonly string _videosPath;

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
            
            // Trigger Bambi Freeze subliminal+audio BEFORE video, but only if no minigame is active
            if (App.BubbleCount == null || !App.BubbleCount.IsBusy)
            {
                App.Subliminal?.TriggerBambiFreeze();
                
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
                // Minigame is active, just play video without freeze
                PlayVideo(path, App.Settings.Current.StrictLockEnabled);
            }
        }

        /// <summary>
        /// Force cleanup without scheduling next - used for panic key and preventing stacking
        /// </summary>
        public void ForceCleanup()
        {
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

            // Stop flashes during video
            App.Flash?.Stop();
            
            // Duck other apps AND pause our background music
            if (App.Settings.Current.AudioDuckingEnabled) 
                App.Audio?.Duck(App.Settings.Current.DuckingLevel);
            App.Audio?.PauseBackgroundMusic();
            
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
                    _duration = mediaElement.NaturalDuration.TimeSpan.TotalSeconds;
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
                win.Deactivated += (s, e) =>
                {
                    if (_videoPlaying && _strictActive)
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
                
                var dur = _duration > 0 ? _duration : 60;
                _total = Math.Max(1, (int)(dur / 30 * App.Settings.Current.AttentionDensity));
                
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
            var settings = App.Settings.Current;
            var pool = settings.AttentionPool.Where(p => p.Value).Select(p => p.Key).ToList();
            var text = pool.Count > 0 ? pool[_random.Next(pool.Count)] : "CLICK ME";

            var screens = settings.DualMonitorEnabled ? Screen.AllScreens : new[] { Screen.PrimaryScreen! };
            var screen = screens[_random.Next(screens.Length)];

            var target = new FloatingText(text, screen, settings.AttentionSize, () =>
            {
                _hits++;
                App.Progression?.AddXP(10);
                App.Logger.Debug("Target hit: {Hits}/{Total}", _hits, _total);
            });

            _targets.Add(target);

            // Auto-expire
            Task.Delay(settings.AttentionLifespan * 1000).ContinueWith(_ =>
                Application.Current.Dispatcher.BeginInvoke(() =>
                {
                    _targets.Remove(target);
                    target.Destroy();
                }));
        }

        #endregion

        #region Video End / Penalty / Mercy

        private void OnEnded()
        {
            if (!_videoPlaying) return;

            var settings = App.Settings.Current;
            bool loop = false, troll = false;

            if (settings.AttentionChecksEnabled && _total > 0)
            {
                bool passed = _hits >= _total;
                App.Logger.Information("Attention result: {Hits}/{Total} = {Result}", _hits, _total, passed ? "PASS" : "FAIL");

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
                        CloseAll();
                        _hits = 0;
                        _spawnTimes.Clear();
                        _videoPlaying = false;
                        PlayVideo(_retryPath!, _strictActive);
                    });
                return;
            }

            Cleanup();
        }

        private void ShowMessage(string text, int ms, Action then)
        {
            CloseAll();
            
            var screens = App.Settings.Current.DualMonitorEnabled ? Screen.AllScreens : new[] { Screen.PrimaryScreen! };
            var msgWindows = new List<Window>();

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
                msgWindows.Add(win);
            }

            Task.Delay(ms).ContinueWith(_ => Application.Current.Dispatcher.BeginInvoke(() =>
            {
                foreach (var w in msgWindows) try { w.Close(); } catch { }
                then();
            }));
        }

        #endregion

        #region Cleanup

        private void CloseAll()
        {
            _attentionTimer?.Stop();
            
            foreach (var t in _targets.ToList()) t.Destroy();
            _targets.Clear();
            
            foreach (var w in _windows.ToList())
            {
                try
                {
                    // Stop any MediaElement
                    if (w.Content is Grid g && g.Children.Count > 0 && g.Children[0] is MediaElement me)
                        me.Stop();
                    w.Close();
                }
                catch { }
            }
            _windows.Clear();
        }

        private void Cleanup()
        {
            _videoPlaying = false;
            CloseAll();
            App.Audio?.Unduck();
            App.Audio?.ResumeBackgroundMusic();
            _strictActive = false;
            _penalties = 0;
            
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
    /// Bouncing magenta text with black outline - like Python TransparentTextWindow
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
            size = Math.Max(40, size);

            // Use WorkingArea (excludes taskbar)
            var area = screen.WorkingArea;
            _minX = area.X + 50;
            _minY = area.Y + 50;
            _maxX = area.X + area.Width - 50;
            _maxY = area.Y + area.Height - 50;

            // Create outlined text
            var grid = new Grid { Background = Brushes.Transparent };
            
            // Black outline (8 directions)
            foreach (var (ox, oy) in new[] { (-3,0), (3,0), (0,-3), (0,3), (-3,-3), (3,-3), (-3,3), (3,3) })
            {
                grid.Children.Add(new TextBlock
                {
                    Text = text,
                    FontFamily = new FontFamily("Impact"),
                    FontSize = size,
                    FontWeight = FontWeights.Bold,
                    Foreground = Brushes.Black,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    RenderTransform = new TranslateTransform(ox, oy)
                });
            }

            // Magenta text on top
            grid.Children.Add(new TextBlock
            {
                Text = text,
                FontFamily = new FontFamily("Impact"),
                FontSize = size,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(255, 0, 255)),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Cursor = System.Windows.Input.Cursors.Hand
            });

            double w = size * text.Length * 0.6 + 50;
            double h = size * 1.5 + 30;

            _win = new Window
            {
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = Brushes.Transparent,
                Topmost = true,
                ShowInTaskbar = false,
                Width = w,
                Height = h,
                Content = grid
            };

            // Random position
            var rnd = new Random();
            _x = _minX + rnd.NextDouble() * Math.Max(1, _maxX - _minX - w);
            _y = _minY + rnd.NextDouble() * Math.Max(1, _maxY - _minY - h);
            _win.Left = _x;
            _win.Top = _y;

            // Random velocity
            var angle = rnd.NextDouble() * Math.PI * 2;
            _vx = Math.Cos(angle) * 2.5;
            _vy = Math.Sin(angle) * 2.5;

            // Click = hit
            bool clicked = false;
            _win.MouseLeftButtonDown += (s, e) =>
            {
                if (clicked) return;
                clicked = true;
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

            _win.Loaded += (s, e) => _timer.Start();
            _win.Show();
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
            _dead = true;
            _timer.Stop();
            try { _win.Close(); } catch { }
        }
    }
}
