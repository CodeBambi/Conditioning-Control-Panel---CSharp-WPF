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

        // Auto-refresh every 5 minutes
        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(5) };
        _refreshTimer.Tick += async (s, e) => await RefreshAsync();
        _refreshTimer.Start();

        App.Logger?.Information("LeaderboardService initialized with 5-minute auto-refresh");
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

            var response = await _httpClient.GetAsync($"{ProxyBaseUrl}/leaderboard?sort_by={sortBy}&limit=1000");

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

    /// <summary>
    /// Display string for achievements (X / Y format)
    /// Uses the total achievement count from the Achievement model
    /// </summary>
    public string AchievementsDisplay => $"{AchievementsCount} / {Models.Achievement.All.Count}";
}
