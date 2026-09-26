using System;
using System.Windows;
using System.Windows.Media;
using static ConditioningControlPanel.Controls.HelpLoops.LoopMath;

namespace ConditioningControlPanel.Controls.HelpLoops
{
    /// <summary>Drawing bits the four webcam loops share (eye, gaze dot, dwell ring).</summary>
    internal static class WebcamKit
    {
        private static readonly Brush Sclera = LoopPalette.Solid("#f3ecff");
        private static readonly Brush Pupil = LoopPalette.Solid("#1a1030");
        private static readonly Brush Shine = LoopPalette.Solid("#ffffffdd");
        private static readonly Brush LidShade = LoopPalette.Solid("#b99cff55");

        private static readonly Brush PinkFill = LoopPalette.Freeze(new LinearGradientBrush(
            LoopPalette.Css("#ff8fc8"), LoopPalette.Css("#9b5cff"), new Point(0, 0), new Point(1, 1)));
        private static readonly Brush PinkShine = LoopPalette.Freeze(new RadialGradientBrush
        {
            Center = new Point(.4, .35),
            GradientOrigin = new Point(.4, .35),
            RadiusX = .85,
            RadiusY = .85,
            GradientStops =
            {
                new GradientStop(LoopPalette.Css("#ffffff99"), 0),
                new GradientStop(LoopPalette.Css("#ffffff00"), .6),
            }
        });
        private static readonly Brush SeaFill = LoopPalette.Freeze(new LinearGradientBrush(
            LoopPalette.Css("#5fffd0"), LoopPalette.Css("#2b6bff"), new Point(.33, .03), new Point(.67, .97)));
        private static readonly Brush SeaShade = LoopPalette.Freeze(new LinearGradientBrush(
            LoopPalette.Css("#00000000"), LoopPalette.Css("#00000066"), new Point(0, 0), new Point(0, 1)));
        private static readonly Brush StripeA = LoopPalette.Solid("#ff6fb5");
        private static readonly Brush StripeB = LoopPalette.Solid("#2b124a");

        /// <summary>A photo: white 3px frame, rounded 4, a drop shadow faked as darker stacked rects.</summary>
        public static void Photo(LoopFrame f, DrawingContext dc, Rect r, int look)
        {
            LoopFrame.SoftShadow(dc, r, 4, 10, 22, 0xAA);
            dc.DrawRoundedRectangle(Brushes.White, null, r, 4, 4);
            var inner = LoopFrame.Inset(r, 3);
            using (f.ClipTo(dc, new RectangleGeometry(inner, 1.5, 1.5)))
            {
                switch (((look % 3) + 3) % 3)
                {
                    case 0:
                        dc.DrawRectangle(PinkFill, null, inner);
                        dc.DrawEllipse(PinkShine, null,
                            new Point(inner.X + inner.Width * .5, inner.Y + inner.Height * .5),
                            inner.Width * .2, inner.Height * .22);
                        break;
                    case 1:
                        dc.DrawRectangle(StripeB, null, inner);
                        DrawWedges(dc, inner);
                        break;
                    default:
                        dc.DrawRectangle(SeaFill, null, inner);
                        dc.DrawRectangle(SeaShade, null, new Rect(inner.X, inner.Bottom - inner.Height * .4, inner.Width, inner.Height * .4));
                        break;
                }
            }
        }

        /// <summary>repeating-conic-gradient: 18 wedges of 20 degrees, pink every other one, from 12 o'clock.</summary>
        private static void DrawWedges(DrawingContext dc, Rect r)
        {
            var c = new Point(r.X + r.Width / 2, r.Y + r.Height / 2);
            double rad = Math.Sqrt(r.Width * r.Width + r.Height * r.Height);
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                for (int i = 0; i < 18; i += 2)
                {
                    double a0 = (i * 20 - 90) * Math.PI / 180, a1 = ((i + 1) * 20 - 90) * Math.PI / 180;
                    ctx.BeginFigure(c, true, true);
                    ctx.LineTo(new Point(c.X + Math.Cos(a0) * rad, c.Y + Math.Sin(a0) * rad), false, false);
                    ctx.LineTo(new Point(c.X + Math.Cos(a1) * rad, c.Y + Math.Sin(a1) * rad), false, false);
                }
            }
            g.Freeze();
            dc.DrawGeometry(StripeA, null, g);
        }

