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
    /// The header Premium spark: a cardstock four-point sparkle with PREMIUM cut out under it.
    /// Free reads dim and still, Basic is gold with a sheen and glints, Prime is ice cyan with a
    /// pulsing halo and diamond motes. Rules and numbers live in <see cref="PremiumSparkRules"/>;
    /// this class only draws them.
    ///
    /// <para><b>Cheap by construction.</b> Every loop is a WPF timeline on a transform or an
    /// opacity of a 54 x 42 card (capped at 30 fps), the two random clocks (glints, motes) are
    /// DispatcherTimers that spawn one tiny shape each, and the motes are capped at six alive.
    /// Everything parks while the spark is not visible or its window is minimised.</para>
    /// </summary>
    public partial class PremiumSpark : Button
    {
        private const double StarLeft = (PremiumSparkRules.ControlWidth - PremiumSparkRules.StarSize) / 2;
        private const double StarTop = 0;
        private const double LabelTop = 34;

        private static readonly Geometry StarGeometry = MakeFrozen(Geometry.Parse(PremiumSparkRules.StarPathData));
        private static readonly Brush GrainBrush = MakeGrain();

        private readonly Random _rng = new();
        private readonly Path _shadowStar = new();
        private readonly Path _dieCutStar = new();
        private readonly Path _faceStar = new();
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

        private DispatcherTimer? _glintClock;
        private DispatcherTimer? _moteClock;
        private int _motesAlive;
        private Window? _window;
        private bool _loopsRunning;
        private bool _hovered;

        /// <summary>What the spark is showing right now (tests read it).</summary>
        internal SparkTier Tier { get; private set; } = SparkTier.Free;
        internal SparkMotion Motion { get; private set; } = SparkMotion.Off;
        internal bool ParticlesAllowed { get; private set; }

        /// <summary>True while the ambient loops are running (tests and parking read it).</summary>
        internal bool LoopsRunning => _loopsRunning;

        /// <summary>When set, Refresh() uses these instead of App's gates (offscreen renders).</summary>
        internal bool Pinned { get; set; }

        public PremiumSpark()
        {
            InitializeComponent();
            BuildCutout();
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
            IsVisibleChanged += (_, _) => UpdateParking();
            Apply(SparkTier.Free, SparkMotion.Off, false);
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
            Apply(tier, motion, MotionFx.AllowParticles);
        }

        /// <summary>Paints one state. Internal so offscreen renders can pin a look.</summary>
        internal void Apply(SparkTier tier, SparkMotion motion, bool particlesAllowed)
        {
            bool changed = tier != Tier || motion != Motion || particlesAllowed != ParticlesAllowed;
            Tier = tier;
            Motion = motion;
            ParticlesAllowed = particlesAllowed;

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
                    new GradientStop(livery.FaceTop, 0.0),
                    new GradientStop(livery.FaceMid, 0.48),
                    new GradientStop(livery.FaceBottom, 1.0),
                },
            };
            face.Freeze();
            _faceStar.Fill = face;

            var edge = new SolidColorBrush(livery.Edge);
            edge.Freeze();
            _dieCutStar.Fill = edge;
            _dieCutStar.Stroke = edge;
            _dieCutLabel.Fill = edge;
            _dieCutLabel.Stroke = edge;

            // The paper shadow: ink pulled toward the tier colour, the DepthRules recipe.
            var shadow = new SolidColorBrush(Depth.DepthRules.ShadowColor(livery.FaceBottom));
            shadow.Freeze();
            _shadowStar.Fill = shadow;
            _shadowStar.Stroke = shadow;
            _shadowLabel.Fill = shadow;
            _shadowLabel.Stroke = shadow;

            var ink = new LinearGradientBrush(livery.FaceMid, livery.FaceBottom, 90);
            ink.Freeze();
            _faceLabel.Fill = ink;

            // Prime's halo: drawn at every motion level, pulsing only at Full.
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
                Halo.BeginAnimation(OpacityProperty, null);
                Halo.Opacity = PremiumSparkRules.HaloStill;
            }
            else
            {
                Halo.BeginAnimation(OpacityProperty, null);
                Halo.Opacity = 0;
            }

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

            _grainStar.Fill = GrainBrush;
            _grainStar.Opacity = 0.18;

            // The lamp is top-left: a second cut of the same star, white fading out by 55%,
            // lifts the upper-left of the card like light on a paper fold.
            var highlight = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 1),
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(0x8C, 0xFF, 0xFF, 0xFF), 0.0),
                    new GradientStop(Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 0.55),
                },
            };
            highlight.Freeze();
            _highlightStar.Fill = highlight;

            // The sheen: a skewed light band, clipped to the star, slid across by the loop.
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
            Cutout.Children.Add(_grainStar);
            Cutout.Children.Add(_highlightStar);
            Cutout.Children.Add(_sheenHost);
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
        //  ambient loops
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
            var tier = Tier;
            var motion = Motion;

            if (PremiumSparkRules.Sheen(tier, motion))
            {
                var cycle = PremiumSparkRules.SheenCycleSec(tier, motion);
                var pass = PremiumSparkRules.SheenPassFor(motion);
                var sweep = new DoubleAnimationUsingKeyFrames
                {
                    Duration = TimeSpan.FromSeconds(cycle),
                    RepeatBehavior = RepeatBehavior.Forever,
                    BeginTime = TimeSpan.FromSeconds(_rng.NextDouble() * 1.2),
                };
                sweep.KeyFrames.Add(new DiscreteDoubleKeyFrame(-20, KeyTime.FromTimeSpan(TimeSpan.Zero)));
                sweep.KeyFrames.Add(new EasingDoubleKeyFrame(PremiumSparkRules.StarSize + 12,
                    KeyTime.FromTimeSpan(TimeSpan.FromSeconds(pass)),
                    new SineEase { EasingMode = EasingMode.EaseInOut }));
                sweep.KeyFrames.Add(new DiscreteDoubleKeyFrame(-20, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(pass + 0.01))));
                Timeline.SetDesiredFrameRate(sweep, PremiumSparkRules.FrameRate);
                _sheenShift.BeginAnimation(TranslateTransform.XProperty, sweep);
            }

            if (PremiumSparkRules.Breath(tier, motion))
            {
                var breath = new DoubleAnimation(1.0, PremiumSparkRules.BreathScale,
                    TimeSpan.FromSeconds(PremiumSparkRules.BreathHalfSec))
                {
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
                };
                Timeline.SetDesiredFrameRate(breath, PremiumSparkRules.FrameRate);
                BreathScale.BeginAnimation(ScaleTransform.ScaleXProperty, breath);
                BreathScale.BeginAnimation(ScaleTransform.ScaleYProperty, breath);
            }

            if (PremiumSparkRules.HaloPulse(tier, motion))
            {
                var pulse = new DoubleAnimation(PremiumSparkRules.HaloLow, PremiumSparkRules.HaloHigh,
                    TimeSpan.FromSeconds(PremiumSparkRules.HaloHalfSec))
                {
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
                };
                Timeline.SetDesiredFrameRate(pulse, PremiumSparkRules.FrameRate);
                Halo.BeginAnimation(OpacityProperty, pulse);
            }

            if (PremiumSparkRules.Glints(tier, motion))
            {
                _glintClock = new DispatcherTimer(DispatcherPriority.Background)
                {
                    Interval = TimeSpan.FromSeconds(PremiumSparkRules.Between(_rng,
                        PremiumSparkRules.GlintMinSec, PremiumSparkRules.GlintMaxSec)),
                };
                _glintClock.Tick += (_, _) =>
                {
                    Glint();
                    if (_glintClock != null)
                        _glintClock.Interval = TimeSpan.FromSeconds(PremiumSparkRules.Between(_rng,
                            PremiumSparkRules.GlintMinSec, PremiumSparkRules.GlintMaxSec));
                };
                _glintClock.Start();
            }

            if (PremiumSparkRules.Motes(tier, motion, ParticlesAllowed))
            {
                _moteClock = new DispatcherTimer(DispatcherPriority.Background)
                {
                    Interval = TimeSpan.FromMilliseconds(PremiumSparkRules.MoteGapMinMs),
                };
                _moteClock.Tick += (_, _) =>
                {
                    Mote();
                    if (_moteClock != null)
                        _moteClock.Interval = TimeSpan.FromMilliseconds(PremiumSparkRules.Between(_rng,
                            PremiumSparkRules.MoteGapMinMs, PremiumSparkRules.MoteGapMaxMs));
                };
                _moteClock.Start();
            }

            _loopsRunning = true;
        }

        private void StopLoops()
        {
            _glintClock?.Stop();
            _glintClock = null;
            _moteClock?.Stop();
            _moteClock = null;
            _sheenShift.BeginAnimation(TranslateTransform.XProperty, null);
            _sheenShift.X = -20;
            BreathScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            BreathScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            BreathScale.ScaleX = BreathScale.ScaleY = 1.0;
            Halo.BeginAnimation(OpacityProperty, null);
            Halo.Opacity = PremiumSparkRules.Halo(Tier) ? PremiumSparkRules.HaloStill : 0;
            FxLayer.Children.Clear();
            _motesAlive = 0;
            _loopsRunning = false;
        }

        // =======================================================================================
        //  particles
        // =======================================================================================

        private static readonly Point[] GlintSpots =
        {
            new(15, 2), new(27, 15), new(15, 28), new(3, 15), new(19, 11), new(11, 19),
        };

        /// <summary>A small white four-point twinkle that pops on a point of the card and goes.</summary>
        private void Glint()
        {
            var spot = GlintSpots[_rng.Next(GlintSpots.Length)];
            var twinkle = new Path
            {
                Data = StarGeometry,
                Fill = Brushes.White,
                Width = 9,
                Height = 9,
                Stretch = Stretch.Uniform,
                IsHitTestVisible = false,
                RenderTransformOrigin = new Point(0.5, 0.5),
            };
            var scale = new ScaleTransform(0, 0);
            twinkle.RenderTransform = new TransformGroup { Children = { scale, new RotateTransform(_rng.Next(0, 45)) } };
            Canvas.SetLeft(twinkle, StarLeft + spot.X - 4.5);
            Canvas.SetTop(twinkle, StarTop + spot.Y - 4.5);
            FxLayer.Children.Add(twinkle);

            var pop = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(520) };
            pop.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromPercent(0.4), new BackEase { EasingMode = EasingMode.EaseOut }));
            pop.KeyFrames.Add(new EasingDoubleKeyFrame(0.0, KeyTime.FromPercent(1.0)));
            Timeline.SetDesiredFrameRate(pop, PremiumSparkRules.FrameRate);
            pop.Completed += (_, _) => FxLayer.Children.Remove(twinkle);
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
        }

        /// <summary>A diamond mote that leaves the card and drifts up and out, fading.</summary>
        private void Mote()
        {
            if (_motesAlive >= PremiumSparkRules.MaxMotes) return;
            var livery = PremiumSparkRules.LiveryFor(Tier);
            var size = 2.5 + _rng.NextDouble() * 2.5;
            var diamond = MakeDiamond(size, _rng.Next(3) == 0 ? Colors.White : livery.Mote);
            var cx = StarLeft + 15 + (_rng.NextDouble() - 0.5) * 16;
            var cy = StarTop + 15 + (_rng.NextDouble() - 0.5) * 12;
            Canvas.SetLeft(diamond, cx - size / 2);
            Canvas.SetTop(diamond, cy - size);
            var shift = new TranslateTransform();
            diamond.RenderTransform = shift;
            FxLayer.Children.Add(diamond);
            _motesAlive++;

            var life = TimeSpan.FromMilliseconds(PremiumSparkRules.Between(_rng,
                PremiumSparkRules.MoteLifeMinMs, PremiumSparkRules.MoteLifeMaxMs));
            var side = (cx - (StarLeft + 15)) >= 0 ? 1 : -1;
            var dx = new DoubleAnimation(0, side * (6 + _rng.NextDouble() * 12), life) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
            var dy = new DoubleAnimation(0, -(8 + _rng.NextDouble() * 12), life);
            var fade = new DoubleAnimationUsingKeyFrames { Duration = life };
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(1.0, KeyTime.FromPercent(0.2)));
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(0.0, KeyTime.FromPercent(1.0)));
            foreach (var t in new Timeline[] { dx, dy, fade }) Timeline.SetDesiredFrameRate(t, PremiumSparkRules.FrameRate);
            fade.Completed += (_, _) =>
            {
                if (FxLayer.Children.Contains(diamond))
                {
                    FxLayer.Children.Remove(diamond);
                    _motesAlive = Math.Max(0, _motesAlive - 1);
                }
            };
            diamond.Opacity = 0;
            shift.BeginAnimation(TranslateTransform.XProperty, dx);
            shift.BeginAnimation(TranslateTransform.YProperty, dy);
            diamond.BeginAnimation(OpacityProperty, fade);
        }

        /// <summary>The click burst: diamonds (Prime) or gold flecks (Basic) thrown out radially.</summary>
        private void Burst()
        {
            var count = PremiumSparkRules.BurstCount(Tier, Motion);
            if (count <= 0 || !MotionFx.AllowTransitions) return;
            var livery = PremiumSparkRules.LiveryFor(Tier);
            var cx = StarLeft + 15;
            var cy = StarTop + 15;
            for (int i = 0; i < count; i++)
            {
                var angle = (Math.PI * 2 * i / count) + _rng.NextDouble() * 0.4;
                var dist = 14 + _rng.NextDouble() * 12;
                var size = Tier == SparkTier.Prime ? 3 + _rng.NextDouble() * 2.5 : 2 + _rng.NextDouble() * 2;
                FrameworkElement bit = Tier == SparkTier.Prime
                    ? MakeDiamond(size, i % 3 == 0 ? Colors.White : livery.Mote)
                    : new Ellipse { Width = size, Height = size, Fill = new SolidColorBrush(i % 3 == 0 ? Colors.White : livery.FaceMid), IsHitTestVisible = false };
                Canvas.SetLeft(bit, cx - size / 2);
                Canvas.SetTop(bit, cy - size / 2);
                var shift = new TranslateTransform();
                bit.RenderTransform = shift;
                FxLayer.Children.Add(bit);

                var life = TimeSpan.FromMilliseconds(420 + _rng.Next(160));
                var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
                var dx = new DoubleAnimation(0, Math.Cos(angle) * dist, life) { EasingFunction = ease };
                var dy = new DoubleAnimation(0, Math.Sin(angle) * dist, life) { EasingFunction = ease };
                var fade = new DoubleAnimation(1, 0, life) { BeginTime = TimeSpan.FromMilliseconds(80) };
                fade.Completed += (_, _) => FxLayer.Children.Remove(bit);
                shift.BeginAnimation(TranslateTransform.XProperty, dx);
                shift.BeginAnimation(TranslateTransform.YProperty, dy);
                bit.BeginAnimation(OpacityProperty, fade);
            }
        }

        private static Path MakeDiamond(double size, Color colour)
        {
            var fill = new SolidColorBrush(colour);
            fill.Freeze();
            return new Path
            {
                Data = MakeFrozen(new PathGeometry(new[]
                {
                    new PathFigure(new Point(size / 2, 0), new PathSegment[]
                    {
                        new LineSegment(new Point(size, size), true),
                        new LineSegment(new Point(size / 2, size * 2), true),
                        new LineSegment(new Point(0, size), true),
                    }, true),
                })),
                Fill = fill,
                IsHitTestVisible = false,
            };
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
