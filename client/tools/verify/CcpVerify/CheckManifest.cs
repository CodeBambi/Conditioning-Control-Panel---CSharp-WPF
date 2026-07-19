using System.Text.Json;

namespace CcpVerify;

/// <summary>
/// The named-check manifest (client/docs/verification-harness.md §schema). Every check
/// declares its evidence class; regions are capture-relative (band or fractional rect),
/// never absolute pixels; the pass criterion is a FRACTION of sampled pixels so one
/// manifest is valid at Windows scale 1.0 and WSLg scale 1.5.
/// </summary>
public sealed record ManifestCheck
{
    public required string Name { get; init; }
    public required string Surface { get; init; }
    public required string State { get; init; }
    public required string EvidenceClass { get; init; }
    public required string Kind { get; init; }
    public required CheckRegion Region { get; init; }
    public required string ExpectedColor { get; init; }
    public required int Tolerance { get; init; }
    public required double MinPixelFraction { get; init; }
}

public sealed record CheckRegion
{
    public string? Band { get; init; }
    public int? ThicknessPx { get; init; }
    public RectFraction? Rect { get; init; }
}

public sealed record RectFraction
{
    public required double X { get; init; }
    public required double Y { get; init; }
    public required double W { get; init; }
    public required double H { get; init; }
}

public static class CheckManifest
{
    public const string KindBorderColorBand = "border-color-band";
    public const string KindRegionColor = "region-color";
    public const string EvidenceDraw = "draw-verified";
    public const string EvidencePresentation = "presentation-verified";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    /// <summary>Loads and validates the manifest. Every problem is a typed error, never a silent skip.</summary>
    public static IReadOnlyList<ManifestCheck> Load(string path)
    {
        using var stream = File.OpenRead(path);
        var doc = JsonSerializer.Deserialize<ManifestDocument>(stream, JsonOptions)
            ?? throw new InvalidDataException($"Manifest '{path}' is empty or not a JSON object.");
        if (doc.Version != 1)
        {
            throw new InvalidDataException($"Manifest '{path}' has unsupported version {doc.Version} (expected 1).");
        }

        var checks = doc.Checks ?? throw new InvalidDataException($"Manifest '{path}' has no 'checks' array.");
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var check in checks)
        {
            Validate(check, path);
            if (!names.Add(check.Name))
            {
                throw new InvalidDataException($"Manifest '{path}': duplicate check name '{check.Name}'.");
            }
        }

        return checks;
    }

    private static void Validate(ManifestCheck check, string path)
    {
        static void Require(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidDataException(message);
            }
        }

        var prefix = $"Manifest '{path}', check '{check.Name}':";
        Require(!string.IsNullOrWhiteSpace(check.Name), $"{prefix} name is required.");
        Require(!string.IsNullOrWhiteSpace(check.Surface), $"{prefix} surface is required.");
        Require(!string.IsNullOrWhiteSpace(check.State), $"{prefix} state is required.");
        Require(check.EvidenceClass is EvidenceDraw or EvidencePresentation,
            $"{prefix} evidenceClass must be '{EvidenceDraw}' or '{EvidencePresentation}'.");
        Require(check.Kind is KindBorderColorBand or KindRegionColor,
            $"{prefix} unknown kind '{check.Kind}'.");
        Require(check.Tolerance is >= 0 and <= 255, $"{prefix} tolerance must be 0..255.");
        Require(check.MinPixelFraction is > 0.0 and <= 1.0, $"{prefix} minPixelFraction must be (0,1].");
        _ = ParseColor(check.ExpectedColor, prefix);

        if (check.Kind == KindBorderColorBand)
        {
            Require(check.Region.Band is "top" or "bottom" or "left" or "right",
                $"{prefix} border-color-band requires region.band top|bottom|left|right.");
            Require(check.Region.ThicknessPx is > 0, $"{prefix} border-color-band requires region.thicknessPx > 0.");
        }
        else
        {
            var rect = check.Region.Rect;
            Require(rect is not null, $"{prefix} region-color requires region.rect.");
            Require(rect!.X >= 0 && rect.Y >= 0 && rect.W > 0 && rect.H > 0 && rect.X + rect.W <= 1.0 && rect.Y + rect.H <= 1.0,
                $"{prefix} region.rect fractions must lie inside [0,1].");
        }
    }

    /// <summary>Parses #RRGGBB into (R,G,B). Typed error on any other shape.</summary>
    public static (byte R, byte G, byte B) ParseColor(string color, string errorPrefix)
    {
        if (color.Length == 7 && color[0] == '#'
            && int.TryParse(color.AsSpan(1, 2), System.Globalization.NumberStyles.HexNumber, null, out var r)
            && int.TryParse(color.AsSpan(3, 2), System.Globalization.NumberStyles.HexNumber, null, out var g)
            && int.TryParse(color.AsSpan(5, 2), System.Globalization.NumberStyles.HexNumber, null, out var b))
        {
            return ((byte)r, (byte)g, (byte)b);
        }

        throw new InvalidDataException($"{errorPrefix} expectedColor must be #RRGGBB, got '{color}'.");
    }

    private sealed record ManifestDocument
    {
        public int Version { get; init; }
        public ManifestCheck[]? Checks { get; init; }
    }
}
