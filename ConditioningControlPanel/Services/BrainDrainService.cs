using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace ConditioningControlPanel.Services;

/// <summary>
/// Brain Drain - Applies a blur effect to the screen, simulating dizziness/brain fog
/// Unlocks at Level 25, awards 10 XP per minute while active
/// </summary>
public class BrainDrainService : IDisposable
{
    private readonly List<Window> _blurWindows = new();
    private DispatcherTimer? _xpTimer;
    private DispatcherTimer? _updateTimer;
    private bool _isRunning;
    private bool _isDisposed;
    private DateTime _startTime;
    
    // Required level to unlock
    public const int REQUIRED_LEVEL = 25;
    
    // XP awarded per minute
    private const int XP_PER_MINUTE = 10;
    
    public bool IsRunning => _isRunning;
    
    public event EventHandler<int>? XPAwarded;

    public void Start()
    {
        if (_isRunning) return;
        
        var settings = App.Settings.Current;
        
        // Check level requirement
        if (settings.PlayerLevel < REQUIRED_LEVEL)
        {
            App.Logger?.Information("BrainDrainService: Level {Level} is below {Required}, not available", 
                settings.PlayerLevel, REQUIRED_LEVEL);
            return;
        }
        
        if (!settings.BrainDrainEnabled)
        {
            App.Logger?.Debug("BrainDrainService: Not enabled in settings");
            return;
        }
        
        _isRunning = true;
        _startTime = DateTime.Now;
        
        Application.Current.Dispatcher.Invoke(() =>
        {
            CreateBlurOverlays();
            
            // Timer to update blur intensity based on settings changes
            _updateTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };
            _updateTimer.Tick += UpdateBlurIntensity;
            _updateTimer.Start();
            
            // Timer to award XP every minute
            _xpTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMinutes(1)
            };
            _xpTimer.Tick += AwardXP;
            _xpTimer.Start();
        });
        
        App.Logger?.Information("BrainDrainService started at intensity {Intensity}%", settings.BrainDrainIntensity);
    }

    public void Stop()
    {
        if (!_isRunning) return;
        _isRunning = false;
        
        Application.Current.Dispatcher.Invoke(() =>
        {
            _updateTimer?.Stop();
            _updateTimer = null;
            
            _xpTimer?.Stop();
            _xpTimer = null;
            
            foreach (var window in _blurWindows)
            {
                try { window.Close(); } catch { }
            }
            _blurWindows.Clear();
        });
        
        var duration = DateTime.Now - _startTime;
        App.Logger?.Information("BrainDrainService stopped after {Duration:F1} minutes", duration.TotalMinutes);
    }

    /// <summary>
    /// Refresh overlays when settings change
    /// </summary>
    public void Refresh()
    {
        if (!_isRunning) return;
        
        var settings = App.Settings.Current;
        
        if (!settings.BrainDrainEnabled || settings.PlayerLevel < REQUIRED_LEVEL)
        {
            Stop();
            return;
        }
        
        Application.Current.Dispatcher.Invoke(() =>
        {
            UpdateBlurIntensity(null, EventArgs.Empty);
        });
    }

    private void CreateBlurOverlays()
    {
        var settings = App.Settings.Current;
        
        // Get screens based on dual monitor setting
        var screens = settings.DualMonitorEnabled
            ? System.Windows.Forms.Screen.AllScreens
            : new[] { System.Windows.Forms.Screen.PrimaryScreen! };
        
        foreach (var screen in screens)
        {
            var window = CreateBlurWindowForScreen(screen, settings.BrainDrainIntensity);
            if (window != null)
            {
                _blurWindows.Add(window);
            }
        }
        
        App.Logger?.Debug("BrainDrain: Created {Count} blur overlay windows", _blurWindows.Count);
    }

    private Window? CreateBlurWindowForScreen(System.Windows.Forms.Screen screen, int intensity)
    {
        try
        {
            // Get DPI scaling
            double dpiScale = GetDpiScaleForScreen(screen);
            double primaryDpi = GetPrimaryDpi();
            
            // Calculate WPF coordinates
            double left = screen.Bounds.X / primaryDpi * 96.0;
            double top = screen.Bounds.Y / primaryDpi * 96.0;
            double width = screen.Bounds.Width / dpiScale;
            double height = screen.Bounds.Height / dpiScale;
            
            // Calculate blur radius (intensity 1-100 maps to blur radius 0.5-15)
            double blurRadius = 0.5 + (intensity / 100.0) * 14.5;
            
            // Create a semi-transparent overlay with blur effect
            // We use a very subtle white/gray tint to create the "foggy" effect
            var overlay = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(
                    (byte)(intensity * 0.3), // Very subtle alpha based on intensity
                    255, 255, 255)),
                Effect = new BlurEffect
                {
                    Radius = blurRadius,
                    KernelType = KernelType.Gaussian,
                    RenderingBias = RenderingBias.Performance
                }
            };
            
            var window = new Window
            {
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = Brushes.Transparent,
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
                Content = overlay
            };
            
            window.SourceInitialized += (s, e) => MakeClickThrough(window);
            window.Show();
            
            App.Logger?.Debug("BrainDrain: Created blur window for {Screen} at intensity {Intensity}% (blur: {Blur})", 
                screen.DeviceName, intensity, blurRadius);
            
            return window;
        }
        catch (Exception ex)
        {
            App.Logger?.Error(ex, "BrainDrain: Failed to create blur window for screen");
            return null;
        }
    }

    private void UpdateBlurIntensity(object? sender, EventArgs e)
    {
        var settings = App.Settings.Current;
        
        if (!settings.BrainDrainEnabled)
        {
            Stop();
            return;
        }
        
        // Calculate blur radius based on intensity
        double blurRadius = 0.5 + (settings.BrainDrainIntensity / 100.0) * 14.5;
        byte alpha = (byte)(settings.BrainDrainIntensity * 0.3);
        
        foreach (var window in _blurWindows)
        {
            if (window.Content is Border border)
            {
                // Update blur effect
                if (border.Effect is BlurEffect blur)
                {
                    blur.Radius = blurRadius;
                }
                
                // Update background opacity
                border.Background = new SolidColorBrush(Color.FromArgb(alpha, 255, 255, 255));
            }
        }
    }

    private void AwardXP(object? sender, EventArgs e)
    {
        if (!_isRunning) return;
        
        App.Progression?.AddXP(XP_PER_MINUTE);
        XPAwarded?.Invoke(this, XP_PER_MINUTE);
        
        App.Logger?.Debug("BrainDrain: Awarded {XP} XP (1 minute elapsed)", XP_PER_MINUTE);
    }

    private void MakeClickThrough(Window window)
    {
        try
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(window).Handle;
            var exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_TRANSPARENT | WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
        }
        catch (Exception ex)
        {
            App.Logger?.Debug("BrainDrain: Failed to make window click-through: {Error}", ex.Message);
        }
    }

    private double GetDpiScaleForScreen(System.Windows.Forms.Screen screen)
    {
        try
        {
            var hMonitor = MonitorFromPoint(new POINT { X = screen.Bounds.X + 1, Y = screen.Bounds.Y + 1 }, 2);
            if (hMonitor != IntPtr.Zero)
            {
                var result = GetDpiForMonitor(hMonitor, 0, out uint dpiX, out uint dpiY);
                if (result == 0)
                {
                    return dpiX / 96.0;
                }
            }
        }
        catch { }
        return 1.0;
    }

    private double GetPrimaryDpi()
    {
        try
        {
            var primary = System.Windows.Forms.Screen.PrimaryScreen;
            if (primary != null)
            {
                return GetDpiScaleForScreen(primary) * 96.0;
            }
        }
        catch { }
        return 96.0;
    }

    #region Win32 Interop

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_LAYERED = 0x00080000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

    [System.Runtime.InteropServices.DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

    #endregion

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        Stop();
    }
}
