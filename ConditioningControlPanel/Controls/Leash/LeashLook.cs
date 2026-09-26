using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using ConditioningControlPanel.Controls.Friends;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Controls.Leash;

/// <summary>
/// The leash's paint box, on top of the drawer's (<see cref="FriendsLook"/>): the gold-over-violet
/// leash card, the chunky gradient buttons from the approved mockup, the icon set (drawn as
/// vector paths, never a font glyph), the chain, the gold heart tag, the ring and the round "?".
/// Everything is code-built so the drawer, the overlays and the suite share one recipe.
/// </summary>
internal static class LeashLook
{
    public static readonly Color GoldDeep = FriendsLook.Rgb(0x6B, 0x53, 0x20);
    public static readonly Color CardTop = FriendsLook.Rgb(0x2A, 0x1D, 0x12);
    public static readonly Color CardBottom = FriendsLook.Rgb(0x1F, 0x15, 0x28);
    public static readonly Color MintDeep = FriendsLook.Rgb(0x28, 0x5D, 0x4F);
    public static readonly Color Ink = FriendsLook.Rgb(0x1B, 0x10, 0x26);
    public static readonly Color Idle = FriendsLook.Rgb(0x4A, 0x3D, 0x61);

    public static readonly Brush InkBrush = FriendsLook.Frozen(Ink);
    public static readonly Brush GoldDeepBrush = FriendsLook.Frozen(GoldDeep);
    public static readonly Brush IdleBrush = FriendsLook.Frozen(Idle);
    public static readonly Brush ShadeBrush = FriendsLook.Frozen(Color.FromArgb(0x66, 0, 0, 0));

    /// <summary>The holder's card: warm gold at the top melting into the drawer's violet.</summary>
    public static readonly Brush CardBrush = Gradient(CardTop, CardBottom);

    /// <summary>The leashed side's own card: a pink blush over the violet.</summary>
    public static readonly Brush SelfCardBrush = Gradient(FriendsLook.Rgb(0x36, 0x14, 0x33), CardBottom);

    public static readonly Brush CutPanelBrush = Gradient(FriendsLook.Rgb(0x1F, 0x3A, 0x33), FriendsLook.Rgb(0x17, 0x2A, 0x26));

    public static Brush Gradient(Color top, Color bottom)
    {
        var b = new LinearGradientBrush(top, bottom, 90);
        b.Freeze();
        return b;
    }

    // ---- buttons ----------------------------------------------------------------------

    public enum Tone { Pink, Gold, Mint, Red, Ghost }

    private static (Color Top, Color Bottom, Brush Ink) Colors(Tone t) => t switch
    {
        Tone.Gold => (FriendsLook.Rgb(0xFF, 0xE3, 0xA0), FriendsLook.Gold, InkBrush),
        Tone.Mint => (FriendsLook.Rgb(0xA8, 0xFF, 0xE6), FriendsLook.Mint, InkBrush),
        Tone.Red => (FriendsLook.Rgb(0xFF, 0x8A, 0x9E), FriendsLook.Red, Brushes.White),
        Tone.Ghost => (FriendsLook.Rgb(0x2E, 0x20, 0x46), FriendsLook.Rgb(0x26, 0x1A, 0x3C), FriendsLook.MutedBrush),
        _ => (FriendsLook.Rgb(0xFF, 0x8C, 0xC6), FriendsLook.Pink, InkBrush),
    };

