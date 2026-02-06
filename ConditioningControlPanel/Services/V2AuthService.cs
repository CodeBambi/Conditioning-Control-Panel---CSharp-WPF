using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// Service for v5.5+ authentication using the v2 API endpoints.
    /// Handles monthly seasons system with OG recognition.
    /// </summary>
    public class V2AuthService
    {
        private static readonly HttpClient _http = new();
        private const string SERVER_URL = "https://codebambi-proxy.vercel.app";

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

            [JsonProperty("email")]
            public string? Email { get; set; }

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

                Log.Debug("[V2Auth] Discord auth response: {Json}", json);

                if (!response.IsSuccessStatusCode)
                {
                    var error = JObject.Parse(json);
                    return new V2AuthResponse
                    {
                        Success = false,
                        Error = error["error"]?.ToString() ?? $"HTTP {response.StatusCode}"
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

                Log.Debug("[V2Auth] Patreon auth response: {Json}", json);

                if (!response.IsSuccessStatusCode)
                {
                    var error = JObject.Parse(json);
                    return new V2AuthResponse
                    {
                        Success = false,
                        Error = error["error"]?.ToString() ?? $"HTTP {response.StatusCode}"
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

                var content = new StringContent(payload.ToString(), Encoding.UTF8, "application/json");
                var response = await _http.PostAsync($"{SERVER_URL}/v2/auth/link", content);
                var json = await response.Content.ReadAsStringAsync();

                Log.Debug("[V2Auth] Link response: {Json}", json);

                if (!response.IsSuccessStatusCode)
                {
                    var error = JObject.Parse(json);
                    return new LinkResponse
                    {
                        Success = false,
                        Error = error["error"]?.ToString() ?? $"HTTP {response.StatusCode}"
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
        {
            try
            {
                var response = await _http.GetAsync($"{SERVER_URL}/v2/user/profile?unified_id={Uri.EscapeDataString(unifiedId)}");
                var json = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    Log.Warning("[V2Auth] Get profile failed: {Json}", json);
                    return null;
                }

                var result = JObject.Parse(json);
                return result["user"]?.ToObject<V2User>();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[V2Auth] Get profile failed");
                return null;
            }
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

                var content = new StringContent(payload.ToString(), Encoding.UTF8, "application/json");
                var response = await _http.PostAsync($"{SERVER_URL}/v2/user/update", content);

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

                var content = new StringContent(payload.ToString(), Encoding.UTF8, "application/json");
                var response = await _http.PostAsync($"{SERVER_URL}/v2/user/heartbeat", content);

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

                var content = new StringContent(payload.ToString(), Encoding.UTF8, "application/json");
                var response = await _http.PostAsync($"{SERVER_URL}/v2/user/delete-account", content);

                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[V2Auth] Delete account failed");
                return false;
            }
        }

        /// <summary>
        /// Apply v2 user data to local settings
        /// </summary>
        public void ApplyUserDataToSettings(V2User user)
        {
            var settings = App.Settings?.Current;
            if (settings == null) return;

            settings.UnifiedId = user.UnifiedId;
            settings.UserDisplayName = user.DisplayName;
            settings.IsSeason0Og = user.IsSeason0Og;
            settings.CurrentSeason = user.CurrentSeason;
            settings.HighestLevelEver = user.HighestLevelEver;
            settings.HasLinkedDiscord = !string.IsNullOrEmpty(user.DiscordId);
            settings.HasLinkedPatreon = !string.IsNullOrEmpty(user.PatreonId);
            settings.PatreonTier = user.PatreonTier;

            // Sync level/XP if server has newer data
            if (user.Level > 0)
            {
                settings.PlayerLevel = user.Level;
                settings.PlayerXP = user.Xp;
            }

            App.Settings?.Save();
        }
    }
}
