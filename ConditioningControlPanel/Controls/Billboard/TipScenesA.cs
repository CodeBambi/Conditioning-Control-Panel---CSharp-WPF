using System;
using System.Windows;
using System.Windows.Media;

namespace ConditioningControlPanel.Controls.Billboard
{
    /// <summary>
    /// Shared bits for the first five tip scenes (quests, awareness, programs, deeper, blink). Each
    /// scene is a short loop drawn in code on the right half of the card, one lamp top left, raised
    /// plates with bevels and soft shadows. Static pieces live in cached layers; a frame only moves
    /// them and draws what changes.
    /// </summary>
    public abstract class TipSceneABase : BillboardVectorArt
    {
        protected static readonly Color Ink = Color.FromRgb(0x16, 0x0c, 0x24);
        protected static readonly Color Pink = Color.FromRgb(0xff, 0x6f, 0xb8);
        protected static readonly Color Paper = Color.FromRgb(0xf7, 0xf0, 0xff);
        private static readonly Color ShadowInk = Color.FromRgb(4, 4, 12);

        /// <summary>The subject's yardstick: about the width of the art area, never taller than the card.</summary>
        protected static double Unit(double w, double h) => Math.Min(w * 0.46, h * 0.98);

        protected static Matrix Pose(double x, double y, double degrees = 0, double sx = 1, double sy = 1)
        {
            var m = Matrix.Identity;
            m.Scale(sx, sy);
            m.Rotate(degrees);
            m.Translate(x, y);
            return m;
        }

