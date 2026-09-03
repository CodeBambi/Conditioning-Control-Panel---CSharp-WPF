using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Interop;
using System.Windows.Threading;
using ConditioningControlPanel.Localization;
using Screen = System.Windows.Forms.Screen;

namespace ConditioningControlPanel
{
    /// <summary>
    /// One quiet line along the bottom of a mandatory video, shown when a dismiss, a skip, an
    /// Alt+F4 or a panic press was refused BECAUSE Strict Lock is on.
    ///
    /// <para>WHY (#nsfw-chat, 2026-08-31): Spitfire lost an hour to a video that would not go away
    /// and it took two other members to work out that Force Strict Lock was the reason. The lock
    /// was working exactly as configured; the app simply never said so, and a window that eats
    /// every key you press is indistinguishable from a window that has crashed.</para>
    ///
    /// <para>Separate topmost tool window rather than content inside the video window, for the same
    /// reason as <see cref="GracePauseOverlayWindow"/>: the classic render path hosts LibVLC in an
    /// HwndHost, and WPF content can never draw above an airspace child HWND.</para>
    ///
    /// <para>It does not weaken the lock by one key. It is a label.</para>
    /// </summary>
    internal sealed class StrictLockNoticeWindow
    {
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_APPWINDOW = 0x00040000;
        private const int WS_EX_TRANSPARENT = 0x00000020;

        private const double NoticeWidth = 430;
        private const double NoticeHeight = 44;
        /// <summary>Distance from the bottom of the screen, in physical pixels.</summary>
        private const double BottomMargin = 70;
        /// <summary>The video windows re-assert their own z-order, so once is not enough.</summary>
        private const int TopmostReassertMs = 300;

        private readonly Window _win;
        private readonly DispatcherTimer _topmostTimer;
        private readonly DispatcherTimer _lifeTimer;
        private IntPtr _hwnd;
        private bool _closed;

        public StrictLockNoticeWindow(Screen screen, TimeSpan life)
        {
            var accentBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF69B4"));
            accentBrush.Freeze();

            var text = new TextBlock
            {
                Text = Loc.Get("video_strict_lock_notice"),
                FontSize = 13,
                Foreground = Brushes.White,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };

            var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            row.Children.Add(new TextBlock
            {
                Text = "\U0001F512",
                FontSize = 14,
                Foreground = accentBrush,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            });
            row.Children.Add(text);

            var pill = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0xE6, 0x1A, 0x1A, 0x2E)),
                BorderBrush = accentBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(16, 8, 16, 8),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Child = row
            };

            _win = new Window
            {
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = Brushes.Transparent,
                Topmost = true,
                ShowInTaskbar = false,
                ShowActivated = false,   // never steal focus from the strict video window
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.Manual,
                IsHitTestVisible = false,
                Width = NoticeWidth,
                Height = NoticeHeight,
                Content = pill
            };

            // Screen bounds are physical pixels; WPF Left/Top/Width/Height are DIPs.
            double dpi = 1.0;
            try { dpi = BubbleCountWindow.GetDpiForScreen(screen); }
            catch (Exception ex) { App.Logger?.Debug("StrictLockNotice: DPI probe failed - {Error}", ex.Message); }
            if (dpi <= 0) dpi = 1.0;
            var b = screen.Bounds;
            _win.Left = (b.X + (b.Width - NoticeWidth * dpi) / 2.0) / dpi;
            _win.Top = (b.Y + b.Height - (NoticeHeight * dpi) - BottomMargin) / dpi;

            _win.SourceInitialized += (s, e) =>
            {
                _hwnd = new WindowInteropHelper(_win).Handle;
                if (_hwnd == IntPtr.Zero) return;
                // Tool window => out of Alt+Tab. TRANSPARENT => clicks fall through to the video,
                // so a label can never become one more thing standing between the user and the app.
                var ex2 = GetWindowLong(_hwnd, GWL_EXSTYLE);
                ex2 |= WS_EX_TOOLWINDOW | WS_EX_TRANSPARENT;
                ex2 &= ~WS_EX_APPWINDOW;
                SetWindowLong(_hwnd, GWL_EXSTYLE, ex2);
            };

            _topmostTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(TopmostReassertMs) };
            _topmostTimer.Tick += (s, e) => Reassert();

            _lifeTimer = new DispatcherTimer { Interval = life };
            _lifeTimer.Tick += (s, e) => Close();

            _win.Closed += (s, e) =>
            {
                _closed = true;
                try { _topmostTimer.Stop(); } catch { }
                try { _lifeTimer.Stop(); } catch { }
            };

            _win.Loaded += (s, e) =>
            {
                if (_hwnd == IntPtr.Zero) _hwnd = new WindowInteropHelper(_win).Handle;
                Reassert();
                _topmostTimer.Start();
                _lifeTimer.Start();
            };

            _win.Show();
        }

        public void Close()
        {
            if (_closed) return;
            _closed = true;
            try { _topmostTimer.Stop(); } catch { }
            try { _lifeTimer.Stop(); } catch { }
            try { _win.Close(); }
            catch (Exception ex) { App.Logger?.Debug("StrictLockNotice: close failed - {Error}", ex.Message); }
        }

        private void Reassert()
        {
            if (_closed || _hwnd == IntPtr.Zero) return;
            try { SetWindowPos(_hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE); }
            catch { }
        }
    }
}
