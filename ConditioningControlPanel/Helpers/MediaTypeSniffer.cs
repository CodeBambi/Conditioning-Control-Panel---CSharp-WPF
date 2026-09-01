using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Serilog;

namespace ConditioningControlPanel.Helpers;

/// <summary>
/// THE one place the app decides what file extension a blob of downloaded media should carry.
///
/// Why this exists: every media scanner in the app is extension-gated (FlashService's image
/// list, VideoService's video list, <c>MainWindow.RefreshAssetTree</c>'s validExtensions,
/// <c>AssetFileItem</c>, <c>App.FileOpenAllowedExtensions</c>...). A file written WITHOUT an
/// extension is therefore invisible to the whole app — it sits in the library forever and the
/// user just sees media they downloaded never show up. Guessing the extension by substring-
/// matching the URL ("does it contain .mp4?") fails exactly where it matters: CDN URLs are
/// routinely a bare hash with no extension at all.
///
/// The order below is deliberate — strongest evidence first:
///   1. <see cref="FromContentType"/>  — what the server SAID it is (liberal about
///      "; charset=" style parameters and about "x-" vendor spellings).
///   2. <see cref="FromMagicBytes"/>   — what the bytes actually ARE. Beats a lying header.
///   3. <see cref="FromUrlPath"/>      — a genuine extension on the URL path, query/fragment
///      stripped, and only when it is one we recognise.
///   4. the caller's folder-appropriate default, with a warning logged.
///
/// <see cref="ResolveExtension"/> runs all four in that order and always returns something
/// beginning with '.'. Nothing here throws.
/// </summary>
public static class MediaTypeSniffer
{
    /// <summary>Bytes to read off the head of a file for <see cref="FromMagicBytes"/>. 64 is
    /// enough for every signature below plus the EBML DocType that separates webm from mkv.</summary>
    public const int MagicProbeBytes = 64;

    /// <summary>Sensible default when nothing else identified a video download.</summary>
    public const string DefaultVideoExtension = ".mp4";

    /// <summary>Sensible default when nothing else identified an image download.</summary>
    public const string DefaultImageExtension = ".jpg";

