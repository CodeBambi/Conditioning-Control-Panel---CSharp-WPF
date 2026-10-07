using System;
using System.Windows;
using System.Windows.Media;

namespace ConditioningControlPanel.Controls.Billboard
{
    /// <summary>
    /// Shared frame for the house scenes in this file: the art lives in a 100 x 80 unit box on the
    /// right half of the card (the card text owns the left), sized to whichever of width or height
    /// runs out first, so the same scene reads at 600 x 340 and at 1000 x 420.
    /// </summary>
    public abstract class HouseSceneArtA : BillboardVectorArt
    {
        protected static readonly Color Pink = Color.FromRgb(0xff, 0x4f, 0xa8);
        protected static readonly Color Cyan = Color.FromRgb(0x5f, 0xe3, 0xff);
        protected static readonly Color Lilac = Color.FromRgb(0x9b, 0x7b, 0xff);
        protected static readonly Color Gold = Color.FromRgb(0xff, 0xc9, 0x4a);
        protected static readonly Color Mint = Color.FromRgb(0x3c, 0xff, 0xb4);
        protected static readonly Color Ink = Color.FromRgb(0x10, 0x0e, 0x22);
        protected static readonly Color Slate = Color.FromRgb(0x2b, 0x28, 0x52);

        /// <summary>The size of the frame being painted (layers key on it).</summary>
        protected double W, H;

        protected readonly struct Box
        {
            public Box(double x, double y, double u) { X = x; Y = y; U = u; }
            public double X { get; }
            public double Y { get; }
            public double U { get; }
            public Point P(double x, double y) => new(X + x * U, Y + y * U);
            public Rect R(double x, double y, double w, double h) => new(X + x * U, Y + y * U, w * U, h * U);
        }

        protected Box Fit(double w, double h)
        {
            W = w;
            H = h;
            return new(w * 0.735, h * 0.5, Math.Min(w * 0.46 / 100, h * 0.86 / 80));
        }

        /// <summary>A static layer for this frame size (see <see cref="BillboardVectorArt.Layer"/>).</summary>
        protected Drawing Lay(string key, Action<DrawingContext> paint) => Layer(key, W, H, paint);

        protected static double Lerp(double a, double b, double k) => a + (b - a) * k;

        protected static double Clamp01(double v) => Math.Clamp(v, 0, 1);

        /// <summary>A bump 0 -> 1 -> 0 over <paramref name="len"/> seconds from <paramref name="start"/>.</summary>
        protected static double Pulse(double t, double start, double len)
        {
            double k = (t - start) / len;
            return k <= 0 || k >= 1 ? 0 : Math.Sin(k * Math.PI);
        }

        protected static Matrix Pose(double x, double y, double degrees, double sx = 1, double sy = 1)
        {
            var m = Matrix.Identity;
            m.Scale(sx, sy);
            m.Rotate(degrees);
            m.Translate(x, y);
            return m;
        }

        protected static Point Cubic(Point a, Point c1, Point c2, Point b, double s)
        {
            double m = 1 - s;
            return new Point(
                m * m * m * a.X + 3 * m * m * s * c1.X + 3 * m * s * s * c2.X + s * s * s * b.X,
                m * m * m * a.Y + 3 * m * m * s * c1.Y + 3 * m * s * s * c2.Y + s * s * s * b.Y);
        }

        /// <summary>An expanding ring that fades as it grows (a click, a landing).</summary>
        protected void Ripple(DrawingContext dc, Point c, double r0, double r1, double k, Color hue, double thick)
        {
            if (k <= 0 || k >= 1) return;
            double r = Lerp(r0, r1, EaseOut(k));
            dc.DrawEllipse(null, Stroke(hue, thick * (1 - k * 0.6), 0.85 * (1 - k)), c, r, r);
        }

        /// <summary>Glints flung outward from a point, fading as they fly.</summary>
        protected void Burst(DrawingContext dc, Point c, double reach, double size, double k, int seed, int count, Color hue)
        {
            if (!Particles || k <= 0 || k >= 1) return;
            for (int i = 0; i < count; i++)
            {
                double a = i * Math.PI * 2 / count + Hash(seed, i, 1) * 0.6;
                double d = reach * (0.55 + 0.45 * Hash(seed, i, 2)) * EaseOut(k);
                var p = new Point(c.X + Math.Cos(a) * d, c.Y + Math.Sin(a) * d * 0.8);
                Spark(dc, p, size * (1 - k) * (0.7 + 0.5 * Hash(seed, i, 3)), i % 3 == 0 ? Colors.White : hue, 1 - k, k * 3 + i);
            }
        }

        /// <summary>3x5 pixel glyph, bit 14 = top left; drawn as tiny lit squares.</summary>
        protected static void PixelGlyph(DrawingContext dc, int bits, double x, double y, double cell, Brush fill)
        {
            for (int row = 0; row < 5; row++)
                for (int col = 0; col < 3; col++)
                    if (((bits >> (14 - (row * 3 + col))) & 1) != 0)
                        dc.DrawRectangle(fill, null, new Rect(x + col * cell, y + row * cell, cell * 0.86, cell * 0.86));
        }

        /// <summary>A plate for a cached layer (shadow, face, lamp bevel).</summary>
        protected void PlateStatic(DrawingContext d, Rect r, double radius, Brush fill, double lift)
        {
            var ink = Color.FromRgb(4, 4, 12);
            d.DrawRoundedRectangle(Solid(ink, 0.22), null, new Rect(r.X + lift * 0.9, r.Y + lift * 1.4, r.Width + lift * 0.4, r.Height + lift * 0.4), radius + lift * 0.4, radius + lift * 0.4);
            d.DrawRoundedRectangle(Solid(ink, 0.32), null, new Rect(r.X + lift * 0.45, r.Y + lift * 0.7, r.Width, r.Height), radius, radius);
            d.DrawRoundedRectangle(fill, null, r, radius, radius);
            double th = Math.Clamp(Math.Min(r.Width, r.Height) * 0.03, 1, 2.6);
            d.DrawRoundedRectangle(null, Bevel(th), new Rect(r.X + th / 2, r.Y + th / 2, r.Width - th, r.Height - th), radius, radius);
        }

        protected static Brush Linear(Color a, Color b, Point start, Point end)
        {
            var g = new LinearGradientBrush(a, b, start, end);
            g.Freeze();
            return g;
        }

        protected static Geometry Frozen(Geometry g)
        {
            g.Freeze();
            return g;
        }
    }

    /// <summary>
    /// Discord: a chat feed on a little stage. Messages arrive one after another (typing dots, then
    /// the bubble pops out of its avatar's corner), the feed scrolls up, a reply gets a mint check,
    /// a heart reaction pops under a post, and every fourth post is a contest: a gold trophy that
    /// lands with confetti while the crowd peeking over the rail hops and cheers. No brand mark.
    /// </summary>
    public sealed class DiscordHouseArt : HouseSceneArtA
    {
        private const double Period = 1.5, TypingFor = 0.5, PopFor = 0.32, Gap = 3.2, FeedBottom = 13.5;
        private static readonly double[] CrowdX = { -36, -18, 0, 18, 36 };
        private static readonly Color[] ConfettiHues = { Pink, Cyan, Gold, Mint, Lilac };

        // Message kinds, one per beat: talk (two lines), reply (check), heart (reaction), trophy (contest).
        private enum Kind { Talk, Reply, Heart, Trophy }

        private static Kind KindOf(int n) => (Kind)((((n + 2) % 4) + 4) % 4);
        private static double ArriveAt(int n) => n * Period - 0.6;
        private static bool RightSide(Kind k) => k == Kind.Reply;
        private static int SenderOf(int n) => (((n * 2) % 5) + 5) % 5;

        private static double Height(Kind k) => k switch { Kind.Talk => 15, Kind.Trophy => 22, _ => 10 };
        private static double Width(Kind k) => k switch { Kind.Talk => 44, Kind.Reply => 32, Kind.Heart => 34, _ => 30 };
        private static double Extra(Kind k) => k == Kind.Heart ? 5 : 0;

        private static readonly Geometry TrophyShape = Frozen(MakeTrophy());
        private static readonly Geometry HeartShape = Frozen(MakeHeart());
        private static readonly Brush TrophyBrush = MakeTrophyBrush();

        private readonly Brush?[] _faces = new Brush?[5];
        private Color _facesFor;

        private Color CrowdColor(int i) => i switch
        {
            0 => Pink,
            1 => Mix(Accent, Colors.White, 0.25),
            2 => Cyan,
            3 => Gold,
            _ => Lilac,
        };

        private Brush Face(int i)
        {
            if (_facesFor != Accent) { Array.Clear(_faces); _facesFor = Accent; }
            if (_faces[i] is { } f) return f;
            var hue = CrowdColor(i);
            var g = new RadialGradientBrush(Mix(hue, Colors.White, 0.35), Mix(hue, Ink, 0.14))
            { GradientOrigin = new Point(0.32, 0.28), Center = new Point(0.4, 0.38), RadiusX = 0.75, RadiusY = 0.75 };
            g.Freeze();
            return _faces[i] = g;
        }

