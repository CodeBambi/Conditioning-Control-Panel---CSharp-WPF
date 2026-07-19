using CcpVerify;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// SP-008 tier-3 assertion-logic unit tests: the CcpVerify check evaluator and manifest
/// loader on SYNTHETIC pixel buffers (no Avalonia runtime, no captures). Pass case,
/// per-check fail case, tolerance and fraction boundaries, region shapes, manifest
/// validation. The 85 landed tests stay untouched; these are additive.
/// </summary>
public class VerifyHarnessTests
{
    private static DecodedImage Solid(int w, int h, (byte R, byte G, byte B) color)
    {
        var bgra = new byte[w * h * 4];
        for (var i = 0; i < w * h; i++)
        {
            bgra[i * 4] = color.B;
            bgra[i * 4 + 1] = color.G;
            bgra[i * 4 + 2] = color.R;
            bgra[i * 4 + 3] = 255;
        }

        return new DecodedImage(w, h, bgra);
    }

    private static void Paint(DecodedImage image, int x0, int y0, int x1, int y1, (byte R, byte G, byte B) color)
    {
        for (var y = y0; y < y1; y++)
        {
            for (var x = x0; x < x1; x++)
            {
                var o = (y * image.Width + x) * 4;
                image.Bgra[o] = color.B;
                image.Bgra[o + 1] = color.G;
                image.Bgra[o + 2] = color.R;
            }
        }
    }

    private static ManifestCheck BorderCheck(string color, int tolerance, double fraction) => new()
    {
        Name = "test-border",
        Surface = "dashboard-card",
        State = "lit",
        EvidenceClass = CheckManifest.EvidencePresentation,
        Kind = CheckManifest.KindBorderColorBand,
        Region = new CheckRegion { Band = "top", ThicknessPx = 3 },
        ExpectedColor = color,
        Tolerance = tolerance,
        MinPixelFraction = fraction,
    };

    [Fact]
    public void BorderBand_Pass_WhenBandMatchesExpectedColor()
    {
        var image = Solid(100, 50, (0x14, 0x10, 0x18));
        Paint(image, 0, 0, 100, 3, (0xE0, 0x66, 0xFF)); // top 3 rows = border color
        var result = CheckEvaluator.Evaluate(BorderCheck("#E066FF", 32, 0.9), image);
        Assert.True(result.Passed);
        Assert.Equal(300, result.Sampled);
        Assert.Equal(300, result.Matched);
        Assert.Equal("test-border", result.Name);
    }

    [Fact]
    public void BorderBand_Fail_WhenBandIsWrongColor()
    {
        var image = Solid(100, 50, (0x3A, 0x2F, 0x3E)); // unlit color where lit expected
        var result = CheckEvaluator.Evaluate(BorderCheck("#E066FF", 32, 0.5), image);
        Assert.False(result.Passed);
        Assert.Equal(0, result.Matched);
        Assert.Equal("test-border", result.Name); // the failed check is NAMED
    }

    [Fact]
    public void ToleranceBoundary_ExactDeltaPasses_PlusOneFails()
    {
        var image = Solid(10, 3, (0xE0, 0x66, 0xFF));
        Paint(image, 0, 0, 10, 3, (0xE0 - 32, 0x66, 0xFF)); // exactly tolerance away on R
        Assert.True(CheckEvaluator.Evaluate(BorderCheck("#E066FF", 32, 1.0), image).Passed);

        Paint(image, 0, 0, 10, 3, (0xE0 - 33, 0x66, 0xFF)); // tolerance + 1
        Assert.False(CheckEvaluator.Evaluate(BorderCheck("#E066FF", 32, 1.0), image).Passed);
    }

    [Fact]
    public void MinPixelFractionBoundary_ExactFractionPasses_BelowFails()
    {
        var image = Solid(100, 3, (0x14, 0x10, 0x18));
        Paint(image, 0, 0, 50, 3, (0xE0, 0x66, 0xFF)); // exactly half the band matches
        Assert.True(CheckEvaluator.Evaluate(BorderCheck("#E066FF", 32, 0.5), image).Passed);
        Assert.False(CheckEvaluator.Evaluate(BorderCheck("#E066FF", 32, 0.51), image).Passed);
    }

    [Fact]
    public void RegionColor_UsesFractionalRect_NotAbsolutePixels()
    {
        // Same fractional rect at two different sizes must sample the same logical region.
        var check = new ManifestCheck
        {
            Name = "test-region",
            Surface = "dashboard",
            State = "unlit",
            EvidenceClass = CheckManifest.EvidencePresentation,
            Kind = CheckManifest.KindRegionColor,
            Region = new CheckRegion { Rect = new RectFraction { X = 0.0, Y = 0.0, W = 0.5, H = 0.5 } },
            ExpectedColor = "#141018",
            Tolerance = 8,
            MinPixelFraction = 1.0,
        };

        var small = Solid(100, 80, (0x14, 0x10, 0x18));
        Paint(small, 50, 40, 100, 80, (0xE0, 0x66, 0xFF)); // bottom-right quadrant different
        var large = Solid(200, 160, (0x14, 0x10, 0x18));
        Paint(large, 100, 80, 200, 160, (0xE0, 0x66, 0xFF));

        Assert.True(CheckEvaluator.Evaluate(check, small).Passed);
        Assert.True(CheckEvaluator.Evaluate(check, large).Passed);
    }

