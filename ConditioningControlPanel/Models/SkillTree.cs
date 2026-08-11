using System.Collections.Generic;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Models;

/// <summary>
/// Defines a skill in the bimbo enhancement tree
/// </summary>
public class SkillDefinition
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Icon { get; set; } = "";
    public string FlavorText { get; set; } = "";
    public string Description { get; set; } = "";
    public int Tier { get; set; }
    public int Cost { get; set; }
    public string? PrerequisiteId { get; set; }
    public bool IsSecret { get; set; }
    public string? SecretRequirementDesc { get; set; }

    /// <summary>
    /// Permanent skills survive the seasonal reset (stat/analytics displays — the
    /// "non-mechanical" nodes). Non-permanent (mechanical/XP-economy) skills are
    /// removed by the server at season rollover and must be re-purchased.
    /// Derived from <see cref="PermanentIds"/>, which must mirror the server's
    /// authoritative PERMANENT_SKILL_IDS list.
    /// </summary>
    public bool IsPermanent => PermanentIds.Contains(Id);

    /// <summary>
    /// Effect type identifier for applying the skill's bonus
    /// </summary>
    public SkillEffectType EffectType { get; set; }

    /// <summary>
    /// Numeric value for the effect (e.g., 0.10 for 10% XP boost)
    /// </summary>
    public double EffectValue { get; set; }

    /// <summary>Localized skill name (falls back to hardcoded Name)</summary>
    public string LocalizedName => Loc.Get($"skill_{Id}_name");
    /// <summary>Localized skill description (falls back to hardcoded Description)</summary>
    public string LocalizedDescription => Loc.Get($"skill_{Id}_desc");
    /// <summary>Localized skill flavor text (falls back to hardcoded FlavorText)</summary>
    public string LocalizedFlavorText => Loc.Get($"skill_{Id}_flavor");

    /// <summary>
    /// Localized "how do I reveal this?" line for a secret skill. Three-step fallback, and the
    /// order matters: the per-skill key, then <see cref="SecretRequirementDesc"/> (the English
    /// source of truth), then the generic unknown-requirement string. Loc.Get echoes the key back
    /// when nothing resolves, so the identity check is what stops a card from rendering the raw
    /// key "rf_skill_night_shift_req" at a user - which is strictly worse than English prose.
    /// </summary>
    public string LocalizedSecretRequirementDesc
    {
        get
        {
            if (!IsSecret) return SecretRequirementDesc ?? string.Empty;

            var key = $"rf_skill_{Id}_req";
            var text = Loc.Get(key);
            if (!string.IsNullOrEmpty(text) && text != key) return text;

            return SecretRequirementDesc ?? Loc.Get("label_secret_skill_unknown_req");
        }
    }

    /// <summary>Ids of all permanent (season-persistent) skills.</summary>
    public static IReadOnlyList<string> PermanentIds { get; } =
        new List<string>
        {
            "pink_hours", "ditzy_data", "hive_mind", "trophy_case", "popular_girl", "eternal_doll",
            "ditzy_data_pro", "season_rewind", "bestie_records", "brain_drain_report", "certified_data_bimbo"
        };

    /// <summary>
    /// All skill definitions in the bimbo enhancement tree
    /// </summary>
    public static readonly List<SkillDefinition> All = new()
    {
        // ═══════════════════════════════════════════════════════════════
        // TIER 1 - Foundation (2 points)
        // ═══════════════════════════════════════════════════════════════
        new()
        {
            Id = "pink_hours",
            Name = "Pink Hours",
            Icon = "⏱️💕",
            Tier = 1,
            Cost = 2,
            FlavorText = "Like, how long have you been getting all pink and pretty? Every second makes your brain more bubbly~",
            Description = "Shows total conditioning time across all sessions",
            EffectType = SkillEffectType.StatDisplay,
            EffectValue = 0
        },

        // ═══════════════════════════════════════════════════════════════
        // TIER 2 - Core Branches (5-8 points)
        // ═══════════════════════════════════════════════════════════════
        new()
        {
            Id = "ditzy_data",
            Name = "Ditzy Data",
            Icon = "📊💭",
            Tier = 2,
            Cost = 5,
            PrerequisiteId = "pink_hours",
            FlavorText = "Numbers are like, SO hard... but these ones are pretty! See all your bimbo stats in one adorable place~",
            Description = "Unlocks statistics panel with session data",
            EffectType = SkillEffectType.StatDisplay,
            EffectValue = 0
        },
        new()
        {
            Id = "sparkle_boost_1",
            Name = "Sparkle Boost",
            Icon = "✨⚡",
            Tier = 2,
            Cost = 8,
            PrerequisiteId = "pink_hours",
            FlavorText = "Good girls deserve extra sparkles! You're just THAT special, sweetie~",
            Description = "+10% XP from all sources. Adds pink glow to flashes and sparkle particles on bubble pops",
            EffectType = SkillEffectType.XpMultiplier,
            EffectValue = 0.10
        },
        new()
        {
            Id = "good_girl_streak",
            Name = "Good Girl Streak",
            Icon = "🔥💖",
            Tier = 2,
            Cost = 5,
            PrerequisiteId = "pink_hours",
            FlavorText = "Being obedient every single day? That deserves protection! One free oopsie per week~",
            Description = "Enhanced streak display + 1 weekly streak shield",
            EffectType = SkillEffectType.StreakShield,
            EffectValue = 1
        },

        // ═══════════════════════════════════════════════════════════════
        // TIER 3 - Specialization (10-15 points)
        // ═══════════════════════════════════════════════════════════════
        new()
        {
            Id = "hive_mind",
            Name = "Hive Mind",
            Icon = "👯‍♀️💕",
            Tier = 3,
            Cost = 10,
            PrerequisiteId = "ditzy_data",
            FlavorText = "See how many other bimbos are conditioning RIGHT NOW! You're never alone in your journey to empty~",
            Description = "Shows live online user count",
            EffectType = SkillEffectType.StatDisplay,
            EffectValue = 0
        },
        new()
        {
            Id = "trophy_case",
            Name = "Trophy Case",
            Icon = "🏆✨",
            Tier = 3,
            Cost = 10,
            PrerequisiteId = "hive_mind",
            FlavorText = "Look at all your pretty accomplishments! Longest session, biggest streak... you're doing SO well!",
            Description = "Shows personal best records",
            EffectType = SkillEffectType.StatDisplay,
            EffectValue = 0
        },
        new()
        {
            Id = "sparkle_boost_2",
            Name = "Extra Sparkly",
            Icon = "💎⚡",
            Tier = 3,
            Cost = 15,
            PrerequisiteId = "sparkle_boost_1",
            FlavorText = "Even MORE sparkles?! +15% extra (25% total!) You're practically GLOWING~",
            Description = "+15% more XP (stacks to 25%). Stronger pink glow on flashes",
            EffectType = SkillEffectType.XpMultiplier,
            EffectValue = 0.15
        },
        new()
        {
            Id = "lucky_bimbo",
            Name = "Lucky Bimbo",
            Icon = "🍀💋",
            Tier = 3,
            Cost = 15,
            PrerequisiteId = "sparkle_boost_2",
            FlavorText = "Being an airhead pays off sometimes! 5% chance any flash gives 10x XP~ Tee-hee!",
            Description = "5% chance for 10x XP on flash images with golden pulsing glow",
            EffectType = SkillEffectType.LuckyFlash,
            EffectValue = 0.05
        },
        new()
        {
            Id = "milestone_rewards",
            Name = "Milestone Rewards",
            Icon = "🎁🎀",
            Tier = 3,
            Cost = 15,
            PrerequisiteId = "good_girl_streak",
            FlavorText = "Good girls who show up every day deserve a treat~ The longer your streak, the bigger your daily welcome bonus!",
            Description = "Daily XP: 50 (1-3d) / 100 (4-6d) / 150 (7-13d) / 200 (14-29d) / 300 (30+d) — scales +3%/level",
            EffectType = SkillEffectType.StreakMilestones,
            EffectValue = 0
        },
        new()
        {
            Id = "oopsie_insurance",
            Name = "Oopsie Insurance",
            Icon = "💕🩹",
            Tier = 3,
            Cost = 12,
            PrerequisiteId = "milestone_rewards",
            FlavorText = "Everyone forgets sometimes, silly! Your streak fixes get spent for you, automatically~",
            Description = "Grants a streak fix on purchase, then spends them automatically when your login streak breaks",
            EffectType = SkillEffectType.StreakRecovery,
            EffectValue = 500
        },

        // ═══════════════════════════════════════════════════════════════
        // TIER 4 - Mastery (15-25 points)
        // ═══════════════════════════════════════════════════════════════
        new()
        {
            Id = "popular_girl",
            Name = "Popular Girl",
            Icon = "👑💅",
            Tier = 4,
            Cost = 15,
            PrerequisiteId = "trophy_case",
            FlavorText = "OMG find out how pretty you are compared to everyone! Top X%... are you the PRETTIEST?",
            Description = "Shows your rank percentile",
            EffectType = SkillEffectType.StatDisplay,
            EffectValue = 0
        },
        new()
        {
            Id = "quest_refresh",
            Name = "Quest Refresh",
            Icon = "🔄💫",
            Tier = 4,
            Cost = 15,
            PrerequisiteId = "popular_girl",
            FlavorText = "Don't like your quest? Swap it for free once a day! Good bimbos get choices~",
            Description = "1 free daily quest reroll",
            EffectType = SkillEffectType.FreeReroll,
            EffectValue = 1
        },
        new()
        {
            Id = "better_quests",
            Name = "Better Quests",
            Icon = "✨📜",
            Tier = 4,
            Cost = 20,
            PrerequisiteId = "quest_refresh",
            FlavorText = "Your rerolled quests are extra rewarding now! +25% XP on any quest you refresh~",
            Description = "+25% XP on rerolled quests",
            EffectType = SkillEffectType.RerollBonus,
            EffectValue = 0.25
        },
        new()
        {
            Id = "sparkle_boost_3",
            Name = "Maximum Sparkle",
            Icon = "💖👸",
            Tier = 4,
            Cost = 25,
            PrerequisiteId = "lucky_bimbo",
            FlavorText = "THE MOST SPARKLES POSSIBLE! +20% more (45% total!) You're basically made of glitter now~",
            Description = "+20% more XP (stacks to 45%). Maximum pink glow on flashes",
            EffectType = SkillEffectType.XpMultiplier,
            EffectValue = 0.20
        },
        new()
        {
            Id = "lucky_bubbles",
            Name = "Lucky Bubbles",
            Icon = "🫧🎰",
            Tier = 4,
            Cost = 20,
            PrerequisiteId = "sparkle_boost_3",
            FlavorText = "Pop pop POP! 5% chance bubbles give 20x points! Empty heads LOVE bubbles~",
            Description = "5% chance for 20x bubble points with golden sparkle burst",
            EffectType = SkillEffectType.LuckyBubble,
            EffectValue = 0.05
        },
        new()
        {
            Id = "pink_rush",
            Name = "Pink Rush",
            Icon = "⚡💗",
            Tier = 4,
            Cost = 25,
            PrerequisiteId = "lucky_bubbles",
            FlavorText = "Random 60-second PINK EMERGENCY! 3x XP while your brain goes completely cotton candy~",
            Description = "Random 60-sec windows of 3x XP",
            EffectType = SkillEffectType.PinkRush,
            EffectValue = 3.0
        },
        new()
        {
            Id = "streak_power",
            Name = "Streak Power",
            Icon = "💪🔥",
            Tier = 4,
            Cost = 20,
            PrerequisiteId = "oopsie_insurance",
            FlavorText = "Each day you're good adds +0.5% XP! At 30 days that's +15%! Consistency is SO hot~",
            Description = "+0.5% XP per streak day (max 15%)",
            EffectType = SkillEffectType.StreakMultiplier,
            EffectValue = 0.005
        },
        new()
        {
            Id = "reroll_addict",
            Name = "Reroll Addict",
            Icon = "🎰💕",
            Tier = 4,
            Cost = 15,
            PrerequisiteId = "streak_power",
            FlavorText = "Can't stop rerolling? Now you have 2 EXTRA rerolls every day! Spin spin spin~",
            Description = "+2 extra daily quest rerolls",
            EffectType = SkillEffectType.ExtraRerolls,
            EffectValue = 2
        },
        new()
        {
            Id = "perfect_bimbo_week",
            Name = "Perfect Bimbo Week",
            Icon = "⭐👼",
            Tier = 4,
            Cost = 20,
            PrerequisiteId = "reroll_addict",
            FlavorText = "Presents for persistent princesses! Earn huge XP bonuses at 7, 14, and 30 day daily quest streaks! ✨",
            Description = "3000/6000/10000 XP at 7/14/30 day streaks (scales with level +2%/lv)",
            EffectType = SkillEffectType.PerfectWeek,
            EffectValue = 3000
        },

        // ═══════════════════════════════════════════════════════════════
        // SECRET TIER - Hidden (8-10 points)
        // ═══════════════════════════════════════════════════════════════
        new()
        {
            Id = "night_shift",
            Name = "Night Shift",
            Icon = "🌙😴",
            Tier = 5,
            Cost = 10,
            IsSecret = true,
            SecretRequirementDesc = "Use the app after midnight 10 times",
            FlavorText = "Good bimbos condition when they should be sleeping~ The pink thoughts never stop!",
            Description = "+50% XP between 11pm-5am",
            EffectType = SkillEffectType.TimeBonus,
            EffectValue = 0.50
        },
        new()
        {
            Id = "early_bird_bimbo",
            Name = "Early Bird Bimbo",
            Icon = "🌅☀️",
            Tier = 5,
            Cost = 10,
            IsSecret = true,
            SecretRequirementDesc = "Use the app before 7am 10 times",
            FlavorText = "Starting your day with programming? Morning conditioning hits DIFFERENT~",
            Description = "+50% XP between 5am-8am",
            EffectType = SkillEffectType.TimeBonus,
            EffectValue = 0.50
        },
        new()
        {
            Id = "eternal_doll",
            Name = "Eternal Doll",
            Icon = "💎♾️",
            Tier = 5,
            Cost = 8,
            IsSecret = true,
            SecretRequirementDesc = "Reach level 50 in any season",
            FlavorText = "Your dedication is FOREVER. Lifetime stats that never reset... you've always been a bimbo~",
            Description = "Shows lifetime stats across all seasons",
            EffectType = SkillEffectType.LifetimeStats,
            EffectValue = 0
        },

        // ═══════════════════════════════════════════════════════════════
        // TIER 6 - Ditzy Data PRO (analytics branch, permanent, 150-1000 points —
        // priced as the long-game sink for banked veteran balances)
        // ═══════════════════════════════════════════════════════════════
        new()
        {
            Id = "ditzy_data_pro",
            Name = "Ditzy Data PRO",
            Icon = "📈👛",
            Tier = 6,
            Cost = 150,
            PrerequisiteId = "better_quests",
            FlavorText = "Regular numbers are for regular girls. YOUR numbers get a whole upgrade, you spreadsheet princess~",
            Description = "Unlocks the PRO lifetime dashboard with every stat the app has ever counted",
            EffectType = SkillEffectType.StatDisplay,
            EffectValue = 0
        },
        new()
        {
            Id = "season_rewind",
            Name = "Season Rewind",
            Icon = "⏪💭",
            Tier = 6,
            Cost = 250,
            PrerequisiteId = "ditzy_data_pro",
            FlavorText = "Wanna see how much emptier you got since last month? The graphs remember even when you don't~",
            Description = "Season-over-season comparison of your past seasons",
            EffectType = SkillEffectType.StatDisplay,
            EffectValue = 0
        },
        new()
        {
            Id = "bestie_records",
            Name = "Bestie Records",
            Icon = "🏅💖",
            Tier = 6,
            Cost = 350,
            PrerequisiteId = "season_rewind",
            FlavorText = "Every record you break makes the old you SO jealous. Keep beating her~",
            Description = "Personal best timeline: all-time records plus each season's peaks",
            EffectType = SkillEffectType.StatDisplay,
            EffectValue = 0
        },
        new()
        {
            Id = "brain_drain_report",
            Name = "Brain Drain Report",
            Icon = "🧠📉",
            Tier = 6,
            Cost = 500,
            PrerequisiteId = "bestie_records",
            FlavorText = "A full report on exactly which toys melted you the most. For science! Giggle.",
            Description = "Per-feature usage breakdown, this season and lifetime",
            EffectType = SkillEffectType.StatDisplay,
            EffectValue = 0
        },
        new()
        {
            Id = "certified_data_bimbo",
            Name = "Certified Data Bimbo",
            Icon = "🎓✨",
            Tier = 6,
            Cost = 1000,
            PrerequisiteId = "brain_drain_report",
            FlavorText = "You looked at ALL the numbers and understood, like, none of them. Certified!!",
            Description = "Capstone: Prestige history per season plus a certified badge on your tree",
            EffectType = SkillEffectType.StatDisplay,
            EffectValue = 0
        }
    };
}

