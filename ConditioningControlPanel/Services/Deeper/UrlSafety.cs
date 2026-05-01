using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace ConditioningControlPanel.Services.Deeper
{
    /// <summary>
    /// Defensive URL/path checks for the Deeper editor. The editor accepts user-
    /// supplied URLs (HypnoTube/TikTok pastes, ccp: references inside scraped
    /// descriptions) and file paths from shared .ccpenh.json files; both are
    /// untrusted and need to be filtered before any network or filesystem hit.
    ///
    /// Threat model: a malicious enhancement file shared between users, a hostile
    /// URL pasted into the editor, or a redirect from an allowlisted host into
    /// a private-IP target (cloud metadata, local services, link-local).
    /// </summary>
    internal static class UrlSafety
    {
        /// <summary>True when the parsed URI's host equals one of <paramref name="domains"/>
        /// or is a subdomain of one. Comparison is case-insensitive on the parsed host
        /// (no substring matching against the raw URL).</summary>
        public static bool HostMatches(Uri uri, params string[] domains)
        {
            if (uri == null || domains == null) return false;
            var host = uri.Host;
            if (string.IsNullOrEmpty(host)) return false;
            foreach (var d in domains)
            {
                if (string.IsNullOrEmpty(d)) continue;
                if (host.Equals(d, StringComparison.OrdinalIgnoreCase)) return true;
                if (host.EndsWith("." + d, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        /// <summary>Returns "host/path" with the query string and fragment removed.
        /// Used for log lines so signed URLs (with ?token= etc) don't end up in
        /// crash.log per the project's PII purge policy. On parse failure
        /// returns "[invalid url]" so callers don't accidentally fall back to
        /// the raw string.</summary>
        public static string RedactUrl(string? url)
        {
            if (string.IsNullOrEmpty(url)) return "";
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return "[invalid url]";
            var host = uri.Host;
            var path = uri.AbsolutePath;
            if (string.IsNullOrEmpty(path) || path == "/") return host;
            return host + path;
        }

        /// <summary>Rejects non-https URLs and any host that resolves to a loopback,
        /// link-local, private, or unique-local-address. Cloud metadata
        /// (169.254.169.254) is blocked by the link-local check. Returns true
        /// when the URL is safe to fetch.</summary>
        public static async Task<bool> IsSafePublicHttpsAsync(Uri uri, CancellationToken ct)
        {
            if (uri == null) return false;
            if (uri.Scheme != Uri.UriSchemeHttps) return false;
            if (string.IsNullOrEmpty(uri.Host)) return false;

            try
            {
                IPAddress[] addrs;
                if (IPAddress.TryParse(uri.Host, out var literal))
                {
                    addrs = new[] { literal };
                }
                else
                {
                    addrs = await Dns.GetHostAddressesAsync(uri.Host, ct).ConfigureAwait(false);
                }
                if (addrs == null || addrs.Length == 0) return false;
                foreach (var ip in addrs)
                {
                    if (IsPrivateOrReservedIp(ip)) return false;
                }
                return true;
            }
            catch (OperationCanceledException) { return false; }
            catch
            {
                // DNS failure — fail closed.
                return false;
            }
        }

        /// <summary>True for any IP we never want the editor to talk to: loopback,
        /// link-local (incl. cloud metadata 169.254.169.254), site-local IPv6,
        /// IPv4 private (10/8, 172.16/12, 192.168/16), unique-local IPv6 (fc00::/7),
        /// multicast, and the unspecified address.</summary>
        public static bool IsPrivateOrReservedIp(IPAddress ip)
        {
            if (ip == null) return true;
            if (IPAddress.IsLoopback(ip)) return true;
            if (ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.IPv6Any)) return true;

            if (ip.AddressFamily == AddressFamily.InterNetwork)
            {
                var b = ip.GetAddressBytes();
                // 10.0.0.0/8
                if (b[0] == 10) return true;
                // 172.16.0.0/12
                if (b[0] == 172 && (b[1] & 0xF0) == 16) return true;
                // 192.168.0.0/16
                if (b[0] == 192 && b[1] == 168) return true;
                // 169.254.0.0/16 (link-local, includes cloud metadata)
                if (b[0] == 169 && b[1] == 254) return true;
                // 127.0.0.0/8 covered by IsLoopback above
                // 0.0.0.0/8
                if (b[0] == 0) return true;
                // 100.64.0.0/10 (CGNAT)
                if (b[0] == 100 && (b[1] & 0xC0) == 64) return true;
                // 224.0.0.0/4 multicast
                if (b[0] >= 224 && b[0] <= 239) return true;
                return false;
            }

            if (ip.AddressFamily == AddressFamily.InterNetworkV6)
            {
                if (ip.IsIPv6LinkLocal) return true;
                if (ip.IsIPv6SiteLocal) return true;
                if (ip.IsIPv6Multicast) return true;
                var b = ip.GetAddressBytes();
                // fc00::/7 unique local
                if ((b[0] & 0xFE) == 0xFC) return true;
                // ::ffff:0:0/96 IPv4-mapped — re-check as IPv4
                if (ip.IsIPv4MappedToIPv6)
                {
                    return IsPrivateOrReservedIp(ip.MapToIPv4());
                }
                return false;
            }

            // Unknown family — fail closed.
            return true;
        }

        /// <summary>
        /// Build a SocketsHttpHandler whose ConnectCallback resolves the target
        /// host inside the connect step and rejects any IP that fails
        /// <see cref="IsPrivateOrReservedIp"/>. This collapses the DNS-rebind
        /// window: even if a hostile DNS server returns a public IP at
        /// pre-flight validation time and 127.0.0.1 a millisecond later, the
        /// connect-time resolve catches it before any bytes leave the box.
        ///
        /// Caller still controls AllowAutoRedirect on the returned handler;
        /// the existing fetchers do manual redirect handling so each hop's
        /// host re-runs through this connect-time guard.
        /// </summary>
        public static SocketsHttpHandler CreateGuardedHandler()
        {
            var handler = new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                AutomaticDecompression = System.Net.DecompressionMethods.All,
                ConnectTimeout = TimeSpan.FromSeconds(10),
                PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            };
            handler.ConnectCallback = async (ctx, ct) =>
            {
                var ep = ctx.DnsEndPoint;
                IPAddress[] addrs;
                if (IPAddress.TryParse(ep.Host, out var literal))
                {
                    addrs = new[] { literal };
                }
                else
                {
                    addrs = await Dns.GetHostAddressesAsync(ep.Host, ct).ConfigureAwait(false);
                }
                if (addrs == null || addrs.Length == 0)
                    throw new IOException($"DNS resolution failed for {ep.Host}.");
                foreach (var ip in addrs)
                {
                    if (IsPrivateOrReservedIp(ip))
                        throw new IOException($"Refusing to connect to private/reserved IP {ip} for {ep.Host}.");
                }

                var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                try
                {
                    await socket.ConnectAsync(addrs, ep.Port, ct).ConfigureAwait(false);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch
                {
                    socket.Dispose();
                    throw;
                }
            };
            return handler;
        }

        /// <summary>True when <paramref name="path"/> is a relative, well-formed local
        /// path that resolves under <paramref name="baseDir"/>. Rejects UNC
        /// (\\server\share), rooted absolutes (C:\... or /...), and any path
        /// containing traversal segments. Returns false (and clears
        /// <paramref name="resolved"/>) on any failure.
        ///
        /// Relative paths supplied in shared .ccpenh.json files are resolved
        /// against <paramref name="baseDir"/> (typically App.EffectiveAssetsPath).</summary>
        public static bool TryResolveLocalPath(string? path, string? baseDir, out string resolved)
        {
            resolved = "";
            if (string.IsNullOrWhiteSpace(path)) return false;
            if (string.IsNullOrWhiteSpace(baseDir)) return false;

            // UNC and extended-length prefixes ("\\?\…", "\\.\…", "\\server\…").
            if (path.StartsWith("\\\\", StringComparison.Ordinal)) return false;
            if (path.StartsWith("//", StringComparison.Ordinal)) return false;

            // Reject obviously rooted paths — only relative paths are allowed for
            // shared files. Local-only authoring uses an absolute MediaSource that
            // bypasses this helper.
            if (System.IO.Path.IsPathRooted(path)) return false;

            try
            {
                var combined = System.IO.Path.GetFullPath(System.IO.Path.Combine(baseDir, path));
                var baseFull = System.IO.Path.GetFullPath(baseDir);
                if (!baseFull.EndsWith(System.IO.Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal))
                    baseFull += System.IO.Path.DirectorySeparatorChar;
                if (!combined.StartsWith(baseFull, StringComparison.OrdinalIgnoreCase)) return false;
                resolved = combined;
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>True when the supplied absolute local path is safe to open
        /// directly (no UNC, no extended-length prefix). Used for the legacy
        /// authoring flow where the user picks a file off their own disk via
        /// the OpenFileDialog — those paths are trusted as user input but we
        /// still want to reject network shares baked into shared files.</summary>
        public static bool IsSafeLocalAbsolute(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            if (path.StartsWith("\\\\", StringComparison.Ordinal)) return false;
            if (path.StartsWith("//", StringComparison.Ordinal)) return false;
            if (path.IndexOf("://", StringComparison.Ordinal) >= 0) return false;
            try
            {
                var full = System.IO.Path.GetFullPath(path);
                if (full.StartsWith("\\\\", StringComparison.Ordinal)) return false;
                return System.IO.Path.IsPathRooted(full);
            }
            catch
            {
                return false;
            }
        }
    }
}
