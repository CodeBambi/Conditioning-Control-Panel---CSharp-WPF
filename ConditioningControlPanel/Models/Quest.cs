using System;
using System.Collections.Generic;
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
    Combined        // Multiple activities (overlay time, XP earned)
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
        QuestCategory category, int target, int xpReward, string icon, string imagePath = "")
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
            "combined" => QuestCategory.Combined,
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
        new("flash_flood_d", "Flash Flood", "View 50 flash images", QuestType.Daily, QuestCategory.Flash, 50, 150, "\u26A1", "pack://application:,,,/Resources/features/flash.png"),
        new("spiral_submersion_d", "Spiral Submersion", "Spend 10 minutes with spiral overlay", QuestType.Daily, QuestCategory.Spiral, 10, 200, "\uD83C\uDF00", "pack://application:,,,/Resources/features/spiral_overlay.png"),
        new("bubble_brain_d", "Bubble Brain", "Pop 30 bubbles", QuestType.Daily, QuestCategory.Bubbles, 30, 150, "\uD83E\uDEE7", "pack://application:,,,/Resources/features/Bubble_pop.png"),
        new("pink_vision_d", "Pink Vision", "Use pink filter for 15 minutes", QuestType.Daily, QuestCategory.PinkFilter, 15, 175, "\uD83D\uDC97", "pack://application:,,,/Resources/features/Pink_filter.png"),
        new("deep_trance_d", "Deep Trance Training", "Watch 10 minutes of video", QuestType.Daily, QuestCategory.Video, 10, 200, "\uD83C\uDFAC", "pack://application:,,,/Resources/features/mandatory_videos.png"),
        new("devotion_display_d", "Devotion Display", "Complete 1 session", QuestType.Daily, QuestCategory.Session, 1, 250, "\uD83D\uDE4F", "pack://application:,,,/Resources/features/bambi takeover.png"),
        new("obedience_drill_d", "Obedience Drill", "Complete 2 lock cards", QuestType.Daily, QuestCategory.LockCard, 2, 200, "\uD83D\uDD12", "pack://application:,,,/Resources/features/Phrase_Lock.png"),
        new("bimbo_basics_d", "Bimbo Basics", "View 25 flash images", QuestType.Daily, QuestCategory.Flash, 25, 100, "\u2728", "pack://application:,,,/Resources/features/flash.png"),
        new("mindless_minutes_d", "Mindless Minutes", "Spend 20 minutes with any overlay active", QuestType.Daily, QuestCategory.Combined, 20, 175, "\uD83E\uDDE0", "pack://application:,,,/Resources/features/brain_drain.png"),
        new("thought_pop_d", "Thought Pop", "Pop 50 bubbles", QuestType.Daily, QuestCategory.Bubbles, 50, 175, "\uD83D\uDCAD", "pack://application:,,,/Resources/features/Bubble_pop.png")
    };

    /// <summary>
    /// All available weekly quests
    /// </summary>
    public static readonly List<QuestDefinition> WeeklyQuests = new()
    {
        new("flash_monsoon_w", "Flash Monsoon", "View 500 flash images", QuestType.Weekly, QuestCategory.Flash, 500, 600, "\u26A1", "pack://application:,,,/Resources/features/flash.png"),
        new("spiral_abyss_w", "Spiral Abyss", "Spend 120 minutes with spiral overlay", QuestType.Weekly, QuestCategory.Spiral, 120, 750, "\uD83C\uDF00", "pack://application:,,,/Resources/features/spiral_overlay.png"),
        new("bubble_tsunami_w", "Bubble Tsunami", "Pop 400 bubbles", QuestType.Weekly, QuestCategory.Bubbles, 400, 600, "\uD83C\uDF0A", "pack://application:,,,/Resources/features/Bubble_pop.png"),
        new("pink_immersion_w", "Pink Immersion", "Use pink filter for 180 minutes", QuestType.Weekly, QuestCategory.PinkFilter, 180, 700, "\uD83D\uDC97", "pack://application:,,,/Resources/features/Pink_filter.png"),
        new("marathon_trance_w", "Marathon Trance", "Watch 90 minutes of video", QuestType.Weekly, QuestCategory.Video, 90, 800, "\uD83C\uDFAC", "pack://application:,,,/Resources/features/mandatory_videos.png"),
        new("weekly_devotion_w", "Weekly Devotion", "Complete 7 sessions", QuestType.Weekly, QuestCategory.Session, 7, 1000, "\uD83D\uDE4F", "pack://application:,,,/Resources/features/bambi takeover.png"),
        new("phrase_mastery_w", "Phrase Mastery", "Complete 15 lock cards", QuestType.Weekly, QuestCategory.LockCard, 15, 750, "\uD83D\uDD12", "pack://application:,,,/Resources/features/Phrase_Lock.png"),
        new("conditioning_champion_w", "Conditioning Champion", "Earn 2000 XP from activities", QuestType.Weekly, QuestCategory.Combined, 2000, 500, "\uD83C\uDFC6", "pack://application:,,,/Resources/logo.png"),
        new("streak_keeper_w", "Streak Keeper", "Maintain a 7-day streak", QuestType.Weekly, QuestCategory.Streak, 7, 600, "\uD83D\uDD25", "pack://application:,,,/Resources/achievements/daily_maintenance.png"),
        new("total_submission_w", "Total Submission", "Complete 15 bubble count games", QuestType.Weekly, QuestCategory.BubbleCount, 15, 700, "\uD83C\uDFAF", "pack://application:,,,/Resources/features/Bubble_count.png")
    };
}
