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
    private const string ProxyBaseUrl = "https://codebambi-proxy.vercel.app";

    /// <summary>
    /// How many rows a refresh fetches. The v3 endpoint takes no offset/cursor, so this is the
    /// whole board the client ever sees — anyone ranked below it can only be located through the
    /// server-provided <see cref="YourRank"/>, never by scanning <see cref="Entries"/> (#693).
    /// </summary>
    public const int FetchLimit = 200;
    private readonly HttpClient _httpClient;
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
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        _httpClient.DefaultRequestHeaders.Add("X-Client-Version", UpdateService.AppVersion);
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd($"ConditioningControlPanel/{UpdateService.AppVersion}");

        // Auto-refresh every 30 minutes (server caches leaderboard in memory for 30s)
        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(30) };
        _refreshTimer.Tick += async (s, e) => await RefreshAsync();
        _refreshTimer.Start();

        App.Logger?.Information("LeaderboardService initialized with 30-minute auto-refresh");
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

            // Use V3 leaderboard — "all-time" mode uses a permanent sorted set
            var season = mode == "all-time" ? "all-time" : DateTime.UtcNow.ToString("yyyy-MM");
            var unifiedId = App.UnifiedUserId;
            var url = $"{ProxyBaseUrl}/v3/leaderboard?season={season}&limit={FetchLimit}";
            if (!string.IsNullOrEmpty(unifiedId))
                url += $"&unified_id={Uri.EscapeDataString(unifiedId)}";
            var response = await _httpClient.GetAsync(url);

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

        [JsonProperty("your_rank")]
        public int? YourRank { get; set; }

        [JsonProperty("your_total")]
        public int? YourTotal { get; set; }
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

    [JsonProperty("seasons_completed")]
    public int SeasonsCompleted { get; set; }

    [JsonProperty("total_xp_earned")]
    public long TotalXpEarned { get; set; }

    /// <summary>
    /// Formatted total XP earned display (e.g., "100.3k" or "1.2M")
    /// </summary>
    public string TotalXpEarnedDisplay => FormatLargeNumber((int)Math.Min(TotalXpEarned, int.MaxValue));

    [JsonProperty("highest_level_ever")]
    public int HighestLevelEver { get; set; }

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
    /// True when this entry belongs to the local signed-in user (matched by unified id).
    /// Used to highlight and "jump to" the user's own row on the leaderboard.
    /// </summary>
    public bool IsCurrentUser => !string.IsNullOrEmpty(UnifiedId)
        && string.Equals(UnifiedId, App.UnifiedUserId, StringComparison.Ordinal);

    /// <summary>
    /// Display name with OG star prefix if applicable
    /// </summary>
    public string DisplayNameWithFlair => DisplayName;

    /// <summary>
    /// Display string for achievements (X / Y format)
    /// Uses the total earnable achievement count from the Achievement model
    /// (parked/IsHidden achievements are excluded from the denominator).
    /// </summary>
    public string AchievementsDisplay => $"{AchievementsCount} / {AchievementsTotal}";

    // ------------------------------------------------------------------
    // Roster-UI display helpers (leaderboard redesign).
    //
    // Everything below is computed on the client and is deliberately
    // [JsonIgnore]'d so it can never leak back into the wire contract. The
    // fetch path in LeaderboardService is untouched by any of it.
    // ------------------------------------------------------------------

    /// <summary>
    /// Denominator for the achievements column / progress bar. Mirrors
    /// <see cref="AchievementsDisplay"/> so the bar and the text can't disagree.
    /// </summary>
    [JsonIgnore]
    public int AchievementsTotal => System.Linq.Enumerable.Count(Models.Achievement.All.Values, a => !a.IsHidden);

    /// <summary>
    /// Discriminator for the roster's heterogeneous ItemsSource: real rows are
    /// not tier bands. Lets a single ItemContainerStyle tell the two apart
    /// without the DataTrigger throwing a binding error on the other type.
    /// </summary>
    [JsonIgnore]
    public bool IsBand => false;

    /// <summary>
    /// Set by the tab when the All-Time board is showing. All-Time re-points the
    /// Level and XP columns at the cumulative fields, because the seasonal
    /// <see cref="Level"/>/<see cref="Xp"/> are meaningless after a season reset.
    /// </summary>
    [JsonIgnore]
    public bool IsAllTimeView { get; set; }

    /// <summary>Value shown in the Level column for the active board.</summary>
    [JsonIgnore]
    public int LevelColumnValue => IsAllTimeView ? HighestLevelEver : Level;

    /// <summary>
    /// Caption for <see cref="LevelColumnValue"/>. On the All-Time board the number is
    /// <see cref="HighestLevelEver"/>, not a current standing, so calling it "Level" next to
    /// an XP-ordered rank makes the board look mis-sorted. Display-only; mirrors the legend
    /// header swap in LeaderboardTabView.ApplyModeLabels().
    /// </summary>
    [JsonIgnore]
    public string LevelLabel => Localization.Loc.Get(IsAllTimeView ? "lb_col_peak" : "label_level");

    /// <summary>Value shown in the XP column for the active board.</summary>
    [JsonIgnore]
    public string XpColumnDisplay => IsAllTimeView ? TotalXpEarnedDisplay : XpDisplay;

    /// <summary>Raw XP for the active board (used for the "gap to next" line).</summary>
    [JsonIgnore]
    public long XpColumnValue => IsAllTimeView ? TotalXpEarned : Xp;

    /// <summary>
    /// Patron tier to badge with. The server ships tier 0 for some legacy
    /// patrons, and the old badge column treated that as tier 1 — keep that.
    /// </summary>
    [JsonIgnore]
    public int EffectivePatreonTier => PatreonTier > 0 ? PatreonTier : (IsPatreon ? 1 : 0);

    [JsonIgnore]
    public bool ShowPatreonChip => EffectivePatreonTier > 0;

    /// <summary>Roman numeral suffix on the patron chip, so the chip text stays localizable.</summary>
    [JsonIgnore]
    public string PatreonTierRoman => EffectivePatreonTier switch { 3 => "III", 2 => "II", 1 => "I", _ => "" };

    /// <summary>Seasons-completed chip — All-Time board only (replaces the old Seasons column).</summary>
    [JsonIgnore]
    public bool ShowSeasonsChip => IsAllTimeView && SeasonsCompleted > 0;

    [JsonIgnore]
    public string SeasonsChipText => SeasonsCompleted.ToString();

    /// <summary>True when the row has nothing to put in the chip strip.</summary>
    [JsonIgnore]
    public bool HasNoBadges => !IsSeason0Og && !ShowPatreonChip && !HasDiscord && !ShowSeasonsChip;

    /// <summary>1-2 uppercase initials for the generated avatar.</summary>
    [JsonIgnore]
    public string Initials => BuildInitials(DisplayName);

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
    /// Rank held at the previous snapshot, or null when the snapshot service has
    /// nothing for this subject (a normal case — it stores the top 500 only).
    /// Materialised eagerly by the tab BEFORE the snapshot is re-recorded; it is
    /// never looked up lazily from a binding, because a virtualized row realises
    /// after the re-record and would then always read a zero delta.
    /// </summary>
    [JsonIgnore]
    public int? PreviousRank { get; private set; }

    /// <summary>"up" | "down" | "same" | "new" | "none". Drives the arrow's colour.</summary>
    [JsonIgnore]
    public string DeltaState { get; private set; } = "none";

    /// <summary>Pre-rendered arrow text ("▲2", "▼1", "–", or the NEW chip label).</summary>
    [JsonIgnore]
    public string DeltaText { get; private set; } = "–";

    /// <summary>
    /// Bake the rank delta into the row. <paramref name="known"/> is false when we
    /// had no unified id to look up at all, which renders as a muted dash rather
    /// than falsely claiming the subject is new to the board.
    /// </summary>
    public void ApplyRankDelta(int? previousRank, bool known)
    {
        PreviousRank = previousRank;

        if (!known)
        {
            DeltaState = "none";
            DeltaText = "–";
            return;
        }

        if (previousRank is not > 0)
        {
            DeltaState = "new";
            DeltaText = SafeLoc("lb_delta_new", "NEW");
            return;
        }

        var moved = previousRank.Value - Rank;
        if (moved > 0) { DeltaState = "up"; DeltaText = "▲" + moved; }
        else if (moved < 0) { DeltaState = "down"; DeltaText = "▼" + (-moved); }
        else { DeltaState = "same"; DeltaText = "–"; }
    }

    private static string SafeLoc(string key, string fallback)
    {
        try
        {
            var s = ConditioningControlPanel.Localization.Loc.Get(key);
            return string.IsNullOrEmpty(s) || s == key ? fallback : s;
        }
        catch { return fallback; }
    }

    /// <summary>1-2 uppercase initials from a display name. "?" when there's nothing usable.</summary>
    public static string BuildInitials(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "?";

        var parts = name.Split(new[] { ' ', '_', '-', '.', '|' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2)
        {
            var a = FirstLetterOrDigit(parts[0]);
            var b = FirstLetterOrDigit(parts[1]);
            if (a != '\0' && b != '\0') return string.Concat(char.ToUpperInvariant(a), char.ToUpperInvariant(b));
        }

        var trimmed = name.Trim();
        var chars = new List<char>(2);
        foreach (var c in trimmed)
        {
            if (!char.IsLetterOrDigit(c)) continue;
            chars.Add(char.ToUpperInvariant(c));
            if (chars.Count == 2) break;
        }
        return chars.Count > 0 ? new string(chars.ToArray()) : "?";
    }

    private static char FirstLetterOrDigit(string s)
    {
        foreach (var c in s) if (char.IsLetterOrDigit(c)) return c;
        return '\0';
    }

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
    /// The owner's Trainer Card customization (Profile redesign Phase 2): banner, accent, worn
    /// title, pinned achievements. Null on any server that predates the field — the card then
    /// renders exactly as it did in Phase 1. Always route it through
    /// <see cref="CosmeticsCatalog.SanitizeViewed"/> before rendering: this is another user's
    /// data and their build may ship art ids this one does not.
    /// </summary>
    [JsonProperty("cosmetics")]
    public Models.ProfileCosmetics? Cosmetics { get; set; }

    /// <summary>
    /// Display name with OG star prefix if applicable
    /// </summary>
    public string DisplayNameWithFlair => DisplayName ?? "";
}
