using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Super;

namespace ConditioningControlPanel.Controls
{
    /// <summary>
    /// The Super switch: a 54x30 pill with a bolt knob (handoff-1001 mockup, `.sw`). On = pink
    /// gradient with a glow and the knob slid right; off = dark; LOCKED (free account, no try
    /// running) = gold ring and a lock glyph on the knob.
    ///
    /// <para>It reads and writes only through <see cref="SuperAccess"/>. A click while locked
    /// never flips anything: it shakes the switch, pokes every BASIC sign showing this effect
    /// (<see cref="LockedPoke"/>), raises <see cref="LockedClickEvent"/> so a host tile can open
    /// the feature, and shows the normal TierGate refusal.</para>
    ///
    /// <para>The switch eats its own mouse buttons (left AND right), so a click on it never
    /// reaches the tile underneath and never toggles the base effect.</para>
    /// </summary>
    public sealed class SuperSwitch : Grid
    {
        private const double PillW = 54, PillH = 30, KnobD = 23, KnobTravel = 24;

        private static readonly Color OffBg = Color.FromRgb(0x0E, 0x08, 0x20);
        private static readonly Color OffRing = Color.FromRgb(0x4A, 0x3A, 0x70);
        private static readonly Color OffKnob = Color.FromRgb(0x6A, 0x5A, 0x90);
        private static readonly Color OnRing = Color.FromRgb(0xFF, 0xA3, 0xD4);
        private static readonly Color OnGlyph = Color.FromRgb(0xD6, 0x3A, 0x86);
        private static readonly Color Gold = Color.FromRgb(0xFF, 0xCF, 0x6B);
        private static readonly Color GoldGlyph = Color.FromRgb(0x2B, 0x1A, 0x00);
        private static readonly Color PinkGlow = Color.FromRgb(0xFF, 0x4F, 0xA3);

        private static readonly Brush OnBgBrush = Freeze(new LinearGradientBrush(
            Color.FromRgb(0xC0, 0x28, 0x7A), Color.FromRgb(0xFF, 0x5F, 0xB0), 0));

        private static readonly Geometry BoltGeo = Geometry.Parse("M13,2 L4,14 L10,14 L9,22 L18,10 L12,10 Z");
        private static readonly Geometry LockGeo = Geometry.Parse(
            "M7,10 V8 A5,5 0 0 1 17,8 V10 H18 A1,1 0 0 1 19,11 V20 A1,1 0 0 1 18,21 H6 A1,1 0 0 1 5,20 V11 A1,1 0 0 1 6,10 Z M9,10 H15 V8 A3,3 0 0 0 9,8 Z");

        /// <summary>A locked switch was clicked: any BASIC sign for this effect shakes too.</summary>
        public static event Action<SuperEffect>? LockedPoke;

        public static readonly RoutedEvent LockedClickEvent = EventManager.RegisterRoutedEvent(
            nameof(LockedClick), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(SuperSwitch));

        /// <summary>Bubbles from a locked click so the host tile can open its feature.</summary>
        public event RoutedEventHandler LockedClick
        {
            add => AddHandler(LockedClickEvent, value);
            remove => RemoveHandler(LockedClickEvent, value);
        }

        public static readonly DependencyProperty EffectProperty = DependencyProperty.Register(
            nameof(Effect), typeof(SuperEffect), typeof(SuperSwitch),
            new PropertyMetadata(SuperEffect.FlickerDeck, (d, _) => ((SuperSwitch)d).Refresh(animate: false)));

        public SuperEffect Effect
        {
            get => (SuperEffect)GetValue(EffectProperty);
            set => SetValue(EffectProperty, value);
        }

        private readonly Border _pill;
        private readonly Ellipse _knob;
        private readonly Path _glyph;
        private readonly Grid _knobHost;
        private readonly TranslateTransform _knobShift = new();
        private readonly TranslateTransform _shakeShift = new();
        private readonly RotateTransform _shakeTilt = new();
        private readonly ScaleTransform _hoverScale = new(1, 1);
        private readonly Border _focusRing;
        private readonly Canvas _sparks = new() { IsHitTestVisible = false, ClipToBounds = false };
        private double _restGlow;
        private bool _glowAllowed;
        private readonly DropShadowEffect _glow = new() { ShadowDepth = 0, BlurRadius = 16, Opacity = 0 };
        private bool _pressed;
        private bool? _lastOn;

