using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Shapes;
using ConditioningControlPanel.Controls.Depth;
using ConditioningControlPanel.Controls.NavRail;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.UI;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Nav polish wave 10 (depth), lane A: every rail medallion is a COIN in a socket. Pins the
/// recipe (which part wears which Depth.xaml brush, where it sits in the row), the travel law on
/// every state (idle raised, hover lift, lit in its socket, pressed down), the hue-tinted shadows,
/// the rail's shadow on the page, and the pixel budget (the coin adds no height to the rail).
/// </summary>
public class NavRailDepthTests
{
    private const double RailColumnHeight = 901 - 36;

    // ------------------------------------------------------------ the rules (pure)

    [Theory]
    [InlineData(false, false, false, 0.0)]
    [InlineData(false, false, true, -DepthRules.HoverLiftPx)]
    [InlineData(false, true, false, DepthRules.ActiveSinkPx)]
    [InlineData(false, true, true, DepthRules.ActiveSinkPx)]   // a lit coin never lifts on hover
    [InlineData(true, false, true, DepthRules.PressTravelPx)]  // a press wins over everything
    [InlineData(true, true, false, DepthRules.PressTravelPx)]
    public void ACoinTravelsByTheSharedLaw(bool pressed, bool active, bool hovered, double expected)
    {
        Assert.Equal(expected, NavRailRules.CoinTravel(pressed, active, hovered));
        Assert.Equal(DepthRules.TravelFor(true, pressed, active, hovered), NavRailRules.CoinTravel(pressed, active, hovered));
    }

    [Theory]
    [InlineData(false, false, false, DepthRules.RaisedPx)]
    [InlineData(false, false, true, DepthRules.RaisedPx + DepthRules.HoverLiftPx)]
    [InlineData(false, true, false, 0.0)]
    [InlineData(true, false, false, 0.0)]
    public void OnIsPressedInSoALitOrPressedCoinThrowsNoShadow(bool pressed, bool active, bool hovered, double expected)
        => Assert.Equal(expected, NavRailRules.CoinShadow(pressed, active, hovered));

    [Fact]
    public void TheReleaseSpringPassesItsTargetByTheOvershootThenSettles()
    {
        // pressed (+2) released into the socket (+1): overshoots up to 0
        Assert.Equal(DepthRules.ActiveSinkPx - DepthRules.ReleaseOvershootPx,
            NavRailRules.CoinOvershoot(DepthRules.PressTravelPx, DepthRules.ActiveSinkPx));
        // pressed (+2) released to hover (-2): overshoots to -3
        Assert.Equal(-DepthRules.HoverLiftPx - DepthRules.ReleaseOvershootPx,
            NavRailRules.CoinOvershoot(DepthRules.PressTravelPx, -DepthRules.HoverLiftPx));
        Assert.Equal(1.0, NavRailRules.CoinOvershoot(1.0, 1.0));
    }

    [Fact]
    public void TheTiltNeverPassesTheLauncherNumberAndOnlyIdleCoinsLean()
    {
        foreach (var nx in new[] { -3.0, -1, -0.4, 0, 0.5, 1, 4 })
            foreach (var ny in new[] { -2.0, -1, 0, 0.3, 1, 5 })
                Assert.InRange(Math.Abs(NavRailRules.CoinTilt(nx, ny)), 0, DepthRules.TiltDegrees + 1e-9);
        Assert.Equal(-DepthRules.TiltDegrees, NavRailRules.CoinTilt(1, 1), 6);
        Assert.Equal(0, NavRailRules.CoinTilt(0, 0));

        Assert.True(NavRailRules.CoinTilts(false, MotionLevel.Full, PerformanceTier.Balanced));
        Assert.False(NavRailRules.CoinTilts(true, MotionLevel.Full, PerformanceTier.Balanced));
        Assert.False(NavRailRules.CoinTilts(false, MotionLevel.Off, PerformanceTier.Balanced));
        Assert.False(NavRailRules.CoinTilts(false, MotionLevel.Full, PerformanceTier.Performance));
        Assert.Equal(90, NavRailRules.CoinTiltMs);
    }

