using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ConditioningControlPanel.Controls.Billboard;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Billboard;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The Daily Daze house card: every tier gets it, its button is a Callback that opens the Back
/// Room through the provider, the scene map draws the wheel, and the wheel's story lands each
/// loop on a different prize, under the pointer. With CCP_WHEEL_SHOTS set to a folder the wheel
/// also writes a strip across a whole loop there; nothing here judges the look.
/// </summary>
[Collection(CompanionWpfRenderCollection.Name)]
public class BillboardDailyDazeTests
{
    private static readonly DateTime Now = new(2026, 10, 7, 20, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(BillboardTier.Free)]
    [InlineData(BillboardTier.Basic)]
    [InlineData(BillboardTier.Prime)]
    public void Every_tier_gets_the_daily_daze_card_with_a_callback(BillboardTier tier)
    {
        var house = new HouseProvider(k => k);
        var card = house.Current(new BillboardContext(tier, Now, Now)).Single(c => c.Id == "house.backroom");
        Assert.Equal(BillboardCardKind.House, card.Kind);
        Assert.Equal(BillboardActionKind.Callback, card.Action.Kind);
        Assert.Equal(HouseProvider.BackRoomCallback, card.Action.Target);
        Assert.Equal("billboard_deck_btn_take_seat", card.Action.Label);
        Assert.Equal("billboard_backroom_title", card.Title);
        Assert.True(DashboardBillboard.IsActionAllowed(card.Action, true));
        Assert.Equal("billboard_chip_backroom", DashboardBillboard.ChipKey(card));
    }

    [Fact]
    public void The_callback_opens_the_back_room_and_nothing_else_does()
    {
        var before = HouseProvider.OpenBackRoom;
        int opened = 0;
        try
        {
            HouseProvider.OpenBackRoom = () => opened++;
            var house = new HouseProvider(k => k);
            house.Invoke("something-else");
            Assert.Equal(0, opened);
            house.Invoke(HouseProvider.BackRoomCallback);
            Assert.Equal(1, opened);
            HouseProvider.OpenBackRoom = () => throw new InvalidOperationException("boom");
            house.Invoke(HouseProvider.BackRoomCallback); // swallowed, logged
        }
        finally { HouseProvider.OpenBackRoom = before; }
    }

    [Fact]
    public void The_scene_map_draws_the_wheel_for_the_card() => WpfRenderHarness.OnStaThread(() =>
    {
        var card = HouseProvider.Cards.Single(c => c.Id == "house.backroom");
        Assert.IsType<WheelArtView>(HouseScenes.Create(card.Poster));
        Assert.True(HouseScenes.DrawnOnly(card.Poster));
        Assert.False(HouseScenes.DrawnOnly("billboard/discord.png"));
    });

    [Fact]
    public void Each_loop_lands_a_different_prize_under_the_pointer_and_the_jackpot_one_in_four()
    {
        int jackpots = 0;
        for (int k = 0; k < WheelArtView.Order.Length; k++)
        {
            int prize = WheelArtView.PrizeOf(k);
            Assert.NotEqual(WheelArtView.PrizeOf(k - 1), prize);
            if (prize == WheelArtView.Jackpot) jackpots++;
            double land = WheelArtView.LandingTime(k);
            // The landed slice's middle sits at the top (screen angle -90).
            double at = WheelArtView.Angle(land) + (prize + 0.5) * 45 + 90;
            double off = at - Math.Round(at / 360) * 360;
            Assert.True(Math.Abs(off) < 0.5, $"loop {k}: slice {prize} is {off:0.0} degrees off the pointer");
            Assert.Equal(prize, WheelArtView.Celebration(land + 0.3, out var dp));
            Assert.InRange(dp, 0.29, 0.31);
        }
        Assert.Equal(WheelArtView.Order.Length / 4, jackpots);
        Assert.Equal(8, WheelArtView.Order.Distinct().Count());
        // The still frame shows a landing.
        Assert.True(WheelArtView.Celebration(BillboardVectorArt.StillSeconds, out var still) >= 0 && still < 0.6);
    }

