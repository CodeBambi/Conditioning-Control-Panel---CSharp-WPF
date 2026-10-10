using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using ConditioningControlPanel.Controls;
using ConditioningControlPanel.Controls.NavRail;
using ConditioningControlPanel.Models;
using SkiaSharp;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Nav polish wave 11, lane FOG: the section edge becomes a drifting fog of light. Pins the gate
/// (Off none, Reduced half fog, Full fog plus embers), the budget (under 60 live elements across
/// the four strips), the band (every puff stays inside its 56 px strip and fades in and out), the
/// thinner static band under the fog, and a frame cost for the whole ring.
///
/// <para>Set <c>CCP_FOG_RENDER_DIR</c> to a folder to also write the review PNGs (corner frames
/// one second apart and a full window for Social and Studio) and the bench line there.</para>
/// </summary>
public class FogPolish11Tests
{
    private static readonly Color Lilac = Color.FromRgb(0xB7, 0x9C, 0xFF);
    private static readonly Color Sky = Color.FromRgb(0x5F, 0xB0, 0xFF);
    private static readonly Color Pink = Color.FromRgb(0xFF, 0x69, 0xB4);
    private static readonly Color Sage = Color.FromRgb(0xA8, 0xD8, 0xA0);
    private static readonly Color VioletBlue = Color.FromRgb(0x7A, 0x86, 0xFF);

    private const double W = 1661, H = 1002;

    private readonly ITestOutputHelper _out;
    public FogPolish11Tests(ITestOutputHelper output) => _out = output;

    [Fact]
    public void TheGateIsNoneReducedOrFull()
    {
        Assert.Equal(EdgeFogMode.None, EdgeParticles.ModeFor(MotionLevel.Off, true));
        Assert.Equal(EdgeFogMode.None, EdgeParticles.ModeFor(MotionLevel.Full, false));
        Assert.Equal(EdgeFogMode.None, EdgeParticles.ModeFor(MotionLevel.Reduced, false));
        Assert.Equal(EdgeFogMode.Reduced, EdgeParticles.ModeFor(MotionLevel.Reduced, true));
        Assert.Equal(EdgeFogMode.Full, EdgeParticles.ModeFor(MotionLevel.Full, true));

        Assert.Equal(AmbientFxLayers.EdgeFog | AmbientFxLayers.EdgeDrift, EdgeParticles.LayersFor(EdgeFogMode.Full));
        Assert.Equal(AmbientFxLayers.EdgeFog, EdgeParticles.LayersFor(EdgeFogMode.Reduced));
        Assert.Equal(AmbientFxLayers.None, EdgeParticles.LayersFor(EdgeFogMode.None));
        Assert.Equal(1 << 7, (int)AmbientFxLayers.EdgeFog);
    }

    [Fact]
    public void ReducedMountsTheFogAloneAtHalfAndRebuildsOnALevelChange()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var edge = new EdgeParticles { MotionOverride = MotionLevel.Reduced, TierAllowsParticlesOverride = true };
            edge.Mount(Sky);
            Assert.Equal(EdgeFogMode.Reduced, edge.Mode);
            Assert.True(edge.FogLive);
            Assert.Equal(4, edge.Strips.Count);
            Assert.All(edge.Strips, s => Assert.Equal(AmbientFxLayers.EdgeFog, s.Layers));
            Assert.All(edge.Strips, s => Assert.Equal(Sky, s.Tint));

            var reduced = edge.Strips[0];
            edge.MotionOverride = MotionLevel.Full;
            edge.Mount(Pink, 250);
            Assert.Equal(EdgeFogMode.Full, edge.Mode);
            Assert.NotSame(reduced, edge.Strips[0]);
            Assert.All(edge.Strips, s => Assert.Equal(AmbientFxLayers.EdgeFog | AmbientFxLayers.EdgeDrift, s.Layers));

