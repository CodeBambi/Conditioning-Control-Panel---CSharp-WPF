using System;
using IOPath = System.IO.Path;

namespace ConditioningControlPanel.Avalonia.Views.Deeper
{
    /// <summary>
    /// The one definition of everything <see cref="DeeperEditorWindow"/>'s preview and
    /// <see cref="EnhancementPlayerWindow"/>'s video pane both need to drive a page through
    /// <c>WebHost.InvokeScriptAsync</c> behind a navigation fence.
    ///
    /// <para>This exists because the two windows had grown five copies of the same rules:
    /// a host fence and <see cref="Unquote"/> byte for byte, the
    /// largest-&lt;video&gt; finder four times over with four different tails, a path comparison the
    /// editor inlined and the player named <c>PathsEqual</c>, and the media extension list twice.
    /// Three of those are SECURITY predicates: a fence that drifts on one window and not the other
    /// is the failure this collapses, not a tidiness one. wire/69 spotted the drift and owned only
    /// one of the two files; this layer owns both.</para>
    ///
    /// <para>Deliberately NOT in CCP.Core. Nothing here is portable logic the engine wants - it is
    /// this head's WebKitGTK quirks (the quote stripping). The host fence calls Core's
    /// <c>UrlSafety.HostMatches</c> + <c>DeeperConfig.PreviewHostAllowlist</c>, the same rule WPF's
    /// NavigationStarting enforced.</para>
    /// </summary>
    internal static class DeeperPreview
    {
        // ---- Fences ---------------------------------------------------------------------

        /// <summary>WPF's IsAllowedPreviewHost / IsAllowedPlayerHost plus the https check both
        /// windows ran beside it (DeeperEditorWindow.xaml.cs:664, EnhancementPlayerWindow.xaml.cs:1775):
        /// the first hop AND every later hop must be https on an allowlisted domain.</summary>
        internal static bool IsAllowedPreviewHost(Uri? u)
            => u != null && u.Scheme == Uri.UriSchemeHttps
               && ConditioningControlPanel.Services.Deeper.UrlSafety.HostMatches(
                   u, ConditioningControlPanel.Services.Deeper.DeeperConfig.PreviewHostAllowlist);

        /// <summary>Full-path equality for the local-file fence. Full paths on both sides because
        /// the engine round-trips the URL through file:// encoding and hands <c>LocalPath</c> back
        /// in a form that only matches after normalisation. A path that will not normalise is not
        /// the file that was pinned.</summary>
        internal static bool PathsEqual(string? candidate, string pinnedFullPath)
        {
            if (string.IsNullOrEmpty(candidate)) return false;
            try { return string.Equals(IOPath.GetFullPath(candidate), pinnedFullPath, StringComparison.Ordinal); }
            catch { return false; }
        }

        /// <summary>Extension gate for a local media path. Not a content sniff - the cheap half of
        /// "is this media", run BEFORE a path reaches the engine, so a shared .ccpenh.json naming
        /// /etc/passwd is not rendered as a page. The list matches the project model's, which is why
        /// .mkv and .avi are in it even though a media document may show black for them.</summary>
        internal static bool IsLocalVideoFile(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            var ext = IOPath.GetExtension(path).ToLowerInvariant();
            return ext is ".mp4" or ".webm" or ".mkv" or ".mov" or ".avi" or ".m4v";
        }

        // ---- The script channel ---------------------------------------------------------

        /// <summary>Binds <c>l</c> to the page's LARGEST &lt;video&gt;, which was
        /// BrowserVideoTimeSource's rule too: a HypnoTube page carries preview thumbnails that are
        /// also video elements, and the first in document order is routinely not the one being
        /// watched. Answers "" when the page has no video at all, so a caller can tell "did
        /// nothing" from "did it".</summary>
        private const string Find =
            "(function(){var l=null,a=0;document.querySelectorAll('video').forEach(function(v){"
            + "var s=(v.clientWidth||0)*(v.clientHeight||0); if(s>=a){a=s;l=v;}});"
            + "if(!l)return '';";

        /// <summary>One-shot, in place of WPF's injected scrollIntoView poller: HT stacks promo
        /// banners above the player, so without it the preview lands at scrollTop=0 with the video
        /// offscreen. The flag lives on the element, so a navigation resets it for free.</summary>
        private const string ScrollOnce =
            "if(!l._ccpScrolled){l._ccpScrolled=1;try{l.scrollIntoView({block:'center'});}catch(e){}}";

        /// <summary>Wraps <paramref name="body"/> - JS statements, with <c>l</c> bound, carrying
        /// their own <c>return</c> - in the finder.</summary>
        internal static string LargestVideo(string body, bool scrollIntoView = false)
            => Find + (scrollIntoView ? ScrollOnce : "") + body + "})()";

        /// <summary>"currentTime|duration|paused" off the page's largest video, or "".</summary>
        internal static string ReadTime(bool scrollIntoView = false)
            => LargestVideo("return l.currentTime+'|'+(l.duration||0)+'|'+(l.paused?0:1);", scrollIntoView);

        /// <summary>Runs <paramref name="statements"/> against <c>l</c> and answers "ok", so a
        /// caller can tell a page that acted from one with no video (which answers "").</summary>
        internal static string Invoke(string statements, bool scrollIntoView = false)
            => LargestVideo(statements + "return 'ok';", scrollIntoView);

        // ---- The answer -----------------------------------------------------------------

        /// <summary>WebView2 hands back a JSON literal (a string arrives quoted); WebKitGTK hands
        /// back the raw value. Strip one layer of quotes so the caller sees the same on both.</summary>
        internal static string Unquote(string? s)
            => string.IsNullOrEmpty(s) ? "" : (s.Length >= 2 && s[0] == '"' && s[^1] == '"' ? s[1..^1] : s);
    }
}
