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
        private static readonly TimeSpan FetchTimeout = TimeSpan.FromSeconds(10);

        private readonly HttpClient _http;
        private readonly Dictionary<string, Enhancement> _cache = new(StringComparer.OrdinalIgnoreCase);
        private readonly object _gate = new();
        private bool _disposed;

        public EnhancementFetcher()
        {
            _http = new HttpClient
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
                if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
                    || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                {
                    App.Logger?.Debug("EnhancementFetcher: rejecting non-http(s) url ({Url})", url);
                    return null;
                }

                using var resp = await _http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode)
                {
                    App.Logger?.Debug("EnhancementFetcher: HTTP {Status} for {Url}", (int)resp.StatusCode, url);
                    return null;
                }

                var contentLength = resp.Content.Headers.ContentLength;
                if (contentLength.HasValue && contentLength.Value > MaxResponseBytes)
                {
                    App.Logger?.Debug("EnhancementFetcher: response too large ({Size} bytes) for {Url}", contentLength.Value, url);
                    return null;
                }

                string json;
                using (var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
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
