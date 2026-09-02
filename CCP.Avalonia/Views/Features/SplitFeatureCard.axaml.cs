using System;
using System.Collections.Generic;
using System.Threading;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;

namespace ConditioningControlPanel.Avalonia.Views.Features
{
    /// <summary>
    /// A mosaic tile carrying TWO features on one diagonal ("/") split: half A is the top-left
    /// triangle, half B the bottom-right. Left-click on a half opens it (ClickA/ClickB),
    /// right-click toggles it (ToggleA/ToggleB). Hovering a half sweeps the seam across the card
    /// until that half fills the square bar a corner peek, so the other half stays clickable.
    /// Ported from the WPF head; the geometry maths is verbatim.
    ///
    /// FX plumbing is deliberately copied from <see cref="FeatureCard"/> rather than shared
    /// through a base class, as in WPF. The WPF gates (MotionFx, PerformanceProfile, window
    /// focus, tab visibility) live in the head, so this card always animates.
    /// ponytail: needs MotionFx/PerformanceProfile, gate the breath and sweep when they move to Core
    /// </summary>
    public partial class SplitFeatureCard : UserControl
    {
        private const double ActiveGlowMinOpacity = 0.50;
        private const double ActiveGlowMaxOpacity = 0.90;
        private const double ActiveRingMinOpacity = 0.55;
        private const double ActiveRingMaxOpacity = 1.00;
        private const double ActiveBreathSeconds = 3.5;
        private const double RimLightOpacity = 0.85;
        private const double HoverLiftScale = 1.02;
        private const int HoverMs = 150;

        // ---- hover fill (the seam sweep) ----
        /// <summary>Seam parameter at rest: the 50/50 "/" diagonal.</summary>
        private const double SplitRest = 1.0;
        /// <summary>How much of the card the RECEDING half keeps: the corner peek, as a fraction of each edge.</summary>
        private const double SplitPeekFraction = 0.26;
        private const double SplitFillA = 2.0 - SplitPeekFraction;
        private const double SplitFillB = SplitPeekFraction;
        private const double SeamRestOpacity = 0.60;
        private const double SeamRestThickness = 1.2;
        private const double SeamFilledThickness = 2.4;
        private const double PeekScrimOpacity = 0.20;
        private const double TitleExpandedScale = 1.35;
        private const double RingInset = 2.0;
        /// <summary>Pill margin + padding, taken off before capping the grown title's width.</summary>
        private const double TitlePillChrome = 34;
        private const int SplitExpandMs = 260;
        private const int SplitCollapseMs = 210;

        private static readonly Geometry EmptyGeometry = new PolylineGeometry(Array.Empty<Point>(), true);

        public static readonly StyledProperty<string> TitleAProperty =
            AvaloniaProperty.Register<SplitFeatureCard, string>(nameof(TitleA), "A");
        public static readonly StyledProperty<string> TitleBProperty =
            AvaloniaProperty.Register<SplitFeatureCard, string>(nameof(TitleB), "B");
        public static readonly StyledProperty<IImageBrushSource?> IconAProperty =
            AvaloniaProperty.Register<SplitFeatureCard, IImageBrushSource?>(nameof(IconA));
        public static readonly StyledProperty<IImageBrushSource?> IconBProperty =
            AvaloniaProperty.Register<SplitFeatureCard, IImageBrushSource?>(nameof(IconB));
        public static readonly StyledProperty<bool> IsActiveAProperty =
            AvaloniaProperty.Register<SplitFeatureCard, bool>(nameof(IsActiveA));
        public static readonly StyledProperty<bool> IsActiveBProperty =
            AvaloniaProperty.Register<SplitFeatureCard, bool>(nameof(IsActiveB));

        /// <summary>
        /// Where the seam sits, normalised: the seam is the line x/W + y/H = SplitProgress. 1 is
        /// the resting diagonal, SplitFillA pushes it towards the bottom-right corner, SplitFillB
        /// towards the top-left. A styled property purely so a DoubleTransition can drive it.
        /// </summary>
        private static readonly StyledProperty<double> SplitProgressProperty =
            AvaloniaProperty.Register<SplitFeatureCard, double>(nameof(SplitProgress), SplitRest);

