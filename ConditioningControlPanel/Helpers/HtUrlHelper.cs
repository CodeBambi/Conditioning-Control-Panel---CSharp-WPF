using System;
using System.Text.RegularExpressions;

namespace ConditioningControlPanel.Helpers;

// HypnoTube URL eligibility + parsing.
//
// MUST stay in lockstep with cclabs-web's normalizeHtUrl + extractHtVideoId in
// src/lib/server/enhancements.ts. The server is authoritative on validation —
// this client-side check exists only as a fast pre-filter so we don't show UI
// (toasts, submit buttons) for rows the server would reject anyway.
//
// Three callers today, all of which MUST agree on what counts as an HT URL:
//   1. W2 — MainWindow.xaml.cs IsCatalogueEligible (submit-button gate on
//      Deeper Library rows)
//   2. W2 — CatalogueService server-side validation (already kept in sync via
//      cclabs-web's enhancements.ts — the canonical definition)
//   3. W3 Piece 1 — CatalogueLookupService navigation hook (this prompt)
//
// If you add a new HT URL pattern, update both this helper AND
// cclabs-web's enhancements.ts in the same change.
//
// Eligibility:
//   - URL must parse as absolute http/https
//   - Host must be hypnotube.com or *.hypnotube.com
//   - Path must match one of two real-world shapes:
//       A. /video/<numeric_id>[(/|.|-)<rest>]   — id as own segment
//       B. /video/<slug>-<numeric_id>.html      — slug with trailing id
public static class HtUrlHelper
{
    private static readonly Regex HtPatternA =
        new(@"^/video/(?<id>\d+)(?:[/.\-]|$)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex HtPatternB =
        new(@"^/video/.+-(?<id>\d+)\.html$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static bool IsEligibleHtUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        if (url == "*") return false;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return false;

        var host = uri.Host.ToLowerInvariant();
        if (host != "hypnotube.com" && !host.EndsWith(".hypnotube.com", StringComparison.Ordinal))
            return false;

        var path = uri.AbsolutePath;
        return HtPatternA.IsMatch(path) || HtPatternB.IsMatch(path);
    }

    // Extract the numeric HT video id from an eligible URL, or null if the URL
    // is not an HT video page. Used for:
    //   - logging (so we don't dump full URLs that might carry tracking params)
    //   - the "Browse all on web" deep link (?video=<id> filter on the catalogue)
    //   - debouncing the in-flight toast against the *currently* shown video
    public static string? TryExtractHtVideoId(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return null;

        var host = uri.Host.ToLowerInvariant();
        if (host != "hypnotube.com" && !host.EndsWith(".hypnotube.com", StringComparison.Ordinal))
            return null;

        var path = uri.AbsolutePath;
        var matchA = HtPatternA.Match(path);
        if (matchA.Success) return matchA.Groups["id"].Value;
        var matchB = HtPatternB.Match(path);
        if (matchB.Success) return matchB.Groups["id"].Value;
        return null;
    }
}
