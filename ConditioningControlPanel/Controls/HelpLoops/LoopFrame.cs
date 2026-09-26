using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using static ConditioningControlPanel.Controls.HelpLoops.LoopMath;

namespace ConditioningControlPanel.Controls.HelpLoops
{
    /// <summary>Where the fake desktop's window and text lines landed, in stage coords.</summary>
    public sealed record DesktopInfo(Rect Window, IReadOnlyList<Rect> Lines);

    /// <summary>
    /// The drawing kit a scene paints with, one per frame. Stage space is the mockup's 480x270,
    /// origin top-left. Two layers: <see cref="Back"/> is the desktop (the only layer
    /// <see cref="BackBlur"/> touches), <see cref="Front"/> is everything over it. Primitives mirror
    /// the mockup's DOM furniture (cursor, ripple, chips, slider, bubble...) and draw on Front
    /// unless noted.
    /// </summary>
    public sealed class LoopFrame
    {
        public const double StageWidth = 480;
        public const double StageHeight = 270;

        public static readonly Rect DefaultWindow = new(60, 28, 320, 190);

        private static FontFamily? _display;
        private static readonly FontFamily MonoFamily = new("Consolas, Courier New");
        private static readonly FontFamily BodyFamily = new("Segoe UI");

        private static readonly Geometry CursorGeometry = LoopPalette.Freeze(
            Geometry.Parse("M1,1 L1,17 L5,13 L8,20 L11,19 L8,12 L14,12 Z"));
        private static readonly Geometry SpeakerGeometry = LoopPalette.Freeze(
            Geometry.Parse("M4,10 H9 L15,5 V21 L9,16 H4 Z"));
        private static readonly Geometry DropGeometry = LoopPalette.Freeze(
            Geometry.Parse("M13,3 C13,3 5,12 5,17 A8,8 0 0 0 21,17 C21,12 13,3 13,3 Z"));

        private static readonly double[] LineWidths = { 88, 64, 92, 50, 76, 70, 40 };

        private static readonly Brush CursorShadow = LoopPalette.Solid("#000000aa");
        private static readonly Pen CursorPen = LoopPalette.Freeze(new Pen(LoopPalette.Solid("#1a1030"), 1.4) { LineJoin = PenLineJoin.Round });
        private static readonly Brush TaskbarFill = LoopPalette.Solid("#0d0a18cc");
        private static readonly Brush SliderFill = LoopPalette.Solid("#0d0a18e6");
        private static readonly Brush BubbleFill = LoopPalette.Freeze(new RadialGradientBrush
        {
            Center = new Point(.32, .28),
            GradientOrigin = new Point(.32, .28),
            RadiusX = .99,
            RadiusY = .99,
            GradientStops =
            {
                new GradientStop(LoopPalette.Css("#ffffffd0"), 0),
                new GradientStop(LoopPalette.Css("#ffffffd0"), .08),
                new GradientStop(LoopPalette.Css("#ffffff30"), .14),
                new GradientStop(LoopPalette.Css("#ffb3dc22"), .45),
                new GradientStop(LoopPalette.Css("#ff6fb566"), .78),
                new GradientStop(LoopPalette.Css("#ffd6ecaa"), 1),
            }
        });
        private static readonly Pen BubbleRim = LoopPalette.Freeze(new Pen(LoopPalette.Solid("#ffffff40"), 1));
        private static readonly Pen BubbleGlow = LoopPalette.Freeze(new Pen(LoopPalette.Solid("#ff6fb526"), 4));

        public LoopFrame(DrawingContext back, DrawingContext front, LoopPalette palette, double pixelsPerDip = 1.0)
        {
            Back = back;
            Front = front;
            P = palette;
            PixelsPerDip = pixelsPerDip <= 0 ? 1.0 : pixelsPerDip;
        }

        /// <summary>The desktop layer.</summary>
        public DrawingContext Back { get; }

        /// <summary>Everything drawn over the desktop.</summary>
        public DrawingContext Front { get; }

        /// <summary>Blur radius in stage px for the Back layer only. 0 = none.</summary>
        public double BackBlur { get; set; }

