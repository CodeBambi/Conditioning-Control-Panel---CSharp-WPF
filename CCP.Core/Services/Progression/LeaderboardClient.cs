using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Serilog;

namespace ConditioningControlPanel.Services;

/// <summary>
/// The leaderboard wire, READ-ONLY: GET /v3/leaderboard and GET /user/lookup (WPF LeaderboardService.RefreshAsync /
/// LookupUserAsync, which delegate here). Ranking is MainWindow.Leaderboard.cs RankLeaderboardEntries' order.
/// </summary>
public sealed class LeaderboardClient
{
    public const string ProxyBaseUrl = "https://codebambi-proxy.vercel.app";
    public const int FetchLimit = 200;

    private static readonly Lazy<HttpClient> Shared = new(() => V2AuthService.Configure(new HttpClient()));
    private readonly HttpClient _http;

    /// <param name="handler">Test seam; null uses the shared production client.</param>
    public LeaderboardClient(HttpMessageHandler? handler = null) =>
        _http = handler == null ? Shared.Value : V2AuthService.Configure(new HttpClient(handler));

    /// <summary>One board page; "all-time" or the current UTC month. Null with <paramref name="error"/> set on a non-2xx.</summary>
    public async Task<(LeaderboardPage<T>? page, string? error)> FetchAsync<T>(string mode, string? unifiedId, DateTime utcNow)
        where T : LeaderboardEntryData
    {
        var season = mode == "all-time" ? "all-time" : utcNow.ToString("yyyy-MM");
        var url = $"{ProxyBaseUrl}/v3/leaderboard?season={season}&limit={FetchLimit}";
        if (!string.IsNullOrEmpty(unifiedId))
            url += $"&unified_id={Uri.EscapeDataString(unifiedId)}";
        var response = await _http.GetAsync(url);
        if (!response.IsSuccessStatusCode)
        {
            Log.Warning("Leaderboard fetch failed: {Status} - {Error}", response.StatusCode, await response.Content.ReadAsStringAsync());
            return (null, $"Server returned {response.StatusCode}");
        }
        var page = JsonConvert.DeserializeObject<LeaderboardPage<T>>(await response.Content.ReadAsStringAsync());
        return page?.Entries == null ? (null, null) : (page, null);
    }

    /// <summary>Canonical ranks: the server sorted set's key (all-time: total XP earned, then peak level; monthly: XP, then
    /// level), so a row's Rank agrees with your_rank. Returns the ordered rows with Rank and IsAllTimeView set.</summary>
    public static List<T> Rank<T>(IEnumerable<T> entries, bool allTime) where T : LeaderboardEntryData
    {
        var ordered = allTime
            ? entries.OrderByDescending(x => x.TotalXpEarned).ThenByDescending(x => x.HighestLevelEver).ToList()
            : entries.OrderByDescending(x => x.Xp).ThenByDescending(x => x.Level).ToList();
        for (int i = 0; i < ordered.Count; i++) { ordered[i].Rank = i + 1; ordered[i].IsAllTimeView = allTime; }
        return ordered;
    }

    /// <summary>A user's fresh profile card by display name; null on any failure.</summary>
    public async Task<UserLookupResult?> LookupUserAsync(string displayName)
    {
        try
        {
            var response = await _http.GetAsync($"{ProxyBaseUrl}/user/lookup?display_name={Uri.EscapeDataString(displayName)}");
            if (!response.IsSuccessStatusCode)
            {
                Log.Warning("User lookup failed: {Status} for {Name}", response.StatusCode, displayName);
                return null;
            }
            return JsonConvert.DeserializeObject<UserLookupResult>(await response.Content.ReadAsStringAsync());
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "User lookup failed for {Name}", displayName);
            return null;
        }
    }
}

/// <summary>GET /v3/leaderboard's body.</summary>
public sealed class LeaderboardPage<T>
{
    [JsonProperty("entries")]
    public List<T>? Entries { get; set; }

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

/// <summary>
/// Represents a single entry on the leaderboard
/// </summary>
public class LeaderboardEntryData
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
            // Invariant: "120.0k" on every locale, as the leaderboard rows read (an Italian machine printed "120,0k").
            if (Xp >= 1_000_000)
                return FormattableString.Invariant($"{Xp / 1_000_000.0:F1}M");
            if (Xp >= 1_000)
                return FormattableString.Invariant($"{Xp / 1_000.0:F1}k");
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
            return FormattableString.Invariant($"{value / 1_000_000.0:F1}M");
        if (value >= 1_000)
            return FormattableString.Invariant($"{value / 1_000.0:F1}k");
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
        && string.Equals(UnifiedId, CoreAccount.UnifiedUserId, StringComparison.Ordinal);

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

    /// <summary>
    /// The initials avatar's two gradient stops (top, bottom) as RGB, from a stable hash of the name. Hues are clamped to
    /// 200-345 deg (blue - indigo - violet - magenta - pink) so the generated avatars stay inside the app's palette.
    /// Each head builds its brush from these (WPF LeaderboardEntry, Avalonia LeaderboardRow).
    /// </summary>
    public static ((byte R, byte G, byte B) Top, (byte R, byte G, byte B) Bottom) AvatarGradient(string? name)
    {
        var hash = StableHash(name ?? "");
        var hue = 200.0 + (hash % 146);              // 200 .. 345
        var hue2 = hue - 14.0; if (hue2 < 195.0) hue2 += 150.0;
        return (FromHsl(hue, 0.70, 0.70), FromHsl(hue2, 0.52, 0.40));
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

    private static (byte, byte, byte) FromHsl(double h, double s, double l)
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

        static byte B(double v) => (byte)Math.Round(Math.Clamp(v * 255, 0, 255));
        return (B(r + m), B(g + m), B(b + m));
    }

    private static char FirstLetterOrDigit(string s)
    {
        foreach (var c in s) if (char.IsLetterOrDigit(c)) return c;
        return '\0';
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

    /// <summary>Whitelist (Lab pass) badge. Server-computed; false on older servers.</summary>
    [JsonProperty("is_whitelisted")]
    public bool IsWhitelisted { get; set; }

    /// <summary>Discord staff-role badge. Only ever written server-side from the bot's
    /// guild-member fetch; false on older servers.</summary>
    [JsonProperty("is_staff")]
    public bool IsStaff { get; set; }

    /// <summary>Staff tier ("owner" | "admin" | "support") or null; badge border color.</summary>
    [JsonProperty("staff_role")]
    public string? StaffRole { get; set; }

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
