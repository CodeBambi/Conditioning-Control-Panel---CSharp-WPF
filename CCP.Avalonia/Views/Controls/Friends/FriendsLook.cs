// PORTED from ConditioningControlPanel/Controls/Friends/FriendsLook.cs (7.1.5): the drawer's palette and
// recipes (same values). Colours and brushes, the header fall and glass body, the fonts, Label, the
// section head, Pill (hover wash + 2% lift, press squish, 40% when disabled), Avatar (the leaderboard's
// name gradient under the initials, a picture when the server sent one, the presence dot) and the tier
// plate. Avalonia rule: no Effect anywhere in here; the online dot's glow is a BoxShadow on a round Border.
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using ConditioningControlPanel.Controls.Friends;

namespace ConditioningControlPanel.Avalonia.Views.Controls.Friends;

internal static class FriendsLook
{
    public static readonly Color Ground = Rgb(0x10, 0x0A, 0x1E), Panel = Rgb(0x1C, 0x12, 0x33), Raised = Rgb(0x2C, 0x14, 0x50),
        Line = Rgb(0x3A, 0x2A, 0x5E), Line2 = Rgb(0x4D, 0x3A, 0x78), Text = Rgb(0xF1, 0xEA, 0xFF), Muted = Rgb(0xA3, 0x95, 0xC4),
        Dim = Rgb(0x6F, 0x62, 0x9A), Lilac = Rgb(0xB9, 0x9C, 0xFF), Pink = Rgb(0xFF, 0x5F, 0xB4), Mint = Rgb(0x5F, 0xFF, 0xD0),
        Gold = Rgb(0xFF, 0xCF, 0x6B), Red = Rgb(0xFF, 0x5F, 0x7A);

