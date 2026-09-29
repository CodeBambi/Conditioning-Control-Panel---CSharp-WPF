using System;
using System.Diagnostics.CodeAnalysis;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// The one rule for honouring a test/sandbox override URL (CCP_CONTENT_BASE_URL,
    /// CCP_UPDATE_API_URL): http(s), a literal loopback IP or "localhost", no user info. An
    /// environment variable must never be able to point an install at someone else's server.
    /// </summary>
    public static class LoopbackUrl
    {
        public static bool IsHonoured(string? url, [NotNullWhen(true)] out Uri? uri) =>
            Uri.TryCreate(url, UriKind.Absolute, out uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            && uri.IsLoopback
            && (uri.HostNameType is UriHostNameType.IPv4 or UriHostNameType.IPv6 || uri.Host == "localhost")
            // .NET rewrites the host "loopback" (and short IPv4 forms) to a loopback name; only the
            // literal as typed counts.
            && url!.Contains("://" + uri.Host, StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrEmpty(uri.UserInfo);
    }
}
