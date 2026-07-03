using ConditioningControlPanel.Core.Services.Progression;
using ConditioningControlPanel.Models;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;

namespace ConditioningControlPanel.Core.Services;

/// <summary>
/// Owns the local-only (decision #2) per-season counters and the Season Recap snapshots.
/// Static because every mutation reads/writes <see cref="CoreApp.Settings"/> and needs no own
/// state, which also keeps it out of the head initialization-order dance.
///
/// Ported from the legacy WPF <c>ConditioningControlPanel.Services.SeasonRecapService</c>;
/// the head-specific dependencies (paths, logger, leaderboard, auth, achievements) are resolved
/// through <see cref="CoreApp.Services"/> so the service works from either head.
///
/// CRITICAL ORDERING: <see cref="CaptureAndRollover"/> writes the snapshot to disk BEFORE it
/// clears the live counters. If the clear ran first the card would be empty.
/// </summary>
public static class SeasonRecapService
{
    private static IAppEnvironment? Env => CoreApp.Services?.GetService<IAppEnvironment>();

    private static string SnapshotDir => Path.Combine(
        Env?.UserDataPath ?? Path.Combine(Path.GetTempPath(), "ConditioningControlPanel"),
        "season-recaps");

    private static string PathFor(string seasonKey) =>
        Path.Combine(SnapshotDir, $"{seasonKey}.json");

    /// <summary>
    /// Current season key, "yyyy-MM". The season boundary is SERVER-authoritative: the server
    /// rotates the season (and fires the level_reset) on its own schedule, which is NOT guaranteed
    /// to align with the local wall-clock 1st-of-month. Keying off wall-clock made the local stats
    /// bucket roll prematurely on the 1st — before the server ended the season — discarding the real
    /// month's totals. So prefer the server's CurrentSeason; fall back to wall-clock only when it's
    /// unknown (not logged in / invite users) or not in yyyy-MM form.
    /// </summary>
    public static string CurrentSeasonKey
    {
        get
        {
            var server = CoreApp.Settings?.Current?.CurrentSeason;
            if (!string.IsNullOrWhiteSpace(server) && LooksLikeMonthKey(server!))
                return server!;
            return DateTime.UtcNow.ToString("yyyy-MM");
        }
    }

    /// <summary>True if the string is a "yyyy-MM" month key (so it's safe to compare/order with our keys).</summary>
    private static bool LooksLikeMonthKey(string k) =>
        k.Length == 7 && k[4] == '-'
        && int.TryParse(k.Substring(0, 4), out _)
        && int.TryParse(k.Substring(5, 2), out _);

    // ---------- live counter mutations (call from feature hook points) ----------

    /// <summary>
    /// Ensure SeasonStatsSeason is initialized. Sets it to the current season only when null
    /// (first run). Deliberately does NOT auto-roll on a mismatch — rollover is handled at startup
    /// so the recap card isn't skipped. Returns the active bucket key.
    /// </summary>
    private static string EnsureBucket(AppSettings s)
    {
        if (string.IsNullOrEmpty(s.SeasonStatsSeason))
            s.SeasonStatsSeason = CurrentSeasonKey;
        return s.SeasonStatsSeason!;
    }

    public static void AddConditioningMinutes(double minutes)
    {
        if (minutes <= 0) return;
        var s = CoreApp.Settings?.Current; if (s == null) return;
        EnsureBucket(s);
        s.SeasonConditioningMinutes += minutes;
        // No Save() here — the all-time write in the skill-tree service saves.
    }

    public static void IncrementSessionStarted()
    {
        var s = CoreApp.Settings?.Current; if (s == null) return;
        EnsureBucket(s);
        s.SeasonSessionsStarted += 1;
    }

    /// <summary>Record today (UTC date) as an active day this season.</summary>
    public static void MarkActiveToday()
    {
        var s = CoreApp.Settings?.Current; if (s == null) return;
        EnsureBucket(s);
        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        if (!s.SeasonActiveDays.Contains(today))
        {
            s.SeasonActiveDays.Add(today);
            CoreApp.Settings?.Save();
        }
    }

    /// <summary>Keep the season peak streak (survives a CurrentStreak reset).</summary>
    public static void TrackStreakPeak(int currentStreak)
    {
        var s = CoreApp.Settings?.Current; if (s == null) return;
        EnsureBucket(s);
        if (currentStreak > s.SeasonPeakStreak)
            s.SeasonPeakStreak = currentStreak;
    }

