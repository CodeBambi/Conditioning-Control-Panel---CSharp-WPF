using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
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
using ConditioningControlPanel.Models;
using SkiaSharp;
using EdgeDriftMath = ConditioningControlPanel.Fx.EdgeDriftMath;
using EdgeSide = ConditioningControlPanel.Fx.EdgeSide;
using EdgeFogMath = ConditioningControlPanel.Fx.EdgeFogMath;
using VaultMoteMath = ConditioningControlPanel.Fx.VaultMoteMath;
using Xunit;

namespace CCP.Avalonia.Tests.Fx;

/// <summary>
/// FxSurface (WPF Controls/FxSurface.cs) and the AmbientFxCanvas put back on it: headless Skia
/// renders proving the layers ADD (SKBlendMode.Plus, the bloom the first port lost to
/// source-over), the reduced resolution, and the new layers.
/// </summary>
public sealed class FxSurfaceRenderTests
{
    private static void Setup()
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }

    /// <summary>RGB of one window pixel (device px).</summary>
    private static (int R, int G, int B) Px(global::Avalonia.Media.Imaging.WriteableBitmap bmp, int x, int y)
    {
        var buf = Marshal.AllocHGlobal(4);
        try
        {
            bmp.CopyPixels(new PixelRect(x, y, 1, 1), buf, 4, 4);
            uint v = (uint)Marshal.ReadInt32(buf);
            // Bgra8888 (the headless frame format): B in the low byte.
            return ((int)((v >> 16) & 0xFF), (int)((v >> 8) & 0xFF), (int)(v & 0xFF));
        }
        finally { Marshal.FreeHGlobal(buf); }
    }

    private static global::Avalonia.Media.Imaging.WriteableBitmap Frame(Window w)
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
        return w.CaptureRenderedFrame()!;
    }

    [Fact]
    public Task OverlappingGlowsAddUp() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var surface = new FxSurface { ResolutionScale = 1.0 };
        var w = new Window { Width = 240, Height = 120, Background = Brushes.Black, Content = surface };
        // Two opaque grey discs, overlapping in the middle. Source-over would leave the overlap at
        // 100; additive must reach 200. Disc A spans x 20..140, disc B 100..220.
        surface.PaintSurface += (_, e) =>
        {
            using var p = FxSprites.AdditivePaint();
            p.Color = new SKColor(100, 100, 100, 255);
            e.Canvas.DrawCircle(80, 60, 60, p);
            e.Canvas.DrawCircle(160, 60, 60, p);
        };
        w.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            surface.Redraw();
            var bmp = Frame(w);
            var a = Px(bmp, 50, 60);
            var b = Px(bmp, 190, 60);
            var both = Px(bmp, 120, 60);
            Assert.InRange(a.R, 95, 105);
            Assert.InRange(b.R, 95, 105);
            Assert.True(both.R > a.R + 80 && both.R > b.R + 80, $"overlap {both} is not the sum of {a} and {b}");
            Assert.InRange(both.R, 190, 210);
        }
        finally { w.Close(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task SoftSpritesBloomWhereTheyOverlap() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var surface = new FxSurface { ResolutionScale = 0.5 };   // half resolution, like the ambient canvas
        var w = new Window { Width = 240, Height = 120, Background = Brushes.Black, Content = surface };
        int mode = 0;   // 1 = left sprite only, 2 = right only, 3 = both
        surface.PaintSurface += (_, e) =>
        {
            using var p = FxSprites.AdditivePaint();
            using var tint = SKColorFilter.CreateBlendMode(new SKColor(255, 105, 180), SKBlendMode.Modulate);
            p.ColorFilter = tint;
            p.Color = SKColors.White.WithAlpha(FxSprites.Alpha(0.5f));
            if ((mode & 1) != 0) FxSprites.DrawSprite(e.Canvas, FxSprites.Dot, p, 100, 60, 120, 120);
            if ((mode & 2) != 0) FxSprites.DrawSprite(e.Canvas, FxSprites.Dot, p, 140, 60, 120, 120);
        };
        w.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            int Mid(int m) { mode = m; surface.Redraw(); return Px(Frame(w), 120, 60).R; }
            int left = Mid(1), right = Mid(2), both = Mid(3);
            Assert.True(left > 20 && right > 20, $"the sprites did not paint (left {left}, right {right})");
            Assert.True(both > Math.Max(left, right) + 15, $"overlap {both} not brighter than either glow ({left}, {right})");
            Assert.InRange(both, left + right - 12, left + right + 12);   // additive: the sum
            Assert.Equal((120, 60), surface.BackingSize);                 // rasters at half the pixels
        }
        finally { w.Close(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task AmbientCanvasPaintsItsLayersThroughTheSurface() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var s = CoreSettings.Current;
        var (motion, perf) = (s.MotionLevel, s.PerformanceMode);
        var old = OsReducedMotion.TestOverride;
        Window? w = null;
        try
        {
            OsReducedMotion.TestOverride = true;
            s.MotionLevel = MotionLevel.Full;
            s.PerformanceMode = false;
            var fx = new AmbientFxCanvas();
            w = new Window { Width = 400, Height = 300, Background = Brushes.Black, Content = fx };
            w.Show();
            w.Activate();
            Dispatcher.UIThread.RunJobs();
            fx.StartLayers(new AmbientFxConfig
            {
                Layers = AmbientFxLayers.FogDrift | AmbientFxLayers.GlowBreath | AmbientFxLayers.Embers,
                Tint = Color.FromRgb(0x40, 0xC0, 0xFF),
            });
            Assert.True(fx.IsTicking, "Full motion, active window: the frame clock must run");
            Assert.Equal((200, 150), fx.Surface.BackingSize);       // AmbientResolution 0.5

            fx.StepForTests(300);                                    // ~10 s of sim
            Assert.True(fx.EmberCount > 0, "no embers rose in ten seconds");

            var bmp = Frame(w);
            var c = Px(bmp, 200, 150);                               // the glow-breath centre
            Assert.True(c.B > 30, $"nothing painted at the glow centre: {c}");

            // Motion Off parks the clock on the next gate check, and the frame stays painted.
            s.MotionLevel = MotionLevel.Off;
            AmbientFxCanvas.Env.RaiseMotionGateChanged();
            Assert.False(fx.IsTicking);
        }
        finally
        {
            w?.Close();
            s.MotionLevel = motion;
            s.PerformanceMode = perf;
            OsReducedMotion.TestOverride = old;
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task OsReducedMotionCapsFullToReducedAndParksLoops() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var s = CoreSettings.Current;
        var (motion, perf) = (s.MotionLevel, s.PerformanceMode);
        var old = OsReducedMotion.TestOverride;
        Window? w = null;
        try
        {
            s.MotionLevel = MotionLevel.Full;
            s.PerformanceMode = false;
            OsReducedMotion.TestOverride = true;
            var fx = new AmbientFxCanvas();
            w = new Window { Width = 300, Height = 200, Content = fx };
            w.Show();
            w.Activate();
            Dispatcher.UIThread.RunJobs();
            fx.StartLayers(AmbientFxLayers.FogDrift);
            Assert.True(fx.IsTicking);

            OsReducedMotion.TestOverride = false;                     // the desktop turns animations off
            AmbientFxCanvas.Env.RaiseMotionGateChanged();
            Assert.Equal(MotionLevel.Reduced, AmbientFxCanvas.Env.Level);
            Assert.False(AmbientFxCanvas.Env.AllowAmbientLoops);
            Assert.True(AmbientFxCanvas.Env.AllowTransitions);       // Reduced still allows hover/press
            Assert.False(fx.IsTicking, "the OS preference must park ambient loops");

            // The fog strip opted into Reduced (EdgeFogReduced) may still tick at Reduced.
            fx.StartLayers(new AmbientFxConfig { Layers = AmbientFxLayers.EdgeFog, EdgeFogReduced = true });
            Assert.True(fx.IsTicking, "EdgeFogReduced is the one layer allowed at Reduced");

            // The OS can only remove motion: Off stays Off.
            s.MotionLevel = MotionLevel.Off;
            Assert.Equal(MotionLevel.Off, AmbientFxCanvas.Env.Level);
            OsReducedMotion.TestOverride = true;
            Assert.Equal(MotionLevel.Off, AmbientFxCanvas.Env.Level);
        }
        finally
        {
            w?.Close();
            s.MotionLevel = motion;
            s.PerformanceMode = perf;
            OsReducedMotion.TestOverride = old;
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task EdgeAndVaultLayersPopulate() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var s = CoreSettings.Current;
        var (motion, perf) = (s.MotionLevel, s.PerformanceMode);
        var old = OsReducedMotion.TestOverride;
        Window? w = null;
        try
        {
            OsReducedMotion.TestOverride = true;
            s.MotionLevel = MotionLevel.Full;
            s.PerformanceMode = false;
            var edge = new AmbientFxCanvas { Height = 56 };
            var vault = new AmbientFxCanvas { Height = 300 };
            w = new Window { Width = 800, Height = 400, Background = Brushes.Black, Content = new StackPanel { Children = { edge, vault } } };
            w.Show();
            w.Activate();
            Dispatcher.UIThread.RunJobs();

            edge.StartLayers(new AmbientFxConfig
            {
                Layers = AmbientFxLayers.EdgeFog | AmbientFxLayers.EdgeDrift,
                EdgeSide = EdgeSide.Top,
                Tint = Color.FromRgb(0x7A, 0xC8, 0xF5),
            });
            // Long side, full budget: 5 big + 5 small puffs and 90 dust specks prefilled at once.
            Assert.Equal(10, edge.FogPuffCount);
            Assert.Equal(EdgeFogMath.DustLong, edge.GrainCount);
            edge.StepForTests(120);
            Assert.InRange(edge.EdgeMoteCount, 1, EdgeDriftMath.MaxPerStrip);
            foreach (var (_, y, size, _) in edge.FogPuffsForTests())
                Assert.True(y + size / 2 <= EdgeFogMath.StripPx + 8 + 0.01, $"a puff left its strip (y {y}, size {size})");

            edge.SetEdgeFog(Color.FromRgb(0xFF, 0x80, 0x60), 1.2, 0);
            Assert.Equal(new SKColor(0xFF, 0x80, 0x60), edge.FogPaint.Tint);
            Assert.Equal(1.2f, edge.FogPaint.Gain, 3);

            vault.StartLayers(AmbientFxLayers.VaultMotes);
            vault.SetVaultZones(new List<VaultZone>
            {
                new(new Rect(40, 40, 200, 120), Color.FromRgb(0xFF, 0xD2, 0x7A), false),
                new(new Rect(400, 40, 200, 120), Color.FromRgb(0x8C, 0xF0, 0xFF), true),
            });
            vault.StepForTests(60);
            Assert.InRange(vault.VaultMoteCount, 20, VaultMoteMath.Cap(MotionLevel.Full));

            var bmp = Frame(w);
            Assert.NotNull(bmp);
        }
        finally
        {
            w?.Close();
            s.MotionLevel = motion;
            s.PerformanceMode = perf;
            OsReducedMotion.TestOverride = old;
        }
        return Task.CompletedTask;
    });
}
