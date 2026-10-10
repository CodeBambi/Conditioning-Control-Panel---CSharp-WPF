// PORTED from ConditioningControlPanel/Windows/RemoteHud/RemoteHudWindow.cs (7.1.5). The pure half is
// Core's RemoteHudRules; the service is Core's RemoteRelay.
//
// Remote Control v2: the subject's own pill, top centre of the monitor CCP is on, up while a controller
// holds the remote. Who has it, for how long, what they did last, and three buttons back: More, Easy, Stop.
//
// Z-order and focus (WPF: topmost, UNOWNED, WS_EX_NOACTIVATE): the pill goes through the port's passive
// door (LandingChrome.Dress + PlacePassive -> X11Overlay.SetOverrideRedirect(passive), the road the friends
// landing and the achievement toast use), so it never takes focus and never lifts the panel over the
// player's current app. It is the sub's standing reminder that a session is live, so NOTHING a controller
// sends may hide, move or cover it: no remote verb reaches this window, it is placed from the work area
// (never dragged, re-placed every second), and it re-asserts its place on top every second and again each
// time a command lands (X11Overlay.Raise: SetWindowPos HWND_TOPMOST on Windows, XRaiseWindow on X11).
// Wayland / headless have no restack call: the window is Topmost and placed only (documented limit).
using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Avalonia.Views.Friends;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.RemoteHud;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    internal sealed class RemoteHudWindow : Window
    {
        private const double TopGapDip = 10;
        private const double ButtonSize = 40;
        private const double PillMargin = 12;   // room for the shadow and the bump inside the transparent window

        private static readonly Color Mint = Color.FromRgb(0x4F, 0xE0, 0xB5);
        private static readonly Color Amber = Color.FromRgb(0xFF, 0xC2, 0x4D);
        private static readonly Color Red = Color.FromRgb(0xFF, 0x54, 0x68);
        private static readonly Color Pink = Color.FromRgb(0xFF, 0x69, 0xB4);
        private static readonly Color Ink = Color.FromRgb(0x1A, 0x1A, 0x2E);
        private static readonly Color Muted = Color.FromRgb(0xB0, 0xA8, 0xC8);

        /// <summary>Clock for the elapsed time, the Stop debounce and the confirmation; tests step it.</summary>
        internal Func<DateTime> UtcNow = () => DateTime.UtcNow;
        /// <summary>The motion level the pill obeys (MotionFx.Level). Tests may pin it.</summary>
        internal static Func<MotionLevel> Level = () =>
        {
            try { return global::ConditioningControlPanel.Avalonia.Controls.AmbientFxCanvas.Env.Level; }
            catch { return MotionLevel.Full; }
        };

        private readonly RemoteRelay _service;
        private readonly Action _onLocalStop;
        private readonly Func<Window?> _anchor;
        private readonly DispatcherTimer _tick;
        private DispatcherTimer? _pulse, _bump, _hideTimer;

        private readonly Border _pill;
        private readonly ScaleTransform _pillScale = new(1, 1);
        private readonly TextBlock _initial;
        private readonly Ellipse _liveDot;
        private readonly StackPanel _expanded;
        private readonly TextBlock _headline;
        private readonly TextBlock _subline;
        private readonly Border _tag;
        private readonly TextBlock _tagText;

        private bool _collapsed, _wasConnected, _disposed, _up;
        private DateTime _seenSinceUtc;
        private DateTime? _lastStopUtc;
        private string? _confirm;
        private DateTime _confirmUntilUtc;
        private Size _placedSize;
        private PixelPoint _placedAt;

        /// <summary>True while the pill is on screen (not counting the 140 ms it takes to fade out).</summary>
        internal bool IsUp => _up;
        internal bool IsCollapsed => _collapsed;
        internal string Headline => _headline.Text ?? "";
        internal string Subline => _subline.Text ?? "";
        internal string Initial => _initial.Text ?? "";
        internal string? StrengthTagText => _tag.IsVisible ? _tagText.Text : null;
        internal bool ButtonsVisible => _expanded.IsVisible;

        public RemoteHudWindow(RemoteRelay service, Action onLocalStop, Func<Window?> anchor)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
            _onLocalStop = onLocalStop;
            _anchor = anchor;
            _seenSinceUtc = UtcNow();

            LandingChrome.Dress(this);
            Title = "CCP remote";

            // ---- badge: initial + live dot
            _initial = new TextBlock
            {
                FontSize = 14,
                FontWeight = FontWeight.Bold,
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
            var badge = new Grid { Width = 30, Height = 30, Cursor = new Cursor(StandardCursorType.Hand), Background = Brushes.Transparent, ClipToBounds = false };
            badge.Children.Add(new Ellipse
            {
                Fill = new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                    EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                    GradientStops = { new GradientStop(Pink, 0), new GradientStop(Color.FromRgb(0xB0, 0x7C, 0xFF), 1) }
                }
            });
            badge.Children.Add(_initial);
            badge.Children.Add(_liveDot);
            ToolTip.SetTip(badge, Loc.Get("remote_hud_fold_tip"));
            badge.PointerReleased += (_, e) => { e.Handled = true; ToggleCollapsed(); };

            // ---- words
            _headline = new TextBlock
            {
                FontSize = 13,
                FontWeight = FontWeight.SemiBold,
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
                Cursor = new Cursor(StandardCursorType.Hand)
            };
            ToolTip.SetTip(words, Loc.Get("remote_hud_fold_tip"));
            words.Children.Add(_headline);
            words.Children.Add(_subline);
            words.PointerReleased += (_, e) => { e.Handled = true; ToggleCollapsed(); };

            _tagText = new TextBlock { FontSize = 11, FontWeight = FontWeight.SemiBold, Foreground = new SolidColorBrush(Amber) };
            _tag = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0x33, Amber.R, Amber.G, Amber.B)),
                BorderBrush = new SolidColorBrush(Amber),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(9),
                Padding = new Thickness(8, 2, 8, 2),
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center,
                IsVisible = false,
                Child = _tagText
            };

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            buttons.Children.Add(MakeSignalButton("more", Mint, "remote_hud_more", "remote_hud_more_tip"));
            buttons.Children.Add(MakeSignalButton("easy", Amber, "remote_hud_easy", "remote_hud_easy_tip"));
            buttons.Children.Add(MakeSignalButton("stop", Red, "remote_hud_stop", "remote_hud_stop_tip"));

            _expanded = new StackPanel { Orientation = Orientation.Horizontal };
            _expanded.Children.Add(words);
            _expanded.Children.Add(_tag);
            _expanded.Children.Add(buttons);

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
                Margin = new Thickness(PillMargin),
                // WPF: DropShadowEffect black, blur 16, depth 0, 60%. A BoxShadow here: no Effect on a HUD.
                BoxShadow = new BoxShadows(new BoxShadow { Blur = 16, Color = Color.FromArgb(0x99, 0, 0, 0) }),
                RenderTransform = _pillScale,
                RenderTransformOrigin = RelativePoint.Center,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Child = row,
                Transitions = new Transitions { new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(180) } }
            };
            Content = _pill;

            Closed += (_, _) => Teardown();
            _service.ControllerConnectedChanged += OnServiceChanged;
            _service.SessionEnded += OnServiceChanged;
            _service.EasyChanged += OnServiceChanged;
            _service.LastActionChanged += OnLastAction;

            // One clock for the elapsed time, the visibility rule (also covers any path that ends a session
            // with the shell handlers detached), the placement and the on-top re-assert.
            _tick = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Normal, (_, _) => Refresh());
            _tick.Start();
        }

        /// <summary>Re-reads the service and shows, hides and repaints the pill. UI thread only.</summary>
        public void Refresh()
        {
            if (_disposed) return;
            bool connected = RemoteHudRules.ShouldShow(_service.IsActive, _service.ControllerConnected);
            if (connected && !_wasConnected)
            {
                _seenSinceUtc = UtcNow();
                _collapsed = false;
                _lastStopUtc = null;
                _confirm = null;
            }
            _wasConnected = connected;

            if (!connected)
            {
                if (_up) Leave();
                return;
            }

            Paint();
            if (!_up)
            {
                _up = true;
                _hideTimer?.Stop();
                bool juicy = Level() == MotionLevel.Full;
                if (juicy && !IsVisible) SetOpacityNow(0);
                Place(force: true);
                if (!IsVisible) Show();
                Place(force: true);   // the scaling is only real once the window is mapped
                _pill.Opacity = 1;    // IN: 180 ms through the transition at Full, at once otherwise
                StartLiveDotPulse();
            }
            else Place(force: false);
            KeepOnTop();
        }

        /// <summary>OUT: the pill fades 140 ms at Full, then the window hides. At once otherwise.</summary>
        private void Leave()
        {
            _up = false;
            _pulse?.Stop();
            _liveDot.Opacity = 1;
            if (Level() != MotionLevel.Full || !IsVisible) { Hide(); return; }
            _pill.Opacity = 0;
            _hideTimer?.Stop();
            _hideTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(180), DispatcherPriority.Normal, (_, _) =>
            {
                _hideTimer?.Stop();
                if (!_up && !_disposed) Hide();
            });
            _hideTimer.Start();
        }

        private void SetOpacityNow(double value)
        {
            var t = _pill.Transitions;
            _pill.Transitions = null;
            _pill.Opacity = value;
            _pill.Transitions = t;
        }

        /// <summary>Stops the clocks, drops the service hooks and closes the window. Idempotent.</summary>
        public void Dispose()
        {
            if (_disposed) return;
            Teardown();
            try { Close(); } catch (Exception ex) { Serilog.Log.Debug("RemoteHud close failed: {E}", ex.Message); }
        }

        private void Teardown()
        {
            if (_disposed) return;
            _disposed = true;
            _up = false;
            try { _tick.Stop(); _pulse?.Stop(); _bump?.Stop(); _hideTimer?.Stop(); } catch { }
            _service.ControllerConnectedChanged -= OnServiceChanged;
            _service.SessionEnded -= OnServiceChanged;
            _service.EasyChanged -= OnServiceChanged;
            _service.LastActionChanged -= OnLastAction;
        }

        private void Paint()
        {
            var name = RemoteHudRules.DisplayName(_service.ControllerName);
            _initial.Text = RemoteHudRules.InitialFrom(name);
            _headline.Text = name != null ? Loc.GetF("remote_hud_has_remote", name) : Loc.Get("remote_hud_someone");

            var now = UtcNow();
            string sub;
            if (_confirm != null && now < _confirmUntilUtc) sub = _confirm;
            else
            {
                _confirm = null;
                sub = RemoteHudRules.FormatElapsed(RemoteHudRules.Elapsed(_service.ControllerConnectedSinceUtc, _seenSinceUtc, now));
                var last = _service.LastActionLabel;
                if (!string.IsNullOrWhiteSpace(last)) sub += "  ·  " + Loc.GetF("remote_hud_last", last);
            }
            _subline.Text = sub;

            switch (RemoteHudRules.StrengthFor(_service.EasyFactor))
            {
                case RemoteHudRules.StrengthTag.Half:
                    _tagText.Text = Loc.Get("remote_hud_half");
                    _tag.IsVisible = true;
                    break;
                case RemoteHudRules.StrengthTag.Quarter:
                    _tagText.Text = Loc.Get("remote_hud_quarter");
                    _tag.IsVisible = true;
                    break;
                default:
                    _tag.IsVisible = false;
                    break;
            }

            _expanded.IsVisible = !_collapsed;
            _pill.Padding = _collapsed ? new Thickness(5) : new Thickness(8, 6, 8, 6);
        }

        /// <summary>The badge and the words fold the pill to its badge and back. The sub's own choice; a new
        /// controller always finds it unfolded, with Stop showing.</summary>
        internal void ToggleCollapsed()
        {
            _collapsed = !_collapsed;
            Paint();
            Place(force: false);
        }

        /// <summary>Top centre of the work area of the monitor the panel is on, in device pixels. Never
        /// anywhere else: the place is recomputed from the screen, so the pill cannot end up off it.</summary>
        internal static PixelPoint PlaceFor(PixelRect work, double scale, Size dip)
        {
            int w = (int)Math.Ceiling(dip.Width * scale);
            int x = work.X + (work.Width - w) / 2;
            x = Math.Max(work.X, Math.Min(x, work.Right - w));
            if (x < work.X) x = work.X;
            int y = work.Y + (int)Math.Round((TopGapDip - PillMargin) * scale);
            return new PixelPoint(x, Math.Max(work.Y - (int)Math.Round(PillMargin * scale), y));
        }

        private void Place(bool force)
        {
            try
            {
                var want = LandingChrome.Want(_pill);
                var screen = LandingChrome.ScreenOf(this, _anchor());
                if (screen is not { } s)
                {
                    if (force || want != _placedSize) { Width = want.Width; Height = want.Height; _placedSize = want; }
                    return;
                }
                var at = PlaceFor(s.Work, s.Scale, want);
                if (!force && want == _placedSize && at == _placedAt && Position == at) return;
                (_placedSize, _placedAt) = (want, at);
                LandingChrome.PlacePassive(this, at, want, s.Scale);
            }
            catch (Exception ex) { Serilog.Log.Debug("RemoteHud placement failed: {E}", ex.Message); }
        }

        /// <summary>Fullscreen video, the overlays and lock cards re-assert their own topmost, so the pill
        /// does the same once a second and whenever a command lands. Never activates.</summary>
        private void KeepOnTop()
        {
            if (!IsVisible) return;
            try { Platform.X11Overlay.Raise(this); }
            catch (Exception ex) { Serilog.Log.Debug("RemoteHud raise failed: {E}", ex.Message); }
        }

        private void OnServiceChanged(object? sender, EventArgs e)
        {
            try { Dispatcher.UIThread.Post(Refresh); } catch { }
        }

        private void OnLastAction(object? sender, EventArgs e)
        {
            try
            {
                Dispatcher.UIThread.Post(() =>
                {
                    Refresh();
                    if (_up) Bump(1.07, 240);
                });
            }
            catch { }
        }

        // ---- buttons

        private Control MakeSignalButton(string kind, Color fill, string labelKey, string tipKey)
        {
            var scale = new ScaleTransform(1, 1);
            // A round face drawn here, not a themed Button: no hover repaint, no focus, nothing to tab to.
            var button = new Border
            {
                Width = ButtonSize,
                Height = ButtonSize,
                Margin = new Thickness(4, 0, 0, 0),
                CornerRadius = new CornerRadius(ButtonSize / 2),
                Background = new SolidColorBrush(fill),
                Cursor = new Cursor(StandardCursorType.Hand),
                Focusable = false,
                RenderTransform = scale,
                RenderTransformOrigin = RelativePoint.Center,
                Tag = kind,
                Child = new TextBlock
                {
                    Text = Loc.Get(labelKey),
                    FontSize = 11,
                    FontWeight = FontWeight.Bold,
                    Foreground = new SolidColorBrush(Ink),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            ToolTip.SetTip(button, Loc.Get(tipKey));
            bool down = false;
            button.PointerPressed += (_, e) =>
            {
                if (!e.GetCurrentPoint(button).Properties.IsLeftButtonPressed) return;
                down = true;
                e.Handled = true;
                Press(scale, true);
            };
            button.PointerReleased += async (_, e) =>
            {
                if (!down) return;
                down = false;
                e.Handled = true;
                Press(scale, false);
                var p = e.GetPosition(button);
                if (p.X < 0 || p.Y < 0 || p.X > button.Bounds.Width || p.Y > button.Bounds.Height) return;
                await SignalAsync(kind);
            };
            button.PointerCaptureLost += (_, _) => { if (down) { down = false; Press(scale, false); } };
            return button;
        }

        private static void Press(ScaleTransform scale, bool down)
        {
            var level = Level();
            if (level == MotionLevel.Off) return;
            double to = down ? 0.88 : 1.0;
            if (level == MotionLevel.Full)
            {
                double from = scale.ScaleX;
                TransformTween.Run(scale, TimeSpan.FromMilliseconds(down ? 70 : 140), new[]
                {
                    (0.0, (AvaloniaProperty)ScaleTransform.ScaleXProperty, from), (1.0, ScaleTransform.ScaleXProperty, to),
                    (0.0, ScaleTransform.ScaleYProperty, from), (1.0, ScaleTransform.ScaleYProperty, to),
                }, down ? null : new BackEaseOut());
            }
            else scale.ScaleX = scale.ScaleY = to;   // Reduced: the press still reads, it just does not move
        }

        /// <summary>One press of More, Easy or Stop. Stop acts here first (the local stop must not wait on
        /// the network) and is debounced so a double click can never be the app's double-panic exit.</summary>
        internal async Task SignalAsync(string kind)
        {
            if (_disposed) return;
            bool isStop = kind == "stop";
            if (isStop)
            {
                var now = UtcNow();
                if (!RemoteHudRules.StopPressAccepted(_lastStopUtc, now)) return;
                _lastStopUtc = now;
                try { _onLocalStop?.Invoke(); }
                catch (Exception ex) { Serilog.Log.Warning("RemoteHud: local stop failed: {E}", ex.Message); }
            }

            bool sent = false;
            try { sent = await _service.SendSignalAsync(kind); }
            catch (Exception ex) { Serilog.Log.Debug("RemoteHud: signal {Kind} failed: {E}", kind, ex.Message); }
            if (_disposed) return;

            _confirm = isStop
                ? Loc.Get(sent ? "remote_hud_stopped" : "remote_hud_stopped_not_sent")
                : sent ? Loc.GetF("remote_hud_sent", Loc.Get("remote_hud_" + kind)) : Loc.Get("remote_hud_not_sent");
            _confirmUntilUtc = UtcNow() + RemoteHudRules.ConfirmFor;
            Refresh();
            if (sent && _up) Bump(1.04, 180);
        }

        // ---- juice

        private void Bump(double peak, int ms)
        {
            if (Level() != MotionLevel.Full) return;
            _bump?.Stop();
            _bump = TransformTween.Run(_pillScale, TimeSpan.FromMilliseconds(ms), new[]
            {
                (0.0, (AvaloniaProperty)ScaleTransform.ScaleXProperty, 1.0), (0.35, ScaleTransform.ScaleXProperty, peak), (1.0, ScaleTransform.ScaleXProperty, 1.0),
                (0.0, ScaleTransform.ScaleYProperty, 1.0), (0.35, ScaleTransform.ScaleYProperty, peak), (1.0, ScaleTransform.ScaleYProperty, 1.0),
            });
        }

        /// <summary>WPF: opacity 1 -> 0.35 over 900 ms, back, forever, while ambient loops are allowed. Driven
        /// by a 15 fps timer on this small window (an infinite Animation would compose it at 60 Hz).</summary>
        private void StartLiveDotPulse()
        {
            _pulse?.Stop();
            bool allowed;
            try { allowed = global::ConditioningControlPanel.Avalonia.Controls.AmbientFxCanvas.Env.AllowAmbientLoops; }
            catch { allowed = false; }
            _liveDot.Opacity = 1;
            if (!allowed) return;
            var started = DateTime.UtcNow;
            _pulse = new DispatcherTimer(TimeSpan.FromMilliseconds(66), DispatcherPriority.Background, (_, _) =>
            {
                if (!_up || !IsVisible) { _pulse?.Stop(); return; }
                double t = (DateTime.UtcNow - started).TotalMilliseconds % 1800 / 900.0;   // 0..2
                double k = t <= 1 ? t : 2 - t;
                _liveDot.Opacity = 1 - 0.65 * k;
            });
            _pulse.Start();
        }
    }
}
