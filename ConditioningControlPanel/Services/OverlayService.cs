using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Drawing; // Added for screen capture functionality

namespace ConditioningControlPanel.Services;

/// <summary>
/// Service that manages screen overlays: Pink Filter and Spiral
/// </summary>
public class OverlayService : IDisposable
{
    private readonly List<Window> _pinkFilterWindows = new();
    private readonly List<Window> _spiralWindows = new();
    private readonly List<MediaElement> _spiralMediaElements = new();
    private readonly List<Window> _brainDrainBlurWindows = new(); // New field for Brain Drain blur
    private bool _isRunning;
    private DispatcherTimer? _updateTimer;
    private DispatcherTimer? _gifLoopTimer;
    private bool _isDisposed;
    private bool _isGifSpiral;
    private string _spiralPath = "";

    public bool IsRunning => _isRunning;

    // P/Invoke declarations for screen capture (BitBlt)
    private const int SRCCOPY = 0x00CC0020; // DwRop parameter for BitBlt
    
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
            
            // If a specific path is set by the user, use it
            if (!string.IsNullOrEmpty(settings.SpiralPath) && File.Exists(settings.SpiralPath))
            {
                return settings.SpiralPath;
            }
            
            // Fallback to Resources/spiral.gif if user-defined path is not set or invalid
            var resourceSpiral = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "spiral.gif");
            if (File.Exists(resourceSpiral))
            {
                return resourceSpiral;
            }
            
            return ""; // No spiral found
        }

    public void Start()
    {
        if (_isRunning) return;
        _isRunning = true;

        Application.Current.Dispatcher.Invoke(() =>
        {
            var settings = App.Settings.Current;

            // Check level requirements
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

    /// <summary>
    /// Immediately refresh overlay state based on current settings
    /// Call this when settings change while engine is running
    /// </summary>
    public void RefreshOverlays()
    {
        if (!_isRunning) return;
        
        Application.Current.Dispatcher.Invoke(() =>
        {
            var settings = App.Settings.Current;
            
            // Pink Filter
            if (settings.PinkFilterEnabled && settings.PlayerLevel >= 10)
            {
                if (_pinkFilterWindows.Count == 0)
                    StartPinkFilter();
                else
                    UpdatePinkFilterOpacity(); // Update opacity if already running
            }
            else
            {
                StopPinkFilter();
            }
            
            // Spiral - use GetSpiralPath to allow random selection
            var spiralPath = GetSpiralPath();
            if (settings.SpiralEnabled && settings.PlayerLevel >= 10 && !string.IsNullOrEmpty(spiralPath))
            {
                _spiralPath = spiralPath;
                if (_spiralWindows.Count == 0)
                    StartSpiral();
                else
                    UpdateSpiralOpacity(); // Update opacity if already running
            }
            else
            {
                StopSpiral();
            }

            // Brain Drain Blur
            if (settings.BrainDrainEnabled && settings.PlayerLevel >= 90)
            {
                if (_brainDrainBlurWindows.Count == 0)
                    StartBrainDrainBlur((int)settings.BrainDrainIntensity);
                else
                    UpdateBrainDrainBlurOpacity((int)settings.BrainDrainIntensity);
            }
            else
            {
                StopBrainDrainBlur();
            }
        });
        
        App.Logger?.Debug("Overlays refreshed - Pink: {Pink}, Spiral: {Spiral}, BrainDrain: {BrainDrain}", 
            _pinkFilterWindows.Count > 0, _spiralWindows.Count > 0, _brainDrainBlurWindows.Count > 0);
    }

    private void UpdateOverlays(object? sender, EventArgs e)
    {
        var settings = App.Settings.Current;

        // Check level requirement
        if (settings.PlayerLevel < 10)
        {
            StopPinkFilter();
            StopSpiral();
            StopBrainDrainBlur(); // Also stop Brain Drain if level is too low
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

        // Spiral - use GetSpiralPath to allow random selection
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
            
            // Get all screens if dual monitor enabled, otherwise just primary
            var screens = settings.DualMonitorEnabled 
                ? System.Windows.Forms.Screen.AllScreens 
                : new[] { System.Windows.Forms.Screen.PrimaryScreen! };

            // Create overlay for each screen
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
            // Get the actual DPI for THIS specific monitor
            double monitorDpi = GetMonitorDpi(screen);
            double primaryDpi = GetPrimaryMonitorDpi();
            
            // WPF uses primary monitor DPI for coordinate system
            // We need to convert physical pixels to WPF units
            double scale = primaryDpi / 96.0;
            
            double left = screen.Bounds.X / scale;
            double top = screen.Bounds.Y / scale;
            double width = screen.Bounds.Width / scale;
            double height = screen.Bounds.Height / scale;

            // Apply exponential curve for finer control at low values
            var actualOpacity = Math.Pow(opacity / 100.0, 2);

            var pinkOverlay = new Border
            {
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(
                                    (byte)(actualOpacity * 255), 255, 105, 180)),
                Opacity = 1.0 // Opacity handled by the solid color brush's alpha
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
                    // Apply exponential curve for finer control at low values
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
                    }    }

    #endregion

    #region Spiral Overlay

    private void StartSpiral()
    {
        if (_spiralWindows.Count > 0) return;

        try
        {
            var settings = App.Settings.Current;
            _spiralPath = settings.SpiralPath;

            if (string.IsNullOrEmpty(_spiralPath) || !File.Exists(_spiralPath))
            {
                App.Logger?.Warning("Spiral path not set or file not found: {Path}", _spiralPath);
                return;
            }

            // Check if it's a GIF
            _isGifSpiral = Path.GetExtension(_spiralPath).Equals(".gif", StringComparison.OrdinalIgnoreCase);

            // Get all screens if dual monitor enabled, otherwise just primary
            var screens = settings.DualMonitorEnabled 
                ? System.Windows.Forms.Screen.AllScreens 
                : new[] { System.Windows.Forms.Screen.PrimaryScreen! };

            foreach (var screen in screens)
            {
                var (window, media) = CreateSpiralForScreen(screen, _spiralPath, settings.SpiralOpacity);
                if (window != null)
                {
                    _spiralWindows.Add(window);
                    if (media != null)
                    {
                        _spiralMediaElements.Add(media);
                    }
                }
            }

            // For GIFs, setup a timer to force loop (MediaElement doesn't loop GIFs well)
            if (_isGifSpiral && _spiralMediaElements.Count > 0)
            {
                _gifLoopTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
                _gifLoopTimer.Tick += (s, e) =>
                {
                    foreach (var media in _spiralMediaElements)
                    {
                        try
                        {
                            media.Position = TimeSpan.Zero;
                            media.Play();
                        }
                        catch { }
                    }
                };
                _gifLoopTimer.Start();
            }

            App.Logger?.Debug("Spiral started on {Count} screens: {Path} at opacity {Opacity}% (GIF: {IsGif})", 
                _spiralWindows.Count, _spiralPath, settings.SpiralOpacity, _isGifSpiral);
        }
        catch (Exception ex)
        {
            App.Logger?.Error("Failed to start spiral: {Error}", ex.Message);
        }
    }

    private (Window?, MediaElement?) CreateSpiralForScreen(System.Windows.Forms.Screen screen, string spiralPath, int opacity)
    {
        try
        {
            // Get the actual DPI for THIS specific monitor
            double monitorDpi = GetMonitorDpi(screen);
            double primaryDpi = GetPrimaryMonitorDpi();
            
            // WPF uses primary monitor DPI for coordinate system
            double scale = primaryDpi / 96.0;
            
            double left = screen.Bounds.X / scale;
            double top = screen.Bounds.Y / scale;
            double width = screen.Bounds.Width / scale;
            double height = screen.Bounds.Height / scale;

            // Apply exponential curve for finer control at low values
            var actualOpacity = Math.Pow(opacity / 100.0, 2);
            
            var media = new MediaElement
            {
                Source = new Uri(spiralPath),
                LoadedBehavior = MediaState.Manual,
                UnloadedBehavior = MediaState.Manual,
                Stretch = Stretch.UniformToFill,
                Opacity = actualOpacity,
                Volume = 0 // Mute spiral videos/gifs
            };
            
            media.MediaEnded += (s, e) =>
            {
                Application.Current.Dispatcher.BeginInvoke(() =>
                {
                    if (media != null)
                    {
                        media.Position = TimeSpan.Zero;
                        media.Play();
                    }
                });
            };
            
            media.MediaOpened += (s, e) =>
            {
                Application.Current.Dispatcher.BeginInvoke(() =>
                {
                    media?.Play();
                });
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
                Content = media
            };

            window.SourceInitialized += (s, e) =>
            {
                var hwnd = new System.Windows.Interop.WindowInteropHelper(window).Handle;
                var exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
                SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TRANSPARENT | WS_EX_LAYERED);
            };

            window.Show();
            media.Play();
            
            App.Logger?.Debug("Spiral for {Screen} at {X},{Y} size {W}x{H} (MonitorDPI:{MonDpi}, PrimaryDPI:{PriDpi})", 
                screen.DeviceName, left, top, width, height, monitorDpi, primaryDpi);

            return (window, media);
        }
        catch (Exception ex)
        {
            App.Logger?.Error("Failed to create spiral for screen: {Error}", ex.Message);
            return (null, null);
        }
    }

    private void StopSpiral()
    {
        // Stop GIF loop timer
        _gifLoopTimer?.Stop();
        _gifLoopTimer = null;
        
        foreach (var media in _spiralMediaElements)
        {
            try { media.Stop(); } catch { }
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
        // Apply exponential curve for finer control at low values
        var opacity = Math.Pow(App.Settings.Current.SpiralOpacity / 100.0, 2);
        foreach (var media in _spiralMediaElements)
        {
            media.Opacity = opacity;
        }
    }

    #endregion

#region Brain Drain Blur

private Dictionary<Window, Border> _brainDrainBlurTargets = new();
private int _currentBrainDrainIntensity = 50;

public void StartBrainDrainBlur(int intensity)
{
    if (_brainDrainBlurWindows.Count > 0) return;

    _currentBrainDrainIntensity = intensity;

    Application.Current.Dispatcher.Invoke(() =>
    {
        try
        {
            var settings = App.Settings.Current;

            // Get all screens if dual monitor enabled, otherwise just primary
            var screens = settings.DualMonitorEnabled
                ? System.Windows.Forms.Screen.AllScreens
                : new[] { System.Windows.Forms.Screen.PrimaryScreen! };

            foreach (var screen in screens)
            {
                var window = CreateBrainDrainBlurForScreen(screen, intensity);
                if (window != null)
                {
                    _brainDrainBlurWindows.Add(window);
                }
            }

            App.Logger?.Debug("Brain Drain blur started on {Count} screens at intensity {Intensity}%",
                _brainDrainBlurWindows.Count, intensity);
        }
        catch (Exception ex)
        {
            App.Logger?.Error("Failed to start Brain Drain blur: {Error}", ex.Message);
        }
    });
}

public void StopBrainDrainBlur()
{
    Application.Current.Dispatcher.Invoke(() =>
    {
        foreach (var window in _brainDrainBlurWindows)
        {
            try { window.Close(); } catch { }
        }
        _brainDrainBlurWindows.Clear();
        _brainDrainBlurTargets.Clear();
        App.Logger?.Debug("Brain Drain blur stopped");
    });
}

public void UpdateBrainDrainBlurOpacity(int intensity)
{
    _currentBrainDrainIntensity = intensity;
    
    Application.Current.Dispatcher.Invoke(() =>
    {
        // Update the opacity/color of each blur window
        var opacity = Math.Clamp((intensity / 100.0) * 0.1, 0.001, 0.08);
        
        foreach (var entry in _brainDrainBlurTargets)
        {
            entry.Value.Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(
                (byte)(opacity * 180), // Alpha: 0-180 based on intensity
                200, 200, 220)); // Slight blue-gray tint
        }
        
        // Re-apply blur effect to windows
        foreach (var window in _brainDrainBlurWindows)
        {
            EnableBlur(window, intensity);
        }
        
        App.Logger?.Debug("Brain Drain blur intensity updated to {Intensity}%", intensity);
    });
}

private Window? CreateBrainDrainBlurForScreen(System.Windows.Forms.Screen screen, int intensity)
{
    try
    {
        // Get the actual DPI for THIS specific monitor
        double monitorDpi = GetMonitorDpi(screen);
        double primaryDpi = GetPrimaryMonitorDpi();
        double scale = primaryDpi / 96.0;

        double left = screen.Bounds.X / scale;
        double top = screen.Bounds.Y / scale;
        double width = screen.Bounds.Width / scale;
        double height = screen.Bounds.Height / scale;

        // Create a semi-transparent background that will receive the blur effect
        var opacity = Math.Clamp(intensity / 100.0, 0.1, 0.9);
        var border = new Border
        {
            Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(
                (byte)(opacity * 180),
                200, 200, 220)) // Slight blue-gray tint for "brain drain" effect
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
            IsHitTestVisible = false, // Make it click-through
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = left,
            Top = top,
            Width = width,
            Height = height,
            Content = border
        };

        window.SourceInitialized += (s, e) =>
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(window).Handle;
            var exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TRANSPARENT | WS_EX_LAYERED);
            
            // Enable blur effect via DWM
            EnableBlur(window, intensity);
        };

        window.Show();
        _brainDrainBlurTargets[window] = border;

        App.Logger?.Debug("Brain Drain blur window for {Screen} at {X},{Y} size {W}x{H}",
            screen.DeviceName, left, top, width, height);

        return window;
    }
    catch (Exception ex)
    {
        App.Logger?.Error("Failed to create Brain Drain blur for screen: {Error}", ex.Message);
        return null;
    }
}

