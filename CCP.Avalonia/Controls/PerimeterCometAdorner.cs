using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace ConditioningControlPanel.Avalonia.Controls
{
    /// <summary>
    /// WPF Controls/PerimeterCometAdorner.cs: a bright white head with a house-coral tail lapping
    /// the OUTLINE of exactly one hero/active card. An ambient loop: it wants AllowTransitions AND
    /// AllowAmbientLoops, and when either is shut it DEGRADES to a static 2px 55% coral stroke so
    /// the active card still reads as lit. The clock parks while the card is hidden.
    /// <para>The arithmetic (dash units, SVG offset convention, the inset rounded-rect perimeter)
    /// is WPF's unchanged; Avalonia keeps both conventions, as TierFxBorder's band already shows.</para>
    /// </summary>
    public sealed class PerimeterCometAdorner : FxAdorner
    {
        public const double DefaultLapSeconds = 9.0;
        private const int AmbientFrameRate = 24;

        private const double HeadThickness = 2.5;
        private const double TailThickness = 2.2;
        private const double StaticThickness = 2.0;

        // Tail share of the perimeter, quantised into constant-alpha dashes (a pen cannot carry a
        // gradient along its own path); head share with a floor so a small card gets a visible head.
        private const double TailFraction = 0.16;
        private const int TailSegments = 7;
        private const double HeadFraction = 0.05;
        private const double HeadMinLength = 14.0;

        private static readonly Color Hue = Color.FromRgb(0xFF, 0x7E, 0x6B);   // house coral

        private static readonly IImmutableSolidColorBrush HeadBrush =
            new ImmutableSolidColorBrush(Color.FromArgb(0xF2, 0xFF, 0xFF, 0xFF));

        private static readonly IPen StaticPen = new ImmutablePen(
            new ImmutableSolidColorBrush(Color.FromArgb(0x8C, Hue.R, Hue.G, Hue.B)),
            StaticThickness, lineJoin: PenLineJoin.Round);

        private static readonly IImmutableSolidColorBrush[] TailBrushes = BuildTailBrushes();

        private readonly double _cornerRadius;
        private readonly double _lapSeconds;
        private bool _wantClock;
        private bool _clockRunning;
        private long _lapStart;
        private double _phase;

        private RectangleGeometry? _geometry;
        private Size _geometrySize;
        private double _perimeter;

        public PerimeterCometAdorner(Control adorned, double cornerRadius, double lapSeconds = DefaultLapSeconds)
            : base(adorned, AmbientFrameRate) => (_cornerRadius, _lapSeconds) = (Math.Max(0, cornerRadius), Math.Max(1.0, lapSeconds));

        /// <summary>Builds a comet, adds it to the element's adorner layer and starts it. Null -
        /// quietly - when there is no layer yet; call again from Loaded.</summary>
        public static PerimeterCometAdorner? Attach(Control? adorned, double cornerRadius,
                                                    double lapSeconds = DefaultLapSeconds)
        {
            if (adorned == null) return null;
            try
            {
                var comet = new PerimeterCometAdorner(adorned, cornerRadius, lapSeconds);
                if (!comet.AddToLayer()) return null;
                comet.Start();
                return comet;
            }
            catch { return null; }
        }

        /// <summary>Stops the clock and takes the comet off its layer. Null-safe, idempotent.</summary>
        public static void Detach(PerimeterCometAdorner? comet)
        {
            try { comet?.Stop(); comet?.RemoveFromLayer(); } catch { }
        }

        /// <summary>Test seams: the lap clock runs (false = static outline); lap position 0-1.</summary>
        internal bool IsLapping => _clockRunning;
        internal double Phase => _phase;

        /// <summary>Starts the lap, or settles into the static outline when motion is reduced/off
        /// or the tier bans ambient loops. Safe to call twice.</summary>
        public void Start()
        {
            _wantClock = AmbientFxCanvas.Env.AllowTransitions && AmbientFxCanvas.Env.AllowAmbientLoops;
            if (_wantClock && IsVisible) StartClock();
            else StopClock();
            InvalidateVisual();
        }

        /// <summary>Drops the clock; the static outline stays until the comet leaves its layer.</summary>
        public void Stop() { _wantClock = false; StopClock(); }

        private void StartClock()
        {
            if (_clockRunning) return;
            _clockRunning = true;
            _lapStart = Time.GetTimestamp();
            StartTicking();
        }

        private void StopClock()
        {
            StopTicking();
            if (!_clockRunning) return;
            _clockRunning = false;
            _phase = 0;
            InvalidateVisual();
        }

        protected override void OnShownChanged(bool shown)
        {
            if (_wantClock && shown) StartClock(); else StopClock();
        }

        internal override void Tick()
        {
            if (!_clockRunning) return;
            _phase = (Time.GetElapsedTime(_lapStart).TotalSeconds / _lapSeconds) % 1.0;
            InvalidateVisual();
        }

        public override void Render(DrawingContext context)
        {
            var size = Adorned.Bounds.Size;
            if (size.Width <= HeadThickness || size.Height <= HeadThickness) return;
            var geometry = EnsureGeometry(size);
            if (geometry == null) return;

            if (!_clockRunning || _perimeter <= 0)
            {
                context.DrawGeometry(null, StaticPen, geometry);   // degrade, never vanish
                return;
            }

            double headLength = Math.Max(HeadMinLength, _perimeter * HeadFraction);
            double segment = _perimeter * TailFraction / TailSegments;
            // Back to front, so the head paints over its own tail.
            for (int i = TailSegments - 1; i >= 0; i--)
                DrawArc(context, geometry, headLength + (i * segment), segment, TailBrushes[i], TailThickness);
            DrawArc(context, geometry, 0, headLength, HeadBrush, HeadThickness);
        }

        /// <summary>One lit arc: a single dash <paramref name="length"/> long, placed
        /// <paramref name="behind"/> pixels back from the head (positive offset shifts backwards).</summary>
        private void DrawArc(DrawingContext dc, Geometry geometry, double behind, double length,
                             IBrush brush, double thickness)
        {
            double dash = length / thickness;
            double gap = (_perimeter - length) / thickness;
            if (length <= 0 || gap <= 0) return;

            double pattern = dash + gap;
            double startPx = (_phase * _perimeter) - behind - length;
            double offset = (-startPx / thickness) % pattern;
            if (offset < 0) offset += pattern;

            dc.DrawGeometry(null, new Pen(brush, thickness, new DashStyle(new[] { dash, gap }, offset),
                                          PenLineCap.Round, PenLineJoin.Round), geometry);
        }

        /// <summary>The outline inset by half the head thickness, cached per size.</summary>
        private RectangleGeometry? EnsureGeometry(Size size)
        {
            if (_geometry != null && _geometrySize == size) return _geometry;
            double inset = HeadThickness / 2.0;
            double w = size.Width - (inset * 2);
            double h = size.Height - (inset * 2);
            if (w <= 0 || h <= 0) return null;

            double r = Math.Max(0, Math.Min(_cornerRadius - inset, Math.Min(w, h) / 2.0));
            _geometry = new RectangleGeometry(new Rect(inset, inset, w, h), r, r);
            _perimeter = (2 * (w - (2 * r))) + (2 * (h - (2 * r))) + (2 * Math.PI * r);
            _geometrySize = size;
            return _geometry;
        }

        private static IImmutableSolidColorBrush[] BuildTailBrushes()
        {
            const byte peak = 0x8C;   // 55% at the head end of the tail, linear fade to the tip
            var brushes = new IImmutableSolidColorBrush[TailSegments];
            for (int i = 0; i < TailSegments; i++)
            {
                double k = 1.0 - ((i + 0.5) / TailSegments);
                brushes[i] = new ImmutableSolidColorBrush(Color.FromArgb((byte)Math.Round(peak * k), Hue.R, Hue.G, Hue.B));
            }
            return brushes;
        }
    }
}
