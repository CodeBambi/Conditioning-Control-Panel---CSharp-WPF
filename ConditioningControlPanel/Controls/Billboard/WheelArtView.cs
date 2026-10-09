using System;
using System.Windows;
using System.Windows.Media;

namespace ConditioningControlPanel.Controls.Billboard
{
    /// <summary>
    /// The Daily Daze prize wheel (owner, 2026-10-07: "put prizes on it, have it actually spin and
    /// give identity to the tiles"). Eight slices, each its own colour and a drawn prize. One loop
    /// is a spin told as a story: rest with a sway, a little wind-back, a fast spin with ghosted
    /// slices, a long ease-out with the pointer flapping over every peg, the landing (slice lit,
    /// bulbs flashing together, the prize popping out of its socket with sparks) and back. Every
    /// loop lands on a different prize from a fixed order; the jackpot comes once in four.
    /// FX PERF RULES: the disc with its icons is one cached drawing turned by a transform; icons
    /// and particles are frozen unit drawings built once for the whole type.
    /// </summary>
    public sealed class WheelArtView : BillboardVectorArt
    {
        /// <summary>The registered factory still hands over a {done, total} payload; the wheel no
        /// longer draws progress (the quests card has its own art), so it is ignored.</summary>
        public WheelArtView((int Done, int Total) progress = default) { }

        // ---- the story clock ------------------------------------------------------------------

        /// <summary>One spin, rest to rest, in seconds.</summary>
        internal const double Loop = 6.5;
        // Story time = t + Shift. The landing sits at story 5.0, so the first one lands at t 1.25
        // and the still frame (1.6 s) shows the prize popping out of the jackpot slice.
        private const double Shift = 3.75;
        private const double RestEnd = 0.9, WindEnd = 1.3, LandAt = 5.0;
        private const int Turns = 3;
        private const double WindBack = 14;   // degrees
        private const double PopOut = 0.08, PopHeld = 0.45, PopBack = 1.4, PopHome = 1.9, GlowGone = 2.4;

        internal const int Pocket = 0, HeadEmpty = 1, Good = 2, RoomService = 3, KeepChange = 4, Double = 5, Spoiled = 6, Jackpot = 7;

        /// <summary>The landing order: never the same prize twice running, the jackpot every fourth.</summary>
        internal static readonly int[] Order = { Jackpot, Pocket, RoomService, Double, Jackpot, Good, HeadEmpty, Spoiled, Jackpot, KeepChange, Double, HeadEmpty };

        private static readonly Color[] SliceHues =
        {
            Color.FromRgb(0xff, 0x4f, 0xa8), // Pocket Sparkles, pink
            Color.FromRgb(0x9b, 0x7b, 0xff), // Head Empty, violet
            Color.FromRgb(0x2f, 0xc4, 0xee), // Good Behaviour, cyan
            Color.FromRgb(0x4a, 0x6b, 0xff), // Room Service, blue
            Color.FromRgb(0xff, 0x7a, 0x4a), // Keep the Change, coral
            Color.FromRgb(0xc8, 0x6b, 0xff), // Seeing Double, orchid
            Color.FromRgb(0xff, 0x3d, 0x6e), // Spoiled Rotten, raspberry
            Color.FromRgb(0xff, 0xc9, 0x4a), // Jackpot, gold
        };

        private static readonly Color Ink = Color.FromRgb(0x1a, 0x10, 0x24);
        private static readonly Color Gold = Color.FromRgb(0xff, 0xe9, 0xa8);
        private static readonly Color Shade = Color.FromRgb(4, 4, 12);

        /// <summary>The loop a moment belongs to (its landing comes at story 5.0).</summary>
        internal static int LoopIndex(double t) => (int)Math.Floor((t + Shift) / Loop);

        /// <summary>The prize loop <paramref name="k"/> lands on.</summary>
        internal static int PrizeOf(int k) => Order[((k % Order.Length) + Order.Length) % Order.Length];

        /// <summary>When loop <paramref name="k"/> lands, in seconds of animation.</summary>
        internal static double LandingTime(int k) => k * Loop + LandAt - Shift;

        /// <summary>The disc angle (degrees, clockwise) that puts a slice's middle under the pointer.</summary>
        private static double LandAngle(int slice) => -90 - (slice + 0.5) * 45;

        private static double Mod(double a, double m) => a - Math.Floor(a / m) * m;

        /// <summary>The disc's angle at <paramref name="t"/>, continuous across loops.</summary>
        internal static double Angle(double t)
        {
            double st = t + Shift;
            int k = (int)Math.Floor(st / Loop);
            double p = st - k * Loop;
            double from = LandAngle(PrizeOf(k - 1)), to = LandAngle(PrizeOf(k));
            if (p < RestEnd) return from + Math.Sin(p / RestEnd * Math.PI) * 2.2 * Math.Sin(p * 5.5);
            if (p < WindEnd) return from - WindBack * Math.Sin((p - RestEnd) / (WindEnd - RestEnd) * Math.PI / 2);
            double delta = Mod(to - from, 360) + 360 * Turns + WindBack;
            if (p < LandAt)
            {
                double x = (p - WindEnd) / (LandAt - WindEnd);
                double f = (1 - Math.Pow(1 - x, 3.6)) * (1 - Math.Exp(-x * 40));
                return from - WindBack + delta * f;
            }
            double dp = p - LandAt;
            return to - 1.6 * Math.Sin(dp * 13) * Math.Exp(-dp * 5);
        }