            edge.MotionOverride = MotionLevel.Off;
            edge.Refresh();
            Assert.False(edge.FogLive);
            Assert.Empty(edge.Strips);
        });
    }

    [Fact]
    public void TheWholeRingStaysUnderSixtyLiveElements()
    {
        int fog = 0;
        foreach (bool longSide in new[] { true, true, false, false })
            fog += EdgeFogMath.Target(EdgeFogMath.FullCount(true, longSide), 60, false)
                 + EdgeFogMath.Target(EdgeFogMath.FullCount(false, longSide), 60, false);
        Assert.Equal(34, fog);
        Assert.True(fog + 4 * EdgeDriftMath.MaxPerStrip < 60);

        // Reduced: half the puffs, never below one per layer.
        int reduced = 0;
        foreach (bool longSide in new[] { true, true, false, false })
            reduced += EdgeFogMath.Target(EdgeFogMath.FullCount(true, longSide), 60, true)
                     + EdgeFogMath.Target(EdgeFogMath.FullCount(false, longSide), 60, true);
        Assert.InRange(reduced, 15, 19);
        Assert.Equal(0, EdgeFogMath.Target(4, 0, false));
        Assert.Equal(3, EdgeFogMath.Target(5, 24, false));   // Balanced keeps the lean share
    }

    [Fact]
    public void ThePuffEnvelopeFadesInHoldsAndFadesOut()
    {
        Assert.Equal(0, EdgeFogMath.Envelope(0, 6));
        Assert.Equal(0, EdgeFogMath.Envelope(6, 6));
        Assert.Equal(1, EdgeFogMath.Envelope(3, 6));
        Assert.InRange(EdgeFogMath.Envelope(0.6, 6), 0.05, 0.6);
        Assert.Equal(EdgeFogMath.Envelope(1, 6), EdgeFogMath.Envelope(5, 6), 9);
        // No hue or gain pushes a puff past the small layer's 0.22 ceiling.
        Assert.Equal(EdgeFogMath.SmallAlphaMax, EdgeFogMath.Alpha(0.22, 3, 6, 1.5), 9);
        Assert.InRange(EdgeFogMath.BigAlphaMax, 0.05, 0.22);
        Assert.InRange(EdgeFogMath.BigSizeMaxPx, 18, 60);
        Assert.InRange(EdgeFogMath.SmallSizeMinPx, 18, 60);
        // The deepest puff still clears the strip's inner edge.
        double deep = EdgeFogMath.MaxDepth(EdgeFogMath.BigSizeMaxPx, EdgeFogMath.BreathePxMax);
        Assert.True(deep + EdgeFogMath.BreathePxMax + EdgeFogMath.BigSizeMaxPx / 2 <= EdgeFogMath.StripPx);
    }

    [Fact]
    public void TheStaticBandThinsUnderTheFogAndKeepsItsWeightWithout()
    {
        Assert.Equal(28, SectionEdgeRules.BandDepth(false));
        Assert.Equal(16, SectionEdgeRules.BandDepth(true));
        foreach (var hue in new[] { Lilac, Sky, Pink, Sage, VioletBlue })
        {
            Assert.Equal(SectionEdgeRules.GlowStops(hue), SectionEdgeRules.GlowStops(hue, false));
            var fog = SectionEdgeRules.GlowStops(hue, true);
            Assert.Equal((byte)Math.Round(SectionEdgeRules.GlowAlpha(hue) * 0.7), fog[0].A);
            Assert.True(fog[0].A < SectionEdgeRules.GlowAlpha(hue));
            Assert.Equal(0, fog[2].A);
            Assert.InRange(SectionEdgeRules.FogGain(hue), 0.69, 1.31);
        }
        // Light hues take less fog than VioletBlue, like the band.
        Assert.True(SectionEdgeRules.FogGain(Sage) < SectionEdgeRules.FogGain(VioletBlue));
    }

    [Fact]
    public void TheEdgePainterThinsTheBandFromTheFogState()
    {
        var root = AppRoot();
        var painter = File.ReadAllText(Path.Combine(root, "MainWindow", "MainWindow.SectionEdge.cs"));
        Assert.Contains("SectionEdgeRules.BandDepth(fogLive)", painter, StringComparison.Ordinal);
        Assert.Contains("SectionEdgeParticles?.FogLive", painter, StringComparison.Ordinal);
        var canvas = File.ReadAllText(Path.Combine(root, "Controls", "AmbientFxCanvas.cs"));
        // The Reduced allowance is the fog's alone; every other layer keeps the Full-only gate.
        Assert.Contains("!MotionFx.AllowAmbientLoops && !ReducedFogMayRun()", canvas, StringComparison.Ordinal);
    }

    [Fact]
    public void PuffsDriftBreatheAndStayInsideTheirStrip()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            foreach (var side in new[] { EdgeSide.Top, EdgeSide.Right, EdgeSide.Bottom, EdgeSide.Left })
            {
                var strip = Strip(side, Sky, reduced: false);
                bool horizontal = side is EdgeSide.Top or EdgeSide.Bottom;
                int cap = EdgeFogMath.FullCount(true, horizontal) + EdgeFogMath.FullCount(false, horizontal);
                Assert.Equal(cap, strip.FogPuffCount);   // pre-filled at random ages

                var first = strip.FogPuffsForTests();
                for (int f = 0; f < 600; f++)
                {
                    strip.StepFog(1f / 30f);
                    Assert.InRange(strip.FogPuffCount, 1, cap);
                    foreach (var (x, y, size, _) in strip.FogPuffsForTests())
                    {
                        double depth = side switch
                        {
                            EdgeSide.Top => y,
                            EdgeSide.Bottom => strip.ActualHeight - y,
                            EdgeSide.Left => x,
                            _ => strip.ActualWidth - x,
                        };
                        Assert.InRange(depth + size / 2, 0, EdgeFogMath.StripPx + 1e-6);
                        Assert.True(depth >= -EdgeFogMath.DepthOutPx - 1e-6);
                    }
                }
                // Twenty seconds later the fog has moved: no puff sits where any started.
                var last = strip.FogPuffsForTests();
                Assert.DoesNotContain(last, p => first.Any(q => Math.Abs(q.X - p.X) < 1e-3 && Math.Abs(q.Y - p.Y) < 1e-3));
            }
        });
    }

    [Fact]
    public void AHueChangeReachesTheFogAndAStoppedCanvasSwapsAtOnce()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var strip = Strip(EdgeSide.Top, Lilac, reduced: false);
            Assert.Equal(new SKColor(Lilac.R, Lilac.G, Lilac.B), strip.FogPaint.Tint);
            strip.SetEdgeFog(Pink, SectionEdgeRules.FogGain(Pink), 250);
            Assert.Equal(new SKColor(Pink.R, Pink.G, Pink.B), strip.FogPaint.Tint);
            Assert.Equal((float)SectionEdgeRules.FogGain(Pink), strip.FogPaint.Gain, 4);
        });
    }

    [Fact]
    public void TheRingCostsLittlePerFrame()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var strips = new[] { EdgeSide.Top, EdgeSide.Right, EdgeSide.Bottom, EdgeSide.Left }
                .Select(s => Strip(s, Sky, reduced: false, embers: true)).ToArray();
            var surfaces = strips.Select(s => SKSurface.Create(new SKImageInfo(
                (int)s.ActualWidth, (int)s.ActualHeight, SKColorType.Bgra8888, SKAlphaType.Premul))).ToArray();
            try
            {
                for (int f = 0; f < 60; f++) Frame(strips, surfaces);   // warm the sprite and JIT
                var sw = Stopwatch.StartNew();
                const int frames = 600;
                for (int f = 0; f < frames; f++) Frame(strips, surfaces);
                sw.Stop();
                double ms = sw.Elapsed.TotalMilliseconds / frames;
                int live = strips.Sum(s => s.FogPuffCount + s.EdgeMoteCount);
                var line = $"fog bench: {ms:F3} ms per frame (step + Skia paint of all four strips, 1661x1002 at 100%), {live} live elements";
                _out.WriteLine(line);
                var dir = Environment.GetEnvironmentVariable("CCP_FOG_RENDER_DIR");
                if (!string.IsNullOrEmpty(dir)) File.WriteAllText(Path.Combine(dir, "fog-bench.txt"), line + Environment.NewLine);
                Assert.True(live < 60, $"{live} live elements");
                Assert.True(ms < 12, line);
            }
            finally { foreach (var s in surfaces) s.Dispose(); }
        });
    }

    [Fact]
    public void ReviewFramesWhenAskedFor()
    {
        var dir = Environment.GetEnvironmentVariable("CCP_FOG_RENDER_DIR");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        WpfRenderHarness.OnStaThread(() =>
        {
            foreach (var (name, hue) in new[] { ("social", Sky), ("studio", Pink) })
            {
                var strips = new[] { EdgeSide.Top, EdgeSide.Right, EdgeSide.Bottom, EdgeSide.Left }
                    .Select(s => Strip(s, hue, reduced: false, embers: true)).ToArray();
                for (int f = 0; f < 90; f++) foreach (var s in strips) { s.StepFog(1f / 30f); s.StepEdge(1f / 30f); }
                for (int second = 0; second < 3; second++)
                {
                    using var window = Compose(strips, hue, fog: true);
                    if (second == 0) Save(window, Path.Combine(dir, $"fog-{name}-window.png"));
                    using var corner = Crop(window, new SKRectI(0, 0, 360, 240), 2);
                    Save(corner, Path.Combine(dir, $"fog-{name}-corner-{second}.png"));
                    for (int f = 0; f < 30; f++) foreach (var s in strips) { s.StepFog(1f / 30f); s.StepEdge(1f / 30f); }
                }
                using (var off = Compose(Array.Empty<AmbientFxCanvas>(), hue, fog: false))
                {
                    Save(off, Path.Combine(dir, $"fog-{name}-motion-off.png"));
                    using var corner = Crop(off, new SKRectI(0, 0, 360, 240), 2);
                    Save(corner, Path.Combine(dir, $"fog-{name}-corner-off.png"));
                }
            }
        });
    }

    // ---------------------------------------------------------------- helpers

    private static AmbientFxCanvas Strip(EdgeSide side, Color hue, bool reduced, bool embers = false)
    {
        var strip = new AmbientFxCanvas();
        bool horizontal = side is EdgeSide.Top or EdgeSide.Bottom;
        double sw = horizontal ? W : EdgeFogMath.StripPx, sh = horizontal ? EdgeFogMath.StripPx : H;
        strip.Width = sw;
        strip.Height = sh;
        strip.Measure(new Size(sw, sh));
        strip.Arrange(new Rect(0, 0, sw, sh));
        strip.StartLayers(new AmbientFxConfig
        {
            Layers = AmbientFxLayers.EdgeFog | (embers ? AmbientFxLayers.EdgeDrift : 0),
            EdgeSide = side,
            Tint = hue,
            Intensity = EdgeParticles.StripIntensity,
            EdgeDriftBandPx = EdgeParticles.EmberBandPx,
            EdgeFogReduced = reduced,
            EdgeFogGain = SectionEdgeRules.FogGain(hue),
        });
        strip.PrimeEdgeForTests(60);
        return strip;
    }

    private static void Frame(AmbientFxCanvas[] strips, SKSurface[] surfaces)
    {
        for (int i = 0; i < strips.Length; i++)
        {
            strips[i].StepFog(1f / 30f);
            strips[i].StepEdge(1f / 30f);
            var c = surfaces[i].Canvas;
            c.Clear(SKColors.Transparent);
            strips[i].PaintEdgeLayersForTests(c, surfaces[i].Canvas.DeviceClipBounds.Width, surfaces[i].Canvas.DeviceClipBounds.Height);
        }
    }

    /// <summary>The window ring as the app stacks it: page, static band, fog strips, frame line.</summary>
    private static SKSurface Compose(AmbientFxCanvas[] strips, Color hue, bool fog)
    {
        var surface = SKSurface.Create(new SKImageInfo((int)W, (int)H, SKColorType.Bgra8888, SKAlphaType.Premul));
        var c = surface.Canvas;
        c.Clear(new SKColor(0x1A, 0x14, 0x28));
        // A stand-in page: two card shapes so the fog is judged against content, not a void.
        using (var card = new SKPaint { Color = new SKColor(0x26, 0x1E, 0x3A), IsAntialias = true })
        {
            c.DrawRoundRect(new SKRect(120, 90, 760, 520), 14, 14, card);
            c.DrawRoundRect(new SKRect(820, 90, 1560, 900), 14, 14, card);
        }

        double depth = SectionEdgeRules.BandDepth(fog);
        var stops = SectionEdgeRules.GlowStops(hue, fog).Select(s => new SKColor(s.R, s.G, s.B, s.A)).ToArray();
        var offs = SectionEdgeRules.GlowOffsets.Select(o => (float)o).ToArray();
        void Band(SKRect r, SKPoint a, SKPoint b)
        {
            using var shader = SKShader.CreateLinearGradient(a, b, stops, offs, SKShaderTileMode.Clamp);
            using var p = new SKPaint { Shader = shader };
            c.DrawRect(r, p);
        }
        float d = (float)depth, w = (float)W, h = (float)H;
        Band(new SKRect(0, 0, w, d), new SKPoint(0, 0), new SKPoint(0, d));
        Band(new SKRect(0, h - d, w, h), new SKPoint(0, h), new SKPoint(0, h - d));
        Band(new SKRect(0, 0, d, h), new SKPoint(0, 0), new SKPoint(d, 0));
        Band(new SKRect(w - d, 0, w, h), new SKPoint(w, 0), new SKPoint(w - d, 0));

        foreach (var s in strips)
        {
            using var layer = SKSurface.Create(new SKImageInfo((int)s.ActualWidth, (int)s.ActualHeight, SKColorType.Bgra8888, SKAlphaType.Premul));
            layer.Canvas.Clear(SKColors.Transparent);
            s.PaintEdgeLayersForTests(layer.Canvas, (float)s.ActualWidth, (float)s.ActualHeight);
            using var img = layer.Snapshot();
            float x = s.EdgeSide == EdgeSide.Right ? w - (float)s.ActualWidth : 0;
            float y = s.EdgeSide == EdgeSide.Bottom ? h - (float)s.ActualHeight : 0;
            c.DrawImage(img, x, y);
        }

        var line = SectionEdgeRules.LineStops(hue, MotionLevel.Full)[0];
        using (var p = new SKPaint { Color = new SKColor(line.R, line.G, line.B, line.A), Style = SKPaintStyle.Stroke, StrokeWidth = 3, IsAntialias = true })
            c.DrawRoundRect(new SKRect(1.5f, 1.5f, w - 1.5f, h - 1.5f), 8, 8, p);
        return surface;
    }

    private static SKSurface Crop(SKSurface src, SKRectI r, int scale)
    {
        var dst = SKSurface.Create(new SKImageInfo(r.Width * scale, r.Height * scale, SKColorType.Bgra8888, SKAlphaType.Premul));
        using var img = src.Snapshot(r);
        using var p = new SKPaint { FilterQuality = SKFilterQuality.None };
        dst.Canvas.DrawImage(img, new SKRect(0, 0, r.Width * scale, r.Height * scale), p);
        return dst;
    }

    private static void Save(SKSurface s, string path)
    {
        using var img = s.Snapshot();
        using var data = img.Encode(SKEncodedImageFormat.Png, 100);
        using var f = File.Create(path);
        data.SaveTo(f);
    }

    private static string AppRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "MainWindow")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "ConditioningControlPanel");
    }
}