        protected override void Paint(DrawingContext dc, double w, double h, double t)
        {
            CachedGround(dc, w, h, Deep(Accent));
            var b = Fit(w, h);
            double u = b.U;
            Motes(dc, w, h, t, Accent, 12, 41, new Rect(0.5, 0.06, 0.48, 0.8), 0.05, 0.005, 0.4);

            double enter = BackOut(t / 0.8, 1.2);
            int latest = (int)Math.Floor((t + 0.6) / Period);

            // Feed: newest at the bottom, older ones pushed up by the newest one's slot as it opens.
            dc.PushOpacity(Clamp01(t / 0.45));
            double bottom = FeedBottom + (1 - enter) * 14;
            Point? trophyAt = null;
            double trophyRevealAt = double.NaN;
            for (int m = latest; m >= latest - 5; m--)
            {
                var kind = KindOf(m);
                double e = t - ArriveAt(m);
                double reveal = (e - TypingFor) / PopFor;
                double full = Height(kind) + Extra(kind);
                double slotH, appear = 1;
                if (m == latest)
                {
                    appear = EaseOut(e / 0.28);
                    slotH = reveal <= 0 ? 9 : Lerp(9, full, EaseOut(reveal));
                }
                else slotH = full;
                double top = bottom - slotH * appear;
                double fade = Clamp01((top + 44) / 9);
                if (fade > 0.01)
                {
                    dc.PushOpacity(fade);
                    var at = DrawMessage(dc, b, m, kind, bottom, e, reveal, appear, t);
                    dc.Pop();
                    if (kind == Kind.Trophy && reveal > 0 && at.HasValue && double.IsNaN(trophyRevealAt))
                    {
                        trophyAt = at;
                        trophyRevealAt = ArriveAt(m) + TypingFor;
                    }
                }
                bottom = top - Gap * appear;
                if (bottom < -50) break;
            }
            dc.Pop();

            // The last contest's confetti and glints over the feed.
            double cheerAge = double.IsNaN(trophyRevealAt) ? 99 : t - trophyRevealAt;
            if (trophyAt.HasValue && cheerAge < 1.6)
            {
                Confetti(dc, trophyAt.Value, u, cheerAge);
                Burst(dc, trophyAt.Value, 20 * u, 3.2 * u, cheerAge / 0.7, 7, 9, Gold);
            }

            // Crowd over the rail: nothing of them shows below it.
            dc.PushClip(new RectangleGeometry(new Rect(0, 0, w, b.Y + (41.5 + (1 - enter) * 10) * u)));
            for (int i = 0; i < CrowdX.Length; i++)
                DrawFan(dc, b, i, t, latest, cheerAge);
            dc.Pop();
            DrawRail(dc, b, t, enter);

            Twinkles(dc, w, h, t, Mix(Accent, Colors.White, 0.4), 5, 43, new Rect(0.52, 0.08, 0.44, 0.55), 0.026);
            Motes(dc, w, h, t, Colors.White, 5, 47, new Rect(0.55, 0.2, 0.42, 0.75), 0.1, 0.008, 0.3);
        }

        /// <summary>Draws one message (typing dots or the bubble), returns the trophy centre if any.</summary>
        private Point? DrawMessage(DrawingContext dc, Box b, int n, Kind kind, double bottom, double e, double reveal, double appear, double t)
        {
            double u = b.U;
            bool right = RightSide(kind);
            double bodyBottom = bottom - Extra(kind) * (reveal > 0 ? EaseOut(reveal) : 0);
            var avatarHue = kind == Kind.Trophy ? Gold : CrowdColor(SenderOf(n));

            // Avatar beside the bubble's tail.
            var ac = b.P(right ? 44 : -44, bodyBottom - 4.2);
            double ar = 4 * u * BackOut(e / 0.35, 2);
            if (ar > 0.3)
            {
                Shadow(dc, new Rect(ac.X - ar, ac.Y - ar, ar * 2, ar * 2), ar, u * 0.6);
                dc.DrawEllipse(Fill(avatarHue), null, ac, ar, ar);
                dc.DrawEllipse(null, Bevel(Math.Max(1, ar * 0.16)), ac, ar * 0.92, ar * 0.92);
                if (kind == Kind.Trophy) Spark(dc, ac, ar * 0.7, Ink, 0.55);
            }

            double ax = right ? 38 : -38;
            if (reveal < 1)
            {
                // Typing: a small bubble with three dots taking turns; it shrinks away as the post pops.
                double k = appear * (reveal > 0 ? 1 - EaseOut(reveal * 1.6) : 1);
                if (k > 0.02)
                {
                    var r = right ? b.R(ax - 18, bottom - 9, 18, 9) : b.R(ax, bottom - 9, 18, 9);
                    var origin = new Point(right ? r.Right : r.Left, r.Bottom);
                    dc.PushTransform(new ScaleTransform(k, k, origin.X, origin.Y));
                    Plate(dc, r, 4.5 * u, Fill(right ? Mix(Accent, Ink, 0.25) : Slate), u * 1.1);
                    for (int i = 0; i < 3; i++)
                    {
                        double hop = Math.Max(0, Math.Sin(t * 7 - i * 0.9)) * 1.6 * u;
                        dc.DrawEllipse(Fill(Colors.White, 0.75), null, new Point(r.X + r.Width * (0.28 + i * 0.22), r.Y + r.Height * 0.55 - hop), u * 1.2, u * 1.2);
                    }
                    dc.Pop();
                }
            }
            if (reveal <= 0) return null;

            double s = BackOut(reveal, 2.1);
            double bw = Width(kind), bh = Height(kind);
            double ox = b.X + ax * u, oy = b.Y + bodyBottom * u;
            var layer = Lay("dc-bubble-" + (int)kind, d => PaintBubble(d, kind, u, right, Accent));
            dc.PushTransform(new MatrixTransform(Pose(ox, oy, 0, s, s)));
            dc.DrawDrawing(layer);
            // A glint across the post a moment after it lands, then every few seconds.
            var shape = new RectangleGeometry(new Rect(right ? -bw * u : 0, -bh * u, bw * u, bh * u), 5 * u, 5 * u);
            shape.Freeze();
            Sheen(dc, shape, shape.Rect, Math.Max(0, reveal * PopFor), 3.0, 2.85, 0.7);
            Point? trophy = null;
            if (kind == Kind.Trophy)
            {
                // The cup lands a beat after the bubble, with a squash.
                double since = reveal * PopFor;
                double cup = BackOut((since - 0.08) / 0.34, 2.4);
                if (cup > 0)
                {
                    double sq = 1 + 0.12 * Pulse(since, 0.3, 0.25);
                    var cc = new Point(bw * u * 0.5, -bh * u * 0.5);
                    double ts = 15 * u * cup;
                    dc.PushTransform(new MatrixTransform(Pose(cc.X, cc.Y + ts * 0.05, Math.Sin(t * 1.4) * 4, ts * sq, ts / sq)));
                    dc.DrawGeometry(TrophyBrush, null, TrophyShape);
                    dc.Pop();
                    Spark(dc, new Point(cc.X + 4.2 * u, cc.Y - 5 * u), 2.6 * u * (0.6 + 0.4 * Math.Sin(t * 3)), Colors.White, 0.85, t);
                }
                trophy = new Point(ox + bw * u * 0.5 * s, oy - bh * u * 0.5 * s);
            }
            dc.Pop();

            if (kind == Kind.Heart)
            {
                // The reaction pops under the post a moment later.
                double since = reveal * PopFor;
                double rk = BackOut((since - 0.45) / 0.3, 2.6);
                if (rk > 0)
                {
                    var chip = b.R(ax + 18, bodyBottom - 2.2, 13, 6.4);
                    var o = new Point(chip.X + chip.Width / 2, chip.Y + chip.Height / 2);
                    dc.PushTransform(new ScaleTransform(rk, rk, o.X, o.Y));
                    Plate(dc, chip, chip.Height / 2, Fill(Mix(Pink, Ink, 0.55)), u * 0.8);
                    double beat = 1 + 0.18 * Pulse(t % 1.1, 0, 0.22);
                    dc.PushTransform(new MatrixTransform(Pose(chip.X + chip.Height * 0.6, o.Y, 0, 2.3 * u * beat, 2.3 * u * beat)));
                    dc.DrawGeometry(Fill(Pink), null, HeartShape);
                    dc.Pop();
                    dc.DrawRoundedRectangle(Fill(Colors.White, 0.7), null, new Rect(chip.X + chip.Width * 0.56, o.Y - u * 0.8, chip.Width * 0.26, u * 1.6), u * 0.8, u * 0.8);
                    dc.Pop();
                    Burst(dc, o, 8 * u, 1.8 * u, (since - 0.45) / 0.5, 13 + n, 6, Pink);
                }
            }
            return trophy;
        }

