using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using ConditioningControlPanel.Fx;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using FrameClock = ConditioningControlPanel.Avalonia.Controls.Fx.FrameClock;
using Env = ConditioningControlPanel.Avalonia.Controls.AmbientFxCanvas.Env;

// Namespace is Controls (not Controls.Header) so MainShellWindow.axaml reaches it through its
// existing fx: prefix: Avalonia XAML allows xmlns declarations on the root element only.
namespace ConditioningControlPanel.Avalonia.Controls
{
    /// <summary>
    /// PORTED from WPF 7.1.5 Controls/Header/PremiumSpark.xaml(.cs) (polish 12, owner 2026-10-07).
    ///
    /// <para>The header Premium spark: a cut-and-folded cardstock four-point sparkle with PREMIUM
    /// cut out under it. Free reads dim and still, Basic is gold with a sheen, spilling glitter,
    /// orbiting twinkles and glints, Prime is ice cyan with a pulsing halo, a dense diamond
    /// field, tip stars and the odd flare. Rules and numbers live in Core
    /// <see cref="PremiumSparkRules"/>, the particles in Core <see cref="PremiumSparkField"/>;
    /// this class only draws them.</para>
    ///
    /// <para>WPF built a Canvas of Paths plus a fixed pool of mote shapes; here the whole card is
    /// one <see cref="Render"/> pass (halo, paper shadow, die-cut, face, folds, grain, lamp wash,
    /// sheen, label, motes), re-recorded by ONE 30 fps <see cref="FrameClock"/>. The visual is
    /// identical; the cost is a draw list for a 54 x 42 control. The clock parks while the window
    /// is hidden or minimised and never starts for Free or at motion Off (a hover tween borrows it
    /// for its few frames, then it stops again). No Effect anywhere: the paper shadow is a second
    /// cut of the same shape (the depth law). Motes spill past the card unclipped.</para>
    /// </summary>
    public sealed class PremiumSpark : Button
    {
        private const double StarLeft = (PremiumSparkRules.ControlWidth - PremiumSparkRules.StarSize) / 2;
        private const double StarTop = 0;
        private const double LabelTop = 34;
        private const double OriginX = PremiumSparkRules.ControlWidth * 0.5;
        private const double OriginY = PremiumSparkRules.ControlHeight * 0.42;

        // Lazy: a Geometry needs the platform render interface, which a type initializer can beat.
        private static Geometry? _star, _unitStar, _unitDiamond, _unitFleck;
        private static (Geometry light, Geometry dark)? _grain;
        private static Geometry StarGeometry => _star ??= BuildStar(1, 0, 0);
        private static Geometry UnitStar => _unitStar ??= BuildStar(1 / (PremiumSparkRules.StarSize / 2),
            -PremiumSparkRules.StarSize / 2, -PremiumSparkRules.StarSize / 2);
        private static Geometry UnitDiamond => _unitDiamond ??= Geometry.Parse("M0,-1 L0.55,0 L0,1 L-0.55,0 Z");
        private static Geometry UnitFleck => _unitFleck ??= Geometry.Parse("M-1,-0.7 L1,-0.7 L1,0.7 L-1,0.7 Z");
        private static (Geometry light, Geometry dark) Grain => _grain ??= BuildGrain();

        private Random _rng = new();
        private PremiumSparkField _field;

        // Painted per tier (Paint), read by Render.
        private IBrush _face = Brushes.Gray, _edge = Brushes.White, _shadow = Brushes.Black, _labelInk = Brushes.Gray;
        private IBrush? _halo;
        private IBrush _flare = Brushes.White;
        private IBrush[] _motes = Array.Empty<IBrush>();
        private readonly List<(Geometry g, IBrush b)> _facets = new();
        private readonly List<(Point a, Point b, IPen pen)> _creases = new();
        private IBrush? _spec;
        private Geometry? _label;

        // The cutout's transform state (breath, tilt + wobble, lift) and the paper shadows.
        private double _breath = 1, _tilt = PremiumSparkRules.Tilt, _lift;
        private double _starShadowX, _starShadowY, _labelShadowX, _labelShadowY;
        private double _sheenX = -20, _haloOpacity, _haloScale = 1;

