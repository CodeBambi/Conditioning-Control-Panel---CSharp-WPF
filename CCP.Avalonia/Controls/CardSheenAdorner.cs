using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace ConditioningControlPanel.Avalonia.Controls
{
    /// <summary>
    /// WPF Controls/CardSheenAdorner.cs: a slow "glass catches the light" band that crosses ONE
    /// card - 1.3s of travel, then rest, on a 12s cycle (one clock; the long flat tail IS the pause).
    /// Colour from FxTheme's glow, re-read on every <see cref="Start"/> so a mod switch re-tints on
    /// the caller's restart. The caller owns the ambient gate; this type never starts a clock it
    /// was not asked for, and <see cref="Stop"/> parks the band off the visible range.
    /// <para>Avalonia: the WPF stop-offset keyframes become one phase computed per frame; during
    /// the 10.7s rest nothing is invalidated, so the resting card costs a timer tick only.</para>
    /// </summary>
    public sealed class CardSheenAdorner : FxAdorner
    {
        private const double TravelSeconds = 1.3;
        private const double CycleSeconds = 12.0;
        private const int AmbientFrameRate = 24;
        private const byte PeakAlpha = 0x4A;

        // The whole band rests before the brush's 0 mark, so the card renders as it shipped.
        private static readonly double[] RestOffsets = { -0.34, -0.17, 0.0 };
        private const double Travel = 1.40;

        private readonly GradientStop[] _stops;
        private readonly LinearGradientBrush _brush;
        private readonly double _cornerRadius;
        private long _cycleStart;
        private bool _running;

        public CardSheenAdorner(Control adorned, double cornerRadius) : base(adorned, AmbientFrameRate)
        {
            _cornerRadius = Math.Max(0, cornerRadius);
            _stops = new[]
            {
                new GradientStop(Colors.Transparent, RestOffsets[0]),
                new GradientStop(Colors.Transparent, RestOffsets[1]),
                new GradientStop(Colors.Transparent, RestOffsets[2]),
            };
            // Angled: a band that leans reads as light across glass, a straight one as a loading bar.
            _brush = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 0.65, RelativeUnit.Relative),
                GradientStops = new GradientStops { _stops[0], _stops[1], _stops[2] },
            };
            ApplyTint();
        }

        /// <summary>Builds and starts a sheen; null when the card has no adorner layer yet.</summary>
        public static CardSheenAdorner? Attach(Control? card, double cornerRadius)
        {
            if (card == null) return null;
            try
            {
                var sheen = new CardSheenAdorner(card, cornerRadius);
                if (!sheen.AddToLayer()) return null;
                sheen.Start();
                return sheen;
            }
            catch { return null; }
        }

        /// <summary>Stops the clock and takes the sheen off its layer. Null-safe, idempotent.</summary>
        public static void Detach(CardSheenAdorner? sheen)
        {
            try { sheen?.Stop(); sheen?.RemoveFromLayer(); } catch { }
        }

        /// <summary>The middle (peak) stop's offset. Test seam.</summary>
        internal double BandOffset => _stops[1].Offset;

        private void ApplyTint()
        {
            var tint = AmbientFxCanvas.Env.GlowColor;
            var clear = Color.FromArgb(0, tint.R, tint.G, tint.B);
            _stops[0].Color = clear;
            _stops[1].Color = Color.FromArgb(PeakAlpha, tint.R, tint.G, tint.B);
            _stops[2].Color = clear;
        }

        public void Start()
        {
            ApplyTint();
            _running = true;
            _cycleStart = Time.GetTimestamp();
            Tick();
            InvalidateVisual();
            StartTicking();
        }

        public void Stop() { _running = false; StopTicking(); SetOffsets(0); }

        protected override void OnShownChanged(bool shown)
        {
            if (shown && _running) StartTicking(); else StopTicking();
        }

        internal override void Tick()
        {
            if (!_running) return;
            double t = Time.GetElapsedTime(_cycleStart).TotalSeconds % CycleSeconds;
            SetOffsets(t < TravelSeconds ? t / TravelSeconds : 0);
        }

        private void SetOffsets(double k)
        {
            double shift = k * Travel;
            if (Math.Abs(_stops[1].Offset - (RestOffsets[1] + shift)) < 1e-6) return;   // at rest: no repaint
            for (int i = 0; i < _stops.Length; i++) _stops[i].Offset = RestOffsets[i] + shift;
            InvalidateVisual();
        }

        public override void Render(DrawingContext context)
        {
            var size = Adorned.Bounds.Size;
            if (size.Width <= 0 || size.Height <= 0) return;
            context.DrawRectangle(_brush, null, new Rect(size), _cornerRadius, _cornerRadius);
        }
    }
}