        private void PaintBubble(DrawingContext d, Kind kind, double u, bool right, Color accent)
        {
            // Local space: the tail corner is 0,0 and the bubble rises above it.
            double bw = Width(kind) * u, bh = Height(kind) * u, rad = 5 * u;
            var rect = new Rect(right ? -bw : 0, -bh, bw, bh);
            var fill = kind switch
            {
                Kind.Reply => Mix(accent, Ink, 0.22),
                Kind.Trophy => Mix(Slate, Gold, 0.16),
                _ => Slate,
            };
            // The corner by the avatar is square (the tail), the rest rounded.
            var outline = Geometry.Combine(
                new RectangleGeometry(rect, rad, rad),
                new RectangleGeometry(right ? new Rect(-rad, -rad, rad, rad) : new Rect(0, -rad, rad, rad)),
                GeometryCombineMode.Union, null);
            outline.Freeze();
            var ink = Color.FromRgb(4, 4, 12);
            d.PushTransform(new TranslateTransform(u * 1.1, u * 1.7));
            d.DrawGeometry(Solid(ink, 0.22), null, outline);
            d.Pop();
            d.PushTransform(new TranslateTransform(u * 0.55, u * 0.85));
            d.DrawGeometry(Solid(ink, 0.32), null, outline);
            d.Pop();
            d.DrawGeometry(Linear(Mix(fill, Colors.White, 0.14), Mix(fill, Ink, 0.12), new Point(0, 0), new Point(0.4, 1)), null, outline);
            d.DrawGeometry(null, Bevel(Math.Max(1, u * 0.45)), outline);
            if (kind == Kind.Trophy)
            {
                d.DrawGeometry(null, Stroke(Gold, Math.Max(1, u * 0.35), 0.55), outline);
                return;
            }

            // Ink lines: words without words.
            double x0 = rect.X + 4 * u, lh = 2.2 * u;
            if (kind == Kind.Talk)
            {
                d.DrawRoundedRectangle(Solid(Colors.White, 0.55), null, new Rect(x0, rect.Y + 4 * u, bw - 8 * u, lh), lh / 2, lh / 2);
                d.DrawRoundedRectangle(Solid(Colors.White, 0.4), null, new Rect(x0, rect.Y + 9 * u, bw * 0.5, lh), lh / 2, lh / 2);
                d.DrawRoundedRectangle(Solid(Cyan, 0.8), null, new Rect(x0 + bw * 0.55, rect.Y + 9 * u, bw * 0.2, lh), lh / 2, lh / 2);
            }
            else if (kind == Kind.Reply)
            {
                d.DrawRoundedRectangle(Solid(Colors.White, 0.8), null, new Rect(x0, rect.Y + (bh - lh) / 2, bw - 15 * u, lh), lh / 2, lh / 2);
                var cc = new Point(rect.Right - 5.5 * u, rect.Y + bh / 2);
                d.DrawEllipse(Solid(Mint), null, cc, 3 * u, 3 * u);
                var check = new StreamGeometry();
                using (var c = check.Open())
                {
                    c.BeginFigure(new Point(cc.X - 1.4 * u, cc.Y + 0.1 * u), false, false);
                    c.LineTo(new Point(cc.X - 0.3 * u, cc.Y + 1.2 * u), true, true);
                    c.LineTo(new Point(cc.X + 1.6 * u, cc.Y - 1.1 * u), true, true);
                }
                check.Freeze();
                d.DrawGeometry(null, Stroke(Ink, Math.Max(1, u * 0.7)), check);
            }
            else
            {
                d.DrawRoundedRectangle(Solid(Colors.White, 0.55), null, new Rect(x0, rect.Y + (bh - lh) / 2, bw * 0.46, lh), lh / 2, lh / 2);
                d.DrawRoundedRectangle(Solid(Pink, 0.85), null, new Rect(x0 + bw * 0.51, rect.Y + (bh - lh) / 2, bw * 0.24, lh), lh / 2, lh / 2);
            }
        }

        private static Brush MakeTrophyBrush()
        {
            var g = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
            g.GradientStops.Add(new GradientStop(Color.FromRgb(0xff, 0xf1, 0xb8), 0));
            g.GradientStops.Add(new GradientStop(Gold, 0.42));
            g.GradientStops.Add(new GradientStop(Color.FromRgb(0xc8, 0x7a, 0x1c), 1));
            g.Freeze();
            return g;
        }

        /// <summary>A cup with two handles, a stem and a foot, about one unit tall, centred.</summary>
        private static Geometry MakeTrophy()
        {
            var g = new StreamGeometry { FillRule = FillRule.Nonzero };
            using (var c = g.Open())
            {
                c.BeginFigure(new Point(-0.32, -0.46), true, true);
                c.LineTo(new Point(0.32, -0.46), false, false);
                c.BezierTo(new Point(0.32, -0.08), new Point(0.18, 0.06), new Point(0.06, 0.1), false, false);
                c.LineTo(new Point(0.06, 0.24), false, false);
                c.LineTo(new Point(0.2, 0.32), false, false);
                c.LineTo(new Point(0.22, 0.44), false, false);
                c.LineTo(new Point(-0.22, 0.44), false, false);
                c.LineTo(new Point(-0.2, 0.32), false, false);
                c.LineTo(new Point(-0.06, 0.24), false, false);
                c.LineTo(new Point(-0.06, 0.1), false, false);
                c.BezierTo(new Point(-0.18, 0.06), new Point(-0.32, -0.08), new Point(-0.32, -0.46), false, false);
                foreach (var s in new[] { 1.0, -1.0 })
                {
                    c.BeginFigure(new Point(0.3 * s, -0.38), true, true);
                    c.BezierTo(new Point(0.52 * s, -0.4), new Point(0.52 * s, -0.1), new Point(0.24 * s, -0.08), false, false);
                    c.LineTo(new Point(0.26 * s, -0.15), false, false);
                    c.BezierTo(new Point(0.42 * s, -0.17), new Point(0.42 * s, -0.33), new Point(0.31 * s, -0.31), false, false);
                }
            }
            return g;
        }

        private static Geometry MakeHeart()
        {
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                c.BeginFigure(new Point(0, 0.8), true, true);
                c.BezierTo(new Point(-1.1, 0.05), new Point(-0.9, -0.95), new Point(0, -0.45), false, false);
                c.BezierTo(new Point(0.9, -0.95), new Point(1.1, 0.05), new Point(0, 0.8), false, false);
            }
            return g;
        }

        private void Confetti(DrawingContext dc, Point at, double u, double age)
        {
            if (!Particles || age < 0) return;
            for (int i = 0; i < 18; i++)
            {
                double vx = (Hash(61, i, 1) - 0.5) * 70 * u, vy = -(28 + Hash(61, i, 2) * 38) * u;
                double x = at.X + vx * age, y = at.Y + vy * age + 60 * u * age * age;
                double a = Clamp01(1.4 - age) * Clamp01(age / 0.08);
                if (a <= 0.02) continue;
                double spin = age * (6 + Hash(61, i, 3) * 8) + i;
                dc.PushTransform(new MatrixTransform(Pose(x, y, spin * 57.3, 1, Math.Max(0.15, Math.Abs(Math.Cos(spin * 1.3))))));
                dc.PushOpacity(a);
                dc.DrawRectangle(Fill(ConfettiHues[i % ConfettiHues.Length]), null, new Rect(-1.1 * u, -0.6 * u, 2.2 * u, 1.2 * u));
                dc.Pop();
                dc.Pop();
            }
        }

        /// <summary>One crowd member peeking over the rail: idle bob, a hop when they post, a cheer for a trophy.</summary>
        private void DrawFan(DrawingContext dc, Box b, int i, double t, int latest, double cheerAge)
        {
            double u = b.U;
            var hue = CrowdColor(i);
            double rise = BackOut((t - 0.12 * i) / 0.7, 1.4);
            double y = 28.5 + (i % 2 == 0 ? 0 : -1.4) + Bob(t, 0.6, i * 1.3) + (1 - rise) * 16;
            double hop = 0;
            double cheer = cheerAge - i * 0.07;
            if (cheer > 0 && cheer < 0.62) hop = Math.Sin(cheer / 0.62 * Math.PI) * 6.5;
            // The poster of a fresh message gives a small hop.
            for (int m = latest; m >= latest - 1; m--)
            {
                if (KindOf(m) == Kind.Trophy || SenderOf(m) != i) continue;
                double since = t - (ArriveAt(m) + TypingFor);
                if (since > 0 && since < 0.45) hop = Math.Max(hop, Math.Sin(since / 0.45 * Math.PI) * 2.8);
            }
            // A squash on landing.
            double land = cheer > 0.55 && cheer < 0.85 ? Math.Sin((cheer - 0.55) / 0.3 * Math.PI) : 0;
            double sx = 1 + 0.12 * land, sy = 1 - 0.12 * land;
            var c = b.P(CrowdX[i], y - hop);
            double r = 7 * u;
            dc.PushTransform(new ScaleTransform(sx, sy, c.X, c.Y + r));
            var body = new Rect(c.X - r * 1.25, c.Y + r * 0.72, r * 2.5, r * 2.2);
            dc.DrawRoundedRectangle(Fill(Mix(hue, Ink, 0.45)), null, body, r * 0.9, r * 0.9);
            dc.DrawEllipse(Fill(Ink, 0.3), null, new Point(c.X + r * 0.18, c.Y + r * 0.26), r, r);
            dc.DrawEllipse(Face(i), null, c, r, r);
            dc.DrawEllipse(null, Bevel(Math.Max(1, r * 0.1)), c, r * 0.94, r * 0.94);
            bool happy = cheer > 0 && cheer < 1.3;
            double look = Math.Sin(t * 0.7 + i) * 0.25 * r;
            if (happy)
            {
                var pen = Stroke(Ink, Math.Max(1, r * 0.13), 0.85);
                foreach (var ex in new[] { -0.36, 0.36 })
                {
                    var p = new Point(c.X + ex * r, c.Y - 0.05 * r);
                    dc.DrawLine(pen, new Point(p.X - r * 0.16, p.Y + r * 0.1), new Point(p.X, p.Y - r * 0.08));
                    dc.DrawLine(pen, new Point(p.X, p.Y - r * 0.08), new Point(p.X + r * 0.16, p.Y + r * 0.1));
                }
                dc.DrawEllipse(Fill(Pink, 0.45), null, new Point(c.X - r * 0.58, c.Y + r * 0.3), r * 0.17, r * 0.1);
                dc.DrawEllipse(Fill(Pink, 0.45), null, new Point(c.X + r * 0.58, c.Y + r * 0.3), r * 0.17, r * 0.1);
            }
            else
            {
                // A blink now and then.
                double blink = ((t + i * 1.7) % 4.3) < 0.12 ? 0.2 : 1;
                var eye = Fill(Ink, 0.85);
                dc.DrawEllipse(eye, null, new Point(c.X - 0.34 * r + look, c.Y - 0.02 * r), r * 0.11, r * 0.13 * blink);
                dc.DrawEllipse(eye, null, new Point(c.X + 0.34 * r + look, c.Y - 0.02 * r), r * 0.11, r * 0.13 * blink);
            }
            dc.Pop();

            // Arms up for the cheer, from the shoulders behind the rail.
            if (happy && cheer < 1.0)
            {
                double k = Math.Sin(Clamp01(cheer / 1.0) * Math.PI);
                var arm = Stroke(Mix(hue, Ink, 0.3), r * 0.42);
                foreach (var side in new[] { -1, 1 })
                {
                    var hand = new Point(c.X + side * r * (1.25 + 0.15 * k), c.Y - r * (0.1 + 0.7 * k));
                    dc.DrawLine(arm, new Point(c.X + side * r * 0.95, c.Y + r * 1.3), hand);
                    dc.DrawEllipse(Fill(Mix(hue, Colors.White, 0.2)), null, hand, r * 0.28, r * 0.28);
                    dc.DrawEllipse(null, Bevel(Math.Max(1, r * 0.06)), hand, r * 0.26, r * 0.26);
                }
            }
        }

