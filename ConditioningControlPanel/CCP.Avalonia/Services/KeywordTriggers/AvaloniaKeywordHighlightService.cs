using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.Settings;
using ConditioningControlPanel.Models;
using Microsoft.Extensions.DependencyInjection;

namespace ConditioningControlPanel.Avalonia.Services.KeywordTriggers;

/// <summary>
/// Avalonia implementation of the keyword/OCR highlight overlay. Creates one
/// transparent, click-through, topmost window per screen and draws fading
/// rectangles around matched words.
/// </summary>
public sealed class AvaloniaKeywordHighlightService : IKeywordHighlightService, IDisposable
{
    private readonly Dictionary<string, (Window window, Canvas canvas)> _screenOverlays = new();
    private readonly IScreenProvider _screenProvider;
    private readonly ISettingsService _settings;
    private readonly ILogger<AvaloniaKeywordHighlightService> _logger;
    private AppSettings? _hookedSettings;
    private bool _disposed;

    public AvaloniaKeywordHighlightService(
        IScreenProvider screenProvider,
        ISettingsService settings,
        ILogger<AvaloniaKeywordHighlightService> logger)
    {
        _screenProvider = screenProvider ?? throw new ArgumentNullException(nameof(screenProvider));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // WPF parity (MainWindow.Awareness.cs:534, MainWindow.KeywordTriggers.cs:214): the
        // OcrHighlightVisibleInCapture toggle must flip display affinity on EXISTING overlay
        // windows immediately. The WPF head wires the two checkboxes to RefreshCaptureVisibility;
        // here we subscribe to the settings model instead so every writer (Awareness tab,
        // Keyword Triggers tab, remote control, presets) gets the live behavior.
        _settings.CurrentReplaced += OnSettingsCurrentReplaced;
        HookSettings(_settings.Current);
    }

    private void HookSettings(AppSettings? settings)
    {
        if (_hookedSettings != null)
            _hookedSettings.PropertyChanged -= OnSettingsPropertyChanged;
        _hookedSettings = settings;
        if (settings != null)
            settings.PropertyChanged += OnSettingsPropertyChanged;
    }

    private void OnSettingsCurrentReplaced()
    {
        HookSettings(_settings.Current);
        RefreshCaptureVisibility();
    }