        public LoopPalette P { get; }

        public double PixelsPerDip { get; }

        /// <summary>Fredoka (bundled), falling back to Segoe UI. Labels and figures.</summary>
        public static FontFamily Display
        {
            get
            {
                if (_display != null) return _display;
                try { _display = new FontFamily(new Uri("pack://application:,,,/"), "./Fonts/#Fredoka, Segoe UI"); }
                catch { _display = new FontFamily("Segoe UI"); }
                return _display;
            }
        }

        /// <summary>Mono chips and dials (house rule: always with a fallback).</summary>
        public static FontFamily Mono => MonoFamily;

        public static FontFamily Body => BodyFamily;

        // ---------------------------------------------------------------- scopes

        /// <summary>
        /// Pushes a scale (about cx,cy), a rotation (degrees, about cx,cy) and an opacity onto
        /// <paramref name="dc"/>; dispose to pop. The mockup's <c>at(e,x,y,scale,opacity,rot)</c>
        /// scales about the element's centre, so pass the centre.
        /// </summary>
        public PopScope At(DrawingContext dc, double cx, double cy, double scale = 1, double opacity = 1, double rotateDeg = 0)
        {
            int n = 0;
            if (opacity < 1) { dc.PushOpacity(Clamp(opacity)); n++; }
            if (Math.Abs(scale - 1) > 1e-6) { dc.PushTransform(new ScaleTransform(scale, scale, cx, cy)); n++; }
            if (Math.Abs(rotateDeg) > 1e-6) { dc.PushTransform(new RotateTransform(rotateDeg, cx, cy)); n++; }
            return new PopScope(dc, n);
        }

        /// <summary>Pushes an opacity; dispose to pop.</summary>
        public PopScope Fade(DrawingContext dc, double opacity)
        {
            dc.PushOpacity(Clamp(opacity));
            return new PopScope(dc, 1);
        }

        /// <summary>Pushes a translation; dispose to pop.</summary>
        public PopScope Offset(DrawingContext dc, double dx, double dy)
        {
            dc.PushTransform(new TranslateTransform(dx, dy));
            return new PopScope(dc, 1);
        }

        /// <summary>Pushes a clip; dispose to pop.</summary>
        public PopScope ClipTo(DrawingContext dc, Geometry clip)
        {
            dc.PushClip(clip);
            return new PopScope(dc, 1);
        }

        public readonly struct PopScope : IDisposable
        {
            private readonly DrawingContext _dc;
            private readonly int _n;
            public PopScope(DrawingContext dc, int n) { _dc = dc; _n = n; }
            public void Dispose() { for (int i = 0; i < _n; i++) _dc.Pop(); }
        }

        // ---------------------------------------------------------------- stage

        /// <summary>The stage ground (the engine paints it before every frame).</summary>
        public void Ground()
        {
            Back.DrawRectangle(GroundBrush, null, new Rect(0, 0, StageWidth, StageHeight));
        }

        private static readonly Brush GroundBrush = LoopPalette.Freeze(new RadialGradientBrush
        {
            Center = new Point(.2, .1),
            GradientOrigin = new Point(.2, .1),
            RadiusX = 1.2,
            RadiusY = .9,
            GradientStops =
            {
                new GradientStop(LoopPalette.Css("#2a1f47"), 0),
                new GradientStop(LoopPalette.Css("#150f26"), .55),
                new GradientStop(LoopPalette.Css("#0b0914"), 1),
            }
        });

        /// <summary>
        /// Back layer: the fake desktop. A window (bar, three dots, text lines) and the taskbar.
        /// Default window = (60,28,320,190).
        /// </summary>
        public DesktopInfo Desktop(Rect? win = null) => Desktop(win, drawLines: true);

