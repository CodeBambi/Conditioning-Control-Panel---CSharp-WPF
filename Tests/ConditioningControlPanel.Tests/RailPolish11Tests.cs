using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using ConditioningControlPanel.Controls.Depth;
using ConditioningControlPanel.Controls.NavRail;
using ConditioningControlPanel.Services.UI;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Nav polish wave 11, lane RAIL (owner 2026-10-07: "the icons on the side rail could look more
/// 3d and less pastel"). Pins the fix: the art is no longer washed by a hue overlay, the ring is
/// the hue made vivid (same hue angle, lower lightness) and lit from the top-left, and each coin
/// carries an inner shadow ring and a small crisp specular crescent instead of a milky veil.
/// CCP_RAIL11_DIR saves rail renders for a look.
/// </summary>
public class RailPolish11Tests
{
    private const double RailColumnHeight = 901 - 36;

    // ------------------------------------------------------------ rules (pure)

    [Fact]
    public void TheArtIsNeverWashedIdleAndOnlyBreathedOnWhenLit()
    {
        Assert.Equal(0, NavRailRules.ArtTintAlpha(false));
        Assert.True(NavRailRules.ArtTintAlpha(true) <= 0x10);
    }

    [Fact]
    public void TheVividHueKeepsItsAngleAndLosesThePastel()
    {
        foreach (var s in NavSections.Order)
        {
            var hue = NavStripRules.Accent(s.Key);
            var vivid = NavRailRules.Vivid(hue);
            var (h0, s0, l0) = NavRailRules.ToHsl(hue);
            var (h1, s1, l1) = NavRailRules.ToHsl(vivid);
            double dh = Math.Abs(h0 - h1); dh = Math.Min(dh, 360 - dh);
            Assert.True(dh <= 3, $"{s.Key}: hue angle moved {dh:F1} degrees");
            Assert.True(l1 <= NavRailRules.VividLightness + 0.01, $"{s.Key}: still pastel, L {l1:F2}");
            Assert.True(s1 >= Math.Min(s0, NavRailRules.VividSaturation) - 0.02, $"{s.Key}: lost saturation");
            Assert.True(l1 < l0, $"{s.Key}: the vivid ring must be deeper than the pastel hue");
        }
    }

    [Fact]
    public void TheRingIsLitFromTheTopLeftAndSitsInItsSocketWhenOn()
    {
        foreach (var s in NavSections.Order)
        {
            var hue = NavStripRules.Accent(s.Key);
            var idle = NavRailRules.RingStops(hue, active: false, hover: false);
            var lit = NavRailRules.RingStops(hue, active: true, hover: false);
            // raised: the top-left stop is the brightest, the bottom-right the darkest
            Assert.True(Luma(idle[0].Color) > Luma(idle[1].Color) && Luma(idle[1].Color) > Luma(idle[^1].Color), s.Key + " idle");
            // on is pressed in: the lamp catches the socket's far (bottom-right) lip
            Assert.True(Luma(lit[0].Color) < Luma(lit[^1].Color), s.Key + " lit");
            Assert.All(lit, st => Assert.Equal(0xFF, st.Color.A));
            Assert.All(idle, st => Assert.Equal(NavRailRules.RingIdleAlpha, st.Color.A));
            Assert.Equal(NavRailRules.RingHoverAlpha, NavRailRules.RingStops(hue, false, true)[1].Color.A);
            // the middle of the ring is the vivid hue itself, so the section still reads at a glance
            var v = NavRailRules.Vivid(hue);
            Assert.Equal((v.R, v.G, v.B), (idle[1].Color.R, idle[1].Color.G, idle[1].Color.B));
        }
    }

    private static double Luma(Color c) => 0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B;

    // ------------------------------------------------------------ the real rail

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(System.IO.Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        Assert.True(dir != null, "could not locate the repo root");
        return dir!.FullName;
    }

    private static readonly string[] HandlerAttributes =
    {
        "Click", "MouseEnter", "MouseLeave", "MouseLeftButtonDown", "MouseLeftButtonUp", "MouseRightButtonUp",
        "MouseDown", "MouseUp", "PreviewMouseLeftButtonDown", "PreviewMouseLeftButtonUp", "PreviewMouseDown",
        "Loaded", "Unloaded", "Checked", "Unchecked", "SizeChanged", "ToolTipOpening", "ContextMenuOpening",
        "KeyDown", "PreviewKeyDown", "ValueChanged", "SelectionChanged", "TextChanged", "GotFocus", "LostFocus",
        "IsVisibleChanged", "MouseWheel", "PreviewMouseWheel",
    };

    /// <summary>The NavSidebar block parsed loose (the NavRailDepthTests recipe).</summary>
    private static Grid BuildRail()
    {
        var xaml = File.ReadAllText(System.IO.Path.Combine(RepoRoot(), "ConditioningControlPanel", "MainWindow", "MainWindow.xaml"));
        var head = Regex.Match(xaml, @"<Window\b(.*?)>", RegexOptions.Singleline).Groups[1].Value;
        var ns = string.Join(" ", Regex.Matches(head, @"xmlns(:\w+)?=""[^""]*""").Select(m => m.Value));
        ns = Regex.Replace(ns, @"clr-namespace:(ConditioningControlPanel[\w.]*)""", "clr-namespace:$1;assembly=ConditioningControlPanel\"");

        var resources = Regex.Match(xaml, @"<Window\.Resources>(.*?)</Window\.Resources>", RegexOptions.Singleline).Groups[1].Value;
        var start = xaml.IndexOf("<Border x:Name=\"NavSidebar\"", StringComparison.Ordinal);
        var stop = xaml.IndexOf("<!-- Header Bar", start, StringComparison.Ordinal);
        Assert.True(start > 0 && stop > start, "the NavSidebar block stopped parsing");
        var sidebar = xaml.Substring(start, stop - start);
        sidebar = sidebar.Substring(0, sidebar.LastIndexOf("</Border>", StringComparison.Ordinal) + "</Border>".Length);
        var firstTagEnd = sidebar.IndexOf('>');
        sidebar = Regex.Replace(sidebar.Substring(0, firstTagEnd), @"Grid\.(Row|RowSpan|Column|ColumnSpan)=""\d+""", string.Empty)
                  + sidebar.Substring(firstTagEnd);

        var loose = "<Grid " + ns + "><Grid.Resources>" + resources + "</Grid.Resources>" + sidebar + "</Grid>";
        foreach (var attr in HandlerAttributes)
            loose = Regex.Replace(loose, @"\s" + attr + @"=""[A-Za-z_][\w]*""", string.Empty);
        loose = Regex.Replace(loose, @"\sx:FieldModifier=""\w+""", string.Empty);

        var host = (Grid)XamlReader.Parse(loose);
        host.Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Parse(File.ReadAllText(
            System.IO.Path.Combine(RepoRoot(), "ConditioningControlPanel", "Resources", "Theme", "Depth.xaml"))));
        return host;
    }

    private static Button[] Rows(Grid host)
    {
        var rows = ((StackPanel)host.FindName("NavSectionRows")).Children.OfType<Button>().ToList();
        rows.Add((Button)host.FindName("DoorSettings"));
        return rows.ToArray();
    }

    private static Border Tagged(Panel face, string tag) =>
        face.Children.OfType<Border>().First(b => (b.Tag as string) == tag);

    /// <summary>Paints the rail the way NavRail.cs CacheNavSectionRows + PaintNavRowActive do, with
    /// row <paramref name="lit"/> active and row <paramref name="hovered"/> under the pointer.</summary>
    private static (MainWindow.NavCoinParts Coin, Grid Face)[] Paint(Grid host, int lit, int hovered)
    {
        var rows = Rows(host);
        var result = new (MainWindow.NavCoinParts, Grid)[rows.Length];
        for (int i = 0; i < rows.Length; i++)
        {
            var face = (Grid)rows[i].Content;
            var hue = NavStripRules.Accent(NavRailRules.SectionForDoorTag((string)rows[i].Tag));
            var tile = face.Children.OfType<Border>().First(b => b.Tag is not string);
            tile.Background = new SolidColorBrush(NavRailRules.WithAlpha(hue, NavRailRules.TileTintAlpha));
            var tint = Tagged(face, "navtint");
            var ring = Tagged(face, "navring");
            MainWindow.PaintNavTintPart(tint, hue, i == lit);
            var coin = MainWindow.BuildNavCoin(face, tile, hue);
            MainWindow.PaintNavRingParts(tile, ring, hue, i == lit, i == hovered);
            var lift = new TranslateTransform();
            face.RenderTransform = lift;
            MainWindow.ApplyNavCoinState(coin, lift, false, active: i == lit, hovered: i == hovered, ms: 0, spring: false);
            if (face.Children.OfType<Ellipse>().FirstOrDefault() is { } glow && i == lit)
            {
                var g = new RadialGradientBrush();
                g.GradientStops.Add(new GradientStop(hue, 0));
                g.GradientStops.Add(new GradientStop(NavRailRules.WithAlpha(hue, 0x80), 0.6));
                g.GradientStops.Add(new GradientStop(NavRailRules.WithAlpha(hue, 0), 1));
                glow.Fill = g;
                glow.Opacity = 0.55;
            }
            result[i] = (coin, face);
        }
        return result;
    }

    private static void Layout(FrameworkElement e, double w, double h)
    {
        e.Measure(new Size(w, h));
        e.Arrange(new Rect(0, 0, w, h));
        e.UpdateLayout();
    }

    [Fact]
    public void EveryCoinCarriesAnInnerShadowAndASmallCrispCrescentAndNoEffect()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var host = BuildRail();
            foreach (var (coin, face) in Paint(host, lit: 0, hovered: 2))
            {
                var kids = face.Children.Cast<UIElement>().ToList();
                int art = kids.FindIndex(k => k is Viewbox);
                int ring = kids.FindIndex(k => k is Border b && (b.Tag as string) == "navring");
                int inner = kids.IndexOf(coin.Inner), spec = kids.IndexOf(coin.Specular), outer = kids.IndexOf(coin.Outer);
                Assert.True(art < inner && inner < ring, "inner shadow sits over the art, under the ring");
                Assert.True(ring < spec, "the crescent is the top layer of the face (glass over everything)");
                Assert.True(outer >= 0 && outer < art, "the outer dark rim sits under the art");
                foreach (var part in new FrameworkElement[] { coin.Inner, coin.Specular, coin.Outer })
                {
                    Assert.Null(part.Effect);
                    Assert.False(part.IsHitTestVisible);
                }
                // the crescent is small: it covers well under a quarter of the face
                Layout(host, 96, RailColumnHeight);
                var bounds = coin.Specular.Data.Bounds;
                Assert.True(bounds.Width * bounds.Height < NavRailRules.CoinRimSize * NavRailRules.CoinRimSize * 0.5);
                Assert.True(coin.Outer.Width <= 60, "the medallion stays inside 60 px");
            }
        });
    }

    [Fact]
    public void TheRailStillFitsAndRendersForALook()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var dir = Environment.GetEnvironmentVariable("CCP_RAIL11_DIR");
            foreach (var (lit, name) in new[] { (0, "home"), (1, "studio"), (4, "social") })
            {
                var host = BuildRail();
                var rows = Rows(host);
                Layout(host, 96, RailColumnHeight);
                var before = rows.Select(r => r.ActualHeight).ToArray();
                Paint(host, lit, hovered: lit == 0 ? 2 : 0);
                Layout(host, 96, RailColumnHeight);
                Assert.Equal(before, rows.Select(r => r.ActualHeight).ToArray());
                var gear = (FrameworkElement)host.FindName("DoorSettings");
                Assert.True(gear.TranslatePoint(new Point(0, gear.ActualHeight), host).Y <= RailColumnHeight);

                if (dir is { Length: > 0 })
                {
                    Directory.CreateDirectory(dir);
                    var shot = new RenderTargetBitmap(96 * 3, (int)RailColumnHeight * 3, 288, 288, PixelFormats.Pbgra32);
                    shot.Render(host);
                    var top = rows[Math.Max(0, lit - 1)].TranslatePoint(new Point(0, 0), host).Y;
                    var crop = new CroppedBitmap(shot, new Int32Rect(0, (int)(top * 3), 96 * 3, (int)Math.Min(76 * 4 * 3, shot.PixelHeight - top * 3)));
                    var enc = new PngBitmapEncoder();
                    enc.Frames.Add(BitmapFrame.Create(crop));
                    using var fs = File.Create(System.IO.Path.Combine(dir, $"rail-{name}.png"));
                    enc.Save(fs);
                }
            }
        });
    }
}