    /// <summary>MIME → extension. Deliberately wider than the spec's minimum: a CDN that
    /// answers <c>image/jpg</c> or <c>video/x-msvideo</c> should still get a usable name.</summary>
    private static readonly Dictionary<string, string> MimeToExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        // video
        ["video/mp4"] = ".mp4",
        ["video/x-m4v"] = ".m4v",
        ["video/webm"] = ".webm",
        ["video/quicktime"] = ".mov",
        ["video/x-matroska"] = ".mkv",
        ["video/x-msvideo"] = ".avi",
        ["video/avi"] = ".avi",
        ["video/msvideo"] = ".avi",
        ["video/x-ms-wmv"] = ".wmv",
        ["application/mp4"] = ".mp4",
        // image
        ["image/gif"] = ".gif",
        ["image/jpeg"] = ".jpg",
        ["image/jpg"] = ".jpg",
        ["image/pjpeg"] = ".jpg",
        ["image/png"] = ".png",
        ["image/apng"] = ".png",
        ["image/webp"] = ".webp",
        ["image/bmp"] = ".bmp",
        ["image/x-ms-bmp"] = ".bmp",
        ["image/tiff"] = ".tif",
        ["image/avif"] = ".avif",
        ["image/heic"] = ".heic",
        // audio (the assets tree has an audio/ folder too)
        ["audio/mpeg"] = ".mp3",
        ["audio/mp3"] = ".mp3",
        ["audio/wav"] = ".wav",
        ["audio/x-wav"] = ".wav",
        ["audio/wave"] = ".wav",
        ["audio/ogg"] = ".ogg",
        ["audio/flac"] = ".flac",
        ["audio/x-flac"] = ".flac",
        ["audio/mp4"] = ".m4a",
        ["audio/aac"] = ".aac",
    };

    /// <summary>Extensions <see cref="FromUrlPath"/> is willing to trust off a URL. Anything
    /// else on a URL path (".php", ".ashx", a hash that happens to contain a dot) is noise.</summary>
    private static readonly HashSet<string> KnownExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".m4v", ".webm", ".mov", ".mkv", ".avi", ".wmv", ".flv",
        ".png", ".jpg", ".jpeg", ".jpe", ".jfif", ".gif", ".webp", ".bmp",
        ".tif", ".tiff", ".heic", ".avif",
        ".mp3", ".wav", ".ogg", ".flac", ".m4a", ".aac",
    };

    private static readonly char[] UrlTail = { '?', '#' };

    // ---- 1. response header ----------------------------------------------------------

    /// <summary>
    /// Extension for an HTTP <c>Content-Type</c>, or null when the header is missing, generic
    /// (<c>application/octet-stream</c>) or unknown. Parameters are stripped, so
    /// <c>"video/mp4; charset=utf-8"</c> and <c>" VIDEO/MP4 "</c> both resolve.
    /// </summary>
    public static string? FromContentType(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType)) return null;
        try
        {
            var cut = contentType.IndexOf(';');
            var media = (cut >= 0 ? contentType[..cut] : contentType).Trim();
            if (media.Length == 0) return null;
            return MimeToExtension.TryGetValue(media, out var ext) ? ext : null;
        }
        catch { return null; }
    }

    // ---- 2. magic bytes --------------------------------------------------------------

    /// <summary>
    /// Extension for the head of a file, decided by its signature alone, or null when the
    /// bytes match nothing known. Pass at least <see cref="MagicProbeBytes"/> bytes when you
    /// have them — fewer still works, it just cannot tell webm from mkv.
    /// </summary>
    public static string? FromMagicBytes(byte[]? head, int count = -1)
    {
        if (head == null) return null;
        var n = count < 0 ? head.Length : Math.Min(count, head.Length);
        if (n < 4) return null;

        try
        {
            // PNG: 89 50 4E 47
            if (head[0] == 0x89 && head[1] == 0x50 && head[2] == 0x4E && head[3] == 0x47) return ".png";

            // JPEG: FF D8 FF
            if (head[0] == 0xFF && head[1] == 0xD8 && head[2] == 0xFF) return ".jpg";

            // GIF: "GIF8"
            if (Matches(head, n, 0, "GIF8")) return ".gif";

            // EBML (webm / mkv): 1A 45 DF A3 — the DocType string decides which.
            if (head[0] == 0x1A && head[1] == 0x45 && head[2] == 0xDF && head[3] == 0xA3)
            {
                var ascii = Encoding.ASCII.GetString(head, 0, n);
                if (ascii.Contains("webm", StringComparison.Ordinal)) return ".webm";
                if (ascii.Contains("matroska", StringComparison.Ordinal)) return ".mkv";
                return ".webm";   // webm is by far the likelier download
            }

            // RIFF containers: "RIFF" .... <fourcc at 8>
            if (Matches(head, n, 0, "RIFF") && n >= 12)
            {
                if (Matches(head, n, 8, "WEBP")) return ".webp";
                if (Matches(head, n, 8, "AVI ")) return ".avi";
                if (Matches(head, n, 8, "WAVE")) return ".wav";
            }

            // ISO-BMFF (mp4 / m4v / mov / heic / avif): "ftyp" at offset 4, brand at 8.
            if (n >= 12 && Matches(head, n, 4, "ftyp"))
            {
                var brand = Encoding.ASCII.GetString(head, 8, 4);
                if (brand.StartsWith("qt", StringComparison.OrdinalIgnoreCase)) return ".mov";
                if (brand.StartsWith("heic", StringComparison.OrdinalIgnoreCase)
                    || brand.StartsWith("heix", StringComparison.OrdinalIgnoreCase)
                    || brand.StartsWith("mif1", StringComparison.OrdinalIgnoreCase)) return ".heic";
                if (brand.StartsWith("avif", StringComparison.OrdinalIgnoreCase)) return ".avif";
                if (brand.StartsWith("M4A", StringComparison.OrdinalIgnoreCase)) return ".m4a";
                if (brand.StartsWith("M4V", StringComparison.OrdinalIgnoreCase)) return ".m4v";
                return ".mp4";
            }

            // BMP
            if (head[0] == 0x42 && head[1] == 0x4D) return ".bmp";

            // Audio odds and ends the library folder can legitimately hold.
            if (Matches(head, n, 0, "OggS")) return ".ogg";
            if (Matches(head, n, 0, "fLaC")) return ".flac";
            if (Matches(head, n, 0, "ID3")) return ".mp3";
            if (head[0] == 0xFF && (head[1] & 0xE0) == 0xE0 && (head[1] & 0x18) != 0x08) return ".mp3";

            // FLV
            if (Matches(head, n, 0, "FLV")) return ".flv";
        }
        catch { /* fall through to null — a malformed head is simply "unknown" */ }

        return null;
    }

    /// <summary>
    /// Extension decided by the first <see cref="MagicProbeBytes"/> bytes ON DISK, or null for
    /// an unreadable, empty or unrecognised file. Never throws.
    /// </summary>
    public static string? FromFileHead(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var buf = new byte[MagicProbeBytes];
            int read = 0;
            while (read < buf.Length)
            {
                int got = fs.Read(buf, read, buf.Length - read);
                if (got <= 0) break;
                read += got;
            }
            return FromMagicBytes(buf, read);
        }
        catch { return null; }
    }

    // ---- 3. the URL's own path -------------------------------------------------------

    /// <summary>
    /// The extension the URL PATH genuinely ends with (query and fragment stripped), but only
    /// when it is one we recognise as media. Null otherwise — including for the bare-hash CDN
    /// URLs that caused this class to exist.
    /// </summary>
    public static string? FromUrlPath(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        try
        {
            var clean = url;
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri)) clean = uri.AbsolutePath;
            else
            {
                var cut = clean.IndexOfAny(UrlTail);
                if (cut >= 0) clean = clean[..cut];
            }

            var ext = Path.GetExtension(clean);
            if (string.IsNullOrEmpty(ext)) return null;
            ext = ext.ToLowerInvariant();
            return KnownExtensions.Contains(ext) ? ext : null;
        }
        catch { return null; }
    }

    // ---- the whole ladder ------------------------------------------------------------

    /// <summary>
    /// Content-Type → magic bytes → URL path → <paramref name="fallbackExtension"/>. Always
    /// returns an extension starting with '.'; logs a warning only when it had to fall all the
    /// way through to the default, because that is the case worth noticing in a bug report.
    /// </summary>
    /// <param name="contentType">The response's <c>Content-Type</c> header, if any.</param>
    /// <param name="head">First bytes of the downloaded body (<see cref="MagicProbeBytes"/> is plenty).</param>
    /// <param name="url">Source URL, for its path extension and for the log line.</param>
    /// <param name="fallbackExtension">Folder-appropriate default, e.g. <see cref="DefaultVideoExtension"/>.</param>
    /// <param name="context">Caller tag for the log line, e.g. "RemoteMediaCache".</param>
    public static string ResolveExtension(string? contentType, byte[]? head, string? url,
        string fallbackExtension, string context = "media")
    {
        var ext = FromContentType(contentType)
                  ?? FromMagicBytes(head)
                  ?? FromUrlPath(url);

        if (!string.IsNullOrEmpty(ext)) return ext;

        var fallback = Normalize(fallbackExtension) ?? DefaultVideoExtension;
        try
        {
            Log.Warning(
                "MediaTypeSniffer[{Context}]: could not identify media from content-type '{Ct}', magic bytes or url path — defaulting to {Ext} for {Url}",
                context, contentType ?? "(none)", fallback, Redact(url));
        }
        catch { }
        return fallback;
    }

    /// <summary>Same ladder, but the bytes are read off a file already on disk.</summary>
    public static string ResolveExtensionForFile(string? contentType, string? filePath, string? url,
        string fallbackExtension, string context = "media")
    {
        var ext = FromContentType(contentType)
                  ?? FromFileHead(filePath)
                  ?? FromUrlPath(url);

        if (!string.IsNullOrEmpty(ext)) return ext;

        var fallback = Normalize(fallbackExtension) ?? DefaultVideoExtension;
        try
        {
            Log.Warning(
                "MediaTypeSniffer[{Context}]: could not identify {File} from content-type '{Ct}', magic bytes or url path — defaulting to {Ext}",
                context, filePath ?? "(none)", contentType ?? "(none)", fallback);
        }
        catch { }
        return fallback;
    }

    /// <summary>True when this extension is one the app's media scanners would accept.</summary>
    public static bool IsKnownMediaExtension(string? ext)
        => !string.IsNullOrEmpty(ext) && KnownExtensions.Contains(ext!);

    // ---- helpers ---------------------------------------------------------------------

    private static string? Normalize(string? ext)
    {
        if (string.IsNullOrWhiteSpace(ext)) return null;
        ext = ext.Trim();
        if (!ext.StartsWith('.')) ext = "." + ext;
        return ext.ToLowerInvariant();
    }

    private static bool Matches(byte[] b, int n, int offset, string ascii)
    {
        if (offset + ascii.Length > n) return false;
        for (int i = 0; i < ascii.Length; i++)
            if (b[offset + i] != (byte)ascii[i]) return false;
        return true;
    }

    /// <summary>URLs go in logs; keep the query string (tokens live there) out of them.</summary>
    private static string Redact(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return "(none)";
        var cut = url.IndexOfAny(UrlTail);
        return cut >= 0 ? url[..cut] : url;
    }
}
