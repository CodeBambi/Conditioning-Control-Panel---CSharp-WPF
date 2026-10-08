using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls.HelpLoops;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>Each ported help-loop scene (help-loops slices, WPF Controls/HelpLoops/Scenes): it is
/// found by its popover's section id, renders a real frame in the middle of every step on the
/// stepped clock without faulting, and the step chips light in order. Plus the kit's Back-layer blur.</summary>
public sealed class HelpLoopScenesTests
{
    [Theory]
    [InlineData("BouncingText")]
    [InlineData("BlinkTrainer")]
    [InlineData("KeywordTriggers")]
    [InlineData("Audio")]
    [InlineData("Scheduler")]
    [InlineData("SpiralOverlay")]
    [InlineData("GazeMinigame")]
    [InlineData("Video")]
    [InlineData("BubblePop")]
    [InlineData("BrainDrain")]
    [InlineData("MindWipe")]
    [InlineData("BubbleCount")]
    [InlineData("LockCard")]
    [InlineData("FocusGaze")]
    [InlineData("IntensityRamp")]
    [InlineData("Presets")]
    [InlineData("Subliminals")]
    [InlineData("WebcamCalibration")]
    public Task SceneRendersEveryStepAndLightsItsChipsInOrder(string id) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        EnsureAvalonia();
        Assert.True(HelpLoopRegistry.TryGet(id, out var scene), $"no scene registered for {id}");
        var oldSettings = CoreSettings.ServiceProvider;
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var clock = new SteppedClock();
        HelpLoopView.Time = clock;
        HelpLoopView.RequestAnimationFrame = (_, _) => { };   // ticks are driven below
        CoreSettings.Current.MotionLevel = MotionLevel.Full;
        var view = new HelpLoopView(scene);
        var steps = new HelpLoopSteps(view, Brushes.HotPink);
        var host = new Window { Width = 480, Height = 400, Content = new StackPanel { Children = { view, steps } } };
        host.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            Assert.True(view.IsRunning);
            Assert.Equal(3, scene.Steps.Count);
            long elapsed = 0;
            for (int i = 0; i < scene.Steps.Count; i++)
            {
                var mid = (scene.Steps[i].StartMs + scene.Steps[i].EndMs) / 2;
                // Forward-only clock: wrap through the loop end when the step lies before the still frame.
                var target = (mid - scene.StillMs + scene.DurationMs) % scene.DurationMs;
                while (target < elapsed) target += scene.DurationMs;
                clock.Now += TimeSpan.FromMilliseconds(target - elapsed).Ticks;
                elapsed = (long)target;
                view.Tick();
                Assert.Equal(mid, view.CurrentTime, 3);
                host.CaptureRenderedFrame();
                Assert.False(view.Failed, $"{id} threw drawing t={mid}");
                for (int j = 0; j < scene.Steps.Count; j++)
                    Assert.True(j == i == steps.IsLit(j), $"{id} t={mid}: chip {j} lit={steps.IsLit(j)}");
            }
        }
        finally
        {
            host.Content = null;
            host.Close();
            HelpLoopView.Time = TimeProvider.System;
            HelpLoopView.RequestAnimationFrame = (top, cb) => top.RequestAnimationFrame(cb);
            CoreSettings.ServiceProvider = oldSettings;
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task BackBlurSoftensTheDesktopOnly() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        EnsureAvalonia();
        uint sharp = EdgePixel(0), blurred = EdgePixel(6);
        Assert.Equal(0u, sharp);                 // black just outside the white Back bar
        Assert.NotEqual(sharp, blurred);         // the blur spills white over it
        // Front and ground stay sharp: next to the Front bar's edge, and the ground at the stage edge
        // (a blurred ground would pull the transparent outside in).
        foreach (var (x, y) in new[] { (443.0, 135.0), (477.0, 135.0) })
            Assert.Equal(Pixel(new BarScene(), x, y), Pixel(new BarScene { Blur = 6 }, x, y));
        return Task.CompletedTask;
    });

    [Fact]
    public Task RoundedShapesKeepTheirCornersThroughTheRecordedLayers() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        EnsureAvalonia();
        // The bar's top-left corner, rounded 20: (122, 62) sits outside the curve, (140, 80) inside.
        Assert.Equal(0u, Pixel(new BarScene { Round = 20 }, 122, 62));
        Assert.Equal(0xFFFFFFu, Pixel(new BarScene { Round = 20 }, 140, 80));
        return Task.CompletedTask;
    });

    [Fact]
    public Task BubbleCountLightsTheRightAnswerMint() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        EnsureAvalonia();
        Assert.True(HelpLoopRegistry.TryGet("BubbleCount", out var scene));
        // t=5000: the "7" button (245,120 40x40) has been picked and fills Mint #5fffd0 (WPF BubbleCountLoop);
        // the frame is RGBA, so the little-endian read gives 0xBBGGRR.
        Assert.Equal(0xD0FF5Fu, Pixel(new FixedAt(scene, 5000), 280, 135));
        return Task.CompletedTask;
    });

    [Fact]
    public Task WebcamCalibrationTicksTheGridPointsAlreadyRead() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        EnsureAvalonia();
        Assert.True(HelpLoopRegistry.TryGet("WebcamCalibration", out var scene));
        // Grid point 7 sits at (60,94) on the snake walk (WPF WebcamCalibrationLoop). At t=5000 the dot is on
        // hop 8, so points 0..7 carry a Mint #5fffd0 tick; at t=2000 nothing has been read yet. RGBA -> 0xBBGGRR.
        Assert.Equal(0xD0FF5Fu, Pixel(new FixedAt(scene, 5000), 60, 94));
        Assert.NotEqual(0xD0FF5Fu, Pixel(new FixedAt(scene, 2000), 60, 94));
        return Task.CompletedTask;
    });

    [Fact]
    public Task IntensityRampCurveStopsAtItsHead() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        EnsureAvalonia();
        // A point on the full climb curve (WPF IntensityRampLoop: plot 332,132 118x74, y = .12 + .78 * EaseInOut(u)),
        // well right of the head at t=1500 (u ~ .23). Before the curve starts (t=300) and at t=1500 it shows the
        // same card; once the curve has reached it (t=4800) it does not.
        const double x = 420;
        double u = (x - 332) / 118.0, y = 206 - 74 * (.12 + .78 * LoopMath.EaseInOut(u));
        uint Px(double at) { Assert.True(HelpLoopRegistry.TryGet("IntensityRamp", out var s)); return Pixel(new FixedAt(s, at), x, y); }
        var empty = Px(300);
        Assert.Equal(empty, Px(1500));
        Assert.NotEqual(empty, Px(4800));
        return Task.CompletedTask;
    });

    /// <summary>A black Back with a white bar ending at x=240 (Front bar 360..440, ground right of 280); the pixel 3 px right of its edge.</summary>
    private static uint EdgePixel(double blur) => Pixel(new BarScene { Blur = blur }, 243, 135);

    private static uint Pixel(HelpLoopScene scene, double x, double y)
    {
        var view = new HelpLoopView(scene);
        var host = new Window { Width = 480, Height = 270, Content = view };
        host.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            var bmp = host.CaptureRenderedFrame()!;
            Assert.False(view.Failed);
            var buf = System.Runtime.InteropServices.Marshal.AllocHGlobal(4);
            try
            {
                var sx = bmp.PixelSize.Width / LoopFrame.StageWidth;
                bmp.CopyPixels(new PixelRect((int)(x * sx), (int)(y * sx), 1, 1), buf, 4, 4);
                return (uint)System.Runtime.InteropServices.Marshal.ReadInt32(buf) & 0x00FFFFFFu;
            }
            finally { System.Runtime.InteropServices.Marshal.FreeHGlobal(buf); }
        }
        finally
        {
            host.Content = null;   // a closed Window keeps its content attached: detach so the loop stops
            host.Close();
        }
    }

    /// <summary>Draws <paramref name="inner"/> frozen at one time.</summary>
    private sealed class FixedAt(HelpLoopScene inner, double at) : HelpLoopScene
    {
        public override string Id => "";
        public override double DurationMs => inner.DurationMs;
        public override double StillMs => 0;
        public override IReadOnlyList<HelpLoopStep> Steps => inner.Steps;
        public override void Draw(LoopFrame f, double t) => inner.Draw(f, at);
    }

    private sealed class BarScene : HelpLoopScene
    {
        public double Blur, Round;
        public override string Id => "";
        public override double DurationMs => 1000;
        public override double StillMs => 0;
        public override IReadOnlyList<HelpLoopStep> Steps { get; } = Array.Empty<HelpLoopStep>();
        public override void Draw(LoopFrame f, double t)
        {
            // Back covers the left of the stage; the ground shows on the right under a Front bar.
            f.Back.DrawRectangle(Brushes.Black, null, new Rect(0, 0, 280, LoopFrame.StageHeight));
            f.Back.DrawRoundedRectangle(Brushes.White, null, new Rect(120, 60, 120, 150), Round, Round);
            f.Front.DrawRectangle(Brushes.White, null, new Rect(360, 60, 80, 150));
            f.BackBlur = Blur;
        }
    }

    private static void EnsureAvalonia()
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        LocalizationManager.Instance.SetLanguage("en");
    }

    private sealed class SteppedClock : TimeProvider
    {
        public long Now;
        public override long GetTimestamp() => Now;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    }
}
