using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Models.Deeper;

namespace ConditioningControlPanel.Services.Deeper
{
    /// <summary>
    /// Fetches and caches Deeper enhancements referenced by URL (used by the
    /// Phase 9 HT description auto-discovery flow). Per-session in-memory
    /// cache; no disk persistence in v1 - creators tend to update files
    /// frequently and we'd rather pay the round-trip than serve stale.
    ///
    /// Hard-capped at 256 KB response (enhancements with realistic content
    /// sit well under 32 KB) and 10 s total timeout. Schema sniff happens
    /// before deserialization so a non-Deeper file at the URL fails fast.
    /// </summary>
    public sealed class EnhancementFetcher : IDisposable
    {
        private const int MaxResponseBytes = 256 * 1024;
        private const int MaxRedirects = 5;
        private static readonly TimeSpan FetchTimeout = TimeSpan.FromSeconds(10);

        private readonly HttpClient _http;
        private readonly Dictionary<string, Enhancement> _cache = new(StringComparer.OrdinalIgnoreCase);
        private readonly object _gate = new();
        private bool _disposed;

        public EnhancementFetcher()
        {
            // Manual redirect handling — every hop is re-validated against the
            // SSRF guard so a 302 on an attacker-controlled host can't ferry
            // the request into a private IP / cloud metadata endpoint.
            var handler = new HttpClientHandler { AllowAutoRedirect = false };
            _http = new HttpClient(handler)
            {
                Timeout = FetchTimeout
            };
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("CCP-Deeper/1.0");
        }

        public bool TryGetCached(string url, out Enhancement? enhancement)
        {
            lock (_gate)
            {
                if (_cache.TryGetValue(url, out var hit))
                {
                    enhancement = hit;
                    return true;
                }
            }
            enhancement = null;
            return false;
        }

        /// <summary>
        /// Fetches the URL, sniffs schema, validates, returns the parsed
        /// enhancement on success or null on any failure path. Failures are
        /// logged at Debug; this never throws to the caller.
        /// </summary>
        public async Task<Enhancement?> FetchAsync(string url, CancellationToken ct = default)
        {
            if (_disposed) return null;
            if (string.IsNullOrEmpty(url)) return null;

            if (TryGetCached(url, out var cached)) return cached;

            try
            {
                if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
                {
                    App.Logger?.Debug("EnhancementFetcher: malformed url ({Url})", url);
                    return null;
                }

                // https-only — `ccp:http://...` references in HT descriptions
                // would otherwise allow a cleartext SSRF detour. Real creators
                // host their ccpenh.json on https hosts (gist, github pages, etc.).
                if (uri.Scheme != Uri.UriSchemeHttps)
                {
                    App.Logger?.Debug("EnhancementFetcher: rejecting non-https url ({Url})", url);
                    return null;
                }

                // Resolve once and reject loopback / private / link-local /
                // ULA before issuing the GET; re-checked per hop on redirect.
                if (!await UrlSafety.IsSafePublicHttpsAsync(uri, ct).ConfigureAwait(false))
                {
                    App.Logger?.Debug("EnhancementFetcher: rejecting unsafe host {Host}", uri.Host);
                    return null;
                }

                HttpResponseMessage? finalResp = null;
                var current = uri;
                for (int hop = 0; hop <= MaxRedirects; hop++)
                {
                    var resp = await _http.GetAsync(current, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                    int code = (int)resp.StatusCode;
                    if (code >= 300 && code < 400 && resp.Headers.Location != null)
                    {
                        resp.Dispose();
                        if (hop >= MaxRedirects) return null;
                        var next = resp.Headers.Location.IsAbsoluteUri
                            ? resp.Headers.Location
                            : new Uri(current, resp.Headers.Location);
                        if (next.Scheme != Uri.UriSchemeHttps) return null;
                        if (!await UrlSafety.IsSafePublicHttpsAsync(next, ct).ConfigureAwait(false)) return null;
                        current = next;
                        continue;
                    }
                    finalResp = resp;
                    break;
                }
                if (finalResp == null) return null;

                using var _resp = finalResp;
                if (!_resp.IsSuccessStatusCode)
                {
                    App.Logger?.Debug("EnhancementFetcher: HTTP {Status} for {Url}", (int)_resp.StatusCode, url);
                    return null;
                }

                var contentLength = _resp.Content.Headers.ContentLength;
                if (contentLength.HasValue && contentLength.Value > MaxResponseBytes)
                {
                    App.Logger?.Debug("EnhancementFetcher: response too large ({Size} bytes) for {Url}", contentLength.Value, url);
                    return null;
                }

                string json;
                using (var stream = await _resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
                {
                    var buffer = new byte[MaxResponseBytes + 1];
                    int read = 0;
                    while (read < buffer.Length)
                    {
                        int n = await stream.ReadAsync(buffer.AsMemory(read, buffer.Length - read), ct).ConfigureAwait(false);
                        if (n == 0) break;
                        read += n;
                    }
                    if (read > MaxResponseBytes)
                    {
                        App.Logger?.Debug("EnhancementFetcher: response exceeded {Cap} bytes (truncated read) for {Url}", MaxResponseBytes, url);
                        return null;
                    }
                    json = System.Text.Encoding.UTF8.GetString(buffer, 0, read);
                }

                // Cheap schema sniff so we fail fast on any non-Deeper file
                // (some HT users embed arbitrary URLs in descriptions).
                if (!json.Contains(Enhancement.SchemaTag, StringComparison.OrdinalIgnoreCase))
                {
                    App.Logger?.Debug("EnhancementFetcher: schema sniff failed for {Url}", url);
                    return null;
                }

                var enh = EnhancementSerializer.Load(json);
                var issues = EnhancementValidator.Validate(enh);
                if (issues.Exists(i => i.Severity == ValidationSeverity.Error))
                {
                    App.Logger?.Debug("EnhancementFetcher: validation failed for {Url}", url);
                    return null;
                }

                lock (_gate) _cache[url] = enh;
                App.Logger?.Information("EnhancementFetcher: cached '{Name}' from {Url}", enh.Metadata?.Name, url);
                return enh;
            }
            catch (TaskCanceledException) { return null; }
            catch (Exception ex)
            {
                App.Logger?.Debug("EnhancementFetcher: fetch failed for {Url} - {Error}", url, ex.Message);
                return null;
            }
        }

        public void ClearCache()
        {
            lock (_gate) _cache.Clear();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _http.Dispose();
        }
    }
}