    private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AppSettings.OcrHighlightVisibleInCapture))
            RefreshCaptureVisibility();
    }

    /// <summary>
    /// Unique per-monitor key. WPF keys overlays by the guaranteed-unique GDI DeviceName
    /// (KeywordHighlightService.cs:96); Avalonia's Screen.DisplayName is the friendly
    /// monitor model name, identical for same-model monitors, so two screens would collapse
    /// into one overlay and highlights would land on the wrong monitor. Compose with the
    /// bounds origin, which is unique per screen within a scan cycle.
    /// </summary>
    private static string ScreenKey(ScreenInfo screen) =>
        $"{screen.Name}|{screen.Bounds.X},{screen.Bounds.Y}";

    public void ShowHighlight(List<OcrWordHit> matchedWords)
    {
        if (_disposed || matchedWords == null || matchedWords.Count == 0) return;

        Dispatcher.UIThread.Post(() =>
        {
            if (_disposed) return;
            var settings = _settings.Current;
            if (settings?.KeywordHighlightEnabled != true) return;

            try
            {
                int added = 0;
                var touchedScreens = new HashSet<string>();

                foreach (var word in matchedWords)
                {
                    if (word?.Screen == null) continue;

                    var (window, canvas) = GetOrCreateOverlay(word.Screen);
                    if (canvas == null || window == null) continue;

                    AddHighlightElement(canvas, word.ScreenRect, word.Screen);
                    touchedScreens.Add(ScreenKey(word.Screen));
                    added++;
                }

                foreach (var name in touchedScreens)
                {
                    if (_screenOverlays.TryGetValue(name, out var entry))
                    {
                        try
                        {
                            entry.window.Topmost = false;
                            entry.window.Topmost = true;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogDebug(ex, "KeywordHighlight: topmost bump failed");
                        }
                    }
                }

                _logger.LogInformation("KeywordHighlight.Show: added {Count} element(s) across {Screens} overlay(s)",
                    added, _screenOverlays.Count);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "KeywordHighlightService: Error showing highlight");
            }
        }, DispatcherPriority.Background);
    }

    public void RefreshCaptureVisibility()
    {
        if (_disposed) return;
        if (!OperatingSystem.IsWindows()) return;
        if (!Dispatcher.UIThread.CheckAccess())
        {
            // Settings can be written from any thread (remote control); window handles are
            // only touched on the UI thread.
            Dispatcher.UIThread.Post(RefreshCaptureVisibility);
            return;
        }

        bool visible = _settings.Current?.OcrHighlightVisibleInCapture == true;
        uint affinity = visible ? 0u : WDA_EXCLUDEFROMCAPTURE;

        foreach (var (window, _) in _screenOverlays.Values)
        {
            try
            {
                var handle = window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
                if (handle != IntPtr.Zero)
                    SetWindowDisplayAffinity(handle, affinity);
            }
            catch { }
        }
    }

    private (Window? window, Canvas? canvas) GetOrCreateOverlay(ScreenInfo screen)
    {
        var key = ScreenKey(screen);
        if (_screenOverlays.TryGetValue(key, out var existing))
            return existing;

        try
        {
            var scaling = screen.Scaling > 0 ? screen.Scaling : 1.0;
            var bounds = screen.Bounds;

            var canvas = new Canvas
            {
                Background = Brushes.Transparent,
                ClipToBounds = true
            };

            var window = new Window
            {
                WindowDecorations = WindowDecorations.None,
                Background = Brushes.Transparent,
                TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent },
                Topmost = true,
                ShowInTaskbar = false,
                ShowActivated = false,
                Focusable = false,
                IsHitTestVisible = false,
                CanResize = false,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Position = new PixelPoint((int)bounds.X, (int)bounds.Y),
                Width = bounds.Width / scaling,
                Height = bounds.Height / scaling,
                Content = canvas
            };

            window.Opened += (_, _) =>
            {
                try
                {
                    var handle = window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
                    if (handle != IntPtr.Zero)
                    {
                        var exStyle = GetWindowLong(handle, GWL_EXSTYLE);
                        SetWindowLong(handle, GWL_EXSTYLE,
                            exStyle | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TRANSPARENT | WS_EX_LAYERED);

                        bool visible = _settings.Current?.OcrHighlightVisibleInCapture == true;
                        if (!visible)
                            SetWindowDisplayAffinity(handle, WDA_EXCLUDEFROMCAPTURE);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "KeywordHighlight: failed to apply overlay styles");
                }
            };

            window.Show();

            var entry = (window, canvas);
            _screenOverlays[key] = entry;

            _logger.LogInformation("KeywordHighlight: Created overlay for {Screen} bounds=({L},{T},{W}x{H}) scale={Scale}",
                key, bounds.X, bounds.Y, bounds.Width, bounds.Height, scaling);
            return entry;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "KeywordHighlightService: Failed to create overlay");
            return (null, null);
        }
    }

    private void AddHighlightElement(Canvas canvas, ConditioningControlPanel.Core.Platform.PixelRect screenRect, ScreenInfo screen)
    {
        var scaling = screen.Scaling > 0 ? screen.Scaling : 1.0;
        var bounds = screen.Bounds;

        double offsetX = screenRect.X - bounds.X;
        double offsetY = screenRect.Y - bounds.Y;

        double localX = offsetX / scaling;
        double localY = offsetY / scaling;
        double width = screenRect.Width / scaling;
        double height = screenRect.Height / scaling;

        var highlightColor = ParseHighlightColor(_settings.Current?.KeywordHighlightColor);
        var fillColor = new Color(0x80, highlightColor.R, highlightColor.G, highlightColor.B);

        const double PAD = 10;

        var rect = new Rectangle
        {
            Width = width + PAD * 2,
            Height = height + PAD * 2,
            RadiusX = 6,
            RadiusY = 6,
            Stroke = new SolidColorBrush(highlightColor),
            StrokeThickness = 4,
            Fill = new SolidColorBrush(fillColor),
            Opacity = 1.0
        };

        Canvas.SetLeft(rect, localX - PAD);
        Canvas.SetTop(rect, localY - PAD);
        canvas.Children.Add(rect);

        var totalMs = _settings.Current?.KeywordHighlightDurationMs ?? 1500;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(totalMs), IsEnabled = true };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            try
            {
                rect.Opacity = 0;
                canvas.Children.Remove(rect);
            }
            catch { }
        };
    }

    private static Color ParseHighlightColor(string? hex)
    {
        if (!string.IsNullOrWhiteSpace(hex))
        {
            try
            {
                return Color.Parse(hex);
            }
            catch { }
        }
        return Color.FromRgb(0xFF, 0x69, 0xB4);
    }

    #region Win32 P/Invoke

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_LAYERED = 0x00080000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const uint WDA_EXCLUDEFROMCAPTURE = 0x11;

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

    [DllImport("user32.dll")]
    private static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint dwAffinity);

    #endregion

    #region IDisposable

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _settings.CurrentReplaced -= OnSettingsCurrentReplaced;
        HookSettings(null);

        // WPF parity (KeywordHighlightService.Dispose): close synchronously when already on
        // the dispatcher thread so shutdown-path disposal cannot outrun the dispatcher; only
        // fall back to Post from background threads.
        if (Dispatcher.UIThread.CheckAccess())
            CloseAllOverlays();
        else
            Dispatcher.UIThread.Post(CloseAllOverlays);
    }

    private void CloseAllOverlays()
    {
        foreach (var (window, canvas) in _screenOverlays.Values)
        {
            try { canvas?.Children.Clear(); } catch { }
            try { window?.Close(); } catch { }
        }
        _screenOverlays.Clear();
    }

    #endregion
}
