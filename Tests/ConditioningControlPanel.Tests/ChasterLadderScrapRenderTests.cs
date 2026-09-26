using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ConditioningControlPanel.Controls;
using ConditioningControlPanel.Services.Chaster;
using ConditioningControlPanel.Views.Tabs;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The month's top ten on its pinned scrap beside the heads-up clock (owner, 2026-09-26): the demo
/// board realizes as ten rows plus a gap and the player's own row, names upright and labels in
/// italics, the own row lit; no board is one quiet line; and the raffle card still paints. Set
/// CCP_SHOTS_DIR to a folder to also get PNGs of the clock row and the raffle card.
/// </summary>
[Collection(CompanionWpfRenderCollection.Name)]
public class ChasterLadderScrapRenderTests
{
    static ChasterLadderScrapRenderTests() => ChasterTrailerView.BrowserEnabled = false;

    private static void Realize(FrameworkElement element, double width, double height)
    {
        var host = new Grid { Width = width, Height = height };
        host.Children.Add(element);
        host.Measure(new Size(width, height));
        host.Arrange(new Rect(new Point(0, 0), new Size(width, height)));
        host.UpdateLayout();
    }

    private static void Shot(FrameworkElement element, string name, Brush? ground = null)
    {
        var dir = Environment.GetEnvironmentVariable("CCP_SHOTS_DIR");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        var w = (int)Math.Ceiling(element.ActualWidth) + 40;
        var h = (int)Math.Ceiling(element.ActualHeight) + 40;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(ground ?? new SolidColorBrush(Color.FromRgb(0x1A, 0x12, 0x30)), null, new Rect(0, 0, w, h));
            dc.DrawRectangle(new VisualBrush(element) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top },
                null, new Rect(20, 20, element.ActualWidth, element.ActualHeight));
        }
        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        using var fs = File.Create(Path.Combine(dir, name));
        enc.Save(fs);
    }

    private static TextBlock[] Texts(FrameworkElement row) =>
        ((Grid)((Border)row).Child).Children.OfType<TextBlock>().ToArray();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_demo_board_hangs_ten_rows_a_gap_and_my_own_row_lit(bool showName)
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var tab = new ChasterTabView();
            tab.LinkedPanel.Visibility = Visibility.Visible;
            tab.PaintScrap(ChasterService.DemoRaffle.Board(showName));
            Realize(tab, 1100, 1600);

            Assert.Equal(Visibility.Visible, tab.LadderScrap.Visibility);
            Assert.Equal(Visibility.Collapsed, tab.TxtLadderScrapState.Visibility);
            var kids = tab.LadderScrapRows.Children.OfType<FrameworkElement>().ToList();
            Assert.Equal(12, kids.Count); // ten rows, the gap, my row
            Assert.IsType<TextBlock>(kids[10]);
            var mine = (Border)kids[11];
            Assert.NotEqual(Brushes.Transparent, mine.Background);
            Assert.Equal("23", Texts(mine)[0].Text);
            Assert.Equal(showName ? "You" : "Locked 3E0B21", Texts(mine)[1].Text);
            Assert.Equal(showName ? FontStyles.Normal : FontStyles.Italic, Texts(mine)[1].FontStyle);
            // a label is italic, a name is not
            var rows = kids.Take(10).Cast<Border>().ToList();
            Assert.Equal(FontStyles.Normal, Texts(rows[0])[1].FontStyle);
            Assert.Equal(FontStyles.Italic, Texts(rows[1])[1].FontStyle);
            Assert.All(rows, r => Assert.Equal(Brushes.Transparent, r.Background));
            // it sits beside the clock card, never on it
            Assert.True(tab.LadderScrap.ActualWidth > 0 && tab.AddedCard.ActualWidth > 0);
            var scrapLeft = tab.LadderScrap.TranslatePoint(new Point(0, 0), tab.AddedRow).X;
            Assert.True(scrapLeft >= tab.AddedCard.ActualWidth, $"the scrap starts at {scrapLeft}, the clock card ends at {tab.AddedCard.ActualWidth}");

            Shot(tab.AddedRow, showName ? "scrap-named.png" : "scrap-label.png");
            // The raffle card as the clock's hover lays it out: under the clock, beside the scrap.
            tab.ShowRaffleCard(new ChasterService.DemoRaffle("in").MeAsync().Result);
            var clockBottom = tab.AddedClock.TranslatePoint(new Point(0, tab.AddedClock.ActualHeight), tab.AddedRow).Y;
            var clockLeft = tab.AddedClock.TranslatePoint(new Point(0, 0), tab.AddedRow).X;
            if (tab.LadderPopup.Child is FrameworkElement raffle)
            {
                tab.LadderPopup.Child = null;
                ((Panel)tab.AddedRow.Parent).Children.Remove(tab.AddedRow);
                tab.AddedRow.Width = 900;
                var host = new Grid { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
                host.Children.Add(tab.AddedRow);
                raffle.HorizontalAlignment = HorizontalAlignment.Left;
                raffle.VerticalAlignment = VerticalAlignment.Top;
                raffle.Margin = new Thickness(clockLeft, clockBottom + 8, 0, 0);
                host.Children.Add(raffle);
                Realize(host, 1000, 900);
                Assert.True(raffle.ActualHeight > 0);
                var raffleRight = raffle.TranslatePoint(new Point(raffle.ActualWidth, 0), host).X;
                var scrapLeftNow = tab.LadderScrap.TranslatePoint(new Point(0, 0), host).X;
                Assert.True(raffleRight < scrapLeftNow, $"the raffle card ({raffleRight}) runs under the scrap ({scrapLeftNow})");
                Shot(host, showName ? "scrap-and-raffle-named.png" : "scrap-and-raffle-label.png");
            }
        });
    }

    [Fact]
    public void No_board_is_one_quiet_line_and_never_an_error()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var tab = new ChasterTabView();
            tab.LinkedPanel.Visibility = Visibility.Visible;
            tab.PaintScrap(null);
            Realize(tab, 1100, 1400);

            Assert.Empty(tab.LadderScrapRows.Children);
            Assert.Equal(Visibility.Visible, tab.TxtLadderScrapState.Visibility);
            Assert.False(string.IsNullOrWhiteSpace(tab.TxtLadderScrapState.Text));

            tab.PaintScrap(new LadderBoard("2026-10", Array.Empty<LadderRow>(), null, false));
            Assert.Empty(tab.LadderScrapRows.Children);
            Assert.Equal(Visibility.Visible, tab.TxtLadderScrapState.Visibility);
            Shot(tab.AddedRow, "scrap-empty.png");
        });
    }
}