        /// <summary>The prize being celebrated at <paramref name="t"/> and seconds since it landed, or -1.</summary>
        internal static int Celebration(double t, out double dp)
        {
            double st = t + Shift;
            int k = (int)Math.Floor(st / Loop);
            double p = st - k * Loop;
            if (p >= LandAt) { dp = p - LandAt; return PrizeOf(k); }
            if (p < WindEnd) { dp = p + Loop - LandAt; return PrizeOf(k - 1); }
            dp = -1;
            return -1;
        }

        private static int PegCount(double angle) => (int)Mod(Math.Floor((angle + 90) / 45), 8);

        /// <summary>The shortest signed turn from <paramref name="a"/> to <paramref name="b"/>, in degrees (the story resets whole turns at a landing).</summary>
        internal static double Turn(double a, double b) => Mod(b - a + 180, 360) - 180;

        /// <summary>How far the pointer flaps (degrees; negative = tip pushed right by a passing peg).</summary>
        private static double Flap(double t)
        {
            const double max = 26, contact = 0.22;
            double a = Angle(t);
            double q = Mod((a + 90) / 45, 1);
            double push = q > 1 - contact ? -(q - (1 - contact)) / contact * max : 0;
            // When did the last peg leave the tip? Walk back in small steps (cheap: closed form).
            int now = PegCount(a);
            double ring = 0;
            for (int i = 1; i <= 42; i++)
            {
                double s = i / 60.0;
                if (PegCount(Angle(t - s)) != now)
                {
                    ring = -max * Math.Cos(s * 30) * Math.Exp(-s * 8);
                    break;
                }
            }
            return push != 0 ? Math.Min(push, ring) : ring;
        }

        // ---- paint ----------------------------------------------------------------------------

        private Geometry[]? _wedges;
        private Geometry? _discClip;
        private double _wedgeR = -1;