        /// <summary>As <see cref="Desktop(Rect?)"/>; with drawLines false the line rects are still
        /// returned so a scene can paint the lines itself (per-line fade or wobble).</summary>
        public DesktopInfo Desktop(Rect? win, bool drawLines, DrawingContext? dc = null)
        {
            dc ??= Back;
            var r = win ?? DefaultWindow;
            SoftShadow(dc, r, 8, 8, 24, 0x88);
            var shape = new RectangleGeometry(r, 8, 8);
            dc.DrawGeometry(P.Window, null, shape);
            var lines = new List<Rect>();
            using (ClipTo(dc, shape))
            {
                dc.DrawRectangle(P.WindowBar, null, new Rect(r.X, r.Y, r.Width, 19));
                for (int i = 0; i < 3; i++)
                    dc.DrawEllipse(P.LineA, null, new Point(r.X + 1 + 8 + 3.5 + i * 12, r.Y + 1 + 9), 3.5, 3.5);

                for (int i = 0; i < LineWidths.Length; i++)
                {
                    if (18 + 10 + i * 17 >= r.Height - 14) break;
                    var lr = new Rect(r.X + 1 + 14, r.Y + 1 + 18 + 10 + i * 17, (r.Width - 2) * LineWidths[i] / 100.0, 7);
                    lines.Add(lr);
                    if (drawLines) dc.DrawRoundedRectangle(i % 3 == 1 ? P.LineB : P.LineA, null, lr, 3.5, 3.5);
                }
            }
            dc.DrawRoundedRectangle(null, new Pen(P.WindowBorder, 1), Inset(r, .5), 7.5, 7.5);

            // Taskbar: bottom 20px, five little icons centred.
            dc.DrawRectangle(TaskbarFill, null, new Rect(0, StageHeight - 20, StageWidth, 20));
            double x0 = (StageWidth - (5 * 11 + 4 * 8)) / 2;
            for (int i = 0; i < 5; i++)
                dc.DrawRoundedRectangle(P.Track, null, new Rect(x0 + i * 19, StageHeight - 20 + 4.5, 11, 11), 3, 3);

            return new DesktopInfo(r, lines);
        }

        /// <summary>One desktop text line, the colour Desktop uses for index i.</summary>
        public void DesktopLine(DrawingContext dc, Rect line, int index, double opacity = 1)
        {
            using (Fade(dc, opacity))
                dc.DrawRoundedRectangle(index % 3 == 1 ? P.LineB : P.LineA, null, line, 3.5, 3.5);
        }

        // ---------------------------------------------------------------- cursor

        /// <summary>The white arrow cursor, tip near (x+1,y+1); pressed shrinks it to 85%.</summary>
        public void Cursor(double x, double y, bool down)
        {
            var dc = Front;
            using (At(dc, x + 8, y + 11, down ? .85 : 1))
            {
                using (Offset(dc, x, y + 1)) dc.DrawGeometry(CursorShadow, null, CursorGeometry);
                using (Offset(dc, x, y)) dc.DrawGeometry(Brushes.White, CursorPen, CursorGeometry);
            }
        }

        /// <summary>Mint click ripple. k 0..1, nothing outside (0,1).</summary>
        public void Ripple(double x, double y, double k)
        {
            if (k <= 0 || k >= 1) return;
            var s = Lerp(.3, 1.7, EaseOut(k));
            var pen = new Pen(P.Mint, 2 * s);
            using (Fade(Front, 1 - k))
                Front.DrawEllipse(null, pen, new Point(x, y), 14 * s, 14 * s);
        }

        /// <summary>A rising "+XP" style label. k 0..1 over its life, nothing outside (0,1).</summary>
        public void Floater(double x, double y, double k, string text, Brush? color = null)
        {
            if (k <= 0 || k >= 1) return;
            var o = k < .15 ? k / .15 : 1 - Seg(k, .6, 1);
            var yy = y - 26 * EaseOut(k);
            using (Fade(Front, o))
            {
                DrawText(Front, text, x, yy + 3 + 1, 12, CursorShadow, Display, FontWeights.SemiBold, TextAlignment.Left);
                DrawText(Front, text, x, yy + 3, 12, color ?? P.Mint, Display, FontWeights.SemiBold, TextAlignment.Left);
            }
        }

        // ---------------------------------------------------------------- chrome

