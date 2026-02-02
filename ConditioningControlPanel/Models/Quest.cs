using System.Collections.Generic;

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
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public QuestType Type { get; set; }
    public QuestCategory Category { get; set; }
    public int TargetValue { get; set; }
    public int XPReward { get; set; }
    public string Icon { get; set; } = "";
    public string ImagePath { get; set; } = "";

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
        new("flash_monsoon_w", "Flash Monsoon", "View 300 flash images", QuestType.Weekly, QuestCategory.Flash, 300, 600, "\u26A1", "pack://application:,,,/Resources/features/flash.png"),
        new("spiral_abyss_w", "Spiral Abyss", "Spend 60 minutes with spiral overlay", QuestType.Weekly, QuestCategory.Spiral, 60, 750, "\uD83C\uDF00", "pack://application:,,,/Resources/features/spiral_overlay.png"),
        new("bubble_tsunami_w", "Bubble Tsunami", "Pop 200 bubbles", QuestType.Weekly, QuestCategory.Bubbles, 200, 600, "\uD83C\uDF0A", "pack://application:,,,/Resources/features/Bubble_pop.png"),
        new("pink_immersion_w", "Pink Immersion", "Use pink filter for 90 minutes", QuestType.Weekly, QuestCategory.PinkFilter, 90, 700, "\uD83D\uDC97", "pack://application:,,,/Resources/features/Pink_filter.png"),
        new("marathon_trance_w", "Marathon Trance", "Watch 60 minutes of video", QuestType.Weekly, QuestCategory.Video, 60, 800, "\uD83C\uDFAC", "pack://application:,,,/Resources/features/mandatory_videos.png"),
        new("weekly_devotion_w", "Weekly Devotion", "Complete 5 sessions", QuestType.Weekly, QuestCategory.Session, 5, 1000, "\uD83D\uDE4F", "pack://application:,,,/Resources/features/bambi takeover.png"),
        new("phrase_mastery_w", "Phrase Mastery", "Complete 10 lock cards", QuestType.Weekly, QuestCategory.LockCard, 10, 750, "\uD83D\uDD12", "pack://application:,,,/Resources/features/Phrase_Lock.png"),
        new("conditioning_champion_w", "Conditioning Champion", "Earn 1000 XP from activities", QuestType.Weekly, QuestCategory.Combined, 1000, 500, "\uD83C\uDFC6", "pack://application:,,,/Resources/logo.png"),
        new("streak_keeper_w", "Streak Keeper", "Maintain a 5-day streak", QuestType.Weekly, QuestCategory.Streak, 5, 600, "\uD83D\uDD25", "pack://application:,,,/Resources/achievements/daily_maintenance.png"),
        new("total_submission_w", "Total Submission", "Complete 10 bubble count games", QuestType.Weekly, QuestCategory.BubbleCount, 10, 700, "\uD83C\uDFAF", "pack://application:,,,/Resources/features/Bubble_count.png")
    };
}
