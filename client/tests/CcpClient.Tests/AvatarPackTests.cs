using CcpClient.Desktop.Features.AvatarTube;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// Synthetic pack pipeline tests (SP-015 Step 2): deterministic generation, strip codec,
/// BMP codec, pack-definition parse/validation, loader slicing + strip self-check, and the
/// committed-asset drift proofs. All pure — no Avalonia platform needed.
/// </summary>
public sealed class AvatarPackTests
{
    [Fact]
    public void BmpCodec_RoundTrips32Bpp()
    {
        var pixels = SyntheticAvatarPacks.GenerateSheetPixels(SyntheticAvatarPacks.Circuit, out var width, out var height);
        var bmp = BmpCodec.Encode32(width, height, pixels);
        var (decodedWidth, decodedHeight, decoded) = BmpCodec.Decode(bmp);
        Assert.Equal(width, decodedWidth);
        Assert.Equal(height, decodedHeight);
        Assert.Equal(pixels, decoded);
    }

    [Fact]
    public void BmpCodec_Decodes24BppTopDownAndRejectsGarbage()
    {
        // 24bpp top-down (negative height), 3x2.
        var width = 3;
        var height = 2;
        var stride = ((width * 24 + 31) / 32) * 4;
        var bmp = new byte[54 + stride * height];
        bmp[0] = (byte)'B';
        bmp[1] = (byte)'M';
        BitConverter.TryWriteBytes(bmp.AsSpan(10), 54);
        BitConverter.TryWriteBytes(bmp.AsSpan(14), 40);
        BitConverter.TryWriteBytes(bmp.AsSpan(18), width);
        BitConverter.TryWriteBytes(bmp.AsSpan(22), -height); // top-down
        BitConverter.TryWriteBytes(bmp.AsSpan(26), (short)1);
        BitConverter.TryWriteBytes(bmp.AsSpan(28), (short)24);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                bmp[54 + y * stride + x * 3 + 0] = (byte)(10 + x);
                bmp[54 + y * stride + x * 3 + 1] = (byte)(20 + y);
                bmp[54 + y * stride + x * 3 + 2] = (byte)30;
            }
        }

        var (w, h, bgra) = BmpCodec.Decode(bmp);
        Assert.Equal(3, w);
        Assert.Equal(2, h);
        Assert.Equal((byte)11, bgra[4 + 0]); // x=1,y=0 → B
        Assert.Equal((byte)21, bgra[(1 * w + 0) * 4 + 1]); // x=0,y=1 → G

        Assert.Throws<InvalidDataException>(() => BmpCodec.Decode([1, 2, 3]));
        Assert.Throws<InvalidDataException>(() => BmpCodec.Decode(BmpCodec.Encode32(2, 2, new byte[16])[..20])); // truncated
    }

    [Fact]
    public void Strip_RoundTripsIdentity_AndFailsLoudlyWithoutMarker()
    {
        var pixels = SyntheticAvatarPacks.GenerateSheetPixels(SyntheticAvatarPacks.Pulse, out var sheetWidth, out _);
        var pack = LoadFromMemory(SyntheticAvatarPacks.Pulse, pixels, sheetWidth);
        for (var clip = 0; clip <= SyntheticAvatarPacks.ClipClick; clip++)
        {
            var def = SyntheticAvatarPacks.Pulse.Clip(clip);
            for (var frame = 0; frame < def.Frames; frame++)
            {
                var cell = pack.Frame(clip, frame);
                var ok = AvatarStripCodec.TryDecode(cell, SyntheticAvatarPacks.CellWidth, SyntheticAvatarPacks.CellHeight,
                    out var packId, out var clipId, out var index, out var failure);
                Assert.True(ok, $"clip {clip} frame {frame}: {failure}");
                Assert.Equal(SyntheticAvatarPacks.Pulse.PackId, packId);
                Assert.Equal(clip, clipId);
                Assert.Equal(frame, index);
            }
        }

        var blank = new byte[SyntheticAvatarPacks.CellWidth * SyntheticAvatarPacks.CellHeight * 4];
        Assert.False(AvatarStripCodec.TryDecode(blank, SyntheticAvatarPacks.CellWidth, SyntheticAvatarPacks.CellHeight,
            out _, out _, out _, out var blankFailure));
        Assert.Contains("no-marker", blankFailure);
    }

    [Fact]
    public void Strip_ToleratesCenteringOffsets_AndContentFractionSeparatesBlankFromLive()
    {
        // Simulate the frame centered inside a wider capture: strip auto-locate must find it.
        var pack = LoadFromMemory(SyntheticAvatarPacks.Circuit,
            SyntheticAvatarPacks.GenerateSheetPixels(SyntheticAvatarPacks.Circuit, out var sw, out _), sw);
        var cell = pack.Frame(SyntheticAvatarPacks.ClipIdle, 3);
        var wide = 200;
        var tall = SyntheticAvatarPacks.CellHeight + 16;
        var capture = new byte[wide * tall * 4];
        // Background fill (tube content bg).
        for (var i = 0; i < wide * tall; i++)
        {
            capture[i * 4 + 0] = AvatarEvidence.ContentBgB;
            capture[i * 4 + 1] = AvatarEvidence.ContentBgG;
            capture[i * 4 + 2] = AvatarEvidence.ContentBgR;
            capture[i * 4 + 3] = 255;
        }

        var x0 = (wide - SyntheticAvatarPacks.CellWidth) / 2;
        var y0 = 5; // float offset headroom
        for (var y = 0; y < SyntheticAvatarPacks.CellHeight; y++)
        {
            Array.Copy(cell, y * SyntheticAvatarPacks.CellWidth * 4,
                capture, ((y0 + y) * wide + x0) * 4, SyntheticAvatarPacks.CellWidth * 4);
        }

        Assert.True(AvatarStripCodec.TryDecode(capture, wide, tall, out var packId, out var clipId, out var index, out var failure),
            failure);
        Assert.Equal(0, packId);
        Assert.Equal(SyntheticAvatarPacks.ClipIdle, clipId);
        Assert.Equal(3, index);

        var liveFraction = AvatarStripCodec.ContentFraction(capture, wide, tall,
            AvatarEvidence.ContentBgR, AvatarEvidence.ContentBgG, AvatarEvidence.ContentBgB, 16);
        Assert.True(liveFraction > AvatarSequenceEvaluator.DefaultBlankFractionThreshold,
            $"live frame fraction {liveFraction:F3} must exceed the blank threshold");

        var blankCapture = new byte[wide * tall * 4];
        for (var i = 0; i < wide * tall; i++)
        {
            blankCapture[i * 4 + 0] = AvatarEvidence.ContentBgB;
            blankCapture[i * 4 + 1] = AvatarEvidence.ContentBgG;
            blankCapture[i * 4 + 2] = AvatarEvidence.ContentBgR;
            blankCapture[i * 4 + 3] = 255;
        }

        var blankFraction = AvatarStripCodec.ContentFraction(blankCapture, wide, tall,
            AvatarEvidence.ContentBgR, AvatarEvidence.ContentBgG, AvatarEvidence.ContentBgB, 16);
        Assert.True(blankFraction < AvatarSequenceEvaluator.DefaultBlankFractionThreshold);
    }

    [Fact]
    public void Strip_FullWindowScan_LocatesMidImageCell_SkipsDecoyMarker_FailsLoudlyWhenAbsent()
    {
        // WSLg XGetImage captures the WHOLE window (no UIA stage probe): the strip can sit
        // anywhere. Scan must find it mid-image, skip a decoy magenta block whose bit area
        // is ambiguous, and fail loudly on a marker-less window.
        var pack = LoadFromMemory(SyntheticAvatarPacks.Circuit,
            SyntheticAvatarPacks.GenerateSheetPixels(SyntheticAvatarPacks.Circuit, out var sw, out _), sw);
        var cell = pack.Frame(SyntheticAvatarPacks.ClipTalk, 2);
        const int winW = 320;
        const int winH = 380;
        var capture = new byte[winW * winH * 4]; // black window

        // Decoy at (20,30), EARLIER in scan order than the real strip: magenta marker block
        // followed by mid-gray bit blocks (luminance 128 = ambiguous) — must be skipped.
        for (var y = 30; y < 30 + AvatarStripCodec.StripHeight; y++)
        {
            for (var x = 20; x < 20 + AvatarStripCodec.StripWidth; x++)
            {
                var off = (y * winW + x) * 4;
                var isMarker = x < 20 + AvatarStripCodec.BlockSize;
                capture[off + 0] = isMarker ? (byte)255 : (byte)128;
                capture[off + 1] = isMarker ? (byte)0 : (byte)128;
                capture[off + 2] = isMarker ? (byte)255 : (byte)128;
                capture[off + 3] = 255;
            }
        }

        const int x0 = 10;
        const int y0 = 44;
        for (var y = 0; y < SyntheticAvatarPacks.CellHeight; y++)
        {
            Array.Copy(cell, y * SyntheticAvatarPacks.CellWidth * 4,
                capture, ((y0 + y) * winW + x0) * 4, SyntheticAvatarPacks.CellWidth * 4);
        }

        var ok = AvatarStripCodec.TryDecodeFullWindow(capture, winW, winH,
            out var packId, out var clipId, out var index, out var stripX, out var stripY, out var failure);
        Assert.True(ok, failure);
        Assert.Equal(SyntheticAvatarPacks.Circuit.PackId, packId);
        Assert.Equal(SyntheticAvatarPacks.ClipTalk, clipId);
        Assert.Equal(2, index);
        // Center-sampling tolerates a ±2px locate shift (4px blocks, center sample) — the
        // leftmost/topmost position whose center lands on the real marker wins the scan.
        Assert.True(Math.Abs(stripX - x0) <= 2, $"stripX {stripX} vs cell x {x0}");
        Assert.True(Math.Abs(stripY - (y0 + SyntheticAvatarPacks.CellHeight - AvatarStripCodec.StripHeight)) <= 2,
            $"stripY {stripY} vs cell strip row {y0 + SyntheticAvatarPacks.CellHeight - AvatarStripCodec.StripHeight}");

        Assert.False(AvatarStripCodec.TryDecodeFullWindow(new byte[winW * winH * 4], winW, winH,
            out _, out _, out _, out _, out _, out var absent));
        Assert.Contains("full window", absent);
    }

    [Fact]
    public void StripDecode_FullWindowMode_DecodesCellContentFromWholeWindowCapture()
    {
        // End-to-end over the CLI surface: BMP on disk -> JSON sample with cell-bounded
        // content (window chrome must not swamp the no-blank signal).
        var pack = LoadFromMemory(SyntheticAvatarPacks.Circuit,
            SyntheticAvatarPacks.GenerateSheetPixels(SyntheticAvatarPacks.Circuit, out var sw, out _), sw);
        var cell = pack.Frame(SyntheticAvatarPacks.ClipIdle, 1);
        const int winW = 300;
        const int winH = 360;
        var capture = new byte[winW * winH * 4];
        const int x0 = 14;
        const int y0 = 60;
        for (var y = 0; y < SyntheticAvatarPacks.CellHeight; y++)
        {
            Array.Copy(cell, y * SyntheticAvatarPacks.CellWidth * 4,
                capture, ((y0 + y) * winW + x0) * 4, SyntheticAvatarPacks.CellWidth * 4);
        }

        var path = Path.Combine(Path.GetTempPath(), $"sp015-scan-{Guid.NewGuid():N}.bmp");
        try
        {
            File.WriteAllBytes(path, BmpCodec.Encode32(winW, winH, capture));
            var writer = new StringWriter();
            Assert.Equal(0, AvatarEvidence.StripDecode(path, writer, fullWindow: true));
            var json = writer.ToString();
            Assert.Contains($"\"Pack\":{SyntheticAvatarPacks.Circuit.PackId}", json);
            Assert.Contains($"\"Clip\":{SyntheticAvatarPacks.ClipIdle}", json);
            Assert.Contains("\"Frame\":1", json);
            Assert.Contains("\"Decoded\":true", json);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Definitions_AreNonUniform_AndCommittedJsonMatchesInCodeSource()
    {
        foreach (var def in SyntheticAvatarPacks.All)
        {
            foreach (var clip in def.Clips)
            {
                Assert.True(clip.DelaysMs.Distinct().Count() > 1,
                    $"pack {def.Name} clip {clip.Name}: uniform delays cannot falsify multiplied-speed (packet rule)");
                Assert.All(clip.DelaysMs, d => Assert.True(d >= 400,
                    $"pack {def.Name} clip {clip.Name}: delay {d}ms below the capture-sampling floor (consult verdict #5)"));
            }

            using var stream = OpenEmbedded($"Assets/avatar/pack-{def.Name}.json");
            using var reader = new StreamReader(stream);
            var parsed = SyntheticAvatarPacks.TryParseDef(reader.ReadToEnd(), out var committed, out var error);
            Assert.True(parsed, error);
            Assert.Equal(def.PackId, committed!.PackId);
            Assert.Equal(def.Name, committed.Name);
            Assert.Equal(def.CellWidth, committed.CellWidth);
            Assert.Equal(def.SheetPath, committed.SheetPath);
            // Structural clip comparison (arrays compare by reference in records).
            Assert.Equal(
                def.Clips.Select(c => (c.ClipId, c.Name, c.Frames, Delays: string.Join(",", c.DelaysMs))),
                committed.Clips.Select(c => (c.ClipId, c.Name, c.Frames, Delays: string.Join(",", c.DelaysMs))));
        }
    }

    [Fact]
    public void Generation_IsDeterministic_CommittedAssetsPixelIdentical()
    {
        foreach (var def in SyntheticAvatarPacks.All)
        {
            var pixels = SyntheticAvatarPacks.GenerateSheetPixels(def, out var width, out var height);
            using var stream = OpenEmbedded(def.SheetPath);
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            var (committedWidth, committedHeight, committedPixels) = BmpCodec.Decode(memory.ToArray());
            Assert.Equal(width, committedWidth);
            Assert.Equal(height, committedHeight);
            // Pixel determinism (what the evidence consumes), not byte determinism (encoder-detail).
            Assert.Equal(pixels, committedPixels);
        }

        var fallback = SyntheticAvatarPacks.GenerateFallbackPixels(out _, out _);
        using var fallbackStream = OpenEmbedded(SyntheticAvatarPacks.FallbackPath);
        using var fallbackMemory = new MemoryStream();
        fallbackStream.CopyTo(fallbackMemory);
        var (_, _, committedFallback) = BmpCodec.Decode(fallbackMemory.ToArray());
        Assert.Equal(fallback, committedFallback);
    }

    [Fact]
    public void Loader_SlicesDeclaredGrid_AndRejectsUndecodable()
    {
        var pixels = SyntheticAvatarPacks.GenerateSheetPixels(SyntheticAvatarPacks.Circuit, out var width, out _);
        var pack = LoadFromMemory(SyntheticAvatarPacks.Circuit, pixels, width);
        Assert.Equal(6, pack.Frames.Count);
        Assert.Equal(6, pack.Frames[SyntheticAvatarPacks.ClipIdle].Length);

        // Undecodable bytes → typed load exception (the participant maps it to SP-006 Degraded).
        Assert.Throws<AvatarPackLoadException>(() =>
            AvatarPackLoader.Load(SyntheticAvatarPacks.Circuit, _ => new MemoryStream([1, 2, 3, 4])));

        // Grid mismatch (right magic, wrong dimensions) → typed load exception.
        var wrong = BmpCodec.Encode32(8, 8, new byte[8 * 8 * 4]);
        Assert.Throws<AvatarPackLoadException>(() =>
            AvatarPackLoader.Load(SyntheticAvatarPacks.Circuit, _ => new MemoryStream(wrong)));
    }

    [Fact]
    public void DefParse_RejectsBadShapes()
    {
        Assert.False(SyntheticAvatarPacks.TryParseDef("{}", out _, out _));
        Assert.False(SyntheticAvatarPacks.TryParseDef("""{"version":2}""", out _, out _));
        var valid = SyntheticAvatarPacks.SerializeDef(SyntheticAvatarPacks.Circuit);
        Assert.True(SyntheticAvatarPacks.TryParseDef(valid, out var roundTripped, out var error), error);
        Assert.Equal(SyntheticAvatarPacks.Circuit.PackId, roundTripped!.PackId);
        Assert.Equal(SyntheticAvatarPacks.Circuit.Clips.Length, roundTripped.Clips.Length);
    }

    /// <summary>Embedded-asset open via the same mechanism the app uses (SP-009 StandardAssetLoader pattern).</summary>
    private static Stream OpenEmbedded(string path) =>
        new Avalonia.Platform.StandardAssetLoader(typeof(CcpClient.Desktop.App).Assembly).Open(
            new Uri(CcpClient.Desktop.Manifest.AssetManifest.AssemblyAssetUriPrefix + path));

    /// <summary>In-memory pack load (deterministic pixels, no embedded assets needed).</summary>
    public static AvatarPack LoadFromMemory(AvatarPackDef def, byte[] sheetPixels, int sheetWidth)
    {
        var rows = def.Clips.Max(c => c.ClipId) + 1;
        var bmp = BmpCodec.Encode32(sheetWidth, rows * def.CellHeight, sheetPixels);
        return AvatarPackLoader.Load(def, _ => new MemoryStream(bmp));
    }

    /// <summary>Asset-open seam backed by generated in-memory bytes (valid packs + fallback).</summary>
    public static Func<string, Stream> InMemoryAssetOpener()
    {
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var def in SyntheticAvatarPacks.All)
        {
            var pixels = SyntheticAvatarPacks.GenerateSheetPixels(def, out var width, out var height);
            files[def.SheetPath] = BmpCodec.Encode32(width, height, pixels);
        }

        var fallback = SyntheticAvatarPacks.GenerateFallbackPixels(out var fw, out var fh);
        files[SyntheticAvatarPacks.FallbackPath] = BmpCodec.Encode32(fw, fh, fallback);
        return path => new MemoryStream(files[path]);
    }
}
