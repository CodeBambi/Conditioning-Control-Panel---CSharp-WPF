namespace CcpClient.Desktop.Features.Intake;

/// <summary>
/// SP-054: the intake-save-image sink (IntakeHostService.cs:547-555, :589-635 parity): a
/// page-rendered spiral PNG written to the user's keepsake folder
/// (&lt;dataDir&gt;/intake_spirals). Discipline: 12MB base64-char ceiling (:554 →
/// "too-big"), PNG 8-byte magic validation (:628-631 → "bad-image"), index clamped 1..99
/// (:609), HOST-BUILT filename <c>intake-spiral-yyyyMMdd-HHmmss-NN.png</c> (:552, :610-612
/// — page strings never become paths), atomic .tmp-swap write, I/O failure → "io-failed".
/// Presence+shape logging only (a keepsake filename is user content).
/// </summary>
public sealed class IntakeSaveImageSink
{
    /// <summary>The 12MB base64-char ceiling (:554).</summary>
    public const int MaxBase64Chars = 12 * 1024 * 1024;

    private static readonly byte[] PngMagic = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private readonly string _folder;
    private readonly Func<DateTimeOffset> _now;
    private readonly Action<string> _log;

    public IntakeSaveImageSink(string folder, Func<DateTimeOffset>? now, Action<string> log)
    {
        _folder = folder;
        _now = now ?? (() => DateTimeOffset.Now);
        _log = log;
    }

    /// <summary>The sink outcome (the reply vocabulary: too-big / bad-image / io-failed).</summary>
    public sealed record Result(bool Ok, string? Path, string? Error);

    /// <summary>Validate + write one keepsake PNG. The filename is HOST-BUILT from the
    /// local clock + the clamped page index; a same-second same-index rewrite overwrites
    /// (the keepsake is idempotent by name, recorded).</summary>
    public Result Save(string? pngBase64, int index)
    {
        if (string.IsNullOrEmpty(pngBase64) || pngBase64.Length > MaxBase64Chars)
        {
            _log("intake: save-image refused (too-big, base64 length out of bounds)");
            return new Result(false, null, "too-big");
        }

        byte[] bytes;
        try { bytes = Convert.FromBase64String(pngBase64); }
        catch
        {
            _log("intake: save-image refused (bad-image, base64 decode)");
            return new Result(false, null, "bad-image");
        }

        if (bytes.Length < PngMagic.Length || !bytes.AsSpan(0, PngMagic.Length).SequenceEqual(PngMagic))
        {
            _log("intake: save-image refused (bad-image, PNG magic)");
            return new Result(false, null, "bad-image");
        }

        var clamped = Math.Clamp(index, 1, 99);
        var name = $"intake-spiral-{_now():yyyyMMdd-HHmmss}-{clamped:D2}.png";
        try
        {
            Directory.CreateDirectory(_folder);
            var path = Path.Combine(_folder, name);
            var tmp = path + ".tmp";
            File.WriteAllBytes(tmp, bytes);
            File.Move(tmp, path, overwrite: true);
            _log($"intake: save-image wrote keepsake ({bytes.Length} bytes)"); // presence+shape — never the filename
            return new Result(true, path, null);
        }
        catch (Exception ex)
        {
            _log($"intake: save-image failed ({ex.GetType().Name}) — io-failed");
            return new Result(false, null, "io-failed");
        }
    }
}
