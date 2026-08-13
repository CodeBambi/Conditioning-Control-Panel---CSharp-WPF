using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Descent;

namespace ConditioningControlPanel.Controls
{
    /// <summary>
    /// THE SPIRAL GLYPH — the tiny native spiral both profile entry points wear.
    ///
    /// PURE WPF, DELIBERATELY. <see cref="SpiralEmbedView"/> (WebView2) is the real
    /// canvas and stays the real canvas; this is the 22-44px INVITATION to open it.
    /// A browser HWND at that size would cost a process, an airspace hole and a
    /// startup race for something that is three strokes and a dot — so this control
    /// is a Grid, a Path, two Ellipses and a TextBlock, and nothing else. It never
    /// touches the network and never asks DescentService for anything: the surfaces
    /// that host it feed it a block through <see cref="Apply"/>.
    ///
    /// WHAT IT DRAWS:
    ///   • an Archimedean spiral of ~2.5 turns, stroked in the MOD ACCENT,
    ///   • a glowing dot travelling that arc — devotion progress toward the next
    ///     stage (<see cref="ComputeProgress"/>, the one testable part),
    ///   • the stage numeral in the middle, borrowed wholesale from
    ///     <see cref="SpiralRailHost.StageNumeral"/> so the two surfaces cannot
    ///     drift apart. The stage NAMES are still an open owner call with no loc
    ///     keys, and this control invents none.
    ///
    /// MOD-AWARE BY CONSTRUCTION. Every accent is a
    /// <see cref="FrameworkElement.SetResourceReference"/> against an EXISTING app
    /// brush key, never a Color baked in the constructor — that is the bug class the
    /// 2026-08-13 mod-aware sweep spent six lanes on (a ctor-resolved brush is right
    /// exactly once, then wrong for the rest of the session). No new theme keys are
    /// added either: a fresh cross-dictionary StaticResource inside a Theme
    /// dictionary is the BAML EndOfStream trap.
    ///
    /// MOTION. The breathing loop is AMBIENT (a forever clock), so it is gated on
    /// <see cref="MotionFx.AllowAmbientLoops"/> — MotionLevel.Full on a tier that
    /// permits ambient motion — and re-evaluated on load, on visibility and whenever
    /// a host calls <see cref="RefreshMotion"/> (MainWindow does that from the motion
    /// combo's handler, the same way the OG border is re-armed). Reduced/Off leaves a
    /// perfectly good static glyph, which is the whole fallback.
    /// </summary>
    public sealed class SpiralGlyph : Grid
    {
        // ---- geometry (unit space: the spiral is authored inside a 0..1 square) ----

        /// <summary>Turns of the Archimedean arm. 2.5 reads as a spiral at 22px and
        /// still has room for the numeral at 44px.</summary>
        private const double Turns = 2.5;

        /// <summary>Outer radius as a fraction of the box. Leaves room for the stroke.</summary>
        private const double OuterRadius = 0.44;

        /// <summary>Points sampled along the arm. Plenty at these sizes; built ONCE.</summary>
        private const int Samples = 180;

        private const double TotalAngle = Turns * 2 * Math.PI;

        /// <summary>Rendered stroke width in DIPs, held constant across sizes.</summary>
        private const double StrokePx = 1.6;

        /// <summary>Below this width the numeral is dropped — at menu-icon size a Roman
        /// numeral is three grey pixels, and three grey pixels are worse than none.</summary>
        internal const double NumeralMinWidth = 28;

        /// <summary>The spiral arm in unit space. One frozen geometry for the whole app.</summary>
        private static readonly StreamGeometry UnitSpiral = BuildUnitSpiral();

        // ---- parts ----

        private readonly Canvas _host;
        private readonly Path _arm;
        private readonly Ellipse _dotGlow;
        private readonly Ellipse _dot;
        private readonly TextBlock _numeral;
        private readonly ScaleTransform _breath;

        private double _progress;
        private bool _hasBlock;
        private bool _breathing;

        public SpiralGlyph()
        {
            Width = 44;
            Height = 44;

            _breath = new ScaleTransform(1, 1);
            RenderTransformOrigin = new Point(0.5, 0.5);
            RenderTransform = _breath;

            _arm = new Path
            {
                Data = UnitSpiral,
                StrokeThickness = StrokePx,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                IsHitTestVisible = false,
                Opacity = 0.85,
            };
            _arm.SetResourceReference(Shape.StrokeProperty, "PinkBrush");

            // The "glow" is a second, larger, translucent disc rather than a
            // DropShadowEffect: an effect's Color is a plain DP with no resource
            // reference, so it could not follow a mod switch — and a blur at 22px
            // costs a render pass for a halo nobody can resolve.
            _dotGlow = new Ellipse { IsHitTestVisible = false, Opacity = 0.30 };
            _dotGlow.SetResourceReference(Shape.FillProperty, "PinkBrush");

            _dot = new Ellipse { IsHitTestVisible = false };
            _dot.SetResourceReference(Shape.FillProperty, "PinkVibrantBrush");

            _host = new Canvas
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false,
            };
            _host.Children.Add(_arm);
            _host.Children.Add(_dotGlow);
            _host.Children.Add(_dot);
            Children.Add(_host);

            _numeral = new TextBlock
            {
                Text = "·",
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false,
            };
            _numeral.SetResourceReference(TextBlock.ForegroundProperty, "PinkSoftBrush");
            Children.Add(_numeral);

            Loaded += (_, _) => RefreshMotion();
            Unloaded += (_, _) => StopBreath();
            IsVisibleChanged += (_, _) => RefreshMotion();
            SizeChanged += (_, _) => Relayout();
        }

