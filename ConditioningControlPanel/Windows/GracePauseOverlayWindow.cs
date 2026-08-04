using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using ConditioningControlPanel.Localization;
using Screen = System.Windows.Forms.Screen;

namespace ConditioningControlPanel
{
    /// <summary>
    /// The "grace pause" card (#735): a small themed panel shown centred on every screen that has a
    /// mandatory-video window while that video is paused by the first panic press. Title, a Resume
    /// button and a live countdown to the automatic resume.
    ///
    /// WHY A SEPARATE WINDOW rather than content inside the video window: the classic render path
    /// hosts LibVLC in a <c>VideoView</c>, i.e. an <c>HwndHost</c>. WPF content can never render
    /// above an airspace child HWND, so a button parented into the video window would be invisible on
    /// exactly the render path that is still the fallback. A dedicated topmost tool window has no
    /// such problem.
    ///
    /// The video windows re-assert HWND_TOPMOST themselves, so this window does the same on a timer
    /// (the identical trick the attention <c>FloatingText</c> targets use to stay above the video).
    /// Built in code rather than XAML to match that neighbour: everything interesting here is
    /// per-screen placement and Win32 z-order, neither of which XAML expresses.
    /// </summary>
    internal sealed class GracePauseOverlayWindow
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

        private const double CardWidth = 360;
        private const double CardHeight = 200;
        /// <summary>Topmost re-assert cadence. The video windows below re-assert their own z-order,
        /// so a one-shot SetWindowPos at Show() is not enough to stay reliably on top.</summary>
        private const int TopmostReassertMs = 300;

        private readonly Window _win;
        private readonly TextBlock _countdown;
        private readonly DispatcherTimer _topmostTimer;
        private IntPtr _hwnd;
        private bool _closed;

        public GracePauseOverlayWindow(Screen screen, Action onResume)
        {
            var accent = (Color)ColorConverter.ConvertFromString("#FF69B4");
            var cardBg = (Color)ColorConverter.ConvertFromString("#1A1A2E");
            var accentBrush = new SolidColorBrush(accent);
            accentBrush.Freeze();

            var glyph = new TextBlock
            {
                Text = "⏸",
                FontSize = 40,
                Foreground = accentBrush,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 2)
            };

