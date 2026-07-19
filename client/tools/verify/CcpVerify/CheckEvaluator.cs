namespace CcpVerify;

/// <summary>Result of evaluating one named check against one capture.</summary>
public sealed record CheckResult(string Name, bool Passed, int Matched, int Sampled, double Fraction)
{
    public override string ToString() =>
        $"{(Passed ? "PASS" : "FAIL")} {Name} — {Matched}/{Sampled} pixels matched (fraction {Fraction:F3})";
}

/// <summary>
/// Deterministic named-check evaluation against a decoded capture. Pure logic — no
/// Avalonia, no I/O — so CcpClient.Tests exercises every branch on synthetic buffers.
/// </summary>
public static class CheckEvaluator
{
    /// <summary>Evaluates one check. The capture belongs to the check's surface+state; the caller scopes selection.</summary>
    public static CheckResult Evaluate(ManifestCheck check, DecodedImage image)
    {
        var (x0, y0, x1, y1) = RegionBounds(check, image);
        var expected = CheckManifest.ParseColor(check.ExpectedColor, $"check '{check.Name}':");
        var matched = 0;
        var sampled = 0;
        for (var y = y0; y < y1; y++)
        {
            for (var x = x0; x < x1; x++)
            {
                sampled++;
                var (r, g, b) = image.PixelAt(x, y);
                if (Math.Abs(r - expected.R) <= check.Tolerance
                    && Math.Abs(g - expected.G) <= check.Tolerance
                    && Math.Abs(b - expected.B) <= check.Tolerance)
                {
                    matched++;
                }
            }
        }

        var fraction = sampled == 0 ? 0.0 : (double)matched / sampled;
        return new CheckResult(check.Name, fraction >= check.MinPixelFraction, matched, sampled, fraction);
    }

    /// <summary>Resolves the capture-relative region to pixel bounds [x0,y1) — never absolute coordinates in the manifest.</summary>
    private static (int X0, int Y0, int X1, int Y1) RegionBounds(ManifestCheck check, DecodedImage image)
    {
        if (check.Kind == CheckManifest.KindBorderColorBand)
        {
            var t = Math.Min(check.Region.ThicknessPx!.Value, check.Region.Band is "top" or "bottom" ? image.Height : image.Width);
            return check.Region.Band switch
            {
                "top" => (0, 0, image.Width, t),
                "bottom" => (0, image.Height - t, image.Width, image.Height),
                "left" => (0, 0, t, image.Height),
                _ => (image.Width - t, 0, image.Width, image.Height), // "right" — validated at load
            };
        }

        var rect = check.Region.Rect!;
        var x0 = (int)Math.Round(rect.X * image.Width);
        var y0 = (int)Math.Round(rect.Y * image.Height);
        var x1 = Math.Min(image.Width, (int)Math.Round((rect.X + rect.W) * image.Width));
        var y1 = Math.Min(image.Height, (int)Math.Round((rect.Y + rect.H) * image.Height));
        return (x0, y0, Math.Max(x1, x0 + 1), Math.Max(y1, y0 + 1));
    }

    /// <summary>Selects checks for one surface+state and evaluates them in manifest order.</summary>
    public static IReadOnlyList<CheckResult> EvaluateCapture(
        IReadOnlyList<ManifestCheck> checks, string surface, string state, DecodedImage image)
    {
        var selected = checks
            .Where(c => string.Equals(c.Surface, surface, StringComparison.Ordinal)
                     && string.Equals(c.State, state, StringComparison.Ordinal))
            .ToList();
        if (selected.Count == 0)
        {
            throw new InvalidDataException($"Manifest has no checks for surface '{surface}' state '{state}'.");
        }

        return selected.Select(c => Evaluate(c, image)).ToList();
    }
}