        /// <summary>
        /// An almond eye centred on (x,y), 44 px wide at scale 1. <paramref name="open"/> 0 = shut
        /// (a lid line with lashes), 1 = wide. <paramref name="lookX"/>/<paramref name="lookY"/> in
        /// -1..1 move the iris.
        /// </summary>
        public static void Eye(LoopFrame f, double x, double y, double open, double lookX, double lookY, double scale = 1)
        {
            var dc = f.Front;
            open = Clamp(open);
            using (f.At(dc, x, y, scale))
            {
                double hw = 22, up = 22 * Math.Max(open, .06), dn = 18 * Math.Max(open, .06);
                var shape = new StreamGeometry();
                using (var c = shape.Open())
                {
                    c.BeginFigure(new Point(x - hw, y), true, true);
                    c.QuadraticBezierTo(new Point(x, y - up), new Point(x + hw, y), true, true);
                    c.QuadraticBezierTo(new Point(x, y + dn), new Point(x - hw, y), true, true);
                }
                shape.Freeze();

                if (open > .12)
                {
                    dc.DrawGeometry(Sclera, null, shape);
                    using (f.ClipTo(dc, shape))
                    {
                        var ix = x + 9 * Clamp(lookX, -1, 1);
                        var iy = y - 1 + 4 * Clamp(lookY, -1, 1);
                        dc.DrawEllipse(f.P.Accent, null, new Point(ix, iy), 8.5, 8.5);
                        dc.DrawEllipse(Pupil, null, new Point(ix, iy), 4, 4);
                        dc.DrawEllipse(Shine, null, new Point(ix - 2.6, iy - 2.8), 1.8, 1.8);
                        // The upper lid's shadow.
                        dc.DrawRectangle(LidShade, null, new Rect(x - hw, y - up / 2 - 12, hw * 2, 12 * (1 - open) + 3));
                    }
                }
                dc.DrawGeometry(null, new Pen(f.P.Lilac, 1.6) { LineJoin = PenLineJoin.Round }, shape);

                // Lashes on the upper lid, drooping as it closes.
                var lash = new Pen(f.P.Lilac, 1.4) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
                for (int i = -1; i <= 1; i++)
                {
                    double u = .5 + i * .28;
                    double lx = x - hw + 2 * hw * u;
                    double ly = y - up * 2 * u * (1 - u);
                    double dir = open < .3 ? 1 : -1;
                    dc.DrawLine(lash, new Point(lx, ly), new Point(lx + i * 3, ly + dir * 5));
                }
            }
        }

        /// <summary>The soft gaze dot: a halo, a core and a white centre. Scale 1 is 11 px of halo.</summary>
        public static void GazeDot(LoopFrame f, double x, double y, double scale, Brush color)
        {
            if (scale <= 0.01) return;
            var dc = f.Front;
            using (f.At(dc, x, y, scale))
            {
                using (f.Fade(dc, .28)) dc.DrawEllipse(color, null, new Point(x, y), 11, 11);
                dc.DrawEllipse(color, null, new Point(x, y), 5, 5);
                dc.DrawEllipse(f.P.White, null, new Point(x, y), 1.8, 1.8);
            }
        }

        /// <summary>A dwell ring: a dim track and an arc from 12 o'clock filling clockwise to k.</summary>
        public static void Ring(LoopFrame f, double x, double y, double r, double k, Brush color, double opacity = 1)
        {
            if (opacity <= 0) return;
            var dc = f.Front;
            using (f.Fade(dc, opacity))
            {
                dc.DrawEllipse(null, new Pen(f.P.Track, 2.5), new Point(x, y), r, r);
                k = Clamp(k);
                if (k <= .002) return;
                var pen = new Pen(color, 3) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
                if (k >= .999) { dc.DrawEllipse(null, pen, new Point(x, y), r, r); return; }
                var a = (k * 360 - 90) * Math.PI / 180;
                var g = new StreamGeometry();
                using (var c = g.Open())
                {
                    c.BeginFigure(new Point(x, y - r), false, false);
                    c.ArcTo(new Point(x + Math.Cos(a) * r, y + Math.Sin(a) * r), new Size(r, r), 0, k > .5,
                        SweepDirection.Clockwise, true, false);
                }
                g.Freeze();
                dc.DrawGeometry(null, pen, g);
            }
        }

        /// <summary>A horizontal level bar: dim track, coloured fill to v.</summary>
        public static void Meter(LoopFrame f, Rect r, double v, Brush fill)
        {
            var dc = f.Front;
            dc.DrawRoundedRectangle(f.P.Track, null, r, r.Height / 2, r.Height / 2);
            v = Clamp(v);
            if (v > .005)
                dc.DrawRoundedRectangle(fill, null, new Rect(r.X, r.Y, Math.Max(r.Height, r.Width * v), r.Height), r.Height / 2, r.Height / 2);
        }

        /// <summary>Pop droplets around (x,y), k 0..1 over the burst.</summary>
        public static void Burst(LoopFrame f, double x, double y, double k, Brush color)
        {
            if (k <= 0 || k >= 1) return;
            var dc = f.Front;
            using (f.Fade(dc, 1 - k))
                for (int m = 0; m < 6; m++)
                {
                    var a = m / 6.0 * Math.PI * 2;
                    var d = 26 * EaseOut(k);
                    dc.DrawEllipse(color, null, new Point(x + Math.Cos(a) * d, y + Math.Sin(a) * d), 3, 3);
                }
        }
    }
}
