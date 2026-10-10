using System;
using System.Net;
using System.Net.Http;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// The one network rule for a CCP_USERDATA_DIR sandbox (tests, kc, render-all): nothing leaves the
    /// machine. <see cref="Install"/> (called by <see cref="CorePaths"/> when it honours the override)
    /// swaps <see cref="HttpClient.DefaultProxy"/> for a proxy at 127.0.0.1:0, which nothing can listen on, so every HttpClient/HttpClientHandler/SocketsHttpHandler/ClientWebSocket that does not set
    /// its own proxy - all of them in this repo - gets "connection refused" before any DNS lookup or
    /// connect to the real host. Loopback targets bypass it, so honoured LoopbackUrl overrides still
    /// work. Non-HTTP egress (WebView sources, browser launches) asks <see cref="Allows"/>.
    /// A production run never calls Install, so nothing changes there.
    /// </summary>
    public static class SandboxNet
    {
        private static readonly object Gate = new();

        /// <summary>Port 0 can never be listened on (bind(0) means "any free port"), so a connect there is refused
        /// deterministically on every OS. A real bound-but-unlistened port was the first idea, but another process
        /// using SO_REUSEADDR/SO_REUSEPORT (or Windows SO_REUSEADDR hijack semantics) could still take it and
        /// receive the traffic; port 0 has no such window and needs no held socket.</summary>
        internal static readonly Uri DeadProxy = new("http://127.0.0.1:0/");

        public static bool Active { get; private set; }

        /// <summary>The one check a feed makes before it goes to the network (hunt3 IC9: community
        /// prompts, showcase clips): never in Offline mode, and never from a CCP_USERDATA_DIR sandbox on
        /// the real transport. A transport a test injected is not the network, so the sandbox half
        /// lets it through; Offline mode still refuses it.</summary>
        public static bool FeedBlocked(bool offlineMode, bool realTransport) => FeedBlocked(offlineMode, realTransport, Active);

        internal static bool FeedBlocked(bool offlineMode, bool realTransport, bool sandboxed) =>
            offlineMode || (realTransport && sandboxed);

        public static void Install()
        {
            lock (Gate)
            {
                if (Active) return;
                var proxy = new Refuser();
                HttpClient.DefaultProxy = proxy;
#pragma warning disable SYSLIB0014 // WebRequest is obsolete; still set so nothing old slips past.
                WebRequest.DefaultWebProxy = proxy;
#pragma warning restore SYSLIB0014
                Active = true;
            }
        }

        /// <summary>True when <paramref name="uri"/> may be opened (WebView, browser, media): always
        /// outside a sandbox; inside one only loopback, local (non-UNC) files, about: and data:.</summary>
        public static bool Allows(Uri? uri) => Allows(uri, Active);

        internal static bool Allows(Uri? uri, bool sandboxed) =>
            !sandboxed || uri is { IsAbsoluteUri: true }
                && (uri.Scheme is "about" or "data" || (uri.IsFile && !uri.IsUnc) || IsLoopbackLiteral(uri));

        /// <summary>A literal loopback IP or "localhost" as typed. Not Uri.IsLoopback alone: .NET also calls the bare
        /// name "loopback" loopback, and that name is resolved by DNS/hosts like any other.</summary>
        internal static bool IsLoopbackLiteral(Uri uri) =>
            uri.IsAbsoluteUri && uri.Host.Length > 0
            // .NET rewrites the host "loopback" to "localhost" (LoopbackUrl has the same guard): only the host as typed counts.
            && uri.OriginalString.Contains("://" + uri.Host, StringComparison.OrdinalIgnoreCase)
            && (string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase)
                || (IPAddress.TryParse(uri.Host.Trim('[', ']'), out var ip) && IPAddress.IsLoopback(ip)));

        internal sealed class Refuser : IWebProxy
        {
            public ICredentials? Credentials { get; set; }
            public Uri GetProxy(Uri destination) => DeadProxy;
            public bool IsBypassed(Uri host) => IsLoopbackLiteral(host);
        }
    }
}
