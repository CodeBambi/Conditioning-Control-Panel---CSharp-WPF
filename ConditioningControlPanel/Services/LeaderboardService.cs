using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows.Threading;
using Newtonsoft.Json;

namespace ConditioningControlPanel.Services;

/// <summary>
/// Service for fetching and caching leaderboard data from the server
/// </summary>
public class LeaderboardService : IDisposable
{
    private const string ProxyBaseUrl = "https://codebambi-proxy.vercel.app";
    private readonly HttpClient _httpClient;
    private readonly DispatcherTimer _refreshTimer;
    private bool _disposed;

    /// <summary>Current leaderboard entries</summary>
    public List<LeaderboardEntry> Entries { get; private set; } = new();

    /// <summary>Total number of users on the leaderboard</summary>
    public int TotalUsers { get; private set; }

    /// <summary>Number of users currently online (active in last minute)</summary>
    public int OnlineUsers { get; private set; }

    /// <summary>Current sort field</summary>
    public string CurrentSortBy { get; private set; } = "level";

    /// <summary>Last successful refresh time</summary>
    public DateTime? LastRefreshTime { get; private set; }

    /// <summary>Last refresh error message (if any)</summary>
    public string? LastRefreshError { get; private set; }

    /// <summary>Whether a refresh is currently in progress</summary>
    public bool IsRefreshing { get; private set; }

    /// <summary>Fired when leaderboard data is updated</summary>
    public event EventHandler? LeaderboardUpdated;

    public LeaderboardService()
    {
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

        // Auto-refresh every 10 minutes (server caches for 2 min, so this is plenty)
        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(10) };
        _refreshTimer.Tick += async (s, e) => await RefreshAsync();
        _refreshTimer.Start();

