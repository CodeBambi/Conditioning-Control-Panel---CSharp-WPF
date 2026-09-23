using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Controls.Friends;

/// <summary>
/// The drawer's paint box: the launcher's violets, the four accents from the approved mockup,
/// Fredoka for names and figures, and the few element recipes every part of the drawer shares
/// (glass card, pill button, avatar). Brushes are frozen once and shared.
/// </summary>
internal static class FriendsLook
{
    public static readonly Color Ground = Rgb(0x10, 0x0A, 0x1E);
    public static readonly Color Panel = Rgb(0x1C, 0x12, 0x33);
    public static readonly Color Raised = Rgb(0x2C, 0x14, 0x50);
    public static readonly Color Line = Rgb(0x3A, 0x2A, 0x5E);
    public static readonly Color Line2 = Rgb(0x4D, 0x3A, 0x78);
    public static readonly Color Text = Rgb(0xF1, 0xEA, 0xFF);
    public static readonly Color Muted = Rgb(0xA3, 0x95, 0xC4);
    public static readonly Color Dim = Rgb(0x6F, 0x62, 0x9A);
    public static readonly Color Lilac = Rgb(0xB9, 0x9C, 0xFF);
    public static readonly Color Pink = Rgb(0xFF, 0x5F, 0xB4);
    public static readonly Color Mint = Rgb(0x5F, 0xFF, 0xD0);
    public static readonly Color Gold = Rgb(0xFF, 0xCF, 0x6B);
    public static readonly Color Red = Rgb(0xFF, 0x5F, 0x7A);

