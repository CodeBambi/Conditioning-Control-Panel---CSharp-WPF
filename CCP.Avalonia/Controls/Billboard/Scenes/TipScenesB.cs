using System;
using System.Collections.Generic;

using Avalonia.Media;

namespace ConditioningControlPanel.Avalonia.Controls.Billboard.Scenes
{
    /// <summary>
    /// Shared bits for the second set of tip scenes: a small pen cache (rings and strokes that
    /// change alpha every frame go through PushOpacity over one cached pen), bursts and floor shadows.
    /// </summary>
    public abstract class TipBScene : BillboardVectorArt
    {
        private readonly Dictionary<long, Pen> _pens = new();
        private double _pensFor;

        /// <summary>A frozen round-capped pen from a per-view cache (thickness in quarter px).</summary>
        protected Pen PenOf(Color c, double thickness, double h)
        {
            if (_pensFor != h) { _pens.Clear(); _pensFor = h; }
            long key = ((long)c.R << 32) | ((long)c.G << 24) | ((long)c.B << 16) | (long)Math.Round(thickness * 4);
            if (_pens.TryGetValue(key, out var p)) return p;
            p = Stroke(c, thickness);
            if (_pens.Count < 64) _pens[key] = p;
            return p;
        }

        /// <summary>Sparks thrown out from a point on a beat, <paramref name="k"/> = 0..1 of their life.</summary>
        protected void Burst(DrawingContext dc, Point c, double reach, double size, double k, Color hue, int count, double turn = 0)
        {
            if (!Particles || k <= 0 || k >= 1) return;
            double fly = EaseOut(k);
            for (int i = 0; i < count; i++)
            {
                double a = turn + i * Math.PI * 2 / count + (i % 2) * 0.2;
                double d = reach * (0.35 + fly * (0.75 + 0.25 * (i % 3) / 2.0));
                Spark(dc, new Point(c.X + Math.Cos(a) * d, c.Y + Math.Sin(a) * d), size * (1 - k * 0.7),
                    i % 3 == 0 ? Colors.White : hue, 1 - k, k * 3 + i);
            }
        }

        /// <summary>A flattened contact shadow on the floor under a lifted thing.</summary>
        protected void FloorShadow(DrawingContext dc, Point c, double rx, double ry, double a)
        {
            dc.DrawEllipse(Fill(Kit.Shade, a * 0.4), null, c, rx, ry);
            dc.DrawEllipse(Fill(Kit.Shade, a * 0.35), null, c, rx * 0.7, ry * 0.7);
        }

        /// <summary>Three bright beads running down a link (a command or a pulse travelling).</summary>
        protected void Beads(DrawingContext dc, Point a, Point c, Point b, double k, double h)
        {
            if (k <= 0 || k >= 1) return;
            for (int j = 0; j < 3; j++)
            {
                double s = k * 1.24 - j * 0.12;
                if (s <= 0 || s >= 1) continue;
                var p = Kit.Quad(a, c, b, s);
                double r = h * (0.017 - j * 0.004);
                Glow(dc, p, r * 3, Kit.Pink, 0.35);
                dc.DrawEllipse(Fill(Kit.Pink), null, p, r, r);
                dc.DrawEllipse(Fill(Colors.White), null, p, r * 0.45, r * 0.45);
            }
        }

        /// <summary>The link itself: a quiet dotted curve.</summary>
        protected void Link(DrawingContext dc, Point a, Point c, Point b, double h, int dots)
        {
            var dot = Fill(Mix(Accent, Colors.White, 0.3), 0.42);
            for (int i = 1; i < dots; i++)
                dc.DrawEllipse(dot, null, Kit.Quad(a, c, b, i / (double)dots), h * 0.0055, h * 0.0055);
        }
    }

    file static class Kit
    {
        public static readonly Color Pink = Color.FromRgb(0xff, 0x4f, 0xa8);
        public static readonly Color Ink = Color.FromRgb(0x14, 0x0f, 0x24);
        public static readonly Color Shade = Color.FromRgb(4, 4, 12);
        public static readonly Color Grey = Color.FromRgb(0x62, 0x60, 0x7a);
        public static readonly Color Metal = Color.FromRgb(0xd8, 0xd2, 0xf0);

        public static double Phase(double t, double period)
        {
            double u = t % period;
            return u < 0 ? u + period : u;
        }

        /// <summary>0 before <paramref name="a"/>, 1 after <paramref name="b"/>, linear between.</summary>
        public static double Win(double tp, double a, double b) => Math.Clamp((tp - a) / (b - a), 0, 1);

        public static Matrix Pose(double x, double y, double degrees = 0, double sx = 1, double sy = 1)
        {
            var m = Matrix.Identity;
            m.Scale(sx, sy);
            m.Rotate(degrees);
            m.Translate(x, y);
            return m;
        }

        public static Point Quad(Point a, Point c, Point b, double s)
        {
            double u = 1 - s;
            return new Point(u * u * a.X + 2 * u * s * c.X + s * s * b.X, u * u * a.Y + 2 * u * s * c.Y + s * s * b.Y);
        }

        public static Rect Shrink(Rect r, double d) => new(r.X + d, r.Y + d, Math.Max(0, r.Width - 2 * d), Math.Max(0, r.Height - 2 * d));

        public static Rect Around(Point c, double w, double h) => new(c.X - w / 2, c.Y - h / 2, w, h);

        /// <summary>A damped spring kick: 1 at the hit, ringing down to rest; 0 before it.</summary>
        public static double Spring(double age, double decay = 7, double freq = 26) =>
            age < 0 ? 0 : Math.Exp(-age * decay) * Math.Cos(age * freq);

        /// <summary>An arc of radius <paramref name="r"/> round <paramref name="c"/>, centred on angle <paramref name="mid"/>.</summary>
        public static void Arc(DrawingContext dc, Point c, double r, double mid, double span, Pen pen)
        {
            double a0 = mid - span / 2, a1 = mid + span / 2;
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                ctx.BeginFigure(new Point(c.X + Math.Cos(a0) * r, c.Y + Math.Sin(a0) * r), false, false);
                ctx.ArcTo(new Point(c.X + Math.Cos(a1) * r, c.Y + Math.Sin(a1) * r), new Size(r, r), 0, span > Math.PI,
                    SweepDirection.Clockwise, true, false);
            }
            g.Freeze();
            dc.DrawGeometry(null, pen, g);
        }