    /// <summary>The mockup's chunky button: a lit top edge, a shaded foot, a soft coloured glow.
    /// <paramref name="icon"/> (optional) sits above the words when <paramref name="stacked"/>.</summary>
    public static Button Chunky(string text, Tone tone, string? icon = null, bool stacked = false, double size = 13)
    {
        var (top, bottom, ink) = Colors(tone);
        var body = new LinearGradientBrush(top, bottom, 90);
        body.Freeze();
        var sp = new StackPanel
        {
            Orientation = stacked ? Orientation.Vertical : Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        if (icon != null)
        {
            var ic = Icon(icon, ink, stacked ? 20 : 15);
            ic.Margin = stacked ? new Thickness(0, 0, 0, 3) : new Thickness(0, 0, 6, 0);
            ic.HorizontalAlignment = HorizontalAlignment.Center;
            sp.Children.Add(ic);
        }
        sp.Children.Add(new TextBlock
        {
            Text = text,
            FontFamily = FriendsLook.Display,
            FontWeight = FontWeights.SemiBold,
            FontSize = size,
            Foreground = ink,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        var b = FriendsLook.Pill(sp, body, ink, FriendsLook.Frozen(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)), 11,
            new Thickness(8, stacked ? 8 : 7, 8, stacked ? 7 : 7), body);
        if (tone != Tone.Ghost) b.Effect = FriendsLook.Glow(bottom, 12, 0.35);
        return b;
    }

    /// <summary>The round "?" every leash surface carries. It opens the explainer on the page
    /// for <paramref name="role"/>.</summary>
    public static Button Help(LeashExplainRole role)
    {
        var t = new TextBlock
        {
            Text = "?",
            FontFamily = FriendsLook.Display,
            FontWeight = FontWeights.Bold,
            FontSize = 12,
            Foreground = FriendsLook.LilacBrush,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var b = FriendsLook.Pill(t, FriendsLook.Frozen(Color.FromArgb(0x22, 0xB9, 0x9C, 0xFF)), FriendsLook.LilacBrush,
            FriendsLook.Frozen(Color.FromArgb(0x66, 0xB9, 0x9C, 0xFF)), 11, new Thickness(0));
        b.Width = 22;
        b.Height = 22;
        b.VerticalAlignment = VerticalAlignment.Center;
        b.ToolTip = Loc.Get("leash_help");
        b.Tag = "leash-help:" + role.ToString().ToLowerInvariant();
        b.Click += (_, e) => { e.Handled = true; LeashExplainHost.Show(role); };
        return b;
    }

    /// <summary>A two to four way switch. <paramref name="onPick"/> gets the picked index.</summary>
    public static Border Segmented(IReadOnlyList<string> labels, int selected, Action<int> onPick, string tag, double size = 12.5)
    {
        var host = new Border
        {
            Background = FriendsLook.Frozen(FriendsLook.Rgb(0x24, 0x18, 0x38)),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(3),
            Tag = tag,
        };
        var g = new UniformGrid { Columns = labels.Count };
        for (int i = 0; i < labels.Count; i++)
        {
            int idx = i;
            bool on = i == selected;
            var b = FriendsLook.Pill(new TextBlock
            {
                Text = labels[i],
                FontSize = size,
                FontWeight = FontWeights.SemiBold,
                FontFamily = FriendsLook.Body,
                Foreground = on ? FriendsLook.TextBrush : FriendsLook.MutedBrush,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            }, on ? FriendsLook.Frozen(FriendsLook.Rgb(0x3A, 0x28, 0x5C)) : Brushes.Transparent,
                FriendsLook.TextBrush, on ? FriendsLook.Line2Brush : Brushes.Transparent, 8, new Thickness(4, 6, 4, 6));
            b.Tag = tag + ":" + i + (on ? ":on" : "");
            b.Margin = new Thickness(i == 0 ? 0 : 1.5, 0, i == labels.Count - 1 ? 0 : 1.5, 0);
            b.Click += (_, _) => onPick(idx);
            g.Children.Add(b);
        }
        host.Child = g;
        return host;
    }

    /// <summary>A small caption in the mono "section" voice.</summary>
    public static TextBlock Caption(string text, Brush? fg = null)
    {
        var t = FriendsLook.Label(text.ToUpperInvariant(), 9.5, fg ?? FriendsLook.DimBrush, FriendsLook.Mono, FontWeights.SemiBold);
        return t;
    }

    public static TextBlock Wrap(TextBlock t)
    {
        t.TextWrapping = TextWrapping.Wrap;
        t.TextTrimming = TextTrimming.None;
        return t;
    }

    // ---- icons ------------------------------------------------------------------------

    /// <summary>(path data, filled, stroke width) per icon, in a 24 unit box. From the mockup.</summary>
    private static readonly Dictionary<string, (string Data, bool Fill, double Stroke)[]> Icons = new()
    {
        ["lock"] = new[] { ("M8,10 L16,10 A3,3 0 0 1 19,13 L19,18 A3,3 0 0 1 16,21 L8,21 A3,3 0 0 1 5,18 L5,13 A3,3 0 0 1 8,10 Z", true, 0.0), ("M8,10 V7 A4,4 0 0 1 16,7 V10", false, 2.4) },
        ["eye"] = new[] { ("M2,12 C2,12 6,5 12,5 C18,5 22,12 22,12 C22,12 18,19 12,19 C6,19 2,12 2,12 Z", false, 2.0), ("M8.8,12 A3.2,3.2 0 1 0 15.2,12 A3.2,3.2 0 1 0 8.8,12 Z", true, 0.0) },
        ["scissors"] = new[] { ("M3,6 A3,3 0 1 0 9,6 A3,3 0 1 0 3,6 Z", false, 2.0), ("M3,18 A3,3 0 1 0 9,18 A3,3 0 1 0 3,18 Z", false, 2.0), ("M8.5,7.5 L20,18 M8.5,16.5 L20,6", false, 2.0) },
        ["star"] = new[] { ("M12,2 L15,8.5 L22,9.3 L16.8,14.1 L18.2,21.1 L12,17.6 L5.8,21 L7.2,14 L2,9.3 L9,8.5 Z", true, 0.0) },
        ["bolt"] = new[] { ("M13,2 L4,14 L11,14 L10,22 L19,10 L12,10 Z", true, 0.0) },
        ["scale"] = new[] { ("M12,3 V21 M5,7 H19 M5,7 L2,14 H8 Z M19,7 L16,14 H22 Z", false, 2.0) },
        ["link"] = new[] { ("M6,8 H9 A4,4 0 0 1 9,16 H6 A4,4 0 0 1 6,8 Z", false, 2.2), ("M15,8 H18 A4,4 0 0 1 18,16 H15 A4,4 0 0 1 15,8 Z", false, 2.2) },
        ["hand"] = new[] { ("M7,11 V5 A1.5,1.5 0 0 1 10,5 V10 V3 A1.5,1.5 0 0 1 13,3 V10 V4 A1.5,1.5 0 0 1 16,4 V11 V7 A1.5,1.5 0 0 1 19,7 V14 A7,7 0 0 1 12,21 H11 A7,7 0 0 1 5.5,18.3 L3,14.5 A1.5,1.5 0 0 1 5.3,12.6 Z", true, 0.0) },
        ["play"] = new[] { ("M7,4 V20 L20,12 Z", true, 0.0) },
        ["bubble"] = new[] { ("M3,12 A8,8 0 1 0 19,12 A8,8 0 1 0 3,12 Z", false, 2.0), ("M6,9 A2,2 0 1 0 10,9 A2,2 0 1 0 6,9 Z", true, 0.0) },
        ["spiral"] = new[] { ("M12,12 A1.5,1.5 0 1 1 13.5,13.5 A3,3 0 1 1 16.5,10.5 A4.5,4.5 0 1 1 12,6 A6,6 0 1 1 6,12", false, 2.0) },
        ["task"] = new[] { ("M6,3 H18 A2,2 0 0 1 20,5 V19 A2,2 0 0 1 18,21 H6 A2,2 0 0 1 4,19 V5 A2,2 0 0 1 6,3 Z", false, 2.0), ("M8,12 L11,15 L16,9", false, 2.2) },
        ["moon"] = new[] { ("M20,14.5 A8.5,8.5 0 1 1 9.5,4 A7,7 0 0 0 20,14.5 Z", true, 0.0) },
        ["heart"] = new[] { ("M12,21 C5,16 2,12.5 2,8.5 A5,5 0 0 1 12,6.2 A5,5 0 0 1 22,8.5 C22,12.5 19,16 12,21 Z", true, 0.0) },
        ["panic"] = new[] { ("M12,2 A10,10 0 1 0 12.01,2 Z", false, 2.2), ("M12,7 V13 M12,16.5 V17", false, 2.6) },
        ["clock"] = new[] { ("M12,2 A10,10 0 1 0 12.01,2 Z", false, 2.0), ("M12,6 V12 L16,14", false, 2.2) },
    };

    public static FrameworkElement Icon(string name, Brush brush, double size)
    {
        var canvas = new Canvas { Width = 24, Height = 24 };
        if (Icons.TryGetValue(name, out var parts))
        {
            foreach (var (data, fill, stroke) in parts)
            {
                var p = new Path
                {
                    Data = Geometry.Parse(data),
                    StrokeLineJoin = PenLineJoin.Round,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round,
                };
                if (fill) p.Fill = brush;
                else { p.Stroke = brush; p.StrokeThickness = stroke; }
                canvas.Children.Add(p);
            }
        }
        return new Viewbox { Width = size, Height = size, Child = canvas, Tag = "leash-icon:" + name };
    }

    // ---- chain, tag, ring, stamp -----------------------------------------------------

    /// <summary>A horizontal chain of oval links, alternating face-on and edge-on, as a single
    /// drawing. <paramref name="length"/> in px; the links are 14 px long.</summary>
    public static FrameworkElement Chain(double length, double height = 12)
    {
        var canvas = new Canvas { Width = length, Height = height, ClipToBounds = false, Tag = "leash-chain" };
        var light = FriendsLook.Frozen(FriendsLook.Rgb(0xD9, 0xC6, 0xFF));
        var dark = FriendsLook.Frozen(FriendsLook.Rgb(0x9F, 0x86, 0xD0));
        double step = 11;
        int n = Math.Max(1, (int)Math.Ceiling(length / step));
        for (int i = 0; i < n; i++)
        {
            bool face = i % 2 == 0;
            var link = new Rectangle
            {
                Width = 15,
                Height = face ? height : height * 0.42,
                RadiusX = face ? height / 2 : 2,
                RadiusY = face ? height / 2 : 2,
                Stroke = face ? light : dark,
                StrokeThickness = face ? 2.2 : 0,
                Fill = face ? Brushes.Transparent : dark,
            };
            Canvas.SetLeft(link, i * step - 2);
            Canvas.SetTop(link, face ? 0 : height * 0.29);
            canvas.Children.Add(link);
        }
        return canvas;
    }

    /// <summary>The gold heart tag on its little ring, as drawn in the mockup (46 x 52).</summary>
    public static FrameworkElement HeartTag(double height = 30)
    {
        var c = new Canvas { Width = 46, Height = 52 };
        var ringBrush = FriendsLook.Frozen(FriendsLook.Rgb(0xD9, 0xC6, 0xFF));
        c.Children.Add(new Path { Data = Geometry.Parse("M23,0 V8"), Stroke = ringBrush, StrokeThickness = 2 });
        c.Children.Add(new Path { Data = Geometry.Parse("M19.8,10 A3.2,3.2 0 1 0 26.2,10 A3.2,3.2 0 1 0 19.8,10 Z"), Stroke = ringBrush, StrokeThickness = 2 });
        c.Children.Add(new Path
        {
            Data = Geometry.Parse("M23,50 C10,41 4,34 4,25.5 C4,19 8.6,14.5 14.4,14.5 C18,14.5 21.2,16.3 23,19.1 C24.8,16.3 28,14.5 31.6,14.5 C37.4,14.5 42,19 42,25.5 C42,34 36,41 23,50 Z"),
            Fill = FriendsLook.GoldBrush,
            Stroke = FriendsLook.Frozen(FriendsLook.Rgb(0xFF, 0xF3, 0xC9)),
            StrokeThickness = 1.2,
        });
        c.Children.Add(new Path
        {
            Data = Geometry.Parse("M11,22 C12,19 14.5,17.5 17,17.5"),
            Stroke = FriendsLook.Frozen(Color.FromArgb(0xCC, 0xFF, 0xFB, 0xE8)),
            StrokeThickness = 2,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
        });
        return new Viewbox { Height = height, Child = c, Tag = "leash-tag", RenderTransformOrigin = new Point(0.5, 0) };
    }

    /// <summary>The minutes ring: a track, an arc for <paramref name="fraction"/>, the figure inside.</summary>
    public static FrameworkElement Ring(double fraction, string figure, Color color, double size = 50)
    {
        var g = new Grid { Width = size, Height = size, Tag = "leash-ring" };
        double stroke = size * 0.11, r = (size - stroke) / 2;
        g.Children.Add(new Ellipse { Stroke = FriendsLook.Frozen(Color.FromArgb(0x16, 0xFF, 0xFF, 0xFF)), StrokeThickness = stroke });
        fraction = Math.Clamp(fraction, 0, 1);
        if (fraction > 0.001)
        {
            var arc = new Path
            {
                Stroke = FriendsLook.Frozen(color),
                StrokeThickness = stroke,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                Data = ArcGeometry(size / 2, size / 2, r, fraction),
                Effect = FriendsLook.Glow(color, 8, 0.5),
            };
            g.Children.Add(arc);
        }
        g.Children.Add(new TextBlock
        {
            Text = figure,
            FontFamily = FriendsLook.Mono,
            FontWeight = FontWeights.Bold,
            FontSize = size * 0.28,
            Foreground = FriendsLook.TextBrush,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        });
        return g;
    }

    private static Geometry ArcGeometry(double cx, double cy, double r, double fraction)
    {
        if (fraction >= 0.999) return new EllipseGeometry(new Point(cx, cy), r, r);
        double a = fraction * Math.PI * 2;
        var start = new Point(cx, cy - r);
        var end = new Point(cx + r * Math.Sin(a), cy - r * Math.Cos(a));
        var fig = new PathFigure { StartPoint = start, IsClosed = false };
        fig.Segments.Add(new ArcSegment(end, new Size(r, r), 0, fraction > 0.5, SweepDirection.Clockwise, true));
        var geo = new PathGeometry();
        geo.Figures.Add(fig);
        geo.Freeze();
        return geo;
    }

    /// <summary>A tilted rubber stamp ("PUNISHED", "DONE").</summary>
    public static Border Stamp(string text, Color color, double angle = 10)
    {
        return new Border
        {
            BorderBrush = FriendsLook.Frozen(color),
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6, 2, 6, 2),
            Opacity = 0.88,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new RotateTransform(angle),
            IsHitTestVisible = false,
            Tag = "leash-stamp",
            Child = new TextBlock
            {
                Text = text,
                FontFamily = FriendsLook.Mono,
                FontWeight = FontWeights.Bold,
                FontSize = 11,
                Foreground = FriendsLook.Frozen(color),
            },
        };
    }

    /// <summary>A round sticker from the shelf, tilted a little, coloured by its id.</summary>
    public static FrameworkElement StickerDisc(string id, double size = 30, int seed = 0)
    {
        var (color, word) = id switch
        {
            "good" => (FriendsLook.Gold, "GOOD"),
            "star" => (FriendsLook.Gold, "★"),
            "pet" => (FriendsLook.Lilac, "PET"),
            "heart" => (FriendsLook.Pink, "♥"),
            "wow" => (FriendsLook.Mint, "WOW"),
            _ => (FriendsLook.Lilac, id.Length > 4 ? id.Substring(0, 4).ToUpperInvariant() : id.ToUpperInvariant()),
        };
        var g = new Grid
        {
            Width = size,
            Height = size,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new RotateTransform(((seed * 37) % 21) - 10),
            Tag = "leash-sticker:" + id,
        };
        g.Children.Add(new Ellipse { Fill = FriendsLook.Frozen(color), Effect = FriendsLook.Glow(color, 6, 0.35) });
        g.Children.Add(new TextBlock
        {
            Text = word,
            FontFamily = FriendsLook.Mono,
            FontWeight = FontWeights.Bold,
            FontSize = word.Length > 1 ? size * 0.26 : size * 0.5,
            Foreground = InkBrush,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        });
        return g;
    }

    /// <summary>One cell of the 7-day strip with its weekday letter.</summary>
    public static FrameworkElement WeekCell(WeekDayView day)
    {
        var sp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Tag = "leash-week:" + day.Mark };
        var dot = new Ellipse { Width = 15, Height = 15, HorizontalAlignment = HorizontalAlignment.Center };
        switch (day.Mark)
        {
            case Services.Leash.WeekMark.Did: dot.Fill = FriendsLook.MintBrush; dot.Effect = FriendsLook.Glow(FriendsLook.Mint, 6, 0.5); break;
            case Services.Leash.WeekMark.Punished: dot.Fill = FriendsLook.RedBrush; dot.Effect = FriendsLook.Glow(FriendsLook.Red, 6, 0.5); break;
            case Services.Leash.WeekMark.Today:
                dot.Fill = Brushes.Transparent;
                dot.Stroke = FriendsLook.GoldBrush;
                dot.StrokeThickness = 2;
                dot.StrokeDashArray = new DoubleCollection { 1.6, 1.2 };
                break;
            default: dot.Fill = IdleBrush; break;
        }
        sp.Children.Add(dot);
        sp.Children.Add(new TextBlock
        {
            Text = day.Letter,
            FontFamily = FriendsLook.Mono,
            FontSize = 9.5,
            FontWeight = FontWeights.Bold,
            Foreground = FriendsLook.DimBrush,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 3, 0, 0),
        });
        return sp;
    }
}

/// <summary>A strip cell as drawn: the mark and its letter.</summary>
internal readonly record struct WeekDayView(Services.Leash.WeekMark Mark, string Letter);
