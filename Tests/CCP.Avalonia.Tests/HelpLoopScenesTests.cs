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
        Assert.Equal(0u, sharp);                 // black just outside the white bar
        Assert.NotEqual(sharp, blurred);                                         // the blur spills white over it
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

    /// <summary>A black Back with a white bar ending at x=240; the pixel 3 px right of its edge.</summary>
    private static uint EdgePixel(double blur) => Pixel(new BarScene { Blur = blur }, 243, 135);

    private static uint Pixel(BarScene scene, double x, double y)
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

    private sealed class BarScene : HelpLoopScene
    {
        public double Blur, Round;
        public override string Id => "";
        public override double DurationMs => 1000;
        public override double StillMs => 0;
        public override IReadOnlyList<HelpLoopStep> Steps { get; } = Array.Empty<HelpLoopStep>();
        public override void Draw(LoopFrame f, double t)
        {
            f.Back.DrawRectangle(Brushes.Black, null, new Rect(0, 0, LoopFrame.StageWidth, LoopFrame.StageHeight));
            f.Back.DrawRoundedRectangle(Brushes.White, null, new Rect(120, 60, 120, 150), Round, Round);
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
