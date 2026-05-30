using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

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
    public class CatalogueService
    {
        private const string CclabsBaseUrl = "https://app.cclabs.app";
        private const string GuidelinesVersion = "1.0";

        // Refresh when the cached token is within this many seconds of expiry.
        // Supabase access tokens default to 1h lifetime; 60s headroom is plenty
        // for the bridge call + submission to complete.
        private const int ExpiryMarginSeconds = 60;

        private static readonly HttpClient _http = BuildHttpClient();

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

        private static HttpClient BuildHttpClient()
        {
            var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            http.DefaultRequestHeaders.Add("X-Client-Version", UpdateService.AppVersion);
            http.DefaultRequestHeaders.UserAgent.ParseAdd($"ConditioningControlPanel/{UpdateService.AppVersion}");
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
                    App.Logger?.Warning(ex, "[CatalogueService] Read file failed");
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
                App.Logger?.Warning(ex, "[CatalogueService] Submit threw");
                return new SubmissionResult.UnknownError(0, ex.Message);
            }
        }

        private SubmissionResult MapResponse(int status, string body, System.Net.Http.Headers.HttpResponseHeaders headers)
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
                    App.Logger?.Information("[CatalogueService] Submission succeeded id={Id}", id);
                    var success = new SubmissionResult.Success(id, rowStatus);
                    try { SubmissionSucceeded?.Invoke(this, success); } catch (Exception ex) { App.Logger?.Debug("SubmissionSucceeded subscriber error: {Error}", ex.Message); }
                    return success;
                }
                case 409:
                {
                    var existingId = parsed?["existing_id"]?.ToString() ?? "";
                    var existingStatus = parsed?["existing_status"]?.ToString() ?? "pending";
                    App.Logger?.Information("[CatalogueService] Submission duplicate existing_id={Id}", existingId);
                    return new SubmissionResult.Duplicate(existingId, existingStatus);
                }
                case 400:
                {
                    var errorCode = parsed?["error"]?.ToString() ?? "invalid_request";
                    App.Logger?.Warning("[CatalogueService] Submission rejected: {ErrorCode}", errorCode);
                    return new SubmissionResult.ValidationError(errorCode);
                }
                case 401:
                {
                    // Invalidate the cached token so the next call refreshes.
                    // The Bearer token might have expired mid-request or the
                    // bridge re-provisioned the auth row.
                    App.Logger?.Warning("[CatalogueService] Auth failed, invalidating cached token");
                    InvalidateCachedToken();
                    return new SubmissionResult.AuthFailed();
                }
                case 403:
                {
                    // The bridge succeeded at minting a token but the catalogue
                    // says we have no public.users row — same recovery path as
                    // 401 from the user's perspective ("re-link your account").
                    App.Logger?.Warning("[CatalogueService] no_profile, invalidating cached token");
                    InvalidateCachedToken();
                    return new SubmissionResult.AuthFailed();
                }
                case 413:
                {
                    App.Logger?.Warning("[CatalogueService] file too large");
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
                    App.Logger?.Warning("[CatalogueService] Submission rate-limited retry_after={RetryAfter}", retryAfter);
                    return new SubmissionResult.RateLimited(retryAfter);
                }
                default:
                {
                    App.Logger?.Warning("[CatalogueService] Submission unknown status={Status} body={Body}", status, body);
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

                var ccpToken = App.Settings?.Current?.AuthToken;
                var unifiedId = App.UnifiedUserId;
                if (string.IsNullOrEmpty(ccpToken) || string.IsNullOrEmpty(unifiedId))
                {
                    App.Logger?.Warning("[CatalogueService] Token exchange skipped: missing CCP auth state");
                    return null;
                }

                App.Logger?.Information("[CatalogueService] Token exchange called");

                var requestBody = JsonConvert.SerializeObject(new { unified_id = unifiedId });
                using var request = new HttpRequestMessage(HttpMethod.Post, $"{CclabsBaseUrl}/api/auth/token-exchange")
                {
                    Content = new StringContent(requestBody, Encoding.UTF8, "application/json"),
                };
                request.Headers.Add("X-CCP-Auth-Token", ccpToken);

                using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    App.Logger?.Warning("[CatalogueService] Token exchange failed: {Status}", response.StatusCode);
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
                    App.Logger?.Warning("[CatalogueService] Token exchange returned malformed payload");
                    return null;
                }

                _cachedToken = accessToken;
                _cachedExpiry = expiresAt;
                App.Logger?.Information("[CatalogueService] Token exchanged, expires_at={ExpiresAt}", _cachedExpiry);
                return _cachedToken;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "[CatalogueService] Token exchange threw");
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
