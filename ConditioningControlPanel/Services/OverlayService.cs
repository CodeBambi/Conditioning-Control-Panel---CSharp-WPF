using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Drawing;
using SharpDX;
using SharpDX.Direct3D11;
using SharpDX.DXGI;

using System.ComponentModel; // Added for INotifyPropertyChanged
using System.Runtime.CompilerServices; // Added for CallerMemberName

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
        
        var resourceSpiral = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "spiral.gif");
        if (File.Exists(resourceSpiral))
        {
            return resourceSpiral;
        }
        
        return "";
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

            if (settings.BrainDrainEnabled)
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
        if (!_isRunning) return;
        _isRunning = false;

        Application.Current.Dispatcher.Invoke(() =>
        {
            _updateTimer?.Stop();
            _updateTimer = null;

            StopPinkFilter();
            StopSpiral();
            StopBrainDrainBlur();
        });

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
                ? System.Windows.Forms.Screen.AllScreens 
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
            double monitorDpi = GetMonitorDpi(screen);
            double primaryDpi = GetPrimaryMonitorDpi();
            double scale = primaryDpi / 96.0;
            
            double left = screen.Bounds.X / scale;
            double top = screen.Bounds.Y / scale;
            double width = screen.Bounds.Width / scale;
            double height = screen.Bounds.Height / scale;

            var actualOpacity = Math.Pow(opacity / 100.0, 2);

            var pinkOverlay = new Border
            {
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(
                                    (byte)(actualOpacity * 255), 255, 105, 180)),
                Opacity = 1.0
            };

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
                Left = left,
                Top = top,
                Width = width,
                Height = height,
                Content = pinkOverlay
            };
            
            window.SourceInitialized += (s, e) =>
            {
                var hwnd = new System.Windows.Interop.WindowInteropHelper(window).Handle;
                var exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
                SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TRANSPARENT | WS_EX_LAYERED);
            };
            
            window.Show();
            
            App.Logger?.Debug("Pink filter for {Screen} at {X},{Y} size {W}x{H} (MonitorDPI:{MonDpi}, PrimaryDPI:{PriDpi})", 
                screen.DeviceName, left, top, width, height, monitorDpi, primaryDpi);
            
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
        foreach (var window in _pinkFilterWindows)
        {
            try { window.Close(); } catch { }
        }
        _pinkFilterWindows.Clear();
        App.Logger?.Debug("Pink filter stopped");
    }

    private void UpdatePinkFilterOpacity()
    {
        var actualOpacity = Math.Pow(App.Settings.Current.PinkFilterOpacity / 100.0, 2);
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
                ? System.Windows.Forms.Screen.AllScreens 
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
            if (media.NaturalDuration.HasTimeSpan && 
                media.Position >= media.NaturalDuration.TimeSpan - TimeSpan.FromMilliseconds(100))
            {
                media.Position = TimeSpan.Zero;
            }
        }
    }

    private (Window? window, MediaElement? media) CreateSpiralForScreen(System.Windows.Forms.Screen screen, int opacity)
    {
        try
        {
            double monitorDpi = GetMonitorDpi(screen);
            double primaryDpi = GetPrimaryMonitorDpi();
            double scale = primaryDpi / 96.0;
            
            double left = screen.Bounds.X / scale;
            double top = screen.Bounds.Y / scale;
            double width = screen.Bounds.Width / scale;
            double height = screen.Bounds.Height / scale;

            var actualOpacity = Math.Pow(opacity / 100.0, 2);

            var mediaElement = new MediaElement
            {
                Source = new Uri(_spiralPath),
                LoadedBehavior = MediaState.Play,
                UnloadedBehavior = MediaState.Manual,
                Stretch = Stretch.UniformToFill,
                Opacity = actualOpacity,
                IsMuted = true
            };

            mediaElement.MediaEnded += (s, e) =>
            {
                mediaElement.Position = TimeSpan.Zero;
                mediaElement.Play();
            };

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
                Left = left,
                Top = top,
                Width = width,
                Height = height,
                Content = mediaElement
            };

            window.SourceInitialized += (s, e) =>
            {
                var hwnd = new System.Windows.Interop.WindowInteropHelper(window).Handle;
                var exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
                SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TRANSPARENT | WS_EX_LAYERED);
            };

            window.Show();
            
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

        foreach (var media in _spiralMediaElements)
        {
            try { media.Stop(); media.Close(); } catch { }
        }
        _spiralMediaElements.Clear();

        foreach (var window in _spiralWindows)
        {
            try { window.Close(); } catch { }
        }
        _spiralWindows.Clear();
        App.Logger?.Debug("Spiral stopped");
    }

    private void UpdateSpiralOpacity()
    {
        var opacity = Math.Pow(App.Settings.Current.SpiralOpacity / 100.0, 2);
        foreach (var media in _spiralMediaElements)
        {
            media.Opacity = opacity;
        }
    }

    #endregion

    #region Brain Drain Blur (DXGI Desktop Duplication)

    private Dictionary<Window, System.Windows.Controls.Image> _brainDrainImages = new();
    private Dictionary<Window, System.Windows.Forms.Screen> _brainDrainScreens = new();
    private Dictionary<int, DesktopDuplicator> _desktopDuplicators = new();
    private DispatcherTimer? _brainDrainCaptureTimer;
    private int _currentBrainDrainIntensity = 50;

    /// <summary>
    /// DXGI Desktop Duplication wrapper for efficient screen capture
    /// </summary>
    private class DesktopDuplicator : IDisposable
    {
        private SharpDX.Direct3D11.Device? _device;
        private OutputDuplication? _duplication;
        private Texture2D? _stagingTexture;
        private readonly int _width;
        private readonly int _height;
        private bool _disposed;

        public int Width => _width;
        public int Height => _height;
        public bool IsValid => _duplication != null && !_disposed;

        public DesktopDuplicator(int adapterIndex, int outputIndex)
        {
            try
            {
                using var factory = new Factory1();
                using var adapter = factory.GetAdapter1(adapterIndex);
                
                _device = new SharpDX.Direct3D11.Device(adapter, DeviceCreationFlags.BgraSupport);
                
                using var output = adapter.GetOutput(outputIndex);
                var outputDesc = output.Description;
                _width = outputDesc.DesktopBounds.Right - outputDesc.DesktopBounds.Left;
                _height = outputDesc.DesktopBounds.Bottom - outputDesc.DesktopBounds.Top;

                using var output1 = output.QueryInterface<Output1>();
                _duplication = output1.DuplicateOutput(_device);

                // Create staging texture for CPU access
                var stagingDesc = new Texture2DDescription
                {
                    Width = _width,
                    Height = _height,
                    MipLevels = 1,
                    ArraySize = 1,
                    Format = Format.B8G8R8A8_UNorm,
                    SampleDescription = new SampleDescription(1, 0),
                    Usage = ResourceUsage.Staging,
                    CpuAccessFlags = CpuAccessFlags.Read,
                    BindFlags = BindFlags.None
                };
                _stagingTexture = new Texture2D(_device, stagingDesc);

                App.Logger?.Debug("DesktopDuplicator initialized: Adapter {Adapter}, Output {Output}, Size {W}x{H}",
                    adapterIndex, outputIndex, _width, _height);
            }
            catch (Exception ex)
            {
                App.Logger?.Error("Failed to initialize DesktopDuplicator: {Error}", ex.Message);
                Dispose();
                throw;
            }
        }

        public BitmapSource? CaptureFrame()
        {
            if (_disposed || _duplication == null || _device == null || _stagingTexture == null)
                return null;

            try
            {
                // Try to acquire next frame (0ms timeout = don't wait)
                var result = _duplication.TryAcquireNextFrame(0, out var frameInfo, out var desktopResource);
                
                if (result.Failure)
                {
                    // No new frame available, which is fine
                    return null;
                }

                try
                {
                    using var desktopTexture = desktopResource.QueryInterface<Texture2D>();
                    
                    // Copy to staging texture
                    _device.ImmediateContext.CopyResource(desktopTexture, _stagingTexture);

                    // Map staging texture to CPU memory
                    var dataBox = _device.ImmediateContext.MapSubresource(
                        _stagingTexture, 0, MapMode.Read, SharpDX.Direct3D11.MapFlags.None);

                    try
                    {
                        // Create WPF BitmapSource from the data
                        var bitmap = new WriteableBitmap(_width, _height, 96, 96, PixelFormats.Bgra32, null);
                        bitmap.Lock();

                        try
                        {
                            // Copy row by row (handle pitch/stride differences)
                            var destPtr = bitmap.BackBuffer;
                            var srcPtr = dataBox.DataPointer;
                            var destStride = bitmap.BackBufferStride;
                            var srcStride = dataBox.RowPitch;

                            for (int y = 0; y < _height; y++)
                            {
                                Utilities.CopyMemory(
                                    destPtr + y * destStride,
                                    srcPtr + y * srcStride,
                                    _width * 4);
                            }

                            bitmap.AddDirtyRect(new Int32Rect(0, 0, _width, _height));
                        }
                        finally
                        {
                            bitmap.Unlock();
                        }

                        bitmap.Freeze();
                        return bitmap;
                    }
                    finally
                    {
                        _device.ImmediateContext.UnmapSubresource(_stagingTexture, 0);
                    }
                }
                finally
                {
                    desktopResource?.Dispose();
                    _duplication.ReleaseFrame();
                }
            }
            catch (SharpDXException ex) when (ex.ResultCode.Code == SharpDX.DXGI.ResultCode.WaitTimeout.Result.Code)
            {
                // Timeout is expected when no new frame
                return null;
            }
            catch (SharpDXException ex) when (ex.ResultCode.Code == SharpDX.DXGI.ResultCode.AccessLost.Result.Code)
            {
                // Desktop duplication lost (e.g., resolution change, secure desktop)
                App.Logger?.Warning("Desktop duplication access lost, will reinitialize");
                return null;
            }
            catch (Exception ex)
            {
                App.Logger?.Error("DesktopDuplicator CaptureFrame error: {Error}", ex.Message);
                return null;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _stagingTexture?.Dispose();
            _stagingTexture = null;

            _duplication?.Dispose();
            _duplication = null;

            _device?.Dispose();
            _device = null;
        }
    }

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
                    ? System.Windows.Forms.Screen.AllScreens
                    : new[] { System.Windows.Forms.Screen.PrimaryScreen! };

                // Initialize desktop duplicators for each output
                InitializeDesktopDuplicators(screens);

                foreach (var screen in screens)
                {
                    var window = CreateBrainDrainWindow(screen, intensity);
                    if (window != null)
                    {
                        _brainDrainBlurWindows.Add(window);
                    }
                }

                // Match capture rate to highest monitor refresh rate
                int maxRefreshRate = screens.Max(s => GetScreenRefreshRate(s));
                double intervalMs = 1000.0 / maxRefreshRate;
                
                _brainDrainCaptureTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(intervalMs)
                };
                _brainDrainCaptureTimer.Tick += BrainDrainCaptureTick;
                _brainDrainCaptureTimer.Start();
                
                App.Logger?.Debug("Brain Drain capture rate: {RefreshRate}Hz ({IntervalMs:F2}ms)", 
                    maxRefreshRate, intervalMs);

                App.Logger?.Debug("Brain Drain started on {Count} screens at intensity {Intensity}% using DXGI",
                    _brainDrainBlurWindows.Count, intensity);
            }
            catch (Exception ex)
            {
                App.Logger?.Error("Failed to start Brain Drain: {Error}", ex.Message);
            }
        });
    }

    private void InitializeDesktopDuplicators(System.Windows.Forms.Screen[] screens)
    {
        // Clean up existing duplicators
        foreach (var dup in _desktopDuplicators.Values)
        {
            dup.Dispose();
        }
        _desktopDuplicators.Clear();

        // Map screens to DXGI outputs
        try
        {
            using var factory = new Factory1();
            
            for (int adapterIdx = 0; adapterIdx < factory.GetAdapterCount1(); adapterIdx++)
            {
                using var adapter = factory.GetAdapter1(adapterIdx);
                
                for (int outputIdx = 0; outputIdx < adapter.GetOutputCount(); outputIdx++)
                {
                    using var output = adapter.GetOutput(outputIdx);
                    var bounds = output.Description.DesktopBounds;
                    
                    // Find matching screen
                    for (int screenIdx = 0; screenIdx < screens.Length; screenIdx++)
                    {
                        var screen = screens[screenIdx];
                        if (screen.Bounds.X == bounds.Left && 
                            screen.Bounds.Y == bounds.Top &&
                            screen.Bounds.Width == bounds.Right - bounds.Left &&
                            screen.Bounds.Height == bounds.Bottom - bounds.Top)
                        {
                            try
                            {
                                _desktopDuplicators[screenIdx] = new DesktopDuplicator(adapterIdx, outputIdx);
                                App.Logger?.Debug("Mapped screen {ScreenIdx} ({Name}) to adapter {Adapter} output {Output}",
                                    screenIdx, screen.DeviceName, adapterIdx, outputIdx);
                            }
                            catch (Exception ex)
                            {
                                App.Logger?.Warning("Failed to create duplicator for screen {Idx}: {Error}", screenIdx, ex.Message);
                            }
                            break;
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            App.Logger?.Error("Failed to initialize desktop duplicators: {Error}", ex.Message);
        }
    }

    public void StopBrainDrainBlur()
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            _brainDrainCaptureTimer?.Stop();
            _brainDrainCaptureTimer = null;

            foreach (var window in _brainDrainBlurWindows)
            {
                try { window.Close(); } catch { }
            }
            _brainDrainBlurWindows.Clear();
            _brainDrainImages.Clear();
            _brainDrainScreens.Clear();

            // Clean up desktop duplicators
            foreach (var dup in _desktopDuplicators.Values)
            {
                dup.Dispose();
            }
            _desktopDuplicators.Clear();

            App.Logger?.Debug("Brain Drain stopped");
        });
    }

    public void UpdateBrainDrainBlurOpacity(int intensity)
    {
        _currentBrainDrainIntensity = intensity;
    
        // Scale intensity (0-25) to blur radius (0-30)
        // Assuming slider max is 25, and this should map to the previous max blur radius of 30.
        double blurRadius = (intensity / 25.0) * 30.0; // Max slider value 25 maps to max blur radius 30.
    
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

        var settings = App.Settings.Current;
        var screens = settings.DualMonitorEnabled
            ? System.Windows.Forms.Screen.AllScreens
            : new[] { System.Windows.Forms.Screen.PrimaryScreen! };

        // Capture and update each screen
        int screenIdx = 0;
        foreach (var kvp in _brainDrainImages)
        {
            var window = kvp.Key;
            var image = kvp.Value;

            if (_desktopDuplicators.TryGetValue(screenIdx, out var duplicator) && duplicator.IsValid)
            {
                var capture = duplicator.CaptureFrame();
                if (capture != null)
                {
                    image.Source = capture;
                }
                // If capture is null, keep the previous frame (no flicker)
            }
            else
            {
                // Fallback to BitBlt if DXGI not available for this screen
                if (_brainDrainScreens.TryGetValue(window, out var screen))
                {
                    var capture = CaptureScreenFallback(screen);
                    if (capture != null)
                    {
                        image.Source = capture;
                    }
                }
            }

            screenIdx++;
        }
    }

    private Window? CreateBrainDrainWindow(System.Windows.Forms.Screen screen, int intensity)
    {
        try
        {
            double primaryDpi = GetPrimaryMonitorDpi();
            double scale = primaryDpi / 96.0;

            double left = screen.Bounds.X / scale;
            double top = screen.Bounds.Y / scale;
            double width = screen.Bounds.Width / scale;
            double height = screen.Bounds.Height / scale;

            double blurRadius = intensity * 0.3;

            var image = new System.Windows.Controls.Image
            {
                Stretch = Stretch.Fill,
                Effect = new System.Windows.Media.Effects.BlurEffect
                {
                    Radius = blurRadius,
                    KernelType = System.Windows.Media.Effects.KernelType.Gaussian
                }
            };

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
                Left = left,
                Top = top,
                Width = width,
                Height = height,
                Content = image
            };

            window.SourceInitialized += (s, e) =>
            {
                var hwnd = new System.Windows.Interop.WindowInteropHelper(window).Handle;
                var exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
                SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TRANSPARENT | WS_EX_LAYERED);
                
                // Exclude this window from screen capture so DXGI doesn't capture itself
                SetWindowDisplayAffinity(hwnd, WDA_EXCLUDEFROMCAPTURE);
            };

            window.Show();

            _brainDrainImages[window] = image;
            _brainDrainScreens[window] = screen;

            return window;
        }
        catch (Exception ex)
        {
            App.Logger?.Error("Failed to create Brain Drain window: {Error}", ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Fallback capture using BitBlt (for when DXGI is unavailable)
    /// </summary>
    private BitmapSource? CaptureScreenFallback(System.Windows.Forms.Screen screen)
    {
        try
        {
            var hdcSrc = GetDC(IntPtr.Zero);
            var hdcDest = CreateCompatibleDC(hdcSrc);
            var hBitmap = CreateCompatibleBitmap(hdcSrc, screen.Bounds.Width, screen.Bounds.Height);
            var hOld = SelectObject(hdcDest, hBitmap);

            BitBlt(hdcDest, 0, 0, screen.Bounds.Width, screen.Bounds.Height,
                   hdcSrc, screen.Bounds.X, screen.Bounds.Y, SRCCOPY);

            SelectObject(hdcDest, hOld);
            DeleteDC(hdcDest);
            ReleaseDC(IntPtr.Zero, hdcSrc);

            var bitmapSource = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                hBitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());

            DeleteObject(hBitmap);
            bitmapSource.Freeze();

            return bitmapSource;
        }
        catch
        {
            return null;
        }
    }

    #endregion

    #region Helpers

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
        catch { }
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
        catch { }
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
            SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_TRANSPARENT | WS_EX_LAYERED);
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

    private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
    private const uint SWP_SHOWWINDOW = 0x0040;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_FRAMECHANGED = 0x0020;

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
        if (settings.BrainDrainEnabled && settings.PlayerLevel >= 90) // Assuming level requirement for Brain Drain
        {
            // If not running or duplicators not initialized (e.g., first time enabling)
            if (_brainDrainBlurWindows.Count == 0 || !_desktopDuplicators.Any(kvp => kvp.Value.IsValid))
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
        Stop();

        // Unsubscribe from settings changes
        if (App.Settings?.Current != null)
        {
            App.Settings.Current.PropertyChanged -= CurrentSettings_PropertyChanged;
        }
    }
}
