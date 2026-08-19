using System;
using System.Runtime.InteropServices;
using ConditioningControlPanel.Services.Compositor;
using SkiaSharp;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// #960 / #975 - "Brain Drain plays the audio but there is no visual", machine-dependent, no GPU
/// vendor pattern, and NOTHING in the log: the route line said COMPOSITOR, the layer armed, the
/// pump published frames and both first-frame watchdogs stayed quiet.
///
/// <para>The cause was the fourth byte of the capture. <c>BrainDrainCapturePump</c> StretchBlts the
/// desktop into a 32bpp BI_RGB DIB, where GDI defines that byte as UNUSED - whether a device-to-DIB
/// blt leaves 0xFF or 0x00 there is the display driver's business. The pump wrapped those bits as
/// <c>Bgra8888 + SKAlphaType.Opaque</c> on the assumption that the declared alpha type makes Skia
/// force the alpha to 1. It does not (<see cref="ZeroAlphaBuffer_DeclaredOpaque_StillDrawsNothing"/>),
/// so on a machine whose driver leaves 0x00 the entire effect rendered fully transparent while
/// every check in the pipeline passed.</para>
///
/// <para>Why no existing test caught it: <c>BrainDrainMeltFilterTests</c> builds its source with
/// <c>SKCanvas.Clear</c>, which writes alpha 255. Every synthetic source in the suite was opaque;
/// the field's is only opaque by driver convention. These tests use the field's shape instead -
/// a buffer whose alpha bytes are ZERO - and assert the production wrap survives it.</para>
/// </summary>
public class BrainDrainCaptureAlphaTests
{
    private const int W = 96, H = 64;
    private const byte SrcB = 200, SrcG = 120, SrcR = 60;

    /// <summary>A capture buffer in GDI's byte order (B,G,R,X) with <paramref name="alphaByte"/>
    /// left in the unused slot - 0 is what the affected machines produce, 255 what ours do.</summary>
    private static byte[] CaptureBuffer(byte alphaByte)
    {
        var px = new byte[W * H * 4];
        for (int p = 0; p < W * H; p++)
        {
            px[p * 4 + 0] = SrcB;
            px[p * 4 + 1] = SrcG;
            px[p * 4 + 2] = SrcR;
            px[p * 4 + 3] = alphaByte;
        }
        return px;
    }

    private sealed class Pinned : IDisposable
    {
        private GCHandle _h;
        public IntPtr Ptr => _h.AddrOfPinnedObject();
        public Pinned(byte[] buf) => _h = GCHandle.Alloc(buf, GCHandleType.Pinned);
        public void Dispose() { if (_h.IsAllocated) _h.Free(); }
    }

    /// <summary>Centre pixel of a run, read back UNPREMULTIPLIED as B,G,R,A.</summary>
    private static (byte B, byte G, byte R, byte A) Centre(SKImage img)
    {
        var info = new SKImageInfo(img.Width, img.Height, SKColorType.Bgra8888, SKAlphaType.Unpremul);
        var o = new byte[info.Width * info.Height * 4];
        using var pin = new Pinned(o);
        Assert.True(img.ReadPixels(info, pin.Ptr, info.Width * 4, 0, 0));
        int c = ((info.Height / 2) * info.Width + info.Width / 2) * 4;
        return (o[c], o[c + 1], o[c + 2], o[c + 3]);
    }