        /// <summary>A mono pill label; hot = bright text and an accent border.</summary>
        public void Chip(double x, double y, string text, bool hot = false, double opacity = 1)
        {
            if (opacity <= 0) return;
            var ft = Format(text, 10, hot ? P.Text : P.Dim, Mono, FontWeights.Medium);
            var r = new Rect(x, y, ft.WidthIncludingTrailingWhitespace + 18, 23);
            using (Fade(Front, opacity))
            {
                Front.DrawRoundedRectangle(P.Glass, new Pen(hot ? P.Accent : P.Border, 1), Inset(r, .5), 11, 11);
                Front.DrawText(ft, new Point(x + 9, y + 1 + 3 + (15 - ft.Height) / 2));
            }
        }

        /// <summary>Width a <see cref="Chip"/> would take for this text.</summary>
        public double ChipWidth(string text) => Format(text, 10, P.Dim, Mono, FontWeights.Medium).WidthIncludingTrailingWhitespace + 18;

        /// <summary>
        /// The 130x34 slider panel (label, track, pink fill, knob, % readout). Returns the point the
        /// mockup's cursor aims at for <paramref name="value"/> (same as <see cref="SliderKnob"/>).
        /// </summary>
        public Point Slider(double x, double y, string label, double value, Brush? border = null)
        {
            var dc = Front;
            var v = Clamp(value);
            var r = new Rect(x, y, 130, 34);
            dc.DrawRoundedRectangle(SliderFill, new Pen(border ?? P.Border, 1), Inset(r, .5), 8.5, 8.5);
            DrawText(dc, label, x + 10, y + 5, 9, P.Dim, Mono, FontWeights.Medium, TextAlignment.Left);
            var track = new Rect(x + 10, y + 20, 80, 4);
            dc.DrawRoundedRectangle(P.Track, null, track, 2, 2);
            if (v > 0) dc.DrawRoundedRectangle(P.Accent, null, new Rect(track.X, track.Y, track.Width * v, 4), 2, 2);
            dc.DrawEllipse(P.White, null, new Point(track.X + track.Width * v, track.Y + 2), 6, 6);
            DrawText(dc, Math.Round(v * 100).ToString(CultureInfo.InvariantCulture) + "%", x + 130 - 9, y + 34 - 6 - 15, 10, P.Text, Mono, FontWeights.Medium, TextAlignment.Right);
            return SliderKnob(x, y, v);
        }

        /// <summary>Where the cursor goes to grab the knob at <paramref name="value"/> (mockup knobAt).</summary>
        public Point SliderKnob(double x, double y, double value) => new(x + 9 + Clamp(value) * 81, y + 25);

        /// <summary>A soap bubble, 44px at scale 1, centred on (cx,cy).</summary>
        public void Bubble(double cx, double cy, double scale = 1, double opacity = 1) => Bubble(Front, cx, cy, scale, opacity);

        public void Bubble(DrawingContext dc, double cx, double cy, double scale = 1, double opacity = 1)
        {
            if (opacity <= 0 || scale <= 0) return;
            using (At(dc, cx, cy, scale, opacity))
            {
                dc.DrawEllipse(null, BubbleGlow, new Point(cx, cy), 23, 23);
                dc.DrawEllipse(BubbleFill, BubbleRim, new Point(cx, cy), 22, 22);
            }
        }

        /// <summary>A speaker (or a water drop) at (x,y) in a 26px box, with two sound waves.</summary>
        public void Speaker(double x, double y, double waveK, bool drop = false) => Speaker(x, y, waveK, drop, 2, 1);

        /// <summary>
        /// Full form: <paramref name="waves"/> arcs (2 or 3), each fading a little later than the one
        /// before, and a scale for the glyph (Brain Drain's drop swells on a clip).
        /// </summary>
        public void Speaker(double x, double y, double waveK, bool drop, int waves, double scale = 1)
        {
            var dc = Front;
            using (At(dc, x + 13, y + 13, scale))
            using (Offset(dc, x, y))
                dc.DrawGeometry(P.Lilac, null, drop ? DropGeometry : SpeakerGeometry);

            if (waveK <= 0 || waveK >= 1) return;
            double step = waves >= 3 ? .15 : .2;
            var pen = new Pen(P.Lilac, 2);
            for (int i = 0; i < waves; i++)
            {
                var o = 1 - Seg(waveK, step * i, 1);
                if (o <= 0) continue;
                double d = 16 + i * 10;
                double left = x + 20 + i * 5, top = y + 5 - i * 4;
                double cx = left + d / 2, cy = top + d / 2, rad = d / 2 - 1;
                var g = new StreamGeometry();
                using (var c = g.Open())
                {
                    c.BeginFigure(new Point(cx, cy - rad), false, false);
                    c.ArcTo(new Point(cx, cy + rad), new Size(rad, rad), 0, false, SweepDirection.Clockwise, true, false);
                }
                g.Freeze();
                using (Fade(dc, o)) dc.DrawGeometry(null, pen, g);
            }
        }

