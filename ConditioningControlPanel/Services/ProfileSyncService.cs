using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// Handles syncing user progression (XP, level, achievements) to the cloud.
    /// Only syncs for authenticated Patreon users.
    /// </summary>
    public class ProfileSyncService : IDisposable
    {
        private const string ProxyBaseUrl = "https://codebambi-proxy.vercel.app";

        private readonly HttpClient _httpClient;
        private bool _disposed;
        private bool _syncEnabled = true;

        /// <summary>
        /// Whether cloud sync is enabled (checks for access token directly)
        /// </summary>
        public bool IsSyncEnabled => _syncEnabled && !string.IsNullOrEmpty(App.Patreon?.GetAccessToken());

        /// <summary>
        /// Last sync time
        /// </summary>
        public DateTime? LastSyncTime { get; private set; }

        /// <summary>
        /// Last sync error (if any)
        /// </summary>
        public string? LastSyncError { get; private set; }

        /// <summary>
        /// Event raised when cloud profile is loaded and merged with local data.
        /// MainWindow should subscribe to this to refresh UI.
        /// </summary>
        public event EventHandler? ProfileLoaded;

        public ProfileSyncService()
        {
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(30)
            };
        }

        /// <summary>
        /// Load profile from cloud and merge with local data.
        /// Called on startup after Patreon authentication.
        /// </summary>
        public async Task<bool> LoadProfileAsync()
        {
            if (!IsSyncEnabled)
            {
                App.Logger?.Debug("Profile sync skipped - not authenticated");
                return false;
            }

            try
            {
                var accessToken = App.Patreon.GetAccessToken();
                if (string.IsNullOrEmpty(accessToken))
                {
                    App.Logger?.Warning("No access token available for profile sync");
                    return false;
                }

                var request = new HttpRequestMessage(HttpMethod.Get, $"{ProxyBaseUrl}/user/profile");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

                var response = await _httpClient.SendAsync(request);

                if (!response.IsSuccessStatusCode)
                {
                    var error = await response.Content.ReadAsStringAsync();
                    App.Logger?.Warning("Profile load failed: {Status} - {Error}", response.StatusCode, error);
                    LastSyncError = $"Load failed: {response.StatusCode}";
                    return false;
                }

                var json = await response.Content.ReadAsStringAsync();
                var result = JsonConvert.DeserializeObject<ProfileResponse>(json);

                if (result == null)
                {
                    App.Logger?.Warning("Profile load returned null");
                    return false;
                }

                if (!result.Exists || result.Profile == null)
                {
                    App.Logger?.Information("No cloud profile found for user {UserId}", result.UserId);
                    return true; // Not an error, just no profile yet
                }

                // Merge cloud profile with local
                MergeCloudProfile(result.Profile);

                LastSyncTime = DateTime.Now;
                LastSyncError = null;

                App.Logger?.Information("Loaded cloud profile: Level {Level}, {Xp} XP, {Achievements} achievements",
                    result.Profile.Level, result.Profile.Xp, result.Profile.Achievements?.Count ?? 0);

                // Notify listeners (MainWindow) to refresh UI
                ProfileLoaded?.Invoke(this, EventArgs.Empty);

                return true;
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Failed to load cloud profile");
                LastSyncError = ex.Message;
                return false;
            }
        }

        /// <summary>
        /// Sync local progression to cloud.
        /// Called after sessions and periodically.
        /// </summary>
        public async Task<bool> SyncProfileAsync()
        {
            if (!IsSyncEnabled)
            {
                App.Logger?.Debug("Profile sync skipped - not authenticated");
                return false;
            }

            try
            {
                var accessToken = App.Patreon.GetAccessToken();
                if (string.IsNullOrEmpty(accessToken))
                {
                    App.Logger?.Warning("No access token available for profile sync");
                    return false;
                }

                // Gather local progression data from Settings
                var settings = App.Settings?.Current;
                var achievements = App.Achievements;

                if (settings == null)
                {
                    App.Logger?.Warning("Settings not available for profile sync");
                    return false;
                }

                // Get achievement stats for additional tracking
                var achievementProgress = achievements?.Progress;

                // Calculate total accumulated XP (sum of all levels + current progress)
                var totalXp = App.Progression?.GetTotalXP(settings.PlayerLevel, settings.PlayerXP) ?? settings.PlayerXP;

                App.Logger?.Information("Syncing profile - Level: {Level}, TotalXP: {Xp}, VideoMinutes: {VideoMin:F1}, LockCards: {LockCards}",
                    settings.PlayerLevel,
                    (int)totalXp,
                    achievementProgress?.TotalVideoMinutes ?? 0,
                    achievementProgress?.TotalLockCardsCompleted ?? 0);

                var syncData = new ProfileSyncData
                {
                    Xp = (int)totalXp,
                    Level = settings.PlayerLevel,
                    Achievements = achievementProgress?.UnlockedAchievements?.ToList() ?? new List<string>(),
                    Stats = new Dictionary<string, object>
                    {
                        ["completed_sessions"] = achievementProgress?.CompletedSessions?.Count ?? 0,
                        ["longest_session_minutes"] = achievementProgress?.LongestSessionMinutes ?? 0,
                        ["total_flashes"] = achievementProgress?.TotalFlashImages ?? 0,
                        ["consecutive_days"] = achievementProgress?.ConsecutiveDays ?? 0,
                        ["total_bubbles_popped"] = achievementProgress?.TotalBubblesPopped ?? 0,
                        ["total_video_minutes"] = Math.Round(achievementProgress?.TotalVideoMinutes ?? 0, 1),
                        ["total_lock_cards_completed"] = achievementProgress?.TotalLockCardsCompleted ?? 0
                    },
                    LastSession = DateTime.Now.ToString("o")
                };

                var request = new HttpRequestMessage(HttpMethod.Post, $"{ProxyBaseUrl}/user/sync");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                request.Content = new StringContent(
                    JsonConvert.SerializeObject(syncData),
                    Encoding.UTF8,
                    "application/json"
                );

                var response = await _httpClient.SendAsync(request);

                if (!response.IsSuccessStatusCode)
                {
                    var error = await response.Content.ReadAsStringAsync();
                    App.Logger?.Warning("Profile sync failed: {Status} - {Error}", response.StatusCode, error);
                    LastSyncError = $"Sync failed: {response.StatusCode}";
                    return false;
                }

                var json = await response.Content.ReadAsStringAsync();
                var result = JsonConvert.DeserializeObject<SyncResponse>(json);

                LastSyncTime = DateTime.Now;
                LastSyncError = null;

                App.Logger?.Information("Profile synced to cloud: Level {Level}, {Xp} XP (merged: {Merged})",
                    result?.Profile?.Level, result?.Profile?.Xp, result?.Merged);

                // If server had higher values, update local
                if (result?.Profile != null && result.Merged)
                {
                    MergeCloudProfile(result.Profile);
                }

                return true;
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Failed to sync profile to cloud");
                LastSyncError = ex.Message;
                return false;
            }
        }

        /// <summary>
        /// Merge cloud profile data with local progression.
        /// Takes the higher value for XP/level, union for achievements.
        /// </summary>
        private void MergeCloudProfile(CloudProfile cloudProfile)
        {
            var settings = App.Settings?.Current;
            var achievements = App.Achievements;

            if (settings == null) return;

            bool needsSave = false;

            // Compare by level (primary progression indicator)
            // Cloud stores total XP, but level is what matters for local state
            if (cloudProfile.Level > settings.PlayerLevel)
            {
                App.Logger?.Information("Cloud has higher level ({CloudLevel} vs {LocalLevel}), updating local",
                    cloudProfile.Level, settings.PlayerLevel);
                settings.PlayerLevel = cloudProfile.Level;
                needsSave = true;

                // Check for level-based achievements that may have been missed
                App.Achievements?.CheckLevelAchievements(cloudProfile.Level);
            }

            // Merge achievements
            if (cloudProfile.Achievements != null && achievements?.Progress != null)
            {
                foreach (var achievementId in cloudProfile.Achievements)
                {
                    if (!achievements.Progress.IsUnlocked(achievementId))
                    {
                        App.Logger?.Information("Unlocking achievement from cloud: {AchievementId}", achievementId);
                        achievements.Progress.Unlock(achievementId);
                        needsSave = true;
                    }
                }
            }

            // Merge stats into achievement progress
            if (cloudProfile.Stats != null && achievements?.Progress != null)
            {
                var progress = achievements.Progress;

                if (cloudProfile.Stats.TryGetValue("longest_session_minutes", out var minutes))
                {
                    var m = Convert.ToDouble(minutes);
                    if (m > progress.LongestSessionMinutes)
                    {
                        progress.LongestSessionMinutes = m;
                        needsSave = true;
                    }
                }
                if (cloudProfile.Stats.TryGetValue("total_flashes", out var flashes))
                {
                    var f = Convert.ToInt32(flashes);
                    if (f > progress.TotalFlashImages)
                    {
                        progress.TotalFlashImages = f;
                        needsSave = true;
                    }
                }
                if (cloudProfile.Stats.TryGetValue("consecutive_days", out var streak))
                {
                    var st = Convert.ToInt32(streak);
                    if (st > progress.ConsecutiveDays)
                    {
                        progress.ConsecutiveDays = st;
                        needsSave = true;
                    }
                }
                if (cloudProfile.Stats.TryGetValue("total_bubbles_popped", out var bubbles))
                {
                    var b = Convert.ToInt32(bubbles);
                    if (b > progress.TotalBubblesPopped)
                    {
                        progress.TotalBubblesPopped = b;
                        needsSave = true;
                    }
                }
            }

            // Save merged data
            if (needsSave)
            {
                App.Settings?.Save();
                achievements?.Save();
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _httpClient.Dispose();
        }

        #region DTOs

        private class ProfileResponse
        {
            [JsonProperty("exists")]
            public bool Exists { get; set; }

            [JsonProperty("user_id")]
            public string? UserId { get; set; }

            [JsonProperty("patron_name")]
            public string? PatronName { get; set; }

            [JsonProperty("profile")]
            public CloudProfile? Profile { get; set; }
        }

        private class SyncResponse
        {
            [JsonProperty("success")]
            public bool Success { get; set; }

            [JsonProperty("user_id")]
            public string? UserId { get; set; }

            [JsonProperty("profile")]
            public CloudProfile? Profile { get; set; }

            [JsonProperty("merged")]
            public bool Merged { get; set; }
        }

        private class CloudProfile
        {
            [JsonProperty("xp")]
            public int Xp { get; set; }

            [JsonProperty("level")]
            public int Level { get; set; }

            [JsonProperty("achievements")]
            public List<string>? Achievements { get; set; }

            [JsonProperty("stats")]
            public Dictionary<string, object>? Stats { get; set; }

            [JsonProperty("last_session")]
            public string? LastSession { get; set; }

            [JsonProperty("updated_at")]
            public string? UpdatedAt { get; set; }
        }

        private class ProfileSyncData
        {
            [JsonProperty("xp")]
            public int Xp { get; set; }

            [JsonProperty("level")]
            public int Level { get; set; }

            [JsonProperty("achievements")]
            public List<string>? Achievements { get; set; }

            [JsonProperty("stats")]
            public Dictionary<string, object>? Stats { get; set; }

            [JsonProperty("last_session")]
            public string? LastSession { get; set; }
        }

        #endregion
    }
}
