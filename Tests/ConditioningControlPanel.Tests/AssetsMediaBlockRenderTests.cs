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
/// Nav polish wave 3, PICKER lane: Library › Assets "Where your media comes from" is calm.
/// Closed it is three things (a segmented source bar, six flavour cards, one Fine-tune row)
/// and fits under 200 px; open, the catalog niches and your own communities are list rows
/// with switches in two clean columns. The cards, rows and switches are the REAL ones
/// (MainWindow's static builders). Set <c>CCP_NAV_PNG_DIR</c> to also write offscreen renders
/// of the block in "Both" with Pink chosen (closed) and with two niches + one own community (open).
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

    private static void MaybeRender(FrameworkElement el, string name)
    {
        var dir = Environment.GetEnvironmentVariable("CCP_NAV_PNG_DIR");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        var w = (int)Math.Ceiling(el.ActualWidth);
        var h = (int)Math.Ceiling(el.ActualHeight);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x12, 0x10, 0x1F)), null, new Rect(0, 0, w, h));
            dc.DrawRectangle(new VisualBrush(el), null, new Rect(0, 0, w, h));
        }
        var bmp = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(visual);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var fs = File.Create(Path.Combine(dir, name));
        enc.Save(fs);
    }

    /// <summary>Builds the block the way MainWindow does, in Both, with this selection.</summary>
    private static (AssetsTabView page, System.Collections.Generic.List<Button> tiles,
        System.Collections.Generic.List<ToggleButton> rows) Build(string[] niches, string[] customs)
    {
        var page = new AssetsTabView();
        Assert.Null(page.FindName("RemoteMediaScroll"));        // no scroll cap
        Assert.Null(page.FindName("RemoteNicheSubs"));          // no disclosure lines per niche

        var segStyle = page.RemoteSourceChips.TryFindResource("RemoteSourceSegment") as Style;
        Assert.NotNull(segStyle);
        foreach (var (label, picked) in new[] { ("My own assets", false), ("Scrolller", false), ("Both", true) })
            page.RemoteSourceChips.Children.Add(new ToggleButton { Style = segStyle, Content = label, IsChecked = picked });
        page.RemoteRatioRow.Visibility = Visibility.Visible;
        page.RemoteMediaDetails.Visibility = Visibility.Visible;

        var tileStyle = page.RemoteFlavourTiles.TryFindResource("RemoteFlavourTile") as Style;
        Assert.NotNull(tileStyle);
        var tiles = FlavourPresets.All.Append(FlavourPresets.Mine)
            .Select(f => MainWindow.CreateRemoteFlavourTile(f, tileStyle)).ToList();
        tiles.ForEach(t => page.RemoteFlavourTiles.Children.Add(t));

        var rowStyle = page.RemoteNicheChips.TryFindResource("RemoteNichePill") as Style;
        Assert.NotNull(rowStyle);
        var rows = FypOnlineCoordinator.Catalog.Select(n =>
        {
            var chip = MainWindow.CreateRemoteNicheChip(n, rowStyle);
            chip.IsChecked = niches.Contains(n.Id);
            page.RemoteNicheChips.Children.Add(chip);
            return chip;
        }).ToList();

        foreach (var c in customs)
            page.RemoteCustomSubChips.Children.Add(MainWindow.BuildRemoteSubRow(page, "r/" + c,
                "Verified · 412 clips on Reddit", on: true, verified: true, onToggle: () => { }, onRemove: () => { }));
        page.RemoteCustomSubChips.Children.Add(MainWindow.BuildRemoteSubRow(page, "r/GoonCaves",
            null, on: false, verified: false, onToggle: () => { }, onRemove: () => { }));

        MainWindow.PaintRemoteFlavourTiles(tiles, niches, customs);
        page.TxtRemoteSummary.Text = MainWindow.RemoteSummaryText(niches, customs);
        return (page, tiles, rows);
    }

    [Fact]
    public void ClosedTheBlockIsSourceFlavourAndOneLine() => WpfRenderHarness.OnStaThread(() =>
    {
        var pink = FlavourPresets.All.Single(f => f.Id == "pink");
        var sel = FlavourPresets.Resolve(pink, FypOnlineCoordinator.Catalog);
        var (page, tiles, _) = Build(sel.NicheIds.ToArray(), sel.CustomSubs.ToArray());
        Assert.Equal(Visibility.Collapsed, page.RemoteFineTuneScroll.Visibility);   // closed by default

        Realize(page, 1469, 891);
        MaybeRender(page.RemoteMediaBlock, "picker-closed.png");

        // Pink is lit: tint border 1.5 px and its check showing; every other card is neutral.
        var lit = tiles.Single(t => (string)t.Tag == "pink");
        Assert.Equal(1.5, lit.BorderThickness.Left);
        Assert.All(tiles.Where(t => t != lit), t => Assert.Equal(1, t.BorderThickness.Left));
        Assert.Contains(((Grid)lit.Content).Children.OfType<TextBlock>(),
            t => (string)t.Tag == "check" && t.Visibility == Visibility.Visible);

        // Six cards, one row, equal width, 72 px tall.
        Assert.Equal(6, page.RemoteFlavourTiles.Children.Count);
        Assert.All(tiles, t => Assert.Equal(72, t.ActualHeight));
        Assert.Single(tiles.Select(t => Math.Round(t.ActualWidth)).Distinct());

        Assert.StartsWith("Fine-tune", page.TxtRemoteSummary.Text);
        Assert.True(page.RemoteMediaBlock.ActualHeight < 200,
            $"closed media block is {page.RemoteMediaBlock.ActualHeight:0} px, owner wants one glance");
    });

    [Fact]
    public void OpenTheRowsSitInTwoCleanColumns() => WpfRenderHarness.OnStaThread(() =>
    {
        var on = new[] { "hypno", "bimbo" };
        var customs = new[] { "Bimbos" };
        var (page, tiles, rows) = Build(on, customs);
        page.RemoteFineTuneScroll.Visibility = Visibility.Visible;
        page.TxtRemoteSubError.Text = "r/Bimbos is already in your pool.";
        page.TxtRemoteSubError.Visibility = Visibility.Visible;

        Realize(page, 1469, 1400);
        MaybeRender(page.RemoteMediaBlock, "picker-open.png");

        // 7 + 3 subs from Hypno and Bimbo plus r/Bimbos = 8 distinct.
        Assert.Contains("2 niches", page.TxtRemoteSummary.Text);
        Assert.Contains("8 communities", page.TxtRemoteSummary.Text);

        // Mine is lit (hypno + bimbo + r/Bimbos is nobody's preset).
        var mine = tiles.Single(t => (string)t.Tag == FlavourPresets.MineId);
        Assert.Equal(1.5, mine.BorderThickness.Left);

        // Rows: two x positions only, every row the same width, none wrapped past 60 px.
        var xs = rows.Select(r => Math.Round(r.TranslatePoint(new Point(0, 0), page.RemoteNicheChips).X)).Distinct().ToList();
        Assert.Equal(2, xs.Count);
        Assert.Single(rows.Select(r => Math.Round(r.ActualWidth)).Distinct());
        Assert.All(rows, r => Assert.InRange(r.ActualHeight, 44, 60));

        // A switch is on in the niche's tint when its row is on, grey when off.
        var hypno = rows.Single(r => (string)r.Tag == "hypno");
        var track = (Border)((Grid)hypno.Content).Children[0];
        Assert.Equal(Color.FromRgb(0xFF, 0x69, 0xB4), ((SolidColorBrush)track.Background).Color);   // on = Pink, never a pale tint
        var offRow = rows.First(r => r.IsChecked != true);
        var offTrack = (Border)((Grid)offRow.Content).Children[0];
        Assert.True(((SolidColorBrush)offTrack.Background).Color.A < 0x40);
    });

    [Fact]
    public void EveryNicheWearsATintAndTheFlavourOwnedOnesWearTheirFlavour()
    {
        Assert.Equal(Color.FromRgb(0xb9, 0x9c, 0xff), MainWindow.RemoteNicheTintOf("hypno"));    // Trance
        Assert.Equal(Color.FromRgb(0xff, 0x87, 0xc7), MainWindow.RemoteNicheTintOf("bimbo"));    // Pink
        Assert.Equal(Color.FromRgb(0xff, 0xb3, 0xd9), MainWindow.RemoteNicheTintOf("sissy"));    // Frills
        Assert.Equal(Color.FromRgb(0xFF, 0x69, 0xB4), MainWindow.RemoteNicheTintOf("hentai"));   // nobody's: Pink
    }

    [Fact]
    public void CommunitiesAndNichesKnowTheirSingular()
    {
        Assert.Equal("1 community", MainWindow.Plural("{0} community|{0} communities", 1));
        Assert.Equal("3 communities", MainWindow.Plural("{0} community|{0} communities", 3));
    }
}
