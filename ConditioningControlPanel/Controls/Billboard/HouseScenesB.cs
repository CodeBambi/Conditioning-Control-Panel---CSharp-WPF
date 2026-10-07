using System;
using System.Windows;
using System.Windows.Media;

namespace ConditioningControlPanel.Controls.Billboard
{
    /// <summary>
    /// House card The Loom ("spin a spiral of your own"). A bobbin on a bracket feeds thread down to
    /// a turning disc; the guide bead walks outward while the disc spins, so the thread lays itself
    /// into a spiral. When the bobbin runs low the thread snips, the finished spiral spins up with a
    /// pulse running out along it, lifts off as a ghost, and the bobbin refills in the other colour.
    /// One loop is seven seconds. The disc, stand, hub and spiral path are cached; a frame only
    /// transforms them and reveals the spiral with a dash length.
    /// </summary>
    public sealed class LoomHouseArt : BillboardVectorArt
    {
        private const double Period = 7.0;
        private const double Lead = 2.2;      // the still frame (1.6 s) lands mostly wound
        private const double WindFrom = 0.5, WindTo = 4.6, LiftFrom = 5.6, LiftTo = 6.4;
        private const double Turns = 3.2;
        private const double LiveSpin = 2.94; // disc speed as the winding ends (rad/s), carried on
        private static readonly double Phi0 = -38 * Math.PI / 180; // where the guide feeds, up and right
        private static readonly Color Ink = Color.FromRgb(4, 4, 12);
        private static readonly Color Rose = Color.FromRgb(0xff, 0x4f, 0xa8);

        private Geometry? _spiral;
        private double _spiralRd = -1, _spiralLen, _r0, _r1;
        private const int SpiralSteps = 260;
        private readonly double[] _arc = new double[SpiralSteps + 1]; // arc length up to each point

