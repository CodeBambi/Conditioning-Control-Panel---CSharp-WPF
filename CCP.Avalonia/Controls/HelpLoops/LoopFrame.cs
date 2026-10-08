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
    /// <see cref="Front"/> everything over it. Only the primitives the ported scenes use are here;
    /// the rest of WPF's kit (bubble, speaker, card, floater, eye, gaze, meter, typed text) comes
    /// across with the scenes that need it.
    /// </summary>
    public sealed class LoopFrame
    {
        public const double StageWidth = 480;
        public const double StageHeight = 270;

        public static readonly Rect DefaultWindow = new(60, 28, 320, 190);

        public static readonly FontFamily Display = new("Fredoka, Segoe UI");
        public static readonly FontFamily Mono = new("Consolas, Courier New, monospace");
        public static readonly FontFamily Body = new("Segoe UI");

        private static readonly Geometry CursorGeometry = Geometry.Parse("M1,1 L1,17 L5,13 L8,20 L11,19 L8,12 L14,12 Z");
        private static readonly double[] LineWidths = { 88, 64, 92, 50, 76, 70, 40 };

        private static readonly IBrush CursorShadow = LoopPalette.Solid("#000000aa");
        private static readonly IPen CursorPen = new ImmutablePen(LoopPalette.Solid("#1a1030").ToImmutable(), 1.4, lineJoin: PenLineJoin.Round);
        private static readonly IBrush TaskbarFill = LoopPalette.Solid("#0d0a18cc");
        private static readonly IBrush SliderFill = LoopPalette.Solid("#0d0a18e6");
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
        {
            Back = back;
            Front = front;
            P = palette;
        }

        public DrawingContext Back { get; }
        public DrawingContext Front { get; }
        public LoopPalette P { get; }

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
            new(dc.PushGeometryClip(new RectangleGeometry(r) { RadiusX = radius, RadiusY = radius }), null, null);

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

        public void Ground() => Back.DrawRectangle(GroundBrush, null, new Rect(0, 0, StageWidth, StageHeight));

        /// <summary>The fake desktop: a window with a bar, three dots, text lines, and a taskbar.</summary>
        public DesktopInfo Desktop(Rect? win = null)
        {
            var dc = Back;
            var r = win ?? DefaultWindow;
            SoftShadow(dc, r, 8, 8, 24, 0x88);
            dc.DrawRectangle(P.Window, null, r, 8, 8);
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
                    dc.DrawRectangle(i % 3 == 1 ? P.LineB : P.LineA, null, lr, 3.5, 3.5);
                }
            }
            dc.DrawRectangle(null, new Pen(P.WindowBorder, 1), Inset(r, .5), 7.5, 7.5);

            dc.DrawRectangle(TaskbarFill, null, new Rect(0, StageHeight - 20, StageWidth, 20));
            double x0 = (StageWidth - (5 * 11 + 4 * 8)) / 2;
            for (int i = 0; i < 5; i++)
                dc.DrawRectangle(P.Track, null, new Rect(x0 + i * 19, StageHeight - 20 + 4.5, 11, 11), 3, 3);
            return new DesktopInfo(r, lines);
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

        // ---------------------------------------------------------------- chrome

        public void Chip(double x, double y, string text, bool hot = false, double opacity = 1)
        {
            if (opacity <= 0) return;
            var ft = Format(text, 10, hot ? P.Text : P.Dim, Mono, FontWeight.Medium);
            var r = new Rect(x, y, ft.WidthIncludingTrailingWhitespace + 18, 23);
            using (Fade(Front, opacity))
            {
                Front.DrawRectangle(P.Glass, new Pen(hot ? P.Accent : P.Border, 1), Inset(r, .5), 11, 11);
                Front.DrawText(ft, new Point(x + 9, y + 1 + 3 + (15 - ft.Height) / 2));
            }
        }

        public Point Slider(double x, double y, string label, double value)
        {
            var dc = Front;
            var v = Clamp(value);
            dc.DrawRectangle(SliderFill, new Pen(P.Border, 1), Inset(new Rect(x, y, 130, 34), .5), 8.5, 8.5);
            DrawText(dc, label, x + 10, y + 5, 9, P.Dim, Mono, FontWeight.Medium, TextAlignment.Left);
            var track = new Rect(x + 10, y + 20, 80, 4);
            dc.DrawRectangle(P.Track, null, track, 2, 2);
            if (v > 0) dc.DrawRectangle(P.Accent, null, new Rect(track.X, track.Y, track.Width * v, 4), 2, 2);
            dc.DrawEllipse(P.White, null, new Point(track.X + track.Width * v, track.Y + 2), 6, 6);
            DrawText(dc, Math.Round(v * 100).ToString(CultureInfo.InvariantCulture) + "%", x + 130 - 9, y + 34 - 6 - 15, 10, P.Text, Mono, FontWeight.Medium, TextAlignment.Right);
            return SliderKnob(x, y, v);
        }

        public static Point SliderKnob(double x, double y, double value) => new(x + 9 + Clamp(value) * 81, y + 25);

        /// <summary>A flash photo: white 3px frame, rounded 4, soft shadow, one of three looks.</summary>
        public void Photo(DrawingContext dc, Rect r, PhotoLook look)
        {
            SoftShadow(dc, r, 4, 10, 22, 0xAA);
            dc.DrawRectangle(Brushes.White, null, r, 4, 4);
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

        public static void SoftShadow(DrawingContext dc, Rect r, double radius, double dy, double blur, byte alpha)
        {
            const int layers = 5;
            var brush = new ImmutableSolidColorBrush(Color.FromArgb((byte)(alpha / (layers + 1.5)), 0, 0, 0));
            for (int i = layers; i >= 1; i--)
            {
                double grow = blur * i / layers / 2;
                dc.DrawRectangle(brush, null, new Rect(r.X - grow, r.Y + dy - grow, r.Width + grow * 2, r.Height + grow * 2), radius + grow, radius + grow);
            }
        }

        public static Rect Inset(Rect r, double d) =>
            new(r.X + d, r.Y + d, Math.Max(0, r.Width - 2 * d), Math.Max(0, r.Height - 2 * d));
    }
}
