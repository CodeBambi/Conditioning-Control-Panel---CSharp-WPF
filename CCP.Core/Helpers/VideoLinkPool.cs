using System;
using System.Collections.Generic;

namespace ConditioningControlPanel.Helpers;

/// <summary>
/// The per-mod Hypnotube link pool rules both heads' Library editors share: which shipped links
/// are browse pages rather than videos, which URLs are usable, and how the edited rows collapse
/// into the name→URL pool saved through <c>ModService.SetUserVideoLinks</c>.
/// Moved out of WPF MainWindow.xaml.cs (IsListingUrl / PersistVideoLinks) unchanged in behaviour.
/// </summary>
public static class VideoLinkPool
{
    /// <summary>
    /// True for a HypnoTube browse/listing page (e.g. /videos/ or the site root) rather than a
    /// specific video. Deliberately narrow: a /video/... page — even a typo'd one missing .html —
    /// is still a video and stays editable.
    /// </summary>
    public static bool IsListingUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        var host = uri.Host.ToLowerInvariant();
        if (host != "hypnotube.com" && !host.EndsWith(".hypnotube.com", StringComparison.Ordinal))
            return false;
        var path = uri.AbsolutePath.TrimEnd('/').ToLowerInvariant();
        return path == "" || path == "/videos" || path == "/video";
    }

    /// <summary>An absolute http(s) URL — anything else is dropped on save.</summary>
    public static bool IsUsableUrl(string? url)
    {
        var u = url?.Trim() ?? "";
        return u.Length > 0 && Uri.TryCreate(u, UriKind.Absolute, out var uri)
               && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }

    /// <summary>
    /// Collects edited rows into a name→URL pool. Blank names are auto-titled from the URL
    /// (HtUrlHelper.DeriveTitleFromUrl); blank/invalid URLs are dropped; duplicate names are made unique.
    /// </summary>
    public static Dictionary<string, string> Build(IEnumerable<(string? Name, string? Url)> rows)
    {
        var pool = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (rawName, rawUrl) in rows)
        {
            var url = rawUrl?.Trim() ?? "";
            if (!IsUsableUrl(url)) continue; // a row with no URL isn't a link yet

            var name = rawName?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(name))
                name = HtUrlHelper.DeriveTitleFromUrl(url);

            var unique = name;
            int n = 2;
            while (pool.ContainsKey(unique) && !string.Equals(pool[unique], url, StringComparison.OrdinalIgnoreCase))
                unique = $"{name} ({n++})";
            pool[unique] = url;
        }
        return pool;
    }
}