        protected override void Paint(DrawingContext dc, double w, double h, double t)
        {
            CachedGround(dc, w, h, Deep(Accent));
            Motes(dc, w, h, t, Accent, 12, 41, new Rect(0.5, 0.06, 0.48, 0.9), 0.05, 0.005, 0.4);

            double u = (t + Lead) / Period;
            int cycle = (int)Math.Floor(u);
            double p = (u - cycle) * Period;
            var thread = cycle % 2 == 0 ? Mix(Accent, Colors.White, 0.3) : Mix(Rose, Colors.White, 0.14);

            double rd = h * 0.36;
            EnsureSpiral(rd);
            double kIn = BackOut(t / 0.9), kBob = BackOut((t - 0.2) / 0.8);
            var c = new Point(w * 0.69, h * 0.54 + Bob(t, h * 0.005));
            var bc = new Point(w * 0.905, h * 0.16 + Bob(t, h * 0.008, 1.3) - (1 - kBob) * h * 0.5);

            // Fill: how much of the spiral is laid. Mostly even, softened at both ends.
            double x = Math.Clamp((p - WindFrom) / (WindTo - WindFrom), 0, 1);
            double k = p < WindFrom ? 0 : 0.6 * x + 0.4 * x * x * (3 - 2 * x);
            double after = p - WindTo;
            double theta = Phi0 + k * Turns * Math.PI * 2 + (after > 0 ? LiveSpin * after + 1.4 * after * after : 0);
            double kick = after > 0 ? 1 + 0.05 * Math.Exp(-after * 6) * Math.Sin(after * 18) : 1;
            double lift = Math.Clamp((p - LiftFrom) / (LiftTo - LiftFrom), 0, 1);

            // The stand behind the disc, then the disc itself.
            dc.PushOpacity(Math.Clamp(kIn * 1.3, 0, 1));
            dc.PushTransform(new TranslateTransform(c.X, c.Y));
            dc.DrawDrawing(Layer("stand", w, h, d => PaintStand(d, h)));
            dc.Pop();
            double ds = kIn * kick;
            dc.PushTransform(new MatrixTransform(Pose(c, 0, ds)));
            dc.DrawDrawing(Layer("disc", w, h, d => PaintDisc(d, rd, h)));
            dc.Pop();

            // The spiral, turning with the disc. It rides the disc's own transform, so the tip of
            // the laid thread always sits under the guide bead.
            var spin = Pose(c, theta, ds * (1 + 0.32 * EaseOut(lift)));
            if (p >= WindFrom && p < LiftTo && _spiral != null)
            {
                double laid = LaidLength(k);
                double th = h * 0.024;
                dc.PushOpacity(1 - lift);
                // Shadow: the same path down and right, not turning the light with it.
                var shade = spin;
                shade.Translate(h * 0.01, h * 0.016);
                dc.PushTransform(new MatrixTransform(shade));
                dc.DrawGeometry(null, Reveal(Ink, th * 1.15, 0.42, laid), _spiral);
                dc.Pop();
                dc.PushTransform(new MatrixTransform(spin));
                dc.DrawGeometry(null, Reveal(Mix(thread, Ink, 0.25), th, 1, laid), _spiral);
                dc.DrawGeometry(null, Reveal(thread, th * 0.62, 1, laid), _spiral);
                dc.DrawGeometry(null, Reveal(Colors.White, th * 0.22, 0.5, laid), _spiral);
                // Done: a bright pulse runs out along the finished spiral, twice.
                if (after > 0.1)
                {
                    for (int i = 0; i < 2; i++)
                    {
                        double q = (after - 0.1 - i * 0.42) / 0.75;
                        if (q <= 0 || q >= 1) continue;
                        dc.DrawGeometry(null, Pulse(th * 0.7, EaseOut(q) * _spiralLen, th * 6, Math.Sin(q * Math.PI)), _spiral);
                    }
                }
                dc.Pop();
                dc.Pop();
            }

            // Hub on top: a raised knob that never turns its light.
            dc.PushTransform(new MatrixTransform(Pose(c, 0, ds)));
            dc.DrawDrawing(Layer("hub", w, h, d => PaintHub(d, h)));
            dc.Pop();
            if (after > 0 && after < 1.2)
                Sheen(dc, new EllipseGeometry(c, rd * ds, rd * ds), new Rect(c.X - rd, c.Y - rd, rd * 2, rd * 2), after, 3, 0, 0.55);

            // The bobbin: full at the start of a loop, running down as the spiral is laid.
            double rc = h * 0.024, full = h * 0.084;
            double rw = p < 0.45
                ? rc + (full - rc) * Math.Clamp(BackOut(p / 0.45, 2.2), 0, 1.08)
                : full - (full - rc) * 0.9 * k;
            var feed = new Point(bc.X - h * 0.03, bc.Y + rw);

            // The thread: the bead drops from the bobbin to the hub, rides the tip while it winds,
            // and on the snip the loose end whips back up to the bobbin.
            // The free end of what is laid so far: the same k that sets the reveal length.
            var tip = spin.Transform(TipLocal(k));
            Point? bead = null;
            if (p >= 0.2 && p < WindFrom)
            {
                var start = Pose(c, Phi0, ds).Transform(new Point(_r0, 0));
                double d = EaseOut((p - 0.2) / 0.3);
                bead = new Point(feed.X + (start.X - feed.X) * d, feed.Y + (start.Y - feed.Y) * d);
            }
            else if (p >= WindFrom && p < WindTo) bead = tip;
            else if (after >= 0 && after < 0.32)
            {
                double d = EaseOut(after / 0.32);
                var end = tip; // the finished spiral's end, still turning with the disc
                bead = new Point(end.X + (feed.X - end.X) * d, end.Y + (feed.Y - end.Y) * d);
            }
            if (bead is Point b && kBob > 0.6)
            {
                double buzz = p >= WindFrom && p < WindTo ? Math.Sin(t * 47) * h * 0.003 : 0;
                double sag = h * 0.02 * (after > 0 ? 1 - after / 0.32 : 1);
                var mid = new Point((b.X + feed.X) / 2 + buzz, (b.Y + feed.Y) / 2 + sag);
                var line = new StreamGeometry();
                using (var ctx = line.Open())
                {
                    ctx.BeginFigure(feed, false, false);
                    ctx.QuadraticBezierTo(mid, b, true, false);
                }
                line.Freeze();
                dc.DrawGeometry(null, Stroke(Mix(thread, Ink, 0.3), h * 0.008), line);
                dc.DrawGeometry(null, Stroke(thread, h * 0.0045), line);
                // The guide bead: a small raised ring with a lit edge.
                double br = h * 0.017;
                dc.DrawEllipse(Fill(Ink, 0.4), null, new Point(b.X + br * 0.35, b.Y + br * 0.5), br, br);
                dc.DrawEllipse(Fill(Mix(Accent, Colors.White, 0.55)), Stroke(Mix(Accent, Ink, 0.4), h * 0.004), b, br, br);
                dc.DrawEllipse(Fill(Colors.White, 0.8), null, new Point(b.X - br * 0.35, b.Y - br * 0.35), br * 0.32, br * 0.32);
                if (p >= WindFrom && p < WindTo)
                {
                    double g = (p * 2.3) % 1;
                    if (g < 0.4) Spark(dc, new Point(b.X + h * 0.02, b.Y - h * 0.02), h * 0.022 * Math.Sin(g / 0.4 * Math.PI), Colors.White, 0.85, g * 3);
                }
            }

            dc.PushOpacity(Math.Clamp(kBob * 1.3, 0, 1));
            PaintBobbin(dc, w, h, bc, rw, rc, thread, k * h * 2.6 - (p < 0.45 ? p * h * 0.5 : 0));
            dc.Pop();
            dc.Pop();

            // The snip: glints off the tip; the lift: a ring of glints off the rim.
            if (after >= 0 && after < 0.6) Burst(dc, Pose(c, Phi0, ds).Transform(new Point(_r1, 0)), h * 0.05, h * 0.15, after / 0.6, thread, 7, 3);
            if (lift > 0 && lift < 1) Burst(dc, c, rd * 0.75, rd * 1.25, lift, thread, 10, 9);
            Twinkles(dc, w, h, t, Mix(Accent, Colors.White, 0.4), 4, 43, new Rect(0.5, 0.1, 0.46, 0.8), 0.026);
            Motes(dc, w, h, t, Colors.White, 5, 47, new Rect(0.55, 0.2, 0.42, 0.8), 0.1, 0.008, 0.3);
        }

        private static Matrix Pose(Point c, double radians, double scale)
        {
            var m = Matrix.Identity;
            m.Scale(scale, scale);
            m.Rotate(radians * 180 / Math.PI);
            m.Translate(c.X, c.Y);
            return m;
        }

        /// <summary>The spiral the thread lays, in disc coordinates, from the hub outward.</summary>
        private void EnsureSpiral(double rd)
        {
            if (_spiral != null && Math.Abs(_spiralRd - rd) < 0.01) return;
            _spiralRd = rd;
            _r0 = rd * 0.13;
            _r1 = rd * 0.86;
            var g = new StreamGeometry();
            double len = 0;
            Point prev = default;
            using (var ctx = g.Open())
            {
                for (int i = 0; i <= SpiralSteps; i++)
                {
                    var pt = SpiralPoint(i / (double)SpiralSteps);
                    if (i == 0) ctx.BeginFigure(pt, false, false);
                    else
                    {
                        ctx.LineTo(pt, true, true);
                        len += (pt - prev).Length;
                    }
                    _arc[i] = len;
                    prev = pt;
                }
            }
            g.Freeze();
            _spiral = g;
            _spiralLen = len;
        }

