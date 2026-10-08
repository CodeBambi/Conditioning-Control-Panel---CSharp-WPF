using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Controls.Fx;
using ConditioningControlPanel.Avalonia.Controls.NavRail;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Nav;
using Xunit;
using CoreFx = ConditioningControlPanel.Fx;

namespace CCP.Avalonia.Tests.Fx;

/// <summary>
/// The section edge (WPF 7.1.5 MainWindow.SectionEdge.cs + Controls/NavRail/EdgeParticles.cs +
/// NavGlow.cs): the frame and band wear the SECTION hue, the lift laps four 3 px strips, the
/// fog/ember strips follow the motion level and tier, the motion-level change reaches the edge at
/// once, and the "moved here" ring plays its track and leaves with its target.
/// </summary>
public sealed class SectionEdgeTests(ITestOutputHelper output)
{
    private static void Setup()
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }

    private static (int R, int G, int B) Px(global::Avalonia.Media.Imaging.WriteableBitmap bmp, int x, int y)
    {
        var buf = Marshal.AllocHGlobal(4);
        try
        {
            bmp.CopyPixels(new PixelRect(x, y, 1, 1), buf, 4, 4);
            uint v = (uint)Marshal.ReadInt32(buf);
            int lo = (int)(v & 0xFF), mid = (int)((v >> 8) & 0xFF), hi = (int)((v >> 16) & 0xFF);
            // Bgra8888 keeps B in the low byte, Rgba8888 keeps R there.
            return bmp.Format == global::Avalonia.Platform.PixelFormat.Rgba8888 ? (lo, mid, hi) : (hi, mid, lo);
        }
        finally { Marshal.FreeHGlobal(buf); }
    }

    /// <summary>Runs <paramref name="body"/> at Full motion, Quality tier, OS animations on; restores after.</summary>
    private static void AtFull(Action body)
    {
        var s = CoreSettings.Current;
        var (motion, perf) = (s.MotionLevel, s.PerformanceMode);
        var old = OsReducedMotion.TestOverride;
        try
        {
            OsReducedMotion.TestOverride = true;
            s.MotionLevel = MotionLevel.Full;
            s.PerformanceMode = false;
            body();
        }
        finally
        {
            s.MotionLevel = motion;
            s.PerformanceMode = perf;
            OsReducedMotion.TestOverride = old;
            AmbientFxCanvas.Env.RaiseMotionGateChanged();
        }
    }

    [Theory]
    [InlineData(CoreFx.EdgeSide.Top, 0.0, -1.0)]
    [InlineData(CoreFx.EdgeSide.Top, 1.5, 0.0)]
    [InlineData(CoreFx.EdgeSide.Top, 6.0, 1.0)]
    [InlineData(CoreFx.EdgeSide.Right, 2.9, -1.0)]
    [InlineData(CoreFx.EdgeSide.Right, 4.5, 0.0)]
    [InlineData(CoreFx.EdgeSide.Bottom, 7.5, 0.0)]
    [InlineData(CoreFx.EdgeSide.Bottom, 9.0, -1.0)]
    [InlineData(CoreFx.EdgeSide.Left, 8.0, 1.0)]
    [InlineData(CoreFx.EdgeSide.Left, 10.5, 0.0)]
    [InlineData(CoreFx.EdgeSide.Top, 12.75, -0.5)]   // the lap repeats
    public void LiftLegsMatchWpfKeyframes(CoreFx.EdgeSide side, double t, double expected) =>
        Assert.Equal(expected, SectionEdgeLift.LegAt(side, t), 6);

    [Fact]
    public Task LiftParksSpinsAndHides() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var lift = new SectionEdgeLift();
        var w = new Window { Width = 600, Height = 400, Content = lift };
        w.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            lift.Apply(SectionEdgeMotion.Fixed, run: true);
            Assert.True(lift.IsVisible);
            Assert.False(lift.IsSpinning);
            Assert.Equal(0, lift.OffsetOf(CoreFx.EdgeSide.Top));          // top centre
            Assert.Equal(-1, lift.OffsetOf(CoreFx.EdgeSide.Right));       // parked off its strip

            lift.Apply(SectionEdgeMotion.Spin, run: false);               // inactive window: no lap
            Assert.False(lift.IsSpinning);
            Assert.Equal(-1, lift.OffsetOf(CoreFx.EdgeSide.Top));
            lift.Step(1.5);
            Assert.Equal(0, lift.OffsetOf(CoreFx.EdgeSide.Top), 6);
            lift.Apply(SectionEdgeMotion.Spin, run: true);
            Assert.True(lift.IsSpinning);
            Assert.Equal(1.5, lift.LapSeconds, 6);                        // a resume keeps its place

            lift.Apply(SectionEdgeMotion.Solid, run: true);
            Assert.False(lift.IsVisible);
            Assert.False(lift.IsSpinning);
        }
        finally { w.Close(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task StripsFollowTheMotionLevelAndTier() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var edge = new EdgeParticles { MotionOverride = MotionLevel.Off, TierAllowsParticlesOverride = true };
        var w = new Window { Width = 800, Height = 500, Content = edge };
        w.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            edge.Mount(NavStripRules.Pink);
            Assert.Empty(edge.Strips);
            Assert.False(edge.FogLive);

            edge.MotionOverride = MotionLevel.Reduced;
            edge.Refresh();
            Assert.Equal(4, edge.Strips.Count);
            Assert.All(edge.Strips, s => Assert.Equal(AmbientFxLayers.EdgeFog, s.Layers));
            Assert.Equal(new[] { EdgeSide.Top, EdgeSide.Right, EdgeSide.Bottom, EdgeSide.Left }, edge.Strips.Select(s => s.EdgeSide));
            Assert.Equal(CoreFx.EdgeFogMath.StripPx, edge.Strips[0].Height);
            Assert.Equal(CoreFx.EdgeFogMath.StripPx, edge.Strips[1].Width);

            edge.MotionOverride = MotionLevel.Full;
            edge.Refresh();
            Assert.All(edge.Strips, s => Assert.Equal(AmbientFxLayers.EdgeFog | AmbientFxLayers.EdgeDrift, s.Layers));
            var before = edge.Strips[0];
            edge.Mount(NavStripRules.Sky, 250);                        // same mode: retint, no rebuild
            Assert.Same(before, edge.Strips[0]);
            Assert.Equal(NavPaint.C(NavStripRules.Sky), edge.Strips[0].Tint);

            edge.TierAllowsParticlesOverride = false;                  // Performance tier: no budget
            edge.Refresh();
            Assert.Empty(edge.Strips);
            Assert.Empty(edge.Children);
        }
        finally { w.Close(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task EdgeWearsTheSectionHueAndFollowsTheMotionLevel() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        AtFull(() =>
        {
            var w = new MainShellWindow();
            w.Show();
            try
            {
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(NavStripRules.Lilac, w.SectionEdgeHue);           // Home

                w.ShowTab("studio");
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(NavStripRules.Pink, w.SectionEdgeHue);
                w.StepEdgeFade(10_000);                                        // land the crossfade
                var (glow, line, lift) = w.SectionEdgePainted;
                Assert.Equal(SectionEdgeRules.LineStops(NavStripRules.Pink, MotionLevel.Full), line);
                // Full on Quality: the fog runs, so the band is thin and softer.
                Assert.True(w.SectionEdgeStrips!.FogLive);
                Assert.Equal(SectionEdgeRules.FogBand, w.SectionEdgeBandDepth);
                Assert.Equal(SectionEdgeRules.GlowStops(NavStripRules.Pink, fogLive: true), glow);
                Assert.Equal(SectionEdgeRules.LiftStops(NavStripRules.Pink, MotionLevel.Full), lift);
                // The frame is the section hue, never the mod accent brush.
                var edge = w.FindControl<Border>("GlassWindowEdge")!;
                Assert.Equal(NavPaint.C(Argb(NavStripRules.Pink, SectionEdgeRules.LineAlpha)),
                             ((ILinearGradientBrush)edge.BorderBrush!).GradientStops[0].Color);

                w.ShowTab("availablesubjects");
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(NavStripRules.Sky, w.SectionEdgeHue);
                w.StepEdgeFade(125);                                           // half way: still between the hues
                var mid = w.SectionEdgePainted.Line[0];
                Assert.NotEqual(SectionEdgeRules.LineStops(NavStripRules.Sky, MotionLevel.Full)[0], mid);
                Assert.NotEqual(SectionEdgeRules.LineStops(NavStripRules.Pink, MotionLevel.Full)[0], mid);
                w.StepEdgeFade(250);
                Assert.Equal(SectionEdgeRules.LineStops(NavStripRules.Sky, MotionLevel.Full), w.SectionEdgePainted.Line);

                // The motion-level fan-out: Off reaches the edge at once (no section change).
                CoreSettings.Current.MotionLevel = MotionLevel.Off;
                AmbientFxCanvas.Env.RaiseMotionGateChanged();
                Assert.Empty(w.SectionEdgeStrips!.Strips);
                Assert.False(w.SectionEdgeLiftStrips!.IsVisible);
                Assert.Equal(SectionEdgeRules.GlowBand, w.SectionEdgeBandDepth);
                Assert.Equal(SectionEdgeRules.GlowStops(NavStripRules.Sky, fogLive: false), w.SectionEdgePainted.Glow);

                CoreSettings.Current.MotionLevel = MotionLevel.Reduced;
                AmbientFxCanvas.Env.RaiseMotionGateChanged();
                Assert.True(w.SectionEdgeLiftStrips!.IsVisible);
                Assert.Equal(SectionEdgeMotion.Fixed, w.SectionEdgeLiftStrips.Motion);
                Assert.Equal(CoreFx.EdgeFogMode.Reduced, w.SectionEdgeStrips.Mode);
            }
            finally { w.Close(); }
        });
        return Task.CompletedTask;
    });

    [Fact]
    public Task FrameRendersInTheSectionHue() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        AtFull(() =>
        {
            CoreSettings.Current.MotionLevel = MotionLevel.Off;               // no fade, no fog: a still frame
            var w = new MainShellWindow();
            w.Show();
            try
            {
                Dispatcher.UIThread.RunJobs();
                foreach (var (tab, hue) in new[] { ("studio", NavStripRules.Pink), ("availablesubjects", NavStripRules.Sky) })
                {
                    w.ShowTab(tab);
                    for (int i = 0; i < 3; i++)
                    {
                        Dispatcher.UIThread.RunJobs();
                        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                    }
                    Dispatcher.UIThread.RunJobs();
                    using var bmp = w.CaptureRenderedFrame()!;
                    var c = Px(bmp, 1, bmp.PixelSize.Height / 2);              // the left frame line
                    Assert.Equal(hue, w.SectionEdgeHue);
                    byte r = (byte)(hue >> 16), g = (byte)(hue >> 8), b = (byte)hue;
                    Assert.InRange(c.R, r - 30, r + 10);
                    Assert.InRange(c.G, g - 30, g + 10);
                    Assert.InRange(c.B, b - 30, b + 10);
                }
            }
            finally { w.Close(); }
        });
        return Task.CompletedTask;
    });

    [Fact]
    public Task MovedHereGlowPlaysItsTrackAndLeavesWithItsTarget() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        AtFull(() =>
        {
            var w = new MainShellWindow();
            w.Show();
            try
            {
                Dispatcher.UIThread.RunJobs();
                w.ShowTab("studio");
                Dispatcher.UIThread.RunJobs();

                Assert.True(w.GlowNavTarget("haptics"));                          // pill + rail row
                var ring = NavGlow.Last!;
                Assert.True(ring.IsLive);
                Assert.IsAssignableFrom<Panel>(ring.Parent);                       // on the adorner layer
                ring.Advance(NavGlowRules.SheenMs / 2.0);
                Assert.Equal(1.0, ring.Opacity, 3);
                ring.Advance(NavGlowRules.SheenMs / 2.0 + 1000);
                Assert.Equal(NavGlowRules.FullHoldOpacity, ring.Opacity, 3);
                ring.Advance(NavGlowRules.TotalMs(MotionLevel.Full));
                Assert.False(ring.IsLive);
                Assert.Null(ring.Parent);

                // A glow on a strip pill leaves when the strip hides (Home draws no strip).
                var pill = w.NavStrip!.PillFor("haptics")!;
                Assert.True(NavGlow.Once(pill, NavStripRules.Pink, MotionLevel.Full, "test"));
                var onPill = NavGlow.Last!;
                w.ShowTab("settings");
                Dispatcher.UIThread.RunJobs();
                Assert.False(onPill.IsLive);
                Assert.Null(onPill.Parent);

                // Off: nothing.
                Assert.False(NavGlow.Once(w.FindControl<Control>("GlassWindowEdge"), NavStripRules.Pink, MotionLevel.Off));
            }
            finally { w.Close(); }
        });
        return Task.CompletedTask;
    });

    /// <summary>FxBench for the whole edge: the four strips of a 1661x1002 window at Full (fog +
    /// embers), sim + Skia paint per frame, summed. Prints the rows; fails only if pathological.</summary>
    [Fact]
    public Task WholeEdgeBench() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        AtFull(() =>
        {
            var edge = new EdgeParticles { MotionOverride = MotionLevel.Full, TierAllowsParticlesOverride = true };
            var w = new Window { Width = 1661, Height = 1002, Content = edge, ShowActivated = false };
            w.Show();
            try
            {
                Dispatcher.UIThread.RunJobs();
                edge.Mount(NavStripRules.Sky);
                Dispatcher.UIThread.RunJobs();
                double total = 0;
                foreach (var strip in edge.Strips)
                {
                    strip.StepForTests(30);
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    strip.StepForTests(120);
                    sw.Stop();
                    double ms = sw.Elapsed.TotalMilliseconds / 120;
                    total += ms;
                    var (bw, bh) = strip.Surface.BackingSize;
                    output.WriteLine(FormattableString.Invariant($"section edge {strip.EdgeSide,-6} fog+drift {ms,7:0.000} ms/frame  backing {bw}x{bh}"));
                }
                output.WriteLine(FormattableString.Invariant($"section edge all four strips   {total,7:0.000} ms/frame"));
                Assert.True(total < 16, $"the edge costs {total:0.00} ms/frame");
            }
            finally { edge.Mount(NavStripRules.Sky); w.Close(); }
        });
        return Task.CompletedTask;
    });

    private static uint Argb(uint c, byte a) => (c & 0x00FFFFFF) | ((uint)a << 24);
}