        private void DrawRail(DrawingContext dc, Box b, double t, double enter)
        {
            double u = b.U;
            double slide = (1 - enter) * 10 * u;
            var r = b.R(-50, 33, 100, 9);
            r.Offset(0, slide);
            dc.PushTransform(new TranslateTransform(0, slide));
            dc.DrawDrawing(Lay("dc-rail", d =>
            {
                var rest = b.R(-50, 33, 100, 9);
                PlateStatic(d, rest, 3 * u, Linear(Mix(Slate, Accent, 0.25), Mix(Ink, Accent, 0.1), new Point(0, 0), new Point(0, 1)), u * 1.2);
                d.DrawLine(Stroke(Accent, Math.Max(1, u * 0.4), 0.55), new Point(rest.X + 3 * u, rest.Y + u * 0.9), new Point(rest.Right - 3 * u, rest.Y + u * 0.9));
            }));
            dc.Pop();
            // Little bulbs along the rail, chasing.
            for (int i = 0; i < 11; i++)
            {
                double on = 0.35 + 0.65 * Math.Max(0, Math.Sin(t * 3.2 - i * 0.7));
                var p = new Point(r.X + r.Width * (0.06 + i * 0.088), r.Y + r.Height * 0.58);
                dc.DrawEllipse(Fill(Mix(Accent, Colors.White, 0.5), on), null, p, u * 0.9, u * 0.9);
            }
            var shape = new RectangleGeometry(r, 3 * u, 3 * u);
            shape.Freeze();
            Sheen(dc, shape, r, t, 5.5, 2.5, 0.5);
        }
    }

    /// <summary>
    /// Web App: the panel on the left and a phone on the right, both showing the same tiles. The
    /// pointer clicks a tile on the panel, it presses in and lights, a packet arcs over to the phone
    /// and the same tile lights there; then a fingertip taps another tile on the phone and the packet
    /// comes back. The XP bars fill in step. Both go dark together at the end and it starts again.
    /// </summary>
    public sealed class WebAppHouseArt : HouseSceneArtA
    {
        private const double Cycle = 6.0;
        private const double ClickAt = 0.75, PhoneLitAt = 1.55, TapAt = 3.55, PanelLitAt = 4.35, DarkAt = 5.45;
        private static readonly Geometry Cursor = Frozen(MakeCursor());

        private Color TileHue(int i) => (i % 6) switch
        {
            0 => Pink,
            1 => Cyan,
            2 => Lilac,
            3 => Gold,
            4 => Mint,
            _ => Mix(Pink, Lilac, 0.5),
        };

        protected override void Paint(DrawingContext dc, double w, double h, double t)
        {
            CachedGround(dc, w, h, Deep(Accent));
            var b = Fit(w, h);
            double u = b.U;
            Motes(dc, w, h, t, Accent, 12, 51, new Rect(0.5, 0.06, 0.48, 0.85), 0.05, 0.005, 0.4);

            int cyc = (int)Math.Floor(t / Cycle);
            double p = t - cyc * Cycle;
            // Rows: the panel lights row rk's tile, the phone taps row rj. Alternates each loop.
            bool even = (cyc & 1) == 0;
            int rk = even ? 0 : 2, rj = even ? 3 : 1;
            int k = RowTile[rk], j = RowTile[rj];

            double enter = BackOut(t / 0.85, 1.3), enterPhone = BackOut((t - 0.15) / 0.85, 1.3);
            double dark = EaseOut((p - DarkAt) / 0.45);

            // Tile states, per device: below 0 = off, else seconds since it lit (for the pop).
            double PanelLit(int i) => i == k ? p - ClickAt : i == j ? p - PanelLitAt : -1;
            double PhoneLit(int row) => row == rk ? p - PhoneLitAt : row == rj ? p - TapAt : -1;
            double panelXp = 0.3 + 0.22 * (p >= ClickAt ? 1 : 0) + 0.22 * (p >= PanelLitAt ? 1 : 0);
            double phoneXp = 0.3 + 0.22 * (p >= PhoneLitAt ? 1 : 0) + 0.22 * (p >= TapAt ? 1 : 0);
            panelXp = Lerp(panelXp, 0.3, dark);
            phoneXp = Lerp(phoneXp, 0.3, dark);

            var phonePose = PhonePose(b, t, enterPhone);

            // Panel window.
            dc.PushTransform(new TranslateTransform(PanelX * u, (1 - enter) * 22 * u));
            dc.PushOpacity(Clamp01(t / 0.35));
            DrawPanel(dc, b, t, PanelLit, panelXp, dark);
            dc.Pop();
            dc.Pop();

            // Phone.
            dc.PushOpacity(Clamp01((t - 0.15) / 0.35));
            dc.PushTransform(new MatrixTransform(phonePose));
            DrawPhone(dc, u, t, PhoneLit, phoneXp, dark);
            dc.Pop();
            dc.Pop();

            // Packets: panel -> phone after the click, phone -> panel after the tap.
            var panelK = PanelTileCentre(b, k, enter);
            var phoneK = phonePose.Transform(PhoneToggle(rk, u));
            var panelJ = PanelTileCentre(b, j, enter);
            var phoneJ = phonePose.Transform(PhoneToggle(rj, u));
            DrawPacket(dc, panelK, phoneK, (p - ClickAt - 0.05) / (PhoneLitAt - ClickAt - 0.05), u, TileHue(k), 3);
            DrawPacket(dc, phoneJ, panelJ, (p - TapAt - 0.05) / (PanelLitAt - TapAt - 0.05), u, TileHue(j), 5);

            // Arrival bursts.
            Burst(dc, phoneK, 9 * u, 2.4 * u, (p - PhoneLitAt) / 0.55, 71 + cyc, 7, TileHue(k));
            Burst(dc, panelJ, 10 * u, 2.6 * u, (p - PanelLitAt) / 0.55, 73 + cyc, 7, TileHue(j));

            DrawPointer(dc, b, p, panelK, u, enter);
            DrawFingertip(dc, phoneJ, p, u);

            Twinkles(dc, w, h, t, Mix(Accent, Colors.White, 0.4), 5, 57, new Rect(0.52, 0.08, 0.44, 0.8), 0.026);
            Motes(dc, w, h, t, Colors.White, 5, 59, new Rect(0.55, 0.2, 0.42, 0.75), 0.1, 0.008, 0.28);
        }

        // Where the pair sits: the panel a touch right of the box's left edge, the phone in front of
        // its lower right corner, overlapping only the bezel and the third tile column.
        private const double PanelX = 1, PhoneX = 24.5, PhoneY = 9;

        // The phone shows four of the panel's tiles as a mobile list, one row each.
        private static readonly int[] RowTile = { 0, 1, 3, 4 };

        private static Matrix PhonePose(Box b, double t, double enter)
        {
            double bob = Bob(t, 0.9, 0.7) * b.U;
            double rot = 3 + Math.Sin(t * 0.75) * 0.5 + (1 - enter) * 5;
            var c = b.P(PhoneX, PhoneY);
            return Pose(c.X, c.Y + bob + (1 - enter) * 24 * b.U, rot);
        }

        // Panel tiles: 3 x 2 grid.
        private static Rect PanelTile(Box b, int i, double shift = PanelX) => b.R(shift - 35 + (i % 3) * 15.2, -16 + (i / 3) * 12.6, 13.4, 10.8);

        private static Point PanelTileCentre(Box b, int i, double enter)
        {
            var r = PanelTile(b, i);
            return new Point(r.X + r.Width / 2, r.Y + r.Height / 2 + (1 - enter) * 22 * b.U);
        }

        // Phone list rows, in the phone's own space (centre of the body at 0,0).
        private static Rect PhoneRow(int row, double u) => new(-11 * u, (-16.5 + row * 8.6) * u, 22 * u, 7 * u);

        private static Point PhoneToggle(int row, double u)
        {
            var r = PhoneRow(row, u);
            return new Point(r.Right - 4.4 * u, r.Y + r.Height / 2);
        }

        private void DrawPanel(DrawingContext dc, Box b, double t, Func<int, double> lit, double xp, double dark)
        {
            double u = b.U;
            var win = b.R(-47, -27, 59, 53);
            dc.DrawDrawing(Lay("wa-panel", d =>
            {
                PlateStatic(d, win, 4 * u, Linear(Mix(Slate, Accent, 0.1), Mix(Ink, Slate, 0.4), new Point(0, 0), new Point(0, 1)), u * 1.6);
                // Title bar with three lamps and a little address line.
                d.DrawRoundedRectangle(Solid(Ink, 0.35), null, new Rect(win.X + u, win.Y + u, win.Width - 2 * u, 6 * u), 3.2 * u, 3.2 * u);
                d.DrawEllipse(Solid(Pink), null, b.P(-43, -23.9), 1.1 * u, 1.1 * u);
                d.DrawEllipse(Solid(Gold), null, b.P(-39.6, -23.9), 1.1 * u, 1.1 * u);
                d.DrawEllipse(Solid(Mint), null, b.P(-36.2, -23.9), 1.1 * u, 1.1 * u);
                d.DrawRoundedRectangle(Solid(Colors.White, 0.18), null, b.R(-22, -24.8, 18, 1.8), 0.9 * u, 0.9 * u);
                // Rail of section buttons.
                d.DrawRoundedRectangle(Solid(Ink, 0.3), null, b.R(-45.6, -17.5, 8, 41), 2.4 * u, 2.4 * u);
                for (int i = 0; i < 4; i++)
                {
                    var c = b.R(-44, -15.5 + i * 7.4, 4.8, 4.8);
                    d.DrawRoundedRectangle(Solid(i == 0 ? Accent : Color.FromRgb(0x6a, 0x66, 0x9c), i == 0 ? 0.95 : 0.55), null, c, 1.4 * u, 1.4 * u);
                }
                // XP well and two text lines.
                d.DrawRoundedRectangle(Solid(Ink, 0.6), null, b.R(-35, 13.6, 45.6, 3.6), 1.8 * u, 1.8 * u);
                d.DrawRoundedRectangle(null, Bevel(Math.Max(1, u * 0.35), pressed: true), b.R(-35, 13.6, 45.6, 3.6), 1.8 * u, 1.8 * u);
                d.DrawRoundedRectangle(Solid(Colors.White, 0.2), null, b.R(-35, 19.4, 20, 1.6), 0.8 * u, 0.8 * u);
            }));
            for (int i = 0; i < 6; i++) DrawTile(dc, PanelTile(b, i, 0), TileHue(i), lit(i), dark, u, 2.4 * u);
            var xr = b.R(-35, 13.6, 45.6 * xp, 3.6);
            if (xr.Width > 1) dc.DrawRoundedRectangle(Fill(Mix(Accent, Colors.White, 0.2)), null, xr, 1.8 * u, 1.8 * u);
            var shape = new RectangleGeometry(win, 4 * u, 4 * u);
            shape.Freeze();
            Sheen(dc, shape, win, t, 6, 3.6, 0.35);
        }

        /// <summary>A toggle tile: raised and dark when off; pressed in and lit when on (on is pressed in).</summary>
        private void DrawTile(DrawingContext dc, Rect r, Color hue, double since, double dark, double u, double radius)
        {
            bool on = since >= 0 && dark < 1;
            if (!on)
            {
                Plate(dc, r, radius, Fill(Color.FromRgb(0x36, 0x33, 0x66)), u * 0.9);
                dc.DrawEllipse(Fill(hue, 0.6), null, new Point(r.X + r.Width * 0.27, r.Y + r.Height * 0.34), r.Height * 0.13, r.Height * 0.13);
                dc.DrawRoundedRectangle(Fill(Colors.White, 0.22), null, new Rect(r.X + r.Width * 0.18, r.Y + r.Height * 0.62, r.Width * 0.6, r.Height * 0.12), r.Height * 0.06, r.Height * 0.06);
                return;
            }
            // Press: travel down a touch, then a lit pop.
            double dy = u * 0.5 * Clamp01(BackOut(since / 0.28, 2.2));
            var pr = new Rect(r.X, r.Y + dy, r.Width, r.Height);
            var c = new Point(pr.X + pr.Width / 2, pr.Y + pr.Height / 2);
            double pop = 1 + 0.1 * Pulse(since, 0, 0.3);
            dc.PushTransform(new ScaleTransform(pop, pop, c.X, c.Y));
            dc.PushOpacity(Lerp(1, 0.3, dark));
            Plate(dc, pr, radius, Fill(hue), 0, pressed: true);
            dc.DrawRoundedRectangle(Fill(Colors.White, 0.22), null, new Rect(pr.X + pr.Width * 0.08, pr.Y + pr.Height * 0.08, pr.Width * 0.84, pr.Height * 0.36), radius * 0.7, radius * 0.7);
            dc.DrawEllipse(Fill(Colors.White, 0.95), null, new Point(pr.X + pr.Width * 0.27, pr.Y + pr.Height * 0.34), pr.Height * 0.13, pr.Height * 0.13);
            dc.DrawRoundedRectangle(Fill(Ink, 0.45), null, new Rect(pr.X + pr.Width * 0.18, pr.Y + pr.Height * 0.62, pr.Width * 0.6, pr.Height * 0.12), pr.Height * 0.06, pr.Height * 0.06);
            dc.Pop();
            dc.Pop();
            Ripple(dc, c, r.Width * 0.4, r.Width * 1.1, since / 0.6, Mix(hue, Colors.White, 0.4), u * 0.7);
        }

        /// <summary>
        /// The phone: upright with a small lean, a thin bezel round a 9:19.5 screen, a pill cut-out
        /// up top. Its screen is the app's mobile layout: a status line, a header, four list rows
        /// with toggles (the same tiles as the panel), the XP bar and the home bar.
        /// </summary>
        private void DrawPhone(DrawingContext dc, double u, double t, Func<int, double> lit, double xp, double dark)
        {
            var screen = new Rect(-12.7 * u, -27.7 * u, 25.4 * u, 55.4 * u);
            dc.DrawDrawing(Lay("wa-phone", d =>
            {
                // Side buttons first, so the body sits over their inner edge.
                var key = Solid(Color.FromRgb(0x3a, 0x36, 0x6c));
                d.DrawRoundedRectangle(key, null, new Rect(-14.7 * u, -16 * u, 1.2 * u, 4.6 * u), 0.5 * u, 0.5 * u);
                d.DrawRoundedRectangle(key, null, new Rect(-14.7 * u, -9.6 * u, 1.2 * u, 4.6 * u), 0.5 * u, 0.5 * u);
                d.DrawRoundedRectangle(key, null, new Rect(13.5 * u, -12.5 * u, 1.2 * u, 7 * u), 0.5 * u, 0.5 * u);
                var body = new Rect(-14 * u, -29 * u, 28 * u, 58 * u);
                PlateStatic(d, body, 5.4 * u, Linear(Color.FromRgb(0x56, 0x50, 0x94), Color.FromRgb(0x1e, 0x1b, 0x3e), new Point(0, 0), new Point(1, 1)), u * 1.8);
                d.DrawRoundedRectangle(Solid(Color.FromRgb(6, 5, 14)), null, new Rect(-13.3 * u, -28.3 * u, 26.6 * u, 56.6 * u), 4.8 * u, 4.8 * u);
                d.DrawRoundedRectangle(Linear(Mix(Slate, Accent, 0.12), Mix(Ink, Slate, 0.3), new Point(0, 0), new Point(0, 1)), null, screen, 4.2 * u, 4.2 * u);
                // Status line: the time on the left, the pill in the middle, a battery on the right.
                d.DrawRoundedRectangle(Solid(Colors.White, 0.55), null, new Rect(-10.2 * u, -25.9 * u, 3.6 * u, 1.1 * u), 0.55 * u, 0.55 * u);
                d.DrawRoundedRectangle(Solid(Color.FromRgb(4, 4, 10)), null, new Rect(-3.6 * u, -26.6 * u, 7.2 * u, 2.3 * u), 1.15 * u, 1.15 * u);
                d.DrawRoundedRectangle(null, Stroke(Colors.White, Math.Max(0.75, u * 0.18), 0.55), new Rect(6.4 * u, -26 * u, 3.4 * u, 1.4 * u), 0.4 * u, 0.4 * u);
                d.DrawRectangle(Solid(Mint, 0.85), null, new Rect(6.8 * u, -25.65 * u, 2.2 * u, 0.7 * u));
                // App header: the mark, a title, an avatar.
                d.DrawEllipse(Solid(Accent), null, new Point(-9.4 * u, -20.6 * u), 1.7 * u, 1.7 * u);
                d.DrawRoundedRectangle(Solid(Colors.White, 0.7), null, new Rect(-6.8 * u, -21.5 * u, 8.6 * u, 1.8 * u), 0.9 * u, 0.9 * u);
                d.DrawEllipse(Solid(Lilac), null, new Point(9.4 * u, -20.6 * u), 1.7 * u, 1.7 * u);
                d.DrawEllipse(null, Bevel(Math.Max(0.75, u * 0.25)), new Point(9.4 * u, -20.6 * u), 1.55 * u, 1.55 * u);
                // XP well, its label and the home bar.
                d.DrawRoundedRectangle(Solid(Colors.White, 0.3), null, new Rect(-11 * u, 18.1 * u, 6 * u, 1.1 * u), 0.55 * u, 0.55 * u);
                d.DrawRoundedRectangle(Solid(Ink, 0.7), null, new Rect(-11 * u, 20.2 * u, 22 * u, 2.4 * u), 1.2 * u, 1.2 * u);
                d.DrawRoundedRectangle(null, Bevel(Math.Max(0.75, u * 0.25), pressed: true), new Rect(-11 * u, 20.2 * u, 22 * u, 2.4 * u), 1.2 * u, 1.2 * u);
                d.DrawRoundedRectangle(Solid(Colors.White, 0.6), null, new Rect(-4.2 * u, 25 * u, 8.4 * u, 0.9 * u), 0.45 * u, 0.45 * u);
            }));
            for (int row = 0; row < 4; row++) DrawRow(dc, PhoneRow(row, u), TileHue(RowTile[row]), lit(row), dark, u);
            if (xp > 0.01) dc.DrawRoundedRectangle(Fill(Mix(Accent, Colors.White, 0.2)), null, new Rect(-11 * u, 20.2 * u, 22 * u * xp, 2.4 * u), 1.2 * u, 1.2 * u);
            var glass = new RectangleGeometry(screen, 4.2 * u, 4.2 * u);
            glass.Freeze();
            Sheen(dc, glass, screen, t, 6, 0.6, 0.4);
        }

        /// <summary>A mobile list row: icon, label lines and a toggle. Off: raised, switch left.
        /// On: pressed in, tinted, the switch fills with the tile's hue and its knob slides right.</summary>
        private void DrawRow(DrawingContext dc, Rect r, Color hue, double since, double dark, double u)
        {
            bool on = since >= 0 && dark < 1;
            double k = on ? Clamp01(BackOut(since / 0.3, 2.2)) * (1 - dark) : 0;
            double knobK = on ? BackOut(since / 0.3, 2.2) * (1 - dark) : 0;
            var baseFill = Color.FromRgb(0x33, 0x30, 0x62);
            var pr = new Rect(r.X, r.Y + u * 0.35 * k, r.Width, r.Height);
            if (k <= 0.01) Plate(dc, pr, 2.2 * u, Fill(baseFill), u * 0.8);
            else Plate(dc, pr, 2.2 * u, Fill(Mix(baseFill, hue, 0.3 * k)), 0, pressed: true);
            double cy = pr.Y + pr.Height / 2;
            // Icon: a small rounded square in the tile's hue.
            var icon = new Rect(pr.X + 1.6 * u, cy - 1.9 * u, 3.8 * u, 3.8 * u);
            dc.DrawRoundedRectangle(Fill(hue, 0.55 + 0.45 * k), null, icon, 1.1 * u, 1.1 * u);
            dc.DrawRoundedRectangle(Fill(Colors.White, 0.35 + 0.4 * k), null, new Rect(icon.X + 0.9 * u, icon.Y + 0.9 * u, 1.3 * u, 1.3 * u), 0.5 * u, 0.5 * u);
            // Label lines.
            dc.DrawRoundedRectangle(Fill(Colors.White, 0.55 + 0.25 * k), null, new Rect(pr.X + 6.9 * u, cy - 1.6 * u, 7 * u, 1.3 * u), 0.65 * u, 0.65 * u);
            dc.DrawRoundedRectangle(Fill(Colors.White, 0.22), null, new Rect(pr.X + 6.9 * u, cy + 0.6 * u, 4.6 * u, 1 * u), 0.5 * u, 0.5 * u);
            // Toggle.
            var tc = new Point(pr.Right - 4.4 * u, cy);
            var track = new Rect(tc.X - 2.8 * u, cy - 1.55 * u, 5.6 * u, 3.1 * u);
            dc.DrawRoundedRectangle(Fill(Ink, 0.65), null, track, 1.55 * u, 1.55 * u);
            if (k > 0.01) dc.DrawRoundedRectangle(Fill(hue, k), null, track, 1.55 * u, 1.55 * u);
            dc.DrawRoundedRectangle(null, Bevel(Math.Max(0.75, u * 0.22), pressed: true), track, 1.55 * u, 1.55 * u);
            double kx = Lerp(track.X + 1.55 * u, track.Right - 1.55 * u, knobK);
            double squish = on ? 1 + 0.25 * Pulse(since, 0.02, 0.22) : 1;
            dc.DrawEllipse(Fill(Ink, 0.35), null, new Point(kx + 0.25 * u, cy + 0.35 * u), 1.2 * u * squish, 1.2 * u);
            dc.DrawEllipse(Fill(Colors.White, on ? 0.98 : 0.7), null, new Point(kx, cy), 1.2 * u * squish, 1.2 * u);
            if (on) Ripple(dc, tc, u * 2, u * 6, since / 0.55, Mix(hue, Colors.White, 0.4), u * 0.5);
        }

        /// <summary>A packet arcing from one tile to the other, a short tail of glints behind it.</summary>
        private void DrawPacket(DrawingContext dc, Point from, Point to, double k, double u, Color hue, int seed)
        {
            if (k <= 0 || k >= 1) return;
            double lift = 24 * u;
            var c1 = new Point(from.X + (to.X - from.X) * 0.2, from.Y - lift);
            var c2 = new Point(to.X - (to.X - from.X) * 0.2, to.Y - lift);
            double s = k < 0.5 ? 2 * k * k : 1 - Math.Pow(-2 * k + 2, 2) / 2;
            var light = Mix(hue, Colors.White, 0.55);
            for (int i = 6; i >= 1; i--)
            {
                double back = s - i * 0.035;
                if (back <= 0) continue;
                var q = Cubic(from, c1, c2, to, back);
                dc.DrawEllipse(Fill(light, 0.55 * (1 - i / 7.0)), null, q, u * (2.1 - i * 0.2), u * (2.1 - i * 0.2));
            }
            var pt = Cubic(from, c1, c2, to, s);
            var ahead = Cubic(from, c1, c2, to, Math.Min(1, s + 0.02));
            double ang = Math.Atan2(ahead.Y - pt.Y, ahead.X - pt.X) * 180 / Math.PI;
            dc.PushTransform(new MatrixTransform(Pose(pt.X, pt.Y, ang)));
            dc.DrawRoundedRectangle(Fill(Ink, 0.3), null, new Rect(-4.6 * u, -1.4 * u, 9.6 * u, 4.6 * u), 2.3 * u, 2.3 * u);
            dc.DrawRoundedRectangle(Fill(hue), null, new Rect(-5 * u, -2.3 * u, 10 * u, 4.6 * u), 2.3 * u, 2.3 * u);
            dc.DrawRoundedRectangle(null, Bevel(Math.Max(1, u * 0.4)), new Rect(-5 * u, -2.3 * u, 10 * u, 4.6 * u), 2.3 * u, 2.3 * u);
            dc.DrawRoundedRectangle(Fill(Colors.White, 0.9), null, new Rect(-1 * u, -1 * u, 4.6 * u, 2 * u), u, u);
            dc.Pop();
            Spark(dc, new Point(pt.X + 3 * u, pt.Y - 3 * u), 3 * u, Colors.White, 0.9, k * 6 + seed);
        }

        private void DrawPointer(DrawingContext dc, Box b, double p, Point target, double u, double enter)
        {
            var rest = b.P(-4 + PanelX, 21);
            var aim = new Point(target.X + 1.5 * u, target.Y + 1 * u);
            double go = EaseOut((p - 0.1) / 0.6);
            double away = EaseOut((p - 1.15) / 0.8);
            double idle = 1 - go + away;
            var pos = new Point(
                Lerp(Lerp(rest.X, aim.X, go), rest.X, away) + Math.Sin(p * 1.3) * u * 0.6 * idle,
                Lerp(Lerp(rest.Y, aim.Y, go), rest.Y, away) + Math.Cos(p * 1.1) * u * 0.5 * idle + (1 - enter) * 22 * u);
            double click = Pulse(p, ClickAt - 0.06, 0.18);
            double s = 6.2 * u * (1 - 0.18 * click);
            dc.PushOpacity(Clamp01(enter));
            dc.PushTransform(new MatrixTransform(Pose(pos.X + u * 0.6, pos.Y + u * 0.9, -8, s, s)));
            dc.DrawGeometry(Fill(Ink, 0.28), null, Cursor);
            dc.Pop();
            dc.PushTransform(new MatrixTransform(Pose(pos.X, pos.Y, -8, s, s)));
            dc.DrawGeometry(Fill(Colors.White), Stroke(Ink, 0.12, 0.9), Cursor);
            dc.Pop();
            dc.Pop();
            Ripple(dc, aim, u * 1.5, u * 7, (p - ClickAt) / 0.5, Colors.White, u * 0.6);
        }

        private void DrawFingertip(DrawingContext dc, Point target, double p, double u)
        {
            double inK = EaseOut((p - (TapAt - 0.45)) / 0.4), outK = EaseOut((p - (TapAt + 0.2)) / 0.4);
            double a = inK * (1 - outK);
            if (a > 0.02)
            {
                double press = Pulse(p, TapAt - 0.05, 0.22);
                var c = new Point(target.X + (1 - inK) * 6 * u + outK * 5 * u, target.Y + (1 - inK) * 8 * u + outK * 6 * u);
                double r = 3.6 * u * (1 - 0.2 * press);
                dc.PushOpacity(a * 0.85);
                dc.DrawEllipse(Fill(Ink, 0.35), null, new Point(c.X + u * 0.8, c.Y + u * 1.2), r, r);
                dc.DrawEllipse(FingerBrush, null, c, r, r);
                dc.Pop();
            }
            Ripple(dc, target, u * 2, u * 8, (p - TapAt) / 0.55, Colors.White, u * 0.6);
        }

        private static readonly Brush FingerBrush = MakeFinger();

        private static Brush MakeFinger()
        {
            var tip = new RadialGradientBrush(Color.FromArgb(0xee, 0xff, 0xff, 0xff), Color.FromArgb(0x90, 0xd8, 0xd0, 0xff)) { GradientOrigin = new Point(0.35, 0.3) };
            tip.Freeze();
            return tip;
        }

        private static Geometry MakeCursor()
        {
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                c.BeginFigure(new Point(0, 0), true, true);
                c.LineTo(new Point(0, 1.0), false, false);
                c.LineTo(new Point(0.26, 0.76), false, false);
                c.LineTo(new Point(0.44, 1.12), false, false);
                c.LineTo(new Point(0.58, 1.05), false, false);
                c.LineTo(new Point(0.4, 0.7), false, false);
                c.LineTo(new Point(0.72, 0.68), false, false);
            }
            return g;
        }
    }

    /// <summary>
    /// The Remix Room: a strip of film slides in and stops, two pink trim handles clamp four frames,
    /// the frames peel off one by one and fly into a gif tile (each landing flashes the screen and
    /// lights a pip), then the tile plays the four frames as a loop: a ball bouncing, with a loop
    /// arrow spinning round its corner and a little pixel GIF badge.
    /// </summary>
    public sealed class RemixHouseArt : HouseSceneArtA
    {
        private const double Cycle = 6.4, Pitch = 14, StripAngle = -8;
        private const double ScrollFor = 1.1, ClampAt = 1.1, LiftAt = 1.75, LiftStep = 0.28, FlyFor = 0.6, ReleaseAt = 5.6;
        private static readonly Geometry LoopArrow = Frozen(MakeLoopArrow());
        private static readonly Geometry Bracket = Frozen(MakeBracket());
        private const int GlyphG = 0b111_100_101_101_111, GlyphI = 0b111_010_010_010_111, GlyphF = 0b111_100_110_100_100;

        protected override void Paint(DrawingContext dc, double w, double h, double t)
        {
            CachedGround(dc, w, h, Deep(Accent));
            var b = Fit(w, h);
            double u = b.U;
            Motes(dc, w, h, t, Accent, 12, 81, new Rect(0.5, 0.06, 0.48, 0.85), 0.05, 0.005, 0.4);

            int cyc = (int)Math.Floor(t / Cycle);
            double p = t - cyc * Cycle;
            double enter = BackOut(t / 0.85, 1.2);

            // Scroll: four frames per cycle, easing to a stop with a small overshoot.
            double off = Pitch * 4 * (cyc + BackOut(p / ScrollFor, 0.9));
            var stripC = b.P(-10, 22 + (1 - enter) * 20);
            var stripM = Pose(stripC.X, stripC.Y, StripAngle);
            double clamp = BackOut((p - ClampAt) / 0.4, 2.2) * (1 - EaseOut((p - ReleaseAt) / 0.5));

            // The gif tile (drawn first so flying frames land on top of it).
            var tile = b.R(8, -39 - (1 - enter) * 10, 40, 37);
            int landed = 0;
            double lastLand = -99;
            for (int f = 0; f < 4; f++)
            {
                double land = LiftAt + f * LiftStep + FlyFor;
                if (p >= land) { landed++; lastLand = land; }
            }
            double flash = p - lastLand < 0.3 ? 1 - (p - lastLand) / 0.3 : 0;
            double done = p - (LiftAt + 3 * LiftStep + FlyFor);
            DrawTile(dc, b, tile, t, p, landed, flash, done);

            // The strip.
            dc.PushOpacity(Clamp01(t / 0.4));
            dc.PushTransform(new MatrixTransform(stripM));
            DrawStrip(dc, u, t, off, clamp);
            DrawHandles(dc, u, clamp, p);
            dc.Pop();
            dc.Pop();

            // Frames in flight: copies of the four clamped frames.
            var screen = Screen(tile, u);
            var screenC = new Point(screen.X + screen.Width / 2, screen.Y + screen.Height / 2);
            for (int f = 0; f < 4; f++)
            {
                double k = (p - (LiftAt + f * LiftStep)) / FlyFor;
                if (k <= 0 || k >= 1) continue;
                double lx = (f - 1.5) * Pitch * u;
                var from = stripM.Transform(new Point(lx, -0.5 * u));
                double e = k * k * (3 - 2 * k);
                var c1 = new Point(from.X, from.Y - 22 * u);
                var c2 = new Point(screenC.X - 10 * u, screenC.Y - 12 * u);
                var pt = Cubic(from, c1, c2, screenC, e);
                double sc = Lerp(1, 1.9, e) * (1 + 0.15 * Math.Sin(k * Math.PI));
                double rot = Lerp(StripAngle, 0, e) + Math.Sin(k * Math.PI) * 14 * (f % 2 == 0 ? 1 : -1);
                double alpha = k < 0.85 ? 1 : 1 - (k - 0.85) / 0.15;
                // The strip's frame i shows phase i, and the clamped four start two before the cycle mark.
                int phase = f + 2;
                dc.PushOpacity(alpha);
                for (int g = 3; g >= 1; g--)
                {
                    double eb = Math.Max(0, e - g * 0.06);
                    var q = Cubic(from, c1, c2, screenC, eb);
                    dc.PushOpacity(0.18 * (4 - g) / 3);
                    DrawFrameCard(dc, q, Lerp(1, 1.9, eb), rot, u, phase, false);
                    dc.Pop();
                }
                DrawFrameCard(dc, pt, sc, rot, u, phase, true);
                dc.Pop();
            }

            // Landing sparks at the tile, and a gold flourish when the loop is complete.
            if (p - lastLand < 0.5) Burst(dc, screenC, 16 * u, 2.6 * u, (p - lastLand) / 0.5, 91 + landed, 7, Mix(Accent, Colors.White, 0.3));
            if (done > 0 && done < 0.8) Burst(dc, new Point(tile.Right - 1 * u, tile.Y + 1 * u), 14 * u, 3 * u, done / 0.8, 97, 9, Gold);

            Twinkles(dc, w, h, t, Mix(Accent, Colors.White, 0.4), 5, 87, new Rect(0.52, 0.08, 0.44, 0.8), 0.026);
            Motes(dc, w, h, t, Colors.White, 5, 89, new Rect(0.55, 0.2, 0.42, 0.75), 0.1, 0.008, 0.28);
        }

        private static Rect Screen(Rect tile, double u) => new(tile.X + 3 * u, tile.Y + 3 * u, tile.Width - 6 * u, tile.Height - 9 * u);

        /// <summary>One frame's picture: a ball on a floor at one of four moments of its bounce.</summary>
        private void DrawPicture(DrawingContext dc, Rect r, int phase, double bright)
        {
            phase = ((phase % 4) + 4) % 4;
            var bg = Mix(Color.FromRgb(0x52, 0x2a, 0x6e), Accent, 0.14 + 0.12 * bright);
            dc.DrawRectangle(Fill(Mix(bg, Colors.White, 0.06 + 0.06 * bright)), null, r);
            double floor = r.Y + r.Height * 0.8;
            dc.DrawRectangle(Fill(Mix(bg, Ink, 0.45)), null, new Rect(r.X, floor, r.Width, r.Bottom - floor));
            dc.DrawRectangle(Fill(Mix(bg, Colors.White, 0.35), 0.6), null, new Rect(r.X, floor, r.Width, Math.Max(0.75, r.Height * 0.03)));
            // Height through the bounce: floor (squash), rising, top, falling.
            double hgt = phase switch { 0 => 0, 1 => 0.62, 2 => 1, _ => 0.62 };
            double rad = r.Height * 0.18;
            double sq = phase == 0 ? 1.35 : phase == 2 ? 0.94 : 0.9;
            double cx = r.X + r.Width * 0.5;
            double cy = floor - rad / sq - hgt * r.Height * 0.42;
            double shadow = rad * (1.1 - hgt * 0.5);
            dc.DrawEllipse(Fill(Ink, 0.5 * (1 - hgt * 0.5)), null, new Point(cx, floor + rad * 0.12), shadow, shadow * 0.28);
            dc.DrawEllipse(Fill(Mix(Pink, Colors.White, 0.12 * bright)), null, new Point(cx, cy), rad * sq, rad / sq);
            dc.DrawEllipse(Fill(Colors.White, 0.75), null, new Point(cx - rad * 0.35, cy - rad * 0.38 / sq), rad * 0.28, rad * 0.22);
        }

        private void DrawStrip(DrawingContext dc, double u, double t, double off, double clamp)
        {
            double halfLen = 42 * u, half = 9 * u;
            var band = new Rect(-halfLen, -half, halfLen * 2, half * 2);
            var ink = Color.FromRgb(4, 4, 12);
            dc.DrawRoundedRectangle(Fill(ink, 0.3), null, new Rect(band.X + u, band.Y + 1.6 * u, band.Width, band.Height), 2 * u, 2 * u);
            dc.DrawRoundedRectangle(Fill(Color.FromRgb(0x1a, 0x13, 0x2c)), null, band, 2 * u, 2 * u);

            double pitch = Pitch * u;
            var bandShape = new RectangleGeometry(band, 2 * u, 2 * u);
            bandShape.Freeze();
            dc.PushClip(bandShape);
            // Sprocket holes, moving with the film.
            double hp = pitch / 4, hoff = off * u % hp;
            for (double x = -halfLen - hoff; x < halfLen + hp; x += hp)
            {
                dc.DrawRoundedRectangle(Fill(Ink, 0.9), null, new Rect(x, -half + 1 * u, hp * 0.5, 1.7 * u), 0.5 * u, 0.5 * u);
                dc.DrawRoundedRectangle(Fill(Ink, 0.9), null, new Rect(x, half - 2.7 * u, hp * 0.5, 1.7 * u), 0.5 * u, 0.5 * u);
            }
            // Frames.
            int first = (int)Math.Floor((off * u - halfLen) / pitch) - 1;
            for (int i = first; i < first + 10; i++)
            {
                double x = (i + 0.5) * pitch - off * u;
                if (x < -halfLen - pitch || x > halfLen + pitch) continue;
                bool chosen = clamp > 0.05 && Math.Abs(x) < pitch * 2;
                double edgeFade = Clamp01((halfLen - Math.Abs(x)) / (pitch * 1.2));
                var fr = new Rect(x - pitch * 0.4, -half + 3.4 * u, pitch * 0.8, half * 2 - 6.8 * u);
                dc.PushOpacity(edgeFade * (clamp > 0.05 && !chosen ? Lerp(1, 0.4, Clamp01(clamp)) : 1));
                DrawPicture(dc, fr, i, chosen ? Clamp01(clamp) : 0);
                dc.Pop();
            }
            dc.Pop();
            dc.DrawRoundedRectangle(null, Bevel(Math.Max(1, u * 0.45)), band, 2 * u, 2 * u);
            // Selection between the handles.
            if (clamp > 0.02)
            {
                double a = Clamp01(clamp);
                var sel = new Rect(-pitch * 2, -half, pitch * 4, half * 2);
                dc.DrawRectangle(Fill(Accent, 0.14 * a), null, sel);
                dc.DrawLine(Stroke(Accent, Math.Max(1, u * 0.6), 0.9 * a), sel.TopLeft, sel.TopRight);
                dc.DrawLine(Stroke(Accent, Math.Max(1, u * 0.6), 0.9 * a), sel.BottomLeft, sel.BottomRight);
            }
            Sheen(dc, bandShape, band, t, 6.4, 5.3, 0.4);
        }

        private void DrawHandles(DrawingContext dc, double u, double clamp, double p)
        {
            if (clamp <= 0.01) return;
            double pitch = Pitch * u;
            double travel = (1 - clamp) * 14 * u;
            double squash = 1 + 0.14 * Pulse(p, ClampAt + 0.25, 0.25);
            var face = Fill(Mix(Accent, Colors.White, 0.12));
            foreach (var side in new[] { -1, 1 })
            {
                double x = side * (pitch * 2 + 1.4 * u + travel);
                double s = 10.5 * u;
                // The bracket is a "[" with its back at x = 0; the right one is mirrored.
                dc.PushOpacity(Clamp01(clamp * 1.5));
                dc.PushTransform(new MatrixTransform(Pose(x + u * 0.6, u * 0.9, 0, -side * s / squash, s * squash)));
                dc.DrawGeometry(Fill(Ink, 0.4), null, Bracket);
                dc.Pop();
                dc.PushTransform(new MatrixTransform(Pose(x, 0, 0, -side * s / squash, s * squash)));
                dc.DrawGeometry(face, Stroke(Colors.White, 0.06, 0.45), Bracket);
                dc.Pop();
                dc.Pop();
            }
        }

        /// <summary>A frame lifted off the strip: a little celluloid card with its picture.</summary>
        private void DrawFrameCard(DrawingContext dc, Point c, double scale, double degrees, double u, int phase, bool lit)
        {
            double fw = Pitch * 0.86 * u, fh = 12 * u;
            dc.PushTransform(new MatrixTransform(Pose(c.X, c.Y, degrees, scale, scale)));
            var r = new Rect(-fw / 2, -fh / 2, fw, fh);
            if (lit) Shadow(dc, r, u, u * 1.2);
            dc.DrawRoundedRectangle(Fill(Color.FromRgb(0x1a, 0x13, 0x2c)), null, r, u, u);
            DrawPicture(dc, new Rect(r.X + 0.9 * u, r.Y + 0.9 * u, r.Width - 1.8 * u, r.Height - 1.8 * u), phase, 1);
            dc.DrawRoundedRectangle(null, Stroke(Mix(Accent, Colors.White, 0.4), Math.Max(1, u * 0.35), 0.85), r, u, u);
            dc.Pop();
        }

        private void DrawTile(DrawingContext dc, Box b, Rect tile, double t, double p, int landed, double flash, double done)
        {
            double u = b.U;
            var c = new Point(tile.X + tile.Width / 2, tile.Y + tile.Height / 2);
            double squash = flash * 0.05;
            double bob = Bob(t, 0.7 * u, 2.1);
            dc.PushOpacity(Clamp01((t - 0.1) / 0.4));
            dc.PushTransform(new TranslateTransform(0, bob));
            dc.PushTransform(new ScaleTransform(1 + squash, 1 - squash, c.X, tile.Bottom));
            Plate(dc, tile, 3.6 * u, TileFace, u * 1.6);
            var screen = Screen(tile, u);
            var screenShape = new RectangleGeometry(screen, 1.6 * u, 1.6 * u);
            screenShape.Freeze();
            dc.DrawGeometry(Fill(Ink), null, screenShape);
            dc.PushClip(screenShape);
            // The loop: four frames stepping like a gif (no tweening between them).
            DrawPicture(dc, screen, (int)Math.Floor(t * 7) % 4, 1);
            if (flash > 0) dc.DrawRectangle(Fill(Colors.White, 0.7 * flash), null, screen);
            dc.Pop();
            dc.DrawGeometry(null, Bevel(Math.Max(1, u * 0.45), pressed: true), screenShape);
            // Pips: one per frame landed this round; they fade out together at the end.
            double fade = 1 - EaseOut((p - ReleaseAt) / 0.6);
            for (int i = 0; i < 4; i++)
            {
                var pc = new Point(tile.X + tile.Width * (0.62 + i * 0.09), tile.Bottom - 3 * u);
                dc.DrawEllipse(Fill(Ink, 0.6), null, pc, u * 1.1, u * 1.1);
                if (i < landed && fade > 0)
                {
                    double pop = BackOut((p - (LiftAt + i * LiftStep + FlyFor)) / 0.25, 2.4);
                    dc.DrawEllipse(Fill(Mix(Accent, Colors.White, 0.45), fade), null, pc, u * 0.9 * pop, u * 0.9 * pop);
                }
            }
            // A pixel GIF badge on the bottom left of the bezel.
            var badge = new Rect(tile.X + 3 * u, tile.Bottom - 5.6 * u, 14.6 * u, 5 * u);
            dc.DrawRoundedRectangle(Fill(Accent), null, badge, 1.2 * u, 1.2 * u);
            dc.DrawRoundedRectangle(null, Bevel(Math.Max(0.75, u * 0.3)), badge, 1.2 * u, 1.2 * u);
            double cell = 0.76 * u;
            var glyph = Fill(Colors.White, 0.95);
            double gy = badge.Y + (badge.Height - cell * 5) / 2, gx = badge.X + 1.7 * u;
            PixelGlyph(dc, GlyphG, gx, gy, cell, glyph);
            PixelGlyph(dc, GlyphI, gx + cell * 3.8, gy, cell, glyph);
            PixelGlyph(dc, GlyphF, gx + cell * 7.6, gy, cell, glyph);
            var shape = new RectangleGeometry(tile, 3.6 * u, 3.6 * u);
            shape.Freeze();
            Sheen(dc, shape, tile, t, 6.4, 3.0, 0.45);
            dc.Pop();
            dc.Pop();

            // The loop badge: a round plate on the top right corner with an arrow chasing itself.
            var lc = new Point(tile.Right - 1 * u, tile.Y + 1 * u + bob);
            double lr = 5.4 * u * (1 + 0.16 * Pulse(done, 0, 0.35));
            dc.DrawEllipse(Fill(Ink, 0.35), null, new Point(lc.X + u * 0.6, lc.Y + u * 0.9), lr, lr);
            dc.DrawEllipse(Fill(Mix(Accent, Ink, 0.15)), null, lc, lr, lr);
            dc.DrawEllipse(null, Bevel(Math.Max(1, u * 0.45)), lc, lr * 0.94, lr * 0.94);
            double spin = t * 90 + (done > 0 ? 360 * EaseOut(done / 0.7) : 0);
            dc.PushTransform(new MatrixTransform(Pose(lc.X, lc.Y, spin, lr * 0.62, lr * 0.62)));
            dc.DrawGeometry(Fill(Colors.White, 0.95), null, LoopArrow);
            dc.Pop();
            dc.Pop();
        }

        private static readonly Brush TileFace = MakeTileFace();

        private static Brush MakeTileFace()
        {
            var g = new LinearGradientBrush(Color.FromRgb(0x4a, 0x3a, 0x7a), Color.FromRgb(0x1d, 0x16, 0x38), new Point(0, 0), new Point(1, 1));
            g.Freeze();
            return g;
        }

        private static Point OnCircle(double a, double r) => new(Math.Cos(a) * r, Math.Sin(a) * r);

        /// <summary>Two arrowed arcs chasing round a circle of radius 1.</summary>
        private static Geometry MakeLoopArrow()
        {
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                for (int k = 0; k < 2; k++)
                {
                    double a0 = k * Math.PI, a1 = a0 + Math.PI * 0.72;
                    c.BeginFigure(OnCircle(a0, 1.0), true, true);
                    c.ArcTo(OnCircle(a1, 1.0), new Size(1.0, 1.0), 0, false, SweepDirection.Clockwise, false, false);
                    c.LineTo(OnCircle(a1, 1.28), false, false);
                    c.LineTo(OnCircle(a1 + 0.42, 0.81), false, false);
                    c.LineTo(OnCircle(a1, 0.34), false, false);
                    c.LineTo(OnCircle(a1, 0.62), false, false);
                    c.ArcTo(OnCircle(a0, 0.62), new Size(0.62, 0.62), 0, false, SweepDirection.Counterclockwise, false, false);
                }
            }
            return g;
        }

        /// <summary>A "[" in a unit box, centred on 0,0 vertically, its back at x = 0.</summary>
        private static Geometry MakeBracket()
        {
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                c.BeginFigure(new Point(0.1, -1), true, true);
                c.LineTo(new Point(0.7, -1), false, false);
                c.LineTo(new Point(0.7, -0.78), false, false);
                c.LineTo(new Point(0.3, -0.78), false, false);
                c.LineTo(new Point(0.3, 0.78), false, false);
                c.LineTo(new Point(0.7, 0.78), false, false);
                c.LineTo(new Point(0.7, 1), false, false);
                c.LineTo(new Point(0.1, 1), false, false);
                c.QuadraticBezierTo(new Point(0, 1), new Point(0, 0.9), false, false);
                c.LineTo(new Point(0, -0.9), false, false);
                c.QuadraticBezierTo(new Point(0, -1), new Point(0.1, -1), false, false);
            }
            return g;
        }
    }
}
