using System;
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
    /// SP3 device-code login (proxy /v2/auth/device/initiate + /poll).
    /// User confirms the code on cclabs-web; desktop polls until 200 confirmed.
    /// /confirm is web-side only; conflict (409) is resolved on the web tab the
    /// user is already in, so this client doesn't surface a desktop conflict UI.
    /// </summary>
    public class V2DeviceCodeService
    {
        private static readonly HttpClient _http = new();
        private const string SERVER_URL = "https://codebambi-proxy.vercel.app";

        // Hardcoded: /v2/auth/device/initiate doesn't return a verification_url.
        // /dashboard/link-device is the canonical pairing page on cclabs-web;
        // the layout's auth gate handles the not-signed-in case via a ?next=
        // round-trip back to this path.
        public const string VerificationUrl = "https://app.cclabs.app/dashboard/link-device";

        static V2DeviceCodeService()
        {
            _http.Timeout = TimeSpan.FromSeconds(30);
            _http.DefaultRequestHeaders.Add("X-Client-Version", UpdateService.AppVersion);
            _http.DefaultRequestHeaders.UserAgent.ParseAdd($"ConditioningControlPanel/{UpdateService.AppVersion}");
        }

        public enum PollStatus
        {
            Pending,
            Confirmed,
            Expired,
            NotFound,
            RateLimited,
            ServiceUnavailable,
            BadRequest,
            Unauthorized,
            Unknown
        }

        public class InitiateResponse
        {
            public bool Success { get; set; }
            public string? Code { get; set; }
            public string? SessionId { get; set; }
            public DateTimeOffset ExpiresAt { get; set; }
            public string? Error { get; set; }
        }

        // Internal DTO for JsonConvert.DeserializeObject — typed deserialization
        // with [JsonProperty] handles ISO 8601 + 'Z' suffix → DateTimeOffset cleanly,
        // unlike JObject.Parse + JValue<DateTime>.ToString round-trip which strips
        // timezone info on non-UTC systems.
        private class InitiateRaw
        {
            [JsonProperty("code")] public string? Code { get; set; }
            [JsonProperty("session_id")] public string? SessionId { get; set; }
            [JsonProperty("expires_at")] public DateTimeOffset ExpiresAt { get; set; }
        }

        public class PollResponse
        {
            public PollStatus Status { get; set; }
            public string? AuthToken { get; set; }
            public string? UnifiedId { get; set; }
            public bool IsNewUser { get; set; }

            // Server-provided user object on confirmed (post-fix proxy build).
            // Null on older proxy builds or if the server's user-fetch failed —
            // caller falls back to bare token+id storage in that case, which
            // leaves the UI at defaults until the user re-auths via Patreon
            // or Discord. See HandleDeviceCodeConfirmed in LoginDialog for
            // how this gets applied via V2AuthService.ApplyUserDataToSettings.
            public V2AuthService.V2User? User { get; set; }
        }

        public async Task<InitiateResponse> InitiateAsync(CancellationToken ct = default)
        {
            try
            {
                var payload = new JObject
                {
                    ["client"] = "ccp-desktop",
                    ["version"] = UpdateService.AppVersion
                };

                var content = new StringContent(payload.ToString(), Encoding.UTF8, "application/json");
                var response = await _http.PostAsync($"{SERVER_URL}/v2/auth/device/initiate", content, ct);
                var json = await response.Content.ReadAsStringAsync(ct);

                if (!response.IsSuccessStatusCode)
                {
                    var error = ParseErrorMessage(json, response.StatusCode);
                    Log.Warning("[DeviceCode] Initiate failed: {Status} {Error}", (int)response.StatusCode, error);
                    return new InitiateResponse { Success = false, Error = error };
                }

                // Typed POCO deserialization — same pattern as V2AuthService. Handles
                // ISO 8601 with 'Z' suffix → DateTimeOffset cleanly, preserving UTC.
                InitiateRaw? raw;
                try
                {
                    raw = JsonConvert.DeserializeObject<InitiateRaw>(json);
                }
                catch (Exception ex)
                {
                    return new InitiateResponse { Success = false, Error = "Bad response: " + ex.Message };
                }

                if (raw == null || string.IsNullOrEmpty(raw.Code))
                {
                    return new InitiateResponse { Success = false, Error = "Invalid response" };
                }

                if (raw.ExpiresAt == default)
                {
                    return new InitiateResponse { Success = false, Error = "Invalid expiry" };
                }

                Log.Information("[DeviceCode] Initiated session={Session}", raw.SessionId?.Substring(0, Math.Min(8, raw.SessionId.Length)));
                return new InitiateResponse
                {
                    Success = true,
                    Code = raw.Code,
                    SessionId = raw.SessionId,
                    ExpiresAt = raw.ExpiresAt
                };
            }
            catch (OperationCanceledException)
            {
                return new InitiateResponse { Success = false, Error = "cancelled" };
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[DeviceCode] Initiate exception");
                return new InitiateResponse { Success = false, Error = ex.Message };
            }
        }

        public async Task<PollResponse> PollAsync(string code, CancellationToken ct = default)
        {
            try
            {
                var payload = new JObject { ["code"] = code };
                var content = new StringContent(payload.ToString(), Encoding.UTF8, "application/json");
                var response = await _http.PostAsync($"{SERVER_URL}/v2/auth/device/poll", content, ct);
                var json = await response.Content.ReadAsStringAsync(ct);
                int statusCode = (int)response.StatusCode;

                switch (statusCode)
                {
                    case 200:
                    {
                        var obj = JObject.Parse(json);
                        var authToken = obj["auth_token"]?.ToString();
                        var unifiedId = obj["unified_id"]?.ToString();
                        var isNewUser = (bool?)obj["is_new_user"] ?? false;

                        // SP3 fix: server now piggybacks user data on confirmed
                        // (mirrors /v2/auth/patreon and /v2/auth/discord shape)
                        // so the desktop can populate local AppSettings before
                        // ProfileSync's anti-cheat guard refuses the post-login
                        // round-trip. Older proxy builds omit this field;
                        // null is handled gracefully by the caller.
                        V2AuthService.V2User? user = null;
                        var userToken = obj["user"];
                        if (userToken != null && userToken.Type == JTokenType.Object)
                        {
                            try
                            {
                                user = userToken.ToObject<V2AuthService.V2User>();
                            }
                            catch (Exception ex)
                            {
                                Log.Warning(ex, "[DeviceCode] Failed to parse user from poll response");
                            }
                        }

                        // Never log auth_token.
                        Log.Information("[DeviceCode] Confirmed uid={Uid} new={New} hasUser={HasUser}", unifiedId, isNewUser, user != null);
                        return new PollResponse
                        {
                            Status = PollStatus.Confirmed,
                            AuthToken = authToken,
                            UnifiedId = unifiedId,
                            IsNewUser = isNewUser,
                            User = user
                        };
                    }
                    case 202:
                        return new PollResponse { Status = PollStatus.Pending };
                    case 400:
                        return new PollResponse { Status = PollStatus.BadRequest };
                    case 401:
                        return new PollResponse { Status = PollStatus.Unauthorized };
                    case 404:
                        return new PollResponse { Status = PollStatus.NotFound };
                    case 410:
                        return new PollResponse { Status = PollStatus.Expired };
                    case 429:
                        return new PollResponse { Status = PollStatus.RateLimited };
                    case 503:
                        return new PollResponse { Status = PollStatus.ServiceUnavailable };
                    default:
                        Log.Warning("[DeviceCode] Poll unexpected status {Status}", statusCode);
                        return new PollResponse { Status = PollStatus.Unknown };
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Log.Warning("[DeviceCode] Poll exception: {Message}", ex.Message);
                return new PollResponse { Status = PollStatus.Unknown };
            }
        }

        private static string ParseErrorMessage(string body, System.Net.HttpStatusCode statusCode)
        {
            try
            {
                var obj = JObject.Parse(body);
                return obj["error"]?.ToString() ?? $"HTTP {(int)statusCode}";
            }
            catch
            {
                return $"HTTP {(int)statusCode}";
            }
        }
    }
}