        // ============================== the maths ==============================

        /// <summary>
        /// Devotion progress toward the NEXT stage, 0..1 — the only number this
        /// control computes and the only part worth a unit test.
        ///
        /// A null or non-positive <paramref name="nextAt"/> means there is no rung
        /// above this one (the server sends no threshold on the final stage), and the
        /// honest reading of "no further to go" is a full arc, not an empty one.
        /// Everything else is days/threshold clamped to 0..1: garbage from the wire
        /// lands on an end of the arc rather than off the control.
        /// </summary>
        internal static double ComputeProgress(int devotionDays, int? nextAt)
        {
            if (nextAt is null || nextAt <= 0) return 1.0;
            if (devotionDays <= 0) return 0.0;
            double p = devotionDays / (double)nextAt.Value;
            return p < 0 ? 0.0 : p > 1.0 ? 1.0 : p;
        }

        /// <summary>The unit-space point at fraction <paramref name="t"/> along the arm.</summary>
        private static Point PointAt(double t)
        {
            double angle = t * TotalAngle;
            double r = OuterRadius * t;
            return new Point(0.5 + r * Math.Cos(angle), 0.5 + r * Math.Sin(angle));
        }

        private static StreamGeometry BuildUnitSpiral()
        {
            var geo = new StreamGeometry();
            using (var ctx = geo.Open())
            {
                ctx.BeginFigure(PointAt(0), isFilled: false, isClosed: false);
                for (int i = 1; i <= Samples; i++)
                    ctx.LineTo(PointAt(i / (double)Samples), isStroked: true, isSmoothJoin: true);
            }
            geo.Freeze();
            return geo;
        }

        // ============================== the apply ==============================

        /// <summary>
        /// Fold a descent block into the glyph. A null block is the legal, common
        /// case (every account outside the server's rollout dial) and draws the bare
        /// pre-begin state: the dot retires and the numeral reads "·".
        /// </summary>
        public void Apply(DescentBlock? block)
        {
            try
            {
                _hasBlock = block is not null;
                _numeral.Text = SpiralRailHost.StageNumeral(block?.Stage?.N ?? 0);
                _progress = _hasBlock
                    ? ComputeProgress(block!.DevotionDays, block.Stage?.NextAt)
                    : 0.0;
                Relayout();
            }
            catch (Exception ex) { App.Logger?.Debug("[Spiral] glyph apply: {E}", ex.Message); }
        }

        // ============================== layout ==============================

        /// <summary>
        /// Scale everything to the current box. The arm's geometry is authored once in
        /// unit space and scaled by a RenderTransform, which also scales the pen — so
        /// StrokeThickness is divided back out and the stroke stays <see cref="StrokePx"/>
        /// wide at every size.
        /// </summary>
        private void Relayout()
        {
            try
            {
                double side = Math.Min(ActualWidth, ActualHeight);
                if (side <= 0 || double.IsNaN(side)) return;

                _host.Width = side;
                _host.Height = side;
                _arm.RenderTransform = new ScaleTransform(side, side);
                _arm.StrokeThickness = StrokePx / side;

                double dotSize = Math.Max(2.5, side * 0.10);
                double glowSize = dotSize * 2.2;
                var p = PointAt(_progress);
                double cx = p.X * side;
                double cy = p.Y * side;

                _dot.Width = _dot.Height = dotSize;
                Canvas.SetLeft(_dot, cx - dotSize / 2);
                Canvas.SetTop(_dot, cy - dotSize / 2);

                _dotGlow.Width = _dotGlow.Height = glowSize;
                Canvas.SetLeft(_dotGlow, cx - glowSize / 2);
                Canvas.SetTop(_dotGlow, cy - glowSize / 2);

                var dotVis = _hasBlock ? Visibility.Visible : Visibility.Collapsed;
                _dot.Visibility = dotVis;
                _dotGlow.Visibility = dotVis;

                _numeral.FontSize = Math.Max(7, Math.Round(side * 0.30));
                _numeral.Visibility = side >= NumeralMinWidth ? Visibility.Visible : Visibility.Collapsed;
            }
            catch (Exception ex) { App.Logger?.Debug("[Spiral] glyph layout: {E}", ex.Message); }
        }

        // ============================== motion ==============================

        /// <summary>
        /// Start or stop the breathing loop against the live motion gate. Public
        /// because there is no app-wide "motion level changed" event: the hosts call
        /// this from the same choke point that re-arms every other ambient loop
        /// (MainWindow.UiUpdates.CmbMotionLevel_SelectionChanged).
        /// </summary>
        public void RefreshMotion()
        {
            try
            {
                bool wanted = IsVisible && IsLoaded && MotionFx.AllowAmbientLoops;
                if (!wanted) { StopBreath(); return; }
                if (_breathing) return;
                _breathing = true;

                var breathe = new DoubleAnimation(1.0, 1.05, TimeSpan.FromSeconds(2.6))
                {
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
                };
                Timeline.SetDesiredFrameRate(breathe, 24);
                _breath.BeginAnimation(ScaleTransform.ScaleXProperty, breathe);
                _breath.BeginAnimation(ScaleTransform.ScaleYProperty, breathe);
            }
            catch (Exception ex) { App.Logger?.Debug("[Spiral] glyph motion: {E}", ex.Message); }
        }

        private void StopBreath()
        {
            if (!_breathing) return;
            _breathing = false;
            try
            {
                _breath.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                _breath.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                _breath.ScaleX = _breath.ScaleY = 1.0;
            }
            catch (Exception ex) { App.Logger?.Debug("[Spiral] glyph stop: {E}", ex.Message); }
        }
    }
}
