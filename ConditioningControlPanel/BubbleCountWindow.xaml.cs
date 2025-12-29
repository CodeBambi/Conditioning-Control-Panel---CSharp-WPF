using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using NAudio.Wave;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel
{
    /// <summary>
    /// Bubble Count Challenge - watch video, count bubbles, enter total
    /// </summary>
    public partial class BubbleCountWindow : Window
    {
        private readonly string _videoPath;
        private readonly BubbleCountService.Difficulty _difficulty;
        private readonly bool _strictMode;
        private readonly Action<bool> _onComplete;
        
        private readonly Random _random = new();
        private readonly List<CountBubble> _activeBubbles = new();
        private DispatcherTimer? _bubbleSpawnTimer;
        private DispatcherTimer? _videoCheckTimer;
        
        private int _bubbleCount = 0;
        private int _targetBubbleCount = 0;
        private double _videoDurationSeconds = 30; // Default, will be updated
        private bool _videoEnded = false;
        private bool _gameCompleted = false;
        
        private BitmapImage? _bubbleImage;
        private string _assetsPath = "";

        public BubbleCountWindow(string videoPath, BubbleCountService.Difficulty difficulty, 
            bool strictMode, Action<bool> onComplete)
        {
            InitializeComponent();
            
            _videoPath = videoPath;
            _difficulty = difficulty;
            _strictMode = strictMode;
            _onComplete = onComplete;
            
            _assetsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets");
            
            // Set difficulty display
            TxtDifficulty.Text = $" ({difficulty})";
            
            // Handle strict mode
            if (_strictMode)
            {
                TxtStrict.Visibility = Visibility.Visible;
                TxtEscHint.Visibility = Visibility.Collapsed;
            }
            
            // Load bubble image
            LoadBubbleImage();
            
            // Key handler
            KeyDown += OnKeyDown;
            
            // Start when loaded
            Loaded += OnLoaded;
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            try
            {
                // Initialize WebView2 for video
                await VideoPlayer.EnsureCoreWebView2Async();
                
                // Get video duration first
                _videoDurationSeconds = await GetVideoDuration(_videoPath);
                
                // Calculate target bubbles based on difficulty and duration
                CalculateTargetBubbles();
                
                // Start playing video
                PlayVideo();
                
                // Start spawning bubbles
                StartBubbleSpawning();
                
                // Monitor for video end
                StartVideoMonitoring();
                
                App.Logger?.Information("Bubble Count game started - Target: {Target} bubbles, Duration: {Duration}s, Difficulty: {Diff}",
                    _targetBubbleCount, _videoDurationSeconds, _difficulty);
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Failed to initialize bubble count game");
                Close();
                _onComplete?.Invoke(false);
            }
        }

        private void CalculateTargetBubbles()
        {
            // Base rate per 30 seconds
            double baseRate = _difficulty switch
            {
                BubbleCountService.Difficulty.Easy => 6,
                BubbleCountService.Difficulty.Medium => 10,
                BubbleCountService.Difficulty.Hard => 16,
                _ => 10
            };
            
            // Scale to video duration
            var scaledCount = (baseRate / 30.0) * _videoDurationSeconds;
            
            // Add randomness (±20%)
            var variance = scaledCount * 0.2;
            _targetBubbleCount = (int)Math.Round(scaledCount + (_random.NextDouble() * variance * 2 - variance));
            _targetBubbleCount = Math.Max(3, _targetBubbleCount); // Minimum 3 bubbles
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

        private async Task<double> GetVideoDuration(string path)
        {
            try
            {
                // Use NAudio to get duration
                using var reader = new MediaFoundationReader(path);
                return reader.TotalTime.TotalSeconds;
            }
            catch
            {
                // Default to 30 seconds if can't determine
                return 30;
            }
        }

        private void PlayVideo()
        {
            try
            {
                var html = $@"
<!DOCTYPE html>
<html>
<head>
    <style>
        * {{ margin: 0; padding: 0; }}
        body {{ background: black; overflow: hidden; }}
        video {{ 
            width: 100vw; 
            height: 100vh; 
            object-fit: contain;
        }}
    </style>
</head>
<body>
    <video id='video' autoplay>
        <source src='file:///{_videoPath.Replace("\\", "/")}' type='video/mp4'>
    </video>
    <script>
        var video = document.getElementById('video');
        video.onended = function() {{
            window.chrome.webview.postMessage('VIDEO_ENDED');
        }};
        video.onerror = function() {{
            window.chrome.webview.postMessage('VIDEO_ERROR');
        }};
    </script>
</body>
</html>";
                
                VideoPlayer.CoreWebView2.WebMessageReceived += (s, args) =>
                {
                    var message = args.TryGetWebMessageAsString();
                    if (message == "VIDEO_ENDED")
                    {
                        Dispatcher.Invoke(() => OnVideoEnded());
                    }
                };
                
                VideoPlayer.CoreWebView2.NavigateToString(html);
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Failed to play video");
            }
        }

        private void StartBubbleSpawning()
        {
            // Calculate spawn interval to distribute bubbles over video duration
            var intervalMs = (_videoDurationSeconds * 1000) / _targetBubbleCount;
            
            // Add some randomness to interval
            _bubbleSpawnTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(intervalMs * 0.7) // Slightly faster to add variance
            };
            
            _bubbleSpawnTimer.Tick += (s, e) =>
            {
                if (_bubbleCount < _targetBubbleCount && !_videoEnded)
                {
                    // Random chance to spawn (creates natural distribution)
                    if (_random.NextDouble() < 0.7 || _bubbleCount < _targetBubbleCount / 2)
                    {
                        SpawnBubble();
                    }
                }
            };
            
            _bubbleSpawnTimer.Start();
            
            // Spawn first bubble after a short delay
            Task.Delay(1000).ContinueWith(_ => Dispatcher.Invoke(() =>
            {
                if (!_videoEnded) SpawnBubble();
            }));
        }

        private void SpawnBubble()
        {
            if (_bubbleCount >= _targetBubbleCount) return;
            
            try
            {
                var screenWidth = ActualWidth;
                var screenHeight = ActualHeight;
                
                // Random size (80-150 pixels)
                var size = _random.Next(80, 150);
                
                // Random position (avoid edges)
                var x = _random.Next(50, (int)Math.Max(100, screenWidth - size - 50));
                var y = _random.Next(100, (int)Math.Max(200, screenHeight - size - 100));
                
                // Create bubble
                var bubble = new CountBubble(_bubbleImage, size, x, y, _random, PlayPopSound, OnBubblePopped);
                _activeBubbles.Add(bubble);
                BubbleCanvas.Children.Add(bubble.Visual);
                
                _bubbleCount++;
                
                App.Logger?.Debug("Spawned bubble #{Count} at ({X}, {Y})", _bubbleCount, x, y);
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

        private void StartVideoMonitoring()
        {
            _videoCheckTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };
            
            var elapsed = 0.0;
            _videoCheckTimer.Tick += (s, e) =>
            {
                elapsed += 0.5;
                
                // Fallback: if video duration exceeded, trigger end
                if (elapsed >= _videoDurationSeconds + 2 && !_videoEnded)
                {
                    OnVideoEnded();
                }
            };
            
            _videoCheckTimer.Start();
        }

        private void OnVideoEnded()
        {
            if (_videoEnded) return;
            _videoEnded = true;
            
            _bubbleSpawnTimer?.Stop();
            _videoCheckTimer?.Stop();
            
            // Clear any remaining bubbles
            foreach (var bubble in _activeBubbles.ToArray())
            {
                bubble.ForcePop();
            }
            _activeBubbles.Clear();
            BubbleCanvas.Children.Clear();
            
            // Show the result input window
            ShowResultWindow();
        }

        private void ShowResultWindow()
        {
            var resultWindow = new BubbleCountResultWindow(
                _targetBubbleCount, 
                _strictMode,
                (success) =>
                {
                    _gameCompleted = true;
                    Close();
                    _onComplete?.Invoke(success);
                });
            
            resultWindow.Show();
            
            // Hide this window but keep it alive until result is done
            Hide();
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape && !_strictMode && !_gameCompleted)
            {
                _gameCompleted = true;
                _bubbleSpawnTimer?.Stop();
                _videoCheckTimer?.Stop();
                Close();
                _onComplete?.Invoke(false);
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            _bubbleSpawnTimer?.Stop();
            _videoCheckTimer?.Stop();
            
            foreach (var bubble in _activeBubbles)
            {
                bubble.Dispose();
            }
            _activeBubbles.Clear();
            
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
        private readonly Action _playSound;
        private readonly Action<CountBubble> _onPopped;
        private readonly Random _random;
        
        private double _scale = 0.1;
        private double _targetScale = 1.0;
        private double _opacity = 1.0;
        private double _rotation = 0;
        private bool _isPopping = false;
        private bool _isDisposed = false;

        public CountBubble(BitmapImage? image, int size, double x, double y, 
            Random random, Action playSound, Action<CountBubble> onPopped)
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
                    // Pop animation - expand and fade
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
                    // Appear animation - grow to target
                    if (_scale < _targetScale)
                    {
                        _scale = Math.Min(_targetScale, _scale + 0.1);
                    }
                    
                    // Gentle wobble
                    _rotation += 0.5;
                }
                
                // Update visual
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
}