private void EnableBlur(Window window, int intensity)
{
    try
    {
        var hwnd = new System.Windows.Interop.WindowInteropHelper(window).Handle;
        
        // Calculate gradient color with intensity-based alpha
        // Format: 0xAABBGGRR (Alpha, Blue, Green, Red)
        var alpha = (uint)(Math.Clamp((intensity / 100.0) * 0.1, 0.001, 0.05) * 255);
        uint gradientColor = (alpha << 24) | 0x00303040; // Semi-transparent dark blue-gray
        
        var accent = new AccentPolicy
        {
            // Use Acrylic on Windows 10 1803+, fall back to blur on older
            AccentState = ACCENT_ENABLE_ACRYLICBLURBEHIND,
            AccentFlags = 2, // Draw all borders
            GradientColor = gradientColor,
            AnimationId = 0
        };

        var accentSize = System.Runtime.InteropServices.Marshal.SizeOf(accent);
        var accentPtr = System.Runtime.InteropServices.Marshal.AllocHGlobal(accentSize);
        System.Runtime.InteropServices.Marshal.StructureToPtr(accent, accentPtr, false);

        var data = new WindowCompositionAttributeData
        {
            Attribute = WCA_ACCENT_POLICY,
            Data = accentPtr,
            SizeOfData = accentSize
        };

        SetWindowCompositionAttribute(hwnd, ref data);
        System.Runtime.InteropServices.Marshal.FreeHGlobal(accentPtr);
        
        App.Logger?.Debug("Enabled DWM blur for Brain Drain window");
    }
    catch (Exception ex)
    {
        App.Logger?.Warning("Failed to enable DWM blur (falling back to simple overlay): {Error}", ex.Message);
        // Blur will still work visually through the semi-transparent overlay, just not as smooth
    }
}

