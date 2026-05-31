using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Models;
using Newtonsoft.Json;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// Owns the local-only (decision #2) per-season counters and the Season Recap snapshots.
    /// Static because every mutation reads/writes App.Settings.Current and needs no own state,
    /// which also keeps it out of the App.OnStartup initialization-order dance.
    ///
    /// CRITICAL ORDERING: <see cref="CaptureAndRollover"/> writes the snapshot to disk BEFORE
    /// it clears the live counters. If the clear ran first the card would be empty.
    /// </summary>
    public static class SeasonRecapService
    {
        private static string SnapshotDir => Path.Combine(App.UserDataPath, "season-recaps");

        private static string PathFor(string seasonKey) =>
            Path.Combine(SnapshotDir, $"{seasonKey}.json");

        /// <summary>Current season key, "yyyy-MM" UTC.</summary>
        public static string CurrentSeasonKey => DateTime.UtcNow.ToString("yyyy-MM");

        // ---------- live counter mutations (call from feature hook points) ----------

        /// <summary>
        /// Ensure SeasonStatsSeason is initialized. Sets it to the current month only when
        /// null (first run). Deliberately does NOT auto-roll on a month mismatch — rollover
        /// is handled at startup so the recap card isn't skipped. Returns the active bucket key.
        /// </summary>
        private static string EnsureBucket(Models.AppSettings s)
        {
            if (string.IsNullOrEmpty(s.SeasonStatsSeason))
                s.SeasonStatsSeason = CurrentSeasonKey;
            return s.SeasonStatsSeason!;
        }

        public static void AddConditioningMinutes(double minutes)
        {
            if (minutes <= 0) return;
            var s = App.Settings?.Current; if (s == null) return;
            EnsureBucket(s);
            s.SeasonConditioningMinutes += minutes;
            // No Save() here — the all-time write in SkillTreeService.AddConditioningTime saves.
        }

        public static void IncrementSessionStarted()
        {
            var s = App.Settings?.Current; if (s == null) return;
            EnsureBucket(s);
            s.SeasonSessionsStarted += 1;
        }

        /// <summary>Record today (UTC date) as an active day this season.</summary>
        public static void MarkActiveToday()
        {
            var s = App.Settings?.Current; if (s == null) return;
            EnsureBucket(s);
            var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
            if (!s.SeasonActiveDays.Contains(today))
            {
                s.SeasonActiveDays.Add(today);
                App.Settings?.Save();
            }
        }

        /// <summary>Keep the season peak streak (survives a CurrentStreak reset).</summary>
        public static void TrackStreakPeak(int currentStreak)
        {
            var s = App.Settings?.Current; if (s == null) return;
            EnsureBucket(s);
            if (currentStreak > s.SeasonPeakStreak)
                s.SeasonPeakStreak = currentStreak;
        }

        /// <summary>
        /// Sample a leaderboard rank (decision #1: client-side peak). Keeps the lowest rank
        /// number seen this season and the user count at that moment. Ignores non-positive ranks.
        /// </summary>
        public static void SampleRank(int rank, int totalUsers)
        {
            if (rank <= 0) return;
            var s = App.Settings?.Current; if (s == null) return;
            EnsureBucket(s);
            if (s.SeasonPeakRank == 0 || rank < s.SeasonPeakRank)
            {
                s.SeasonPeakRank = rank;
                s.SeasonPeakRankTotal = totalUsers;
            }
        }

        public static void TrackFeature(string featureKey)
        {
            var s = App.Settings?.Current; if (s == null) return;
            EnsureBucket(s);
            s.TrackSeasonFeature(featureKey);
        }

        // ---------- snapshot + rollover ----------

        /// <summary>
        /// Build a snapshot of the just-ended season, persist it (if it holds any real data),
        /// then roll the live counters to <paramref name="newSeasonKey"/>. Returns the snapshot
        /// so the caller can present the card, or null on failure. Snapshot is saved BEFORE clear.
        /// </summary>
        public static SeasonRecapSnapshot? CaptureAndRollover(string newSeasonKey)
        {
            try
            {
                var s = App.Settings?.Current;
                if (s == null) return null;

                var ended = string.IsNullOrEmpty(s.SeasonStatsSeason)
                    ? SeasonNumbering.Previous(newSeasonKey)
                    : s.SeasonStatsSeason!;

                var snap = BuildSnapshot(ended, s);

                if (HasMeaningfulData(snap))
                    Save(snap);              // <-- WRITE BEFORE CLEAR

                RollBucket(s, newSeasonKey); // clears counters, sets SeasonStatsSeason
                return HasMeaningfulData(snap) ? snap : null;
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "SeasonRecap CaptureAndRollover failed");
                return null;
            }
        }

        private static SeasonRecapSnapshot BuildSnapshot(string seasonKey, Models.AppSettings s)
        {
            int percentile = PercentileFor(s.SeasonPeakRank, s.SeasonPeakRankTotal);
            if (percentile == 0)
                percentile = App.Leaderboard?.GetPlayerPercentile() ?? 0;

            return new SeasonRecapSnapshot
            {
                SeasonKey = seasonKey,
                CapturedAtUtc = DateTime.UtcNow,
                Handle = App.UserDisplayName ?? "you",
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
                IsSupporter = App.Patreon?.HasPremiumAccess ?? false,
                IsOg = s.IsSeason0Og,
                FeatureUse = new Dictionary<string, int>(s.SeasonFeatureUse),
                FeaturesTotal = SeasonFeatureKeys.TotalCount,
            };
        }

        private static void RollBucket(Models.AppSettings s, string newSeasonKey)
        {
            s.SeasonConditioningMinutes = 0;
            s.SeasonSessionsStarted = 0;
            s.SeasonActiveDays = new List<string>();
            s.SeasonPeakStreak = 0;
            s.SeasonPeakRank = 0;
            s.SeasonPeakRankTotal = 0;
            s.SeasonFeatureUse = new Dictionary<string, int>();
            s.SeasonStatsSeason = newSeasonKey;
            App.Settings?.Save();
            App.Logger?.Information("SeasonRecap: rolled season counters to {Season}", newSeasonKey);
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
                App.Logger?.Information("SeasonRecap: saved snapshot for {Season}", snapshot.SeasonKey);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "SeasonRecap: failed to save snapshot for {Season}", snapshot.SeasonKey);
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
                App.Logger?.Warning(ex, "SeasonRecap: failed to load snapshot {Season}", seasonKey);
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
                App.Logger?.Warning(ex, "SeasonRecap: failed to list snapshots");
                return new List<string>();
            }
        }

        public static bool HasAnySnapshot() => ListSeasonKeys().Count > 0;
    }
}
