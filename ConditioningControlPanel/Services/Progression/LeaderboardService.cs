using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Threading;
using Newtonsoft.Json;

namespace ConditioningControlPanel.Services;

/// <summary>
/// Service for fetching and caching leaderboard data from the server
/// </summary>
public class LeaderboardService : IDisposable
{
    /// <summary>
    /// How many rows a refresh fetches. The v3 endpoint takes no offset/cursor, so this is the
    /// whole board the client ever sees — anyone ranked below it can only be located through the
    /// server-provided <see cref="YourRank"/>, never by scanning <see cref="Entries"/> (#693).
    /// </summary>
    public const int FetchLimit = LeaderboardClient.FetchLimit;
    private readonly LeaderboardClient _client = new();
    private readonly DispatcherTimer _refreshTimer;
    private bool _disposed;

    /// <summary>Current leaderboard entries</summary>
    public List<LeaderboardEntry> Entries { get; private set; } = new();

    /// <summary>Total number of users on the leaderboard</summary>
    public int TotalUsers { get; private set; }

    /// <summary>Number of users currently online (active in last minute)</summary>
    public int OnlineUsers { get; private set; }

    /// <summary>Server-provided rank for the current player (1-indexed), or null if not available</summary>
    public int? YourRank { get; private set; }

    /// <summary>Total number of season leaderboard members (for percentile calculation)</summary>
    public int? YourTotal { get; private set; }

    /// <summary>Current sort field</summary>
    public string CurrentSortBy { get; private set; } = "level";

