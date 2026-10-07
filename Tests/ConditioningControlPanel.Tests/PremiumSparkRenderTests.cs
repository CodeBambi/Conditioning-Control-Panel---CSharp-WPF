using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ConditioningControlPanel.Controls.Header;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Polish 12, BADGE lane: the real PremiumSpark realised offscreen in each tier. It must hold a
/// 34 px layout footprint (the header row's height) with its bleed, draw Free at 40%, and paint
/// visibly different cards per tier. Set <c>CCP_PREMIUM_PNG_DIR</c> to write badge-FREE/BASIC/PRIME
/// renders (on the header's dark background, 4x).
/// </summary>
[Collection(CompanionWpfRenderCollection.Name)]
public class PremiumSparkRenderTests
{
    private static (PremiumSpark spark, StackPanel host) Realize(SparkTier tier)
    {
        var spark = new PremiumSpark { Pinned = true, Margin = new Thickness(0, -4, 12, -4) };
        spark.Apply(tier, SparkMotion.Off, particlesAllowed: false);
        var host = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Background = new SolidColorBrush(Color.FromRgb(0x12, 0x10, 0x1F)),
        };
        host.Children.Add(spark);
        host.Measure(new Size(400, 200));
        host.Arrange(new Rect(host.DesiredSize));
        host.UpdateLayout();
        return (spark, host);
    }

    [Theory]
    [InlineData(SparkTier.Free)]
    [InlineData(SparkTier.Basic)]
    [InlineData(SparkTier.Prime)]
    public void HoldsTheHeaderRowHeight(SparkTier tier) => WpfRenderHarness.OnStaThread(() =>
    {
        var (spark, host) = Realize(tier);
        Assert.Equal(34, host.DesiredSize.Height, 1);
        Assert.Equal(54 + 12, host.DesiredSize.Width, 1);
        Assert.Equal(tier, spark.Tier);
        Assert.False(spark.LoopsRunning && tier == SparkTier.Free && spark.Motion != SparkMotion.Off);
    });

    [Fact]
    public void FreeIsDimmedAndTheTiersDiffer() => WpfRenderHarness.OnStaThread(() =>
    {
        MaybeSave(Realize(SparkTier.Free).spark, "badge-FREE.png");
        MaybeSave(Realize(SparkTier.Basic).spark, "badge-BASIC.png");
        MaybeSave(Realize(SparkTier.Prime).spark, "badge-PRIME.png");

        var free = Average(Render(Realize(SparkTier.Free).spark));
        var basic = Average(Render(Realize(SparkTier.Basic).spark));
        var prime = Average(Render(Realize(SparkTier.Prime).spark));

        // Gold reads warm (red over blue); Prime reads cold (blue over red); Free is the darkest.
        Assert.True(basic.r > basic.b + 10, $"basic not gold: {basic}");
        Assert.True(prime.b > prime.r + 10, $"prime not cyan: {prime}");
        Assert.True(Lum(free) < Lum(basic) && Lum(free) < Lum(prime), $"free not dimmed: {free} {basic} {prime}");

    });

    [Fact]
    public void StatesSwapWithoutRebuilding() => WpfRenderHarness.OnStaThread(() =>
    {
        var (spark, _) = Realize(SparkTier.Free);
        spark.Apply(SparkTier.Prime, SparkMotion.Off, false);
        Assert.Equal(SparkTier.Prime, spark.Tier);
        Assert.Equal(1.0, spark.Root.Opacity);
        spark.Apply(SparkTier.Free, SparkMotion.Off, false);
        Assert.Equal(0.40, spark.Root.Opacity, 3);
        Assert.Equal(0, spark.Halo.Opacity);
    });

    // ---- round 2: the particles ------------------------------------------------------------

    /// <summary>A spark at Full on a seeded field, warmed up so the frame is mid-stream.</summary>
    private static (PremiumSpark spark, StackPanel host) RealizeLive(SparkTier tier, int seed = 21)
    {
        var (spark, host) = Realize(tier);
        spark.Apply(tier, SparkMotion.Full, particlesAllowed: true);
        spark.Reseed(seed);
        spark.Prewarm(1.5);
        spark.Advance(1.0 / 30);
        host.UpdateLayout();
        return (spark, host);
    }

    private static int VisibleMotes(PremiumSpark spark) =>
        spark.FxLayer.Children.OfType<UIElement>().Count(e => e.Visibility == Visibility.Visible);

    [Fact]
    public void PrimeAndBasicDrawManyParticlesAndFreeNone() => WpfRenderHarness.OnStaThread(() =>
    {
        var prime = RealizeLive(SparkTier.Prime).spark;
        var basic = RealizeLive(SparkTier.Basic).spark;
        var free = RealizeLive(SparkTier.Free).spark;
        Assert.True(VisibleMotes(prime) >= 14, $"prime motes {VisibleMotes(prime)}");
        Assert.True(VisibleMotes(basic) >= 9, $"basic motes {VisibleMotes(basic)}");
        Assert.Equal(0, VisibleMotes(free));
        Assert.Equal(0.40, free.Root.Opacity, 3);

        // The overlay takes no layout space: the control still holds the header row.
        Assert.Equal(54, prime.ActualWidth, 1);
        Assert.Equal(42, prime.ActualHeight, 1);
        Assert.False(prime.FxLayer.IsHitTestVisible);

        // Motes spill past the card: some pixels outside the 54 x 42 box are lit.
        Assert.True(LitOutsideCard(Render(prime)) > LitOutsideCard(Render(RealizeStill(SparkTier.Prime))),
            "the particles should spill outside the card");
    });

    [Fact]
    public void MotionOffDrawsTheStaticLitCard() => WpfRenderHarness.OnStaThread(() =>
    {
        var (spark, host) = RealizeLive(SparkTier.Prime);
        spark.Apply(SparkTier.Prime, SparkMotion.Off, particlesAllowed: true);
        spark.Advance(0.5);
        host.UpdateLayout();
        Assert.True(VisibleMotes(spark) == 0, $"visible {VisibleMotes(spark)} alive {spark.Field.Alive.Count} kinds {string.Join(",", spark.Field.Alive.Select(m => m.Kind))} children {spark.FxLayer.Children.Count}");
        Assert.Equal(PremiumSparkRules.HaloStill, spark.Halo.Opacity, 3);
    });

    [Fact]
    public void RoundTwoRendersAndFrameStrips() => WpfRenderHarness.OnStaThread(() =>
    {
        var dir = Environment.GetEnvironmentVariable("CCP_PREMIUM_PNG_DIR");
        foreach (var tier in new[] { SparkTier.Prime, SparkTier.Basic, SparkTier.Free })
        {
            var name = tier.ToString().ToUpperInvariant();
            var (spark, host) = RealizeLive(tier);
            var frames = new List<BitmapSource>();
            for (int f = 0; f < 6; f++)
            {
                if (f > 0)
                    for (int i = 0; i < 12; i++) spark.Advance(1.0 / 30);   // 0.4 s between frames
                host.UpdateLayout();
                frames.Add(Render(spark));
            }
            if (tier != SparkTier.Free)
                Assert.True(Differs(frames[0], frames[3]), $"{name} did not move");
            else
                Assert.False(Differs(frames[0], frames[3]), "free must stay still");

            if (string.IsNullOrEmpty(dir)) continue;
            Save(frames[0], Path.Combine(dir, $"r2-badge-{name}.png"));
            Save(Strip(frames), Path.Combine(dir, $"r2-badge-{name.ToLowerInvariant()}-strip.png"));
        }
    });

    private static PremiumSpark RealizeStill(SparkTier tier)
    {
        var (spark, host) = Realize(tier);
        host.UpdateLayout();
        return spark;
    }

    private static int LitOutsideCard(BitmapSource bmp)
    {
        var stride = bmp.PixelWidth * 4;
        var px = new byte[stride * bmp.PixelHeight];
        bmp.CopyPixels(px, stride, 0);
        int lit = 0;
        for (int y = 0; y < bmp.PixelHeight; y++)
        for (int x = 0; x < bmp.PixelWidth; x++)
        {
            double cx = x / (double)Scale - Pad, cy = y / (double)Scale - Pad;
            // Outside the card plus a 6 px margin (die-cut, paper shadow and the halo's core).
            if (cx > -6 && cx < 60 && cy > -6 && cy < 48) continue;
            int i = y * stride + x * 4;
            if (px[i] + px[i + 1] + px[i + 2] > 3 * 0x60) lit++;
        }
        return lit;
    }

    private static bool Differs(BitmapSource a, BitmapSource b)
    {
        var stride = a.PixelWidth * 4;
        var pa = new byte[stride * a.PixelHeight];
        var pb = new byte[stride * b.PixelHeight];
        a.CopyPixels(pa, stride, 0);
        b.CopyPixels(pb, stride, 0);
        int diff = 0;
        for (int i = 0; i < pa.Length; i++) if (Math.Abs(pa[i] - pb[i]) > 24) diff++;
        return diff > 200;
    }

    private static BitmapSource Strip(List<BitmapSource> frames)
    {
        int w = frames[0].PixelWidth, h = frames[0].PixelHeight, gap = 8;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x08, 0x07, 0x10)), null,
                new Rect(0, 0, frames.Count * (w + gap) - gap, h));
            for (int i = 0; i < frames.Count; i++)
                dc.DrawImage(frames[i], new Rect(i * (w + gap), 0, w, h));
        }
        var bmp = new RenderTargetBitmap(frames.Count * (w + gap) - gap, h, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(visual);
        return bmp;
    }

    private static void Save(BitmapSource bmp, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var fs = File.Create(path);
        enc.Save(fs);
    }

    // ---- pixels ----------------------------------------------------------------------------

    private const int Scale = 4;

    /// <summary>How far past the card a render reaches: the halo, the shadow and spilled motes.</summary>
    private const double Pad = 20;

    private static RenderTargetBitmap Render(PremiumSpark spark)
    {
        // Draw past the card: the halo, the shadow and the spilled motes leave the 54 x 42 box.
        const double pad = Pad;
        var w = (int)((spark.ActualWidth + pad * 2) * Scale);
        var h = (int)((spark.ActualHeight + pad * 2) * Scale);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.PushTransform(new ScaleTransform(Scale, Scale));
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x12, 0x10, 0x1F)), null,
                new Rect(0, 0, spark.ActualWidth + pad * 2, spark.ActualHeight + pad * 2));
            var brush = new VisualBrush(spark)
            {
                Stretch = Stretch.None,
                ViewboxUnits = BrushMappingMode.Absolute,
                Viewbox = new Rect(-pad, -pad, spark.ActualWidth + pad * 2, spark.ActualHeight + pad * 2),
                ViewportUnits = BrushMappingMode.Absolute,
                Viewport = new Rect(0, 0, spark.ActualWidth + pad * 2, spark.ActualHeight + pad * 2),
            };
            dc.DrawRectangle(brush, null, new Rect(0, 0, spark.ActualWidth + pad * 2, spark.ActualHeight + pad * 2));
            dc.Pop();
        }
        var bmp = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(visual);
        return bmp;
    }

    private static (double r, double g, double b) Average(BitmapSource bmp)
    {
        var stride = bmp.PixelWidth * 4;
        var px = new byte[stride * bmp.PixelHeight];
        bmp.CopyPixels(px, stride, 0);
        double r = 0, g = 0, b = 0;
        int n = px.Length / 4;
        n = 0;
        for (int i = 0; i < px.Length; i += 4)
        {
            // Only the card: skip the header background and anything within a shade of it.
            if (Math.Abs(px[i] - 0x1F) + Math.Abs(px[i + 1] - 0x10) + Math.Abs(px[i + 2] - 0x12) < 24) continue;
            b += px[i]; g += px[i + 1]; r += px[i + 2]; n++;
        }
        if (n == 0) return (0, 0, 0);
        return (r / n, g / n, b / n);
    }

    private static double Lum((double r, double g, double b) c) => 0.2126 * c.r + 0.7152 * c.g + 0.0722 * c.b;

    private static void MaybeSave(PremiumSpark spark, string name)
    {
        var dir = Environment.GetEnvironmentVariable("CCP_PREMIUM_PNG_DIR");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(Render(spark)));
        using var fs = File.Create(Path.Combine(dir, name));
        enc.Save(fs);
    }
}