    /// <summary>
    /// Sample a leaderboard rank (decision #1: client-side peak). Keeps the lowest rank number
    /// seen this season and the user count at that moment. Ignores non-positive ranks.
    /// </summary>
    public static void SampleRank(int rank, int totalUsers)
    {
        if (rank <= 0) return;
        var s = CoreApp.Settings?.Current; if (s == null) return;
        EnsureBucket(s);
        if (s.SeasonPeakRank == 0 || rank < s.SeasonPeakRank)
        {
            s.SeasonPeakRank = rank;
            s.SeasonPeakRankTotal = totalUsers;
        }
    }

    public static void TrackFeature(string featureKey)
    {
        var s = CoreApp.Settings?.Current; if (s == null) return;
        EnsureBucket(s);
        s.TrackSeasonFeature(featureKey);
    }

    /// <summary>Count sparkle points spent on an enhancement this season (Prestige delta).</summary>
    public static void TrackPointsSpent(int amount)
    {
        if (amount <= 0) return;
        var s = CoreApp.Settings?.Current; if (s == null) return;
        EnsureBucket(s);
        s.SeasonPointsSpent += amount;
        // No Save() here — PurchaseSkillAsync saves right after adopting server values.
    }

    // ---------- snapshot + rollover ----------

    /// <summary>
    /// Build a snapshot of the just-ended season, persist it (if it holds any real data), then roll
    /// the live counters to <paramref name="newSeasonKey"/>. Returns the snapshot so the caller can
    /// present the card, or null on failure. Snapshot is saved BEFORE clear.
    /// </summary>
    public static SeasonRecapSnapshot? CaptureAndRollover(string newSeasonKey)
    {
        try
        {
            var s = CoreApp.Settings?.Current;
            if (s == null) return null;

            var ended = string.IsNullOrEmpty(s.SeasonStatsSeason)
                ? SeasonNumbering.Previous(newSeasonKey)
                : s.SeasonStatsSeason!;

            // Only ever roll FORWARD. If the target season isn't strictly after the ended bucket,
            // nothing actually ended, so refuse to snapshot/roll (equal = duplicate replayed reset;
            // earlier = a stale/desynced backward key). Ordinal string compare is chronological for
            // zero-padded yyyy-MM keys.
            if (string.CompareOrdinal(newSeasonKey, ended) <= 0)
            {
                CoreApp.Logger?.LogInformation("SeasonRecap: skipping rollover — target {New} is not after ended {Ended}", newSeasonKey, ended);
                return null;
            }

            var snap = BuildSnapshot(ended, s);

            if (HasMeaningfulData(snap))
                Save(snap);              // <-- WRITE BEFORE CLEAR

            RollBucket(s, newSeasonKey); // clears counters, sets SeasonStatsSeason
            return HasMeaningfulData(snap) ? snap : null;
        }
        catch (Exception ex)
        {
            CoreApp.Logger?.LogWarning(ex, "SeasonRecap CaptureAndRollover failed");
            return null;
        }
    }

    private static SeasonRecapSnapshot BuildSnapshot(string seasonKey, AppSettings s)
    {
        int percentile = PercentileFor(s.SeasonPeakRank, s.SeasonPeakRankTotal);
        if (percentile == 0)
            percentile = CoreApp.Services?.GetService<ILeaderboardService>()?.GetPlayerPercentile() ?? 0;

        var lifetimePointsSpent = CoreApp.Services?.GetService<IAchievementService>()?.Progress?.LifetimeSkillPointsSpent ?? 0;
        var isSupporter = CoreApp.Services?.GetService<IAuthProvider>()?.HasPremiumAccess ?? false;

        return new SeasonRecapSnapshot
        {
            SeasonKey = seasonKey,
            CapturedAtUtc = DateTime.UtcNow,
            Handle = string.IsNullOrWhiteSpace(s.UserDisplayName) ? "you" : s.UserDisplayName!,
            SeasonMinutes = s.SeasonConditioningMinutes,
            AllTimeMinutes = s.TotalConditioningMinutes,
            SessionCount = s.SeasonSessionsStarted,
            PeakRank = s.SeasonPeakRank,
            PeakRankTotal = s.SeasonPeakRankTotal,
            Percentile = percentile,
            DaysActive = s.SeasonActiveDays.Count,
            SeasonLengthDays = SeasonNumbering.LengthDays(seasonKey),
            LongestStreak = s.SeasonPeakStreak,
            HighestLevelEver = s.HighestLevelEver,
            IsSupporter = isSupporter,
            IsOg = s.IsSeason0Og,
            FeatureUse = new Dictionary<string, int>(s.SeasonFeatureUse),
            FeaturesTotal = SeasonFeatureKeys.TotalCount,
            // Schema 2 (Prestige / Ditzy Data PRO)
            PeakLevel = Math.Max(s.SeasonPeakLevel, s.PlayerLevel),
            PointsSpentSeason = s.SeasonPointsSpent,
            LifetimePointsSpent = lifetimePointsSpent,
            SkillsOwned = s.UnlockedSkills?.Count ?? 0,
            ActiveDays = new List<string>(s.SeasonActiveDays),
        };
    }

