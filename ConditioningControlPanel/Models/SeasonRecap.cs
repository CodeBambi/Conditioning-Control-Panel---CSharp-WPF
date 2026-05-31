using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace ConditioningControlPanel.Models
{
    /// <summary>
    /// Canonical catalog of features whose per-season engagement we count for the
    /// Season Recap badge row. Keys are stable strings persisted in
    /// AppSettings.SeasonFeatureUse — DO NOT rename a key without a migration, it
    /// would orphan historical counts. Labels are localized; glyphs are placeholder
    /// vector paths (real feature icons slot in later, per the mission).
    /// </summary>
    public static class SeasonFeatureKeys
    {
        public const string Flash = "flash";
        public const string Video = "video";
        public const string Subliminal = "subliminal";
        public const string Overlay = "overlay";
        public const string Bubbles = "bubbles";
        public const string BubbleCount = "bubblecount";
        public const string BouncingText = "bouncingtext";
        public const string LockCard = "lockcard";
        public const string PopQuiz = "popquiz";
        public const string MindWipe = "mindwipe";
        public const string BlinkTrainer = "blink";
        public const string Companion = "companion";

        public sealed record FeatureDef(string Key, string LabelLocKey, string ImagePath);

        /// <summary>
        /// The trackable conditioning features shown in the badge row. Count is the "/ total used"
        /// denominator on the card. ImagePath is a path RELATIVE to Resources/ — resolved at runtime
        /// via ModResourceResolver, which prefers the active mod's override
        /// (&lt;mod&gt;/resources/features/*.png) and falls back to the embedded default. Only
        /// dashboard conditioning effects are included (same icon set the dashboard uses).
        /// </summary>
        public static readonly IReadOnlyList<FeatureDef> Catalog = new List<FeatureDef>
        {
            new(Flash,        "recap_feat_flash",        "features/flash.png"),
            new(Video,        "recap_feat_video",        "features/mandatory_videos.png"),
            new(Subliminal,   "recap_feat_subliminal",   "features/subliminal.png"),
            new(Overlay,      "recap_feat_overlay",      "features/spiral_overlay.png"),
            new(Bubbles,      "recap_feat_bubbles",      "features/Bubble_pop.png"),
            new(BubbleCount,  "recap_feat_bubblecount",  "features/Bubble_count.png"),
            new(BouncingText, "recap_feat_bouncingtext", "features/bouncing_text.png"),
            new(LockCard,     "recap_feat_lockcard",     "features/Phrase_Lock.png"),
            new(MindWipe,     "recap_feat_mindwipe",     "features/Mind_Wipers.png"),
        };

        public static int TotalCount => Catalog.Count;

        public static FeatureDef? Find(string key) => Catalog.FirstOrDefault(f => f.Key == key);
    }

    /// <summary>
    /// Monthly season ("yyyy-MM") helpers. The app rotates seasons on the 1st of every
    /// month UTC. The displayed "SEASON NN" is months-since-Epoch, where Epoch (Feb 2026)
    /// is Season 0 — so Feb 2026 = 0, Mar 2026 = 1, May 2026 = 3, etc.
    ///
    /// EDITABLE: <see cref="Epoch"/> lives in exactly this one place so the numbering can be
    /// corrected without touching the card.
    /// (Deviation from mockup: the mockup shows a multi-month range "feb 01 — apr 30"; the
    /// real seasons are monthly, so the range is the calendar month and the number is derived.)
    /// </summary>
    public static class SeasonNumbering
    {
        /// <summary>Season 0 — the first season the app ran (February 2026).</summary>
        public const string Epoch = "2026-02";

        /// <summary>Parse a "yyyy-MM" key into (year, month). Returns false if malformed.</summary>
        public static bool TryParse(string? seasonKey, out int year, out int month)
        {
            year = 0; month = 0;
            if (string.IsNullOrWhiteSpace(seasonKey)) return false;
            var parts = seasonKey.Split('-');
            if (parts.Length != 2) return false;
            return int.TryParse(parts[0], out year) && int.TryParse(parts[1], out month)
                   && month >= 1 && month <= 12;
        }

        /// <summary>Season number for a "yyyy-MM" key (Epoch = 0), or -1 if unknown/pre-epoch.</summary>
        public static int ToSeasonNumber(string? seasonKey)
        {
            if (!TryParse(seasonKey, out var y, out var m)) return -1;
            if (!TryParse(Epoch, out var ey, out var em)) return -1;
            var n = (y - ey) * 12 + (m - em);
            return n < 0 ? -1 : n;
        }

        /// <summary>The previous season key for a given "yyyy-MM" (handles January wraparound).</summary>
        public static string Previous(string seasonKey)
        {
            if (!TryParse(seasonKey, out var y, out var m)) return seasonKey;
            var dt = new DateTime(y, m, 1).AddMonths(-1);
            return dt.ToString("yyyy-MM");
        }

        /// <summary>UTC first/last day of the month a season covers.</summary>
        public static (DateTime start, DateTime end) DateRange(string seasonKey)
        {
            if (!TryParse(seasonKey, out var y, out var m))
                return (DateTime.MinValue, DateTime.MinValue);
            var start = new DateTime(y, m, 1, 0, 0, 0, DateTimeKind.Utc);
            var end = start.AddMonths(1).AddDays(-1);
            return (start, end);
        }

        /// <summary>Number of calendar days in the season's month.</summary>
        public static int LengthDays(string seasonKey)
        {
            if (!TryParse(seasonKey, out var y, out var m)) return 0;
            return DateTime.DaysInMonth(y, m);
        }
    }

    /// <summary>
    /// Immutable snapshot of one completed season, persisted to disk so the recap card can be
    /// re-viewed and re-shared after the rollover moment. Written by SeasonRecapService BEFORE
    /// the live season counters are cleared. Serialized with Newtonsoft (matches AppSettings).
    /// </summary>
    public class SeasonRecapSnapshot
    {
        [JsonProperty("season_key")] public string SeasonKey { get; set; } = "";
        [JsonProperty("captured_at_utc")] public DateTime CapturedAtUtc { get; set; }

        [JsonProperty("handle")] public string Handle { get; set; } = "";

        // Time (minutes). Season resets each season; all-time persists across resets.
        [JsonProperty("season_minutes")] public double SeasonMinutes { get; set; }
        [JsonProperty("all_time_minutes")] public double AllTimeMinutes { get; set; }
        [JsonProperty("session_count")] public int SessionCount { get; set; }

        // Rank / percentile. PeakRank 0 = never sampled (fall back to "—").
        [JsonProperty("peak_rank")] public int PeakRank { get; set; }
        [JsonProperty("peak_rank_total")] public int PeakRankTotal { get; set; }
        [JsonProperty("percentile")] public int Percentile { get; set; }

        // Streaks / days.
        [JsonProperty("days_active")] public int DaysActive { get; set; }
        [JsonProperty("season_length_days")] public int SeasonLengthDays { get; set; }
        [JsonProperty("longest_streak")] public int LongestStreak { get; set; }

        // Permanent progression (for title tier resolution).
        [JsonProperty("highest_level_ever")] public int HighestLevelEver { get; set; }

        // Status flags shown as pills under the hero.
        [JsonProperty("is_supporter")] public bool IsSupporter { get; set; }
        [JsonProperty("is_og")] public bool IsOg { get; set; }

        // Per-feature engagement counts + catalog size at capture time.
        [JsonProperty("feature_use")] public Dictionary<string, int> FeatureUse { get; set; } = new();
        [JsonProperty("features_total")] public int FeaturesTotal { get; set; }

        [JsonProperty("schema")] public int Schema { get; set; } = 1;
    }

    /// <summary>
    /// Per-mod recap card backdrop art (seasonal neon). Mode-sensitive: each mod can have its own
    /// background; unmapped mods fall back to the default. EDITABLE in one place — add a mod id to
    /// the switch as new per-mod art is bundled into Resources/. Images are pack URIs.
    /// </summary>
    public static class RecapBackgrounds
    {
        public static string ForMod(string? modId) => modId switch
        {
            "drone-mode" => "pack://application:,,,/Resources/february_drone.png", // green neon
            _ => "pack://application:,,,/Resources/february.png",                   // default pink neon
        };
    }

    /// <summary>
    /// Configurable flavor-title mapping shown under the handle ("Deep Subject · Tier IV").
    /// PLACEHOLDER thresholds/names — kept in this ONE place so they can be finalized without
    /// hunting the codebase (mission requirement). Resolved live from snapshot inputs so that
    /// editing the table also updates previously-captured cards on re-view.
    /// Label strings are localized via the loc keys below.
    /// </summary>
    public static class TitleTiers
    {
        /// <summary>
        /// A tier: minimum all-time hours OR maximum percentile (top X%) to qualify.
        /// Evaluated top-down; first match wins. The last entry is the floor (always matches).
        /// </summary>
        public sealed record Tier(string LabelLocKey, double MinAllTimeHours, int MaxPercentile);

        // Ordered best -> floor. MaxPercentile 100 = "any". Tune freely.
        private static readonly IReadOnlyList<Tier> Table = new List<Tier>
        {
            new("recap_tier_5", 250, 1),   // Deep Subject · Tier V
            new("recap_tier_4", 120, 5),   // Deep Subject · Tier IV
            new("recap_tier_3", 50,  15),  // Tier III
            new("recap_tier_2", 15,  40),  // Tier II
            new("recap_tier_1", 0,   100), // Tier I (floor)
        };

        /// <summary>
        /// Resolve the tier loc-key for the given inputs. percentile == 0 means "unknown",
        /// treated as worst (100) for matching so a missing rank never inflates the tier.
        /// </summary>
        public static string Resolve(double allTimeHours, int percentile)
        {
            var effPct = percentile <= 0 ? 100 : percentile;
            foreach (var t in Table)
            {
                if (allTimeHours >= t.MinAllTimeHours && effPct <= t.MaxPercentile)
                    return t.LabelLocKey;
            }
            return Table[Table.Count - 1].LabelLocKey;
        }
    }
}
