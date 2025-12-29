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
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel
{
    /// <summary>
    /// Bubble Count Challenge - watch video, count bubbles, enter total
    /// Multi-monitor support with synced bubbles
    /// </summary>
    public partial class BubbleCountWindow : Window
    {
        private readonly string _videoPath;
        private readonly BubbleCountService.Difficulty _difficulty;
        private readonly bool _strictMode;
        private readonly Action<bool> _onComplete;
        private readonly System.Windows.Forms.Screen _screen;
        private readonly bool _isPrimary;
        
        private readonly Random _random = new();
        private readonly List<CountBubble> _activeBubbles = new();
        private DispatcherTimer? _bubbleSpawnTimer;
        
        private int _bubbleCount = 0;
        private int _targetBubbleCount = 0;
        private double _videoDurationSeconds = 30;
        private bool _videoEnded = false;
        private bool _gameCompleted = false;
        
        private BitmapImage? _bubbleImage;
        private string _assetsPath = "";
        
        // Multi-monitor support
        private static List<BubbleCountWindow> _allWindows = new();
        private static int _sharedBubbleCount = 0;
        private static int _sharedTargetCount = 0;

        public BubbleCountWindow(string videoPath, BubbleCountService.Difficulty difficulty, 
            bool strictMode, Action<bool> onComplete, 
            System.Windows.Forms.Screen? screen = null, bool isPrimary = true)
        {
            InitializeComponent();
            
            _videoPath = videoPath;
            _difficulty = difficulty;
            _strictMode = strictMode;
            _onComplete = onComplete;
            _screen = screen ?? System.Windows.Forms.Screen.PrimaryScreen!;
            _isPrimary = isPrimary;
            
            _assetsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets");
            
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
            
            // Register window
            _allWindows.Add(this);
            
            // Start when loaded
            Loaded += OnLoaded;
        }

        /// <summary>
        /// Show bubble count game on all monitors
        /// </summary>
        public static void ShowOnAllMonitors(string videoPath, BubbleCountService.Difficulty difficulty, 
            bool strictMode, Action<bool> onComplete)
        {
            // Reset shared state
            _allWindows.Clear();
            _sharedBubbleCount = 0;
            _sharedTargetCount = 0;
            
            var settings = App.Settings.Current;
            var screens = settings.DualMonitorEnabled 
                ? System.Windows.Forms.Screen.AllScreens 
                : new[] { System.Windows.Forms.Screen.PrimaryScreen! };
            
            var primary = screens.FirstOrDefault(s => s.Primary) ?? screens[0];
            
            // Create secondary windows FIRST (give them head start for video loading)
            foreach (var screen in screens.Where(s => s != primary))
            {
                var secondaryWindow = new BubbleCountWindow(videoPath, difficulty, strictMode, onComplete, screen, false);
                secondaryWindow.Show();
                secondaryWindow.WindowState = WindowState.Maximized;
                ForceTopmost(secondaryWindow);
            }
            
            // Create primary window last (with audio)
            var primaryWindow = new BubbleCountWindow(videoPath, difficulty, strictMode, onComplete, primary, true);
            primaryWindow.Show();
            primaryWindow.WindowState = WindowState.Maximized;
            primaryWindow.Activate();
            ForceTopmost(primaryWindow);
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

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_isPrimary)
                {
                    // Get video duration
                    _videoDurationSeconds = GetVideoDuration(_videoPath);
                    
                    // Calculate target bubbles
                    CalculateTargetBubbles();
                    _sharedTargetCount = _targetBubbleCount;
                    
                    // Start spawning bubbles
                    StartBubbleSpawning();
                    
                    App.Logger?.Information("Bubble Count game started - Target: {Target} bubbles, Duration: {Duration}s, Difficulty: {Diff}",
                        _targetBubbleCount, _videoDurationSeconds, _difficulty);
                }
                else
                {
                    // Secondary windows sync with primary
                    _targetBubbleCount = _sharedTargetCount;
                }
                
                // Start video on all windows
                PlayVideo();
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Failed to initialize bubble count game");
                if (_isPrimary)
                {
                    CloseAllWindows();
                    _onComplete?.Invoke(false);
                }
            }
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
                var imagePath = Path.Combine(_assetsPath, "images", "bubble.png");
                if (File.Exists(imagePath))
                {
                    _bubbleImage = new BitmapImage();
                    _bubbleImage.BeginInit();
                    _bubbleImage.UriSource = new Uri(imagePath);
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

        private void PlayVideo()
        {
            try
            {
                // Only primary window plays audio
                VideoPlayer.Volume = _isPrimary ? 1.0 : 0.0;
                VideoPlayer.IsMuted = !_isPrimary;
                
                VideoPlayer.Source = new Uri(_videoPath);
                VideoPlayer.Play();
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Failed to play video");
            }
        }

        private void VideoPlayer_MediaOpened(object sender, RoutedEventArgs e)
        {
            App.Logger?.Debug("Bubble count video opened on {Primary}", _isPrimary ? "primary" : "secondary");
        }

        private void VideoPlayer_MediaEnded(object sender, RoutedEventArgs e)
        {
            // Only primary triggers end
            if (_isPrimary)
            {
                OnVideoEnded();
            }
        }

        private void VideoPlayer_MediaFailed(object sender, ExceptionRoutedEventArgs e)
        {
            App.Logger?.Error("Bubble count video failed: {Error}", e.ErrorException?.Message);
            if (_isPrimary)
            {
                OnVideoEnded();
            }
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
                if (_sharedBubbleCount < _targetBubbleCount && !_videoEnded)
                {
                    if (_random.NextDouble() < 0.7 || _sharedBubbleCount < _targetBubbleCount / 2)
                    {
                        SpawnBubbleOnAllWindows();
                    }
                }
            };
            
            _bubbleSpawnTimer.Start();
            
            // Spawn first bubble after delay
            Task.Delay(1000).ContinueWith(_ => Dispatcher.Invoke(() =>
            {
                if (!_videoEnded) SpawnBubbleOnAllWindows();
            }));
        }

        private void SpawnBubbleOnAllWindows()
        {
            if (_sharedBubbleCount >= _targetBubbleCount) return;
            _sharedBubbleCount++;
            _bubbleCount = _sharedBubbleCount;
            
            // Generate random position (relative 0-1)
            var relX = _random.NextDouble() * 0.7 + 0.15; // 15% to 85%
            var relY = _random.NextDouble() * 0.5 + 0.25; // 25% to 75%
            var size = _random.Next(80, 150);
            
            // Spawn on all windows
            foreach (var window in _allWindows)
            {
                window.SpawnBubbleAt(relX, relY, size);
            }
        }

        private void SpawnBubbleAt(double relX, double relY, int size)
        {
            try
            {
                // Use canvas actual size for positioning
                var canvasWidth = BubbleCanvas.ActualWidth > 0 ? BubbleCanvas.ActualWidth : ActualWidth;
                var canvasHeight = BubbleCanvas.ActualHeight > 0 ? BubbleCanvas.ActualHeight : ActualHeight;
                
                var x = relX * canvasWidth - size / 2;
                var y = relY * canvasHeight - size / 2;
                
                // Only primary plays sound
                var bubble = new CountBubble(_bubbleImage, size, x, y, _random, 
                    _isPrimary ? PlayPopSound : null, OnBubblePopped);
                _activeBubbles.Add(bubble);
                BubbleCanvas.Children.Add(bubble.Visual);
                
                App.Logger?.Debug("Spawned bubble #{Count} at ({X:F0}, {Y:F0}) on {Primary}", 
                    _sharedBubbleCount, x, y, _isPrimary ? "primary" : "secondary");
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("Failed to spawn bubble: {Error}", ex.Message);
            }
        }

        private void OnBubblePopped(CountBubble bubble)
        {
            _activeBubbles.Remove(bubble);
            BubbleCanvas.Children.Remove(bubble.Visual);
        }

        private void PlayPopSound()
        {
            try
            {
                var soundsPath = Path.Combine(_assetsPath, "sounds");
                var popFiles = new[] { "Pop.mp3", "Pop2.mp3", "Pop3.mp3" };
                var chosenPop = popFiles[_random.Next(popFiles.Length)];
                var popPath = Path.Combine(soundsPath, chosenPop);

                if (File.Exists(popPath))
                {
                    Task.Run(() =>
                    {
                        try
                        {
                            using var audioFile = new AudioFileReader(popPath);
                            audioFile.Volume = 0.5f;
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

        private void OnVideoEnded()
        {
            if (_videoEnded) return;
            _videoEnded = true;
            
            _bubbleSpawnTimer?.Stop();
            
            // Mark all windows as ended
            foreach (var window in _allWindows)
            {
                window._videoEnded = true;
                window._bubbleSpawnTimer?.Stop();
            }
            
            // Clear remaining bubbles on all windows
            foreach (var window in _allWindows)
            {
                foreach (var bubble in window._activeBubbles.ToArray())
                {
                    bubble.ForcePop();
                }
                window._activeBubbles.Clear();
                window.BubbleCanvas.Children.Clear();
            }
            
            // Show result window (only from primary)
            if (_isPrimary)
            {
                ShowResultWindow();
            }
        }

        private void ShowResultWindow()
        {
            // Hide all game windows first
            foreach (var window in _allWindows)
            {
                window.Hide();
            }
            
            // Show result window on all monitors
            BubbleCountResultWindow.ShowOnAllMonitors(
                _sharedBubbleCount, 
                _strictMode,
                (success) =>
                {
                    _gameCompleted = true;
                    CloseAllWindows();
                    _onComplete?.Invoke(success);
                });
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape && !_strictMode && !_gameCompleted)
            {
                _gameCompleted = true;
                CloseAllWindows();
                _onComplete?.Invoke(false);
            }
        }

        private void CloseAllWindows()
        {
            foreach (var window in _allWindows.ToArray())
            {
                try 
                { 
                    window._bubbleSpawnTimer?.Stop();
                    window.VideoPlayer?.Stop();
                    window.Close(); 
                } 
                catch { }
            }
            _allWindows.Clear();
        }

        protected override void OnClosed(EventArgs e)
        {
            _bubbleSpawnTimer?.Stop();
            VideoPlayer?.Stop();
            
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
    /// Individual bubble for counting game - appears, stays briefly, then pops
    /// </summary>
    internal class CountBubble : IDisposable
    {
        public Image Visual { get; }
        
        private readonly DispatcherTimer _lifeTimer;
        private readonly DispatcherTimer _animTimer;
        private readonly Action? _playSound;
        private readonly Action<CountBubble> _onPopped;
        private readonly Random _random;
        
        private double _scale = 0.1;
        private double _targetScale = 1.0;
        private double _opacity = 1.0;
        private double _rotation = 0;
        private bool _isPopping = false;
        private bool _isDisposed = false;

        public CountBubble(BitmapImage? image, int size, double x, double y, 
            Random random, Action? playSound, Action<CountBubble> onPopped)
        {
            _random = random;
            _playSound = playSound;
            _onPopped = onPopped;
            _rotation = random.Next(360);
            
            Visual = new Image
            {
                Width = size,
                Height = size,
                Stretch = Stretch.Uniform,
                RenderTransformOrigin = new Point(0.5, 0.5),
                Opacity = 0
            };
            
            Canvas.SetLeft(Visual, x);
            Canvas.SetTop(Visual, y);
            
            if (image != null)
            {
                Visual.Source = image;
            }
            else
            {
                // Fallback gradient bubble
                var drawing = new DrawingGroup();
                using (var ctx = drawing.Open())
                {
                    var gradientBrush = new RadialGradientBrush(
                        Color.FromArgb(200, 255, 182, 193),
                        Color.FromArgb(100, 255, 105, 180));
                    ctx.DrawEllipse(gradientBrush, new Pen(Brushes.White, 2),
                        new Point(size / 2, size / 2), size / 2 - 5, size / 2 - 5);
                }
                Visual.Source = new DrawingImage(drawing);
            }
            
            // Transform for scale and rotation
            var transformGroup = new TransformGroup();
            transformGroup.Children.Add(new ScaleTransform(_scale, _scale));
            transformGroup.Children.Add(new RotateTransform(_rotation));
            Visual.RenderTransform = transformGroup;
            
            // Animation timer
            _animTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
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
                
                Visual.Opacity = Math.Max(0, _opacity);
                
                if (Visual.RenderTransform is TransformGroup tg && tg.Children.Count >= 2)
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