    [Fact]
    public void Bands_LeftRightBottom_SampleTheCorrectEdge()
    {
        var image = Solid(60, 40, (0x14, 0x10, 0x18));
        Paint(image, 0, 0, 2, 40, (0xE0, 0x66, 0xFF)); // left edge
        Paint(image, 58, 0, 60, 40, (0x3A, 0x2F, 0x3E)); // right edge

        ManifestCheck For(string band, string color) => BorderCheck(color, 8, 0.9) with
        {
            Region = new CheckRegion { Band = band, ThicknessPx = 2 },
        };

        Assert.True(CheckEvaluator.Evaluate(For("left", "#E066FF"), image).Passed);
        Assert.True(CheckEvaluator.Evaluate(For("right", "#3A2F3E"), image).Passed);
        Assert.False(CheckEvaluator.Evaluate(For("left", "#3A2F3E"), image).Passed);
        Assert.False(CheckEvaluator.Evaluate(For("bottom", "#E066FF"), image).Passed);
    }

    [Fact]
    public void EvaluateCapture_SelectsBySurfaceAndState_AndFailsOnUnknown()
    {
        var checks = new[]
        {
            BorderCheck("#E066FF", 32, 0.5),
            BorderCheck("#3A2F3E", 24, 0.5) with { Name = "other", State = "unlit" },
        };
        var image = Solid(100, 3, (0xE0, 0x66, 0xFF));
        var results = CheckEvaluator.EvaluateCapture(checks, "dashboard-card", "lit", image);
        Assert.Single(results);
        Assert.Equal("test-border", results[0].Name);
        Assert.Throws<InvalidDataException>(() =>
            CheckEvaluator.EvaluateCapture(checks, "dashboard-card", "hover", image));
    }

    [Fact]
    public void ManifestLoad_ValidatesKindColorVersionAndDuplicateNames()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ccp-sp008-manifest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        string Write(string name, string json)
        {
            var path = Path.Combine(dir, name);
            File.WriteAllText(path, json);
            return path;
        }

        var valid = Write("valid.json", """
            { "version": 1, "checks": [ { "name": "a", "surface": "s", "state": "t",
              "evidenceClass": "presentation-verified", "kind": "border-color-band",
              "region": { "band": "top", "thicknessPx": 3 }, "expectedColor": "#E066FF",
              "tolerance": 32, "minPixelFraction": 0.5 } ] }
            """);
        Assert.Single(CheckManifest.Load(valid));

        Assert.Throws<InvalidDataException>(() => CheckManifest.Load(Write("bad-kind.json", """
            { "version": 1, "checks": [ { "name": "a", "surface": "s", "state": "t",
              "evidenceClass": "presentation-verified", "kind": "magic",
              "region": { "band": "top", "thicknessPx": 3 }, "expectedColor": "#E066FF",
              "tolerance": 32, "minPixelFraction": 0.5 } ] }
            """)));
        Assert.Throws<InvalidDataException>(() => CheckManifest.Load(Write("bad-color.json", """
            { "version": 1, "checks": [ { "name": "a", "surface": "s", "state": "t",
              "evidenceClass": "presentation-verified", "kind": "border-color-band",
              "region": { "band": "top", "thicknessPx": 3 }, "expectedColor": "E066FF",
              "tolerance": 32, "minPixelFraction": 0.5 } ] }
            """)));
        Assert.Throws<InvalidDataException>(() => CheckManifest.Load(Write("bad-version.json", """
            { "version": 2, "checks": [] }
            """)));
        Assert.Throws<InvalidDataException>(() => CheckManifest.Load(Write("dup.json", """
            { "version": 1, "checks": [
              { "name": "a", "surface": "s", "state": "t", "evidenceClass": "draw-verified",
                "kind": "region-color", "region": { "rect": { "x": 0, "y": 0, "w": 1, "h": 1 } },
                "expectedColor": "#141018", "tolerance": 8, "minPixelFraction": 0.5 },
              { "name": "a", "surface": "s", "state": "t", "evidenceClass": "draw-verified",
                "kind": "region-color", "region": { "rect": { "x": 0, "y": 0, "w": 1, "h": 1 } },
                "expectedColor": "#141018", "tolerance": 8, "minPixelFraction": 0.5 } ] }
            """)));
        Assert.Throws<InvalidDataException>(() => CheckManifest.Load(Write("bad-class.json", """
            { "version": 1, "checks": [ { "name": "a", "surface": "s", "state": "t",
              "evidenceClass": "looks-verified", "kind": "border-color-band",
              "region": { "band": "top", "thicknessPx": 3 }, "expectedColor": "#E066FF",
              "tolerance": 32, "minPixelFraction": 0.5 } ] }
            """)));
    }

    [Fact]
    public void DecodedImage_RejectsBadDimensionsAndBufferSize()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new DecodedImage(0, 10, new byte[0]));
        Assert.Throws<ArgumentException>(() => new DecodedImage(10, 10, new byte[10]));
    }
}