        public static readonly RoutedEvent<RoutedEventArgs> ClickAEvent =
            RoutedEvent.Register<SplitFeatureCard, RoutedEventArgs>(nameof(ClickA), RoutingStrategies.Bubble);
        public static readonly RoutedEvent<RoutedEventArgs> ClickBEvent =
            RoutedEvent.Register<SplitFeatureCard, RoutedEventArgs>(nameof(ClickB), RoutingStrategies.Bubble);
        public static readonly RoutedEvent<RoutedEventArgs> ToggleAEvent =
            RoutedEvent.Register<SplitFeatureCard, RoutedEventArgs>(nameof(ToggleA), RoutingStrategies.Bubble);
        public static readonly RoutedEvent<RoutedEventArgs> ToggleBEvent =
            RoutedEvent.Register<SplitFeatureCard, RoutedEventArgs>(nameof(ToggleB), RoutingStrategies.Bubble);

        public string TitleA { get => GetValue(TitleAProperty); set => SetValue(TitleAProperty, value); }
        public string TitleB { get => GetValue(TitleBProperty); set => SetValue(TitleBProperty, value); }
        public IImageBrushSource? IconA { get => GetValue(IconAProperty); set => SetValue(IconAProperty, value); }
        public IImageBrushSource? IconB { get => GetValue(IconBProperty); set => SetValue(IconBProperty, value); }
        public bool IsActiveA { get => GetValue(IsActiveAProperty); set => SetValue(IsActiveAProperty, value); }
        public bool IsActiveB { get => GetValue(IsActiveBProperty); set => SetValue(IsActiveBProperty, value); }
        private double SplitProgress { get => GetValue(SplitProgressProperty); set => SetValue(SplitProgressProperty, value); }

        public event EventHandler<RoutedEventArgs> ClickA { add => AddHandler(ClickAEvent, value); remove => RemoveHandler(ClickAEvent, value); }
        public event EventHandler<RoutedEventArgs> ClickB { add => AddHandler(ClickBEvent, value); remove => RemoveHandler(ClickBEvent, value); }
        public event EventHandler<RoutedEventArgs> ToggleA { add => AddHandler(ToggleAEvent, value); remove => RemoveHandler(ToggleAEvent, value); }
        public event EventHandler<RoutedEventArgs> ToggleB { add => AddHandler(ToggleBEvent, value); remove => RemoveHandler(ToggleBEvent, value); }

        private readonly Border _rootBorder, _halfHostA, _halfHostB, _titlePillA, _titlePillB, _rimLight;
        private readonly Grid _contentRoot;
        private readonly Path _hoverWashA, _hoverWashB, _peekScrimA, _peekScrimB, _seamLine, _activeRingA, _activeRingB;
        private readonly TextBlock _txtTitleA, _txtTitleB;
        private readonly DropShadowEffect _activeGlow;
        private readonly ScaleTransform _rootScale = new(1, 1), _titleScaleA = new(1, 1), _titleScaleB = new(1, 1);
        /// <summary>Drives SplitProgress. ONE instance, mutated per sweep: replacing it mid-flight
        /// drops the animated value and the seam snaps to the old target before the new sweep.</summary>
        private readonly DoubleTransition _splitTransition = new() { Property = SplitProgressProperty };
        private CancellationTokenSource? _breath;
        private bool _hovered;
        /// <summary>Which half the pointer has committed the card to: true = A, false = B, null = neither.</summary>
        private bool? _halfHover;