    /// <summary>One capture tick, replicating <c>BrainDrainCapturePump.CaptureOnce</c>'s draw:
    /// wrap the DIB bits, blur (and optionally melt-warp), publish. <paramref name="productionWrap"/>
    /// false reproduces the pre-fix Bgra8888 wrap with no colour filter.</summary>
    private static SKImage Tick(byte[] buffer, bool melt, bool productionWrap)
    {
        const int downscale = 4, intensity = 50;
        float sigma = (float)(intensity * BrainDrainLayer.RadiusScale / downscale / 3.0);
        float amplitude = (float)((1.0 + intensity * 0.045) * 4.0 / downscale);

        using var pin = new Pinned(buffer);
        var type = productionWrap ? BrainDrainCapturePump.CaptureColorType : SKColorType.Bgra8888;
        using var raw = SKImage.FromPixels(new SKImageInfo(W, H, type, SKAlphaType.Opaque), pin.Ptr, W * 4);
        Assert.NotNull(raw);

        using var surface = SKSurface.Create(new SKImageInfo(W, H, SKColorType.Bgra8888, SKAlphaType.Premul));
        using var blurFilter = sigma > 0.05f ? SKImageFilter.CreateBlur(sigma, sigma) : null;
        using var paint = new SKPaint
        {
            ImageFilter = blurFilter,
            ColorFilter = productionWrap ? BrainDrainCapturePump.SwapRedBlue : null,
        };

        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);

        SKImageFilter? meltFilter = null;
        SKShader? drift = null;
        SKImageFilter? noiseFilter = null;
        SKPaint? noisePaint = null;
        SKImage? tile = null;
        if (melt)
        {
            // Turbulence tile + displacement, as the pump composes it. Only the presence of the
            // warp matters here; its tuning is BrainDrainMeltFilterTests' job.
            float freq = 12f / 256f;
            using var noise = SKShader.CreatePerlinNoiseTurbulence(freq, freq * 0.65f, 2, 7f, new SKSizeI(256, 256));
            using var tileSurface = SKSurface.Create(new SKImageInfo(256, 256, SKColorType.Bgra8888, SKAlphaType.Premul));
            using (var np = new SKPaint { Shader = noise })
            {
                tileSurface.Canvas.Clear(SKColors.Transparent);
                tileSurface.Canvas.DrawRect(0, 0, 256, 256, np);
            }
            tile = tileSurface.Snapshot();
            drift = SKShader.CreateImage(tile, SKShaderTileMode.Repeat, SKShaderTileMode.Repeat,
                                         SKMatrix.CreateScale(3f, 3f));
            noisePaint = new SKPaint { FilterQuality = SKFilterQuality.Low, Shader = drift };
            noiseFilter = SKImageFilter.CreatePaint(noisePaint);
            meltFilter = SKImageFilter.CreateDisplacementMapEffect(
                SKColorChannel.R, SKColorChannel.G, amplitude, noiseFilter, blurFilter);
            paint.ImageFilter = meltFilter;
        }

        canvas.DrawImage(raw, 0, 0, paint);
        var published = surface.Snapshot();

