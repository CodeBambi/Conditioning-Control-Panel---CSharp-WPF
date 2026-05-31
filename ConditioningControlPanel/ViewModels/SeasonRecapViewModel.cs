using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.ViewModels
{
    /// <summary>One badge in the card's "Conditioning Used" row.</summary>
    public class FeatureBadgeViewModel
    {
        public string Label { get; init; } = "";
        public int Count { get; init; }
        /// <summary>pack:// URI of the dashboard feature icon (bound to Image.Source).</summary>
        public string? ImagePath { get; init; }
    }

    /// <summary>
    /// Fully data-bound presentation model for the Season Recap card. Built from an immutable
    /// <see cref="SeasonRecapSnapshot"/> so the same VM serves both the live rollover moment and
    /// the re-view surface. Holds NO placeholder values — every string derives from the snapshot.
    ///
    /// The three "count-up" figures (season time, all-time time, peak rank) are exposed both as
    /// final formatted strings (for the frozen PNG still) and as numeric targets the control
    /// animates toward on reveal.
    /// </summary>
    public class SeasonRecapViewModel
    {
        private readonly SeasonRecapSnapshot _s;

        public SeasonRecapViewModel(SeasonRecapSnapshot snapshot)
        {
            _s = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        }

        /// <summary>Mode-sensitive backdrop art for the card (green for drone mode, pink default).</summary>
        public string BackgroundImagePath => RecapBackgrounds.ForMod(App.Mods?.ActiveModId);

        // ---------- header ----------
        public int SeasonNumber => SeasonNumbering.ToSeasonNumber(_s.SeasonKey);
        public int NextSeasonNumber => SeasonNumber + 1;
        public string SeasonLabel => Loc.Get("recap_header_season"); // "SEASON"
        public string SeasonNumberText => SeasonNumber >= 0 ? SeasonNumber.ToString("00") : "--";
        public string StatusText => Loc.Get("recap_status_complete");

        public string DateRangeText
        {
            get
            {
                var (start, end) = SeasonNumbering.DateRange(_s.SeasonKey);
                if (start == DateTime.MinValue) return "";
                // Lowercase month abbreviations to match the card's voice (deviation from a
                // locale-default date format is intentional — this reads as brand copy).
                string fmt(DateTime d) => d.ToString("MMM dd", CultureInfo.InvariantCulture).ToLowerInvariant();
                return $"{fmt(start)} - {fmt(end)} · {start.Year}";
            }
        }

        // ---------- identity ----------
        public string Handle => string.IsNullOrWhiteSpace(_s.Handle) ? Loc.Get("recap_default_handle") : _s.Handle;

        /// <summary>Flavor title resolved live from the configurable <see cref="TitleTiers"/> table.</summary>
        public string TitleText => Loc.Get(TitleTiers.Resolve(AllTimeHours, _s.Percentile));

        // Status pills (shown under the hero).
        public string SupporterLabel => Loc.Get("recap_badge_supporter");
        public string OgLabel => Loc.Get("recap_badge_og");
        public Visibility SupporterVisibility => _s.IsSupporter ? Visibility.Visible : Visibility.Collapsed;
        public Visibility OgVisibility => _s.IsOg ? Visibility.Visible : Visibility.Collapsed;
        public Visibility StatusRowVisibility => (_s.IsSupporter || _s.IsOg) ? Visibility.Visible : Visibility.Collapsed;

        // ---------- hero (season time + all-time) ----------
        public double SeasonMinutes => _s.SeasonMinutes;
        public double AllTimeMinutes => _s.AllTimeMinutes;
        public double AllTimeHours => _s.AllTimeMinutes / 60.0;

        public string SeasonTimeText => FormatHm(_s.SeasonMinutes);
        public string AllTimeText => FormatHm(_s.AllTimeMinutes);
        public string HeroLabel => Loc.Get("recap_hero_label");
        public string AllTimeLabel => Loc.Get("recap_alltime_label");
        public string SessionsSubline => Loc.GetF("recap_sessions_subline", _s.SessionCount);

        // ---------- stat grid ----------
        public bool HasRank => _s.PeakRank > 0;
        public string PeakRankText => _s.PeakRank > 0 ? $"#{_s.PeakRank}" : "—";
        public int PeakRankTarget => _s.PeakRank;
        public string PeakRankOfText => _s.PeakRank > 0 && _s.PeakRankTotal > 0
            ? Loc.GetF("recap_rank_of", _s.PeakRankTotal.ToString("N0"))
            : "";
        public string PercentileText => _s.Percentile > 0 ? Loc.GetF("recap_top_percent", _s.Percentile) : "—";
        public string DaysActiveText => _s.DaysActive.ToString();
        public string DaysActiveOfText => _s.SeasonLengthDays > 0 ? $"/ {_s.SeasonLengthDays}" : "";
        public string LongestStreakText => _s.LongestStreak.ToString();
        public string StreakUnitText => Loc.Get("recap_streak_days");

        public string StatPeakRankLabel => Loc.Get("recap_stat_peak_rank");
        public string StatPercentileLabel => Loc.Get("recap_stat_percentile");
        public string StatDaysActiveLabel => Loc.Get("recap_stat_days_active");
        public string StatStreakLabel => Loc.Get("recap_stat_longest_streak");

        // ---------- badge row ----------
        public string BadgesTitle => Loc.Get("recap_badges_title");

        public int FeaturesUsedCount => _s.FeatureUse.Count(kv => kv.Value > 0);
        public int FeaturesTotal => _s.FeaturesTotal > 0 ? _s.FeaturesTotal : SeasonFeatureKeys.TotalCount;
        public string FeaturesUsedText => Loc.GetF("recap_features_used", FeaturesUsedCount, FeaturesTotal);

        /// <summary>Top 6 features used this season, ranked by engagement count (desc).</summary>
        public IReadOnlyList<FeatureBadgeViewModel> TopBadges
        {
            get
            {
                return _s.FeatureUse
                    .Where(kv => kv.Value > 0)
                    .OrderByDescending(kv => kv.Value)
                    .ThenBy(kv => kv.Key, StringComparer.Ordinal) // stable tie-break
                    .Take(6)
                    .Select(kv =>
                    {
                        var def = SeasonFeatureKeys.Find(kv.Key);
                        return new FeatureBadgeViewModel
                        {
                            Label = def != null ? Loc.Get(def.LabelLocKey) : kv.Key,
                            Count = kv.Value,
                            // Mod-aware: prefer the active mod's feature icon, else the embedded default.
                            ImagePath = def != null
                                ? Services.ModResourceResolver.ResolveUri(def.ImagePath)
                                : null,
                        };
                    })
                    .ToList();
            }
        }

        // ---------- verdict ----------
        public string VerdictEyebrow => Loc.Get("recap_verdict_eyebrow");

        // The verdict template contains the "{0}" placeholder where the handle goes; we split on
        // it so the handle can be styled separately. The bucket is chosen deterministically from
        // the data so re-view is stable.
        private string VerdictKey
        {
            get
            {
                var pct = _s.Percentile;
                if (pct > 0 && pct <= 2) return "recap_verdict_elite";
                if (pct > 0 && pct <= 10) return "recap_verdict_strong";
                if (AllTimeHours >= 50 || _s.SeasonMinutes >= 600) return "recap_verdict_mid";
                return "recap_verdict_gentle";
            }
        }
        public string VerdictBefore => SplitVerdict().before;
        public string VerdictName => Handle;
        public string VerdictAfter => SplitVerdict().after;

        private (string before, string after) SplitVerdict()
        {
            var t = Loc.Get(VerdictKey); // raw template, still contains "{0}"
            const string token = "{0}";
            var idx = t.IndexOf(token, StringComparison.Ordinal);
            if (idx < 0) return (t, "");
            return (t.Substring(0, idx), t.Substring(idx + token.Length));
        }

        // ---------- reset / brand ----------
        public int AllTimeHoursRounded => (int)Math.Round(AllTimeHours, MidpointRounding.AwayFromZero);
        public string ResetBefore => Loc.GetF("recap_reset_before", SeasonNumberText);
        public string ResetBold => Loc.GetF("recap_reset_bold", AllTimeHoursRounded);
        public string ResetAfter => Loc.GetF("recap_reset_after", NextSeasonNumber.ToString("00"));
        public string ResetCta => Loc.Get("recap_reset_cta");
        public string BrandText => Loc.Get("recap_brand"); // contains cclabs.app — must reach the PNG

        // ---------- share prefill (single editable resource, lowercase voice) ----------
        public string SharePrefillText => Loc.GetF(
            "recap_share_prefill",
            SeasonNumberText,
            FormatHmCompact(_s.SeasonMinutes),
            FormatHmCompact(_s.AllTimeMinutes),
            _s.Percentile > 0 ? _s.Percentile.ToString() : "?");

        public string SuggestedFileName => $"cclabs-season-{_s.SeasonKey}.png";

        // ---------- formatting helpers ----------
        public static string FormatHm(double totalMinutes)
        {
            var t = Math.Max(0, totalMinutes);
            var hours = (int)(t / 60);
            var mins = (int)(t % 60);
            return Loc.GetF("recap_time_hm", hours, mins);
        }

        /// <summary>Compact "47h" / "312h" for the share string (hours only, rounded).</summary>
        public static string FormatHmCompact(double totalMinutes)
        {
            var hours = (int)Math.Round(Math.Max(0, totalMinutes) / 60.0, MidpointRounding.AwayFromZero);
            return $"{hours}h";
        }
    }
}
