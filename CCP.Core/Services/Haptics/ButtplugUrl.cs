using System;
using System.Linq;

namespace ConditioningControlPanel.Services.Haptics
{
    /// <summary>
    /// Cleans up the Intiface / Buttplug server address a user types (ccp-bugs #1310: a user typed
    /// <c>localhost.12345</c> and the app said the server was not running). PURE.
    /// <list type="bullet">
    /// <item>blank means the default, <c>ws://127.0.0.1:12345</c>;</item>
    /// <item>a missing scheme gets <c>ws://</c>; <c>http</c>/<c>https</c> become <c>ws</c>/<c>wss</c>;</item>
    /// <item>a dot before a trailing port number becomes a colon, but only when the tail is all
    /// digits, there is no port already, and the host would not otherwise be a plain IPv4
    /// address (so <c>ws://192.168.1.20</c> keeps its address);</item>
    /// <item>a bare number is a port on 127.0.0.1, and a <c>ws://</c> address with no port gets
    /// Intiface's 12345 (ws would otherwise mean 80);</item>
    /// <item>anything that still is not a ws/wss address with a host comes back null with a
    /// reason, so the UI can say what is wrong instead of "make sure it is running".</item>
    /// </list>
    /// </summary>
    public static class ButtplugUrl
    {
        public const string Default = "ws://127.0.0.1:12345";

        /// <summary>The address to connect to, or null when it cannot be made valid
        /// (<paramref name="error"/> then says why, in plain words).</summary>
        public static string? Normalize(string? raw, out string? error)
        {
            error = null;
            var text = (raw ?? "").Trim();
            if (text.Length == 0) return Default;
            if (text.Any(char.IsWhiteSpace))
            {
                error = "it has a space in it";
                return null;
            }

            var sep = text.IndexOf("://", StringComparison.Ordinal);
            string scheme, rest;
            if (sep < 0) { scheme = "ws"; rest = text; }
            else { scheme = text.Substring(0, sep).ToLowerInvariant(); rest = text.Substring(sep + 3); }

            scheme = scheme switch { "http" => "ws", "https" => "wss", _ => scheme };
            if (scheme != "ws" && scheme != "wss")
            {
                error = "it has to start with ws://";
                return null;
            }

            // Split the authority (host[:port]) from any path.
            var slash = rest.IndexOf('/');
            var authority = slash < 0 ? rest : rest.Substring(0, slash);
            var path = slash < 0 ? "" : rest.Substring(slash);
            // A bare number is a port on this PC, never the IPv4 address 0.0.48.57.
            if (authority.Length > 0 && authority.All(char.IsDigit))
            {
                if (!int.TryParse(authority, out var bare) || bare < 1 || bare > 65535)
                {
                    error = "it is not a valid address";
                    return null;
                }
                authority = "127.0.0.1:" + bare;
            }
            authority = FixDotPort(authority);
            // No port on ws:// means Intiface's own 12345, not ws's 80. Intiface serves plain ws,
            // so a wss:// address is a tunnel or a proxy and keeps its TLS default.
            if (scheme == "ws" && !HasPort(authority)) authority += ":12345";

            var candidate = $"{scheme}://{authority}{path}";
            if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.Host))
            {
                error = "it is not a valid address";
                return null;
            }
            // A slash typo ("ws:/localhost:12345") leaves the scheme behind as the host.
            if (uri.Host is "ws" or "wss" or "http" or "https")
            {
                error = "it has to start with ws://";
                return null;
            }
            return candidate;
        }

        /// <summary>Whether the authority names a port. An IPv6 literal's colons are not one.</summary>
        private static bool HasPort(string authority)
        {
            var host = authority.Substring(authority.LastIndexOf('@') + 1);
            return host.IndexOf(':', host.LastIndexOf(']') + 1) >= 0;
        }

        /// <summary><c>localhost.12345</c> to <c>localhost:12345</c>. Left alone when there is a
        /// colon already (a port, or IPv6), the tail is not 1 to 5 digits, or the host is a bare
        /// IPv4 address that happens to end in a number.</summary>
        internal static string FixDotPort(string authority)
        {
            if (authority.Contains(':') || authority.Contains('@')) return authority;
            var dot = authority.LastIndexOf('.');
            if (dot <= 0 || dot == authority.Length - 1) return authority;

            var tail = authority.Substring(dot + 1);
            if (tail.Length > 5 || !tail.All(char.IsDigit)) return authority;
            if (!int.TryParse(tail, out var port) || port < 1 || port > 65535) return authority;

            // "192.168.1.20" has four numeric labels and is an address, not host + port.
            var labels = authority.Split('.');
            if (labels.All(l => l.Length > 0 && l.All(char.IsDigit)) && labels.Length <= 4) return authority;

            return authority.Substring(0, dot) + ":" + tail;
        }
    }
}
