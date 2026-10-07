using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ConditioningControlPanel.Views.Controls.Companion;
using ConditioningControlPanel.Views.Controls.Companion.Pages;
using ConditioningControlPanel.Views.Controls.Companion.Runtime;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The three Companion pages build and lay out offscreen with their live cells hosted (fresh
/// cell instances stand in for the room's, which need a MainWindow). Set CCP_SHOTS_DIR to get
/// companion-*.png for a desk look.
/// </summary>
public class CompanionPagesRenderTests
{
    private static void Realize(FrameworkElement element, double width, double height)
    {
        element.Measure(new Size(width, height));
        element.Arrange(new Rect(new Point(0, 0), new Size(width, height)));
        element.UpdateLayout();
    }

    private static void Shot(FrameworkElement element, string name)
    {
        var dir = Environment.GetEnvironmentVariable("CCP_SHOTS_DIR");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        var w = (int)Math.Ceiling(element.ActualWidth);
        var h = (int)Math.Ceiling(element.ActualHeight);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x1A, 0x12, 0x30)), null, new Rect(0, 0, w, h));
            dc.DrawRectangle(new VisualBrush(element) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top },
                null, new Rect(0, 0, w, h));
        }
        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        using var fs = File.Create(Path.Combine(dir, name));
        enc.Save(fs);
    }

    [Fact]
    public void LinksPage_Renders() => WpfRenderHarness.OnStaThread(() =>
    {
        var page = new LinksPage();
        page.VideosHost.Content = new WorkshopLibraryCell();
        page.KnowledgeLinks.Links.Add(new Models.KnowledgeBaseLink { Title = "Example", Url = "https://example.com/a", Description = "A sample link" });
        Realize(page, 1100, 900);
        Assert.True(page.KnowledgeLinks.ActualHeight > 0);
        Shot(page, "companion-links.png");
    });

    [Fact]
    public void PermissionsPage_Renders() => WpfRenderHarness.OnStaThread(() =>
    {
        var page = new PermissionsPage();
        page.PermissionsHost.Content = new AiPermissionsGrid();
        Realize(page, 1300, 1000);
        Assert.True(page.PermissionsHost.ActualHeight > 0);
        Shot(page, "companion-permissions.png");
    });

    [Fact]
    public void PersonalityPage_Renders() => WpfRenderHarness.OnStaThread(() =>
    {
        var page = new PersonalityPage();
        page.PresetsHost.Content = new MakeHerYoursView { DataContext = new MockMakeHerYoursVm() };
        page.CommunityHost.Content = new WorkshopCommunityCell();
        Realize(page, 1150, 2400);
        Assert.True(page.CommunityHost.ActualHeight > 0);
        Shot(page, "companion-personality.png");
    });

    [Fact]
    public void AiPage_Renders_WithBehaviourOpen() => WpfRenderHarness.OnStaThread(() =>
    {
        var page = new AiPage();
        page.BehaviorHost.Content = new WorkshopBehaviorCell();
        page.TriggersHost.Content = new WorkshopTriggersCell();
        Realize(page, 1150, 2400);
        Assert.True(page.BehaviorHost.ActualHeight > 0);
        Assert.True(page.TriggersHost.ActualHeight > 0);
        Shot(page, "companion-ai.png");
    });
}
