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
    /// The Super switch, the quiet version (owner, 2026-10-01: "not subtle enough"). A 36x20 thin
    /// track with a plain knob and no icon. Off = muted lilac on dark; on = gold with a soft gold
    /// glow; LOCKED (free account, no try running) = dim, with a tiny lock on the knob. It lives
    /// only inside <see cref="SuperBox"/> on a feature page, never on a dashboard tile.
    ///
    /// <para>It reads and writes only through <see cref="SuperAccess"/>. The knob draws the
    /// player's own choice (<see cref="SuperAccess.IsSelected"/>), not whether the base feature is
    /// on, so switching a base off never makes this switch look flipped. A click while locked never
    /// flips anything: it shakes the switch, pokes every BASIC sign showing this effect
    /// (<see cref="LockedPoke"/>), raises <see cref="LockedClickEvent"/> and shows the normal
    /// TierGate refusal.</para>
    /// </summary>
    public sealed class SuperSwitch : Grid
    {
        private const double TrackW = 36, TrackH = 20, KnobD = 14, KnobInset = 3, KnobTravel = TrackW - KnobD - 2 * KnobInset;

        private static readonly Color OffBg = Color.FromRgb(0x16, 0x10, 0x28);
        private static readonly Color OffRing = Color.FromRgb(0x3A, 0x30, 0x52);
        private static readonly Color OffKnob = Color.FromRgb(0x74, 0x68, 0x90);
        private static readonly Color Gold = Color.FromRgb(0xFF, 0xCF, 0x6B);
        private static readonly Color GoldDeep = Color.FromRgb(0x8A, 0x63, 0x1E);
        private static readonly Color Cream = Color.FromRgb(0xFF, 0xF6, 0xE0);
        private static readonly Color LockedRing = Color.FromArgb(0x80, 0xFF, 0xCF, 0x6B);
        private static readonly Color LockedKnob = Color.FromRgb(0x8A, 0x74, 0x48);

        private static readonly Brush OnBgBrush = Freeze(new LinearGradientBrush(GoldDeep, Gold, 0));

        private static readonly Geometry LockGeo = Geometry.Parse(
            "M7,10 V8 A5,5 0 0 1 17,8 V10 H18 A1,1 0 0 1 19,11 V20 A1,1 0 0 1 18,21 H6 A1,1 0 0 1 5,20 V11 A1,1 0 0 1 6,10 Z M9,10 H15 V8 A3,3 0 0 0 9,8 Z");

        /// <summary>A locked switch was clicked: any BASIC sign for this effect shakes too.</summary>
        public static event Action<SuperEffect>? LockedPoke;

        public static readonly RoutedEvent LockedClickEvent = EventManager.RegisterRoutedEvent(
            nameof(LockedClick), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(SuperSwitch));

        /// <summary>Bubbles from a locked click so a host can react.</summary>
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

        private readonly Border _track;
        private readonly Ellipse _knob;
        private readonly Path _lock;
        private readonly Grid _knobHost;
        private readonly TranslateTransform _knobShift = new();
        private readonly TranslateTransform _shakeShift = new();
        private readonly RotateTransform _shakeTilt = new();
        private readonly Border _focusRing;
        private readonly Canvas _sparks = new() { IsHitTestVisible = false, ClipToBounds = false };
        private readonly DropShadowEffect _glow = new() { ShadowDepth = 0, BlurRadius = 10, Opacity = 0, Color = Gold };
        private double _restGlow;
        private bool _glowAllowed;
        private bool _pressed;
        private bool? _lastOn;

        public SuperSwitch()
        {
            Width = TrackW;
            Height = TrackH;
            Cursor = Cursors.Hand;
            Focusable = true;
            FocusVisualStyle = null;
            RenderTransformOrigin = new Point(0.5, 0.5);
            RenderTransform = new TransformGroup { Children = { _shakeTilt, _shakeShift } };

            _focusRing = new Border
            {
                CornerRadius = new CornerRadius(TrackH / 2 + 2),
                BorderThickness = new Thickness(1.5),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF)),
                Margin = new Thickness(-3),
                IsHitTestVisible = false,
                Visibility = Visibility.Collapsed,
            };
            Children.Add(_focusRing);

            _track = new Border
            {
                CornerRadius = new CornerRadius(TrackH / 2),
                BorderThickness = new Thickness(1),
                Background = new SolidColorBrush(OffBg),
                BorderBrush = new SolidColorBrush(OffRing),
                Opacity = 0.92,
                Effect = _glow,
            };
            Children.Add(_track);

            _knob = new Ellipse { Width = KnobD, Height = KnobD, Fill = new SolidColorBrush(OffKnob) };
            _lock = new Path
            {
                Data = LockGeo,
                Width = 8, Height = 8,
                Stretch = Stretch.Uniform,
                Fill = new SolidColorBrush(OffBg),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Visibility = Visibility.Collapsed,
            };
            _knobHost = new Grid
            {
                Width = KnobD, Height = KnobD,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(KnobInset, (TrackH - KnobD) / 2, 0, 0),
                RenderTransform = _knobShift,
                IsHitTestVisible = false,
            };
            _knobHost.Children.Add(_knob);
            _knobHost.Children.Add(_lock);
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

        /// <summary>The knob sits on the on side (the player's choice, or a running try).</summary>
        public bool ShowsOn => _lastOn == true;

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
            bool on = !locked && SuperAccess.IsSelected(Effect);
            string name = SuperNames.Name(Effect);
            AutomationProperties.SetName(this, Loc.GetF("super_switch_name", name));
            ToolTip = locked ? Loc.GetF("super_switch_locked_tip", name) : null;
            _glow.BeginAnimation(DropShadowEffect.OpacityProperty, null);
            _glowAllowed = PerformanceProfile.AllowGlow(PerformanceProfile.CurrentTier);

            if (on)
            {
                _track.Background = OnBgBrush;
                _track.BorderBrush = new SolidColorBrush(Gold);
                _glow.Opacity = _glowAllowed ? 0.35 : 0;
                _knob.Fill = new SolidColorBrush(Cream);
                _lock.Visibility = Visibility.Collapsed;
            }
            else if (locked)
            {
                _track.Background = new SolidColorBrush(OffBg);
                _track.BorderBrush = new SolidColorBrush(LockedRing);
                _glow.Opacity = 0;
                _knob.Fill = new SolidColorBrush(LockedKnob);
                _lock.Fill = new SolidColorBrush(OffBg);
                _lock.Visibility = Visibility.Visible;
            }
            else
            {
                _track.Background = new SolidColorBrush(OffBg);
                _track.BorderBrush = new SolidColorBrush(OffRing);
                _glow.Opacity = 0;
                _knob.Fill = new SolidColorBrush(OffKnob);
                _lock.Visibility = Visibility.Collapsed;
            }

            _restGlow = _glow.Opacity;
            double to = on ? KnobTravel : 0;
            // Only the moment of switching on earns the small gold burst.
            if (animate && _lastOn == false && on) Burst();
            if (animate && _lastOn != null && _lastOn != on && MotionFx.AllowTransitions)
            {
                var slide = new DoubleAnimation(to, TimeSpan.FromMilliseconds(180))
                {
                    EasingFunction = new BackEase { Amplitude = 0.3, EasingMode = EasingMode.EaseOut },
                };
                _knobShift.BeginAnimation(TranslateTransform.XProperty, slide);
            }
            else
            {
                _knobShift.BeginAnimation(TranslateTransform.XProperty, null);
                _knobShift.X = to;
            }
            _lastOn = on;
            if (IsMouseOver) ApplyHover(true);
        }

        protected override void OnMouseEnter(MouseEventArgs e) { base.OnMouseEnter(e); ApplyHover(true); }

        protected override void OnMouseLeave(MouseEventArgs e) { base.OnMouseLeave(e); ApplyHover(false); }

        protected override void OnIsKeyboardFocusedChanged(DependencyPropertyChangedEventArgs e)
        {
            base.OnIsKeyboardFocusedChanged(e);
            _focusRing.Visibility = IsKeyboardFocused ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>Hover lifts the track a touch; the gold glow only rises when on.</summary>
        private void ApplyHover(bool over)
        {
            if (_glowAllowed && _lastOn == true)
                _glow.Opacity = over ? Math.Min(0.6, _restGlow + 0.15) : _restGlow;
            _track.Opacity = over ? 1.0 : 0.92;
        }

        /// <summary>Six small gold sparks off the knob over 420 ms, only at the moment of switching
        /// on. Reduced motion = half the distance, motion off = none.</summary>
        private void Burst()
        {
            if (!MotionFx.AllowTransitions) return;
            _sparks.Children.Clear();
            double reach = MotionFx.Level == MotionLevel.Reduced ? 0.5 : 1.0;
            double cx = KnobInset + KnobTravel + KnobD / 2, cy = TrackH / 2;
            var dur = TimeSpan.FromMilliseconds(420);
            for (int i = 0; i < SparkCount; i++)
            {
                double ang = i * (2 * Math.PI / SparkCount) + 0.4;
                double dist = (11 + (i % 2) * 4) * reach;
                var dot = new Ellipse { Width = 2.5, Height = 2.5, Fill = SparkBrush };
                Canvas.SetLeft(dot, cx - 1.25);
                Canvas.SetTop(dot, cy - 1.25);
                var move = new TranslateTransform();
                dot.RenderTransform = move;
                _sparks.Children.Add(dot);
                var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
                move.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, Math.Cos(ang) * dist, dur) { EasingFunction = ease });
                move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, Math.Sin(ang) * dist, dur) { EasingFunction = ease });
                var fade = new DoubleAnimation(0.9, 0, dur) { BeginTime = TimeSpan.FromMilliseconds(80) };
                if (i == SparkCount - 1) fade.Completed += (_, _) => _sparks.Children.Clear();
                dot.BeginAnimation(OpacityProperty, fade);
            }
        }

        private const int SparkCount = 6;
        private static readonly Brush SparkBrush = Freeze(new SolidColorBrush(Gold));

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
        /// when motion is off, halved when reduced.</summary>
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
