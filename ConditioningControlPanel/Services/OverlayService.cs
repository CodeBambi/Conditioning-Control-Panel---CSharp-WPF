using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using System.ComponentModel;

namespace ConditioningControlPanel.Services;

/// <summary>
/// Service that manages screen overlays: Pink Filter and Spiral
/// </summary>
public class OverlayService : IDisposable
{
    private readonly List<Window> _pinkFilterWindows = new();

    public OverlayService()
    {
        // Subscribe to settings changes if App.Settings.Current is available
        if (App.Settings?.Current != null)
        {
            App.Settings.Current.PropertyChanged += CurrentSettings_PropertyChanged;
        }
    }
    private readonly List<Window> _spiralWindows = new();
    private readonly List<MediaElement> _spiralMediaElements = new();
    private readonly List<Window> _brainDrainBlurWindows = new();
    private bool _isRunning;
    private DispatcherTimer? _updateTimer;
    private DispatcherTimer? _gifLoopTimer;
    private bool _isDisposed;
    private bool _isGifSpiral;
    private string _spiralPath = "";
    private Dictionary<MediaElement, DateTime> _mediaStartTimes = new();
    private const double GIF_LOOP_INTERVAL_SECONDS = 4.0; // Restart GIFs every 4 seconds

    public bool IsRunning => _isRunning;

    // Legacy P/Invoke declarations (kept for compatibility)
    private const int SRCCOPY = 0x00CC0020;
    
    [System.Runtime.InteropServices.DllImport("gdi32.dll")]
    private static extern bool BitBlt(IntPtr hdcDest, int xDest, int yDest, int wDest, int hDest, IntPtr hdcSrc, int xSrc, int ySrc, int dwRop);
    
    [System.Runtime.InteropServices.DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int nWidth, int nHeight);
    
