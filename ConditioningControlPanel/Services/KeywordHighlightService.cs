using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using WinForms = System.Windows.Forms;

namespace ConditioningControlPanel.Services
{
    public class OcrWordHit
    {
        public string Text { get; init; } = "";
        public System.Drawing.Rectangle ScreenRect { get; init; }
        public WinForms.Screen Screen { get; init; } = null!;
    }

    public class KeywordHighlightService : IDisposable
    {
        private readonly Dictionary<string, (Window window, Canvas canvas)> _screenOverlays = new();
        private bool _disposed;

        public KeywordHighlightService()
        {
            // Ensure overlays close when the app exits, regardless of disposal order.
            // Without this, ShutdownMode=OnLastWindowClose keeps the process alive.
            if (Application.Current != null)
                Application.Current.Exit += (_, _) => Dispose();
        }

        public void ShowHighlight(List<OcrWordHit> matchedWords)
        {
            if (_disposed || matchedWords == null || matchedWords.Count == 0) return;
            if (App.Settings?.Current?.KeywordHighlightEnabled != true) return;

            App.Logger?.Information("KeywordHighlight.Show: {Count} word(s)", matchedWords.Count);

            try
            {
                int added = 0;
                var touchedScreens = new HashSet<string>();

                foreach (var word in matchedWords)
                {
                    if (word?.Screen == null)
                    {
                        App.Logger?.Warning("KeywordHighlight.Show: word has null Screen, skipping");
                        continue;
                    }

                    var (window, canvas) = GetOrCreateOverlay(word.Screen);
                    if (canvas == null || window == null) continue;

                    AddHighlightElement(canvas, word.ScreenRect, word.Screen);
                    touchedScreens.Add(word.Screen.DeviceName);
                    added++;
                }

                // Force each touched overlay window to topmost on every fire.
                // Without this, the overlay can get buried behind the avatar tube
                // or other topmost WPF windows the first time they render, and
                // subsequent highlights never become visible even though their
                // Canvas elements are added.
                foreach (var key in touchedScreens)
                {
                    if (_screenOverlays.TryGetValue(key, out var entry))
                    {
                        try
                        {
                            var hwnd = new WindowInteropHelper(entry.window).Handle;
                            if (hwnd != IntPtr.Zero)
                                SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0,
                                    SWP_NOACTIVATE | SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
                        }
                        catch (Exception ex)
                        {
                            App.Logger?.Debug("KeywordHighlight: topmost bump failed: {Error}", ex.Message);
                        }
                    }
                }

                App.Logger?.Information("KeywordHighlight.Show: added {Count} element(s) across {Screens} overlay(s)",
                    added, _screenOverlays.Count);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning("KeywordHighlightService: Error showing highlight: {Error}", ex.Message);
            }
        }

        private (Window window, Canvas canvas) GetOrCreateOverlay(WinForms.Screen screen)
        {
            var key = screen.DeviceName;
            if (_screenOverlays.TryGetValue(key, out var existing))
                return existing;

            try
            {
                var wpfBounds = GetWpfScreenBounds(screen);

                var canvas = new Canvas
                {
                    Background = Brushes.Transparent,
                    ClipToBounds = true
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
                    Left = wpfBounds.Left,
                    Top = wpfBounds.Top,
                    Width = wpfBounds.Width,
                    Height = wpfBounds.Height,
                    Content = canvas
                };

                var targetScreen = screen;
                window.SourceInitialized += (s, e) =>
                {
                    var hwnd = new WindowInteropHelper(window).Handle;
                    var exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
                    SetWindowLong(hwnd, GWL_EXSTYLE,
                        exStyle | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TRANSPARENT | WS_EX_LAYERED);

                    PositionWindowOnScreen(window, targetScreen);

                    // Exclude from screen capture so our highlights don't get OCR'd
                    // (unless user wants highlights visible in streams/recordings)
                    if (App.Settings?.Current?.OcrHighlightVisibleInCapture != true)
                        SetWindowDisplayAffinity(hwnd, WDA_EXCLUDEFROMCAPTURE);
                };

                window.Show();

                var entry = (window, canvas);
                _screenOverlays[key] = entry;

                App.Logger?.Information("KeywordHighlight: Created overlay for {Screen} bounds=({L},{T},{W}x{H})",
                    key, wpfBounds.Left, wpfBounds.Top, wpfBounds.Width, wpfBounds.Height);
                return entry;
            }
            catch (Exception ex)
            {
                App.Logger?.Error("KeywordHighlightService: Failed to create overlay: {Error}", ex.Message);
                return (null!, null!);
            }
        }