        public SuperSwitch()
        {
            Width = PillW;
            Height = PillH;
            Cursor = Cursors.Hand;
            Focusable = true;
            FocusVisualStyle = null;
            RenderTransformOrigin = new Point(0.5, 0.5);
            RenderTransform = new TransformGroup { Children = { _hoverScale, _shakeTilt, _shakeShift } };

            // FocusVisualStyle is off (the default dashed box reads as a bug on a pill), so keyboard
            // focus gets its own ring instead of nothing.
            _focusRing = new Border
            {
                CornerRadius = new CornerRadius(PillH / 2 + 3),
                BorderThickness = new Thickness(2),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF)),
                Margin = new Thickness(-4),
                IsHitTestVisible = false,
                Visibility = Visibility.Collapsed,
            };
            Children.Add(_focusRing);

            _pill = new Border
            {
                CornerRadius = new CornerRadius(PillH / 2),
                BorderThickness = new Thickness(1.5),
                Background = new SolidColorBrush(OffBg),
                BorderBrush = new SolidColorBrush(OffRing),
                Effect = _glow,
            };
            Children.Add(_pill);

            _knob = new Ellipse { Width = KnobD, Height = KnobD, Fill = new SolidColorBrush(OffKnob) };
            _glyph = new Path
            {
                Data = BoltGeo,
                Width = 12, Height = 12,
                Stretch = Stretch.Uniform,
                Fill = new SolidColorBrush(OffBg),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            _knobHost = new Grid
            {
                Width = KnobD, Height = KnobD,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(2.5, 2.5, 0, 0),
                RenderTransform = _knobShift,
                IsHitTestVisible = false,
            };
            _knobHost.Children.Add(_knob);
            _knobHost.Children.Add(_glyph);
            Children.Add(_knobHost);
            Children.Add(_sparks);

            Loaded += (_, _) =>
            {
                SuperAccess.HookTierEvents();
                SuperAccess.Changed += OnChanged;
                SuperPreview.StateChanged += OnPreviewChanged;
                Refresh(animate: false);
            };
            Unloaded += (_, _) =>
            {
                SuperAccess.Changed -= OnChanged;
                SuperPreview.StateChanged -= OnPreviewChanged;
            };
        }

        /// <summary>Unlocked = paid tier or this effect's weekly try running.</summary>
        public bool IsLockedNow => !SuperAccess.IsUnlocked(Effect);

        private void OnChanged(SuperEffect e)
        {
            if (e != Effect) return;
            if (Dispatcher.CheckAccess()) Refresh(animate: true);
            else Dispatcher.BeginInvoke(() => Refresh(animate: true));
        }

        private void OnPreviewChanged()
        {
            if (Dispatcher.CheckAccess()) Refresh(animate: true);
            else Dispatcher.BeginInvoke(() => Refresh(animate: true));
        }

        /// <summary>Repaint from <see cref="SuperAccess"/>.</summary>
        public void Refresh(bool animate)
        {
            bool locked = IsLockedNow;
            bool on = !locked && SuperAccess.IsOn(Effect);
            string name = SuperNames.Name(Effect);
            AutomationProperties.SetName(this, Loc.GetF("super_switch_name", name));
            ToolTip = locked ? Loc.GetF("super_switch_locked_tip", name) : Loc.GetF("super_switch_name", name);
            // A bloom still running would hide every Opacity write below.
            _glow.BeginAnimation(DropShadowEffect.OpacityProperty, null);


            if (on)
            {
                _pill.Background = OnBgBrush;
                _pill.BorderBrush = new SolidColorBrush(OnRing);
                _glow.Color = PinkGlow;
                _glow.BlurRadius = 16;
                _glow.Opacity = PerformanceProfile.AllowGlow(PerformanceProfile.CurrentTier) ? 0.55 : 0;
                _knob.Fill = Brushes.White;
                _glyph.Fill = new SolidColorBrush(OnGlyph);
                _glyph.Data = BoltGeo;
            }
            else if (locked)
            {
                _pill.Background = new SolidColorBrush(OffBg);
                _pill.BorderBrush = new SolidColorBrush(Gold);
                _glow.Color = Gold;
                _glow.BlurRadius = 12;
                _glow.Opacity = PerformanceProfile.AllowGlow(PerformanceProfile.CurrentTier) ? 0.3 : 0;
                _knob.Fill = new SolidColorBrush(Gold);
                _glyph.Fill = new SolidColorBrush(GoldGlyph);
                _glyph.Data = LockGeo;
            }
            else
            {
                _pill.Background = new SolidColorBrush(OffBg);
                _pill.BorderBrush = new SolidColorBrush(OffRing);
                _glow.Opacity = 0;
                _knob.Fill = new SolidColorBrush(OffKnob);
                _glyph.Fill = new SolidColorBrush(OffBg);
                _glyph.Data = BoltGeo;
            }

            _glowAllowed = PerformanceProfile.AllowGlow(PerformanceProfile.CurrentTier);
            _restGlow = _glow.Opacity;
            if (IsMouseOver) ApplyHover(true);

            double to = on ? KnobTravel : 0;
            // Switching on (or a weekly try starting) earns a small burst: the conversion moment.
            if (animate && _lastOn == false && on) Burst();
            if (animate && _lastOn != on && MotionFx.AllowTransitions)
            {
                // cubic-bezier(.2,1.6,.4,1) over 220 ms: a small overshoot past the end.
                var slide = new DoubleAnimation(to, TimeSpan.FromMilliseconds(220))
                {
                    EasingFunction = new BackEase { Amplitude = 0.45, EasingMode = EasingMode.EaseOut },
                };
                _knobShift.BeginAnimation(TranslateTransform.XProperty, slide);
            }
            else
            {
                _knobShift.BeginAnimation(TranslateTransform.XProperty, null);
                _knobShift.X = to;
            }
            _lastOn = on;
        }

