using System;
using System.Linq;
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

    // Derive a human-readable title from a HypnoTube (or any) video URL when the user
    // didn't supply a name. Takes the last path segment, strips a trailing ".html" (even a
    // doubled "...html.html"), drops the trailing "-<numeric_id>", and Title-Cases the rest.
    // Falls back to "this video" when there's no usable slug (e.g. a bare listing URL like
    // /videos/). Shared by the pool editor (blank name), the prompt builder, and the speech
    // bubble renderer so all three agree on what an unnamed link is called.
    public static string DeriveTitleFromUrl(string? url)
    {
        const string Fallback = "this video";
        if (string.IsNullOrWhiteSpace(url)) return Fallback;

        // Last path segment (ignore query/fragment), tolerant of trailing slashes/dots.
        var core = url.Split('?', '#')[0].TrimEnd('/');
        var slug = core.Split('/').LastOrDefault()?.Trim().Trim('.') ?? "";

        // Strip one or more trailing ".html" suffixes (handles the "...95541.html.html" typo).
        while (slug.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
            slug = slug[..^5];

        // Drop a trailing "-<id>" video id, then any leftover trailing punctuation.
        slug = Regex.Replace(slug, @"-\d+$", "").Trim('-', '.', ' ');

        if (string.IsNullOrWhiteSpace(slug)) return Fallback;

        var title = System.Globalization.CultureInfo.CurrentCulture.TextInfo
            .ToTitleCase(slug.Replace('-', ' ').Replace('_', ' '));
        return string.IsNullOrWhiteSpace(title) ? Fallback : title;
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
