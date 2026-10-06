using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Views.Tabs;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Nav polish wave 4, BROWSER lane (owner, 2026-10-06): "put an unmissable dropdown arrow here on
/// top of the browser, the little grey icon we got is basically invisible - also I think we can
/// lose the companion fraction of the UI here". This realizes the REAL SettingsTabView at the page
/// width a 1585 px window leaves after the 96 px rail, paints the browser card folded and open the
/// way MainWindow.DashboardFold.cs settles it, and holds what the owner will look at: the arrow is
/// a wide pill in the header's middle, it glows only while the card is shut, and the companion
/// strip is gone so the account strip sits right under the card.
/// Set <c>CCP_NAV_PNG_DIR</c> to also write browser-folded.png and browser-open.png (the right
/// column only).
/// </summary>
[Collection(CompanionWpfRenderCollection.Name)]
public class DashboardFoldArrowRenderTests
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

    private static Rect Bounds(FrameworkElement e, Visual root) =>
        e.TransformToAncestor(root).TransformBounds(new Rect(0, 0, e.ActualWidth, e.ActualHeight));

    private static void MaybeRender(FrameworkElement host, Rect crop, string name)
    {
        var dir = Environment.GetEnvironmentVariable("CCP_NAV_PNG_DIR");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        var bmp = new RenderTargetBitmap((int)Math.Ceiling(host.ActualWidth), (int)Math.Ceiling(host.ActualHeight),
            96, 96, PixelFormats.Pbgra32);
        bmp.Render(host);
        var r = new Int32Rect((int)crop.X, (int)crop.Y,
            (int)Math.Min(crop.Width, bmp.PixelWidth - (int)crop.X),
            (int)Math.Min(crop.Height, bmp.PixelHeight - (int)crop.Y));
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(new CroppedBitmap(bmp, r)));
        using var fs = File.Create(Path.Combine(dir, name));
        enc.Save(fs);
    }

    /// <summary>The settle's row arithmetic, as MainWindow.SettleBrowserFold writes it.</summary>
    private static void Fold(SettingsTabView page, bool collapsed)
    {
        var star = new GridLength(1, GridUnitType.Star);
        page.BrowserCardRow.Height = BrowserFoldRule.CardRowIsStar(collapsed) ? star : GridLength.Auto;
        page.BrowserFoldRow.Height = BrowserFoldRule.FoldRowIsStar(collapsed) ? star : new GridLength(0);
        page.BrowserFoldBody.Visibility = BrowserFoldRule.BodyShown(collapsed) ? Visibility.Visible : Visibility.Collapsed;
        page.PaintFoldArrow(collapsed);
    }

    [Fact]
    public void The_companion_strip_is_gone() => WpfRenderHarness.OnStaThread(() =>
    {
        var page = new SettingsTabView();
        Assert.Null(page.FindName("CompanionStrip"));
    });

    [Fact]
    public void The_fold_arrow_is_a_wide_pill_that_glows_only_while_shut() => WpfRenderHarness.OnStaThread(() =>
    {
        var page = new SettingsTabView();
        var column = (FrameworkElement)page.BrowserCardFrame.Parent;

        // ---- folded ----
        Fold(page, collapsed: true);
        var host = Realize(page);
        var arrow = Bounds(page.BtnFoldBrowser, host);
        var reload = Bounds((FrameworkElement)page.FindName("BtnReloadBrowser"), host);
        var status = Bounds(page.TxtBrowserStatus, host);
        var card = Bounds(page.BrowserCardFrame, host);
        var col = Bounds(column, host);

        Assert.True(arrow.Width >= 120, $"the arrow is only {arrow.Width} wide");
        Assert.True(arrow.Height >= 26, $"the arrow is only {arrow.Height} tall");
        // In the header's middle: right of the reload button, left of the status text, inside the card.
        Assert.True(arrow.Left >= reload.Right, "the arrow overlaps the reload button");
        Assert.True(arrow.Right <= status.Left, "the arrow overlaps the status text");
        Assert.True(arrow.Top >= card.Top && arrow.Bottom <= card.Bottom);
        Assert.Equal(BrowserFoldRule.ChevronShut, page.TxtFoldBrowser.Text);
        Assert.Equal("Segoe MDL2 Assets, Segoe UI Symbol, Segoe UI", page.TxtFoldBrowser.FontFamily.Source);
        // Shut, the arrow is the loudest thing on the card: it wears a glow in Home's hue.
        var glow = Assert.IsType<DropShadowEffect>(page.BtnFoldBrowser.Effect);
        Assert.Equal(ConditioningControlPanel.Controls.NavRail.NavStripRules.Lilac, glow.Color);
        Assert.Equal(Color.FromArgb(0x3D, 0xB7, 0x9C, 0xFF), ((SolidColorBrush)page.BtnFoldBrowser.Background).Color);
        Assert.Equal(Color.FromArgb(0x66, 0xB7, 0x9C, 0xFF), ((SolidColorBrush)page.BtnFoldBrowser.Tag).Color);
        MaybeRender(host, col, "browser-folded.png");
        host.Children.Clear();

        // ---- open ----
        Fold(page, collapsed: false);
        host = Realize(page);
        Assert.Equal(BrowserFoldRule.ChevronOpen, page.TxtFoldBrowser.Text);
        Assert.Null(page.BtnFoldBrowser.Effect);
        Assert.True(Bounds(page.BtnFoldBrowser, host).Width >= 120);
        MaybeRender(host, Bounds(column, host), "browser-open.png");
    });
}