        private void AddHighlightElement(Canvas canvas, System.Drawing.Rectangle screenRect, WinForms.Screen screen)
        {
            // The OCR service captures via Graphics.CopyFromScreen(screen.Bounds.Left,
            // screen.Bounds.Top, bounds.Size) and stores word rects as:
            //   ScreenRect.X = screen.Bounds.Left + word.BoundingRect.X
            // Both screen.Bounds (WinForms) and the OCR bitmap coords are in the same
            // coordinate system — whatever CopyFromScreen produced. With PerMonitorV2
            // awareness that's physical pixels for the DIP-aware process, but on some
            // Windows configurations the values come back in logical/DIP units instead.
            //
            // The previous code divided by the monitor's DPI scale (e.g. 1.25). But the
            // WPF overlay window itself was sized using primaryScale, and the Canvas
            // inside a per-monitor-aware WPF Window already uses DIPs at the window's
            // effective monitor scale. Dividing again in this method double-corrects
            // for DPI and places elements at wrong coordinates — which is exactly the
            // "highlight appears on wrong word" symptom the user is seeing.
            //
            // Fix: treat (screenRect.X - bounds.Left) as the word's offset within the
            // captured bitmap, then express it as a fraction of the bitmap size and
            // multiply by the canvas size. This is DPI-independent because the canvas
            // and the bitmap cover the same region of screen real-estate.
            double canvasW = canvas.ActualWidth > 0 ? canvas.ActualWidth : screen.Bounds.Width;
            double canvasH = canvas.ActualHeight > 0 ? canvas.ActualHeight : screen.Bounds.Height;
            double bitmapW = Math.Max(1, screen.Bounds.Width);
            double bitmapH = Math.Max(1, screen.Bounds.Height);

            double offsetX = screenRect.X - screen.Bounds.Left;
            double offsetY = screenRect.Y - screen.Bounds.Top;

            double localX = offsetX * canvasW / bitmapW;
            double localY = offsetY * canvasH / bitmapH;
            double width  = screenRect.Width  * canvasW / bitmapW;
            double height = screenRect.Height * canvasH / bitmapH;

            App.Logger?.Information(
                "KeywordHighlight.coord: screenRect=({Sx},{Sy},{Sw}x{Sh}) bounds.L={Bl} bounds.T={Bt} bounds.W={Bw} bounds.H={Bh} canvas={Cw}x{Ch} → local=({Lx:0},{Ly:0},{Lw:0}x{Lh:0})",
                screenRect.X, screenRect.Y, screenRect.Width, screenRect.Height,
                screen.Bounds.Left, screen.Bounds.Top, screen.Bounds.Width, screen.Bounds.Height,
                canvasW, canvasH,
                localX, localY, width, height);

            // User-configurable highlight color (defaults to neon pink). Parsed at
            // render time so live-edits from the settings picker take effect on the
            // next match without restart.
            var highlightColor = ParseHighlightColor(App.Settings?.Current?.KeywordHighlightColor);
            var fillColor = Color.FromArgb(0x80, highlightColor.R, highlightColor.G, highlightColor.B);

            // Padding around the word so the glow is clearly visible outside the text.
            const double PAD = 10;

            // Use a Shape (Rectangle) rather than a Border with an Effect. Borders
            // with DropShadowEffect inside a transparent-background Canvas silently
            // fail to render on several GPU/driver combos — shapes bypass the effect
            // pipeline entirely and always render.
            var rect = new System.Windows.Shapes.Rectangle
            {
                Width = width + PAD * 2,
                Height = height + PAD * 2,
                RadiusX = 6,
                RadiusY = 6,
                Stroke = new SolidColorBrush(highlightColor),
                StrokeThickness = 4,
                Fill = new SolidColorBrush(fillColor),
                Opacity = 1.0, // start fully visible; animation fades out
                SnapsToDevicePixels = true,
            };

            Canvas.SetLeft(rect, localX - PAD);
            Canvas.SetTop(rect, localY - PAD);
            canvas.Children.Add(rect);

            App.Logger?.Information("KeywordHighlight: added element at ({X:0},{Y:0}) size={W:0}x{H:0}",
                localX - PAD, localY - PAD, rect.Width, rect.Height);

            AnimateHighlight(rect, canvas);
        }