    [Fact]
    public void TheCoinShadowAndTheRailShadowAreTintedByTheHue()
    {
        foreach (var section in NavRailRules.RailSections.Select(s => s.Key).Append(NavSections.Settings))
        {
            var hue = NavStripRules.Accent(section);
            var disc = NavRailRules.CoinDiscStops(hue);
            Assert.Equal(DepthRules.ShadowColor(hue), disc[0].Color);
            Assert.Equal((byte)Math.Round(DepthRules.ShadowAlpha * 255), disc[0].Color.A);
            Assert.Equal((byte)0x5C, disc[1].Color.A);
            Assert.Equal((byte)0, disc[^1].Color.A);
            Assert.Equal(1.0, disc[^1].Offset);

            var rail = NavRailRules.RailShadowStops(hue);
            Assert.Equal((byte)0xBF, rail[0].Color.A);
            Assert.Equal((byte)0, rail[^1].Color.A);
            // same ink, same tint: the RGB of a shadow never depends on its strength
            Assert.Equal(disc[0].Color.R, rail[0].Color.R);
            Assert.Equal(disc[0].Color.B, rail[0].Color.B);
        }
    }

    // ------------------------------------------------------------ the real rail

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(System.IO.Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        Assert.True(dir != null, "could not locate the repo root");
        return dir!.FullName;
    }

    private static string WindowXaml() =>
        File.ReadAllText(System.IO.Path.Combine(RepoRoot(), "ConditioningControlPanel", "MainWindow", "MainWindow.xaml"));

    private static ResourceDictionary DepthDictionary() =>
        (ResourceDictionary)XamlReader.Parse(File.ReadAllText(
            System.IO.Path.Combine(RepoRoot(), "ConditioningControlPanel", "Resources", "Theme", "Depth.xaml")));

    private static readonly string[] HandlerAttributes =
    {
        "Click", "MouseEnter", "MouseLeave", "MouseLeftButtonDown", "MouseLeftButtonUp", "MouseRightButtonUp",
        "MouseDown", "MouseUp", "PreviewMouseLeftButtonDown", "PreviewMouseLeftButtonUp", "PreviewMouseDown",
        "Loaded", "Unloaded", "Checked", "Unchecked", "SizeChanged", "ToolTipOpening", "ContextMenuOpening",
        "KeyDown", "PreviewKeyDown", "ValueChanged", "SelectionChanged", "TextChanged", "GotFocus", "LostFocus",
        "IsVisibleChanged", "MouseWheel", "PreviewMouseWheel",
    };

    /// <summary>The NavSidebar block parsed loose (the NavFinalRenderTests recipe), with the depth
    /// dictionary merged so the coin's resource references resolve.</summary>
    private static Grid BuildRail()
    {
        var xaml = WindowXaml();
        var head = Regex.Match(xaml, @"<Window\b(.*?)>", RegexOptions.Singleline).Groups[1].Value;
        var ns = string.Join(" ", Regex.Matches(head, @"xmlns(:\w+)?=""[^""]*""").Select(m => m.Value))
            .Replace("clr-namespace:ConditioningControlPanel\"", "clr-namespace:ConditioningControlPanel;assembly=ConditioningControlPanel\"");
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
        host.Resources.MergedDictionaries.Add(DepthDictionary());
        return host;
    }

    private static Button[] Rows(Grid host)
    {
        var rows = ((StackPanel)host.FindName("NavSectionRows")).Children.OfType<Button>().ToList();
        rows.Add((Button)host.FindName("DoorSettings"));
        return rows.ToArray();
    }

    /// <summary>The tile exactly as NavRail.cs CacheNavSectionRows finds it: the first untagged Border.</summary>
    private static Border TileOf(Panel face) =>
        face.Children.OfType<Border>().First(b => b.Tag is not string);

    private static (MainWindow.NavCoinParts Coin, Grid Face, Border Tile) Coin(Button row)
    {
        var face = (Grid)row.Content;
        var tile = TileOf(face);
        var hue = NavStripRules.Accent(NavRailRules.SectionForDoorTag((string)row.Tag));
        return (MainWindow.BuildNavCoin(face, tile, hue), face, tile);
    }

