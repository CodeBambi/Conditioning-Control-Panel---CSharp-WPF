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
            RenderTransform = new TransformGroup { Children = { _shakeTilt, _shakeShift } };

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

            double to = on ? KnobTravel : 0;
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
