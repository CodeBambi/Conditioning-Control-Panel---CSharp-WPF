using System;
using System.Threading;

namespace ConditioningControlPanel.Services.JustDrop
{
    /// <summary>What kind of request the web view is making, where the engine says.</summary>
    internal enum WebRequestKind
    {
        /// <summary>The engine does not say (the Avalonia adapter today).</summary>
        Unknown = 0,
        /// <summary>A top-level document navigation.</summary>
        Document = 1,
        /// <summary>Anything a page asks for: fetch, XHR, frame, image, script.</summary>
        SubResource = 2,
    }

    /// <summary>
    /// WPF 7.1.5 stamps the sign-in header on Document requests only
    /// (JustDropHostService.cs:189-191, CoreWebView2WebResourceContext.Document). The Avalonia
    /// adapter raises one event for every request and names no resource kind, so the port scopes the
    /// stamp by identity instead: ONE window, ONE request, the exact start url the host itself
    /// navigated to, and never again in that window's life. The page the handoff redirects to (and
    /// anything it embeds) can ask for the handoff url as often as it likes: the stamp is spent.
    ///
    /// <para>The url half of the rule is still <see cref="JustDropHostService.AuthHeaderFor(Uri?, string?)"/>
    /// (https, exact host, default port, no userinfo, exact path). The credential is never stored
    /// here: the caller passes it per request.</para>
    /// </summary>
    internal sealed class JustDropHandoffStamp
    {
        private readonly Uri? _start;
        private int _spent;

        /// <param name="startUrl">The url the window's host navigates to first.</param>
        public JustDropHandoffStamp(Uri? startUrl)
        {
            _start = startUrl is { IsAbsoluteUri: true } ? startUrl : null;
        }

        /// <summary>True once the one stamp has been handed out (tests, logs).</summary>
        public bool Spent => Volatile.Read(ref _spent) != 0;

        /// <summary>
        /// The credential for <paramref name="request"/>, or null. Non-null at most once: for the
        /// first request that is the start url itself, is not a known sub-resource, and passes the
        /// url rule.
        /// </summary>
        public string? Take(Uri? request, WebRequestKind kind, string? authToken)
        {
            if (kind == WebRequestKind.SubResource) return null;
            if (_start is null || Spent) return null;
            if (JustDropHostService.AuthHeaderFor(request, authToken) is not { Length: > 0 } token) return null;
            if (JustDropHostService.AuthHeaderFor(_start, authToken) is null) return null;   // a signed-out start url earns nothing
            if (!SameRequest(_start, request!)) return null;
            return Interlocked.Exchange(ref _spent, 1) == 0 ? token : null;
        }

        /// <summary>Scheme, host, port and path already matched the rule; the query has to be the
        /// start url's own (the engine may re-case an escape, so compare what the escapes mean).</summary>
        private static bool SameRequest(Uri start, Uri request)
        {
            if (!string.Equals(start.AbsolutePath, request.AbsolutePath, StringComparison.Ordinal)) return false;
            if (!string.IsNullOrEmpty(request.Fragment) && request.Fragment != "#") return false;
            return string.Equals(Decode(start.Query), Decode(request.Query), StringComparison.Ordinal);
        }

        private static string Decode(string query)
        {
            try { return Uri.UnescapeDataString(query ?? string.Empty); }
            catch { return query ?? string.Empty; }
        }
    }
}