        protected override void OnMouseEnter(MouseEventArgs e) { base.OnMouseEnter(e); ApplyHover(true); }

        protected override void OnMouseLeave(MouseEventArgs e) { base.OnMouseLeave(e); ApplyHover(false); }

        protected override void OnIsKeyboardFocusedChanged(DependencyPropertyChangedEventArgs e)
        {
            base.OnIsKeyboardFocusedChanged(e);
            _focusRing.Visibility = IsKeyboardFocused ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>Hover: the glow lifts (gold when locked, lilac when off, pink when on) and the
        /// pill grows 4%. The grow is skipped with motion off; the glow follows the perf tier.</summary>
        private void ApplyHover(bool over)
        {
            if (_glowAllowed)
            {
                if (over)
                {
                    if (_glow.Opacity <= 0.01) _glow.Color = OffRing;
                    _glow.Opacity = Math.Min(0.9, Math.Max(_restGlow + 0.3, 0.4));
                }
                else
                {
                    _glow.Opacity = _restGlow;
                }
            }
            double scale = over && MotionFx.AllowTransitions ? 1.04 : 1.0;
            if (MotionFx.AllowTransitions)
            {
                var a = new DoubleAnimation(scale, TimeSpan.FromMilliseconds(140)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
                _hoverScale.BeginAnimation(ScaleTransform.ScaleXProperty, a);
                _hoverScale.BeginAnimation(ScaleTransform.ScaleYProperty, a);
            }
            else
            {
                _hoverScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                _hoverScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                _hoverScale.ScaleX = _hoverScale.ScaleY = 1;
            }
        }

        /// <summary>
        /// Eight sparks fly off the knob and fade over 520 ms. Reduced motion = half the distance,
        /// motion off = none. Purely visual, nothing hit-testable.
        /// </summary>
        private void Burst()
        {
            if (!MotionFx.AllowTransitions) return;
            _sparks.Children.Clear();
            double reach = MotionFx.Level == MotionLevel.Reduced ? 0.5 : 1.0;
            double cx = 2.5 + KnobTravel + KnobD / 2, cy = PillH / 2;
            var dur = TimeSpan.FromMilliseconds(520);
            for (int i = 0; i < SparkCount; i++)
            {
                double ang = i * (2 * Math.PI / SparkCount) + 0.3;
                double dist = (18 + (i % 3) * 6) * reach;
                double size = i % 2 == 0 ? 4 : 3;
                var dot = new Ellipse
                {
                    Width = size, Height = size,
                    Fill = i % 3 == 0 ? Brushes.White : SparkBrush,
                };
                Canvas.SetLeft(dot, cx - size / 2);
                Canvas.SetTop(dot, cy - size / 2);
                var move = new TranslateTransform();
                dot.RenderTransform = move;
                _sparks.Children.Add(dot);
                var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
                move.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, Math.Cos(ang) * dist, dur) { EasingFunction = ease });
                move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, Math.Sin(ang) * dist, dur) { EasingFunction = ease });
                var fade = new DoubleAnimation(1, 0, dur) { BeginTime = TimeSpan.FromMilliseconds(120) };
                if (i == SparkCount - 1) fade.Completed += (_, _) => _sparks.Children.Clear();
                dot.BeginAnimation(OpacityProperty, fade);
            }
            if (_glowAllowed)
            {
                // One bloom of the glow on top of the sparks, back to rest.
                var bloom = new DoubleAnimation(0.95, _restGlow, TimeSpan.FromMilliseconds(600)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
                bloom.Completed += (_, _) => { _glow.BeginAnimation(DropShadowEffect.OpacityProperty, null); _glow.Opacity = IsMouseOver ? Math.Max(_restGlow + 0.3, 0.4) : _restGlow; };
                _glow.BeginAnimation(DropShadowEffect.OpacityProperty, bloom);
            }
        }

        private const int SparkCount = 8;
        private static readonly Brush SparkBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xFF, 0xA3, 0xD4)));

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            _pressed = true;
            CaptureMouse();
            Focus();
            e.Handled = true;
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonUp(e);
            bool wasPressed = _pressed;
            _pressed = false;
            if (IsMouseCaptured) ReleaseMouseCapture();
            e.Handled = true;
            if (!wasPressed) return;
            var p = e.GetPosition(this);
            if (p.X < 0 || p.Y < 0 || p.X > ActualWidth || p.Y > ActualHeight) return;
            Activate();
        }

        protected override void OnMouseRightButtonDown(MouseButtonEventArgs e) { base.OnMouseRightButtonDown(e); e.Handled = true; }

        protected override void OnMouseRightButtonUp(MouseButtonEventArgs e) { base.OnMouseRightButtonUp(e); e.Handled = true; }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.Key is Key.Space or Key.Enter) { e.Handled = true; Activate(); }
        }

        /// <summary>The one click path: flip when unlocked, refuse with a shake when locked.</summary>
        public void Activate()
        {
            var effect = Effect;
            if (IsLockedNow)
            {
                Shake(this);
                try { LockedPoke?.Invoke(effect); } catch (Exception ex) { App.Logger?.Debug("SuperSwitch poke: {E}", ex.Message); }
                RaiseEvent(new RoutedEventArgs(LockedClickEvent, this));
                TierGate.DemandPremium(Loc.GetF("super_switch_name", SuperNames.Name(effect)));
                return;
            }
            // A running try is not the player's switch: leave the setting alone while it runs.
            if (SuperPreview.Trying == effect && !TierGate.HasPremium) return;
            SuperAccess.Set(effect, !SuperAccess.IsSwitchedOn(effect));
            Refresh(animate: true);
        }

        /// <summary>The mockup's `.shake`: 400 ms, -4/+4/-3/+2 px with a 1.5 degree rock. Skipped
        /// when motion is off.</summary>
        public static void Shake(UIElement target)
        {
            if (!MotionFx.AllowTransitions) return;
            var shift = new TranslateTransform();
            var tilt = new RotateTransform();
            if (target is SuperSwitch sw)
            {
                shift = sw._shakeShift;
                tilt = sw._shakeTilt;
            }
            else
            {
                if (target is FrameworkElement fe) fe.RenderTransformOrigin = new Point(0.5, 0.5);
                target.RenderTransform = new TransformGroup { Children = { tilt, shift } };
            }
            double half = MotionFx.Level == MotionLevel.Reduced ? 0.5 : 1.0;
            var dur = TimeSpan.FromMilliseconds(400);
            var x = new DoubleAnimationUsingKeyFrames { Duration = dur };
            x.KeyFrames.Add(new LinearDoubleKeyFrame(-4 * half, KeyTime.FromPercent(0.2)));
            x.KeyFrames.Add(new LinearDoubleKeyFrame(4 * half, KeyTime.FromPercent(0.4)));
            x.KeyFrames.Add(new LinearDoubleKeyFrame(-3 * half, KeyTime.FromPercent(0.6)));
            x.KeyFrames.Add(new LinearDoubleKeyFrame(2 * half, KeyTime.FromPercent(0.8)));
            x.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(1)));
            var r = new DoubleAnimationUsingKeyFrames { Duration = dur };
            r.KeyFrames.Add(new LinearDoubleKeyFrame(-1.5 * half, KeyTime.FromPercent(0.2)));
            r.KeyFrames.Add(new LinearDoubleKeyFrame(1.5 * half, KeyTime.FromPercent(0.4)));
            r.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(0.6)));
            shift.BeginAnimation(TranslateTransform.XProperty, x);
            tilt.BeginAnimation(RotateTransform.AngleProperty, r);
        }

        private static Brush Freeze(Brush b) { b.Freeze(); return b; }
    }

    /// <summary>Display names for the eight effects (brand names, one loc key each).</summary>
    public static class SuperNames
    {
        public static string Name(SuperEffect e) => Loc.Get("super_name_" + e.ToString().ToLowerInvariant());
    }
}