    public static readonly Brush PanelBrush = Frozen(Panel);
    public static readonly Brush RaisedBrush = Frozen(Raised);
    public static readonly Brush LineBrush = Frozen(Line);
    public static readonly Brush Line2Brush = Frozen(Line2);
    public static readonly Brush TextBrush = Frozen(Text);
    public static readonly Brush MutedBrush = Frozen(Muted);
    public static readonly Brush DimBrush = Frozen(Dim);
    public static readonly Brush LilacBrush = Frozen(Lilac);
    public static readonly Brush PinkBrush = Frozen(Pink);
    public static readonly Brush MintBrush = Frozen(Mint);
    public static readonly Brush GoldBrush = Frozen(Gold);
    public static readonly Brush RedBrush = Frozen(Red);
    public static readonly Brush HoverBrush = Frozen(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF));
    public static readonly Brush ButtonBrush = Frozen(Rgb(0x1C, 0x12, 0x33));
    public static readonly Brush ButtonHoverBrush = Frozen(Rgb(0x3A, 0x1F, 0x66));
    public static readonly Brush FootBrush = Frozen(Rgb(0x16, 0x0E, 0x29));
    public static readonly Brush OfflineDotBrush = Frozen(Rgb(0x4A, 0x3F, 0x66));
    public static readonly Brush MintInkBrush = Frozen(Rgb(0x06, 0x2A, 0x1F));

    /// <summary>The header's violet fall, top to bottom, as in the mockup.</summary>
    public static readonly Brush HeadBrush = FrozenGradient(Raised, Panel);

    /// <summary>The glass body: a faint lilac lift at the top over the panel violet.</summary>
    public static readonly Brush GlassBrush = FrozenGradient(Rgb(0x24, 0x17, 0x42), Panel);

    public static readonly FontFamily Display = new("/Fonts/#Fredoka, Segoe UI");
    public static readonly FontFamily Body = new("Segoe UI");
    public static readonly FontFamily Mono = new("Consolas, Courier New");

    public static Color Rgb(byte r, byte g, byte b) => Color.FromRgb(r, g, b);

    public static Brush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    private static Brush FrozenGradient(Color top, Color bottom)
    {
        var b = new LinearGradientBrush(top, bottom, 90);
        b.Freeze();
        return b;
    }

    public static TextBlock Label(string text, double size, Brush fg, FontFamily? font = null,
        FontWeight? weight = null)
        => new()
        {
            Text = text,
            FontSize = size,
            Foreground = fg,
            FontFamily = font ?? Body,
            FontWeight = weight ?? FontWeights.Normal,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };

    /// <summary>The section header strip: mono, spaced, dim, with the count at the right.</summary>
    public static Grid SectionHead(string title, int count)
    {
        var g = new Grid { Margin = new Thickness(8, 10, 8, 4) };
        var t = Label(title, 10, DimBrush, Mono);
        t.Tag = "friends-section";
        var n = Label(count.ToString(), 10, DimBrush, Mono);
        n.HorizontalAlignment = HorizontalAlignment.Right;
        g.Children.Add(t);
        g.Children.Add(n);
        return g;
    }

    /// <summary>A flat button in the drawer's recipe: a rounded border, a lift on hover, a squish
    /// on press. Content is whatever the caller gives it.</summary>
    public static Button Pill(object content, Brush bg, Brush fg, Brush border, double radius = 10,
        Thickness? padding = null, Brush? hoverBg = null)
    {
        var b = new Button
        {
            Content = content,
            Foreground = fg,
            Background = bg,
            BorderBrush = border,
            BorderThickness = new Thickness(1),
            Padding = padding ?? new Thickness(10, 6, 10, 6),
            FontFamily = Display,
            FontSize = 13,
            Cursor = System.Windows.Input.Cursors.Hand,
            Focusable = false,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new ScaleTransform(1, 1),
        };
        b.Template = PillTemplate(radius);
        var rest = bg;
        var hover = hoverBg ?? ButtonHoverBrush;
        b.MouseEnter += (_, _) => { if (b.IsEnabled) { b.Background = hover; MotionFx.HoverLift(b, true); } };
        b.MouseLeave += (_, _) => { b.Background = rest; MotionFx.HoverLift(b, false); };
        b.PreviewMouseLeftButtonDown += (_, _) => MotionFx.PressSquish(b, true);
        b.PreviewMouseLeftButtonUp += (_, _) => MotionFx.PressSquish(b, false);
        b.IsEnabledChanged += (_, _) => b.Opacity = b.IsEnabled ? 1.0 : 0.4;
        return b;
    }

    private static readonly Dictionary<double, ControlTemplate> Templates = new();

    private static ControlTemplate PillTemplate(double radius)
    {
        if (Templates.TryGetValue(radius, out var t)) return t;
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(radius));
        border.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        border.SetBinding(Border.BorderBrushProperty, new System.Windows.Data.Binding("BorderBrush") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        border.SetBinding(Border.BorderThicknessProperty, new System.Windows.Data.Binding("BorderThickness") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        border.SetBinding(Border.PaddingProperty, new System.Windows.Data.Binding("Padding") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        var cp = new FrameworkElementFactory(typeof(ContentPresenter));
        cp.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
        cp.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(cp);
        t = new ControlTemplate(typeof(Button)) { VisualTree = border };
        t.Seal();
        Templates[radius] = t;
        return t;
    }

    /// <summary>A soft coloured glow for a lit element.</summary>
    public static DropShadowEffect Glow(Color c, double radius = 10, double opacity = 0.8)
        => new() { Color = c, BlurRadius = radius, ShadowDepth = 0, Opacity = opacity };

    /// <summary>A packed feature picture (Resources/features/...), decoded small. Null when
    /// the art is missing, which a tile survives by showing its name alone.</summary>
    public static ImageSource? Art(string relative, int decodeWidth = 256)
    {
        try { return ModResourceResolver.ResolveImageDecoded(relative, decodeWidth); }
        catch { return null; }
    }

    // ---- avatars ----------------------------------------------------------------------

    private const string ProxyBase = "https://codebambi-proxy.vercel.app";
    private static readonly Dictionary<string, ImageSource> PhotoCache = new();
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

    /// <summary>
    /// A round avatar: initials on the roster's deterministic gradient, replaced by the
    /// friend's picture when the proxy has one. The same fall-through the header bubble uses,
    /// so a friend looks the same here as on the leaderboard. <paramref name="dot"/> adds the
    /// presence dot at the bottom right (null = no dot).
    /// </summary>
    public static Grid Avatar(string name, string? url, double size, bool? dot = null)
    {
        var host = new Grid { Width = size, Height = size, VerticalAlignment = VerticalAlignment.Center };
        var disc = new Ellipse { Fill = SafeAvatarBrush(name) };
        var initials = new TextBlock
        {
            Text = FriendsDrawerRules.Initials(name),
            FontFamily = Display,
            FontWeight = FontWeights.SemiBold,
            FontSize = Math.Round(size * 0.4),
            Foreground = Frozen(Rgb(0x0B, 0x07, 0x16)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        host.Children.Add(disc);
        host.Children.Add(initials);
        if (dot is bool on)
        {
            var d = new Ellipse
            {
                Width = Math.Max(10, size * 0.3),
                Height = Math.Max(10, size * 0.3),
                Fill = on ? MintBrush : OfflineDotBrush,
                Stroke = PanelBrush,
                StrokeThickness = 2,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, -1, -1),
                Tag = on ? "friends-dot-on" : "friends-dot-off",
            };
            if (on) d.Effect = Glow(Mint, 8, 0.9);
            host.Children.Add(d);
        }
        if (!string.IsNullOrEmpty(url)) _ = PaintPhotoAsync(disc, initials, url!);
        return host;
    }

    private static Brush SafeAvatarBrush(string name)
    {
        try { return LeaderboardEntry.BuildAvatarBrush(name); }
        catch { return LilacBrush; }
    }

    private static async Task PaintPhotoAsync(Ellipse disc, TextBlock initials, string url)
    {
        try
        {
            var full = url.StartsWith("/", StringComparison.Ordinal) ? ProxyBase + url : url;
            if (!full.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return;
            if (!PhotoCache.TryGetValue(full, out var img))
            {
                var bytes = await Http.GetByteArrayAsync(full).ConfigureAwait(true);
                var bmp = new BitmapImage();
                using (var ms = new MemoryStream(bytes))
                {
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.DecodePixelWidth = 96;
                    bmp.StreamSource = ms;
                    bmp.EndInit();
                }
                bmp.Freeze();
                img = bmp;
                PhotoCache[full] = img;
            }
            disc.Fill = new ImageBrush(img) { Stretch = Stretch.UniformToFill };
            initials.Visibility = Visibility.Collapsed;
        }
        catch
        {
            // A missing picture keeps the initials. Nothing to tell anyone.
        }
    }

    /// <summary>The small tier plate from the launcher's neon art: Basic (gold) or Prime
    /// (cyan). Free wears nothing, so this returns null for tier 0.</summary>
    public static Image? TierPlate(int tier, double height)
    {
        if (tier <= 0) return null;
        var art = Art(tier >= 2 ? "features/tier_badge_t2.png" : "features/tier_badge_t1.png", 96);
        if (art == null) return null;
        return new Image
        {
            Source = art,
            Height = height,
            Stretch = Stretch.Uniform,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 0, 0),
            Tag = tier >= 2 ? "friends-tier-2" : "friends-tier-1",
        };
    }
}