        private FrameClock? _clock;
        private readonly System.Diagnostics.Stopwatch _watch = new();
        private double _last, _t;
        private bool _loopsRunning, _hovered, _attached;
        private Window? _window;
        private readonly List<Tween> _tweens = new();

        public SparkTier Tier { get; private set; } = SparkTier.Free;
        public SparkMotion Motion { get; private set; } = SparkMotion.Off;
        public bool ParticlesAllowed { get; private set; }

        /// <summary>True while the ambient clock is running (tests and parking read it).</summary>
        public bool LoopsRunning => _loopsRunning;

        /// <summary>The particle field (tests read its counts).</summary>
        public PremiumSparkField Field => _field;

        /// <summary>When set, Refresh() keeps the pinned look (offscreen renders, tests).</summary>
        public bool Pinned { get; set; }

        /// <summary>Where the tier comes from. The profile bubble's rule: HasLabAccess is Prime,
        /// else HasPremiumAccess is Basic, else Free (Core CoreAccount providers).</summary>
        public static Func<SparkTier> TierSource { get; set; } =
            () => PremiumSparkRules.TierFrom(CoreAccount.HasPremiumAccess, CoreAccount.HasLabAccess);

        public PremiumSpark()
        {
            _field = new PremiumSparkField(_rng);
            Width = PremiumSparkRules.ControlWidth;
            Height = PremiumSparkRules.ControlHeight;
            Padding = default;
            Background = Brushes.Transparent;
            BorderThickness = default;
            Cursor = new Cursor(StandardCursorType.Hand);
            ClipToBounds = false;
            Focusable = true;
            Apply(SparkTier.Free, SparkMotion.Off, false);
        }

        /// <summary>Repeatable particles for offscreen frame strips.</summary>
        public void Reseed(int seed)
        {
            _rng = new Random(seed);
            _field = new PremiumSparkField(_rng);
            _field.Configure(Tier, Motion, ParticlesAllowed);
            _t = 0;
        }

        // =======================================================================================
        //  state
        // =======================================================================================

        /// <summary>Re-reads the tier and the motion level and repaints. Idempotent.</summary>
        public void Refresh()
        {
            if (Pinned) return;
            SparkTier tier;
            try { tier = TierSource(); }
            catch { tier = SparkTier.Free; }
            var perf = Env.CurrentTier;
            var motion = PremiumSparkRules.MotionFrom(Env.Level, Env.AllowAmbientMotion(perf));
            if (Controls.Fx.FxBisect.Off("spark")) motion = SparkMotion.Off;
            Apply(tier, motion, Env.AllowParticles);
        }

        /// <summary>Paints one state. Public so offscreen renders can pin a look.</summary>
        public void Apply(SparkTier tier, SparkMotion motion, bool particlesAllowed)
        {
            bool changed = tier != Tier || motion != Motion || particlesAllowed != ParticlesAllowed;
            Tier = tier;
            Motion = motion;
            ParticlesAllowed = particlesAllowed;
            _field.Configure(tier, motion, particlesAllowed);
            Paint();
            var tip = Loc.Get(PremiumSparkRules.TooltipKey(tier));
            ToolTip.SetTip(this, tip);
            AutomationProperties.SetName(this, tip);
            if (changed || !_loopsRunning) RestartLoops();
            InvalidateVisual();
        }

        private void Paint()
        {
            var livery = PremiumSparkRules.LiveryFor(Tier);
            _face = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0.2, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(0.8, 1, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(C(livery.FaceTop), 0.0),
                    new GradientStop(C(livery.FaceMid), 0.48),
                    new GradientStop(C(livery.FaceBottom), 1.0),
                },
            }.ToImmutable();
            _edge = new ImmutableSolidColorBrush(C(livery.Edge));
            // The paper shadow: ink pulled toward the tier colour, the DepthRules recipe.
            _shadow = new ImmutableSolidColorBrush(C(ConditioningControlPanel.Depth.DepthRules.ShadowColor(livery.FaceBottom)));
            _labelInk = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                GradientStops = { new GradientStop(C(livery.FaceMid), 0), new GradientStop(C(livery.FaceBottom), 1) },
            }.ToImmutable();