        /// <summary>A face lit from the top left: lighter there, the hue in the middle, shaded bottom right.</summary>
        protected static Brush Lit(Color c, double up = 0.3, double down = 0.3)
        {
            var g = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0.55, 1) };
            g.GradientStops.Add(new GradientStop(Mix(c, Colors.White, up), 0));
            g.GradientStops.Add(new GradientStop(c, 0.5));
            g.GradientStops.Add(new GradientStop(Mix(c, Ink, down), 1));
            g.Freeze();
            return g;
        }

        /// <summary>The two stacked shades a raised piece throws down and right, under any transform.</summary>
        protected void DropShadow(DrawingContext dc, Matrix pose, Rect r, double radius, double lift)
        {
            if (lift <= 0) return;
            var m1 = pose; m1.Translate(lift * 0.9, lift * 1.4);
            var m2 = pose; m2.Translate(lift * 0.45, lift * 0.7);
            dc.PushTransform(new MatrixTransform(m1));
            dc.DrawRoundedRectangle(Fill(ShadowInk, 0.22), null, r, radius, radius);
            dc.Pop();
            dc.PushTransform(new MatrixTransform(m2));
            dc.DrawRoundedRectangle(Fill(ShadowInk, 0.3), null, r, radius, radius);
            dc.Pop();
        }

        /// <summary>A soft oval shadow on the floor under something hopping.</summary>
        protected void FloorShadow(DrawingContext dc, Point c, double rx, double ry, double a)
        {
            if (a <= 0.01) return;
            dc.DrawEllipse(Fill(ShadowInk, 0.18 * a), null, c, rx, ry);
            dc.DrawEllipse(Fill(ShadowInk, 0.22 * a), null, c, rx * 0.7, ry * 0.7);
        }

        /// <summary>Glints flung out from a point, fading as they go.</summary>
        protected void Burst(DrawingContext dc, Point c, double reach, double size, double k, int count, int seed, Color hue)
        {
            if (!Particles || k <= 0 || k >= 1) return;
            for (int i = 0; i < count; i++)
            {
                double a = i * Math.PI * 2 / count + Hash(seed, i, 1) * 0.6;
                double d = reach * (0.45 + 0.55 * EaseOut(k)) * (0.75 + Hash(seed, i, 2) * 0.45);
                var p = new Point(c.X + Math.Cos(a) * d, c.Y + Math.Sin(a) * d * 0.8);
                Spark(dc, p, size * (1 - k) * (0.7 + Hash(seed, i, 3) * 0.5), i % 2 == 0 ? Colors.White : hue, 1 - k, k * 3 + i);
            }
        }

        protected static double Clamp01(double v) => Math.Clamp(v, 0, 1);

        /// <summary>A hump 0..1..0 over the window [a, b], zero outside.</summary>
        protected static double Hump(double u, double a, double b) =>
            u <= a || u >= b ? 0 : Math.Sin((u - a) / (b - a) * Math.PI);

        /// <summary>A squash that lands, overshoots and settles: 0 at rest, positive = squashed.</summary>
        protected static double Squash(double since, double length = 0.35) =>
            since < 0 || since > length ? 0 : Math.Sin(since / length * Math.PI * 2) * (1 - since / length);

        private static Geometry Frozen(Geometry g)
        {
            g.Freeze();
            return g;
        }

        /// <summary>A five point star of radius 1.</summary>
        protected static readonly Geometry StarUnit = Frozen(MakeStar());

        private static Geometry MakeStar()
        {
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                for (int i = 0; i < 10; i++)
                {
                    double a = -Math.PI / 2 + i * Math.PI / 5, r = i % 2 == 0 ? 1 : 0.46;
                    var p = new Point(Math.Cos(a) * r, Math.Sin(a) * r);
                    if (i == 0) ctx.BeginFigure(p, true, true); else ctx.LineTo(p, false, false);
                }
            }
            return g;
        }

        /// <summary>A crescent moon of radius 1.</summary>
        protected static readonly Geometry MoonUnit = Frozen(new CombinedGeometry(GeometryCombineMode.Exclude,
            new EllipseGeometry(new Point(0, 0), 1, 1), new EllipseGeometry(new Point(0.48, -0.3), 0.86, 0.86))
            .GetFlattenedPathGeometry(0.01, ToleranceType.Absolute));

        /// <summary>A diamond (a gem) of radius 1.</summary>
        protected static readonly Geometry GemUnit = Frozen(Poly(new Point(0, -1), new Point(0.78, -0.25), new Point(0, 1), new Point(-0.78, -0.25)));

        /// <summary>A lightning bolt about 2 tall.</summary>
        protected static readonly Geometry BoltUnit = Frozen(Poly(new Point(0.2, -1), new Point(-0.5, 0.12), new Point(-0.04, 0.12),
            new Point(-0.22, 1), new Point(0.52, -0.18), new Point(0.06, -0.18)));

        /// <summary>A tick mark, as a stroke path in a 2-wide box.</summary>
        protected static readonly Geometry TickUnit = Frozen(Open(new Point(-0.7, 0.05), new Point(-0.2, 0.55), new Point(0.75, -0.55)));

        /// <summary>The reroll arrow: three quarters of a circle with a head, radius 1.</summary>
        protected static readonly Geometry RerollArc = Frozen(MakeArc());
        protected static readonly Geometry RerollHead = Frozen(MakeHead());

        private const double ArcStart = -Math.PI * 0.3, ArcSweep = Math.PI * 1.5;

        private static Geometry MakeArc()
        {
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                double a1 = ArcStart - ArcSweep;
                ctx.BeginFigure(new Point(Math.Cos(ArcStart), Math.Sin(ArcStart)), false, false);
                ctx.ArcTo(new Point(Math.Cos(a1), Math.Sin(a1)), new Size(1, 1), 0, true, SweepDirection.Counterclockwise, true, true);
            }
            return g;
        }

        private static Geometry MakeHead()
        {
            // At the arc's start, pointing along the clockwise tangent (the way it spins).
            double a = ArcStart;
            var p = new Point(Math.Cos(a), Math.Sin(a));
            var tan = new Vector(-Math.Sin(a), Math.Cos(a));
            var nrm = new Vector(Math.Cos(a), Math.Sin(a));
            return Poly(p + tan * 0.55, p + nrm * 0.42 - tan * 0.12, p - nrm * 0.42 - tan * 0.12);
        }

        protected static Geometry Poly(params Point[] pts)
        {
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                ctx.BeginFigure(pts[0], true, true);
                for (int i = 1; i < pts.Length; i++) ctx.LineTo(pts[i], false, true);
            }
            return g;
        }

        private static Geometry Open(params Point[] pts)
        {
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                ctx.BeginFigure(pts[0], false, false);
                for (int i = 1; i < pts.Length; i++) ctx.LineTo(pts[i], true, true);
            }
            return g;
        }

        /// <summary>Draws a unit shape scaled to radius <paramref name="r"/> at <paramref name="c"/>.</summary>
        protected static void Shape(DrawingContext dc, Geometry unit, Point c, double r, Brush? fill, Pen? pen = null, double degrees = 0)
        {
            dc.PushTransform(new MatrixTransform(Pose(c.X, c.Y, degrees, r, r)));
            dc.DrawGeometry(fill, pen, unit);
            dc.Pop();
        }

        protected static Pen UnitPen(Color c, double thickness, double a = 1)
        {
            var p = new Pen(Solid(c, a), thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
            p.Freeze();
            return p;
        }

        /// <summary>A rounded "word": a pill of ink standing in for text, never real glyphs.</summary>
        protected void Word(DrawingContext dc, Rect r, Color c, double a) =>
            dc.DrawRoundedRectangle(Fill(c, a), null, r, r.Height / 2, r.Height / 2);
    }

    /// <summary>
    /// Tip "a quest can be swapped": a quest card stands on the right with a die beside it. The die
    /// crouches, hops up tumbling, lands with a squash, the reroll button on the card spins and the
    /// card flips over to a new quest with a burst of glints. Every loop shows the next quest.
    /// </summary>
    public sealed class QuestsTipArt : TipSceneABase
    {
        private const double Loop = 5.0;
        private const double Toss = 1.0, Land = 1.85, FlipEnd = 2.4;
        private Geometry? _cardClip;
        private double _clipW;
        private Brush? _dieBrush;

        protected override void Paint(DrawingContext dc, double w, double h, double t)
        {
            CachedGround(dc, w, h, Deep(Accent));
            Motes(dc, w, h, t, Accent, 12, 31, new Rect(0.5, 0.08, 0.48, 0.86), 0.05, 0.006, 0.4);

            double S = Unit(w, h), cx = w * 0.69, cy = h * 0.5;
            double u = t % Loop;
            int cycle = (int)Math.Floor(t / Loop);
            double cw = S * 0.5, ch = S * 0.7, cr = cw * 0.09;
            double kIn = BackOut(t / 0.8);

            // The card: flips edge-on and back over the land, swapping its face at the middle.
            double f = Clamp01((u - Land) / (FlipEnd - Land));
            int face = f < 0.5 ? cycle : cycle + 1;
            int v = ((face % 3) + 3) % 3;
            double sx = Math.Max(0.03, Math.Abs(Math.Cos(f * Math.PI)));
            double pop = 1 + 0.06 * Hump(u, FlipEnd, FlipEnd + 0.35);
            double hop = Math.Sin(f * Math.PI) * S * 0.05;
            double bob = Bob(t, S * 0.012);
            double rot = -5 + Math.Sin(t * 0.7) * 1.2 + Math.Sin(f * Math.PI) * 3;
            double y = cy + bob - hop + (1 - kIn) * h * 0.45;
            var rect = new Rect(-cw / 2, -ch / 2, cw, ch);
            var pose = Pose(cx, y, rot, sx * pop, pop);

            dc.PushOpacity(Clamp01(kIn * 1.4));
            DropShadow(dc, pose, rect, cr, S * 0.03 + hop * 0.6);
            dc.PushTransform(new MatrixTransform(pose));
            dc.DrawDrawing(Layer("card" + v, w, h, d => PaintCard(d, cw, ch, cr, v)));
            if (f > 0 && f < 1) dc.DrawRoundedRectangle(Fill(Ink, 0.55 * Math.Sin(f * Math.PI)), null, rect, cr, cr);
            if (_cardClip == null || _clipW != cw) { _cardClip = new RectangleGeometry(rect, cr, cr); _cardClip.Freeze(); _clipW = cw; }
            Sheen(dc, _cardClip, rect, t, Loop, Loop - FlipEnd - 0.1, 0.8);
            dc.Pop();

            // The reroll button on the card's top right corner: pressed as the die lands, spins with the flip.
            var corner = pose.Transform(new Point(cw * 0.5 - cw * 0.04, -ch * 0.5 + cw * 0.04));
            double br = S * 0.072;
            bool pressed = u > Land - 0.05 && u < FlipEnd;
            var bpose = Pose(corner.X, corner.Y);
            var brect = new Rect(-br, -br, br * 2, br * 2);
            if (!pressed) DropShadow(dc, bpose, brect, br, S * 0.012);
            dc.PushTransform(new MatrixTransform(bpose));
            Plate(dc, brect, br, Fill(pressed ? Mix(Pink, Ink, 0.15) : Pink), 0, pressed);
            dc.Pop();
            double spin = EaseOut(f) * 360 + Math.Sin(t * 0.9) * 6;
            Shape(dc, RerollArc, corner, br * 0.52, null, UnitPen(Colors.White, 0.26), spin);
            Shape(dc, RerollHead, corner, br * 0.52, Fill(Colors.White), null, spin);
            dc.Pop();

            PaintDie(dc, S, cx, cy, cw, ch, u, cycle, kIn);

            // Glints when the new quest lands, then twinkles and near motes.
            Burst(dc, new Point(cx, y), cw * 0.95, S * 0.04, (u - FlipEnd) / 0.6, 10, 7 + cycle, Mix(Pink, Colors.White, 0.3));
            Twinkles(dc, w, h, t, Mix(Accent, Colors.White, 0.35), 4, 13, new Rect(0.52, 0.1, 0.44, 0.8), 0.028);
            Motes(dc, w, h, t, Colors.White, 5, 19, new Rect(0.55, 0.2, 0.42, 0.8), 0.1, 0.008, 0.3);
        }

        private void PaintDie(DrawingContext dc, double S, double cx, double cy, double cw, double ch, double u, int cycle, double kIn)
        {
            double d = S * 0.16;
            var rest = new Point(cx + cw * 0.62 + d * 0.15, cy + ch * 0.5 - d * 0.5);
            double r = Clamp01((u - Toss) / (Land - Toss));
            bool flying = u > Toss && u < Land;
            double air = flying ? 4 * r * (1 - r) : 0;
            double x = rest.X + (flying ? Math.Sin(r * Math.PI) * S * 0.07 : 0);
            double y = rest.Y - air * S * 0.5 + (1 - kIn) * S * 0.6;

            // Crouch before the toss, stretch in the air, squash on landing.
            double crouch = Hump(u, Toss - 0.3, Toss + 0.04) * 0.18;
            double land = Squash(u - Land, 0.4) * 0.22;
            double stretch = flying ? Hump(r, 0, 0.35) * 0.12 : 0;
            double sy = 1 - crouch - land + stretch, sxx = 1 + crouch * 0.8 + land * 0.8 - stretch * 0.6;
            double rot = flying ? r * 450 : -6;

            FloorShadow(dc, new Point(rest.X, rest.Y + d * 0.52), d * 0.62 * (1 - air * 0.45), d * 0.16 * (1 - air * 0.45), Clamp01(kIn) * (1 - air * 0.5));

            int value = flying ? 1 + (int)(r * 9) % 6 : 1 + (int)(Hash(3, u >= Land ? cycle : cycle - 1, 1) * 6) % 6;
            var pose = Pose(x, y + d * 0.5 * (1 - sy), rot, sxx, sy);
            dc.PushOpacity(Clamp01(kIn * 1.4));
            dc.PushTransform(new MatrixTransform(pose));
            var body = new Rect(-d / 2, -d / 2, d, d);
            Plate(dc, body, d * 0.22, _dieBrush ??= Lit(Paper, 0.3, 0.22), 0);
            var pip = Fill(Mix(Accent, Ink, 0.55));
            double pr = d * 0.085, o = d * 0.26;
            foreach (var p in Pips(value))
                dc.DrawEllipse(pip, null, new Point(p.X * o, p.Y * o), pr, pr);
            dc.Pop();
            dc.Pop();

            // Dust where it lands.
            Burst(dc, new Point(rest.X, rest.Y + d * 0.45), d * 0.9, S * 0.025, (u - Land) / 0.45, 6, 41 + cycle, Mix(Accent, Colors.White, 0.5));
        }

        private static Point[] Pips(int v) => v switch
        {
            1 => new[] { new Point(0, 0) },
            2 => new[] { new Point(-1, -1), new Point(1, 1) },
            3 => new[] { new Point(-1, -1), new Point(0, 0), new Point(1, 1) },
            4 => new[] { new Point(-1, -1), new Point(1, -1), new Point(-1, 1), new Point(1, 1) },
            5 => new[] { new Point(-1, -1), new Point(1, -1), new Point(0, 0), new Point(-1, 1), new Point(1, 1) },
            _ => new[] { new Point(-1, -1), new Point(1, -1), new Point(-1, 0), new Point(1, 0), new Point(-1, 1), new Point(1, 1) },
        };

        /// <summary>One quest face: an emblem in a medallion, two lines of "text", a checkbox and a reward row.</summary>
        private void PaintCard(DrawingContext dc, double cw, double ch, double cr, int v)
        {
            var tint = v switch { 0 => Accent, 1 => Pink, _ => Mix(Accent, Pink, 0.5) };
            var rect = new Rect(-cw / 2, -ch / 2, cw, ch);
            dc.DrawRoundedRectangle(Lit(Mix(Paper, tint, 0.14), 0.4, 0.2), null, rect, cr, cr);

            // A coloured band along the top, like a quest's category.
            dc.PushClip(new RectangleGeometry(rect, cr, cr));
            dc.DrawRectangle(Lit(tint, 0.25, 0.25), null, new Rect(rect.X, rect.Y, cw, ch * 0.14));
            dc.Pop();
            double th = Math.Max(1, cw * 0.022);
            dc.DrawRoundedRectangle(null, Bevel(th), new Rect(rect.X + th / 2, rect.Y + th / 2, rect.Width - th, rect.Height - th), cr - th / 2, cr - th / 2);

            // Medallion with the emblem.
            var mc = new Point(0, -ch * 0.1);
            double mr = cw * 0.25;
            dc.DrawEllipse(Fill(Ink, 0.18), null, new Point(mc.X + mr * 0.08, mc.Y + mr * 0.12), mr, mr);
            dc.DrawEllipse(Lit(tint, 0.35, 0.3), null, mc, mr, mr);
            dc.DrawEllipse(null, Bevel(Math.Max(1, mr * 0.08)), mc, mr * 0.96, mr * 0.96);
            var emblem = v switch { 0 => StarUnit, 1 => MoonUnit, _ => GemUnit };
            Shape(dc, emblem, mc, mr * 0.58, Fill(Colors.White, 0.95), null, v == 1 ? -20 : 0);

            // Two lines of text, a different length on each face.
            var ink = Mix(tint, Ink, 0.65);
            double lh = ch * 0.045;
            Word(dc, new Rect(-cw * 0.32, ch * 0.16, cw * (0.64 - v * 0.08), lh), ink, 0.55);
            Word(dc, new Rect(-cw * 0.32, ch * 0.24, cw * (0.4 + v * 0.1), lh), ink, 0.32);

            // A checkbox waiting, and three reward pips.
            double bs = cw * 0.15;
            Plate(dc, new Rect(-cw * 0.36, ch * 0.33, bs, bs), bs * 0.22, Fill(Mix(Paper, tint, 0.3)), 0, pressed: true);
            for (int i = 0; i < 3; i++)
                dc.DrawEllipse(Lit(tint, 0.4, 0.2), null, new Point(cw * (0.06 + i * 0.12), ch * 0.33 + bs / 2), bs * 0.24, bs * 0.24);
        }
    }

    /// <summary>
    /// Tip "the app can read the room": a monitor with lines of "words" scrolling up past a scan
    /// line. Every few lines one word crosses it, lights up pink with a ring, and a glint flies out
    /// to a trigger badge on the monitor's corner, which pops.
    /// </summary>
    public sealed class AwarenessTipArt : TipSceneABase
    {
        private Geometry? _screenClip;
        private Rect _screenRect;

        protected override void Paint(DrawingContext dc, double w, double h, double t)
        {
            CachedGround(dc, w, h, Deep(Accent));
            Motes(dc, w, h, t, Accent, 12, 51, new Rect(0.5, 0.08, 0.48, 0.86), 0.05, 0.006, 0.4);

            double S = Unit(w, h), cx = w * 0.71, cy = h * 0.45;
            double sw = S * 0.88, sh = S * 0.56, bz = sw * 0.045;
            double kIn = BackOut(t / 0.8);
            double oy = (1 - kIn) * h * 0.45;
            var screen = new Rect(-sw / 2 + bz, -sh / 2 + bz, sw - bz * 2, sh - bz * 2);
            if (_screenClip == null || _screenRect != screen)
            {
                _screenClip = new RectangleGeometry(screen, bz * 0.6, bz * 0.6);
                _screenClip.Freeze();
                _screenRect = screen;
            }

            dc.PushOpacity(Clamp01(kIn * 1.4));
            dc.PushTransform(new TranslateTransform(cx, cy + oy));
            dc.DrawDrawing(Layer("monitor", w, h, d => PaintMonitor(d, sw, sh, bz, screen)));

            // The scrolling text, clipped to the glass.
            double rowH = screen.Height / 6.2, speed = rowH / 0.6, scroll = t * speed;
            double scanY = screen.Top + screen.Height * 0.58;
            int first = (int)Math.Floor(scroll / rowH) - 3;
            double best = double.MaxValue;
            Point hit = default;
            Rect hitRect = default;
            dc.PushClip(_screenClip);
            dc.DrawRectangle(Fill(Accent, 0.08 + 0.03 * Math.Sin(t * 2.2)), null, new Rect(screen.X, scanY - rowH * 0.5, screen.Width, rowH));
            for (int k = 0; k < 12; k++)
            {
                int id = first + k;
                double yc = screen.Top + id * rowH - scroll + rowH * 0.5;
                if (yc < screen.Top - rowH || yc > screen.Bottom + rowH) continue;
                int n = 2 + (int)(Hash(7, id, 1) * 3);
                double x = screen.Left + screen.Width * 0.07, lh = rowH * 0.36;
                bool keyRow = ((id % 4) + 4) % 4 == 0;
                int keyWord = keyRow ? 1 + (int)(Hash(7, id, 9) * (n - 1)) : -1;
                double since = (scanY - yc) / speed;
                for (int i = 0; i < n && x < screen.Right - screen.Width * 0.12; i++)
                {
                    double ww = screen.Width * (0.1 + Hash(7, id * 5 + i, 2) * 0.17);
                    ww = Math.Min(ww, screen.Right - screen.Width * 0.08 - x);
                    var r = new Rect(x, yc - lh / 2, ww, lh);
                    if (i == keyWord)
                    {
                        if (since >= 0)
                        {
                            double flare = Math.Exp(-since * 2.5);
                            var big = new Rect(r.X - lh * 0.3, r.Y - lh * 0.3, r.Width + lh * 0.6, r.Height + lh * 0.6);
                            Word(dc, big, Pink, 0.3 + 0.45 * flare);
                            Word(dc, r, Mix(Pink, Colors.White, 0.35 + 0.5 * flare), 1);
                            if (since < best) { best = since; hit = new Point(r.X + r.Width / 2, r.Y + r.Height / 2); hitRect = big; }
                        }
                        else Word(dc, r, Mix(Paper, Pink, 0.3), 0.36);
                    }
                    else Word(dc, r, Paper, 0.26);
                    x += ww + lh * 0.9;
                }
            }
            // The scan line itself.
            dc.DrawRectangle(Fill(Mix(Accent, Colors.White, 0.4), 0.55 + 0.2 * Math.Sin(t * 2.2)), null, new Rect(screen.X, scanY - 0.75, screen.Width, 1.5));
            // The ring round the word as it catches.
            if (best < 0.45)
            {
                double k = best / 0.45, g = EaseOut(k) * S * 0.05;
                var ring = new Rect(hitRect.X - g, hitRect.Y - g, hitRect.Width + g * 2, hitRect.Height + g * 2);
                dc.DrawRoundedRectangle(null, Stroke(Mix(Pink, Colors.White, 0.4), Math.Max(1, S * 0.008), 1 - k), ring, ring.Height / 2, ring.Height / 2);
            }
            dc.Pop();
            dc.DrawDrawing(Layer("glass", w, h, d => PaintGlass(d, screen)));
            Sheen(dc, _screenClip, screen, t, 5.3, 2.2, 0.5);
            dc.Pop();

            // The glint flies from the word to the badge, which pops when it arrives.
            double brr = S * 0.085;
            var badge = new Point(cx + sw * 0.5 - brr * 0.2, cy + oy - sh * 0.5 + brr * 0.2);
            var from = new Point(cx + hit.X, cy + oy + hit.Y);
            double fly = best / 0.5;
            double arrive = best - 0.5;
            double pop = arrive >= 0 ? 1 + 0.3 * Squash(arrive, 0.45) : 1;
            double lit = arrive >= 0 && arrive < 0.9 ? 1 - arrive / 0.9 : 0;
            var bpose = Pose(badge.X, badge.Y, Math.Sin(t * 1.1) * 4, pop, pop);
            var brect = new Rect(-brr, -brr, brr * 2, brr * 2);
            DropShadow(dc, bpose, brect, brr, S * 0.016);
            if (lit > 0) Glow(dc, badge, brr * (1.4 + lit * 0.6), Pink, 0.45 * lit);
            dc.PushTransform(new MatrixTransform(bpose));
            Plate(dc, brect, brr, Fill(Mix(Pink, Colors.White, 0.35 * lit)), 0);
            dc.Pop();
            Shape(dc, BoltUnit, badge, brr * 0.56 * pop, Fill(Colors.White), null, 8);
            if (best != double.MaxValue && fly < 1 && fly >= 0)
            {
                var ctl = new Point((from.X + badge.X) / 2 - S * 0.05, Math.Min(from.Y, badge.Y) - S * 0.2);
                for (int i = 4; i >= 0; i--)
                {
                    double q = Math.Max(0, EaseOut(fly) - i * 0.05);
                    var p = Quad(from, ctl, badge, q);
                    Spark(dc, p, S * (0.05 - i * 0.007), i == 0 ? Colors.White : Pink, 1 - i * 0.18, fly * 6 + i);
                }
            }
            Burst(dc, badge, brr * 2.1, S * 0.035, arrive / 0.6, 8, 61, Mix(Pink, Colors.White, 0.3));
            dc.Pop();

            Twinkles(dc, w, h, t, Mix(Accent, Colors.White, 0.35), 4, 23, new Rect(0.52, 0.08, 0.44, 0.82), 0.028);
            Motes(dc, w, h, t, Colors.White, 5, 29, new Rect(0.55, 0.2, 0.42, 0.8), 0.1, 0.008, 0.3);
        }

        private static Point Quad(Point a, Point c, Point b, double q)
        {
            double m = 1 - q;
            return new Point(m * m * a.X + 2 * m * q * c.X + q * q * b.X, m * m * a.Y + 2 * m * q * c.Y + q * q * b.Y);
        }

        private void PaintMonitor(DrawingContext dc, double sw, double sh, double bz, Rect screen)
        {
            // Stand: a neck and a foot, behind the bezel.
            double nw = sw * 0.12, fw = sw * 0.42;
            var neck = new Rect(-nw / 2, sh * 0.4, nw, sh * 0.26);
            var foot = new Rect(-fw / 2, sh * 0.62, fw, sh * 0.08);
            Plate(dc, neck, nw * 0.15, Lit(Mix(Accent, Ink, 0.55), 0.2, 0.3), sh * 0.03);
            Plate(dc, foot, foot.Height / 2, Lit(Mix(Accent, Ink, 0.45), 0.25, 0.3), sh * 0.03);
            var body = new Rect(-sw / 2, -sh / 2, sw, sh);
            Plate(dc, body, bz * 1.3, Lit(Mix(Accent, Ink, 0.4), 0.3, 0.35), sh * 0.06);
            // The glass, sunk into the bezel.
            Plate(dc, screen, bz * 0.6, Lit(Mix(Accent, Ink, 0.86), 0.06, 0.2), 0, pressed: true);
            // A power light on the chin.
            dc.DrawEllipse(Fill(Pink, 0.9), null, new Point(sw * 0.4, sh / 2 - bz * 0.5), bz * 0.16, bz * 0.16);
        }

        private static void PaintGlass(DrawingContext dc, Rect screen)
        {
            var g = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
            g.GradientStops.Add(new GradientStop(Color.FromArgb(0x22, 255, 255, 255), 0));
            g.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, 255, 255, 255), 0.42));
            g.Freeze();
            dc.DrawRoundedRectangle(g, null, screen, 4, 4);
        }
    }

    /// <summary>
    /// Tip "programs plan the days": a week on a wall calendar. A ball hops day to day; each day it
    /// lands on presses in and gets a tick with a puff of glints, the bar underneath fills, and on
    /// the day off it lands soft, a moon comes up and the ball naps a beat. Then the week starts over.
    /// </summary>
    public sealed class ProgramsTipArt : TipSceneABase
    {
        private const int Days = 7, Rest = 4;
        private const double Step = 0.55, Hold = 1.4, Back = 0.5, LandAt = 0.62;
        private static readonly double Loop = Days * Step + Hold + Back;
        private Brush? _ball;

        protected override void Paint(DrawingContext dc, double w, double h, double t)
        {
            CachedGround(dc, w, h, Deep(Accent));
            Motes(dc, w, h, t, Accent, 12, 71, new Rect(0.5, 0.08, 0.48, 0.86), 0.05, 0.006, 0.4);

            double S = Unit(w, h), cx = w * 0.72, cy = h * 0.54;
            double pw = S * 0.98, ph = S * 0.62;
            double kIn = BackOut(t / 0.8);
            double oy = (1 - kIn) * h * 0.45 + Bob(t, S * 0.006);
            var page = new Rect(-pw / 2, -ph / 2, pw, ph);
            double pad = pw * 0.05, gap = pw * 0.018;
            double tw = (pw - pad * 2 - gap * (Days - 1)) / Days, th = ph * 0.42;
            double tilesTop = -ph / 2 + ph * 0.32;

            double u = t % Loop;
            int step = (int)Math.Floor(u / Step);
            double q = (u % Step) / Step;
            double fade = Clamp01((u - (Days * Step + Hold)) / Back);

            dc.PushOpacity(Clamp01(kIn * 1.4));
            dc.PushTransform(new TranslateTransform(cx, cy + oy));
            dc.DrawDrawing(Layer("page", w, h, d => PaintPage(d, page, tw, th, pad, gap, tilesTop)));

            // Days pressed in so far: tick on a session day, moon on the day off.
            int done = u >= Days * Step ? Days : step + (q >= LandAt ? 1 : 0);
            for (int i = 0; i < done; i++)
            {
                var tile = TileRect(i, tw, th, pad, gap, tilesTop, pw);
                double since = u - (i * Step + Step * LandAt);
                double press = 1 - Squash(since, 0.3) * 0.12;
                var inner = new Rect(tile.X + tile.Width * (1 - press) / 2, tile.Y + tile.Height * (1 - press) / 2, tile.Width * press, tile.Height * press);
                dc.PushOpacity(1 - fade);
                if (i == Rest)
                {
                    Plate(dc, inner, tw * 0.16, Lit(Mix(Accent, Ink, 0.5), 0.15, 0.2), 0, pressed: true);
                    double rise = EaseOut(since / 0.5);
                    Shape(dc, MoonUnit, new Point(inner.X + inner.Width / 2, inner.Y + inner.Height * (0.62 - 0.12 * rise)), tw * 0.26, Fill(Mix(Paper, Accent, 0.15), rise), null, -25);
                }
                else
                {
                    Plate(dc, inner, tw * 0.16, Lit(Mix(Pink, Colors.White, 0.1), 0.2, 0.25), 0, pressed: true);
                    double draw = EaseOut(since / 0.25);
                    Shape(dc, TickUnit, new Point(inner.X + inner.Width / 2, inner.Y + inner.Height * 0.55), tw * 0.3 * (0.6 + 0.4 * draw), null, UnitPen(Colors.White, 0.28, draw));
                }
                dc.Pop();
            }

            // The progress bar under the week fills with the sessions done.
            double barW = pw - pad * 2, barY = tilesTop + th + ph * 0.1, barH = ph * 0.07;
            int sessions = 0;
            for (int i = 0; i < done; i++) if (i != Rest) sessions++;
            double fill = sessions / (double)(Days - 1) * (1 - fade);
            if (fill > 0)
            {
                var bar = new Rect(-pw / 2 + pad + barH * 0.15, barY + barH * 0.15, Math.Max(barH * 0.7, (barW - barH * 0.3) * fill), barH * 0.7);
                dc.DrawRoundedRectangle(Lit(Pink, 0.35, 0.2), null, bar, bar.Height / 2, bar.Height / 2);
            }

            PaintBall(dc, u, step, q, fade, tw, th, pad, gap, tilesTop, pw, ph);
            dc.Pop();
            dc.Pop();

            // A puff from the tile it just pressed.
            if (step < Days)
            {
                var tile = TileRect(step, tw, th, pad, gap, tilesTop, pw);
                var at = new Point(cx + tile.X + tile.Width / 2, cy + oy + tile.Y);
                if (step != Rest) Burst(dc, at, tw * 1.1, S * 0.03, (q - LandAt) / (1 - LandAt), 7, 81 + step, Mix(Pink, Colors.White, 0.3));
            }
            if (u >= Days * Step && u < Days * Step + 0.6)
                Burst(dc, new Point(cx, cy + oy + barY), pw * 0.55, S * 0.04, (u - Days * Step) / 0.6, 12, 91, Mix(Pink, Colors.White, 0.3));

            Twinkles(dc, w, h, t, Mix(Accent, Colors.White, 0.35), 4, 33, new Rect(0.52, 0.08, 0.44, 0.82), 0.028);
            Motes(dc, w, h, t, Colors.White, 5, 39, new Rect(0.55, 0.2, 0.42, 0.8), 0.1, 0.008, 0.3);
        }

        private static Rect TileRect(int i, double tw, double th, double pad, double gap, double top, double pw) =>
            new(-pw / 2 + pad + i * (tw + gap), top, tw, th);

        private void PaintBall(DrawingContext dc, double u, int step, double q, double fade, double tw, double th,
            double pad, double gap, double top, double pw, double ph)
        {
            double br = tw * 0.4;
            Point Seat(int i)
            {
                var r = TileRect(i, tw, th, pad, gap, top, pw);
                return new Point(r.X + r.Width / 2, r.Y - br);
            }
            Point p;
            double sy = 1, sx = 1;
            var above0 = new Point(Seat(0).X, -ph / 2 - br * 2.5);
            if (u < Days * Step)
            {
                var from = step == 0 ? above0 : Seat(step - 1);
                var to = Seat(step);
                double k = Clamp01(q / LandAt);
                double arc = step == 0 ? 0 : 4 * k * (1 - k) * th * 0.8;
                double ease = step == 0 ? k * k : k;
                p = new Point(from.X + (to.X - from.X) * k, from.Y + (to.Y - from.Y) * ease - arc);
                double since = u - (step * Step + Step * LandAt);
                bool nap = step == Rest && since > 0;
                double land = Squash(since, nap ? 0.5 : 0.3) * (nap ? 0.15 : 0.24);
                double stretch = since < 0 ? Hump(k, 0.1, 0.9) * 0.12 : 0;
                double crouch = step < Days - 1 ? Hump(q, 0.85, 1.0) * 0.2 : 0;
                double napSquash = nap ? 0.12 * Hump(since, 0, (1 - LandAt) * Step + 0.01) : 0;
                sy = 1 - land + stretch - crouch - napSquash;
                sx = 1 + land * 0.8 - stretch * 0.5 + crouch * 0.7 + napSquash * 0.8;
            }
            else if (fade <= 0)
            {
                // The week is done: a happy bounce on the last day.
                double hold = u - Days * Step;
                double jump = Math.Abs(Math.Sin(hold * Math.PI / 0.7)) * th * 0.3 * Math.Exp(-hold * 1.2);
                p = new Point(Seat(Days - 1).X, Seat(Days - 1).Y - jump);
            }
            else
            {
                // Back to the start of the week, over the top of the page.
                var from = Seat(Days - 1);
                double k = EaseOut(fade);
                double arc = Math.Sin(k * Math.PI) * ph * 0.25;
                p = new Point(from.X + (above0.X - from.X) * k, from.Y + (above0.Y - from.Y) * k - arc);
            }

            double ground = top;
            double height = Math.Max(0, ground - (p.Y + br));
            FloorShadow(dc, new Point(p.X, ground + tw * 0.04), br * Math.Max(0.3, 1 - height / (th * 1.2)), br * 0.22, Clamp01(1 - height / (th * 1.4)));
            var pose = Pose(p.X, p.Y + br * (1 - sy), 0, sx, sy);
            dc.PushTransform(new MatrixTransform(pose));
            dc.DrawEllipse(BallBrush(), null, new Point(0, 0), br, br);
            dc.DrawEllipse(null, Bevel(Math.Max(1, br * 0.12)), new Point(0, 0), br * 0.94, br * 0.94);
            dc.DrawEllipse(Fill(Colors.White, 0.75), null, new Point(-br * 0.35, -br * 0.38), br * 0.2, br * 0.14);
            dc.Pop();
        }

        private Brush BallBrush()
        {
            if (_ball != null) return _ball;
            var g = new RadialGradientBrush(Mix(Pink, Colors.White, 0.45), Mix(Pink, Ink, 0.35)) { GradientOrigin = new Point(0.3, 0.28), Center = new Point(0.42, 0.4), RadiusX = 0.7, RadiusY = 0.7 };
            g.Freeze();
            return _ball = g;
        }

        private void PaintPage(DrawingContext dc, Rect page, double tw, double th, double pad, double gap, double top)
        {
            double r = page.Width * 0.05;
            Plate(dc, page, r, Lit(Mix(Paper, Accent, 0.12), 0.35, 0.18), page.Width * 0.035);
            // The header band in the hue, with two binder rings.
            var head = new Rect(page.X, page.Y, page.Width, page.Height * 0.16);
            dc.PushClip(new RectangleGeometry(page, r, r));
            dc.DrawRectangle(Lit(Accent, 0.25, 0.3), null, head);
            dc.Pop();
            foreach (var fx in new[] { -0.3, 0.3 })
            {
                var c = new Point(page.Width * fx, page.Y);
                double rr = page.Width * 0.035;
                dc.DrawEllipse(Fill(Ink, 0.55), null, new Point(c.X, c.Y + page.Height * 0.05), rr * 0.55, rr * 0.55);
                dc.DrawRoundedRectangle(Lit(Mix(Paper, Accent, 0.3), 0.4, 0.4), null, new Rect(c.X - rr * 0.35, c.Y - rr * 1.3, rr * 0.7, rr * 2.4), rr * 0.35, rr * 0.35);
            }
            // A small pip per day name above the tiles, and the empty days, raised.
            for (int i = 0; i < Days; i++)
            {
                var tile = TileRect(i, tw, th, pad, gap, top, page.Width);
                Plate(dc, tile, tw * 0.16, Lit(Mix(Paper, Accent, i == Rest ? 0.35 : 0.08), 0.3, 0.18), tw * 0.08);
                if (i == Rest)
                    dc.DrawEllipse(Fill(Mix(Accent, Ink, 0.4), 0.35), null, new Point(tile.X + tile.Width / 2, tile.Y + tile.Height * 0.55), tw * 0.09, tw * 0.09);
            }
            // The progress groove under the week.
            double barW = page.Width - pad * 2, barY = top + th + page.Height * 0.1, barH = page.Height * 0.07;
            Plate(dc, new Rect(-page.Width / 2 + pad, barY, barW, barH), barH / 2, Fill(Mix(Accent, Ink, 0.5), 0.55), 0, pressed: true);
        }
    }

    /// <summary>
    /// Tip "Deeper times effects to a video": a video plays on a screen with a timeline under it.
    /// The playhead walks along; where it crosses a pin, the effect lands on the screen right then:
    /// a picture flash bursts in, a word plate pops up, a second flash, then the clip rewinds.
    /// </summary>
    public sealed class DeeperTipArt : TipSceneABase
    {
        private const double Loop = 5.2, Run = 4.7;
        private static readonly double[] Pins = { 0.2, 0.52, 0.8 };
        private Geometry? _clip;
        private Rect _clipRect;

        protected override void Paint(DrawingContext dc, double w, double h, double t)
        {
            CachedGround(dc, w, h, Deep(Accent));
            Motes(dc, w, h, t, Accent, 12, 111, new Rect(0.5, 0.08, 0.48, 0.86), 0.05, 0.006, 0.4);

            double S = Unit(w, h), cx = w * 0.72, cy = h * 0.42;
            double sw = S * 0.94, sh = sw * 0.54, bz = sw * 0.035;
            double kIn = BackOut(t / 0.8);
            double oy = (1 - kIn) * h * 0.45;
            var screen = new Rect(-sw / 2 + bz, -sh / 2 + bz, sw - bz * 2, sh - bz * 2);
            if (_clip == null || _clipRect != screen) { _clip = new RectangleGeometry(screen, bz, bz); _clip.Freeze(); _clipRect = screen; }

            double u = t % Loop;
            bool running = u < Run;
            double head = running ? u / Run : 1 - EaseOut((u - Run) / (Loop - Run));
            double trackY = sh / 2 + S * 0.12, trackL = -sw / 2 + sw * 0.04, trackW = sw * 0.92;

            dc.PushOpacity(Clamp01(kIn * 1.4));
            dc.PushTransform(new TranslateTransform(cx, cy + oy));
            dc.DrawDrawing(Layer("frame", w, h, d => PaintFrame(d, sw, sh, bz, screen, trackY, trackL, trackW, S)));

            // The video: hills drifting under a slow sun, clipped to the glass.
            dc.PushClip(_clip);
            double drift = (t * screen.Width * 0.05) % screen.Width;
            var sun = new Point(screen.X + screen.Width * (0.7 - 0.05 * Math.Sin(t * 0.3)), screen.Y + screen.Height * (0.36 + 0.03 * Math.Sin(t * 0.5)));
            dc.DrawEllipse(Fill(Mix(Pink, Colors.White, 0.55), 0.85), null, sun, screen.Height * 0.13, screen.Height * 0.13);
            var hills = Layer("hills", w, h, d => PaintHills(d, screen));
            dc.PushTransform(new TranslateTransform(-drift, 0));
            dc.DrawDrawing(hills);
            dc.Pop();
            dc.PushTransform(new TranslateTransform(screen.Width - drift, 0));
            dc.DrawDrawing(hills);
            dc.Pop();

            // The effects, each landing the moment the playhead reaches its pin.
            PaintFlash(dc, screen, running ? (head - Pins[0]) * Run : -1, 0, S);
            PaintWord(dc, screen, running ? (head - Pins[1]) * Run : -1, S);
            PaintFlash(dc, screen, running ? (head - Pins[2]) * Run : -1, 1, S);
            dc.Pop();
            Sheen(dc, _clip, screen, t, 6.1, 3.4, 0.4);

            // Pins on the track pop as the head passes; the head itself, and its line up the glass.
            for (int i = 0; i < Pins.Length; i++)
            {
                double since = running ? (head - Pins[i]) * Run : 9;
                bool passed = since >= 0;
                double pop = 1 + 0.45 * Squash(since, 0.4) + (passed ? 0.12 : 0);
                var pc = new Point(trackL + trackW * Pins[i], trackY - S * 0.05);
                var hue = i == 1 ? Pink : Mix(Paper, Accent, 0.25);
                Shape(dc, GemUnit, new Point(pc.X + S * 0.004, pc.Y + S * 0.007), S * 0.034 * pop, Fill(Ink, 0.35));
                Shape(dc, GemUnit, pc, S * 0.034 * pop, passed ? Lit(Mix(hue, Colors.White, 0.25), 0.4, 0.1) : Fill(Mix(hue, Ink, 0.45)));
            }
            double hx = trackL + trackW * head;
            var fillBar = new Rect(trackL, trackY - S * 0.008, Math.Max(0, hx - trackL), S * 0.016);
            dc.DrawRoundedRectangle(Fill(Pink, 0.85), null, fillBar, fillBar.Height / 2, fillBar.Height / 2);
            dc.DrawRectangle(Fill(Colors.White, 0.3), null, new Rect(hx - 0.6, screen.Bottom + bz, 1.2, trackY - screen.Bottom - bz));
            double kr = S * 0.026;
            dc.DrawEllipse(Fill(Ink, 0.35), null, new Point(hx + kr * 0.2, trackY + kr * 0.35), kr, kr);
            dc.DrawEllipse(Lit(Colors.White, 0, 0.25), null, new Point(hx, trackY), kr, kr);
            dc.DrawEllipse(null, Bevel(Math.Max(1, kr * 0.15)), new Point(hx, trackY), kr * 0.92, kr * 0.92);
            dc.Pop();
            dc.Pop();

            // Glints from the pins as they fire.
            for (int i = 0; i < Pins.Length; i++)
            {
                double since = running ? (head - Pins[i]) * Run : -1;
                Burst(dc, new Point(cx + trackL + trackW * Pins[i], cy + oy + trackY - S * 0.05), S * 0.09, S * 0.022, since / 0.4, 6, 121 + i, Mix(Pink, Colors.White, 0.3));
            }
            Twinkles(dc, w, h, t, Mix(Accent, Colors.White, 0.35), 4, 43, new Rect(0.52, 0.08, 0.44, 0.82), 0.028);
            Motes(dc, w, h, t, Colors.White, 5, 49, new Rect(0.55, 0.2, 0.42, 0.8), 0.1, 0.008, 0.3);
        }

        /// <summary>A picture flash: the glass whites out, a framed picture snaps in tilted, glints, fades.</summary>
        private void PaintFlash(DrawingContext dc, Rect screen, double since, int which, double S)
        {
            if (since < 0 || since > 1.0) return;
            dc.DrawRectangle(Fill(Mix(Pink, Colors.White, 0.7), 0.7 * Math.Exp(-since * 9)), null, screen);
            double k = BackOut(since / 0.25, 2.2), a = since < 0.75 ? 1 : 1 - (since - 0.75) / 0.25;
            double pw = screen.Width * 0.36, ph = pw * 0.78;
            var c = new Point(screen.X + screen.Width * (which == 0 ? 0.36 : 0.64), screen.Y + screen.Height * 0.48);
            var pose = Pose(c.X, c.Y, which == 0 ? -7 : 6, k, k);
            var rect = new Rect(-pw / 2, -ph / 2, pw, ph);
            dc.PushOpacity(Clamp01(a));
            DropShadow(dc, pose, rect, pw * 0.05, S * 0.02);
            dc.PushTransform(new MatrixTransform(pose));
            dc.DrawRoundedRectangle(Fill(Paper), null, rect, pw * 0.05, pw * 0.05);
            var pic = new Rect(rect.X + pw * 0.07, rect.Y + pw * 0.07, pw * 0.86, ph - pw * 0.14);
            dc.DrawRectangle(Lit(which == 0 ? Pink : Accent, 0.3, 0.35), null, pic);
            Shape(dc, which == 0 ? StarUnit : MoonUnit, new Point(pic.X + pic.Width / 2, pic.Y + pic.Height / 2), pic.Height * 0.3, Fill(Colors.White, 0.9), null, which == 0 ? 0 : -20);
            dc.Pop();
            dc.Pop();
            Burst(dc, c, pw * 0.8, S * 0.035, since / 0.55, 8, 131 + which, Colors.White);
        }

        /// <summary>A word lands: a pill plate pops up from the bottom of the glass, holds, then sinks.</summary>
        private void PaintWord(DrawingContext dc, Rect screen, double since, double S)
        {
            if (since < 0 || since > 1.3) return;
            double k = BackOut(since / 0.3, 2.4), a = since < 1.0 ? 1 : 1 - (since - 1.0) / 0.3;
            double pw = screen.Width * 0.5, ph = screen.Height * 0.22;
            var c = new Point(screen.X + screen.Width / 2, screen.Y + screen.Height * 0.5);
            var pose = Pose(c.X, c.Y + (1 - k) * ph, -3, 0.6 + 0.4 * k, 0.6 + 0.4 * k);
            var rect = new Rect(-pw / 2, -ph / 2, pw, ph);
            dc.PushOpacity(Clamp01(a * Math.Min(1, k * 1.5)));
            DropShadow(dc, pose, rect, ph / 2, S * 0.018);
            dc.PushTransform(new MatrixTransform(pose));
            Plate(dc, rect, ph / 2, Lit(Pink, 0.3, 0.25), 0);
            Word(dc, new Rect(-pw * 0.34, -ph * 0.13, pw * 0.38, ph * 0.26), Colors.White, 0.95);
            Word(dc, new Rect(pw * 0.08, -ph * 0.13, pw * 0.26, ph * 0.26), Colors.White, 0.95);
            dc.Pop();
            dc.Pop();
        }

        private void PaintHills(DrawingContext dc, Rect screen)
        {
            // Two bands of soft hills, the far one lighter; drawn twice side by side so it wraps.
            for (int layer = 0; layer < 2; layer++)
            {
                var g = new StreamGeometry();
                double baseY = screen.Y + screen.Height * (layer == 0 ? 0.64 : 0.8);
                double amp = screen.Height * (layer == 0 ? 0.1 : 0.07);
                using (var ctx = g.Open())
                {
                    ctx.BeginFigure(new Point(screen.X, screen.Bottom + 2), true, true);
                    for (int i = 0; i <= 32; i++)
                    {
                        double x = i / 32.0;
                        double y = baseY - amp * (Math.Sin(x * Math.PI * 2 * (layer + 1) + layer) * 0.6 + Math.Sin(x * Math.PI * 4 + 1.3) * 0.4);
                        ctx.LineTo(new Point(screen.X + x * screen.Width, y), true, true);
                    }
                    ctx.LineTo(new Point(screen.Right, screen.Bottom + 2), false, true);
                }
                g.Freeze();
                dc.DrawGeometry(Fill(Mix(Accent, layer == 0 ? Pink : Ink, layer == 0 ? 0.25 : 0.55), layer == 0 ? 0.55 : 0.95), null, g);
            }
        }

        private void PaintFrame(DrawingContext dc, double sw, double sh, double bz, Rect screen, double trackY, double trackL, double trackW, double S)
        {
            var body = new Rect(-sw / 2, -sh / 2, sw, sh);
            Plate(dc, body, bz * 1.6, Lit(Mix(Accent, Ink, 0.4), 0.3, 0.35), sh * 0.06);
            var sky = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
            sky.GradientStops.Add(new GradientStop(Mix(Accent, Ink, 0.35), 0));
            sky.GradientStops.Add(new GradientStop(Mix(Pink, Ink, 0.45), 0.7));
            sky.Freeze();
            Plate(dc, screen, bz, sky, 0, pressed: true);
            // The timeline: a groove with tick marks.
            var groove = new Rect(trackL - S * 0.012, trackY - S * 0.016, trackW + S * 0.024, S * 0.032);
            Plate(dc, groove, groove.Height / 2, Fill(Mix(Accent, Ink, 0.65)), 0, pressed: true);
            for (int i = 0; i <= 16; i++)
            {
                double x = trackL + trackW * i / 16;
                double len = i % 4 == 0 ? S * 0.03 : S * 0.016;
                dc.DrawLine(Stroke(Paper, Math.Max(0.75, S * 0.003), 0.3), new Point(x, trackY + S * 0.03), new Point(x, trackY + S * 0.03 + len));
            }
        }
    }

    /// <summary>
    /// Tip "Blink Trainer keeps score": a webcam watches a wide open eye, its beam lit on the iris.
    /// The eye holds, starts to quiver, then blinks: the camera's light flashes and one more tally
    /// mark lands on the score card with a pop. Then the eye opens and tries again.
    /// </summary>
    public sealed class BlinkTipArt : TipSceneABase
    {
        private const double Loop = 4.6, BlinkAt = 3.1, BlinkLen = 0.32;
        private Geometry? _eye;
        private double _eyeW;
        private Brush? _skin, _white, _iris;
        private Color _brushHue;

        protected override void Paint(DrawingContext dc, double w, double h, double t)
        {
            CachedGround(dc, w, h, Deep(Accent));
            Motes(dc, w, h, t, Accent, 12, 151, new Rect(0.5, 0.08, 0.48, 0.86), 0.05, 0.006, 0.4);

            double S = Unit(w, h);
            double u = t % Loop;
            int cycle = (int)Math.Floor(t / Loop);
            double kIn = BackOut(t / 0.8);
            double oy = (1 - kIn) * h * 0.45;

            var eye = new Point(w * 0.73, h * 0.5 + oy + Bob(t, S * 0.006));
            double ea = S * 0.3, eb = S * 0.16;
            var cam = new Point(w * 0.575, h * 0.19 + oy * 0.7 + Bob(t, S * 0.01, 1.3));
            var card = new Point(w * 0.875, h * 0.8 + oy);

            // How shut the eye is: open, a strained quiver before the blink, the blink itself.
            double strain = Clamp01((u - 1.6) / (BlinkAt - 1.6));
            double quiver = u < BlinkAt ? strain * strain * 0.08 * (0.5 + 0.5 * Math.Sin(t * 41)) : 0;
            double blink = Hump(u, BlinkAt, BlinkAt + BlinkLen);
            double shut = Math.Max(quiver, blink);
            double since = u - (BlinkAt + BlinkLen * 0.5);

            dc.PushOpacity(Clamp01(kIn * 1.4));

            // The beam from the lens to the iris, behind the eye.
            var lens = Pose(cam.X, cam.Y, 8).Transform(new Point(S * 0.27 * 0.12, 0));
            var look = new Point(eye.X + Math.Sin(t * 0.8) * ea * 0.12, eye.Y + Math.Sin(t * 0.55) * eb * 0.15);
            double beamA = 0.12 + 0.04 * Math.Sin(t * 3) + (since >= 0 && since < 0.4 ? 0.22 * (1 - since / 0.4) : 0);
            var beam = new StreamGeometry();
            using (var ctx = beam.Open())
            {
                ctx.BeginFigure(lens, true, true);
                ctx.LineTo(new Point(look.X - eb * 0.5, look.Y - eb * 0.6), false, false);
                ctx.LineTo(new Point(look.X + eb * 0.5, look.Y + eb * 0.6), false, false);
            }
            beam.Freeze();
            dc.DrawGeometry(Fill(Pink, beamA), null, beam);

            PaintEye(dc, eye, ea, eb, look, shut, t, S);
            PaintCamera(dc, w, h, cam, since, S, t);
            PaintScore(dc, card, since, cycle, S);
            dc.Pop();

            // A little puff off the lashes on the blink.
            Burst(dc, eye, ea * 1.05, S * 0.03, since / 0.5, 8, 161 + cycle, Mix(Pink, Colors.White, 0.4));
            Twinkles(dc, w, h, t, Mix(Accent, Colors.White, 0.35), 4, 53, new Rect(0.52, 0.08, 0.44, 0.82), 0.028);
            Motes(dc, w, h, t, Colors.White, 5, 59, new Rect(0.55, 0.2, 0.42, 0.8), 0.1, 0.008, 0.3);
        }

        private void PaintEye(DrawingContext dc, Point c, double a, double b, Point look, double shut, double t, double S)
        {
            if (_eye == null || _eyeW != a)
            {
                var g = new StreamGeometry();
                using (var ctx = g.Open())
                {
                    ctx.BeginFigure(new Point(-a, 0), true, true);
                    ctx.QuadraticBezierTo(new Point(0, -b * 2), new Point(a, 0), true, true);
                    ctx.QuadraticBezierTo(new Point(0, b * 1.7), new Point(-a, 0), true, true);
                }
                g.Freeze();
                _eye = g;
                _eyeW = a;
            }
            Refresh();
            // A blink folds the whole eye down onto its lower lid, so shut reads as a thin smile line.
            double open = Math.Max(0.07, 1 - shut);
            double pivot = b * 0.7;
            var fold = new Matrix(1, 0, 0, open, c.X, c.Y + pivot * (1 - open));

            // The socket: a soft raised rim of skin in the hue, folding a little less than the eye.
            double so = open;
            var socket = new Matrix(1.1, 0, 0, 1.2 * so, c.X, c.Y + pivot * (1 - so));
            var sh = socket; sh.Translate(S * 0.012, S * 0.018);
            dc.PushTransform(new MatrixTransform(sh));
            dc.DrawGeometry(Fill(Color.FromRgb(4, 4, 12), 0.3), null, _eye);
            dc.Pop();
            dc.PushTransform(new MatrixTransform(socket));
            dc.DrawGeometry(_skin, null, _eye);
            dc.DrawGeometry(null, Bevel(Math.Max(1, S * 0.006)), _eye);
            dc.Pop();

            dc.PushTransform(new MatrixTransform(fold));
            dc.DrawGeometry(_white, null, _eye);
            // Iris and pupil, following the camera's beam a little, clipped to the eye.
            dc.PushClip(_eye);
            var ip = new Point(look.X - c.X, look.Y - c.Y);
            double ir = b * 0.95;
            dc.DrawEllipse(_iris, null, ip, ir, ir);
            dc.DrawEllipse(null, Stroke(Mix(Accent, Ink, 0.55), Math.Max(1, ir * 0.08)), ip, ir * 0.96, ir * 0.96);
            double pr = ir * (0.42 + 0.04 * Math.Sin(t * 1.7));
            dc.DrawEllipse(Fill(Ink), null, ip, pr, pr);
            dc.DrawEllipse(Fill(Colors.White, 0.9), null, new Point(ip.X - ir * 0.32, ip.Y - ir * 0.36), ir * 0.2, ir * 0.15);
            dc.DrawEllipse(Fill(Colors.White, 0.5), null, new Point(ip.X + ir * 0.3, ip.Y + ir * 0.3), ir * 0.08, ir * 0.08);
            // A lid shadow along the top, deeper as it strains.
            dc.DrawEllipse(Fill(Mix(Accent, Ink, 0.5), 0.18 + shut * 0.5), null, new Point(0, -b * 1.15), a * 0.9, b * 0.55);
            dc.Pop();
            dc.Pop();

            // The upper lid line and its lashes, in card space so the strokes never squash.
            Point Lid(double x)
            {
                double k = (x + a) / (2 * a), y = 2 * (1 - k) * k * (-b * 2);
                return fold.Transform(new Point(x, y));
            }
            var line = new StreamGeometry();
            using (var ctx = line.Open())
            {
                ctx.BeginFigure(Lid(-a), false, false);
                for (int i = 1; i <= 16; i++) ctx.LineTo(Lid(-a + i * a / 8), true, true);
            }
            line.Freeze();
            var lash = Stroke(Mix(Accent, Ink, 0.75), Math.Max(1.4, S * 0.013));
            dc.DrawGeometry(null, lash, line);
            bool closed = open < 0.4;
            for (int i = 0; i < 7; i++)
            {
                // Shut, only the outer lashes show, short and slanting down and out.
                if (closed && i > 1 && i < 5) continue;
                double x = -a * 0.72 + i * a * 0.24;
                var p = Lid(x);
                double side = x / a;
                double ang = closed ? Math.PI / 2 - Math.Sign(side) * 0.9 : -Math.PI / 2 + side * 0.85;
                double len = b * (0.46 - Math.Abs(side) * 0.15) * (closed ? 0.6 : 1);
                dc.DrawLine(lash, p, new Point(p.X + Math.Cos(ang) * len, p.Y + Math.Sin(ang) * len));
            }
        }

        private void PaintCamera(DrawingContext dc, double w, double h, Point c, double since, double S, double t)
        {
            double bw = S * 0.27, bh = S * 0.17;
            var pose = Pose(c.X, c.Y, 8 + Math.Sin(t * 0.7) * 2);
            var body = new Rect(-bw / 2, -bh / 2, bw, bh);
            DropShadow(dc, pose, body, bh * 0.3, S * 0.02);
            dc.PushTransform(new MatrixTransform(pose));
            dc.DrawDrawing(Layer("cam", w, h, d =>
            {
                // A clip on the back, the body, the lens ring and the glass.
                Plate(d, new Rect(-bw * 0.18, bh * 0.38, bw * 0.36, bh * 0.36), bh * 0.08, Lit(Mix(Accent, Ink, 0.5), 0.2, 0.3), 0);
                Plate(d, body, bh * 0.3, Lit(Mix(Paper, Accent, 0.25), 0.3, 0.3), 0);
                var lc = new Point(bw * 0.12, 0);
                d.DrawEllipse(Lit(Mix(Accent, Ink, 0.55), 0.15, 0.3), null, lc, bh * 0.4, bh * 0.4);
                d.DrawEllipse(null, Bevel(Math.Max(1, bh * 0.05), pressed: true), lc, bh * 0.38, bh * 0.38);
                d.DrawEllipse(Fill(Ink), null, lc, bh * 0.24, bh * 0.24);
                d.DrawEllipse(Fill(Accent, 0.6), null, lc, bh * 0.13, bh * 0.13);
                d.DrawEllipse(Fill(Colors.White, 0.75), null, new Point(lc.X - bh * 0.09, lc.Y - bh * 0.1), bh * 0.06, bh * 0.05);
            }));
            // The record light: steady, then a bright flash on the blink.
            var led = new Point(-bw * 0.3, -bh * 0.18);
            double flash = since >= 0 && since < 0.5 ? 1 - since / 0.5 : 0;
            double on = 0.55 + 0.25 * Math.Sin(t * 4) + flash * 0.45;
            if (flash > 0) Glow(dc, led, bh * (0.25 + flash * 0.35), Pink, 0.6 * flash);
            dc.DrawEllipse(Fill(Mix(Pink, Colors.White, flash * 0.6), Clamp01(on)), null, led, bh * 0.07, bh * 0.07);
            dc.Pop();
        }

        private void PaintScore(DrawingContext dc, Point c, double since, int cycle, double S)
        {
            double cw = S * 0.34, ch = S * 0.2;
            var rect = new Rect(-cw / 2, -ch / 2, cw, ch);
            double bump = since >= 0 ? Squash(since - 0.08, 0.4) * 0.08 : 0;
            var pose = Pose(c.X, c.Y, -5, 1 + bump, 1 - bump);
            DropShadow(dc, pose, rect, ch * 0.15, S * 0.02);
            dc.PushTransform(new MatrixTransform(pose));
            Plate(dc, rect, ch * 0.15, Lit(Mix(Paper, Accent, 0.1), 0.35, 0.18), 0);
            // A strip of the hue down the left, like a score card.
            dc.PushClip(new RectangleGeometry(rect, ch * 0.15, ch * 0.15));
            dc.DrawRectangle(Lit(Accent, 0.2, 0.3), null, new Rect(rect.X, rect.Y, cw * 0.12, ch));
            dc.Pop();

            // Tally marks: four strokes and a slash make five; one more lands on every blink.
            int before = 3 + ((cycle % 3) + 3) % 3;
            bool landed = since >= 0.08;
            int marks = landed ? before + 1 : before;
            double k = landed ? EaseOut((since - 0.08) / 0.18) : 1;
            var ink = Mix(Accent, Ink, 0.7);
            double x0 = -cw * 0.24, gap = cw * 0.09, top = -ch * 0.28, bot = ch * 0.28;
            for (int i = 0; i < marks; i++)
            {
                bool newest = landed && i == marks - 1;
                double kk = newest ? k : 1;
                var pen = Stroke(newest ? Pink : ink, Math.Max(1.2, S * 0.012));
                if (i != 4)
                {
                    double x = i == 5 ? x0 + gap * 5.2 : x0 + gap * i;
                    double tilt = (i % 2 == 0 ? 1 : -1) * cw * 0.008;
                    dc.DrawLine(pen, new Point(x - tilt, top), new Point(x - tilt + tilt * 2 * kk, top + (bot - top) * kk));
                }
                else
                {
                    var a = new Point(x0 - gap * 0.5, bot - ch * 0.05);
                    var b = new Point(x0 + gap * 3.5, top + ch * 0.05);
                    dc.DrawLine(pen, a, new Point(a.X + (b.X - a.X) * kk, a.Y + (b.Y - a.Y) * kk));
                }
            }
            dc.Pop();
            if (landed)
            {
                double mx = marks - 1 == 4 ? x0 + gap * 1.5 : marks - 1 == 5 ? x0 + gap * 5.2 : x0 + gap * (marks - 1);
                Burst(dc, pose.Transform(new Point(mx, 0)), ch * 0.9, S * 0.025, (since - 0.08) / 0.45, 6, 171 + cycle, Mix(Pink, Colors.White, 0.3));
            }
        }

        private void Refresh()
        {
            if (_skin != null && _brushHue == Accent) return;
            _brushHue = Accent;
            _skin = Lit(Mix(Accent, Pink, 0.35), 0.25, 0.4);
            var w = new RadialGradientBrush(Colors.White, Mix(Paper, Accent, 0.3)) { GradientOrigin = new Point(0.4, 0.35), RadiusX = 0.65, RadiusY = 0.8 };
            w.Freeze();
            _white = w;
            var i = new RadialGradientBrush(Mix(Pink, Colors.White, 0.25), Mix(Accent, Ink, 0.25)) { GradientOrigin = new Point(0.4, 0.4), RadiusX = 0.6, RadiusY = 0.6 };
            i.Freeze();
            _iris = i;
        }
    }
}
