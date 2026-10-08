using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using static ConditioningControlPanel.Avalonia.Controls.HelpLoops.LoopMath;

namespace ConditioningControlPanel.Avalonia.Controls.HelpLoops
{
    public enum PhotoLook { Pink, Stripes, Sea }

    /// <summary>Where the fake desktop's window and text lines landed, in stage coords.</summary>
    public sealed record DesktopInfo(Rect Window, IReadOnlyList<Rect> Lines);

    /// <summary>
    /// The drawing kit a scene paints with, one per frame (WPF Controls/HelpLoops/LoopFrame.cs and
    /// LoopFrame.Kit.cs). Stage space is the mockup's 480x270. <see cref="Back"/> is the desktop,
    /// <see cref="Front"/> everything over it; <see cref="BackBlur"/> touches Back only.
    /// </summary>
    public sealed partial class LoopFrame
    {
        public const double StageWidth = 480;
        public const double StageHeight = 270;

        public static readonly Rect DefaultWindow = new(60, 28, 320, 190);

        public static readonly FontFamily Display = new("Fredoka, Segoe UI");
        public static readonly FontFamily Mono = new("Consolas, Courier New, monospace");
        public static readonly FontFamily Body = new("Segoe UI");

        private static readonly Geometry CursorGeometry = Geometry.Parse("M1,1 L1,17 L5,13 L8,20 L11,19 L8,12 L14,12 Z");
        private static readonly Geometry SpeakerGeometry = Geometry.Parse("M4,10 H9 L15,5 V21 L9,16 H4 Z");
        private static readonly Geometry DropGeometry = Geometry.Parse("M13,3 C13,3 5,12 5,17 A8,8 0 0 0 21,17 C21,12 13,3 13,3 Z");
        private static readonly double[] LineWidths = { 88, 64, 92, 50, 76, 70, 40 };

        private static readonly IBrush CursorShadow = LoopPalette.Solid("#000000aa");
        private static readonly IPen CursorPen = new ImmutablePen(LoopPalette.Solid("#1a1030").ToImmutable(), 1.4, lineJoin: PenLineJoin.Round);
        private static readonly IBrush TaskbarFill = LoopPalette.Solid("#0d0a18cc");
        private static readonly IBrush SliderFill = LoopPalette.Solid("#0d0a18e6");
        private static readonly IBrush BubbleFill = new RadialGradientBrush
        {
            Center = new RelativePoint(.32, .28, RelativeUnit.Relative),
            GradientOrigin = new RelativePoint(.32, .28, RelativeUnit.Relative),
            RadiusX = new RelativeScalar(.99, RelativeUnit.Relative),
            RadiusY = new RelativeScalar(.99, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(LoopPalette.Css("#ffffffd0"), 0),
                new GradientStop(LoopPalette.Css("#ffffffd0"), .08),
                new GradientStop(LoopPalette.Css("#ffffff30"), .14),
                new GradientStop(LoopPalette.Css("#ffb3dc22"), .45),
                new GradientStop(LoopPalette.Css("#ff6fb566"), .78),
                new GradientStop(LoopPalette.Css("#ffd6ecaa"), 1),
            },
        }.ToImmutable();
        private static readonly IPen BubbleRim = new ImmutablePen(LoopPalette.Solid("#ffffff40").ToImmutable(), 1);
        private static readonly IPen BubbleGlow = new ImmutablePen(LoopPalette.Solid("#ff6fb526").ToImmutable(), 4);
        private static readonly IBrush PhotoPinkFill = Linear("#ff8fc8", "#9b5cff", 0, 0, 1, 1);
        private static readonly IBrush PhotoPinkShine = new RadialGradientBrush
        {
            Center = new RelativePoint(.4, .35, RelativeUnit.Relative),
            GradientOrigin = new RelativePoint(.4, .35, RelativeUnit.Relative),
            RadiusX = new RelativeScalar(.85, RelativeUnit.Relative),
            RadiusY = new RelativeScalar(.85, RelativeUnit.Relative),
            GradientStops = { new GradientStop(LoopPalette.Css("#ffffff99"), 0), new GradientStop(LoopPalette.Css("#ffffff00"), .6) },
        }.ToImmutable();
        private static readonly IBrush PhotoSeaFill = Linear("#5fffd0", "#2b6bff", .33, .03, .67, .97);
        private static readonly IBrush PhotoSeaShade = Linear("#00000000", "#00000066", 0, 0, 0, 1);
        private static readonly IBrush PhotoStripeA = LoopPalette.Solid("#ff6fb5");
        private static readonly IBrush PhotoStripeB = LoopPalette.Solid("#2b124a");

