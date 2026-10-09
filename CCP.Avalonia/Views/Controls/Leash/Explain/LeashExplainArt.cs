// PORTED from WPF 7.1.5 Controls/Leash/Explain/LeashExplainArt.cs: the four explainer pictures as
// flat vector shapes on a 120 x 80 canvas (people = head and shoulders, the leash a gold line, the
// collar a gold ring). Same path data. The resting state is the finished picture (leash on, strict
// bar picked, leash cut), which is what Motion Off shows.
using System;
using System.Globalization;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Avalonia.Views.Controls.Leash.Explain;

internal static class LeashExplainArt
{
    public const double W = 120, H = 80;

    public static IBrush GoldBrush => FriendsDrawer.Gold;
    private static readonly IBrush Line2Brush = FriendsDrawer.Line2, LineBrush = FriendsDrawer.Line;

    private static string F(FormattableString s) => s.ToString(CultureInfo.InvariantCulture);

    // ---- 1. an offer: a friend holds out the leash, you are the outline on the right ----------

    public static Canvas Offer()
    {
        var c = NewCanvas("offer");
        Person(c, 30, FriendsDrawer.Lilac, null);
        Person(c, 95, null, FriendsDrawer.Pink, dashed: true);
        c.Children.Add(PathOf("M42,50 C52,62 60,32 69,42", GoldBrush, 2.6));
        c.Children.Add(Ring(73.5, 43, 4.5, GoldBrush, 2.4));
        c.Children.Add(Spark(62, 18, 5, FriendsDrawer.Pink));
        c.Children.Add(Spark(74, 12, 3, FriendsDrawer.Lilac));
        return c;
    }

    // ---- 2. you put it on and pick how strict --------------------------------------------------

    public static Canvas PutOn(out Rectangle[] bars)
    {
        var c = NewCanvas("puton");
        Person(c, 34, FriendsDrawer.Pink, null);
        var collar = new Ellipse { Width = 20, Height = 6, Stroke = GoldBrush, StrokeThickness = 2.6 };
        Canvas.SetLeft(collar, 24);
        Canvas.SetTop(collar, 39);
        c.Children.Add(collar);
        c.Children.Add(Disc(34, 48, 2.8, GoldBrush));

        bars = new Rectangle[3];
        double[] heights = { 14, 22, 30 };
        for (int i = 0; i < 3; i++)
        {
            bool picked = i == 1;
            var bar = new Rectangle
            {
                Width = 10,
                Height = heights[i],
                RadiusX = 3,
                RadiusY = 3,
                Fill = picked ? FriendsDrawer.Pink : Line2Brush,
                RenderTransformOrigin = new RelativePoint(0.5, 1, RelativeUnit.Relative),
                RenderTransform = new ScaleTransform(1, 1),
                Tag = picked ? "bar-picked" : "bar",
            };
            Canvas.SetLeft(bar, 68 + i * 15);
            Canvas.SetTop(bar, 62 - heights[i]);
            c.Children.Add(bar);
            bars[i] = bar;
        }
        c.Children.Add(PathOf("M79,28 L83,33 L87,28", FriendsDrawer.Pink, 2));
        c.Children.Add(new Line { StartPoint = new Point(64, 65), EndPoint = new Point(110, 65), Stroke = LineBrush, StrokeThickness = 1.5 });
        return c;
    }

    // ---- 3. they see your day and steer: eye, the week strip, task / reward / punishment -------

    public static Canvas SeeAndSteer(out Control eye)
    {
        var c = NewCanvas("see");
        var eyeHost = new Canvas { Width = W, Height = 30 };
        eyeHost.Children.Add(PathOf("M38,17 Q60,0 82,17 Q60,34 38,17 Z", FriendsDrawer.Lilac, 2, new SolidColorBrush(Color.FromArgb(0x22, 0xB9, 0x9C, 0xFF))));
        eyeHost.Children.Add(Disc(60, 17, 6, FriendsDrawer.Lilac));
        eyeHost.Children.Add(Disc(62, 15, 1.8, FriendsDrawer.Text));
        eyeHost.RenderTransformOrigin = new RelativePoint(0.5, 0.55, RelativeUnit.Relative);
        eyeHost.RenderTransform = new ScaleTransform(1, 1);
        c.Children.Add(eyeHost);
        eye = eyeHost;

        IBrush?[] marks =
        {
            FriendsDrawer.Mint, FriendsDrawer.Mint, Line2Brush, FriendsDrawer.Red,
            FriendsDrawer.Mint, FriendsDrawer.Mint, null,
        };
        const double sq = 9, gap = 3;
        double x0 = 60 - (7 * sq + 6 * gap) / 2;
        for (int i = 0; i < 7; i++)
        {
            var r = new Rectangle { Width = sq, Height = sq, RadiusX = 2, RadiusY = 2 };
            if (marks[i] == null) { r.Stroke = FriendsDrawer.Pink; r.StrokeThickness = 1.6; }
            else r.Fill = marks[i];
            Canvas.SetLeft(r, x0 + i * (sq + gap));
            Canvas.SetTop(r, 36);
            c.Children.Add(r);
        }

        c.Children.Add(Chip(34, 64, Check(34, 64, FriendsDrawer.Lilac), Color.FromRgb(0xB9, 0x9C, 0xFF), Loc.Get("leash_explain_task")));
        c.Children.Add(Chip(60, 64, Star(60, 64, 5.5, FriendsDrawer.Gold), Color.FromRgb(0xFF, 0xCF, 0x6B), Loc.Get("leash_explain_reward")));
        c.Children.Add(Chip(86, 64, Bolt(86, 64, FriendsDrawer.Red), Color.FromRgb(0xFF, 0x5F, 0x7A), Loc.Get("leash_explain_punish")));
        return c;
    }