    private static void Layout(FrameworkElement e, double w, double h)
    {
        e.Measure(new Size(w, h));
        e.Arrange(new Rect(0, 0, w, h));
        e.UpdateLayout();
    }

    [Fact]
    public void EveryRowIsACoinWithItsPartsInTheLampOrder()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var host = BuildRail();
            var depth = host.Resources.MergedDictionaries.Last();
            var rows = Rows(host);
            Assert.Equal(8, rows.Length);
            foreach (var row in rows)
            {
                var (coin, face, tile) = Coin(row);
                var kids = face.Children.Cast<UIElement>().ToList();
                int disc = kids.IndexOf(coin.Disc), t = kids.IndexOf(tile), dish = kids.IndexOf(coin.Dish);
                int art = kids.FindIndex(k => k is Viewbox);
                int tint = kids.FindIndex(k => k is Border b && (b.Tag as string) == "navtint");
                int ring = kids.FindIndex(k => k is Border b && (b.Tag as string) == "navring");
                int socket = kids.IndexOf(coin.Socket), rim = kids.IndexOf(coin.Rim);

                // disc UNDER the tile, dish over the tile UNDER the art, shade + lip over the wash UNDER the ring
                Assert.True(disc < t && t < dish && dish < art, row.Name + ": disc/tile/dish/art order");
                Assert.True(tint < socket && socket < rim && rim < ring, row.Name + ": wash/socket/lip/ring order");

                Assert.Equal(depth["DepthCoinDish"], coin.Dish.Background);
                Assert.Equal(depth["DepthPressedShade"], coin.Socket.Background);
                Assert.Equal(depth["DepthCoinRim"], coin.Rim.BorderBrush);
                Assert.Equal(new Thickness(1), coin.Rim.BorderThickness);
                Assert.Equal(tile.Width, coin.Dish.Width);
                Assert.Equal(tile.Width - 2 * NavRailRules.RingIdleThickness, coin.Rim.Width);

                // the contact disc wears the row's own shadow colour
                var fill = Assert.IsType<RadialGradientBrush>(coin.Disc.Fill);
                var hue = NavStripRules.Accent(NavRailRules.SectionForDoorTag((string)row.Tag));
                Assert.Equal(DepthRules.ShadowColor(hue), fill.GradientStops[0].Color);

                // the law: no Effect on the coin's own parts, nothing hit-testable
                foreach (var part in new FrameworkElement[] { coin.Disc, coin.Dish, coin.Socket, coin.Rim })
                {
                    Assert.Null(part.Effect);
                    Assert.False(part.IsHitTestVisible);
                }
            }
        });
    }

    [Fact]
    public void TheCoinStatesFollowOnIsPressedIn()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var host = BuildRail();
            var depth = host.Resources.MergedDictionaries.Last();
            var (coin, _, _) = Coin(Rows(host)[0]);
            var lift = new TranslateTransform();

            // idle: raised lip, shadow RaisedPx under it, face at rest
            MainWindow.ApplyNavCoinState(coin, lift, pressed: false, active: false, hovered: false, ms: 0, spring: false);
            Assert.Equal(0, lift.Y);
            Assert.Equal(1, coin.Disc.Opacity);
            Assert.Equal(DepthRules.RaisedPx, coin.DiscShift.Y);
            Assert.Equal(0, coin.Socket.Opacity);
            Assert.Equal(depth["DepthCoinRim"], coin.Rim.BorderBrush);

            // hover: lifts, its shadow lengthens
            MainWindow.ApplyNavCoinState(coin, lift, false, false, true, 0, false);
            Assert.Equal(-DepthRules.HoverLiftPx, lift.Y);
            Assert.Equal(DepthRules.RaisedPx + DepthRules.HoverLiftPx, coin.DiscShift.Y);

            // lit: sits in its socket, pressed lip + shade, no shadow
            MainWindow.ApplyNavCoinState(coin, lift, false, true, true, 0, false);
            Assert.Equal(DepthRules.ActiveSinkPx, lift.Y);
            Assert.Equal(0, coin.Disc.Opacity);
            Assert.Equal(1, coin.Socket.Opacity);
            Assert.Equal(depth["DepthPressedBevel"], coin.Rim.BorderBrush);

            // pressed: travels the press distance, seated light
            MainWindow.ApplyNavCoinState(coin, lift, true, false, true, 0, false);
            Assert.Equal(DepthRules.PressTravelPx, lift.Y);
            Assert.Equal(0, coin.Disc.Opacity);
            Assert.Equal(depth["DepthPressedBevel"], coin.Rim.BorderBrush);
        });
    }

    [Fact]
    public void TheCoinsAddNoHeightAndTheRailStillFitsItsColumn()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var host = BuildRail();
            var rows = Rows(host);
            Layout(host, 96, RailColumnHeight);
            var before = rows.Select(r => r.ActualHeight).ToArray();

            var coins = rows.Select(r => Coin(r).Coin).ToArray();
            var sidebar = (Border)host.FindName("NavSidebar");
            var inner = (Grid)sidebar.Child;
            inner.Measure(new Size(96, double.PositiveInfinity));
            double natural = inner.DesiredSize.Height + inner.Margin.Top + inner.Margin.Bottom;
            Layout(host, 96, RailColumnHeight);

            Assert.Equal(before, rows.Select(r => r.ActualHeight).ToArray());
            Assert.True(natural <= RailColumnHeight, $"the rail needs {natural:F0} of {RailColumnHeight:F0}");
            var gear = (FrameworkElement)host.FindName("DoorSettings");
            Assert.True(gear.TranslatePoint(new Point(0, gear.ActualHeight), host).Y <= RailColumnHeight);

            // CCP_NAV_FINAL_DIR: save the rail at 3x with Home lit and Companion hovered, for a look.
            if (Environment.GetEnvironmentVariable("CCP_NAV_FINAL_DIR") is { Length: > 0 } dir)
            {
                for (int i = 0; i < rows.Length; i++)
                {
                    var face = (Grid)rows[i].Content;
                    var lift = new TranslateTransform();
                    face.RenderTransform = lift;
                    MainWindow.ApplyNavCoinState(coins[i], lift, false, active: i == 0, hovered: i == 2, ms: 0, spring: false);
                }
                Layout(host, 96, RailColumnHeight);
                Directory.CreateDirectory(dir);
                var shot = new System.Windows.Media.Imaging.RenderTargetBitmap(96 * 3, (int)RailColumnHeight * 3, 288, 288, PixelFormats.Pbgra32);
                shot.Render(host);
                var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
                enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(shot));
                using var fs = File.Create(System.IO.Path.Combine(dir, "depth-rail.png"));
                enc.Save(fs);
            }
        });
    }

    [Fact]
    public void TheRailThrowsItsShadowOntoThePage()
    {
        var xaml = WindowXaml();
        int sidebarEnd = xaml.IndexOf("<friends:FriendsRailChip", StringComparison.Ordinal);
        int rect = xaml.IndexOf("<Rectangle x:Name=\"NavRailShadow\"", StringComparison.Ordinal);
        int hud = xaml.IndexOf("<Border x:Name=\"HudBand\"", StringComparison.Ordinal);
        Assert.True(sidebarEnd > 0 && rect > sidebarEnd && rect < hud, "NavRailShadow must sit right after the NavSidebar block");
        var tag = xaml.Substring(rect, xaml.IndexOf("/>", rect, StringComparison.Ordinal) - rect);
        Assert.Contains("Grid.Column=\"1\"", tag);
        Assert.Contains("Grid.Row=\"1\"", tag);
        Assert.Contains("Grid.RowSpan=\"5\"", tag);
        Assert.Contains("HorizontalAlignment=\"Left\"", tag);
        Assert.Contains("Panel.ZIndex=\"59\"", tag);
        Assert.Contains("IsHitTestVisible=\"False\"", tag);
        Assert.Contains("{DynamicResource DepthRailShadow}", tag);
        Assert.Contains($"Width=\"{DepthRules.RailShadowPx:0}\"", tag);
    }
}
