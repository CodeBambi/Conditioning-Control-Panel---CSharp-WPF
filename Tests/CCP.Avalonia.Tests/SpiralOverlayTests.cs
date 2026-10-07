using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Features;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Newtonsoft.Json;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>The spiral overlay (WPF OverlayService spiral region): the #722 opacity curve, the #572
/// frame budget, the decode, start/stop with the engine, panic, the card's slider, and the settings.</summary>
public sealed class SpiralOverlayTests(ITestOutputHelper output)
{
    // 4x4, three frames (red, green, blue), 30 ms each, looping - made with PIL.
    private const string TinyGif =
        "R0lGODlhBAAEAIEAAP8AAAAAAAAAAAAAACH/C05FVFNDQVBFMi4wAwEAAAAh+QQAAwAAACwAAAAABAAEAAAICQABCBxIsCCAgAAh+QQBAwABACwAAAAABAAEAIEA/wAAAAAAAAAAAAAICQABCBxIsCCAgAAh+QQBAwABACwAAAAABAAEAIEAAP8AAAAAAAAAAAAICQABCBxIsCCAgAA7";

    [Theory]
    [InlineData(0.0, 0.0)]
    [InlineData(0.3, 0.03)]     // bottom half: the old flat tenth, unchanged
    [InlineData(0.5, 0.05)]
    [InlineData(0.75, 0.325)]   // top half climbs at 1.1
    [InlineData(1.0, 0.6)]      // 100% paints 0.6, not 0.1
    [InlineData(2.0, 0.6)]
    [InlineData(double.NaN, 0.0)]
    public void Opacity_curve_matches_wpf_722(double slider, double painted)
        => Assert.Equal(painted, SpiralFrames.Paint(slider), 10);

    [Fact]
    public void Frame_plan_caps_size_frames_and_scales_the_delay()
    {
        Assert.Equal((1280, 853, 32, 1, 30), SpiralFrames.Plan(2400, 1600, 32, 30));     // the shipped spiral
        Assert.Equal((1280, 853, 72, 2, 100), SpiralFrames.Plan(2400, 1600, 120, 10));   // 300 MB budget -> stride 2; 10 ms -> 50
        Assert.Equal((4, 4, 3, 1, 30), SpiralFrames.Plan(4, 4, 3, 30));
    }

