using System;
using System.Diagnostics;
using SkiaSharp;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Replicates the melt filter chain from <c>BrainDrainLayer.CapturePass</c> (Perlin turbulence ->
/// paint filter -> displacement map over the blur) on an offscreen surface. The layer itself owns
/// GDI screen capture and cannot be driven headless, so the chain is rebuilt here verbatim -
/// keep the constants below in sync with the layer.
/// The load-bearing assertions are the two GUTTER ones: displacement pulls samples from outside
/// the source and those bleed transparent, so the layer draws scaled up about the centre to push
/// that band off-frame. A gaussian has its own soft edge which the plain-blur path has always
/// shown, so melt is measured against that baseline rather than against zero.
/// </summary>
public class BrainDrainMeltFilterTests
{
    private const float NoiseCellFraction = 0.20f;
    private const float NoiseCellAspect = 0.65f;
    private const int NoiseOctaves = 2;
    private const float NoiseSeed = 7f;
    private const int NoiseTilePx = 256;
    private const float NoiseCellsPerTile = 12f;
    private const float MeltDriftPxPerSec = 6f;
    private const float MeltSwayPx = 2.5f;
    private const float MeltSwayRate = 0.55f;

    private static float Amplitude(int intensity, int downscale) =>
        (float)((2.0 + Math.Clamp(intensity, 0, 200) * 0.12) * 4.0 / Math.Max(1, downscale));

    private static SKImage NoiseTile()
    {
        float freq = NoiseCellsPerTile / NoiseTilePx;
        using var noise = SKShader.CreatePerlinNoiseTurbulence(
            freq, freq * NoiseCellAspect, NoiseOctaves, NoiseSeed, new SKSizeI(NoiseTilePx, NoiseTilePx));
        using var surface = SKSurface.Create(
            new SKImageInfo(NoiseTilePx, NoiseTilePx, SKColorType.Bgra8888, SKAlphaType.Premul));
        using var paint = new SKPaint { Shader = noise };
        surface.Canvas.Clear(SKColors.Transparent);
        surface.Canvas.DrawRect(0, 0, NoiseTilePx, NoiseTilePx, paint);
        return surface.Snapshot();
    }

    private sealed class PassResult : IDisposable
    {
        public SKBitmap Frame = null!;
        public double MsPerTick;
        public double MinMsPerTick;
        public int TransparentPx;
        public void Dispose() => Frame.Dispose();
    }

    /// <summary>Runs <paramref name="ticks"/> capture passes and returns the last frame, the mean
    /// ms/tick and the count of non-opaque pixels. <paramref name="warp"/> false = the plain-blur
    /// path exactly as it is today; <paramref name="blur"/> false forces sigma to 0.</summary>
    private static PassResult RunPass(int w, int h, int intensity, int downscale, int ticks,
                                      bool warp, bool blur = true)
    {
        float amplitude = Amplitude(intensity, downscale);
        float sigma = blur ? (float)(intensity * 0.4 / downscale / 3.0) : 0f;
        float coverage = Math.Max(8f, Math.Min(w, h) * NoiseCellFraction) * NoiseCellsPerTile;
        float tierScale = 4f / downscale;
        float gutter = amplitude * 0.5f + 2f;

        // Fully opaque source: any transparency in the output came from a filter's edge behaviour.
        using var src = new SKBitmap(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Opaque));
        using (var srcCanvas = new SKCanvas(src))
        {
            srcCanvas.Clear(SKColors.DarkSlateBlue);
            using var p = new SKPaint { Color = SKColors.Orange };
            for (int i = 0; i < w; i += 17) srcCanvas.DrawRect(i, 0, 8, h, p);
        }
        using var raw = SKImage.FromBitmap(src);

        using var surface = SKSurface.Create(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul));
        using var blurFilter = sigma > 0.05f ? SKImageFilter.CreateBlur(sigma, sigma) : null;
        using var blurPaint = new SKPaint { ImageFilter = blurFilter };
        using var tile = NoiseTile();
        using var noisePaint = new SKPaint { FilterQuality = SKFilterQuality.Low };
        using var meltPaint = new SKPaint();

        var canvas = surface.Canvas;
        var sw = new Stopwatch();
        double minTicks = double.MaxValue;
        float t = 0f;
        for (int i = 0; i < ticks; i++)
        {
            t += 1f / 30f;
            long before = sw.ElapsedTicks;
            sw.Start();
            if (warp)
            {
                float dx = MeltSwayPx * tierScale * MathF.Sin(t * MeltSwayRate);
                float dy = (MeltDriftPxPerSec * tierScale * t) % coverage;
                float s = coverage / NoiseTilePx;
                using var drift = SKShader.CreateImage(tile, SKShaderTileMode.Repeat, SKShaderTileMode.Repeat,
                                                       SKMatrix.CreateScaleTranslation(s, s, dx, dy));
                noisePaint.Shader = drift;
                using var noiseFilter = SKImageFilter.CreatePaint(noisePaint);
                using var meltFilter = SKImageFilter.CreateDisplacementMapEffect(
                    SKColorChannel.R, SKColorChannel.G, amplitude, noiseFilter, blurFilter);
                meltPaint.ImageFilter = meltFilter;

                canvas.Clear(SKColors.Transparent);
                canvas.Save();
                canvas.Translate(w * 0.5f, h * 0.5f);
                canvas.Scale((w + 2f * gutter) / w, (h + 2f * gutter) / h);
                canvas.Translate(-w * 0.5f, -h * 0.5f);
                canvas.DrawImage(raw, 0, 0, meltPaint);
                canvas.Restore();
                canvas.Flush();
                meltPaint.ImageFilter = null;
                noisePaint.Shader = null;
            }
            else
            {
                canvas.Clear(SKColors.Transparent);
                canvas.DrawImage(raw, 0, 0, blurFilter != null ? blurPaint : null);
                canvas.Flush();
            }
            sw.Stop();
            minTicks = Math.Min(minTicks, sw.ElapsedTicks - before);
        }

        var frame = new SKBitmap(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul));
        Assert.True(surface.ReadPixels(frame.Info, frame.GetPixels(), frame.RowBytes, 0, 0));
        int transparent = 0;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                if (frame.GetPixel(x, y).Alpha < 250) transparent++;

        return new PassResult
        {
            Frame = frame,
            MsPerTick = sw.Elapsed.TotalMilliseconds / ticks,
            MinMsPerTick = minTicks * 1000.0 / Stopwatch.Frequency,
            TransparentPx = transparent,
        };
    }

    /// <summary>With the blur out of the way, the centre scale-up must fully swallow the
    /// displacement gutter - a displacement map offsets by at most +/- amplitude/2.</summary>
    [Theory]
    [InlineData(480, 270, 200, 4)] // 1080p, downscale 4, amplitude ceiling
    [InlineData(480, 270, 100, 4)]
    [InlineData(240, 135, 200, 8)] // Performance tier
    public void Warp_LeavesNoDisplacementGutter(int w, int h, int intensity, int downscale)
    {
        using var melt = RunPass(w, h, intensity, downscale, 20, warp: true, blur: false);
        Assert.True(melt.TransparentPx == 0,
            $"{melt.TransparentPx} px of displacement gutter leaked into the {w}x{h} frame " +
            $"(intensity {intensity}, downscale {downscale}, amplitude {Amplitude(intensity, downscale):F1}px); " +
            "the centre scale-up no longer covers amplitude/2.");
    }

    /// <summary>Melt must not add any transparency the plain-blur path did not already have
    /// (a gaussian fades out over ~3 sigma at the frame edge; that is pre-existing behaviour).</summary>
    [Theory]
    [InlineData(480, 270, 100, 4)]
    [InlineData(480, 270, 30, 4)]
    [InlineData(240, 135, 100, 8)]
    public void Melt_AddsNoGutterOverThePlainBlurBaseline(int w, int h, int intensity, int downscale)
    {
        using var plain = RunPass(w, h, intensity, downscale, 20, warp: false);
        using var melt = RunPass(w, h, intensity, downscale, 20, warp: true);
        Assert.True(melt.TransparentPx <= plain.TransparentPx,
            $"melt left {melt.TransparentPx} non-opaque px vs {plain.TransparentPx} for the same plain blur " +
            $"({w}x{h}, intensity {intensity}, downscale {downscale}).");
    }

    /// <summary>Same source, different phase => different pixels. Guards against the displacement
    /// chain silently degrading into a plain blur (which is what the pre-melt flag did).</summary>
    [Fact]
    public void Melt_ActuallyWarps_AndAnimates()
    {
        using var plain = RunPass(240, 135, 100, 4, 1, warp: false);
        using var early = RunPass(240, 135, 100, 4, 1, warp: true);
        using var late = RunPass(240, 135, 100, 4, 40, warp: true);

        Assert.True(Differing(plain.Frame, early.Frame) > 240 * 135 / 10, "melt is not warping at all.");
        Assert.True(Differing(early.Frame, late.Frame) > 240 * 135 / 10, "the melt warp is not animating.");
    }

    /// <summary>Guards the reason the turbulence is pre-rasterised into a tile instead of being
    /// evaluated live: Skia's raster Perlin shader measured 24ms/tick at 480x270 (a whole core at
    /// the 30fps capture cadence), the tile fetch ~1.6ms. Asserts on the FASTEST of 60 ticks so a
    /// busy or throttled CI box cannot flake it, against a bound ~7x the observed cost.</summary>
    [Theory]
    [InlineData(480, 270, 100, 4)]  // 1080p, default downscale
    [InlineData(240, 135, 100, 8)]  // Performance tier
    public void Melt_TickStaysCheapOnTheSmallSurface(int w, int h, int intensity, int downscale)
    {
        using var warm = RunPass(w, h, intensity, downscale, 10, warp: true);
        using var melt = RunPass(w, h, intensity, downscale, 60, warp: true);
        Assert.True(melt.MinMsPerTick < 12.0,
            $"melt tick at {w}x{h} took {melt.MinMsPerTick:F2}ms (mean {melt.MsPerTick:F2}ms) - " +
            "the displacement chain has regressed to evaluating noise per pixel.");
    }

    private static int Differing(SKBitmap a, SKBitmap b)
    {
        int n = 0;
        for (int y = 0; y < a.Height; y++)
            for (int x = 0; x < a.Width; x++)
                if (a.GetPixel(x, y) != b.GetPixel(x, y)) n++;
        return n;
    }
}