        private static IBrush Linear(string c0, string c1, double x0, double y0, double x1, double y1) => new LinearGradientBrush
        {
            StartPoint = new RelativePoint(x0, y0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(x1, y1, RelativeUnit.Relative),
            GradientStops = { new GradientStop(LoopPalette.Css(c0), 0), new GradientStop(LoopPalette.Css(c1), 1) },
        }.ToImmutable();

        private static readonly IBrush GroundBrush = new RadialGradientBrush
        {
            Center = new RelativePoint(.2, .1, RelativeUnit.Relative),
            GradientOrigin = new RelativePoint(.2, .1, RelativeUnit.Relative),
            RadiusX = new RelativeScalar(1.2, RelativeUnit.Relative),
            RadiusY = new RelativeScalar(.9, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(LoopPalette.Css("#2a1f47"), 0),
                new GradientStop(LoopPalette.Css("#150f26"), .55),
                new GradientStop(LoopPalette.Css("#0b0914"), 1),
            },
        }.ToImmutable();

        public LoopFrame(DrawingContext back, DrawingContext front, LoopPalette palette)
            : this(null, back, front, palette) { }

        /// <summary>With a separate ground layer under Back, so <see cref="BackBlur"/> never smears
        /// the stage edges (the view passes one; a bare two-layer frame paints ground on Back).</summary>
        public LoopFrame(DrawingContext? ground, DrawingContext back, DrawingContext front, LoopPalette palette)
        {
            _groundDc = ground;
            Back = back;
            Front = front;
            P = palette;
        }

        private readonly DrawingContext? _groundDc;

        public DrawingContext Back { get; }
        public DrawingContext Front { get; }
        public LoopPalette P { get; }

        /// <summary>Blur radius in stage px for the Back layer only. 0 = none.</summary>
        public double BackBlur { get; set; }

        // ---------------------------------------------------------------- scopes

        /// <summary>Opacity, then scale, then rotation about (cx, cy); undone on Dispose.</summary>
        public PopScope At(DrawingContext dc, double cx, double cy, double scale = 1, double opacity = 1, double rotateDeg = 0)
        {
            DrawingContext.PushedState? a = null, b = null, c = null;
            if (opacity < 1) a = dc.PushOpacity(Clamp(opacity));
            if (Math.Abs(scale - 1) > 1e-6) b = dc.PushTransform(About(Matrix.CreateScale(scale, scale), cx, cy));
            if (Math.Abs(rotateDeg) > 1e-6) c = dc.PushTransform(About(Matrix.CreateRotation(rotateDeg * Math.PI / 180), cx, cy));
            return new PopScope(a, b, c);
        }

        public PopScope Fade(DrawingContext dc, double opacity) => new(dc.PushOpacity(Clamp(opacity)), null, null);

        public PopScope Offset(DrawingContext dc, double dx, double dy) => new(dc.PushTransform(Matrix.CreateTranslation(dx, dy)), null, null);

        /// <summary>A geometry clip: the recorded (DrawingGroup) context implements only this one.</summary>
        public PopScope ClipTo(DrawingContext dc, Rect r, double radius) =>
            ClipTo(dc, new RectangleGeometry(r) { RadiusX = radius, RadiusY = radius });

        public PopScope ClipTo(DrawingContext dc, Geometry clip) => new(dc.PushGeometryClip(clip), null, null);

        private static Matrix About(Matrix m, double cx, double cy) =>
            Matrix.CreateTranslation(-cx, -cy) * m * Matrix.CreateTranslation(cx, cy);

        /// <summary>Pops up to three pushes in reverse order.</summary>
        public readonly struct PopScope : IDisposable
        {
            private readonly DrawingContext.PushedState? _a, _b, _c;
            public PopScope(DrawingContext.PushedState? a, DrawingContext.PushedState? b, DrawingContext.PushedState? c) { _a = a; _b = b; _c = c; }
            public void Dispose() { _c?.Dispose(); _b?.Dispose(); _a?.Dispose(); }
        }

        // ---------------------------------------------------------------- stage

        public void Ground() => (_groundDc ?? Back).DrawRectangle(GroundBrush, null, new Rect(0, 0, StageWidth, StageHeight));

        /// <summary>The fake desktop: a window with a bar, three dots, text lines, and a taskbar.</summary>
        public DesktopInfo Desktop(Rect? win = null) => Desktop(win, drawLines: true);

        /// <summary>With drawLines false the line rects are still returned so a scene can paint the
        /// lines itself (per-line fade or wobble).</summary>
        public DesktopInfo Desktop(Rect? win, bool drawLines, DrawingContext? dc = null)
        {
            dc ??= Back;
            var r = win ?? DefaultWindow;
            SoftShadow(dc, r, 8, 8, 24, 0x88);
            dc.DrawRoundedRectangle(P.Window, null, r, 8, 8);
            var lines = new List<Rect>();
            using (ClipTo(dc, r, 8))
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

        // ---------------------------------------------------------------- pointer

        public void Cursor(double x, double y, bool down)
        {
            var dc = Front;
            using (At(dc, x + 8, y + 11, down ? .85 : 1))
            {
                using (Offset(dc, x, y + 1)) dc.DrawGeometry(CursorShadow, null, CursorGeometry);
                using (Offset(dc, x, y)) dc.DrawGeometry(Brushes.White, CursorPen, CursorGeometry);
            }
        }

        public void Ripple(double x, double y, double k)
        {
            if (k <= 0 || k >= 1) return;
            var s = Lerp(.3, 1.7, EaseOut(k));
            using (Fade(Front, 1 - k))
                Front.DrawEllipse(null, new Pen(P.Mint, 2 * s), new Point(x, y), 14 * s, 14 * s);
        }

        /// <summary>A rising "+XP" style label. k 0..1 over its life, nothing outside (0,1).</summary>
        public void Floater(double x, double y, double k, string text, IBrush? color = null)
        {
            if (k <= 0 || k >= 1) return;
            var o = k < .15 ? k / .15 : 1 - Seg(k, .6, 1);
            var yy = y - 26 * EaseOut(k);
            using (Fade(Front, o))
            {
                DrawText(Front, text, x, yy + 3 + 1, 12, CursorShadow, Display, FontWeight.SemiBold, TextAlignment.Left);
                DrawText(Front, text, x, yy + 3, 12, color ?? P.Mint, Display, FontWeight.SemiBold, TextAlignment.Left);
            }
        }

        // ---------------------------------------------------------------- chrome

        public void Chip(double x, double y, string text, bool hot = false, double opacity = 1)
        {
            if (opacity <= 0) return;
            var ft = Format(text, 10, hot ? P.Text : P.Dim, Mono, FontWeight.Medium);
            var r = new Rect(x, y, ft.WidthIncludingTrailingWhitespace + 18, 23);
            using (Fade(Front, opacity))
            {
                Front.DrawRoundedRectangle(P.Glass, new Pen(hot ? P.Accent : P.Border, 1), Inset(r, .5), 11, 11);
                Front.DrawText(ft, new Point(x + 9, y + 1 + 3 + (15 - ft.Height) / 2));
            }
        }

        /// <summary>Width a <see cref="Chip"/> would take for this text.</summary>
        public double ChipWidth(string text) => Format(text, 10, P.Dim, Mono, FontWeight.Medium).WidthIncludingTrailingWhitespace + 18;

        public Point Slider(double x, double y, string label, double value, IBrush? border = null)
        {
            var dc = Front;
            var v = Clamp(value);
            dc.DrawRoundedRectangle(SliderFill, new Pen(border ?? P.Border, 1), Inset(new Rect(x, y, 130, 34), .5), 8.5, 8.5);
            DrawText(dc, label, x + 10, y + 5, 9, P.Dim, Mono, FontWeight.Medium, TextAlignment.Left);
            var track = new Rect(x + 10, y + 20, 80, 4);
            dc.DrawRoundedRectangle(P.Track, null, track, 2, 2);
            if (v > 0) dc.DrawRoundedRectangle(P.Accent, null, new Rect(track.X, track.Y, track.Width * v, 4), 2, 2);
            dc.DrawEllipse(P.White, null, new Point(track.X + track.Width * v, track.Y + 2), 6, 6);
            DrawText(dc, Math.Round(v * 100).ToString(CultureInfo.InvariantCulture) + "%", x + 130 - 9, y + 34 - 6 - 15, 10, P.Text, Mono, FontWeight.Medium, TextAlignment.Right);
            return SliderKnob(x, y, v);
        }

        public static Point SliderKnob(double x, double y, double value) => new(x + 9 + Clamp(value) * 81, y + 25);

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

        /// <summary>Full form: <paramref name="waves"/> arcs (2 or 3), each fading a little later than
        /// the one before, and a scale for the glyph (Brain Drain's drop swells on a clip).</summary>
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
                    c.BeginFigure(new Point(cx, cy - rad), false);
                    c.ArcTo(new Point(cx, cy + rad), new Size(rad, rad), 0, false, SweepDirection.Clockwise);
                    c.EndFigure(false);
                }
                using (Fade(dc, o)) dc.DrawGeometry(null, pen, g);
            }
        }

