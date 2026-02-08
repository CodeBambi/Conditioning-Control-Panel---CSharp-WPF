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
        private bool _pendingQuestResetClear;
        private bool _pendingSkillsResetAck;

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
            // Skip if offline mode is enabled
            if (App.Settings?.Current?.OfflineMode == true) return;

            if (!IsSyncEnabled) return;

            try
            {
                var accessToken = GetAccessToken();
                if (string.IsNullOrEmpty(accessToken)) return;

                // Use V2 heartbeat if user has unified_id (new v5.5 system)
                var unifiedId = App.Settings?.Current?.UnifiedId;
                if (!string.IsNullOrEmpty(unifiedId))
                {
                    // V2 heartbeat - uses unified_id
                    var v2Request = new HttpRequestMessage(HttpMethod.Post, $"{ProxyBaseUrl}/v2/user/heartbeat");
                    v2Request.Content = new StringContent(
                        Newtonsoft.Json.JsonConvert.SerializeObject(new { unified_id = unifiedId }),
                        Encoding.UTF8, "application/json");

                    var v2Response = await _httpClient.SendAsync(v2Request);
                    App.Logger?.Debug("V2 Heartbeat: {Status}", v2Response.StatusCode);
                    return;
                }

                // Legacy heartbeat - use appropriate endpoint based on auth type
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
            // Skip if offline mode is enabled
            if (App.Settings?.Current?.OfflineMode == true)
            {
                App.Logger?.Debug("Profile sync skipped - offline mode enabled");
                return false;
            }

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

                App.Logger?.Information("Loaded cloud profile: Level {Level}, {Xp} XP, {Achievements} achievements, {SkillPoints} skill points, {UnlockedSkills} skills",
                    result.Profile.Level, result.Profile.Xp, result.Profile.Achievements?.Count ?? 0,
                    result.Profile.SkillPoints ?? 0, result.Profile.UnlockedSkills?.Count ?? 0);

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
            // Skip if offline mode is enabled
            if (App.Settings?.Current?.OfflineMode == true)
            {
                App.Logger?.Debug("Profile sync skipped - offline mode enabled");
                return false;
            }

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

                // Use V2 sync if user has unified_id (new v5.5 system)
                var unifiedId = App.Settings?.Current?.UnifiedId;
                if (!string.IsNullOrEmpty(unifiedId))
                {
                    var questProgress = App.Quests?.Progress;
                    var v2SyncData = new
                    {
                        unified_id = unifiedId,
                        xp = (int)totalXp,
                        level = settings.PlayerLevel,
                        achievements = achievementProgress?.UnlockedAchievements?.ToList() ?? new List<string>(),
                        stats = new Dictionary<string, object>
                        {
                            ["completed_sessions"] = achievementProgress?.CompletedSessions?.Count ?? 0,
                            ["longest_session_minutes"] = achievementProgress?.LongestSessionMinutes ?? 0,
                            ["highest_streak"] = settings.HighestStreak,
                            ["total_flashes"] = achievementProgress?.TotalFlashImages ?? 0,
                            ["consecutive_days"] = achievementProgress?.ConsecutiveDays ?? 0,
                            ["total_bubbles_popped"] = achievementProgress?.TotalBubblesPopped ?? 0,
                            ["total_video_minutes"] = Math.Round(achievementProgress?.TotalVideoMinutes ?? 0, 1),
                            ["total_lock_cards_completed"] = achievementProgress?.TotalLockCardsCompleted ?? 0,
                            // Quest streak data
                            ["daily_quest_streak"] = settings.DailyQuestStreak,
                            ["last_daily_quest_date"] = settings.LastDailyQuestDate?.ToString("o") ?? "",
                            ["quest_completion_dates"] = questProgress?.DailyQuestCompletionDates?
                                .Select(d => d.ToString("yyyy-MM-dd")).ToList() ?? new List<string>(),
                            ["total_daily_quests_completed"] = questProgress?.TotalDailyQuestsCompleted ?? 0,
                            ["total_weekly_quests_completed"] = questProgress?.TotalWeeklyQuestsCompleted ?? 0,
                            ["total_xp_from_quests"] = questProgress?.TotalXPFromQuests ?? 0
                        },
                        unlocked_skills = settings.UnlockedSkills?.ToList() ?? new List<string>(),
                        skill_points = settings.SkillPoints,
                        allow_discord_dm = settings.AllowDiscordDm,
                        show_online_status = settings.ShowOnlineStatus,
                        share_profile_picture = settings.ShareProfilePicture,
                        // Send false to clear server-side reset flags only when acknowledging
                        reset_weekly_quest = false,
                        reset_daily_quest = false,
                        force_streak_override = false,
                        force_skills_reset = _pendingSkillsResetAck ? (bool?)false : null
                    };

                    var v2Request = new HttpRequestMessage(HttpMethod.Post, $"{ProxyBaseUrl}/v2/user/sync");
                    v2Request.Content = new StringContent(
                        JsonConvert.SerializeObject(v2SyncData),
                        Encoding.UTF8,
                        "application/json"
                    );

                    var v2Response = await _httpClient.SendAsync(v2Request);

                    if (!v2Response.IsSuccessStatusCode)
                    {
                        var error = await v2Response.Content.ReadAsStringAsync();
                        App.Logger?.Warning("V2 Profile sync failed: {Status} - {Error}", v2Response.StatusCode, error);
                        LastSyncError = $"Sync failed: {v2Response.StatusCode}";
                        return false;
                    }

                    LastSyncTime = DateTime.Now;
                    LastSyncError = null;

                    var v2Json = await v2Response.Content.ReadAsStringAsync();
                    App.Logger?.Information("V2 Profile synced successfully: {Response}", v2Json);

                    // Check for server-side flags in V2 sync response
                    try
                    {
                        var v2Result = JsonConvert.DeserializeObject<V2SyncResponse>(v2Json);
                        if (v2Result?.ResetWeeklyQuest == true)
                        {
                            App.Logger?.Information("V2 Sync: Server requested weekly quest reset");
                            App.Quests?.ForceRegenerateWeeklyQuest();
                        }
                        if (v2Result?.ResetDailyQuest == true)
                        {
                            App.Logger?.Information("V2 Sync: Server requested daily quest reset");
                            App.Quests?.ForceRegenerateDailyQuest();
                        }

                        // Handle force_streak_override - adopt server values even if lower
                        if (v2Result?.ForceStreakOverride == true && v2Result.StreakStats != null)
                        {
                            App.Logger?.Information("V2 Sync: Force streak override - adopting server streak values");
                            ApplyForceStreakOverride(v2Result.StreakStats);
                        }

                        // Handle force_skills_reset - clear all skills and refund points
                        if (v2Result?.ForceSkillsReset == true)
                        {
                            App.Logger?.Information("V2 Sync: Force skills reset - clearing all skills");
                            ApplyForceSkillsReset(v2Result.SkillPoints);
                            _pendingSkillsResetAck = true; // Acknowledge on next sync to clear server flag
                        }
                        else if (_pendingSkillsResetAck)
                        {
                            // Server flag was cleared by our acknowledgment
                            _pendingSkillsResetAck = false;
                        }
                        else if (v2Result?.SkillPoints.HasValue == true && v2Result.SkillPoints.Value != settings.SkillPoints)
                        {
                            // Server is source of truth for skill points
                            App.Logger?.Information("V2 Sync: Skill points server={Server} local={Local} — using server value",
                                v2Result.SkillPoints.Value, settings.SkillPoints);
                            settings.SkillPoints = v2Result.SkillPoints.Value;
                            App.Settings?.Save();
                        }

                        // Sync oopsie insurance season usage from server
                        if (v2Result?.OopsieUsedSeason != null)
                        {
                            var currentSeason = DateTime.UtcNow.ToString("yyyy-MM");
                            var oopsieUsed = v2Result.OopsieUsedSeason == currentSeason;
                            if (settings.SeasonalStreakRecoveryUsed != oopsieUsed)
                            {
                                settings.SeasonalStreakRecoveryUsed = oopsieUsed;
                                App.Settings?.Save();
                                App.Logger?.Information("V2 Sync: Oopsie insurance season sync - used={Used} (season={Season})", oopsieUsed, v2Result.OopsieUsedSeason);
                            }
                        }

                        // Sync display name from server (server is authoritative — admin renames, etc.)
                        if (!string.IsNullOrEmpty(v2Result?.User?.DisplayName) &&
                            v2Result.User.DisplayName != settings.UserDisplayName)
                        {
                            App.Logger?.Information("V2 Sync: display name updated from server: \"{Old}\" -> \"{New}\"",
                                settings.UserDisplayName, v2Result.User.DisplayName);
                            settings.UserDisplayName = v2Result.User.DisplayName;
                            App.Settings?.Save();
                        }

                        // Sync OG status from server (server is authoritative)
                        if (v2Result?.IsSeason0Og != null && settings.IsSeason0Og != v2Result.IsSeason0Og.Value)
                        {
                            settings.IsSeason0Og = v2Result.IsSeason0Og.Value;
                            App.Settings?.Save();
                            App.Logger?.Information("V2 Sync: OG status synced from server: {IsOg}", v2Result.IsSeason0Og.Value);
                        }

                        // Sync whitelist status from server — enables Patreon features for whitelisted users
                        // even if they never did Patreon OAuth (e.g. Discord-only users)
                        if (v2Result?.PatreonIsWhitelisted == true)
                        {
                            // Refresh the cached premium access window (25h > sync interval)
                            settings.PatreonPremiumValidUntil = DateTime.UtcNow.AddHours(25);
                            App.Settings?.Save();
                            App.Logger?.Information("V2 Sync: Whitelisted user — premium access granted via sync");
                        }

                        // Sync highest_level_ever from server (server is authoritative)
                        if (v2Result?.User?.HighestLevelEver != null)
                        {
                            var serverHighest = v2Result.User.HighestLevelEver.Value;
                            if (serverHighest != settings.HighestLevelEver)
                            {
                                App.Logger?.Information("V2 Sync: highest_level_ever server={Server} local={Local} — using server value",
                                    serverHighest, settings.HighestLevelEver);
                                settings.HighestLevelEver = serverHighest;
                                App.Settings?.Save();
                            }
                        }

                        // Handle level_reset — server admin reset all levels, force client to accept
                        if (v2Result?.LevelReset == true && v2Result.User != null)
                        {
                            var serverLevel = v2Result.User.Level;
                            var serverXp = v2Result.User.Xp;
                            var serverLevelXp = App.Progression?.GetCurrentLevelXP(serverLevel, serverXp) ?? 0;

                            App.Logger?.Information("V2 Sync: Level reset by admin — forcing Level {Level}, XP {Xp}", serverLevel, serverXp);
                            settings.PlayerLevel = serverLevel;
                            settings.PlayerXP = serverLevelXp;
                            // Use server's highest_level_ever (preserved across resets for permanent unlocks)
                            settings.HighestLevelEver = v2Result.User.HighestLevelEver ?? 0;
                            App.Settings?.Save();
                        }
                    }
                    catch (Exception parseEx)
                    {
                        App.Logger?.Debug("V2 Sync: Could not parse server flags: {Error}", parseEx.Message);
                    }

                    return true;
                }

                // Legacy sync for users without unified_id
                var legacyQuestProgress = App.Quests?.Progress;
                var syncData = new ProfileSyncData
                {
                    Xp = (int)totalXp,
                    Level = settings.PlayerLevel,
                    Achievements = achievementProgress?.UnlockedAchievements?.ToList() ?? new List<string>(),
                    Stats = new Dictionary<string, object>
                    {
                        ["completed_sessions"] = achievementProgress?.CompletedSessions?.Count ?? 0,
                        ["longest_session_minutes"] = achievementProgress?.LongestSessionMinutes ?? 0,
                        ["highest_streak"] = settings.HighestStreak,
                        ["total_flashes"] = achievementProgress?.TotalFlashImages ?? 0,
                        ["consecutive_days"] = achievementProgress?.ConsecutiveDays ?? 0,
                        ["total_bubbles_popped"] = achievementProgress?.TotalBubblesPopped ?? 0,
                        ["total_video_minutes"] = Math.Round(achievementProgress?.TotalVideoMinutes ?? 0, 1),
                        ["total_lock_cards_completed"] = achievementProgress?.TotalLockCardsCompleted ?? 0,
                        // Attention check stats
                        ["total_attention_checks_passed"] = achievementProgress?.TotalAttentionChecksPassed ?? 0,
                        ["video_attention_checks_passed"] = achievementProgress?.VideoAttentionChecksPassed ?? 0,
                        ["video_attention_checks_failed"] = achievementProgress?.VideoAttentionChecksFailed ?? 0,
                        ["total_attention_check_failures"] = achievementProgress?.AttentionCheckFailures ?? 0,
                        // Bubble count stats
                        ["total_bubble_count_games"] = achievementProgress?.TotalBubbleCountGames ?? 0,
                        ["total_bubble_count_correct"] = achievementProgress?.TotalBubbleCountCorrect ?? 0,
                        ["total_bubble_count_failed"] = achievementProgress?.TotalBubbleCountFailed ?? 0,
                        ["bubble_count_best_streak"] = achievementProgress?.BubbleCountBestStreak ?? 0,
                        // Session stats
                        ["total_sessions_started"] = achievementProgress?.TotalSessionsStarted ?? 0,
                        ["total_sessions_abandoned"] = achievementProgress?.TotalSessionsAbandoned ?? 0,
                        // XP & Progression stats
                        ["total_xp_earned"] = Math.Round(achievementProgress?.TotalXPEarned ?? 0, 0),
                        ["total_skill_points_earned"] = achievementProgress?.TotalSkillPointsEarned ?? 0,
                        // Time stats
                        ["total_pink_filter_minutes"] = Math.Round(achievementProgress?.TotalPinkFilterMinutes ?? 0, 1),
                        ["total_spiral_minutes"] = Math.Round(achievementProgress?.TotalSpiralMinutes ?? 0, 1),
                        // Quest streak data
                        ["daily_quest_streak"] = settings.DailyQuestStreak,
                        ["last_daily_quest_date"] = settings.LastDailyQuestDate?.ToString("o") ?? "",
                        ["quest_completion_dates"] = legacyQuestProgress?.DailyQuestCompletionDates?
                            .Select(d => d.ToString("yyyy-MM-dd")).ToList() ?? new List<string>(),
                        ["total_daily_quests_completed"] = legacyQuestProgress?.TotalDailyQuestsCompleted ?? 0,
                        ["total_weekly_quests_completed"] = legacyQuestProgress?.TotalWeeklyQuestsCompleted ?? 0,
                        ["total_xp_from_quests"] = legacyQuestProgress?.TotalXPFromQuests ?? 0
                    },
                    LastSession = DateTime.Now.ToString("o"),
                    AllowDiscordDm = settings.AllowDiscordDm,
                    ShareProfilePicture = settings.ShareProfilePicture,
                    ShowOnlineStatus = settings.ShowOnlineStatus,
                    DiscordId = App.Discord?.UserId,  // Include Discord ID even when syncing via Patreon
                    AvatarUrl = App.Discord?.GetAvatarUrl(256),  // Include Discord avatar URL
                    SkillPoints = settings.SkillPoints,
                    UnlockedSkills = settings.UnlockedSkills?.ToList() ?? new List<string>(),
                    TotalConditioningMinutes = settings.TotalConditioningMinutes
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

            // Merge quest streak data (skip if force_streak_override is active - handled separately)
            if (cloudProfile.Stats != null && cloudProfile.ForceStreakOverride != true)
            {
                // Take higher streak
                if (cloudProfile.Stats.TryGetValue("daily_quest_streak", out var cloudStreak))
                {
                    var cs = Convert.ToInt32(cloudStreak);
                    if (cs > settings.DailyQuestStreak)
                    {
                        App.Logger?.Debug("Quest sync: DailyQuestStreak cloud ({Cloud}) > local ({Local})", cs, settings.DailyQuestStreak);
                        settings.DailyQuestStreak = cs;
                        needsSave = true;
                    }
                }

                // Take most recent last_daily_quest_date
                if (cloudProfile.Stats.TryGetValue("last_daily_quest_date", out var cloudLastDate))
                {
                    var dateStr = cloudLastDate?.ToString();
                    if (!string.IsNullOrEmpty(dateStr) && DateTime.TryParse(dateStr, out var cloudDate))
                    {
                        if (!settings.LastDailyQuestDate.HasValue || cloudDate.Date > settings.LastDailyQuestDate.Value.Date)
                        {
                            App.Logger?.Debug("Quest sync: LastDailyQuestDate cloud ({Cloud}) > local ({Local})", cloudDate.Date, settings.LastDailyQuestDate);
                            settings.LastDailyQuestDate = cloudDate.Date;
                            needsSave = true;
                        }
                    }
                }

                // Merge completion dates (union of both sets)
                var questProgress = App.Quests?.Progress;
                if (questProgress != null && cloudProfile.Stats.TryGetValue("quest_completion_dates", out var cloudDatesObj))
                {
                    try
                    {
                        var cloudDates = JsonConvert.DeserializeObject<List<string>>(cloudDatesObj?.ToString() ?? "[]");
                        if (cloudDates != null)
                        {
                            var localDates = new HashSet<DateTime>(questProgress.DailyQuestCompletionDates.Select(d => d.Date));
                            bool datesChanged = false;
                            foreach (var ds in cloudDates)
                            {
                                if (DateTime.TryParse(ds, out var d) && !localDates.Contains(d.Date))
                                {
                                    questProgress.DailyQuestCompletionDates.Add(d.Date);
                                    datesChanged = true;
                                }
                            }
                            if (datesChanged)
                            {
                                // Trim to last 30 days
                                var cutoff = DateTime.Today.AddDays(-30);
                                questProgress.DailyQuestCompletionDates.RemoveAll(d => d.Date < cutoff);
                                App.Logger?.Debug("Quest sync: Merged completion dates from cloud");
                                needsSave = true;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        App.Logger?.Debug("Quest sync: Failed to parse cloud completion dates: {Error}", ex.Message);
                    }
                }

                // Take higher quest totals
                if (questProgress != null)
                {
                    if (cloudProfile.Stats.TryGetValue("total_daily_quests_completed", out var cloudDailyTotal))
                    {
                        var cdt = Convert.ToInt32(cloudDailyTotal);
                        if (cdt > questProgress.TotalDailyQuestsCompleted)
                        {
                            questProgress.TotalDailyQuestsCompleted = cdt;
                            needsSave = true;
                        }
                    }
                    if (cloudProfile.Stats.TryGetValue("total_weekly_quests_completed", out var cloudWeeklyTotal))
                    {
                        var cwt = Convert.ToInt32(cloudWeeklyTotal);
                        if (cwt > questProgress.TotalWeeklyQuestsCompleted)
                        {
                            questProgress.TotalWeeklyQuestsCompleted = cwt;
                            needsSave = true;
                        }
                    }
                    if (cloudProfile.Stats.TryGetValue("total_xp_from_quests", out var cloudQuestXp))
                    {
                        var cqx = Convert.ToInt32(cloudQuestXp);
                        if (cqx > questProgress.TotalXPFromQuests)
                        {
                            questProgress.TotalXPFromQuests = cqx;
                            needsSave = true;
                        }
                    }
                }
            }

            // Merge skill tree data - server is source of truth for skill points
            if (cloudProfile.SkillPoints.HasValue)
            {
                if (cloudProfile.SkillPoints.Value != settings.SkillPoints)
                {
                    App.Logger?.Information("Skill tree sync: Server has {Cloud} skill points, local has {Local} — using server value",
                        cloudProfile.SkillPoints.Value, settings.SkillPoints);
                    settings.SkillPoints = cloudProfile.SkillPoints.Value;
                    needsSave = true;
                }
            }

            // Merge unlocked skills - union of both (never lose unlocked skills)
            if (cloudProfile.UnlockedSkills != null && cloudProfile.UnlockedSkills.Count > 0)
            {
                var localSkills = settings.UnlockedSkills ?? new List<string>();
                var skillsToAdd = cloudProfile.UnlockedSkills.Except(localSkills).ToList();

                if (skillsToAdd.Count > 0)
                {
                    App.Logger?.Information("Skill tree sync: Adding {Count} unlocked skills from cloud: {Skills}",
                        skillsToAdd.Count, string.Join(", ", skillsToAdd));

                    // Add all cloud skills that aren't in local
                    foreach (var skill in skillsToAdd)
                    {
                        if (!localSkills.Contains(skill))
                        {
                            localSkills.Add(skill);
                        }
                    }

                    settings.UnlockedSkills = localSkills;
                    needsSave = true;
                }
            }

            // Merge conditioning time - take HIGHER value to prevent loss
            if (cloudProfile.TotalConditioningMinutes.HasValue)
            {
                if (cloudProfile.TotalConditioningMinutes.Value > settings.TotalConditioningMinutes)
                {
                    App.Logger?.Information("Conditioning time sync: Cloud has more time ({Cloud:F1} min) > local ({Local:F1} min), syncing DOWN",
                        cloudProfile.TotalConditioningMinutes.Value, settings.TotalConditioningMinutes);
                    settings.TotalConditioningMinutes = cloudProfile.TotalConditioningMinutes.Value;
                    needsSave = true;
                }
                else if (settings.TotalConditioningMinutes > cloudProfile.TotalConditioningMinutes.Value)
                {
                    App.Logger?.Information("Conditioning time sync: Local has more time ({Local:F1} min) > cloud ({Cloud:F1} min), will sync UP",
                        settings.TotalConditioningMinutes, cloudProfile.TotalConditioningMinutes.Value);
                    // Will sync up on next SyncProfileAsync
                }
            }

            // Handle server-side quest reset flags
            if (cloudProfile.ResetWeeklyQuest == true)
            {
                App.Logger?.Information("Server requested weekly quest reset for this user");
                App.Quests?.ForceRegenerateWeeklyQuest();
                needsSave = true;
                // Trigger sync to clear the flag on server
                _pendingQuestResetClear = true;
            }
            if (cloudProfile.ResetDailyQuest == true)
            {
                App.Logger?.Information("Server requested daily quest reset for this user");
                App.Quests?.ForceRegenerateDailyQuest();
                needsSave = true;
                _pendingQuestResetClear = true;
            }

            // Save merged data
            if (needsSave)
            {
                App.Settings?.Save();
                achievements?.Save();
            }

            // Handle force_streak_override for legacy path (profile includes the flag)
            if (cloudProfile.ForceStreakOverride == true && cloudProfile.Stats != null)
            {
                App.Logger?.Information("Legacy sync: Force streak override - adopting server streak values");
                var legacyStreakStats = new V2StreakStats();
                if (cloudProfile.Stats.TryGetValue("daily_quest_streak", out var fStreak))
                    legacyStreakStats.DailyQuestStreak = Convert.ToInt32(fStreak);
                if (cloudProfile.Stats.TryGetValue("last_daily_quest_date", out var fDate))
                    legacyStreakStats.LastDailyQuestDate = fDate?.ToString();
                if (cloudProfile.Stats.TryGetValue("quest_completion_dates", out var fDates))
                {
                    try { legacyStreakStats.QuestCompletionDates = JsonConvert.DeserializeObject<List<string>>(fDates?.ToString() ?? "[]"); }
                    catch { }
                }
                if (cloudProfile.Stats.TryGetValue("total_daily_quests_completed", out var fDailyTotal))
                    legacyStreakStats.TotalDailyQuestsCompleted = Convert.ToInt32(fDailyTotal);
                if (cloudProfile.Stats.TryGetValue("total_weekly_quests_completed", out var fWeeklyTotal))
                    legacyStreakStats.TotalWeeklyQuestsCompleted = Convert.ToInt32(fWeeklyTotal);
                if (cloudProfile.Stats.TryGetValue("total_xp_from_quests", out var fXp))
                    legacyStreakStats.TotalXPFromQuests = Convert.ToInt32(fXp);

                ApplyForceStreakOverride(legacyStreakStats);
                needsSave = true;
                // Trigger sync to clear the flag on server
                _pendingQuestResetClear = true;
            }

            // If quest reset flags or force streak override were processed, sync back to clear them on server
            if (_pendingQuestResetClear)
            {
                _pendingQuestResetClear = false;
                _ = Task.Run(async () =>
                {
                    await Task.Delay(500);
                    await SyncProfileAsync();
                });
            }
        }

        /// <summary>
        /// Force-set local streak values from server (bypasses "take higher" logic).
        /// Used when admin has force-set streak values via /admin/set-streak.
        /// </summary>
        private void ApplyForceStreakOverride(V2StreakStats streakStats)
        {
            var settings = App.Settings?.Current;
            if (settings == null) return;

            App.Logger?.Information("Applying force streak override: streak={Streak}, date={Date}, daily={Daily}, weekly={Weekly}, xp={Xp}",
                streakStats.DailyQuestStreak, streakStats.LastDailyQuestDate,
                streakStats.TotalDailyQuestsCompleted, streakStats.TotalWeeklyQuestsCompleted,
                streakStats.TotalXPFromQuests);

            // Force-set streak (even if lower than local)
            settings.DailyQuestStreak = streakStats.DailyQuestStreak;

            // Force-set last daily quest date
            if (!string.IsNullOrEmpty(streakStats.LastDailyQuestDate) && DateTime.TryParse(streakStats.LastDailyQuestDate, out var parsedDate))
            {
                settings.LastDailyQuestDate = parsedDate.Date;
            }

            // Force-set completion dates
            var questProgress = App.Quests?.Progress;
            if (questProgress != null)
            {
                if (streakStats.QuestCompletionDates != null)
                {
                    questProgress.DailyQuestCompletionDates.Clear();
                    foreach (var ds in streakStats.QuestCompletionDates)
                    {
                        if (DateTime.TryParse(ds, out var d))
                            questProgress.DailyQuestCompletionDates.Add(d.Date);
                    }
                }

                // Force-set totals (even if lower)
                questProgress.TotalDailyQuestsCompleted = streakStats.TotalDailyQuestsCompleted;
                questProgress.TotalWeeklyQuestsCompleted = streakStats.TotalWeeklyQuestsCompleted;
                questProgress.TotalXPFromQuests = streakStats.TotalXPFromQuests;
            }

            App.Settings?.Save();
        }

        /// <summary>
        /// Force-reset all skills and refund points. Used when admin resets skills via /admin/reset-skills.
        /// </summary>
        private void ApplyForceSkillsReset(int? serverSkillPoints)
        {
            var settings = App.Settings?.Current;
            if (settings == null) return;

            var refundedPoints = serverSkillPoints ?? (settings.PlayerLevel * SkillTreeService.PointsPerLevel);

            App.Logger?.Information("Applying force skills reset: clearing {Count} skills, setting points to {Points}",
                settings.UnlockedSkills?.Count ?? 0, refundedPoints);

            settings.UnlockedSkills = new List<string>();
            settings.SkillPoints = refundedPoints;
            App.Settings?.Save();
        }

        /// <summary>
        /// Use oopsie insurance via server-side validation.
        /// Deducts 500 XP on server and marks as used for this season.
        /// </summary>
        /// <param name="fixDate">The date to fix, in YYYY-MM-DD format</param>
        /// <returns>Tuple of (success, error message, new XP value)</returns>
        public async Task<(bool success, string? error, int? newXp)> UseOopsieInsuranceAsync(string fixDate)
        {
            var unifiedId = App.Settings?.Current?.UnifiedId;
            if (string.IsNullOrEmpty(unifiedId))
            {
                return (false, "Oopsie Insurance requires an internet connection", null);
            }

            try
            {
                var requestData = new { unified_id = unifiedId, fix_date = fixDate };
                var request = new HttpRequestMessage(HttpMethod.Post, $"{ProxyBaseUrl}/v2/user/use-oopsie");
                request.Content = new StringContent(
                    JsonConvert.SerializeObject(requestData),
                    Encoding.UTF8,
                    "application/json"
                );

                var response = await _httpClient.SendAsync(request);
                var json = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    var errorResult = JsonConvert.DeserializeObject<OopsieErrorResponse>(json);
                    var errorMsg = errorResult?.Error ?? $"Server error: {response.StatusCode}";
                    App.Logger?.Warning("Oopsie insurance failed: {Error}", errorMsg);
                    return (false, errorMsg, null);
                }

                var result = JsonConvert.DeserializeObject<OopsieSuccessResponse>(json);
                App.Logger?.Information("Oopsie insurance used via server: new XP = {NewXP}", result?.NewXp);
                return (true, null, result?.NewXp);
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Oopsie insurance request failed");
                return (false, "Oopsie Insurance requires an internet connection", null);
            }
        }

        /// <summary>
        /// Change the user's display name via server-side validation.
        /// Name must be unique (case-insensitive). Case-only changes are allowed.
        /// </summary>
        public async Task<(bool success, string? error, string? newName)> ChangeDisplayNameAsync(string newName)
        {
            var unifiedId = App.Settings?.Current?.UnifiedId;
            if (string.IsNullOrEmpty(unifiedId))
            {
                return (false, "You must be logged in to change your name", null);
            }

            try
            {
                var requestData = new { unified_id = unifiedId, new_display_name = newName };
                var request = new HttpRequestMessage(HttpMethod.Post, $"{ProxyBaseUrl}/v2/user/change-display-name");
                request.Content = new StringContent(
                    JsonConvert.SerializeObject(requestData),
                    Encoding.UTF8,
                    "application/json"
                );

                var response = await _httpClient.SendAsync(request);
                var json = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    var errorResult = JsonConvert.DeserializeObject<ChangeDisplayNameErrorResponse>(json);
                    var errorMsg = errorResult?.Error ?? $"Server error: {response.StatusCode}";
                    App.Logger?.Warning("Change display name failed: {Error}", errorMsg);
                    return (false, errorMsg, null);
                }

                var result = JsonConvert.DeserializeObject<ChangeDisplayNameResponse>(json);
                App.Logger?.Information("Display name changed to: {NewName}", result?.NewDisplayName);
                return (true, null, result?.NewDisplayName);
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Change display name request failed");
                return (false, "Name change requires an internet connection", null);
            }
        }

        /// <summary>
        /// Delete the user's account and all server-side data (GDPR).
        /// Requires confirmation string "DELETE".
        /// </summary>
        public async Task<(bool success, string? error)> DeleteAccountAsync()
        {
            var unifiedId = App.Settings?.Current?.UnifiedId;
            if (string.IsNullOrEmpty(unifiedId))
            {
                return (false, "You must be logged in to delete your account");
            }

            try
            {
                var requestData = new { unified_id = unifiedId, confirmation = "DELETE" };
                var request = new HttpRequestMessage(HttpMethod.Post, $"{ProxyBaseUrl}/v2/user/delete-account");
                request.Content = new StringContent(
                    JsonConvert.SerializeObject(requestData),
                    Encoding.UTF8,
                    "application/json"
                );

                var response = await _httpClient.SendAsync(request);
                var json = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    var errorResult = JsonConvert.DeserializeObject<DeleteAccountErrorResponse>(json);
                    var errorMsg = errorResult?.Error ?? $"Server error: {response.StatusCode}";
                    App.Logger?.Warning("Delete account failed: {Error}", errorMsg);
                    return (false, errorMsg);
                }

                var result = JsonConvert.DeserializeObject<DeleteAccountResponse>(json);
                App.Logger?.Information("Account deleted: {UnifiedId} ({Name})", result?.DeletedUnifiedId, result?.DeletedDisplayName);
                return (true, null);
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Delete account request failed");
                return (false, "Account deletion requires an internet connection");
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

            [JsonProperty("skill_points")]
            public int? SkillPoints { get; set; }

            [JsonProperty("unlocked_skills")]
            public List<string>? UnlockedSkills { get; set; }

            [JsonProperty("total_conditioning_minutes")]
            public double? TotalConditioningMinutes { get; set; }

            [JsonProperty("reset_weekly_quest")]
            public bool? ResetWeeklyQuest { get; set; }

            [JsonProperty("reset_daily_quest")]
            public bool? ResetDailyQuest { get; set; }

            [JsonProperty("force_streak_override")]
            public bool? ForceStreakOverride { get; set; }
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

            [JsonProperty("share_profile_picture")]
            public bool ShareProfilePicture { get; set; }

            [JsonProperty("show_online_status")]
            public bool ShowOnlineStatus { get; set; } = true;

            [JsonProperty("discord_id")]
            public string? DiscordId { get; set; }

            [JsonProperty("avatar_url")]
            public string? AvatarUrl { get; set; }

            [JsonProperty("skill_points")]
            public int SkillPoints { get; set; }

            [JsonProperty("unlocked_skills")]
            public List<string>? UnlockedSkills { get; set; }

            [JsonProperty("total_conditioning_minutes")]
            public double TotalConditioningMinutes { get; set; }

            [JsonProperty("reset_weekly_quest")]
            public bool ResetWeeklyQuest { get; set; }

            [JsonProperty("reset_daily_quest")]
            public bool ResetDailyQuest { get; set; }

            [JsonProperty("force_streak_override")]
            public bool ForceStreakOverride { get; set; }
        }

        private class V2SyncResponse
        {
            [JsonProperty("success")]
            public bool Success { get; set; }

            [JsonProperty("reset_weekly_quest")]
            public bool? ResetWeeklyQuest { get; set; }

            [JsonProperty("reset_daily_quest")]
            public bool? ResetDailyQuest { get; set; }

            [JsonProperty("force_streak_override")]
            public bool? ForceStreakOverride { get; set; }

            [JsonProperty("streak_stats")]
            public V2StreakStats? StreakStats { get; set; }

            [JsonProperty("force_skills_reset")]
            public bool? ForceSkillsReset { get; set; }

            [JsonProperty("skill_points")]
            public int? SkillPoints { get; set; }

            [JsonProperty("oopsie_used_season")]
            public string? OopsieUsedSeason { get; set; }

            [JsonProperty("is_season0_og")]
            public bool? IsSeason0Og { get; set; }

            [JsonProperty("patreon_is_whitelisted")]
            public bool? PatreonIsWhitelisted { get; set; }

            [JsonProperty("level_reset")]
            public bool? LevelReset { get; set; }

            [JsonProperty("user")]
            public V2SyncUser? User { get; set; }
        }

        private class V2SyncUser
        {
            [JsonProperty("display_name")]
            public string? DisplayName { get; set; }

            [JsonProperty("level")]
            public int Level { get; set; }

            [JsonProperty("xp")]
            public int Xp { get; set; }

            [JsonProperty("highest_level_ever")]
            public int? HighestLevelEver { get; set; }
        }

        private class OopsieSuccessResponse
        {
            [JsonProperty("success")]
            public bool Success { get; set; }

            [JsonProperty("new_xp")]
            public int NewXp { get; set; }

            [JsonProperty("oopsie_used_season")]
            public string? OopsieUsedSeason { get; set; }
        }

        private class OopsieErrorResponse
        {
            [JsonProperty("error")]
            public string? Error { get; set; }
        }

        private class ChangeDisplayNameResponse
        {
            [JsonProperty("success")]
            public bool Success { get; set; }

            [JsonProperty("new_display_name")]
            public string? NewDisplayName { get; set; }
        }

        private class ChangeDisplayNameErrorResponse
        {
            [JsonProperty("error")]
            public string? Error { get; set; }
        }

        private class DeleteAccountResponse
        {
            [JsonProperty("success")]
            public bool Success { get; set; }

            [JsonProperty("deleted_unified_id")]
            public string? DeletedUnifiedId { get; set; }

            [JsonProperty("deleted_display_name")]
            public string? DeletedDisplayName { get; set; }
        }

        private class DeleteAccountErrorResponse
        {
            [JsonProperty("error")]
            public string? Error { get; set; }
        }

        private class V2StreakStats
        {
            [JsonProperty("daily_quest_streak")]
            public int DailyQuestStreak { get; set; }

            [JsonProperty("last_daily_quest_date")]
            public string? LastDailyQuestDate { get; set; }

            [JsonProperty("quest_completion_dates")]
            public List<string>? QuestCompletionDates { get; set; }

            [JsonProperty("total_daily_quests_completed")]
            public int TotalDailyQuestsCompleted { get; set; }

            [JsonProperty("total_weekly_quests_completed")]
            public int TotalWeeklyQuestsCompleted { get; set; }

            [JsonProperty("total_xp_from_quests")]
            public int TotalXPFromQuests { get; set; }
        }

        #endregion
    }
}