        /// <summary>A point on the spiral at parameter <paramref name="s"/> (0 hub, 1 rim), disc coordinates.</summary>
        private Point SpiralPoint(double s)
        {
            double a = -s * Turns * Math.PI * 2, r = _r0 + (_r1 - _r0) * s;
            return new Point(Math.Cos(a) * r, Math.Sin(a) * r);
        }

        /// <summary>
        /// Where the free end of the laid thread is at fill <paramref name="k"/>: the polyline's own
        /// point, so it sits exactly where the dash of <see cref="LaidLength"/> ends.
        /// </summary>
        private Point TipLocal(double k)
        {
            double f = Math.Clamp(k, 0, 1) * SpiralSteps;
            int i = Math.Min((int)Math.Floor(f), SpiralSteps - 1);
            var a = SpiralPoint(i / (double)SpiralSteps);
            var b = SpiralPoint((i + 1) / (double)SpiralSteps);
            double r = f - i;
            return new Point(a.X + (b.X - a.X) * r, a.Y + (b.Y - a.Y) * r);
        }

        /// <summary>
        /// The length of thread laid at fill <paramref name="k"/>, measured along the path. The arc
        /// of a spiral grows with its radius, so this is never <c>k * length</c>: that ran the reveal
        /// past the bead by about half a turn.
        /// </summary>
        private double LaidLength(double k)
        {
            double f = Math.Clamp(k, 0, 1) * SpiralSteps;
            int i = Math.Min((int)Math.Floor(f), SpiralSteps - 1);
            return _arc[i] + (_arc[i + 1] - _arc[i]) * (f - i);
        }

        /// <summary>Tests: the bead's point and the end of the revealed path at fill <paramref name="k"/>,
        /// both in disc coordinates for a card <paramref name="h"/> tall. The path end is measured by WPF
        /// along the geometry itself, the way the dash is.</summary>
        internal (Point Bead, Point PathEnd) TipForTests(double k, double h)
        {
            EnsureSpiral(h * 0.36);
            var path = PathGeometry.CreateFromGeometry(_spiral);
            path.GetPointAtFractionLength(Math.Clamp(LaidLength(k) / _spiralLen, 0, 1), out var end, out _);
            return (TipLocal(k), end);
        }

        /// <summary>A pen that draws only the first <paramref name="laid"/> px of a path.</summary>
        private Pen Reveal(Color c, double th, double a, double laid)
        {
            var pen = new Pen(Fill(c, a), th) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, DashCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
            if (laid < _spiralLen - 0.5) pen.DashStyle = new DashStyle(new[] { Math.Max(0.001, laid / th), 1e5 }, 0);
            pen.Freeze();
            return pen;
        }

        /// <summary>A pen that lights one stretch of a path, ending <paramref name="at"/> px along it.</summary>
        private Pen Pulse(double th, double at, double length, double a)
        {
            var pen = new Pen(Fill(Colors.White, 0.8 * a), th) { DashCap = PenLineCap.Round, StartLineCap = PenLineCap.Flat, EndLineCap = PenLineCap.Flat };
            pen.DashStyle = new DashStyle(new[] { 0.0, Math.Max(0.001, (at - length) / th), length / th, 1e5 }, 0);
            pen.Freeze();
            return pen;
        }

        private void PaintStand(DrawingContext dc, double h)
        {
            // A post from behind the disc to a foot, so the disc stands on something.
            Plate(dc, new Rect(-h * 0.035, 0, h * 0.07, h * 0.37), h * 0.012, Fill(Mix(Accent, Ink, 0.62)), h * 0.02);
            Plate(dc, new Rect(-h * 0.17, h * 0.34, h * 0.34, h * 0.055), h * 0.02, Fill(Mix(Accent, Ink, 0.52)), h * 0.022);
        }

        private void PaintDisc(DrawingContext dc, double rd, double h)
        {
            var ink = Fill(Ink, 0.24);
            double lift = h * 0.03;
            dc.DrawEllipse(Fill(Ink, 0.22), null, new Point(lift * 0.9, lift * 1.4), rd + lift * 0.2, rd + lift * 0.2);
            dc.DrawEllipse(Fill(Ink, 0.32), null, new Point(lift * 0.45, lift * 0.7), rd, rd);
            var face = new RadialGradientBrush { GradientOrigin = new Point(0.34, 0.3), Center = new Point(0.42, 0.4), RadiusX = 0.75, RadiusY = 0.75 };
            face.GradientStops.Add(new GradientStop(Mix(Accent, Ink, 0.4), 0));
            face.GradientStops.Add(new GradientStop(Mix(Accent, Ink, 0.62), 0.7));
            face.GradientStops.Add(new GradientStop(Mix(Accent, Ink, 0.72), 1));
            face.Freeze();
            dc.DrawEllipse(face, null, new Point(0, 0), rd, rd);
            // Turned grooves: a dark line with a pale one just under it.
            var dark = new Pen(ink, Math.Max(0.6, h * 0.0025));
            var pale = new Pen(Fill(Colors.White, 0.05), Math.Max(0.6, h * 0.002));
            for (int i = 0; i < 6; i++)
            {
                double r = rd * (0.22 + i * 0.13);
                dc.DrawEllipse(null, dark, new Point(0, 0), r, r);
                dc.DrawEllipse(null, pale, new Point(0, h * 0.003), r, r);
            }
            double th = Math.Max(1.2, h * 0.012);
            dc.DrawEllipse(null, Bevel(th), new Point(0, 0), rd - th / 2, rd - th / 2);
            // A pressed groove round the rim the thread never crosses.
            dc.DrawEllipse(null, Bevel(Math.Max(0.8, h * 0.006), pressed: true), new Point(0, 0), rd * 0.92, rd * 0.92);
        }

