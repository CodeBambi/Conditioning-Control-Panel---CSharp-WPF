using System;
using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Localization;
using Newtonsoft.Json;

namespace ConditioningControlPanel.Models;

public enum QuestType
{
    Daily,
    Weekly
}

public enum QuestCategory
{
    Flash,          // View flash images
    Video,          // Watch video minutes
    Spiral,         // Use spiral overlay
    PinkFilter,     // Use pink filter
    Bubbles,        // Pop bubbles
    LockCard,       // Complete lock cards
    Session,        // Complete sessions
    Streak,         // Daily streak
    BubbleCount,    // Bubble count minigame
    Mantra,         // Complete mantras
    Combined,       // Multiple activities (overlay time, XP earned)

    // Patreon-exclusive categories (quests carrying these set RequiresPremium = true)
    Autonomy,       // Minutes Bambi Takeover (autonomy) is running
    Lockdown,       // Lockdowns completed
    Remote,         // Remote-control commands received
    KeywordTrigger, // Keyword/OCR triggers fired
    BlinkTrainer    // Blinks logged in the live blink trainer
}

public class QuestDefinition
{
    [JsonProperty("id")]
    public string Id { get; set; } = "";

    [JsonProperty("name")]
    public string Name { get; set; } = "";

    [JsonProperty("description")]
    public string Description { get; set; } = "";

    [JsonProperty("type")]
    public QuestType Type { get; set; }

    [JsonProperty("category")]
    public QuestCategory Category { get; set; }

    [JsonProperty("targetValue")]
    public int TargetValue { get; set; }

    [JsonProperty("xpReward")]
    public int XPReward { get; set; }

    [JsonProperty("icon")]
    public string Icon { get; set; } = "";

    /// <summary>Localized quest name (falls back to hardcoded Name)</summary>
    [JsonIgnore]
    public string LocalizedName => Loc.Get($"quest_{Id}_name");
    /// <summary>Localized quest description (falls back to hardcoded Description)</summary>
    [JsonIgnore]
    public string LocalizedDescription => Loc.Get($"quest_{Id}_desc");

    /// <summary>
    /// Local embedded image path (pack://application:,,,/Resources/...)
    /// Used as fallback when ImageUrl is not available
    /// </summary>
    [JsonProperty("imagePath")]
    public string ImagePath { get; set; } = "";

    /// <summary>
    /// Remote image URL (e.g., https://bambi-cdn.b-cdn.net/quests/...)
    /// Takes precedence over ImagePath when available
    /// </summary>
    [JsonProperty("imageUrl")]
    public string? ImageUrl { get; set; }

    /// <summary>
    /// Whether this quest requires Patreon premium access. Premium quests are filtered
    /// out of the generation pool for non-premium users (see QuestService.GenerateNew*Quest)
    /// so a free user never rolls a quest they can't complete.
    /// </summary>
    [JsonProperty("requiresPremium")]
    public bool RequiresPremium { get; set; }

    /// <summary>
    /// Whether this is a seasonal quest (temporary/event-based)
    /// </summary>
    [JsonProperty("seasonal")]
    public bool IsSeasonal { get; set; }

    /// <summary>
    /// Start date for seasonal quests (YYYY-MM-DD format)
    /// </summary>
    [JsonProperty("activeFrom")]
    public string? ActiveFrom { get; set; }

    /// <summary>
    /// End date for seasonal quests (YYYY-MM-DD format)
    /// </summary>
    [JsonProperty("activeUntil")]
    public string? ActiveUntil { get; set; }

    /// <summary>
    /// Local cached path for the quest image (set by QuestDefinitionService)
    /// </summary>
    [JsonIgnore]
    public string? CachedImagePath { get; set; }

    /// <summary>
    /// Gets the best available image path (cached remote > remote URL > local embedded)
    /// </summary>
    [JsonIgnore]
    public string EffectiveImagePath
    {
        get
        {
            // Prefer cached local copy of remote image
            if (!string.IsNullOrEmpty(CachedImagePath) && System.IO.File.Exists(CachedImagePath))
                return CachedImagePath;

            // Fall back to embedded resource
            return ImagePath;
        }
    }

    public QuestDefinition() { }

    public QuestDefinition(string id, string name, string description, QuestType type,
        QuestCategory category, int target, int xpReward, string icon, string imagePath = "",
        bool requiresPremium = false)
    {
        Id = id;
        Name = name;
        Description = description;
        Type = type;
        Category = category;
        TargetValue = target;
        XPReward = xpReward;
        Icon = icon;
        ImagePath = imagePath;
        RequiresPremium = requiresPremium;
    }

