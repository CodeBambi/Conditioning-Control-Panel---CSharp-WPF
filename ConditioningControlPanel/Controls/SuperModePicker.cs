using System;
using System.Collections.Generic;
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
    /// The per-effect pick in the gold v2 box (owner, 2026-10-01), replacing the on/off switch:
    /// one thin pill row of Classic / Both / Super only (Super only only where
    /// <see cref="SuperModeRule.Offered"/> says). A lit thumb slides under the shown pick: gold
    /// when Super runs, muted lilac on Classic.
    ///
    /// <para>It reads and writes only through <see cref="SuperAccess"/>; the rules live in
    /// <see cref="SuperModePick"/>. LOCKED (free account, no try running) = Classic shown, the
    /// Super segments wear a tiny lock, and a press on one never stores anything: the row shakes,
    /// the lock pops, every BASIC sign for the effect is poked (<see cref="LockedPoke"/>) and the
    /// normal TierGate refusal opens. Classic stays pressable. Arrow keys move a focus cursor,
    /// Space or Enter picks it.</para>
    /// </summary>
    public sealed class SuperModePicker : Grid
    {
        private static readonly Color OffBg = Color.FromRgb(0x16, 0x10, 0x28);
        private static readonly Color OffRing = Color.FromRgb(0x3A, 0x30, 0x52);
        private static readonly Color ClassicThumb = Color.FromRgb(0x4A, 0x3E, 0x66);
        private static readonly Color IdleText = Color.FromRgb(0x9C, 0x90, 0xB8);
        private static readonly Color Gold = Color.FromRgb(0xFF, 0xCF, 0x6B);
        private static readonly Color GoldDeep = Color.FromRgb(0xC9, 0x8E, 0x2E);
        private static readonly Color Cream = Color.FromRgb(0xFF, 0xF6, 0xE0);
        private static readonly Color Ink = Color.FromRgb(0x2A, 0x1A, 0x08);
        private static readonly Color LockedRing = Color.FromArgb(0x80, 0xFF, 0xCF, 0x6B);
        private static readonly Color LockedText = Color.FromRgb(0x8A, 0x74, 0x48);

        private static readonly Geometry LockGeo = Freeze(Geometry.Parse(
            "M7,10 V8 A5,5 0 0 1 17,8 V10 H18 A1,1 0 0 1 19,11 V20 A1,1 0 0 1 18,21 H6 A1,1 0 0 1 5,20 V11 A1,1 0 0 1 6,10 Z M9,10 H15 V8 A3,3 0 0 0 9,8 Z"));

        /// <summary>A refused press on a locked Super segment: any BASIC sign for this effect shakes too.</summary>
        public static event Action<SuperEffect>? LockedPoke;

        public static readonly RoutedEvent LockedClickEvent = EventManager.RegisterRoutedEvent(
            nameof(LockedClick), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(SuperModePicker));

        /// <summary>Bubbles from a refused press so a host can react.</summary>
        public event RoutedEventHandler LockedClick
        {
            add => AddHandler(LockedClickEvent, value);
            remove => RemoveHandler(LockedClickEvent, value);
        }

        public static readonly DependencyProperty EffectProperty = DependencyProperty.Register(
            nameof(Effect), typeof(SuperEffect), typeof(SuperModePicker),
            new PropertyMetadata(SuperEffect.FlickerDeck, (d, _) => ((SuperModePicker)d).Rebuild()));

        public SuperEffect Effect
        {
            get => (SuperEffect)GetValue(EffectProperty);
            set => SetValue(EffectProperty, value);
        }

        private sealed class Segment
        {
            public SuperMode Mode;
            public Border Host = null!;
            public TextBlock Label = null!;
            public SolidColorBrush Ink = null!;
            public Path Lock = null!;
            public ScaleTransform LockPop = null!;
        }

        private readonly List<Segment> _segs = new();
        private readonly StackPanel _row = new() { Orientation = Orientation.Horizontal };
        private readonly Canvas _thumbLayer = new() { IsHitTestVisible = false };
        private readonly Border _thumb;
        private readonly GradientStop _thumbFrom = new(ClassicThumb, 0), _thumbTo = new(ClassicThumb, 1);
        private readonly TranslateTransform _thumbShift = new();
        private readonly ScaleTransform _thumbSquash = new();
        private readonly DropShadowEffect _glow = new() { ShadowDepth = 0, BlurRadius = 10, Opacity = 0, Color = Gold };
        private readonly SolidColorBrush _ringBrush = new(OffRing);
        private readonly TranslateTransform _shakeShift = new();
        private readonly RotateTransform _shakeTilt = new();
        private readonly Canvas _sparks = new() { IsHitTestVisible = false, ClipToBounds = false };
        private SuperMode? _lastShown;
        private bool _lastLocked;
        private bool _placed;
        private SuperMode _cursor = SuperModeRule.Default;
        private SuperMode? _pressed;

        public SuperModePicker()
        {
            Focusable = true;
            FocusVisualStyle = null;
            HorizontalAlignment = HorizontalAlignment.Left;
            VerticalAlignment = VerticalAlignment.Top;
            RenderTransformOrigin = new Point(0.5, 0.5);
            RenderTransform = new TransformGroup { Children = { _shakeTilt, _shakeShift } };

            _thumb = new Border
            {
                CornerRadius = new CornerRadius(9),
                Background = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1), GradientStops = { _thumbFrom, _thumbTo } },
                Effect = _glow,
                RenderTransform = new TransformGroup { Children = { _thumbSquash, _thumbShift } },
                Visibility = Visibility.Hidden,
            };
            _thumbLayer.Children.Add(_thumb);

            var inner = new Grid();
            inner.Children.Add(_thumbLayer);
            inner.Children.Add(_row);
            Children.Add(new Border
            {
                CornerRadius = new CornerRadius(11),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(1.5),
                Background = Freeze(new SolidColorBrush(Color.FromArgb(0xCC, OffBg.R, OffBg.G, OffBg.B))),
                BorderBrush = _ringBrush,
                Child = inner,
            });
            Children.Add(_sparks);

            _row.SizeChanged += (_, _) => PlaceThumb(animate: false);
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
            Rebuild();
        }

        /// <summary>Unlocked = paid tier or this effect's weekly try running.</summary>
        public bool IsLockedNow => !SuperAccess.IsUnlocked(Effect);

        /// <summary>The segment drawn lit right now.</summary>
        public SuperMode ShownMode => _lastShown ?? SuperModeRule.Default;

        /// <summary>The modes on the row, in order (tests).</summary>
        public IReadOnlyList<SuperMode> Modes => _segs.ConvertAll(s => s.Mode);

        private static bool FreeTrying(SuperEffect e) => SuperPreview.Trying == e && !TierGate.HasPremium;

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

        /// <summary>One segment per offered mode; rebuilt when the effect changes.</summary>
        private void Rebuild()
        {
            _row.Children.Clear();
            _segs.Clear();
            foreach (var mode in SuperModeRule.Offered(Effect))
            {
                var seg = new Segment { Mode = mode, Ink = new SolidColorBrush(IdleText), LockPop = new ScaleTransform { CenterX = 4, CenterY = 4 } };
                seg.Label = new TextBlock { FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = seg.Ink, VerticalAlignment = VerticalAlignment.Center };
                seg.Lock = new Path
                {
                    Data = LockGeo, Width = 8, Height = 8, Stretch = Stretch.Uniform, Fill = seg.Ink,
                    Margin = new Thickness(0, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center,
                    Visibility = Visibility.Collapsed, RenderTransform = seg.LockPop,
                };
                var content = new StackPanel { Orientation = Orientation.Horizontal };
                content.Children.Add(seg.Lock);
                content.Children.Add(seg.Label);
                seg.Host = new Border
                {
                    CornerRadius = new CornerRadius(9),
                    Padding = new Thickness(10, 1, 10, 2),
                    BorderThickness = new Thickness(1),
                    BorderBrush = Brushes.Transparent,
                    Background = Brushes.Transparent,
                    Cursor = Cursors.Hand,
                    Child = content,
                };
                var s = seg;
                seg.Host.MouseEnter += (_, _) => Hover(s, true);
                seg.Host.MouseLeave += (_, _) => { Hover(s, false); if (_pressed == s.Mode) _pressed = null; };
                seg.Host.MouseLeftButtonDown += (_, e) => { _pressed = s.Mode; _cursor = s.Mode; Focus(); e.Handled = true; };
                seg.Host.MouseLeftButtonUp += (_, e) =>
                {
                    e.Handled = true;
                    if (_pressed != s.Mode) return;
                    _pressed = null;
                    Activate(s.Mode);
                };
                _row.Children.Add(seg.Host);
                _segs.Add(seg);
            }
            _lastShown = null;
            _placed = false;
            Refresh(animate: false);
        }

        /// <summary>Repaint from <see cref="SuperAccess"/>: thumb, colours, locks, tooltips.</summary>
        public void Refresh(bool animate)
        {
            var effect = Effect;
            bool locked = IsLockedNow;
            var shown = SuperModePick.Shown(effect, !locked, FreeTrying(effect), SuperAccess.GetMode(effect));
            string name = SuperNames.Name(effect);
            bool glowAllowed = PerformanceProfile.AllowGlow(PerformanceProfile.CurrentTier);
            bool changed = animate && _lastShown != null && (_lastShown != shown || _lastLocked != locked);
            int ms = changed ? SuperChromeJuice.ChangeMs(MotionFx.Level) : 0;
            bool runs = SuperModeRule.RunsSuper(shown);

            AutomationProperties.SetName(this, Loc.GetF("super_switch_name", name));
            AutomationProperties.SetItemStatus(this, Loc.Get(SuperModePick.LabelKey(shown)));
            AutomationProperties.SetHelpText(this, Loc.Get(SuperModePick.LineKey(shown)));

            foreach (var seg in _segs)
            {
                bool lit = seg.Mode == shown;
                bool segLocked = locked && SuperModeRule.RunsSuper(seg.Mode);
                seg.Label.Text = Loc.Get(SuperModePick.LabelKey(seg.Mode));
                seg.Lock.Visibility = segLocked ? Visibility.Visible : Visibility.Collapsed;
                seg.Host.ToolTip = segLocked ? Loc.GetF("super_switch_locked_tip", name) : Loc.Get(SuperModePick.LineKey(seg.Mode));
                AutomationProperties.SetName(seg.Host, seg.Label.Text);
                var ink = lit ? (runs ? Ink : Cream) : segLocked ? LockedText : IdleText;
                SuperChrome.Tween(seg.Ink, SolidColorBrush.ColorProperty, ink, ms);
            }
            SuperChrome.Tween(_thumbFrom, GradientStop.ColorProperty, runs ? GoldDeep : ClassicThumb, ms);
            SuperChrome.Tween(_thumbTo, GradientStop.ColorProperty, runs ? Gold : ClassicThumb, ms);
            SuperChrome.Tween(_glow, DropShadowEffect.OpacityProperty, runs && glowAllowed ? 0.35 : 0.0, ms);
            SuperChrome.Tween(_ringBrush, SolidColorBrush.ColorProperty, runs ? Color.FromArgb(0xB3, Gold.R, Gold.G, Gold.B) : locked ? LockedRing : OffRing, ms);

            bool lightsUp = animate && _lastShown is SuperMode was && SuperModePick.LightsUp(was, shown);
            bool moved = _lastShown != shown;
            _lastShown = shown;
            _lastLocked = locked;
            if (!IsKeyboardFocusWithin) _cursor = shown;
            if (moved) PlaceThumb(animate && _placed);
            if (lightsUp) { Burst(); Thud(); }
            PaintCursor();
        }

        /// <summary>The lit thumb under the shown segment. The slide ends on a 3% overshoot (BackEase), half
        /// when reduced, and lands at once when motion is off or nothing is laid out yet.</summary>
        private void PlaceThumb(bool animate)
        {
            var seg = _segs.Find(s => s.Mode == ShownMode);
            if (seg == null || seg.Host.ActualWidth <= 0) return;
            double x = seg.Host.TranslatePoint(new Point(0, 0), _thumbLayer).X;
            double w = seg.Host.ActualWidth, h = seg.Host.ActualHeight;
            _thumb.Height = h;
            _thumbSquash.CenterX = w / 2;
            _thumbSquash.CenterY = h / 2;
            _thumb.Visibility = Visibility.Visible;
            if (animate && MotionFx.AllowTransitions && _placed)
            {
                var dur = TimeSpan.FromMilliseconds(MotionFx.Level == MotionLevel.Reduced ? 140 : 190);
                double amp = MotionFx.Level == MotionLevel.Reduced ? 0.15 : 0.3;
                double fromX = _thumbShift.X, fromW = double.IsNaN(_thumb.Width) ? w : _thumb.Width;
                _thumbShift.X = x;
                _thumb.Width = w;
                _thumbShift.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(fromX, x, dur)
                {
                    EasingFunction = new BackEase { Amplitude = amp, EasingMode = EasingMode.EaseOut },
                    FillBehavior = FillBehavior.Stop,
                });
                _thumb.BeginAnimation(WidthProperty, new DoubleAnimation(fromW, w, dur)
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                    FillBehavior = FillBehavior.Stop,
                });
            }
            else
            {
                _thumbShift.BeginAnimation(TranslateTransform.XProperty, null);
                _thumb.BeginAnimation(WidthProperty, null);
                _thumbShift.X = x;
                _thumb.Width = w;
            }
            _placed = true;
        }

        private void Hover(Segment seg, bool over)
        {
            if (seg.Mode == ShownMode) { seg.Host.Background = Brushes.Transparent; return; }
            seg.Host.Background = over ? HoverWash : Brushes.Transparent;
        }

        private static readonly Brush HoverWash = Freeze(new SolidColorBrush(Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF)));
        private static readonly Brush CursorRing = Freeze(new SolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF)));

        /// <summary>The keyboard cursor: a thin white ring on one segment, only while focused from the keyboard.</summary>
        private void PaintCursor()
        {
            foreach (var seg in _segs)
                seg.Host.BorderBrush = IsKeyboardFocused && seg.Mode == _cursor ? CursorRing : Brushes.Transparent;
        }

        protected override void OnIsKeyboardFocusedChanged(DependencyPropertyChangedEventArgs e)
        {
            base.OnIsKeyboardFocusedChanged(e);
            if (IsKeyboardFocused && _pressed == null) _cursor = ShownMode;
            PaintCursor();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            var offered = SuperModeRule.Offered(Effect);
            switch (e.Key)
            {
                case Key.Left: case Key.Up: _cursor = SuperModePick.Step(Effect, _cursor, -1); break;
                case Key.Right: case Key.Down: _cursor = SuperModePick.Step(Effect, _cursor, +1); break;
                case Key.Home: _cursor = offered[0]; break;
                case Key.End: _cursor = offered[^1]; break;
                case Key.Space: case Key.Enter: Activate(_cursor); break;
                default: return;
            }
            e.Handled = true;
            PaintCursor();
        }

        protected override void OnMouseRightButtonDown(MouseButtonEventArgs e) { base.OnMouseRightButtonDown(e); e.Handled = true; }

        protected override void OnMouseRightButtonUp(MouseButtonEventArgs e) { base.OnMouseRightButtonUp(e); e.Handled = true; }

        /// <summary>The one press path: store, refuse with a shake, or hold still during a try.</summary>
        public void Activate(SuperMode mode)
        {
            var effect = Effect;
            bool locked = IsLockedNow;
            var shown = SuperModePick.Shown(effect, !locked, FreeTrying(effect), SuperAccess.GetMode(effect));
            switch (SuperModePick.Decide(!locked, FreeTrying(effect), shown, mode))
            {
                case SuperPickOutcome.Refuse:
                    SuperChrome.Shake(this);
                    Nudge(mode);
                    try { LockedPoke?.Invoke(effect); } catch (Exception ex) { App.Logger?.Debug("SuperModePicker poke: {E}", ex.Message); }
                    RaiseEvent(new RoutedEventArgs(LockedClickEvent, this));
                    TierGate.DemandPremium(Loc.GetF("super_switch_name", SuperNames.Name(effect)));
                    return;
                case SuperPickOutcome.Store:
                    SuperAccess.SetMode(effect, mode);
                    Refresh(animate: true);
                    return;
            }
        }

        /// <summary>Six small gold sparks off the thumb over 420 ms, only when Super lights up.
        /// Reduced motion = half the distance, motion off = none.</summary>
        private void Burst()
        {
            if (!MotionFx.AllowTransitions || !_placed) return;
            _sparks.Children.Clear();
            double reach = MotionFx.Level == MotionLevel.Reduced ? 0.5 : 1.0;
            var origin = _thumb.TranslatePoint(new Point(_thumb.Width / 2, _thumb.Height / 2), _sparks);
            var dur = TimeSpan.FromMilliseconds(420);
            for (int i = 0; i < SparkCount; i++)
            {
                double ang = i * (2 * Math.PI / SparkCount) + 0.4;
                double dist = (14 + (i % 2) * 5) * reach;
                var dot = new Ellipse { Width = 2.5, Height = 2.5, Fill = SparkBrush };
                Canvas.SetLeft(dot, origin.X - 1.25);
                Canvas.SetTop(dot, origin.Y - 1.25);
                var move = new TranslateTransform();
                dot.RenderTransform = move;
                _sparks.Children.Add(dot);
                var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
                move.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, Math.Cos(ang) * dist, dur) { EasingFunction = ease });
                move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, Math.Sin(ang) * dist * 0.6, dur) { EasingFunction = ease });
                var fade = new DoubleAnimation(0.9, 0, dur) { BeginTime = TimeSpan.FromMilliseconds(80) };
                if (i == SparkCount - 1) fade.Completed += (_, _) => _sparks.Children.Clear();
                dot.BeginAnimation(OpacityProperty, fade);
            }
        }

        /// <summary>The thumb lands with weight as the slide arrives (<see cref="SuperChromeJuice.ThudKeys"/>).</summary>
        private void Thud()
        {
            var keys = SuperChromeJuice.ThudKeys(SuperChromeJuice.Amount(MotionFx.Level));
            if (keys.Length == 0) return;
            var x = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(SuperChromeJuice.ThudMs), BeginTime = TimeSpan.FromMilliseconds(SuperChromeJuice.ThudDelayMs) };
            var y = new DoubleAnimationUsingKeyFrames { Duration = x.Duration, BeginTime = x.BeginTime };
            foreach (var (t, sx, sy) in keys)
            {
                var at = KeyTime.FromPercent(t);
                // The thumb is wide: a third of the squash along it reads the same as the knob's full one.
                x.KeyFrames.Add(new EasingDoubleKeyFrame(1 + (sx - 1) / 3, at, new SineEase { EasingMode = EasingMode.EaseInOut }));
                y.KeyFrames.Add(new EasingDoubleKeyFrame(sy, at, new SineEase { EasingMode = EasingMode.EaseInOut }));
            }
            x.FillBehavior = y.FillBehavior = FillBehavior.Stop;
            _thumbSquash.BeginAnimation(ScaleTransform.ScaleXProperty, x);
            _thumbSquash.BeginAnimation(ScaleTransform.ScaleYProperty, y);
        }

        /// <summary>A refused press: the segment's lock pops and the ring blinks gold once (a single
        /// swell, never a flash). The pop sizes with motion; the blink is colour only.</summary>
        private void Nudge(SuperMode mode)
        {
            var seg = _segs.Find(s => s.Mode == mode);
            var keys = SuperChromeJuice.LockPopKeys(SuperChromeJuice.Amount(MotionFx.Level));
            if (seg != null && keys.Length > 0)
            {
                var pop = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(SuperChromeJuice.LockPopMs), FillBehavior = FillBehavior.Stop };
                foreach (var (t, s) in keys)
                    pop.KeyFrames.Add(new EasingDoubleKeyFrame(s, KeyTime.FromPercent(t), new CubicEase { EasingMode = EasingMode.EaseOut }));
                seg.LockPop.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
                seg.LockPop.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
            }
            var rest = (Color)_ringBrush.GetValue(SolidColorBrush.ColorProperty);
            _ringBrush.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(rest, Gold, TimeSpan.FromMilliseconds(SuperChromeJuice.RingBlinkMs / 2))
            {
                AutoReverse = true,
                FillBehavior = FillBehavior.Stop,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
            });
        }

        private const int SparkCount = 6;
        private static readonly Brush SparkBrush = Freeze(new SolidColorBrush(Gold));

        private static T Freeze<T>(T f) where T : Freezable { f.Freeze(); return f; }
    }

    /// <summary>The small tween and shake helpers the gold v2 box and its pick share.</summary>
    public static class SuperChrome
    {
        /// <summary>
        /// Ease a colour or a double to <paramref name="to"/> over <paramref name="ms"/>. The base value
        /// is written first and the tween stops onto it, so hover or a repaint never fights a held
        /// animation. 0 ms lands at once, but leaves a running tween alone when it already heads there.
        /// </summary>
        internal static void Tween(Animatable target, DependencyProperty prop, object to, int ms)
        {
            object current = target.GetValue(prop);
            bool headingThere = Equals(target.ReadLocalValue(prop), to);
            if (ms <= 0)
            {
                if (headingThere) return;
                target.BeginAnimation(prop, null);
                target.SetValue(prop, to);
                return;
            }
            if (headingThere && Equals(current, to)) return;
            target.SetValue(prop, to);
            AnimationTimeline anim = to is Color c
                ? new ColorAnimation((Color)current, c, TimeSpan.FromMilliseconds(ms)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.Stop }
                : new DoubleAnimation((double)current, (double)to, TimeSpan.FromMilliseconds(ms)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.Stop };
            target.BeginAnimation(prop, anim);
        }

        /// <summary><see cref="Tween(Animatable, DependencyProperty, object, int)"/> for an element's opacity.</summary>
        internal static void TweenOpacity(UIElement e, double to, int ms)
        {
            double current = e.Opacity;
            bool headingThere = Equals(e.ReadLocalValue(UIElement.OpacityProperty), to);
            if (ms <= 0)
            {
                if (headingThere) return;
                e.BeginAnimation(UIElement.OpacityProperty, null);
                e.Opacity = to;
                return;
            }
            if (headingThere && current == to) return;
            e.Opacity = to;
            e.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(current, to, TimeSpan.FromMilliseconds(ms))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                FillBehavior = FillBehavior.Stop,
            });
        }

        /// <summary>The mockup's `.shake`: 400 ms, -4/+4/-3/+2 px with a 1.5 degree rock. Skipped
        /// when motion is off, halved when reduced. Reuses a [rotate, translate] group already on
        /// the element so a second shake never stacks transforms.</summary>
        public static void Shake(UIElement target)
        {
            if (!MotionFx.AllowTransitions) return;
            RotateTransform tilt;
            TranslateTransform shift;
            if (target.RenderTransform is TransformGroup { IsFrozen: false } g && g.Children.Count == 2
                && g.Children[0] is RotateTransform r && g.Children[1] is TranslateTransform t)
            {
                tilt = r;
                shift = t;
            }
            else
            {
                tilt = new RotateTransform();
                shift = new TranslateTransform();
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
            var rot = new DoubleAnimationUsingKeyFrames { Duration = dur };
            rot.KeyFrames.Add(new LinearDoubleKeyFrame(-1.5 * half, KeyTime.FromPercent(0.2)));
            rot.KeyFrames.Add(new LinearDoubleKeyFrame(1.5 * half, KeyTime.FromPercent(0.4)));
            rot.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(0.6)));
            shift.BeginAnimation(TranslateTransform.XProperty, x);
            tilt.BeginAnimation(RotateTransform.AngleProperty, rot);
        }
    }

    /// <summary>Display names for the eight effects (brand names, one loc key each).</summary>
    public static class SuperNames
    {
        public static string Name(SuperEffect e) => Loc.Get("super_name_" + e.ToString().ToLowerInvariant());
    }
}
