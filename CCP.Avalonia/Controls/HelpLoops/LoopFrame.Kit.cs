using System;
using Avalonia;
using Avalonia.Media;
using static ConditioningControlPanel.Avalonia.Controls.HelpLoops.LoopMath;

namespace ConditioningControlPanel.Avalonia.Controls.HelpLoops
{
    /// <summary>
    /// Wave 2 shared kit (WPF Controls/HelpLoops/LoopFrame.Kit.cs): typed text with a caret, an eye,
    /// a gaze dot and its dwell ring, a level meter. All draw on <see cref="Front"/>.
    /// </summary>
    public sealed partial class LoopFrame
    {
        private static readonly IBrush InputFill = LoopPalette.Solid("#0f0b1c");
        private static readonly IBrush EyeWhite = LoopPalette.Solid("#f3ecff");
        private static readonly IBrush EyePupil = LoopPalette.Solid("#1a1030");
        private static readonly IBrush EyeGlint = LoopPalette.Solid("#ffffffdd");
        private static readonly IBrush GazeCore = LoopPalette.Solid("#ffffffee");
        private static readonly IBrush RingRest = LoopPalette.Solid("#ffffff26");
        private static readonly IBrush MeterTrack = LoopPalette.Solid("#0d0a18e6");

        // ---------------------------------------------------------------- typed text

        /// <summary>How much of <paramref name="text"/> is typed at progress k (0..1), whole characters.</summary>
        public static string Typed(string text, double k) =>
            string.IsNullOrEmpty(text) ? "" : text[..(int)Math.Floor(Clamp(k) * text.Length + 1e-9)];

        /// <summary>An input field with the first k of <paramref name="text"/> typed in mono and a
        /// blinking accent caret after it (solid while typing). <paramref name="box"/> false draws
        /// the text and caret only. Returns the caret's x.</summary>
        public double TypeInto(Rect field, string text, double k, double t, bool caret = true,
            IBrush? brush = null, IBrush? border = null, double size = 13, bool box = true)
        {
            var dc = Front;
            if (box)
                dc.DrawRoundedRectangle(InputFill, new Pen(border ?? P.Border, 1), Inset(field, .5), 6.5, 6.5);
            var shown = Typed(text, k);
            double x = field.X + (box ? 10 : 0);
            double midY = field.Y + field.Height / 2;
            if (shown.Length > 0)
            {
                var ft = Format(shown, size, brush ?? P.Text, Mono, FontWeight.Medium);
                dc.DrawText(ft, new Point(x, midY - ft.Height / 2));
                x += ft.WidthIncludingTrailingWhitespace;
            }
            if (caret)
            {
                bool typing = k > 0 && k < 1;
                bool on = typing || (int)Math.Floor(t / 400) % 2 != 0;
                using (Fade(dc, on ? 1 : .2))
                    dc.DrawRectangle(P.Accent, null, new Rect(x + 1, midY - size * .58, 2, size * 1.15));
            }
            return x;
        }

        // ---------------------------------------------------------------- eye

        /// <summary>An almond eye centred on (x,y), 44x24 at scale 1. openK 1 = open, 0 = shut.
        /// lookDx/lookDy in -1..1 move the iris inside the white.</summary>
        public void Eye(double x, double y, double openK, double lookDx = 0, double lookDy = 0, double scale = 1)
        {
            var dc = Front;
            double w = 22 * scale, h = 12 * scale * Clamp(openK);
            var lidPen = new Pen(P.Lilac, 2 * scale) { LineCap = PenLineCap.Round };
            if (h < .8)
            {
                // Shut: one lash line with a slight droop.
                var g = new StreamGeometry();
                using (var c = g.Open())
                {
                    c.BeginFigure(new Point(x - w, y), false);
                    c.QuadraticBezierTo(new Point(x, y + 5 * scale), new Point(x + w, y));
                    c.EndFigure(false);
                }
                dc.DrawGeometry(null, lidPen, g);
                return;
            }
            var almond = new StreamGeometry();
            using (var c = almond.Open())
            {
                c.BeginFigure(new Point(x - w, y), true);
                c.QuadraticBezierTo(new Point(x, y - h * 2), new Point(x + w, y));
                c.QuadraticBezierTo(new Point(x, y + h * 2), new Point(x - w, y));
                c.EndFigure(true);
            }
            dc.DrawGeometry(EyeWhite, null, almond);
            using (ClipTo(dc, almond))
            {
                double ir = 8 * scale;
                var ic = new Point(x + Clamp(lookDx, -1, 1) * w * .45, y + Clamp(lookDy, -1, 1) * 5 * scale);
                dc.DrawEllipse(P.Accent, null, ic, ir, ir);
                dc.DrawEllipse(EyePupil, null, ic, ir * .48, ir * .48);
                dc.DrawEllipse(EyeGlint, null, new Point(ic.X + ir * .35, ic.Y - ir * .35), ir * .2, ir * .2);
            }
            dc.DrawGeometry(null, lidPen, almond);
        }

