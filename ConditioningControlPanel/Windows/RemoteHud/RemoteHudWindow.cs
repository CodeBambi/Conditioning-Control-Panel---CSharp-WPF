using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Screen = System.Windows.Forms.Screen;

namespace ConditioningControlPanel.RemoteHud
{
    /// <summary>
    /// Remote Control v2: the subject's own pill, top centre of the monitor CCP is on, up while a
    /// controller holds the remote. Who has it, for how long, what they did last, and three
    /// buttons back: More, Easy, Stop.
    ///
    /// <para>Z-order: topmost, and deliberately UNOWNED. Showing a window whose native owner chain
    /// reaches MainWindow lifts the whole chain over the user's current app (the companion bubble
    /// trap, e29358238), and this pill appears every time a controller connects. Unowned visible
    /// windows hold OnLastWindowClose open, so MainWindow.OnClosing disposes it on a real exit.</para>
    ///
    /// <para>Never takes focus: ShowActivated is false and the HWND wears WS_EX_NOACTIVATE, so a
    /// press on More does not pull the keyboard out of whatever the subject is doing (a lock card,
    /// a game). Built in code like GracePauseOverlayWindow: placement and z-order are Win32 work.</para>
    /// </summary>
    internal sealed class RemoteHudWindow : Window
    {
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_NOOWNERZORDER = 0x0200;
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_APPWINDOW = 0x00040000;
        private const int WS_EX_NOACTIVATE = 0x08000000;

        private const double TopGapDip = 10;
        private const double ButtonSize = 40;

        private static readonly Color Mint = Color.FromRgb(0x4F, 0xE0, 0xB5);
        private static readonly Color Amber = Color.FromRgb(0xFF, 0xC2, 0x4D);
        private static readonly Color Red = Color.FromRgb(0xFF, 0x54, 0x68);
        private static readonly Color Pink = Color.FromRgb(0xFF, 0x69, 0xB4);
        private static readonly Color Ink = Color.FromRgb(0x1A, 0x1A, 0x2E);
        private static readonly Color Muted = Color.FromRgb(0xB0, 0xA8, 0xC8);

        private readonly RemoteControlService _service;
        private readonly Action _onLocalStop;
        private readonly Func<IntPtr> _anchorHwnd;
        private readonly DispatcherTimer _tick;

        private readonly Border _pill;
        private readonly ScaleTransform _pillScale = new(1, 1);
        private readonly TextBlock _initial;
        private readonly Ellipse _liveDot;
        private readonly FrameworkElement _expanded;
        private readonly TextBlock _headline;
        private readonly TextBlock _subline;
        private readonly Border _tag;
        private readonly TextBlock _tagText;

        private IntPtr _hwnd;
        private bool _collapsed;
        private bool _wasConnected;
        private bool _disposed;
        private DateTime _seenSinceUtc = DateTime.UtcNow;
        private DateTime? _lastStopUtc;
        private string? _confirm;
        private DateTime _confirmUntilUtc;

        public RemoteHudWindow(RemoteControlService service, Action onLocalStop, Func<IntPtr> anchorHwnd)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
            _onLocalStop = onLocalStop;
            _anchorHwnd = anchorHwnd;

            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            ShowActivated = false;
            Focusable = false;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.WidthAndHeight;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Title = "CCP remote";

            // ---- badge: initial + live dot -------------------------------------------------
            _initial = new TextBlock
            {
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            _liveDot = new Ellipse
            {
                Width = 10,
                Height = 10,
                Fill = new SolidColorBrush(Mint),
                Stroke = new SolidColorBrush(Ink),
                StrokeThickness = 2,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, -2, -2)
            };
            var badge = new Grid { Width = 30, Height = 30, Cursor = Cursors.Hand };
            badge.Children.Add(new Ellipse
            {
                Fill = new LinearGradientBrush(Pink, Color.FromRgb(0xB0, 0x7C, 0xFF), 45)
            });
            badge.Children.Add(_initial);
            badge.Children.Add(_liveDot);
            badge.ToolTip = Loc.Get("remote_hud_fold_tip");
            badge.MouseLeftButtonUp += (_, e) => { e.Handled = true; ToggleCollapsed(); };