    public static readonly IBrush GroundBrush = Frozen(Ground), PanelBrush = Frozen(Panel), RaisedBrush = Frozen(Raised), LineBrush = Frozen(Line),
        Line2Brush = Frozen(Line2), TextBrush = Frozen(Text), MutedBrush = Frozen(Muted), DimBrush = Frozen(Dim), LilacBrush = Frozen(Lilac),
        PinkBrush = Frozen(Pink), MintBrush = Frozen(Mint), GoldBrush = Frozen(Gold), RedBrush = Frozen(Red);
    public static readonly IBrush HoverBrush = Frozen(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF)), ButtonBrush = Frozen(Rgb(0x1C, 0x12, 0x33)),
        ButtonHoverBrush = Frozen(Rgb(0x3A, 0x1F, 0x66)), FootBrush = Frozen(Rgb(0x16, 0x0E, 0x29)), OfflineDotBrush = Frozen(Rgb(0x4A, 0x3F, 0x66)),
        MintInkBrush = Frozen(Rgb(0x06, 0x2A, 0x1F)), MenuBrush = Frozen(Rgb(0x22, 0x16, 0x41));

    /// <summary>The header's violet fall, top to bottom.</summary>
    public static readonly IBrush HeadBrush = Fall(Raised, Panel);
    /// <summary>The glass body: a faint lilac lift at the top over the panel violet.</summary>
    public static readonly IBrush GlassBrush = Fall(Rgb(0x24, 0x17, 0x42), Panel);
    /// <summary>The drawer's drop shadow (WPF DropShadowEffect blur 30, depth 10, down, 55% black) as a BoxShadow.</summary>
    public static readonly BoxShadows DrawerShadow = new(new BoxShadow { OffsetY = 10, Blur = 30, Color = Color.FromArgb(0x8C, 0, 0, 0) });

    public static readonly FontFamily Display = new("Fredoka, Segoe UI"), Body = new("Segoe UI"), Mono = new("Consolas, Courier New");

    public static Color Rgb(byte r, byte g, byte b) => Color.FromRgb(r, g, b);
    public static IBrush Frozen(Color c) => new ImmutableSolidColorBrush(c);
    private static IBrush Fall(Color top, Color bottom) => new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
        GradientStops = { new GradientStop(top, 0), new GradientStop(bottom, 1) },
    }.ToImmutable();

    public static TextBlock Label(string text, double size, IBrush fg, FontFamily? font = null, FontWeight weight = FontWeight.Normal) => new()
    {
        Text = text, FontSize = size, Foreground = fg, FontFamily = font ?? Body, FontWeight = weight,
        TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>A flat button in the drawer's recipe: a rounded border, the hover wash and a 2% lift,
    /// a squish on press, 40% when disabled. Text goes in a TextBlock (Avalonia reads "_" as an access key).</summary>
    public static Button Pill(object content, IBrush bg, IBrush fg, string tag, IBrush? border = null, IBrush? hoverBg = null)
    {
        var b = new Button
        {
            Content = content is string s ? new TextBlock { Text = s, FontFamily = Display, FontSize = 12 } : content,
            Background = bg, Foreground = fg, BorderBrush = border ?? Brushes.Transparent, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8), Padding = new Thickness(9, 3, 9, 3), Tag = tag, VerticalAlignment = VerticalAlignment.Center,
            FontFamily = Display, RenderTransformOrigin = RelativePoint.Center,
        };
        // Fluent paints its own hover/press/disabled faces from these keys; the drawer's recipe owns them.
        var hover = hoverBg ?? (ReferenceEquals(bg, Brushes.Transparent) ? HoverBrush : ButtonHoverBrush);
        foreach (var state in new[] { "PointerOver", "Pressed" })
        {
            b.Resources["ButtonBackground" + state] = hover;
            b.Resources["ButtonForeground" + state] = fg;
            b.Resources["ButtonBorderBrush" + state] = border ?? Brushes.Transparent;
        }
        b.Resources["ButtonBackgroundDisabled"] = bg;
        b.Resources["ButtonForegroundDisabled"] = fg;
        b.Resources["ButtonBorderBrushDisabled"] = border ?? Brushes.Transparent;
        b.Opacity = 1;
        b.PropertyChanged += (_, e) => { if (e.Property == InputElement.IsEnabledProperty) b.Opacity = b.IsEnabled ? 1.0 : 0.4; };
        b.PointerEntered += (_, _) => { if (b.IsEnabled) Scale(b, 1.02, 150); };
        b.PointerExited += (_, _) => Scale(b, 1.0, 150);
        b.AddHandler(InputElement.PointerPressedEvent, (_, _) => Scale(b, 0.97, 80), global::Avalonia.Interactivity.RoutingStrategies.Tunnel);
        b.AddHandler(InputElement.PointerReleasedEvent, (_, _) => Scale(b, b.IsPointerOver ? 1.02 : 1.0, 80), global::Avalonia.Interactivity.RoutingStrategies.Tunnel);
        return b;
    }

    /// <summary>WPF MotionFx.HoverLift / PressSquish: a short scale tween, instant at motion Off.</summary>
    private static void Scale(Control c, double to, int ms)
    {
        if (c.RenderTransform is not ScaleTransform s) c.RenderTransform = s = new ScaleTransform(1, 1);
        if (FriendsDrawer.Amount <= 0) { s.ScaleX = s.ScaleY = to; return; }
        Helpers.TransformTween.Run(s, TimeSpan.FromMilliseconds(ms), new (double, AvaloniaProperty, double)[]
        {
            (0, ScaleTransform.ScaleXProperty, s.ScaleX), (1, ScaleTransform.ScaleXProperty, to),
            (0, ScaleTransform.ScaleYProperty, s.ScaleY), (1, ScaleTransform.ScaleYProperty, to),
        });
    }

    /// <summary>The section header strip: mono, spaced, dim, with the count.</summary>
    public static Grid SectionHead(string title, int count)
    {
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Margin = new Thickness(8, 10, 8, 4) };
        var t = Label(title, 10.5, DimBrush, Mono, FontWeight.SemiBold);
        t.Tag = "friends-section";
        var n = Label(count.ToString(), 10.5, DimBrush, Mono);
        n.Margin = new Thickness(6, 0, 0, 0);
        Grid.SetColumn(n, 1);
        g.Children.Add(t);
        g.Children.Add(n);
        return g;
    }

    /// <summary>Initials on the name's gradient disc (the leaderboard's), a picture once it loads, and a
    /// presence dot (mint on with a soft glow, grey off, none for null).</summary>
    public static Grid Avatar(string name, double size, bool? dot, string? url = null)
    {
        var g = new Grid { Width = size, Height = size, VerticalAlignment = VerticalAlignment.Center };
        var initials = new TextBlock
        {
            Text = FriendsDrawerRules.Initials(name), FontFamily = Display, FontWeight = FontWeight.SemiBold, FontSize = Math.Round(size * 0.4),
            Foreground = Frozen(Rgb(0x0B, 0x07, 0x16)), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        };
        var disc = new Border { CornerRadius = new CornerRadius(size / 2), Background = DiscBrush(name), ClipToBounds = true, Child = initials };
        g.Children.Add(disc);
        Helpers.AvatarPhotos.Paint(disc, initials, url, (int)Math.Ceiling(size * 2));
        if (dot is bool on)
        {
            double d = Math.Max(10, size * 0.3);
            g.Children.Add(new Border
            {
                Width = d, Height = d, CornerRadius = new CornerRadius(d / 2), Background = on ? MintBrush : OfflineDotBrush,
                BorderBrush = PanelBrush, BorderThickness = new Thickness(2), Margin = new Thickness(0, 0, -1, -1),
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom,
                BoxShadow = on ? new BoxShadows(new BoxShadow { Blur = 8, Color = Color.FromArgb(0xE6, Mint.R, Mint.G, Mint.B) }) : default,
                Tag = on ? "friends-dot-on" : "friends-dot-off",
            });
        }
        return g;
    }

    private static IBrush DiscBrush(string name)
    {
        try { return Tabs.LeaderboardRow.BuildAvatarBrush(name); }
        catch { return LilacBrush; }
    }

    /// <summary>Test seam: the badge art (TierBadge.TierArt, the same files WPF reads).</summary>
    internal static Func<int, IImage?> TierArt { get; set; } = t => global::ConditioningControlPanel.Avalonia.Controls.TierBadge.TierArt(t);

    /// <summary>The tier badge art after a name, nothing for free accounts or without art.</summary>
    public static Control? TierPlate(int tier, double height)
    {
        if (tier <= 0) return null;
        if (TierArt(tier) is not { } art) return null;
        return new Image
        {
            Source = art, Height = height, Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 0, 0), Tag = tier >= 2 ? "friends-tier-2" : "friends-tier-1",
        };
    }
}
