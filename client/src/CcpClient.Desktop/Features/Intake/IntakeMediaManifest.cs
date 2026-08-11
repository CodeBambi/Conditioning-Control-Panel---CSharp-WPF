using CcpClient.Desktop.Features.Dtrh;

namespace CcpClient.Desktop.Features.Intake;

/// <summary>
/// SP-054: the init media manifest (IntakeHostService.cs:260, :724-832 parity): a SMALL
/// random sample of the user's media pool — ≤18 gifs + ≤18 stills — as §4 /umedia/ URLs
/// (the ccp.assets-class route; WebGL texture upload stays untainted). Empty pool →
/// media:null (the page renders its shipped art — fresh-install parity). The mod bubble
/// sprite is null (no mod system — the unfiled mod-system row). Presence+shape logging
/// ONLY — the sampled URLs are user content, never logged (SP-018 V5 class).
/// </summary>
public static class IntakeMediaManifest
{
    /// <summary>Per-kind sample ceiling (:724-832).</summary>
    public const int MaxPerKind = 18;

    /// <summary>Sample the pool. Returns null on an empty pool (media:null on init).
    /// SP-055: <paramref name="disabled"/>/<paramref name="useWhitelist"/> pass through to
    /// the ONE active-pool definition in <c>DtrhUserMedia</c> — never a second scan that
    /// can disagree (IntakeHostService.cs:776-790 parity; #762/#798/#619 contract).</summary>
    public static object? Build(string userMediaRoot, string mediaOrigin, Action<string> log, Random? rng = null,
        HashSet<string>? disabled = null, bool useWhitelist = false)
    {
        var manifest = DtrhUserMedia.Build(userMediaRoot, mediaOrigin, log, disabled, useWhitelist);
        if (manifest.Images.Count == 0)
        {
            return null;
        }

        var random = rng ?? Random.Shared;
        var gifs = Sample(manifest.Images
            .Where(e => e.Url.EndsWith(".gif", StringComparison.OrdinalIgnoreCase))
            .Select(e => e.Url), random);
        var stills = Sample(manifest.Images
            .Where(e => !e.Url.EndsWith(".gif", StringComparison.OrdinalIgnoreCase))
            .Select(e => e.Url), random);
        log($"intake: media manifest sampled ({gifs.Count} gif(s), {stills.Count} still(s) from a pool of {manifest.Images.Count})"); // presence+shape only
        return new { gifs, images = stills, bubbleSprite = (string?)null };
    }

    private static List<string> Sample(IEnumerable<string> urls, Random rng) =>
        urls.OrderBy(_ => rng.Next()).Take(MaxPerKind).ToList();
}
