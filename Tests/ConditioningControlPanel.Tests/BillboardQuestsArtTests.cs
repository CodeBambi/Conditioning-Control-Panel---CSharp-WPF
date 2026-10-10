using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ConditioningControlPanel.Controls.Billboard;
using ConditioningControlPanel.Services.Billboard;
using ConditioningControlPanel.Services.Billboard.Providers;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The "N quests left today" card's own art: the provider names it, it is registered, the slip
/// count keeps what is LEFT true, the stamp only ever lands on a checked slip, and the view paints
/// a whole loop for every total 1..5 and done 0..total. With CCP_QUESTS_ART_SHOTS set to a folder
/// it writes PNG strips there for a look check.
/// </summary>
[Collection(CompanionWpfRenderCollection.Name)]
public class BillboardQuestsArtTests
{
    private static string Loc(string key) => key;

    private static Dictionary<string, int> D(int done, int total) => new() { ["done"] = done, ["total"] = total };

    [Fact]
    public void Quests_card_uses_its_own_art_and_keeps_the_data()
    {
        var card = WaitingCards.Quests(4, 1, Loc)!;
        Assert.Equal(CardArt.Quests, card.ArtKey);
        Assert.Equal(BuiltInArtKeys.Quests, card.ArtKey);
        var data = Assert.IsAssignableFrom<IReadOnlyDictionary<string, int>>(card.ArtData);
        Assert.Equal(1, data["done"]);
        Assert.Equal(4, data["total"]);
        Assert.Contains(CardArt.Quests, CardArt.All);
        Assert.Contains(BuiltInArtKeys.Quests, BuiltInArtKeys.All);
    }

    [Fact]
    public void Quests_key_is_registered_and_makes_the_view() => WpfRenderHarness.OnStaThread(() =>
    {
        BuiltInArt.Register();
        Assert.True(BillboardArt.IsRegistered(BuiltInArtKeys.Quests));
        var view = Assert.IsType<QuestsArtView>(BillboardArt.Create(BuiltInArtKeys.Quests, D(2, 3)));
        Assert.Equal(3, view.Shown);
        Assert.Equal(2, view.DoneShown);
        view.Release();
    });

    [Theory]
    [InlineData(0, 3, 3, 0)]
    [InlineData(2, 3, 3, 2)]
    [InlineData(5, 5, 5, 5)]
    [InlineData(9, 3, 3, 3)]
    [InlineData(-1, 2, 2, 0)]
    [InlineData(5, 7, 5, 3)] // two left of seven: three checked, two open
    [InlineData(1, 9, 5, 0)] // eight left: none checked, never claims progress
    [InlineData(0, 0, 1, 0)]
    public void Fit_keeps_the_open_count(int done, int total, int shown, int doneShown)
    {
        Assert.Equal((shown, doneShown), QuestsArtView.Fit(done, total));
    }

    [Fact]
    public void Data_reads_the_payload_and_falls_back()
    {
        Assert.Equal((2, 4), QuestsArtView.Data(D(2, 4)));
        Assert.Equal((4, 4), QuestsArtView.Data(D(9, 4)));
        Assert.Equal((0, 3), QuestsArtView.Data(null));
    }

    [Fact]
    public void The_stamp_only_lands_on_a_checked_slip()
    {
        Assert.Equal(-1, QuestsArtView.StampTarget(5, 0));
        Assert.Equal(-1, QuestsArtView.StampTarget(QuestsArtView.LoopStart - 0.01, 3));
        for (int done = 1; done <= QuestsArtView.MaxSlips; done++)
        {
            var seen = new HashSet<int>();
            for (double t = QuestsArtView.LoopStart; t < 60; t += 0.3)
            {
                int target = QuestsArtView.StampTarget(t, done);
                Assert.InRange(target, 0, done - 1);
                seen.Add(target);
            }
            Assert.Equal(done, seen.Count); // every checked slip gets its turn
        }
    }

    [Fact]
    public void Layout_fits_the_board_for_every_count()
    {
        for (int n = 1; n <= QuestsArtView.MaxSlips; n++)
        {
            double bh = QuestsArtView.BoardHeight(340, n);
            var rects = QuestsArtView.Layout(200, bh, n, 340);
            Assert.Equal(n, rects.Length);
            Assert.True(bh <= 340 * 0.9);
            for (int i = 0; i < n; i++)
            {
                Assert.True(rects[i].Top >= -bh / 2 && rects[i].Bottom <= bh / 2);
                if (i > 0) Assert.True(rects[i].Top > rects[i - 1].Bottom);
            }
        }
    }

    private static Grid Realize(FrameworkElement element, double w, double h)
    {
        var host = new Grid { Background = new SolidColorBrush(Color.FromRgb(0x12, 0x10, 0x1F)) };
        host.Children.Add(element);
        host.Measure(new Size(w, h));
        host.Arrange(new Rect(0, 0, w, h));
        host.UpdateLayout();
        return host;
    }

    [Fact]
    public void Paints_a_whole_loop_for_every_count() => WpfRenderHarness.OnStaThread(() =>
    {
        for (int total = 1; total <= QuestsArtView.MaxSlips; total++)
        {
            for (int done = 0; done <= total; done++)
            {
                var art = new QuestsArtView((done, total)) { Accent = Color.FromRgb(0xff, 0xc9, 0x4a) };
                var host = Realize(art, 600, 340);
                for (double t = 0; t <= 8; t += 0.19)
                {
                    art.SecondsForTests = t;
                    art.Redraw();
                }
                host.Children.Clear();
                art.Release();
            }
        }
    });

    [Fact]
    public void Shots_when_asked() => WpfRenderHarness.OnStaThread(() =>
    {
        var dir = Environment.GetEnvironmentVariable("CCP_QUESTS_ART_SHOTS");
        if (string.IsNullOrWhiteSpace(dir)) return;
        System.IO.Directory.CreateDirectory(dir);
        // 0.6 entrance, 1.6 still, then a stamp loop: descend, wind-up, contact, squash, lift, poke, nudge.
        var times = new[] { 0.6, 1.6, 2.4, 2.95, 3.05, 3.15, 3.5, 4.2, 4.8, 5.0 };
        var cases = new[] { (1, 3), (0, 3), (2, 5), (4, 5), (0, 1), (1, 2) };
        foreach (var (done, total) in cases)
        {
            foreach (var (w, h) in new[] { (600, 340), (1000, 420) })
            {
                var art = new QuestsArtView((done, total)) { Accent = Color.FromRgb(0xff, 0xc9, 0x4a) };
                var host = Realize(art, w, h);
                int cols = 5, rows = (times.Length + cols - 1) / cols;
                var strip = new DrawingVisual();
                using (var dc = strip.RenderOpen())
                {
                    for (int i = 0; i < times.Length; i++)
                    {
                        art.SecondsForTests = times[i];
                        art.Redraw();
                        host.UpdateLayout();
                        var bmp = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
                        bmp.Render(host);
                        bmp.Freeze();
                        dc.DrawImage(bmp, new Rect((i % cols) * w, (i / cols) * h, w, h));
                    }
                }
                var all = new RenderTargetBitmap(w * cols, h * rows, 96, 96, PixelFormats.Pbgra32);
                all.Render(strip);
                var enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(all));
                using var f = System.IO.File.Create(System.IO.Path.Combine(dir, $"quests-{done}of{total}-{w}x{h}.png"));
                enc.Save(f);
                art.Release();
            }
        }
    });
}