        public SplitFeatureCard()
        {
            AvaloniaXamlLoader.Load(this);
            _rootBorder = this.FindControl<Border>("RootBorder")!;
            _halfHostA = this.FindControl<Border>("HalfHostA")!;
            _halfHostB = this.FindControl<Border>("HalfHostB")!;
            _titlePillA = this.FindControl<Border>("TitlePillA")!;
            _titlePillB = this.FindControl<Border>("TitlePillB")!;
            _rimLight = this.FindControl<Border>("RimLight")!;
            _contentRoot = this.FindControl<Grid>("ContentRoot")!;
            _hoverWashA = this.FindControl<Path>("HoverWashA")!;
            _hoverWashB = this.FindControl<Path>("HoverWashB")!;
            _peekScrimA = this.FindControl<Path>("PeekScrimA")!;
            _peekScrimB = this.FindControl<Path>("PeekScrimB")!;
            _seamLine = this.FindControl<Path>("SeamLine")!;
            _activeRingA = this.FindControl<Path>("ActiveRingA")!;
            _activeRingB = this.FindControl<Path>("ActiveRingB")!;
            _txtTitleA = this.FindControl<TextBlock>("TxtTitleA")!;
            _txtTitleB = this.FindControl<TextBlock>("TxtTitleB")!;
            _activeGlow = (DropShadowEffect)_rootBorder.Effect!;

            _rootScale.Transitions = ScaleTransitions(HoverMs, new QuadraticEaseOut());
            _titleScaleA.Transitions = ScaleTransitions(SplitExpandMs, new QuadraticEaseOut());
            _titleScaleB.Transitions = ScaleTransitions(SplitExpandMs, new QuadraticEaseOut());
            _rootBorder.RenderTransform = _rootScale;
            _titlePillA.RenderTransform = _titleScaleA;
            _titlePillB.RenderTransform = _titleScaleB;

            _contentRoot.SizeChanged += (_, _) => { UpdateRoundedClip(); RebuildGeometry(); };
            PointerEntered += (_, _) => ApplyHover(true);
            PointerExited += (_, _) => { ApplyHover(false); SetHalfHover(null); };
            PointerMoved += (_, e) => SetHalfHover(IsInHalfA(e.GetPosition(_contentRoot)));
            PointerReleased += OnPointerReleased;
            Unloaded += (_, _) => { ApplyActiveBreath(false); ResetSplit(); };
            Loaded += (_, _) => ApplyActiveState(); // re-arm the breath after a detach/re-attach (tab switch)
            Transitions = new Transitions { _splitTransition };
        }

        private static Transitions ScaleTransitions(int ms, Easing easing) => new()
        {
            new DoubleTransition { Property = ScaleTransform.ScaleXProperty, Duration = TimeSpan.FromMilliseconds(ms), Easing = easing },
            new DoubleTransition { Property = ScaleTransform.ScaleYProperty, Duration = TimeSpan.FromMilliseconds(ms), Easing = easing },
        };

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (_rootBorder is null) return; // fired before the XAML loaded
            if (change.Property == TitleAProperty) _txtTitleA.Text = TitleA ?? "";
            else if (change.Property == TitleBProperty) _txtTitleB.Text = TitleB ?? "";
            else if (change.Property == IconAProperty) ApplyIcon(_halfHostA, IconA);
            else if (change.Property == IconBProperty) ApplyIcon(_halfHostB, IconB);
            else if (change.Property == IsActiveAProperty || change.Property == IsActiveBProperty) ApplyActiveState();
            else if (change.Property == SplitProgressProperty) RebuildGeometry();
            else if (change.Property == IsVisibleProperty && !IsVisible) ResetSplit();
        }

        private static void ApplyIcon(Border host, IImageBrushSource? src)
        {
            host.Background = src == null
                ? null
                : new ImageBrush(src) { Stretch = Stretch.UniformToFill, AlignmentY = AlignmentY.Center };
        }

        // ============================== geometry ==============================

        /// <summary>True when the point sits on half A's side of the seam AT THE SEAM'S CURRENT
        /// POSITION, so the test follows the fill right down to the corner wedge.</summary>
        private bool IsInHalfA(Point p)
        {
            double w = _contentRoot.Bounds.Width, h = _contentRoot.Bounds.Height;
            if (w <= 0 || h <= 0) return true;
            return p.X / w + p.Y / h <= SplitProgress;
        }

        /// <summary>Rounded clip for the whole content stack: the polygon clips have square outer
        /// corners, so without this the art pokes past the rounded frame at every card corner.</summary>
        private void UpdateRoundedClip()
        {
            var b = _contentRoot.Bounds;
            _contentRoot.Clip = b.Width <= 0 || b.Height <= 0
                ? null
                : new RectangleGeometry(new Rect(0, 0, b.Width, b.Height)) { RadiusX = 11, RadiusY = 11 };
        }

        /// <summary>Rebuilds every seam-dependent geometry. Runs on resize AND on every frame of the sweep.</summary>
        private void RebuildGeometry()
        {
            double w = _contentRoot.Bounds.Width, h = _contentRoot.Bounds.Height;
            if (w <= 0 || h <= 0) return;
            double k = SplitProgress;

            var regionA = RegionGeometry(true, k, w, h, 0);
            var regionB = RegionGeometry(false, k, w, h, 0);

            _halfHostA.Clip = regionA;
            _halfHostB.Clip = regionB;
            _hoverWashA.Data = regionA;
            _hoverWashB.Data = regionB;

            // Rings inset by their stroke so the outline hugs the region instead of being clipped.
            if (_activeRingA.IsVisible) _activeRingA.Data = RegionGeometry(true, k, w, h, RingInset);
            if (_activeRingB.IsVisible) _activeRingB.Data = RegionGeometry(false, k, w, h, RingInset);

            var (s1, s2) = SeamPoints(k, w, h);
            _seamLine.Data = new LineGeometry(s1, s2);

            ApplyPeekChrome(k, regionA, regionB);
        }