        /// <summary>A little landscape (two hills) inside a rect: how a picture file reads.</summary>
        public static Geometry Hills(Rect r)
        {
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                ctx.BeginFigure(new Point(r.Left, r.Bottom), true, true);
                ctx.LineTo(new Point(r.Left + r.Width * 0.34, r.Top + r.Height * 0.42), false, false);
                ctx.LineTo(new Point(r.Left + r.Width * 0.55, r.Top + r.Height * 0.7), false, false);
                ctx.LineTo(new Point(r.Left + r.Width * 0.74, r.Top + r.Height * 0.5), false, false);
                ctx.LineTo(new Point(r.Right, r.Top + r.Height * 0.78), false, false);
                ctx.LineTo(new Point(r.Right, r.Bottom), false, false);
            }
            g.Freeze();
            return g;
        }
    }

    /// <summary>
    /// Tip "effects can reach a toy": a bubble rises on a small screen and pops, the pop runs down
    /// the dotted link as three bright beads, and the rounded pebble on the floor takes it with a
    /// squash, a buzz and rings rolling off both ends. Loops in 4.4 s.
    /// </summary>
    public sealed class HapticsTipArt : TipBScene
    {
        private const double Period = 4.4, PopAt = 0.9, SendAt = 0.95, HitAt = 1.45;
        private Geometry? _pebble;
        private double _pebbleFor;

        protected override void Paint(DrawingContext dc, double w, double h, double t)
        {
            CachedGround(dc, w, h, Deep(Accent));
            Motes(dc, w, h, t, Accent, 12, 81, new Rect(0.5, 0.08, 0.48, 0.86), 0.05, 0.005, 0.4);
            double tp = Kit.Phase(t, Period);

            // The source: a small screen on a raised frame.
            double sw = h * 0.46, sh = h * 0.34;
            var sc = new Point(w * 0.615, h * 0.32 + Bob(t, h * 0.008));
            var frame = Kit.Around(sc, sw, sh);
            var glass = Kit.Shrink(frame, sh * 0.1);
            Plate(dc, frame, sh * 0.16, Fill(Mix(Accent, Kit.Ink, 0.5)), h * 0.022);
            Plate(dc, glass, sh * 0.1, Fill(Kit.Ink, 0.94), 0, pressed: true);

            // The pop lights the glass for a moment: an effect firing.
            double flash = tp >= PopAt ? Math.Exp(-(tp - PopAt) * 5) : 0;
            if (flash > 0.02) dc.DrawRoundedRectangle(Fill(Kit.Pink, 0.35 * flash), null, glass, sh * 0.1, sh * 0.1);

            // The bubble rises and swells, then pops into a ring and a few glints.
            var popAt = new Point(glass.X + glass.Width * 0.45, glass.Y + glass.Height * 0.4);
            // Small bubbles drift up the glass the whole time: the screen is always playing something.
            dc.PushClip(new RectangleGeometry(glass, sh * 0.1, sh * 0.1));
            for (int i = 0; i < 4; i++)
            {
                double life = (Hash(85, i, 1) + t * (0.16 + Hash(85, i, 2) * 0.1)) % 1.0;
                double br = sh * (0.035 + 0.03 * Hash(85, i, 3));
                var bp = new Point(glass.X + glass.Width * (0.12 + 0.76 * Hash(85, i, 4)) + Math.Sin(t * 1.3 + i) * sh * 0.03,
                    glass.Bottom + br - life * (glass.Height + br * 2));
                dc.PushOpacity(0.45 * Math.Sin(life * Math.PI));
                dc.DrawEllipse(null, PenOf(Mix(Accent, Colors.White, 0.4), Math.Max(1, sh * 0.012), h), bp, br, br);
                dc.Pop();
            }
            dc.Pop();
            if (tp < PopAt)
            {
                dc.PushClip(new RectangleGeometry(glass, sh * 0.1, sh * 0.1));
                double k = EaseOut(tp / PopAt);
                double r = sh * (0.1 + 0.08 * k);
                var bc = new Point(popAt.X + Math.Sin(tp * 5) * glass.Width * 0.04, glass.Bottom + r - k * (glass.Bottom + r - popAt.Y));
                dc.DrawEllipse(Fill(Kit.Pink, 0.28), PenOf(Mix(Kit.Pink, Colors.White, 0.6), Math.Max(1, sh * 0.02), h), bc, r, r);
                dc.DrawEllipse(Fill(Colors.White, 0.8), null, new Point(bc.X - r * 0.38, bc.Y - r * 0.38), r * 0.18, r * 0.18);
                dc.Pop();
            }
            double pk = Kit.Win(tp, PopAt, PopAt + 0.45);
            if (pk > 0 && pk < 1)
            {
                dc.PushOpacity(1 - pk);
                double rr = sh * (0.16 + 0.3 * EaseOut(pk));
                dc.DrawEllipse(null, PenOf(Colors.White, Math.Max(1, sh * 0.025), h), popAt, rr, rr);
                dc.Pop();
                Burst(dc, popAt, sh * 0.42, h * 0.022, pk, Kit.Pink, 6, 0.3);
            }

            // The pebble on the floor, and the link between them.
            double rx = h * 0.17, ry = h * 0.1;
            var pc = new Point(w * 0.815, h * 0.69);
            var from = new Point(frame.Right - sh * 0.05, sc.Y + sh * 0.1);
            var to = new Point(pc.X - rx * 0.55, pc.Y - ry * 0.9);
            var ctrl = new Point((from.X + to.X) / 2 + h * 0.04, Math.Min(from.Y, to.Y) - h * 0.02);
            Link(dc, from, ctrl, to, h, 14);
            Beads(dc, from, ctrl, to, Kit.Win(tp, SendAt, HitAt), h);

            // The buzz: a squash on the hit, then a shiver that dies away.
            double age = tp - HitAt;
            double buzz = age >= 0 ? Math.Exp(-age * 1.15) * (1 - Kit.Win(tp, HitAt + 2.3, HitAt + 2.8)) : 0;
            double sq = Kit.Spring(age, 6, 24);
            double jx = Math.Sin(t * 83) * buzz * h * 0.007, jy = Math.Cos(t * 71) * buzz * h * 0.004;
            double rot = -12 + Math.Sin(t * 61) * buzz * 1.6;
            FloorShadow(dc, new Point(pc.X + h * 0.012, pc.Y + ry * 0.95), rx * (1.05 + sq * 0.06), ry * 0.32, 0.9);

            // Rings rolling off both ends while it hums.
            if (age >= 0)
            {
                var ringPen = PenOf(Mix(Kit.Pink, Colors.White, 0.35), Math.Max(1, h * 0.008), h);
                var cc = new Point(pc.X + jx, pc.Y + jy);
                double tiltRad = rot * Math.PI / 180;
                for (int n = 0; n < 9; n++)
                {
                    double ra = age - n * 0.26;
                    if (ra < 0 || ra > 0.9) continue;
                    double strength = Math.Exp(-(n * 0.26) * 1.15) * (1 - ra / 0.9);
                    if (strength < 0.04) continue;
                    double rad = rx * (1.05 + ra * 0.8);
                    dc.PushOpacity(strength * 0.9);
                    Kit.Arc(dc, cc, rad, tiltRad, 0.85, ringPen);
                    Kit.Arc(dc, cc, rad, tiltRad + Math.PI, 0.85, ringPen);
                    dc.Pop();
                }
            }

            double breath = Math.Sin(t * 1.8) * 0.012;
            dc.PushTransform(new MatrixTransform(Kit.Pose(pc.X + jx, pc.Y + jy, rot, 1 + 0.1 * sq + breath, 1 - 0.13 * sq - breath)));
            dc.DrawDrawing(Layer("pebble", w, h, d => PaintPebble(d, rx, ry)));
            // The little light at its tip wakes with the buzz.
            var led = new Point(rx * 0.08, -ry * 0.42);
            double on = Math.Min(1, buzz * 1.4);
            dc.DrawEllipse(Fill(Mix(Accent, Kit.Ink, 0.3), 0.8), null, led, ry * 0.1, ry * 0.1);
            if (on > 0.03)
            {
                Glow(dc, led, ry * 0.6, Kit.Pink, 0.5 * on);
                dc.DrawEllipse(Fill(Mix(Kit.Pink, Colors.White, 0.4), on), null, led, ry * 0.1, ry * 0.1);
            }
            Sheen(dc, PebbleGeometry(rx, ry), new Rect(-rx, -ry, rx * 2, ry * 2), t, 4.4, 2.6, 0.8);
            dc.Pop();

            Burst(dc, new Point(pc.X, pc.Y - ry * 0.4), rx * 0.9, h * 0.024, Kit.Win(tp, HitAt, HitAt + 0.55), Mix(Accent, Colors.White, 0.4), 7, -0.4);

            Twinkles(dc, w, h, t, Mix(Accent, Colors.White, 0.4), 4, 83, new Rect(0.52, 0.08, 0.44, 0.8), 0.026);
            Motes(dc, w, h, t, Colors.White, 5, 87, new Rect(0.55, 0.2, 0.42, 0.75), 0.1, 0.008, 0.3);
        }

        private Geometry PebbleGeometry(double rx, double ry)
        {
            if (_pebble == null || _pebbleFor != rx)
            {
                _pebble = new EllipseGeometry(new Point(), rx, ry);
                _pebble.Freeze();
                _pebbleFor = rx;
            }
            return _pebble;
        }

        /// <summary>The pebble at the origin: a smooth body lit from the top left, a seam and a soft highlight.</summary>
        private void PaintPebble(DrawingContext d, double rx, double ry)
        {
            var body = new RadialGradientBrush
            {
                Center = new Point(0.36, 0.3),
                GradientOrigin = new Point(0.3, 0.22),
                RadiusX = 0.85,
                RadiusY = 0.9,
            };
            body.GradientStops.Add(new GradientStop(Mix(Accent, Colors.White, 0.55), 0));
            body.GradientStops.Add(new GradientStop(Accent, 0.45));
            body.GradientStops.Add(new GradientStop(Mix(Accent, Kit.Ink, 0.55), 1));
            body.Freeze();
            d.DrawEllipse(body, null, new Point(), rx, ry);
            d.DrawEllipse(null, Bevel(Math.Max(1.2, ry * 0.06)), new Point(), rx - ry * 0.03, ry - ry * 0.03);
            // A soft highlight where the lamp catches it, and a darker underside band.
            d.DrawEllipse(Fill(Colors.White, 0.3), null, new Point(-rx * 0.42, -ry * 0.48), rx * 0.3, ry * 0.15);
            d.DrawEllipse(Fill(Colors.White, 0.5), null, new Point(-rx * 0.5, -ry * 0.5), rx * 0.1, ry * 0.06);
        }
    }

    /// <summary>
    /// Tip "leave a whole folder out": the switch on a folder flips off and the folder goes grey;
    /// new pictures drop in and go grey with it, so they stay out too. Then it flips back on,
    /// lights up, and the new ones sink away for the next loop. Loops in 6 s.
    /// </summary>
    public sealed class FoldersTipArt : TipBScene
    {
        private const double Period = 6.0, OffAt = 0.5, OnAt = 4.6;

        protected override void Paint(DrawingContext dc, double w, double h, double t)
        {
            CachedGround(dc, w, h, Deep(Accent));
            Motes(dc, w, h, t, Accent, 12, 91, new Rect(0.5, 0.06, 0.48, 0.88), 0.05, 0.005, 0.4);
            double tp = Kit.Phase(t, Period);

            // g: how switched off the folder is (0 on, 1 off).
            double g = Math.Min(Kit.Win(tp, OffAt + 0.1, OffAt + 0.5), 1 - Kit.Win(tp, OnAt, OnAt + 0.35));
            double fw = h * 0.64, fh = h * 0.46;
            double sink = Math.Min(Kit.Win(tp, OffAt, OffAt + 0.15), 1 - Kit.Win(tp, OnAt, OnAt + 0.15)) * h * 0.006;
            var c = new Point(w * 0.735, h * 0.6 + Bob(t, h * 0.006) + sink);
            var back = Kit.Around(c, fw, fh);
            var tab = new Rect(back.X + fw * 0.05, back.Y - fh * 0.13, fw * 0.36, fh * 0.26);

            var lit = Mix(Accent, Colors.White, 0.12);
            var backCol = Mix(Mix(Accent, Kit.Ink, 0.38), Mix(Kit.Grey, Kit.Ink, 0.4), g);
            var frontCol = Mix(lit, Kit.Grey, g);

            FloorShadow(dc, new Point(c.X + h * 0.01, back.Bottom + h * 0.012), fw * 0.55, h * 0.03, 0.8);
            Plate(dc, tab, fh * 0.07, Fill(backCol), h * 0.018);
            Plate(dc, back, fh * 0.08, Fill(backCol), h * 0.022);

            // The pictures already inside, peeking over the rim.
            File(dc, new Point(back.X + fw * 0.33, back.Y + fh * 0.36), fw * 0.32, -7, g, h);
            File(dc, new Point(back.X + fw * 0.62, back.Y + fh * 0.3), fw * 0.32, 6, g, h);

            // New pictures dropping in; they take the folder's state as they pass the rim.
            double fade = 1 - Kit.Win(tp, 5.2, 5.8);
            double slide = Kit.Win(tp, 5.2, 5.8) * fh * 0.25;
            var front = new Rect(back.X - fw * 0.02, back.Y + fh * 0.34, fw * 1.04, fh * 0.66);
            dc.PushClip(new RectangleGeometry(new Rect(0, 0, w, front.Bottom - fh * 0.04)));
            dc.PushOpacity(fade);
            Drop(dc, tp, 1.0, back, fw, fh, fw * 0.5, -3, g, slide, h);
            Drop(dc, tp, 2.6, back, fw, fh, fw * 0.76, 9, g, slide, h);
            dc.Pop();
            dc.Pop();

            // The front of the folder, with its switch.
            Plate(dc, front, fh * 0.09, Fill(frontCol), h * 0.016);
            var frontGeo = new RectangleGeometry(front, fh * 0.09, fh * 0.09);
            if (g < 0.5) Sheen(dc, frontGeo, front, t, 5.2, 1.2, 0.6);

            double tw = fh * 0.5, th = fh * 0.22;
            var track = Kit.Around(new Point(front.X + front.Width * 0.5, front.Y + front.Height * 0.52), tw, th);
            Plate(dc, track, th / 2, Fill(Mix(Kit.Pink, Mix(Kit.Ink, Kit.Grey, 0.25), g)), 0, pressed: true);
            // The knob slides with a little overshoot each way.
            double kOff = BackOut(Kit.Win(tp, OffAt, OffAt + 0.3), 2.2), kOn = BackOut(Kit.Win(tp, OnAt, OnAt + 0.3), 2.2);
            double pos = 1 - kOff + kOn; // 1 = right (on), 0 = left (off)
            double kr = th * 0.42;
            var knob = new Point(track.X + th / 2 + (tw - th) * pos, track.Y + th / 2);
            dc.DrawEllipse(Fill(Kit.Shade, 0.35), null, new Point(knob.X + kr * 0.15, knob.Y + kr * 0.25), kr, kr);
            dc.DrawEllipse(Fill(Mix(Colors.White, Accent, 0.12)), Bevel(Math.Max(1, kr * 0.12)), knob, kr, kr);

            // A puff of grey as it goes off, sparks as it comes back on.
            double puff = Kit.Win(tp, OffAt + 0.1, OffAt + 0.8);
            if (puff > 0 && puff < 1 && Particles)
            {
                for (int i = 0; i < 6; i++)
                {
                    double a = -Math.PI * (0.15 + i * 0.14);
                    double d = fw * (0.3 + 0.2 * EaseOut(puff));
                    var p = new Point(c.X + Math.Cos(a) * d * 0.9, back.Y + fh * 0.4 + Math.Sin(a) * d * 0.5 - puff * h * 0.03);
                    dc.DrawEllipse(Fill(Kit.Grey, 0.45 * (1 - puff)), null, p, h * 0.014 * (1 + puff), h * 0.014 * (1 + puff));
                }
            }
            Burst(dc, knob, fh * 0.55, h * 0.026, Kit.Win(tp, OnAt + 0.15, OnAt + 0.75), Kit.Pink, 7, -0.6);

            Twinkles(dc, w, h, t, Mix(Accent, Colors.White, 0.4), 3, 93, new Rect(0.52, 0.06, 0.44, 0.4), 0.024);
            Motes(dc, w, h, t, Colors.White, 5, 97, new Rect(0.55, 0.2, 0.42, 0.75), 0.1, 0.008, 0.3);
        }

        /// <summary>A file falling in from above, landing with a little bounce inside the folder.</summary>
        private void Drop(DrawingContext dc, double tp, double at, Rect back, double fw, double fh, double x, double rest, double g, double slide, double h)
        {
            double k = Kit.Win(tp, at, at + 1.0);
            if (k <= 0) return;
            double y0 = -fh * 0.3, y1 = back.Y + fh * 0.32;
            double fall = k < 0.72 ? Math.Pow(k / 0.72, 2) : 1 - Math.Sin((k - 0.72) / 0.28 * Math.PI) * 0.06;
            double y = y0 + (y1 - y0) * fall + slide;
            double rot = rest + (1 - fall) * 22;
            // Greys as it passes the rim of a folder that is off.
            double through = Math.Clamp((y - (back.Y - fh * 0.15)) / (fh * 0.3), 0, 1);
            File(dc, new Point(back.X + x, y), fw * 0.3, rot, g * through, h, lift: (1 - fall) * h * 0.03 + h * 0.008);
            // A soft grey puff when it lands in an off folder: it stays out.
            double land = Kit.Win(tp, at + 0.72, at + 1.4);
            if (land > 0 && land < 1 && g > 0.5 && Particles)
            {
                for (int i = 0; i < 4; i++)
                {
                    double side = i % 2 == 0 ? -1 : 1;
                    var p = new Point(back.X + x + side * fw * (0.12 + 0.1 * EaseOut(land)) * (1 + i / 3.0 * 0.4), y1 - fh * 0.04 - land * h * 0.04 * (1 + i * 0.3));
                    dc.DrawEllipse(Fill(Kit.Grey, 0.5 * (1 - land)), null, p, h * 0.011 * (1 + land), h * 0.011 * (1 + land));
                }
            }
        }

        /// <summary>A picture file: a paper card with a little landscape and two caption lines.</summary>
        private void File(DrawingContext dc, Point c, double cw, double degrees, double grey, double h, double lift = 0)
        {
            double ch = cw * 1.22;
            var r = new Rect(-cw / 2, -ch / 2, cw, ch);
            dc.PushTransform(new MatrixTransform(Kit.Pose(c.X, c.Y, degrees)));
            var paper = Mix(Mix(Colors.White, Accent, 0.12), Mix(Kit.Grey, Colors.White, 0.25), grey);
            Plate(dc, r, cw * 0.08, Fill(paper), lift);
            var pic = new Rect(r.X + cw * 0.12, r.Y + ch * 0.12, cw * 0.76, ch * 0.5);
            var sky = Mix(Mix(Kit.Pink, Colors.White, 0.35), Mix(Kit.Grey, Colors.White, 0.1), grey);
            var hill = Mix(Mix(Accent, Kit.Ink, 0.2), Mix(Kit.Grey, Kit.Ink, 0.35), grey);
            dc.DrawRoundedRectangle(Fill(sky), null, pic, cw * 0.04, cw * 0.04);
            dc.PushClip(new RectangleGeometry(pic, cw * 0.04, cw * 0.04));
            dc.DrawGeometry(Fill(hill), null, Kit.Hills(pic));
            dc.Pop();
            dc.DrawEllipse(Fill(Mix(Colors.White, Kit.Grey, grey * 0.6), 0.9), null, new Point(pic.Right - pic.Width * 0.22, pic.Top + pic.Height * 0.26), pic.Height * 0.12, pic.Height * 0.12);
            var ink = PenOf(Mix(Kit.Ink, Kit.Grey, grey), Math.Max(1, ch * 0.035), h);
            dc.PushOpacity(0.35);
            dc.DrawLine(ink, new Point(pic.Left, r.Bottom - ch * 0.22), new Point(pic.Right - cw * 0.1, r.Bottom - ch * 0.22));
            dc.DrawLine(ink, new Point(pic.Left, r.Bottom - ch * 0.12), new Point(pic.Left + pic.Width * 0.55, r.Bottom - ch * 0.12));
            dc.Pop();
            dc.Pop();
        }
    }

    /// <summary>
    /// Tip "Lockdown holds a session in place": a spiral session plays behind a padlock on a round
    /// seal whose timer ring drains; under it the phrase gets typed one key at a time, and on the
    /// last key the shackle springs open with a burst. Then it closes again with a thunk. 6.4 s.
    /// </summary>
    public sealed class LockdownTipArt : TipBScene
    {
        private const double Period = 6.4, FirstKey = 0.9, KeyGap = 0.38, OpenAt = 3.55, CloseAt = 5.3;
        private const int Keys = 7;
        private Geometry? _shackle;
        private double _shackleFor;

        protected override void Paint(DrawingContext dc, double w, double h, double t)
        {
            CachedGround(dc, w, h, Deep(Accent));
            Motes(dc, w, h, t, Accent, 12, 101, new Rect(0.5, 0.06, 0.48, 0.88), 0.05, 0.005, 0.4);
            double tp = Kit.Phase(t, Period);
            double open = Math.Min(BackOut(Kit.Win(tp, OpenAt, OpenAt + 0.4), 2), 1 - EaseIn(Kit.Win(tp, CloseAt, CloseAt + 0.2)));
            double thunk = Kit.Spring(tp - (CloseAt + 0.2), 8, 30);

            // The session: a screen with a spiral turning in it, dimmer once it lets go.
            double sw = h * 0.68, sh = h * 0.44;
            var sc = new Point(w * 0.72, h * 0.33 + Bob(t, h * 0.006));
            var frame = Kit.Around(sc, sw, sh);
            var glass = Kit.Shrink(frame, sh * 0.08);
            Plate(dc, frame, sh * 0.12, Fill(Mix(Accent, Kit.Ink, 0.55)), h * 0.022);
            Plate(dc, glass, sh * 0.07, Fill(Kit.Ink, 0.95), 0, pressed: true);
            dc.PushClip(new RectangleGeometry(glass, sh * 0.07, sh * 0.07));
            double held = 1 - 0.5 * Math.Min(Kit.Win(tp, OpenAt, OpenAt + 0.5), 1 - Kit.Win(tp, CloseAt, CloseAt + 0.4));
            var turn = Matrix.Identity;
            turn.Rotate(-t * 50);
            turn.Translate(sc.X, sc.Y);
            dc.PushOpacity(0.62 * held);
            dc.PushTransform(new MatrixTransform(turn));
            dc.DrawDrawing(Layer("spiral", w, h, d => Arms(d, sw * 0.62, Stroke(Accent, h * 0.016, 0.85), Stroke(Kit.Pink, h * 0.016, 0.85))));
            dc.Pop();
            dc.Pop();
            Glow(dc, sc, sh * 0.35, Kit.Pink, 0.18 * held);
            dc.Pop();

            // The seal: a round plate with the timer ring on its edge.
            double bw = h * 0.2, bh = h * 0.155;
            var lc = new Point(sc.X, frame.Bottom - sh * 0.02);
            double R = bw * 0.92;
            var seal = new Point(lc.X, lc.Y - bh * 0.12);
            dc.DrawEllipse(Fill(Kit.Shade, 0.35), null, new Point(seal.X + R * 0.08, seal.Y + R * 0.12), R, R);
            dc.DrawEllipse(Fill(Mix(Kit.Ink, Accent, 0.18)), Bevel(Math.Max(1, R * 0.05)), seal, R, R);
            dc.DrawEllipse(null, PenOf(Kit.Shade, R * 0.09, h), seal, R * 0.86, R * 0.86);
            double left = tp < OpenAt ? 1 - 0.38 * Kit.Win(tp, 0, OpenAt)
                : tp < CloseAt + 0.2 ? 0.62 * (1 - EaseOut(Kit.Win(tp, OpenAt, OpenAt + 0.5)))
                : EaseOut(Kit.Win(tp, CloseAt + 0.2, CloseAt + 0.9));
            if (left > 0.01)
            {
                double span = Math.Min(left, 0.999) * Math.PI * 2;
                Kit.Arc(dc, seal, R * 0.86, -Math.PI / 2 + span / 2, span, PenOf(Mix(Accent, Colors.White, 0.35), R * 0.085, h));
                // The draining end: a bright bead counting the time down.
                double end = -Math.PI / 2 + span;
                var bead = new Point(seal.X + Math.Cos(end) * R * 0.86, seal.Y + Math.Sin(end) * R * 0.86);
                Glow(dc, bead, R * 0.22, Colors.White, 0.35);
                dc.DrawEllipse(Fill(Colors.White), null, bead, R * 0.07, R * 0.07);
            }

            // The padlock: the shackle lifts and swings on its left leg when it opens.
            double hop = open * bh * 0.12;
            var bodyC = new Point(lc.X, lc.Y - hop);
            double sw2 = bw * 0.62, legX = -sw2 / 2;
            var shackle = Shackle(sw2, bh * 0.95);
            var m = Matrix.Identity;
            m.Translate(-legX, 0);
            m.Rotate(-20 * Math.Max(0, open));
            m.Translate(legX, -open * bh * 0.42);
            m.Translate(bodyC.X, bodyC.Y - bh * 0.42 + thunk * bh * 0.04);
            dc.PushTransform(new MatrixTransform(m));
            dc.DrawGeometry(null, PenOf(Kit.Shade, bw * 0.15, h), shackle);
            dc.DrawGeometry(null, PenOf(Mix(Kit.Metal, Kit.Ink, 0.25), bw * 0.12, h), shackle);
            dc.PushTransform(new TranslateTransform(-bw * 0.02, -bw * 0.02));
            dc.PushOpacity(0.5);
            dc.DrawGeometry(null, PenOf(Colors.White, bw * 0.03, h), shackle);
            dc.Pop();
            dc.Pop();
            dc.Pop();

            // The body, squashing on the thunk (anchored at its foot).
            double sy = 1 - thunk * 0.1, sx = 1 + thunk * 0.06;
            var bm = Matrix.Identity;
            bm.Translate(0, -bh / 2);
            bm.Scale(sx, sy);
            bm.Translate(bodyC.X, bodyC.Y + bh / 2);
            dc.PushTransform(new MatrixTransform(bm));
            dc.DrawDrawing(Layer("lockbody", w, h, d => PaintBody(d, bw, bh, h)));
            dc.Pop();

            // The phrase field: one dot per key, a caret blinking after the last one.
            double fw = sw * 0.72, fh = h * 0.085;
            var field = Kit.Around(new Point(sc.X, lc.Y + bh * 0.5 + h * 0.07 + fh * 0.5), fw, fh);
            Plate(dc, field, fh * 0.3, Fill(Kit.Ink, 0.92), 0, pressed: true);
            double clear = 1 - Kit.Win(tp, CloseAt - 0.3, CloseAt);
            double done = Kit.Win(tp, OpenAt - 0.05, OpenAt + 0.6);
            if (done > 0 && done < 1)
            {
                dc.PushOpacity(Math.Sin(done * Math.PI));
                dc.DrawRoundedRectangle(null, PenOf(Mix(Kit.Pink, Colors.White, 0.5), Math.Max(1, fh * 0.08), h), Kit.Shrink(field, -fh * 0.06), fh * 0.34, fh * 0.34);
                dc.Pop();
            }
            double pitch = (fw - fh) / Keys;
            int typed = 0;
            for (int i = 0; i < Keys; i++)
            {
                double at = FirstKey + i * KeyGap;
                double k = Kit.Win(tp, at, at + 0.2);
                var p = new Point(field.X + fh * 0.5 + pitch * (i + 0.5), field.Y + fh / 2);
                dc.DrawEllipse(Fill(Mix(Accent, Kit.Ink, 0.5), 0.6), null, p, fh * 0.07, fh * 0.07);
                if (k <= 0) continue;
                typed = i + 1;
                double s = BackOut(k, 2.6) * clear;
                if (s <= 0.01) continue;
                var col = done > 0 ? Mix(Mix(Accent, Colors.White, 0.5), Kit.Pink, done) : Mix(Accent, Colors.White, 0.5);
                dc.DrawEllipse(Fill(col), null, p, fh * 0.17 * s, fh * 0.17 * s);
                if (k < 1) Spark(dc, new Point(p.X, p.Y - fh * 0.5 * k), fh * 0.16 * (1 - k), Colors.White, 1 - k);
            }
            if (typed < Keys && clear > 0.5 && Math.Sin(t * 7) > 0)
            {
                double cx = field.X + fh * 0.5 + pitch * typed + pitch * 0.12;
                dc.DrawLine(PenOf(Mix(Accent, Colors.White, 0.6), Math.Max(1, fh * 0.07), h), new Point(cx, field.Y + fh * 0.25), new Point(cx, field.Bottom - fh * 0.25));
            }

            Burst(dc, new Point(bodyC.X, bodyC.Y - bh * 0.5), bw * 1.1, h * 0.03, Kit.Win(tp, OpenAt, OpenAt + 0.7), Kit.Pink, 9, -Math.PI / 2);
            Twinkles(dc, w, h, t, Mix(Accent, Colors.White, 0.4), 3, 103, new Rect(0.52, 0.06, 0.44, 0.84), 0.024);
            Motes(dc, w, h, t, Colors.White, 5, 107, new Rect(0.55, 0.2, 0.42, 0.75), 0.1, 0.008, 0.3);
        }

        private static double EaseIn(double k) => k * k * k;

        /// <summary>The shackle: two legs and a round top, its feet at y = 0.</summary>
        private Geometry Shackle(double sw, double sh)
        {
            if (_shackle == null || _shackleFor != sw)
            {
                double r = sw / 2;
                var g = new StreamGeometry();
                using (var ctx = g.Open())
                {
                    ctx.BeginFigure(new Point(-r, 0), false, false);
                    ctx.LineTo(new Point(-r, -sh + r), true, false);
                    ctx.ArcTo(new Point(r, -sh + r), new Size(r, r), 0, false, SweepDirection.Clockwise, true, false);
                    ctx.LineTo(new Point(r, 0), true, false);
                }
                g.Freeze();
                _shackle = g;
                _shackleFor = sw;
            }
            return _shackle;
        }

        /// <summary>The lock body round the origin: a pink block, a keyhole, a lit top edge.</summary>
        private void PaintBody(DrawingContext d, double bw, double bh, double h)
        {
            var body = new Rect(-bw / 2, -bh / 2, bw, bh);
            var fill = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
            fill.GradientStops.Add(new GradientStop(Mix(Kit.Pink, Colors.White, 0.3), 0));
            fill.GradientStops.Add(new GradientStop(Kit.Pink, 0.5));
            fill.GradientStops.Add(new GradientStop(Mix(Kit.Pink, Kit.Ink, 0.4), 1));
            fill.Freeze();
            Plate(d, body, bh * 0.2, fill, h * 0.018);
            var kc = new Point(0, -bh * 0.08);
            d.DrawEllipse(Fill(Kit.Ink, 0.8), null, kc, bh * 0.11, bh * 0.11);
            d.DrawRoundedRectangle(Fill(Kit.Ink, 0.8), null, new Rect(kc.X - bh * 0.045, kc.Y, bh * 0.09, bh * 0.26), bh * 0.03, bh * 0.03);
            d.DrawRectangle(Fill(Colors.White, 0.22), null, new Rect(body.X + bh * 0.2, body.Y + bh * 0.1, bw - bh * 0.4, Math.Max(1, bh * 0.05)));
        }

        /// <summary>Four arms round the origin, alternating two pens.</summary>
        private static void Arms(DrawingContext dc, double R, Pen a, Pen b)
        {
            for (int arm = 0; arm < 4; arm++)
            {
                var g = new StreamGeometry();
                using (var ctx = g.Open())
                {
                    for (int k = 0; k <= 90; k++)
                    {
                        double s = k / 90.0, th = s * Math.PI * 4.5 + arm * Math.PI / 2, rr = s * R;
                        var p = new Point(Math.Cos(th) * rr, Math.Sin(th) * rr);
                        if (k == 0) ctx.BeginFigure(p, false, false);
                        else ctx.LineTo(p, true, false);
                    }
                }
                g.Freeze();
                dc.DrawGeometry(null, arm % 2 == 1 ? b : a, g);
            }
        }
    }

    /// <summary>
    /// Tip "someone else can drive": a thumb taps the big button on a phone and the lamp on the
    /// desktop panel lights; then it drags the phone's slider and the panel's slider follows.
    /// Each move runs down the link as beads. Everything slides home for the next loop. 5.2 s.
    /// </summary>
    public sealed class RemoteTipArt : TipBScene
    {
        private const double Period = 5.2, TapAt = 0.5, LampAt = 1.0, DragAt = 2.2, DragEnd = 2.8, SlideAt = 3.25, HomeAt = 4.5;

        protected override void Paint(DrawingContext dc, double w, double h, double t)
        {
            CachedGround(dc, w, h, Deep(Accent));
            Motes(dc, w, h, t, Accent, 12, 111, new Rect(0.5, 0.06, 0.48, 0.88), 0.05, 0.005, 0.4);
            double tp = Kit.Phase(t, Period);
            double home = EaseOut(Kit.Win(tp, HomeAt, HomeAt + 0.45));

            // The desktop panel: a monitor on a stand.
            double mw = h * 0.56, mh = h * 0.38;
            var mc = new Point(w * 0.625, h * 0.4);
            var frame = Kit.Around(mc, mw, mh);
            var glass = Kit.Shrink(frame, mh * 0.08);
            dc.DrawDrawing(Layer("monitor", w, h, d =>
            {
                var neck = new Rect(mc.X - mw * 0.06, frame.Bottom - 2, mw * 0.12, mh * 0.24);
                var foot = new Rect(mc.X - mw * 0.24, neck.Bottom - mh * 0.02, mw * 0.48, mh * 0.08);
                Plate(d, foot, foot.Height / 2, Fill(Mix(Accent, Kit.Ink, 0.62)), h * 0.014);
                Plate(d, neck, mw * 0.02, Fill(Mix(Accent, Kit.Ink, 0.66)), h * 0.01);
                Plate(d, frame, mh * 0.1, Fill(Mix(Accent, Kit.Ink, 0.52)), h * 0.022);
                Plate(d, glass, mh * 0.06, Fill(Kit.Ink, 0.95), 0, pressed: true);
            }));

            // The panel's lamp (left) and slider (right).
            double lampOn = Math.Max(0, Math.Min(Kit.Win(tp, LampAt, LampAt + 0.1), 1 - home));
            double lampKick = Kit.Spring(tp - LampAt, 7, 26);
            var lamp = new Point(glass.X + glass.Width * 0.24, glass.Y + glass.Height * 0.5);
            double lr0 = glass.Height * 0.2, lr = lr0 * (1 + 0.14 * lampKick);
            dc.DrawEllipse(Fill(Mix(Kit.Ink, Accent, 0.25)), Bevel(Math.Max(1, lr0 * 0.1), pressed: true), lamp, lr0 * 1.14, lr0 * 1.14);
            if (lampOn > 0)
            {
                Glow(dc, lamp, lr * 2.4, Kit.Pink, 0.35 * lampOn);
                dc.DrawEllipse(Fill(Kit.Pink, lampOn), null, lamp, lr, lr);
                dc.DrawEllipse(Fill(Colors.White, 0.55 * lampOn), null, new Point(lamp.X - lr * 0.32, lamp.Y - lr * 0.32), lr * 0.28, lr * 0.28);
            }
            else dc.DrawEllipse(Fill(Mix(Kit.Ink, Accent, 0.4)), null, lamp, lr0, lr0);
            var track = new Rect(glass.X + glass.Width * 0.48, glass.Y + glass.Height * 0.5 - glass.Height * 0.06, glass.Width * 0.42, glass.Height * 0.12);
            Plate(dc, track, track.Height / 2, Fill(Mix(Kit.Ink, Accent, 0.2)), 0, pressed: true);
            double mpos = 0.15 + 0.7 * BackOut(Kit.Win(tp, SlideAt, SlideAt + 0.45), 1.8) * (1 - home);
            var mfill = new Rect(track.X, track.Y, Math.Max(track.Height, track.Width * mpos), track.Height);
            dc.DrawRoundedRectangle(Fill(Accent, 0.85), null, mfill, track.Height / 2, track.Height / 2);
            var mk = new Point(track.X + track.Width * mpos, track.Y + track.Height / 2);
            double mkr = track.Height * 0.95;
            dc.DrawEllipse(Fill(Kit.Shade, 0.4), null, new Point(mk.X + mkr * 0.15, mk.Y + mkr * 0.25), mkr, mkr);
            dc.DrawEllipse(Fill(Mix(Colors.White, Accent, 0.15)), Bevel(Math.Max(1, mkr * 0.12)), mk, mkr, mkr);
            // A wash on the glass whenever a command lands.
            double wash = Math.Max(tp >= LampAt ? Math.Exp(-(tp - LampAt) * 5) : 0, tp >= SlideAt ? Math.Exp(-(tp - SlideAt) * 5) : 0);
            if (wash > 0.02) dc.DrawRoundedRectangle(Fill(Mix(Accent, Colors.White, 0.4), 0.18 * wash), null, glass, mh * 0.06, mh * 0.06);
            Sheen(dc, new RectangleGeometry(glass, mh * 0.06, mh * 0.06), glass, t, 5.2, 3.9, 0.5);

            // The phone, held at a tilt, bobbing.
            double pw = h * 0.25, ph = h * 0.48;
            var pc = new Point(w * 0.885, h * 0.58 + Bob(t, h * 0.01, 1.3));
            double tilt = 9 + Math.Sin(t * 0.7) * 1.5;
            var pose = Kit.Pose(pc.X, pc.Y, tilt);
            FloorShadow(dc, new Point(pc.X + h * 0.02, pc.Y + ph * 0.58), pw * 0.6, h * 0.025, 0.7);

            // The link, from the phone's top edge to the panel.
            var top = pose.Transform(new Point(0, -ph / 2 - h * 0.012));
            var to = new Point(frame.Right - mh * 0.05, frame.Y + mh * 0.2);
            var ctrl = new Point((top.X + to.X) / 2, Math.Min(top.Y, to.Y) - h * 0.1);
            Link(dc, top, ctrl, to, h, 12);
            Beads(dc, top, ctrl, to, Kit.Win(tp, TapAt + 0.05, LampAt), h);
            Beads(dc, top, ctrl, to, Kit.Win(tp, DragEnd - 0.05, SlideAt), h);
            // Little signal arcs above the phone when it sends.
            var wavePen = PenOf(Mix(Kit.Pink, Colors.White, 0.4), Math.Max(1, h * 0.007), h);
            foreach (double at in new[] { TapAt + 0.05, DragEnd - 0.05 })
            {
                for (int n = 0; n < 3; n++)
                {
                    double age = tp - at - n * 0.09;
                    if (age < 0 || age > 0.5) continue;
                    dc.PushOpacity(1 - age / 0.5);
                    Kit.Arc(dc, top, h * (0.03 + age * 0.12), -Math.PI / 2 + tilt * Math.PI / 180, 1.3, wavePen);
                    dc.Pop();
                }
            }

            dc.PushTransform(new MatrixTransform(pose));
            var body = new Rect(-pw / 2, -ph / 2, pw, ph);
            var screen = Kit.Shrink(body, pw * 0.07);
            dc.DrawDrawing(Layer("phone", w, h, d =>
            {
                Plate(d, body, pw * 0.18, Fill(Mix(Accent, Kit.Ink, 0.48)), h * 0.022);
                Plate(d, screen, pw * 0.13, Fill(Kit.Ink, 0.95), 0, pressed: true);
                d.DrawRoundedRectangle(Fill(Mix(Accent, Kit.Ink, 0.45)), null, new Rect(-pw * 0.14, screen.Y + pw * 0.05, pw * 0.28, pw * 0.05), pw * 0.025, pw * 0.025);
            }));

            // The big button: pressed in under the thumb, lit while the lamp is lit.
            var btn = new Point(0, screen.Y + screen.Height * 0.36);
            double br = pw * 0.27;
            double press = Math.Max(0, 1 - Math.Abs(tp - TapAt) / 0.12);
            bool pressedIn = tp >= TapAt - 0.05 && tp < TapAt + 0.18;
            var btnCol = Mix(Mix(Accent, Kit.Ink, 0.15), Kit.Pink, lampOn);
            var bc = new Point(btn.X, btn.Y + press * br * 0.06);
            if (!pressedIn) dc.DrawEllipse(Fill(Kit.Shade, 0.45), null, new Point(btn.X + br * 0.08, btn.Y + br * 0.14), br, br);
            dc.DrawEllipse(Fill(btnCol), Bevel(Math.Max(1, br * 0.08), pressedIn), bc, br, br);
            dc.DrawEllipse(null, PenOf(Mix(btnCol, Colors.White, 0.5), Math.Max(1, br * 0.07), h), bc, br * 0.5, br * 0.5);
            double ripple = Kit.Win(tp, TapAt, TapAt + 0.5);
            if (ripple > 0 && ripple < 1)
            {
                dc.PushOpacity(1 - ripple);
                dc.DrawEllipse(null, PenOf(Colors.White, Math.Max(1, br * 0.06), h), btn, br * (1 + ripple * 0.7), br * (1 + ripple * 0.7));
                dc.Pop();
            }

            // The phone's slider.
            var ptrack = new Rect(screen.X + screen.Width * 0.14, screen.Y + screen.Height * 0.76, screen.Width * 0.72, pw * 0.09);
            dc.DrawRoundedRectangle(Fill(Mix(Kit.Ink, Accent, 0.22)), Bevel(1, pressed: true), ptrack, ptrack.Height / 2, ptrack.Height / 2);
            double drag = EaseInOut(Kit.Win(tp, DragAt + 0.1, DragEnd));
            double ppos = 0.15 + 0.7 * drag * (1 - home);
            dc.DrawRoundedRectangle(Fill(Accent, 0.85), null, new Rect(ptrack.X, ptrack.Y, Math.Max(ptrack.Height, ptrack.Width * ppos), ptrack.Height), ptrack.Height / 2, ptrack.Height / 2);
            var pk = new Point(ptrack.X + ptrack.Width * ppos, ptrack.Y + ptrack.Height / 2);
            dc.DrawEllipse(Fill(Mix(Colors.White, Accent, 0.15)), Bevel(1), pk, ptrack.Height * 0.95, ptrack.Height * 0.95);

            // The thumb: a soft fingertip that comes in, taps, then drags.
            double tapIn = Math.Min(Kit.Win(tp, TapAt - 0.35, TapAt - 0.1), 1 - Kit.Win(tp, TapAt + 0.25, TapAt + 0.5));
            double dragIn = Math.Min(Kit.Win(tp, DragAt - 0.3, DragAt), 1 - Kit.Win(tp, DragEnd + 0.1, DragEnd + 0.35));
            if (tapIn > 0) Thumb(dc, new Point(bc.X + br * 0.15, bc.Y + br * 0.2 + (1 - tapIn) * pw * 0.3), pw, tapIn, press);
            if (dragIn > 0) Thumb(dc, new Point(pk.X + pw * 0.02, pk.Y + pw * 0.04 + (1 - dragIn) * pw * 0.3), pw, dragIn, tp > DragAt && tp < DragEnd ? 0.6 : 0);
            dc.Pop();

            Burst(dc, lamp, lr0 * 2.4, h * 0.026, Kit.Win(tp, LampAt, LampAt + 0.6), Kit.Pink, 7, -0.3);
            Burst(dc, mk, mkr * 4, h * 0.022, Kit.Win(tp, SlideAt + 0.25, SlideAt + 0.8), Mix(Accent, Colors.White, 0.4), 6, 0.2);
            Twinkles(dc, w, h, t, Mix(Accent, Colors.White, 0.4), 3, 113, new Rect(0.52, 0.06, 0.44, 0.3), 0.024);
            Motes(dc, w, h, t, Colors.White, 5, 117, new Rect(0.55, 0.2, 0.42, 0.75), 0.1, 0.008, 0.3);
        }

        private static double EaseInOut(double k) => k < 0.5 ? 4 * k * k * k : 1 - Math.Pow(-2 * k + 2, 3) / 2;

        /// <summary>A fingertip seen from above: a pale rounded tip with a nail; pressing squashes it a touch.</summary>
        private void Thumb(DrawingContext dc, Point at, double pw, double a, double press)
        {
            // A finger reaching in from the lower right: tip on the point, the rest running off
            // past the phone's edge, a nail at the tip and a knuckle crease, all in one skin tone.
            double fw = pw * 0.3 * (1 + press * 0.06), len = pw * 1.5;
            var skin = Mix(Color.FromRgb(0xf4, 0xcf, 0xdc), Accent, 0.1);
            dc.PushOpacity(a);
            dc.PushTransform(new MatrixTransform(Kit.Pose(at.X, at.Y - press * fw * 0.05, -28)));
            var finger = new Rect(-fw / 2, -fw * 0.45, fw, len);
            dc.DrawRoundedRectangle(Fill(Kit.Shade, 0.32), null, new Rect(finger.X + fw * 0.18, finger.Y + fw * 0.22, fw, len), fw / 2, fw / 2);
            dc.DrawRoundedRectangle(Fill(skin), Bevel(Math.Max(1, fw * 0.07)), finger, fw / 2, fw / 2);
            dc.DrawRoundedRectangle(Fill(Mix(skin, Colors.White, 0.55)), PenOf(Mix(skin, Kit.Ink, 0.25), Math.Max(0.8, fw * 0.04), ActualHeight),
                new Rect(-fw * 0.28, -fw * 0.32, fw * 0.56, fw * 0.62), fw * 0.26, fw * 0.26);
            dc.PushOpacity(0.35);
            dc.DrawLine(PenOf(Mix(skin, Kit.Ink, 0.4), Math.Max(0.8, fw * 0.05), ActualHeight), new Point(-fw * 0.25, fw * 0.75), new Point(fw * 0.25, fw * 0.72));
            dc.Pop();
            dc.Pop();
            dc.Pop();
        }
    }
}
