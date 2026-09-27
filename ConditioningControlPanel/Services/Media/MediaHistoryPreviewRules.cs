using System;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services
{
    /// <summary>What the media-log preview pane should do with one history entry.</summary>
    public enum MediaPreviewKind
    {
        /// <summary>Nothing selected.</summary>
        None,
        /// <summary>A file on this disk that is still there: decode and play it.</summary>
        LocalFile,
        /// <summary>A file on this disk that has gone: "file not found".</summary>
        LocalMissing,
        /// <summary>An online item whose bytes are still in the remote cache: decode from memory.</summary>
        RemoteCached,
        /// <summary>An online item that was streamed and is not held any more: say so, offer the source.</summary>
        RemoteUncached,
    }

    /// <summary>
    /// The preview pane's whole decision for one row: which surface to show, what the path line
    /// reads, and which of the four actions are live.
    /// </summary>
    public sealed class MediaPreviewPlan
    {
        public MediaPreviewKind Kind { get; init; } = MediaPreviewKind.None;

        /// <summary>The entry came off the internet rather than the user's library.</summary>
        public bool IsRemote { get; init; }

        /// <summary>Text for the path line under the preview (never the stored value: see
        /// <see cref="MediaHistoryPreviewRules.SourceText"/>).</summary>
        public string SourceText { get; init; } = "";

        public bool CanOpenFolder { get; init; }
        public bool CanOpenFile { get; init; }

        /// <summary>Copy the source to the clipboard. Any online entry can, including plain http.</summary>
        public bool CanCopyLink { get; init; }

        /// <summary>Hand the source to the browser. https only - we do not launch a plain-http page.</summary>
        public bool CanOpenSource { get; init; }

        /// <summary>
        /// The exact string to hand the browser, or null when this entry must not be launched.
        /// PARSED and re-serialised, never the logged text: see
        /// <see cref="MediaHistoryPreviewRules.BrowsableUrl"/>.
        /// </summary>
        public string? BrowseUrl { get; init; }

        /// <summary>
        /// Offer a deliberate one-off fetch so the user can still see what played. False without
        /// remote-media consent: this window is not a way around the app-wide gate.
        /// </summary>
        public bool CanLoadPreview { get; init; }
    }

    /// <summary>
    /// Pure decision logic behind the media log's preview pane (ccp-bugs #1233, #1237).
    ///
    /// The log stores one string per entry and both kinds of media land in it: a library path for
    /// local files, and the absolute source URL for anything the online feed showed (remote stills
    /// decode from bytes and LibVLC plays a remote clip straight FromLocation, so neither ever has
    /// a file of its own). The pane used to key everything on <c>File.Exists</c>, so every online
    /// item drew the empty "file not found" card, and the path line ran through the backslash
    /// tidy-up meant for library paths, which turned the URL into "https:\\...". Hence: blank
    /// preview, unusable link.
    /// </summary>
    public static class MediaHistoryPreviewRules
    {
        /// <summary>True when the stored value is an absolute http(s) URL, not a disk path.</summary>
        public static bool IsRemote(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            if (!Uri.TryCreate(path, UriKind.Absolute, out var uri)) return false;
            return uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps;
        }

        /// <summary>
        /// The string to hand the browser for this entry, or null when it must not be launched.
        ///
        /// <para>THE LOGGED STRING IS NEVER LAUNCHED. It came off a third-party feed, and
        /// <see cref="Helpers.BrowserLauncher"/>'s last two fallbacks - for the machines with no
        /// default browser association, which is the whole reason that helper exists - interpolate
        /// what they are given into <c>cmd.exe /c start "" "{url}"</c> and
        /// <c>rundll32 url.dll,FileProtocolHandler {url}</c>. <c>Uri.TryCreate</c> is happy with
        /// <c>https://a.example.com/x.jpg"&amp;calc&amp;"</c> - the scheme really is https and
        /// <c>OriginalString</c> keeps the quote - so a poisoned feed item would be a command line
        /// on exactly the machines that reach those fallbacks.</para>
        ///
        /// <para>So: https only, refuse outright anything whose original text carries a double
        /// quote or a control character, and hand back <c>AbsoluteUri</c> - the parsed,
        /// percent-escaped form - rather than what was stored.</para>
        /// </summary>
        public static string? BrowsableUrl(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            foreach (var c in path!)
            {
                if (c == '"' || char.IsControl(c)) return null;
            }
            if (!Uri.TryCreate(path, UriKind.Absolute, out var uri)) return null;
            if (uri.Scheme != Uri.UriSchemeHttps) return null;

            var absolute = uri.AbsoluteUri;
            // AbsoluteUri escapes, but a belt on the way out costs nothing and the alternative
            // failure is a shell.
            foreach (var c in absolute)
            {
                if (c == '"' || char.IsControl(c)) return null;
            }
            return absolute;
        }

        /// <summary>True when we are willing to hand this source to the user's browser.</summary>
        public static bool IsBrowsable(string? path) => BrowsableUrl(path) != null;

        /// <summary>
        /// Display form of the stored value. A library path can reach the log with forward slashes
        /// from whichever writer produced it (#1108), so those are tidied; a URL is left exactly as
        /// stored, because the user is going to copy it.
        /// </summary>
        public static string SourceText(string? path)
        {
            if (string.IsNullOrEmpty(path)) return "";
            if (IsRemote(path)) return path;
            return path.Replace('/', '\\');
        }

        /// <summary>
        /// The preview pane's plan for one entry. <paramref name="localExists"/> is the caller's
        /// <c>File.Exists</c>, <paramref name="remoteCached"/> its
        /// <c>RemoteMediaCache.IsCached</c> and <paramref name="remoteConsent"/> the app-wide
        /// remote-media gate; all three are passed in so this stays testable and free of disk,
        /// network and settings.
        /// </summary>
        public static MediaPreviewPlan Plan(string? path, MediaType type, bool localExists,
            bool remoteCached, bool remoteConsent = false)
        {
            if (string.IsNullOrWhiteSpace(path))
                return new MediaPreviewPlan();

            if (IsRemote(path))
            {
                // A remote clip has no local file and MediaElement would have to stream it back off
                // the CDN to show one frame, so only stills get the cached-bytes path.
                bool canDraw = remoteCached && type == MediaType.Image;
                var browse = BrowsableUrl(path);
                return new MediaPreviewPlan
                {
                    Kind = canDraw ? MediaPreviewKind.RemoteCached : MediaPreviewKind.RemoteUncached,
                    IsRemote = true,
                    SourceText = SourceText(path),
                    CanOpenFolder = false,
                    CanOpenFile = false,
                    CanCopyLink = true,
                    CanOpenSource = browse != null,
                    BrowseUrl = browse,
                    // Fetching again is reaching a remote host, so it answers to the same gate
                    // every other surface does (MediaSource != "local" && HasRemoteMediaConsent).
                    // The button is hidden rather than refusing a press.
                    CanLoadPreview = !canDraw && type == MediaType.Image && remoteConsent,
                };
            }

            return new MediaPreviewPlan
            {
                Kind = localExists ? MediaPreviewKind.LocalFile : MediaPreviewKind.LocalMissing,
                IsRemote = false,
                SourceText = SourceText(path),
                // The reveal helper falls back to the containing folder for a missing file (#998),
                // so that one stays live either way; opening the file itself cannot.
                CanOpenFolder = true,
                CanOpenFile = localExists,
                CanCopyLink = false,
                CanOpenSource = false,
                CanLoadPreview = false,
            };
        }
    }
}
