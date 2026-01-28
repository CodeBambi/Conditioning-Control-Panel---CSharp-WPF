using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Threading;
using Newtonsoft.Json;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// Handles syncing user progression (XP, level, achievements) to the cloud.
    /// Supports both Patreon and Discord authentication.
    /// </summary>
    public class ProfileSyncService : IDisposable
    {
        private const string ProxyBaseUrl = "https://codebambi-proxy.vercel.app";
        private const int HeartbeatIntervalSeconds = 45; // Send heartbeat every 45 seconds

        private readonly HttpClient _httpClient;
        private DispatcherTimer? _heartbeatTimer;
        private bool _disposed;
        private bool _syncEnabled = true;

        /// <summary>
        /// Whether using Patreon auth (vs Discord)
        /// </summary>
        private bool IsPatreonAuth => !string.IsNullOrEmpty(App.Patreon?.GetAccessToken());

        /// <summary>
        /// Whether using Discord auth
        /// </summary>
        private bool IsDiscordAuth => !IsPatreonAuth && !string.IsNullOrEmpty(App.Discord?.GetAccessToken());

        /// <summary>
        /// Get the appropriate access token (Patreon preferred, then Discord)
        /// </summary>
        private string? GetAccessToken() => App.Patreon?.GetAccessToken() ?? App.Discord?.GetAccessToken();

        /// <summary>
        /// Whether cloud sync is enabled (checks for either Patreon or Discord token)
        /// </summary>
        public bool IsSyncEnabled => _syncEnabled && App.IsLoggedIn;

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

        #region Heartbeat

        /// <summary>
        /// Start the heartbeat timer to keep user showing as online.
        /// Call this after successful Patreon authentication.
        /// </summary>
        public void StartHeartbeat()
        {
            if (_heartbeatTimer != null) return;

            _heartbeatTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(HeartbeatIntervalSeconds)
            };
            _heartbeatTimer.Tick += async (s, e) => await SendHeartbeatAsync();
            _heartbeatTimer.Start();

            // Send initial heartbeat immediately
            _ = SendHeartbeatAsync();

            App.Logger?.Information("Heartbeat started (every {Seconds}s)", HeartbeatIntervalSeconds);
        }

        /// <summary>
        /// Stop the heartbeat timer.
        /// Call this on logout or app shutdown.
        /// </summary>
        public void StopHeartbeat()
        {
            _heartbeatTimer?.Stop();
            _heartbeatTimer = null;
            App.Logger?.Debug("Heartbeat stopped");
        }

        /// <summary>
        /// Send a lightweight heartbeat to keep user showing as online.
        /// Only updates last_seen timestamp, doesn't sync full profile.
        /// </summary>
        private async Task SendHeartbeatAsync()
        {
            if (!IsSyncEnabled) return;

            try
            {
                var accessToken = GetAccessToken();
                if (string.IsNullOrEmpty(accessToken)) return;

                // Use appropriate endpoint based on auth type
                var endpoint = IsPatreonAuth ? "/user/heartbeat" : "/user/heartbeat-discord";
                var request = new HttpRequestMessage(HttpMethod.Post, $"{ProxyBaseUrl}{endpoint}");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                request.Content = new StringContent("{}", Encoding.UTF8, "application/json");

                var response = await _httpClient.SendAsync(request);

                if (response.IsSuccessStatusCode)
                {
                    App.Logger?.Debug("Heartbeat sent successfully");
                }
                else
                {
                    App.Logger?.Debug("Heartbeat failed: {Status}", response.StatusCode);
                }
            }
            catch (Exception ex)
            {
                // Silently fail - heartbeat is not critical
                App.Logger?.Debug("Heartbeat error: {Error}", ex.Message);
            }
        }

        #endregion

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
                var accessToken = GetAccessToken();
                if (string.IsNullOrEmpty(accessToken))
                {
                    App.Logger?.Warning("No access token available for profile sync");
                    return false;
                }

                // Use appropriate endpoint based on auth type
                var endpoint = IsPatreonAuth ? "/user/profile" : "/user/profile-discord";
                var request = new HttpRequestMessage(HttpMethod.Get, $"{ProxyBaseUrl}{endpoint}");
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
                    // Cloud profile doesn't exist - check if we have local progress to sync UP
                    var settings = App.Settings?.Current;
                    var localLevel = settings?.PlayerLevel ?? 1;
                    var localXp = settings?.PlayerXP ?? 0;

                    if (localLevel > 1 || localXp > 100)
                    {
                        // We have local progress but no cloud profile - sync UP immediately
                        // This handles cases where cloud profile was deleted/corrupted
                        App.Logger?.Warning("No cloud profile found but local has progress (Level {Level}, {XP} XP) - syncing UP to create cloud profile",
                            localLevel, (int)localXp);

                        // Trigger sync UP to create the cloud profile with local data
                        _ = Task.Run(async () =>
                        {
                            await Task.Delay(500); // Small delay
                            await SyncProfileAsync();
                        });
                    }
                    else
                    {
                        App.Logger?.Information("No cloud profile found for user {UserId} (new user)", result.UserId);
                    }

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
                var accessToken = GetAccessToken();
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
                    LastSession = DateTime.Now.ToString("o"),
                    AllowDiscordDm = settings.AllowDiscordDm,
                    DiscordId = App.Discord?.UserId  // Include Discord ID even when syncing via Patreon
                };

                // Use appropriate endpoint based on auth type
                var endpoint = IsPatreonAuth ? "/user/sync" : "/user/sync-discord";
                var request = new HttpRequestMessage(HttpMethod.Post, $"{ProxyBaseUrl}{endpoint}");
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
        /// Merge cloud profile with local data, taking the HIGHER values to prevent progress loss.
        /// This protects against cloud data corruption, sync issues, or stale cloud profiles.
        /// </summary>
        private void MergeCloudProfile(CloudProfile cloudProfile)
        {
            var settings = App.Settings?.Current;
            var achievements = App.Achievements;

            if (settings == null) return;

            bool needsSave = false;

            // Calculate total XP for both local and cloud to compare properly
            // Cloud stores TOTAL XP, local stores current-level XP
            var localTotalXp = App.Progression?.GetTotalXP(settings.PlayerLevel, settings.PlayerXP) ?? settings.PlayerXP;
            var cloudTotalXp = (double)cloudProfile.Xp;

            // TAKE HIGHER VALUES - prevents progress loss from cloud corruption/sync issues
            // This is safer than "cloud is truth" which can wipe legitimate progress
            if (cloudTotalXp > localTotalXp)
            {
                // Cloud has more progress - use cloud values
                var cloudLevelXp = App.Progression?.GetCurrentLevelXP(cloudProfile.Level, cloudProfile.Xp) ?? 0;

                App.Logger?.Information("Cloud has higher progress - syncing DOWN: Cloud Level {CloudLevel} ({CloudXP} total XP) > Local Level {LocalLevel} ({LocalXP} total XP)",
                    cloudProfile.Level, (int)cloudTotalXp, settings.PlayerLevel, (int)localTotalXp);

                settings.PlayerLevel = cloudProfile.Level;
                settings.PlayerXP = cloudLevelXp;
                needsSave = true;

                // Check for level-based achievements with the new level
                App.Achievements?.CheckLevelAchievements(cloudProfile.Level);
            }
            else if (localTotalXp > cloudTotalXp)
            {
                // Local has more progress - keep local, will sync UP on next SyncProfileAsync
                App.Logger?.Information("Local has higher progress - keeping local: Local Level {LocalLevel} ({LocalXP} total XP) > Cloud Level {CloudLevel} ({CloudXP} total XP)",
                    settings.PlayerLevel, (int)localTotalXp, cloudProfile.Level, (int)cloudTotalXp);

                // Trigger an immediate sync UP so cloud gets the correct data
                _ = Task.Run(async () =>
                {
                    await Task.Delay(1000); // Small delay to let startup complete
                    await SyncProfileAsync();
                });
            }
            else
            {
                App.Logger?.Debug("Local and cloud progress are equal: Level {Level}, Total XP {XP}",
                    settings.PlayerLevel, (int)localTotalXp);
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

            // Merge stats - take HIGHER values to prevent progress loss
            if (cloudProfile.Stats != null && achievements?.Progress != null)
            {
                var progress = achievements.Progress;

                if (cloudProfile.Stats.TryGetValue("longest_session_minutes", out var minutes))
                {
                    var m = Convert.ToDouble(minutes);
                    if (m > progress.LongestSessionMinutes)
                    {
                        App.Logger?.Debug("Stats sync: LongestSessionMinutes cloud ({Cloud}) > local ({Local})", m, progress.LongestSessionMinutes);
                        progress.LongestSessionMinutes = m;
                        needsSave = true;
                    }
                }
                if (cloudProfile.Stats.TryGetValue("total_flashes", out var flashes))
                {
                    var f = Convert.ToInt32(flashes);
                    if (f > progress.TotalFlashImages)
                    {
                        App.Logger?.Debug("Stats sync: TotalFlashImages cloud ({Cloud}) > local ({Local})", f, progress.TotalFlashImages);
                        progress.TotalFlashImages = f;
                        needsSave = true;
                    }
                }
                if (cloudProfile.Stats.TryGetValue("consecutive_days", out var streak))
                {
                    var st = Convert.ToInt32(streak);
                    if (st > progress.ConsecutiveDays)
                    {
                        App.Logger?.Debug("Stats sync: ConsecutiveDays cloud ({Cloud}) > local ({Local})", st, progress.ConsecutiveDays);
                        progress.ConsecutiveDays = st;
                        needsSave = true;
                    }
                }
                if (cloudProfile.Stats.TryGetValue("total_bubbles_popped", out var bubbles))
                {
                    var b = Convert.ToInt32(bubbles);
                    if (b > progress.TotalBubblesPopped)
                    {
                        App.Logger?.Debug("Stats sync: TotalBubblesPopped cloud ({Cloud}) > local ({Local})", b, progress.TotalBubblesPopped);
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
            StopHeartbeat();
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

            [JsonProperty("allow_discord_dm")]
            public bool AllowDiscordDm { get; set; }

            [JsonProperty("discord_id")]
            public string? DiscordId { get; set; }
        }

        #endregion
    }
}