// REMOVE the old CaptureScreen method - it's no longer needed for Brain Drain
// (Keep it only if used elsewhere in your code)

#endregion

    #region Helpers

    /// <summary>
    /// Get DPI for a specific monitor
    /// </summary>
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

    /// <summary>
    /// Get DPI for the primary monitor
    /// </summary>
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

    /// <summary>
    /// Get DPI scale for a specific screen using its bounds
    /// This handles multi-monitor setups with different scaling per monitor
    /// </summary>
    private double GetDpiScaleForScreen(System.Windows.Forms.Screen screen)
    {
        try
        {
            // Try to get per-monitor DPI using Win32 API
            var hwnd = IntPtr.Zero;
            
            // Get DPI for the monitor this screen is on
            uint dpiX = 96, dpiY = 96;
            var hMonitor = MonitorFromPoint(new POINT { X = screen.Bounds.X + 1, Y = screen.Bounds.Y + 1 }, 2);
            
            if (hMonitor != IntPtr.Zero)
            {
                // Try GetDpiForMonitor (Windows 8.1+)
                var result = GetDpiForMonitor(hMonitor, 0, out dpiX, out dpiY);
                if (result == 0)
                {
                    return dpiX / 96.0;
                }
            }
            
            // Fallback to system DPI
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

    // Per-monitor DPI support (Windows 8.1+)
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

    [System.Runtime.InteropServices.DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

    // SetWindowPos for positioning windows in physical pixels
    private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
    private const uint SWP_SHOWWINDOW = 0x0040;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_FRAMECHANGED = 0x0020;

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    #endregion

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        Stop();
    }
}