/// <summary>
/// Types of effects that skills can have
/// </summary>
public enum SkillEffectType
{
    /// <summary>Displays a statistic in the Enhancements tab</summary>
    StatDisplay,

    /// <summary>Adds to the XP multiplier</summary>
    XpMultiplier,

    /// <summary>Provides weekly streak shield</summary>
    StreakShield,

    /// <summary>Grants XP at streak milestones</summary>
    StreakMilestones,

    /// <summary>Allows streak recovery for XP cost</summary>
    StreakRecovery,

    /// <summary>Chance for bonus XP on flash images</summary>
    LuckyFlash,

    /// <summary>Chance for bonus points on bubbles</summary>
    LuckyBubble,

    /// <summary>Random temporary XP boost windows</summary>
    PinkRush,

    /// <summary>XP multiplier based on streak length</summary>
    StreakMultiplier,

    /// <summary>Free quest rerolls per day</summary>
    FreeReroll,

    /// <summary>Additional quest rerolls per day</summary>
    ExtraRerolls,

    /// <summary>Bonus XP on rerolled quests</summary>
    RerollBonus,

    /// <summary>Bonus for completing daily quests 7 days straight</summary>
    PerfectWeek,

    /// <summary>XP bonus during certain hours</summary>
    TimeBonus,

    /// <summary>Shows lifetime stats that persist across seasons</summary>
    LifetimeStats
}
