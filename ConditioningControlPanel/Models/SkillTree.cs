using System.Collections.Generic;

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
    /// Effect type identifier for applying the skill's bonus
    /// </summary>
    public SkillEffectType EffectType { get; set; }

    /// <summary>
    /// Numeric value for the effect (e.g., 0.10 for 10% XP boost)
    /// </summary>
    public double EffectValue { get; set; }

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
            Description = "+10% XP from all sources",
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
            PrerequisiteId = "ditzy_data",
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
            Description = "+15% more XP (stacks to 25%)",
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
            PrerequisiteId = "sparkle_boost_1",
            FlavorText = "Being an airhead pays off sometimes! 5% chance any flash gives 5x XP~ Tee-hee!",
            Description = "5% chance for 5x XP on flash images",
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
            PrerequisiteId = "good_girl_streak",
            FlavorText = "Everyone forgets sometimes, silly! Pay 500 XP to fix a broken streak once per season~",
            Description = "Restore broken streak for 500 XP (once per season)",
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
            PrerequisiteId = "hive_mind",
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
            PrerequisiteId = "trophy_case",
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
            PrerequisiteId = "trophy_case",
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
            PrerequisiteId = "sparkle_boost_2",
            FlavorText = "THE MOST SPARKLES POSSIBLE! +20% more (45% total!) You're basically made of glitter now~",
            Description = "+20% more XP (stacks to 45%)",
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
            PrerequisiteId = "lucky_bimbo",
            FlavorText = "Pop pop POP! 5% chance bubbles give 10x points! Empty heads LOVE bubbles~",
            Description = "5% chance for 10x bubble points",
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
            PrerequisiteId = "lucky_bimbo",
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
            PrerequisiteId = "milestone_rewards",
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
            PrerequisiteId = "milestone_rewards",
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
            PrerequisiteId = "oopsie_insurance",
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
