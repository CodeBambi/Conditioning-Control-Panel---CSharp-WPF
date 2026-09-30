using System;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// The one network rule for a CCP_USERDATA_DIR sandbox (tests, kc, render-all): nothing leaves the
    /// machine. <see cref="Install"/> (called by <see cref="CorePaths"/> when it honours the override)
    /// swaps <see cref="HttpClient.DefaultProxy"/> for a proxy on a loopback port that is bound but never
    /// listens, so every HttpClient/HttpClientHandler/SocketsHttpHandler/ClientWebSocket that does not set
    /// its own proxy - all of them in this repo - gets "connection refused" before any DNS lookup or
    /// connect to the real host. Loopback targets bypass it, so honoured LoopbackUrl overrides still
    /// work. Non-HTTP egress (WebView sources, browser launches) asks <see cref="Allows"/>.
    /// A production run never calls Install, so nothing changes there.
    /// </summary>
    public static class SandboxNet
    {
        private static readonly object Gate = new();
        private static Socket? _deadPort;

        public static bool Active { get; private set; }

        public static void Install()
        {
            lock (Gate)
            {
                if (Active) return;
                // Bound, never Listen()ed: connects are refused, and holding it means nothing else can
                // take the port and receive the traffic.
                _deadPort = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                _deadPort.Bind(new IPEndPoint(IPAddress.Loopback, 0));
                var proxy = new Refuser(new Uri($"http://127.0.0.1:{((IPEndPoint)_deadPort.LocalEndPoint!).Port}/"));
                HttpClient.DefaultProxy = proxy;
#pragma warning disable SYSLIB0014 // WebRequest is obsolete; still set so nothing old slips past.
                WebRequest.DefaultWebProxy = proxy;
#pragma warning restore SYSLIB0014
                Active = true;
            }
        }

        /// <summary>True when <paramref name="uri"/> may be opened (WebView, browser, media): always
        /// outside a sandbox; inside one only loopback, local files, about: and data:.</summary>
        public static bool Allows(Uri? uri) => Allows(uri, Active);

        internal static bool Allows(Uri? uri, bool sandboxed) =>
            !sandboxed || uri is { IsAbsoluteUri: true }
                && (uri.Scheme is "about" or "data" || uri.IsFile || (uri.Host.Length > 0 && uri.IsLoopback));

        internal sealed class Refuser(Uri dead) : IWebProxy
        {
            public ICredentials? Credentials { get; set; }
            public Uri GetProxy(Uri destination) => dead;
            public bool IsBypassed(Uri host) => host.IsLoopback;
        }
    }
}
