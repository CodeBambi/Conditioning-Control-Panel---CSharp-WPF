using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Features;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Flash;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>Flashes v2 Drift and Bounce on this head: the card's motion picker + #1265 speed row
/// (WPF FlashFeatureControl BuildMotionPicker/UpdateDriftSpeedRow) and the overlay drift tick.</summary>
public sealed class FlashDriftTests
{
    private static void EnsureApp()
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }

    [Fact]
    public async Task Owned_drift_shows_the_picker_and_the_speed_row_saves()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            EnsureApp();
            var s = CoreSettings.Current;
            var (style, speed, seam) = (s.FlashMotionStyle, s.FlashDriftSpeed, PrizeOwnership.IsGranted);
            Window? host = null;
            try
            {
                s.FlashMotionStyle = FlashMotionStyle.Still;
                PrizeOwnership.IsGranted = _ => false;
                var unowned = new FlashFeatureControl();
                Assert.False(unowned.FindControl<Border>("BoxFlashV2")!.IsVisible);

                PrizeOwnership.IsGranted = id => id == PrizeOwnership.FlashDriftBounce;
                var card = new FlashFeatureControl();
                host = new Window { Content = card };
                host.Show();
                var cmb = card.FindControl<ComboBox>("CmbMotion")!;
                var speedRow = card.FindControl<Grid>("RowDriftSpeed")!;
                Assert.True(card.FindControl<Border>("BoxFlashV2")!.IsVisible);
                Assert.Equal(new[] { FlashMotionStyle.Still, FlashMotionStyle.DriftBounce, FlashMotionStyle.Mix },
                    cmb.Items.OfType<ComboBoxItem>().Select(i => (FlashMotionStyle)i.Tag!));
                Assert.False(speedRow.IsVisible);

                cmb.SelectedIndex = 1;
                Assert.Equal(FlashMotionStyle.DriftBounce, s.FlashMotionStyle);
                Assert.True(speedRow.IsVisible);
                card.FindControl<Slider>("SliderDriftSpeed")!.Value = 2;
                Assert.Equal(2, s.FlashDriftSpeed);
                Assert.Equal("2x", card.FindControl<TextBlock>("TxtDriftSpeed")!.Text);
            }
            finally
            {
                host?.Close();
                (s.FlashMotionStyle, s.FlashDriftSpeed, PrizeOwnership.IsGranted) = (style, speed, seam);
            }
            return Task.CompletedTask;
        });
    }

    [Fact]
    public void Drift_speed_scales_the_rolled_velocity_and_unowned_or_off_stays_still()
    {
        var seam = PrizeOwnership.IsGranted;
        try
        {
            var rect = new PixelRect(100, 100, 400, 300);
            var screen = new PixelRect(0, 0, 1920, 1080);
            var s = new AppSettings { FlashMotionStyle = FlashMotionStyle.DriftBounce, FlashDriftSpeed = 1, MotionLevel = MotionLevel.Full };
            PrizeOwnership.IsGranted = _ => true;
            var one = FlashOverlay.BuildMotion(rect, screen, s, new Random(3))!;
            s.FlashDriftSpeed = 2;
            var two = FlashOverlay.BuildMotion(rect, screen, s, new Random(3))!;
            Assert.Equal(one.Vx * 2, two.Vx, 6);
            Assert.Equal(one.Vy * 2, two.Vy, 6);
            Assert.NotEqual(0, one.Vx);

            s.MotionLevel = MotionLevel.Off;
            Assert.Null(FlashOverlay.BuildMotion(rect, screen, s, new Random(3)));
            s.MotionLevel = MotionLevel.Full;
            PrizeOwnership.IsGranted = _ => false;
            Assert.Null(FlashOverlay.BuildMotion(rect, screen, s, new Random(3)));
        }
        finally { PrizeOwnership.IsGranted = seam; }
    }

    [Fact]
    public void Debug_override_patterns_match_like_wpf()
    {
        Assert.True(PrizeOwnership.Matches("fx.*", PrizeOwnership.FlashDriftBounce));
        Assert.True(PrizeOwnership.Matches(" *", PrizeOwnership.FlashDriftBounce));
        Assert.True(PrizeOwnership.Matches("a, fx.flash.drift_bounce", PrizeOwnership.FlashDriftBounce));
        Assert.False(PrizeOwnership.Matches("fx.flash.pendulum", PrizeOwnership.FlashDriftBounce));
        Assert.False(PrizeOwnership.Matches("fx*", PrizeOwnership.FlashDriftBounce));
        Assert.False(PrizeOwnership.Matches(null, PrizeOwnership.FlashDriftBounce));
    }

    [Fact]
    public async Task The_drift_tick_moves_the_window_by_real_elapsed_time_and_stops_with_the_last_flash()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            EnsureApp();
            var clock = new SteppedClock();
            var old = FlashOverlay.Clock;
            FlashOverlay.Clock = clock;
            var w = new Window { Position = new PixelPoint(100, 100) };
            try
            {
                w.Show();
                var m = FlashMotion.Create(FlashMotionStyle.DriftBounce, 100, 100, 200, 100, 0, 0, 1920, 1080, MotionLevel.Full, new Random(1));
                m.Vx = 100; m.Vy = -40;
                FlashOverlay.StartDrift(w, m);
                Assert.True(FlashOverlay.DriftRunning);

                clock.Now += TimeSpan.TicksPerSecond / 2;
                FlashOverlay.DriftTick();
                Assert.Equal(new PixelPoint(150, 80), w.Position);

                w.Close();
                Assert.False(FlashOverlay.DriftRunning);
                Assert.Empty(FlashOverlay.Drifting);
            }
            finally
            {
                w.Close();
                FlashOverlay.Clock = old;
            }
            return Task.CompletedTask;
        });
    }

    private sealed class SteppedClock : TimeProvider
    {
        public long Now = 1_000_000;
        public override long GetTimestamp() => Now;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    }
}