        private void PaintHub(DrawingContext dc, double h)
        {
            double r = h * 0.05;
            dc.DrawEllipse(Fill(Ink, 0.35), null, new Point(h * 0.008, h * 0.012), r, r);
            var face = new RadialGradientBrush { GradientOrigin = new Point(0.32, 0.28), Center = new Point(0.4, 0.38), RadiusX = 0.7, RadiusY = 0.7 };
            face.GradientStops.Add(new GradientStop(Mix(Accent, Colors.White, 0.5), 0));
            face.GradientStops.Add(new GradientStop(Accent, 0.55));
            face.GradientStops.Add(new GradientStop(Mix(Accent, Ink, 0.4), 1));
            face.Freeze();
            dc.DrawEllipse(face, null, new Point(0, 0), r, r);
            dc.DrawEllipse(null, Bevel(Math.Max(1, h * 0.006)), new Point(0, 0), r - h * 0.003, r - h * 0.003);
            dc.DrawEllipse(Fill(Mix(Accent, Ink, 0.6)), Bevel(Math.Max(0.8, h * 0.004), pressed: true), new Point(0, 0), r * 0.32, r * 0.32);
        }

        /// <summary>The bobbin side on, on a bracket from the card's edge: two flanges and the wound thread between.</summary>
        private void PaintBobbin(DrawingContext dc, double w, double h, Point bc, double rw, double rc, Color thread, double roll)
        {
            double len = h * 0.24, rf = h * 0.1, fw = h * 0.032;
            // The bracket the axle rides on.
            Plate(dc, new Rect(bc.X, bc.Y - h * 0.018, w - bc.X + h * 0.05, h * 0.036), h * 0.012, Fill(Mix(Accent, Ink, 0.6)), h * 0.016);
            Shadow(dc, new Rect(bc.X - len / 2, bc.Y - rf, len, rf * 2), fw * 0.5, h * 0.022);

            // Wound thread, with slanted wraps that roll as the thread pays out.
            var body = new Rect(bc.X - len / 2 + fw * 0.6, bc.Y - rw, len - fw * 1.2, rw * 2);
            dc.DrawRectangle(Fill(Mix(Accent, Ink, 0.5)), null, new Rect(bc.X - len / 2, bc.Y - rc, len, rc * 2));
            dc.DrawRoundedRectangle(Fill(thread), null, body, rw * 0.35, rw * 0.35);
            dc.PushClip(new RectangleGeometry(body, rw * 0.35, rw * 0.35));
            double pitch = h * 0.016, off = ((roll % pitch) + pitch) % pitch;
            var wrap = Stroke(Mix(thread, Ink, 0.3), Math.Max(0.6, h * 0.003), 0.55);
            for (double xx = body.Left - rw * 2 + off; xx < body.Right + rw; xx += pitch)
                dc.DrawLine(wrap, new Point(xx, body.Top), new Point(xx + rw * 0.5, body.Bottom));
            dc.DrawRectangle(Fill(Colors.White, 0.26), null, new Rect(body.Left, body.Top + rw * 0.18, body.Width, rw * 0.3));
            dc.DrawRectangle(Fill(Ink, 0.28), null, new Rect(body.Left, body.Bottom - rw * 0.55, body.Width, rw * 0.55));
            dc.Pop();

            // The flanges, raised plates with the lamp's bevel.
            var flange = Fill(Mix(Accent, Ink, 0.35));
            Plate(dc, new Rect(bc.X - len / 2, bc.Y - rf, fw, rf * 2), fw * 0.45, flange, 0);
            Plate(dc, new Rect(bc.X + len / 2 - fw, bc.Y - rf, fw, rf * 2), fw * 0.45, flange, 0);
            dc.DrawEllipse(Fill(Mix(Accent, Colors.White, 0.4)), Bevel(1), new Point(bc.X + len / 2 - fw / 2, bc.Y), fw * 0.28, fw * 0.28);
        }

        /// <summary>Glints flung out from a point between two radii as <paramref name="k"/> runs 0 to 1.</summary>
        private void Burst(DrawingContext dc, Point c, double r0, double r1, double k, Color hue, int n, int seed)
        {
            if (!Particles) return;
            for (int i = 0; i < n; i++)
            {
                double a = i * Math.PI * 2 / n + Hash(seed, i, 1) * 0.6, d = r0 + (r1 - r0) * EaseOut(k) * (0.75 + Hash(seed, i, 2) * 0.4);
                var pt = new Point(c.X + Math.Cos(a) * d, c.Y + Math.Sin(a) * d);
                Spark(dc, pt, (r1 - r0) * 0.16 * (1 - k), i % 2 == 0 ? Colors.White : Mix(hue, Colors.White, 0.3), 1 - k, k * 3 + i);
            }
        }
    }

    /// <summary>
    /// House card Support ("the links that keep this going"). Gold coins tumble in one by one and
    /// land in a glass jar with a bounce, a glint and a thump of the heart on its label; when the
    /// pile is up, the jar warms, a heart rises out of the mouth, beats twice with a burst, and
    /// floats off while the coins melt into sparks. One loop is six and a half seconds.
    /// </summary>
    public sealed class SupportHouseArt : BillboardVectorArt
    {
        private const double Period = 6.5;
        private const double Lead = 3.0;     // the still frame (1.6 s) shows the heart up and beating
        private const int Coins = 12;
        private const double FirstLand = 0.5, Gap = 0.29, Fall = 0.55;
        private const double HeartUp = 3.95, HeartGo = 5.25, HeartGone = 6.15;
        private static readonly Color Ink = Color.FromRgb(4, 4, 12);
        private static readonly Color Rose = Color.FromRgb(0xff, 0x4f, 0xa8);