        /// <summary>A rounded dark card (lock card, question card) with its drop shadow.</summary>
        public void Card(Rect r, Brush border, double opacity = 1)
        {
            if (opacity <= 0) return;
            using (Fade(Front, opacity))
            {
                SoftShadow(Front, r, 12, 14, 34, 0xCC);
                Front.DrawRoundedRectangle(P.Panel, new Pen(border, 1), Inset(r, .5), 11.5, 11.5);
            }
        }

        // ---------------------------------------------------------------- text

        /// <summary>Text in the display face (Fredoka). (x,y) is the top of the line; with Center or
        /// Right alignment x is the centre or the right edge.</summary>
        public void Text(string s, double x, double y, double size, Brush brush, bool bold = false,
            TextAlignment align = TextAlignment.Left, double opacity = 1)
        {
            if (opacity <= 0 || string.IsNullOrEmpty(s)) return;
            using (Fade(Front, opacity))
                DrawText(Front, s, x, y, size, brush, Display, bold ? FontWeights.SemiBold : FontWeights.Medium, align);
        }

        /// <summary>Text on any layer in any face; the general form behind <see cref="Text"/>.</summary>
        public void DrawText(DrawingContext dc, string s, double x, double y, double size, Brush brush,
            FontFamily family, FontWeight weight, TextAlignment align)
        {
            if (string.IsNullOrEmpty(s)) return;
            var ft = Format(s, size, brush, family, weight);
            ft.TextAlignment = align;
            dc.DrawText(ft, new Point(x, y));
        }

        /// <summary>Measures text as <see cref="DrawText"/> would draw it.</summary>
        public Size Measure(string s, double size, FontFamily? family = null, bool bold = false)
        {
            var ft = Format(s, size, P.Text, family ?? Display, bold ? FontWeights.SemiBold : FontWeights.Medium);
            return new Size(ft.WidthIncludingTrailingWhitespace, ft.Height);
        }

        public FormattedText Format(string s, double size, Brush brush, FontFamily family, FontWeight weight) =>
            new(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface(family, FontStyles.Normal, weight, FontStretches.Normal), size, brush, PixelsPerDip);

        // ---------------------------------------------------------------- small helpers

        /// <summary>A straight line.</summary>
        public void Line(DrawingContext dc, Point a, Point b, Brush brush, double thickness = 1)
        {
            dc.DrawLine(new Pen(brush, thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, a, b);
        }

        /// <summary>
        /// A soft box shadow (CSS <c>0 dy blur color</c>) faked with stacked translucent rounded
        /// rects, since a DrawingContext has no blur. <paramref name="alpha"/> is the shadow's peak.
        /// </summary>
        public static void SoftShadow(DrawingContext dc, Rect r, double radius, double dy, double blur, byte alpha)
        {
            const int layers = 5;
            for (int i = layers; i >= 1; i--)
            {
                double grow = blur * i / layers / 2;
                var rr = new Rect(r.X - grow, r.Y + dy - grow, r.Width + grow * 2, r.Height + grow * 2);
                var a = (byte)(alpha / (layers + 1.5));
                dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(a, 0, 0, 0)), null, rr, radius + grow, radius + grow);
            }
        }

        public static Rect Inset(Rect r, double d) =>
            new(r.X + d, r.Y + d, Math.Max(0, r.Width - 2 * d), Math.Max(0, r.Height - 2 * d));
    }
}
