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

            try
            {
                foreach (var word in matchedWords)
                {
                    var (_, canvas) = GetOrCreateOverlay(word.Screen);
                    if (canvas == null) continue;

                    AddHighlightElement(canvas, word.ScreenRect, word.Screen);
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("KeywordHighlightService: Error showing highlight: {Error}", ex.Message);
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
                    SetWindowDisplayAffinity(hwnd, WDA_EXCLUDEFROMCAPTURE);
                };

                window.Show();

                var entry = (window, canvas);
                _screenOverlays[key] = entry;

                App.Logger?.Debug("KeywordHighlightService: Created overlay for {Screen}", key);
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
            var dpiScale = GetDpiScaleForScreen(screen);
            if (dpiScale <= 0) dpiScale = 1.0;

            // Convert from absolute screen coords to local canvas coords (in WPF DIPs)
            double localX = (screenRect.X - screen.Bounds.Left) / dpiScale;
            double localY = (screenRect.Y - screen.Bounds.Top) / dpiScale;
            double width = screenRect.Width / dpiScale;
            double height = screenRect.Height / dpiScale;

            var pinkColor = Color.FromRgb(0xFF, 0x69, 0xB4);

            var border = new Border
            {
                Width = width + 8,
                Height = height + 8,
                CornerRadius = new CornerRadius(4),
                BorderBrush = new SolidColorBrush(pinkColor),
                BorderThickness = new Thickness(2),
                Background = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0x69, 0xB4)),
                Opacity = 0,
                Effect = new DropShadowEffect
                {
                    Color = pinkColor,
                    BlurRadius = 20,
                    ShadowDepth = 0,
                    Opacity = 0.8
                }
            };

            Canvas.SetLeft(border, localX - 4);
            Canvas.SetTop(border, localY - 4);
            canvas.Children.Add(border);

            AnimateHighlight(border, canvas);
        }

        private void AnimateHighlight(Border border, Canvas canvas)
        {
            var totalMs = App.Settings?.Current?.KeywordHighlightDurationMs ?? 600;
            var popIn = (int)(totalMs * 0.13);
            var holdEnd = (int)(totalMs * 0.42);

            var anim = new DoubleAnimationUsingKeyFrames();
            anim.KeyFrames.Add(new LinearDoubleKeyFrame(1.0, TimeSpan.FromMilliseconds(popIn)));     // pop in
            anim.KeyFrames.Add(new LinearDoubleKeyFrame(1.0, TimeSpan.FromMilliseconds(holdEnd)));   // hold
            anim.KeyFrames.Add(new LinearDoubleKeyFrame(0.0, TimeSpan.FromMilliseconds(totalMs)));   // fade out

            Storyboard.SetTarget(anim, border);
            Storyboard.SetTargetProperty(anim, new PropertyPath(UIElement.OpacityProperty));

            var storyboard = new Storyboard();
            storyboard.Children.Add(anim);
            storyboard.Completed += (s, e) =>
            {
                try { canvas.Children.Remove(border); } catch { }
            };

            storyboard.Begin();
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
