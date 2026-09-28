using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using ConditioningControlPanel.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// Service for v5.5+ authentication using the v2 API endpoints.
    /// Handles monthly seasons system with OG recognition.
    ///
    /// <para>Lives in Core so every head talks to the server through one client (decision:
    /// "Where OAuth and the V2 client live" = Core, WPF delegating). Settings come from the
    /// constructor argument (default <see cref="CoreSettings.Current"/>); the version from
    /// <see cref="CoreReleaseContent.AppVersion"/>. Applying a profile to settings stays in the
    /// WPF head (<c>V2AuthServiceHead.ApplyUserDataToSettings</c>) until unit 6.</para>
    /// </summary>
    public class V2AuthService
    {
        // One shared client per process, as before; configured on first use rather than at type
        // init so the head has seeded CoreReleaseContent.AppVersion by then.
        private static readonly Lazy<HttpClient> Shared = new(() => Configure(new HttpClient()));
        private readonly HttpClient _http;
        private readonly Func<AppSettings?> _settings;
        private const string SERVER_URL = "https://codebambi-proxy.vercel.app";

        /// <summary>
        /// Contract D (409 merged) handler. The recovery re-runs head sign-in, so the head seeds
        /// this with <c>MergedAccountRecovery.TryHandleAsync</c>. Unseeded = never handled.
        /// </summary>
        public static volatile Func<HttpResponseMessage, string?, Task<bool>>? MergedRecovery;

        private static Task<bool> TryHandleMergedAsync(HttpResponseMessage response, string? body = null) =>
            MergedRecovery?.Invoke(response, body) ?? Task.FromResult(false);

        /// <param name="settings">Where the auth token and unified id are read. Default: the head's settings service, or
        /// null before it exists (as WPF's <c>App.Settings?.Current</c> was; never the fallback's stored token).</param>
        /// <param name="handler">Test seam; null uses the shared production client.</param>
        public V2AuthService(Func<AppSettings?>? settings = null, HttpMessageHandler? handler = null)
        {
            _settings = settings ?? (() => CoreSettings.HasProvider ? CoreSettings.Service?.Current : null);
            _http = handler == null ? Shared.Value : Configure(new HttpClient(handler));
        }

        /// <summary>Redact auth_token and password values from JSON strings before logging.</summary>
        private static string RedactSensitiveFields(string json)
        {
            json = System.Text.RegularExpressions.Regex.Replace(
                json, @"""auth_token""\s*:\s*""[^""]+""", @"""auth_token"":""[REDACTED]""");
            json = System.Text.RegularExpressions.Regex.Replace(
                json, @"""password""\s*:\s*""[^""]+""", @"""password"":""[REDACTED]""");
            return json;
        }

        /// <summary>Safely extract error message from a response body that may not be JSON.</summary>
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

        private static HttpClient Configure(HttpClient http)
        {
            http.Timeout = TimeSpan.FromSeconds(30);
            http.DefaultRequestHeaders.Add("X-Client-Version", CoreReleaseContent.AppVersion);
            http.DefaultRequestHeaders.UserAgent.ParseAdd($"ConditioningControlPanel/{CoreReleaseContent.AppVersion}");
            return http;
        }

        #region Response Models

        public class V2AuthResponse
        {
            [JsonProperty("success")]
            public bool Success { get; set; }

            [JsonProperty("is_new_user")]
            public bool IsNewUser { get; set; }

            [JsonProperty("needs_registration")]
            public bool NeedsRegistration { get; set; }

            [JsonProperty("is_legacy_user")]
            public bool IsLegacyUser { get; set; }

            [JsonProperty("unified_id")]
            public string? UnifiedId { get; set; }

            [JsonProperty("legacy_data")]
            public LegacyData? LegacyData { get; set; }

            [JsonProperty("user")]
            public V2User? User { get; set; }

            [JsonProperty("discord")]
            public DiscordInfo? Discord { get; set; }

            [JsonProperty("patreon")]
            public PatreonInfo? Patreon { get; set; }

            [JsonProperty("error")]
            public string? Error { get; set; }

            [JsonProperty("auth_token")]
            public string? AuthToken { get; set; }

            // The same-email hint the server attaches to a needs_registration answer when an
            // existing account already carries this provider's verified email (server.js, the
            // autoLinkHint block of /v2/auth/discord and /v2/auth/patreon). It was on the wire
            // all along and never deserialised, which is why the desktop minted a twin instead
            // of telling the user their account already exists. Hint only: the client never
            // links on it (that needs the TARGET account's token), it ASKS.
            [JsonProperty("can_auto_link")]
            public bool CanAutoLink { get; set; }

            [JsonProperty("auto_link_unified_id")]
            public string? AutoLinkUnifiedId { get; set; }

            [JsonProperty("auto_link_display_name")]
            public string? AutoLinkDisplayName { get; set; }
        }

        public class LegacyData
        {
            [JsonProperty("display_name")]
            public string? DisplayName { get; set; }

            [JsonProperty("highest_level_ever")]
            public int HighestLevelEver { get; set; }

            [JsonProperty("achievements_count")]
            public int AchievementsCount { get; set; }

            [JsonProperty("unlocks")]
            public Unlocks? Unlocks { get; set; }
        }

        /// <summary>
        /// DO NOT ADD A DateTime OR DateTimeOffset MEMBER HERE without revisiting
        /// <see cref="Descent.DescentReader.ParseWire"/>.
        ///
        /// This DTO is materialised from a JObject that was loaded with
        /// DateParseHandling.None, because the additive `descent` block needs its
        /// date-ish STRINGS left as strings. Today every member is a string, a
        /// number, a bool or a nested object, so that setting is invisible here —
        /// Newtonsoft converts a plain string to a DateTime happily on ToObject. The
        /// moment a member's parsing depends on the token ALREADY being a Date, this
        /// class and that reader are coupled and only one of them says so.
        /// </summary>
        public class V2User
        {
            [JsonProperty("unified_id")]
            public string? UnifiedId { get; set; }

            [JsonProperty("display_name")]
            public string? DisplayName { get; set; }

            [JsonProperty("discord_id")]
            public string? DiscordId { get; set; }

            [JsonProperty("patreon_id")]
            public string? PatreonId { get; set; }

            [JsonProperty("level")]
            public int Level { get; set; }

            [JsonProperty("xp")]
            public int Xp { get; set; }

            // Which curve `level` is priced on (0 = v1, 1 = v2). /v2/user/profile only; null on
            // servers before CCP-Server #210 and on the auth projections.
            [JsonProperty("curve_epoch")]
            public int? CurveEpoch { get; set; }

            [JsonProperty("current_season")]
            public string? CurrentSeason { get; set; }

            [JsonProperty("highest_level_ever")]
            public int HighestLevelEver { get; set; }

            [JsonProperty("unlocks")]
            public Unlocks? Unlocks { get; set; }

            [JsonProperty("achievements")]
            public string[]? Achievements { get; set; }

            [JsonProperty("is_season0_og")]
            public bool IsSeason0Og { get; set; }

            [JsonProperty("patreon_tier")]
            public int PatreonTier { get; set; }

            [JsonProperty("patreon_is_active")]
            public bool PatreonIsActive { get; set; }

            [JsonProperty("patreon_is_whitelisted")]
            public bool PatreonIsWhitelisted { get; set; }

            // Every provider folded into one number (0..2, whitelist 2). Raw token on purpose: read
            // through EntitlementTierRule.ParseTier so a malformed value is "no signal", never a throw.
            [JsonProperty("effective_tier")]
            public JToken? EffectiveTierRaw { get; set; }
        }

        public class Unlocks
        {
            [JsonProperty("avatars")]
            public bool Avatars { get; set; }

            [JsonProperty("autonomy_mode")]
            public bool AutonomyMode { get; set; }

            [JsonProperty("takeover_mode")]
            public bool TakeoverMode { get; set; }

            [JsonProperty("ai_companion")]
            public bool AiCompanion { get; set; }
        }

        public class DiscordInfo
        {
            [JsonProperty("id")]
            public string? Id { get; set; }

            [JsonProperty("username")]
            public string? Username { get; set; }

            [JsonProperty("global_name")]
            public string? GlobalName { get; set; }

            [JsonProperty("avatar")]
            public string? Avatar { get; set; }
        }

        public class PatreonInfo
        {
            [JsonProperty("id")]
            public string? Id { get; set; }

            [JsonProperty("name")]
            public string? Name { get; set; }

            [JsonProperty("tier")]
            public int Tier { get; set; }

            [JsonProperty("is_active")]
            public bool IsActive { get; set; }
        }

        public class LinkResponse
        {
            [JsonProperty("success")]
            public bool Success { get; set; }

            [JsonProperty("unified_id")]
            public string? UnifiedId { get; set; }

            [JsonProperty("linked_provider")]
            public string? LinkedProvider { get; set; }

            [JsonProperty("error")]
            public string? Error { get; set; }

            [JsonProperty("auth_token")]
            public string? AuthToken { get; set; }
        }

        #endregion

        /// <summary>
        /// Authenticate with Discord using v2 API
        /// </summary>
        /// <param name="accessToken">Discord OAuth access token</param>
        /// <param name="displayName">Optional display name for registration</param>
        public async Task<V2AuthResponse> AuthenticateWithDiscordAsync(string accessToken, string? displayName = null)
        {
            try
            {
                var payload = new JObject
                {
                    ["access_token"] = accessToken
                };

                if (!string.IsNullOrWhiteSpace(displayName))
                {
                    payload["display_name"] = displayName;
                }

                var content = new StringContent(payload.ToString(), Encoding.UTF8, "application/json");
                var response = await _http.PostAsync($"{SERVER_URL}/v2/auth/discord", content);
                var json = await response.Content.ReadAsStringAsync();

                Log.Debug("[V2Auth] Discord auth response ({Bytes} bytes)", json?.Length ?? 0);

                if (!response.IsSuccessStatusCode)
                {
                    return new V2AuthResponse
                    {
                        Success = false,
                        Error = ParseErrorMessage(json, response.StatusCode)
                    };
                }

                return JsonConvert.DeserializeObject<V2AuthResponse>(json) ?? new V2AuthResponse { Success = false, Error = "Invalid response" };
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[V2Auth] Discord auth failed");
                return new V2AuthResponse { Success = false, Error = ex.Message };
            }
        }

        /// <summary>
        /// Authenticate with Patreon using v2 API
        /// </summary>
        /// <param name="accessToken">Patreon OAuth access token</param>
        /// <param name="displayName">Optional display name for registration</param>
        public async Task<V2AuthResponse> AuthenticateWithPatreonAsync(string accessToken, string? displayName = null)
        {
            try
            {
                var payload = new JObject
                {
                    ["access_token"] = accessToken
                };

                if (!string.IsNullOrWhiteSpace(displayName))
                {
                    payload["display_name"] = displayName;
                }

                var content = new StringContent(payload.ToString(), Encoding.UTF8, "application/json");
                var response = await _http.PostAsync($"{SERVER_URL}/v2/auth/patreon", content);
                var json = await response.Content.ReadAsStringAsync();

                Log.Debug("[V2Auth] Patreon auth response ({Bytes} bytes)", json?.Length ?? 0);

                if (!response.IsSuccessStatusCode)
                {
                    return new V2AuthResponse
                    {
                        Success = false,
                        Error = ParseErrorMessage(json, response.StatusCode)
                    };
                }

                return JsonConvert.DeserializeObject<V2AuthResponse>(json) ?? new V2AuthResponse { Success = false, Error = "Invalid response" };
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[V2Auth] Patreon auth failed");
                return new V2AuthResponse { Success = false, Error = ex.Message };
            }
        }

        /// <summary>
        /// Authenticate with SubscribeStar using v2 API. Mirrors the Patreon path:
        /// pass no displayName to probe (server returns needs_registration for a brand-new
        /// user); pass a chosen displayName to create the account with that name.
        /// </summary>
        /// <param name="accessToken">SubscribeStar OAuth access token</param>
        /// <param name="displayName">Optional display name for registration</param>
        public async Task<V2AuthResponse> AuthenticateWithSubstarAsync(string accessToken, string? displayName = null)
        {
            try
            {
                var payload = new JObject
                {
                    ["access_token"] = accessToken
                };

                if (!string.IsNullOrWhiteSpace(displayName))
                {
                    payload["display_name"] = displayName;
                }

                var content = new StringContent(payload.ToString(), Encoding.UTF8, "application/json");
                var response = await _http.PostAsync($"{SERVER_URL}/v2/auth/substar", content);
                var json = await response.Content.ReadAsStringAsync();

                Log.Debug("[V2Auth] SubscribeStar auth response ({Bytes} bytes)", json?.Length ?? 0);

                if (!response.IsSuccessStatusCode)
                {
                    return new V2AuthResponse
                    {
                        Success = false,
                        Error = ParseErrorMessage(json, response.StatusCode)
                    };
                }

                return JsonConvert.DeserializeObject<V2AuthResponse>(json) ?? new V2AuthResponse { Success = false, Error = "Invalid response" };
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[V2Auth] SubscribeStar auth failed");
                return new V2AuthResponse { Success = false, Error = ex.Message };
            }
        }

        /// <summary>
        /// Register a new account with invite code, display name, and password
        /// </summary>
        public async Task<V2AuthResponse> RegisterAsync(string inviteCode, string displayName, string password)
        {
            try
            {
                var payload = new JObject
                {
                    ["invite_code"] = inviteCode,
                    ["display_name"] = displayName,
                    ["password"] = password
                };

                var content = new StringContent(payload.ToString(), Encoding.UTF8, "application/json");
                var response = await _http.PostAsync($"{SERVER_URL}/v2/auth/register", content);
                var json = await response.Content.ReadAsStringAsync();

                Log.Debug("[V2Auth] Register response ({Bytes} bytes)", json?.Length ?? 0);

                if (!response.IsSuccessStatusCode)
                {
                    return new V2AuthResponse
                    {
                        Success = false,
                        Error = ParseErrorMessage(json, response.StatusCode)
                    };
                }

                return JsonConvert.DeserializeObject<V2AuthResponse>(json) ?? new V2AuthResponse { Success = false, Error = "Invalid response" };
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[V2Auth] Register failed");
                return new V2AuthResponse { Success = false, Error = "Registration failed. Please try again." };
            }
        }

        /// <summary>The identity half of a sign-in (WPF V2AuthServiceHead.ApplyUserDataToSettings, which calls
        /// this first). Linked-tier grace and the XP take-higher adopt stay in the WPF head until unit 6.</summary>
        public static void ApplyIdentity(AppSettings settings, V2User user, string? authToken)
        {
            settings.UnifiedId = user.UnifiedId;
            settings.UserDisplayName = user.DisplayName;
            settings.IsSeason0Og = user.IsSeason0Og;
            settings.CurrentSeason = user.CurrentSeason;
            settings.HighestLevelEver = user.HighestLevelEver;
            settings.HasLinkedDiscord = !string.IsNullOrEmpty(user.DiscordId);
            settings.HasLinkedPatreon = !string.IsNullOrEmpty(user.PatreonId);
            settings.PatreonTier = user.PatreonTier;
            // Store auth token if provided
            if (!string.IsNullOrEmpty(authToken)) settings.AuthToken = authToken;
        }

        public enum RestoreOutcome { Kept, Validated, Cleared, TokenCleared }

        /// <summary>
        /// The check half of WPF App.ValidateRestoredSessionAsync (App.xaml.cs:4191): POST
        /// /v2/auth/restore-session. 404 clears the unified id; 2xx stores a re-issued token; 401 clears the
        /// token (and the id for a legacy re-auth); anything else, or a network error, keeps the cache.
        /// Stops before the cloud profile load (unit 6). The caller clears the session's id on Cleared.
        /// </summary>
        public async Task<RestoreOutcome> ValidateRestoredSessionAsync(string unifiedId)
        {
            var settings = _settings();
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, $"{SERVER_URL}/v2/auth/restore-session")
                {
                    Content = new StringContent(new JObject { ["unified_id"] = unifiedId, ["client_version"] = CoreReleaseContent.AppVersion }.ToString(),
                        Encoding.UTF8, "application/json"),
                };
                var storedToken = settings?.AuthToken;
                if (!string.IsNullOrEmpty(storedToken)) request.Headers.Add("X-Auth-Token", storedToken);
                using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(10));
                var response = await _http.SendAsync(request, cts.Token);

                // Split-accounts contract D: the head's recovery owns a merge tombstone.
                if (await TryHandleMergedAsync(response)) return RestoreOutcome.Kept;

                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    Log.Warning("Restored session invalid (user not found on server). Clearing UnifiedUserId.");
                    if (settings != null) { settings.UnifiedId = null; CoreSettings.Save(); }
                    return RestoreOutcome.Cleared;
                }
                if (response.IsSuccessStatusCode)
                {
                    try
                    {
                        var authToken = JObject.Parse(await response.Content.ReadAsStringAsync())["auth_token"]?.ToString();
                        if (!string.IsNullOrEmpty(authToken) && settings != null)
                        {
                            settings.AuthToken = authToken;
                            CoreSettings.Save();
                            Log.Information("Stored auth token from restore-session.");
                        }
                    }
                    catch (Exception parseEx) { Log.Debug("Failed to parse restore-session auth token: {Error}", parseEx.Message); }
                    Log.Information("Restored session validated successfully.");
                    return RestoreOutcome.Validated;
                }
                if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    var legacy = (await response.Content.ReadAsStringAsync()).Contains("legacy_user_reauth_required");
                    Log.Warning(legacy ? "Restored session rejected (legacy user, no token ever issued). Clearing all auth state."
                                       : "Restored session rejected (invalid token). Clearing auth token.");
                    if (settings != null)
                    {
                        if (legacy) settings.UnifiedId = null;
                        settings.AuthToken = null;
                        CoreSettings.Save(suppressCloudBackup: true);
                    }
                    return legacy ? RestoreOutcome.Cleared : RestoreOutcome.TokenCleared;
                }
                Log.Warning("Session validation returned {Status} - keeping cached state.", response.StatusCode);
            }
            catch (Exception ex) { Log.Warning(ex, "Session validation failed (network error) - keeping cached state."); }
            return RestoreOutcome.Kept;
        }

        /// <summary>
        /// Login with display name and password
        /// </summary>
        public async Task<V2AuthResponse> LoginAsync(string displayName, string password)
        {
            try
            {
                var payload = new JObject
                {
                    ["display_name"] = displayName,
                    ["password"] = password
                };

                var content = new StringContent(payload.ToString(), Encoding.UTF8, "application/json");
                var response = await _http.PostAsync($"{SERVER_URL}/v2/auth/login", content);
                var json = await response.Content.ReadAsStringAsync();

                Log.Debug("[V2Auth] Login response ({Bytes} bytes)", json?.Length ?? 0);

                if (!response.IsSuccessStatusCode)
                {
                    return new V2AuthResponse
                    {
                        Success = false,
                        Error = ParseErrorMessage(json, response.StatusCode)
                    };
                }

                return JsonConvert.DeserializeObject<V2AuthResponse>(json) ?? new V2AuthResponse { Success = false, Error = "Invalid response" };
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[V2Auth] Login failed");
                return new V2AuthResponse { Success = false, Error = "Login failed. Please try again." };
            }
        }

        /// <summary>
        /// Link a second provider to an existing unified account
        /// </summary>
        /// <param name="unifiedId">Existing unified user ID</param>
        /// <param name="provider">"discord" or "patreon"</param>
        /// <param name="accessToken">OAuth access token for the provider</param>
        public async Task<LinkResponse> LinkProviderAsync(string unifiedId, string provider, string accessToken)
        {
            try
            {
                var payload = new JObject
                {
                    ["unified_id"] = unifiedId,
                    ["provider"] = provider,
                    ["access_token"] = accessToken
                };

                var request = new HttpRequestMessage(HttpMethod.Post, $"{SERVER_URL}/v2/auth/link")
                {
                    Content = new StringContent(payload.ToString(), Encoding.UTF8, "application/json")
                };
                AddAuthHeader(request);
                var response = await _http.SendAsync(request);
                var json = await response.Content.ReadAsStringAsync();

                Log.Debug("[V2Auth] Link response ({Bytes} bytes)", json?.Length ?? 0);

                if (!response.IsSuccessStatusCode)
                {
                    if (await TryHandleMergedAsync(response, json))
                        return new LinkResponse { Success = false, Error = "account_merged" };
                    return new LinkResponse
                    {
                        Success = false,
                        Error = ParseErrorMessage(json, response.StatusCode)
                    };
                }

                return JsonConvert.DeserializeObject<LinkResponse>(json) ?? new LinkResponse { Success = false, Error = "Invalid response" };
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[V2Auth] Link provider failed");
                return new LinkResponse { Success = false, Error = ex.Message };
            }
        }

        /// <summary>
        /// Get user profile from v2 API
        /// </summary>
        public async Task<V2User?> GetUserProfileAsync(string unifiedId)
            => (await GetUserProfileNodeAsync(unifiedId))?.ToObject<V2User>();

        /// <summary>
        /// The RAW `user` node of /v2/user/profile.
        ///
        /// V2User is a fixed DTO and drops every key it does not declare, which is
        /// fine for the account fields and useless for anything additive the server
        /// grows later. The `descent` block is exactly that (ccp-server server.js
        /// attachDescentBlocks) — it rides inside this node, ships only to accounts
        /// inside the rollout dial, and is read straight off the JSON by
        /// Services.Descent.DescentReader.
        ///
        /// One request path, one set of auth headers, one place a 401 can be handled:
        /// GetUserProfileAsync is a thin projection of this method rather than a
        /// second copy of it.
        /// </summary>
        public async Task<JObject?> GetUserProfileNodeAsync(string unifiedId)
        {
            try
            {
                var request = new HttpRequestMessage(HttpMethod.Get, $"{SERVER_URL}/v2/user/profile?unified_id={Uri.EscapeDataString(unifiedId)}");
                AddAuthHeader(request);
                var response = await _http.SendAsync(request);
                var json = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    // Contract D: profile fetch on a merge tombstone.
                    if (await TryHandleMergedAsync(response, json)) return null;

                    // TRUNCATED, and it matters more here than at the other call
                    // sites: the vat put this request on a 60s cadence, so a server
                    // erroring out with a fat body would write that whole body into
                    // the log once a minute for as long as the Trainer Card is open.
                    // The status plus the first 200 chars is all triage ever needs.
                    Log.Warning("[V2Auth] Get profile failed: {Status} (body {Bytes} bytes)",
                        (int)response.StatusCode, json?.Length ?? 0);
                    return null;
                }

                // NOT JObject.Parse: date coercion has to be off or every date-ish
                // string in the additive `descent` block reads as absent. See
                // Services.Descent.DescentReader.ParseWire for the whole trap.
                // V2User is unaffected — its DateTime members still deserialize from
                // plain strings.
                var result = Descent.DescentReader.ParseWire(json);
                return result?["user"] as JObject;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[V2Auth] Get profile failed");
                return null;
            }
        }

        /// <summary>First 200 characters of a response body, for a non-2xx log line.</summary>
        private static string TruncateForLog(string? body)
        {
            if (string.IsNullOrEmpty(body)) return string.Empty;
            return body.Length <= 200 ? body : body.Substring(0, 200) + "…";
        }

        /// <summary>
        /// Update user profile (XP, level, stats, achievements)
        /// </summary>
        public async Task<bool> UpdateUserProfileAsync(string unifiedId, int? xp = null, int? level = null,
            JObject? stats = null, string[]? achievements = null)
        {
            try
            {
                var payload = new JObject
                {
                    ["unified_id"] = unifiedId
                };

                if (xp.HasValue) payload["xp"] = xp.Value;
                if (level.HasValue) payload["level"] = level.Value;
                if (stats != null) payload["stats"] = stats;
                if (achievements != null) payload["achievements"] = new JArray(achievements);

                var request = new HttpRequestMessage(HttpMethod.Post, $"{SERVER_URL}/v2/user/update");
                AddAuthHeader(request);
                request.Content = new StringContent(payload.ToString(), Encoding.UTF8, "application/json");
                var response = await _http.SendAsync(request);

                if (await TryHandleMergedAsync(response)) return false;
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[V2Auth] Update profile failed");
                return false;
            }
        }

        /// <summary>
        /// Send heartbeat to update online status
        /// </summary>
        public async Task<bool> SendHeartbeatAsync(string unifiedId)
        {
            try
            {
                var payload = new JObject
                {
                    ["unified_id"] = unifiedId
                };

                var request = new HttpRequestMessage(HttpMethod.Post, $"{SERVER_URL}/v2/user/heartbeat");
                AddAuthHeader(request);
                request.Content = new StringContent(payload.ToString(), Encoding.UTF8, "application/json");
                var response = await _http.SendAsync(request);

                if (await TryHandleMergedAsync(response)) return false;
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Delete user account (GDPR)
        /// </summary>
        public async Task<bool> DeleteAccountAsync(string unifiedId)
        {
            try
            {
                var payload = new JObject
                {
                    ["unified_id"] = unifiedId,
                    ["confirmation"] = "DELETE"
                };

                var request = new HttpRequestMessage(HttpMethod.Post, $"{SERVER_URL}/v2/user/delete-account");
                AddAuthHeader(request);
                request.Content = new StringContent(payload.ToString(), Encoding.UTF8, "application/json");
                var response = await _http.SendAsync(request);

                if (await TryHandleMergedAsync(response)) return false;
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[V2Auth] Delete account failed");
                return false;
            }
        }

        /// <summary>
        /// Adds the X-Auth-Token header to a V2 API request if an auth token is available.
        /// </summary>
        private void AddAuthHeader(HttpRequestMessage request)
        {
            var token = _settings()?.AuthToken;
            if (!string.IsNullOrEmpty(token))
                request.Headers.Add("X-Auth-Token", token);
        }

        /// <summary>Result of /v2/auth/device/authorize (mobile QR pairing).</summary>
        public class MobileLinkResponse
        {
            public bool Success { get; set; }
            public string? LinkCode { get; set; }
            public DateTimeOffset ExpiresAt { get; set; }
            public string? QrPayload { get; set; }
            public string? Error { get; set; }
        }

        // Typed POCO for JsonConvert — same rationale as V2DeviceCodeService.InitiateRaw
        // (clean ISO 8601 'Z' → DateTimeOffset handling).
        private class MobileLinkRaw
        {
            [JsonProperty("success")] public bool Success { get; set; }
            [JsonProperty("link_code")] public string? LinkCode { get; set; }
            [JsonProperty("expires_at")] public DateTimeOffset ExpiresAt { get; set; }
            [JsonProperty("qr_payload")] public string? QrPayload { get; set; }
        }

        /// <summary>
        /// Request a short-lived one-time link code for pairing the mobile companion app.
        /// The phone scans the code (rendered as a QR by LinkPhoneDialog) and redeems it
        /// via /v2/auth/device/claim for its own device token — the desktop session is
        /// never invalidated. Requires a logged-in account (UnifiedId + AuthToken).
        /// </summary>
        public async Task<MobileLinkResponse> AuthorizeMobileLinkAsync()
        {
            try
            {
                var unifiedId = _settings()?.UnifiedId;
                if (string.IsNullOrEmpty(unifiedId))
                    return new MobileLinkResponse { Success = false, Error = "not_logged_in" };

                var payload = new JObject
                {
                    ["unified_id"] = unifiedId,
                    ["client"] = "ccp-desktop"
                };
                var request = new HttpRequestMessage(HttpMethod.Post, $"{SERVER_URL}/v2/auth/device/authorize")
                {
                    Content = new StringContent(payload.ToString(), Encoding.UTF8, "application/json")
                };
                AddAuthHeader(request);
                var response = await _http.SendAsync(request);
                var json = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    if (await TryHandleMergedAsync(response, json))
                        return new MobileLinkResponse { Success = false, Error = "account_merged" };
                    var error = ParseErrorMessage(json, response.StatusCode);
                    Log.Warning("[V2Auth] Mobile link authorize failed: {Status} {Error}", (int)response.StatusCode, error);
                    return new MobileLinkResponse { Success = false, Error = error };
                }

                var raw = JsonConvert.DeserializeObject<MobileLinkRaw>(json);
                if (raw == null || !raw.Success || string.IsNullOrEmpty(raw.LinkCode))
                    return new MobileLinkResponse { Success = false, Error = "invalid_response" };

                // Never log the code itself — it is a live credential for ~3 minutes.
                Log.Information("[V2Auth] Mobile link code issued (expires {ExpiresAt})", raw.ExpiresAt);
                return new MobileLinkResponse
                {
                    Success = true,
                    LinkCode = raw.LinkCode,
                    ExpiresAt = raw.ExpiresAt,
                    QrPayload = raw.QrPayload
                };
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[V2Auth] Mobile link authorize exception");
                return new MobileLinkResponse { Success = false, Error = ex.Message };
            }
        }
    }
}