    [System.Runtime.InteropServices.DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    
    [System.Runtime.InteropServices.DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr hdc);
    
    [System.Runtime.InteropServices.DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);
    
    [System.Runtime.InteropServices.DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hObject);
    
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hwnd);
    
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);

    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS margins);

    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct MARGINS
    {
        public int Left, Right, Top, Bottom;
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct WindowCompositionAttributeData
    {
        public int Attribute;
        public IntPtr Data;
        public int SizeOfData;
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        public int AccentState;
        public int AccentFlags;
        public uint GradientColor;
        public int AnimationId;
    }

    private const int ACCENT_ENABLE_BLURBEHIND = 3;
    private const int ACCENT_ENABLE_ACRYLICBLURBEHIND = 4;
    private const int WCA_ACCENT_POLICY = 19;

            private string GetSpiralPath()
            {
                var settings = App.Settings.Current;
                
                if (!string.IsNullOrEmpty(settings.SpiralPath) && File.Exists(settings.SpiralPath))
                {
                    return settings.SpiralPath;
                }
                
                return "pack://application:,,,/Resources/spiral.gif";
            }
    public void Start()
    {
        if (_isRunning) return;
        _isRunning = true;

        Application.Current.Dispatcher.Invoke(() =>
        {
            var settings = App.Settings.Current;

            if (settings.PlayerLevel < 10)
            {
                App.Logger?.Information("OverlayService: Level {Level} is below 10, overlays not available", settings.PlayerLevel);
                return;
            }

            if (settings.PinkFilterEnabled)
            {
                StartPinkFilter();
            }

            var spiralPath = GetSpiralPath();
            if (settings.SpiralEnabled && !string.IsNullOrEmpty(spiralPath))
            {
                _spiralPath = spiralPath;
                StartSpiral();
            }

            if (settings.BrainDrainEnabled && settings.PlayerLevel >= 70)
            {
                StartBrainDrainBlur((int)settings.BrainDrainIntensity);
            }

            _updateTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _updateTimer.Tick += UpdateOverlays;
            _updateTimer.Start();
        });

        App.Logger?.Information("OverlayService started");
    }

    public void Stop()
    {
        _isRunning = false;

        try
        {
            _updateTimer?.Stop();
            _updateTimer = null;

            StopPinkFilter();
            StopSpiral();
            StopBrainDrainBlur();
        }
        catch (Exception ex)
        {
            App.Logger?.Error(ex, "Error during OverlayService Stop");
        }

        App.Logger?.Information("OverlayService stopped");
    }

    public void RefreshOverlays()
    {
        if (!_isRunning) return;

        Application.Current.Dispatcher.Invoke(() =>
        {
            var settings = App.Settings.Current;

            if (settings.PinkFilterEnabled && settings.PlayerLevel >= 10)
            {
                if (_pinkFilterWindows.Count == 0)
                    StartPinkFilter();
                else
                    UpdatePinkFilterOpacity();
            }
            else
            {
                StopPinkFilter();
            }

            var spiralPath = GetSpiralPath();
            if (settings.SpiralEnabled && settings.PlayerLevel >= 10 && !string.IsNullOrEmpty(spiralPath))
            {
                _spiralPath = spiralPath;
                if (_spiralWindows.Count == 0)
                    StartSpiral();
                else
                    UpdateSpiralOpacity();
            }
            else
            {
                StopSpiral();
            }

            // Handle Brain Drain via its dedicated refresh state method
            RefreshBrainDrainState();
        });

        App.Logger?.Debug("Overlays refreshed - Pink: {Pink}, Spiral: {Spiral}, BrainDrain: {BrainDrain}",
            _pinkFilterWindows.Count > 0, _spiralWindows.Count > 0, _brainDrainBlurWindows.Count > 0);
    }

    /// <summary>
    /// Restart all overlays when dual monitor setting changes.
    /// Windows need to be recreated to match the new monitor setup.
    /// </summary>
    public void RefreshForDualMonitorChange()
    {
        if (!_isRunning) return;

        Application.Current.Dispatcher.Invoke(() =>
        {
            var settings = App.Settings.Current;

            // Stop and restart pink filter if enabled
            if (settings.PinkFilterEnabled && settings.PlayerLevel >= 10)
            {
                StopPinkFilter();
                StartPinkFilter();
            }

            // Stop and restart spiral if enabled
            var spiralPath = GetSpiralPath();
            if (settings.SpiralEnabled && settings.PlayerLevel >= 10 && !string.IsNullOrEmpty(spiralPath))
            {
                StopSpiral();
                _spiralPath = spiralPath;
                StartSpiral();
            }

            // Stop and restart brain drain if enabled
            if (settings.BrainDrainEnabled && settings.PlayerLevel >= 70)
            {
                StopBrainDrainBlur();
                StartBrainDrainBlur((int)settings.BrainDrainIntensity);
            }
        });

        App.Logger?.Information("Overlays refreshed for dual monitor change - DualMonitor: {Enabled}",
            App.Settings.Current.DualMonitorEnabled);
    }

    private void UpdateOverlays(object? sender, EventArgs e)
    {
        var settings = App.Settings.Current;

        if (settings.PlayerLevel < 10)
        {
            StopPinkFilter();
            StopSpiral();
            StopBrainDrainBlur();
            return;
        }

        if (settings.PinkFilterEnabled && _pinkFilterWindows.Count == 0)
        {
            StartPinkFilter();
        }
        else if (!settings.PinkFilterEnabled && _pinkFilterWindows.Count > 0)
        {
            StopPinkFilter();
        }
        else if (_pinkFilterWindows.Count > 0)
        {
            UpdatePinkFilterOpacity();
        }

        var spiralPath = GetSpiralPath();
        if (settings.SpiralEnabled && !string.IsNullOrEmpty(spiralPath) && _spiralWindows.Count == 0)
        {
            _spiralPath = spiralPath;
            StartSpiral();
        }
        else if (!settings.SpiralEnabled && _spiralWindows.Count > 0)
        {
            StopSpiral();
        }
        else if (_spiralWindows.Count > 0)
        {
            UpdateSpiralOpacity();
        }
    }

    #region Pink Filter

    private void StartPinkFilter()
    {
        if (_pinkFilterWindows.Count > 0) return;

        try
        {
            var settings = App.Settings.Current;
            
            var screens = settings.DualMonitorEnabled 
                ? App.GetAllScreensCached() 
                : new[] { System.Windows.Forms.Screen.PrimaryScreen! };

            foreach (var screen in screens)
            {
                var window = CreatePinkFilterForScreen(screen, settings.PinkFilterOpacity);
                if (window != null)
                {
                    _pinkFilterWindows.Add(window);
                }
            }

            App.Logger?.Debug("Pink filter started on {Count} screens at opacity {Opacity}%", 
                _pinkFilterWindows.Count, settings.PinkFilterOpacity);
        }
        catch (Exception ex)
        {
            App.Logger?.Error("Failed to start pink filter: {Error}", ex.Message);
        }
    }

    private Window? CreatePinkFilterForScreen(System.Windows.Forms.Screen screen, int opacity)
    {
        try
        {
            // Get WPF-compatible screen bounds for initial window creation
            var wpfBounds = GetWpfScreenBounds(screen);

            // Linear opacity (no exponential curve)
            var actualOpacity = opacity / 100.0;

            var pinkOverlay = new Border
            {
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(
                                    (byte)(actualOpacity * 255), 255, 105, 180)),
                Opacity = 1.0
            };

            // Create window - initial position is approximate, will be corrected via SetWindowPos
            var window = new Window
            {
                WindowStyle = System.Windows.WindowStyle.None,
                AllowsTransparency = true,
                Background = System.Windows.Media.Brushes.Transparent,
                Topmost = true,
                ShowInTaskbar = false,
                ShowActivated = false,
                Focusable = false,
                IsHitTestVisible = false,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = wpfBounds.Left,
                Top = wpfBounds.Top,
                Width = wpfBounds.Width,
                Height = wpfBounds.Height,
                Content = pinkOverlay
            };

            // Capture screen reference for use in handler
            var targetScreen = screen;

            window.SourceInitialized += (s, e) =>
            {
                var hwnd = new System.Windows.Interop.WindowInteropHelper(window).Handle;
                var exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
                SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TRANSPARENT | WS_EX_LAYERED);

                // Use SetWindowPos with physical pixel coordinates for exact positioning
                // This bypasses WPF's DPI virtualization which causes offset issues on mixed-DPI setups
                PositionWindowOnScreen(window, targetScreen);
            };

            window.Show();

            App.Logger?.Debug("Pink filter created for {Screen}", screen.DeviceName);

            return window;
        }
        catch (Exception ex)
        {
            App.Logger?.Error("Failed to create pink filter for screen: {Error}", ex.Message);
            return null;
        }
    }

    private void StopPinkFilter()
    {
        foreach (var window in _pinkFilterWindows.ToList())
        {
            try { window.Close(); }
            catch (Exception ex)
            {
                App.Logger?.Debug("Failed to close pink filter window: {Error}", ex.Message);
            }
        }
        _pinkFilterWindows.Clear();
        App.Logger?.Debug("Pink filter stopped");
    }

    private void UpdatePinkFilterOpacity()
    {
        var actualOpacity = App.Settings.Current.PinkFilterOpacity / 100.0;
        foreach (var window in _pinkFilterWindows)
        {
            if (window.Content is Border border)
            {
                if (border.Background is System.Windows.Media.SolidColorBrush brush)
                {
                    brush.Color = System.Windows.Media.Color.FromArgb((byte)(actualOpacity * 255), 255, 105, 180);
                }
            }
        }
    }

    #endregion

    #region Spiral

    private void StartSpiral()
    {
        if (_spiralWindows.Count > 0) return;

        try
        {
            var settings = App.Settings.Current;

            var screens = settings.DualMonitorEnabled 
                ? App.GetAllScreensCached() 
                : new[] { System.Windows.Forms.Screen.PrimaryScreen! };

            _isGifSpiral = _spiralPath.EndsWith(".gif", StringComparison.OrdinalIgnoreCase);

            foreach (var screen in screens)
            {
                var (window, media) = CreateSpiralForScreen(screen, settings.SpiralOpacity);
                if (window != null)
                {
                    _spiralWindows.Add(window);
                    if (media != null)
                    {
                        _spiralMediaElements.Add(media);
                    }
                }
            }

            if (_isGifSpiral && _spiralMediaElements.Count > 0)
            {
                _gifLoopTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
                _gifLoopTimer.Tick += GifLoopTimer_Tick;
                _gifLoopTimer.Start();
            }

            App.Logger?.Debug("Spiral started on {Count} screens at opacity {Opacity}%", 
                _spiralWindows.Count, settings.SpiralOpacity);
        }
        catch (Exception ex)
        {
            App.Logger?.Error("Failed to start spiral: {Error}", ex.Message);
        }
    }

    private void GifLoopTimer_Tick(object? sender, EventArgs e)
    {
        foreach (var media in _spiralMediaElements)
        {
            try
            {
                // For video files with known duration - use position-based looping
                if (media.NaturalDuration.HasTimeSpan)
                {
                    var currentPos = media.Position;
                    if (currentPos >= media.NaturalDuration.TimeSpan - TimeSpan.FromMilliseconds(100))
                    {
                        media.Position = TimeSpan.Zero;
                        media.Play();
                        _mediaStartTimes[media] = DateTime.Now;
                    }
                    continue;
                }

                // For GIFs - use time-based restart (WPF Position doesn't work for GIFs)
                if (!_mediaStartTimes.TryGetValue(media, out var startTime))
                {
                    _mediaStartTimes[media] = DateTime.Now;
                    continue;
                }

                var elapsed = (DateTime.Now - startTime).TotalSeconds;
                if (elapsed >= GIF_LOOP_INTERVAL_SECONDS)
                {
                    // Restart the GIF by seeking to start
                    media.Position = TimeSpan.Zero;
                    media.Play();
                    _mediaStartTimes[media] = DateTime.Now;
                }
            }
            catch
            {
                // Ignore errors during tick
            }
        }
    }

    private (Window? window, MediaElement? media) CreateSpiralForScreen(System.Windows.Forms.Screen screen, int opacity)
    {
        try
        {
            // Get WPF-compatible screen bounds for initial window creation
            var wpfBounds = GetWpfScreenBounds(screen);

            // Very subtle opacity - 90% reduction
            var actualOpacity = (opacity / 100.0) * 0.1;

            var mediaElement = new MediaElement
            {
                Source = new Uri(_spiralPath),
                LoadedBehavior = MediaState.Play,
                UnloadedBehavior = MediaState.Manual,
                Stretch = Stretch.UniformToFill,
                Opacity = actualOpacity,
                IsMuted = true,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            mediaElement.MediaEnded += (s, e) =>
            {
                mediaElement.Position = TimeSpan.Zero;
                mediaElement.Play();
            };

            // Use a Viewbox to ensure proper centering and scaling on all monitors
            var viewbox = new Viewbox
            {
                Stretch = Stretch.UniformToFill,
                StretchDirection = StretchDirection.Both,
                Child = mediaElement
            };

            // Create window - initial position is approximate, will be corrected via SetWindowPos
            var window = new Window
            {
                WindowStyle = System.Windows.WindowStyle.None,
                AllowsTransparency = true,
                Background = System.Windows.Media.Brushes.Transparent,
                Topmost = true,
                ShowInTaskbar = false,
                ShowActivated = false,
                Focusable = false,
                IsHitTestVisible = false,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = wpfBounds.Left,
                Top = wpfBounds.Top,
                Width = wpfBounds.Width,
                Height = wpfBounds.Height,
                Content = viewbox
            };

            // Capture screen reference for use in handler
            var targetScreen = screen;

            window.SourceInitialized += (s, e) =>
            {
                var hwnd = new System.Windows.Interop.WindowInteropHelper(window).Handle;
                var exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
                SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TRANSPARENT | WS_EX_LAYERED);

                // Use SetWindowPos with physical pixel coordinates for exact positioning
                // This bypasses WPF's DPI virtualization which causes offset issues on mixed-DPI setups
                PositionWindowOnScreen(window, targetScreen);
            };

            window.Show();

            App.Logger?.Debug("Spiral created for {Screen}", screen.DeviceName);

            return (window, mediaElement);
        }
        catch (Exception ex)
        {
            App.Logger?.Error("Failed to create spiral for screen: {Error}", ex.Message);
            return (null, null);
        }
    }

    private void StopSpiral()
    {
        _gifLoopTimer?.Stop();
        _gifLoopTimer = null;

        foreach (var media in _spiralMediaElements.ToList())
        {
            try { media.Stop(); media.Close(); }
            catch (Exception ex)
            {
                App.Logger?.Debug("Failed to stop spiral media: {Error}", ex.Message);
            }
        }
        _spiralMediaElements.Clear();
        _mediaStartTimes.Clear();

        foreach (var window in _spiralWindows.ToList())
        {
            try { window.Close(); }
            catch (Exception ex)
            {
                App.Logger?.Debug("Failed to close spiral window: {Error}", ex.Message);
            }
        }
        _spiralWindows.Clear();
        App.Logger?.Debug("Spiral stopped");
    }

    private void UpdateSpiralOpacity()
    {
        // Very subtle opacity - 90% reduction
        var opacity = (App.Settings.Current.SpiralOpacity / 100.0) * 0.1;
        foreach (var media in _spiralMediaElements)
        {
            media.Opacity = opacity;
        }
    }

    #endregion

    #region Brain Drain Blur (Screen Capture - Optimized)

    private readonly Dictionary<Window, System.Windows.Controls.Image> _brainDrainImages = new();
    private readonly Dictionary<Window, System.Windows.Forms.Screen> _brainDrainScreens = new();
    private DispatcherTimer? _brainDrainCaptureTimer;
    private int _currentBrainDrainIntensity = 50;
    private System.Drawing.Bitmap? _captureBitmap;
    private IntPtr _captureHdc;
    private IntPtr _captureMemDc;
    private IntPtr _captureHBitmap;

    public void StartBrainDrainBlur(int intensity)
    {
        if (_brainDrainBlurWindows.Count > 0) return;

        _currentBrainDrainIntensity = intensity;

        Application.Current.Dispatcher.Invoke(() =>
        {
            try
            {
                var settings = App.Settings.Current;
                var screens = settings.DualMonitorEnabled
                    ? App.GetAllScreensCached()
                    : new[] { System.Windows.Forms.Screen.PrimaryScreen! };

                foreach (var screen in screens)
                {
                    var window = CreateBrainDrainWindow(screen, intensity);
                    if (window != null)
                    {
                        _brainDrainBlurWindows.Add(window);
                    }
                }

                // Refresh rate based on setting:
                // Normal: 30 FPS (balanced)
                // High Refresh: 60 FPS (smoother, more CPU)
                // For 144Hz monitors, even 60 FPS looks acceptable
                int fps = settings.BrainDrainHighRefresh ? 60 : 30;
                double intervalMs = 1000.0 / fps;

                _brainDrainCaptureTimer = new DispatcherTimer(DispatcherPriority.Render)
                {
                    Interval = TimeSpan.FromMilliseconds(intervalMs)
                };
                _brainDrainCaptureTimer.Tick += BrainDrainCaptureTick;
                _brainDrainCaptureTimer.Start();

                App.Logger?.Information("Brain Drain started on {Count} screens at {Fps} FPS, intensity {Intensity}%",
                    _brainDrainBlurWindows.Count, fps, intensity);
            }
            catch (Exception ex)
            {
                App.Logger?.Error("Failed to start Brain Drain: {Error}", ex.Message);
            }
        });
    }

    public void StopBrainDrainBlur()
    {
        try
        {
            _brainDrainCaptureTimer?.Stop();
            _brainDrainCaptureTimer = null;

            // Clean up GDI resources
            CleanupCaptureResources();

            var windowsToClose = _brainDrainBlurWindows.ToList();
            foreach (var window in windowsToClose)
            {
                try
                {
                    if (Application.Current?.Dispatcher?.CheckAccess() == true)
                        window.Close();
                    else if (Application.Current?.Dispatcher != null)
                        Application.Current.Dispatcher.Invoke(() => window.Close());
                    else
                        window.Close();
                }
                catch (Exception ex)
                {
                    App.Logger?.Debug("Failed to close brain drain window: {Error}", ex.Message);
                }
            }
            _brainDrainBlurWindows.Clear();
            _brainDrainImages.Clear();
            _brainDrainScreens.Clear();

            App.Logger?.Debug("Brain Drain stopped");
        }
        catch (Exception ex)
        {
            App.Logger?.Error(ex, "Error stopping Brain Drain blur");
        }
    }

    public void UpdateBrainDrainBlurOpacity(int intensity)
    {
        _currentBrainDrainIntensity = intensity;
        double blurRadius = intensity * 0.4; // Slightly lower multiplier for performance

        Application.Current.Dispatcher.Invoke(() =>
        {
            foreach (var img in _brainDrainImages.Values)
            {
                if (img.Effect is System.Windows.Media.Effects.BlurEffect blur)
                {
                    blur.Radius = blurRadius;
                }
            }
        });
    }

    private void BrainDrainCaptureTick(object? sender, EventArgs e)
    {
        if (_brainDrainImages.Count == 0)
        {
            _brainDrainCaptureTimer?.Stop();
            return;
        }

        foreach (var kvp in _brainDrainImages)
        {
            var window = kvp.Key;
            var image = kvp.Value;

            if (_brainDrainScreens.TryGetValue(window, out var screen))
            {
                var capture = CaptureScreenOptimized(screen);
                if (capture != null)
                {
                    image.Source = capture;
                }
            }
        }
    }

    private System.Windows.Media.Imaging.BitmapSource? CaptureScreenOptimized(System.Windows.Forms.Screen screen)
    {
        IntPtr hdcSrc = IntPtr.Zero;
        IntPtr hdcDest = IntPtr.Zero;
        IntPtr hBitmap = IntPtr.Zero;
        IntPtr hOld = IntPtr.Zero;

        try
        {
            var bounds = screen.Bounds;

            // Get screen DC
            hdcSrc = GetDC(IntPtr.Zero);
            if (hdcSrc == IntPtr.Zero) return null;

            hdcDest = CreateCompatibleDC(hdcSrc);
            if (hdcDest == IntPtr.Zero) return null;

            hBitmap = CreateCompatibleBitmap(hdcSrc, bounds.Width, bounds.Height);
            if (hBitmap == IntPtr.Zero) return null;

            hOld = SelectObject(hdcDest, hBitmap);

            // Copy screen content
            BitBlt(hdcDest, 0, 0, bounds.Width, bounds.Height,
                   hdcSrc, bounds.X, bounds.Y, SRCCOPY);

            // Restore selection before creating bitmap source
            if (hOld != IntPtr.Zero)
            {
                SelectObject(hdcDest, hOld);
                hOld = IntPtr.Zero;
            }

            // Convert to WPF BitmapSource
            var bitmapSource = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                hBitmap, IntPtr.Zero, System.Windows.Int32Rect.Empty,
                System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());

            bitmapSource.Freeze();
            return bitmapSource;
        }
        catch (Exception ex)
        {
            App.Logger?.Debug("Screen capture failed: {Error}", ex.Message);
            return null;
        }
        finally
        {
            // Always cleanup GDI handles in reverse order of creation
            if (hOld != IntPtr.Zero && hdcDest != IntPtr.Zero)
                SelectObject(hdcDest, hOld);
            if (hBitmap != IntPtr.Zero)
                DeleteObject(hBitmap);
            if (hdcDest != IntPtr.Zero)
                DeleteDC(hdcDest);
            if (hdcSrc != IntPtr.Zero)
                ReleaseDC(IntPtr.Zero, hdcSrc);
        }
    }

    private void CleanupCaptureResources()
    {
        try
        {
            if (_captureHBitmap != IntPtr.Zero) { DeleteObject(_captureHBitmap); _captureHBitmap = IntPtr.Zero; }
            if (_captureMemDc != IntPtr.Zero) { DeleteDC(_captureMemDc); _captureMemDc = IntPtr.Zero; }
            if (_captureHdc != IntPtr.Zero) { ReleaseDC(IntPtr.Zero, _captureHdc); _captureHdc = IntPtr.Zero; }
            _captureBitmap?.Dispose();
            _captureBitmap = null;
        }
        catch (Exception ex)
        {
            App.Logger?.Debug("Error cleaning up capture resources: {Error}", ex.Message);
        }
    }

    private Window? CreateBrainDrainWindow(System.Windows.Forms.Screen screen, int intensity)
    {
        try
        {
            var wpfBounds = GetWpfScreenBounds(screen);
            double blurRadius = intensity * 0.4;

            var image = new System.Windows.Controls.Image
            {
                Stretch = Stretch.Fill,
                Effect = new System.Windows.Media.Effects.BlurEffect
                {
                    Radius = blurRadius,
                    KernelType = System.Windows.Media.Effects.KernelType.Gaussian,
                    RenderingBias = System.Windows.Media.Effects.RenderingBias.Performance
                }
            };

            // Create window - initial position is approximate, will be corrected via SetWindowPos
            var window = new Window
            {
                WindowStyle = System.Windows.WindowStyle.None,
                AllowsTransparency = true,
                Background = System.Windows.Media.Brushes.Transparent,
                Topmost = true,
                ShowInTaskbar = false,
                ShowActivated = false,
                Focusable = false,
                IsHitTestVisible = false,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = wpfBounds.Left,
                Top = wpfBounds.Top,
                Width = wpfBounds.Width,
                Height = wpfBounds.Height,
                Content = image
            };

            // Capture screen reference for use in handler
            var targetScreen = screen;

            window.SourceInitialized += (s, e) =>
            {
                var hwnd = new System.Windows.Interop.WindowInteropHelper(window).Handle;
                var exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
                SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TRANSPARENT | WS_EX_LAYERED);

                // Exclude from capture so we don't capture ourselves
                SetWindowDisplayAffinity(hwnd, WDA_EXCLUDEFROMCAPTURE);

                // Use SetWindowPos with physical pixel coordinates for exact positioning
                // This bypasses WPF's DPI virtualization which causes offset issues on mixed-DPI setups
                PositionWindowOnScreen(window, targetScreen);
            };

            window.Show();

            _brainDrainImages[window] = image;
            _brainDrainScreens[window] = screen;

            App.Logger?.Debug("Brain Drain created for {Screen}", screen.DeviceName);

            return window;
        }
        catch (Exception ex)
        {
            App.Logger?.Error("Failed to create Brain Drain window: {Error}", ex.Message);
            return null;
        }
    }

    #endregion

    #region Helpers

    /// <summary>
    /// Represents screen bounds - can be in physical pixels or WPF logical units
    /// </summary>
    private struct WpfScreenBounds
    {
        public double Left;
        public double Top;
        public double Width;
        public double Height;
    }

    /// <summary>
    /// Represents screen bounds in physical pixels (for use with SetWindowPos)
    /// </summary>
    private struct PhysicalScreenBounds
    {
        public int Left;
        public int Top;
        public int Width;
        public int Height;
    }

    /// <summary>
    /// Gets the actual physical pixel bounds of a monitor using Win32 APIs.
    /// This is the most reliable method for multi-monitor setups with different DPI.
    /// </summary>
    private PhysicalScreenBounds GetPhysicalScreenBounds(System.Windows.Forms.Screen screen)
    {
        try
        {
            // Get monitor handle from a point inside the screen
            var point = new POINT { X = screen.Bounds.X + screen.Bounds.Width / 2, Y = screen.Bounds.Y + screen.Bounds.Height / 2 };
            var hMonitor = MonitorFromPoint(point, 2); // MONITOR_DEFAULTTONEAREST

            if (hMonitor != IntPtr.Zero)
            {
                var monitorInfo = new MONITORINFO();
                monitorInfo.cbSize = System.Runtime.InteropServices.Marshal.SizeOf(typeof(MONITORINFO));

                if (GetMonitorInfo(hMonitor, ref monitorInfo))
                {
                    var bounds = new PhysicalScreenBounds
                    {
                        Left = monitorInfo.rcMonitor.Left,
                        Top = monitorInfo.rcMonitor.Top,
                        Width = monitorInfo.rcMonitor.Right - monitorInfo.rcMonitor.Left,
                        Height = monitorInfo.rcMonitor.Bottom - monitorInfo.rcMonitor.Top
                    };

                    App.Logger?.Debug("Screen {Name}: Physical bounds from Win32 = ({X},{Y},{W}x{H})",
                        screen.DeviceName, bounds.Left, bounds.Top, bounds.Width, bounds.Height);

                    return bounds;
                }
            }
        }
        catch (Exception ex)
        {
            App.Logger?.Warning("Failed to get physical screen bounds via Win32: {Error}", ex.Message);
        }

        // Fallback to Screen.Bounds (may be virtualized on mixed-DPI setups)
        App.Logger?.Debug("Screen {Name}: Falling back to Screen.Bounds = ({X},{Y},{W}x{H})",
            screen.DeviceName, screen.Bounds.X, screen.Bounds.Y, screen.Bounds.Width, screen.Bounds.Height);

        return new PhysicalScreenBounds
        {
            Left = screen.Bounds.X,
            Top = screen.Bounds.Y,
            Width = screen.Bounds.Width,
            Height = screen.Bounds.Height
        };
    }

    /// <summary>
    /// Positions a window to exactly cover a screen using physical pixel coordinates.
    /// This bypasses WPF's DPI virtualization for reliable multi-monitor positioning.
    /// </summary>
    private void PositionWindowOnScreen(Window window, System.Windows.Forms.Screen screen)
    {
        var hwnd = new System.Windows.Interop.WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
        {
            App.Logger?.Warning("Cannot position window - no HWND yet");
            return;
        }

        var bounds = GetPhysicalScreenBounds(screen);

        // Use SetWindowPos with physical pixel coordinates - this bypasses WPF's DPI translation
        bool success = SetWindowPos(
            hwnd,
            HWND_TOPMOST,
            bounds.Left,
            bounds.Top,
            bounds.Width,
            bounds.Height,
            SWP_NOACTIVATE | SWP_SHOWWINDOW);

        App.Logger?.Debug("Positioned window on {Screen} at physical ({X},{Y},{W}x{H}), success={Success}",
            screen.DeviceName, bounds.Left, bounds.Top, bounds.Width, bounds.Height, success);
    }

    /// <summary>
    /// Gets the screen bounds converted to WPF device-independent coordinates.
    /// Used for initial window creation - final positioning done via SetWindowPos.
    /// </summary>
    private WpfScreenBounds GetWpfScreenBounds(System.Windows.Forms.Screen screen)
    {
        // For initial window creation, we use approximate WPF coordinates
        // The SourceInitialized handler will then use SetWindowPos with physical pixels
        // to get the exact positioning right
        double primaryDpi = GetPrimaryMonitorDpi();
        double primaryScale = primaryDpi / 96.0;

        // Use physical bounds from Win32 for more accurate initial position
        var physicalBounds = GetPhysicalScreenBounds(screen);

        double left = physicalBounds.Left / primaryScale;
        double top = physicalBounds.Top / primaryScale;
        double width = physicalBounds.Width / primaryScale;
        double height = physicalBounds.Height / primaryScale;

        App.Logger?.Debug("Screen {Name}: Physical=({PX},{PY},{PW}x{PH}), PrimaryDPI={PDPI}, WPF=({WX},{WY},{WW}x{WH})",
            screen.DeviceName,
            physicalBounds.Left, physicalBounds.Top, physicalBounds.Width, physicalBounds.Height,
            primaryDpi,
            left, top, width, height);

        return new WpfScreenBounds
        {
            Left = left,
            Top = top,
            Width = width,
            Height = height
        };
    }

    private double GetMonitorDpi(System.Windows.Forms.Screen screen)
    {
        try
        {
            var hMonitor = MonitorFromPoint(new POINT { X = screen.Bounds.X + 1, Y = screen.Bounds.Y + 1 }, 2);
            if (hMonitor != IntPtr.Zero)
            {
                var result = GetDpiForMonitor(hMonitor, 0, out uint dpiX, out uint dpiY);
                if (result == 0)
                {
                    return dpiX;
                }
            }
        }
        catch (Exception ex)
        {
            App.Logger?.Debug("Could not get DPI for monitor: {Error}", ex.Message);
        }
        return 96.0;
    }

    private double GetPrimaryMonitorDpi()
    {
        try
        {
            var primary = System.Windows.Forms.Screen.PrimaryScreen;
            if (primary != null)
            {
                return GetMonitorDpi(primary);
            }
        }
        catch (Exception ex)
        {
            App.Logger?.Debug("Could not get primary monitor DPI: {Error}", ex.Message);
        }
        return 96.0;
    }

    private double GetDpiScaleForScreen(System.Windows.Forms.Screen screen)
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

    private double GetDpiScale()
    {
        try
        {
            using var g = System.Drawing.Graphics.FromHwnd(IntPtr.Zero);
            return g.DpiX / 96.0;
        }
        catch
        {
            return 1.0;
        }
    }

    private void MakeClickThrough(Window window)
    {
        try
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(window).Handle;
            var exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            // WS_EX_TRANSPARENT: clicks pass through
            // WS_EX_LAYERED: allows transparency
            // WS_EX_NOACTIVATE: never steals keyboard/mouse focus
            SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_TRANSPARENT | WS_EX_LAYERED | WS_EX_NOACTIVATE);
        }
        catch (Exception ex)
        {
            App.Logger?.Debug("Failed to make window click-through: {Error}", ex.Message);
        }
    }

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_LAYERED = 0x00080000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint dwAffinity);

    private const uint WDA_NONE = 0x0;
    private const uint WDA_EXCLUDEFROMCAPTURE = 0x11; // Windows 10 2004+

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

    [System.Runtime.InteropServices.DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
    private static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);
    private const uint SWP_SHOWWINDOW = 0x0040;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_FRAMECHANGED = 0x0020;
    private const uint SWP_NOZORDER = 0x0004;

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    public static extern bool EnumDisplaySettingsEx(string? lpszDeviceName, uint iModeNum, ref DEVMODE lpDevMode, uint dwFlags);

    public const int ENUM_CURRENT_SETTINGS = -1;
    public const int ENUM_REGISTRY_SETTINGS = -2;

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, CharSet = System.Runtime.InteropServices.CharSet.Ansi)]
    public struct DEVMODE
    {
        [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.ByValTStr, SizeConst = 32)]
        public string dmDeviceName;
        public ushort dmSpecVersion;
        public ushort dmDriverVersion;
        public ushort dmSize;
        public ushort dmDriverExtra;
        public uint dmCurrentMode;
        public uint dmFields;

        public short dmPositionX;
        public short dmPositionY;
        public Orientation dmDisplayOrientation;
        public DisplayFixedOutput dmDisplayFixedOutput;

        public short dmColor;
        public short dmDuplex;
        public short dmYResolution;
        public short dmTTOption;
        public short dmCollate;
        [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.ByValTStr, SizeConst = 32)]
        public string dmFormName;
        public ushort dmLogPixels;
        public uint dmBitsPerPel;
        public uint dmPelsWidth;
        public uint dmPelsHeight;
        public uint dmDisplayFlags;
        public uint dmDisplayFrequency;
        public uint dmICMMethod;
        public uint dmICMIntent;
        public uint dmMediaType;
        public uint dmDitherType;
        public uint dmReserved1;
        public uint dmReserved2;
        public uint dmPanningWidth;
        public uint dmPanningHeight;
    }

    public enum Orientation : int
    {
        DMDO_DEFAULT = 0,
        DMDO_90 = 1,
        DMDO_180 = 2,
        DMDO_270 = 3
    }

    public enum DisplayFixedOutput : int
    {
        DMDFO_DEFAULT = 0,
        DMDFO_STRETCH = 1,
        DMDFO_CENTER = 2
    }

    private int GetScreenRefreshRate(System.Windows.Forms.Screen screen)
    {
        DEVMODE dm = new DEVMODE();
        dm.dmSize = (ushort)System.Runtime.InteropServices.Marshal.SizeOf(typeof(DEVMODE));
        if (EnumDisplaySettingsEx(screen.DeviceName, unchecked((uint)ENUM_CURRENT_SETTINGS), ref dm, 0))
        {
            return (int)dm.dmDisplayFrequency;
        }
        return 60;
    }

    #endregion

    private void CurrentSettings_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Ensure this is executed on the UI thread
        Application.Current.Dispatcher.Invoke(() =>
        {
            if (e.PropertyName == nameof(App.Settings.Current.BrainDrainIntensity) ||
                e.PropertyName == nameof(App.Settings.Current.BrainDrainEnabled))
            {
                App.Logger?.Debug("Brain Drain setting changed: {PropertyName}. Refreshing state.", e.PropertyName);
                RefreshBrainDrainState();
            }
            // Add other property names for PinkFilter, Spiral, etc. here if needed
            // else if (e.PropertyName == nameof(App.Settings.Current.PinkFilterEnabled) ||
            //          e.PropertyName == nameof(App.Settings.Current.PinkFilterOpacity))
            // {
            //      RefreshPinkFilterState();
            // }
            // else if (e.PropertyName == nameof(App.Settings.Current.SpiralEnabled) ||
            //          e.PropertyName == nameof(App.Settings.Current.SpiralOpacity))
            // {
            //      RefreshSpiralState();
            // }
        });
    }

    // New method to encapsulate Brain Drain specific refresh logic
    private void RefreshBrainDrainState()
    {
        var settings = App.Settings.Current;

        // Only start/update brain drain if the overlay service is running (engine is active)
        if (!_isRunning)
        {
            // Don't start brain drain if engine isn't running
            StopBrainDrainBlur();
            return;
        }

        if (settings.BrainDrainEnabled && settings.PlayerLevel >= 70) // Level 70 requirement for Brain Drain
        {
            if (_brainDrainBlurWindows.Count == 0)
            {
                StartBrainDrainBlur((int)settings.BrainDrainIntensity);
            }
            else
            {
                // Already running, just update intensity
                UpdateBrainDrainBlurOpacity((int)settings.BrainDrainIntensity);
            }
        }
        else
        {
            StopBrainDrainBlur();
        }
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        // Stop the service normally first
        _isRunning = false;
        _updateTimer?.Stop();
        _updateTimer = null;

        // Unsubscribe from settings changes
        if (App.Settings?.Current != null)
        {
            App.Settings.Current.PropertyChanged -= CurrentSettings_PropertyChanged;
        }

        // Forcefully close all overlay windows - don't rely on Dispatcher during shutdown
        try
        {
            // Close all brain drain blur windows
            foreach (var window in _brainDrainBlurWindows.ToList())
            {
                try { window.Close(); }
                catch (Exception ex)
                {
                    App.Logger?.Debug("Failed to close brain drain window on dispose: {Error}", ex.Message);
                }
            }
            _brainDrainBlurWindows.Clear();

            // Close all pink filter windows
            foreach (var window in _pinkFilterWindows.ToList())
            {
                try { window.Close(); }
                catch (Exception ex)
                {
                    App.Logger?.Debug("Failed to close pink filter window on dispose: {Error}", ex.Message);
                }
            }
            _pinkFilterWindows.Clear();

            // Close all spiral windows
            foreach (var window in _spiralWindows.ToList())
            {
                try { window.Close(); }
                catch (Exception ex)
                {
                    App.Logger?.Debug("Failed to close spiral window on dispose: {Error}", ex.Message);
                }
            }
            _spiralWindows.Clear();

            App.Logger?.Debug("OverlayService disposed - all windows closed");
        }
        catch (Exception ex)
        {
            App.Logger?.Error(ex, "Error during OverlayService disposal");
        }
    }
}
