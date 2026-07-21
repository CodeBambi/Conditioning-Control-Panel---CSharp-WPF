using System.Text.Json;

namespace CcpClient.Desktop.Features.AvatarTube;

/// <summary>
/// The demonstrator's bounded evidence diagnostics (asset-manifest --verify-assets
/// pattern: bounded paths at the top of Main, before any phase — no window, no lifetime,
/// no participants). ALL pixel/temporal logic lives here and in the evaluator (unit-tested
/// pure code); the evidence scripts only capture, transport, and append timestamps.
/// <list type="bullet">
/// <item><c>--avatar-strip-decode --capture &lt;bmp&gt;</c>: decode one capture's strip + content fraction → one JSON line (the script appends its capture timestamp to build the samples file).</item>
/// <item><c>--avatar-sequence &lt;samples.jsonl&gt; --pack &lt;pack.json&gt; [--trace &lt;trace.jsonl&gt;]</c>: the named verdicts, exit 0/2.</item>
/// </list>
/// </summary>
public static class AvatarEvidence
{
    public const string StripDecodeFlag = "--avatar-strip-decode";
    public const string SequenceFlag = "--avatar-sequence";
    public const string GenerateFlag = "--generate-avatar-packs";

    /// <summary>CLI modifier for <see cref="StripDecodeFlag"/>: full-window marker scan (WSLg).</summary>
    public const string ScanFlag = "--scan";

    /// <summary>One samples-file line (JSONL): capture timestamp + strip decode + content fraction + content Y centroid.</summary>
    public sealed record SampleLine(long T, bool Decoded, int Pack, int Clip, int Frame, double Content, double Cy, string? Failure);

    /// <summary>One trace-file line (JSONL), written by the participant's trace sink.</summary>
    public sealed record TraceLine(long T, string Kind, int Pack, int Clip, int Frame);

    /// <summary>
    /// Decodes one BMP capture's strip; prints the JSON sample (without timestamp — the script owns time).
    /// <paramref name="fullWindow"/> (CLI <see cref="ScanFlag"/>) scans the whole image for the strip
    /// (WSLg XGetImage full-window captures — no UIA stage probe there) and bounds the content
    /// measure to the located frame cell instead of the whole window.
    /// </summary>
    public static int StripDecode(string capturePath, TextWriter output, bool fullWindow = false)
    {
        byte[] bgra;
        int width;
        int height;
        try
        {
            (width, height, bgra) = BmpCodec.Decode(File.ReadAllBytes(capturePath));
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            output.WriteLine(JsonSerializer.Serialize(new SampleLine(0, false, -1, -1, -1, 0.0, -1.0, $"capture-decode: {ex.Message}")));
            return 1;
        }

        if (fullWindow)
        {
            var decodedFull = AvatarStripCodec.TryDecodeFullWindow(
                bgra, width, height, out var packF, out var clipF, out var frameF, out var stripX, out var stripY, out var failureF);
            double contentF;
            double centroidF;
            if (decodedFull)
            {
                // The strip sits at the bottom-left of its 96x128 frame cell: the cell spans
                // (stripX, stripY + stripHeight - cellHeight). Measure content over the CELL,
                // not the window — the window's chrome/text would swamp the no-blank signal.
                // The centroid is reported WINDOW-RELATIVE (cellTop + cell-centroid): the
                // located cell rides the float transform, so a cell-relative centroid is
                // constant and the float-liveness signal would read zero (WSLg Step-5 finding).
                var cellTop = stripY + AvatarStripCodec.StripHeight - SyntheticAvatarPacks.CellHeight;
                if (cellTop >= 0 && stripX + SyntheticAvatarPacks.CellWidth <= width)
                {
                    var cell = ExtractRegion(bgra, width, stripX, cellTop, SyntheticAvatarPacks.CellWidth, SyntheticAvatarPacks.CellHeight);
                    double cellCy;
                    (contentF, cellCy) = AvatarStripCodec.ContentMeasure(cell, SyntheticAvatarPacks.CellWidth, SyntheticAvatarPacks.CellHeight, ContentBgR, ContentBgG, ContentBgB, tolerance: 16);
                    centroidF = cellCy < 0 ? -1.0 : cellTop + cellCy;
                }
                else
                {
                    decodedFull = false;
                    contentF = 0.0;
                    centroidF = -1.0;
                    failureF = $"cell-outside-capture: strip at {stripX},{stripY} implies cell top {cellTop}";
                }
            }
            else
            {
                // Decode failure mid-crossfade is NOT blank (consult verdict #8): a
                // marker-independent content signal over the whole window — saturated bright
                // pixels. The synthetic art is saturated cyan/magenta/amber; text, the dark
                // background, and a truly unpainted (black) window never saturate, so this
                // separates a blend from a genuine blank without any layout constants.
                (contentF, centroidF) = SaturatedContentMeasure(bgra, width, height);
            }

            output.WriteLine(JsonSerializer.Serialize(new SampleLine(0, decodedFull, decodedFull ? packF : -1, decodedFull ? clipF : -1, decodedFull ? frameF : -1, contentF, centroidF, failureF)));
            return decodedFull ? 0 : 2;
        }

        // The tube's content background (AvatarTubeDemonstratorWindow) — the union no-blank
        // reference. Kept as constants here so the evaluator and the window share one value.
        var (content, centroidY) = AvatarStripCodec.ContentMeasure(bgra, width, height, ContentBgR, ContentBgG, ContentBgB, tolerance: 16);
        var decoded = AvatarStripCodec.TryDecode(bgra, width, height, out var pack, out var clip, out var frame, out var failure);
        output.WriteLine(JsonSerializer.Serialize(new SampleLine(0, decoded, decoded ? pack : -1, decoded ? clip : -1, decoded ? frame : -1, content, centroidY, failure)));
        return decoded ? 0 : 2;
    }

