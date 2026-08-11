using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Features
{
    /// <summary>
    /// A mosaic tile carrying TWO features on one diagonal ("/") split: half A is the top-left
    /// triangle, half B the bottom-right. Left-click on a half opens that half's page (ClickA/
    /// ClickB), right-click toggles it (ToggleA/ToggleB) - the same left=open / right=toggle
    /// grammar the rail chips and the single tiles use.
    ///
    /// FX plumbing (breath clock, hover lift, focus gating) is deliberately copied from
    /// <see cref="FeatureCard"/> rather than shared through a base class: the two controls'
    /// visual trees have nothing in common below the root border, and a base class over two
    /// different XAML trees is where template bugs go to hide. Keep the constants in step with
    /// FeatureCard's when either changes.
    /// </summary>
    public partial class SplitFeatureCard : UserControl
    {
        private const double ActiveGlowMinOpacity = 0.50;
        private const double ActiveGlowMaxOpacity = 0.90;
        private const double ActiveRingMinOpacity = 0.55;
        private const double ActiveRingMaxOpacity = 1.00;
        private const double ActiveBreathSeconds = 3.5;
        private const double RimLightOpacity = 0.85;
        private const int RimLightMs = 150;
        private const int AmbientFrameRate = 24;

        private Window? _hostWindow;
        private bool _hovered;

        public static readonly DependencyProperty TitleAProperty =
            DependencyProperty.Register(nameof(TitleA), typeof(string), typeof(SplitFeatureCard),
                new PropertyMetadata("A", (d, e) => ((SplitFeatureCard)d).TxtTitleA.Text = e.NewValue as string ?? ""));

        public static readonly DependencyProperty TitleBProperty =
            DependencyProperty.Register(nameof(TitleB), typeof(string), typeof(SplitFeatureCard),
                new PropertyMetadata("B", (d, e) => ((SplitFeatureCard)d).TxtTitleB.Text = e.NewValue as string ?? ""));

        public static readonly DependencyProperty IconAProperty =
            DependencyProperty.Register(nameof(IconA), typeof(ImageSource), typeof(SplitFeatureCard),
                new PropertyMetadata(null, (d, e) => ((SplitFeatureCard)d).ApplyIcon(((SplitFeatureCard)d).HalfHostA, e.NewValue as ImageSource)));

        public static readonly DependencyProperty IconBProperty =
            DependencyProperty.Register(nameof(IconB), typeof(ImageSource), typeof(SplitFeatureCard),
                new PropertyMetadata(null, (d, e) => ((SplitFeatureCard)d).ApplyIcon(((SplitFeatureCard)d).HalfHostB, e.NewValue as ImageSource)));

        public static readonly DependencyProperty IsActiveAProperty =
            DependencyProperty.Register(nameof(IsActiveA), typeof(bool), typeof(SplitFeatureCard),
                new PropertyMetadata(false, (d, _) => ((SplitFeatureCard)d).ApplyActiveState()));

        public static readonly DependencyProperty IsActiveBProperty =
            DependencyProperty.Register(nameof(IsActiveB), typeof(bool), typeof(SplitFeatureCard),
                new PropertyMetadata(false, (d, _) => ((SplitFeatureCard)d).ApplyActiveState()));

        public static readonly RoutedEvent ClickAEvent =
            EventManager.RegisterRoutedEvent(nameof(ClickA), RoutingStrategy.Bubble,
                typeof(RoutedEventHandler), typeof(SplitFeatureCard));

        public static readonly RoutedEvent ClickBEvent =
            EventManager.RegisterRoutedEvent(nameof(ClickB), RoutingStrategy.Bubble,
                typeof(RoutedEventHandler), typeof(SplitFeatureCard));

        public static readonly RoutedEvent ToggleAEvent =
            EventManager.RegisterRoutedEvent(nameof(ToggleA), RoutingStrategy.Bubble,
                typeof(RoutedEventHandler), typeof(SplitFeatureCard));

        public static readonly RoutedEvent ToggleBEvent =
            EventManager.RegisterRoutedEvent(nameof(ToggleB), RoutingStrategy.Bubble,
                typeof(RoutedEventHandler), typeof(SplitFeatureCard));

        public string TitleA { get => (string)GetValue(TitleAProperty); set => SetValue(TitleAProperty, value); }
        public string TitleB { get => (string)GetValue(TitleBProperty); set => SetValue(TitleBProperty, value); }
        public ImageSource? IconA { get => (ImageSource?)GetValue(IconAProperty); set => SetValue(IconAProperty, value); }
        public ImageSource? IconB { get => (ImageSource?)GetValue(IconBProperty); set => SetValue(IconBProperty, value); }
        public bool IsActiveA { get => (bool)GetValue(IsActiveAProperty); set => SetValue(IsActiveAProperty, value); }
        public bool IsActiveB { get => (bool)GetValue(IsActiveBProperty); set => SetValue(IsActiveBProperty, value); }

        public event RoutedEventHandler ClickA { add => AddHandler(ClickAEvent, value); remove => RemoveHandler(ClickAEvent, value); }
        public event RoutedEventHandler ClickB { add => AddHandler(ClickBEvent, value); remove => RemoveHandler(ClickBEvent, value); }
        public event RoutedEventHandler ToggleA { add => AddHandler(ToggleAEvent, value); remove => RemoveHandler(ToggleAEvent, value); }
        public event RoutedEventHandler ToggleB { add => AddHandler(ToggleBEvent, value); remove => RemoveHandler(ToggleBEvent, value); }

        public SplitFeatureCard()
        {
            InitializeComponent();
            Loaded += OnCardLoaded;
            Unloaded += OnCardUnloaded;
            MouseEnter += (_, _) => ApplyHover(true);
            MouseLeave += (_, _) => { ApplyHover(false); SetHalfHover(null); };
        }

        private void ApplyIcon(Border host, ImageSource? src)
        {
            host.Background = src == null
                ? null
                : new ImageBrush(src) { Stretch = Stretch.UniformToFill, AlignmentY = AlignmentY.Center };
        }

        // ============================== geometry ==============================

        /// <summary>True when the point sits in half A (top-left of the "/" diagonal).</summary>
        private bool IsInHalfA(Point p)
        {
            double w = ContentRoot.ActualWidth, h = ContentRoot.ActualHeight;
            if (w <= 0 || h <= 0) return true;
            return p.X / w + p.Y / h <= 1.0;
        }

        private void OnContentSizeChanged(object sender, SizeChangedEventArgs e)
        {
            try { RebuildGeometry(); }
            catch (Exception ex) { App.Logger?.Debug("SplitFeatureCard.RebuildGeometry: {E}", ex.Message); }
        }

        /// <summary>
        /// Rebuilds every size-dependent geometry: the two triangle clips, the seam and the
        /// active-ring paths. Freezing them matters - four fresh Geometries per resize on
        /// three tiles adds up during the Viewbox's continuous window-resize scaling.
        /// </summary>
        private void RebuildGeometry()
        {
            double w = ContentRoot.ActualWidth, h = ContentRoot.ActualHeight;
            if (w <= 0 || h <= 0) return;

            var triA = TriangleGeometry(new Point(0, 0), new Point(w, 0), new Point(0, h));
            var triB = TriangleGeometry(new Point(w, 0), new Point(w, h), new Point(0, h));

            HalfHostA.Clip = triA;
            HalfHostB.Clip = triB;
            HoverWashA.Data = triA;
            HoverWashB.Data = triB;

            // Rings inset by half their stroke so the outline hugs the triangle instead of
            // being clipped in half by ContentRoot's bounds.
            const double inset = 2.0;
            ActiveRingA.Data = TriangleGeometry(new Point(inset, inset), new Point(w - inset * 2.5, inset), new Point(inset, h - inset * 2.5));
            ActiveRingB.Data = TriangleGeometry(new Point(w - inset, inset * 2.5), new Point(w - inset, h - inset), new Point(inset * 2.5, h - inset));

            var seam = new LineGeometry(new Point(0, h), new Point(w, 0));
            seam.Freeze();
            SeamLine.Data = seam;
        }

        private static PathGeometry TriangleGeometry(Point a, Point b, Point c)
        {
            var fig = new PathFigure(a, new[]
            {
                new LineSegment(b, isStroked: true),
                new LineSegment(c, isStroked: true),
            }, closed: true);
            var geo = new PathGeometry(new[] { fig });
            geo.Freeze();
            return geo;
        }

        // ============================== input ==============================

        private void OnLeftClick(object sender, MouseButtonEventArgs e)
        {
            var evt = IsInHalfA(e.GetPosition(ContentRoot)) ? ClickAEvent : ClickBEvent;
            RaiseEvent(new RoutedEventArgs(evt, this));
        }

        private void OnRightClick(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            var evt = IsInHalfA(e.GetPosition(ContentRoot)) ? ToggleAEvent : ToggleBEvent;
            RaiseEvent(new RoutedEventArgs(evt, this));
        }

        private void OnHalfHoverMove(object sender, MouseEventArgs e)
            => SetHalfHover(IsInHalfA(e.GetPosition(ContentRoot)));

        /// <summary>Plain opacity flips - a wash that tells the cursor which half it is over.</summary>
        private void SetHalfHover(bool? halfA)
        {
            HoverWashA.Opacity = halfA == true ? 1 : 0;
            HoverWashB.Opacity = halfA == false ? 1 : 0;
        }

        // ============================== FX (kept in step with FeatureCard) ==============================

        internal void RefreshFx()
        {
            try { ApplyActiveState(); }
            catch (Exception ex) { App.Logger?.Debug("SplitFeatureCard.RefreshFx: {E}", ex.Message); }
        }

        private bool AmbientAllowed
        {
            get
            {
                var w = _hostWindow;
                if (w != null && (!w.IsActive || w.WindowState == WindowState.Minimized)) return false;
                return MotionFx.AllowAmbientLoops;
            }
        }

        private void ApplyActiveState()
        {
            ActiveRingA.Visibility = IsActiveA ? Visibility.Visible : Visibility.Collapsed;
            ActiveRingB.Visibility = IsActiveB ? Visibility.Visible : Visibility.Collapsed;
            ApplyActiveBreath(IsActiveA || IsActiveB);
        }

        /// <summary>
        /// The card-level glow breathes when EITHER half is on (the drop shadow is a border
        /// effect and cannot be halved); the per-half rings breathe with it. Peak-parked when
        /// ambient motion is off, exactly like FeatureCard.
        /// </summary>
        private void ApplyActiveBreath(bool active)
        {
            try
            {
                if (ActiveGlow == null) return;

                if (!active)
                {
                    StopAnim(ActiveGlow, DropShadowEffect.OpacityProperty);
                    ActiveGlow.Opacity = 0;
                    StopRing(ActiveRingA);
                    StopRing(ActiveRingB);
                    return;
                }

                var tier = PerformanceProfile.CurrentTier;
                bool glow = PerformanceProfile.AllowGlow(tier) && MotionFx.Level != Models.MotionLevel.Off;
                if (glow && !ActiveGlow.IsFrozen)
                    ActiveGlow.BlurRadius = Math.Min(18, PerformanceProfile.MaxGlowBlurRadius(tier));

                if (!AmbientAllowed)
                {
                    StopAnim(ActiveGlow, DropShadowEffect.OpacityProperty);
                    ActiveGlow.Opacity = glow ? ActiveGlowMaxOpacity : 0;
                    ParkRing(ActiveRingA);
                    ParkRing(ActiveRingB);
                    return;
                }

                if (glow) Breathe(ActiveGlow, DropShadowEffect.OpacityProperty, ActiveGlowMinOpacity, ActiveGlowMaxOpacity);
                else { StopAnim(ActiveGlow, DropShadowEffect.OpacityProperty); ActiveGlow.Opacity = 0; }

                if (IsActiveA) Breathe(ActiveRingA, OpacityProperty, ActiveRingMinOpacity, ActiveRingMaxOpacity); else ParkRing(ActiveRingA);
                if (IsActiveB) Breathe(ActiveRingB, OpacityProperty, ActiveRingMinOpacity, ActiveRingMaxOpacity); else ParkRing(ActiveRingB);
            }
            catch (Exception ex) { App.Logger?.Debug("SplitFeatureCard.ApplyActiveBreath: {E}", ex.Message); }
        }

        private static void ParkRing(System.Windows.Shapes.Path ring)
        {
            ring.BeginAnimation(OpacityProperty, null);
            ring.Opacity = ActiveRingMaxOpacity;
        }

        private static void StopRing(System.Windows.Shapes.Path ring)
        {
            ring.BeginAnimation(OpacityProperty, null);
            ring.Opacity = 1;
        }

        private static void Breathe(IAnimatable target, DependencyProperty property, double min, double max)
        {
            if (target is Freezable { IsFrozen: true }) return;
            var anim = new DoubleAnimation(min, max, TimeSpan.FromSeconds(ActiveBreathSeconds))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
            };
            Timeline.SetDesiredFrameRate(anim, AmbientFrameRate);
            target.BeginAnimation(property, anim);
        }

        private static void StopAnim(IAnimatable target, DependencyProperty property)
        {
            if (target is Freezable { IsFrozen: true }) return;
            target.BeginAnimation(property, null);
        }

        private void OnCardLoaded(object sender, RoutedEventArgs e)
        {
            try
            {
                HookWindow(Window.GetWindow(this));
                RebuildGeometry();
                RefreshFx();
            }
            catch (Exception ex) { App.Logger?.Debug("SplitFeatureCard.OnCardLoaded: {E}", ex.Message); }
        }

        private void OnCardUnloaded(object sender, RoutedEventArgs e)
        {
            try
            {
                UnhookWindow();
                ApplyActiveBreath(false);
            }
            catch (Exception ex) { App.Logger?.Debug("SplitFeatureCard.OnCardUnloaded: {E}", ex.Message); }
        }

        private void HookWindow(Window? window)
        {
            if (ReferenceEquals(_hostWindow, window)) return;
            UnhookWindow();
            _hostWindow = window;
            if (_hostWindow == null) return;
            _hostWindow.Activated += OnHostWindowStateish;
            _hostWindow.Deactivated += OnHostWindowStateish;
            _hostWindow.StateChanged += OnHostWindowStateish;
        }

        private void UnhookWindow()
        {
            if (_hostWindow == null) return;
            _hostWindow.Activated -= OnHostWindowStateish;
            _hostWindow.Deactivated -= OnHostWindowStateish;
            _hostWindow.StateChanged -= OnHostWindowStateish;
            _hostWindow = null;
        }

        private void OnHostWindowStateish(object? sender, EventArgs e) => RefreshFx();

        private void ApplyHover(bool on)
        {
            if (_hovered == on) return;
            _hovered = on;
            try
            {
                MotionFx.HoverLift(RootBorder, on);
                if (RimLight == null) return;
                double to = on ? RimLightOpacity : 0;
                if (!MotionFx.AllowTransitions)
                {
                    RimLight.BeginAnimation(OpacityProperty, null);
                    RimLight.Opacity = to;
                    return;
                }
                var fade = new DoubleAnimation(to, TimeSpan.FromMilliseconds(RimLightMs))
                {
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
                };
                RimLight.BeginAnimation(OpacityProperty, fade);
            }
            catch (Exception ex) { App.Logger?.Debug("SplitFeatureCard.ApplyHover: {E}", ex.Message); }
        }
    }
}