        /// <summary>A rounded dark card (lock card, question card) with its drop shadow.</summary>
        public void Card(Rect r, IBrush border, double opacity = 1)
        {
            if (opacity <= 0) return;
            using (Fade(Front, opacity))
            {
                SoftShadow(Front, r, 12, 14, 34, 0xCC);
                Front.DrawRoundedRectangle(P.Panel, new Pen(border, 1), Inset(r, .5), 11.5, 11.5);
            }
        }

        /// <summary>A flash photo: white 3px frame, rounded 4, soft shadow, one of three looks.
        /// <paramref name="shadow"/> false for a photo that sits inside something (a thumbnail grid).</summary>
        public void Photo(Rect r, PhotoLook look, bool shadow = true) => Photo(Front, r, look, shadow);

        public void Photo(DrawingContext dc, Rect r, PhotoLook look, bool shadow = true)
        {
            if (shadow) SoftShadow(dc, r, 4, 10, 22, 0xAA);
            dc.DrawRoundedRectangle(Brushes.White, null, r, 4, 4);
            var inner = Inset(r, 3);
            using (ClipTo(dc, inner, 1.5))
            {
                switch (look)
                {
                    case PhotoLook.Pink:
                        dc.DrawRectangle(PhotoPinkFill, null, inner);
                        dc.DrawEllipse(PhotoPinkShine, null, inner.Center, inner.Width * .2, inner.Height * .22);
                        break;
                    case PhotoLook.Stripes:
                        dc.DrawRectangle(PhotoStripeB, null, inner);
                        PhotoWedges(dc, inner);
                        break;
                    default:
                        dc.DrawRectangle(PhotoSeaFill, null, inner);
                        dc.DrawRectangle(PhotoSeaShade, null, new Rect(inner.X, inner.Bottom - inner.Height * .4, inner.Width, inner.Height * .4));
                        break;
                }
            }
        }