        /// <summary>
        /// Parses a hex color string (<c>#RRGGBB</c> or <c>#AARRGGBB</c>) from settings.
        /// Falls back to neon pink (<c>#FF69B4</c>) on null/empty/invalid input.
        /// </summary>
        private static Color ParseHighlightColor(string? hex)
        {
            if (!string.IsNullOrWhiteSpace(hex))
            {
                try
                {
                    var obj = ColorConverter.ConvertFromString(hex);
                    if (obj is Color c) return c;
                }
                catch { }
            }
            return Color.FromRgb(0xFF, 0x69, 0xB4);
        }

        private void AnimateHighlight(UIElement element, Canvas canvas)
        {
            var totalMs = App.Settings?.Current?.KeywordHighlightDurationMs ?? 1500;
            // Hold at full opacity for 60% of the duration, then fade out the remaining 40%.
            var holdMs = (int)(totalMs * 0.60);
            var fadeMs = Math.Max(1, totalMs - holdMs);

            // Direct-target double animation on the element's Opacity property. This
            // is simpler than a Storyboard and avoids the known WPF gotcha where
            // Storyboard.Begin() with no namescope can silently fail to start.
            var anim = new DoubleAnimation
            {
                From = 1.0,
                To = 0.0,
                BeginTime = TimeSpan.FromMilliseconds(holdMs),
                Duration = new Duration(TimeSpan.FromMilliseconds(fadeMs)),
                FillBehavior = FillBehavior.Stop,
            };
            anim.Completed += (_, _) =>
            {
                try
                {
                    element.Opacity = 0;
                    canvas.Children.Remove(element);
                }
                catch { }
            };

            element.BeginAnimation(UIElement.OpacityProperty, anim);
        }

        /// <summary>
        /// Updates the display affinity on all existing overlay windows
        /// so the capture-visibility setting takes effect immediately.
        /// </summary>
        public void RefreshCaptureVisibility()
        {
            if (_disposed) return;
            bool visible = App.Settings?.Current?.OcrHighlightVisibleInCapture == true;
            uint affinity = visible ? 0u : WDA_EXCLUDEFROMCAPTURE;

            foreach (var (window, _) in _screenOverlays.Values)
            {
                try
                {
                    var hwnd = new WindowInteropHelper(window).Handle;
                    if (hwnd != IntPtr.Zero)
                        SetWindowDisplayAffinity(hwnd, affinity);
                }
                catch { }
            }
        }

        #region DPI / Positioning Helpers

        private struct WpfScreenBounds
        {
            public double Left, Top, Width, Height;
        }

        private struct PhysicalScreenBounds
        {
            public int Left, Top, Width, Height;
        }