    /// <summary>Copies a (w,h) region at (x,y) out of a top-down BGRA buffer.</summary>
    private static byte[] ExtractRegion(byte[] bgra, int width, int x, int y, int w, int h)
    {
        var region = new byte[w * h * 4];
        for (var row = 0; row < h; row++)
        {
            Array.Copy(bgra, ((y + row) * width + x) * 4, region, row * w * 4, w * 4);
        }

        return region;
    }

    /// <summary>
    /// Fraction + Y centroid of saturated bright pixels (channel spread > 60, max channel > 60).
    /// The synthetic avatar art is fully saturated; neutral text/background/black never is.
    /// </summary>
    private static (double Fraction, double CentroidY) SaturatedContentMeasure(byte[] bgra, int width, int height)
    {
        var matched = 0L;
        var ySum = 0L;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var offset = (y * width + x) * 4;
                int r = bgra[offset + 2];
                int g = bgra[offset + 1];
                int b = bgra[offset + 0];
                var max = Math.Max(r, Math.Max(g, b));
                var min = Math.Min(r, Math.Min(g, b));
                if (max > 60 && max - min > 60)
                {
                    matched++;
                    ySum += y;
                }
            }
        }

        var total = (long)width * height;
        return (total == 0 ? 0.0 : (double)matched / total, matched == 0 ? -1.0 : (double)ySum / matched);
    }

    /// <summary>Reads a text file another process may hold open for writing.</summary>
    private static string[] ReadAllLinesShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        var lines = new List<string>();
        while (reader.ReadLine() is { } line)
        {
            lines.Add(line);
        }

        return lines.ToArray();
    }

    /// <summary>Evaluates a samples file (+ optional trace) against a pack definition; prints named verdicts.</summary>
    public static int RunSequence(string samplesPath, string packPath, string? tracePath, TextWriter output)
    {
        if (!SyntheticAvatarPacks.TryParseDef(File.ReadAllText(packPath), out var def, out var error) || def is null)
        {
            output.WriteLine($"FAIL pack-definition — {error}");
            return 1;
        }

        var samples = File.ReadAllLines(samplesPath)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Select(l => JsonSerializer.Deserialize<SampleLine>(l) ?? throw new InvalidDataException($"bad sample line: {l}"))
            .Select(l => new AvatarSample(l.T, l.Decoded, l.Pack, l.Clip, l.Frame, l.Content, l.Cy, l.Failure))
            .ToArray();
        var trace = tracePath is null || !File.Exists(tracePath)
            ? []
            // FileShare.ReadWrite: the writer (headed app) holds the trace open for write;
            // Windows requires the READER to allow Write sharing or the open is refused.
            : ReadAllLinesShared(tracePath)
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .Select(l => JsonSerializer.Deserialize<TraceLine>(l) ?? throw new InvalidDataException($"bad trace line: {l}"))
                .Select(l => new AvatarTraceEvent(l.T, l.Kind, l.Pack))
                .ToArray();

        var verdicts = AvatarSequenceEvaluator.Evaluate(samples, trace, def);
        foreach (var verdict in verdicts)
        {
            output.WriteLine(verdict.ToString());
        }

        var failed = verdicts.Where(v => !v.Passed).ToArray();
        output.WriteLine(failed.Length == 0
            ? $"ALL VERDICTS PASSED ({verdicts.Count})"
            : $"FIRST FAILED VERDICT: {failed[0].Name} ({failed.Length}/{verdicts.Count} failed)");
        return failed.Length == 0 ? 0 : 2;
    }

    /// <summary>Serializes one trace event for the --avatar-trace file sink.</summary>
    public static string SerializeTrace(AvatarTraceEventArgs args) =>
        JsonSerializer.Serialize(new TraceLine(args.WallNowMs, args.Kind, args.PackId, args.ClipId, args.FrameIndex));

    /// <summary>The demonstrator tube's content-area background (shared with the window in Step 3).</summary>
    public const byte ContentBgR = 0x14;

    public const byte ContentBgG = 0x10;

    public const byte ContentBgB = 0x18;
}
