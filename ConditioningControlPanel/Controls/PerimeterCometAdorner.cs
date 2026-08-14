using System;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Controls
{
    /// <summary>
    /// A bright head with a hue tail, lapping the OUTLINE of exactly one card.
    ///
    /// <para>Velvet Kit 2's answer to "the program is full of horribly thin lines". The baseline
    /// fix is static (2px gradient outlines, <c>VelvetCardBorderBrush</c> in
    /// <c>Resources/Theme/Controls.xaml</c>); this is the tier above it, and it is deliberately
    /// scarce. Eighty animated borders is a GPU bill and a migraine, so a comet is attached to
    /// HERO/ACTIVE surfaces only - today that is the Studio rack's checked tile, and nothing
    /// else.</para>
    ///
    /// <para><b>Why an adorner, same as <see cref="CardSheenAdorner"/>.</b> The decorated card is
    /// somebody else's Border/Grid with its own Padding and children; drawing the stroke inside
    /// it would mean fighting that padding (a child stops at the padding box, not the card edge),
    /// editing every card builder, and remembering to tear the stroke out when the selection
    /// moves. An adorner costs the card nothing - no element in its visual tree, no layout pass -
    /// and moving the comet is a <see cref="Detach"/> plus an <see cref="Attach"/>.</para>
    ///
    /// <para><b>House rules.</b>
    /// <list type="bullet">
    /// <item>One clock, capped at <see cref="AmbientFrameRate"/> fps via
    /// <c>Timeline.SetDesiredFrameRate</c> - the same discipline CardSheenAdorner's ambient loop
    /// keeps. The clock drives a single <c>AffectsRender</c> DP; WPF's own animation system is
    /// the frame pump, so there is no DispatcherTimer to leak and no per-tick allocation beyond
    /// the pens.</item>
    /// <item>It is an AMBIENT loop by the app's definition (an 8-60s repeat), so it wants
    /// <see cref="MotionFx.AllowAmbientLoops"/>, not merely
    /// <see cref="MotionFx.AllowTransitions"/>. When either gate is shut it DEGRADES rather than
    /// vanishes: a static 2px 55% hue stroke, so the active card still reads as lit.</item>
    /// <item>It parks its clock while the adorned element is not visible (leaving the Studio
    /// door collapses the tile), and picks it back up on the way in.</item>
    /// <item>No effect, no transform, no layout property is touched on the adorned element -
    /// this type only ever draws.</item>
    /// </list></para>
    /// </summary>
    public sealed class PerimeterCometAdorner : Adorner
    {
        /// <summary>Seconds for one lap of the perimeter. Slow enough to read as ambient.</summary>
        public const double DefaultLapSeconds = 9.0;

        private const int AmbientFrameRate = 24;

        private const double HeadThickness = 2.5;
        private const double TailThickness = 2.2;
        private const double StaticThickness = 2.0;

        /// <summary>Tail length as a share of the perimeter, split into
        /// <see cref="TailSegments"/> constant-alpha dashes (a Pen cannot carry a gradient along
        /// its own path, so the fade is quantised - seven steps is past the point where the
        /// banding is visible at these alphas).</summary>
        private const double TailFraction = 0.16;
        private const int TailSegments = 7;

        /// <summary>Head length as a share of the perimeter, with a floor so a small card still
        /// gets a head you can see rather than a dot.</summary>
        private const double HeadFraction = 0.05;
        private const double HeadMinLength = 14.0;

        private static readonly Color Hue = Color.FromRgb(0xFF, 0x7E, 0x6B);   // house coral

        private static readonly SolidColorBrush HeadBrush =
            Frozen(new SolidColorBrush(Color.FromArgb(0xF2, 0xFF, 0xFF, 0xFF)));   // white @ 95%

        /// <summary>The motion-off / ambient-off fallback: a plain lit outline.</summary>
        private static readonly Pen StaticPen =
            Frozen(new Pen(Frozen(new SolidColorBrush(Color.FromArgb(0x8C, Hue.R, Hue.G, Hue.B))),
                           StaticThickness)
            {
                LineJoin = PenLineJoin.Round,
            });

        /// <summary>Tail brushes, brightest (nearest the head) first. Built once and shared.</summary>
        private static readonly SolidColorBrush[] TailBrushes = BuildTailBrushes();

        private readonly double _cornerRadius;
        private readonly double _lapSeconds;

        private AdornerLayer? _layer;
        private FrameworkElement? _visibilityHost;

        /// <summary>True once <see cref="Start"/> has decided this instance should be moving -
        /// the visibility hook suspends and resumes the clock against this, so a card that scrolls
        /// or tabs out of sight is not paying 24fps to draw nothing.</summary>
        private bool _wantClock;
        private bool _clockRunning;

        // Geometry cache, rebuilt only when the adorned element's size actually changes.
        private RectangleGeometry? _geometry;
        private Size _geometrySize;
        private double _perimeter;

        /// <summary>
        /// Lap position, 0-1. <c>AffectsRender</c> is the whole mechanism: animating this
        /// re-runs <see cref="OnRender"/>, and <c>Timeline.SetDesiredFrameRate</c> on the
        /// animation is what keeps that to 24 frames a second.
        /// </summary>
        private static readonly DependencyProperty PhaseProperty =
            DependencyProperty.Register(
                "Phase", typeof(double), typeof(PerimeterCometAdorner),
                new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

        /// <param name="adorned">The element whose outline the comet laps.</param>
        /// <param name="cornerRadius">Corner radius of that element, so the stroke follows its
        /// rounding instead of cutting the corners.</param>
        /// <param name="lapSeconds">Seconds for one lap. Clamped to a sane floor.</param>
        public PerimeterCometAdorner(UIElement adorned, double cornerRadius,
                                     double lapSeconds = DefaultLapSeconds)
            : base(adorned)
        {
            IsHitTestVisible = false;
            _cornerRadius = Math.Max(0, cornerRadius);
            _lapSeconds = Math.Max(1.0, lapSeconds);
        }

        // =====================================================================================
        //  attach / detach — the whole public surface
        // =====================================================================================

        /// <summary>
        /// Builds a comet on <paramref name="adorned"/>, adds it to that element's adorner layer
        /// and starts it. Returns null - quietly - when there is no adorner layer yet (the element
        /// is not in the visual tree), which is a normal state during construction, not an error:
        /// call again from Loaded.
        /// </summary>
        public static PerimeterCometAdorner? Attach(UIElement? adorned, double cornerRadius,
                                                    double lapSeconds = DefaultLapSeconds)
        {
            if (adorned == null) return null;
            try
            {
                var layer = AdornerLayer.GetAdornerLayer(adorned);
                if (layer == null) return null;

                var comet = new PerimeterCometAdorner(adorned, cornerRadius, lapSeconds);
                layer.Add(comet);
                comet._layer = layer;
                comet.Start();
                return comet;
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("PerimeterCometAdorner.Attach: {E}", ex.Message);
                return null;
            }
        }

        /// <summary>Stops the clock and takes the comet back off its layer. Null-safe and
        /// idempotent - detaching twice is not an error.</summary>
        public static void Detach(PerimeterCometAdorner? comet)
        {
            if (comet == null) return;
            try
            {
                comet.Stop();
                comet._layer?.Remove(comet);
                comet._layer = null;
            }
            catch (Exception ex) { App.Logger?.Debug("PerimeterCometAdorner.Detach: {E}", ex.Message); }
        }

        /// <summary>
        /// Starts the lap, or - when motion is reduced/off or the tier bans ambient loops -
        /// settles into the static lit outline. Safe to call twice.
        /// </summary>
        public void Start()
        {
            try
            {
                bool ambient;
                // App.Settings is null very early in startup and in test hosts; MotionFx already
                // defaults to Full there, but a throw here must not cost the caller its card.
                try { ambient = MotionFx.AllowTransitions && MotionFx.AllowAmbientLoops; }
                catch { ambient = false; }

                HookVisibility();

                _wantClock = ambient;
                if (!ambient)
                {
                    StopClock();
                    InvalidateVisual();       // repaint as the static stroke
                    return;
                }
                if (_visibilityHost != null && !_visibilityHost.IsVisible)
                {
                    // Attached while collapsed - e.g. a preselect that picks the module before
                    // the door is shown. The visibility hook starts the clock when it can be seen.
                    StopClock();
                    return;
                }
                StartClock();
            }
            catch (Exception ex) { App.Logger?.Debug("PerimeterCometAdorner.Start: {E}", ex.Message); }
        }

        /// <summary>Drops the clock. The comet keeps drawing its static outline until it is
        /// removed from the layer, so stopping never makes a card flicker to bare.</summary>
        public void Stop()
        {
            try
            {
                _wantClock = false;
                StopClock();
                UnhookVisibility();
            }
            catch (Exception ex) { App.Logger?.Debug("PerimeterCometAdorner.Stop: {E}", ex.Message); }
        }

        // =====================================================================================
        //  clock
        // =====================================================================================

        private void StartClock()
        {
            if (_clockRunning) return;
            var lap = new DoubleAnimation(0.0, 1.0, TimeSpan.FromSeconds(_lapSeconds))
            {
                RepeatBehavior = RepeatBehavior.Forever,
            };
            Timeline.SetDesiredFrameRate(lap, AmbientFrameRate);
            BeginAnimation(PhaseProperty, lap);
            _clockRunning = true;
        }

        private void StopClock()
        {
            if (!_clockRunning) return;
            BeginAnimation(PhaseProperty, null);
            SetValue(PhaseProperty, 0.0);
            _clockRunning = false;
        }

        /// <summary>
        /// A collapsed card is still adorned: without this the Studio door's clock would keep
        /// ticking behind every other tab. RenderSize goes to zero when the tile is collapsed, so
        /// the frames would draw nothing at all - pure waste.
        /// </summary>
        private void HookVisibility()
        {
            if (_visibilityHost != null) return;
            if (AdornedElement is not FrameworkElement fe) return;
            _visibilityHost = fe;
            fe.IsVisibleChanged += OnAdornedVisibilityChanged;
        }

        private void UnhookVisibility()
        {
            var fe = _visibilityHost;
            _visibilityHost = null;
            if (fe != null) fe.IsVisibleChanged -= OnAdornedVisibilityChanged;
        }

        private void OnAdornedVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            try
            {
                if (!_wantClock) return;
                if (e.NewValue is true) StartClock();
                else StopClock();
            }
            catch { }
        }

        // =====================================================================================
        //  drawing
        // =====================================================================================

        protected override void OnRender(DrawingContext drawingContext)
        {
            try
            {
                var size = AdornedElement.RenderSize;
                if (size.Width <= HeadThickness || size.Height <= HeadThickness) return;

                var geometry = EnsureGeometry(size);
                if (geometry == null) return;

                if (!_clockRunning || _perimeter <= 0)
                {
                    // Degrade, never vanish.
                    drawingContext.DrawGeometry(null, StaticPen, geometry);
                    return;
                }

                double phase = (double)GetValue(PhaseProperty);
                double headLength = Math.Max(HeadMinLength, _perimeter * HeadFraction);
                double tailLength = _perimeter * TailFraction;
                double segment = tailLength / TailSegments;

                // Back to front, so the head paints over its own tail rather than under it.
                for (int i = TailSegments - 1; i >= 0; i--)
                {
                    DrawArc(drawingContext, geometry, phase,
                            behind: headLength + (i * segment),
                            length: segment,
                            brush: TailBrushes[i],
                            thickness: TailThickness);
                }
                DrawArc(drawingContext, geometry, phase, behind: 0, length: headLength,
                        brush: HeadBrush, thickness: HeadThickness);
            }
            catch (Exception ex) { App.Logger?.Debug("PerimeterCometAdorner.OnRender: {E}", ex.Message); }
        }

        /// <summary>
        /// Paints ONE lit arc of the outline: a dashed pen whose single dash is
        /// <paramref name="length"/> long, whose gap is the rest of the perimeter, and whose
        /// offset places it <paramref name="behind"/> pixels back from the head.
        ///
        /// <para>DashStyle counts in multiples of the pen thickness, and a positive Offset shifts
        /// the pattern BACKWARDS along the path (SVG's stroke-dashoffset convention) - which is
        /// why the offset here is the negated head position, wrapped into one pattern length.</para>
        /// </summary>
        private void DrawArc(DrawingContext dc, Geometry geometry, double phase,
                             double behind, double length, Brush brush, double thickness)
        {
            if (length <= 0 || thickness <= 0) return;

            double dash = length / thickness;
            double gap = (_perimeter - length) / thickness;
            if (gap <= 0) return;                       // arc longer than the card: skip it

            double pattern = dash + gap;
            double startPx = (phase * _perimeter) - behind - length;
            double offset = (-startPx / thickness) % pattern;
            if (offset < 0) offset += pattern;

            var pen = new Pen(brush, thickness)
            {
                DashStyle = new DashStyle(new[] { dash, gap }, offset),
                DashCap = PenLineCap.Round,
                LineJoin = PenLineJoin.Round,
            };
            pen.Freeze();
            dc.DrawGeometry(null, pen, geometry);
        }

        /// <summary>
        /// The outline the comet rides, inset by half the head thickness so the stroke sits just
        /// INSIDE the card's own border instead of bleeding over its edge. Cached: the size only
        /// changes when the card resizes, and rebuilding a geometry 24 times a second to draw the
        /// same rectangle would be the one expensive thing in this file.
        /// </summary>
        private RectangleGeometry? EnsureGeometry(Size size)
        {
            if (_geometry != null && _geometrySize == size) return _geometry;

            double inset = HeadThickness / 2.0;
            double w = size.Width - (inset * 2);
            double h = size.Height - (inset * 2);
            if (w <= 0 || h <= 0) return null;

            double r = Math.Max(0, Math.Min(_cornerRadius - inset, Math.Min(w, h) / 2.0));
            var geometry = new RectangleGeometry(new Rect(inset, inset, w, h), r, r);
            geometry.Freeze();

            // Rounded-rect perimeter: the two straight runs per axis plus one full circle of
            // corner. Only ever used to convert lengths into dash units, so it does not have to
            // be exact to the pixel - but it is.
            _perimeter = (2 * (w - (2 * r))) + (2 * (h - (2 * r))) + (2 * Math.PI * r);
            _geometry = geometry;
            _geometrySize = size;
            return geometry;
        }

        private static SolidColorBrush[] BuildTailBrushes()
        {
            const byte peak = 0x8C;                     // 55% at the head end of the tail
            var brushes = new SolidColorBrush[TailSegments];
            for (int i = 0; i < TailSegments; i++)
            {
                // Linear fade to (near) nothing at the tip of the tail.
                double k = 1.0 - ((i + 0.5) / TailSegments);
                var alpha = (byte)Math.Round(peak * k);
                brushes[i] = Frozen(new SolidColorBrush(Color.FromArgb(alpha, Hue.R, Hue.G, Hue.B)));
            }
            return brushes;
        }

        private static T Frozen<T>(T freezable) where T : Freezable
        {
            try { if (freezable.CanFreeze) freezable.Freeze(); } catch { }
            return freezable;
        }
    }
}