        // The pile, in card heights from the jar's inside floor: x, height, tilt (degrees).
        // Landing order goes round the floor first, then up, so the pile grows like a real one.
        private static readonly double[,] Slots =
        {
            { -0.15, 0.022, -6 }, { 0.05, 0.02, 4 }, { 0.15, 0.024, 8 }, { -0.05, 0.026, -3 },
            { 0.1, 0.05, 10 }, { -0.1, 0.052, -9 }, { 0.0, 0.056, 2 }, { 0.155, 0.05, 14 },
            { -0.06, 0.082, -12 }, { 0.06, 0.085, 7 }, { -0.135, 0.078, -16 }, { 0.0, 0.11, -3 },
        };

        private static readonly Geometry HeartShape = MakeHeart();
        private Geometry? _jar;
        private double _jarH = -1;

        protected override void Paint(DrawingContext dc, double w, double h, double t)
        {
            CachedGround(dc, w, h, Deep(Accent));
            Motes(dc, w, h, t, Accent, 12, 61, new Rect(0.5, 0.06, 0.48, 0.9), 0.05, 0.005, 0.38);

            double u = (t + Lead) / Period;
            int cycle = (int)Math.Floor(u);
            double p = (u - cycle) * Period;

            double jw = h * 0.5, jh = h * 0.6, neck = h * 0.32;
            EnsureJar(jw, jh, neck, h);
            var jar = _jar!;
            double kIn = BackOut(t / 0.85);
            var jc = new Point(w * 0.72, h * 0.585 + Bob(t, h * 0.004) + (1 - kIn) * h * 0.35);
            double top = jc.Y - jh / 2, floor = jc.Y + jh / 2 - h * 0.025;

            // The most recent landing: the jar squashes a touch and the label's heart thumps.
            double sinceLand = 9;
            for (int i = 0; i < Coins; i++)
            {
                double s = p - (FirstLand + i * Gap);
                if (s >= 0 && s < sinceLand) sinceLand = s;
            }
            double squash = sinceLand < 0.3 ? 0.03 * Math.Sin(sinceLand / 0.3 * Math.PI) * (1 - sinceLand / 0.3) : 0;
            double thump = sinceLand < 0.35 ? 0.22 * Math.Sin(sinceLand / 0.35 * Math.PI) : 0;

            // Warmth inside the jar as it fills and the heart forms; then the coins melt away.
            double warm = Math.Clamp((p - 3.2) / 0.9, 0, 1) * (1 - Math.Clamp((p - 5.2) / 0.8, 0, 1));
            double drain = Math.Clamp((p - 5.0) / 0.9, 0, 1);

            var jarM = Matrix.Identity;
            jarM.ScaleAt(1 + squash, 1 - squash, 0, jh / 2);
            jarM.Translate(jc.X, jc.Y);
            var unJar = jarM;
            unJar.Invert();

            dc.PushOpacity(Math.Clamp(kIn * 1.4, 0, 1));
            // Shadow and the glass's back wall.
            dc.DrawEllipse(Fill(Ink, 0.4), null, new Point(jc.X + h * 0.02, jc.Y + jh / 2 + h * 0.008), jw * 0.56 * (1 + squash), h * 0.03);
            var shade = jarM;
            shade.Translate(h * 0.016, h * 0.02);
            dc.PushTransform(new MatrixTransform(shade));
            dc.DrawGeometry(Fill(Ink, 0.2), null, jar);
            dc.Pop();
            dc.PushTransform(new MatrixTransform(jarM));
            dc.DrawDrawing(Layer("jarBack", w, h, d => PaintJarBack(d, jw, jh, h, jar)));
            dc.PushClip(jar);
            if (warm > 0) Glow(dc, new Point(0, jh * 0.22), jw * 0.75, Mix(Accent, Colors.White, 0.2), 0.5 * warm);

            // The pile and any coin already inside the mouth, clipped to the glass.
            dc.PushTransform(new MatrixTransform(unJar));
            double rx = h * 0.072, ry = h * 0.026;
            for (int i = 0; i < Coins; i++)
            {
                double land = FirstLand + i * Gap;
                if (p < land) continue;
                double s = p - land;
                double bounce = s < 0.3 ? Math.Abs(Math.Sin(s / 0.3 * Math.PI)) * h * 0.022 * (1 - s / 0.3) : 0;
                double melt = Melt(drain, i);
                if (melt >= 1) continue;
                var at = new Point(jc.X + Slots[i, 0] * h, floor - Slots[i, 1] * h - bounce - melt * h * 0.04);
                dc.PushOpacity(1 - melt);
                FlatCoin(dc, w, h, at, rx, ry, Slots[i, 2], 1 - melt * 0.3);
                dc.Pop();
            }
            for (int i = 0; i < Coins; i++) FallingCoin(dc, w, h, p, i, jc, top, floor, jh, insidePass: true);
            dc.Pop();
            dc.Pop();

            // The glass's front: highlights, the label, a glint now and then.
            dc.DrawDrawing(Layer("jarFront", w, h, d => PaintJarFront(d, jw, jh, neck, h, jar)));
            PaintLabel(dc, jw, jh, h, thump, warm);
            Sheen(dc, jar, new Rect(-jw / 2, -jh / 2, jw, jh), p, Period, 4.0, 0.7);
            dc.Pop();

            // Coins still above the collar, drawn over the glass; then the collar over them.
            dc.PushClip(new RectangleGeometry(new Rect(-10, -h, w + 20, top + h * 1.012)));
            for (int i = 0; i < Coins; i++) FallingCoin(dc, w, h, p, i, jc, top, floor, jh, insidePass: false);
            dc.Pop();
            dc.PushTransform(new MatrixTransform(jarM));
            dc.DrawDrawing(Layer("collar", w, h, d => PaintCollar(d, jh, neck, h)));
            dc.Pop();
            dc.Pop();

            // Landing glints off the newest coin.
            if (sinceLand < 0.45)
            {
                int last = (int)Math.Round((p - sinceLand - FirstLand) / Gap);
                if (last >= 0 && last < Coins)
                {
                    var at = new Point(jc.X + Slots[last, 0] * h, floor - Slots[last, 1] * h);
                    Burst(dc, at, h * 0.03, h * 0.09, sinceLand / 0.45, 5, last + 3, h * 0.016);
                }
            }

            // The heart: up out of the mouth, two beats, then off and away.
            if (p > HeartUp && p < HeartGone)
            {
                double up = BackOut((p - HeartUp) / 0.55, 1.9);
                double go = Math.Clamp((p - HeartGo) / (HeartGone - HeartGo), 0, 1);
                double beat = Beat(p - (HeartUp + 0.55)) + Beat(p - (HeartUp + 0.85)) * 0.7 + Beat(p - (HeartUp + 1.45)) + Beat(p - (HeartUp + 1.75)) * 0.7;
                double size = h * 0.098 * (0.25 + 0.75 * up) * (1 + 0.13 * beat) * (1 - 0.25 * go);
                var hc = new Point(jc.X + Math.Sin(p * 2.1) * h * 0.012 * go, top - h * 0.02 - h * 0.115 * up - h * 0.22 * EaseOut(go) * go);
                double tilt = Math.Sin(p * 3.1) * 4;
                dc.PushOpacity(Math.Clamp(1 - go * go, 0, 1));
                PaintHeart(dc, w, h, hc, size, tilt);
                dc.Pop();
                double b0 = p - (HeartUp + 0.55);
                if (b0 > 0 && b0 < 0.7) Burst(dc, hc, size * 1.0, size * 2.4, b0 / 0.7, 10, 71, h * 0.02);
                double b1 = p - (HeartUp + 1.45);
                if (b1 > 0 && b1 < 0.6) Burst(dc, hc, size * 1.0, size * 2.0, b1 / 0.6, 8, 73, h * 0.016);
            }

            // Melting coins rise as small sparks toward the heart.
            if (drain > 0 && drain < 1 && Particles)
            {
                for (int i = 0; i < Coins; i++)
                {
                    double q = Melt(drain, i);
                    if (q <= 0 || q >= 1) continue;
                    var from = new Point(jc.X + Slots[i, 0] * h, floor - Slots[i, 1] * h);
                    var to = new Point(jc.X, top - h * 0.18);
                    var pt = new Point(from.X + (to.X - from.X) * EaseOut(q) + Math.Sin(q * 6 + i) * h * 0.02, from.Y + (to.Y - from.Y) * q);
                    Spark(dc, pt, h * 0.022 * Math.Sin(q * Math.PI), i % 2 == 0 ? Colors.White : Mix(Accent, Colors.White, 0.3), Math.Sin(q * Math.PI), q * 4);
                }
            }

            Twinkles(dc, w, h, t, Mix(Accent, Colors.White, 0.45), 4, 67, new Rect(0.52, 0.08, 0.44, 0.8), 0.025);
            Motes(dc, w, h, t, Colors.White, 5, 69, new Rect(0.55, 0.2, 0.42, 0.8), 0.1, 0.008, 0.3);
        }