    private static void RollBucket(AppSettings s, string newSeasonKey)
    {
        s.SeasonConditioningMinutes = 0;
        s.SeasonSessionsStarted = 0;
        s.SeasonActiveDays = new List<string>();
        s.SeasonPeakStreak = 0;
        s.SeasonPeakRank = 0;
        s.SeasonPeakRankTotal = 0;
        s.SeasonFeatureUse = new Dictionary<string, int>();
        s.SeasonPeakLevel = 0;
        s.SeasonPointsSpent = 0;
        s.SeasonStatsSeason = newSeasonKey;
        CoreApp.Settings?.Save();
        CoreApp.Logger?.LogInformation("SeasonRecap: rolled season counters to {Season}", newSeasonKey);
    }

    public static int PercentileFor(int rank, int total)
    {
        if (rank <= 0 || total <= 0) return 0;
        var pct = (int)Math.Ceiling((double)rank / total * 100);
        return Math.Min(99, Math.Max(1, pct));
    }

    public static bool HasMeaningfulData(SeasonRecapSnapshot s) =>
        s.SeasonMinutes > 0 || s.SessionCount > 0 || s.FeatureUse.Count > 0
        || s.LongestStreak > 0 || s.DaysActive > 0 || s.PeakRank > 0;

    // ---------- persistence / re-view ----------

    public static void Save(SeasonRecapSnapshot snapshot)
    {
        try
        {
            Directory.CreateDirectory(SnapshotDir);
            var json = JsonConvert.SerializeObject(snapshot, Formatting.Indented);
            File.WriteAllText(PathFor(snapshot.SeasonKey), json);
            CoreApp.Logger?.LogInformation("SeasonRecap: saved snapshot for {Season}", snapshot.SeasonKey);
        }
        catch (Exception ex)
        {
            CoreApp.Logger?.LogWarning(ex, "SeasonRecap: failed to save snapshot for {Season}", snapshot.SeasonKey);
        }
    }

    public static SeasonRecapSnapshot? Load(string seasonKey)
    {
        try
        {
            var path = PathFor(seasonKey);
            if (!File.Exists(path)) return null;
            return JsonConvert.DeserializeObject<SeasonRecapSnapshot>(File.ReadAllText(path));
        }
        catch (Exception ex)
        {
            CoreApp.Logger?.LogWarning(ex, "SeasonRecap: failed to load snapshot {Season}", seasonKey);
            return null;
        }
    }

    /// <summary>Most recently completed season's snapshot, or null if none exist yet.</summary>
    public static SeasonRecapSnapshot? LoadLatest()
    {
        var keys = ListSeasonKeys();
        return keys.Count == 0 ? null : Load(keys[0]);
    }

    /// <summary>Available snapshot season keys, newest first.</summary>
    public static List<string> ListSeasonKeys()
    {
        try
        {
            if (!Directory.Exists(SnapshotDir)) return new List<string>();
            return Directory.GetFiles(SnapshotDir, "*.json")
                .Select(Path.GetFileNameWithoutExtension)
                .Where(k => !string.IsNullOrEmpty(k))
                .OrderByDescending(k => k, StringComparer.Ordinal)
                .Cast<string>()
                .ToList();
        }
        catch (Exception ex)
        {
            CoreApp.Logger?.LogWarning(ex, "SeasonRecap: failed to list snapshots");
            return new List<string>();
        }
    }

    public static bool HasAnySnapshot() => ListSeasonKeys().Count > 0;
}
