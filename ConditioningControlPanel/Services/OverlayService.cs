using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace ConditioningControlPanel.Services;

/// <summary>
/// Service that manages screen overlays: Pink Filter and Spiral
/// </summary>
public class OverlayService : IDisposable
{
    private readonly List<Window> _pinkFilterWindows = new();
    private readonly List<Window> _spiralWindows = new();
    private readonly List<MediaElement> _spiralMediaElements = new();
    private bool _isRunning;
    private DispatcherTimer? _updateTimer;
    private DispatcherTimer? _gifLoopTimer;
    private bool _isDisposed;
    private bool _isGifSpiral;
    private string _spiralPath = "";

    public bool IsRunning => _isRunning;

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

            if (settings.SpiralEnabled && !string.IsNullOrEmpty(settings.SpiralPath))
            {
                StartSpiral();
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
            }
            else
            {
                StopPinkFilter();
            }
            
            // Spiral
            if (settings.SpiralEnabled && settings.PlayerLevel >= 10 && !string.IsNullOrEmpty(settings.SpiralPath))
            {
                if (_spiralWindows.Count == 0)
                    StartSpiral();
            }
            else
            {
                StopSpiral();
            }
        });
        
        App.Logger?.Debug("Overlays refreshed - Pink: {Pink}, Spiral: {Spiral}", 
            _pinkFilterWindows.Count > 0, _spiralWindows.Count > 0);
    }

    private void UpdateOverlays(object? sender, EventArgs e)
    {
        var settings = App.Settings.Current;

        // Check level requirement
        if (settings.PlayerLevel < 10)
        {
            StopPinkFilter();
            StopSpiral();
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

        if (settings.SpiralEnabled && !string.IsNullOrEmpty(settings.SpiralPath) && _spiralWindows.Count == 0)
        {
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
            var dpiScale = GetDpiScale();

            var window = new Window
            {
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = Brushes.Transparent,
                Topmost = true,
                ShowInTaskbar = false,
                IsHitTestVisible = false,
                Left = screen.Bounds.X / dpiScale,
                Top = screen.Bounds.Y / dpiScale,
                Width = screen.Bounds.Width / dpiScale,
                Height = screen.Bounds.Height / dpiScale,
                WindowState = WindowState.Normal
            };

            var pinkOverlay = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(255, 105, 180)),
                Opacity = opacity / 100.0
            };

            window.Content = pinkOverlay;
            
            // Set extended window styles after window is created
            window.SourceInitialized += (s, e) =>
            {
                MakeClickThrough(window);
            };
            
            window.Show();
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
        var opacity = App.Settings.Current.PinkFilterOpacity / 100.0;
        foreach (var window in _pinkFilterWindows)
        {
            if (window.Content is Border border)
            {
                border.Opacity = opacity;
            }
        }
    }

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
            var dpiScale = GetDpiScale();

            var window = new Window
            {
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = Brushes.Transparent,
                Topmost = true,
                ShowInTaskbar = false,
                IsHitTestVisible = false,
                Left = screen.Bounds.X / dpiScale,
                Top = screen.Bounds.Y / dpiScale,
                Width = screen.Bounds.Width / dpiScale,
                Height = screen.Bounds.Height / dpiScale,
                WindowState = WindowState.Normal
            };

            // Use MediaElement for all media types (works for GIF, MP4, etc.)
            var media = new MediaElement
            {
                Source = new Uri(spiralPath),
                LoadedBehavior = MediaState.Manual,
                UnloadedBehavior = MediaState.Manual,
                Stretch = Stretch.UniformToFill,
                Opacity = opacity / 100.0,
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
            
            window.Content = media;

            window.SourceInitialized += (s, e) =>
            {
                MakeClickThrough(window);
            };

            window.Show();
            
            // Start playing after window is shown
            media.Play();

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
        var opacity = App.Settings.Current.SpiralOpacity / 100.0;
        foreach (var media in _spiralMediaElements)
        {
            media.Opacity = opacity;
        }
    }

    #endregion

    #region Helpers

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

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

    #endregion

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        Stop();
    }
}