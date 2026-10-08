using System;

using Avalonia.Media;

namespace ConditioningControlPanel.Avalonia.Controls.Billboard.Scenes
{
    /// <summary>
    /// The share-invite card: a paper pass in the card's hue with a torn-off stub, a row of code
    /// sockets where one glyph at a time flips up and shimmers, seven day pips on the stub filling
    /// in (a free week), a second pass tucked behind (one a month), a glint crossing it, and a few
    /// sparkles. The pass itself is a cached layer; a frame only moves it and draws what changes.
    /// </summary>
    public sealed class InviteArtView : BillboardVectorArt
    {
        private const int Slots = 6;
        private const double FlipSeconds = 1.7;

        // 3x5 pixel glyphs, row by row from the top (bit 14 = top left). Read as a code, never a word.
        private static readonly int[] Glyphs =
        {
            0b010_101_111_101_101, // A
            0b111_001_010_010_010, // 7
            0b101_110_100_110_101, // K
            0b111_001_011_001_111, // 3
            0b101_101_010_101_101, // X
            0b111_101_111_001_111, // 9
        };

        private static readonly Color Ink = Color.FromRgb(0x2a, 0x16, 0x0c);
        private Geometry? _shape;
        private double _shapeW;

        protected override void Paint(DrawingContext dc, double w, double h, double t)
        {
            CachedGround(dc, w, h, Deep(Accent));

            double tw = Math.Min(w * 0.36, h * 0.9), th = tw / 2.1;
            double cx = w * 0.745, cy = h * 0.5;
            var body = new Rect(-tw / 2, -th / 2, tw, th);
            if (_shape == null || _shapeW != tw) { _shape = TicketShape(tw, th); _shapeW = tw; }
            var ticketShape = _shape;

            // Far motes first: they sit behind the passes.
            Motes(dc, w, h, t, Accent, 14, 3, new Rect(0.5, 0.1, 0.48, 0.85), 0.06, 0.006, 0.45);

            // Entrance: the passes rise in with a small overshoot, the back one a beat later.
            double kIn = BackOut(t / 0.85), kBack = BackOut((t - 0.15) / 0.85);
            double bob = Bob(t, h * 0.012), sway = Math.Sin(t * 0.8) * 1.2;

            // The pass behind: darker, turned the other way, moving at half the bob (parallax).
            {
                double ox = -tw * 0.07, oy = -th * 0.22 + bob * 0.5 + (1 - kBack) * h * 0.3;
                var m = Pose(cx + ox, cy + oy, 7 + sway * 0.5 + (1 - kBack) * 12);
                dc.PushOpacity(Math.Clamp(kBack, 0, 1));
                DrawShadow(dc, ticketShape, cx + ox, cy + oy, 7 + sway * 0.5, h * 0.02);
                dc.PushTransform(new MatrixTransform(m));
                dc.DrawDrawing(Layer("pass", w, h, d => PaintPass(d, tw, th, ticketShape)));
                dc.DrawGeometry(Fill(Ink, 0.5), null, ticketShape);
                dc.Pop();
                dc.Pop();
            }

            // The pass in front.
            double lift = h * 0.022 - bob * 0.35;
            double py = cy + bob + (1 - kIn) * h * 0.35, rot = -7 + sway + (1 - kIn) * -14;
            dc.PushOpacity(Math.Clamp(kIn * 1.4, 0, 1));
            DrawShadow(dc, ticketShape, cx, py, rot, lift);
            dc.PushTransform(new MatrixTransform(Pose(cx, py, rot)));
            dc.DrawDrawing(Layer("pass", w, h, d => PaintPass(d, tw, th, ticketShape)));
            PaintCode(dc, tw, th, t);
            PaintDays(dc, tw, th, t);
            Sheen(dc, ticketShape, body, t, 3.8, 1.9, 0.9);
            dc.Pop();
            dc.Pop();

            // Landing burst, then the slow twinkles and the near motes in front.
            if (t > 0.5 && t < 1.3) Burst(dc, cx, cy + bob, tw, th, (t - 0.5) / 0.8);
            Twinkles(dc, w, h, t, Mix(Accent, Colors.White, 0.35), 5, 11, new Rect(0.52, 0.12, 0.44, 0.76), 0.03);
            Motes(dc, w, h, t, Colors.White, 6, 17, new Rect(0.55, 0.2, 0.42, 0.8), 0.11, 0.009, 0.35);
        }

        private static Matrix Pose(double x, double y, double degrees)
        {
            var m = Matrix.Identity;
            m.Rotate(degrees);
            m.Translate(x, y);
            return m;
        }

        private void DrawShadow(DrawingContext dc, Geometry shape, double x, double y, double degrees, double lift)
        {
            if (lift <= 0) return;
            var ink = Color.FromRgb(4, 4, 12);
            dc.PushTransform(new MatrixTransform(Pose(x + lift * 0.9, y + lift * 1.4, degrees)));
            dc.DrawGeometry(Fill(ink, 0.22), null, shape);
            dc.Pop();
            dc.PushTransform(new MatrixTransform(Pose(x + lift * 0.45, y + lift * 0.7, degrees)));
            dc.DrawGeometry(Fill(ink, 0.3), null, shape);
            dc.Pop();
        }

