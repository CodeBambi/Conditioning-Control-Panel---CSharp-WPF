// Port of ConditioningControlPanel/Controls/DescentFuseRailChip.cs: the gold clock in the nav
// rail's bottom stack, seven days before the ceremony. Same palette (literals, never the accent),
// same tiers (Core SpiralRoom.WobbleFor), same 2.5 s flash-out at zero, same door (Spiral Room).
// WPF runs key-framed compositor clocks; here one frame timer evaluates the same curves from a
// TimeProvider, so tests step it. The rare/frequent tiers sleep through their stillness.
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services.Descent;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Controls
{
    public sealed class DescentFuseRailChip : Grid
    {
        private static readonly Color FuseGold = Color.FromRgb(0xE0, 0xB0, 0x52);
        private static readonly IBrush GoldBrush = new global::Avalonia.Media.Immutable.ImmutableSolidColorBrush(FuseGold);
        private static readonly IBrush NeutralDigits = new global::Avalonia.Media.Immutable.ImmutableSolidColorBrush(Color.FromRgb(0xC9, 0xC4, 0xD6));
        private static readonly IBrush FaintDigits = new global::Avalonia.Media.Immutable.ImmutableSolidColorBrush(Color.FromArgb(0x8A, 0xC9, 0xC4, 0xD6));
        private static readonly FontFamily Mono = new("Cascadia Mono, Consolas, Courier New, monospace");

        private const double BurstPeakDegrees = 8.0, RareBurstPeriodSeconds = 20.0, FrequentBurstPeriodSeconds = 9.0;
        private const double TrembleDegrees = 3.0, TrembleHalfCycleSeconds = 0.45, ViolentHalfCycleSeconds = 0.11;
        internal const double FlashOutSeconds = 2.5;
        private const double FlashShakePixels = 6.0, FlashSpinDegrees = 7.5;
        private const double RingIdleOpacity = 0.30, RingBreathLo = 0.18, RingBreathHi = 0.55, RingFlareLo = 0.25, RingFlareHi = 0.90;
        private const double RingBreathSeconds = 1.9, RingFlareSeconds = 0.28;
        private static readonly TimeSpan AmbientFrame = TimeSpan.FromMilliseconds(1000.0 / 30);
        private static readonly TimeSpan UrgentFrame = TimeSpan.FromMilliseconds(1000.0 / 50);

        /// <summary>WPF BeginBurst's key frames (seconds, degrees), sine-eased per segment.</summary>
        private static readonly (double T, double A)[] Burst =
        {
            (0.00, 0), (0.06, BurstPeakDegrees), (0.16, -BurstPeakDegrees * 0.80), (0.26, BurstPeakDegrees * 0.55),
            (0.36, -BurstPeakDegrees * 0.32), (0.46, BurstPeakDegrees * 0.15), (0.58, 0),
        };

        /// <summary>WPF BeginFlashClocks' flicker: discrete, accelerating from 0.20 s to ~0.05 s apart.</summary>
        private static readonly (double T, double O)[] Flicker = BuildFlicker();

        /// <summary>The clock (tests step it).</summary>
        internal static TimeProvider Time = TimeProvider.System;

        private readonly Border _ring;
        private readonly RotateTransform _rotate = new(0);
        private readonly TranslateTransform _shake = new(0, 0);
        private readonly TextBlock _label;
        private TextBlock? _tipDigits, _tipPresence;
        private Border? _tip;
        private DispatcherTimer? _timer;
        private long _since;
        private bool _wired, _flashingOut, _quietFade;
        private FuseWobbleTier _tier = FuseWobbleTier.None;
        private DescentFusePhase _phase = DescentFusePhase.Dark;

        public DescentFuseRailChip()
        {
            IsVisible = false;   // dark by default: every install today measures as if absent
            Margin = new Thickness(0, 2, 0, 4);
            Cursor = new Cursor(StandardCursorType.Hand);
            Background = Brushes.Transparent;
            RenderTransform = _shake;
            Focusable = true;   // keyboard reach (P17): Enter/Space opens the room like a click
            ColumnDefinitions.Add(new ColumnDefinition(56, GridUnitType.Pixel));
            ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));

            _ring = new Border
            {
                Width = 50, Height = 50, CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(1),
                BorderBrush = GoldBrush, Opacity = 0, IsHitTestVisible = false,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            };
            Children.Add(_ring);

            var glyph = new Path
            {
                Width = 22, Height = 22, Stretch = Stretch.Uniform, Fill = GoldBrush, IsHitTestVisible = false,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                Data = BuildClockGeometry(), RenderTransformOrigin = RelativePoint.Center, RenderTransform = _rotate,
            };
            Children.Add(new Border
            {
                Width = 44, Height = 44, CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1),
                Background = new SolidColorBrush(Color.FromRgb(0x23, 0x1E, 0x33)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x8A, 0x6D, 0x3B)),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                Child = glyph,
            });

            // The open-rail readout: a plain TextBlock, so CacheNavRailParts fades it with the rail.
            _label = new TextBlock
            {
                FontFamily = Mono, FontSize = 12, Foreground = GoldBrush, Opacity = 0, IsHitTestVisible = false,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 8, 0), Text = string.Empty,
            };
            SetColumn(_label, 1);
            Children.Add(_label);

            PointerReleased += (_, e) => { if (e.InitialPressMouseButton == MouseButton.Left) OpenSpiralRoom(); };
            KeyDown += (_, e) =>
            {
                if (e.Key is not (Key.Enter or Key.Space)) return;
                e.Handled = true;
                OpenSpiralRoom();
            };
            Loaded += (_, _) => Wire();
            Unloaded += (_, _) => Unwire();
        }

        internal bool IsWobbling => _timer != null || _flashingOut;
        internal bool IsFlashingOut => _flashingOut;
        internal FuseWobbleTier Tier => _tier;
        internal double Angle => _rotate.Angle;
        internal double RingOpacity => _ring.Opacity;
        internal string Label => _label.Text ?? string.Empty;
        internal string TipText => _tip is null ? string.Empty
            : $"{_tipDigits?.Text}|{DescentFuseCopy.ChipSubtitle}" + (_tipPresence is { IsVisible: true } ? "|" + _tipPresence.Text : "");

        private void Wire()
        {
            if (_wired) return;
            _wired = true;
            try
            {
                var fuse = App.DescentCountdown;
                if (fuse is null) { Apply(DescentFusePhase.Dark); return; }
                fuse.PhaseChanged += OnPhaseChanged;
                fuse.Tick += OnTick;
                Apply(fuse.LastAnnouncedPhase);   // the Start() announcement predates the shell
            }
            catch (Exception ex) { Log.Debug("[Fuse] rail chip could not wire: {E}", ex.Message); }
        }

        private void Unwire()
        {
            if (!_wired) return;
            _wired = false;
            if (App.DescentCountdown is { } fuse)
            {
                fuse.PhaseChanged -= OnPhaseChanged;
                fuse.Tick -= OnTick;
            }
            if (_flashingOut) FinishFlashOut();
            StopWobble();
        }

        private void OnPhaseChanged(object? sender, DescentFusePhaseChangedEventArgs e) => Apply(e.Current);

        private void OnTick(object? sender, TimeSpan remaining)
        {
            if (!IsVisible || _flashingOut) return;
            var text = DescentFuseCopy.TMinus(remaining);
            _label.Text = text;
            if (_tipDigits != null) _tipDigits.Text = text;   // retyped in place, never rebuilt
            ApplyPresence();
        }

        /// <summary>WPF Apply: the whole chip from scratch for a phase.</summary>
        internal void Apply(DescentFusePhase phase)
        {
            var previous = _phase;
            _phase = phase;
            if (_flashingOut) return;   // the goodbye owns the chip for its 2.5 s

            if (!SpiralRoom.FuseChipVisible(phase, CoreSettings.Current?.DescentMigrationCompleted == true))
            {
                if (IsVisible && SpiralRoom.ShouldFlashOutAtZero(previous, phase)) BeginFlashOut();
                else HideChip();
                return;
            }

            IsVisible = true;
            var text = DescentFuseCopy.TMinus(App.DescentCountdown?.Remaining ?? TimeSpan.Zero);
            _label.Text = text;
            ToolTip.SetTip(this, BuildTooltip());
            _tipDigits!.Text = text;
            _tipDigits.Foreground = phase >= DescentFusePhase.Terminal ? GoldBrush : NeutralDigits;
            ApplyPresence();
            StartWobble(SpiralRoom.WobbleFor(phase));
        }

        private void HideChip()
        {
            IsVisible = false;
            StopWobble();
            ToolTip.SetTip(this, null);
            _tip = null;
            _tipDigits = _tipPresence = null;
            _label.Text = string.Empty;
        }

        /// <summary>"N falling with you", from Vigil on, only when the server said N (0 is a reading).</summary>
        private void ApplyPresence()
        {
            if (_tipPresence is null) return;
            var count = _phase >= DescentFusePhase.Vigil ? App.DescentCountdown?.VigilCount : null;
            _tipPresence.IsVisible = count is >= 0;
            if (count is >= 0) _tipPresence.Text = DescentFuseCopy.Presence(count.Value);
        }

        private Border BuildTooltip()
        {
            if (_tip != null) return _tip;
            _tipDigits = new TextBlock { FontFamily = Mono, FontSize = 15, Foreground = NeutralDigits, HorizontalAlignment = HorizontalAlignment.Center };
            _tipPresence = new TextBlock { FontSize = 10, Margin = new Thickness(0, 4, 0, 0), Foreground = FaintDigits, HorizontalAlignment = HorizontalAlignment.Center, IsVisible = false };
            var stack = new StackPanel();
            stack.Children.Add(_tipDigits);
            stack.Children.Add(new TextBlock
            {
                Text = DescentFuseCopy.ChipSubtitle, FontSize = 9, Margin = new Thickness(0, 3, 0, 0),
                Foreground = FaintDigits, HorizontalAlignment = HorizontalAlignment.Center,
                FontFeatures = new FontFeatureCollection { FontFeature.Parse("smcp") },
            });
            stack.Children.Add(_tipPresence);
            return _tip = new Border
            {
                Child = stack,
                Background = new SolidColorBrush(Color.FromArgb(0xE0, 0x0A, 0x05, 0x14)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 0xE0, 0xB0, 0x52)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(9, 4, 9, 4),
            };
        }

        // ============================== the clocks ==============================

        /// <summary>Reduced motion gets a STATIC chip (ring idle, no tilt), never a missing one.</summary>
        private void StartWobble(FuseWobbleTier tier)
        {
            if (tier == _tier && _timer != null) return;   // a repaint must not restart the burst
            StopWobble();
            _tier = tier;
            if (tier == FuseWobbleTier.None) return;
            if (!AmbientFxCanvas.Env.AllowAmbientLoops)
            {
                _ring.Opacity = RingIdleOpacity;
                return;
            }
            StartClock(tier == FuseWobbleTier.Violent ? UrgentFrame : AmbientFrame);
        }

        private void StartClock(TimeSpan frame)
        {
            _since = Time.GetTimestamp();
            _timer = new DispatcherTimer(frame, DispatcherPriority.Render, (_, _) => Step());
            _timer.Start();
            Step();
        }

        private void StopWobble()
        {
            _timer?.Stop();
            _timer = null;
            _tier = FuseWobbleTier.None;
            _rotate.Angle = 0;
            _ring.Opacity = 0;
            Opacity = 1;
            _shake.X = 0;
        }

        /// <summary>One frame: every value is a pure function of the time since the clock started.</summary>
        internal void Step()
        {
            if (_timer is null) return;
            var t = Time.GetElapsedTime(_since).TotalSeconds;
            if (_flashingOut) { FlashFrame(t); return; }
            // Shell hidden to the tray: doze at 1 Hz instead of drawing for nobody (P01).
            if (!IsEffectivelyVisible || TopLevel.GetTopLevel(this) is not { IsVisible: true }) { _timer.Interval = TimeSpan.FromSeconds(1); return; }
            if (_tier != FuseWobbleTier.Rare && _tier != FuseWobbleTier.Frequent)
                _timer.Interval = _tier == FuseWobbleTier.Violent ? UrgentFrame : AmbientFrame;
            switch (_tier)
            {
                case FuseWobbleTier.Rare:
                case FuseWobbleTier.Frequent:
                {
                    var period = _tier == FuseWobbleTier.Rare ? RareBurstPeriodSeconds : FrequentBurstPeriodSeconds;
                    var at = t % period;
                    _ring.Opacity = RingIdleOpacity;
                    _rotate.Angle = BurstAngle(at);
                    // Sleep through the stillness instead of waking 30 times a second for nothing.
                    _timer.Interval = at >= Burst[^1].T ? TimeSpan.FromSeconds(Math.Max(period - at, 0.001)) : AmbientFrame;
                    break;
                }
                case FuseWobbleTier.Tremble:
                    _rotate.Angle = Swing(t, TrembleHalfCycleSeconds, -TrembleDegrees, TrembleDegrees, eased: true);
                    _ring.Opacity = Swing(t, RingBreathSeconds, RingBreathLo, RingBreathHi, eased: true);
                    break;
                case FuseWobbleTier.Violent:
                    _rotate.Angle = Swing(t, ViolentHalfCycleSeconds, -TrembleDegrees, TrembleDegrees, eased: true);
                    _ring.Opacity = Swing(t, RingFlareSeconds, RingFlareLo, RingFlareHi, eased: true);
                    break;
            }
        }

        private static double SineInOut(double x) => -(Math.Cos(Math.PI * x) - 1) / 2;

        /// <summary>WPF DoubleAnimation from->to over a half cycle, AutoReverse, repeating.</summary>
        private static double Swing(double t, double half, double from, double to, bool eased)
        {
            var phase = t % (2 * half) / half;
            var x = phase <= 1 ? phase : 2 - phase;
            return from + (to - from) * (eased ? SineInOut(x) : x);
        }

        private static double BurstAngle(double at)
        {
            for (int i = 1; i < Burst.Length; i++)
                if (at <= Burst[i].T)
                {
                    var (t0, a0) = Burst[i - 1];
                    var (t1, a1) = Burst[i];
                    return a0 + (a1 - a0) * SineInOut((at - t0) / (t1 - t0));
                }
            return 0;
        }

        // ============================== the flash-out ==============================

        /// <summary>ZERO, live, with the chip on screen: flicker, shake and flare for 2.5 s, then
        /// collapse. Reduced motion (transitions off) fades quietly over the same span.</summary>
        private void BeginFlashOut()
        {
            StopWobble();
            _flashingOut = true;
            _quietFade = !AmbientFxCanvas.Env.AllowTransitions;
            IsVisible = true;
            ToolTip.SetTip(this, null);
            Log.Information("[Fuse] the rail chip is flashing out - zero has arrived.");
            StartClock(UrgentFrame);
        }

        private void FlashFrame(double t)
        {
            if (t >= FlashOutSeconds) { FinishFlashOut(); return; }
            if (_quietFade) { var x = t / FlashOutSeconds; Opacity = 1 - x * x; return; }
            var o = 0.0;
            foreach (var (at, v) in Flicker) { if (at > t) break; o = v; }
            Opacity = o;
            _shake.X = Swing(t, 0.045, -FlashShakePixels, FlashShakePixels, eased: false);
            _rotate.Angle = Swing(t, 0.07, -FlashSpinDegrees, FlashSpinDegrees, eased: false);
            _ring.Opacity = t < 0.14 ? Lerp(RingFlareLo, 1.0, SineOut(t / 0.14))
                : t < FlashOutSeconds * 0.6 ? Lerp(1.0, 0.55, SineOut((t - 0.14) / (FlashOutSeconds * 0.6 - 0.14)))
                : Lerp(0.55, 0.0, SineOut((t - FlashOutSeconds * 0.6) / (FlashOutSeconds * 0.4)));
        }

        private static double SineOut(double x) => Math.Sin(x * Math.PI / 2);
        private static double Lerp(double a, double b, double x) => a + (b - a) * x;

        private void FinishFlashOut()
        {
            _flashingOut = false;
            HideChip();
        }

        private static (double, double)[] BuildFlicker()
        {
            var list = new System.Collections.Generic.List<(double, double)>();
            double at = 0;
            bool lit = true;
            while (at < FlashOutSeconds)
            {
                list.Add((at, lit ? 1.0 : 0.12));
                lit = !lit;
                at += 0.20 - 0.15 * (at / FlashOutSeconds);
            }
            return list.ToArray();
        }

        /// <summary>The chip is a door into the Spiral Room and nothing else.</summary>
        private void OpenSpiralRoom()
        {
            try { (TopLevel.GetTopLevel(this) as MainShellWindow)?.ShowTab(SpiralRoom.TabKey); }
            catch (Exception ex) { Log.Debug("[Fuse] rail chip door: {E}", ex.Message); }
        }

        /// <summary>WPF BuildClockGeometry: a small alarm clock under EvenOdd.</summary>
        private static Geometry BuildClockGeometry()
        {
            var g = new GeometryGroup { FillRule = FillRule.EvenOdd };
            var dial = new Point(12, 13.2);
            g.Children.Add(new EllipseGeometry(new Rect(dial.X - 7.4, dial.Y - 7.4, 14.8, 14.8)));
            g.Children.Add(new EllipseGeometry(new Rect(dial.X - 5.9, dial.Y - 5.9, 11.8, 11.8)));
            g.Children.Add(new RectangleGeometry(new Rect(11.35, 8.7, 1.3, 5.15)));
            g.Children.Add(new RectangleGeometry(new Rect(12.65, 12.55, 3.5, 1.3)));
            g.Children.Add(new EllipseGeometry(new Rect(4.8, 2.7, 4, 4)));
            g.Children.Add(new EllipseGeometry(new Rect(15.2, 2.7, 4, 4)));
            return g;
        }
    }
}
