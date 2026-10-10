using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using ConditioningControlPanel.Controls.Depth;
using ConditioningControlPanel.Features;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Views.Tabs;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Nav polish wave 10, Home lane: the browser card is a bezel around its screen (never over it),
/// the header is the bezel's lip, the billboard is a well with raised coins, the account strip is
/// a ledge, the logo plate is the deepest well, and the mosaic takes the shared lamp and travel.
/// </summary>
[Collection(CompanionWpfRenderCollection.Name)]
public class HomeDepthTests
{
    private const double PageWidth = 1585 - 96;
    private const double PageHeight = 865;

    // ---- the mosaic's travel ----------------------------------------------------------------

    [Fact]
    public void Mosaic_travel_follows_the_shared_rule()
    {
        Assert.Equal(DepthRules.PressTravelPx, DashboardCardDepth.TravelFor(true, pressed: true, active: true, hovered: true));
        Assert.Equal(DepthRules.ActiveSinkPx, DashboardCardDepth.TravelFor(true, false, active: true, hovered: true));
        Assert.Equal(-DepthRules.HoverLiftPx, DashboardCardDepth.TravelFor(true, false, false, hovered: true));
        // An off tile at rest stands half a hover lift proud of its socket.
        Assert.Equal(-DepthRules.HoverLiftPx / 2, DashboardCardDepth.TravelFor(true, false, false, false));
        Assert.Equal(0, DashboardCardDepth.TravelFor(false, true, true, true));
    }

    [Fact]
    public void Mosaic_bevel_is_pressed_when_on_or_pressed_and_raised_otherwise()
    {
        Assert.Equal("DepthPressedBevel", DashboardCardDepth.BevelKeyFor(pressed: false, active: true));
        Assert.Equal("DepthPressedBevel", DashboardCardDepth.BevelKeyFor(pressed: true, active: false));
        Assert.Equal("DepthRaisedBevel", DashboardCardDepth.BevelKeyFor(false, false));
    }

    [Fact]
    public void Mosaic_tilt_never_passes_the_launcher_number()
    {
        foreach (var nx in new[] { -3.0, -1, -0.4, 0, 0.7, 1, 5 })
            foreach (var ny in new[] { -2.0, -1, 0, 0.5, 1, 9 })
                Assert.InRange(Math.Abs(DashboardCardDepth.TiltFor(nx, ny)), 0, DepthRules.TiltDegrees);
        Assert.Equal(DepthRules.TiltDegrees, DashboardCardDepth.TiltFor(1, -1));
        Assert.Equal(0, DashboardCardDepth.TiltFor(0, 0));
    }

    [Fact]
    public void Feature_card_wears_a_one_px_bevel_and_a_sunken_socket() => WpfRenderHarness.OnStaThread(() =>
    {
        var card = new FeatureCard();
        Assert.Equal(new Thickness(1), card.DepthBevel.BorderThickness);
        Assert.False(card.DepthBevel.IsHitTestVisible);
        Assert.False(card.DepthSocket.IsHitTestVisible);
        // The socket holds a well: top band, left band, lit foot.
        var bands = Assert.IsType<Grid>(card.DepthSocket.Child);
        Assert.Equal(3, bands.Children.Count);
    });

    // ---- the page ------------------------------------------------------------------------------

    private static Grid Realize(FrameworkElement element)
    {
        var host = new Grid { Background = new SolidColorBrush(Color.FromRgb(0x12, 0x10, 0x1F)) };
        host.Children.Add(element);
        host.Measure(new Size(PageWidth, PageHeight));
        host.Arrange(new Rect(0, 0, PageWidth, PageHeight));
        host.UpdateLayout();
        return host;
    }

    private static Rect Bounds(FrameworkElement e, Visual root) =>
        e.TransformToAncestor(root).TransformBounds(new Rect(0, 0, e.ActualWidth, e.ActualHeight));

    [Fact]
    public void The_screen_sits_inset_in_its_bezel_and_nothing_draws_over_it() => WpfRenderHarness.OnStaThread(() =>
    {
        var page = new SettingsTabView();
        var host = Realize(page);
        var bezel = Bounds(page.BrowserBezel, host);
        var screen = Bounds(page.BrowserFoldBody, host);
        Assert.True(bezel.Width > 100 && bezel.Height > 100, $"bezel {bezel}");
        // Inset 4-6 px on every side: the ring the well bands live in.
        Assert.InRange(screen.Left - bezel.Left, 4, 6);
        Assert.InRange(bezel.Right - screen.Right, 4, 6);
        Assert.InRange(screen.Top - bezel.Top, 4, 6);
        Assert.InRange(bezel.Bottom - screen.Bottom, 4, 6);
        Assert.False(page.BrowserBezel.IsHitTestVisible);
        // The bezel is declared BEFORE the screen in the same cell: it paints under it.
        var cell = (Panel)page.BrowserBezel.Parent;
        Assert.True(cell.Children.IndexOf(page.BrowserBezel) < cell.Children.IndexOf(page.BrowserFoldBody));
        // The ledge's upward shadow stays out of the screen rect.
        var ledge = Bounds(page.AccountLedgeUp, host);
        Assert.Equal(DepthRules.LedgeUpPx, page.AccountLedgeUp.Height);
        Assert.False(page.AccountLedgeUp.IsHitTestVisible);
        Assert.True(ledge.Top >= screen.Bottom, $"ledge {ledge.Top} over the screen ending {screen.Bottom}");
        // The lip sits behind the header, hit-test off.
        Assert.False(page.BrowserLip.IsHitTestVisible);
    });

    [Fact]
    public void A_folded_card_keeps_its_header_only() => WpfRenderHarness.OnStaThread(() =>
    {
        var page = new SettingsTabView();
        page.BrowserFoldBody.Visibility = Visibility.Collapsed;
        Realize(page);
        Assert.Equal(Visibility.Collapsed, page.BrowserBezel.Visibility);
    });

    [Fact]
    public void The_logo_plate_is_the_deepest_well_on_the_page() => WpfRenderHarness.OnStaThread(() =>
    {
        var page = new SettingsTabView();
        Assert.True(page.LogoWellTop.Height > DepthRules.WellPx, "the logo well must be deeper than any other");
        Assert.True(page.LogoWellTop.Height >= page.BillboardWellTop.Height);
        Assert.False(page.LogoWellTop.IsHitTestVisible);
        Assert.False(page.LogoWellLeft.IsHitTestVisible);
        Assert.False(page.AccountStripSheen.IsHitTestVisible);
        Assert.Equal(DepthRules.RaisedPx, page.AccountStripDrop.Height);
    });

    [Fact]
    public void PaintDepthHome_tints_the_page_shadows_toward_the_hue() => WpfRenderHarness.OnStaThread(() =>
    {
        var page = new SettingsTabView();
        var hue = Color.FromRgb(0xB7, 0x9C, 0xFF);
        page.PaintDepthHome(hue);
        var band = Assert.IsType<LinearGradientBrush>(page.Resources["HomeDepthDropBand"]);
        Assert.Equal(DepthRules.ShadowColor(hue), band.GradientStops[0].Color);
        Assert.Equal(0, band.GradientStops[^1].Color.A);
        var disc = Assert.IsType<RadialGradientBrush>(page.Resources["HomeDepthDropDisc"]);
        Assert.Equal(DepthRules.ShadowColor(hue), disc.GradientStops[0].Color);
        // The strip's drop band follows the repaint (DynamicResource).
        Assert.Same(band, page.AccountStripDrop.Fill);
    });
}
