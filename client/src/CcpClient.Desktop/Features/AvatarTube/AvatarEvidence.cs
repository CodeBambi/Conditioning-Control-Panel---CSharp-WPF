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

    /// <summary>One samples-file line (JSONL): capture timestamp + strip decode + content fraction + content Y centroid.</summary>
    public sealed record SampleLine(long T, bool Decoded, int Pack, int Clip, int Frame, double Content, double Cy, string? Failure);

    /// <summary>One trace-file line (JSONL), written by the participant's trace sink.</summary>
    public sealed record TraceLine(long T, string Kind, int Pack, int Clip, int Frame);

    /// <summary>Decodes one BMP capture's strip; prints the JSON sample (without timestamp — the script owns time).</summary>
    public static int StripDecode(string capturePath, TextWriter output)
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

        // The tube's content background (AvatarTubeDemonstratorWindow) — the union no-blank
        // reference. Kept as constants here so the evaluator and the window share one value.
        var (content, centroidY) = AvatarStripCodec.ContentMeasure(bgra, width, height, ContentBgR, ContentBgG, ContentBgB, tolerance: 16);
        var decoded = AvatarStripCodec.TryDecode(bgra, width, height, out var pack, out var clip, out var frame, out var failure);
        output.WriteLine(JsonSerializer.Serialize(new SampleLine(0, decoded, decoded ? pack : -1, decoded ? clip : -1, decoded ? frame : -1, content, centroidY, failure)));
        return decoded ? 0 : 2;
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
            : File.ReadAllLines(tracePath)
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
