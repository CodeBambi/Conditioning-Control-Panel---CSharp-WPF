using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace ConditioningControlPanel.Services.Deeper
{
    public class HtVideoMetadata
    {
        public string? Title { get; set; }
        public string? Uploader { get; set; }     // immutable Creator
        public string? Description { get; set; }
        public List<string> Tags { get; set; } = new();
    }

    /// <summary>
    /// Best-effort metadata scraper for HypnoTube video pages. Open Graph tags
    /// first (most stable across redesigns), JSON-LD VideoObject second, last-
    /// resort regex on HT-specific markup. Hostname-gated so we never silently
    /// scrape arbitrary URLs.
    ///
    /// Caller must <c>await</c> WITHOUT <c>ConfigureAwait(false)</c> so the
    /// continuation lands on the UI thread to update bound TextBoxes.
    /// </summary>
    public static class HtMetadataFetcher
    {
        private const int MaxBytes = 512 * 1024;          // pages have grown past 256 KB
        private const int FetcherTimeoutMs = 3000;        // separate from HTTP timeout
        private static readonly TimeSpan HttpTimeout = TimeSpan.FromSeconds(10);

        private static readonly HttpClient Http = new(new HttpClientHandler { AllowAutoRedirect = true })
        {
            Timeout = HttpTimeout
        };

        private static readonly Dictionary<string, HtVideoMetadata> Cache = new();
        private static readonly object CacheLock = new();

        public static async Task<HtVideoMetadata?> FetchAsync(string url, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(url)) return null;
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return null;
            if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return null;

            // Hostname gate — never scrape non-HT.
            if (!uri.Host.Contains("hypnotube", StringComparison.OrdinalIgnoreCase))
                return null;

            lock (CacheLock)
            {
                if (Cache.TryGetValue(url, out var cached)) return cached;
            }

            using var fetcherCts = new CancellationTokenSource(FetcherTimeoutMs);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, fetcherCts.Token);

            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, uri);
                // HT serves a leaner page to known browsers; mimic.
                req.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) CCP/Deeper");
                req.Headers.AcceptLanguage.ParseAdd("en-US,en;q=0.9");

                using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, linked.Token);
                if (!resp.IsSuccessStatusCode) return null;

                using var stream = await resp.Content.ReadAsStreamAsync(linked.Token);
                var html = await ReadCappedAsync(stream, MaxBytes, linked.Token);
                if (string.IsNullOrEmpty(html)) return null;

                var meta = Parse(html);
                if (meta != null)
                {
                    lock (CacheLock) Cache[url] = meta;
                }
                return meta;
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("HtMetadataFetcher: {Error}", ex.Message);
                return null;
            }
        }

        private static async Task<string?> ReadCappedAsync(Stream s, int cap, CancellationToken ct)
        {
            try
            {
                var ms = new MemoryStream();
                var buf = new byte[8192];
                int total = 0;
                while (total < cap)
                {
                    int read = await s.ReadAsync(buf.AsMemory(0, Math.Min(buf.Length, cap - total)), ct);
                    if (read <= 0) break;
                    ms.Write(buf, 0, read);
                    total += read;
                }
                return Encoding.UTF8.GetString(ms.ToArray());
            }
            catch
            {
                return null;
            }
        }

        // -- Parsing --------------------------------------------------------------

        public static HtVideoMetadata? Parse(string html)
        {
            if (string.IsNullOrEmpty(html)) return null;

            var meta = new HtVideoMetadata();

            // 1) Open Graph (most stable).
            meta.Title = ExtractMetaContent(html, "property", "og:title")
                          ?? ExtractMetaContent(html, "name", "twitter:title")
                          ?? ExtractTitleTag(html);

            meta.Description = ExtractMetaContent(html, "property", "og:description")
                              ?? ExtractMetaContent(html, "name", "description")
                              ?? ExtractMetaContent(html, "name", "twitter:description");

            // og:video:tag can appear multiple times.
            foreach (Match m in OgTagRegex.Matches(html))
            {
                if (m.Groups.Count > 1)
                {
                    var tag = WebDecode(m.Groups[1].Value).Trim();
                    if (!string.IsNullOrEmpty(tag) && !meta.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase))
                        meta.Tags.Add(tag);
                }
            }

            // 2) HypnoTube-specific uploader link is the most accurate source —
            // it points at the actual user who uploaded. JSON-LD author and the
            // meta author tag often resolve to "Mechbunny.com" / the site itself
            // rather than the uploader, so we prefer the /user/ link first.
            var linkMatch = HtUploaderRegex.Match(html);
            if (linkMatch.Success && linkMatch.Groups.Count > 1)
            {
                var uploader = WebDecode(linkMatch.Groups[1].Value).Trim();
                // Strip any inner HTML (avatar img tags, spans, etc.)
                uploader = Regex.Replace(uploader, "<[^>]+>", "").Trim();
                if (!string.IsNullOrEmpty(uploader)) meta.Uploader = uploader;
            }

            // 3) JSON-LD VideoObject for uploader (fallback).
            if (string.IsNullOrEmpty(meta.Uploader))
                meta.Uploader = TryExtractJsonLdAuthor(html);

            // 4) Last-resort regexes for uploader.
            if (string.IsNullOrEmpty(meta.Uploader))
                meta.Uploader = ExtractMetaContent(html, "name", "author");

            // Trim and normalize.
            meta.Title = NormalizeWhitespace(meta.Title);
            meta.Description = NormalizeWhitespace(meta.Description);
            meta.Uploader = NormalizeWhitespace(meta.Uploader);

            // If we got nothing useful, return null so callers skip silently.
            if (string.IsNullOrEmpty(meta.Title)
                && string.IsNullOrEmpty(meta.Uploader)
                && string.IsNullOrEmpty(meta.Description)
                && meta.Tags.Count == 0)
                return null;

            return meta;
        }

        private static readonly Regex OgTagRegex = new(
            @"<meta[^>]+property\s*=\s*[""']og:video:tag[""'][^>]+content\s*=\s*[""']([^""']+)[""']",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // HypnoTube uses /user/ (singular) for profile URLs, e.g.
        // /user/viatrixia-224012/. Match singular AND plural to be robust.
        private static readonly Regex HtUploaderRegex = new(
            @"<a[^>]+href\s*=\s*[""']/users?/[^""']+[""'][^>]*>(.*?)</a>",
            RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.Singleline);

        private static readonly Regex JsonLdRegex = new(
            @"<script[^>]+type\s*=\s*[""']application/ld\+json[""'][^>]*>(.*?)</script>",
            RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.Singleline);

        private static readonly Regex JsonLdAuthorRegex = new(
            @"""author""\s*:\s*\{[^{}]*?""name""\s*:\s*""([^""]+)""",
            RegexOptions.Compiled | RegexOptions.Singleline);

        private static readonly Regex JsonLdAuthorStringRegex = new(
            @"""author""\s*:\s*""([^""]+)""",
            RegexOptions.Compiled);

        private static readonly Regex TitleTagRegex = new(
            @"<title[^>]*>(.*?)</title>",
            RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.Singleline);

        private static string? TryExtractJsonLdAuthor(string html)
        {
            foreach (Match block in JsonLdRegex.Matches(html))
            {
                if (block.Groups.Count <= 1) continue;
                var json = block.Groups[1].Value;
                if (json.IndexOf("VideoObject", StringComparison.OrdinalIgnoreCase) < 0) continue;

                var m = JsonLdAuthorRegex.Match(json);
                if (m.Success && m.Groups.Count > 1) return WebDecode(m.Groups[1].Value);

                m = JsonLdAuthorStringRegex.Match(json);
                if (m.Success && m.Groups.Count > 1) return WebDecode(m.Groups[1].Value);
            }
            return null;
        }

        private static string? ExtractMetaContent(string html, string attr, string value)
        {
            // Two orderings: content-then-property and property-then-content.
            var p1 = new Regex(
                $@"<meta[^>]+{Regex.Escape(attr)}\s*=\s*[""']{Regex.Escape(value)}[""'][^>]+content\s*=\s*[""']([^""']*)[""']",
                RegexOptions.IgnoreCase);
            var m = p1.Match(html);
            if (m.Success && m.Groups.Count > 1) return WebDecode(m.Groups[1].Value);

            var p2 = new Regex(
                $@"<meta[^>]+content\s*=\s*[""']([^""']*)[""'][^>]+{Regex.Escape(attr)}\s*=\s*[""']{Regex.Escape(value)}[""']",
                RegexOptions.IgnoreCase);
            m = p2.Match(html);
            if (m.Success && m.Groups.Count > 1) return WebDecode(m.Groups[1].Value);

            return null;
        }

        private static string? ExtractTitleTag(string html)
        {
            var m = TitleTagRegex.Match(html);
            if (!m.Success || m.Groups.Count < 2) return null;
            // Strip embedded HTML tags (e.g. <span>) and decode entities.
            var raw = Regex.Replace(m.Groups[1].Value, "<[^>]+>", "");
            return WebDecode(raw);
        }

        private static string WebDecode(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            try { return System.Net.WebUtility.HtmlDecode(s); }
            catch { return s; }
        }

        private static string? NormalizeWhitespace(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            return Regex.Replace(s, @"\s+", " ").Trim();
        }
    }
}
