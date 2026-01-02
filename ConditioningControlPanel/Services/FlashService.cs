using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Windows.Forms; // For Screen class
using NAudio.Wave;
using Serilog;
using ConditioningControlPanel.Models;
using Image = System.Windows.Controls.Image;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// Handles flash image display with full GIF animation support.
    /// Ported from Python engine.py with all features intact.
    /// </summary>
    public class FlashService : IDisposable
    {
        #region Fields

        private readonly Random _random = new();
        private readonly List<FlashWindow> _activeWindows = new();
        private Queue<string> _imageQueue = new();  // Performance: Changed to Queue for O(1) dequeue
        private Queue<string> _soundQueue = new();  // Performance: Changed to Queue for O(1) dequeue
        private readonly object _lockObj = new();

        // Performance: Cache for directory file listings to avoid repeated disk scans
        private static readonly Dictionary<string, (List<string> files, DateTime lastScan)> _fileListCache = new();
        private static readonly object _cacheLock = new();
        private const int CACHE_EXPIRY_SECONDS = 60;  // Re-scan directories every 60 seconds

        private DispatcherTimer? _schedulerTimer;
        private DispatcherTimer? _heartbeatTimer;
        private CancellationTokenSource? _cancellationSource;
        
        private bool _isRunning;
        private bool _isBusy;
        private DateTime _virtualEndTime = DateTime.MinValue;
        private bool _cleanupInProgress;
        
        // Audio - only ONE sound per flash event
        private WaveOutEvent? _currentSound;
        private AudioFileReader? _currentAudioFile;
        private bool _soundPlayingForCurrentFlash;

        // Paths
        private readonly string _imagesPath;
        private readonly string _soundsPath;

        #endregion

        #region Events

        public event EventHandler? FlashDisplayed;
        public event EventHandler? FlashClicked;

        #endregion

        #region Constructor

        public FlashService()
        {
            var assetsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets");
            _imagesPath = Path.Combine(assetsPath, "images");
            _soundsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "sounds", "flashes_audio");

            Directory.CreateDirectory(_imagesPath);
            Directory.CreateDirectory(_soundsPath);

            // Heartbeat timer for animation and fade management
            _heartbeatTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(33) // ~30 FPS
            };
            _heartbeatTimer.Tick += Heartbeat_Tick;
        }

        #endregion

        #region Public Methods

        public void Start()
        {
            if (_isRunning) return;
            
            _isRunning = true;
            _cancellationSource = new CancellationTokenSource();
            _heartbeatTimer?.Start();
            
            ScheduleNextFlash();
            
            App.Logger.Information("FlashService started");
        }

        public void Stop()
        {
            _isRunning = false;
            _cancellationSource?.Cancel();
            _heartbeatTimer?.Stop();
            _schedulerTimer?.Stop();
            
            StopCurrentSound();
            CloseAllWindows();
            
            App.Logger.Information("FlashService stopped");
        }

        public void TriggerFlash()
        {
            if (!_isRunning || _isBusy) return;
            
            _isBusy = true;
            _soundPlayingForCurrentFlash = false; // Reset for new flash event
            Task.Run(() => LoadAndShowImages());
        }

        public void LoadAssets()
        {
            lock (_lockObj)
            {
                _imageQueue = new Queue<string>();  // Performance: Reset queues
                _soundQueue = new Queue<string>();
            }
            ClearFileCache();  // Performance: Clear cached file listings to pick up new files
            App.Logger.Information("Assets reloaded");
        }

        /// <summary>
        /// Refresh the flash schedule when frequency changes
        /// </summary>
        public void RefreshSchedule()
        {
            if (!_isRunning) return;
            ScheduleNextFlash();
        }

        #endregion

        #region Scheduling

        private void ScheduleNextFlash()
        {
            if (!_isRunning) return;
            
            var settings = App.Settings.Current;
            if (!settings.FlashEnabled) return;
            
            // flash_freq = flashes per HOUR (1-180)
            var baseFreq = Math.Max(1, settings.FlashFrequency);
            var baseInterval = 3600.0 / baseFreq; // seconds between flashes
            
            // Add ±30% variance
            var variance = baseInterval * 0.3;
            var interval = baseInterval + (_random.NextDouble() * variance * 2 - variance);
            interval = Math.Max(3, interval); // Minimum 3 seconds
            
            _schedulerTimer?.Stop();
            _schedulerTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(interval)
            };
            _schedulerTimer.Tick += (s, e) =>
            {
                _schedulerTimer?.Stop();
                if (_isRunning && !_isBusy)
                {
                    TriggerFlash();
                }
                ScheduleNextFlash();
            };
            _schedulerTimer.Start();
        }

        #endregion

        #region Image Loading

        private async void LoadAndShowImages()
        {
            try
            {
                var settings = App.Settings.Current;
                var images = GetNextImages(settings.SimultaneousImages);
                
                if (images.Count == 0)
                {
                    _isBusy = false;
                    return;
                }

                // Get sound ONCE for this flash event
                var soundPath = GetNextSound();
                var monitors = GetMonitors(settings.DualMonitorEnabled);
                
                // Scale is percentage: 50-250%, stored as 50-250, so divide by 100
                var scale = settings.ImageScale / 100.0;
                
                // Load images in background
                var loadedImages = new List<LoadedImageData>();
                foreach (var imagePath in images)
                {
                    var data = await LoadImageAsync(imagePath);
                    if (data != null)
                    {
                        var monitor = monitors[_random.Next(monitors.Count)];
                        var geometry = CalculateGeometry(data.Width, data.Height, monitor, scale);
                        data.Geometry = geometry;
                        data.Monitor = monitor;
                        loadedImages.Add(data);
                    }
                }

                if (loadedImages.Count == 0)
                {
                    _isBusy = false;
                    return;
                }

                // Show on UI thread - pass sound path only ONCE
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    ShowImages(loadedImages, soundPath, false);
                });
            }
            catch (Exception ex)
            {
                App.Logger.Error(ex, "Error loading flash images");
                _isBusy = false;
            }
        }

        private async Task<LoadedImageData?> LoadImageAsync(string path)
        {
            try
            {
                return await Task.Run(() =>
                {
                    var extension = Path.GetExtension(path).ToLowerInvariant();
                    var data = new LoadedImageData { FilePath = path };
                    
                    if (extension == ".gif")
                    {
                        // Load GIF frames using System.Drawing (more reliable)
                        LoadGifFramesSystemDrawing(path, data);
                    }
                    else
                    {
                        // Load static image
                        using var bitmap = new System.Drawing.Bitmap(path);
                        var bitmapSource = ConvertToBitmapSource(bitmap);
                        bitmapSource.Freeze();
                        
                        data.Frames.Add(bitmapSource);
                        data.Width = bitmap.Width;
                        data.Height = bitmap.Height;
                        data.FrameDelay = TimeSpan.FromMilliseconds(100);
                    }
                    
                    return data.Frames.Count > 0 ? data : null;
                });
            }
            catch (Exception ex)
            {
                App.Logger.Debug("Could not load image {Path}: {Error}", path, ex.Message);
                return null;
            }
        }

        private void LoadGifFramesSystemDrawing(string path, LoadedImageData data)
        {
            try
            {
                using var gif = System.Drawing.Image.FromFile(path);
                var dimension = new FrameDimension(gif.FrameDimensionsList[0]);
                var frameCount = gif.GetFrameCount(dimension);

                // Get frame delay from metadata
                var frameDelay = 100; // Default 100ms
                try
                {
                    var propertyItem = gif.GetPropertyItem(0x5100); // FrameDelay property
                    if (propertyItem != null && propertyItem.Value != null)
                    {
                        frameDelay = BitConverter.ToInt32(propertyItem.Value, 0) * 10; // Convert to ms
                        if (frameDelay < 20) frameDelay = 100; // Sanity check
                    }
                }
                catch { /* Use default */ }

                // Performance: Dynamically limit frames based on image dimensions
                // Large images need fewer frames to avoid memory pressure
                var pixelsPerFrame = gif.Width * gif.Height * 4; // BGRA32 = 4 bytes per pixel
                var estimatedMemoryMB = (pixelsPerFrame * frameCount) / (1024.0 * 1024.0);

                // Cap at 30MB per GIF to prevent memory issues
                const double MAX_MEMORY_MB = 30.0;
                var maxFrames = frameCount;
                if (estimatedMemoryMB > MAX_MEMORY_MB)
                {
                    maxFrames = (int)(frameCount * (MAX_MEMORY_MB / estimatedMemoryMB));
                    maxFrames = Math.Max(10, maxFrames); // At least 10 frames for animation
                }
                maxFrames = Math.Min(maxFrames, 60); // Hard cap at 60 frames

                var step = frameCount > maxFrames ? frameCount / maxFrames : 1;
                
                for (int i = 0; i < frameCount && data.Frames.Count < maxFrames; i += step)
                {
                    gif.SelectActiveFrame(dimension, i);
                    
                    // Clone the frame to avoid disposal issues
                    using var frameBitmap = new System.Drawing.Bitmap(gif.Width, gif.Height);
                    using (var g = Graphics.FromImage(frameBitmap))
                    {
                        g.DrawImage(gif, 0, 0, gif.Width, gif.Height);
                    }
                    
                    var bitmapSource = ConvertToBitmapSource(frameBitmap);
                    bitmapSource.Freeze();
                    data.Frames.Add(bitmapSource);
                }

                data.Width = gif.Width;
                data.Height = gif.Height;
                data.FrameDelay = TimeSpan.FromMilliseconds(step > 1 ? frameDelay * step : frameDelay);
                
                App.Logger.Debug("Loaded GIF with {Count} frames, delay {Delay}ms", data.Frames.Count, data.FrameDelay.TotalMilliseconds);
            }
            catch (Exception ex)
            {
                App.Logger.Debug("Could not load GIF frames: {Error}", ex.Message);
                
                // Fallback: load as static image
                try
                {
                    using var bitmap = new System.Drawing.Bitmap(path);
                    var bitmapSource = ConvertToBitmapSource(bitmap);
                    bitmapSource.Freeze();
                    
                    data.Frames.Add(bitmapSource);
                    data.Width = bitmap.Width;
                    data.Height = bitmap.Height;
                    data.FrameDelay = TimeSpan.FromMilliseconds(100);
                }
                catch { /* Give up */ }
            }
        }

        private BitmapSource ConvertToBitmapSource(System.Drawing.Bitmap bitmap)
        {
            var bitmapData = bitmap.LockBits(
                new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height),
                ImageLockMode.ReadOnly,
                bitmap.PixelFormat);

            var bitmapSource = BitmapSource.Create(
                bitmap.Width, bitmap.Height,
                96, 96,
                PixelFormats.Bgra32,
                null,
                bitmapData.Scan0,
                bitmapData.Stride * bitmap.Height,
                bitmapData.Stride);

            bitmap.UnlockBits(bitmapData);
            return bitmapSource;
        }

        #endregion

        #region Display

        private void ShowImages(List<LoadedImageData> images, string? soundPath, bool isMultiplication)
        {
            if (!_isRunning)
            {
                if (!isMultiplication) _isBusy = false;
                return;
            }

            var settings = App.Settings.Current;
            double duration = settings.FlashDuration; // Default to manual duration setting

            // Play sound ONLY ONCE per flash event (not for hydra spawns) - only if audio enabled
            if (settings.FlashAudioEnabled && !_soundPlayingForCurrentFlash && !isMultiplication && !string.IsNullOrEmpty(soundPath) && File.Exists(soundPath))
            {
                try
                {
                    _soundPlayingForCurrentFlash = true;
                    duration = PlaySound(soundPath, settings.MasterVolume);
                    
                    // Audio ducking
                    if (settings.AudioDuckingEnabled)
                    {
                        App.Audio.Duck(settings.DuckingLevel);
                        
                        // Schedule unduck
                        var unduckDelay = (int)(duration * 1000) + 1500;
                        Task.Delay(unduckDelay).ContinueWith(_ =>
                        {
                            System.Windows.Application.Current.Dispatcher.BeginInvoke(() =>
                            {
                                App.Audio.Unduck();
                            });
                        });
                    }
                }
                catch (Exception ex)
                {
                    App.Logger.Debug("Could not play sound: {Error}", ex.Message);
                }
            }

            // Set virtual end time for fade control (only on initial flash, not hydra)
            if (!isMultiplication)
            {
                _virtualEndTime = DateTime.Now.AddSeconds(duration);
                
                // Schedule cleanup after sound ends
                var cleanupDelay = (int)(duration * 1000) + 1000;
                Task.Delay(cleanupDelay).ContinueWith(_ =>
                {
                    System.Windows.Application.Current.Dispatcher.BeginInvoke(() =>
                    {
                        ForceFlashCleanup();
                    });
                });
            }

            // Spawn windows
            for (int i = 0; i < images.Count; i++)
            {
                var imageData = images[i];
                var delayMs = isMultiplication ? i * 100 : i * 300;
                
                if (delayMs == 0)
                {
                    SpawnFlashWindow(imageData, settings);
                }
                else
                {
                    var capturedData = imageData;
                    Task.Delay(delayMs).ContinueWith(_ =>
                    {
                        System.Windows.Application.Current.Dispatcher.BeginInvoke(() =>
                        {
                            if (_isRunning)
                                SpawnFlashWindow(capturedData, settings);
                        });
                    });
                }
            }

            FlashDisplayed?.Invoke(this, EventArgs.Empty);
            
            if (!isMultiplication)
            {
                _isBusy = false;
            }
        }

        private void SpawnFlashWindow(LoadedImageData imageData, AppSettings settings)
        {
            if (!_isRunning) return;

            var geom = imageData.Geometry;
            
            // Avoid overlap with existing windows
            var finalX = geom.X;
            var finalY = geom.Y;
            var monitor = imageData.Monitor;
            
            for (int attempt = 0; attempt < 10; attempt++)
            {
                if (!IsOverlapping(finalX, finalY, geom.Width, geom.Height))
                    break;
                
                finalX = monitor.X + _random.Next(0, Math.Max(1, monitor.Width - geom.Width));
                finalY = monitor.Y + _random.Next(0, Math.Max(1, monitor.Height - geom.Height));
            }

            var window = new FlashWindow
            {
                Left = finalX,
                Top = finalY,
                Width = geom.Width,
                Height = geom.Height,
                Frames = imageData.Frames,
                FrameDelay = imageData.FrameDelay,
                StartTime = DateTime.Now,
                IsClickable = settings.FlashClickable,
                AllowsTransparency = true,
                WindowStyle = WindowStyle.None,
                Topmost = true,
                ShowInTaskbar = false,
                Background = System.Windows.Media.Brushes.Black,
                ResizeMode = ResizeMode.NoResize
            };

            // Create image control
            var image = new Image
            {
                Stretch = Stretch.Uniform,
                Source = imageData.Frames[0]
            };
            
            window.ImageControl = image;
            window.Content = image;
            window.Opacity = 0;

            // Click handler
            if (settings.FlashClickable)
            {
                window.Cursor = System.Windows.Input.Cursors.Hand;
                window.MouseLeftButtonDown += (s, e) => OnFlashClicked(window, settings);
            }
            else
            {
                window.Cursor = System.Windows.Input.Cursors.No;
                MakeClickThrough(window);
            }

            // Hide from Alt+Tab for ALL flash windows
            HideFromAltTab(window);

            window.Show();
            
            // Force topmost even over fullscreen apps
            ForceTopmost(window);
            
            lock (_lockObj)
            {
                _activeWindows.Add(window);
            }
            
            // Award XP for viewing
            var xpAmount = _soundPlayingForCurrentFlash ? 15 : 7;
            App.Progression.AddXP(xpAmount);
            
            // Track for achievement
            App.Achievements?.TrackFlashImage();
        }

        private void OnFlashClicked(FlashWindow window, AppSettings settings)
        {
            lock (_lockObj)
            {
                _activeWindows.Remove(window);
            }
            
            window.Close();
            FlashClicked?.Invoke(this, EventArgs.Empty);

            // Hydra mode: spawn 2 more when clicking (NO NEW AUDIO)
            if (settings.CorruptionMode && !_cleanupInProgress)
            {
                var maxHydra = Math.Min(settings.HydraLimit, 20);
                int currentCount;
                lock (_lockObj)
                {
                    currentCount = _activeWindows.Count;
                }

                if (currentCount + 1 < maxHydra)
                {
                    TriggerMultiplication(maxHydra, currentCount);
                }
            }
        }

        private async void TriggerMultiplication(int maxHydra, int currentCount)
        {
            if (!_isRunning) return;

            var spaceAvailable = maxHydra - currentCount;
            var numToSpawn = Math.Min(2, spaceAvailable);
            
            if (numToSpawn <= 0) return;

            var settings = App.Settings.Current;
            var images = GetNextImages(numToSpawn);
            if (images.Count == 0) return;

            var monitors = GetMonitors(settings.DualMonitorEnabled);
            var scale = settings.ImageScale / 100.0;

            var loadedImages = new List<LoadedImageData>();
            foreach (var imagePath in images)
            {
                var data = await LoadImageAsync(imagePath);
                if (data != null)
                {
                    var monitor = monitors[_random.Next(monitors.Count)];
                    var geometry = CalculateGeometry(data.Width, data.Height, monitor, scale);
                    data.Geometry = geometry;
                    data.Monitor = monitor;
                    loadedImages.Add(data);
                }
            }

            if (loadedImages.Count > 0)
            {
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    // Pass null for sound - NO AUDIO FOR HYDRA
                    ShowImages(loadedImages, null, true);
                });
            }
        }

        #endregion

        #region Heartbeat & Animation

        private void Heartbeat_Tick(object? sender, EventArgs e)
        {
            if (!_isRunning) return;

            var settings = App.Settings.Current;
            var maxAlpha = Math.Min(1.0, Math.Max(0.0, settings.FlashOpacity / 100.0));
            var showImages = DateTime.Now < _virtualEndTime;
            var targetAlpha = showImages ? maxAlpha : 0.0;

            List<FlashWindow> windowsCopy;
            lock (_lockObj)
            {
                windowsCopy = _activeWindows.ToList();
            }

            var toRemove = new List<FlashWindow>();

            foreach (var window in windowsCopy)
            {
                try
                {
                    if (!window.IsLoaded || !window.IsVisible)
                    {
                        toRemove.Add(window);
                        continue;
                    }

                    // Fade in/out
                    var currentAlpha = window.Opacity;
                    if (targetAlpha > currentAlpha)
                    {
                        window.Opacity = Math.Min(targetAlpha, currentAlpha + 0.08);
                    }
                    else if (targetAlpha < currentAlpha)
                    {
                        var newAlpha = Math.Max(0.0, currentAlpha - 0.08);
                        window.Opacity = newAlpha;
                        
                        if (newAlpha <= 0)
                        {
                            toRemove.Add(window);
                            continue;
                        }
                    }

                    // Animate GIF frames
                    if (window.Frames.Count > 1 && window.ImageControl != null)
                    {
                        var elapsed = DateTime.Now - window.StartTime;
                        var frameIndex = (int)(elapsed.TotalMilliseconds / window.FrameDelay.TotalMilliseconds) % window.Frames.Count;
                        
                        if (frameIndex != window.CurrentFrameIndex)
                        {
                            window.CurrentFrameIndex = frameIndex;
                            window.ImageControl.Source = window.Frames[frameIndex];
                        }
                    }
                }
                catch (Exception ex)
                {
                    App.Logger.Debug("Heartbeat error: {Error}", ex.Message);
                    toRemove.Add(window);
                }
            }

            // Clean up windows
            foreach (var window in toRemove)
            {
                try
                {
                    window.Close();
                }
                catch { }
                
                lock (_lockObj)
                {
                    _activeWindows.Remove(window);
                }
            }
        }

        private void ForceFlashCleanup()
        {
            if (!_isRunning) return;
            
            _virtualEndTime = DateTime.Now;
            _cleanupInProgress = true;
            _soundPlayingForCurrentFlash = false; // Reset for next flash
            
            // Re-enable after windows fade out
            Task.Delay(2000).ContinueWith(_ =>
            {
                _cleanupInProgress = false;
            });
        }

        #endregion

        #region Monitor Support

        private List<MonitorInfo> GetMonitors(bool dualMonitor)
        {
            var monitors = new List<MonitorInfo>();
            
            // Get DPI scale factor to convert physical pixels to WPF DIPs
            var dpiScale = GetDpiScale();
            
            try
            {
                foreach (var screen in Screen.AllScreens)
                {
                    // Convert from physical pixels to WPF device-independent pixels
                    monitors.Add(new MonitorInfo
                    {
                        X = (int)(screen.Bounds.X / dpiScale),
                        Y = (int)(screen.Bounds.Y / dpiScale),
                        Width = (int)(screen.Bounds.Width / dpiScale),
                        Height = (int)(screen.Bounds.Height / dpiScale),
                        IsPrimary = screen.Primary
                    });
                }
            }
            catch (Exception ex)
            {
                App.Logger.Debug("Could not enumerate monitors: {Error}", ex.Message);
            }

            if (monitors.Count == 0)
            {
                // SystemParameters already returns DIPs, so no conversion needed
                monitors.Add(new MonitorInfo
                {
                    X = 0,
                    Y = 0,
                    Width = (int)SystemParameters.PrimaryScreenWidth,
                    Height = (int)SystemParameters.PrimaryScreenHeight,
                    IsPrimary = true
                });
            }

            // If dual monitor is disabled, only use primary
            if (!dualMonitor)
            {
                var primary = monitors.FirstOrDefault(m => m.IsPrimary) ?? monitors[0];
                return new List<MonitorInfo> { primary };
            }

            return monitors;
        }
        
        private double GetDpiScale()
        {
            try
            {
                var mainWindow = System.Windows.Application.Current?.MainWindow;
                if (mainWindow != null)
                {
                    var source = PresentationSource.FromVisual(mainWindow);
                    if (source?.CompositionTarget != null)
                    {
                        return source.CompositionTarget.TransformToDevice.M11;
                    }
                }
            }
            catch { }
            
            // Fallback: try to get from system
            try
            {
                using (var g = System.Drawing.Graphics.FromHwnd(IntPtr.Zero))
                {
                    return g.DpiX / 96.0;
                }
            }
            catch { }
            
            return 1.0; // Default to no scaling
        }

        private ImageGeometry CalculateGeometry(int origWidth, int origHeight, MonitorInfo monitor, double scale)
        {
            // Base size is 40% of monitor dimensions (matching Python)
            var baseWidth = monitor.Width * 0.4;
            var baseHeight = monitor.Height * 0.4;
            
            // Calculate scale ratio to fit within base size while maintaining aspect ratio
            // Then multiply by user's scale setting (0.5 to 2.5)
            var ratio = Math.Min(baseWidth / origWidth, baseHeight / origHeight) * scale;
            
            var targetWidth = Math.Max(50, (int)(origWidth * ratio));
            var targetHeight = Math.Max(50, (int)(origHeight * ratio));

            // Random position within monitor bounds
            var maxX = Math.Max(1, monitor.Width - targetWidth);
            var maxY = Math.Max(1, monitor.Height - targetHeight);
            
            var x = monitor.X + _random.Next(0, maxX);
            var y = monitor.Y + _random.Next(0, maxY);

            return new ImageGeometry
            {
                X = x,
                Y = y,
                Width = targetWidth,
                Height = targetHeight
            };
        }

        private bool IsOverlapping(int x, int y, int w, int h)
        {
            lock (_lockObj)
            {
                foreach (var window in _activeWindows)
                {
                    try
                    {
                        var wx = (int)window.Left;
                        var wy = (int)window.Top;
                        var ww = (int)window.Width;
                        var wh = (int)window.Height;

                        var dx = Math.Min(x + w, wx + ww) - Math.Max(x, wx);
                        var dy = Math.Min(y + h, wy + wh) - Math.Max(y, wy);

                        if (dx >= 0 && dy >= 0)
                        {
                            var overlapArea = dx * dy;
                            var windowArea = w * h;
                            if (overlapArea > windowArea * 0.3)
                                return true;
                        }
                    }
                    catch { }
                }
            }
            return false;
        }

        #endregion

        #region Media Queue

        private List<string> GetNextImages(int count)
        {
            lock (_lockObj)
            {
                if (_imageQueue.Count == 0)
                {
                    var files = GetMediaFiles(_imagesPath, new[] { ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp" });
                    if (files.Count == 0) return new List<string>();

                    // Performance: Shuffle and enqueue all at once
                    _imageQueue = new Queue<string>(files.OrderBy(_ => _random.Next()));
                }

                var result = new List<string>(count);
                for (int i = 0; i < count && _imageQueue.Count > 0; i++)
                {
                    result.Add(_imageQueue.Dequeue()); // Performance: O(1) instead of O(n)
                }
                return result;
            }
        }

        private string? GetNextSound()
        {
            lock (_lockObj)
            {
                if (_soundQueue.Count == 0)
                {
                    var files = GetMediaFiles(_soundsPath, new[] { ".mp3", ".wav", ".ogg" });
                    if (files.Count == 0) return null;

                    // Performance: Shuffle and enqueue all at once
                    _soundQueue = new Queue<string>(files.OrderBy(_ => _random.Next()));
                }

                return _soundQueue.Count > 0 ? _soundQueue.Dequeue() : null; // Performance: O(1) instead of O(n)
            }
        }

        private List<string> GetMediaFiles(string folder, string[] extensions)
        {
            if (!Directory.Exists(folder)) return new List<string>();

            // Performance: Create cache key from folder + extensions
            var cacheKey = $"{folder}|{string.Join(",", extensions)}";

            lock (_cacheLock)
            {
                // Check if we have a valid cached result
                if (_fileListCache.TryGetValue(cacheKey, out var cached))
                {
                    var age = (DateTime.UtcNow - cached.lastScan).TotalSeconds;
                    if (age < CACHE_EXPIRY_SECONDS)
                    {
                        return new List<string>(cached.files);  // Return copy to prevent modification
                    }
                }
            }

            // Scan directory (cache miss or expired)
            var files = new List<string>();
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;

            foreach (var ext in extensions)
            {
                foreach (var file in Directory.GetFiles(folder, $"*{ext}", SearchOption.TopDirectoryOnly))
                {
                    // Security: Validate path is within allowed directory
                    if (SecurityHelper.IsPathSafe(file, baseDir))
                    {
                        // Security: Sanitize filename to prevent path traversal
                        var fileName = SecurityHelper.SanitizeFilename(Path.GetFileName(file));
                        if (!string.IsNullOrEmpty(fileName))
                        {
                            files.Add(file);
                        }
                    }
                    else
                    {
                        App.Logger?.Warning("Blocked file outside allowed directory: {Path}", file);
                    }
                }
            }

            // Update cache
            lock (_cacheLock)
            {
                _fileListCache[cacheKey] = (new List<string>(files), DateTime.UtcNow);
            }

            return files;
        }

        /// <summary>
        /// Clear the file list cache (called when assets are reloaded)
        /// </summary>
        private void ClearFileCache()
        {
            lock (_cacheLock)
            {
                _fileListCache.Clear();
            }
        }

        #endregion

        #region Audio

        private double PlaySound(string path, int volumePercent)
        {
            StopCurrentSound();
            
            try
            {
                _currentAudioFile = new AudioFileReader(path);
                _currentSound = new WaveOutEvent();
                
                // Apply volume curve (gentler, minimum 5%)
                var volume = volumePercent / 100.0f;
                var curvedVolume = Math.Max(0.05f, (float)Math.Pow(volume, 1.5));
                _currentAudioFile.Volume = curvedVolume;
                
                _currentSound.Init(_currentAudioFile);
                _currentSound.Play();
                
                return _currentAudioFile.TotalTime.TotalSeconds;
            }
            catch (Exception ex)
            {
                App.Logger.Debug("Could not play sound {Path}: {Error}", path, ex.Message);
                return 5.0;
            }
        }

        private void StopCurrentSound()
        {
            try
            {
                _currentSound?.Stop();
                _currentSound?.Dispose();
                _currentAudioFile?.Dispose();
            }
            catch { }
            
            _currentSound = null;
            _currentAudioFile = null;
        }

        #endregion

        #region Window Management

        private void MakeClickThrough(Window window)
        {
            try
            {
                // Need to do this after window is shown
                window.SourceInitialized += (s, e) =>
                {
                    var hwnd = new System.Windows.Interop.WindowInteropHelper(window).Handle;
                    var extendedStyle = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
                    NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE,
                        extendedStyle | NativeMethods.WS_EX_TRANSPARENT | NativeMethods.WS_EX_LAYERED);
                };
            }
            catch (Exception ex)
            {
                App.Logger.Debug("Could not make window click-through: {Error}", ex.Message);
            }
        }

        private void HideFromAltTab(Window window)
        {
            try
            {
                window.SourceInitialized += (s, e) =>
                {
                    var hwnd = new System.Windows.Interop.WindowInteropHelper(window).Handle;
                    var extendedStyle = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
                    NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE,
                        extendedStyle | NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE);
                };
            }
            catch (Exception ex)
            {
                App.Logger.Debug("Could not hide window from Alt+Tab: {Error}", ex.Message);
            }
        }

        private void ForceTopmost(Window window)
        {
            try
            {
                var hwnd = new System.Windows.Interop.WindowInteropHelper(window).Handle;
                NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0, 
                    NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
            }
            catch { }
        }

        private void CloseAllWindows()
        {
            List<FlashWindow> windowsCopy;
            lock (_lockObj)
            {
                windowsCopy = _activeWindows.ToList();
                _activeWindows.Clear();
            }

            foreach (var window in windowsCopy)
            {
                try
                {
                    window.Close();
                }
                catch { }
            }
            
            _soundPlayingForCurrentFlash = false;
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            Stop();
            _cancellationSource?.Dispose();
            StopCurrentSound();
        }

        #endregion
    }

    #region Supporting Classes

    internal class FlashWindow : Window
    {
        public List<BitmapSource> Frames { get; set; } = new();
        public TimeSpan FrameDelay { get; set; }
        public DateTime StartTime { get; set; }
        public int CurrentFrameIndex { get; set; }
        public Image? ImageControl { get; set; }
        public bool IsClickable { get; set; }
    }

    internal class LoadedImageData
    {
        public string FilePath { get; set; } = "";
        public List<BitmapSource> Frames { get; } = new();
        public int Width { get; set; }
        public int Height { get; set; }
        public TimeSpan FrameDelay { get; set; }
        public ImageGeometry Geometry { get; set; } = new();
        public MonitorInfo Monitor { get; set; } = new();
    }

    internal class ImageGeometry
    {
        public int X { get; set; }
        public int Y { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
    }

    internal class MonitorInfo
    {
        public int X { get; set; }
        public int Y { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public bool IsPrimary { get; set; }
    }

    internal static class NativeMethods
    {
        public const int GWL_EXSTYLE = -20;
        public const int WS_EX_TRANSPARENT = 0x00000020;
        public const int WS_EX_LAYERED = 0x00080000;
        public const int WS_EX_TOOLWINDOW = 0x00000080;
        public const int WS_EX_NOACTIVATE = 0x08000000;
        
        public const uint SWP_NOMOVE = 0x0002;
        public const uint SWP_NOSIZE = 0x0001;
        public const uint SWP_NOACTIVATE = 0x0010;
        public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern int GetWindowLong(IntPtr hwnd, int index);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
    }

    #endregion
}
