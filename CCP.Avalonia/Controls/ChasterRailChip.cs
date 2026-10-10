// PORTED from ConditioningControlPanel/Controls/ChasterRailChip.cs: the padlock at the foot of the
// rail with the lock's clock under it; opens Circe's tab; dimmed, never hidden, while unlinked.
// The hover peek (WPF FillPeek :546) and the glow following the ring colour (:429) are real.
// Peek spring-in (:516), idle breath/swing (StartIdle :645), mood-pip pop (:492) and Pulse (:681)
// are real; the booked flash floats off it (MainShellWindow.ChasterFlash.cs), which calls Pulse.
using System;
using System.Linq;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Rendering.Composition;
using Avalonia.Rendering.Composition.Animations;
using Avalonia.Styling;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Chaster;

namespace ConditioningControlPanel.Avalonia.Controls
{
    public sealed class ChasterRailChip : Grid
    {
        public const string TabKey = "chaster";
        private const double RingSize = 40;
        private const string NavRailStaticTextTag = "navrailstatic";

        private static readonly Geometry ShackleShut = Geometry.Parse("M7.5,11 V7.6 a4.5,4.5 0 0 1 9,0 V11");
        private static readonly Geometry ShackleOpen = Geometry.Parse("M12.5,11 V7.6 a4.5,4.5 0 0 1 9,0 V11.5");
        private static readonly Geometry BodyGeometry = Geometry.Parse(
            "M5,10.5 h14 a2,2 0 0 1 2,2 v7.5 a2,2 0 0 1 -2,2 h-14 a2,2 0 0 1 -2,-2 v-7.5 a2,2 0 0 1 2,-2 Z "
            + "M12,13.6 a1.6,1.6 0 0 0 -0.8,3 v2 h1.6 v-2 a1.6,1.6 0 0 0 -0.8,-3 Z");
        private static readonly Geometry BodyGlint = Geometry.Parse("M5.2,11.6 h13.6 a0.9,0.9 0 0 1 0,1.8 h-13.6 a0.9,0.9 0 0 1 0,-1.8 Z");

        private static readonly Color Ink = Color.FromRgb(0xFF, 0x7A, 0xC0);
        private static readonly Color RingPink = Color.FromArgb(0xCC, 0xFF, 0x69, 0xB4);
        private static readonly Color Ice = Color.FromRgb(0x8F, 0xD8, 0xFF);
        private static readonly Color RingIce = Color.FromArgb(0xCC, 0x8F, 0xD8, 0xFF);
        private static readonly Color RingAmber = Color.FromArgb(0xCC, 0xE0, 0xB0, 0x52);
        private static readonly Color Grey = Color.FromRgb(0x9A, 0xA0, 0xB8);
        private static readonly Color RingGrey = Color.FromArgb(0x88, 0x9A, 0xA0, 0xB8);
        private static readonly Color BadgeRed = Color.FromRgb(0xFF, 0x2D, 0x55);
        private static readonly Color CreditMint = Color.FromRgb(0x5F, 0xFF, 0xD0);
        private static readonly IBrush Keyline = new SolidColorBrush(Color.FromRgb(0x17, 0x12, 0x2A));
        private static readonly FontFamily Mono = new("Consolas, Courier New");
        private static readonly FontFamily Display = new("Fredoka, Segoe UI");
        private static readonly Color DebtRed = Color.FromRgb(0xFF, 0x6B, 0x8A);
        private static readonly Color PeekMuted = Color.FromRgb(0xB8, 0xB0, 0xCC);