        /// <summary>How far coin <paramref name="i"/> has melted: the top of the pile goes first.</summary>
        private static double Melt(double drain, int i) => Math.Clamp(drain * 1.6 - (Coins - 1 - i) * 0.05, 0, 1);

        /// <summary>A heartbeat bump: quick up, slower settle, 0 outside about a third of a second.</summary>
        private static double Beat(double s)
        {
            if (s <= 0 || s >= 0.32) return 0;
            return s < 0.08 ? s / 0.08 : Math.Pow(1 - (s - 0.08) / 0.24, 2);
        }

        private void EnsureJar(double jw, double jh, double neck, double h)
        {
            if (_jar != null && Math.Abs(_jarH - jh) < 0.01) return;
            _jarH = jh;
            double yTop = -jh / 2, yBot = jh / 2, nh = h * 0.05, sh = h * 0.07, br = h * 0.06;
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                ctx.BeginFigure(new Point(-neck / 2, yTop), true, true);
                ctx.LineTo(new Point(-neck / 2, yTop + nh), true, true);
                ctx.QuadraticBezierTo(new Point(-jw / 2, yTop + nh), new Point(-jw / 2, yTop + nh + sh), true, true);
                ctx.LineTo(new Point(-jw / 2, yBot - br), true, true);
                ctx.QuadraticBezierTo(new Point(-jw / 2, yBot), new Point(-jw / 2 + br, yBot), true, true);
                ctx.LineTo(new Point(jw / 2 - br, yBot), true, true);
                ctx.QuadraticBezierTo(new Point(jw / 2, yBot), new Point(jw / 2, yBot - br), true, true);
                ctx.LineTo(new Point(jw / 2, yTop + nh + sh), true, true);
                ctx.QuadraticBezierTo(new Point(jw / 2, yTop + nh), new Point(neck / 2, yTop + nh), true, true);
                ctx.LineTo(new Point(neck / 2, yTop), true, true);
            }
            g.Freeze();
            _jar = g;
        }

        private void PaintJarBack(DrawingContext dc, double jw, double jh, double h, Geometry jar)
        {
            var back = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
            back.GradientStops.Add(new GradientStop(WithAlpha(Mix(Accent, Colors.White, 0.2), 0.14), 0));
            back.GradientStops.Add(new GradientStop(WithAlpha(Mix(Accent, Ink, 0.6), 0.28), 0.55));
            back.GradientStops.Add(new GradientStop(WithAlpha(Mix(Accent, Ink, 0.85), 0.55), 1));
            back.Freeze();
            dc.DrawGeometry(back, null, jar);
            // The floor's curve, seen through the glass.
            dc.DrawEllipse(null, Stroke(Mix(Accent, Colors.White, 0.3), Math.Max(0.6, h * 0.003), 0.18), new Point(0, jh / 2 - h * 0.03), jw * 0.45, h * 0.03);
        }

