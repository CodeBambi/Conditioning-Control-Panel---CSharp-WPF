using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ConditioningControlPanel.Services.Fyp.Online;
using ConditioningControlPanel.Views.Tabs;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Nav polish wave 2, NICHES lane: Library › Assets "Where your media comes from" lays out in
/// full (no 200 px scroll cap, no ▸ disclosure lines), with flavour tiles, tinted niche pills
/// carrying count badges, an always-visible r/ add row and a summary line. The pills and tiles
/// are the REAL ones (MainWindow's static builders). Set <c>CCP_NAV_PNG_DIR</c> to also write
/// an offscreen render of the page in "Both" with two niches on and one custom sub.
/// </summary>
[Collection(CompanionWpfRenderCollection.Name)]
public class AssetsMediaBlockRenderTests
{
    private static Grid Realize(FrameworkElement element, double width, double height)
    {
        var host = new Grid { Background = new SolidColorBrush(Color.FromRgb(0x12, 0x10, 0x1F)) };
        host.Children.Add(element);
        host.Measure(new Size(width, height));
        host.Arrange(new Rect(0, 0, width, height));
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

    [Fact]
    public void TheMediaBlockLaysOutInFullWithFlavoursPillsAndASummary() => WpfRenderHarness.OnStaThread(() =>
    {
        var page = new AssetsTabView();
        Assert.Null(page.FindName("RemoteMediaScroll"));        // the 200 px cap is gone
        Assert.Null(page.FindName("RemoteNicheSubs"));          // so are the disclosure lines
        Assert.Null(page.FindName("RemoteCustomSubsBody"));     // the custom subs never fold

        // Source chips the way MainWindow builds them, "Both" picked.
        var chipStyle = page.TryFindResource("AchievementFilterChip") as Style;
        foreach (var (label, picked) in new[] { ("My own assets", false), ("Reddit", false), ("Both", true) })
            page.RemoteSourceChips.Children.Add(new ToggleButton { Style = chipStyle, Content = label, IsChecked = picked });
        page.RemoteRatioRow.Visibility = Visibility.Visible;
        page.RemoteMediaDetails.Visibility = Visibility.Visible;

        var tileStyle = page.RemoteFlavourTiles.TryFindResource("RemoteFlavourTile") as Style;
        Assert.NotNull(tileStyle);
        var tiles = FlavourPresets.All.Append(FlavourPresets.Mine)
            .Select(f => MainWindow.CreateRemoteFlavourTile(f, tileStyle)).ToList();
        tiles.ForEach(t => page.RemoteFlavourTiles.Children.Add(t));

        var pillStyle = page.RemoteNicheChips.TryFindResource("RemoteNichePill") as Style;
        Assert.NotNull(pillStyle);
        var on = new[] { "hypno", "bimbo" };
        foreach (var niche in FypOnlineCoordinator.Catalog)
        {
            var chip = MainWindow.CreateRemoteNicheChip(niche, pillStyle);
            chip.IsChecked = on.Contains(niche.Id);
            page.RemoteNicheChips.Children.Add(chip);
        }

        var customs = new[] { "Bimbos" };
        page.RemoteCustomSubChips.Children.Add(new Border
        {
            Style = page.TryFindResource("RemoteChipBorder") as Style,
            BorderBrush = page.TryFindResource("RemoteSubVerifiedBrush") as Brush,
            Child = new TextBlock { Text = "r/Bimbos", Foreground = Brushes.White, FontSize = 11, FontWeight = FontWeights.SemiBold }
        });
        page.TxtRemoteSubError.Text = "r/Bimbos is already in your pool.";
        page.TxtRemoteSubError.Visibility = Visibility.Visible;

        MainWindow.PaintRemoteFlavourTiles(tiles, on, customs);
        page.TxtRemoteSummary.Text = MainWindow.RemoteSummaryText(on, customs);

        var host = Realize(page, 1469, 891);
        MaybeRender(host, "niches-assets-both.png");

        // 7 + 3 subs from Hypno and Bimbo plus r/Bimbos = 8 distinct.
        Assert.Contains("2", page.TxtRemoteSummary.Text);
        Assert.Contains("8", page.TxtRemoteSummary.Text);

        // Mine is lit (hypno + bimbo + r/Bimbos is nobody's preset): its border is solid.
        var mine = tiles.Single(t => (string)t.Tag == FlavourPresets.MineId);
        Assert.Equal(2, mine.BorderThickness.Left);
        Assert.All(tiles.Where(t => t != mine), t => Assert.Equal(1, t.BorderThickness.Left));

        // Pills are 32 px at least and keep their label on one line.
        foreach (ToggleButton chip in page.RemoteNicheChips.Children)
        {
            Assert.True(chip.ActualHeight >= 32, $"{chip.Tag} is {chip.ActualHeight} px tall");
            Assert.True(chip.ActualHeight < 44, $"{chip.Tag} wrapped to {chip.ActualHeight} px");
        }

        // The whole block must leave the asset browser its 260 px floor on the 891 px canvas:
        // header row (about 40) + this block + 260 must fit.
        var block = (FrameworkElement)VisualTreeHelper.GetParent(VisualTreeHelper.GetParent(page.RemoteSourceChips));
        while (block is not Border) block = (FrameworkElement)VisualTreeHelper.GetParent(block);
        Assert.True(block.ActualHeight + 40 + 260 <= 891 - 10,
            $"media block is {block.ActualHeight:0} px, too tall for the browser floor");
    });

    [Fact]
    public void EveryNicheWearsATintAndTheFlavourOwnedOnesWearTheirFlavour()
    {
        Assert.Equal(Color.FromRgb(0xb9, 0x9c, 0xff), MainWindow.RemoteNicheTintOf("hypno"));    // Trance
        Assert.Equal(Color.FromRgb(0xff, 0x87, 0xc7), MainWindow.RemoteNicheTintOf("bimbo"));    // Pink
        Assert.Equal(Color.FromRgb(0xff, 0xb3, 0xd9), MainWindow.RemoteNicheTintOf("sissy"));    // Frills
        Assert.Equal(Color.FromRgb(0xFF, 0x69, 0xB4), MainWindow.RemoteNicheTintOf("hentai"));   // nobody's: Pink
    }
}