    // ---- 4. scissors: you cut it, any time ------------------------------------------------------

    public sealed class Scissors
    {
        public required Canvas Canvas { get; init; }
        public required RotateTransform BladeA { get; init; }
        public required RotateTransform BladeB { get; init; }
        public required TranslateTransform LeftHalf { get; init; }
        public required TranslateTransform RightHalf { get; init; }
        public const double PivotX = 60, PivotY = 36, CutY = 64;
    }

    public static Scissors Cut()
    {
        var c = NewCanvas("cut");
        var leftT = new TranslateTransform();
        var rightT = new TranslateTransform();
        var left = new Canvas { RenderTransform = leftT, Width = W, Height = H };
        left.Children.Add(new Line { StartPoint = new Point(6, 64), EndPoint = new Point(52, 64), Stroke = GoldBrush, StrokeThickness = 2.6, StrokeLineCap = PenLineCap.Round });
        left.Children.Add(PathOf("M52,64 L56,61 M52,64 L56,67", GoldBrush, 1.4));
        var right = new Canvas { RenderTransform = rightT, Width = W, Height = H };
        right.Children.Add(new Line { StartPoint = new Point(68, 64), EndPoint = new Point(114, 64), Stroke = GoldBrush, StrokeThickness = 2.6, StrokeLineCap = PenLineCap.Round });
        right.Children.Add(PathOf("M68,64 L64,61 M68,64 L64,67", GoldBrush, 1.4));
        c.Children.Add(left);
        c.Children.Add(right);

        var steel = FriendsDrawer.Mint;
        var ink = new SolidColorBrush(Color.FromRgb(0x0E, 0x3A, 0x2E));
        var a = new RotateTransform { CenterX = Scissors.PivotX, CenterY = Scissors.PivotY };
        var b = new RotateTransform { CenterX = Scissors.PivotX, CenterY = Scissors.PivotY };
        // The pieces are full-size canvases with a top-left origin so CenterX/Y are canvas units.
        var pa = new Canvas { RenderTransform = a, Width = W, Height = H, RenderTransformOrigin = new RelativePoint(0, 0, RelativeUnit.Absolute) };
        pa.Children.Add(PathOf("M58,37 L97,29 L95,33 L60,40 Z", ink, 1, steel));
        pa.Children.Add(PathOf("M59,37 L45,46", steel, 3.2));
        pa.Children.Add(Ring(39, 50, 7, steel, 3));
        var pb = new Canvas { RenderTransform = b, Width = W, Height = H, RenderTransformOrigin = new RelativePoint(0, 0, RelativeUnit.Absolute) };
        pb.Children.Add(PathOf("M58,35 L97,43 L95,39 L60,32 Z", ink, 1, steel));
        pb.Children.Add(PathOf("M59,35 L45,26", steel, 3.2));
        pb.Children.Add(Ring(39, 22, 7, steel, 3));
        c.Children.Add(pb);
        c.Children.Add(pa);
        c.Children.Add(Disc(Scissors.PivotX, Scissors.PivotY, 2.4, ink));
        return new Scissors { Canvas = c, BladeA = a, BladeB = b, LeftHalf = leftT, RightHalf = rightT };
    }

    // ---- bullet glyphs --------------------------------------------------------------------------

    public static Control EyeGlyph()
    {
        var c = new Canvas { Width = 18, Height = 12 };
        c.Children.Add(PathOf("M1,6 Q9,-1 17,6 Q9,13 1,6 Z", FriendsDrawer.Lilac, 1.5));
        c.Children.Add(Disc(9, 6, 2.6, FriendsDrawer.Lilac));
        return c;
    }

