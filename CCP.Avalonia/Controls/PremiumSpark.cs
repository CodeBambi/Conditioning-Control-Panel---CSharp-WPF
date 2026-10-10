using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Rendering;
using Avalonia.Threading;
using ConditioningControlPanel.Controls.Header;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Controls
{
    /// <summary>
    /// The header Premium spark (WPF Controls/Header/PremiumSpark.xaml(.cs), polish 12 + round 2): a
    /// cut-and-folded cardstock four-point sparkle with PREMIUM cut out under it. Free dim and still,
    /// Basic gold with sheen, breath, glitter, orbiting twinkles and glints, Prime ice cyan with a
    /// pulsing halo, diamonds, tip stars and flares. Rules and particles are Core
    /// (<see cref="PremiumSparkRules"/>, <see cref="PremiumSparkField"/>); this class only draws them.
    /// WPF builds a tree of Paths and a mote pool; here the same layers are painted in one Render
    /// pass (same order, offsets and brushes). ONE 30 fps clock (WPF FrameClock) runs only while the
    /// spark is attached, effectively visible, its window not minimised, and the look can move (P01,
    /// P65); it also runs briefly while a hover/press settle or wobble is in flight.
    /// </summary>
    public sealed class PremiumSpark : Control, ICustomHitTest
    {
        // Geometry and brushes are per instance, not static: a static would touch the render platform
        // before Avalonia starts (P26).
        private const double W = PremiumSparkRules.ControlWidth, H = PremiumSparkRules.ControlHeight;
        private const double StarLeft = (W - PremiumSparkRules.StarSize) / 2, StarTop = 0, LabelTop = 34;
        private const double ShadowAlpha = 0.62, ShadowHuePull = 0.28;   // DepthRules.ShadowAlpha / ShadowHuePull

        private readonly Geometry Star = StreamGeometry.Parse(PremiumSparkRules.StarPathData);
        private readonly Geometry UnitDiamond = StreamGeometry.Parse("M0,-1 L0.55,0 L0,1 L-0.55,0 Z");
        private readonly Geometry UnitFleck = StreamGeometry.Parse("M-1,-0.7 L1,-0.7 L1,0.7 L-1,0.7 Z");
        private readonly Geometry UnitStar = MakeUnitStar();
        private static readonly Rect[] Grain = MakeGrain();
        private readonly IBrush GrainLight = new SolidColorBrush(Color.FromArgb(0x90, 0xFF, 0xFF, 0xFF));
        private readonly IBrush GrainDark = new SolidColorBrush(Color.FromArgb(0x90, 0x2A, 0x1A, 0x10));

        private Random _rng = new();
        private PremiumSparkField _field;
        private DispatcherTimer? _clock;
        private readonly System.Diagnostics.Stopwatch _watch = new();
        private double _last, _t;
        private Window? _window;
        private IDisposable? _visibilityWatch;
        private bool _attached, _hovered, _pressed;

        // Painted per tier (WPF Paint()).
        private IBrush _face = Brushes.Transparent, _edge = Brushes.Transparent, _shadow = Brushes.Transparent, _ink = Brushes.Transparent;
        private IBrush _halo = Brushes.Transparent, _flare = Brushes.White;
        private IBrush[] _motes = Array.Empty<IBrush>();
        private readonly List<(Geometry g, IBrush b)> _facets = new();
        private Pen _valleyPen = new(Brushes.Transparent);
        private Geometry? _label;

        // Hover/press settle (WPF Slide: BackEase out, amplitude 0.35) and the hover wobble.
        private double _travelFrom, _travelTo, _shadowFrom, _shadowTo, _settleStart = -1, _settleMs = 1;
        private double _wobbleStart = -1;

        /// <summary>Raised on click / Enter / Space, after the burst (WPF OnClick).</summary>
        public event EventHandler<RoutedEventArgs>? Click;

        internal SparkTier Tier { get; private set; } = SparkTier.Free;
        internal SparkMotion Motion { get; private set; } = SparkMotion.Off;
        internal bool ParticlesAllowed { get; private set; }
        internal bool LoopsRunning => _clock != null;
        internal PremiumSparkField Field => _field;
        internal double SheenX => PremiumSparkRules.SheenAt(_t, Tier, Motion);
        /// <summary>When set, Refresh() keeps the pinned look (tests, renders).</summary>
        internal bool Pinned { get; set; }

        public PremiumSpark()
        {
            Width = W; Height = H;
            Focusable = true;
            Cursor = new Cursor(StandardCursorType.Hand);
            _field = new PremiumSparkField(_rng);
            Apply(SparkTier.Free, SparkMotion.Off, false);
        }

        internal void Reseed(int seed)
        {
            _rng = new Random(seed);
            _field = new PremiumSparkField(_rng);
            _field.Configure(Tier, Motion, ParticlesAllowed);
            _t = 0;
        }

        // ================================ state ================================

        /// <summary>WPF Refresh: tier from the canonical gates, motion from the level folded with
        /// the performance tier. Idempotent.</summary>
        public void Refresh()
        {
            if (Pinned) return;
            var tier = PremiumSparkRules.TierFrom(CoreAccount.HasPremiumAccess, CoreAccount.HasLabAccess);
            var motion = PremiumSparkRules.MotionFrom(AmbientFxCanvas.Env.Level,
                AmbientFxCanvas.Env.AllowAmbientMotion(AmbientFxCanvas.Env.CurrentTier));
            Apply(tier, motion, AmbientFxCanvas.Env.AllowParticles);
        }

        internal void Apply(SparkTier tier, SparkMotion motion, bool particlesAllowed)
        {
            Tier = tier; Motion = motion; ParticlesAllowed = particlesAllowed;
            _field.Configure(tier, motion, particlesAllowed);
            Paint();
            var tip = Loc.Get(PremiumSparkRules.TooltipKey(tier));
            ToolTip.SetTip(this, tip);
            AutomationProperties.SetName(this, tip);
            RestartLoops();
        }

        private void Paint()
        {
            var l = PremiumSparkRules.LiveryFor(Tier);
            Opacity = PremiumSparkRules.Opacity(Tier);
            _face = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0.2, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(0.8, 1, RelativeUnit.Relative),
                GradientStops = { new(C(l.FaceTop), 0), new(C(l.FaceMid), 0.48), new(C(l.FaceBottom), 1) },
            };
            _edge = new SolidColorBrush(C(l.Edge));
            var dark = ShadowInk(C(l.FaceBottom));
            _shadow = new SolidColorBrush(dark);
            _ink = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                GradientStops = { new(C(l.FaceMid), 0), new(C(l.FaceBottom), 1) },
            };
            BuildFolds(dark);
            _motes = Tier == SparkTier.Basic
                ? new IBrush[] { new SolidColorBrush(Color.FromRgb(0xFF, 0xD2, 0x4A)), Brushes.White, new SolidColorBrush(Color.FromRgb(0xFF, 0xB0, 0x22)) }
                : new IBrush[] { new SolidColorBrush(C(l.Mote)), Brushes.White, new SolidColorBrush(C(l.Ink)) };
            _flare = new RadialGradientBrush { GradientStops = { new(Colors.White, 0), new(Colors.White, 0.35), new(C(l.Ink), 1) } };
            _halo = new RadialGradientBrush
            {
                GradientStops =
                {
                    new(Color.FromArgb(0xA0, 0x7F, 0xE9, 0xFF), 0), new(Color.FromArgb(0x55, 0x5E, 0xE6, 0xFF), 0.45),
                    new(Color.FromArgb(0x00, 0x5E, 0xE6, 0xFF), 1),
                },
            };
            BuildLabel();
            SettleDepth(animate: false);
            InvalidateVisual();
        }

        /// <summary>WPF BuildFolds: eight facets meeting at a raised heart, shaded by the lamp.</summary>
        private void BuildFolds(Color dark)
        {
            _facets.Clear();
            var heart = P(PremiumSparkRules.Heart);
            for (int k = 0; k < 4; k++)
            {
                var flank = PremiumSparkRules.Flanks[k];
                var (h1, h2) = PremiumSparkRules.Split(flank, 0.5);
                AddFacet(heart, h1, PremiumSparkRules.FacetShade(flank.p0, h1.Item4), dark);
                AddFacet(heart, h2, PremiumSparkRules.FacetShade(h1.Item4, flank.p3), dark);
            }
            _valleyPen = new Pen(new SolidColorBrush(Color.FromArgb(0x55, dark.R, dark.G, dark.B)), 0.5, lineCap: PenLineCap.Round);
        }

        private void AddFacet(Point heart, (SparkPoint p0, SparkPoint c1, SparkPoint c2, SparkPoint p3) half, double shade, Color dark)
        {
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                c.BeginFigure(heart, true);
                c.LineTo(P(half.p0));
                c.CubicBezierTo(P(half.c1), P(half.c2), P(half.p3));
                c.EndFigure(true);
            }
            var colour = shade >= 0
                ? Color.FromArgb((byte)Math.Round(Math.Min(1, shade) * 0x78), 0xFF, 0xFF, 0xFF)
                : Color.FromArgb((byte)Math.Round(Math.Min(1, -shade) * 0x68), dark.R, dark.G, dark.B);
            _facets.Add((g, new SolidColorBrush(colour)));
        }

        /// <summary>WPF BuildLabel/BuildTrackedText: letter-spaced by hand, shrunk to fit the card.</summary>
        private void BuildLabel()
        {
            var text = Loc.Get("premium_spark_label");
            if (string.IsNullOrWhiteSpace(text) || text == "premium_spark_label") text = "PREMIUM";
            var typeface = new Typeface(new FontFamily("Segoe UI Black, Segoe UI"), FontStyle.Normal, FontWeight.Black);
            var group = new GeometryGroup { FillRule = FillRule.NonZero };
            double x = 0;
            foreach (var ch in text.ToUpper(CultureInfo.CurrentCulture))
            {
                var ft = new FormattedText(ch.ToString(), CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                    typeface, PremiumSparkRules.LabelSize, Brushes.Black);
                if (ft.BuildGeometry(new Point(x, 0)) is { } g) group.Children.Add(g);
                x += ft.WidthIncludingTrailingWhitespace + PremiumSparkRules.LabelTracking;
            }
            var b = group.Bounds;
            if (b.Width <= 0) { _label = null; return; }
            var maxWidth = W - 4;
            var scale = b.Width > maxWidth ? maxWidth / b.Width : 1.0;
            group.Transform = new MatrixTransform(Matrix.CreateTranslation(-b.X, -b.Y) * Matrix.CreateScale(scale, scale)
                * Matrix.CreateTranslation((W - b.Width * scale) / 2, LabelTop));
            _label = group;
        }

        // ============================ depth: hover, press ============================

        protected override void OnPointerEntered(PointerEventArgs e)
        {
            base.OnPointerEntered(e);
            _hovered = true;
            SettleDepth(animate: true);
            if (PremiumSparkRules.Wobble(Tier, Motion) && AmbientFxCanvas.Env.AllowTransitions) { _wobbleStart = Now; EnsureClock(); }
        }

        protected override void OnPointerExited(PointerEventArgs e)
        {
            base.OnPointerExited(e);
            _hovered = false; _pressed = false;
            SettleDepth(animate: true);
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            _pressed = true; e.Handled = true;
            SettleDepth(animate: true);
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);
            if (!_pressed) return;
            _pressed = false; e.Handled = true;
            SettleDepth(animate: true);
            if (new Rect(Bounds.Size).Contains(e.GetPosition(this))) OnClick();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.Key is Key.Enter or Key.Space) { e.Handled = true; OnClick(); }
        }

        internal void OnClick()
        {
            Burst();
            Click?.Invoke(this, new RoutedEventArgs());
        }

        private void SettleDepth(bool animate)
        {
            double travel = PremiumSparkRules.Travel(_pressed, _hovered), shadow = PremiumSparkRules.ShadowLength(_pressed, _hovered);
            var (ct, cs) = CurrentDepth();
            _travelTo = travel; _shadowTo = shadow;
            if (!animate || Motion == SparkMotion.Off || !AmbientFxCanvas.Env.AllowTransitions || !_attached)
            {
                _travelFrom = travel; _shadowFrom = shadow; _settleStart = -1;
            }
            else
            {
                _travelFrom = ct; _shadowFrom = cs; _settleStart = Now;
                _settleMs = _pressed ? PremiumSparkRules.PressMs : PremiumSparkRules.HoverMs;
                EnsureClock();
            }
            InvalidateVisual();
        }

        private (double travel, double shadow) CurrentDepth()
        {
            if (_settleStart < 0) return (_travelTo, _shadowTo);
            var k = Math.Clamp((Now - _settleStart) * 1000 / _settleMs, 0, 1);
            if (k >= 1) { _settleStart = -1; return (_travelTo, _shadowTo); }
            const double a = 0.35;   // BackEase EaseOut
            double u = 1 - k, e = 1 - (u * u * u - u * a * Math.Sin(Math.PI * u));
            return (_travelFrom + (_travelTo - _travelFrom) * e, _shadowFrom + (_shadowTo - _shadowFrom) * e);
        }

        /// <summary>WPF Wobble keyframes: +w at 22%, -0.6w at 50%, +0.25w at 75%, rest at 100%.</summary>
        private double WobbleAngle()
        {
            if (_wobbleStart < 0) return 0;
            var k = (Now - _wobbleStart) * 1000 / PremiumSparkRules.WobbleMs;
            if (k >= 1) { _wobbleStart = -1; return 0; }
            double w = PremiumSparkRules.WobbleDegrees;
            ReadOnlySpan<double> at = stackalloc double[] { 0, 0.22, 0.5, 0.75, 1 };
            ReadOnlySpan<double> v = stackalloc double[] { 0, w, -w * 0.6, w * 0.25, 0 };
            for (int i = 1; i < at.Length; i++)
                if (k <= at[i]) return v[i - 1] + (v[i] - v[i - 1]) * (k - at[i - 1]) / (at[i] - at[i - 1]);
            return 0;
        }

        // ============================ the clock ============================

        private double Now => _watch.Elapsed.TotalSeconds;

        private bool Parked => !_attached || !IsEffectivelyVisible || _window?.WindowState == WindowState.Minimized;

        private void UpdateParking()
        {
            if (Parked) StopLoops();
            else if (!LoopsRunning) StartLoops();
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
            _t = _rng.NextDouble() * 1.2;   // start mid-stream, never a bare card
            Prewarm(1.2);
            EnsureClock();
        }

        private void EnsureClock()
        {
            if (_clock != null || Parked) return;
            if (!_watch.IsRunning) _watch.Restart();
            _last = Now;
            _clock = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000.0 / PremiumSparkRules.FrameRate) };
            _clock.Tick += OnTick;
            _clock.Start();
        }

        private void StopLoops()
        {
            if (_clock != null) { _clock.Stop(); _clock.Tick -= OnTick; _clock = null; }
            _field.Clear();
            _settleStart = -1; _wobbleStart = -1;
            InvalidateVisual();
        }

        private void OnTick(object? sender, EventArgs e)
        {
            var now = Now;
            var dt = now - _last;
            _last = now;
            if (PremiumSparkRules.Clock(Tier, Motion)) Advance(dt);
            else if (_settleStart < 0 && _wobbleStart < 0) StopLoops();
            else InvalidateVisual();
        }

        internal void Prewarm(double seconds)
        {
            const double step = 1.0 / PremiumSparkRules.FrameRate;
            for (double s = 0; s < seconds; s += step) _field.Step(step);
        }

        /// <summary>Moves everything by dt seconds (the clock calls it; tests call it by hand).</summary>
        internal void Advance(double dt)
        {
            if (dt < 0) dt = 0;
            _t += dt;
            _field.Step(dt);
            InvalidateVisual();
        }

        private void Burst()
        {
            var count = PremiumSparkRules.BurstCount(Tier, Motion);
            if (count <= 0 || !AmbientFxCanvas.Env.AllowTransitions) return;
            _field.Burst(count);
            InvalidateVisual();
        }

        // ============================ lifecycle ============================

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            _attached = true;
            _window = TopLevel.GetTopLevel(this) as Window;
            if (_window != null) _window.PropertyChanged += OnWindowProperty;
            _visibilityWatch = EffectiveVisibility.Watch(this, UpdateParking);
            AmbientFxCanvas.Env.MotionGateChanged += OnGate;
            if (Platform.AccountSeed.Patreon is { } p) p.TierChanged += OnTierChanged;
            if (Platform.AccountSeed.SubscribeStar is { } s) s.TierChanged += OnTierChanged;
            LocalizationManager.Instance.LanguageChanged += OnLanguageChanged;
            _watch.Restart();
            Refresh();
            UpdateParking();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnDetachedFromVisualTree(e);
            _attached = false;
            if (_window != null) _window.PropertyChanged -= OnWindowProperty;
            _window = null;
            _visibilityWatch?.Dispose(); _visibilityWatch = null;
            AmbientFxCanvas.Env.MotionGateChanged -= OnGate;
            if (Platform.AccountSeed.Patreon is { } p) p.TierChanged -= OnTierChanged;
            if (Platform.AccountSeed.SubscribeStar is { } s) s.TierChanged -= OnTierChanged;
            LocalizationManager.Instance.LanguageChanged -= OnLanguageChanged;
            StopLoops();
            _watch.Reset();
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (change.Property == IsVisibleProperty && _attached) UpdateParking();
        }

        private void OnWindowProperty(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property == Window.WindowStateProperty) UpdateParking();
        }

        private void OnGate() => Dispatcher.UIThread.Post(Refresh);
        private void OnTierChanged(object? sender, PatreonTier tier) => Dispatcher.UIThread.Post(Refresh);
        private void OnLanguageChanged(object? sender, EventArgs e) => Dispatcher.UIThread.Post(() =>
        {
            BuildLabel();
            var tip = Loc.Get(PremiumSparkRules.TooltipKey(Tier));
            ToolTip.SetTip(this, tip);
            AutomationProperties.SetName(this, tip);
            InvalidateVisual();
        });

        // ============================ drawing ============================

        public bool HitTest(Point point) => new Rect(Bounds.Size).Contains(point);

        public override void Render(DrawingContext dc)
        {
            try { Draw(dc); }
            catch (Exception ex) { Log.Debug("Premium spark render failed: {E}", ex.Message); }
        }

        private void Draw(DrawingContext dc)
        {
            var flareGlow = LoopsRunning ? _field.FlareGlow : 0;
            var motion = LoopsRunning ? Motion : SparkMotion.Off;

            // Prime's halo: 52 px ellipse centred at (27, 15), behind the card, swelling with a flare.
            if (PremiumSparkRules.Halo(Tier))
            {
                using (dc.PushOpacity(PremiumSparkRules.HaloAt(_t, Tier, motion, flareGlow)))
                {
                    var r = 26 * (1 + 0.14 * flareGlow);
                    dc.DrawEllipse(_halo, null, new Point(W / 2, 15), r, r);
                }
            }

            var (travel, shadow) = CurrentDepth();
            var breath = PremiumSparkRules.BreathAt(_t, Tier, motion);
            var origin = new Point(W * 0.5, H * 0.42);
            var cut = Matrix.CreateTranslation(-origin.X, -origin.Y) * Matrix.CreateScale(breath, breath)
                * Matrix.CreateRotation((PremiumSparkRules.Tilt + WobbleAngle()) * Math.PI / 180)
                * Matrix.CreateTranslation(origin.X, origin.Y + travel);
            using (dc.PushTransform(cut))
            {
                // The label: paper shadow, die-cut edge, ink face.
                if (_label != null)
                {
                    using (dc.PushTransform(Matrix.CreateTranslation(shadow * 0.3, shadow * 0.5)))
                        dc.DrawGeometry(_shadow, new Pen(_shadow, PremiumSparkRules.LabelDieCutPx, lineJoin: PenLineJoin.Round), _label);
                    dc.DrawGeometry(_edge, new Pen(_edge, PremiumSparkRules.LabelDieCutPx, lineJoin: PenLineJoin.Round), _label);
                    dc.DrawGeometry(_ink, null, _label);
                }

                using (dc.PushTransform(Matrix.CreateTranslation(StarLeft, StarTop)))
                {
                    using (dc.PushTransform(Matrix.CreateTranslation(shadow * 0.45, shadow * 0.8)))
                        dc.DrawGeometry(_shadow, new Pen(_shadow, PremiumSparkRules.DieCutPx, lineJoin: PenLineJoin.Round), Star);
                    dc.DrawGeometry(_edge, new Pen(_edge, PremiumSparkRules.DieCutPx, lineJoin: PenLineJoin.Round), Star);
                    dc.DrawGeometry(_face, new Pen(Rim, 1.3, lineJoin: PenLineJoin.Round), Star);
                    using (dc.PushGeometryClip(Star))
                    {
                        foreach (var (g, b) in _facets) dc.DrawGeometry(b, null, g);
                        var heart = P(PremiumSparkRules.Heart);
                        for (int k = 0; k < 4; k++)
                        {
                            var (h1, _) = PremiumSparkRules.Split(PremiumSparkRules.Flanks[k], 0.5);
                            dc.DrawLine(_valleyPen, heart, P(h1.Item4));
                            dc.DrawLine(Crease, heart, P(PremiumSparkRules.Flanks[k].p0));
                        }
                        dc.DrawEllipse(Spec, null, new Point(heart.X - 1.9, heart.Y - 2.6), 2.4, 2.4);
                        using (dc.PushOpacity(0.16))
                            for (double gx = 0; gx < PremiumSparkRules.StarSize; gx += 7)
                                for (double gy = 0; gy < PremiumSparkRules.StarSize; gy += 7)
                                    for (int i = 0; i < Grain.Length; i++)
                                        dc.FillRectangle(i % 2 == 0 ? GrainLight : GrainDark, Grain[i].Translate(new Vector(gx, gy)));
                        dc.DrawGeometry(Highlight, null, Star);
                        if (PremiumSparkRules.Sheen(Tier, Motion))
                        {
                            var sheen = Matrix.CreateSkew(-22 * Math.PI / 180, 0)
                                * Matrix.CreateTranslation(PremiumSparkRules.SheenAt(_t, Tier, motion), -6);
                            using (dc.PushTransform(sheen))
                                dc.FillRectangle(Band, new Rect(0, 0, 11, PremiumSparkRules.StarSize + 12));
                        }
                    }
                }
            }

            // Particles: unclipped, untransformed, spilling past the card (WPF FxLayer).
            foreach (var m in _field.Alive)
            {
                var half = m.Size / 2 * PremiumSparkField.ScaleOf(m);
                var sy = half;
                if (m.Kind == MoteKind.Glitter || (m.Kind == MoteKind.Burst && Tier == SparkTier.Basic))
                    sy = half * (0.25 + 0.75 * Math.Abs(Math.Cos(m.Phase + m.Age * 9)));
                var mx = Matrix.CreateScale(half, sy) * Matrix.CreateRotation(m.Angle * Math.PI / 180) * Matrix.CreateTranslation(m.X, m.Y);
                var brush = m.Kind == MoteKind.Flare ? _flare : _motes.Length > m.Colour && m.Colour >= 0 ? _motes[m.Colour] : Brushes.White;
                using (dc.PushOpacity(PremiumSparkField.OpacityOf(m)))
                using (dc.PushTransform(mx))
                    dc.DrawGeometry(brush, null, GeometryFor(m.Kind));
            }
        }

        private Geometry GeometryFor(MoteKind kind) => kind switch
        {
            MoteKind.Diamond or MoteKind.Glitter => UnitDiamond,
            MoteKind.Burst => Tier == SparkTier.Prime ? UnitDiamond : UnitFleck,
            _ => UnitStar,
        };

        // ============================ helpers ============================

        private readonly IBrush Rim = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            GradientStops = { new(Color.FromArgb(0xE0, 0xFF, 0xFF, 0xFF), 0), new(Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 0.52) },
        };
        private readonly IBrush Highlight = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            GradientStops = { new(Color.FromArgb(0x50, 0xFF, 0xFF, 0xFF), 0), new(Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 0.5) },
        };
        private readonly IBrush Band = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
            GradientStops = { new(Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 0), new(Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF), 0.5), new(Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 1) },
        };
        private readonly IBrush Spec = new RadialGradientBrush
        {
            GradientStops = { new(Color.FromArgb(0xC8, 0xFF, 0xFF, 0xFF), 0), new(Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 1) },
        };
        private readonly Pen Crease = new(new SolidColorBrush(Color.FromArgb(0x9A, 0xFF, 0xFF, 0xFF)), 0.55, lineCap: PenLineCap.Round);

        private static Color C(SparkColor c) => Color.FromRgb(c.R, c.G, c.B);
        private static Point P(SparkPoint p) => new(p.X, p.Y);

        /// <summary>DepthRules.ShadowColor: neutral ink pulled 28% toward the hue, at ShadowAlpha.</summary>
        internal static Color ShadowInk(Color hue)
        {
            static byte L(byte a, byte b) => (byte)Math.Round(a + (b - a) * ShadowHuePull);
            return Color.FromArgb((byte)Math.Round(ShadowAlpha * 255), L(0x06, hue.R), L(0x02, hue.G), L(0x10, hue.B));
        }

        private static Geometry MakeUnitStar()
        {
            var g = StreamGeometry.Parse(PremiumSparkRules.StarPathData);
            var half = PremiumSparkRules.StarSize / 2;
            g.Transform = new MatrixTransform(Matrix.CreateTranslation(-half, -half) * Matrix.CreateScale(1 / half, 1 / half));
            return g;
        }

        /// <summary>WPF MakeGrain: a 7 px tile of nine faint flecks, light and dark, seed 1007.</summary>
        private static Rect[] MakeGrain()
        {
            var rng = new Random(1007);
            var r = new Rect[9];
            for (int i = 0; i < r.Length; i++) r[i] = new Rect(rng.NextDouble() * 6.4, rng.NextDouble() * 6.4, 0.6, 0.6);
            return r;
        }
    }
}