        /// <summary>The corner-peek chrome, a function of how far the fill has run: 0 at rest, 1 filled.
        /// The scrim goes on the RECEDING half; the seam brightens and thickens for both.</summary>
        private void ApplyPeekChrome(double k, Geometry regionA, Geometry regionB)
        {
            double fill = Math.Min(1.0, Math.Abs(k - SplitRest) / (SplitRest - SplitPeekFraction));
            bool aFilling = k > SplitRest;

            _peekScrimA.Data = regionA;
            _peekScrimB.Data = regionB;
            _peekScrimA.Opacity = aFilling ? 0 : fill * PeekScrimOpacity;
            _peekScrimB.Opacity = aFilling ? fill * PeekScrimOpacity : 0;

            _seamLine.Opacity = SeamRestOpacity + (1 - SeamRestOpacity) * fill;
            _seamLine.StrokeThickness = SeamRestThickness + (SeamFilledThickness - SeamRestThickness) * fill;
        }

        /// <summary>The seam's two endpoints on the card's edge for a given seam parameter.</summary>
        private static (Point A, Point B) SeamPoints(double k, double w, double h)
            => k <= SplitRest
                ? (new Point(k * w, 0), new Point(0, k * h))
                : (new Point(w, (k - SplitRest) * h), new Point((k - SplitRest) * w, h));

        /// <summary>One half's region: the card rectangle (optionally inset) clipped against the
        /// seam's half-plane. A real polygon clip, because the shape passes through triangle,
        /// pentagon, square and nothing as the seam sweeps.</summary>
        private static Geometry RegionGeometry(bool halfA, double k, double w, double h, double inset)
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
            // The ends of the sweep put a corner exactly on the seam, which the clip walks into twice.
            if (kept.Count > 1 && Near(kept[0], kept[^1])) kept.RemoveAt(kept.Count - 1);
            if (kept.Count < 3) return EmptyGeometry;

