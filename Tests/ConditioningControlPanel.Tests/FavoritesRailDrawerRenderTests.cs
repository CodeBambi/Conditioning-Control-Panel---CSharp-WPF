using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ConditioningControlPanel.Views.Tabs;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Nav polish wave 3, DRAWER lane: the FAVORITES + RECENT column left the space beside the
/// section rail for a drawer at the right edge of Home (owner, 2026-10-06). This realizes the
/// REAL SettingsTabView at the page width the 1585 px window leaves after the 96 px rail, closed
/// and open, and holds the three things the owner will look at: the handle is there in both, the
/// mosaic does not move, and the body pushes the browser column rather than floating over it.
/// Set <c>CCP_NAV_PNG_DIR</c> to also write drawer-closed.png and drawer-open.png.
/// </summary>
[Collection(CompanionWpfRenderCollection.Name)]
public class FavoritesRailDrawerRenderTests
{
    private const double PageWidth = 1585 - 96;
    private const double PageHeight = 865;

    private static Grid Realize(FrameworkElement element)
    {
        var host = new Grid { Background = new SolidColorBrush(Color.FromRgb(0x12, 0x10, 0x1F)) };
        host.Children.Add(element);
        host.Measure(new Size(PageWidth, PageHeight));
        host.Arrange(new Rect(0, 0, PageWidth, PageHeight));
        host.UpdateLayout();
        return host;
    }

    private static void MaybeRender(FrameworkElement host, string name)
    {
        var dir = Environment.GetEnvironmentVariable("CCP_NAV_PNG_DIR");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        var bmp = new RenderTargetBitmap((int)Math.Ceiling(host.ActualWidth), (int)Math.Ceiling(host.ActualHeight),
            96, 96, PixelFormats.Pbgra32);
        bmp.Render(host);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var fs = File.Create(Path.Combine(dir, name));
        enc.Save(fs);
    }

    private static Rect Bounds(FrameworkElement e, Visual root) =>
        e.TransformToAncestor(root).TransformBounds(new Rect(0, 0, e.ActualWidth, e.ActualHeight));

    [Fact]
    public void The_drawer_opens_at_the_right_edge_and_the_mosaic_stays_put() => WpfRenderHarness.OnStaThread(() =>
    {
        var page = new SettingsTabView();
        // A few chips so the open state has something to show (the real ones are built by
        // MainWindow from the palette; the style is the page's own).
        var style = page.FavoritesRail.TryFindResource("RailChipStyle") as Style;
        Assert.NotNull(style);
        foreach (var label in new[] { "Deeper", "Lobby", "Remote" })
            page.FavoritesList.Children.Add(new Button { Style = style, Tag = "tab." + label.ToLowerInvariant(),
                Content = new TextBlock { Text = label, Style = page.FavoritesRail.TryFindResource("RailChipCaption") as Style } });
        page.FavoritesEmpty.Visibility = Visibility.Collapsed;

        page.SetFavoritesDrawer(false, animate: false);
        var host = Realize(page);
        Assert.False(page.FavoritesDrawerIsOpen);
        var handleClosed = Bounds(page.FavoritesDrawerHandle, host);
        var mosaicClosed = Bounds(page.VelvetFeatureGrid, host);
        Assert.Equal(0, page.FavoritesDrawerBody.ActualWidth);
        // The handle is at the right edge, readable: 18 wide (22 less its margins), most of the row tall.
        Assert.InRange(handleClosed.Right, PageWidth - 3, PageWidth);
        Assert.InRange(handleClosed.Width, 16, 22);
        Assert.True(handleClosed.Height > 400, $"the handle is only {handleClosed.Height} tall");
        Assert.Equal("‹", page.FavoritesDrawerChevron.Text);
        MaybeRender(host, "drawer-closed.png");
        host.Children.Clear();

        page.SetFavoritesDrawer(true, animate: false);
        host = Realize(page);
        Assert.True(page.FavoritesDrawerIsOpen);
        Assert.Equal(SettingsTabView.FavoritesDrawerWidth, page.FavoritesDrawerBody.ActualWidth);
        var handleOpen = Bounds(page.FavoritesDrawerHandle, host);
        var mosaicOpen = Bounds(page.VelvetFeatureGrid, host);
        var rail = Bounds(page.FavoritesRail, host);

        // The mosaic does not move by a pixel; the handle moved left by exactly the body.
        Assert.Equal(mosaicClosed, mosaicOpen);
        Assert.Equal(handleClosed.X - SettingsTabView.FavoritesDrawerWidth, handleOpen.X, 3);
        // The rail sits right of the handle, inside the page, at its pinned 87.
        Assert.True(rail.Left >= handleOpen.Right, "the rail overlaps its own handle");
        Assert.InRange(rail.Right, PageWidth - 6, PageWidth);
        Assert.Equal(87, rail.Width);
        Assert.Equal("›", page.FavoritesDrawerChevron.Text);
        Assert.NotNull(page.FindFavoriteChip("tab.lobby"));
        MaybeRender(host, "drawer-open.png");
    });
}