        /// <summary>repeating-conic-gradient: 18 wedges of 20 degrees, pink every other one, from 12 o'clock.</summary>
        private static void PhotoWedges(DrawingContext dc, Rect r)
        {
            var c = r.Center;
            double rad = Math.Sqrt(r.Width * r.Width + r.Height * r.Height);
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                for (int i = 0; i < 18; i += 2)
                {
                    double a0 = (i * 20 - 90) * Math.PI / 180, a1 = ((i + 1) * 20 - 90) * Math.PI / 180;
                    ctx.BeginFigure(c, true);
                    ctx.LineTo(new Point(c.X + Math.Cos(a0) * rad, c.Y + Math.Sin(a0) * rad));
                    ctx.LineTo(new Point(c.X + Math.Cos(a1) * rad, c.Y + Math.Sin(a1) * rad));
                    ctx.EndFigure(true);
                }
            }
            dc.DrawGeometry(PhotoStripeA, null, g);
        }

        // ---------------------------------------------------------------- text

        /// <summary>Text in the display face (Fredoka). (x,y) is the top of the line; with Center or
        /// Right alignment x is the centre or the right edge.</summary>
        public void Text(string s, double x, double y, double size, IBrush brush, bool bold = false,
            TextAlignment align = TextAlignment.Left, double opacity = 1)
        {
            if (opacity <= 0 || string.IsNullOrEmpty(s)) return;
            using (Fade(Front, opacity))
                DrawText(Front, s, x, y, size, brush, Display, bold ? FontWeight.SemiBold : FontWeight.Medium, align);
        }

        /// <summary>Measures text as <see cref="DrawText"/> would draw it.</summary>
        public Size Measure(string s, double size, FontFamily? family = null, bool bold = false)
        {
            var ft = Format(s, size, P.Text, family ?? Display, bold ? FontWeight.SemiBold : FontWeight.Medium);
            return new Size(ft.WidthIncludingTrailingWhitespace, ft.Height);
        }

        /// <summary>WPF FormattedText.TextAlignment moves the line around x (Right ends at x).</summary>
        public void DrawText(DrawingContext dc, string s, double x, double y, double size, IBrush brush,
            FontFamily family, FontWeight weight, TextAlignment align)
        {
            if (string.IsNullOrEmpty(s)) return;
            var ft = Format(s, size, brush, family, weight);
            var w = ft.WidthIncludingTrailingWhitespace;
            dc.DrawText(ft, new Point(align == TextAlignment.Right ? x - w : align == TextAlignment.Center ? x - w / 2 : x, y));
        }

        public FormattedText Format(string s, double size, IBrush brush, FontFamily family, FontWeight weight) =>
            new(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(family, FontStyle.Normal, weight), size, brush);

        // ---------------------------------------------------------------- helpers

        /// <summary>A straight line with round caps.</summary>
        public void Line(DrawingContext dc, Point a, Point b, IBrush brush, double thickness = 1) =>
            dc.DrawLine(new Pen(brush, thickness) { LineCap = PenLineCap.Round }, a, b);

        public static void SoftShadow(DrawingContext dc, Rect r, double radius, double dy, double blur, byte alpha)
        {
            const int layers = 5;
            var brush = new ImmutableSolidColorBrush(Color.FromArgb((byte)(alpha / (layers + 1.5)), 0, 0, 0));
            for (int i = layers; i >= 1; i--)
            {
                double grow = blur * i / layers / 2;
                dc.DrawRoundedRectangle(brush, null, new Rect(r.X - grow, r.Y + dy - grow, r.Width + grow * 2, r.Height + grow * 2), radius + grow, radius + grow);
            }
        }

        public static Rect Inset(Rect r, double d) =>
            new(r.X + d, r.Y + d, Math.Max(0, r.Width - 2 * d), Math.Max(0, r.Height - 2 * d));
    }

    /// <summary>WPF's DrawRoundedRectangle. Avalonia's recorded (DrawingGroup) context drops the
    /// corner radius of DrawRectangle(.., rx, ry) and draws square corners, so rounded shapes go
    /// through a RectangleGeometry, which keeps them.</summary>
    public static class LoopDrawing
    {
        public static void DrawRoundedRectangle(this DrawingContext dc, IBrush? brush, IPen? pen, Rect r, double rx, double ry) =>
            dc.DrawGeometry(brush, pen, new RectangleGeometry(r) { RadiusX = rx, RadiusY = ry });
    }
}