            BuildFolds(livery);

            // Particle inks: the tier's mote colour, white, and a deeper tone (Prime: the cyan ink).
            _motes = Tier == SparkTier.Basic
                ? new IBrush[] { Solid(0xFF, 0xD2, 0x4A), Brushes.White, Solid(0xFF, 0xB0, 0x22) }
                : new IBrush[] { new ImmutableSolidColorBrush(C(livery.Mote)), Brushes.White, new ImmutableSolidColorBrush(C(livery.Ink)) };
            _flare = new RadialGradientBrush
            {
                GradientStops =
                {
                    new GradientStop(Colors.White, 0.0),
                    new GradientStop(Colors.White, 0.35),
                    new GradientStop(C(livery.Ink), 1.0),
                },
            }.ToImmutable();

            // Prime's halo: drawn at every motion level, pulsing only on the clock.
            _halo = PremiumSparkRules.Halo(Tier)
                ? new RadialGradientBrush
                {
                    GradientStops =
                    {
                        new GradientStop(Color.FromArgb(0xA0, 0x7F, 0xE9, 0xFF), 0.0),
                        new GradientStop(Color.FromArgb(0x55, 0x5E, 0xE6, 0xFF), 0.45),
                        new GradientStop(Color.FromArgb(0x00, 0x5E, 0xE6, 0xFF), 1.0),
                    },
                }.ToImmutable()
                : null;
            _haloOpacity = PremiumSparkRules.HaloAt(_t, Tier, SparkMotion.Off, 0);
            _haloScale = 1;
            BuildLabel();
            SettleDepth(animate: false);
        }

        /// <summary>The folded cardstock: the star cut into eight facets that meet at a raised
        /// heart, each shaded by how it faces the top-left lamp, mountain folds as fine light
        /// creases, valley folds as fine dark ones, and a specular catch by the heart.</summary>
        private void BuildFolds(PremiumSparkRules.Livery livery)
        {
            _facets.Clear();
            _creases.Clear();
            var dark = C(ConditioningControlPanel.Depth.DepthRules.ShadowColor(livery.FaceBottom));
            var heart = P(PremiumSparkRules.Heart);
            var valleys = new Point[4];
            for (int k = 0; k < 4; k++)
            {
                var flank = PremiumSparkRules.Flanks[k];
                var (h1, h2) = PremiumSparkRules.Split(flank, 0.5);
                valleys[k] = P(h1.Item4);
                AddFacet(heart, h1, PremiumSparkRules.FacetShade(flank.p0, h1.Item4), dark);
                AddFacet(heart, h2, PremiumSparkRules.FacetShade(h1.Item4, flank.p3), dark);
            }
            var crease = new ImmutablePen(new ImmutableSolidColorBrush(Color.FromArgb(0x9A, 0xFF, 0xFF, 0xFF)), 0.55, lineCap: PenLineCap.Round);
            var valley = new ImmutablePen(new ImmutableSolidColorBrush(Color.FromArgb(0x55, dark.R, dark.G, dark.B)), 0.5, lineCap: PenLineCap.Round);
            for (int k = 0; k < 4; k++)
            {
                _creases.Add((heart, valleys[k], valley));
                _creases.Add((heart, P(PremiumSparkRules.Flanks[k].p0), crease));
            }
            _spec = new RadialGradientBrush
            {
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(0xC8, 0xFF, 0xFF, 0xFF), 0.0),
                    new GradientStop(Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 1.0),
                },
            }.ToImmutable();
        }

        private void AddFacet(Point heart, (Vec2 p0, Vec2 c1, Vec2 c2, Vec2 p3) half, double shade, Color dark)
        {
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                ctx.BeginFigure(heart, true);
                ctx.LineTo(P(half.p0));
                ctx.CubicBezierTo(P(half.c1), P(half.c2), P(half.p3));
                ctx.EndFigure(true);
            }
            // Lit facets catch white, turned-away ones take the tier-tinted shadow ink.
            var colour = shade >= 0
                ? Color.FromArgb((byte)Math.Round(Math.Min(1, shade) * 0x78), 0xFF, 0xFF, 0xFF)
                : Color.FromArgb((byte)Math.Round(Math.Min(1, -shade) * 0x68), dark.R, dark.G, dark.B);
            _facets.Add((g, new ImmutableSolidColorBrush(colour)));
        }

        /// <summary>The label as one geometry, letter-spaced by hand, scaled down if a long
        /// translation would outgrow the card.</summary>
        private void BuildLabel()
        {
            var text = Loc.Get("premium_spark_label");
            if (string.IsNullOrWhiteSpace(text) || text == "premium_spark_label") text = "PREMIUM";
            var geometry = BuildTrackedText(text.ToUpper(CultureInfo.CurrentCulture),
                PremiumSparkRules.LabelSize, PremiumSparkRules.LabelTracking);
            if (geometry == null) { _label = null; return; }
            var bounds = geometry.Bounds;
            if (bounds.Width <= 0) { _label = null; return; }
            var maxWidth = PremiumSparkRules.ControlWidth - 4;
            var scale = bounds.Width > maxWidth ? maxWidth / bounds.Width : 1.0;
            geometry.Transform = new MatrixTransform(
                Matrix.CreateTranslation(-bounds.X, -bounds.Y)
                * Matrix.CreateScale(scale, scale)
                * Matrix.CreateTranslation((PremiumSparkRules.ControlWidth - bounds.Width * scale) / 2, LabelTop));
            _label = geometry;
        }

        internal static Geometry? BuildTrackedText(string text, double size, double tracking)
        {
            var typeface = new Typeface(new FontFamily("Segoe UI Black, Segoe UI, Inter"), FontStyle.Normal, FontWeight.Black);
            var group = new GeometryGroup { FillRule = FillRule.NonZero };
            double x = 0;
            foreach (var ch in text)
            {
                var ft = new FormattedText(ch.ToString(), CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                    typeface, size, Brushes.Black);
                if (ft.BuildGeometry(new Point(x, 0)) is { } g) group.Children.Add(g);
                x += ft.WidthIncludingTrailingWhitespace + tracking;
            }
            return group.Children.Count == 0 ? null : group;
        }

        // =======================================================================================
        //  drawing
        // =======================================================================================

        public override void Render(DrawingContext context)
        {
            // The whole card hit-tests (a transparent fill is hittable; nothing else here is).
            context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));
            using var root = context.PushOpacity(PremiumSparkRules.Opacity(Tier));

            if (_halo != null && _haloOpacity > 0)
            {
                using var _o = context.PushOpacity(_haloOpacity);
                var r = 26 * _haloScale;
                context.DrawEllipse(_halo, null, new Point(PremiumSparkRules.ControlWidth / 2, -11 + 26), r, r);
            }

            var cut = Matrix.CreateTranslation(-OriginX, -OriginY)
                      * Matrix.CreateScale(_breath, _breath)
                      * Matrix.CreateRotation(_tilt * Math.PI / 180)
                      * Matrix.CreateTranslation(OriginX, OriginY + _lift);
            using (context.PushTransform(cut))
            {
                if (_label != null)
                {
                    using (context.PushTransform(Matrix.CreateTranslation(_labelShadowX, _labelShadowY)))
                        context.DrawGeometry(_shadow, new ImmutablePen((IImmutableBrush)_shadow, PremiumSparkRules.LabelDieCutPx, lineJoin: PenLineJoin.Round), _label);
                    context.DrawGeometry(_edge, new ImmutablePen((IImmutableBrush)_edge, PremiumSparkRules.LabelDieCutPx, lineJoin: PenLineJoin.Round), _label);
                    context.DrawGeometry(_labelInk, null, _label);
                }

                using (context.PushTransform(Matrix.CreateTranslation(StarLeft, StarTop)))
                {
                    using (context.PushTransform(Matrix.CreateTranslation(_starShadowX, _starShadowY)))
                        context.DrawGeometry(_shadow, new ImmutablePen((IImmutableBrush)_shadow, PremiumSparkRules.DieCutPx, lineJoin: PenLineJoin.Round), StarGeometry);
                    context.DrawGeometry(_edge, new ImmutablePen((IImmutableBrush)_edge, PremiumSparkRules.DieCutPx, lineJoin: PenLineJoin.Round), StarGeometry);
                    context.DrawGeometry(_face, RimPen, StarGeometry);
                    using (context.PushGeometryClip(StarGeometry))
                    {
                        foreach (var (g, b) in _facets) context.DrawGeometry(b, null, g);
                        foreach (var (a, b, pen) in _creases) context.DrawLine(pen, a, b);
                        if (_spec != null)
                        {
                            var h = PremiumSparkRules.Heart;
                            context.DrawEllipse(_spec, null, new Point(h.X - 1.9, h.Y - 2.6), 2.4, 2.4);
                        }
                        using (context.PushOpacity(0.16))
                        {
                            context.DrawGeometry(GrainLight, null, Grain.light);
                            context.DrawGeometry(GrainDark, null, Grain.dark);
                        }
                        context.DrawGeometry(Highlight, null, StarGeometry);
                        if (PremiumSparkRules.Sheen(Tier, Motion))
                        {
                            // A skewed light band slid across by the clock.
                            var band = Matrix.CreateSkew(Math.Tan(-22 * Math.PI / 180), 0)
                                       * Matrix.CreateTranslation(_sheenX, -6);
                            using (context.PushTransform(band))
                                context.FillRectangle(SheenBand, new Rect(0, 0, 11, PremiumSparkRules.StarSize + 12));
                        }
                    }
                }
            }

            DrawMotes(context);
        }

        private void DrawMotes(DrawingContext context)
        {
            var alive = _field.Alive;
            for (int i = 0; i < alive.Count; i++)
            {
                var m = alive[i];
                var op = PremiumSparkField.OpacityOf(m);
                if (op <= 0.003) continue;
                var half = m.Size / 2 * PremiumSparkField.ScaleOf(m);
                var sy = half;
                if (m.Kind == MoteKind.Glitter || (m.Kind == MoteKind.Burst && Tier == SparkTier.Basic))
                    sy = half * (0.25 + 0.75 * Math.Abs(Math.Cos(m.Phase + m.Age * 9)));   // a fleck flipping in the light
                if (half <= 0 || sy <= 0) continue;
                var brush = m.Kind == MoteKind.Flare ? _flare
                    : m.Colour >= 0 && m.Colour < _motes.Length ? _motes[m.Colour] : Brushes.White;
                var matrix = Matrix.CreateScale(half, sy)
                             * Matrix.CreateRotation(m.Angle * Math.PI / 180)
                             * Matrix.CreateTranslation(m.X, m.Y);
                using (context.PushOpacity(op))
                using (context.PushTransform(matrix))
                    context.DrawGeometry(brush, null, GeometryFor(m.Kind));
            }
        }

        private Geometry GeometryFor(MoteKind kind) => kind switch
        {
            MoteKind.Diamond => UnitDiamond,
            MoteKind.Glitter => UnitDiamond,
            MoteKind.Burst => Tier == SparkTier.Prime ? UnitDiamond : UnitFleck,
            _ => UnitStar,   // orbits, glints and flares are little four-point stars
        };

        // =======================================================================================
        //  depth: hover, press, release
        // =======================================================================================

        protected override void OnPointerEntered(PointerEventArgs e)
        {
            base.OnPointerEntered(e);
            _hovered = true;
            SettleDepth(animate: true);
            if (PremiumSparkRules.Wobble(Tier, Motion)) Wobble();
        }

        protected override void OnPointerExited(PointerEventArgs e)
        {
            base.OnPointerExited(e);
            _hovered = false;
            SettleDepth(animate: true);
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (change.Property == IsPressedProperty) SettleDepth(animate: true);
            else if (change.Property == IsVisibleProperty) UpdateParking();
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
            int ms = IsPressed ? ConditioningControlPanel.Depth.DepthRules.PressMs : ConditioningControlPanel.Depth.DepthRules.HoverMs;
            bool tween = animate && Motion != SparkMotion.Off && Env.AllowTransitions;
            Slide(TweenKind.Lift, travel, tween, ms);
            Slide(TweenKind.StarShadowX, shadow * 0.45, tween, ms);
            Slide(TweenKind.StarShadowY, shadow * 0.8, tween, ms);
            Slide(TweenKind.LabelShadowX, shadow * 0.3, tween, ms);
            Slide(TweenKind.LabelShadowY, shadow * 0.5, tween, ms);
        }

        private enum TweenKind { Lift, StarShadowX, StarShadowY, LabelShadowX, LabelShadowY, Wobble }

        private sealed class Tween
        {
            public TweenKind Kind;
            public double From, To, Ms, Elapsed;
        }

        private double Get(TweenKind k) => k switch
        {
            TweenKind.Lift => _lift,
            TweenKind.StarShadowX => _starShadowX,
            TweenKind.StarShadowY => _starShadowY,
            TweenKind.LabelShadowX => _labelShadowX,
            TweenKind.LabelShadowY => _labelShadowY,
            _ => _tilt,
        };

        private void Set(TweenKind k, double v)
        {
            switch (k)
            {
                case TweenKind.Lift: _lift = v; break;
                case TweenKind.StarShadowX: _starShadowX = v; break;
                case TweenKind.StarShadowY: _starShadowY = v; break;
                case TweenKind.LabelShadowX: _labelShadowX = v; break;
                case TweenKind.LabelShadowY: _labelShadowY = v; break;
                default: _tilt = v; break;
            }
        }

        private void Slide(TweenKind kind, double to, bool animate, int ms)
        {
            _tweens.RemoveAll(t => t.Kind == kind);
            if (!animate) { Set(kind, to); InvalidateVisual(); return; }
            _tweens.Add(new Tween { Kind = kind, From = Get(kind), To = to, Ms = ms });
            EnsureClock();
        }

        private void Wobble()
        {
            _tweens.RemoveAll(t => t.Kind == TweenKind.Wobble);
            _tweens.Add(new Tween { Kind = TweenKind.Wobble, From = PremiumSparkRules.Tilt, To = PremiumSparkRules.Tilt, Ms = PremiumSparkRules.WobbleMs });
            EnsureClock();
        }

        /// <summary>BackEase out (amplitude 0.35) for the depth slides; keyframes for the wobble.</summary>
        private void StepTweens(double dtMs)
        {
            for (int i = _tweens.Count - 1; i >= 0; i--)
            {
                var tw = _tweens[i];
                tw.Elapsed += dtMs;
                var u = Math.Clamp(tw.Elapsed / tw.Ms, 0, 1);
                if (tw.Kind == TweenKind.Wobble)
                {
                    var w = PremiumSparkRules.WobbleDegrees;
                    _tilt = PremiumSparkRules.Tilt + WobbleAt(u, w);
                }
                else
                {
                    Set(tw.Kind, tw.From + (tw.To - tw.From) * BackOut(u, 0.35));
                }
                if (u >= 1)
                {
                    if (tw.Kind == TweenKind.Wobble) _tilt = PremiumSparkRules.Tilt;
                    else Set(tw.Kind, tw.To);
                    _tweens.RemoveAt(i);
                }
            }
        }

        private static double WobbleAt(double u, double w)
        {
            (double at, double v)[] keys = { (0, 0), (0.22, w), (0.5, -w * 0.6), (0.75, w * 0.25), (1, 0) };
            for (int i = 1; i < keys.Length; i++)
                if (u <= keys[i].at)
                {
                    var k = (u - keys[i - 1].at) / (keys[i].at - keys[i - 1].at);
                    return keys[i - 1].v + (keys[i].v - keys[i - 1].v) * k;
                }
            return 0;
        }

        private static double BackOut(double u, double amplitude)
        {
            var t = 1 - u;
            return 1 - (t * t * t - t * amplitude * Math.Sin(t * Math.PI));
        }

        // =======================================================================================
        //  the clock: sheen, breath, halo, particles and tweens on one 30 fps frame clock
        // =======================================================================================

        private bool Parked
        {
            get
            {
                if (!IsEffectivelyVisible || !_attached) return true;
                var win = _window ?? TopLevel.GetTopLevel(this) as Window;
                return win != null && (win.WindowState == WindowState.Minimized || !win.IsVisible);
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
            _loopsRunning = true;
            EnsureClock();
        }

        private void StopLoops()
        {
            _loopsRunning = false;
            _field.Clear();
            _sheenX = -20;
            _breath = 1.0;
            _haloOpacity = PremiumSparkRules.HaloAt(0, Tier, SparkMotion.Off, 0);
            _haloScale = 1;
            if (_tweens.Count == 0) StopClock();
            InvalidateVisual();
        }

        private void EnsureClock()
        {
            if (_clock != null) return;
            if (!_attached) return;   // not attached: tweens snap on attach
            _clock = new FrameClock(this) { Interval = TimeSpan.FromMilliseconds(1000.0 / PremiumSparkRules.FrameRate) };
            _clock.Tick += OnClockTick;
            _watch.Restart();
            _last = 0;
            _clock.Start();
        }

        private void StopClock()
        {
            if (_clock == null) return;
            _clock.Stop();
            _clock.Tick -= OnClockTick;
            _clock = null;
            _watch.Reset();
        }

        private void OnClockTick(object? sender, EventArgs e)
        {
            var now = _watch.Elapsed.TotalSeconds;
            var dt = now - _last;
            _last = now;
            if (_tweens.Count > 0) StepTweens(dt * 1000);
            if (_loopsRunning) Advance(dt);
            else InvalidateVisual();
            if (!_loopsRunning && _tweens.Count == 0 && _field.Alive.Count == 0) StopClock();
        }

        /// <summary>Steps the field without drawing (so the first frame already has particles).</summary>
        public void Prewarm(double seconds)
        {
            const double step = 1.0 / PremiumSparkRules.FrameRate;
            for (double s = 0; s < seconds; s += step) _field.Step(step);
        }

        /// <summary>Moves everything by dt seconds and draws it. The clock calls it; offscreen
        /// frame strips call it by hand.</summary>
        public void Advance(double dt)
        {
            if (dt < 0) dt = 0;
            _t += dt;
            _field.Step(dt);
            _sheenX = PremiumSparkRules.SheenAt(_t, Tier, Motion);
            _breath = PremiumSparkRules.BreathAt(_t, Tier, Motion);
            _haloOpacity = PremiumSparkRules.HaloAt(_t, Tier, Motion, _field.FlareGlow);
            _haloScale = 1 + 0.14 * _field.FlareGlow;
            InvalidateVisual();
        }

        /// <summary>The click burst: diamonds (Prime) or gold flecks (Basic) thrown out radially.</summary>
        public void Burst()
        {
            var count = PremiumSparkRules.BurstCount(Tier, Motion);
            if (count <= 0 || !Env.AllowTransitions) return;
            _field.Burst(count);
            InvalidateVisual();
        }

        // =======================================================================================
        //  lifecycle
        // =======================================================================================

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            _attached = true;
            _window = TopLevel.GetTopLevel(this) as Window;
            if (_window != null) _window.PropertyChanged += OnWindowPropertyChanged;
            Env.MotionGateChanged += OnMotionGateChanged;
            CoreAccount.UnifiedIdentityChanged += OnIdentityChanged;
            LocalizationManager.Instance.LanguageChanged += OnLanguageChanged;
            Refresh();
            UpdateParking();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnDetachedFromVisualTree(e);
            _attached = false;
            if (_window != null) _window.PropertyChanged -= OnWindowPropertyChanged;
            _window = null;
            Env.MotionGateChanged -= OnMotionGateChanged;
            CoreAccount.UnifiedIdentityChanged -= OnIdentityChanged;
            LocalizationManager.Instance.LanguageChanged -= OnLanguageChanged;
            _tweens.Clear();
            StopLoops();
            StopClock();
        }

        private void OnWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property == Window.WindowStateProperty || e.Property == IsVisibleProperty) UpdateParking();
        }

        private void OnMotionGateChanged() => Refresh();

        private void OnIdentityChanged(object? sender, EventArgs e) =>
            global::Avalonia.Threading.Dispatcher.UIThread.Post(Refresh);

        private void OnLanguageChanged(object? sender, EventArgs e) =>
            global::Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                BuildLabel();
                ToolTip.SetTip(this, Loc.Get(PremiumSparkRules.TooltipKey(Tier)));
                InvalidateVisual();
            });

        // =======================================================================================
        //  helpers
        // =======================================================================================

        private static readonly IPen RimPen = new ImmutablePen(new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Color.FromArgb(0xE0, 0xFF, 0xFF, 0xFF), 0.0),
                new GradientStop(Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 0.52),
            },
        }.ToImmutable(), 1.3, lineJoin: PenLineJoin.Round);

        private static readonly IBrush Highlight = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Color.FromArgb(0x50, 0xFF, 0xFF, 0xFF), 0.0),
                new GradientStop(Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 0.5),
            },
        }.ToImmutable();

        private static readonly IBrush SheenBand = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 0.0),
                new GradientStop(Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF), 0.5),
                new GradientStop(Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 1.0),
            },
        }.ToImmutable();

        private static readonly IBrush GrainLight = new ImmutableSolidColorBrush(Color.FromArgb(0x90, 0xFF, 0xFF, 0xFF));
        private static readonly IBrush GrainDark = new ImmutableSolidColorBrush(Color.FromArgb(0x90, 0x2A, 0x1A, 0x10));

        private static Color C(uint argb) =>
            Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);

        private static IBrush Solid(byte r, byte g, byte b) => new ImmutableSolidColorBrush(Color.FromRgb(r, g, b));

        private static Point P(Vec2 v) => new(v.X, v.Y);

        /// <summary>The sparkle outline from Core's four flanks, scaled then offset.</summary>
        private static Geometry BuildStar(double scale, double dx, double dy)
        {
            Point M(Vec2 v) => new((v.X + dx) * scale, (v.Y + dy) * scale);
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                ctx.BeginFigure(M(PremiumSparkRules.Flanks[0].p0), true);
                foreach (var f in PremiumSparkRules.Flanks) ctx.CubicBezierTo(M(f.c1), M(f.c2), M(f.p3));
                ctx.EndFigure(true);
            }
            return g;
        }

        /// <summary>Paper grain: a 7 px tile of faint flecks, light and dark, seeded so every
        /// build draws the same paper, laid over the star box (clipped to the star by Render).</summary>
        private static (Geometry, Geometry) BuildGrain()
        {
            var rng = new Random(1007);
            var tile = new (double x, double y, bool light)[9];
            for (int i = 0; i < 9; i++) tile[i] = (rng.NextDouble() * 6.4, rng.NextDouble() * 6.4, i % 2 == 0);
            var light = new StreamGeometry();
            var dark = new StreamGeometry();
            using (var lc = light.Open())
            using (var dc = dark.Open())
            {
                for (double ty = 0; ty < PremiumSparkRules.StarSize; ty += 7)
                for (double tx = 0; tx < PremiumSparkRules.StarSize; tx += 7)
                    foreach (var (x, y, isLight) in tile)
                    {
                        var c = isLight ? lc : dc;
                        c.BeginFigure(new Point(tx + x, ty + y), true);
                        c.LineTo(new Point(tx + x + 0.6, ty + y));
                        c.LineTo(new Point(tx + x + 0.6, ty + y + 0.6));
                        c.LineTo(new Point(tx + x, ty + y + 0.6));
                        c.EndFigure(true);
                    }
            }
            return (light, dark);
        }
    }
}
