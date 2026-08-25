using CcpVerify;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// Tier-3 assertion-logic unit tests: the CcpVerify check evaluator and manifest
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
        var image = Solid(100, 50, (0x12, 0x12, 0x20));
        Paint(image, 0, 0, 100, 3, (0xFF, 0x8F, 0xAF)); // top 3 rows = border color
        var result = CheckEvaluator.Evaluate(BorderCheck("#FF8FAF", 32, 0.9), image);
        Assert.True(result.Passed);
        Assert.Equal(300, result.Sampled);
        Assert.Equal(300, result.Matched);
        Assert.Equal("test-border", result.Name);
    }

    [Fact]
    public void BorderBand_Fail_WhenBandIsWrongColor()
    {
        var image = Solid(100, 50, (0x3A, 0x3A, 0x5C)); // unlit color where lit expected
        var result = CheckEvaluator.Evaluate(BorderCheck("#FF8FAF", 32, 0.5), image);
        Assert.False(result.Passed);
        Assert.Equal(0, result.Matched);
        Assert.Equal("test-border", result.Name); // the failed check is NAMED
    }

    [Fact]
    public void ToleranceBoundary_ExactDeltaPasses_PlusOneFails()
    {
        var image = Solid(10, 3, (0xFF, 0x8F, 0xAF));
        Paint(image, 0, 0, 10, 3, (0xFF - 32, 0x8F, 0xAF)); // exactly tolerance away on R
        Assert.True(CheckEvaluator.Evaluate(BorderCheck("#FF8FAF", 32, 1.0), image).Passed);

        Paint(image, 0, 0, 10, 3, (0xFF - 33, 0x8F, 0xAF)); // tolerance + 1
        Assert.False(CheckEvaluator.Evaluate(BorderCheck("#FF8FAF", 32, 1.0), image).Passed);
    }

    [Fact]
    public void MinPixelFractionBoundary_ExactFractionPasses_BelowFails()
    {
        var image = Solid(100, 3, (0x12, 0x12, 0x20));
        Paint(image, 0, 0, 50, 3, (0xFF, 0x8F, 0xAF)); // exactly half the band matches
        Assert.True(CheckEvaluator.Evaluate(BorderCheck("#FF8FAF", 32, 0.5), image).Passed);
        Assert.False(CheckEvaluator.Evaluate(BorderCheck("#FF8FAF", 32, 0.51), image).Passed);
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
            ExpectedColor = "#121220",
            Tolerance = 8,
            MinPixelFraction = 1.0,
        };

        var small = Solid(100, 80, (0x12, 0x12, 0x20));
        Paint(small, 50, 40, 100, 80, (0xFF, 0x8F, 0xAF)); // bottom-right quadrant different
        var large = Solid(200, 160, (0x12, 0x12, 0x20));
        Paint(large, 100, 80, 200, 160, (0xFF, 0x8F, 0xAF));

        Assert.True(CheckEvaluator.Evaluate(check, small).Passed);
        Assert.True(CheckEvaluator.Evaluate(check, large).Passed);
    }

    [Fact]
    public void Bands_LeftRightBottom_SampleTheCorrectEdge()
    {
        var image = Solid(60, 40, (0x12, 0x12, 0x20));
        Paint(image, 0, 0, 2, 40, (0xFF, 0x8F, 0xAF)); // left edge
        Paint(image, 58, 0, 60, 40, (0x3A, 0x3A, 0x5C)); // right edge

        ManifestCheck For(string band, string color) => BorderCheck(color, 8, 0.9) with
        {
            Region = new CheckRegion { Band = band, ThicknessPx = 2 },
        };

        Assert.True(CheckEvaluator.Evaluate(For("left", "#FF8FAF"), image).Passed);
        Assert.True(CheckEvaluator.Evaluate(For("right", "#3A3A5C"), image).Passed);
        Assert.False(CheckEvaluator.Evaluate(For("left", "#3A3A5C"), image).Passed);
        Assert.False(CheckEvaluator.Evaluate(For("bottom", "#FF8FAF"), image).Passed);
    }

    [Fact]
    public void EvaluateCapture_SelectsBySurfaceAndState_AndFailsOnUnknown()
    {
        var checks = new[]
        {
            BorderCheck("#FF8FAF", 32, 0.5),
            BorderCheck("#3A3A5C", 24, 0.5) with { Name = "other", State = "unlit" },
        };
        var image = Solid(100, 3, (0xFF, 0x8F, 0xAF));
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
              "region": { "band": "top", "thicknessPx": 3 }, "expectedColor": "#FF8FAF",
              "tolerance": 32, "minPixelFraction": 0.5 } ] }
            """);
        Assert.Single(CheckManifest.Load(valid));

        Assert.Throws<InvalidDataException>(() => CheckManifest.Load(Write("bad-kind.json", """
            { "version": 1, "checks": [ { "name": "a", "surface": "s", "state": "t",
              "evidenceClass": "presentation-verified", "kind": "magic",
              "region": { "band": "top", "thicknessPx": 3 }, "expectedColor": "#FF8FAF",
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
                "expectedColor": "#121220", "tolerance": 8, "minPixelFraction": 0.5 },
              { "name": "a", "surface": "s", "state": "t", "evidenceClass": "draw-verified",
                "kind": "region-color", "region": { "rect": { "x": 0, "y": 0, "w": 1, "h": 1 } },
                "expectedColor": "#121220", "tolerance": 8, "minPixelFraction": 0.5 } ] }
            """)));
        Assert.Throws<InvalidDataException>(() => CheckManifest.Load(Write("bad-class.json", """
            { "version": 1, "checks": [ { "name": "a", "surface": "s", "state": "t",
              "evidenceClass": "looks-verified", "kind": "border-color-band",
              "region": { "band": "top", "thicknessPx": 3 }, "expectedColor": "#FF8FAF",
              "tolerance": 32, "minPixelFraction": 0.5 } ] }
            """)));
    }

    [Fact]
    public void DecodedImage_RejectsBadDimensionsAndBufferSize()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new DecodedImage(0, 10, new byte[0]));
        Assert.Throws<ArgumentException>(() => new DecodedImage(10, 10, new byte[10]));
    }

    /// <summary>
    /// THE CAPTURE STEP'S OWN GATE, on the exact shape that burned this board: a correctly-sized
    /// 175x44 capture of 7,700 identical black pixels, which <c>capture-wslg.sh</c> called
    /// CAPTURE PASS. The count is asserted as part of the message because a refusal that does not
    /// say what it counted sends its reader nowhere.
    /// </summary>
    [Fact]
    public void Census_RefusesAnAllBlackCapture_AndNamesTheCount()
    {
        var census = CaptureCensus.Of(Solid(175, 44, (0, 0, 0)));

        Assert.True(census.IsVacuous);
        Assert.Equal(1, census.DistinctColors);
        Assert.Equal(7700, census.Pixels);
        Assert.Contains("1 distinct colour", census.ToString(), StringComparison.Ordinal);
        Assert.Contains("all 7700 pixels", census.ToString(), StringComparison.Ordinal);
        Assert.Contains("#000000", census.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// "Entirely the background" is the same refusal and needs no second rule — but the colour has
    /// to be NAMED, or the reader cannot tell a black capture (nothing drawn) from a capture of an
    /// empty panel (the module ground, MainWindow.axaml:122).
    /// </summary>
    [Fact]
    public void Census_RefusesACaptureThatIsEntirelyTheBackground_AndNamesTheColour()
    {
        var census = CaptureCensus.Of(Solid(64, 64, (0x1C, 0x1C, 0x35)));

        Assert.True(census.IsVacuous);
        Assert.Contains("1 distinct colour", census.ToString(), StringComparison.Ordinal);
        Assert.Contains("RGB(28,28,53) #1C1C35", census.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// The other direction, and it is not optional: a gate that only ever fires is as broken as
    /// one that never fires. One painted band — the selected door's border — is a second colour,
    /// and the census passes it while still reporting what it counted.
    /// </summary>
    [Fact]
    public void Census_AcceptsACaptureCarryingASecondColour()
    {
        var image = Solid(175, 44, (0, 0, 0));
        Paint(image, 0, 0, 175, 3, (0xFF, 0x8F, 0xAF));

        var census = CaptureCensus.Of(image);

        Assert.False(census.IsVacuous);
        Assert.Equal(2, census.DistinctColors);
        Assert.Equal("2 distinct colours across 7700 pixels", census.ToString());
    }

    /// <summary>
    /// Alpha is not a colour. A capture transport that varies the alpha byte over an otherwise
    /// uniform image must not be able to manufacture a second "colour" and buy itself a pass.
    /// </summary>
    [Fact]
    public void Census_DoesNotLetVaryingAlphaCountAsASecondColour()
    {
        var image = Solid(32, 32, (0, 0, 0));
        for (var i = 0; i < image.Width * image.Height; i++)
        {
            image.Bgra[i * 4 + 3] = (byte)(i % 256);
        }

        var census = CaptureCensus.Of(image);

        Assert.True(census.IsVacuous);
        Assert.Equal(1, census.DistinctColors);
    }
}