        paint.ImageFilter = null;
        paint.ColorFilter = null;   // shared static: drop the reference, never dispose it
        meltFilter?.Dispose();
        noiseFilter?.Dispose();
        noisePaint?.Dispose();
        drift?.Dispose();
        tile?.Dispose();
        return published;
    }

    // ---------------------------------------------------------------- the trap itself

    [Fact]
    public void ZeroAlphaBuffer_DeclaredOpaque_StillDrawsNothing()
    {
        // THE bug, pinned. SKAlphaType.Opaque is a claim about the buffer, not an instruction to
        // Skia: a Bgra8888 wrap whose alpha bytes are 0 composites to nothing. If a future Skia
        // bump makes this assertion fail, the trap is gone - but do NOT go back to Bgra8888 on the
        // strength of that, because the byte is still undefined at the GDI end.
        using var img = Tick(CaptureBuffer(alphaByte: 0), melt: false, productionWrap: false);
        var (b, g, r, a) = Centre(img);
        Assert.Equal(0, a);
        Assert.Equal((0, 0, 0), (b, g, r));
    }

    [Fact]
    public void ZeroAlphaBuffer_ProductionWrap_IsOpaqueAndKeepsItsColours()
    {
        using var img = Tick(CaptureBuffer(alphaByte: 0), melt: false, productionWrap: true);
        var (b, g, r, a) = Centre(img);
        Assert.Equal(255, a);
        Assert.InRange(b, SrcB - 2, SrcB + 2);
        Assert.InRange(g, SrcG - 2, SrcG + 2);
        Assert.InRange(r, SrcR - 2, SrcR + 2);
    }

    [Fact]
    public void ZeroAlphaBuffer_ProductionWrap_SurvivesTheMeltVariantToo()
    {
        // Both reporters had melt ON, and the melt chain routes the capture through an extra
        // displacement node - the one place a "force the alpha inside a filter" fix silently
        // degraded to opaque BLACK. Ignoring the byte at the wrap has no such failure mode.
        using var img = Tick(CaptureBuffer(alphaByte: 0), melt: true, productionWrap: true);
        var (b, g, r, a) = Centre(img);
        Assert.Equal(255, a);
        Assert.InRange(b, SrcB - 8, SrcB + 8);
        Assert.InRange(g, SrcG - 8, SrcG + 8);
        Assert.InRange(r, SrcR - 8, SrcR + 8);
    }

    [Fact]
    public void HealthyAlphaBuffer_ProductionWrap_IsUnchanged()
    {
        // The regression guard: the machines that always worked must render identically.
        using var fixedWrap = Tick(CaptureBuffer(alphaByte: 255), melt: false, productionWrap: true);
        using var oldWrap = Tick(CaptureBuffer(alphaByte: 255), melt: false, productionWrap: false);
        Assert.Equal(Centre(oldWrap), Centre(fixedWrap));
        Assert.Equal(255, Centre(fixedWrap).A);
    }

    // ---------------------------------------------------------------- the diagnostic sample

    [Fact]
    public void SampleFrame_ReportsTheRawAlphaByteTheDriverLeft()
    {
        var zero = CaptureBuffer(alphaByte: 0);
        using (var pin = new Pinned(zero))
        {
            var s = BrainDrainCapturePump.SampleFrame(pin.Ptr, W, H, W * 4);
            Assert.Equal(0, s.MaxAlpha);          // the line that names an affected machine
            Assert.Equal(0.0, s.MeanAlpha, 3);
            Assert.True(s.MeanLuma > 1.0);        // ...while the picture itself is fine
        }

        var opaque = CaptureBuffer(alphaByte: 255);
        using (var pin = new Pinned(opaque))
        {
            var s = BrainDrainCapturePump.SampleFrame(pin.Ptr, W, H, W * 4);
            Assert.Equal(255, s.MinAlpha);
            Assert.Equal(255.0, s.MeanAlpha, 3);
        }
    }

    [Fact]
    public void SampleFrame_BlackCaptureIsDistinguishableFromAnInvisibleOne()
    {
        // MPO / HDR read-back returns a BLACK desktop with a perfectly good alpha byte. That is a
        // different failure with a different symptom (a dark scrim, not nothing), and the two have
        // to be separable from one log line.
        var black = new byte[W * H * 4];
        for (int p = 0; p < W * H; p++) black[p * 4 + 3] = 255;
        using var pin = new Pinned(black);
        var s = BrainDrainCapturePump.SampleFrame(pin.Ptr, W, H, W * 4);
        Assert.Equal(255, s.MinAlpha);
        Assert.Equal(0.0, s.MeanLuma, 3);
    }

    [Fact]
    public void SampleFrame_RejectsNonsenseGeometryRatherThanReadingWildMemory()
    {
        Assert.Equal((0.0, 0.0, 0, 0), BrainDrainCapturePump.SampleFrame(IntPtr.Zero, W, H, W * 4));
        var buf = CaptureBuffer(255);
        using var pin = new Pinned(buf);
        Assert.Equal((0.0, 0.0, 0, 0), BrainDrainCapturePump.SampleFrame(pin.Ptr, 0, H, W * 4));
        Assert.Equal((0.0, 0.0, 0, 0), BrainDrainCapturePump.SampleFrame(pin.Ptr, W, -1, W * 4));
    }
}