        // ---------------------------------------------------------------- gaze

        /// <summary>A soft glowing gaze dot (where the webcam thinks you look), centred on (x,y).</summary>
        public void GazeDot(double x, double y, double opacity = 1)
        {
            if (opacity <= 0) return;
            var dc = Front;
            using (Fade(dc, opacity))
            {
                for (int i = 3; i >= 1; i--)
                    using (Fade(dc, .14))
                        dc.DrawEllipse(P.Accent, null, new Point(x, y), 5 + i * 4, 5 + i * 4);
                dc.DrawEllipse(P.Accent, null, new Point(x, y), 5.5, 5.5);
                dc.DrawEllipse(GazeCore, null, new Point(x, y), 2.2, 2.2);
            }
        }

        /// <summary>A thin ring of radius r filling clockwise from 12 o'clock, k 0..1 (dwell progress).</summary>
        public void DwellRing(double x, double y, double r, double k, IBrush? brush = null, double thickness = 3)
        {
            var dc = Front;
            var c = new Point(x, y);
            dc.DrawEllipse(null, new Pen(RingRest, thickness), c, r, r);
            k = Clamp(k);
            if (k <= .002) return;
            var pen = new Pen(brush ?? P.Mint, thickness) { LineCap = PenLineCap.Round };
            if (k >= .998) { dc.DrawEllipse(null, pen, c, r, r); return; }
            var a = (k * 360 - 90) * Math.PI / 180;
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                ctx.BeginFigure(new Point(c.X, c.Y - r), false);
                ctx.ArcTo(new Point(c.X + Math.Cos(a) * r, c.Y + Math.Sin(a) * r), new Size(r, r), 0, k > .5, SweepDirection.Clockwise);
                ctx.EndFigure(false);
            }
            dc.DrawGeometry(null, pen, g);
        }

        /// <summary>conic-gradient(color deg, rest): a filled pie from 12 o'clock, clockwise.</summary>
        public static void Sweep(DrawingContext dc, Point c, double r, double deg, IBrush brush)
        {
            if (deg <= 0.5) return;
            if (deg >= 359.5) { dc.DrawEllipse(brush, null, c, r, r); return; }
            var a = (deg - 90) * Math.PI / 180;
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                ctx.BeginFigure(c, true);
                ctx.LineTo(new Point(c.X, c.Y - r));
                ctx.ArcTo(new Point(c.X + Math.Cos(a) * r, c.Y + Math.Sin(a) * r), new Size(r, r), 0, deg > 180, SweepDirection.Clockwise);
                ctx.EndFigure(true);
            }
            dc.DrawGeometry(brush, null, g);
        }

        // ---------------------------------------------------------------- meter

        /// <summary>A horizontal level bar in r: dark track, rounded fill to v (0..1). With a label,
        /// a mono caption sits above the bar's left end (r stays the bar itself).</summary>
        public void Meter(Rect r, double v, IBrush? fill = null, string? label = null, double opacity = 1)
        {
            if (opacity <= 0) return;
            var dc = Front;
            using (Fade(dc, opacity))
            {
                double rad = r.Height / 2;
                dc.DrawRoundedRectangle(MeterTrack, new Pen(P.Border, 1), Inset(r, -.5), rad + .5, rad + .5);
                var w = r.Width * Clamp(v);
                if (w > .5) dc.DrawRoundedRectangle(fill ?? P.Accent, null, new Rect(r.X, r.Y, Math.Max(w, r.Height), r.Height), rad, rad);
                if (!string.IsNullOrEmpty(label))
                    DrawText(dc, label, r.X, r.Y - 14, 9, P.Dim, Mono, FontWeight.Medium, TextAlignment.Left);
            }
        }
    }
}