        App.Logger?.Information("LeaderboardService initialized with 10-minute auto-refresh");
    }

    /// <summary>
    /// Refresh leaderboard data from the server
    /// </summary>
    /// <param name="sortBy">Field to sort by (xp, level, total_bubbles_popped, total_flashes, total_video_minutes, total_lock_cards_completed)</param>
    /// <returns>True if successful</returns>
    public async Task<bool> RefreshAsync(string? sortBy = null)
    {
        // Skip if offline mode is enabled
        if (App.Settings?.Current?.OfflineMode == true)
        {
            App.Logger?.Debug("Offline mode enabled, skipping leaderboard refresh");
            return false;
        }

        if (IsRefreshing) return false;

        sortBy ??= CurrentSortBy;
        IsRefreshing = true;

        try
        {
            App.Logger?.Debug("Fetching leaderboard with sort_by={SortBy}", sortBy);

            // Use V2 leaderboard (monthly seasons system)
            var season = DateTime.UtcNow.ToString("yyyy-MM");
            var response = await _httpClient.GetAsync($"{ProxyBaseUrl}/v3/leaderboard?season={season}&limit=10000");

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                App.Logger?.Warning("Leaderboard fetch failed: {Status} - {Error}", response.StatusCode, errorBody);
                LastRefreshError = $"Server returned {response.StatusCode}";
                return false;
            }

            var json = await response.Content.ReadAsStringAsync();
            var result = JsonConvert.DeserializeObject<LeaderboardResponse>(json);

            if (result?.Entries != null)
            {
                Entries = result.Entries;
                TotalUsers = result.TotalUsers;
                OnlineUsers = result.OnlineUsers;
                CurrentSortBy = sortBy;
                LastRefreshTime = DateTime.Now;
                LastRefreshError = null;

                App.Logger?.Information("Leaderboard refreshed: {Count} entries, {Total} total users, {Online} online, sorted by {SortBy}",
                    Entries.Count, TotalUsers, OnlineUsers, sortBy);

                LeaderboardUpdated?.Invoke(this, EventArgs.Empty);
                return true;
            }

            LastRefreshError = "Invalid response from server";
            return false;
        }
        catch (TaskCanceledException)
        {
            App.Logger?.Warning("Leaderboard fetch timed out");
            LastRefreshError = "Request timed out";
            return false;
        }
        catch (Exception ex)
        {
            App.Logger?.Error(ex, "Failed to fetch leaderboard");
            LastRefreshError = ex.Message;
            return false;
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    /// <summary>
    /// Look up a specific user's fresh profile data by display name.
    /// Returns fresh online status and avatar URL.
    /// </summary>
    public async Task<UserLookupResult?> LookupUserAsync(string displayName)
    {
        try
        {
            var url = $"{ProxyBaseUrl}/user/lookup?display_name={Uri.EscapeDataString(displayName)}";
            var response = await _httpClient.GetAsync(url);

            if (!response.IsSuccessStatusCode)
            {
                App.Logger?.Warning("User lookup failed: {Status} for {Name}", response.StatusCode, displayName);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync();
            var result = JsonConvert.DeserializeObject<UserLookupResult>(json);

            App.Logger?.Debug("User lookup successful: {Name}, Online={Online}, Avatar={HasAvatar}",
                displayName, result?.IsOnline, !string.IsNullOrEmpty(result?.AvatarUrl));

            return result;
        }
        catch (Exception ex)
        {
            App.Logger?.Warning(ex, "User lookup failed for {Name}", displayName);
            return null;
        }
    }

    /// <summary>
    /// Get the current player's rank percentile.
    /// Returns 0 if not found or not enough data.
    /// </summary>
    public int GetPlayerPercentile()
    {
        try
        {
            if (Entries.Count == 0 || TotalUsers == 0)
            {
                App.Logger?.Debug("GetPlayerPercentile: No entries ({Count}) or users ({Total})", Entries.Count, TotalUsers);
                return 0;
            }

            // Try to find the player by unified ID, Discord ID, then display name
            var unifiedId = App.UnifiedUserId;
            var discordId = App.Discord?.UserId;
            var displayName = App.UserDisplayName;

            // Find player in leaderboard
            int position = -1;
            for (int i = 0; i < Entries.Count; i++)
            {
                var entry = Entries[i];
                // Match by Unified ID (primary)
                if (!string.IsNullOrEmpty(unifiedId) && entry.UnifiedId == unifiedId)
                {
                    position = i + 1;
                    App.Logger?.Debug("GetPlayerPercentile: Found player by Unified ID at position {Position} out of {Total}", position, TotalUsers);
                    break;
                }
                // Match by Discord ID
                if (!string.IsNullOrEmpty(discordId) && entry.DiscordId == discordId)
                {
                    position = i + 1;
                    App.Logger?.Debug("GetPlayerPercentile: Found player by Discord ID at position {Position} out of {Total}", position, TotalUsers);
                    break;
                }
                // Fallback: match by display name
                if (!string.IsNullOrEmpty(displayName) && string.Equals(entry.DisplayName, displayName, StringComparison.OrdinalIgnoreCase))
                {
                    position = i + 1;
                    App.Logger?.Debug("GetPlayerPercentile: Found player by display name at position {Position} out of {Total}", position, TotalUsers);
                    break;
                }
            }

            if (position <= 0)
            {
                App.Logger?.Debug("GetPlayerPercentile: Player not found in leaderboard (UnifiedId={UnifiedId}, DiscordId={DiscordId}, DisplayName={DisplayName})", unifiedId, discordId, displayName);
                return 0;
            }

            // Calculate percentile (higher is better, so invert)
            // Top 10% means better than 90% of players
            var percentile = (int)Math.Ceiling((double)position / TotalUsers * 100);
            var clampedPercentile = Math.Min(99, Math.Max(1, percentile));

            App.Logger?.Debug("GetPlayerPercentile: Player rank {Position}/{Total} = Top {Percentile}%",
                position, TotalUsers, clampedPercentile);

            return clampedPercentile;
        }
        catch (Exception ex)
        {
            App.Logger?.Warning(ex, "Failed to calculate player percentile");
            return 0;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _refreshTimer.Stop();
        _httpClient.Dispose();
        App.Logger?.Debug("LeaderboardService disposed");
    }

    #region DTOs

    private class LeaderboardResponse
    {
        [JsonProperty("entries")]
        public List<LeaderboardEntry>? Entries { get; set; }

        [JsonProperty("total_users")]
        public int TotalUsers { get; set; }

        [JsonProperty("online_users")]
        public int OnlineUsers { get; set; }

        [JsonProperty("sort_by")]
        public string? SortBy { get; set; }

        [JsonProperty("fetched_at")]
        public string? FetchedAt { get; set; }
    }

    #endregion
}

/// <summary>
/// Represents a single entry on the leaderboard
/// </summary>
public class LeaderboardEntry
{
    [JsonProperty("rank")]
    public int Rank { get; set; }

    [JsonProperty("unified_id")]
    public string? UnifiedId { get; set; }

    [JsonProperty("display_name")]
    public string DisplayName { get; set; } = "";

    [JsonProperty("level")]
    public int Level { get; set; }

    [JsonProperty("xp")]
    public int Xp { get; set; }

    /// <summary>
    /// Formatted XP display (e.g., "100.3k" or "1.2M")
    /// </summary>
    public string XpDisplay
    {
        get
        {
            if (Xp >= 1_000_000)
                return $"{Xp / 1_000_000.0:F1}M";
            if (Xp >= 1_000)
                return $"{Xp / 1_000.0:F1}k";
            return Xp.ToString();
        }
    }

    [JsonProperty("total_bubbles_popped")]
    public int BubblesPopped { get; set; }

    /// <summary>
    /// Formatted bubbles display (e.g., "100.3k" or "1.2M")
    /// </summary>
    public string BubblesPoppedDisplay => FormatLargeNumber(BubblesPopped);

    [JsonProperty("total_flashes")]
    public int GifsSpawned { get; set; }

    /// <summary>
    /// Formatted GIFs display (e.g., "100.3k" or "1.2M")
    /// </summary>
    public string GifsSpawnedDisplay => FormatLargeNumber(GifsSpawned);

    private static string FormatLargeNumber(int value)
    {
        if (value >= 1_000_000)
            return $"{value / 1_000_000.0:F1}M";
        if (value >= 1_000)
            return $"{value / 1_000.0:F1}k";
        return value.ToString();
    }

    [JsonProperty("total_video_minutes")]
    public double VideoMinutes { get; set; }

    [JsonProperty("total_lock_cards_completed")]
    public int LockCardsCompleted { get; set; }

    [JsonProperty("achievements_count")]
    public int AchievementsCount { get; set; }

    [JsonProperty("has_trophy_case")]
    public bool HasTrophyCase { get; set; }

    [JsonProperty("longest_session_minutes")]
    public double LongestSessionMinutes { get; set; }

    /// <summary>
    /// Formatted longest session display — blank if user doesn't have trophy_case skill
    /// </summary>
    public string LongestSessionDisplay => HasTrophyCase ? $"{LongestSessionMinutes:F1}" : "";

    [JsonProperty("highest_streak")]
    public int HighestStreak { get; set; }

    /// <summary>
    /// Formatted highest streak display — blank if user doesn't have trophy_case skill
    /// </summary>
    public string HighestStreakDisplay => HasTrophyCase ? HighestStreak.ToString() : "";

    [JsonProperty("is_online", NullValueHandling = NullValueHandling.Ignore)]
    public bool IsOnline { get; set; }

    [JsonProperty("is_patreon", NullValueHandling = NullValueHandling.Ignore)]
    public bool IsPatreon { get; set; }

    [JsonProperty("patreon_tier")]
    public int PatreonTier { get; set; }

    [JsonProperty("discord_id")]
    public string? DiscordId { get; set; }

    /// <summary>
    /// Whether this user has a Discord ID available for DM
    /// </summary>
    public bool HasDiscord => !string.IsNullOrEmpty(DiscordId);

    [JsonProperty("is_season0_og")]
    public bool IsSeason0Og { get; set; }

    /// <summary>
    /// Display name with OG star prefix if applicable
    /// </summary>
    public string DisplayNameWithFlair => DisplayName;

    /// <summary>
    /// Display string for achievements (X / Y format)
    /// Uses the total achievement count from the Achievement model
    /// </summary>
    public string AchievementsDisplay => $"{AchievementsCount} / {Models.Achievement.All.Count}";
}

/// <summary>
/// Result of looking up a specific user's profile
/// </summary>
public class UserLookupResult
{
    [JsonProperty("display_name")]
    public string? DisplayName { get; set; }

    [JsonProperty("level")]
    public int Level { get; set; }

    [JsonProperty("xp")]
    public int Xp { get; set; }

    [JsonProperty("total_bubbles_popped")]
    public int BubblesPopped { get; set; }

    [JsonProperty("total_flashes")]
    public int GifsSpawned { get; set; }

    [JsonProperty("total_video_minutes")]
    public double VideoMinutes { get; set; }

    [JsonProperty("total_lock_cards_completed")]
    public int LockCardsCompleted { get; set; }

    [JsonProperty("achievements_count")]
    public int AchievementsCount { get; set; }

    [JsonProperty("achievements")]
    public List<string>? Achievements { get; set; }

    [JsonProperty("is_online")]
    public bool IsOnline { get; set; }

    [JsonProperty("is_patreon")]
    public bool IsPatreon { get; set; }

    [JsonProperty("patreon_tier")]
    public int PatreonTier { get; set; }

    [JsonProperty("discord_id")]
    public string? DiscordId { get; set; }

    [JsonProperty("avatar_url")]
    public string? AvatarUrl { get; set; }

    [JsonProperty("last_seen")]
    public string? LastSeen { get; set; }

    [JsonProperty("is_season0_og")]
    public bool IsSeason0Og { get; set; }

    /// <summary>
    /// Display name with OG star prefix if applicable
    /// </summary>
    public string DisplayNameWithFlair => DisplayName ?? "";
}
