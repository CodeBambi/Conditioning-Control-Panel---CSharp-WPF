using System;
using ConditioningControlPanel.Helpers;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// The address rule for the controller's play_hypnotube verb (owner, 2026-10-10: ported, site-locked).
    /// Stricter than WPF 7.1.5 (HtUrlHelper.IsEligibleHtUrl took http and any subdomain): the string a
    /// controller sends is the one thing crossing the trust boundary, so it must be a plain https video
    /// page on the one site, and only the parsed <see cref="Uri.AbsoluteUri"/> ever travels on.
    /// </summary>
    public static class RemoteVideoLink
    {
        public const string Refused = "not a video link from the site";
        public const int MaxLength = 300;
        public const string Site = "hypnotube.com";

        public static bool TryParse(string? raw, out Uri link)
        {
            link = null!;
            if (string.IsNullOrEmpty(raw) || raw.Length > MaxLength) return false;
            foreach (var c in raw)
            {
                // Printable ASCII only: no whitespace, no control characters, nothing a look-alike host
                // could be folded from, and none of the characters a command line or a page would quote with.
                if (c <= ' ' || c >= (char)0x7F) return false;
                if (c is '"' or '\'' or '`' or '\\' or '<' or '>' or '@') return false;
            }
            if (HasEncodedControl(raw)) return false;
            if (!raw.StartsWith("https://", StringComparison.Ordinal)) return false;
            if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri)) return false;
            if (uri.Scheme != Uri.UriSchemeHttps || !uri.IsDefaultPort || uri.UserInfo.Length != 0) return false;
            if (uri.HostNameType != UriHostNameType.Dns) return false;
            var host = uri.IdnHost;
            if (!string.Equals(host, Site, StringComparison.Ordinal) && !string.Equals(host, "www." + Site, StringComparison.Ordinal)) return false;
            // The host the parser kept must be the host that was written (no folding, no stray port).
            var authority = raw.Substring(8).Split('/', '?', '#')[0];
            if (!string.Equals(authority, host, StringComparison.Ordinal)) return false;
            if (!HtUrlHelper.IsEligibleHtUrl(uri.AbsoluteUri)) return false;   // a video page, as WPF
            link = uri;
            return true;
        }

        // %00-%1F and %7F (an encoded newline, tab or NUL), and a doubly encoded percent that could hide one.
        private static bool HasEncodedControl(string raw)
        {
            for (var i = 0; i + 2 < raw.Length; i++)
            {
                if (raw[i] != '%') continue;
                var (a, b) = (char.ToLowerInvariant(raw[i + 1]), char.ToLowerInvariant(raw[i + 2]));
                if (a is '0' or '1') return true;
                if (a == '7' && b == 'f') return true;
                if (a == '2' && b == '5') return true;
            }
            return false;
        }
    }
}
