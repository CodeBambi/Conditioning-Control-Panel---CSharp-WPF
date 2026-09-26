using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using ConditioningControlPanel.Controls.Friends;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Controls.Leash.Explain;

/// <summary>
/// The four explainer pictures, drawn as flat vector shapes on a 120 x 80 canvas: people are a
/// head and shoulders, the leash is a gold line, the collar a gold ring. No bitmaps, no glow
/// fog, so it stays crisp at any size and reads the same with motion off.
///
/// <para>Each builder returns the canvas plus the few named parts the card animates. The
/// resting state (what a still frame and Motion Off show) is always the finished picture: the
/// leash is on, the strict bar is picked, the leash is cut.</para>
/// </summary>
internal static class LeashExplainArt
{
    public const double W = 120, H = 80;

    public static readonly Color Gold = FriendsLook.Gold;
    public static readonly Brush GoldBrush = FriendsLook.GoldBrush;

    // ---- 1. an offer: a friend holds out the leash, you are the outline on the right ----------

    public static Canvas Offer()
    {
        var c = NewCanvas("offer");
        Person(c, 30, FriendsLook.LilacBrush, null);
        Person(c, 95, null, FriendsLook.PinkBrush, dashed: true);
        // The leash leaves the friend's hand and ends in an open loop, held out.
        var leash = PathOf("M42,50 C52,62 60,32 69,42", GoldBrush, 2.6);
        c.Children.Add(leash);
        c.Children.Add(Ring(73.5, 43, 4.5, GoldBrush, 2.4));
        c.Children.Add(Spark(62, 18, 5, FriendsLook.PinkBrush));
        c.Children.Add(Spark(74, 12, 3, FriendsLook.LilacBrush));
        return c;
    }

    // ---- 2. you put it on and pick how strict --------------------------------------------------

