using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Descent;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

/// <summary>THE FUSE RAIL CHIP and THE ZERO SHOW's canvas on this head (WPF Controls/
/// DescentFuseRailChip.cs, Controls/DescentFuseStageVisual.cs), driven from the shell ctor and
/// the Core countdown's own sync entry point on a stepped clock.</summary>
public sealed class DescentFuseRailChipTests
{
    private static void Setup()
    {
        if (Application.Current is null)
            AppBuilder.Configure<AvApp>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    [Fact]
    public Task ChipCountsDown_Shakes_OpensTheRoom_AndFlashesOutAtZero() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var s = service.Current;
        s.DescentCeremonyAtUtc = null;
        s.DescentMigrationCompleted = false;
        s.PendingDescentMigrationChoice = null;
        s.MotionLevel = MotionLevel.Full;
        s.PerformanceMode = false;
        var oldFuse = AvApp.DescentCountdown;
        var fuse = new DescentCountdownService();
        AvApp.DescentCountdown = fuse;
        var clock = new SteppedClock();
        DescentFuseRailChip.Time = clock;

        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            Dispatcher.UIThread.RunJobs();
            var chip = shell.Named<DescentFuseRailChip>("FuseRailChip")!;
            Assert.False(chip.IsVisible);   // fuse dark: every install today

            fuse.ApplyCeremonyAt(DateTime.UtcNow.AddHours(48).ToString("yyyy-MM-ddTHH:mm:ssZ"));
            Dispatcher.UIThread.RunJobs();
            Assert.True(chip.IsVisible);
            Assert.Equal(FuseWobbleTier.Frequent, chip.Tier);
            Assert.StartsWith("1d 23:", chip.Label);
            Assert.Contains(DescentFuseCopy.ChipSubtitle, chip.TipText);
            clock.Now += TimeSpan.FromSeconds(0.06).Ticks;   // the burst's first peak
            chip.Step();
            Assert.Equal(8.0, chip.Angle, 3);
            clock.Now += TimeSpan.FromSeconds(1).Ticks;      // still between bursts
            chip.Step();
            Assert.Equal(0, chip.Angle, 3);

            // The door: keyboard as well as pointer (P17).
            chip.Focus();
            shell.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("spiral", shell.CurrentTab);

            fuse.ApplyCeremonyAt(DateTime.UtcNow.AddMinutes(8).ToString("yyyy-MM-ddTHH:mm:ssZ"));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(FuseWobbleTier.Violent, chip.Tier);
            clock.Now += TimeSpan.FromSeconds(0.11).Ticks;   // half a violent cycle: full throw
            chip.Step();
            Assert.Equal(3.0, Math.Abs(chip.Angle), 3);
            var rail = shell.Named<Border>("NavSidebar")!;
            var before = chip.Angle;
            rail.IsVisible = false;                          // nobody can see it: no drawing (P01)
            clock.Now += TimeSpan.FromSeconds(0.11).Ticks;
            chip.Step();
            Assert.Equal(before, chip.Angle);
            rail.IsVisible = true;
            chip.Step();
            Assert.NotEqual(before, chip.Angle, 3);

            // ZERO, live: the chip flashes for 2.5 s and only then leaves.
            s.DescentCeremonyAtUtc = DateTime.UtcNow.AddSeconds(-1).ToString("o");
            fuse.OnTimerTick();
            Dispatcher.UIThread.RunJobs();
            Assert.True(chip.IsFlashingOut);
            Assert.True(chip.IsVisible);
            clock.Now += TimeSpan.FromSeconds(1.2).Ticks;
            chip.Step();
            Assert.True(chip.IsVisible);
            clock.Now += TimeSpan.FromSeconds(1.4).Ticks;
            chip.Step();
            Assert.False(chip.IsVisible);
            Assert.False(chip.IsWobbling);

            // The live show the same zero opened draws on the ported canvas.
            var show = DescentFuseWindow.OnScreen.Single();
            Assert.Same(show.FindControl<Grid>("StageHost"), show.Stage.Parent);
            Assert.Equal(DescentFuseStage.Freeze, show.Stage.Frame.Stage);
        }
        finally
        {
            DescentFuseWindow.ForceCloseAll();
            shell.Close();
            DescentFuseRailChip.Time = TimeProvider.System;
            AvApp.DescentCountdown = oldFuse;
            fuse.Dispose();
            service.SaveImmediate();
            CoreSettings.ServiceProvider = null;
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task StageVisual_DrawsEachBeatLikeWpf() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var stage = new DescentFuseStageVisual(Color.FromRgb(0xFF, 0x69, 0xB4));
        var w = new Window { Width = 400, Height = 300, Background = Brushes.Black, Content = stage };
        try
        {
            w.Show();
            int Draw(DescentFuseStage beat, double p)
            {
                stage.SetFrame(new DescentFuseFrame(beat, p, -1));
                w.CaptureRenderedFrame();
                return stage.LastDrawCount;
            }
            Assert.Equal(0, Draw(DescentFuseStage.Freeze, 0));        // still dark
            Assert.True(Draw(DescentFuseStage.Freeze, 1) > 90);       // the spark field + sealed glyph
            Assert.True(Draw(DescentFuseStage.Crack, 1) > 90 + 9 * 9);   // + nine hairlines
            Assert.Equal(0, Draw(DescentFuseStage.Black, 0.5));       // one full second of nothing
            Assert.True(Draw(DescentFuseStage.Held, 1) > 0);
            var frame = w.CaptureRenderedFrame()!;
            using (var fb = frame.Lock())
            {
                var px = new byte[4];
                System.Runtime.InteropServices.Marshal.Copy(fb.Address + 150 * fb.RowBytes + 200 * 4, px, 0, 4);
                Assert.True(px[0] + px[1] + px[2] > 120, "the bloom is not lit at the centre");
            }

            stage.Begin(DescentShowKind.Ignition, reducedMotion: false);
            stage.SetIgnition(60);
            w.CaptureRenderedFrame();
            Assert.True(stage.LastDrawCount > 200);                   // the whole Year One arm
        }
        finally { w.Close(); }
        return Task.CompletedTask;
    });

    private sealed class SteppedClock : TimeProvider
    {
        public long Now;
        public override long GetTimestamp() => Now;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    }
}