        /// <summary>The pass outline: a rounded card with two notches where the stub tears off.</summary>
        internal static Geometry TicketShape(double tw, double th)
        {
            double px = tw / 2 - tw * 0.27, nr = th * 0.1;
            var card = new RectangleGeometry(new Rect(-tw / 2, -th / 2, tw, th), th * 0.1, th * 0.1);
            var notches = new GeometryGroup();
            notches.Children.Add(new EllipseGeometry(new Point(px, -th / 2), nr, nr));
            notches.Children.Add(new EllipseGeometry(new Point(px, th / 2), nr, nr));
            var g = new CombinedGeometry(GeometryCombineMode.Exclude, card, notches).GetFlattenedPathGeometry(0.25, ToleranceType.Absolute);
            g.Freeze();
            return g;
        }

        /// <summary>The static pass: paper lit from the top left, the stub, the tear line, a frame and a stamp.</summary>
        private void PaintPass(DrawingContext dc, double tw, double th, Geometry shape)
        {
            double px = tw / 2 - tw * 0.27;
            var paper = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
            paper.GradientStops.Add(new GradientStop(Mix(Accent, Colors.White, 0.32), 0));
            paper.GradientStops.Add(new GradientStop(Accent, 0.48));
            paper.GradientStops.Add(new GradientStop(Mix(Accent, Ink, 0.28), 1));
            paper.Freeze();
            dc.DrawGeometry(paper, null, shape);

            // The stub, a shade darker so it reads as its own piece.
            dc.PushClip(shape);
            dc.DrawRectangle(Fill(Ink, 0.12), null, new Rect(px, -th / 2, tw / 2 - px, th));
            dc.Pop();

            // The lamp's bevel round the edge.
            dc.DrawGeometry(null, Bevel(Math.Max(1, th * 0.03)), shape);

            // Tear line: a column of punched dots between the notches.
            var hole = Fill(Ink, 0.5);
            int dots = 9;
            for (int i = 0; i < dots; i++)
            {
                double y = -th / 2 + th * 0.17 + i * (th * 0.66 / (dots - 1));
                dc.DrawEllipse(hole, null, new Point(px, y), th * 0.018, th * 0.018);
            }

            // A thin frame inside the main part, dashed like a printed border.
            double m = th * 0.1;
            var frame = new Pen(Fill(Ink, 0.32), Math.Max(0.75, th * 0.012)) { DashStyle = new DashStyle(new[] { 3.0, 2.0 }, 0) };
            frame.Freeze();
            dc.DrawRoundedRectangle(null, frame, new Rect(-tw / 2 + m, -th / 2 + m, px - m * 1.4 + tw / 2 - m, th - m * 2), th * 0.05, th * 0.05);

            // A round stamp with a star in it, top left, and a double rule beside it.
            var sc = new Point(-tw / 2 + m + th * 0.2, -th * 0.2);
            double sr = th * 0.12;
            dc.DrawEllipse(null, Stroke(Ink, th * 0.018, 0.5), sc, sr, sr);
            dc.DrawGeometry(Fill(Ink, 0.5), null, Star(sc, sr * 0.68, sr * 0.3));
            double rx0 = sc.X + sr + th * 0.08, rx1 = px - m * 1.6;
            dc.DrawLine(Stroke(Ink, th * 0.014, 0.4), new Point(rx0, sc.Y - th * 0.03), new Point(rx1, sc.Y - th * 0.03));
            dc.DrawLine(Stroke(Ink, th * 0.014, 0.25), new Point(rx0, sc.Y + th * 0.03), new Point(rx0 + (rx1 - rx0) * 0.6, sc.Y + th * 0.03));

            // The code sockets, pressed into the paper.
            foreach (var r in SlotRects(tw, th))
                Plate(dc, r, r.Height * 0.18, Fill(Ink, 0.86), 0, pressed: true);
        }

        private static Rect[] SlotRects(double tw, double th)
        {
            double px = tw / 2 - tw * 0.27, m = th * 0.1;
            double left = -tw / 2 + m * 1.9, right = px - m * 1.9;
            double pitch = (right - left) / Slots, sw = pitch * 0.82, sh = th * 0.3, y = th * 0.1 - sh / 2;
            var rects = new Rect[Slots];
            for (int i = 0; i < Slots; i++) rects[i] = new Rect(left + i * pitch + (pitch - sw) / 2, y, sw, sh);
            return rects;
        }

