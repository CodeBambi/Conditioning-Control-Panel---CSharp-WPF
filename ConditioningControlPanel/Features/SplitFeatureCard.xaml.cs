using System;
using System.Collections.Generic;
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
    /// Hovering a half sweeps the seam off the card until that half fills the square (see
    /// <see cref="SplitProgressProperty"/>); leaving sweeps it back. While a half is filled it
    /// owns the whole tile, clicks included.
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

        // ---- hover fill (the seam sweep) ----
        /// <summary>Seam parameter at rest: the 50/50 "/" diagonal.</summary>
        private const double SplitRest = 1.0;
        /// <summary>Seam parameter with half A grown over the card (the line has left by the BR corner).</summary>
        private const double SplitFullA = 2.0;
        /// <summary>Seam parameter with half B grown over the card (the line has left by the TL corner).</summary>
        private const double SplitFullB = 0.0;
        private const double TitleExpandedScale = 1.35;
        private const double RingInset = 2.0;
        /// <summary>Pill margin + padding, taken off before capping the grown title's width.</summary>
        private const double TitlePillChrome = 34;
        private const int SplitExpandMs = 260;
        private const int SplitCollapseMs = 210;
        private const int SplitReducedMs = 110;
        private const int SplitFrameRate = 30;

        /// <summary>Shared frozen stand-in for a region the seam has swept out of existence.</summary>
        private static readonly PathGeometry EmptyGeometry = CreateEmptyGeometry();

        private Window? _hostWindow;
        private bool _hovered;
        /// <summary>Which half the mouse has committed the card to: true = A, false = B, null = neither.</summary>
        private bool? _halfHover;

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
            // A tile hidden mid-hover (tab switch out of the dashboard) can be denied its
            // MouseLeave, and would come back still filled - so drop the fill on the way out.
            IsVisibleChanged += (_, _) => { if (!IsVisible) ResetSplit(); RefreshFx(); };
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

        /// <summary>
        /// Where the seam sits, in the card's own normalised coordinates: the seam is the line
        /// x/W + y/H = SplitProgress. 1 is the resting 50/50 "/" diagonal, 2 is the line pushed
        /// out past the bottom-right corner (half A owns the whole square), 0 is it pulled past
        /// the top-left corner (half B owns it).
        ///
        /// EVERY size-dependent thing on this card - both clips, both washes, the seam, both
        /// active rings and the per-half hit test - is a function of this one number, which is
        /// what makes the hover fill a single animation and keeps the hit test honest at every
        /// frame of it. It is a DependencyProperty purely so a DoubleAnimation can drive it.
        /// </summary>
        private static readonly DependencyProperty SplitProgressProperty =
            DependencyProperty.Register(nameof(SplitProgress), typeof(double), typeof(SplitFeatureCard),
                new PropertyMetadata(SplitRest, (d, _) => ((SplitFeatureCard)d).SafeRebuildGeometry()));

        private double SplitProgress
        {
            get => (double)GetValue(SplitProgressProperty);
            set => SetValue(SplitProgressProperty, value);
        }

        /// <summary>
        /// True when the point sits on half A's side of the seam AT THE SEAM'S CURRENT POSITION -
        /// so the test follows the fill: once A has swept over the card, every point is A's.
        /// </summary>
        private bool IsInHalfA(Point p)
        {
            double w = ContentRoot.ActualWidth, h = ContentRoot.ActualHeight;
            if (w <= 0 || h <= 0) return true;
            return p.X / w + p.Y / h <= SplitProgress;
        }

        /// <summary>
        /// Which half a click belongs to: the half the mouse committed the card to (so an expanded
        /// half takes clicks anywhere on the tile, even mid-sweep), falling back to the positional
        /// test when nothing is hovered - touch and programmatic clicks never move the mouse.
        /// </summary>
        private bool ResolveHalfA(Point p) => _halfHover ?? IsInHalfA(p);

        private void OnContentSizeChanged(object sender, SizeChangedEventArgs e) => SafeRebuildGeometry();

        private void SafeRebuildGeometry()
        {
            try { RebuildGeometry(); }
            catch (Exception ex) { App.Logger?.Debug("SplitFeatureCard.RebuildGeometry: {E}", ex.Message); }
        }

        /// <summary>
        /// Rebuilds every seam-dependent geometry: the two region clips, the washes, the seam and
        /// the active-ring paths. Runs on resize AND on every frame of the hover fill, so it stays
        /// lean: frozen geometries, and the rings only when they are actually on screen.
        /// </summary>
        private void RebuildGeometry()
        {
            double w = ContentRoot.ActualWidth, h = ContentRoot.ActualHeight;
            if (w <= 0 || h <= 0) return;
            double k = SplitProgress;

            var regionA = RegionGeometry(true, k, w, h, 0);
            var regionB = RegionGeometry(false, k, w, h, 0);

            HalfHostA.Clip = regionA;
            HalfHostB.Clip = regionB;
            HoverWashA.Data = regionA;
            HoverWashB.Data = regionB;

            // Rings inset by their stroke so the outline hugs the region instead of being clipped
            // in half by ContentRoot's bounds. ApplyActiveState re-enters here after flipping the
            // Visibility, so a ring that just came on still gets its geometry.
            if (ActiveRingA.Visibility == Visibility.Visible)
                ActiveRingA.Data = RegionGeometry(true, k, w, h, RingInset);
            if (ActiveRingB.Visibility == Visibility.Visible)
                ActiveRingB.Data = RegionGeometry(false, k, w, h, RingInset);

            var (s1, s2) = SeamPoints(k, w, h);
            var seam = new LineGeometry(s1, s2);
            seam.Freeze();
            SeamLine.Data = seam;
            // A seam with nothing left to separate should not sit in the corner as a stray tick.
            SeamLine.Opacity = Math.Max(0, 1 - Math.Abs(k - SplitRest));
        }

        /// <summary>The seam's two endpoints on the card's edge for a given seam parameter.</summary>
        private static (Point A, Point B) SeamPoints(double k, double w, double h)
            => k <= SplitRest
                ? (new Point(k * w, 0), new Point(0, k * h))
                : (new Point(w, (k - SplitRest) * h), new Point((k - SplitRest) * w, h));

        /// <summary>
        /// One half's region: the card rectangle (optionally inset all round) clipped against the
        /// seam's half-plane. Written as a real polygon clip rather than a hand-built triangle
        /// because the shape passes through triangle, pentagon, square and nothing as the seam
        /// sweeps, and the inset has to hold at every one of them.
        /// </summary>
        private static PathGeometry RegionGeometry(bool halfA, double k, double w, double h, double inset)
        {
            double l = inset, t = inset, r = w - inset, b = h - inset;
            if (r <= l || b <= t) return EmptyGeometry;

            // Insetting the diagonal means moving the line along its own normal, and the seam's
            // gradient is (1/w, 1/h) - so `inset` pixels are worth that much of k.
            double seamK = k + (halfA ? -1 : 1) * inset * Math.Sqrt(1.0 / (w * w) + 1.0 / (h * h));
            double sign = halfA ? 1.0 : -1.0;

            var rect = new[] { new Point(l, t), new Point(r, t), new Point(r, b), new Point(l, b) };
            var kept = new List<Point>(6);
            for (int i = 0; i < rect.Length; i++)
            {
                Point p1 = rect[i], p2 = rect[(i + 1) % rect.Length];
                double d1 = sign * (p1.X / w + p1.Y / h - seamK);
                double d2 = sign * (p2.X / w + p2.Y / h - seamK);
                if (d1 <= 0) AddVertex(kept, p1);
                if ((d1 <= 0) != (d2 <= 0))
                {
                    double f = d1 / (d1 - d2);
                    AddVertex(kept, new Point(p1.X + (p2.X - p1.X) * f, p1.Y + (p2.Y - p1.Y) * f));
                }
            }
            // The ends of the sweep put a corner exactly on the seam, which the clip walks into
            // twice; a stroked ring with zero-length segments in it is asking for cap artefacts.
            if (kept.Count > 1 && Near(kept[0], kept[^1])) kept.RemoveAt(kept.Count - 1);
            if (kept.Count < 3) return EmptyGeometry;

            var segments = new PathSegment[kept.Count - 1];
            for (int i = 1; i < kept.Count; i++) segments[i - 1] = new LineSegment(kept[i], isStroked: true);
            var geo = new PathGeometry(new[] { new PathFigure(kept[0], segments, closed: true) });
            geo.Freeze();
            return geo;
        }

        private static void AddVertex(List<Point> poly, Point p)
        {
            if (poly.Count > 0 && Near(poly[^1], p)) return;
            poly.Add(p);
        }

        private static bool Near(Point a, Point b) => Math.Abs(a.X - b.X) < 0.01 && Math.Abs(a.Y - b.Y) < 0.01;

        private static PathGeometry CreateEmptyGeometry()
        {
            var geo = new PathGeometry();
            geo.Freeze();
            return geo;
        }

        // ============================== input ==============================

        private void OnLeftClick(object sender, MouseButtonEventArgs e)
        {
            var evt = ResolveHalfA(e.GetPosition(ContentRoot)) ? ClickAEvent : ClickBEvent;
            RaiseEvent(new RoutedEventArgs(evt, this));
        }

        private void OnRightClick(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            var evt = ResolveHalfA(e.GetPosition(ContentRoot)) ? ToggleAEvent : ToggleBEvent;
            RaiseEvent(new RoutedEventArgs(evt, this));
        }

        private void OnHalfHoverMove(object sender, MouseEventArgs e)
            => SetHalfHover(IsInHalfA(e.GetPosition(ContentRoot)));

        /// <summary>
        /// Commits the card to a half: the wash flips, the seam sweeps until that half fills the
        /// square and its title grows into the tile's only label. Null hands the card back to the
        /// 50/50 split.
        ///
        /// Idempotent on purpose - MouseMove calls this on every pixel, and restarting the sweep
        /// each time would pin it at its first frame. Note the seam always moves AWAY from the
        /// point that summoned it, so the hovered half can never flip out from under the cursor
        /// mid-sweep: once a half owns the card the only way to the other one is to leave and
        /// come back in through it, which is the owner's grammar.
        /// </summary>
        private void SetHalfHover(bool? halfA)
        {
            if (_halfHover == halfA) return;
            _halfHover = halfA;

            HoverWashA.Opacity = halfA == true ? 1 : 0;
            HoverWashB.Opacity = halfA == false ? 1 : 0;

            double target = halfA switch { true => SplitFullA, false => SplitFullB, _ => SplitRest };
            AnimateSplit(target, expanding: halfA != null);
            ApplyTitleEmphasis(halfA);
        }

        /// <summary>
        /// The sweep is interaction motion - a direct answer to the mouse - but it costs an ambient
        /// clock (a fresh set of geometries per frame), so it wants Full motion on a tier that pays
        /// for glow. Anything below that gets the state change with no sweep, per the FX plan's
        /// rule that the fallback is the static end state, not a cheaper loop.
        /// </summary>
        private static bool SweepAllowed =>
            MotionFx.AllowTransitions
            && MotionFx.AllowAmbientLoops
            && PerformanceProfile.AllowGlow(PerformanceProfile.CurrentTier);

        /// <summary>Sweeps the seam to <paramref name="target"/>, or snaps it there when the sweep
        /// is gated off.</summary>
        private void AnimateSplit(double target, bool expanding)
        {
            try
            {
                if (!SweepAllowed)
                {
                    BeginAnimation(SplitProgressProperty, null);
                    SplitProgress = target;
                    return;
                }

                // To-only, so SnapshotAndReplace hands the new sweep the current animated value and
                // a reversal picks up exactly where the outgoing one had got to.
                var anim = new DoubleAnimation(target, TimeSpan.FromMilliseconds(expanding ? SplitExpandMs : SplitCollapseMs))
                {
                    EasingFunction = new CubicEase { EasingMode = expanding ? EasingMode.EaseOut : EasingMode.EaseIn },
                };
                Timeline.SetDesiredFrameRate(anim, SplitFrameRate);
                BeginAnimation(SplitProgressProperty, anim);
            }
            catch (Exception ex) { App.Logger?.Debug("SplitFeatureCard.AnimateSplit: {E}", ex.Message); }
        }

        /// <summary>The filled half's title grows into the card; the other one gets out of the way.</summary>
        private void ApplyTitleEmphasis(bool? halfA)
        {
            try
            {
                if (TitleScaleA == null || TitleScaleB == null) return;
                bool animate = MotionFx.AllowTransitions;
                int ms = !SweepAllowed
                    ? SplitReducedMs
                    : halfA == null ? SplitCollapseMs : SplitExpandMs;

                CapGrownTitle(TxtTitleA, halfA == true);
                CapGrownTitle(TxtTitleB, halfA == false);

                EmphasiseTitle(TitlePillA, TitleScaleA, grown: halfA == true, hidden: halfA == false, animate, ms);
                EmphasiseTitle(TitlePillB, TitleScaleB, grown: halfA == false, hidden: halfA == true, animate, ms);
            }
            catch (Exception ex) { App.Logger?.Debug("SplitFeatureCard.ApplyTitleEmphasis: {E}", ex.Message); }
        }

        private static void EmphasiseTitle(UIElement pill, ScaleTransform scale, bool grown, bool hidden, bool animate, int ms)
        {
            if (scale.IsFrozen) return;
            double to = grown ? TitleExpandedScale : 1.0;
            double opacity = hidden ? 0.0 : 1.0;

            if (!animate)
            {
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                pill.BeginAnimation(OpacityProperty, null);
                scale.ScaleX = scale.ScaleY = to;
                pill.Opacity = opacity;
                return;
            }

            var ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };
            var grow = new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms)) { EasingFunction = ease };
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
            pill.BeginAnimation(OpacityProperty, new DoubleAnimation(opacity, TimeSpan.FromMilliseconds(ms)) { EasingFunction = ease });
        }

        /// <summary>
        /// A render scale is applied after measure, so a grown pill would run its longer localised
        /// titles straight under the root border's ClipToBounds with no ellipsis to admit it. Cap
        /// the TextBlock at the width the scale leaves it; clear the cap on the way back down.
        /// </summary>
        private void CapGrownTitle(TextBlock text, bool grown)
        {
            double w = ContentRoot.ActualWidth;
            text.MaxWidth = grown && w > 0
                ? Math.Max(40, (w - TitlePillChrome) / TitleExpandedScale)
                : double.PositiveInfinity;
        }

        /// <summary>
        /// Drops any in-flight fill back to the resting split, without motion. A card can be torn
        /// down or hidden (tab switch, mosaic rebuild) while a half is expanded, and MouseLeave
        /// never arrives to undo it.
        /// </summary>
        private void ResetSplit()
        {
            try
            {
                _halfHover = null;
                BeginAnimation(SplitProgressProperty, null);
                SplitProgress = SplitRest;
                HoverWashA.Opacity = 0;
                HoverWashB.Opacity = 0;
                CapGrownTitle(TxtTitleA, false);
                CapGrownTitle(TxtTitleB, false);
                EmphasiseTitle(TitlePillA, TitleScaleA, grown: false, hidden: false, animate: false, ms: 0);
                EmphasiseTitle(TitlePillB, TitleScaleB, grown: false, hidden: false, animate: false, ms: 0);
            }
            catch (Exception ex) { App.Logger?.Debug("SplitFeatureCard.ResetSplit: {E}", ex.Message); }
        }

        // ============================== FX (kept in step with FeatureCard) ==============================

        internal void RefreshFx()
        {
            try { ApplyActiveState(); }
            catch (Exception ex) { App.Logger?.Debug("SplitFeatureCard.RefreshFx: {E}", ex.Message); }
        }

        /// <summary>Visibility + window focus + motion + tier, exactly FeatureCard's gate. A
        /// Forever clock on a collapsed tab burns a composition slot with nothing on screen to
        /// show for it, and a hidden tab leaves the card Loaded - so IsVisibleChanged (wired in
        /// the ctor), not Unloaded, is what catches it.</summary>
        private bool AmbientAllowed
        {
            get
            {
                if (!IsVisible) return false;
                var w = _hostWindow;
                if (w != null && (!w.IsActive || w.WindowState == WindowState.Minimized)) return false;
                return MotionFx.AllowAmbientLoops;
            }
        }

        private void ApplyActiveState()
        {
            ActiveRingA.Visibility = IsActiveA ? Visibility.Visible : Visibility.Collapsed;
            ActiveRingB.Visibility = IsActiveB ? Visibility.Visible : Visibility.Collapsed;
            // RebuildGeometry only draws the rings that are on screen (it also runs per frame of
            // the hover fill), so a ring that just came on needs this to get its geometry.
            SafeRebuildGeometry();
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
                ResetSplit();
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
