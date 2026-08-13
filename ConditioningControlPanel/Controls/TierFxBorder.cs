using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Controls
{
    /// <summary>
    /// THE TIER LIVERY, ANIMATED. Attached properties that turn any <see cref="Border"/> already
    /// wearing the tier gradient (Tier1Gold*/Tier2Diamond*, Resources\Theme\Brushes.xaml) into
    /// "living metal": the resting rim is the Border's own thick gradient stroke, and this puts a
    /// slow traveling highlight band on top of it.
    ///
    /// <para><b>Why the thickness stays on the Border and only the band lives here.</b> The
    /// reduced-motion fallback is a static thick gradient rim - which is exactly what the Border
    /// paints by itself. So the degrade path is "attach nothing, draw nothing", not "draw a
    /// substitute": there is no second code path that can drift away from the resting look, and a
    /// user on Performance tier pays literally zero for the feature.</para>
    ///
    /// <para><b>Usage.</b> In a Style setter or on the element:
    /// <code>&lt;Setter Property="fx:TierFxBorder.Tier" Value="1"/&gt;</code>
    /// plus the matching <c>BorderThickness</c> (3 on cards, 4 on hero surfaces) and
    /// <c>fx:TierFxBorder.RimThickness</c> if it is not the default 3. Hover is picked up from the
    /// element itself, so a card's existing IsMouseOver trigger (which brightens the resting
    /// gradient) and the band's own hover intensification stay in step without either knowing
    /// about the other.</para>
    ///
    /// <para><b>House rules kept</b> (same contract as <see cref="PerimeterCometAdorner"/>): one
    /// WPF clock per surface capped at 24fps, ambient gating via
    /// <see cref="MotionFx.AllowAmbientLoops"/> re-checked every time the surface becomes visible,
    /// the clock parks when the owning tab hides, and nothing here touches a layout, effect or
    /// transform property of the decorated element - this type only ever draws.</para>
    ///
    /// <para>Tier livery is commerce chrome: the band colours are CONSTANT across mods and never
    /// take FxTheme's hue, exactly like the brushes they ride on.</para>
    /// </summary>
    public static class TierFxBorder
    {
        /// <summary>Default rim weight for a card. Hero surfaces pass 4.</summary>
        public const double DefaultRimThickness = 3.0;

        // =====================================================================================
        //  attached properties
        // =====================================================================================

        /// <summary>
        /// The livery tier this border wears: 1 = gold, 2+ = diamond, 0 = none (detaches).
        /// This is the VISUAL tier, never an entitlement check - nothing in this file gates
        /// anything.
        /// </summary>
        public static readonly DependencyProperty TierProperty =
            DependencyProperty.RegisterAttached(
                "Tier", typeof(int), typeof(TierFxBorder),
                new PropertyMetadata(0, OnTierChanged));

        public static void SetTier(DependencyObject d, int value) => d.SetValue(TierProperty, value);
        public static int GetTier(DependencyObject d) => (int)d.GetValue(TierProperty);

        /// <summary>
        /// Width of the band, which must match the Border's own BorderThickness or the highlight
        /// will not sit on the metal. Set alongside BorderThickness, never instead of it.
        /// </summary>
        public static readonly DependencyProperty RimThicknessProperty =
            DependencyProperty.RegisterAttached(
                "RimThickness", typeof(double), typeof(TierFxBorder),
                new PropertyMetadata(DefaultRimThickness, OnRimThicknessChanged));

        public static void SetRimThickness(DependencyObject d, double value) => d.SetValue(RimThicknessProperty, value);
        public static double GetRimThickness(DependencyObject d) => (double)d.GetValue(RimThicknessProperty);

        /// <summary>The live adorner for this element, so a re-attach can find and drop it.</summary>
        private static readonly DependencyProperty AdornerProperty =
            DependencyProperty.RegisterAttached(
                "Adorner", typeof(TierFxBorderAdorner), typeof(TierFxBorder),
                new PropertyMetadata(null));

        /// <summary>True once the element's Loaded/Unloaded/hover hooks are in place.</summary>
        private static readonly DependencyProperty HookedProperty =
            DependencyProperty.RegisterAttached(
                "Hooked", typeof(bool), typeof(TierFxBorder), new PropertyMetadata(false));

        // =====================================================================================
        //  wiring
        // =====================================================================================

        private static void OnTierChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not FrameworkElement fe) return;
            try
            {
                Detach(fe);
                if (GetTier(fe) <= 0)
                {
                    Unhook(fe);
                    return;
                }
                Hook(fe);
                if (fe.IsLoaded) Attach(fe);
            }
            catch (Exception ex) { App.Logger?.Debug("TierFxBorder.OnTierChanged: {E}", ex.Message); }
        }

        private static void OnRimThicknessChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            // The band's geometry is baked at construction, so a live thickness change means a new
            // adorner. In practice this only ever fires once, from the style that set it.
            if (d is not FrameworkElement fe || GetTier(fe) <= 0) return;
            try
            {
                Detach(fe);
                if (fe.IsLoaded) Attach(fe);
            }
            catch (Exception ex) { App.Logger?.Debug("TierFxBorder.OnRimThicknessChanged: {E}", ex.Message); }
        }

        private static void Hook(FrameworkElement fe)
        {
            if ((bool)fe.GetValue(HookedProperty)) return;
            fe.SetValue(HookedProperty, true);
            fe.Loaded += OnLoaded;
            fe.Unloaded += OnUnloaded;
            fe.MouseEnter += OnMouseEnter;
            fe.MouseLeave += OnMouseLeave;
        }

        private static void Unhook(FrameworkElement fe)
        {
            if (!(bool)fe.GetValue(HookedProperty)) return;
            fe.SetValue(HookedProperty, false);
            fe.Loaded -= OnLoaded;
            fe.Unloaded -= OnUnloaded;
            fe.MouseEnter -= OnMouseEnter;
            fe.MouseLeave -= OnMouseLeave;
        }

        private static void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe) Attach(fe);
        }

        private static void OnUnloaded(object sender, RoutedEventArgs e)
        {
            // A card that leaves the tree must not keep an adorner (or a clock) alive on a layer
            // it no longer belongs to. Re-attached by Loaded if it comes back.
            if (sender is FrameworkElement fe) Detach(fe);
        }

        private static void OnMouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (sender is FrameworkElement fe) (fe.GetValue(AdornerProperty) as TierFxBorderAdorner)?.SetHover(true);
        }

        private static void OnMouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (sender is FrameworkElement fe) (fe.GetValue(AdornerProperty) as TierFxBorderAdorner)?.SetHover(false);
        }

        /// <summary>
        /// Builds and starts the band for <paramref name="fe"/>. Silent no-op when the element has
        /// no adorner layer yet (normal during construction) - the retry rides on Loaded, and on
        /// the bounded Background-priority requeue below, which is the same shape (and the same
        /// livelock-avoiding priority) the vault's card sheens use.
        /// </summary>
        private static void Attach(FrameworkElement fe, int retriesLeft = 3)
        {
            try
            {
                int tier = GetTier(fe);
                if (tier <= 0) return;
                if (fe.GetValue(AdornerProperty) is TierFxBorderAdorner live)
                {
                    live.Start();
                    return;
                }

                var layer = AdornerLayer.GetAdornerLayer(fe);
                if (layer == null)
                {
                    if (retriesLeft <= 0) return;
                    fe.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        try { if (fe.IsLoaded) Attach(fe, retriesLeft - 1); }
                        catch (Exception ex) { App.Logger?.Debug("TierFxBorder retry: {E}", ex.Message); }
                    }), System.Windows.Threading.DispatcherPriority.Background);
                    return;
                }

                double radius = fe is Border b ? b.CornerRadius.TopLeft : 0;
                var adorner = new TierFxBorderAdorner(fe, tier, radius, GetRimThickness(fe));
                layer.Add(adorner);
                fe.SetValue(AdornerProperty, adorner);
                adorner.Start();
            }
            catch (Exception ex) { App.Logger?.Debug("TierFxBorder.Attach: {E}", ex.Message); }
        }

        private static void Detach(FrameworkElement fe)
        {
            try
            {
                if (fe.GetValue(AdornerProperty) is not TierFxBorderAdorner adorner) return;
                fe.SetValue(AdornerProperty, null);
                adorner.Stop();
                AdornerLayer.GetAdornerLayer(fe)?.Remove(adorner);
            }
            catch (Exception ex) { App.Logger?.Debug("TierFxBorder.Detach: {E}", ex.Message); }
        }

        // =====================================================================================
        //  park / resume - for surfaces with an explicit motion contract (the vault's
        //  Start/StopExclusivesMotion). Visibility parking happens on its own regardless.
        // =====================================================================================

        /// <summary>Stops this element's band, if it has one. Idempotent and null-safe.</summary>
        public static void Park(DependencyObject? d) =>
            (d?.GetValue(AdornerProperty) as TierFxBorderAdorner)?.Stop();

        /// <summary>Starts (or re-decides) this element's band. Idempotent and null-safe.</summary>
        public static void Resume(DependencyObject? d) =>
            (d?.GetValue(AdornerProperty) as TierFxBorderAdorner)?.Start();

        /// <summary>True when this element currently has a live band. Test seam.</summary>
        internal static bool HasBand(DependencyObject? d) =>
            d?.GetValue(AdornerProperty) is TierFxBorderAdorner;
    }

    /// <summary>
    /// The traveling highlight itself: one soft-edged band lapping the rim, plus (Tier 2 only)
    /// sparkle glints at fixed, precomputed offsets.
    ///
    /// <para>Tier 1 is "molten gold" - a warm band, CLOCKWISE, 6.5s, constant drift (luxury, not
    /// urgency). Tier 2 is "frost fire" - a cool white-cyan band, COUNTER-clockwise, 5s, so a gold
    /// card and a diamond card sitting side by side never march in lockstep. Hover tightens the lap
    /// and brightens the band; on Tier 2 a second glint set switches on, which reads as the glint
    /// rate doubling.</para>
    ///
    /// <para><b>Glints are deterministic.</b> Their positions and their moments are static arrays,
    /// not <c>Random</c>: a render test can assert the same frame twice, and two cards on the same
    /// wall stay decorrelated by their phase, not by luck.</para>
    /// </summary>
    public sealed class TierFxBorderAdorner : Adorner
    {
        private const int AmbientFrameRate = 24;

        /// <summary>Share of the perimeter the band covers.</summary>
        private const double BandFraction = 0.25;

        /// <summary>Quantisation of the band's soft edges. A Pen cannot carry a gradient along its
        /// own path, so the ramp is drawn as this many constant-alpha dashes (the same trick, and
        /// the same reasoning, as PerimeterCometAdorner's tail).</summary>
        private const int BandSegments = 11;

        private const double RestLapT1 = 6.5;
        private const double RestLapT2 = 5.0;
        private const double HoverLapT1 = 3.0;
        private const double HoverLapT2 = 2.6;

        /// <summary>Where the Tier 2 glints sit on the rim, as a share of the perimeter, and when
        /// they fire, as a share of the lap. Three at rest; hover adds the second set.</summary>
        private static readonly double[] GlintWhere = { 0.13, 0.47, 0.79 };
        private static readonly double[] GlintWhen = { 0.08, 0.41, 0.74 };
        private static readonly double[] GlintWhereHover = { 0.28, 0.62, 0.95 };
        private static readonly double[] GlintWhenHover = { 0.24, 0.57, 0.90 };

        /// <summary>Glint life as a share of the lap is period-dependent, so it is stored in
        /// seconds and converted per frame - a glint always pops for ~300ms.</summary>
        private const double GlintSeconds = 0.30;
        private const double GlintRadius = 3.2;

        private readonly int _tier;
        private readonly double _cornerRadius;
        private readonly double _thickness;

        /// <summary>Band brushes, brightest at the band's centre. Two sets: resting and hover.</summary>
        private readonly SolidColorBrush[] _restBand;
        private readonly SolidColorBrush[] _hoverBand;
        private readonly Brush _glintBrush;

        // No AdornerLayer field: TierFxBorder owns adding and removing this from the layer, so an
        // adorner holding its own layer reference would be a second owner of the same lifetime.
        private FrameworkElement? _visibilityHost;

        private bool _wantClock;
        private bool _clockRunning;
        private bool _hover;

        private RectangleGeometry? _geometry;
        private Size _geometrySize;
        private double _perimeter;

        /// <summary>Lap position, 0-1, and the whole mechanism: <c>AffectsRender</c> means animating
        /// it re-runs <see cref="OnRender"/>, and the frame rate cap on the animation is what holds
        /// that to 24 frames a second.</summary>
        private static readonly DependencyProperty PhaseProperty =
            DependencyProperty.Register(
                "Phase", typeof(double), typeof(TierFxBorderAdorner),
                new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

        public TierFxBorderAdorner(UIElement adorned, int tier, double cornerRadius, double thickness)
            : base(adorned)
        {
            IsHitTestVisible = false;
            _tier = tier >= 2 ? 2 : 1;
            _cornerRadius = Math.Max(0, cornerRadius);
            _thickness = Math.Max(1.0, thickness);

            if (_tier >= 2)
            {
                // Frost fire: ice white-cyan, brightening to pure white on hover.
                _restBand = BuildBand(Color.FromRgb(0xEA, 0xFB, 0xFF), 0xB4);
                _hoverBand = BuildBand(Color.FromRgb(0xFF, 0xFF, 0xFF), 0xE6);
            }
            else
            {
                // Molten gold: a warm highlight over the gradient, waking toward #FFE9A8.
                _restBand = BuildBand(Color.FromRgb(0xFF, 0xD9, 0xA0), 0xAA);
                _hoverBand = BuildBand(Color.FromRgb(0xFF, 0xE9, 0xA8), 0xDE);
            }

            var glint = new SolidColorBrush(Color.FromArgb(0xF2, 0xFF, 0xFF, 0xFF));
            glint.Freeze();
            _glintBrush = glint;
        }

        /// <summary>Seconds for one lap right now.</summary>
        private double LapSeconds => _tier >= 2
            ? (_hover ? HoverLapT2 : RestLapT2)
            : (_hover ? HoverLapT1 : RestLapT1);

        // =====================================================================================
        //  start / stop
        // =====================================================================================

        /// <summary>
        /// Starts the lap - or, when ambient motion is off, simply does not, leaving the Border's
        /// own thick gradient rim as the resting look. Safe to call repeatedly; the gate is
        /// re-read on every call, so a motion-level change lands on the next tab visit rather than
        /// needing a restart.
        /// </summary>
        public void Start()
        {
            try
            {
                bool ambient;
                try { ambient = MotionFx.AllowAmbientLoops; }
                catch { ambient = false; }

                HookVisibility();
                _wantClock = ambient;

                if (!ambient)
                {
                    StopClock();
                    return;
                }
                if (_visibilityHost != null && !_visibilityHost.IsVisible)
                {
                    StopClock();
                    return;
                }
                StartClock();
            }
            catch (Exception ex) { App.Logger?.Debug("TierFxBorderAdorner.Start: {E}", ex.Message); }
        }

        /// <summary>Parks the clock. The rim underneath is untouched, so nothing flickers to bare.</summary>
        public void Stop()
        {
            try
            {
                _wantClock = false;
                StopClock();
                UnhookVisibility();
            }
            catch (Exception ex) { App.Logger?.Debug("TierFxBorderAdorner.Stop: {E}", ex.Message); }
        }

        /// <summary>Hover intensification: a tighter lap and a brighter band (Tier 2 also reveals
        /// its second glint set). Restarting the clock is what re-times the lap.</summary>
        public void SetHover(bool on)
        {
            if (_hover == on) return;
            _hover = on;
            try
            {
                if (_clockRunning)
                {
                    // Restart at the new period, carrying the current phase so the band does not
                    // jump backwards under the pointer.
                    double phase = (double)GetValue(PhaseProperty);
                    StopClock();
                    StartClock(phase);
                }
                else InvalidateVisual();
            }
            catch (Exception ex) { App.Logger?.Debug("TierFxBorderAdorner.SetHover: {E}", ex.Message); }
        }

        /// <summary>True while a clock is actually running. Test seam.</summary>
        internal bool IsAnimating => _clockRunning;

        /// <summary>
        /// One clock for both tiers, always running 0 -> 1; DIRECTION is applied at draw time
        /// (Tier 2 reads the phase mirrored), which keeps the timing code single-path and makes
        /// "counter-clockwise" a one-line statement instead of a second set of animations.
        /// </summary>
        private void StartClock(double from = 0.0)
        {
            if (_clockRunning) return;
            _clockRunning = true;

            double start = Math.Clamp(from, 0.0, 0.999);
            var loop = new DoubleAnimation(0.0, 1.0, TimeSpan.FromSeconds(LapSeconds))
            {
                RepeatBehavior = RepeatBehavior.Forever,
            };
            Timeline.SetDesiredFrameRate(loop, AmbientFrameRate);

            if (start <= 0.0005)
            {
                BeginAnimation(PhaseProperty, loop);
                return;
            }

            // Resuming mid-lap (a hover re-times the period): finish the current revolution at the
            // new speed, then hand over to the endless loop, so the band never jumps backwards
            // under the pointer.
            var partial = new DoubleAnimation(start, 1.0, TimeSpan.FromSeconds(LapSeconds * (1.0 - start)));
            Timeline.SetDesiredFrameRate(partial, AmbientFrameRate);
            partial.Completed += (_, _) =>
            {
                if (_clockRunning) BeginAnimation(PhaseProperty, loop);
            };
            BeginAnimation(PhaseProperty, partial);
        }

        private void StopClock()
        {
            if (!_clockRunning) return;
            _clockRunning = false;
            BeginAnimation(PhaseProperty, null);
            SetValue(PhaseProperty, 0.0);
            InvalidateVisual();
        }

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
                if (e.NewValue is true)
                {
                    // Re-read the gate on the way in: this is what makes the motion kill-switch
                    // reach a card that was already built when the setting changed.
                    bool ambient;
                    try { ambient = MotionFx.AllowAmbientLoops; }
                    catch { ambient = false; }
                    _wantClock = ambient;
                    if (ambient) StartClock();
                    else StopClock();
                }
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
                // Motion off = the Border's own gradient rim, undecorated. No substitute stroke:
                // one resting look, drawn by one thing.
                if (!_clockRunning) return;

                var size = AdornedElement.RenderSize;
                if (size.Width <= _thickness || size.Height <= _thickness) return;

                var geometry = EnsureGeometry(size);
                if (geometry == null || _perimeter <= 0) return;

                double clock = (double)GetValue(PhaseProperty);
                // Tier 1 laps clockwise, Tier 2 counter-clockwise: two tiers on one wall must not
                // march in lockstep, and mirroring the phase is the cheapest way to say so.
                double phase = _tier >= 2 ? 1.0 - clock : clock;
                var band = _hover ? _hoverBand : _restBand;
                double bandLength = _perimeter * BandFraction;
                double segment = bandLength / BandSegments;

                // Dimmest segments first so the bright core paints over its own falloff.
                for (int i = 0; i < BandSegments; i++)
                {
                    int order = SegmentPaintOrder(i);
                    DrawArc(drawingContext, geometry, phase,
                            behind: order * segment,
                            length: segment,
                            brush: band[order],
                            thickness: _thickness);
                }

                if (_tier >= 2) DrawGlints(drawingContext, phase);
            }
            catch (Exception ex) { App.Logger?.Debug("TierFxBorderAdorner.OnRender: {E}", ex.Message); }
        }

        /// <summary>Paint order 0,10,1,9,2,8,... - the two faint tips go down first and the bright
        /// core paints last, over its own falloff.</summary>
        private static int SegmentPaintOrder(int i) =>
            (i % 2 == 0) ? i / 2 : BandSegments - 1 - (i / 2);

        /// <summary>
        /// One lit arc of the rim: a dashed pen whose single dash is <paramref name="length"/>
        /// long, whose gap is the rest of the perimeter, and whose offset places it
        /// <paramref name="behind"/> pixels back from the band's leading edge.
        ///
        /// <para>DashStyle counts in multiples of pen thickness, and a positive Offset shifts the
        /// pattern BACKWARDS along the path (SVG's stroke-dashoffset convention) - hence the
        /// negated start position, wrapped into one pattern length.</para>
        /// </summary>
        private void DrawArc(DrawingContext dc, Geometry geometry, double phase,
                             double behind, double length, Brush brush, double thickness)
        {
            if (length <= 0 || thickness <= 0) return;

            double dash = length / thickness;
            double gap = (_perimeter - length) / thickness;
            if (gap <= 0) return;

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
        /// Tier 2's sparkle ticks: tiny star-glints that pop at fixed points on the rim, at fixed
        /// moments in the lap. Each one scales in and fades out over ~300ms; hover switches on a
        /// second set, which reads as the rate doubling.
        /// </summary>
        private void DrawGlints(DrawingContext dc, double phase)
        {
            double life = Math.Min(0.5, GlintSeconds / Math.Max(0.5, LapSeconds));

            DrawGlintSet(dc, phase, life, GlintWhere, GlintWhen);
            if (_hover) DrawGlintSet(dc, phase, life, GlintWhereHover, GlintWhenHover);
        }

        private void DrawGlintSet(DrawingContext dc, double phase, double life,
                                  double[] where, double[] when)
        {
            for (int i = 0; i < where.Length; i++)
            {
                double t = phase - when[i];
                if (t < 0) t += 1.0;
                if (t > life) continue;

                // Scale in / fade out on one sine hump: 0 at both ends, 1 in the middle.
                double envelope = Math.Sin(Math.PI * (t / life));
                if (envelope <= 0.02) continue;

                var point = PointOnRim(where[i]);
                if (point == null) continue;

                double r = GlintRadius * envelope;
                var brush = new SolidColorBrush(Color.FromArgb((byte)(0xF2 * envelope), 0xFF, 0xFF, 0xFF));
                brush.Freeze();
                dc.DrawEllipse(brush, null, point.Value, r, r);
            }
        }

        /// <summary>
        /// Where a share of the perimeter lands, walking the rounded rectangle's straight runs and
        /// treating each corner as a quarter circle. Approximate through the corners by design -
        /// a glint is 3px wide, and exactness there would cost a PathGeometry walk per frame.
        /// </summary>
        private Point? PointOnRim(double fraction)
        {
            if (_geometry == null || _perimeter <= 0) return null;
            var rect = _geometry.Rect;
            double r = _geometry.RadiusX;
            double straightX = Math.Max(0, rect.Width - (2 * r));
            double straightY = Math.Max(0, rect.Height - (2 * r));
            double corner = (Math.PI * r) / 2.0;

            double d = (fraction % 1.0) * _perimeter;
            if (d < 0) d += _perimeter;

            // top edge -> TR corner -> right edge -> BR -> bottom -> BL -> left -> TL
            if (d < straightX) return new Point(rect.Left + r + d, rect.Top);
            d -= straightX;
            if (d < corner) return CornerPoint(rect.Right - r, rect.Top + r, r, -90, d / corner);
            d -= corner;
            if (d < straightY) return new Point(rect.Right, rect.Top + r + d);
            d -= straightY;
            if (d < corner) return CornerPoint(rect.Right - r, rect.Bottom - r, r, 0, d / corner);
            d -= corner;
            if (d < straightX) return new Point(rect.Right - r - d, rect.Bottom);
            d -= straightX;
            if (d < corner) return CornerPoint(rect.Left + r, rect.Bottom - r, r, 90, d / corner);
            d -= corner;
            if (d < straightY) return new Point(rect.Left, rect.Bottom - r - d);
            d -= straightY;
            return CornerPoint(rect.Left + r, rect.Top + r, r, 180, Math.Min(1, d / corner));
        }

        private static Point CornerPoint(double cx, double cy, double r, double startDeg, double t)
        {
            double angle = (startDeg + (90 * t)) * Math.PI / 180.0;
            return new Point(cx + (r * Math.Cos(angle)), cy + (r * Math.Sin(angle)));
        }

        /// <summary>
        /// The path the band rides: the element's outline inset by half the rim weight, so the
        /// highlight sits ON the border stroke rather than beside it. Cached - rebuilding a
        /// geometry 24 times a second to describe the same rectangle would be the one expensive
        /// thing in this file.
        /// </summary>
        private RectangleGeometry? EnsureGeometry(Size size)
        {
            if (_geometry != null && _geometrySize == size) return _geometry;

            double inset = _thickness / 2.0;
            double w = size.Width - (inset * 2);
            double h = size.Height - (inset * 2);
            if (w <= 0 || h <= 0) return null;

            double r = Math.Max(0, Math.Min(_cornerRadius - inset, Math.Min(w, h) / 2.0));
            var geometry = new RectangleGeometry(new Rect(inset, inset, w, h), r, r);
            geometry.Freeze();

            _perimeter = (2 * (w - (2 * r))) + (2 * (h - (2 * r))) + (2 * Math.PI * r);
            _geometry = geometry;
            _geometrySize = size;
            return geometry;
        }

        /// <summary>
        /// The band's alpha ramp: faint at both tips, full in the middle, so the highlight has soft
        /// edges instead of two hard ends chasing each other around the card.
        /// </summary>
        private static SolidColorBrush[] BuildBand(Color hue, byte peak)
        {
            var brushes = new SolidColorBrush[BandSegments];
            for (int i = 0; i < BandSegments; i++)
            {
                double k = Math.Sin(Math.PI * ((i + 0.5) / BandSegments));
                var alpha = (byte)Math.Round(peak * k * k);
                var brush = new SolidColorBrush(Color.FromArgb(alpha, hue.R, hue.G, hue.B));
                brush.Freeze();
                brushes[i] = brush;
            }
            return brushes;
        }
    }
}