    /// <summary>
    /// Parse QuestCategory from server string (case-insensitive)
    /// </summary>
    public static QuestCategory ParseCategory(string category)
    {
        return category?.ToLowerInvariant() switch
        {
            "flash" => QuestCategory.Flash,
            "video" => QuestCategory.Video,
            "spiral" => QuestCategory.Spiral,
            "pinkfilter" => QuestCategory.PinkFilter,
            "bubbles" => QuestCategory.Bubbles,
            "lockcard" => QuestCategory.LockCard,
            "session" => QuestCategory.Session,
            "streak" => QuestCategory.Streak,
            "bubblecount" => QuestCategory.BubbleCount,
            "mantra" => QuestCategory.Mantra,
            "combined" => QuestCategory.Combined,
            "autonomy" => QuestCategory.Autonomy,
            "lockdown" => QuestCategory.Lockdown,
            "remote" => QuestCategory.Remote,
            "keyword" => QuestCategory.KeywordTrigger,
            "keywordtrigger" => QuestCategory.KeywordTrigger,
            "blink" => QuestCategory.BlinkTrainer,
            "blinktrainer" => QuestCategory.BlinkTrainer,
            _ => QuestCategory.Combined
        };
    }

    /// <summary>
    /// Parse QuestType from server string (case-insensitive)
    /// </summary>
    public static QuestType ParseType(string type)
    {
        return type?.ToLowerInvariant() switch
        {
            "weekly" => QuestType.Weekly,
            _ => QuestType.Daily
        };
    }

    /// <summary>
    /// All available daily quests
    /// </summary>
    public static readonly List<QuestDefinition> DailyQuests = new()
    {
        // --- FREE (non-gated features) ---
        new("flash_rush_d", "Flash Rush", "View 40 flash images", QuestType.Daily, QuestCategory.Flash, 40, 130, "\u26A1", "pack://application:,,,/Resources/quests/flash_rush_d.png"),
        new("bimbo_basics_d", "Bimbo Basics", "View 25 flash images", QuestType.Daily, QuestCategory.Flash, 25, 100, "\u2728", "pack://application:,,,/Resources/quests/bimbo_basics_d.png"),
        new("spiral_sink_d", "Spiral Sink", "Spend 12 minutes with spiral overlay", QuestType.Daily, QuestCategory.Spiral, 12, 200, "\uD83C\uDF00", "pack://application:,,,/Resources/quests/spiral_sink_d.png"),
        new("pink_haze_d", "Pink Haze", "Use pink filter for 15 minutes", QuestType.Daily, QuestCategory.PinkFilter, 15, 175, "\uD83D\uDC97", "pack://application:,,,/Resources/quests/pink_haze_d.png"),
        new("pop_parade_d", "Pop Parade", "Pop 40 bubbles", QuestType.Daily, QuestCategory.Bubbles, 40, 150, "\uD83E\uDEE7", "pack://application:,,,/Resources/quests/pop_parade_d.png"),
        new("screen_trance_d", "Screen Trance", "Watch 12 minutes of video", QuestType.Daily, QuestCategory.Video, 12, 200, "\uD83C\uDFAC", "pack://application:,,,/Resources/quests/screen_trance_d.png"),
        new("daily_devotion_d", "Daily Devotion", "Complete 1 session", QuestType.Daily, QuestCategory.Session, 1, 250, "\uD83D\uDE4F", "pack://application:,,,/Resources/quests/daily_devotion_d.png"),
        new("lock_it_in_d", "Lock It In", "Complete 2 lock cards", QuestType.Daily, QuestCategory.LockCard, 2, 200, "\uD83D\uDD12", "pack://application:,,,/Resources/quests/lock_it_in_d.png"),
        new("count_along_d", "Count Along", "Finish 2 bubble count games", QuestType.Daily, QuestCategory.BubbleCount, 2, 175, "\uD83C\uDFAF", "pack://application:,,,/Resources/quests/count_along_d.png"),
        new("soft_static_d", "Soft Static", "Spend 25 minutes with any overlay active", QuestType.Daily, QuestCategory.Combined, 25, 175, "\uD83E\uDDE0", "pack://application:,,,/Resources/quests/soft_static_d.png"),

        // --- PATREON (exclusive features, RequiresPremium) ---
        new("takeover_drift_d", "Hands Off", "Let Bambi Takeover run for 15 minutes", QuestType.Daily, QuestCategory.Autonomy, 15, 250, "\uD83C\uDF80", "pack://application:,,,/Resources/quests/takeover_drift_d.png", true),
        new("takeover_deep_d", "On Autopilot", "Let Bambi Takeover run for 25 minutes", QuestType.Daily, QuestCategory.Autonomy, 25, 350, "\uD83C\uDF80", "pack://application:,,,/Resources/quests/takeover_deep_d.png", true),
        new("takeover_full_d", "Surrender", "Let Bambi Takeover run for 40 minutes", QuestType.Daily, QuestCategory.Autonomy, 40, 450, "\uD83C\uDF80", "pack://application:,,,/Resources/quests/takeover_full_d.png", true),
        new("locked_away_d", "Locked Away", "Complete 1 lockdown", QuestType.Daily, QuestCategory.Lockdown, 1, 300, "\u26D3", "pack://application:,,,/Resources/quests/locked_away_d.png", true),
        new("trigger_words_d", "Trigger Words", "Fire 15 keyword triggers", QuestType.Daily, QuestCategory.KeywordTrigger, 15, 200, "\uD83D\uDC41", "pack://application:,,,/Resources/quests/trigger_words_d.png", true),
        new("word_slave_d", "Word Slave", "Fire 30 keyword triggers", QuestType.Daily, QuestCategory.KeywordTrigger, 30, 300, "\uD83D\uDC41", "pack://application:,,,/Resources/quests/word_slave_d.png", true),
        new("handed_over_d", "Hand Over Control", "Take 25 remote commands", QuestType.Daily, QuestCategory.Remote, 25, 250, "\uD83D\uDCE1", "pack://application:,,,/Resources/quests/handed_over_d.png", true),
        new("remote_hands_d", "Remote Hands", "Take 50 remote commands", QuestType.Daily, QuestCategory.Remote, 50, 350, "\uD83D\uDCE1", "pack://application:,,,/Resources/quests/remote_hands_d.png", true),
        new("blink_drill_d", "Blink Drill", "Log 30 blinks in the live blink trainer", QuestType.Daily, QuestCategory.BlinkTrainer, 30, 200, "\uD83D\uDC40", "pack://application:,,,/Resources/quests/blink_drill_d.png", true),
        new("obedient_eyes_d", "Obedient Eyes", "Log 50 blinks in the live blink trainer", QuestType.Daily, QuestCategory.BlinkTrainer, 50, 300, "\uD83D\uDC40", "pack://application:,,,/Resources/quests/obedient_eyes_d.png", true)
    };