        protected override void Paint(DrawingContext dc, double w, double h, double t)
        {
            CachedGround(dc, w, h, Color.FromRgb(0x2b, 0x1a, 0x3d));
            Motes(dc, w, h, t, Accent, 12, 41, new Rect(0.5, 0.05, 0.48, 0.9), 0.05, 0.005, 0.4);

            double r = Math.Min(h * 0.34, w * 0.2);
            var c = new Point(w * 0.735, h * 0.53);
            EnsureWedges(r);

            double angle = Angle(t);
            double speed = Turn(Angle(t - 0.01), Angle(t + 0.01)) / 0.02;
            int prize = Celebration(t, out double dp);

            // Shadow on the wall, down and right of the lamp, and a soft pool of the card's hue.
            dc.DrawEllipse(Fill(Shade, 0.22), null, new Point(c.X + r * 0.09, c.Y + r * 0.13), r * 1.2, r * 1.2);
            dc.DrawEllipse(Fill(Shade, 0.32), null, new Point(c.X + r * 0.045, c.Y + r * 0.065), r * 1.17, r * 1.17);
            Glow(dc, c, r * 1.5, Accent, prize >= 0 && dp < 1.4 ? 0.26 : 0.15);

            // The rim (static): a velvet band the bulbs sit on.
            dc.PushTransform(new TranslateTransform(c.X, c.Y));
            dc.DrawDrawing(Layer("rim", w, h, d => PaintRim(d, r)));
            dc.Pop();

            // The disc turns as one cached drawing; at speed, ghosts of it trail behind.
            var disc = Layer("disc", w, h, d => PaintDisc(d, r));
            dc.PushTransform(new MatrixTransform(Rot(angle, c)));
            dc.DrawDrawing(disc);
            if (prize >= 0 && dp < GlowGone) PaintLanded(dc, r, prize, dp);
            dc.Pop();
            double blur = Math.Clamp((Math.Abs(speed) - 260) / 700, 0, 1);
            if (blur > 0)
            {
                double[] ghost = { 0.34, 0.22, 0.12 };
                for (int i = 0; i < ghost.Length; i++)
                {
                    double lag = Math.Min(Math.Abs(speed) * 0.011, 11) * (i + 1) * Math.Sign(speed);
                    dc.PushTransform(new MatrixTransform(Rot(angle - lag, c)));
                    dc.PushOpacity(ghost[i] * blur);
                    dc.DrawDrawing(disc);
                    dc.Pop();
                    dc.Pop();
                }
                // A smear of light round the band the icons ride on.
                var smear = new RadialGradientBrush();
                smear.Center = smear.GradientOrigin = new Point(0.5, 0.5);
                smear.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 0.4));
                smear.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(70 * blur), 255, 240, 250), 0.66));
                smear.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 0.92));
                smear.Freeze();
                dc.DrawEllipse(smear, null, c, r, r);
            }

            // The lamp's light and the hub do not turn.
            dc.PushTransform(new TranslateTransform(c.X, c.Y));
            dc.DrawDrawing(Layer("lamp", w, h, d => PaintLamp(d, r)));
            dc.Pop();
            if (_discClip != null && blur == 0)
            {
                dc.PushTransform(new TranslateTransform(c.X, c.Y));
                Sheen(dc, _discClip, new Rect(-r, -r, r * 2, r * 2), t, 6.5, 2.2, 0.55);
                dc.Pop();
            }

            PaintBulbs(dc, c, r, t, angle, prize, dp);
            PaintPointer(dc, c, r, t, prize, dp);
            if (prize >= 0 && dp < PopHome + 0.2) PaintPop(dc, c, r, prize, dp);
            Twinkles(dc, w, h, t, Gold, 4, 43, new Rect(0.52, 0.06, 0.42, 0.86), 0.026);
        }

        private static Matrix Rot(double degrees, Point c)
        {
            var m = Matrix.Identity;
            m.Rotate(degrees);
            m.Translate(c.X, c.Y);
            return m;
        }

        private void EnsureWedges(double r)
        {
            if (_wedges != null && Math.Abs(_wedgeR - r) < 0.01) return;
            _wedgeR = r;
            _wedges = new Geometry[8];
            for (int i = 0; i < 8; i++) _wedges[i] = Wedge(i, r);
            var clip = new EllipseGeometry(new Point(0, 0), r, r);
            clip.Freeze();
            _discClip = clip;
        }

        private static Geometry Wedge(int i, double r)
        {
            double s = i * Math.PI / 4, e = s + Math.PI / 4;
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                ctx.BeginFigure(new Point(0, 0), true, true);
                ctx.LineTo(new Point(Math.Cos(s) * r, Math.Sin(s) * r), true, false);
                ctx.ArcTo(new Point(Math.Cos(e) * r, Math.Sin(e) * r), new Size(r, r), 0, false, SweepDirection.Clockwise, true, false);
            }
            g.Freeze();
            return g;
        }

        /// <summary>Where slice <paramref name="i"/>'s prize sits (disc frame) and the turn that stands it upright.</summary>
        private static (Point At, double Turn) IconSeat(int i, double r)
        {
            double a = (i + 0.5) * 45;
            double rad = a * Math.PI / 180;
            return (new Point(Math.Cos(rad) * r * 0.61, Math.Sin(rad) * r * 0.61), a + 90);
        }

        private const double IconScale = 0.21; // icon radius in disc radii

        private void PaintRim(DrawingContext d, double r)
        {
            var band = new RadialGradientBrush { MappingMode = BrushMappingMode.Absolute, Center = new Point(0, 0), GradientOrigin = new Point(-r * 0.3, -r * 0.4), RadiusX = r * 1.18, RadiusY = r * 1.18 };
            band.GradientStops.Add(new GradientStop(Color.FromRgb(0x5a, 0x2a, 0x56), 0.8));
            band.GradientStops.Add(new GradientStop(Color.FromRgb(0x2a, 0x12, 0x2e), 1));
            band.Freeze();
            d.DrawEllipse(band, null, new Point(0, 0), r * 1.16, r * 1.16);
            d.DrawEllipse(null, Bevel(r * 0.03), new Point(0, 0), r * 1.145, r * 1.145);
            d.DrawEllipse(null, Stroke(Color.FromRgb(0xc9, 0x93, 0x3a), r * 0.016), new Point(0, 0), r * 1.16, r * 1.16);
        }

        private void PaintDisc(DrawingContext d, double r)
        {
            for (int i = 0; i < 8; i++)
            {
                var hue = SliceHues[i];
                var fill = new RadialGradientBrush { MappingMode = BrushMappingMode.Absolute, Center = new Point(0, 0), GradientOrigin = new Point(0, 0), RadiusX = r, RadiusY = r };
                fill.GradientStops.Add(new GradientStop(Mix(hue, Ink, 0.4), 0.15));
                fill.GradientStops.Add(new GradientStop(hue, 0.78));
                fill.GradientStops.Add(new GradientStop(Mix(hue, Colors.White, i == Jackpot ? 0.45 : 0.18), 1));
                fill.Freeze();
                d.DrawGeometry(fill, null, _wedges![i]);
                if (i == Jackpot)
                {
                    // A sunburst inside the gold slice.
                    d.PushClip(_wedges[i]);
                    var ray = Stroke(Colors.White, r * 0.03, 0.35);
                    for (int k = 0; k < 7; k++)
                    {
                        double a = (i * 45 + 4 + k * 6.2) * Math.PI / 180;
                        d.DrawLine(ray, new Point(Math.Cos(a) * r * 0.25, Math.Sin(a) * r * 0.25), new Point(Math.Cos(a) * r, Math.Sin(a) * r));
                    }
                    d.Pop();
                }
            }
            // Dividers and the pegs the pointer catches on.
            var div = Stroke(Gold, r * 0.016, 0.85);
            var divShade = Stroke(Ink, r * 0.028, 0.45);
            for (int i = 0; i < 8; i++)
            {
                double a = i * Math.PI / 4;
                var p0 = new Point(Math.Cos(a) * r * 0.22, Math.Sin(a) * r * 0.22);
                var p1 = new Point(Math.Cos(a) * r, Math.Sin(a) * r);
                d.DrawLine(divShade, p0, p1);
                d.DrawLine(div, p0, p1);
            }
            for (int i = 0; i < 8; i++)
            {
                double a = i * Math.PI / 4;
                var peg = new Point(Math.Cos(a) * r * 0.93, Math.Sin(a) * r * 0.93);
                d.DrawEllipse(Fill(Shade, 0.4), null, new Point(peg.X + r * 0.01, peg.Y + r * 0.015), r * 0.034, r * 0.034);
                d.DrawEllipse(Fill(Color.FromRgb(0xff, 0xd3, 0x6a)), Bevel(r * 0.012), peg, r * 0.03, r * 0.03);
            }
            // Each slice's prize, standing up toward the rim.
            for (int i = 0; i < 8; i++) DrawIcon(d, i, IconSeat(i, r), r * IconScale);
        }

        private static void DrawIcon(DrawingContext d, int i, (Point At, double Turn) seat, double size)
        {
            var m = Matrix.Identity;
            m.Scale(size, size);
            m.Rotate(seat.Turn);
            m.Translate(seat.At.X, seat.At.Y);
            d.PushTransform(new MatrixTransform(m));
            d.DrawDrawing(Icons[i]);
            d.Pop();
        }

        private void PaintLamp(DrawingContext d, double r)
        {
            var lamp = new RadialGradientBrush();
            lamp.Center = lamp.GradientOrigin = new Point(0.3, 0.28);
            lamp.RadiusX = lamp.RadiusY = 0.9;
            lamp.GradientStops.Add(new GradientStop(Color.FromArgb(0x48, 255, 255, 255), 0));
            lamp.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, 255, 255, 255), 0.45));
            lamp.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, 0, 0, 0), 0.62));
            lamp.GradientStops.Add(new GradientStop(Color.FromArgb(0x68, 0, 0, 0), 1));
            lamp.Freeze();
            d.DrawEllipse(lamp, null, new Point(0, 0), r, r);
            d.DrawEllipse(null, Stroke(Gold, r * 0.022), new Point(0, 0), r, r);
            d.DrawEllipse(null, Bevel(r * 0.014), new Point(0, 0), r * 0.995, r * 0.995);
            // The hub: a raised boss, a gold ring, a cap with a glint.
            d.DrawEllipse(Fill(Shade, 0.4), null, new Point(r * 0.03, r * 0.045), r * 0.22, r * 0.22);
            var boss = new RadialGradientBrush(Color.FromRgb(0x3a, 0x2c, 0x5c), Color.FromRgb(0x16, 0x12, 0x2a)) { GradientOrigin = new Point(0.35, 0.3) };
            boss.Freeze();
            d.DrawEllipse(boss, null, new Point(0, 0), r * 0.21, r * 0.21);
            d.DrawEllipse(null, Bevel(r * 0.04), new Point(0, 0), r * 0.19, r * 0.19);
            d.DrawEllipse(null, Stroke(Color.FromRgb(0xc9, 0x93, 0x3a), r * 0.02), new Point(0, 0), r * 0.13, r * 0.13);
            var cap = new RadialGradientBrush(Color.FromRgb(0xff, 0xf3, 0xc4), Color.FromRgb(0xd8, 0x9a, 0x2a)) { GradientOrigin = new Point(0.35, 0.3) };
            cap.Freeze();
            d.DrawEllipse(cap, null, new Point(0, 0), r * 0.085, r * 0.085);
            d.DrawEllipse(null, Bevel(r * 0.018), new Point(0, 0), r * 0.08, r * 0.08);
            d.DrawEllipse(Fill(Colors.White, 0.75), null, new Point(-r * 0.03, -r * 0.03), r * 0.022, r * 0.022);
        }

        /// <summary>The landed slice lights up; once its prize has popped out, an empty socket shows.</summary>
        private void PaintLanded(DrawingContext dc, double r, int prize, double dp)
        {
            double pulse = dp < 1.4 ? 0.55 + 0.45 * Math.Cos(dp * 15) : 1;
            double fade = dp < 1.9 ? 1 : Math.Clamp(1 - (dp - 1.9) / (GlowGone - 1.9), 0, 1);
            dc.PushOpacity(0.32 * pulse * fade);
            dc.DrawGeometry(Fill(Colors.White), null, _wedges![prize]);
            dc.Pop();
            dc.DrawGeometry(null, Stroke(Color.FromRgb(0xff, 0xf6, 0xcf), r * 0.035, 0.9 * fade), _wedges[prize]);

            if (dp > PopOut && dp < PopHome)
            {
                var seat = IconSeat(prize, r);
                double k = Math.Clamp((dp - PopOut) / 0.12, 0, 1) * Math.Clamp((PopHome - dp) / 0.12, 0, 1);
                double sr = r * IconScale * 1.05;
                var hue = SliceHues[prize];
                dc.PushOpacity(k);
                dc.DrawEllipse(Fill(Mix(hue, Ink, 0.55)), null, seat.At, sr, sr);
                dc.DrawEllipse(null, Bevel(sr * 0.14, pressed: true), seat.At, sr * 0.93, sr * 0.93);
                dc.Pop();
            }
        }

        private void PaintBulbs(DrawingContext dc, Point c, double r, double t, double angle, int prize, double dp)
        {
            var on = Fill(Color.FromRgb(0xff, 0xf6, 0xcf));
            var off = Fill(Color.FromRgb(0x7a, 0x5a, 0x30));
            var halo = Fill(Gold, 0.24);
            bool landing = prize >= 0 && dp < 1.3;
            bool allOn = landing && Math.Floor(dp * 8) % 2 == 0;
            double st = Mod(t + Shift, Loop);
            bool spinning = st >= WindEnd && st < LandAt;
            int step = spinning ? (int)Math.Floor(angle / 22.5) : (int)Math.Floor(t * 4);
            for (int i = 0; i < 16; i++)
            {
                double a = i * Math.PI / 8 + Math.PI / 16;
                var bp = new Point(c.X + Math.Cos(a) * r * 1.08, c.Y + Math.Sin(a) * r * 1.08);
                bool lit = landing ? allOn : spinning ? Mod(i - step, 3) == 0 : Mod(i + step, 4) == 0;
                if (lit) dc.DrawEllipse(halo, null, bp, r * (landing ? 0.11 : 0.075), r * (landing ? 0.11 : 0.075));
                dc.DrawEllipse(lit ? on : off, null, bp, r * 0.03, r * 0.03);
                if (lit) dc.DrawEllipse(Fill(Colors.White, 0.8), null, new Point(bp.X - r * 0.008, bp.Y - r * 0.008), r * 0.011, r * 0.011);
            }
        }

        private static readonly Geometry PointerShape = MakePointer();
        private static readonly Pen PointerPen = InkPen(0.06);

        private static Geometry MakePointer()
        {
            // Unit flapper hanging from its pivot at (0, 0), the tip down at (0, 1).
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                ctx.BeginFigure(new Point(0, 1), true, true);
                ctx.LineTo(new Point(-0.32, 0.05), true, true);
                ctx.ArcTo(new Point(0.32, 0.05), new Size(0.32, 0.32), 0, true, SweepDirection.Clockwise, true, true);
            }
            g.Freeze();
            return g;
        }

        private void PaintPointer(DrawingContext dc, Point c, double r, double t, int prize, double dp)
        {
            double flap = Flap(t);
            double pop = 1;
            if (prize >= 0 && dp < 1.2)
            {
                flap += 11 * Math.Sin(dp * 20) * Math.Exp(-dp * 5);
                pop = 1 + 0.16 * Math.Sin(Math.Min(dp, 0.5) / 0.5 * Math.PI) ;
            }
            double len = r * 0.34;
            var pivot = new Point(c.X, c.Y - r * 1.2);
            var pm = Matrix.Identity;
            pm.Scale(len * pop, len * pop);
            pm.Rotate(flap);
            pm.Translate(pivot.X, pivot.Y);
            var sm = pm;
            sm.Translate(r * 0.04, r * 0.06);
            dc.PushTransform(new MatrixTransform(sm));
            dc.DrawGeometry(Fill(Shade, 0.42), null, PointerShape);
            dc.Pop();
            var body = new LinearGradientBrush(Color.FromRgb(0xff, 0xf0, 0xb8), Color.FromRgb(0xd9, 0x92, 0x22), new Point(0, 0), new Point(1, 1));
            body.Freeze();
            dc.PushTransform(new MatrixTransform(pm));
            dc.DrawGeometry(body, PointerPen, PointerShape);
            dc.Pop();
            // The bolt it swings on.
            dc.DrawEllipse(Fill(Color.FromRgb(0x2a, 0x1c, 0x40)), null, pivot, r * 0.08, r * 0.08);
            dc.DrawEllipse(null, Bevel(r * 0.025), pivot, r * 0.075, r * 0.075);
            dc.DrawEllipse(Fill(Gold), null, pivot, r * 0.035, r * 0.035);
        }

        /// <summary>The prize leaves its socket toward the viewer, holds with sparks, and goes home.</summary>
        private void PaintPop(DrawingContext dc, Point c, double r, int prize, double dp)
        {
            if (dp < PopOut) return;
            var socket = new Point(c.X, c.Y - r * 0.61);
            var target = new Point(c.X - r * 0.62, c.Y - r * 0.44);
            double size = r * IconScale;

            Point At(double s)
            {
                if (s < PopHeld)
                {
                    double e = BackOut((s - PopOut) / (PopHeld - PopOut), 2.2);
                    return new Point(socket.X + (target.X - socket.X) * e, socket.Y + (target.Y - socket.Y) * e - Math.Sin(Math.Clamp(e, 0, 1) * Math.PI) * r * 0.12);
                }
                if (s < PopBack) return new Point(target.X, target.Y + Math.Sin((s - PopHeld) * 4.2) * r * 0.025);
                double k = Math.Clamp((s - PopBack) / (PopHome - PopBack), 0, 1);
                k = k * k * (3 - 2 * k);
                return new Point(target.X + (socket.X - target.X) * k, target.Y + (socket.Y - target.Y) * k);
            }

            double scale;
            if (dp < PopHeld) scale = 1 + 1.5 * BackOut((dp - PopOut) / (PopHeld - PopOut), 2.2);
            else if (dp < PopBack) scale = 2.5 + Math.Sin((dp - PopHeld) * 6) * 0.06;
            else
            {
                double k = Math.Clamp((dp - PopBack) / (PopHome - PopBack), 0, 1);
                scale = 2.5 - 1.5 * k * k * (3 - 2 * k);
            }

            // Sparks thrown from where the prize was as it came out.
            if (Particles) PaintBurst(dc, r, prize, dp, At);

            if (dp >= PopHome) return;
            var p = At(dp);
            double lift = (scale - 1) / 1.5;
            dc.DrawEllipse(Fill(Shade, 0.3 * lift), null, new Point(p.X + size * scale * 0.18, p.Y + size * scale * 0.32), size * scale * 0.95, size * scale * 0.8);
            double wob = dp > PopHeld && dp < PopBack ? Math.Sin((dp - PopHeld) * 5) * 5 : 0;
            var m = Matrix.Identity;
            m.Scale(size * scale, size * scale);
            m.Rotate(wob);
            m.Translate(p.X, p.Y);
            dc.PushTransform(new MatrixTransform(m));
            dc.DrawDrawing(Icons[prize]);
            dc.Pop();
            if (dp < PopHeld + 0.4) Spark(dc, new Point(p.X - size * scale * 0.55, p.Y - size * scale * 0.6), size * 0.9 * scale * 0.5, Colors.White, 1 - (dp - PopOut) / (PopHeld + 0.4 - PopOut), dp * 3);
        }

        private void PaintBurst(DrawingContext dc, double r, int prize, double dp, Func<double, Point> at)
        {
            int n = prize == Jackpot ? 22 : 14;
            for (int i = 0; i < n; i++)
            {
                double te = 0.14 + Hash(prize, i, 1) * 0.22;
                double life = dp - te;
                double span = 0.9 + Hash(prize, i, 2) * 0.4;
                if (life <= 0 || life > span) continue;
                var o = at(te);
                double dir = -Math.PI / 2 + (Hash(prize, i, 3) - 0.5) * Math.PI * 1.7;
                double v = r * (1.3 + Hash(prize, i, 4) * 1.3);
                double g = r * 2.8;
                var p = new Point(o.X + Math.Cos(dir) * v * life, o.Y + Math.Sin(dir) * v * life + 0.5 * g * life * life);
                double a = Math.Clamp(1 - life / span, 0, 1);
                a = Math.Min(1, a * 1.6);
                double s = r * (0.05 + Hash(prize, i, 5) * 0.035);
                if (i % 4 == 3) { Spark(dc, p, s * 1.4, i % 8 == 3 ? Colors.White : Gold, a, life * 4); continue; }
                Drawing piece = prize switch
                {
                    Jackpot => Coin,
                    RoomService => Leaf,
                    Double => StarBit,
                    HeadEmpty => Bubble,
                    _ => GemBit,
                };
                double spin = life * (4 + Hash(prize, i, 6) * 6) + i;
                var m = Matrix.Identity;
                // Coins flip as they fly; the rest tumble.
                if (prize == Jackpot) m.Scale(s * Math.Max(0.2, Math.Abs(Math.Cos(spin))), s);
                else { m.Scale(s, s); m.Rotate(spin * 57.3 * 0.6); }
                m.Translate(p.X, p.Y);
                dc.PushTransform(new MatrixTransform(m));
                dc.PushOpacity(a);
                dc.DrawDrawing(piece);
                dc.Pop();
                dc.Pop();
            }
        }

        // ---- the prize icons: frozen unit drawings, radius 1, "up" is -y --------------------------

        private static readonly Drawing[] Icons = BuildIcons();
        private static readonly Drawing GemBit = Unit(d => Gem(d, new Point(0, 0), 1, Color.FromRgb(0xdf, 0xf8, 0xff)));
        private static readonly Drawing StarBit = Unit(d => Star(d, new Point(0, 0), 1, Color.FromRgb(0xff, 0xf3, 0xc4)));
        private static readonly Drawing Coin = Unit(d =>
        {
            d.DrawEllipse(new SolidColorBrush(Color.FromRgb(0xb8, 0x7a, 0x14)), null, new Point(0, 0), 1, 1);
            d.DrawEllipse(new SolidColorBrush(Color.FromRgb(0xff, 0xd0, 0x4a)), null, new Point(-0.06, -0.06), 0.86, 0.86);
            d.DrawEllipse(null, new Pen(new SolidColorBrush(Color.FromRgb(0xc9, 0x8a, 0x1c)), 0.12), new Point(-0.06, -0.06), 0.55, 0.55);
            d.DrawEllipse(new SolidColorBrush(Color.FromArgb(0xc0, 255, 255, 255)), null, new Point(-0.35, -0.38), 0.16, 0.16);
        });
        private static readonly Drawing Leaf = Unit(d => LeafShape(d, new Point(0, 0.8), 1.7, 0, Color.FromRgb(0x4f, 0xe3, 0x9a)));
        private static readonly Drawing Bubble = Unit(d =>
        {
            d.DrawEllipse(new SolidColorBrush(Color.FromArgb(0x40, 0xe8, 0xdc, 0xff)), new Pen(new SolidColorBrush(Color.FromArgb(0xe0, 255, 255, 255)), 0.16), new Point(0, 0), 0.9, 0.9);
            d.DrawEllipse(Brushes.White, null, new Point(-0.35, -0.35), 0.18, 0.18);
        });

        private static Drawing Unit(Action<DrawingContext> paint)
        {
            var g = new DrawingGroup();
            using (var d = g.Open()) paint(d);
            g.Freeze();
            return g;
        }

        private static Pen InkPen(double th)
        {
            var p = new Pen(new SolidColorBrush(Ink), th) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            p.Freeze();
            return p;
        }

        private static Geometry Poly(params Point[] pts)
        {
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                ctx.BeginFigure(pts[0], true, true);
                for (int i = 1; i < pts.Length; i++) ctx.LineTo(pts[i], true, true);
            }
            g.Freeze();
            return g;
        }

        private static Brush B(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

        /// <summary>A cut gem, lit from the top left: table, crown and pavilion facets, inked outline.</summary>
        private static void Gem(DrawingContext d, Point c, double s, Color tint)
        {
            Point P(double x, double y) => new(c.X + x * s, c.Y + y * s);
            var outline = Poly(P(-1, -0.3), P(-0.55, -0.78), P(0.55, -0.78), P(1, -0.3), P(0, 0.95));
            d.DrawGeometry(B(Color.FromArgb(0x60, 4, 4, 12)), null, Poly(P(-0.92, -0.18), P(-0.47, -0.66), P(0.63, -0.66), P(1.08, -0.18), P(0.08, 1.07)));
            d.DrawGeometry(B(Mix(tint, Colors.White, 0.85)), null, Poly(P(-0.55, -0.78), P(0.55, -0.78), P(0.3, -0.3), P(-0.3, -0.3)));
            d.DrawGeometry(B(Mix(tint, Colors.White, 0.55)), null, Poly(P(-1, -0.3), P(-0.55, -0.78), P(-0.3, -0.3)));
            d.DrawGeometry(B(Mix(tint, Color.FromRgb(0x6a, 0x4a, 0xc8), 0.3)), null, Poly(P(1, -0.3), P(0.55, -0.78), P(0.3, -0.3)));
            d.DrawGeometry(B(tint), null, Poly(P(-1, -0.3), P(-0.3, -0.3), P(0, 0.95)));
            d.DrawGeometry(B(Mix(tint, Color.FromRgb(0x8a, 0x6a, 0xe0), 0.25)), null, Poly(P(-0.3, -0.3), P(0.3, -0.3), P(0, 0.95)));
            d.DrawGeometry(B(Mix(tint, Color.FromRgb(0x4a, 0x2a, 0x9a), 0.5)), null, Poly(P(0.3, -0.3), P(1, -0.3), P(0, 0.95)));
            d.DrawGeometry(null, InkPen(0.13 * s), outline);
            d.DrawLine(InkPen(0.06 * s), P(-1, -0.3), P(1, -0.3));
            d.DrawEllipse(B(Colors.White), null, P(-0.32, -0.6), 0.1 * s, 0.1 * s);
        }

        private static Geometry StarGeometry(Point c, double ro, double ri, int points, double turn)
        {
            var pts = new Point[points * 2];
            for (int i = 0; i < points * 2; i++)
            {
                double a = -Math.PI / 2 + turn + i * Math.PI / points;
                double rr = i % 2 == 0 ? ro : ri;
                pts[i] = new Point(c.X + Math.Cos(a) * rr, c.Y + Math.Sin(a) * rr);
            }
            return Poly(pts);
        }

        private static void Star(DrawingContext d, Point c, double s, Color fill)
        {
            var g = StarGeometry(c, s, s * 0.46, 5, 0);
            d.DrawGeometry(B(Color.FromArgb(0x60, 4, 4, 12)), null, StarGeometry(new Point(c.X + s * 0.1, c.Y + s * 0.13), s, s * 0.46, 5, 0));
            d.DrawGeometry(B(fill), InkPen(0.14 * s), g);
            d.DrawGeometry(B(Color.FromArgb(0xa0, 255, 255, 255)), null, StarGeometry(new Point(c.X - s * 0.08, c.Y - s * 0.1), s * 0.42, s * 0.2, 5, 0));
        }

        private static void LeafShape(DrawingContext d, Point baseAt, double len, double turnDeg, Color hue)
        {
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                ctx.BeginFigure(new Point(0, 0), true, true);
                ctx.QuadraticBezierTo(new Point(len * 0.45, -len * 0.45), new Point(0, -len), true, true);
                ctx.QuadraticBezierTo(new Point(-len * 0.45, -len * 0.45), new Point(0, 0), true, true);
            }
            g.Freeze();
            var m = Matrix.Identity;
            m.Rotate(turnDeg);
            m.Translate(baseAt.X, baseAt.Y);
            d.PushTransform(new MatrixTransform(m));
            d.DrawGeometry(B(hue), InkPen(0.09 * len), g);
            d.DrawLine(new Pen(B(Mix(hue, Ink, 0.45)), 0.05 * len), new Point(0, -len * 0.1), new Point(0, -len * 0.82));
            d.DrawEllipse(B(Color.FromArgb(0x90, 255, 255, 255)), null, new Point(-len * 0.1, -len * 0.62), len * 0.06, len * 0.12);
            d.Pop();
        }

        private static Drawing[] BuildIcons()
        {
            var ice = Color.FromRgb(0xdf, 0xf8, 0xff);
            var icons = new Drawing[8];
            // 15 SP: one gem.
            icons[Pocket] = Unit(d => Gem(d, new Point(0, 0.05), 0.62, ice));
            // Head Empty: a swirl.
            icons[HeadEmpty] = Unit(d =>
            {
                var g = new StreamGeometry();
                using (var ctx = g.Open())
                {
                    const double turns = 2.3;
                    ctx.BeginFigure(new Point(0, 0), false, false);
                    for (int i = 1; i <= 90; i++)
                    {
                        double u = i / 90.0, a = u * turns * Math.PI * 2;
                        double rr = 0.06 + 0.78 * u;
                        ctx.LineTo(new Point(Math.Cos(a) * rr, Math.Sin(a) * rr), true, true);
                    }
                }
                g.Freeze();
                d.DrawGeometry(null, new Pen(B(Color.FromArgb(0x60, 4, 4, 12)), 0.3) { LineJoin = PenLineJoin.Round }, g.Clone());
                d.PushTransform(new TranslateTransform(-0.06, -0.08));
                d.DrawGeometry(null, InkPen(0.3), g);
                var white = new Pen(B(Color.FromRgb(0xf6, 0xee, 0xff)), 0.16) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
                white.Freeze();
                d.DrawGeometry(null, white, g);
                d.Pop();
            });
            // 30 SP: two gems.
            icons[Good] = Unit(d =>
            {
                Gem(d, new Point(-0.42, 0.1), 0.48, ice);
                Gem(d, new Point(0.42, -0.02), 0.48, ice);
            });
            // Room Service: a little potted plant (a decoration for the room).
            icons[RoomService] = Unit(d =>
            {
                var leaf = Color.FromRgb(0x4f, 0xe3, 0x9a);
                LeafShape(d, new Point(0, 0.2), 0.95, -38, Mix(leaf, Ink, 0.12));
                LeafShape(d, new Point(0, 0.2), 0.95, 38, Mix(leaf, Ink, 0.12));
                LeafShape(d, new Point(0, 0.22), 1.05, 0, leaf);
                var pot = Poly(new Point(-0.46, 0.2), new Point(0.46, 0.2), new Point(0.34, 0.92), new Point(-0.34, 0.92));
                d.DrawGeometry(B(Color.FromArgb(0x60, 4, 4, 12)), null, Poly(new Point(-0.38, 0.3), new Point(0.54, 0.3), new Point(0.42, 1.02), new Point(-0.26, 1.02)));
                var clay = new LinearGradientBrush(Color.FromRgb(0xff, 0xa8, 0x78), Color.FromRgb(0xc2, 0x5a, 0x34), new Point(0, 0), new Point(1, 0));
                clay.Freeze();
                d.DrawGeometry(clay, InkPen(0.09), pot);
                d.DrawRoundedRectangle(B(Color.FromRgb(0xe8, 0x82, 0x5a)), InkPen(0.09), new Rect(-0.54, 0.12, 1.08, 0.24), 0.06, 0.06);
                d.DrawLine(new Pen(B(Color.FromArgb(0x90, 255, 255, 255)), 0.06), new Point(-0.44, 0.18), new Point(0.1, 0.18));
            });
            // 60 SP: three gems.
            icons[KeepChange] = Unit(d =>
            {
                Gem(d, new Point(-0.5, 0.28), 0.4, ice);
                Gem(d, new Point(0.5, 0.28), 0.4, ice);
                Gem(d, new Point(0, -0.18), 0.48, ice);
            });
            // Seeing Double: twin stars.
            icons[Double] = Unit(d =>
            {
                Star(d, new Point(-0.3, 0.12), 0.62, Color.FromRgb(0xf4, 0xe6, 0xff));
                Star(d, new Point(0.3, -0.1), 0.62, Color.FromRgb(0xff, 0xf3, 0xc4));
            });
            // 150 SP: a big pink gem flanked by two small ones.
            icons[Spoiled] = Unit(d =>
            {
                Gem(d, new Point(-0.66, 0.38), 0.32, ice);
                Gem(d, new Point(0.66, 0.38), 0.32, ice);
                Gem(d, new Point(0, -0.02), 0.78, Color.FromRgb(0xff, 0xd6, 0xee));
            });
            // Jackpot: a ruby crown over a star burst.
            icons[Jackpot] = Unit(d =>
            {
                d.DrawGeometry(B(Color.FromRgb(0xff, 0xf3, 0xc8)), InkPen(0.05), StarGeometry(new Point(0, 0), 1.05, 0.74, 12, 0.13));
                var crown = Poly(new Point(-0.62, 0.32), new Point(-0.74, -0.42), new Point(-0.32, -0.04), new Point(0, -0.62), new Point(0.32, -0.04), new Point(0.74, -0.42), new Point(0.62, 0.32));
                d.DrawGeometry(B(Color.FromArgb(0x70, 4, 4, 12)), null, Poly(new Point(-0.52, 0.44), new Point(-0.64, -0.3), new Point(-0.22, 0.08), new Point(0.1, -0.5), new Point(0.42, 0.08), new Point(0.84, -0.3), new Point(0.72, 0.62), new Point(-0.52, 0.62)));
                var ruby = new LinearGradientBrush(Color.FromRgb(0xff, 0x6a, 0x9a), Color.FromRgb(0xa8, 0x14, 0x48), new Point(0, 0), new Point(1, 1));
                ruby.Freeze();
                d.DrawGeometry(ruby, InkPen(0.09), crown);
                var band = new LinearGradientBrush(Color.FromRgb(0xff, 0xf0, 0xb8), Color.FromRgb(0xc9, 0x8a, 0x1c), new Point(0, 0), new Point(0, 1));
                band.Freeze();
                d.DrawRoundedRectangle(band, InkPen(0.09), new Rect(-0.66, 0.26, 1.32, 0.3), 0.06, 0.06);
                foreach (var (x, y) in new[] { (-0.74, -0.46), (0.0, -0.66), (0.74, -0.46) })
                {
                    d.DrawEllipse(B(Color.FromRgb(0xff, 0xd0, 0x4a)), InkPen(0.07), new Point(x, y), 0.13, 0.13);
                    d.DrawEllipse(B(Colors.White), null, new Point(x - 0.04, y - 0.04), 0.04, 0.04);
                }
                d.DrawEllipse(B(Color.FromRgb(0x5f, 0xe3, 0xff)), InkPen(0.05), new Point(0, 0.41), 0.09, 0.09);
                d.DrawLine(new Pen(B(Color.FromArgb(0x90, 255, 255, 255)), 0.07), new Point(-0.5, -0.2), new Point(-0.38, 0.18));
            });
            return icons;
        }
    }
}
