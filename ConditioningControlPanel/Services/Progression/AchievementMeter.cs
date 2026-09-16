using System;
using System.Collections.Generic;
using System.Globalization;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services;

/// <summary>
/// How far a locked, countable achievement has come: <see cref="Current"/> out of
/// <see cref="Target"/>, both in the unit the card shows (checks, days, hours).
/// </summary>
public readonly record struct AchievementMeter(double Current, double Target)
{
    public bool IsComplete => Current >= Target;

    /// <summary>0..1, for the bar.</summary>
    public double Fraction => Target <= 0 ? 1 : Math.Clamp(Current / Target, 0, 1);

    /// <summary>"76 / 100", or "5.2 / 10" when the unit is hours. No words, so no loc key.</summary>
    public string Label => Fmt(Current) + " / " + Fmt(Target);

    private static string Fmt(double v)
    {
        var culture = CultureInfo.CurrentCulture;
        return Math.Abs(v - Math.Round(v)) < 0.0005
            ? Math.Round(v).ToString("N0", culture)
            : v.ToString("0.#", culture);
    }
}

/// <summary>
/// The one table that says which achievements are countable and where their number comes from.
///
/// <para>Countable means: the tracker that unlocks it compares ONE persisted lifetime counter on
/// <see cref="AchievementProgress"/> (or the player level) against ONE fixed threshold. Every
/// entry below reads the same field the tracker branches on and the same constant it branches
/// against, so the bar on the card and the unlock can never drift apart. First-time badges
/// (target 1), per-session windows, continuous timers that reset, flags, and anything with two
/// alternate counters are not in this table and report no progress at all.</para>
///
/// <para>Pure and static on purpose: no App, no service instance, so it runs in the test host
/// and costs one dictionary hit per card per refresh.</para>
/// </summary>
public static class AchievementMeters
{
    private delegate double Reader(AchievementProgress progress, int playerLevel);

    private static readonly Dictionary<string, (Reader Read, double Target)> Rules = BuildRules();

    /// <summary>Ids that carry a meter, in table order.</summary>
    public static IEnumerable<string> CountableIds => Rules.Keys;

    public static bool IsCountable(string achievementId) => Rules.ContainsKey(achievementId);

    /// <summary>
    /// Null for anything not in the table. An unlocked countable achievement reports full; a
    /// locked one reports its counter clamped to the target (a cloud restore can push a counter
    /// past the bar before the tracker's next tick fires the unlock).
    /// </summary>
    public static AchievementMeter? Compute(Achievement achievement, AchievementProgress progress, int playerLevel)
    {
        if (achievement == null || progress == null) return null;
        if (!Rules.TryGetValue(achievement.Id, out var rule)) return null;

        var target = rule.Target;
        if (progress.IsUnlocked(achievement.Id)) return new AchievementMeter(target, target);

        var current = rule.Read(progress, playerLevel);
        if (double.IsNaN(current)) current = 0;
        return new AchievementMeter(Math.Clamp(current, 0, target), target);
    }

    private static Dictionary<string, (Reader, double)> BuildRules()
    {
        var rules = new Dictionary<string, (Reader, double)>(StringComparer.Ordinal);

        void Add(string id, double target, Reader read) => rules[id] = (read, target);
        void Hours(string id, double targetMinutes, Func<AchievementProgress, double> minutes)
            => Add(id, targetMinutes / 60.0, (p, _) => minutes(p) / 60.0);

        // ---- progression: the level milestones, straight off the tracker's own list ----
        foreach (var (id, level) in AchievementService.LevelMilestones)
            Add(id, level, (_, lvl) => lvl);
        Add("window_shopping", AchievementService.WindowShoppingPointsSpent, (p, _) => p.LifetimeSkillPointsSpent);

        // ---- time & sessions ----
        Hours("rose_tinted_reality", AchievementService.RoseTintedPinkFilterMinutes, p => p.TotalPinkFilterMinutes);
        Hours("threadbare", AchievementService.ThreadbareSpiralMinutes, p => p.TotalSpiralMinutes);
        Hours("screen_time", AchievementService.ScreenTimeVideoMinutes, p => p.TotalVideoMinutes);
        Add("daily_maintenance", AchievementService.DailyMaintenanceConsecutiveDays, (p, _) => p.ConsecutiveDays);
        Add("thirty_day_doll", AchievementService.ThirtyDayDollConsecutiveDays, (p, _) => p.ConsecutiveDays);
        Add("retinal_burn", AchievementService.RetinalBurnFlashImages, (p, _) => p.TotalFlashImages);
        Add("pavlov", GamificationBridge.PavlovKeywordTriggers, (p, _) => p.KeywordTriggersFired);

        // ---- minigames & skill ----
        Add("mathematicians_nightmare", AchievementService.MathematiciansNightmareStreak, (p, _) => p.BubbleCountCorrectStreak);
        Add("pop_the_thought", AchievementService.PopTheThoughtBubbles, (p, _) => p.TotalBubblesPopped);
        Add("word_perfect", AchievementService.WordPerfectLockCards, (p, _) => p.TotalLockCardsCompleted);
        Add("eyes_front", AchievementService.EyesFrontAttentionChecks, (p, _) => p.TotalAttentionChecksPassed);
        Add("mercy_beggar", AchievementService.MercyBeggarFailures, (p, _) => p.AttentionCheckFailures);
        Add("pillow_talk", GamificationBridge.PillowTalkMessages, (p, _) => p.CompanionMessages);
        Add("blink_and_youll_miss_it", GamificationBridge.BlinkAndYoullMissItBlinks, (p, _) => p.BlinkTrainerBlinks);
        Add("teachers_pet", GamificationBridge.TeachersPetPasses, (p, _) => p.QuizzesPassed);
        Add("honor_roll", GamificationBridge.HonorRollCategories, (p, _) => p.PerfectedQuizCategories.Count);
        Add("hands_free", GamificationBridge.HandsFreeGazePops, (p, _) => p.GazePops);

        // ---- deeper ----
        Add("down_the_rabbit_hole", GamificationBridge.DownTheRabbitHolePlays, (p, _) => p.EnhancementsPlayed);
        Hours("permanent_resident", AchievementService.PermanentResidentDeeperMinutes, p => p.DeeperMinutes);

        // ---- creator ----
        Add("curator", GamificationBridge.CuratorDistinctMods, (p, _) => p.ActivatedModIds.Count);
        Add("community_supported", GamificationBridge.CommunityModsCount, (p, _) => p.CommunityModIds.Count);

        return rules;
    }
}