        private readonly SolidColorBrush _inkBrush = new(Ink);
        private readonly GradientStop _bodyTop = new(Color.FromRgb(0xFF, 0xC4, 0xE4), 0);
        private readonly GradientStop _bodyBottom = new(Ink, 1);
        private readonly SolidColorBrush _ringBrush = new(RingPink);
        private readonly SolidColorBrush _badgeBrush = new(BadgeRed);
        private readonly SolidColorBrush _badgeInk = new(Colors.White);
        private readonly SolidColorBrush _moodBrush = new(CreditMint);
        private readonly Path _shackle;
        private readonly Border _ring;
        private readonly TextBlock _clock;
        private readonly Border _badge;
        private readonly TextBlock _badgeText;
        private readonly Border _moodPip;
        private readonly ScaleTransform _moodPop = new(1, 1);
        private Color _glowColour = Color.FromRgb(0xFF, 0x69, 0xB4);
        // The glow is a BoxShadow on a disc behind the ring whose Opacity breathes on the window's
        // 30 fps beat: an Effect re-renders every frame, and the compositor's Forever opacity loop
        // kept the whole window composing at 60 Hz (AGENTS.md GPU CACHE + 60 Hz TRAP).
        // 0.69 x 0.8 = WPF's 0.55 rest.
        private readonly Border _glowHost;
        private global::ConditioningControlPanel.Avalonia.Views.Features.BreathClock? _breath;
        private DispatcherTimer? _swingTimer, _swingRun;
        private readonly RotateTransform _swing = new();
        private readonly Canvas _art;
        private readonly ScaleTransform _peekScale = new(1, 1);
        private readonly Border _peekCard;
        private System.Threading.CancellationTokenSource? _idle, _pulse, _pop, _peekRun;
        private CircesMood? _mood;
        private Color _ringRest = RingPink;
        private DispatcherTimer? _tick;
        private ChasterService? _wired;
        private readonly Popup _peek;
        private readonly TextBlock _peekTitle, _peekLead, _peekEnds, _peekPending, _peekMood, _peekNote;
        private readonly StackPanel _peekNumber = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 0) };

        public ChasterRailChip()
        {
            Height = 62;
            Margin = new Thickness(0, 6, 0, 2);
            Cursor = new Cursor(StandardCursorType.Hand);
            Background = Brushes.Transparent; // the whole row takes the click, not just the ring
            ClipToBounds = true;

            var art = _art = new Canvas { Width = 24, Height = 24, RenderTransformOrigin = new RelativePoint(0.5, 0.2, RelativeUnit.Relative), RenderTransform = _swing };
            _shackle = new Path
            {
                Data = ShackleShut, Stroke = _inkBrush, StrokeThickness = 2.8, StrokeLineCap = PenLineCap.Round,
            };
            art.Children.Add(_shackle);
            var bodyFill = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            };
            bodyFill.GradientStops.Add(_bodyTop);
            bodyFill.GradientStops.Add(_bodyBottom);
            art.Children.Add(new Path { Data = BodyGeometry, Fill = bodyFill, Stroke = Keyline, StrokeThickness = 0.6 });
            art.Children.Add(new Path { Data = BodyGlint, Fill = new SolidColorBrush(Color.FromArgb(0x88, 0xFF, 0xFF, 0xFF)) });

            _glowHost = new Border
            {
                Width = RingSize, Height = RingSize, CornerRadius = new CornerRadius(RingSize / 2), Background = Keyline,
                HorizontalAlignment = HorizontalAlignment.Center, BoxShadow = GlowShadow(RingPink), Opacity = 0.55 / 0.8,
            };
            _ring = new Border
            {
                Width = RingSize, Height = RingSize, CornerRadius = new CornerRadius(RingSize / 2),
                BorderThickness = new Thickness(2), BorderBrush = _ringBrush,
                Background = new RadialGradientBrush
                {
                    GradientOrigin = new RelativePoint(0.4, 0.3, RelativeUnit.Relative),
                    GradientStops = { new GradientStop(Color.FromRgb(0x3A, 0x22, 0x4E), 0), new GradientStop(Color.FromRgb(0x17, 0x12, 0x2A), 1) },
                },
                HorizontalAlignment = HorizontalAlignment.Center,
                Child = new Viewbox { Width = 27, Height = 27, Child = art },
            };
            _clock = new TextBlock
            {
                FontFamily = Mono, FontSize = 10, FontWeight = FontWeight.Bold, Foreground = _inkBrush,
                HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 2, 0, 0),
                IsHitTestVisible = false, Tag = NavRailStaticTextTag, IsVisible = false,
            };
            var column = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false,
                Children = { new Panel { Children = { _glowHost, _ring } }, _clock },
            };
            Children.Add(column);

            _badgeText = new TextBlock
            {
                FontFamily = Mono, FontSize = 8.5, FontWeight = FontWeight.Bold, Foreground = _badgeInk,
                Margin = new Thickness(3, 0), VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center, Tag = NavRailStaticTextTag,
            };
            _badge = new Border
            {
                Height = 13, MinWidth = 13, CornerRadius = new CornerRadius(6.5), Background = _badgeBrush,
                BorderBrush = Keyline, BorderThickness = new Thickness(1.2),
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 1, 2, 0), IsVisible = false, IsHitTestVisible = false, Child = _badgeText,
            };
            Children.Add(_badge);
            _moodPip = new Border
            {
                Width = 9, Height = 9, CornerRadius = new CornerRadius(4.5), Background = _moodBrush,
                BorderBrush = Keyline, BorderThickness = new Thickness(1.2),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 5, RingSize - 8, 0), IsVisible = false, IsHitTestVisible = false,
                RenderTransformOrigin = RelativePoint.Center, RenderTransform = _moodPop,
            };
            Children.Add(_moodPip);

            // ---- the peek ----
            _peekTitle = new TextBlock
            {
                FontFamily = Display, FontSize = 14, FontWeight = FontWeight.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xB8, 0xDC)),
                TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 380,
            };
            _peekLead = new TextBlock { FontSize = 13, Margin = new Thickness(2, -4, 0, 6), Foreground = new SolidColorBrush(PeekMuted) };
            _peekEnds = new TextBlock { FontSize = 13, Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xE0, 0xF4)) };
            _peekPending = new TextBlock
            {
                FontFamily = Display, FontSize = 16, FontWeight = FontWeight.Bold, Margin = new Thickness(0, 4, 0, 0), Foreground = new SolidColorBrush(DebtRed),
            };
            _peekMood = new TextBlock { FontFamily = Display, FontSize = 14, FontWeight = FontWeight.Bold, Margin = new Thickness(0, 4, 0, 0), Foreground = _moodBrush };
            _peekNote = new TextBlock
            {
                FontSize = 12, Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap, MaxWidth = 380, Foreground = new SolidColorBrush(PeekMuted),
            };
            var rim = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Color.FromRgb(0xFF, 0x69, 0xB4), 0), new GradientStop(Color.FromRgb(0xB9, 0x9C, 0xFF), 1) },
            };
            var paper = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Color.FromArgb(0xF4, 0x2A, 0x16, 0x3C), 0), new GradientStop(Color.FromArgb(0xF4, 0x14, 0x10, 0x26), 1) },
            };
            _peek = new Popup
            {
                PlacementTarget = this, Placement = PlacementMode.Right, VerticalOffset = -40, HorizontalOffset = 6,
                IsLightDismissEnabled = false, Focusable = false, IsHitTestVisible = false,
                Child = _peekCard = new Border
                {
                    Background = paper, BorderBrush = rim, BorderThickness = new Thickness(1.5), CornerRadius = new CornerRadius(18),
                    Padding = new Thickness(22, 14, 26, 16), Margin = new Thickness(6, 16, 24, 24), IsHitTestVisible = false,
                    BoxShadow = BoxShadows.Parse("0 0 22 0 #73FF69B4"),
                    RenderTransformOrigin = new RelativePoint(0, 0.5, RelativeUnit.Relative), RenderTransform = _peekScale,
                    Child = new StackPanel { Children = { _peekTitle, _peekNumber, _peekLead, _peekEnds, _peekPending, _peekMood, _peekNote } },
                },
            };
            Children.Add(_peek);

            PointerEntered += (_, _) => OpenPeek();
            PointerExited += (_, _) => ClosePeek();
            PointerReleased += (_, e) =>
            {
                if (e.InitialPressMouseButton != MouseButton.Left) return;
                ClosePeek();
                (TopLevel.GetTopLevel(this) as MainShellWindow)?.ShowTab(TabKey);
            };
            AttachedToVisualTree += (_, _) => Wire();
            DetachedFromVisualTree += (_, _) => { ClosePeek(); Unwire(); };
        }

        /// <summary>The hover card. Exposed for tests.</summary>
        internal Popup Peek => _peek;
        internal string PeekText => string.Join("|", new[] { _peekTitle }.Concat(_peekNumber.Children.OfType<TextBlock>())
            .Concat(new[] { _peekLead, _peekEnds, _peekPending, _peekMood, _peekNote }).Where(t => t.IsVisible && !string.IsNullOrEmpty(t.Text)).Select(t => t.Text));
        internal Color GlowColour => _glowColour;
        internal Color RingColour => _ringBrush.Color;
        internal bool Idling => _idle != null;
        internal bool Breathing => _breath?.IsRunning == true;
        /// <summary>The tint the last pulse started from. For the tests.</summary>
        internal Color? LastPulse { get; private set; }
        internal bool MoodPipShown => _moodPip.IsVisible;

        private void OpenPeek()
        {
            try
            {
                FillPeek();
                _peek.IsOpen = true;
                if (_tick != null && _tick.Interval != TimeSpan.FromSeconds(1)) _tick.Interval = TimeSpan.FromSeconds(1);
                _peekRun?.Cancel();
                _peekRun = null;
                if (!AmbientFxCanvas.Env.AllowTransitions) return;
                var run = _peekRun = new System.Threading.CancellationTokenSource();
                _ = CircesMoodMeter.Tween(_peekScale, ScaleTransform.ScaleXProperty, 0.55, 1d, 460, new ElasticEaseOut(), run);
                _ = CircesMoodMeter.Tween(_peekScale, ScaleTransform.ScaleYProperty, 0.55, 1d, 460, new ElasticEaseOut(), run);
                _ = CircesMoodMeter.Tween(_peekCard, OpacityProperty, 0d, 1d, 140, new LinearEasing(), run);
            }
            catch (Exception ex) { Serilog.Log.Debug("[Chaster] rail chip peek: {E}", ex.Message); }
        }

        private void ClosePeek()
        {
            try { _peek.IsOpen = false; } catch { }
        }

        /// <summary>WPF FillPeek (:546): everything the old tooltip said, the time left as the big thing.</summary>
        internal void FillPeek()
        {
            var chaster = ChasterHead.Service;
            var now = DateTime.UtcNow;
            _peekNumber.Children.Clear();
            _peekEnds.Text = _peekPending.Text = _peekNote.Text = _peekLead.Text = _peekMood.Text = string.Empty;
            if (chaster == null || !chaster.IsLinked)
            {
                _peekTitle.Text = Loc.Get("chaster_title");
                BigWord(Loc.Get("chaster_hero_title"), Color.FromRgb(0xFF, 0x9A, 0xCB));
                _peekNote.Text = Loc.Get("chaster_chip_unlinked");
                Collapse();
                return;
            }
            var snapshot = chaster.Lock;
            var balance = chaster.BalanceSeconds;
            _peekTitle.Text = snapshot == null ? Loc.Get("chaster_title")
                : string.IsNullOrWhiteSpace(snapshot.Title) ? Loc.Get("chaster_lock_untitled") : snapshot.Title!;
            var hold = chaster.SafetyHoldRemaining;
            if (snapshot == null)
            {
                BigWord(Loc.Get(chaster.LockLookup == LockLookup.Ambiguous ? "chaster_pill_pick" : "chaster_pill_nolock"), Color.FromRgb(0xE0, 0xB0, 0x52));
                _peekNote.Text = chaster.LockLookup switch
                {
                    LockLookup.Ambiguous => Loc.Get("chaster_lock_pick"),
                    LockLookup.Away => Loc.Get("chaster_chip_away_tip"),
                    _ => Loc.Get("chaster_lock_none"),
                };
            }
            else if (snapshot.TimerHidden)
            {
                BigWord(Loc.Get("chaster_pill_hidden"), Grey);
                _peekNote.Text = Loc.Get("chaster_chip_hidden_tip");
            }
            else if (LiveLockClock.Remaining(snapshot, balance, now) is { } left)
            {
                if (left <= TimeSpan.Zero) BigWord(Loc.Get("chaster_clock_ready"), CreditMint);
                else
                {
                    var ink = new SolidColorBrush(snapshot.IsFrozen ? Ice : Color.FromRgb(0xFF, 0xF0, 0xF8));
                    var unitInk = new SolidColorBrush(Color.FromRgb(0xFF, 0x9A, 0xCB));
                    foreach (var (value, unit) in LiveLockClock.Parts(left))
                    {
                        _peekNumber.Children.Add(new TextBlock { Text = value, FontFamily = Display, FontSize = 64, FontWeight = FontWeight.Bold, Foreground = ink, VerticalAlignment = VerticalAlignment.Bottom });
                        _peekNumber.Children.Add(new TextBlock
                        {
                            Text = Loc.Get("chaster_unit_" + unit), FontFamily = Display, FontSize = 24, FontWeight = FontWeight.SemiBold,
                            Foreground = unitInk, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(2, 0, 12, 12),
                        });
                    }
                    _peekLead.Text = Loc.Get("chaster_peek_left");
                }
                if (LiveLockClock.EndsAt(snapshot, balance, now) is { } ends)
                    _peekEnds.Text = Loc.GetF("chaster_chip_ends", ends.ToLocalTime().ToString("ddd d MMM HH:mm"));
                if (snapshot.IsFrozen) _peekNote.Text = Loc.Get("chaster_chip_frozen_tip");
                else if (chaster.LockLookup == LockLookup.Away) _peekNote.Text = Loc.Get("chaster_chip_away_tip");
            }
            else BigWord(Loc.Get("chaster_pill_hidden"), Grey);

            if (LiveLockClock.PendingAdd(balance) > 0) _peekPending.Text = Loc.GetF("chaster_peek_pending", CircesTab.Format(balance));
            if (chaster.Mood is { } mood) _peekMood.Text = Loc.GetF("chaster_mood_peek", Loc.Get(mood.WordKey), mood.FactorText);
            if (chaster.IsPaused) _peekNote.Text = Loc.Get("chaster_chip_paused_tip");
            if (hold > TimeSpan.Zero) _peekNote.Text = Loc.GetF("chaster_chip_hold_tip", LockClockText.HoldClock(hold));
            Collapse();
        }

        private void Collapse()
        {
            foreach (var t in new[] { _peekLead, _peekEnds, _peekPending, _peekMood, _peekNote })
                t.IsVisible = !string.IsNullOrEmpty(t.Text);
        }

        private void BigWord(string word, Color ink) => _peekNumber.Children.Add(new TextBlock
        {
            Text = word, FontFamily = Display, FontSize = 44, FontWeight = FontWeight.Bold, Foreground = new SolidColorBrush(ink),
        });

        internal string ClockText => _clock.IsVisible ? _clock.Text ?? "" : "";

        private void Wire()
        {
            _wired = ChasterHead.Service;
            if (_wired != null)
            {
                _wired.LockChanged += OnServiceChanged;
                _wired.LinkChanged += OnServiceChanged;
                _wired.Booked += OnBooked;
            }
            _tick = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _tick.Tick += (_, _) => Apply();
            _tick.Start();
            Apply();
            StartIdle();
        }

        private void Unwire()
        {
            if (_wired != null)
            {
                _wired.LockChanged -= OnServiceChanged;
                _wired.LinkChanged -= OnServiceChanged;
                _wired.Booked -= OnBooked;
                _wired = null;
            }
            _tick?.Stop();
            _tick = null;
            StopIdle();
        }

        /// <summary>WPF StartIdle (:645): a slow breath on the rim glow and a small swing of the
        /// padlock every 7 s. Ambient loops only; stopped when the chip leaves the tree.</summary>
        private void StartIdle()
        {
            if (_idle != null || !AmbientFxCanvas.Env.AllowAmbientLoops) return;
            var run = _idle = new System.Threading.CancellationTokenSource();
            try
            {
                TryBreathe();
                // WPF's 7 s storyboard rests 5.6 s, then swings for 0.9 s. The rest needs no clock: a
                // timer fires each swing and the tween runs only while the padlock moves. (The old
                // Animation targeted the Canvas with RotateTransform keys, so it never moved.)
                var timer = _swingTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(5600) };
                timer.Tick += (_, _) =>
                {
                    if (run.IsCancellationRequested) { timer.Stop(); return; }
                    timer.Interval = TimeSpan.FromSeconds(7);
                    _swingRun?.Stop();
                    _swingRun = global::ConditioningControlPanel.Avalonia.Helpers.TransformTween.Run(
                        _swing, TimeSpan.FromMilliseconds(900), SwingKeys, token: run.Token);
                };
                timer.Start();
            }
            catch (Exception ex) { Serilog.Log.Debug("[Chaster] rail chip idle: {E}", ex.Message); }
        }

        // WPF keys 5600 -> 6500 ms of the 7 s storyboard, as fractions of the 900 ms swing.
        private static readonly (double, AvaloniaProperty, double)[] SwingKeys =
        {
            (0d, RotateTransform.AngleProperty, 0d), (180 / 900d, RotateTransform.AngleProperty, -9d),
            (400 / 900d, RotateTransform.AngleProperty, 7d), (620 / 900d, RotateTransform.AngleProperty, -4d),
            (1d, RotateTransform.AngleProperty, 0d),
        };

        /// <summary>WPF :656: the rim glow breathes 0.35 &lt;-&gt; 0.8 over 2.2 s, on the shared beat.</summary>
        private void TryBreathe()
        {
            if (_idle == null || _breath?.IsRunning == true) return;
            _breath ??= new global::ConditioningControlPanel.Avalonia.Views.Features.BreathClock(_glowHost, 2.2);
            _breath.Start((_glowHost, 0.35 / 0.8, 1.0));
        }

        private void StopIdle()
        {
            _idle?.Cancel();
            _idle = null;
            _breath?.Stop();
            _swingTimer?.Stop();
            _swingTimer = null;
            _swingRun?.Stop();
            _swingRun = null;
            _swing.Angle = 0;
        }

        /// <summary>The rim glow: WPF's DropShadowEffect (blur 12, opacity 0.8) as a BoxShadow.</summary>
        private static BoxShadows GlowShadow(Color c) =>
            new(new BoxShadow { Blur = 12, Color = Color.FromArgb(0xCC, c.R, c.G, c.B) });

        /// <summary>WPF Pulse (:681): a 250 ms tint on the ring for whoever just booked something
        /// at this chip. Silent under MotionLevel Off, where the resting colour is all there is.</summary>
        public void Pulse(Color tint)
        {
            try
            {
                if (!AmbientFxCanvas.Env.AllowTransitions) return;
                _pulse?.Cancel();
                var run = _pulse = new System.Threading.CancellationTokenSource();
                LastPulse = tint;
                _ = CircesMoodMeter.Tween(_ringBrush, SolidColorBrush.ColorProperty, tint, _ringRest, 250, new QuadraticEaseOut(), run);
            }
            catch (Exception ex) { Serilog.Log.Debug("[Chaster] rail chip pulse: {E}", ex.Message); }
        }

        private void OnServiceChanged() => Dispatcher.UIThread.Post(Apply, DispatcherPriority.Background);
        private void OnBooked(string eventId, TabBooking booking) => OnServiceChanged();

        /// <summary>WPF Apply (:365): the readout, the palette, the shackle, the dim, the badge, the pip.</summary>
        internal void Apply()
        {
            try
            {
                var chaster = ChasterHead.Service;
                var now = DateTime.UtcNow;
                var snapshot = chaster?.Lock;
                var balance = chaster?.BalanceSeconds ?? 0;
                var clock = LockClockText.State(chaster?.LockLookup ?? LockLookup.Unlinked, snapshot,
                    chaster?.IsLinked == true, chaster?.SafetyHoldRemaining ?? TimeSpan.Zero, now, paused: chaster?.IsPaused == true);

                _clock.Text = clock.State switch
                {
                    LockClockState.Frozen => Loc.Get("chaster_chip_frozen"),
                    LockClockState.Held => Loc.GetF("chaster_chip_hold", clock.Text),
                    LockClockState.Paused => Loc.GetF("chaster_chip_paused", clock.Text == "" || clock.Text == LockClockText.HiddenMark
                        ? clock.Text : LiveText(snapshot, balance, now, clock.Text)).Trim(),
                    LockClockState.Locked or LockClockState.Away => LiveText(snapshot, balance, now, clock.Text),
                    _ => clock.Text,
                };
                _clock.IsVisible = !string.IsNullOrEmpty(_clock.Text);

                var (ink, ring) = clock.State switch
                {
                    LockClockState.Frozen => (Ice, RingIce),
                    LockClockState.Away => (Ink, RingAmber),
                    LockClockState.Held or LockClockState.Paused => (Grey, RingGrey),
                    _ => (Ink, RingPink),
                };
                _inkBrush.Color = ink;
                _bodyBottom.Color = ink;
                _bodyTop.Color = Color.FromRgb((byte)(ink.R + (255 - ink.R) * 0.55), (byte)(ink.G + (255 - ink.G) * 0.55), (byte)(ink.B + (255 - ink.B) * 0.55));
                _ringBrush.Color = _ringRest = ring;
                _glowColour = Color.FromRgb(ring.R, ring.G, ring.B);
                _glowHost.BoxShadow = GlowShadow(_glowColour);
                // A safety hold is not a lock state: the shackle keeps what the lookup said.
                _shackle.Data = clock.State is LockClockState.Locked or LockClockState.Frozen or LockClockState.Hidden
                    or LockClockState.Away or LockClockState.Held or LockClockState.Paused ? ShackleShut : ShackleOpen;
                Opacity = clock.State == LockClockState.Unlinked ? 0.5 : 1.0;

                if (_tick != null)
                {
                    var want = clock.State == LockClockState.Held || LiveLockClock.Ticks(snapshot) || _peek.IsOpen ? TimeSpan.FromSeconds(1) : TimeSpan.FromMinutes(1);
                    if (_tick.Interval != want) _tick.Interval = want;
                }

                var badge = LiveLockClock.Badge(balance);
                _badge.IsVisible = badge.Length > 0;
                _badgeText.Text = badge;
                _badgeBrush.Color = balance > 0 ? BadgeRed : CreditMint;
                _badgeInk.Color = balance > 0 ? Colors.White : Color.FromRgb(0x10, 0x20, 0x1A);

                ApplyMood(chaster?.Mood);
                TryBreathe();
                if (_peek.IsOpen) FillPeek();
            }
            catch (Exception ex) { Serilog.Log.Debug("[Chaster] rail chip paint: {E}", ex.Message); }
        }

        /// <summary>WPF ApplyMood (:492): the pip, popping when the mood moves, at MotionLevel Full only.</summary>
        private void ApplyMood(CircesMood? mood)
        {
            var changed = _mood is { } was && mood is { } now && was != now;
            _mood = mood;
            if (mood is { } m) _moodBrush.Color = CircesMoodMeter.ColourOf(m.Level);
            _moodPip.IsVisible = mood is { Level: not MoodLevel.Calm };
            if (!_moodPip.IsVisible || !changed || AmbientFxCanvas.Env.Level != Models.MotionLevel.Full) return;
            _pop?.Cancel();
            var run = _pop = new System.Threading.CancellationTokenSource();
            _ = CircesMoodMeter.Tween(_moodPop, ScaleTransform.ScaleXProperty, 1.8, 1d, 420, new ElasticEaseOut(), run);
            _ = CircesMoodMeter.Tween(_moodPop, ScaleTransform.ScaleYProperty, 1.8, 1d, 420, new ElasticEaseOut(), run);
        }

        private static string LiveText(LockSnapshot? snapshot, int balance, DateTime now, string fallback)
        {
            if (LiveLockClock.Remaining(snapshot, balance, now) is not { } left) return fallback;
            return left <= TimeSpan.Zero ? Loc.Get("chaster_chip_ready") : LiveLockClock.Compact(left);
        }
    }
}
