using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace ConditioningControlPanel.Controls.Billboard
{
    /// <summary>
    /// "N quests left today": today's quests as paper slips on a clipboard. The finished ones are
    /// checked, pressed in and dimmed; the open ones stand up bright, and the next one to do is
    /// pulled out a little with a pointer, a glint and a nudge now and then. Every loop a rubber
    /// stamp comes down on a slip that is ALREADY done and re-inks its check with a squash and
    /// sparks: the picture replays progress, it never claims any the player has not made.
    ///
    /// <para>ArtData = {done, total}. At most <see cref="MaxSlips"/> slips are drawn; above that
    /// the count of open slips is what stays true (see <see cref="Fit"/>).</para>
    /// </summary>
    public sealed class QuestsArtView : BillboardVectorArt
    {
        /// <summary>The most slips the clipboard holds.</summary>
        public const int MaxSlips = 5;

        /// <summary>When the stamp loop starts (every slip has landed by then).</summary>
        internal const double LoopStart = 1.4;

        /// <summary>One stamp loop.</summary>
        internal const double LoopSeconds = 5.2;

        // Stamp beats inside a loop, in seconds.
        private const double DescendAt = 0.6, HoverAt = 1.2, SlamAt = 1.5, ContactAt = 1.62, LiftAt = 2.3, GoneAt = 3.2;
        private const double PokeAt = 3.25, NudgeAt = 3.45;

        private static readonly Color Ink = Color.FromRgb(0x2a, 0x1c, 0x44);
        private static readonly Color Mint = Color.FromRgb(0x3c, 0xe6, 0x7d);
        private static readonly Color Pink = Color.FromRgb(0xff, 0x4f, 0xa8);
        private static readonly Color Shade = Color.FromRgb(4, 4, 12);

        private static readonly Geometry CheckShape = MakeCheck();
        private static readonly Geometry GemShape = MakeGem();
        private static readonly Geometry ArrowShape = MakeArrow();
        private static readonly Pen CheckPen = MakeCheckPen();

        private readonly int _shown;
        private readonly int _done;
        private Geometry? _slipShape;
        private Size _slipSize;

        public QuestsArtView((int Done, int Total) data)
        {
            (_shown, _done) = Fit(data.Done, data.Total);
        }

        /// <summary>Slips drawn.</summary>
        public int Shown => _shown;

        /// <summary>Slips drawn checked.</summary>
        public int DoneShown => _done;

        /// <summary>{done, total} from the provider's plain payload; nothing reads as three open quests.</summary>
        internal static (int Done, int Total) Data(object? data)
        {
            int? Get(string key) => data is IReadOnlyDictionary<string, int> d && d.TryGetValue(key, out var v) ? v : null;
            int total = Math.Max(0, Get("total") ?? 3);
            return (Math.Clamp(Get("done") ?? 0, 0, total), total);
        }

        /// <summary>
        /// Slips to draw and how many of them are checked. Up to five slips; past that the OPEN
        /// count is kept (seven quests with two left draw three checked and two open), and when
        /// more are open than fit, none is checked. A total of 0 draws one open slip.
        /// </summary>
        internal static (int Shown, int Done) Fit(int done, int total)
        {
            total = Math.Max(1, total);
            done = Math.Clamp(done, 0, total);
            int shown = Math.Min(total, MaxSlips);
            int left = total - done;
            return (shown, Math.Max(0, shown - left));
        }

        /// <summary>Which slip the stamp lands on at time <paramref name="t"/>: a checked one, cycling
        /// loop by loop, or -1 (no checked slip, or the loop has not started).</summary>
        internal static int StampTarget(double t, int doneShown)
        {
            if (doneShown <= 0 || t < LoopStart) return -1;
            int loop = (int)Math.Floor((t - LoopStart) / LoopSeconds);
            return loop % doneShown;
        }

        protected override void Paint(DrawingContext dc, double w, double h, double t)
        {
            CachedGround(dc, w, h, Deep(Accent));
            Motes(dc, w, h, t, Accent, 12, 23, new Rect(0.5, 0.08, 0.48, 0.86), 0.05, 0.005, 0.4);

            double bw = Math.Min(w * 0.36, h * 0.85), bh = BoardHeight(h, _shown);
            double kIn = BackOut(t / 0.7);
            double bob = Bob(t, h * 0.008), sway = Math.Sin(t * 0.7) * 0.8;
            double cx = w * 0.73, cy = h * 0.52 + bob + (1 - kIn) * h * 0.4;
            var slips = Layout(bw, bh, _shown, h);
            double sw = slips[0].Width, sh = slips[0].Height;
            double s = sh * 1.4;

            double p = -1;
            if (t >= LoopStart)
            {
                double u = (t - LoopStart) / LoopSeconds;
                p = (u - Math.Floor(u)) * LoopSeconds;
            }
            int target = StampTarget(t, _done);
            int next = _done < _shown ? _done : -1;

            if (_slipShape == null || _slipSize != new Size(sw, sh))
            {
                var g = new RectangleGeometry(new Rect(-sw / 2, -sh / 2, sw, sh), sh * 0.16, sh * 0.16);
                g.Freeze();
                _slipShape = g;
                _slipSize = new Size(sw, sh);
            }

            dc.PushOpacity(Math.Clamp(kIn * 1.5, 0, 1));
            dc.PushTransform(new MatrixTransform(Pose(cx, cy, -2.5 + sway)));
            dc.DrawDrawing(Layer("board", w, h, d => PaintBoard(d, bw, bh)));

            Point nextAt = default;
            for (int i = 0; i < _shown; i++)
            {
                var r = slips[i];
                double k = BackOut((t - 0.25 - i * 0.11) / 0.45);
                if (k <= 0.001) continue;
                double dy = (1 - k) * -bh * 0.45, dx = 0, tilt = (Hash(7, i, 1) - 0.5) * 3.0;
                bool open = i >= _done;

                if (i == next)
                {
                    dx = -sh * 0.2;
                    if (p >= NudgeAt) dx += Math.Sin((p - NudgeAt) * 20) * Math.Exp(-(p - NudgeAt) * 5) * sh * 0.14;
                    tilt += Math.Sin(t * 1.3) * 0.6;
                }
                if (i == target && p >= ContactAt)
                    dy += Math.Exp(-(p - ContactAt) * 9) * Math.Abs(Math.Cos((p - ContactAt) * 22)) * sh * 0.07;

                var at = new Point(r.X + r.Width / 2 + dx, r.Y + r.Height / 2 + dy);
                if (i == next) nextAt = at;
                dc.PushOpacity(Math.Clamp(k * 2, 0, 1));
                dc.PushTransform(new MatrixTransform(Pose(at.X, at.Y, tilt)));
                dc.DrawDrawing(open
                    ? Layer("slip-open", w, h, d => PaintSlip(d, sw, sh, false))
                    : Layer("slip-done", w, h, d => PaintSlip(d, sw, sh, true)));

                if (i == next)
                {
                    double pulse = 0.5 + 0.5 * Math.Sin(t * 3.1);
                    var halo = new Rect(-sw / 2 - sh * 0.06, -sh / 2 - sh * 0.06, sw + sh * 0.12, sh + sh * 0.12);
                    dc.DrawRoundedRectangle(null, Stroke(Accent, sh * 0.05, 0.25 + 0.35 * pulse), halo, sh * 0.2, sh * 0.2);
                    Sheen(dc, _slipShape, new Rect(-sw / 2, -sh / 2, sw, sh), t, 2.9, 0.4, 0.95);
                }
                if (i == target && p >= ContactAt && p < ContactAt + 1.2)
                    PaintFreshInk(dc, sw, sh, p - ContactAt);
                dc.Pop();
                dc.Pop();
            }

            if (target >= 0) PaintStamp(dc, w, h, slips[target], s, bh, p);
            if (next >= 0) PaintPointer(dc, nextAt, sw, sh, s, t, p);
            dc.Pop();
            dc.Pop();

            Twinkles(dc, w, h, t, Mix(Accent, Colors.White, 0.35), 4, 29, new Rect(0.52, 0.1, 0.44, 0.8), 0.028);
            Motes(dc, w, h, t, Colors.White, 5, 31, new Rect(0.55, 0.2, 0.42, 0.8), 0.1, 0.008, 0.3);
        }

        /// <summary>The board grows with the slips it holds: one slip on a short board, five on a tall one.
        /// <paramref name="unit"/> is the card height.</summary>
        internal static double BoardHeight(double unit, int n)
        {
            n = Math.Clamp(n, 1, MaxSlips);
            double sh = n <= 3 ? 0.15 : 0.125;
            return unit * Math.Clamp(0.155 + n * sh + (n - 1) * 0.025, 0.5, 0.86);
        }

        /// <summary>The slips' places on the board (board-local, origin at its centre), top to bottom.
        /// <paramref name="unit"/> is the card height.</summary>
        internal static Rect[] Layout(double bw, double bh, int n, double unit)
        {
            n = Math.Clamp(n, 1, MaxSlips);
            double top = -bh / 2 + unit * 0.1, avail = bh - unit * 0.155, gap = unit * 0.025;
            double sh = Math.Min(unit * 0.15, (avail - gap * (n - 1)) / n), sw = bw * 0.86;
            double block = n * sh + (n - 1) * gap, y0 = top + (avail - block) / 2;
            var rects = new Rect[n];
            for (int i = 0; i < n; i++) rects[i] = new Rect(-sw / 2, y0 + i * (sh + gap), sw, sh);
            return rects;
        }

        private static Matrix Pose(double x, double y, double degrees)
        {
            var m = Matrix.Identity;
            m.Rotate(degrees);
            m.Translate(x, y);
            return m;
        }

        /// <summary>The clipboard: a raised plum board, a sunken well that holds the slips, a gold clip.</summary>
        private void PaintBoard(DrawingContext d, double bw, double bh)
        {
            var board = new Rect(-bw / 2, -bh / 2, bw, bh);
            var wood = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0.6, 1) };
            wood.GradientStops.Add(new GradientStop(Color.FromRgb(0x46, 0x34, 0x66), 0));
            wood.GradientStops.Add(new GradientStop(Color.FromRgb(0x26, 0x1c, 0x3c), 1));
            wood.Freeze();
            Plate(d, board, bw * 0.07, wood, bh * 0.035);

            var well = new Rect(-bw * 0.46, -bh / 2 + bh * 0.085, bw * 0.92, bh * 0.875);
            Plate(d, well, bw * 0.05, Fill(Shade, 0.22), 0, pressed: true);

            double ch = Math.Min(bh * 0.11, bw * 0.13);
            var clip = new Rect(-bw * 0.19, -bh / 2 - ch * 0.5, bw * 0.38, ch);
            var metal = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
            metal.GradientStops.Add(new GradientStop(Mix(Accent, Colors.White, 0.5), 0));
            metal.GradientStops.Add(new GradientStop(Accent, 0.45));
            metal.GradientStops.Add(new GradientStop(Mix(Accent, Shade, 0.45), 1));
            metal.Freeze();
            Plate(d, clip, ch * 0.22, metal, ch * 0.16);
            var slot = new Rect(-bw * 0.08, clip.Y + clip.Height * 0.3, bw * 0.16, clip.Height * 0.32);
            Plate(d, slot, slot.Height / 2, Fill(Shade, 0.75), 0, pressed: true);
            foreach (var x in new[] { -bw * 0.145, bw * 0.145 })
            {
                var c = new Point(x, clip.Y + clip.Height * 0.5);
                d.DrawEllipse(Fill(Mix(Accent, Shade, 0.3)), null, c, ch * 0.11, ch * 0.11);
                d.DrawEllipse(Fill(Colors.White, 0.55), null, new Point(c.X - ch * 0.035, c.Y - ch * 0.035), ch * 0.035, ch * 0.035);
            }
        }

        /// <summary>One slip, centred on the origin: an open one raised and bright, a done one pressed in,
        /// dimmed, ink struck through and its box checked.</summary>
        private void PaintSlip(DrawingContext d, double sw, double sh, bool done)
        {
            var r = new Rect(-sw / 2, -sh / 2, sw, sh);
            var lit = Mix(Accent, Colors.White, 0.86);
            var paper = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0.35, 1) };
            paper.GradientStops.Add(new GradientStop(done ? Mix(lit, Color.FromRgb(0x1c, 0x16, 0x30), 0.55) : lit, 0));
            paper.GradientStops.Add(new GradientStop(done ? Mix(lit, Color.FromRgb(0x1c, 0x16, 0x30), 0.68) : Mix(Accent, Colors.White, 0.68), 1));
            paper.Freeze();
            Plate(d, r, sh * 0.16, paper, done ? 0 : sh * 0.1, pressed: done);

            var box = new Point(-sw / 2 + sh * 0.5, 0);
            double br = sh * 0.27;
            if (done)
            {
                d.DrawEllipse(Fill(Mix(Mint, Shade, 0.35)), null, box, br, br);
                d.DrawEllipse(null, Bevel(sh * 0.04, pressed: true), box, br * 0.94, br * 0.94);
                d.DrawEllipse(null, Stroke(Mint, sh * 0.03, 0.35), box, br * 1.32, br * 1.32);
                PaintCheck(d, box, br * 0.9, 0.85);
            }
            else
            {
                d.DrawEllipse(Fill(Ink, 0.08), null, box, br, br);
                d.DrawEllipse(null, Stroke(Ink, sh * 0.05, 0.45), box, br, br);
                d.DrawEllipse(null, Bevel(sh * 0.03, pressed: true), box, br * 0.86, br * 0.86);
            }

            double x0 = -sw / 2 + sh * 0.98, maxW = sw - sh * 0.98 - sh * 0.95;
            var lineInk = done ? Fill(Colors.White, 0.16) : Fill(Ink, 0.34);
            var lineInk2 = done ? Fill(Colors.White, 0.1) : Fill(Ink, 0.2);
            d.DrawRoundedRectangle(lineInk, null, new Rect(x0, -sh * 0.18, maxW * 0.8, sh * 0.13), sh * 0.06, sh * 0.06);
            d.DrawRoundedRectangle(lineInk2, null, new Rect(x0, sh * 0.07, maxW * 0.5, sh * 0.11), sh * 0.05, sh * 0.05);
            if (done)
                d.DrawLine(Stroke(Mint, sh * 0.06, 0.7), new Point(x0 - sh * 0.08, -sh * 0.1), new Point(x0 + maxW * 0.86, -sh * 0.13));

            // The reward: a little gem, lit while it is still to be earned.
            var gem = new Point(sw / 2 - sh * 0.45, 0);
            double gs = sh * 0.22;
            d.PushTransform(new MatrixTransform(gs, 0, 0, gs, gem.X, gem.Y));
            if (done)
            {
                d.DrawGeometry(Fill(Colors.White, 0.14), null, GemShape);
            }
            else
            {
                d.PushTransform(new TranslateTransform(0.12, 0.18));
                d.DrawGeometry(Fill(Shade, 0.25), null, GemShape);
                d.Pop();
                d.DrawGeometry(Fill(Pink), null, GemShape);
                var facet = new StreamGeometry();
                using (var ctx = facet.Open())
                {
                    ctx.BeginFigure(new Point(0, -1), true, true);
                    ctx.LineTo(new Point(-0.75, 0), false, false);
                    ctx.LineTo(new Point(0, 0.05), false, false);
                }
                facet.Freeze();
                d.DrawGeometry(Fill(Colors.White, 0.5), null, facet);
            }
            d.Pop();
        }

        private static void PaintCheck(DrawingContext d, Point c, double r, double alpha)
        {
            d.PushTransform(new MatrixTransform(r, 0, 0, r, c.X, c.Y));
            d.PushOpacity(alpha);
            d.DrawGeometry(null, CheckPen, CheckShape);
            d.Pop();
            d.Pop();
        }

        /// <summary>The stamp's mark on a slip that was already done: the check flares, a ring rolls
        /// out and sparks fly from the box.</summary>
        private void PaintFreshInk(DrawingContext dc, double sw, double sh, double f)
        {
            var box = new Point(-sw / 2 + sh * 0.5, 0);
            double br = sh * 0.27;
            double flare = Math.Exp(-f * 2.6);
            dc.DrawEllipse(Fill(Mint, 0.85 * flare), null, box, br * (1 + 0.25 * flare), br * (1 + 0.25 * flare));
            PaintCheck(dc, box, br * (0.9 + 0.4 * Math.Exp(-f * 7)), Math.Min(1, 0.85 + flare));

            double e = EaseOut(f / 0.55);
            if (f < 0.55)
                dc.DrawEllipse(null, Stroke(Mint, sh * 0.06 * (1 - e) + 0.6, 0.8 * (1 - e)), box, br * (1.2 + 1.6 * e), br * (1.2 + 1.6 * e));
            if (f < 0.7 && Particles)
            {
                double k = EaseOut(f / 0.7);
                for (int i = 0; i < 8; i++)
                {
                    double a = i * Math.PI / 4 + 0.3;
                    double dist = br * (1.3 + 2.4 * k) * (0.8 + 0.4 * Hash(13, i, 2));
                    var at = new Point(box.X + Math.Cos(a) * dist, box.Y + Math.Sin(a) * dist * 0.8);
                    Spark(dc, at, sh * 0.13 * (1 - k * 0.6), i % 2 == 0 ? Colors.White : Accent, 1 - k, a);
                }
            }
        }

        /// <summary>The rubber stamp: comes in from above, winds up, slams onto the target's box,
        /// squashes, then lifts away.</summary>
        private void PaintStamp(DrawingContext dc, double w, double h, Rect slip, double s, double bh, double p)
        {
            if (p < DescendAt || p > GoneAt) return;
            double y, alpha = 1, tilt = 0, sx = 1, sy = 1;
            double hover = -slip.Height * 0.9;
            if (p < HoverAt)
            {
                double e = EaseOut((p - DescendAt) / (HoverAt - DescendAt));
                y = -bh * 0.75 + (hover + bh * 0.75) * e;
                alpha = e;
                tilt = (1 - e) * 10;
            }
            else if (p < SlamAt)
            {
                double k = Math.Sin((p - HoverAt) / (SlamAt - HoverAt) * Math.PI / 2);
                y = hover - k * slip.Height * 0.35;
                tilt = -4 * k;
                sy = 1 + 0.06 * k;
                sx = 1 - 0.04 * k;
            }
            else if (p < ContactAt)
            {
                double k = (p - SlamAt) / (ContactAt - SlamAt);
                y = (hover - slip.Height * 0.35) * (1 - k * k);
                tilt = -4 * (1 - k);
                sy = 1.08;
                sx = 0.95;
            }
            else if (p < LiftAt)
            {
                y = 0;
                double f = p - ContactAt;
                sy = 1 - 0.22 * Math.Exp(-f * 9) * Math.Cos(f * 28);
                sx = 2 - sy;
            }
            else
            {
                double e = EaseOut((p - LiftAt) / (GoneAt - LiftAt));
                y = -e * bh * 0.75;
                alpha = 1 - e;
                tilt = e * 8;
            }

            var contact = new Point(slip.X + slip.Height * 0.5, slip.Y + slip.Height / 2);
            double near = 1 - Math.Clamp(-y / (bh * 0.75), 0, 1);
            dc.DrawEllipse(Fill(Shade, (0.12 + 0.28 * near) * alpha), null,
                new Point(contact.X + s * 0.06, contact.Y + slip.Height * 0.1), s * 0.38 * (0.6 + 0.4 * near), s * 0.1 * (0.6 + 0.4 * near));

            var m = Matrix.Identity;
            m.Scale(sx, sy);
            m.Rotate(tilt);
            m.Translate(contact.X, contact.Y + y + slip.Height * 0.12);
            dc.PushOpacity(alpha);
            dc.PushTransform(new MatrixTransform(m));
            dc.DrawDrawing(Layer("stamp", w, h, d => PaintStampBody(d, s)));
            dc.Pop();
            dc.Pop();
        }

        /// <summary>A rubber stamp side on, its pad at the origin: pad, pink block, neck, knob.</summary>
        private void PaintStampBody(DrawingContext d, double s)
        {
            Plate(d, new Rect(-s * 0.4, -s * 0.13, s * 0.8, s * 0.13), s * 0.03, Fill(Color.FromRgb(0x2a, 0x16, 0x3a)), 0);
            d.DrawRectangle(Fill(Mint, 0.7), null, new Rect(-s * 0.36, -s * 0.03, s * 0.72, s * 0.03));
            var block = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0.4, 1) };
            block.GradientStops.Add(new GradientStop(Mix(Pink, Colors.White, 0.3), 0));
            block.GradientStops.Add(new GradientStop(Mix(Pink, Shade, 0.25), 1));
            block.Freeze();
            Plate(d, new Rect(-s * 0.36, -s * 0.5, s * 0.72, s * 0.38), s * 0.08, block, s * 0.03);
            var neck = new LinearGradientBrush { StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5) };
            neck.GradientStops.Add(new GradientStop(Color.FromRgb(0x8a, 0x62, 0x9c), 0));
            neck.GradientStops.Add(new GradientStop(Color.FromRgb(0x4a, 0x30, 0x5e), 1));
            neck.Freeze();
            Plate(d, new Rect(-s * 0.09, -s * 0.78, s * 0.18, s * 0.3), s * 0.04, neck, 0);
            var knob = new RadialGradientBrush(Mix(Pink, Colors.White, 0.55), Mix(Pink, Shade, 0.2))
            {
                Center = new Point(0.35, 0.3),
                GradientOrigin = new Point(0.35, 0.3),
                RadiusX = 0.8,
                RadiusY = 0.8,
            };
            knob.Freeze();
            var kc = new Point(0, -s * 0.9);
            d.DrawEllipse(Fill(Shade, 0.3), null, new Point(kc.X + s * 0.02, kc.Y + s * 0.03), s * 0.21, s * 0.18);
            d.DrawEllipse(knob, null, kc, s * 0.21, s * 0.18);
            d.DrawEllipse(null, Bevel(s * 0.025), kc, s * 0.2, s * 0.17);
        }

        /// <summary>A pink arrow left of the next slip, bobbing, that pokes it once a loop.</summary>
        private void PaintPointer(DrawingContext dc, Point slipCentre, double sw, double sh, double s, double t, double p)
        {
            double poke = 0;
            if (p >= PokeAt && p < PokeAt + 0.45) poke = Math.Sin((p - PokeAt) / 0.45 * Math.PI) * s * 0.16;
            double size = s * 0.26;
            double x = slipCentre.X - sw / 2 - size * 1.45 + Math.Sin(t * 3.2) * s * 0.04 + poke;
            var at = new Point(x, slipCentre.Y);
            dc.PushTransform(new MatrixTransform(size, 0, 0, size, at.X + size * 0.12, at.Y + size * 0.2));
            dc.DrawGeometry(Fill(Shade, 0.35), null, ArrowShape);
            dc.Pop();
            dc.PushTransform(new MatrixTransform(size, 0, 0, size, at.X, at.Y));
            dc.DrawGeometry(Fill(Pink), null, ArrowShape);
            dc.Pop();
            dc.DrawLine(Stroke(Colors.White, size * 0.12, 0.55), new Point(at.X - size * 0.8, at.Y - size * 0.22), new Point(at.X, at.Y - size * 0.22));
        }

        private static Geometry MakeCheck()
        {
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                ctx.BeginFigure(new Point(-0.52, 0.02), false, false);
                ctx.LineTo(new Point(-0.14, 0.38), true, true);
                ctx.LineTo(new Point(0.52, -0.38), true, true);
            }
            g.Freeze();
            return g;
        }

        private static Pen MakeCheckPen()
        {
            var pen = new Pen(Brushes.White, 0.26) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
            pen.Freeze();
            return pen;
        }

        private static Geometry MakeGem()
        {
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                ctx.BeginFigure(new Point(0, -1), true, true);
                ctx.LineTo(new Point(0.75, 0), false, false);
                ctx.LineTo(new Point(0, 1), false, false);
                ctx.LineTo(new Point(-0.75, 0), false, false);
            }
            g.Freeze();
            return g;
        }

        private static Geometry MakeArrow()
        {
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                ctx.BeginFigure(new Point(-1, -0.34), true, true);
                ctx.LineTo(new Point(0.1, -0.34), false, false);
                ctx.LineTo(new Point(0.1, -0.82), false, false);
                ctx.LineTo(new Point(1, 0), false, false);
                ctx.LineTo(new Point(0.1, 0.82), false, false);
                ctx.LineTo(new Point(0.1, 0.34), false, false);
                ctx.LineTo(new Point(-1, 0.34), false, false);
            }
            g.Freeze();
            return g;
        }
    }
}
