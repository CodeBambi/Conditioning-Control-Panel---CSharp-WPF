// PORTED from WPF 7.1.5 Controls/Leash/LeashLook.cs (Help, Icon, Chain, HeartTag, StickerDisc).
// Same path data, same sizes. No Effect on the sticker discs (Avalonia effect/cache rule): the WPF
// soft glow is a 1 px halo ring instead.
using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Avalonia.Views.Controls.Leash;

internal static partial class LeashLook
{
    /// <summary>WPF LeashLook.CutPanelBrush: the deep mint panel behind the cut lines.</summary>
    internal static readonly IBrush CutPanelBrush = new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
        GradientStops = { new GradientStop(Color.FromRgb(0x1F, 0x3A, 0x33), 0), new GradientStop(Color.FromRgb(0x17, 0x2A, 0x26), 1) },
    };

    /// <summary>The round "?" every leash surface carries; opens the explainer for <paramref name="role"/>.</summary>
    internal static Button Help(LeashExplainRole role)
    {
        var t = new TextBlock
        {
            Text = "?",
            FontFamily = FriendsDrawer.Display,
            FontWeight = FontWeight.Bold,
            FontSize = 12,
            Foreground = FriendsDrawer.Lilac,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var b = FriendsDrawer.Pill(t, Rgb(0xB9, 0x9C, 0xFF, 0x22), FriendsDrawer.Lilac, "leash-help:" + role.ToString().ToLowerInvariant(),
            Rgb(0xB9, 0x9C, 0xFF, 0x66));
        b.CornerRadius = new CornerRadius(11);
        b.Padding = new Thickness(0);
        b.Width = 22;
        b.Height = 22;
        b.HorizontalContentAlignment = HorizontalAlignment.Center;
        b.VerticalContentAlignment = VerticalAlignment.Center;
        b.VerticalAlignment = VerticalAlignment.Center;
        b.Cursor = FriendsDrawer.Hand();
        ToolTip.SetTip(b, Loc.Get("leash_help"));
        b.Click += (_, e) => { e.Handled = true; LeashExplainHost.Show(role); };
        return b;
    }

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

    internal static Control Icon(string name, IBrush brush, double size)
    {
        var canvas = new Canvas { Width = 24, Height = 24 };
        if (Icons.TryGetValue(name, out var parts))
        {
            foreach (var (data, fill, stroke) in parts)
            {
                var p = new Path { Data = Geometry.Parse(data), StrokeJoin = PenLineJoin.Round, StrokeLineCap = PenLineCap.Round };
                if (fill) p.Fill = brush;
                else { p.Stroke = brush; p.StrokeThickness = stroke; }
                canvas.Children.Add(p);
            }
        }
        return new Viewbox { Width = size, Height = size, Child = canvas, Tag = "leash-icon:" + name };
    }

    /// <summary>A horizontal chain of oval links, alternating face-on and edge-on.</summary>
    internal static Control Chain(double length, double height = 12)
    {
        var canvas = new Canvas { Width = length, Height = height, ClipToBounds = false, Tag = "leash-chain" };
        var light = Rgb(0xD9, 0xC6, 0xFF);
        var dark = Rgb(0x9F, 0x86, 0xD0);
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

    /// <summary>The gold heart tag on its little ring (46 x 52 drawing).</summary>
    internal static Control HeartTag(double height = 30)
    {
        var c = new Canvas { Width = 46, Height = 52 };
        var ring = Rgb(0xD9, 0xC6, 0xFF);
        c.Children.Add(new Path { Data = Geometry.Parse("M23,0 V8"), Stroke = ring, StrokeThickness = 2 });
        c.Children.Add(new Path { Data = Geometry.Parse("M19.8,10 A3.2,3.2 0 1 0 26.2,10 A3.2,3.2 0 1 0 19.8,10 Z"), Stroke = ring, StrokeThickness = 2 });
        c.Children.Add(new Path
        {
            Data = Geometry.Parse("M23,50 C10,41 4,34 4,25.5 C4,19 8.6,14.5 14.4,14.5 C18,14.5 21.2,16.3 23,19.1 C24.8,16.3 28,14.5 31.6,14.5 C37.4,14.5 42,19 42,25.5 C42,34 36,41 23,50 Z"),
            Fill = FriendsDrawer.Gold,
            Stroke = Rgb(0xFF, 0xF3, 0xC9),
            StrokeThickness = 1.2,
        });
        c.Children.Add(new Path
        {
            Data = Geometry.Parse("M11,22 C12,19 14.5,17.5 17,17.5"),
            Stroke = Rgb(0xFF, 0xFB, 0xE8, 0xCC),
            StrokeThickness = 2,
            StrokeLineCap = PenLineCap.Round,
        });
        return new Viewbox { Height = height, Child = c, Tag = "leash-tag", RenderTransformOrigin = new RelativePoint(0.5, 0, RelativeUnit.Relative) };
    }

    /// <summary>A round sticker from the shelf, tilted a little, coloured by its id.</summary>
    internal static Control StickerDisc(string id, double size = 30, int seed = 0)
    {
        var (color, word) = id switch
        {
            "good" => (LeashFx.GoldC, "GOOD"),
            "star" => (LeashFx.GoldC, "★"),
            "pet" => (LeashFx.LilacC, "PET"),
            "heart" => (LeashFx.PinkC, "♥"),
            "wow" => (LeashFx.MintC, "WOW"),
            _ => (LeashFx.LilacC, id.Length > 4 ? id.Substring(0, 4).ToUpperInvariant() : id.ToUpperInvariant()),
        };
        var g = new Grid
        {
            Width = size,
            Height = size,
            RenderTransformOrigin = RelativePoint.Center,
            RenderTransform = new RotateTransform(((seed * 37) % 21) - 10),
            Tag = "leash-sticker:" + id,
        };
        g.Children.Add(new Ellipse { Margin = new Thickness(-1.5), Stroke = new SolidColorBrush(color, 0.35), StrokeThickness = 1.5 });
        g.Children.Add(new Ellipse { Fill = new SolidColorBrush(color) });
        g.Children.Add(new TextBlock
        {
            Text = word,
            FontFamily = new FontFamily("Consolas, Courier New, Segoe UI Symbol, Segoe UI"),
            FontWeight = FontWeight.Bold,
            FontSize = word.Length > 1 ? size * 0.26 : size * 0.5,
            Foreground = InkBrush,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        });
        return g;
    }
}
