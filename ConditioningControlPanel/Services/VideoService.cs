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
        private readonly List<string> _videoQueue = new();
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
            Cleanup();
        }

        public void TriggerVideo()
        {
            if (_videoPlaying) return;
            
            var path = GetNextVideo();
            if (string.IsNullOrEmpty(path))
            {
                System.Windows.MessageBox.Show($"No videos in:\n{_videosPath}", "No Videos");
                return;
            }
            
            PlayVideo(path, App.Settings.Current.StrictLockEnabled);
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
            
            App.Progression?.AddXP(50);

            Application.Current.Dispatcher.Invoke(() =>
            {
                var allScreens = Screen.AllScreens.ToList();
                var primary = allScreens.FirstOrDefault(s => s.Primary) ?? allScreens[0];
                var secondaries = allScreens.Where(s => !s.Primary).ToList();

                // Create secondary screens first (give them head start)
                if (App.Settings.Current.DualMonitorEnabled)
                {
                    foreach (var scr in secondaries)
                    {
                        var win = CreateFullscreenVideoWindow(path, scr, strict, withAudio: false);
                        _windows.Add(win);
                    }
                }

                // Create primary screen
                var primaryWin = CreateFullscreenVideoWindow(path, primary, strict, withAudio: true);
                _windows.Add(primaryWin);

                if (App.Settings.Current.AttentionChecksEnabled)
                    SetupAttention();
            });

            VideoStarted?.Invoke(this, EventArgs.Empty);
            App.Logger.Information("Playing: {File}", Path.GetFileName(path));
        }

        /// <summary>
        /// Creates a fullscreen video window on the specified screen.
        /// </summary>
        private Window CreateFullscreenVideoWindow(string path, Screen screen, bool strict, bool withAudio)
        {
            var win = new Window
            {
                WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false,
                Topmost = true,
                Background = Brushes.Black,
                WindowStartupLocation = WindowStartupLocation.Manual,
                // Position on target screen first
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
                Volume = withAudio ? App.Settings.Current.MasterVolume / 100.0 : 0,
                IsMuted = !withAudio
            };

            if (withAudio)
            {
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
            }

            var grid = new Grid { Background = Brushes.Black };
            grid.Children.Add(mediaElement);
            win.Content = grid;

            SetupStrictHandlers(win, strict);

            win.Show();
            win.WindowState = WindowState.Maximized;
            win.Activate();

            // Load and play
            mediaElement.Source = new Uri(path);
            mediaElement.Play();

            App.Logger.Debug("Video window on: {Screen} (audio: {Audio})", screen.DeviceName, withAudio);
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
                App.Progression?.AddXP(5);
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
                
                if (!passed) loop = true;
                else if (_random.NextDouble() < 0.1) { loop = troll = true; }
                if (passed) App.Progression?.AddXP(10);
            }

            if (loop && !string.IsNullOrEmpty(_retryPath))
            {
                _penalties++;
                if (_penalties >= 3 && settings.MercySystemEnabled)
                    ShowMessage("BAMBI GETS MERCY", 2500, Cleanup);
                else
                    ShowMessage(troll ? "GOOD GIRL!\nWATCH AGAIN 😈" : "DUMB BAMBI!\nTRY AGAIN", 2000, () =>
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
                var files = Directory.Exists(_videosPath)
                    ? Directory.GetFiles(_videosPath)
                        .Where(f => new[] { ".mp4", ".mov", ".avi", ".wmv", ".mkv", ".webm" }
                            .Contains(Path.GetExtension(f).ToLowerInvariant()))
                        .ToList()
                    : new List<string>();
                if (files.Count == 0) return null;
                _videoQueue.AddRange(files.OrderBy(_ => _random.Next()));
            }
            var v = _videoQueue[0];
            _videoQueue.RemoveAt(0);
            return v;
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