            return new PolylineGeometry(kept, true);
        }

        private static void AddVertex(List<Point> poly, Point p)
        {
            if (poly.Count > 0 && Near(poly[^1], p)) return;
            poly.Add(p);
        }

        private static bool Near(Point a, Point b) => Math.Abs(a.X - b.X) < 0.01 && Math.Abs(a.Y - b.Y) < 0.01;

        // ============================== input ==============================

        private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            // Purely positional, against the seam where it is DRAWN at that instant - the corner
            // wedge is on screen precisely so it can be clicked.
            bool halfA = IsInHalfA(e.GetPosition(_contentRoot));
            if (e.InitialPressMouseButton == MouseButton.Left)
            {
                RaiseEvent(new RoutedEventArgs(halfA ? ClickAEvent : ClickBEvent, this));
            }
            else if (e.InitialPressMouseButton == MouseButton.Right)
            {
                e.Handled = true;
                RaiseEvent(new RoutedEventArgs(halfA ? ToggleAEvent : ToggleBEvent, this));
            }
        }

        /// <summary>Commits the card to a half: the wash flips, the seam sweeps, its title grows.
        /// Null hands the card back to the 50/50 split. Idempotent: PointerMoved calls this on every pixel.</summary>
        private void SetHalfHover(bool? halfA)
        {
            if (_halfHover == halfA) return;
            _halfHover = halfA;

            _hoverWashA.Opacity = halfA == true ? 1 : 0;
            _hoverWashB.Opacity = halfA == false ? 1 : 0;

            double target = halfA switch { true => SplitFillA, false => SplitFillB, _ => SplitRest };
            bool expanding = halfA != null;
            // WPF: a to-only DoubleAnimation; here the one transition, retuned so expand and
            // collapse keep their own durations and easings.
            _splitTransition.Duration = TimeSpan.FromMilliseconds(expanding ? SplitExpandMs : SplitCollapseMs);
            _splitTransition.Easing = expanding ? new CubicEaseOut() : new CubicEaseIn();
            SplitProgress = target;
            ApplyTitleEmphasis(halfA);
        }

        /// <summary>The filled half's title grows into the card; the other one gets out of the way.</summary>
        private void ApplyTitleEmphasis(bool? halfA)
        {
            CapGrownTitle(_txtTitleA, halfA == true);
            CapGrownTitle(_txtTitleB, halfA == false);
            EmphasiseTitle(_titlePillA, _titleScaleA, grown: halfA == true, hidden: halfA == false);
            EmphasiseTitle(_titlePillB, _titleScaleB, grown: halfA == false, hidden: halfA == true);
        }

        private static void EmphasiseTitle(Border pill, ScaleTransform scale, bool grown, bool hidden)
        {
            scale.ScaleX = scale.ScaleY = grown ? TitleExpandedScale : 1.0;
            pill.Opacity = hidden ? 0.0 : 1.0;
        }

        /// <summary>A render scale is applied after measure, so a grown pill would run its longer
        /// localised titles under ClipToBounds with no ellipsis. Cap the width the scale leaves it.</summary>
        private void CapGrownTitle(TextBlock text, bool grown)
        {
            double w = _contentRoot.Bounds.Width;
            text.MaxWidth = grown && w > 0
                ? Math.Max(40, (w - TitlePillChrome) / TitleExpandedScale)
                : double.PositiveInfinity;
        }

        /// <summary>Drops any in-flight fill back to the resting split, without motion.</summary>
        private void ResetSplit()
        {
            _halfHover = null;
            Transitions = null; // no motion on the way back
            SplitProgress = SplitRest;
            Transitions = new Transitions { _splitTransition };
            // Assigning a value it already holds raises no change, so rebuild explicitly.
            RebuildGeometry();
            _hoverWashA.Opacity = 0;
            _hoverWashB.Opacity = 0;
            CapGrownTitle(_txtTitleA, false);
            CapGrownTitle(_txtTitleB, false);
            EmphasiseTitle(_titlePillA, _titleScaleA, grown: false, hidden: false);
            EmphasiseTitle(_titlePillB, _titleScaleB, grown: false, hidden: false);
        }

        // ============================== FX (kept in step with FeatureCard) ==============================

        private void ApplyActiveState()
        {
            _activeRingA.IsVisible = IsActiveA;
            _activeRingB.IsVisible = IsActiveB;
            // RebuildGeometry only draws the rings that are on screen, so a ring that just came on
            // needs this to get its geometry.
            RebuildGeometry();
            ApplyActiveBreath(IsActiveA || IsActiveB);
        }

        /// <summary>The card-level glow breathes when EITHER half is on (the drop shadow cannot be
        /// halved); the per-half rings breathe with it.</summary>
        private void ApplyActiveBreath(bool active)
        {
            _breath?.Cancel();
            _breath = null;
            _activeRingA.Opacity = 1;
            _activeRingB.Opacity = 1;
            if (!active) { _activeGlow.Opacity = 0; return; }

            _breath = new CancellationTokenSource();
            _ = Breathe(DropShadowEffect.OpacityProperty, ActiveGlowMinOpacity, ActiveGlowMaxOpacity).RunAsync(_activeGlow, _breath.Token);
            if (IsActiveA) _ = Breathe(OpacityProperty, ActiveRingMinOpacity, ActiveRingMaxOpacity).RunAsync(_activeRingA, _breath.Token);
            if (IsActiveB) _ = Breathe(OpacityProperty, ActiveRingMinOpacity, ActiveRingMaxOpacity).RunAsync(_activeRingB, _breath.Token);
        }

        private static Animation Breathe(AvaloniaProperty prop, double min, double max) => new()
        {
            Duration = TimeSpan.FromSeconds(ActiveBreathSeconds),
            IterationCount = IterationCount.Infinite,
            PlaybackDirection = PlaybackDirection.Alternate,
            Easing = new SineEaseInOut(),
            Children =
            {
                new KeyFrame { Cue = new Cue(0d), Setters = { new Setter(prop, min) } },
                new KeyFrame { Cue = new Cue(1d), Setters = { new Setter(prop, max) } },
            },
        };

        private void ApplyHover(bool on)
        {
            if (_hovered == on) return;
            _hovered = on;
            _rootScale.ScaleX = _rootScale.ScaleY = on ? HoverLiftScale : 1;
            _rimLight.Opacity = on ? RimLightOpacity : 0;
        }
    }
}