    [Fact]
    public void The_angle_never_jumps_and_the_spin_is_fast()
    {
        double max = 0;
        for (double t = -2; t < WheelArtView.Loop * 13; t += 1 / 120.0)
        {
            double step = Math.Abs(WheelArtView.Turn(WheelArtView.Angle(t), WheelArtView.Angle(t + 1 / 120.0)));
            Assert.True(step < 20, $"jump of {step:0.0} degrees at {t:0.000}");
            max = Math.Max(max, step * 120);
        }
        Assert.True(max > 600, $"top speed only {max:0} degrees a second");
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
    public void The_wheel_paints_whole_loops_and_releases() => WpfRenderHarness.OnStaThread(() =>
    {
        var view = new WheelArtView { Accent = BillboardVectorArt.ParseHue("#ffc94a", Colors.Gold) };
        foreach (var (w, h) in new[] { (600.0, 340.0), (1000.0, 420.0) })
        {
            var host = Realize(view, w, h);
            for (double t = 0; t < WheelArtView.Loop * 2.2; t += 0.07)
            {
                view.SecondsForTests = t;
                view.Redraw();
            }
            host.Children.Clear();
        }
        view.SecondsForTests = null;
        view.Redraw();
        view.Release();
    });

    [Fact]
    public void Shots_when_asked() => WpfRenderHarness.OnStaThread(() =>
    {
        var dir = Environment.GetEnvironmentVariable("CCP_WHEEL_SHOTS");
        if (string.IsNullOrWhiteSpace(dir)) return;
        Directory.CreateDirectory(dir);
        const int fw = 600, fh = 340, cols = 4;
        var view = new WheelArtView { Accent = BillboardVectorArt.ParseHue("#ffc94a", Colors.Gold) };
        var host = Realize(view, fw, fh);
        // A whole loop (landing at 1.25) plus the next landing, and the still.
        var times = new[] { 1.25, 1.4, 1.6, 1.9, 2.4, 3.0, 3.5, 3.8, 4.2, 4.6, 5.2, 6.0, 6.8, 7.4, 7.75, 8.2 };
        int rows = (times.Length + cols - 1) / cols;
        var sheet = new DrawingVisual();
        using (var dc = sheet.RenderOpen())
        {
            for (int i = 0; i < times.Length; i++)
            {
                view.SecondsForTests = times[i];
                view.Redraw();
                host.UpdateLayout();
                var bmp = new RenderTargetBitmap(fw, fh, 96, 96, PixelFormats.Pbgra32);
                bmp.Render(host);
                bmp.Freeze();
                dc.DrawImage(bmp, new Rect(i % cols * fw, i / cols * fh, fw, fh));
                if (Math.Abs(times[i] - BillboardVectorArt.StillSeconds) < 0.01) Save(bmp, Path.Combine(dir, "wheel-still.png"));
            }
        }
        var all = new RenderTargetBitmap(fw * cols, fh * rows, 96, 96, PixelFormats.Pbgra32);
        all.Render(sheet);
        Save(all, Path.Combine(dir, "wheel-sheet.png"));

        // Every prize's landing, popped out, so each icon can be checked.
        var landings = new DrawingVisual();
        using (var dc = landings.RenderOpen())
        {
            for (int k = 0; k < 8; k++)
            {
                view.SecondsForTests = WheelArtView.LandingTime(k + 1) + 0.7;
                view.Redraw();
                host.UpdateLayout();
                var bmp = new RenderTargetBitmap(fw, fh, 96, 96, PixelFormats.Pbgra32);
                bmp.Render(host);
                bmp.Freeze();
                dc.DrawImage(bmp, new Rect(k % cols * fw, k / cols * fh, fw, fh));
            }
        }
        var lb = new RenderTargetBitmap(fw * cols, fh * 2, 96, 96, PixelFormats.Pbgra32);
        lb.Render(landings);
        Save(lb, Path.Combine(dir, "wheel-landings.png"));

        host.Children.Clear();
        var wide = Realize(view, 1000, 420);
        view.SecondsForTests = 3.0;
        view.Redraw();
        wide.UpdateLayout();
        var bw = new RenderTargetBitmap(1000, 420, 96, 96, PixelFormats.Pbgra32);
        bw.Render(wide);
        Save(bw, Path.Combine(dir, $"wheel-wide-{3.0.ToString("0.0", CultureInfo.InvariantCulture)}.png"));
        view.Release();
    });

    private static void Save(BitmapSource bmp, string path)
    {
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var f = File.Create(path);
        enc.Save(f);
    }
}
