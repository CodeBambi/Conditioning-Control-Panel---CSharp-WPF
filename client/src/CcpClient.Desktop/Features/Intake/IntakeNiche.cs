namespace CcpClient.Desktop.Features.Intake;

/// <summary>
/// SP-054: the intake niche (IntakeNiche.cs:23-59 + the host-side bank clamp,
/// IntakeHostService.cs:674-702). Greenfield has NO mod system (the mod-system row is
/// unfiled), so the active-mod mapping is a TYPED SEAM defaulting to the WPF last-resort
/// <see cref="Fallback"/> — recorded DISTINCTLY from the bank clamp (pre-approach consult
/// ruling 8: the seam default and the clamp both land on bambi; the untested clamp must
/// not read as exercised). The clamp is real and ported: a niche whose bank file is
/// missing from the SERVED intake tree falls back to bambi (one warning per session;
/// IO error errs to "exists").
/// </summary>
public static class IntakeNiche
{
    /// <summary>The four authored niches (:23; banks/{niche}.json all present in the tree).</summary>
    public static readonly IReadOnlyList<string> All = ["bambi", "sissy", "drone", "circe"];

    /// <summary>WPF's own last-resort (:25) — and the greenfield seam default (no mod system).</summary>
    public const string Fallback = "bambi";

    /// <summary>The typed seam default (no mod system — the unfiled mod-system row owns a
    /// real source). Always <see cref="Fallback"/> this build.</summary>
    public static string CurrentFromSeam() => Fallback;

    /// <summary>The host-side bank clamp (:674-702): <paramref name="niche"/> stands only
    /// when its bank file exists under the served intake tree; otherwise bambi. Unknown
    /// niches clamp too (the page would fall back itself, web-shim.js:124 — the host never
    /// ships an unclamped value). IO error errs to "exists". Returns the clamped niche;
    /// the CALLER logs the clamp (the window boots once per launch — WPF's one-warning-per-
    /// session (:692-702) becomes one-per-boot without a static latch).</summary>
    public static string ClampToOnDiskBanks(string? niche, string intakePayloadRoot)
    {
        var candidate = string.IsNullOrWhiteSpace(niche) ? Fallback : niche.Trim().ToLowerInvariant();
        if (!All.Contains(candidate, StringComparer.Ordinal))
        {
            candidate = Fallback;
        }

        bool exists;
        try
        {
            exists = File.Exists(Path.Combine(intakePayloadRoot, "banks", candidate + ".json"));
        }
        catch
        {
            exists = true; // IO error errs to "exists" (:674-702)
        }

        return !exists && candidate != Fallback ? Fallback : candidate;
    }
}