    /// <param name="bars">The three strictness bars, soft to strict, returned for the card's pop.</param>
    public static Canvas PutOn(out Rectangle[] bars)
    {
        var c = NewCanvas("puton");
        Person(c, 34, FriendsLook.PinkBrush, null);
        // Collar: a gold band at the neck with a round tag.
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
                Fill = picked ? FriendsLook.PinkBrush : FriendsLook.Line2Brush,
                RenderTransformOrigin = new Point(0.5, 1),
                RenderTransform = new ScaleTransform(1, 1),
            };
            bar.Tag = picked ? "bar-picked" : "bar";
            Canvas.SetLeft(bar, 68 + i * 15);
            Canvas.SetTop(bar, 62 - heights[i]);
            c.Children.Add(bar);
            bars[i] = bar;
        }
        // A small caret over the picked bar: "this one".
        c.Children.Add(PathOf("M79,28 L83,33 L87,28", FriendsLook.PinkBrush, 2));
        c.Children.Add(new Line { X1 = 64, Y1 = 65, X2 = 110, Y2 = 65, Stroke = FriendsLook.LineBrush, StrokeThickness = 1.5 });
        return c;
    }

    // ---- 3. they see your day and steer: eye, the week strip, task / reward / punishment -------

    public static Canvas SeeAndSteer(out FrameworkElement eye)
    {
        var c = NewCanvas("see");
        var eyeHost = new Canvas { Width = W, Height = 30 };
        eyeHost.Children.Add(PathOf("M38,17 Q60,0 82,17 Q60,34 38,17 Z", FriendsLook.LilacBrush, 2,
            FriendsLook.Frozen(Color.FromArgb(0x22, 0xB9, 0x9C, 0xFF))));
        eyeHost.Children.Add(Disc(60, 17, 6, FriendsLook.LilacBrush));
        eyeHost.Children.Add(Disc(62, 15, 1.8, FriendsLook.Frozen(FriendsLook.Text)));
        eyeHost.RenderTransformOrigin = new Point(0.5, 0.55);
        eyeHost.RenderTransform = new ScaleTransform(1, 1);
        c.Children.Add(eyeHost);
        eye = eyeHost;

        // The week, as the holder's card draws it: did / did / idle / punished / did / did / today.
        Brush[] marks =
        {
            FriendsLook.MintBrush, FriendsLook.MintBrush, FriendsLook.Line2Brush, FriendsLook.RedBrush,
            FriendsLook.MintBrush, FriendsLook.MintBrush, null!,
        };
        const double sq = 9, gap = 3;
        double x0 = 60 - (7 * sq + 6 * gap) / 2;
        for (int i = 0; i < 7; i++)
        {
            var r = new Rectangle { Width = sq, Height = sq, RadiusX = 2, RadiusY = 2 };
            if (marks[i] == null) { r.Stroke = FriendsLook.PinkBrush; r.StrokeThickness = 1.6; }
            else r.Fill = marks[i];
            Canvas.SetLeft(r, x0 + i * (sq + gap));
            Canvas.SetTop(r, 36);
            c.Children.Add(r);
        }

        c.Children.Add(Chip(34, 64, Check(34, 64, FriendsLook.LilacBrush), FriendsLook.Lilac, Loc.Get("leash_explain_task")));
        c.Children.Add(Chip(60, 64, Star(60, 64, 5.5, FriendsLook.GoldBrush), FriendsLook.Gold, Loc.Get("leash_explain_reward")));
        c.Children.Add(Chip(86, 64, Bolt(86, 64, FriendsLook.RedBrush), FriendsLook.Red, Loc.Get("leash_explain_punish")));
        return c;
    }

    // ---- 4. scissors: you cut it, any time ------------------------------------------------------

    public sealed class Scissors
    {
        public required Canvas Canvas { get; init; }
        /// <summary>Upper blade + lower handle: one steel piece, rotates about the pivot.</summary>
        public required RotateTransform BladeA { get; init; }
        /// <summary>Lower blade + upper handle.</summary>
        public required RotateTransform BladeB { get; init; }
        public required TranslateTransform LeftHalf { get; init; }
        public required TranslateTransform RightHalf { get; init; }
        public const double PivotX = 60, PivotY = 36, CutY = 64;
    }

    public static Scissors Cut()
    {
        var c = NewCanvas("cut");
        // The leash, already in two pieces with frayed ends.
        var leftT = new TranslateTransform();
        var rightT = new TranslateTransform();
        var left = new Canvas { RenderTransform = leftT };
        left.Children.Add(new Line { X1 = 6, Y1 = 64, X2 = 52, Y2 = 64, Stroke = GoldBrush, StrokeThickness = 2.6, StrokeStartLineCap = PenLineCap.Round });
        left.Children.Add(PathOf("M52,64 L56,61 M52,64 L56,67", GoldBrush, 1.4));
        var right = new Canvas { RenderTransform = rightT };
        right.Children.Add(new Line { X1 = 68, Y1 = 64, X2 = 114, Y2 = 64, Stroke = GoldBrush, StrokeThickness = 2.6, StrokeEndLineCap = PenLineCap.Round });
        right.Children.Add(PathOf("M68,64 L64,61 M68,64 L64,67", GoldBrush, 1.4));
        c.Children.Add(left);
        c.Children.Add(right);

        var steel = FriendsLook.MintBrush;
        var ink = FriendsLook.Frozen(FriendsLook.Rgb(0x0E, 0x3A, 0x2E));
        var a = new RotateTransform(0, Scissors.PivotX, Scissors.PivotY);
        var b = new RotateTransform(0, Scissors.PivotX, Scissors.PivotY);

        // Piece A: upper blade, handle ring below-left.
        var pa = new Canvas { RenderTransform = a };
        pa.Children.Add(PathOf("M58,37 L97,29 L95,33 L60,40 Z", ink, 1, steel));
        pa.Children.Add(PathOf("M59,37 L45,46", steel, 3.2));
        pa.Children.Add(Ring(39, 50, 7, steel, 3));
        // Piece B: lower blade, handle ring above-left.
        var pb = new Canvas { RenderTransform = b };
        pb.Children.Add(PathOf("M58,35 L97,43 L95,39 L60,32 Z", ink, 1, steel));
        pb.Children.Add(PathOf("M59,35 L45,26", steel, 3.2));
        pb.Children.Add(Ring(39, 22, 7, steel, 3));
        c.Children.Add(pb);
        c.Children.Add(pa);
        c.Children.Add(Disc(Scissors.PivotX, Scissors.PivotY, 2.4, ink));
        return new Scissors { Canvas = c, BladeA = a, BladeB = b, LeftHalf = leftT, RightHalf = rightT };
    }

    // ---- bullet glyphs (small copies of panel 3 and panel 4) ------------------------------------

    public static FrameworkElement EyeGlyph()
    {
        var c = new Canvas { Width = 18, Height = 12 };
        c.Children.Add(PathOf("M1,6 Q9,-1 17,6 Q9,13 1,6 Z", FriendsLook.LilacBrush, 1.5));
        c.Children.Add(Disc(9, 6, 2.6, FriendsLook.LilacBrush));
        return c;
    }

    public static FrameworkElement ScissorsGlyph()
    {
        var c = new Canvas { Width = 18, Height = 14 };
        var m = FriendsLook.MintBrush;
        c.Children.Add(PathOf("M8,7 L17,3 M8,7 L17,11", m, 1.8));
        c.Children.Add(Ring(4, 3.5, 2.6, m, 1.5));
        c.Children.Add(Ring(4, 10.5, 2.6, m, 1.5));
        return c;
    }

    // ---- primitives ------------------------------------------------------------------------------

    private static Canvas NewCanvas(string tag) => new() { Width = W, Height = H, Tag = "leash-explain-art:" + tag, ClipToBounds = false };

    /// <summary>Head and shoulders centred on <paramref name="x"/>.</summary>
    private static void Person(Canvas c, double x, Brush? fill, Brush? stroke, bool dashed = false)
    {
        var head = new Ellipse { Width = 17, Height = 17, Fill = fill, Stroke = stroke, StrokeThickness = stroke != null ? 2 : 0 };
        Canvas.SetLeft(head, x - 8.5);
        Canvas.SetTop(head, 18);
        var shoulders = new Path
        {
            Data = Geometry.Parse(System.FormattableString.Invariant($"M{x - 15},62 C{x - 15},46 {x + 15},46 {x + 15},62 Z")),
            Fill = fill,
            Stroke = stroke,
            StrokeThickness = stroke != null ? 2 : 0,
        };
        if (dashed)
        {
            head.StrokeDashArray = new DoubleCollection { 2.2, 1.6 };
            shoulders.StrokeDashArray = new DoubleCollection { 2.2, 1.6 };
        }
        c.Children.Add(head);
        c.Children.Add(shoulders);
    }

    public static Path PathOf(string data, Brush stroke, double thickness, Brush? fill = null)
        => new()
        {
            Data = Geometry.Parse(data),
            Stroke = stroke,
            StrokeThickness = thickness,
            Fill = fill,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
        };

    public static Ellipse Disc(double cx, double cy, double r, Brush fill)
    {
        var e = new Ellipse { Width = r * 2, Height = r * 2, Fill = fill };
        Canvas.SetLeft(e, cx - r);
        Canvas.SetTop(e, cy - r);
        return e;
    }

    private static Ellipse Ring(double cx, double cy, double r, Brush stroke, double t)
    {
        var e = new Ellipse { Width = r * 2, Height = r * 2, Stroke = stroke, StrokeThickness = t };
        Canvas.SetLeft(e, cx - r);
        Canvas.SetTop(e, cy - r);
        return e;
    }

    private static Path Spark(double cx, double cy, double r, Brush fill)
        => PathOf(System.FormattableString.Invariant($"M{cx},{cy - r} Q{cx},{cy} {cx + r},{cy} Q{cx},{cy} {cx},{cy + r} Q{cx},{cy} {cx - r},{cy} Q{cx},{cy} {cx},{cy - r} Z"), fill, 0.5, fill);

    private static Path Check(double cx, double cy, Brush b) => PathOf(System.FormattableString.Invariant($"M{cx - 4},{cy} L{cx - 1},{cy + 3} L{cx + 4.5},{cy - 3.5}"), b, 2);

    private static Path Bolt(double cx, double cy, Brush b)
        => PathOf(System.FormattableString.Invariant($"M{cx + 1.5},{cy - 6} L{cx - 3.5},{cy + 1} L{cx},{cy + 1} L{cx - 1.5},{cy + 6} L{cx + 3.5},{cy - 1} L{cx},{cy - 1} Z"), b, 0.8, b);

    private static Path Star(double cx, double cy, double r, Brush b)
    {
        var pts = new System.Text.StringBuilder();
        for (int i = 0; i < 10; i++)
        {
            double rr = i % 2 == 0 ? r : r * 0.45;
            double ang = -System.Math.PI / 2 + i * System.Math.PI / 5;
            pts.Append(i == 0 ? "M" : " L");
            pts.Append(System.FormattableString.Invariant($"{cx + rr * System.Math.Cos(ang):0.##},{cy + rr * System.Math.Sin(ang):0.##}"));
        }
        pts.Append(" Z");
        return PathOf(pts.ToString(), b, 0.6, b);
    }

    /// <summary>A round chip behind a small icon, tinted with the icon's colour, with a tooltip
    /// that names it (the only text the third picture carries).</summary>
    private static Canvas Chip(double cx, double cy, UIElement icon, Color tint, string tip)
    {
        var host = new Canvas { Background = Brushes.Transparent, ToolTip = tip, Tag = "leash-explain-chip" };
        var bg = new Ellipse
        {
            Width = 20,
            Height = 20,
            Fill = FriendsLook.Frozen(Color.FromArgb(0x2A, tint.R, tint.G, tint.B)),
            Stroke = FriendsLook.Frozen(Color.FromArgb(0x88, tint.R, tint.G, tint.B)),
            StrokeThickness = 1.2,
        };
        Canvas.SetLeft(bg, cx - 10);
        Canvas.SetTop(bg, cy - 10);
        host.Children.Add(bg);
        host.Children.Add(icon);
        return host;
    }
}
