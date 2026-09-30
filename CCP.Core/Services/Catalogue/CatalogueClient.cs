using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// Deeper enhancement catalogue submission client (W2).
    ///
    /// Two endpoints, one auth boundary:
    ///   1. POST https://app.cclabs.app/api/auth/token-exchange — converts our
    ///      ccp-server-issued AuthToken into a short-lived Supabase access
    ///      token, cached in-memory and refreshed when it nears expiry. Never
    ///      persisted to disk.
    ///   2. POST https://app.cclabs.app/api/enhancements — the actual catalogue
    ///      submission, authenticated with the Supabase token in an
    ///      Authorization: Bearer header.
    ///
    /// Bridge needs both the CCP token (X-CCP-Auth-Token header) and the
    /// unified_id (body) — the proxy /v2/user/profile call cross-validates
    /// the pair, so we have to send both.
    ///
    /// TODO(W2-post-launch): retry once with 500ms backoff on 502/503
    /// specifically, to absorb Vercel cold starts on the bridge or catalogue
    /// route. Deferred until we have real telemetry on transient failure
    /// rates — premature retry policies often paper over real bugs.
    /// </summary>
    /// <remarks>Core since catalogue U1; WPF's <c>CatalogueService</c> is a thin subclass. The write gate is
    /// WPF's token-and-unified-id check in <see cref="GetSupabaseTokenAsync"/>, shared by every head.</remarks>
    public class CatalogueClient
    {
        internal const string RealBaseUrl = "https://app.cclabs.app";

        /// <summary>Where the catalogue lives, or null for "no network". CCP_CATALOGUE_BASE_URL is honoured
        /// only for a loopback host (the Core LoopbackUrl rule); a CCP_USERDATA_DIR sandbox without one never
        /// reaches the real server - so tests and Keincheck cannot write to it.</summary>
        internal static string? ResolveBaseUrl(string? overrideUrl, bool sandboxed) =>
            LoopbackUrl.IsHonoured(overrideUrl, out var u) ? u.GetLeftPart(UriPartial.Path).TrimEnd('/')
            : sandboxed ? null : RealBaseUrl;

        private readonly string? CclabsBaseUrl;
        private const string GuidelinesVersion = "1.0";

        // Refresh when the cached token is within this many seconds of expiry.
        // Supabase access tokens default to 1h lifetime; 60s headroom is plenty
        // for the bridge call + submission to complete.
        private const int ExpiryMarginSeconds = 60;

        // One client per process, as in WPF; a test handler gets its own.
        private static HttpClient? _shared;
        private readonly HttpClient _http;
        private readonly Func<string?> _ccpToken, _unifiedId;

        // The token cache lives in this instance, behind a semaphore so two
        // simultaneous submissions don't each trigger a fresh exchange. Once
        // a token is cached, both submissions read it without serializing.
        private readonly SemaphoreSlim _tokenLock = new(1, 1);
        private string? _cachedToken;
        private DateTimeOffset _cachedExpiry = DateTimeOffset.MinValue;

        /// <summary>
        /// Fired when a catalogue submission succeeds (HTTP 201). Consumed by
        /// GamificationBridge for the "published to catalogue" achievement.
        /// </summary>
        public event EventHandler<SubmissionResult.Success>? SubmissionSucceeded;

        /// <param name="ccpToken">The CCP AuthToken (WPF: App.Settings.Current.AuthToken).</param>
        /// <param name="unifiedId">The unified user id (WPF: App.UnifiedUserId).</param>
        /// <param name="appVersion">Sent as X-Client-Version and in the User-Agent.</param>
        /// <param name="handler">Test seam; null is the real network.</param>
        public CatalogueClient(Func<string?> ccpToken, Func<string?> unifiedId, string appVersion, HttpMessageHandler? handler = null)
        {
            (_ccpToken, _unifiedId) = (ccpToken, unifiedId);
            // A test handler never reaches the network, so it keeps the real route shape.
            CclabsBaseUrl = handler != null ? RealBaseUrl : ResolveBaseUrl(
                Environment.GetEnvironmentVariable("CCP_CATALOGUE_BASE_URL"),
                !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CCP_USERDATA_DIR")));
            _http = handler != null ? BuildHttpClient(new HttpClient(handler), appVersion)
                : _shared ??= BuildHttpClient(new HttpClient(), appVersion);
        }

        private static HttpClient BuildHttpClient(HttpClient http, string appVersion)
        {
            http.Timeout = TimeSpan.FromSeconds(30);
            http.DefaultRequestHeaders.Add("X-Client-Version", appVersion);
            http.DefaultRequestHeaders.UserAgent.ParseAdd($"ConditioningControlPanel/{appVersion}");
            return http;
        }

        /// <summary>
        /// Submit a .ccpenh.json bundle to the catalogue. Reads the file,
        /// wraps it in the affirmation envelope, acquires (or refreshes) the
        /// Supabase token, and POSTs to /api/enhancements.
        ///
        /// All failure modes — including unexpected exceptions — surface as
        /// a SubmissionResult variant; callers never see raw HttpRequestException.
        /// </summary>
        public async Task<SubmissionResult> SubmitEnhancementAsync(
            string ccpenhJsonPath,
            CancellationToken ct)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(ccpenhJsonPath) || !File.Exists(ccpenhJsonPath))
                {
                    return new SubmissionResult.UnknownError(0, $"file_not_found: {ccpenhJsonPath}");
                }

                string fileContents;
                try
                {
                    fileContents = await File.ReadAllTextAsync(ccpenhJsonPath, Encoding.UTF8, ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "[CatalogueService] Read file failed");
                    return new SubmissionResult.UnknownError(0, $"read_error: {ex.Message}");
                }

                // Parse the bundle so we can rebuild the envelope as a typed
                // object — sending it as a raw string would put the bundle in
                // an envelope field as a JSON string instead of a JSON object,
                // breaking server-side validation.
                JToken bundle;
                try
                {
                    bundle = JToken.Parse(fileContents);
                }
                catch (JsonReaderException)
                {
                    // The server will reject anyway, but we can short-circuit.
                    return new SubmissionResult.ValidationError("invalid_json");
                }

                var envelope = new JObject
                {
                    ["bundle"] = bundle,
                    ["affirmation"] = new JObject
                    {
                        ["guidelines_version"] = GuidelinesVersion,
                        ["affirmed"] = true,
                    },
                };
                var envelopeJson = envelope.ToString(Formatting.None);

                var token = await GetSupabaseTokenAsync(ct).ConfigureAwait(false);
                if (token == null)
                {
                    return new SubmissionResult.AuthFailed();
                }

                using var request = new HttpRequestMessage(HttpMethod.Post, $"{CclabsBaseUrl}/api/enhancements")
                {
                    Content = new StringContent(envelopeJson, Encoding.UTF8, "application/json"),
                };
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

                using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
                var status = (int)response.StatusCode;
                var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

                return MapResponse(status, body, response.Headers);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[CatalogueService] Submit threw");
                return new SubmissionResult.UnknownError(0, ex.Message);
            }
        }

        /// <summary>
        /// Submit a generalized catalogue asset (a Preset or Session) to the
        /// /api/catalogue/{kind} endpoint. Unlike enhancements — whose .ccpenh.json
        /// already IS the bundle — preset/session native files carry no creator or
        /// tags, so we build the bundle wrapper here:
        ///
        ///   { affirmation, bundle: { $schema, version, metadata:{creator,tags}, asset } }
        ///
        /// <paramref name="kind"/> is the route segment ("presets" | "sessions").
        /// <paramref name="asset"/> is the pristine native object (the bytes the
        /// catalogue will store and serve back for drag-drop import).
        /// All failure modes surface as a SubmissionResult variant.
        /// </summary>
        public async Task<SubmissionResult> SubmitCatalogueAssetAsync(
            string kind,
            JToken asset,
            string schemaTag,
            string creator,
            IReadOnlyList<string> tags,
            CancellationToken ct)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(kind) || asset == null)
                {
                    return new SubmissionResult.UnknownError(0, "invalid_asset");
                }

                var tagArray = new JArray();
                if (tags != null)
                {
                    foreach (var t in tags)
                    {
                        if (!string.IsNullOrWhiteSpace(t)) tagArray.Add(t.Trim());
                    }
                }

                var envelope = new JObject
                {
                    ["affirmation"] = new JObject
                    {
                        ["guidelines_version"] = GuidelinesVersion,
                        ["affirmed"] = true,
                    },
                    ["bundle"] = new JObject
                    {
                        ["$schema"] = schemaTag,
                        ["version"] = 1,
                        ["metadata"] = new JObject
                        {
                            ["creator"] = creator ?? "",
                            ["tags"] = tagArray,
                        },
                        ["asset"] = asset,
                    },
                };
                var envelopeJson = envelope.ToString(Formatting.None);

                var token = await GetSupabaseTokenAsync(ct).ConfigureAwait(false);
                if (token == null)
                {
                    return new SubmissionResult.AuthFailed();
                }

                using var request = new HttpRequestMessage(HttpMethod.Post, $"{CclabsBaseUrl}/api/catalogue/{kind}")
                {
                    Content = new StringContent(envelopeJson, Encoding.UTF8, "application/json"),
                };
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

                using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
                var status = (int)response.StatusCode;
                var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

                // Reuse the enhancement status mapping, but suppress the
                // enhancement-only SubmissionSucceeded gamification event.
                return MapResponse(status, body, response.Headers, fireSuccessEvent: false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[CatalogueService] SubmitCatalogueAsset threw");
                return new SubmissionResult.UnknownError(0, ex.Message);
            }
        }

        /// <summary>
        /// Fetch the authenticated user's submissions for a catalogue asset kind
        /// ("presets" | "sessions") from GET /api/catalogue/{kind}/mine. Same
        /// contract + recovery as <see cref="FetchMySubmissionsAsync"/>.
        /// </summary>
        public Task<Dictionary<string, string>?> FetchMyCatalogueAssetsAsync(string kind, CancellationToken ct)
            => FetchMineAsync($"{CclabsBaseUrl}/api/catalogue/{kind}/mine", "assets", ct);

        /// <summary>
        /// Fetch the authenticated user's catalogue submissions and their
        /// current status, so the app can surface acceptance/publication
        /// feedback after the otherwise fire-and-forget submit. Returns a map of
        /// submission id → status ("pending"|"approved"|"published"|"rejected"),
        /// or null when the call could not be made (no auth, network error, or
        /// the endpoint is unavailable). Never throws past the boundary except
        /// for cancellation.
        ///
        /// Server contract (CCP-Server, NOT yet deployed):
        ///   GET https://app.cclabs.app/api/enhancements/mine
        ///   Authorization: Bearer &lt;supabase token&gt;
        ///   200 → { "enhancements": [ { "id", "status", "title" }, ... ] }
        /// Until that route exists this returns null (non-2xx) and the caller
        /// simply leaves the last-known status untouched.
        /// </summary>
        public Task<Dictionary<string, string>?> FetchMySubmissionsAsync(CancellationToken ct)
            => FetchMineAsync($"{CclabsBaseUrl}/api/enhancements/mine", "enhancements", ct);

        /// <summary>
        /// Shared "GET …/mine" reader: returns a map of submission id → status, or
        /// null on no-auth/network/non-2xx. <paramref name="arrayKey"/> is the
        /// preferred JSON array property; if absent we fall back to the first array
        /// in the payload so a presets/sessions endpoint keyed differently still works.
        /// </summary>
        private async Task<Dictionary<string, string>?> FetchMineAsync(string url, string arrayKey, CancellationToken ct)
        {
            try
            {
                var token = await GetSupabaseTokenAsync(ct).ConfigureAwait(false);
                if (token == null) return null;

                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

                using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
                if (response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.Forbidden)
                {
                    // Token expired or no profile — same recovery as a submit 401/403.
                    InvalidateCachedToken();
                    return null;
                }
                if (!response.IsSuccessStatusCode)
                {
                    Log.Debug("[CatalogueService] FetchMine non-success status={Status} url={Url}", (int)response.StatusCode, url);
                    return null;
                }

                var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                JObject parsed;
                try { parsed = JObject.Parse(body); }
                catch { return null; }

                if (parsed[arrayKey] is not JArray arr)
                {
                    arr = parsed.Properties().Select(p => p.Value).OfType<JArray>().FirstOrDefault();
                    if (arr == null) return null;
                }

                var map = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var item in arr)
                {
                    var id = item["id"]?.ToString();
                    var status = item["status"]?.ToString();
                    if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(status))
                        map[id] = status;
                }
                return map;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Log.Debug("[CatalogueService] FetchMine threw: {Error}", ex.Message);
                return null;
            }
        }

        private SubmissionResult MapResponse(int status, string body, System.Net.Http.Headers.HttpResponseHeaders headers, bool fireSuccessEvent = true)
        {
            // The catalogue API responds with JSON for every documented status.
            // Parse defensively — an unparseable body just means we treat it as
            // an UnknownError with the raw text for the caller's logs.
            JObject? parsed = null;
            try { parsed = JObject.Parse(body); }
            catch { /* leave parsed null */ }

            switch (status)
            {
                case 201:
                {
                    var id = parsed?["id"]?.ToString() ?? "";
                    var rowStatus = parsed?["status"]?.ToString() ?? "pending";
                    Log.Information("[CatalogueService] Submission succeeded id={Id}", id);
                    var success = new SubmissionResult.Success(id, rowStatus);
                    if (fireSuccessEvent)
                    {
                        try { SubmissionSucceeded?.Invoke(this, success); } catch (Exception ex) { Log.Debug("SubmissionSucceeded subscriber error: {Error}", ex.Message); }
                    }
                    return success;
                }
                case 409:
                {
                    var existingId = parsed?["existing_id"]?.ToString() ?? "";
                    var existingStatus = parsed?["existing_status"]?.ToString() ?? "pending";
                    Log.Information("[CatalogueService] Submission duplicate existing_id={Id}", existingId);
                    return new SubmissionResult.Duplicate(existingId, existingStatus);
                }
                case 400:
                {
                    var errorCode = parsed?["error"]?.ToString() ?? "invalid_request";
                    Log.Warning("[CatalogueService] Submission rejected: {ErrorCode}", errorCode);
                    return new SubmissionResult.ValidationError(errorCode);
                }
                case 401:
                {
                    // Invalidate the cached token so the next call refreshes.
                    // The Bearer token might have expired mid-request or the
                    // bridge re-provisioned the auth row.
                    Log.Warning("[CatalogueService] Auth failed, invalidating cached token");
                    InvalidateCachedToken();
                    return new SubmissionResult.AuthFailed();
                }
                case 403:
                {
                    // The bridge succeeded at minting a token but the catalogue
                    // says we have no public.users row — same recovery path as
                    // 401 from the user's perspective ("re-link your account").
                    Log.Warning("[CatalogueService] no_profile, invalidating cached token");
                    InvalidateCachedToken();
                    return new SubmissionResult.AuthFailed();
                }
                case 413:
                {
                    Log.Warning("[CatalogueService] file too large");
                    return new SubmissionResult.TooLarge();
                }
                case 429:
                {
                    int? retryAfter = null;
                    var retryHeader = headers.RetryAfter?.Delta?.TotalSeconds;
                    if (retryHeader.HasValue)
                    {
                        retryAfter = (int)retryHeader.Value;
                    }
                    else if (parsed?["retry_after"] != null && int.TryParse(parsed["retry_after"]!.ToString(), out var ra))
                    {
                        retryAfter = ra;
                    }
                    Log.Warning("[CatalogueService] Submission rate-limited retry_after={RetryAfter}", retryAfter);
                    return new SubmissionResult.RateLimited(retryAfter);
                }
                default:
                {
                    Log.Warning("[CatalogueService] Submission unknown status={Status} body={Body}", status, body);
                    return new SubmissionResult.UnknownError(status, body);
                }
            }
        }

        /// <summary>
        /// Public hook for tests / future "force refresh" actions. Wipes the
        /// in-memory cache so the next submission re-exchanges.
        /// </summary>
        public void InvalidateCachedToken()
        {
            // Cheap, lock-free clear is fine — a race here just causes one
            // extra exchange on the next call, never a stale cached token.
            _cachedToken = null;
            _cachedExpiry = DateTimeOffset.MinValue;
        }

        private async Task<string?> GetSupabaseTokenAsync(CancellationToken ct)
        {
            // Fast path: cached and not near expiry. Reading two fields without
            // the lock is racy but the worst case is one wasted exchange.
            if (_cachedToken != null && _cachedExpiry > DateTimeOffset.UtcNow.AddSeconds(ExpiryMarginSeconds))
            {
                return _cachedToken;
            }

            await _tokenLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                // Re-check inside the lock: another caller may have refreshed
                // while we were waiting.
                if (_cachedToken != null && _cachedExpiry > DateTimeOffset.UtcNow.AddSeconds(ExpiryMarginSeconds))
                {
                    return _cachedToken;
                }

                var ccpToken = _ccpToken();
                var unifiedId = _unifiedId();
                if (CclabsBaseUrl == null) { Log.Warning("[CatalogueService] Sandboxed without a loopback CCP_CATALOGUE_BASE_URL: no network"); return null; }
                if (string.IsNullOrEmpty(ccpToken) || string.IsNullOrEmpty(unifiedId))
                {
                    Log.Warning("[CatalogueService] Token exchange skipped: missing CCP auth state");
                    return null;
                }

                Log.Information("[CatalogueService] Token exchange called");

                var requestBody = JsonConvert.SerializeObject(new { unified_id = unifiedId });
                using var request = new HttpRequestMessage(HttpMethod.Post, $"{CclabsBaseUrl}/api/auth/token-exchange")
                {
                    Content = new StringContent(requestBody, Encoding.UTF8, "application/json"),
                };
                request.Headers.Add("X-CCP-Auth-Token", ccpToken);

                using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    Log.Warning("[CatalogueService] Token exchange failed: {Status}", response.StatusCode);
                    _cachedToken = null;
                    _cachedExpiry = DateTimeOffset.MinValue;
                    return null;
                }

                var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                var parsed = JObject.Parse(body);
                var accessToken = parsed["access_token"]?.ToString();
                var expiresAtStr = parsed["expires_at"]?.ToString();
                if (string.IsNullOrEmpty(accessToken) || string.IsNullOrEmpty(expiresAtStr) ||
                    !DateTimeOffset.TryParse(expiresAtStr, out var expiresAt))
                {
                    Log.Warning("[CatalogueService] Token exchange returned malformed payload");
                    return null;
                }

                _cachedToken = accessToken;
                _cachedExpiry = expiresAt;
                Log.Information("[CatalogueService] Token exchanged, expires_at={ExpiresAt}", _cachedExpiry);
                return _cachedToken;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[CatalogueService] Token exchange threw");
                _cachedToken = null;
                _cachedExpiry = DateTimeOffset.MinValue;
                return null;
            }
            finally
            {
                _tokenLock.Release();
            }
        }
    }

    /// <summary>
    /// Discriminated union for catalogue submission outcomes. Pattern-match
    /// on the concrete subtype in UI code:
    ///
    ///   switch (result) {
    ///     case SubmissionResult.Success s: ...
    ///     case SubmissionResult.Duplicate d: ...
    ///     case SubmissionResult.ValidationError v: ...
    ///     case SubmissionResult.AuthFailed: ...
    ///     case SubmissionResult.TooLarge: ...
    ///     case SubmissionResult.RateLimited r: ...
    ///     case SubmissionResult.UnknownError u: ...
    ///   }
    /// </summary>
    public abstract record SubmissionResult
    {
        public sealed record Success(string Id, string Status) : SubmissionResult;
        public sealed record Duplicate(string ExistingId, string ExistingStatus) : SubmissionResult;
        public sealed record ValidationError(string ErrorCode) : SubmissionResult;
        public sealed record AuthFailed : SubmissionResult;
        public sealed record TooLarge : SubmissionResult;
        public sealed record RateLimited(int? RetryAfterSeconds) : SubmissionResult;
        public sealed record UnknownError(int StatusCode, string Body) : SubmissionResult;
    }
}
