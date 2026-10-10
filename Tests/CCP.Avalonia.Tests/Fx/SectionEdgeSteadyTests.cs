using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Controls.Fx;
using ConditioningControlPanel.Avalonia.Controls.NavRail;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Nav;
using SkiaSharp;
using Xunit;

namespace CCP.Avalonia.Tests.Fx;

/// <summary>
/// The section edge glow must be steady (owner, 2026-10-09: "tends to flicker", WPF is steady).
/// Root cause: FxSurface freed frame N's snapshot as soon as the UI thread painted N+1, while the
/// render thread (a frame behind) could still be drawing the op recorded for N, which then drew
/// nothing: a blank strip for one composed frame. These pin the frame lifetime and the glow's
/// frame-to-frame steadiness.
/// </summary>
public sealed class SectionEdgeSteadyTests(ITestOutputHelper output)
{
    private static void Setup()
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }

    private static void Pump()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    [Fact]
    public Task AnOpRecordedForTheLastFrameStillDrawsAfterTheNextPaint() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var surface = new FxSurface { ResolutionScale = 1.0 };
        var w = new Window { Width = 120, Height = 60, Background = Brushes.Black, Content = surface };
        surface.PaintSurface += (_, e) =>
        {
            using var p = new SKPaint { Color = new SKColor(200, 100, 50, 255) };
            e.Canvas.DrawRect(0, 0, e.Info.Width, e.Info.Height, p);
        };
        w.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            surface.Redraw();
            var late = surface.RecordForTests();            // what the render thread holds for frame N
            Assert.NotNull(late);
            surface.Redraw();                               // the UI thread paints N+1
            surface.Redraw();                               // and N+2, before the render thread catches up
            Assert.True(FxSurface.OpCanDraw(late!), "frame N was freed under a draw op that still renders it (blank frame = flicker)");
            late!.Dispose();                                // the compositor lets go of it
            Assert.False(FxSurface.OpCanDraw(late));
            // The compositor drops the ops it recorded; it may hold the newest one a pulse longer.
            for (int i = 0; i < 5 && surface.LiveFrames > 1; i++) Pump();
            Assert.Equal(1, surface.LiveFrames);            // only the current frame is kept
        }
        finally { w.Close(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task FramesAreFreedAsTheCompositorLetsGo() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var surface = new FxSurface { ResolutionScale = 0.5 };
        var w = new Window { Width = 200, Height = 80, Background = Brushes.Black, Content = surface };
        surface.PaintSurface += (_, e) =>
        {
            using var p = new SKPaint { Color = new SKColor(90, 160, 220, 255) };
            e.Canvas.DrawCircle(e.Info.Width / 2f, e.Info.Height / 2f, 20, p);
        };
        w.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            int worst = 0;
            for (int i = 0; i < 60; i++)
            {
                surface.Redraw();
                Pump();
                worst = Math.Max(worst, surface.LiveFrames);
            }
            output.WriteLine($"live snapshots at most {worst} over 60 frames");
            // Avalonia disposes a replaced op; if it ever stopped, frames would pile up to the cap.
            Assert.InRange(worst, 1, 3);
        }
        finally { w.Close(); }
        return Task.CompletedTask;
    });

    /// <summary>
    /// Sample the four strips' painted glow over 150 frames at Full (fog + embers, Sky): the
    /// summed brightness never drops toward zero (a blank strip) and moves only by the dust's own
    /// twinkle between frames (about 7% on this seed; the fog numbers are WPF's, pinned in Core).
    /// </summary>
    [Fact]
    public Task EdgeGlowIsSteadyFrameToFrame() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var s = CoreSettings.Current;
        var (motion, perf) = (s.MotionLevel, s.PerformanceMode);
        var old = OsReducedMotion.TestOverride;
        var edge = new EdgeParticles { MotionOverride = MotionLevel.Full, TierAllowsParticlesOverride = true };
        var w = new Window { Width = 900, Height = 600, Content = edge, ShowActivated = false };
        try
        {
            OsReducedMotion.TestOverride = true;
            s.MotionLevel = MotionLevel.Full;
            s.PerformanceMode = false;
            w.Show();
            Dispatcher.UIThread.RunJobs();
            edge.Mount(NavStripRules.Sky);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(4, edge.Strips.Count);

            foreach (var strip in edge.Strips) strip.StepForTests(60);   // past the fog's fade-in
            var samples = new List<double>();
            for (int f = 0; f < 150; f++)
            {
                double sum = 0;
                foreach (var strip in edge.Strips)
                {
                    strip.StepForTests(1);
                    using var bmp = strip.Surface.CopyBacking();
                    Assert.NotNull(bmp);
                    sum += Brightness(bmp!);
                }
                samples.Add(sum);
            }
            double min = samples.Min(), max = samples.Max(), mean = samples.Average();
            double worstStep = samples.Zip(samples.Skip(1), (a, b) => Math.Abs(b - a) / mean).Max();
            output.WriteLine(FormattableString.Invariant($"edge glow mean {mean:0.0} min {min:0.0} max {max:0.0} worst step {worstStep:P2}"));
            Assert.True(min > mean * 0.5, $"the glow dipped to {min:0.0} of mean {mean:0.0}");
            // Embers spawn at random, so a healthy edge steps 7-13% frame to frame (10 runs,
            // 2026-10-09); a strip blinking out is far bigger and the dip check above catches it.
            Assert.True(worstStep < 0.20, $"the glow jumped {worstStep:P1} in one frame");
        }
        finally
        {
            edge.MotionOverride = MotionLevel.Off;
            edge.Refresh();
            w.Close();
            s.MotionLevel = motion;
            s.PerformanceMode = perf;
            OsReducedMotion.TestOverride = old;
            AmbientFxCanvas.Env.RaiseMotionGateChanged();
        }
        return Task.CompletedTask;
    });

    /// <summary>Sum of R+G+B over every pixel, scaled per pixel (the glow's total light).</summary>
    private static double Brightness(SKBitmap bmp)
    {
        double sum = 0;
        var px = bmp.Pixels;
        foreach (var c in px) sum += c.Red + c.Green + c.Blue;
        return sum / Math.Max(1, px.Length);
    }
}