    [Fact]
    public async Task Engine_start_shows_and_animates_panic_and_stop_clear_it_and_the_slider_repaints()
    {
        var dir = Directory.CreateTempSubdirectory("ccp-spiral-").FullName;
        var gif = Path.Combine(dir, "tiny.gif");
        File.WriteAllBytes(gif, Convert.FromBase64String(TinyGif));
        await AvaloniaTestDispatcher.RunAsync(async () =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var s = CoreSettings.Current;
            var saved = (s.SpiralEnabled, s.SpiralPath, s.SpiralOpacity, s.SpiralTargetMonitor, s.PanicKeyEnabled, s.PanicKey);
            var provider = CoreSession.IsEngineRunningProvider;
            var shell = new MainShellWindow();
            var card = new SpiralFeatureControl();
            var cardHost = new Window { Content = card };
            shell.Show();
            try
            {
                CoreSession.IsEngineRunningProvider = () => CoreEngine.IsRunning;
                CoreEngine.StoppedHook = shell.OnEngineStopped;
                SpiralOverlay.SkipPlatformChecksForTests = true;
                (s.SpiralEnabled, s.SpiralPath, s.SpiralOpacity, s.SpiralTargetMonitor) = (true, gif, 40, -1);
                (s.PanicKeyEnabled, s.PanicKey) = (true, "F8");

                // Armed but the engine is off: nothing on screen, nothing ticking (WPF RefreshOverlays).
                SpiralOverlay.Refresh(shell);
                Assert.False(SpiralOverlay.IsShowing || SpiralOverlay.IsAnimating);

                // Start: the decode lands off-thread, then the windows come up and the clock runs.
                shell.StartEngine();
                if (SpiralOverlay.Decoding is { } d) await d;
                Assert.True(SpiralOverlay.IsShowing);
                Assert.True(SpiralOverlay.IsAnimating);
                Assert.Equal(TimeSpan.FromMilliseconds(30), SpiralOverlay.FrameDelay);
                Assert.Equal(SpiralFrames.Paint(0.4), SpiralOverlay.Shown[0].Spiral.Opacity, 10);
                var first = SpiralOverlay.Shown[0].Spiral.Source;
                SpiralOverlay.Tick();
                Assert.Equal(1, SpiralOverlay.FrameIndex);
                Assert.NotSame(first, SpiralOverlay.Shown[0].Spiral.Source);

                // Frame cost: a tick swaps a cached bitmap, so it stays cheap and allocates little.
                var alloc = GC.GetAllocatedBytesForCurrentThread();
                var sw = Stopwatch.StartNew();
                for (var i = 0; i < 1000; i++) SpiralOverlay.Tick();
                sw.Stop();
                var perTick = (GC.GetAllocatedBytesForCurrentThread() - alloc) / 1000.0;
                output.WriteLine($"spiral tick: {sw.Elapsed.TotalMilliseconds / 1000 * 1000:F2} us, {perTick:F0} B");
                Assert.True(perTick < 4096, $"{perTick} B per tick");

                // The card's slider repaints the live overlay through the curve.
                cardHost.Show();
                card.FindControl<Slider>("SliderOpacity")!.Value = 80;
                Assert.Equal(80, s.SpiralOpacity);
                Assert.Equal(SpiralFrames.Paint(0.8), SpiralOverlay.Shown[0].Spiral.Opacity, 10);

                // Panic: windows and clock gone; the saved flag is untouched.
                shell.HandlePanicKeyPress(new DateTime(2026, 1, 1, 12, 0, 0));
                Assert.False(SpiralOverlay.IsShowing || SpiralOverlay.IsAnimating);
                Assert.True(s.SpiralEnabled);

                // Restart reuses the cached frames at once; Stop clears it again.
                shell.StartEngine();
                Assert.True(SpiralOverlay.IsShowing && SpiralOverlay.IsAnimating);
                MainShellWindow.StopEngine();
                Assert.False(SpiralOverlay.IsShowing || SpiralOverlay.IsAnimating);
            }
            finally
            {
                CoreEngine.StoppedHook = null;
                CoreEngine.Stop();
                SpiralOverlay.CloseAll();
                SpiralOverlay.SkipPlatformChecksForTests = false;
                CoreSession.IsEngineRunningProvider = provider;
                (s.SpiralEnabled, s.SpiralPath, s.SpiralOpacity, s.SpiralTargetMonitor, s.PanicKeyEnabled, s.PanicKey) = saved;
                cardHost.Close();
                shell.Close();
                Directory.Delete(dir, true);
            }
        });
    }

    [Fact]
    public void The_shipped_spiral_is_linked_and_decodes_to_the_planned_frames()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Resources", "spiral.gif");
        var sw = Stopwatch.StartNew();
        var (frames, delay) = SpiralOverlay.Decode(path);
        output.WriteLine($"spiral decode: {sw.ElapsedMilliseconds} ms");
        try
        {
            Assert.Equal(32, frames.Count);
            Assert.Equal(new PixelSize(1280, 853), frames[0].PixelSize);
            Assert.Equal(TimeSpan.FromMilliseconds(30), delay);
        }
        finally { foreach (var f in frames) f.Dispose(); }
    }

    [Fact]
    public void Spiral_settings_round_trip_through_settings_json()
    {
        var a = new AppSettings { SpiralEnabled = true, SpiralOpacity = 73, SpiralTargetMonitor = 1, SpiralPath = "/x/s.gif", SpiralRandomize = true };
        var b = JsonConvert.DeserializeObject<AppSettings>(JsonConvert.SerializeObject(a))!;
        Assert.Equal((true, 73, 1, "/x/s.gif", true), (b.SpiralEnabled, b.SpiralOpacity, b.SpiralTargetMonitor, b.SpiralPath, b.SpiralRandomize));
    }
}
