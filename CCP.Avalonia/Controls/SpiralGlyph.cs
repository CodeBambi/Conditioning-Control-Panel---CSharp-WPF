using System;
using System.Threading;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Styling;
using ConditioningControlPanel.Services.Descent;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Controls
{
    /// <summary>
    /// THE SPIRAL GLYPH — the tiny native spiral both profile entry points wear. Ported from the
    /// WPF head's <c>Controls/SpiralGlyph.cs</c>; a code-only control there, a code-only control here.
    ///
    /// PURE VECTOR, DELIBERATELY. <c>SpiralEmbedView</c> (WebView2) is the real canvas and stays
    /// the real canvas; this is the 22-44px INVITATION to open it. A browser surface at that size
    /// would cost a process, an airspace hole and a startup race for something that is three
    /// strokes and a dot — so this control is a Grid, a Path, two Ellipses and a TextBlock, and
    /// nothing else. It never touches the network and never asks DescentService for anything: the
    /// surfaces that host it feed it a block through <see cref="Apply"/>.
    ///
    /// WHAT IT DRAWS:
    ///   • an Archimedean spiral of ~2.5 turns, stroked in the MOD ACCENT,
    ///   • a glowing dot travelling that arc — devotion progress toward the next
    ///     stage (<see cref="ComputeProgress"/>, the one testable part),
    ///   • the stage numeral in the middle (<see cref="StageNumeral"/>). The stage
    ///     NAMES are still an open owner call with no loc keys, and this control
    ///     invents none.
    ///
    /// MOD-AWARE BY CONSTRUCTION. Every accent is a <c>DynamicResource</c> against an EXISTING app
    /// brush key (WPF's <c>SetResourceReference</c>), never a Color baked in the constructor — that
    /// is the bug class the 2026-08-13 mod-aware sweep spent six lanes on (a ctor-resolved brush is
    /// right exactly once, then wrong for the rest of the session). No new theme keys are added
    /// either.
    ///
    /// <para><b>Deviations from the WPF original:</b></para>
    /// <list type="bullet">
    ///   <item>ponytail: needs <c>MotionFx.AllowAmbientLoops</c> (the reduced-motion gate, still in
    ///     the WPF head), so <see cref="RefreshMotion"/> currently only asks whether the glyph is
    ///     loaded and visible and the breath always runs. Restore the gate when MotionFx moves to
    ///     Core — the call site is already there, and the hosts already call this method.</item>
    ///   <item>ponytail: <c>SpiralRailHost.StageNumeral</c> is a WPF-head control's static, so the
    ///     numeral table is copied into <see cref="StageNumeral"/> verbatim. Delete the copy and
    ///     call the shared one when the rail host lands on this head or in Core — the two must not
    ///     drift apart.</item>
    ///   <item>WPF <c>Timeline.SetDesiredFrameRate(24)</c> has no Avalonia twin; the breath runs at
    ///     the compositor's rate.</item>
    /// </list>
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

        /// <summary>Seconds per breath, and how far it swells.</summary>
        private const double BreathSeconds = 2.6;
        private const double BreathScale = 1.05;

        /// <summary>The spiral arm in unit space. One geometry for the whole app.</summary>
        private static readonly StreamGeometry UnitSpiral = BuildUnitSpiral();

        // ---- parts ----

        private readonly Canvas _host;
        private readonly Path _arm;
        private readonly Ellipse _dotGlow;
        private readonly Ellipse _dot;
        private readonly TextBlock _numeral;
        private readonly ScaleTransform _breath;
        private readonly ScaleTransform _armScale = new(1, 1);

        private CancellationTokenSource? _breathClock;
        private double _progress;
        private bool _hasBlock;
        private bool _breathing;

        public SpiralGlyph()
        {
            Width = 44;
            Height = 44;

            _breath = new ScaleTransform(1, 1);
            RenderTransformOrigin = RelativePoint.Center;
            RenderTransform = _breath;

            _arm = new Path
            {
                Data = UnitSpiral,
                StrokeThickness = StrokePx,
                StrokeLineCap = PenLineCap.Round,
                IsHitTestVisible = false,
                Opacity = 0.85,
                // WPF's RenderTransformOrigin defaults to the top-left; Avalonia's defaults to the
                // CENTRE, which would scale the unit-space arm away from the canvas origin.
                RenderTransformOrigin = RelativePoint.TopLeft,
                RenderTransform = _armScale,
            };
            _arm[!Shape.StrokeProperty] = new DynamicResourceExtension("PinkBrush");

            // The "glow" is a second, larger, translucent disc rather than a DropShadowEffect: an
            // effect's Color is a plain property with no resource reference, so it could not follow
            // a mod switch — and a blur at 22px costs a render pass for a halo nobody can resolve.
            _dotGlow = new Ellipse { IsHitTestVisible = false, Opacity = 0.30 };
            _dotGlow[!Shape.FillProperty] = new DynamicResourceExtension("PinkBrush");

            _dot = new Ellipse { IsHitTestVisible = false };
            _dot[!Shape.FillProperty] = new DynamicResourceExtension("PinkVibrantBrush");

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
                FontWeight = FontWeight.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false,
            };
            _numeral[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("PinkSoftBrush");
            Children.Add(_numeral);

            Loaded += (_, _) => { Relayout(); RefreshMotion(); };
            Unloaded += (_, _) => StopBreath();
            SizeChanged += (_, _) => Relayout();
        }

        /// <summary>WPF hangs this on <c>IsVisibleChanged</c>; Avalonia routes it through the
        /// property-changed override.</summary>
        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (_arm is null) return;   // a base setter fired during construction
            if (change.Property == IsVisibleProperty) RefreshMotion();
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

        /// <summary>
        /// A 40px circle has room for a glyph and not for "Crush Depth", so the stage rides as a
        /// numeral and the name rides a tooltip on the surfaces with room for one. n = 0 is the
        /// real pre-begin rung and reads as a dot.
        /// ponytail: verbatim copy of <c>SpiralRailHost.StageNumeral</c> (WPF head); call the
        /// shared one when the rail host reaches this head or Core.
        /// </summary>
        internal static string StageNumeral(int n) => n switch
        {
            1 => "I",
            2 => "II",
            3 => "III",
            4 => "IV",
            5 => "V",
            6 => "VI",
            7 => "VII",
            8 => "VIII",
            _ => n > 8 ? n.ToString(System.Globalization.CultureInfo.InvariantCulture) : "·",
        };

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
                ctx.BeginFigure(PointAt(0), isFilled: false);
                for (int i = 1; i <= Samples; i++)
                    ctx.LineTo(PointAt(i / (double)Samples));
                ctx.EndFigure(false);
            }
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
                _numeral.Text = StageNumeral(block?.Stage?.N ?? 0);
                _progress = _hasBlock
                    ? ComputeProgress(block!.DevotionDays, block.Stage?.NextAt)
                    : 0.0;
                Relayout();
            }
            catch (Exception ex) { Log.Debug("[Spiral] glyph apply: {E}", ex.Message); }
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
                double side = Math.Min(Bounds.Width, Bounds.Height);
                if (side <= 0 || double.IsNaN(side)) return;

                _host.Width = side;
                _host.Height = side;
                _armScale.ScaleX = _armScale.ScaleY = side;
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

                _dot.IsVisible = _hasBlock;
                _dotGlow.IsVisible = _hasBlock;

                _numeral.FontSize = Math.Max(7, Math.Round(side * 0.30));
                _numeral.IsVisible = side >= NumeralMinWidth;
            }
            catch (Exception ex) { Log.Debug("[Spiral] glyph layout: {E}", ex.Message); }
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
                // ponytail: needs MotionFx.AllowAmbientLoops, wired when it moves to Core
                bool wanted = IsVisible && IsLoaded;
                if (!wanted) { StopBreath(); return; }
                if (_breathing) return;
                _breathing = true;

                _breathClock = new CancellationTokenSource();
                var breathe = new Animation
                {
                    Duration = TimeSpan.FromSeconds(BreathSeconds),
                    IterationCount = IterationCount.Infinite,
                    PlaybackDirection = PlaybackDirection.Alternate,
                    Easing = new SineEaseInOut(),
                    Children =
                    {
                        new KeyFrame
                        {
                            Cue = new Cue(0d),
                            Setters =
                            {
                                new Setter(ScaleTransform.ScaleXProperty, 1.0),
                                new Setter(ScaleTransform.ScaleYProperty, 1.0),
                            },
                        },
                        new KeyFrame
                        {
                            Cue = new Cue(1d),
                            Setters =
                            {
                                new Setter(ScaleTransform.ScaleXProperty, BreathScale),
                                new Setter(ScaleTransform.ScaleYProperty, BreathScale),
                            },
                        },
                    },
                };
                _ = breathe.RunAsync(_breath, _breathClock.Token);
            }
            catch (Exception ex) { Log.Debug("[Spiral] glyph motion: {E}", ex.Message); }
        }

        private void StopBreath()
        {
            if (!_breathing) return;
            _breathing = false;
            try
            {
                _breathClock?.Cancel();
                _breathClock = null;
                _breath.ScaleX = _breath.ScaleY = 1.0;
            }
            catch (Exception ex) { Log.Debug("[Spiral] glyph stop: {E}", ex.Message); }
        }
    }
}