        private WpfScreenBounds GetWpfScreenBounds(WinForms.Screen screen)
        {
            double primaryDpi = GetPrimaryMonitorDpi();
            double primaryScale = primaryDpi / 96.0;
            var physicalBounds = GetPhysicalScreenBounds(screen);

            return new WpfScreenBounds
            {
                Left = physicalBounds.Left / primaryScale,
                Top = physicalBounds.Top / primaryScale,
                Width = physicalBounds.Width / primaryScale,
                Height = physicalBounds.Height / primaryScale
            };
        }

        private PhysicalScreenBounds GetPhysicalScreenBounds(WinForms.Screen screen)
        {
            try
            {
                var point = new POINT
                {
                    X = screen.Bounds.X + screen.Bounds.Width / 2,
                    Y = screen.Bounds.Y + screen.Bounds.Height / 2
                };
                var hMonitor = MonitorFromPoint(point, 2);

                if (hMonitor != IntPtr.Zero)
                {
                    var monitorInfo = new MONITORINFO();
                    monitorInfo.cbSize = Marshal.SizeOf(typeof(MONITORINFO));

                    if (GetMonitorInfo(hMonitor, ref monitorInfo))
                    {
                        return new PhysicalScreenBounds
                        {
                            Left = monitorInfo.rcMonitor.Left,
                            Top = monitorInfo.rcMonitor.Top,
                            Width = monitorInfo.rcMonitor.Right - monitorInfo.rcMonitor.Left,
                            Height = monitorInfo.rcMonitor.Bottom - monitorInfo.rcMonitor.Top
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("KeywordHighlightService: GetPhysicalScreenBounds failed: {Error}", ex.Message);
            }

            return new PhysicalScreenBounds
            {
                Left = screen.Bounds.Left,
                Top = screen.Bounds.Top,
                Width = screen.Bounds.Width,
                Height = screen.Bounds.Height
            };
        }

        private void PositionWindowOnScreen(Window window, WinForms.Screen screen)
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return;

            var bounds = GetPhysicalScreenBounds(screen);
            SetWindowPos(hwnd, HWND_TOPMOST,
                bounds.Left, bounds.Top, bounds.Width, bounds.Height,
                SWP_NOACTIVATE | SWP_SHOWWINDOW);
        }

        private double GetDpiScaleForScreen(WinForms.Screen screen)
        {
            try
            {
                var hMonitor = MonitorFromPoint(
                    new POINT { X = screen.Bounds.X + 1, Y = screen.Bounds.Y + 1 }, 2);
                if (hMonitor != IntPtr.Zero)
                {
                    var result = GetDpiForMonitor(hMonitor, 0, out uint dpiX, out _);
                    if (result == 0)
                        return dpiX / 96.0;
                }
            }
            catch { }
            return 1.0;
        }

        private double GetPrimaryMonitorDpi()
        {
            try
            {
                var primary = WinForms.Screen.PrimaryScreen;
                if (primary != null)
                {
                    var hMonitor = MonitorFromPoint(
                        new POINT { X = primary.Bounds.X + 1, Y = primary.Bounds.Y + 1 }, 2);
                    if (hMonitor != IntPtr.Zero)
                    {
                        var result = GetDpiForMonitor(hMonitor, 0, out uint dpiX, out _);
                        if (result == 0) return dpiX;
                    }
                }
            }
            catch { }
            return 96.0;
        }

        #endregion

        #region Win32 P/Invoke

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_LAYERED = 0x00080000;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const uint WDA_EXCLUDEFROMCAPTURE = 0x11;
        private static readonly IntPtr HWND_TOPMOST = new(-1);
        private const uint SWP_SHOWWINDOW = 0x0040;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOSIZE = 0x0001;

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X; public int Y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hwnd, int index);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

        [DllImport("user32.dll")]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

        [DllImport("shcore.dll")]
        private static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")]
        private static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint dwAffinity);

        #endregion

        #region IDisposable

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            if (Application.Current?.Dispatcher?.CheckAccess() == true)
            {
                CloseAllOverlays();
            }
            else
            {
                try { Application.Current?.Dispatcher?.Invoke(CloseAllOverlays); } catch { }
            }
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
}
