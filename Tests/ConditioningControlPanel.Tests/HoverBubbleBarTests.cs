using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ConditioningControlPanel.Controls;
using ConditioningControlPanel.Views.Tabs;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Nav polish wave 4, BUBBLES lane: the five Home pills (Webcam &amp; Mic, System, Scheduler +
/// Intensity Ramp, CCP Catalogue, App Info &amp; Data) became round hover bubbles at the bottom
/// right, under the account strip (owner, 2026-10-06). Realizes the REAL SettingsTabView at the
/// page width and holds: five 34 px bubbles right-aligned under the right column, 8 px apart,
/// never over the account strip; one opens at a time and its label is fully readable; the row
/// costs the mosaic nothing. Set <c>CCP_NAV_PNG_DIR</c> to write bubbles-rest.png and
/// bubbles-open.png.
/// </summary>
[Collection(CompanionWpfRenderCollection.Name)]
public class HoverBubbleBarTests
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

    private static void MaybeRender(FrameworkElement host, Rect crop, string name)
    {
        var dir = Environment.GetEnvironmentVariable("CCP_NAV_PNG_DIR");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        var full = new RenderTargetBitmap((int)Math.Ceiling(host.ActualWidth), (int)Math.Ceiling(host.ActualHeight),
            96, 96, PixelFormats.Pbgra32);
        full.Render(host);
        var r = Rect.Intersect(crop, new Rect(0, 0, full.PixelWidth, full.PixelHeight));
        var cropped = new CroppedBitmap(full, new Int32Rect((int)r.X, (int)r.Y, (int)r.Width, (int)r.Height));
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(cropped));
        using var fs = File.Create(Path.Combine(dir, name));
        enc.Save(fs);
    }

    private static Rect Bounds(FrameworkElement e, Visual root) =>
        e.TransformToAncestor(root).TransformBounds(new Rect(0, 0, e.ActualWidth, e.ActualHeight));

    [Fact]
    public void Five_bubbles_sit_bottom_right_and_one_opens_with_a_readable_label() => WpfRenderHarness.OnStaThread(() =>
    {
        var page = new SettingsTabView();
        var bar = page.HomeBubbleBar;
        Assert.Equal(new[] { page.VelvetBtnWebcam, page.VelvetBtnSystem, page.VelvetBtnSchedulerRamp,
                             page.VelvetBtnCatalogue, page.VelvetBtnAppInfo }, bar.Bubbles.ToArray());

        var host = Realize(page);
        var row = Bounds(page.VelvetHelperButtonRow, host);
        Assert.InRange(page.VelvetHelperButtonRow.ActualHeight, 30, 40);

        var rects = bar.Bubbles.Select(b => Bounds(b, host)).ToArray();
        foreach (var r in rects)
        {
            Assert.Equal(HoverBubbleBar.BubbleSize, r.Width, 1);
            Assert.Equal(HoverBubbleBar.BubbleSize, r.Height, 1);
        }
        for (int i = 1; i < rects.Length; i++)
            Assert.Equal(HoverBubbleBar.Gap, rects[i].Left - rects[i - 1].Right, 1);

        // Right-aligned under the right column (the account strip's right edge), never under the
        // favourites drawer handle, and below the right column: nothing overlaps the strip.
        var rightColumn = Bounds(page.VelvetFeatureGrid, host);
        Assert.True(rects[^1].Right <= PageWidth - 22, $"last bubble ends at {rects[^1].Right}, under the drawer");
        Assert.True(rects[0].Left > rightColumn.Right, "bubbles reach left over the mosaic column");
        Assert.True(Bounds(page.VelvetBtnWebcam, host).Left > rightColumn.Right, "bubbles reach left over the mosaic column");
        Assert.True(rects.All(r => r.Top >= row.Top - 0.5), "a bubble pokes above its row");

        Assert.Equal("Webcam & Mic", bar.LabelOf(page.VelvetBtnWebcam));
        Assert.Equal("System", bar.LabelOf(page.VelvetBtnSystem));
        Assert.Equal("Scheduler + Intensity Ramp", bar.LabelOf(page.VelvetBtnSchedulerRamp));
        Assert.Equal("CCP Catalogue", bar.LabelOf(page.VelvetBtnCatalogue));
        Assert.Equal("App Info & Data", bar.LabelOf(page.VelvetBtnAppInfo));
        Assert.Equal("System", AutomationProperties.GetName(page.VelvetBtnSystem));
        Assert.Equal("Opens app.cclabs.app/catalogue in your browser", page.VelvetBtnCatalogue.ToolTip);

        var crop = new Rect(row.Right - 560, row.Top - 60, 560, row.Height + 70);
        MaybeRender(host, crop, "bubbles-rest.png");

        // Open the widest one (forcing the hover state), then another: only one stays open.
        bar.Expand(page.VelvetBtnAppInfo, animate: false);
        bar.Expand(page.VelvetBtnSchedulerRamp, animate: false);
        host.UpdateLayout();
        Assert.True(bar.IsExpanded(page.VelvetBtnSchedulerRamp));
        Assert.False(bar.IsExpanded(page.VelvetBtnAppInfo));
        var open = Bounds(page.VelvetBtnSchedulerRamp, host);
        var label = (TextBlock)FindLabel(page.VelvetBtnSchedulerRamp);
        label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Assert.True(open.Width >= HoverBubbleBar.BubbleSize + label.DesiredSize.Width - 1,
            $"open bubble {open.Width} does not fit its label {label.DesiredSize.Width}");
        // The glyph end stays put (bar is right-aligned): bubbles to its right do not move.
        Assert.Equal(rects[^1].Right, Bounds(page.VelvetBtnAppInfo, host).Right, 1);
        MaybeRender(host, crop, "bubbles-open.png");

        bar.Collapse(page.VelvetBtnSchedulerRamp, animate: false);
        host.UpdateLayout();
        Assert.False(bar.IsExpanded(page.VelvetBtnSchedulerRamp));
        Assert.Equal(HoverBubbleBar.BubbleSize, page.VelvetBtnSchedulerRamp.ActualWidth, 1);
    });

    private static FrameworkElement FindLabel(Button b)
    {
        var plate = (Border)b.Content;
        var row = (StackPanel)plate.Child;
        return (FrameworkElement)((Border)row.Children[0]).Child;
    }

    [Theory]
    [InlineData("⚙ System", "System")]
    [InlineData("📅 Scheduler", "Scheduler")]
    [InlineData("App Info & Data", "App Info & Data")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void Leading_emoji_leave_the_label(string? raw, string expected) =>
        Assert.Equal(expected, HoverBubbleBar.StripLeadingGlyph(raw));
}