        /// <summary>The code: every socket shows a dot except one, whose glyph flips up and shimmers.</summary>
        private void PaintCode(DrawingContext dc, double tw, double th, double t)
        {
            var rects = SlotRects(tw, th);
            double u = (t + 0.5) / FlipSeconds;
            int cycle = (int)Math.Floor(u);
            double p = u - cycle;
            int open = (int)(Hash(5, cycle, 1) * Slots) % Slots;
            if (cycle > 0 && open == (int)(Hash(5, cycle - 1, 1) * Slots) % Slots) open = (open + 1) % Slots;
            var cover = Fill(Mix(Accent, Colors.White, 0.35), 0.55);
            var lit = Mix(Accent, Colors.White, 0.7);
            for (int i = 0; i < Slots; i++)
            {
                var r = rects[i];
                var c = new Point(r.X + r.Width / 2, r.Y + r.Height / 2);
                if (i != open)
                {
                    dc.DrawEllipse(cover, null, c, r.Height * 0.09, r.Height * 0.09);
                    continue;
                }
                // Flip: the dot shrinks, the glyph opens up from its middle line; it closes again at the end.
                double open01 = Math.Min(EaseOut(p / 0.16), 1 - EaseOut((p - 0.86) / 0.14));
                if (open01 < 0.5) dc.DrawEllipse(cover, null, c, r.Height * 0.09 * (1 - open01 * 2), r.Height * 0.09 * (1 - open01 * 2));
                if (open01 <= 0.05) continue;
                int glyph = Glyphs[(int)(Hash(9, cycle, 2) * Glyphs.Length) % Glyphs.Length];
                double cell = r.Height * 0.15, gw = cell * 3, gh = cell * 5;
                dc.PushTransform(new ScaleTransform(1, open01, c.X, c.Y));
                for (int row = 0; row < 5; row++)
                    for (int col = 0; col < 3; col++)
                    {
                        if ((glyph >> (14 - (row * 3 + col)) & 1) == 0) continue;
                        // Shimmer: a bright line runs down the glyph once it is open.
                        double run = (p - 0.18) / 0.4 * 6 - row;
                        double shine = Math.Clamp(1 - Math.Abs(run - 0.5), 0, 1);
                        var px = Mix(lit, Colors.White, shine);
                        dc.DrawRectangle(Fill(px), null, new Rect(c.X - gw / 2 + col * cell + cell * 0.08, c.Y - gh / 2 + row * cell + cell * 0.08, cell * 0.84, cell * 0.84));
                    }
                dc.Pop();
                if (p > 0.2 && p < 0.7)
                    Spark(dc, new Point(r.Right - r.Width * 0.12, r.Top + r.Height * 0.12), r.Height * 0.24 * Math.Sin((p - 0.2) / 0.5 * Math.PI), Colors.White, 0.9, p * 2);
            }
        }

        /// <summary>Seven day pips down the stub, filling one by one, then all bright for a beat.</summary>
        private void PaintDays(DrawingContext dc, double tw, double th, double t)
        {
            double px = tw / 2 - tw * 0.27, x = px + (tw / 2 - px) / 2;
            double pitch = th * 0.105, y0 = -pitch * 3, r = th * 0.033;
            double loop = (t % 5.6) / 5.6 * 9; // seven fills, two beats full
            for (int i = 0; i < 7; i++)
            {
                var c = new Point(x, y0 + i * pitch);
                double on = Math.Clamp(loop - i, 0, 1);
                dc.DrawEllipse(Fill(Ink, 0.42), null, c, r, r);
                if (on <= 0) continue;
                double pop = on < 1 ? BackOut(on, 2.2) : 1;
                dc.DrawEllipse(Fill(Mix(Accent, Colors.White, 0.8), 0.95), null, c, r * 0.78 * pop, r * 0.78 * pop);
            }
        }

        /// <summary>Glints flung out from the pass as it lands.</summary>
        private void Burst(DrawingContext dc, double cx, double cy, double tw, double th, double k)
        {
            if (!Particles) return;
            for (int i = 0; i < 9; i++)
            {
                double a = i * Math.PI * 2 / 9 + 0.3, d = (0.55 + 0.35 * EaseOut(k)) * tw * (0.8 + Hash(21, i, 1) * 0.3);
                var p = new Point(cx + Math.Cos(a) * d * 0.72, cy + Math.Sin(a) * d * 0.5);
                Spark(dc, p, th * 0.07 * (1 - k), i % 2 == 0 ? Colors.White : Mix(Accent, Colors.White, 0.4), 1 - k, k * 3);
            }
        }

        private static Geometry Star(Point c, double r, double inner)
        {
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                for (int i = 0; i < 10; i++)
                {
                    double a = -Math.PI / 2 + i * Math.PI / 5, rr = i % 2 == 0 ? r : inner;
                    var p = new Point(c.X + Math.Cos(a) * rr, c.Y + Math.Sin(a) * rr);
                    if (i == 0) ctx.BeginFigure(p, true, true);
                    else ctx.LineTo(p, false, false);
                }
            }
            g.Freeze();
            return g;
        }
    }
}
