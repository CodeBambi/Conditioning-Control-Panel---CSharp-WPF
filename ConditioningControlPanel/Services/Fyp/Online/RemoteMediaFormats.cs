using System;
using System.Collections.Generic;

namespace ConditioningControlPanel.Services.Fyp.Online;

/// <summary>
/// The single authority on what a REMOTE entry is allowed to be. Local enumerators each
/// carry their own extension list — FlashService accepts 13 image types, FypAssetManifest
/// accepts .gif only, DtrhAssetManifest accepts 5 — so before this existed, whichever list
/// a consumer happened to reach for silently decided what remote media it could show.
/// Remote entries are validated here and nowhere else; the local lists are deliberately
/// left alone (they describe a disk the user controls, this describes a CDN we don't).
///
/// Contents are what scrolller actually returns, live-verified 2026-08-12:
///
///   * video — webm and mp4 only. Both play natively in Chromium/WebView2.
///   * stills — webp, jpg/jpeg, png. STATIC, always. The provider's "GIF" filter is a
///     misnomer for "animated content": it returns webm/mp4 renditions plus static
///     webp/jpg posters and zero .gif files across three subreddits. Six of those webp
///     posters were byte-checked and are all plain VP8 with no ANIM chunk, so the
///     AnimatedWebp SKCodec path cannot animate them either.
///   * .gif is NOT in the still set on purpose. Real .gif appears only incidentally under
///     the PICTURE filter (4 of ~140 sources in one subreddit, none in another) — far too
///     thin to build a pool on, and admitting it would let "remote stills sometimes
///     animate" back into surfaces that were designed around stills-only.
/// </summary>
internal static class RemoteMediaFormats
{
    /// <summary><see cref="FypAssetManifest.Entry.Type"/> for a remote clip.</summary>
    public const string TypeVideo = "video";

    /// <summary><see cref="FypAssetManifest.Entry.Type"/> for a remote still.</summary>
    public const string TypeImage = "image";

    /// <summary><see cref="FypAssetManifest.Entry.Origin"/> that marks an entry as remote.</summary>
    public const string OriginOnline = "online";

    public static readonly IReadOnlyList<string> VideoExtensions = new[] { ".mp4", ".webm" };

    public static readonly IReadOnlyList<string> ImageExtensions = new[] { ".webp", ".jpg", ".jpeg", ".png" };

    private static readonly char[] UrlTail = { '?', '#' };

    /// <summary>True when the URL names a container we can play remotely.</summary>
    public static bool IsRemoteVideo(string? url) => HasExtension(url, VideoExtensions);

    /// <summary>True when the URL names a STATIC image we can show remotely.</summary>
    public static bool IsRemoteImage(string? url) => HasExtension(url, ImageExtensions);

    /// <summary>
    /// Gate one remote entry before it joins a pool. Callers pass the kind their surface can
    /// actually render — a flash pool asks for <see cref="FeedMediaKind.Image"/> and a
    /// mismatched entry is rejected rather than silently rendered as a broken tile.
    /// Apply this to entries you already know are remote (<see cref="OriginOnline"/>);
    /// library entries follow their enumerator's rules, not these.
    /// </summary>
    /// <param name="reason">Why it was rejected, for the log line. Null when valid.</param>
    public static bool Validate(FypAssetManifest.Entry? entry, FeedMediaKind kind, out string? reason)
    {
        if (entry == null) { reason = "null entry"; return false; }

        if (string.IsNullOrWhiteSpace(entry.Id)) { reason = "empty id"; return false; }
        // segIds are "<id>:<k>", so a colon anywhere in an id corrupts every segment
        // reference built from it. Remote ids are path-shaped ("scrolller/<sub>/<postId>").
        if (entry.Id.Contains(':')) { reason = "id contains ':'"; return false; }

        if (string.IsNullOrWhiteSpace(entry.Url)) { reason = "empty url"; return false; }
        if (!Uri.TryCreate(entry.Url, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            reason = "url is not an absolute http(s) address";
            return false;
        }

        bool isVideo = entry.Type == TypeVideo;
        bool isImage = entry.Type == TypeImage;
        if (!isVideo && !isImage)
        {
            // "gif" lands here by design: no remote source produces one (see class remarks).
            reason = $"unsupported remote type '{entry.Type}'";
            return false;
        }

        if (kind == FeedMediaKind.Video && !isVideo) { reason = "caller wanted video, entry is a still"; return false; }
        if (kind == FeedMediaKind.Image && !isImage) { reason = "caller wanted a still, entry is video"; return false; }

        if (isVideo && !IsRemoteVideo(entry.Url)) { reason = "video url has no playable extension"; return false; }
        if (isImage && !IsRemoteImage(entry.Url)) { reason = "image url has no static-image extension"; return false; }

        reason = null;
        return true;
    }

    /// <summary>Kind-agnostic overload — accepts either shape.</summary>
    public static bool Validate(FypAssetManifest.Entry? entry, out string? reason)
        => Validate(entry, FeedMediaKind.Any, out reason);

    /// <summary>Extension test that ignores any query string or fragment (CDN renditions
    /// are bare paths today, but a signed-URL variant would otherwise fail every check).</summary>
    private static bool HasExtension(string? url, IReadOnlyList<string> extensions)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        int cut = url.IndexOfAny(UrlTail);
        string clean = cut >= 0 ? url[..cut] : url;
        for (int i = 0; i < extensions.Count; i++)
            if (clean.EndsWith(extensions[i], StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }
}