    /// <summary>Current leaderboard mode (monthly or all-time)</summary>
    public string CurrentMode { get; private set; } = "monthly";

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
        // Auto-refresh every 30 minutes (server caches leaderboard in memory for 30s)
        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(30) };
        _refreshTimer.Tick += async (s, e) => await RefreshAsync();
        _refreshTimer.Start();

        App.Logger?.Debug("LeaderboardService initialized with 30-minute auto-refresh");
    }

    /// <summary>
    /// Refresh leaderboard data from the server
    /// </summary>
    /// <param name="sortBy">Field to sort by (xp, level, total_bubbles_popped, total_flashes, total_video_minutes, total_lock_cards_completed)</param>
    /// <param name="mode">Leaderboard mode: "monthly" (default) or "all-time"</param>
    /// <returns>True if successful</returns>
    public async Task<bool> RefreshAsync(string? sortBy = null, string? mode = null)
    {
        // Skip if offline mode is enabled
        if (App.Settings?.Current?.OfflineMode == true)
        {
            App.Logger?.Debug("Offline mode enabled, skipping leaderboard refresh");
            return false;
        }

        if (IsRefreshing) return false;

        sortBy ??= CurrentSortBy;
        mode ??= CurrentMode;
        IsRefreshing = true;

        try
        {
            App.Logger?.Debug("Fetching leaderboard with sort_by={SortBy}, mode={Mode}", sortBy, mode);

            var (result, error) = await _client.FetchAsync<LeaderboardEntry>(mode, App.UnifiedUserId, DateTime.UtcNow);
            if (error != null)
            {
                LastRefreshError = error;
                return false;
            }

            if (result?.Entries != null)
            {
                Entries = result.Entries;
                TotalUsers = result.TotalUsers;
                OnlineUsers = result.OnlineUsers;
                YourRank = result.YourRank;
                YourTotal = result.YourTotal;

                // Season Recap (decision #1): client-sampled season peak rank. Only the
                // monthly board maps to a season; ignore the all-time board.
                if (mode != "all-time" && YourRank.HasValue)
                    SeasonRecapService.SampleRank(YourRank.Value, YourTotal ?? TotalUsers);

                CurrentSortBy = sortBy;
                CurrentMode = mode;
                LastRefreshTime = DateTime.Now;
                LastRefreshError = null;

                App.Logger?.Debug("Leaderboard refreshed: {Count} entries, {Total} total users, {Online} online, sorted by {SortBy}",
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
    public Task<UserLookupResult?> LookupUserAsync(string displayName) => _client.LookupUserAsync(displayName);

    /// <summary>
    /// Get the current player's rank percentile.
    /// Returns 0 if not found or not enough data.
    /// </summary>
    public int GetPlayerPercentile()
    {
        try
        {
            // Prefer server-provided rank (works for any rank, not just top 200)
            if (YourRank.HasValue && YourTotal.HasValue && YourTotal.Value > 0)
            {
                var percentile = (int)Math.Ceiling((double)YourRank.Value / YourTotal.Value * 100);
                var clampedPercentile = Math.Min(99, Math.Max(1, percentile));

                App.Logger?.Debug("GetPlayerPercentile: Server rank {Position}/{Total} = Top {Percentile}%",
                    YourRank.Value, YourTotal.Value, clampedPercentile);

                return clampedPercentile;
            }

            // Fallback: scan local entries (only works if player is within the fetched set)
            if (Entries.Count == 0 || TotalUsers == 0)
            {
                App.Logger?.Debug("GetPlayerPercentile: No entries ({Count}) or users ({Total})", Entries.Count, TotalUsers);
                return 0;
            }

            var unifiedId = App.UnifiedUserId;
            var discordId = App.Discord?.UserId;
            var displayName = App.UserDisplayName;

            int position = -1;
            for (int i = 0; i < Entries.Count; i++)
            {
                var entry = Entries[i];
                if (!string.IsNullOrEmpty(unifiedId) && entry.UnifiedId == unifiedId)
                {
                    position = i + 1;
                    break;
                }
                if (!string.IsNullOrEmpty(discordId) && entry.DiscordId == discordId)
                {
                    position = i + 1;
                    break;
                }
                if (!string.IsNullOrEmpty(displayName) && string.Equals(entry.DisplayName, displayName, StringComparison.OrdinalIgnoreCase))
                {
                    position = i + 1;
                    break;
                }
            }

            if (position <= 0)
            {
                App.Logger?.Debug("GetPlayerPercentile: Player not found in leaderboard");
                return 0;
            }

            var fallbackPercentile = (int)Math.Ceiling((double)position / TotalUsers * 100);
            var clampedFallback = Math.Min(99, Math.Max(1, fallbackPercentile));

            App.Logger?.Debug("GetPlayerPercentile: Fallback scan rank {Position}/{Total} = Top {Percentile}%",
                position, TotalUsers, clampedFallback);

            return clampedFallback;
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
        App.Logger?.Debug("LeaderboardService disposed");
    }
}

/// <summary>Leaderboard row: the Core wire model plus the WPF avatar brush.</summary>
public class LeaderboardEntry : LeaderboardEntryData
{
    private Brush? _avatarBrush;

    /// <summary>
    /// Deterministic two-stop gradient for the initials avatar. The leaderboard
    /// payload carries no avatar URL (only /user/lookup does, and 200 lookups per
    /// refresh is not acceptable), so the circle is generated from a stable hash
    /// of the display name: the same subject always gets the same colours.
    /// </summary>
    [JsonIgnore]
    public Brush AvatarBrush => _avatarBrush ??= BuildAvatarBrush(DisplayName);

    /// <summary>
    /// Frozen two-stop gradient derived from a stable hash of the name. Hues are
    /// clamped to 200-345 deg (blue - indigo - violet - magenta - pink) so the
    /// generated avatars stay inside the app's palette instead of turning the
    /// roster into a rainbow.
    /// </summary>
    public static Brush BuildAvatarBrush(string? name)
    {
        var hash = StableHash(name ?? "");
        var hue = 200.0 + (hash % 146);              // 200 .. 345
        var hue2 = hue - 14.0; if (hue2 < 195.0) hue2 += 150.0;

        var top = FromHsl(hue, 0.70, 0.70);
        var bottom = FromHsl(hue2, 0.52, 0.40);

        var brush = new LinearGradientBrush
        {
            StartPoint = new System.Windows.Point(0.15, 0),
            EndPoint = new System.Windows.Point(0.85, 1)
        };
        brush.GradientStops.Add(new GradientStop(top, 0));
        brush.GradientStops.Add(new GradientStop(bottom, 1));
        brush.Freeze();
        return brush;
    }

    /// <summary>FNV-1a over the lower-cased name — stable across runs and machines.</summary>
    private static uint StableHash(string s)
    {
        unchecked
        {
            uint h = 2166136261;
            foreach (var c in s)
            {
                h ^= char.ToLowerInvariant(c);
                h *= 16777619;
            }
            return h;
        }
    }

    private static Color FromHsl(double h, double s, double l)
    {
        h = ((h % 360) + 360) % 360;
        var c = (1 - Math.Abs(2 * l - 1)) * s;
        var x = c * (1 - Math.Abs((h / 60.0) % 2 - 1));
        var m = l - c / 2;

        double r, g, b;
        if (h < 60) { r = c; g = x; b = 0; }
        else if (h < 120) { r = x; g = c; b = 0; }
        else if (h < 180) { r = 0; g = c; b = x; }
        else if (h < 240) { r = 0; g = x; b = c; }
        else if (h < 300) { r = x; g = 0; b = c; }
        else { r = c; g = 0; b = x; }

        return Color.FromRgb(
            (byte)Math.Round(Math.Clamp((r + m) * 255, 0, 255)),
            (byte)Math.Round(Math.Clamp((g + m) * 255, 0, 255)),
            (byte)Math.Round(Math.Clamp((b + m) * 255, 0, 255)));
    }
}
