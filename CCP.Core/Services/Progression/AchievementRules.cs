namespace ConditioningControlPanel.Services;

/// <summary>
/// Every achievement unlock threshold, in one place. <c>AchievementService</c> and
/// <c>GamificationBridge</c> (WPF head) branch on these and <see cref="AchievementMeters"/> draws
/// the gallery bars from them, so a head can never hold a second copy that drifts.
/// Values lifted verbatim from AchievementService.cs:59-119 and GamificationBridge.cs:27-47.
/// </summary>
internal static class AchievementRules
{
    // ===== Cumulative-counter thresholds =====
    // Named so the number the tracker branches on and the number the achievement's Requirement
    // string promises the user can be pinned together by a test instead of drifting apart.

    /// <summary>"screen_time" — 10 cumulative hours of mandatory video.</summary>
    internal const double ScreenTimeVideoMinutes = 600;

    /// <summary>"threadbare" — 10 cumulative hours under the spiral.</summary>
    internal const double ThreadbareSpiralMinutes = 600;

    /// <summary>"eyes_front" — attention checks passed, lifetime.</summary>
    internal const int EyesFrontAttentionChecks = 100;

    /// <summary>"word_perfect" — lock cards completed, lifetime.</summary>
    internal const int WordPerfectLockCards = 50;

    /// <summary>"thirty_day_doll" — consecutive launch days.</summary>
    internal const int ThirtyDayDollConsecutiveDays = 30;

    /// <summary>"window_shopping" — sparkle points spent, lifetime (the Prestige metric).</summary>
    internal const long WindowShoppingPointsSpent = 100;

    // The rest of the countable thresholds, pulled out of the trackers so the gallery meter
    // (AchievementMeters) reads the number the tracker branches on, not a copy of it.

    /// <summary>"rose_tinted_reality" - 10 cumulative hours of pink filter.</summary>
    internal const double RoseTintedPinkFilterMinutes = 600;

    /// <summary>"permanent_resident" - 10 cumulative hours in the Deeper player.</summary>
    internal const double PermanentResidentDeeperMinutes = 600;

    /// <summary>"daily_maintenance" - consecutive launch days.</summary>
    internal const int DailyMaintenanceConsecutiveDays = 7;

    /// <summary>"retinal_burn" - flash images shown, lifetime.</summary>
    internal const int RetinalBurnFlashImages = 5000;

    /// <summary>"pop_the_thought" - bubbles popped, lifetime.</summary>
    internal const int PopTheThoughtBubbles = 1000;

    /// <summary>"mathematicians_nightmare" - correct bubble counts in a row.</summary>
    internal const int MathematiciansNightmareStreak = 5;

    /// <summary>"mercy_beggar" - attention checks failed, lifetime.</summary>
    internal const int MercyBeggarFailures = 3;

    /// <summary>
    /// The level milestones, lowest first. AchievementService.CheckLevelAchievements walks this list
    /// and the gallery meter reads it, so the two cannot disagree.
    /// </summary>
    internal static readonly (string Id, int Level)[] LevelMilestones =
    {
        ("plastic_initiation", 10),
        ("dumb_bimbo", 20),
        ("fully_synthetic", 50),
        ("docile_cow", 75),
        ("perfect_plastic_puppet", 100),
        ("brainwashed_slavedoll", 125),
        ("platinum_puppet", 150),
    };

    // --- tunable thresholds (chosen here, flagged for review) ---
    // GamificationBridge branches on these; the lifetime-counter bars are also read by
    // AchievementMeters, so the card and the unlock share one number.
    internal const int BestFriendsCompanionLevel = 25;   // "reach a companion level milestone"
    internal const int PillowTalkMessages = 100;         // "exchange 100 messages"
    internal const int PavlovKeywordTriggers = 500;      // "fire 500 keyword triggers"
    internal const int CuratorDistinctMods = 10;         // "activate 10 different mods"
    internal const int MadScientistRules = 5;             // "build using 5+ triggers" (Rules)
    internal const int PuppetStringsCommands = 100;        // "100 remote commands in one session"
    internal const int ThrowAwayKeyMinutes = 60;           // "60+ minute lockdown"
    internal const int CommunityModsCount = 3;            // "activate 3 community mods"
    internal const int DownTheRabbitHolePlays = 25;       // "play 25 enhancements"
    internal const int OnRailsTriggerTypes = 5;            // "5+ distinct trigger types"
    internal const int HandsFreeGazePops = 50;            // "pop 50 bubbles by gaze"
    internal const int HonorRollCategories = 3;           // "top marks in 3 different categories"
    internal const int BlinkAndYoullMissItBlinks = 100;   // "log 100 blinks in the Blink Trainer"
    // 25 -> 10 with the quiz retired: an intake is a 20+ minute banded descent, not a 10-question
    // quiz, so 25 of them was a different order of ask than the requirement text implied.
    internal const int TeachersPetPasses = 10;            // "pass 10 graded runs"
    internal const int HeldBackFailStreak = 3;             // "fail 3 in a row" (classic quiz only)
    internal const int HeldBackQuitStreak = 3;             // "quit 3 intakes early" (the live path)
}