    /// <summary>
    /// All available weekly quests
    /// </summary>
    public static readonly List<QuestDefinition> WeeklyQuests = new()
    {
        // --- FREE (non-gated features) ---
        new("flash_monsoon_w", "Flash Monsoon", "View 600 flash images", QuestType.Weekly, QuestCategory.Flash, 600, 600, "\u26A1", "pack://application:,,,/Resources/quests/flash_monsoon_w.png"),
        new("spiral_descent_w", "Spiral Descent", "Spend 150 minutes with spiral overlay", QuestType.Weekly, QuestCategory.Spiral, 150, 750, "\uD83C\uDF00", "pack://application:,,,/Resources/quests/spiral_descent_w.png"),
        new("pink_world_w", "Pink World", "Use pink filter for 200 minutes", QuestType.Weekly, QuestCategory.PinkFilter, 200, 700, "\uD83D\uDC97", "pack://application:,,,/Resources/quests/pink_world_w.png"),
        new("bubble_storm_w", "Bubble Storm", "Pop 500 bubbles", QuestType.Weekly, QuestCategory.Bubbles, 500, 600, "\uD83C\uDF0A", "pack://application:,,,/Resources/quests/bubble_storm_w.png"),
        new("marathon_trance_w", "Marathon Trance", "Watch 90 minutes of video", QuestType.Weekly, QuestCategory.Video, 90, 800, "\uD83C\uDFAC", "pack://application:,,,/Resources/quests/marathon_trance_w.png"),
        new("weekly_devotion_w", "Weekly Devotion", "Complete 7 sessions", QuestType.Weekly, QuestCategory.Session, 7, 1000, "\uD83D\uDE4F", "pack://application:,,,/Resources/quests/weekly_devotion_w.png"),
        new("phrase_mastery_w", "Phrase Mastery", "Complete 15 lock cards", QuestType.Weekly, QuestCategory.LockCard, 15, 750, "\uD83D\uDD12", "pack://application:,,,/Resources/quests/phrase_mastery_w.png"),
        new("total_submission_w", "Total Submission", "Complete 15 bubble count games", QuestType.Weekly, QuestCategory.BubbleCount, 15, 700, "\uD83C\uDFAF", "pack://application:,,,/Resources/quests/total_submission_w.png"),
        new("streak_keeper_w", "Streak Keeper", "Maintain a 7-day streak", QuestType.Weekly, QuestCategory.Streak, 7, 600, "\uD83D\uDD25", "pack://application:,,,/Resources/quests/streak_keeper_w.png"),
        new("conditioning_champion_w", "Conditioning Champion", "Earn 2000 XP from activities", QuestType.Weekly, QuestCategory.Combined, 2000, 500, "\uD83C\uDFC6", "pack://application:,,,/Resources/quests/conditioning_champion_w.png"),

        // --- PATREON (exclusive features, RequiresPremium) ---
        new("autopilot_week_w", "Set It and Forget It", "Let Bambi Takeover run for 120 minutes this week", QuestType.Weekly, QuestCategory.Autonomy, 120, 900, "\uD83C\uDF80", "pack://application:,,,/Resources/quests/autopilot_week_w.png", true),
        new("always_on_w", "Always On", "Let Bambi Takeover run for 180 minutes this week", QuestType.Weekly, QuestCategory.Autonomy, 180, 1100, "\uD83C\uDF80", "pack://application:,,,/Resources/quests/always_on_w.png", true),
        new("lockdown_habit_w", "Lockdown Habit", "Complete 5 lockdowns", QuestType.Weekly, QuestCategory.Lockdown, 5, 800, "\u26D3", "pack://application:,,,/Resources/quests/lockdown_habit_w.png", true),
        new("throw_away_key_w", "Throw Away the Key", "Complete 7 lockdowns", QuestType.Weekly, QuestCategory.Lockdown, 7, 1000, "\u26D3", "pack://application:,,,/Resources/quests/throw_away_key_w.png", true),
        new("pavlov_w", "Pavlov", "Fire 300 keyword triggers", QuestType.Weekly, QuestCategory.KeywordTrigger, 300, 750, "\uD83D\uDC41", "pack://application:,,,/Resources/quests/pavlov_w.png", true),
        new("word_conditioned_w", "Word-Conditioned", "Fire 1000 keyword triggers", QuestType.Weekly, QuestCategory.KeywordTrigger, 1000, 1200, "\uD83D\uDC41", "pack://application:,,,/Resources/quests/word_conditioned_w.png", true),
        new("puppet_strings_w", "Puppet Strings", "Take 100 remote commands this week", QuestType.Weekly, QuestCategory.Remote, 100, 800, "\uD83D\uDCE1", "pack://application:,,,/Resources/quests/puppet_strings_w.png", true),
        new("fully_remote_w", "Fully Remote", "Take 200 remote commands this week", QuestType.Weekly, QuestCategory.Remote, 200, 1100, "\uD83D\uDCE1", "pack://application:,,,/Resources/quests/fully_remote_w.png", true),
        new("blink_century_w", "Blink Century", "Log 100 blinks in the live blink trainer", QuestType.Weekly, QuestCategory.BlinkTrainer, 100, 700, "\uD83D\uDC40", "pack://application:,,,/Resources/quests/blink_century_w.png", true),
        new("eyes_trained_w", "Eyes Trained", "Log 200 blinks in the live blink trainer", QuestType.Weekly, QuestCategory.BlinkTrainer, 200, 1000, "\uD83D\uDC40", "pack://application:,,,/Resources/quests/eyes_trained_w.png", true)
    };

    /// <summary>
    /// Ids of quests that ship bespoke art bundled in the app under
    /// Resources/quests/&lt;id&gt;.png. Derived from the embedded definitions, which are
    /// the source of truth for what art this build carries. Used by the server-parse
    /// path to resolve per-quest art (with a category fallback for any unknown id).
    /// </summary>
    private static readonly HashSet<string> BundledArtIds =
        DailyQuests.Concat(WeeklyQuests).Select(q => q.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>True if this build bundles a bespoke art PNG for the given quest id.</summary>
    public static bool HasBundledArt(string? id) =>
        !string.IsNullOrEmpty(id) && BundledArtIds.Contains(id);

    /// <summary>pack:// URI for a bundled quest's bespoke art (no existence check).</summary>
    public static string BundledArtPath(string id) =>
        $"pack://application:,,,/Resources/quests/{id}.png";
}
