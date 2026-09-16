using System;

namespace ConditioningControlPanel.Services.Logging
{
    /// <summary>
    /// Turns a URL into the only part of it that is safe to write to a log file: the host.
    ///
    /// Everything after the host is content. A HypnoTube path is the title of the video the
    /// user was watching, a query string carries session tokens and signed asset URLs, and a
    /// fragment can carry either. The logging policy (CLAUDE.md) allows ids, counts, enums and
    /// status codes only, so every log line that used to interpolate a URL now interpolates
    /// <see cref="Host"/> instead.
    ///
    /// Never throws and never returns null: a log call must not be able to take the app down,
    /// and a caller must never be tempted to fall back to the raw URL on failure.
    /// </summary>
    public static class UrlLog
    {
        /// <summary>Placeholder for a URL that will not parse. Deliberately not the raw
        /// string: an unparseable "URL" is exactly the case most likely to be a pasted
        /// blob of user text.</summary>
        public const string Invalid = "<invalid>";

        /// <summary>Placeholder for a null/empty/whitespace URL.</summary>
        public const string Empty = "<none>";

        /// <summary>
        /// Host of <paramref name="url"/> (for example <c>hypnotube.com</c>), lower-cased.
        /// Returns <see cref="Empty"/> for null/blank input and <see cref="Invalid"/> when the
        /// string is not an absolute URI or carries no host (relative paths, <c>data:</c>,
        /// <c>about:blank</c>, <c>javascript:</c>). For a non-default port the port is kept
        /// (<c>127.0.0.1:20010</c>) because local haptics/dev endpoints are identified by it.
        /// </summary>
        public static string Host(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return Empty;
            try
            {
                if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)) return Invalid;
                return Host(uri);
            }
            catch
            {
                return Invalid;
            }
        }

        /// <summary>Overload for callers that already hold a <see cref="Uri"/> (WebView2
        /// navigation args, HttpRequestMessage.RequestUri).</summary>
        public static string Host(Uri? uri)
        {
            if (uri == null) return Empty;
            try
            {
                if (!uri.IsAbsoluteUri) return Invalid;
                var host = uri.Host;
                if (string.IsNullOrEmpty(host)) return Invalid;
                host = host.ToLowerInvariant();
                return uri.IsDefaultPort ? host : host + ":" + uri.Port.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
            catch
            {
                return Invalid;
            }
        }
    }
}
