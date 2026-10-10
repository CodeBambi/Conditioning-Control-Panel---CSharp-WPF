using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Controls.Header
{
    /// <summary>
    /// The header Premium spark: a cut-and-folded cardstock four-point sparkle with PREMIUM cut out
    /// under it. Free reads dim and still, Basic is gold with a sheen, spilling glitter, orbiting
    /// twinkles and glints, Prime is ice cyan with a pulsing halo, a dense diamond field, tip
    /// stars and the odd flare. Rules and numbers live in <see cref="PremiumSparkRules"/>, the
    /// particles in <see cref="PremiumSparkField"/>; this class only draws them.
    ///
    /// <para><b>Cheap by construction.</b> ONE 30 fps DispatcherTimer drives everything that moves
    /// on its own (sheen, breath, halo, particles); the particles draw into a fixed pool of small
    /// shapes in an unclipped, non-hit-testable overlay that takes no layout space, so motes can
    /// spill past the 54 x 42 card without growing the header. The clock parks while the spark is
    /// not visible or its window is minimised, and never starts for Free or at motion Off.</para>
    /// </summary>
    public partial class PremiumSpark : Button
    {
        private const double StarLeft = (PremiumSparkRules.ControlWidth - PremiumSparkRules.StarSize) / 2;
        private const double StarTop = 0;
        private const double LabelTop = 34;

        private static readonly Geometry StarGeometry = MakeFrozen(Geometry.Parse(PremiumSparkRules.StarPathData));
        private static readonly Brush GrainBrush = MakeGrain();

        // Particle shapes, unit sized and centred on the origin (a mote's size scales them).
        private static readonly Geometry UnitDiamond = MakeFrozen(Geometry.Parse("M0,-1 L0.55,0 L0,1 L-0.55,0 Z"));
        private static readonly Geometry UnitFleck = MakeFrozen(Geometry.Parse("M-1,-0.7 L1,-0.7 L1,0.7 L-1,0.7 Z"));
        private static readonly Geometry UnitDot = MakeFrozen(new EllipseGeometry(new Point(0, 0), 1, 1));
        private static readonly Geometry UnitStar = MakeUnitStar();

        private Random _rng = new();
        private PremiumSparkField _field;
        private readonly Path _shadowStar = new();
        private readonly Path _dieCutStar = new();
        private readonly Path _faceStar = new();
        private readonly Image _folds = new();
        private readonly Path _grainStar = new();
        private readonly Path _highlightStar = new();
        private readonly Canvas _sheenHost = new();
        private readonly Rectangle _sheen = new();
        private readonly TranslateTransform _sheenShift = new();
        private readonly Path _shadowLabel = new();
        private readonly Path _dieCutLabel = new();
        private readonly Path _faceLabel = new();
        private readonly TranslateTransform _starShadowShift = new();
        private readonly TranslateTransform _labelShadowShift = new();
        private readonly ScaleTransform _haloScale = new(1, 1);

        private readonly Path[] _pool;
        private readonly MoteKind[] _poolKind;
        private readonly int[] _poolColour;
        private Brush[] _moteBrushes = Array.Empty<Brush>();
        private Brush _flareBrush = Brushes.White;

        private FrameClock? _clock;
        private readonly System.Diagnostics.Stopwatch _clockWatch = new();
        private double _clockLast;
        private double _t;
        private Window? _window;
        private bool _loopsRunning;
        private bool _hovered;

        /// <summary>What the spark is showing right now (tests read it).</summary>
        internal SparkTier Tier { get; private set; } = SparkTier.Free;
        internal SparkMotion Motion { get; private set; } = SparkMotion.Off;
        internal bool ParticlesAllowed { get; private set; }

        /// <summary>True while the ambient clock is running (tests and parking read it).</summary>
        internal bool LoopsRunning => _loopsRunning;

        /// <summary>The particle field (tests read its counts).</summary>
        internal PremiumSparkField Field => _field;

        /// <summary>When set, Refresh() uses these instead of App's gates (offscreen renders).</summary>
        internal bool Pinned { get; set; }

        public PremiumSpark()
        {
            InitializeComponent();
            _field = new PremiumSparkField(_rng);
            _pool = new Path[PremiumSparkField.PoolSize];
            _poolKind = new MoteKind[_pool.Length];
            _poolColour = new int[_pool.Length];
            BuildCutout();
            BuildPool();
            Halo.RenderTransformOrigin = new Point(0.5, 0.5);
            Halo.RenderTransform = _haloScale;
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
            IsVisibleChanged += (_, _) => UpdateParking();
            Apply(SparkTier.Free, SparkMotion.Off, false);
        }

        /// <summary>Repeatable particles for offscreen frame strips.</summary>
        internal void Reseed(int seed)
        {
            _rng = new Random(seed);
            _field = new PremiumSparkField(_rng);
            _field.Configure(Tier, Motion, ParticlesAllowed);
            _t = 0;
        }

        // =======================================================================================
        //  state
        // =======================================================================================

        /// <summary>Re-reads the tier and the motion level from the app and repaints. Safe to
        /// call from any tier or motion change; it is idempotent.</summary>
        public void Refresh()
        {
            if (Pinned) return;
            var patreon = App.Patreon;
            var tier = PremiumSparkRules.TierFrom(patreon?.HasPremiumAccess == true, patreon?.HasLabAccess == true);
            var level = MotionFx.Level;
            var perf = PerformanceProfile.CurrentTier;
            var motion = PremiumSparkRules.MotionFrom(level, PerformanceProfile.AllowAmbientMotion(perf));
            if (Services.Diagnostics.FxBisect.Off("spark")) motion = SparkMotion.Off;
            Apply(tier, motion, MotionFx.AllowParticles);
        }

        /// <summary>Paints one state. Internal so offscreen renders can pin a look.</summary>
        internal void Apply(SparkTier tier, SparkMotion motion, bool particlesAllowed)
        {
            bool changed = tier != Tier || motion != Motion || particlesAllowed != ParticlesAllowed;
            Tier = tier;
            Motion = motion;
            ParticlesAllowed = particlesAllowed;
            _field.Configure(tier, motion, particlesAllowed);

            Paint();
            ToolTip = Loc.Get(PremiumSparkRules.TooltipKey(tier));
            System.Windows.Automation.AutomationProperties.SetName(this, Loc.Get(PremiumSparkRules.TooltipKey(tier)));

            if (changed || !_loopsRunning) RestartLoops();
        }

        private void Paint()
        {
            var livery = PremiumSparkRules.LiveryFor(Tier);
            Root.Opacity = PremiumSparkRules.Opacity(Tier);

            var face = new LinearGradientBrush
            {
                StartPoint = new Point(0.2, 0),
                EndPoint = new Point(0.8, 1),
                GradientStops =
                {
                    new GradientStop(C(livery.FaceTop), 0.0),
                    new GradientStop(C(livery.FaceMid), 0.48),
                    new GradientStop(C(livery.FaceBottom), 1.0),
                },
            };
            face.Freeze();
            _faceStar.Fill = face;

            var edge = Frozen(C(livery.Edge));
            _dieCutStar.Fill = edge;
            _dieCutStar.Stroke = edge;
            _dieCutLabel.Fill = edge;
            _dieCutLabel.Stroke = edge;

            // The paper shadow: ink pulled toward the tier colour, the DepthRules recipe.
            var shadow = Frozen(Depth.DepthRules.ShadowColor(C(livery.FaceBottom)));
            _shadowStar.Fill = shadow;
            _shadowStar.Stroke = shadow;
            _shadowLabel.Fill = shadow;
            _shadowLabel.Stroke = shadow;

            var ink = new LinearGradientBrush(C(livery.FaceMid), C(livery.FaceBottom), 90);
            ink.Freeze();
            _faceLabel.Fill = ink;

            _folds.Source = BuildFolds(livery);

            // Particle inks: the tier's mote colour, white, and a deeper tone (Prime: the cyan ink).
            _moteBrushes = Tier == SparkTier.Basic
                ? new Brush[] { Frozen(Color.FromRgb(0xFF, 0xD2, 0x4A)), Brushes.White, Frozen(Color.FromRgb(0xFF, 0xB0, 0x22)) }   // glitter gold, rich enough to read at a few px
                : new Brush[] { Frozen(C(livery.Mote)), Brushes.White, Frozen(C(livery.Ink)) };
            var flare = new RadialGradientBrush
            {
                GradientStops =
                {
                    new GradientStop(Colors.White, 0.0),
                    new GradientStop(Colors.White, 0.35),
                    new GradientStop(C(livery.Ink), 1.0),
                },
            };
            flare.Freeze();
            _flareBrush = flare;
            Array.Fill(_poolColour, -1);

            // Prime's halo: drawn at every motion level, pulsing only on the clock.
            if (PremiumSparkRules.Halo(Tier))
            {
                var halo = new RadialGradientBrush
                {
                    GradientStops =
                    {
                        new GradientStop(Color.FromArgb(0xA0, 0x7F, 0xE9, 0xFF), 0.0),
                        new GradientStop(Color.FromArgb(0x55, 0x5E, 0xE6, 0xFF), 0.45),
                        new GradientStop(Color.FromArgb(0x00, 0x5E, 0xE6, 0xFF), 1.0),
                    },
                };
                halo.Freeze();
                Halo.Fill = halo;
            }
            Halo.Opacity = PremiumSparkRules.HaloAt(_t, Tier, SparkMotion.Off, 0);
            _haloScale.ScaleX = _haloScale.ScaleY = 1;

            _sheen.Visibility = PremiumSparkRules.Sheen(Tier, Motion) ? Visibility.Visible : Visibility.Hidden;
            BuildLabel();
            SettleDepth(animate: false);
        }

        // =======================================================================================
        //  the cutout
        // =======================================================================================

        private void BuildCutout()
        {
            TiltRotate.Angle = PremiumSparkRules.Tilt;

            foreach (var p in new[] { _shadowStar, _dieCutStar, _faceStar, _grainStar, _highlightStar })
            {
                p.Data = StarGeometry;
                p.IsHitTestVisible = false;
                Canvas.SetLeft(p, StarLeft);
                Canvas.SetTop(p, StarTop);
            }

            _shadowStar.StrokeThickness = PremiumSparkRules.DieCutPx;
            _shadowStar.StrokeLineJoin = PenLineJoin.Round;
            _shadowStar.RenderTransform = _starShadowShift;

            _dieCutStar.StrokeThickness = PremiumSparkRules.DieCutPx;
            _dieCutStar.StrokeLineJoin = PenLineJoin.Round;

            // A crisp inner rim on the lit edges: the face's own stroke, white top-left fading out
            // by the middle. Its outer half lands on the white die-cut, so only the inner half shows.
            var rim = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 1),
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(0xE0, 0xFF, 0xFF, 0xFF), 0.0),
                    new GradientStop(Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 0.52),
                },
            };
            rim.Freeze();
            _faceStar.Stroke = rim;
            _faceStar.StrokeThickness = 1.3;
            _faceStar.StrokeLineJoin = PenLineJoin.Round;

            // The folds: eight facets lit from the lamp, ridges and valleys, one image.
            _folds.Width = PremiumSparkRules.StarSize;
            _folds.Height = PremiumSparkRules.StarSize;
            _folds.Stretch = Stretch.None;
            _folds.IsHitTestVisible = false;
            Canvas.SetLeft(_folds, StarLeft);
            Canvas.SetTop(_folds, StarTop);

            _grainStar.Fill = GrainBrush;
            _grainStar.Opacity = 0.16;

            // The lamp is top-left: a soft wash over the upper-left of the card, lighter than
            // round 1 now that the folds carry most of the light.
            var highlight = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 1),
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(0x50, 0xFF, 0xFF, 0xFF), 0.0),
                    new GradientStop(Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 0.5),
                },
            };
            highlight.Freeze();
            _highlightStar.Fill = highlight;

            // The sheen: a skewed light band, clipped to the star, slid across by the clock.
            _sheenHost.Width = PremiumSparkRules.StarSize;
            _sheenHost.Height = PremiumSparkRules.StarSize;
            _sheenHost.Clip = StarGeometry;
            _sheenHost.IsHitTestVisible = false;
            Canvas.SetLeft(_sheenHost, StarLeft);
            Canvas.SetTop(_sheenHost, StarTop);
            _sheen.Width = 11;
            _sheen.Height = PremiumSparkRules.StarSize + 12;
            Canvas.SetTop(_sheen, -6);
            var band = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 0),
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 0.0),
                    new GradientStop(Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF), 0.5),
                    new GradientStop(Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 1.0),
                },
            };
            band.Freeze();
            _sheen.Fill = band;
            _sheenShift.X = -20;
            _sheen.RenderTransform = new TransformGroup
            {
                Children = { new SkewTransform(-22, 0), _sheenShift },
            };
            _sheenHost.Children.Add(_sheen);

            foreach (var p in new[] { _shadowLabel, _dieCutLabel, _faceLabel })
            {
                p.IsHitTestVisible = false;
                Canvas.SetTop(p, LabelTop);
            }
            _shadowLabel.StrokeThickness = PremiumSparkRules.LabelDieCutPx;
            _shadowLabel.StrokeLineJoin = PenLineJoin.Round;
            _shadowLabel.RenderTransform = _labelShadowShift;
            _dieCutLabel.StrokeThickness = PremiumSparkRules.LabelDieCutPx;
            _dieCutLabel.StrokeLineJoin = PenLineJoin.Round;

            Cutout.Children.Add(_shadowLabel);
            Cutout.Children.Add(_dieCutLabel);
            Cutout.Children.Add(_faceLabel);
            Cutout.Children.Add(_shadowStar);
            Cutout.Children.Add(_dieCutStar);
            Cutout.Children.Add(_faceStar);
            Cutout.Children.Add(_folds);
            Cutout.Children.Add(_grainStar);
            Cutout.Children.Add(_highlightStar);
            Cutout.Children.Add(_sheenHost);
        }

        /// <summary>The folded cardstock: the star cut into eight facets that meet at a raised
        /// heart, each shaded by how it faces the top-left lamp, the mountain folds (heart to tip)
        /// drawn as fine light creases, the valley folds as fine dark ones, and a small specular
        /// catch by the heart. One frozen image per tier.</summary>
        private static ImageSource BuildFolds(PremiumSparkRules.Livery livery)
        {
            var dark = Depth.DepthRules.ShadowColor(C(livery.FaceBottom));
            var group = new DrawingGroup();
            using (var dc = group.Open())
            {
                // Pin the drawing's bounds to the star box so the Image never shifts it.
                dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, PremiumSparkRules.StarSize, PremiumSparkRules.StarSize));
                dc.PushClip(StarGeometry);

                var heart = P(PremiumSparkRules.Heart);
                var valleys = new Point[4];
                for (int k = 0; k < 4; k++)
                {
                    var flank = PremiumSparkRules.Flanks[k];
                    var (h1, h2) = PremiumSparkRules.Split(flank, 0.5);
                    valleys[k] = P(h1.Item4);
                    DrawFacet(dc, heart, h1, PremiumSparkRules.FacetShade(flank.p0, h1.Item4), dark);
                    DrawFacet(dc, heart, h2, PremiumSparkRules.FacetShade(h1.Item4, flank.p3), dark);
                }

                var crease = new Pen(Frozen(Color.FromArgb(0x9A, 0xFF, 0xFF, 0xFF)), 0.55)
                    { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
                var valley = new Pen(Frozen(Color.FromArgb(0x55, dark.R, dark.G, dark.B)), 0.5)
                    { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
                crease.Freeze();
                valley.Freeze();
                for (int k = 0; k < 4; k++)
                {
                    dc.DrawLine(valley, heart, valleys[k]);
                    dc.DrawLine(crease, heart, P(PremiumSparkRules.Flanks[k].p0));
                }

                var spec = new RadialGradientBrush
                {
                    GradientStops =
                    {
                        new GradientStop(Color.FromArgb(0xC8, 0xFF, 0xFF, 0xFF), 0.0),
                        new GradientStop(Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 1.0),
                    },
                };
                spec.Freeze();
                dc.DrawEllipse(spec, null, new Point(heart.X - 1.9, heart.Y - 2.6), 2.4, 2.4);
                dc.Pop();
            }
            group.Freeze();
            var image = new DrawingImage(group);
            image.Freeze();
            return image;
        }

        private static void DrawFacet(DrawingContext dc, Point heart,
            (SparkPoint p0, SparkPoint c1, SparkPoint c2, SparkPoint p3) half, double shade, Color dark)
        {
            var figure = new PathFigure { StartPoint = heart, IsClosed = true, IsFilled = true };
            figure.Segments.Add(new LineSegment(P(half.p0), false));
            figure.Segments.Add(new BezierSegment(P(half.c1), P(half.c2), P(half.p3), false));
            var geometry = new PathGeometry(new[] { figure });
            geometry.Freeze();
            // Lit facets catch white, turned-away ones take the tier-tinted shadow ink.
            var colour = shade >= 0
                ? Color.FromArgb((byte)Math.Round(Math.Min(1, shade) * 0x78), 0xFF, 0xFF, 0xFF)
                : Color.FromArgb((byte)Math.Round(Math.Min(1, -shade) * 0x68), dark.R, dark.G, dark.B);
            dc.DrawGeometry(Frozen(colour), null, geometry);
        }

        /// <summary>The label as one geometry, letter-spaced by hand (WPF text has no tracking),
        /// scaled down if a long translation would outgrow the card.</summary>
        private void BuildLabel()
        {
            var text = Loc.Get("premium_spark_label");
            if (string.IsNullOrWhiteSpace(text) || text == "premium_spark_label") text = "PREMIUM";
            var geometry = BuildTrackedText(text.ToUpper(CultureInfo.CurrentCulture),
                PremiumSparkRules.LabelSize, PremiumSparkRules.LabelTracking);
            var bounds = geometry.Bounds;
            if (bounds.IsEmpty) return;

            var maxWidth = PremiumSparkRules.ControlWidth - 4;
            var scale = bounds.Width > maxWidth ? maxWidth / bounds.Width : 1.0;
            var place = new TransformGroup
            {
                Children =
                {
                    new TranslateTransform(-bounds.X, -bounds.Y),
                    new ScaleTransform(scale, scale),
                    new TranslateTransform((PremiumSparkRules.ControlWidth - bounds.Width * scale) / 2, 0),
                },
            };
            var placed = geometry.Clone();
            placed.Transform = place;
            placed.Freeze();

            _shadowLabel.Data = placed;
            _dieCutLabel.Data = placed;
            _faceLabel.Data = placed;
        }

        internal static Geometry BuildTrackedText(string text, double size, double tracking)
        {
            var typeface = new Typeface(new FontFamily("Segoe UI Black, Segoe UI"),
                FontStyles.Normal, FontWeights.Black, FontStretches.Normal);
            var group = new GeometryGroup { FillRule = FillRule.Nonzero };
            double x = 0;
            foreach (var ch in text)
            {
                var ft = new FormattedText(ch.ToString(), CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                    typeface, size, Brushes.Black, 1.0);
                group.Children.Add(ft.BuildGeometry(new Point(x, 0)));
                x += ft.WidthIncludingTrailingWhitespace + tracking;
            }
            group.Freeze();
            return group;
        }

        // =======================================================================================
        //  depth: hover, press, release
        // =======================================================================================

        protected override void OnMouseEnter(System.Windows.Input.MouseEventArgs e)
        {
            base.OnMouseEnter(e);
            _hovered = true;
            SettleDepth(animate: true);
            if (PremiumSparkRules.Wobble(Tier, Motion)) Wobble();
        }

        protected override void OnMouseLeave(System.Windows.Input.MouseEventArgs e)
        {
            base.OnMouseLeave(e);
            _hovered = false;
            SettleDepth(animate: true);
        }

        protected override void OnIsPressedChanged(DependencyPropertyChangedEventArgs e)
        {
            base.OnIsPressedChanged(e);
            SettleDepth(animate: true);
        }

        protected override void OnClick()
        {
            Burst();
            base.OnClick();
        }

        private void SettleDepth(bool animate)
        {
            var travel = PremiumSparkRules.Travel(IsPressed, _hovered);
            var shadow = PremiumSparkRules.ShadowLength(IsPressed, _hovered);
            int ms = IsPressed ? Depth.DepthRules.PressMs : Depth.DepthRules.HoverMs;
            Slide(LiftShift, TranslateTransform.YProperty, travel, animate, ms);
            Slide(_starShadowShift, TranslateTransform.XProperty, shadow * 0.45, animate, ms);
            Slide(_starShadowShift, TranslateTransform.YProperty, shadow * 0.8, animate, ms);
            Slide(_labelShadowShift, TranslateTransform.XProperty, shadow * 0.3, animate, ms);
            Slide(_labelShadowShift, TranslateTransform.YProperty, shadow * 0.5, animate, ms);
        }

        private void Slide(Animatable target, DependencyProperty dp, double to, bool animate, int ms)
        {
            if (!animate || Motion == SparkMotion.Off || !MotionFx.AllowTransitions)
            {
                target.BeginAnimation(dp, null);
                target.SetValue(dp, to);
                return;
            }
            var anim = new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms))
            {
                EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.35 },
            };
            Timeline.SetDesiredFrameRate(anim, 60);
            target.BeginAnimation(dp, anim);
        }

        private void Wobble()
        {
            var tilt = PremiumSparkRules.Tilt;
            var w = PremiumSparkRules.WobbleDegrees;
            var anim = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(PremiumSparkRules.WobbleMs) };
            anim.KeyFrames.Add(new EasingDoubleKeyFrame(tilt + w, KeyTime.FromPercent(0.22)));
            anim.KeyFrames.Add(new EasingDoubleKeyFrame(tilt - w * 0.6, KeyTime.FromPercent(0.5)));
            anim.KeyFrames.Add(new EasingDoubleKeyFrame(tilt + w * 0.25, KeyTime.FromPercent(0.75)));
            anim.KeyFrames.Add(new EasingDoubleKeyFrame(tilt, KeyTime.FromPercent(1.0)));
            Timeline.SetDesiredFrameRate(anim, 60);
            TiltRotate.BeginAnimation(RotateTransform.AngleProperty, anim);
        }

        // =======================================================================================
        //  the clock: sheen, breath, halo and particles on one 30 fps timer
        // =======================================================================================

        private bool Parked
        {
            get
            {
                if (!IsVisible) return true;
                var win = _window ?? Window.GetWindow(this);
                return win != null && win.WindowState == WindowState.Minimized;
            }
        }

        private void UpdateParking()
        {
            if (Parked) StopLoops();
            else if (!_loopsRunning) StartLoops();
        }

        private void RestartLoops()
        {
            StopLoops();
            if (!Parked) StartLoops();
        }

        private void StartLoops()
        {
            StopLoops();
            if (!PremiumSparkRules.Clock(Tier, Motion)) return;

            // Start mid-stream: a field that has been running for a second, never a bare card.
            _t = _rng.NextDouble() * 1.2;
            Prewarm(1.2);

            _clock = new FrameClock
            {
                Interval = TimeSpan.FromMilliseconds(1000.0 / PremiumSparkRules.FrameRate),
            };
            _clock.Tick += OnClockTick;
            _clockWatch.Restart();
            _clockLast = 0;
            _clock.Start();
            _loopsRunning = true;
        }

        private void StopLoops()
        {
            if (_clock != null)
            {
                _clock.Stop();
                _clock.Tick -= OnClockTick;
                _clock = null;
            }
            _clockWatch.Reset();
            _field.Clear();
            _sheenShift.X = -20;
            BreathScale.ScaleX = BreathScale.ScaleY = 1.0;
            Halo.Opacity = PremiumSparkRules.HaloAt(0, Tier, SparkMotion.Off, 0);
            _haloScale.ScaleX = _haloScale.ScaleY = 1;
            DrawMotes();
            _loopsRunning = false;
        }

        private void OnClockTick(object? sender, EventArgs e)
        {
            var now = _clockWatch.Elapsed.TotalSeconds;
            var dt = now - _clockLast;
            _clockLast = now;
            Advance(dt);
        }

        /// <summary>Steps the field without drawing (so the first frame already has particles).</summary>
        internal void Prewarm(double seconds)
        {
            const double step = 1.0 / PremiumSparkRules.FrameRate;
            for (double s = 0; s < seconds; s += step) _field.Step(step);
        }

        /// <summary>Moves everything by dt seconds and draws it. The clock calls it; offscreen
        /// frame strips call it by hand.</summary>
        internal void Advance(double dt)
        {
            if (dt < 0) dt = 0;
            _t += dt;
            _field.Step(dt);

            _sheenShift.X = PremiumSparkRules.SheenAt(_t, Tier, Motion);
            var breath = PremiumSparkRules.BreathAt(_t, Tier, Motion);
            BreathScale.ScaleX = BreathScale.ScaleY = breath;
            Halo.Opacity = PremiumSparkRules.HaloAt(_t, Tier, Motion, _field.FlareGlow);
            var swell = 1 + 0.14 * _field.FlareGlow;
            _haloScale.ScaleX = _haloScale.ScaleY = swell;
            DrawMotes();
        }

        // =======================================================================================
        //  particles: a fixed pool of small shapes, filled from the field every frame
        // =======================================================================================

        private void BuildPool()
        {
            for (int i = 0; i < _pool.Length; i++)
            {
                var p = new Path
                {
                    IsHitTestVisible = false,
                    Visibility = Visibility.Hidden,
                    RenderTransform = new MatrixTransform(),
                };
                _pool[i] = p;
                _poolKind[i] = (MoteKind)(-1);
                _poolColour[i] = -1;
                FxLayer.Children.Add(p);
            }
        }

        private void DrawMotes()
        {
            var alive = _field.Alive;
            for (int i = 0; i < _pool.Length; i++)
            {
                var p = _pool[i];
                if (i >= alive.Count)
                {
                    if (p.Visibility != Visibility.Hidden) p.Visibility = Visibility.Hidden;
                    continue;
                }

                var m = alive[i];
                if (_poolKind[i] != m.Kind)
                {
                    _poolKind[i] = m.Kind;
                    p.Data = GeometryFor(m.Kind);
                    _poolColour[i] = -1;
                }
                var colour = m.Kind == MoteKind.Flare ? 99 : m.Colour;
                if (_poolColour[i] != colour)
                {
                    _poolColour[i] = colour;
                    p.Fill = colour == 99 ? _flareBrush : _moteBrushes.Length > m.Colour ? _moteBrushes[m.Colour] : Brushes.White;
                }

                var half = m.Size / 2 * PremiumSparkField.ScaleOf(m);
                var sy = half;
                if (m.Kind == MoteKind.Glitter || (m.Kind == MoteKind.Burst && Tier == SparkTier.Basic))
                    sy = half * (0.25 + 0.75 * Math.Abs(Math.Cos(m.Phase + m.Age * 9)));   // a fleck flipping in the light
                var matrix = Matrix.Identity;
                matrix.Scale(half, sy);
                matrix.Rotate(m.Angle);
                matrix.Translate(m.X, m.Y);
                ((MatrixTransform)p.RenderTransform).Matrix = matrix;
                p.Opacity = PremiumSparkField.OpacityOf(m);
                if (p.Visibility != Visibility.Visible) p.Visibility = Visibility.Visible;
            }
        }

        private Geometry GeometryFor(MoteKind kind) => kind switch
        {
            MoteKind.Diamond => UnitDiamond,
            MoteKind.Glitter => UnitDiamond,
            MoteKind.Burst => Tier == SparkTier.Prime ? UnitDiamond : UnitFleck,
            _ => UnitStar,   // orbits, glints and flares are little four-point stars
        };

        /// <summary>The click burst: diamonds (Prime) or gold flecks (Basic) thrown out radially.</summary>
        private void Burst()
        {
            var count = PremiumSparkRules.BurstCount(Tier, Motion);
            if (count <= 0 || !MotionFx.AllowTransitions) return;
            _field.Burst(count);
            if (!_loopsRunning) DrawMotes();
        }

        // =======================================================================================
        //  lifecycle
        // =======================================================================================

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            _window = Window.GetWindow(this);
            if (_window != null) _window.StateChanged += OnWindowStateChanged;
            try
            {
                if (App.Patreon != null) App.Patreon.TierChanged += OnTierChanged;
                if (App.SubscribeStar != null) App.SubscribeStar.TierChanged += OnTierChanged;
            }
            catch { /* services not up (tests): Refresh still reads null as Free */ }
            EntitlementTierSync.TierRaised += OnTierRaised;
            LocalizationManager.Instance.LanguageChanged += OnLanguageChanged;
            Refresh();
            UpdateParking();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            if (_window != null) _window.StateChanged -= OnWindowStateChanged;
            _window = null;
            try
            {
                if (App.Patreon != null) App.Patreon.TierChanged -= OnTierChanged;
                if (App.SubscribeStar != null) App.SubscribeStar.TierChanged -= OnTierChanged;
            }
            catch { }
            EntitlementTierSync.TierRaised -= OnTierRaised;
            LocalizationManager.Instance.LanguageChanged -= OnLanguageChanged;
            StopLoops();
        }

        private void OnWindowStateChanged(object? sender, EventArgs e) => UpdateParking();

        private void OnTierChanged(object? sender, PatreonTier tier) =>
            Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(Refresh));

        private void OnTierRaised(int tier) =>
            Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
            {
                Refresh();
                Burst();
            }));

        private void OnLanguageChanged(object? sender, EventArgs e) =>
            Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
            {
                BuildLabel();
                ToolTip = Loc.Get(PremiumSparkRules.TooltipKey(Tier));
            }));

        // =======================================================================================
        //  helpers
        // =======================================================================================

        private static T MakeFrozen<T>(T freezable) where T : Freezable
        {
            freezable.Freeze();
            return freezable;
        }

        private static SolidColorBrush Frozen(Color colour) => MakeFrozen(new SolidColorBrush(colour));

        // The rules live in Core (shared with the Avalonia head) with Core-typed colours and points.
        private static Color C(SparkColor c) => Color.FromRgb(c.R, c.G, c.B);
        private static Point P(SparkPoint p) => new(p.X, p.Y);

        /// <summary>The sparkle outline at unit radius, centred on the origin, for star particles.</summary>
        private static Geometry MakeUnitStar()
        {
            var g = Geometry.Parse(PremiumSparkRules.StarPathData).Clone();
            var half = PremiumSparkRules.StarSize / 2;
            g.Transform = new TransformGroup
            {
                Children = { new TranslateTransform(-half, -half), new ScaleTransform(1 / half, 1 / half) },
            };
            return MakeFrozen(g);
        }

        /// <summary>Paper grain: a 7 px tile of faint flecks, light and dark, seeded so every
        /// build draws the same paper.</summary>
        private static Brush MakeGrain()
        {
            var rng = new Random(1007);
            var group = new DrawingGroup();
            using (var dc = group.Open())
            {
                dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, 7, 7));
                for (int i = 0; i < 9; i++)
                {
                    var light = i % 2 == 0;
                    var b = new SolidColorBrush(light ? Color.FromArgb(0x90, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x90, 0x2A, 0x1A, 0x10));
                    b.Freeze();
                    dc.DrawRectangle(b, null, new Rect(rng.NextDouble() * 6.4, rng.NextDouble() * 6.4, 0.6, 0.6));
                }
            }
            group.Freeze();
            var brush = new DrawingBrush(group)
            {
                TileMode = TileMode.Tile,
                Viewport = new Rect(0, 0, 7, 7),
                ViewportUnits = BrushMappingMode.Absolute,
                Viewbox = new Rect(0, 0, 7, 7),
                ViewboxUnits = BrushMappingMode.Absolute,
            };
            brush.Freeze();
            return brush;
        }
    }
}