        private void PaintJarFront(DrawingContext dc, double jw, double jh, double neck, double h, Geometry jar)
        {
            // Glass: a pale edge, a tall highlight on the lamp side, a small one on the shoulder.
            dc.DrawGeometry(null, Stroke(Colors.White, Math.Max(1, h * 0.006), 0.32), jar);
            dc.DrawGeometry(null, Stroke(Mix(Accent, Colors.White, 0.5), Math.Max(0.6, h * 0.0025), 0.35), jar);
            double x0 = -jw / 2 + h * 0.035;
            dc.DrawRoundedRectangle(Fill(Colors.White, 0.24), null, new Rect(x0, -jh / 2 + h * 0.15, h * 0.026, jh * 0.62), h * 0.013, h * 0.013);
            dc.DrawRoundedRectangle(Fill(Colors.White, 0.14), null, new Rect(x0 + h * 0.04, -jh / 2 + h * 0.17, h * 0.01, jh * 0.3), h * 0.005, h * 0.005);
            dc.DrawEllipse(Fill(Colors.White, 0.3), null, new Point(-neck / 2 - h * 0.03, -jh / 2 + h * 0.085), h * 0.014, h * 0.008);
            dc.DrawRoundedRectangle(Fill(Ink, 0.16), null, new Rect(jw / 2 - h * 0.05, -jh / 2 + h * 0.16, h * 0.022, jh * 0.6), h * 0.011, h * 0.011);
        }

        private void PaintCollar(DrawingContext dc, double jh, double neck, double h)
        {
            // The jar's screw collar, a raised gold band across the mouth.
            var r = new Rect(-neck / 2 - h * 0.018, -jh / 2 - h * 0.024, neck + h * 0.036, h * 0.05);
            var band = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
            band.GradientStops.Add(new GradientStop(Mix(Accent, Colors.White, 0.4), 0));
            band.GradientStops.Add(new GradientStop(Accent, 0.45));
            band.GradientStops.Add(new GradientStop(Mix(Accent, Ink, 0.45), 1));
            band.Freeze();
            Plate(dc, r, h * 0.012, band, h * 0.012);
            var groove = Stroke(Mix(Accent, Ink, 0.5), Math.Max(0.6, h * 0.0025), 0.6);
            for (int i = 1; i < 3; i++) dc.DrawLine(groove, new Point(r.Left + h * 0.008, r.Top + r.Height * i / 3), new Point(r.Right - h * 0.008, r.Top + r.Height * i / 3));
        }

        private void PaintLabel(DrawingContext dc, double jw, double jh, double h, double thump, double warm)
        {
            var r = new Rect(-jw * 0.26, -jh * 0.2, jw * 0.52, jh * 0.24);
            Plate(dc, r, h * 0.016, Fill(Mix(Accent, Colors.White, 0.62)), h * 0.008);
            var hc = new Point(r.X + r.Width / 2, r.Y + r.Height / 2 + h * 0.004);
            double s = h * 0.034 * (1 + thump + 0.12 * warm);
            var m = Matrix.Identity;
            m.Scale(s, s);
            m.Translate(hc.X, hc.Y);
            dc.PushTransform(new MatrixTransform(m));
            dc.DrawGeometry(Fill(Mix(Rose, Accent, 0.15 + 0.3 * thump)), null, HeartShape);
            dc.Pop();
        }

        /// <summary>A coin lying on the pile, seen a little from above: its edge, then its face.</summary>
        private void FlatCoin(DrawingContext dc, double w, double h, Point at, double rx, double ry, double tilt, double lit)
        {
            dc.PushTransform(new RotateTransform(tilt, at.X, at.Y));
            dc.DrawEllipse(Fill(Mix(Accent, Ink, 0.5)), null, new Point(at.X, at.Y + h * 0.011), rx, ry);
            dc.PushTransform(new MatrixTransform(rx, 0, 0, ry, at.X, at.Y));
            dc.DrawDrawing(Layer("coinFace", w, h, PaintCoinFace));
            dc.Pop();
            dc.DrawEllipse(Fill(Colors.White, 0.35 * lit), null, new Point(at.X - rx * 0.35, at.Y - ry * 0.35), rx * 0.22, ry * 0.25);
            dc.Pop();
        }

        /// <summary>A unit coin face (radius 1): a minted rim and a raised star.</summary>
        private void PaintCoinFace(DrawingContext dc)
        {
            var face = new RadialGradientBrush { GradientOrigin = new Point(0.3, 0.25), Center = new Point(0.4, 0.35), RadiusX = 0.8, RadiusY = 0.8 };
            face.GradientStops.Add(new GradientStop(Mix(Accent, Colors.White, 0.55), 0));
            face.GradientStops.Add(new GradientStop(Accent, 0.55));
            face.GradientStops.Add(new GradientStop(Mix(Accent, Ink, 0.3), 1));
            face.Freeze();
            dc.DrawEllipse(face, null, new Point(0, 0), 1, 1);
            dc.DrawEllipse(null, new Pen(Fill(Mix(Accent, Ink, 0.45), 0.7), 0.09), new Point(0, 0), 0.74, 0.74);
            dc.DrawEllipse(null, new Pen(Fill(Colors.White, 0.35), 0.05), new Point(-0.02, -0.03), 0.8, 0.8);
            dc.DrawGeometry(Fill(Mix(Accent, Ink, 0.35), 0.75), null, Star(new Point(0, 0), 0.42, 0.18));
        }