    public static Control ScissorsGlyph()
    {
        var c = new Canvas { Width = 18, Height = 14 };
        var m = FriendsDrawer.Mint;
        c.Children.Add(PathOf("M8,7 L17,3 M8,7 L17,11", m, 1.8));
        c.Children.Add(Ring(4, 3.5, 2.6, m, 1.5));
        c.Children.Add(Ring(4, 10.5, 2.6, m, 1.5));
        return c;
    }

    // ---- primitives ------------------------------------------------------------------------------

    private static Canvas NewCanvas(string tag) => new() { Width = W, Height = H, Tag = "leash-explain-art:" + tag, ClipToBounds = false };

    private static void Person(Canvas c, double x, IBrush? fill, IBrush? stroke, bool dashed = false)
    {
        var head = new Ellipse { Width = 17, Height = 17, Fill = fill, Stroke = stroke, StrokeThickness = stroke != null ? 2 : 0 };
        Canvas.SetLeft(head, x - 8.5);
        Canvas.SetTop(head, 18);
        var shoulders = new Path
        {
            Data = Geometry.Parse(F($"M{x - 15},62 C{x - 15},46 {x + 15},46 {x + 15},62 Z")),
            Fill = fill,
            Stroke = stroke,
            StrokeThickness = stroke != null ? 2 : 0,
        };
        if (dashed)
        {
            head.StrokeDashArray = new AvaloniaList<double> { 2.2, 1.6 };
            shoulders.StrokeDashArray = new AvaloniaList<double> { 2.2, 1.6 };
        }
        c.Children.Add(head);
        c.Children.Add(shoulders);
    }

    public static Path PathOf(string data, IBrush stroke, double thickness, IBrush? fill = null) => new()
    {
        Data = Geometry.Parse(data),
        Stroke = stroke,
        StrokeThickness = thickness,
        Fill = fill,
        StrokeJoin = PenLineJoin.Round,
        StrokeLineCap = PenLineCap.Round,
    };

    public static Ellipse Disc(double cx, double cy, double r, IBrush fill)
    {
        var e = new Ellipse { Width = r * 2, Height = r * 2, Fill = fill };
        Canvas.SetLeft(e, cx - r);
        Canvas.SetTop(e, cy - r);
        return e;
    }

    private static Ellipse Ring(double cx, double cy, double r, IBrush stroke, double t)
    {
        var e = new Ellipse { Width = r * 2, Height = r * 2, Stroke = stroke, StrokeThickness = t };
        Canvas.SetLeft(e, cx - r);
        Canvas.SetTop(e, cy - r);
        return e;
    }

    private static Path Spark(double cx, double cy, double r, IBrush fill)
        => PathOf(F($"M{cx},{cy - r} Q{cx},{cy} {cx + r},{cy} Q{cx},{cy} {cx},{cy + r} Q{cx},{cy} {cx - r},{cy} Q{cx},{cy} {cx},{cy - r} Z"), fill, 0.5, fill);

    private static Path Check(double cx, double cy, IBrush b) => PathOf(F($"M{cx - 4},{cy} L{cx - 1},{cy + 3} L{cx + 4.5},{cy - 3.5}"), b, 2);

    private static Path Bolt(double cx, double cy, IBrush b)
        => PathOf(F($"M{cx + 1.5},{cy - 6} L{cx - 3.5},{cy + 1} L{cx},{cy + 1} L{cx - 1.5},{cy + 6} L{cx + 3.5},{cy - 1} L{cx},{cy - 1} Z"), b, 0.8, b);

    private static Path Star(double cx, double cy, double r, IBrush b)
    {
        var pts = new System.Text.StringBuilder();
        for (int i = 0; i < 10; i++)
        {
            double rr = i % 2 == 0 ? r : r * 0.45;
            double ang = -Math.PI / 2 + i * Math.PI / 5;
            pts.Append(i == 0 ? "M" : " L");
            pts.Append(F($"{cx + rr * Math.Cos(ang):0.##},{cy + rr * Math.Sin(ang):0.##}"));
        }
        pts.Append(" Z");
        return PathOf(pts.ToString(), b, 0.6, b);
    }

    /// <summary>A round chip behind a small icon, tinted with the icon's colour, with a tooltip.</summary>
    private static Canvas Chip(double cx, double cy, Control icon, Color tint, string tip)
    {
        var host = new Canvas { Tag = "leash-explain-chip" };
        ToolTip.SetTip(host, tip);
        var bg = new Ellipse
        {
            Width = 20,
            Height = 20,
            Fill = new SolidColorBrush(Color.FromArgb(0x2A, tint.R, tint.G, tint.B)),
            Stroke = new SolidColorBrush(Color.FromArgb(0x88, tint.R, tint.G, tint.B)),
            StrokeThickness = 1.2,
        };
        Canvas.SetLeft(bg, cx - 10);
        Canvas.SetTop(bg, cy - 10);
        host.Children.Add(bg);
        host.Children.Add(icon);
        return host;
    }
}
