using System;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ConditioningControlPanel.Controls;
using ConditioningControlPanel.Views.Tabs;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The raffle card's counts row in a longer language (Discord, v7.0.1, German UI): "Gezählte Tage
/// 0/25" and "00:00 von 31:00:00 hinzugefügt" shared one Grid cell, one left and one right, and
/// ran into each other in the 360 px card. The row is a SplitRowPanel now: side by side while
/// both fit, the total on its own line when they do not, never drawn over the days. Set
/// CCP_SHOTS_DIR to get PNGs of the card in English and German.
/// </summary>
[Collection(CompanionWpfRenderCollection.Name)]
public class ChasterRaffleCardLayoutTests
{
    static ChasterRaffleCardLayoutTests() => ChasterTrailerView.BrowserEnabled = false;

    private static string Lang(string lang, string key)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        Assert.True(dir != null, "could not locate the repo root from " + AppContext.BaseDirectory);
        var path = Path.Combine(SourceRoots.LanguagesDirectory, lang + ".json");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        return doc.RootElement.GetProperty(key).GetString()!;
    }

    private static Rect Box(FrameworkElement e, Visual root) =>
        new(e.TranslatePoint(new Point(0, 0), (UIElement)root), new Size(e.ActualWidth, e.ActualHeight));

    [Theory]
    [InlineData("de", 360.0)]
    [InlineData("de", 280.0)]
    [InlineData("en", 360.0)]
    public void The_days_and_the_total_never_draw_over_each_other(string lang, double cardWidth)
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var tab = new ChasterTabView();
            var card = (Border)tab.LadderPopup.Child;
            tab.LadderPopup.Child = null;
            card.Width = cardWidth;
            tab.LadderRows.Visibility = Visibility.Visible;
            tab.TxtRaffleDay.Text = string.Format(Lang(lang, "chaster_raffle_day"), 4, 31);
            tab.TxtRaffleDays.Text = string.Format(Lang(lang, "chaster_raffle_days"), 0, 25);
            tab.TxtRaffleTotal.Text = string.Format(Lang(lang, "chaster_raffle_total"), "00:00", "31:00:00");
            tab.TxtRaffleStatus.Text = string.Format(Lang(lang, "chaster_raffle_need_days"), 25);

            var host = new Grid { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
            host.Children.Add(card);
            host.Measure(new Size(1000, 1000));
            host.Arrange(new Rect(0, 0, 1000, 1000));
            host.UpdateLayout();

            var days = Box(tab.TxtRaffleDays, host);
            var total = Box(tab.TxtRaffleTotal, host);
            Assert.True(days.Width > 0 && total.Width > 0);
            var both = Rect.Intersect(days, total);
            Assert.True(both.IsEmpty || both.Width < 0.5 || both.Height < 0.5,
                $"{lang} @ {cardWidth}: days {days} overlaps total {total}");
            // both stay inside the card
            var row = Box(tab.RaffleCountsRow, host);
            Assert.True(total.Right <= row.Right + 0.5 && days.Right <= row.Right + 0.5, $"{lang}: text runs out of the row {row}");
            // and the status chip below sits under both
            var chip = Box(tab.RaffleStatusChip, host);
            Assert.True(chip.Top >= Math.Max(days.Bottom, total.Bottom), $"{lang}: the status chip {chip} sits on the counts row");

            if (lang == "en")
                Assert.False(tab.RaffleCountsRow.IsStacked, "English fits on one line and keeps its look");
            if (lang == "de")
                Assert.True(tab.RaffleCountsRow.IsStacked, "German does not fit on one line in the card");

            Shot(card, $"raffle-card-{lang}-{cardWidth:0}.png");
        });
    }

    private static void Shot(FrameworkElement element, string name)
    {
        var dir = Environment.GetEnvironmentVariable("CCP_SHOTS_DIR");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        var w = (int)Math.Ceiling(element.ActualWidth) + 40;
        var h = (int)Math.Ceiling(element.ActualHeight) + 40;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x1A, 0x12, 0x30)), null, new Rect(0, 0, w, h));
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
}