        /// <summary>
        /// Coin <paramref name="i"/> in flight: in from above on a short arc, tumbling (its face
        /// flips edge-on and back), dropping into the slot it lands on.
        /// </summary>
        private void FallingCoin(DrawingContext dc, double w, double h, double p, int i, Point jc, double top, double floor, double jh, bool insidePass)
        {
            double land = FirstLand + i * Gap, s = (p - (land - Fall)) / Fall;
            if (s <= 0 || s >= 1) return;
            double sx = jc.X + (Hash(81, i, 1) - 0.3) * h * 0.4, sy = -h * 0.12;
            double ex = jc.X + Slots[i, 0] * h, ey = floor - Slots[i, 1] * h;
            double x = sx + (ex - sx) * EaseOut(s);
            double y = sy + (ey - sy) * s * s;
            if (insidePass && y < top - h * 0.03) return;
            double spin = s * Math.PI * (2.5 + Hash(81, i, 2));
            double r = h * 0.05 * (1 - 0.15 * s);
            double flat = Math.Max(0.12, Math.Abs(Math.Cos(spin)));
            double edge = h * 0.012 * Math.Sin(spin);
            dc.PushTransform(new RotateTransform((Hash(81, i, 3) - 0.5) * 50, x, y));
            dc.DrawEllipse(Fill(Mix(Accent, Ink, 0.5)), null, new Point(x, y + edge), r, r * flat);
            dc.PushTransform(new MatrixTransform(r, 0, 0, r * flat, x, y));
            dc.DrawDrawing(Layer("coinFace", w, h, PaintCoinFace));
            dc.Pop();
            dc.Pop();
            if (!insidePass && Math.Cos(spin) > 0.9) Spark(dc, new Point(x - r * 0.4, y - r * 0.4 * flat), h * 0.02, Colors.White, 0.8, spin);
        }

        private void PaintHeart(DrawingContext dc, double w, double h, Point c, double s, double tilt)
        {
            var m = Matrix.Identity;
            m.Scale(s, s);
            m.Rotate(tilt);
            m.Translate(c.X, c.Y);
            var sm = m;
            sm.Translate(h * 0.008, h * 0.013);
            dc.PushTransform(new MatrixTransform(sm));
            dc.DrawGeometry(Fill(Ink, 0.22), null, HeartShape);
            dc.Pop();
            m.ScalePrepend(0.01, 0.01);
            dc.PushTransform(new MatrixTransform(m));
            dc.DrawDrawing(Layer("heart", w, h, PaintHeartFace));
            dc.Pop();
        }

        /// <summary>The heart at 100 px a unit (so the bevel has real pixels to work with): warm rose
        /// lit from the top left, the lamp's bevel, and a glossy fleck.</summary>
        private void PaintHeartFace(DrawingContext dc)
        {
            var shape = HeartShape.Clone();
            shape.Transform = new ScaleTransform(100, 100);
            shape.Freeze();
            var body = Mix(Rose, Accent, 0.22);
            var g = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
            g.GradientStops.Add(new GradientStop(Mix(body, Colors.White, 0.4), 0));
            g.GradientStops.Add(new GradientStop(body, 0.5));
            g.GradientStops.Add(new GradientStop(Mix(body, Ink, 0.35), 1));
            g.Freeze();
            dc.DrawGeometry(g, null, shape);
            dc.PushClip(shape);
            dc.DrawGeometry(null, Bevel(9), shape);
            dc.Pop();
            dc.PushTransform(new RotateTransform(-28, -50, -45));
            dc.DrawEllipse(Fill(Colors.White, 0.6), null, new Point(-50, -45), 18, 9);
            dc.Pop();
            dc.DrawEllipse(Fill(Colors.White, 0.4), null, new Point(-24, -62), 6, 4);
        }

        private void Burst(DrawingContext dc, Point c, double r0, double r1, double k, int n, int seed, double size)
        {
            if (!Particles) return;
            for (int i = 0; i < n; i++)
            {
                double a = i * Math.PI * 2 / n + Hash(seed, i, 1) * 0.5, d = r0 + (r1 - r0) * EaseOut(k) * (0.75 + Hash(seed, i, 2) * 0.4);
                var pt = new Point(c.X + Math.Cos(a) * d, c.Y + Math.Sin(a) * d * 0.85);
                var hue = i % 3 == 0 ? Colors.White : (i % 3 == 1 ? Mix(Accent, Colors.White, 0.3) : Mix(Rose, Colors.White, 0.35));
                Spark(dc, pt, size * (1 - k * 0.8), hue, 1 - k, k * 3 + i);
            }
        }

        private static Geometry MakeHeart()
        {
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                ctx.BeginFigure(new Point(0, 0.88), true, true);
                ctx.BezierTo(new Point(-0.28, 0.62), new Point(-1, 0.18), new Point(-1, -0.3), true, true);
                ctx.BezierTo(new Point(-1, -0.78), new Point(-0.42, -0.98), new Point(0, -0.52), true, true);
                ctx.BezierTo(new Point(0.42, -0.98), new Point(1, -0.78), new Point(1, -0.3), true, true);
                ctx.BezierTo(new Point(1, 0.18), new Point(0.28, 0.62), new Point(0, 0.88), true, true);
            }
            g.Freeze();
            return g;
        }

        private static Geometry Star(Point c, double r, double inner)
        {
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                for (int i = 0; i < 10; i++)
                {
                    double a = -Math.PI / 2 + i * Math.PI / 5, rr = i % 2 == 0 ? r : inner;
                    var pt = new Point(c.X + Math.Cos(a) * rr, c.Y + Math.Sin(a) * rr);
                    if (i == 0) ctx.BeginFigure(pt, true, true);
                    else ctx.LineTo(pt, false, false);
                }
            }
            g.Freeze();
            return g;
        }
    }
}
