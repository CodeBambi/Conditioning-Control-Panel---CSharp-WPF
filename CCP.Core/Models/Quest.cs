using System;
using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Localization;
using Newtonsoft.Json;
using Serilog;

namespace ConditioningControlPanel.Models;

public enum QuestType
{
    Daily,
    Weekly
}

// QuestCategory lives in CCP.Core/Models/QuestCategory.cs (ProgramDay.Verifier is typed on it).


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
    /// Devices this quest cannot be completed without, as published by the definitions channel:
    /// "camera"/"webcam", "microphone"/"mic", or several separated by commas or spaces. Null on
    /// every embedded quest - their requirements come from the category instead
    /// (QuestHardwareGate.NeedsCamera / NeedsMicrophone). Unknown words are ignored, so a typo in
    /// hand-authored JSON costs the gate rather than the player's quest. ccp-bugs#1151.
    /// </summary>
    [JsonProperty("requiresHardware")]
    public string? RequiresHardware { get; set; }

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
    /// The categories whose underlying feature is behind the premium bar, as a set the roll can
    /// consult directly.
    ///
    /// ccp-bugs#1186 / #1192. Until now the ONLY thing that made a quest premium was the
    /// <see cref="RequiresPremium"/> flag, and that flag is hand-authored: it lives in
    /// quests_payload.json and in the server's DEFAULT_QUEST_DEFINITIONS, and a quest published
    /// by the definitions channel without it reads as free to every client. A free user then
    /// rolls "Let Bambi Takeover run for 25 minutes" or "Log 50 blinks in the live blink
    /// trainer", cannot open the feature at all, and loses the day's XP - which is exactly what
    /// both reports describe.
    ///
    /// The category is not hand-authored in the same way: it is what the client counts progress
    /// against, so a quest that moves on Takeover minutes MUST carry category "autonomy" or it
    /// would never move at all. Deriving the bar from it makes the gate impossible to
    /// under-declare, and the flag stays as an additive override for a free-category quest that
    /// still wants the bar.
    ///
    /// <see cref="QuestCategory.RemoteIssue"/> is deliberately NOT here: the giving side of
    /// remote control is open to every tier (see its declaration above). The receiving side
    /// (<see cref="QuestCategory.Remote"/>) is the premium one.
    /// </summary>
    private static readonly HashSet<QuestCategory> PremiumCategories = new()
    {
        QuestCategory.Autonomy,        // Bambi Takeover - TierGate.DemandPremium
        QuestCategory.Lockdown,        // Lockdown Mode  - TierGate.DemandPremium
        QuestCategory.Remote,          // Remote control, subject side - TierGate.RequiresPremium
        QuestCategory.KeywordTrigger,  // Awareness Engine - TierGate.DemandPremium
        QuestCategory.BlinkTrainer     // Blink Trainer  - TierGate.RequiresPremium
    };

    /// <summary>True when the category's feature is premium-only. See PremiumCategories.</summary>
    public static bool IsPremiumCategory(QuestCategory category) => PremiumCategories.Contains(category);

    /// <summary>
    /// THE ONE QUESTION THE ROLL ASKS: does finishing this quest need premium access? True when
    /// the definition says so OR when the category's feature is premium-only, so a definition
    /// that forgot the flag still cannot land on a free user's board (ccp-bugs#1186 / #1192).
    /// </summary>
    [JsonIgnore]
    public bool NeedsPremium => RequiresPremium || IsPremiumCategory(Category);

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
            "remoteissue" => QuestCategory.RemoteIssue,
            "remoteissued" => QuestCategory.RemoteIssue,
            "remotegiven" => QuestCategory.RemoteIssue,
            "keyword" => QuestCategory.KeywordTrigger,
            "keywordtrigger" => QuestCategory.KeywordTrigger,
            "blink" => QuestCategory.BlinkTrainer,
            "blinktrainer" => QuestCategory.BlinkTrainer,
            _ => UnknownCategory(category)
        };
    }

    /// <summary>
    /// Combined is the safe bucket for a category this build does not know (a server-side quest
    /// type newer than the client), but silently is how a typo in a quest definition shipped for
    /// a month as a "Combined" quest. Say so once per unknown value.
    /// </summary>
    private static readonly System.Collections.Generic.HashSet<string> _warnedCategories = new(StringComparer.OrdinalIgnoreCase);
    private static QuestCategory UnknownCategory(string? category)
    {
        var key = category ?? "(null)";
        lock (_warnedCategories)
        {
            if (_warnedCategories.Add(key))
                Log.Warning("Quest: unknown category '{Category}' mapped to Combined", key);
        }
        return QuestCategory.Combined;
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
        new("flash_rush_d", "Flash Rush", "View 40 flash images", QuestType.Daily, QuestCategory.Flash, 40, 130, "\u26A1", BundledArtPath("flash_rush_d")),
        new("bimbo_basics_d", "Bimbo Basics", "View 25 flash images", QuestType.Daily, QuestCategory.Flash, 25, 100, "\u2728", BundledArtPath("bimbo_basics_d")),
        new("spiral_sink_d", "Spiral Sink", "Spend 12 minutes with spiral overlay", QuestType.Daily, QuestCategory.Spiral, 12, 200, "\uD83C\uDF00", BundledArtPath("spiral_sink_d")),
        new("pink_haze_d", "Pink Haze", "Use pink filter for 15 minutes", QuestType.Daily, QuestCategory.PinkFilter, 15, 175, "\uD83D\uDC97", BundledArtPath("pink_haze_d")),
        new("pop_parade_d", "Pop Parade", "Pop 40 bubbles", QuestType.Daily, QuestCategory.Bubbles, 40, 150, "\uD83E\uDEE7", BundledArtPath("pop_parade_d")),
        new("screen_trance_d", "Screen Trance", "Watch 12 minutes of video", QuestType.Daily, QuestCategory.Video, 12, 200, "\uD83C\uDFAC", BundledArtPath("screen_trance_d")),
        new("daily_devotion_d", "Daily Devotion", "Complete 1 session", QuestType.Daily, QuestCategory.Session, 1, 250, "\uD83D\uDE4F", BundledArtPath("daily_devotion_d")),
        new("lock_it_in_d", "Lock It In", "Complete 2 lock cards", QuestType.Daily, QuestCategory.LockCard, 2, 200, "\uD83D\uDD12", BundledArtPath("lock_it_in_d")),
        new("count_along_d", "Count Along", "Finish 2 bubble count games", QuestType.Daily, QuestCategory.BubbleCount, 2, 175, "\uD83C\uDFAF", BundledArtPath("count_along_d")),
        new("soft_static_d", "Soft Static", "Spend 25 minutes with any overlay active", QuestType.Daily, QuestCategory.Combined, 25, 175, "\uD83E\uDDE0", BundledArtPath("soft_static_d")),

        // --- GIVING SIDE of remote control (free for every tier) ---
        // The answer to the two threads: a solo user could not finish a "take N remote
        // commands" quest, and nothing rewarded the Controllers the matchmaking pool needs.
        // Counts commands issued to ANOTHER subject only (self-control never counts - see
        // QuestService.TrackRemoteCommandIssued) and counts them at ANY intensity level.
        new("take_the_reins_d", "Take the Reins", "Issue 10 remote commands to other subjects", QuestType.Daily, QuestCategory.RemoteIssue, 10, 200, "\uD83C\uDFAE", BundledArtPath("take_the_reins_d")),

        // --- PATREON (exclusive features, RequiresPremium) ---
        new("takeover_drift_d", "Hands Off", "Let Bambi Takeover run for 15 minutes", QuestType.Daily, QuestCategory.Autonomy, 15, 250, "\uD83C\uDF80", BundledArtPath("takeover_drift_d"), true),
        new("takeover_deep_d", "On Autopilot", "Let Bambi Takeover run for 25 minutes", QuestType.Daily, QuestCategory.Autonomy, 25, 350, "\uD83C\uDF80", BundledArtPath("takeover_deep_d"), true),
        new("takeover_full_d", "Surrender", "Let Bambi Takeover run for 40 minutes", QuestType.Daily, QuestCategory.Autonomy, 40, 450, "\uD83C\uDF80", BundledArtPath("takeover_full_d"), true),
        new("locked_away_d", "Locked Away", "Complete 1 lockdown", QuestType.Daily, QuestCategory.Lockdown, 1, 300, "\u26D3", BundledArtPath("locked_away_d"), true),
        new("trigger_words_d", "Trigger Words", "Fire 15 keyword triggers", QuestType.Daily, QuestCategory.KeywordTrigger, 15, 200, "\uD83D\uDC41", BundledArtPath("trigger_words_d"), true),
        new("word_slave_d", "Word Slave", "Fire 30 keyword triggers", QuestType.Daily, QuestCategory.KeywordTrigger, 30, 300, "\uD83D\uDC41", BundledArtPath("word_slave_d"), true),
        new("handed_over_d", "Hand Over Control", "Take 25 remote commands", QuestType.Daily, QuestCategory.Remote, 25, 250, "\uD83D\uDCE1", BundledArtPath("handed_over_d"), true),
        new("remote_hands_d", "Remote Hands", "Take 50 remote commands", QuestType.Daily, QuestCategory.Remote, 50, 350, "\uD83D\uDCE1", BundledArtPath("remote_hands_d"), true),
        new("blink_drill_d", "Blink Drill", "Log 30 blinks in the live blink trainer", QuestType.Daily, QuestCategory.BlinkTrainer, 30, 200, "\uD83D\uDC40", BundledArtPath("blink_drill_d"), true),
        new("obedient_eyes_d", "Obedient Eyes", "Log 50 blinks in the live blink trainer", QuestType.Daily, QuestCategory.BlinkTrainer, 50, 300, "\uD83D\uDC40", BundledArtPath("obedient_eyes_d"), true)
    };

    /// <summary>
    /// All available weekly quests
    /// </summary>
    public static readonly List<QuestDefinition> WeeklyQuests = new()
    {
        // --- FREE (non-gated features) ---
        new("flash_monsoon_w", "Flash Monsoon", "View 600 flash images", QuestType.Weekly, QuestCategory.Flash, 600, 600, "\u26A1", BundledArtPath("flash_monsoon_w")),
        new("spiral_descent_w", "Spiral Descent", "Spend 150 minutes with spiral overlay", QuestType.Weekly, QuestCategory.Spiral, 150, 750, "\uD83C\uDF00", BundledArtPath("spiral_descent_w")),
        new("pink_world_w", "Pink World", "Use pink filter for 200 minutes", QuestType.Weekly, QuestCategory.PinkFilter, 200, 700, "\uD83D\uDC97", BundledArtPath("pink_world_w")),
        new("bubble_storm_w", "Bubble Storm", "Pop 500 bubbles", QuestType.Weekly, QuestCategory.Bubbles, 500, 600, "\uD83C\uDF0A", BundledArtPath("bubble_storm_w")),
        new("marathon_trance_w", "Marathon Trance", "Watch 90 minutes of video", QuestType.Weekly, QuestCategory.Video, 90, 800, "\uD83C\uDFAC", BundledArtPath("marathon_trance_w")),
        new("weekly_devotion_w", "Weekly Devotion", "Complete 7 sessions", QuestType.Weekly, QuestCategory.Session, 7, 1000, "\uD83D\uDE4F", BundledArtPath("weekly_devotion_w")),
        new("phrase_mastery_w", "Phrase Mastery", "Complete 15 lock cards", QuestType.Weekly, QuestCategory.LockCard, 15, 750, "\uD83D\uDD12", BundledArtPath("phrase_mastery_w")),
        new("total_submission_w", "Total Submission", "Complete 15 bubble count games", QuestType.Weekly, QuestCategory.BubbleCount, 15, 700, "\uD83C\uDFAF", BundledArtPath("total_submission_w")),
        new("streak_keeper_w", "Streak Keeper", "Maintain a 7-day streak", QuestType.Weekly, QuestCategory.Streak, 7, 600, "\uD83D\uDD25", BundledArtPath("streak_keeper_w")),
        new("conditioning_champion_w", "Conditioning Champion", "Earn 2000 XP from activities", QuestType.Weekly, QuestCategory.Combined, 2000, 500, "\uD83C\uDFC6", BundledArtPath("conditioning_champion_w")),

        // --- PATREON (exclusive features, RequiresPremium) ---
        new("autopilot_week_w", "Set It and Forget It", "Let Bambi Takeover run for 120 minutes this week", QuestType.Weekly, QuestCategory.Autonomy, 120, 900, "\uD83C\uDF80", BundledArtPath("autopilot_week_w"), true),
        new("always_on_w", "Always On", "Let Bambi Takeover run for 180 minutes this week", QuestType.Weekly, QuestCategory.Autonomy, 180, 1100, "\uD83C\uDF80", BundledArtPath("always_on_w"), true),
        new("lockdown_habit_w", "Lockdown Habit", "Complete 5 lockdowns", QuestType.Weekly, QuestCategory.Lockdown, 5, 800, "\u26D3", BundledArtPath("lockdown_habit_w"), true),
        new("throw_away_key_w", "Throw Away the Key", "Complete 7 lockdowns", QuestType.Weekly, QuestCategory.Lockdown, 7, 1000, "\u26D3", BundledArtPath("throw_away_key_w"), true),
        new("pavlov_w", "Pavlov", "Fire 300 keyword triggers", QuestType.Weekly, QuestCategory.KeywordTrigger, 300, 750, "\uD83D\uDC41", BundledArtPath("pavlov_w"), true),
        new("word_conditioned_w", "Word-Conditioned", "Fire 1000 keyword triggers", QuestType.Weekly, QuestCategory.KeywordTrigger, 1000, 1200, "\uD83D\uDC41", BundledArtPath("word_conditioned_w"), true),
        new("puppet_strings_w", "Puppet Strings", "Take 100 remote commands this week", QuestType.Weekly, QuestCategory.Remote, 100, 800, "\uD83D\uDCE1", BundledArtPath("puppet_strings_w"), true),
        new("fully_remote_w", "Fully Remote", "Take 200 remote commands this week", QuestType.Weekly, QuestCategory.Remote, 200, 1100, "\uD83D\uDCE1", BundledArtPath("fully_remote_w"), true),
        new("blink_century_w", "Blink Century", "Log 100 blinks in the live blink trainer", QuestType.Weekly, QuestCategory.BlinkTrainer, 100, 700, "\uD83D\uDC40", BundledArtPath("blink_century_w"), true),
        new("eyes_trained_w", "Eyes Trained", "Log 200 blinks in the live blink trainer", QuestType.Weekly, QuestCategory.BlinkTrainer, 200, 1000, "\uD83D\uDC40", BundledArtPath("eyes_trained_w"), true)
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
        $"{PackResources}quests/{id}.png";

    /// <summary>
    /// The one pack:// root in Core, baselined in core-guards.sh. It is DATA here, not a load:
    /// ImagePath is persisted in quest_definitions_cache.json in this exact form, so it cannot
    /// become a bare name without changing that file. Core never resolves it; heads do.
    /// </summary>
    internal const string PackResources = "pack://application:,,,/Resources/";
}