            var title = new TextBlock
            {
                Text = Loc.Get("video_grace_paused_title"),
                FontSize = 24,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 12)
            };

            var resumeContent = new TextBlock
            {
                Text = "▶  " + Loc.Get("btn_video_grace_resume"),
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White
            };

            var resumeButton = new Button
            {
                Content = resumeContent,
                Padding = new Thickness(26, 8, 26, 8),
                HorizontalAlignment = HorizontalAlignment.Center,
                Cursor = System.Windows.Input.Cursors.Hand,
                BorderThickness = new Thickness(0),
                Background = accentBrush,
                Foreground = Brushes.White,
                // Code-built windows do not inherit MainWindow.xaml's resource dictionary, so the
                // pink pill is a local template rather than the app-wide button style.
                Template = BuildPillTemplate()
            };
            resumeButton.Click += (s, e) =>
            {
                try { onResume?.Invoke(); }
                catch (Exception ex) { App.Logger?.Debug("GracePauseOverlay: resume handler threw - {Error}", ex.Message); }
            };

            _countdown = new TextBlock
            {
                Text = string.Empty,
                FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromRgb(0xB0, 0xA8, 0xC8)),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 14, 0, 0)
            };

            var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            stack.Children.Add(glyph);
            stack.Children.Add(title);
            stack.Children.Add(resumeButton);
            stack.Children.Add(_countdown);

            var card = new Border
            {
                Background = new SolidColorBrush(cardBg),
                BorderBrush = accentBrush,
                BorderThickness = new Thickness(2),
                CornerRadius = new CornerRadius(16),
                Padding = new Thickness(24, 18, 24, 18),
                Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 24, ShadowDepth = 0, Opacity = 0.75 },
                Child = stack
            };

            _win = new Window
            {
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = Brushes.Transparent,
                Topmost = true,
                ShowInTaskbar = false,
                ShowActivated = false,   // never steal focus from the (possibly strict) video window
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Width = CardWidth,
                Height = CardHeight,
                Content = card
            };

            // Screen bounds are physical pixels; WPF Left/Top/Width/Height are DIPs.
            double dpi = 1.0;
            try { dpi = BubbleCountWindow.GetDpiForScreen(screen); }
            catch (Exception ex) { App.Logger?.Debug("GracePauseOverlay: DPI probe failed - {Error}", ex.Message); }
            if (dpi <= 0) dpi = 1.0;
            var b = screen.Bounds;
            _win.Left = (b.X + (b.Width - CardWidth * dpi) / 2.0) / dpi;
            _win.Top = (b.Y + (b.Height - CardHeight * dpi) / 2.0) / dpi;

            _win.SourceInitialized += (s, e) =>
            {
                _hwnd = new WindowInteropHelper(_win).Handle;
                if (_hwnd == IntPtr.Zero) return;
                // Tool window => out of Alt+Tab. Must be applied before the window is visible.
                var ex2 = GetWindowLong(_hwnd, GWL_EXSTYLE);
                ex2 |= WS_EX_TOOLWINDOW;
                ex2 &= ~WS_EX_APPWINDOW;
                SetWindowLong(_hwnd, GWL_EXSTYLE, ex2);
            };

            _topmostTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(TopmostReassertMs) };
            _topmostTimer.Tick += (s, e) => Reassert();

            // Close() is not the only route out (Alt+F4, a dispatcher shutdown, an owner tearing the
            // window down). Whichever one fires, the 300ms tick must stop — otherwise it keeps calling
            // SetWindowPos on a dead HWND for the rest of the session.
            _win.Closed += (s, e) =>
            {
                _closed = true;
                try { _topmostTimer.Stop(); } catch { }
            };

            _win.Loaded += (s, e) =>
            {
                if (_hwnd == IntPtr.Zero) _hwnd = new WindowInteropHelper(_win).Handle;
                Reassert();
                _topmostTimer.Start();
            };

            _win.Show();
        }

        /// <summary>Updates the "resuming in Ns" line. Safe to call after Close (no-op).</summary>
        public void SetRemainingSeconds(int seconds)
        {
            if (_closed) return;
            try { _countdown.Text = Loc.GetF("video_grace_auto_resume_in", Math.Max(0, seconds)); }
            catch (Exception ex) { App.Logger?.Debug("GracePauseOverlay: countdown update failed - {Error}", ex.Message); }
        }

        public void Close()
        {
            if (_closed) return;
            _closed = true;
            try { _topmostTimer.Stop(); } catch { }
            try { _win.Close(); }
            catch (Exception ex) { App.Logger?.Debug("GracePauseOverlay: close failed - {Error}", ex.Message); }
        }

        private void Reassert()
        {
            if (_closed || _hwnd == IntPtr.Zero) return;
            try { SetWindowPos(_hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE); }
            catch { }
        }

        /// <summary>Rounded pink pill with a hover/press tint — the app's accent button in miniature.</summary>
        private static ControlTemplate BuildPillTemplate()
        {
            var border = new FrameworkElementFactory(typeof(Border));
            border.Name = "PillBorder";
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(20));
            border.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background")
            { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
            border.SetBinding(Border.PaddingProperty, new System.Windows.Data.Binding("Padding")
            { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });

            var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            border.AppendChild(presenter);

            var template = new ControlTemplate(typeof(Button)) { VisualTree = border };

            var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(Border.OpacityProperty, 0.85, "PillBorder"));
            template.Triggers.Add(hover);

            var pressed = new Trigger { Property = ButtonBase_IsPressed, Value = true };
            pressed.Setters.Add(new Setter(Border.OpacityProperty, 0.7, "PillBorder"));
            template.Triggers.Add(pressed);

            template.Seal();
            return template;
        }

        private static readonly DependencyProperty ButtonBase_IsPressed =
            System.Windows.Controls.Primitives.ButtonBase.IsPressedProperty;
    }
}