            // ---- words ----------------------------------------------------------------------
            _headline = new TextBlock
            {
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 260
            };
            _subline = new TextBlock
            {
                FontSize = 11,
                Foreground = new SolidColorBrush(Muted),
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 260,
                Margin = new Thickness(0, 1, 0, 0)
            };
            var words = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(10, 0, 10, 0),
                Background = Brushes.Transparent,   // hit-testable for the fold click
                Cursor = Cursors.Hand,
                ToolTip = Loc.Get("remote_hud_fold_tip")
            };
            words.Children.Add(_headline);
            words.Children.Add(_subline);
            words.MouseLeftButtonUp += (_, e) => { e.Handled = true; ToggleCollapsed(); };

            _tagText = new TextBlock
            {
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Amber)
            };
            _tag = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0x33, Amber.R, Amber.G, Amber.B)),
                BorderBrush = new SolidColorBrush(Amber),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(9),
                Padding = new Thickness(8, 2, 8, 2),
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Visibility = Visibility.Collapsed,
                Child = _tagText
            };

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            buttons.Children.Add(MakeSignalButton("more", Mint, "remote_hud_more", "remote_hud_more_tip"));
            buttons.Children.Add(MakeSignalButton("easy", Amber, "remote_hud_easy", "remote_hud_easy_tip"));
            buttons.Children.Add(MakeSignalButton("stop", Red, "remote_hud_stop", "remote_hud_stop_tip"));

            var expanded = new StackPanel { Orientation = Orientation.Horizontal };
            expanded.Children.Add(words);
            expanded.Children.Add(_tag);
            expanded.Children.Add(buttons);
            _expanded = expanded;

            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(badge);
            row.Children.Add(_expanded);

            _pill = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0xEE, Ink.R, Ink.G, Ink.B)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x99, Pink.R, Pink.G, Pink.B)),
                BorderThickness = new Thickness(1.5),
                CornerRadius = new CornerRadius(26),
                Padding = new Thickness(8, 6, 8, 6),
                // Room for the shadow and the bump inside the transparent window.
                Margin = new Thickness(12),
                Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 16, ShadowDepth = 0, Opacity = 0.6 },
                RenderTransform = _pillScale,
                RenderTransformOrigin = new Point(0.5, 0.5),
                Child = row
            };
            Content = _pill;

            SourceInitialized += (_, _) =>
            {
                _hwnd = new WindowInteropHelper(this).Handle;
                if (_hwnd == IntPtr.Zero) return;
                var ex = GetWindowLong(_hwnd, GWL_EXSTYLE);
                ex |= WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
                ex &= ~WS_EX_APPWINDOW;
                SetWindowLong(_hwnd, GWL_EXSTYLE, ex);
            };
            SizeChanged += (_, _) => Place();
            Closed += (_, _) => Teardown();

            _service.ControllerConnectedChanged += OnServiceChanged;
            _service.SessionEnded += OnServiceChanged;
            _service.EasyChanged += OnServiceChanged;
            _service.LastActionChanged += OnLastAction;

            // One clock for the elapsed time, the visibility rule (also covers any path that
            // ends a session with the MainWindow handlers detached) and the topmost re-assert.
            _tick = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromSeconds(1) };
            _tick.Tick += (_, _) => Refresh();
            _tick.Start();
        }

        /// <summary>Re-reads the service and shows, hides and repaints the pill. UI thread only.</summary>
        public void Refresh()
        {
            if (_disposed) return;
            bool connected = RemoteHudRules.ShouldShow(_service.IsActive, _service.ControllerConnected);
            if (connected && !_wasConnected)
            {
                _seenSinceUtc = DateTime.UtcNow;
                _collapsed = false;
                _lastStopUtc = null;
                _confirm = null;
            }
            _wasConnected = connected;

            if (!connected)
            {
                if (IsVisible)
                {
                    _liveDot.BeginAnimation(OpacityProperty, null);
                    Hide();
                }
                return;
            }

            Paint();
            if (!IsVisible)
            {
                Opacity = Juicy ? 0 : 1;
                Show();
                Place();
                if (Juicy) BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
                StartLiveDotPulse();
            }
            KeepOnTop();
        }

        /// <summary>Stops the clock, drops the service hooks and closes the window. Idempotent.</summary>
        public void Dispose()
        {
            if (_disposed) return;
            Teardown();
            try { Close(); } catch (Exception ex) { App.Logger?.Debug("RemoteHud close failed: {E}", ex.Message); }
        }

        private void Teardown()
        {
            if (_disposed) return;
            _disposed = true;
            try { _tick.Stop(); } catch { }
            _service.ControllerConnectedChanged -= OnServiceChanged;
            _service.SessionEnded -= OnServiceChanged;
            _service.EasyChanged -= OnServiceChanged;
            _service.LastActionChanged -= OnLastAction;
        }

        private static bool Juicy => MotionFx.Level == MotionLevel.Full;

        private void Paint()
        {
            var name = RemoteHudRules.DisplayName(_service.ControllerName);
            _initial.Text = RemoteHudRules.InitialFrom(name);
            _headline.Text = name != null
                ? Loc.GetF("remote_hud_has_remote", name)
                : Loc.Get("remote_hud_someone");

            var now = DateTime.UtcNow;
            string sub;
            if (_confirm != null && now < _confirmUntilUtc)
            {
                sub = _confirm;
            }
            else
            {
                _confirm = null;
                sub = RemoteHudRules.FormatElapsed(
                    RemoteHudRules.Elapsed(_service.ControllerConnectedSinceUtc, _seenSinceUtc, now));
                var last = _service.LastActionLabel;
                if (!string.IsNullOrWhiteSpace(last))
                    sub += "  ·  " + Loc.GetF("remote_hud_last", last);
            }
            _subline.Text = sub;

            switch (RemoteHudRules.StrengthFor(_service.EasyFactor))
            {
                case RemoteHudRules.StrengthTag.Half:
                    _tagText.Text = Loc.Get("remote_hud_half");
                    _tag.Visibility = Visibility.Visible;
                    break;
                case RemoteHudRules.StrengthTag.Quarter:
                    _tagText.Text = Loc.Get("remote_hud_quarter");
                    _tag.Visibility = Visibility.Visible;
                    break;
                default:
                    _tag.Visibility = Visibility.Collapsed;
                    break;
            }

            _expanded.Visibility = _collapsed ? Visibility.Collapsed : Visibility.Visible;
            _pill.Padding = _collapsed ? new Thickness(5) : new Thickness(8, 6, 8, 6);
        }

        private void ToggleCollapsed()
        {
            _collapsed = !_collapsed;
            Paint();
        }

        /// <summary>Top centre of the work area of the monitor MainWindow is on.</summary>
        private void Place()
        {
            if (!IsVisible) return;
            try
            {
                var anchor = _anchorHwnd?.Invoke() ?? IntPtr.Zero;
                var screen = anchor != IntPtr.Zero ? Screen.FromHandle(anchor) : Screen.PrimaryScreen;
                if (screen == null) return;
                double dpi = 1.0;
                try { dpi = BubbleCountWindow.GetDpiForScreen(screen); } catch { }
                if (dpi <= 0) dpi = 1.0;
                var wa = screen.WorkingArea;
                double w = ActualWidth > 0 ? ActualWidth : 360;
                Left = (wa.X + (wa.Width - w * dpi) / 2.0) / dpi;
                Top = wa.Y / dpi + TopGapDip - _pill.Margin.Top;
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("RemoteHud placement failed: {E}", ex.Message);
            }
        }

        private void KeepOnTop()
        {
            if (_hwnd == IntPtr.Zero) return;
            // Fullscreen video and lock card windows re-assert their own topmost, so the pill does
            // the same once a second. NOOWNERZORDER + NOACTIVATE: nothing else moves, focus stays.
            SetWindowPos(_hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOOWNERZORDER);
        }

        private void OnServiceChanged(object? sender, EventArgs e)
        {
            try { Dispatcher.BeginInvoke(new Action(Refresh)); } catch { }
        }

        private void OnLastAction(object? sender, EventArgs e)
        {
            try
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    Refresh();
                    if (IsVisible) Bump(1.07, 240);
                }));
            }
            catch { }
        }

        // ---- buttons -------------------------------------------------------------------------

        private Button MakeSignalButton(string kind, Color fill, string labelKey, string tipKey)
        {
            var scale = new ScaleTransform(1, 1);
            var button = new Button
            {
                Width = ButtonSize,
                Height = ButtonSize,
                Margin = new Thickness(4, 0, 0, 0),
                Focusable = false,
                Cursor = Cursors.Hand,
                Background = new SolidColorBrush(fill),
                Foreground = new SolidColorBrush(Ink),
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Content = Loc.Get(labelKey),
                ToolTip = Loc.Get(tipKey),
                RenderTransform = scale,
                RenderTransformOrigin = new Point(0.5, 0.5),
                Template = BuildRoundTemplate()
            };
            button.PreviewMouseLeftButtonDown += (_, _) => Press(scale, true);
            button.PreviewMouseLeftButtonUp += (_, _) => Press(scale, false);
            button.MouseLeave += (_, _) => Press(scale, false);
            button.Click += async (_, _) => await OnSignalAsync(kind, labelKey);
            return button;
        }

        private static void Press(ScaleTransform scale, bool down)
        {
            var level = MotionFx.Level;
            if (level == MotionLevel.Off) return;
            double to = down ? 0.88 : 1.0;
            if (level == MotionLevel.Full)
            {
                var anim = new DoubleAnimation(to, TimeSpan.FromMilliseconds(down ? 70 : 140))
                {
                    EasingFunction = down ? null : new BackEase { Amplitude = 0.5, EasingMode = EasingMode.EaseOut }
                };
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, anim);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, anim);
            }
            else
            {
                // Reduced: the press still reads, it just does not move.
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                scale.ScaleX = scale.ScaleY = to;
            }
        }

        private async Task OnSignalAsync(string kind, string labelKey)
        {
            if (_disposed) return;
            bool isStop = kind == "stop";
            if (isStop)
            {
                var now = DateTime.UtcNow;
                if (!RemoteHudRules.StopPressAccepted(_lastStopUtc, now)) return;
                _lastStopUtc = now;
                // Local first: the stop must not wait on the network.
                try { _onLocalStop?.Invoke(); }
                catch (Exception ex) { App.Logger?.Warning("RemoteHud: local stop failed: {E}", ex.Message); }
            }

            bool sent = false;
            try { sent = await _service.SendSignalAsync(kind); }
            catch (Exception ex) { App.Logger?.Debug("RemoteHud: signal {Kind} failed: {E}", kind, ex.Message); }
            if (_disposed) return;

            _confirm = isStop
                ? Loc.Get(sent ? "remote_hud_stopped" : "remote_hud_stopped_not_sent")
                : sent ? Loc.GetF("remote_hud_sent", Loc.Get(labelKey)) : Loc.Get("remote_hud_not_sent");
            _confirmUntilUtc = DateTime.UtcNow + RemoteHudRules.ConfirmFor;
            Refresh();
            if (sent && IsVisible) Bump(1.04, 180);
        }

        private static ControlTemplate BuildRoundTemplate()
        {
            var template = new ControlTemplate(typeof(Button));
            var grid = new FrameworkElementFactory(typeof(Grid));
            var circle = new FrameworkElementFactory(typeof(Ellipse));
            circle.SetValue(Shape.FillProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            grid.AppendChild(circle);
            var content = new FrameworkElementFactory(typeof(ContentPresenter));
            content.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
            content.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
            grid.AppendChild(content);
            template.VisualTree = grid;
            return template;
        }

        // ---- juice ---------------------------------------------------------------------------

        private void Bump(double peak, int ms)
        {
            if (!Juicy) return;
            var anim = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(ms) };
            anim.KeyFrames.Add(new EasingDoubleKeyFrame(peak, KeyTime.FromPercent(0.35),
                new QuadraticEase { EasingMode = EasingMode.EaseOut }));
            anim.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromPercent(1.0),
                new BackEase { Amplitude = 0.4, EasingMode = EasingMode.EaseOut }));
            _pillScale.BeginAnimation(ScaleTransform.ScaleXProperty, anim);
            _pillScale.BeginAnimation(ScaleTransform.ScaleYProperty, anim);
        }

        private void StartLiveDotPulse()
        {
            if (!MotionFx.AllowAmbientLoops)
            {
                _liveDot.BeginAnimation(OpacityProperty, null);
                _liveDot.Opacity = 1;
                return;
            }
            _liveDot.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0.35, TimeSpan.FromMilliseconds(900))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever
            });
        }
    }
}
