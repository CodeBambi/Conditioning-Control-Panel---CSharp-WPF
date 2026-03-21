using System.Collections.Generic;
using Newtonsoft.Json;

namespace ConditioningControlPanel.Models
{
    /// <summary>
    /// Manifest data model for a .ccpmod package (deserialized from mod.json).
    /// Every section except id/name/version/author is optional.
    /// </summary>
    public class ModManifest
    {
        // REQUIRED
        [JsonProperty("id")]
        public string Id { get; set; } = "";

        [JsonProperty("name")]
        public string Name { get; set; } = "";

        [JsonProperty("version")]
        public string Version { get; set; } = "1.0.0";

        [JsonProperty("author")]
        public string Author { get; set; } = "";

        // OPTIONAL metadata
        [JsonProperty("description")]
        public string? Description { get; set; }

        [JsonProperty("minAppVersion")]
        public string? MinAppVersion { get; set; }

        [JsonProperty("tags")]
        public List<string>? Tags { get; set; }

        [JsonProperty("previewImage")]
        public string? PreviewImage { get; set; }

        // OPTIONAL sections
        [JsonProperty("theme")]
        public ModTheme? Theme { get; set; }

        [JsonProperty("identity")]
        public ModIdentity? Identity { get; set; }

        [JsonProperty("subliminalPool")]
        public Dictionary<string, bool>? SubliminalPool { get; set; }

        [JsonProperty("lockCardPhrases")]
        public Dictionary<string, bool>? LockCardPhrases { get; set; }

        [JsonProperty("customTriggers")]
        public List<string>? CustomTriggers { get; set; }

        [JsonProperty("triggers")]
        public ModTriggers? Triggers { get; set; }

        [JsonProperty("messages")]
        public ModMessages? Messages { get; set; }

        [JsonProperty("browser")]
        public ModBrowser? Browser { get; set; }

        [JsonProperty("phrases")]
        public Dictionary<string, string[]>? Phrases { get; set; }

        [JsonProperty("personalities")]
        public List<ModPersonality>? Personalities { get; set; }

        [JsonProperty("textReplacements")]
        public Dictionary<string, string>? TextReplacements { get; set; }

        [JsonProperty("enhancementOverrides")]
        public ModEnhancementOverrides? EnhancementOverrides { get; set; }

        [JsonProperty("tubeLayout")]
        public ModTubeLayout? TubeLayout { get; set; }

        [JsonProperty("supportedAvatarSets")]
        public List<int>? SupportedAvatarSets { get; set; }

        [JsonProperty("customAvatarSets")]
        public List<CustomAvatarSet>? CustomAvatarSets { get; set; }
    }

    public class CustomAvatarSet
    {
        [JsonProperty("setNumber")]
        public int SetNumber { get; set; }

        [JsonProperty("label")]
        public string Label { get; set; } = "";

        [JsonProperty("unlockLevel")]
        public int UnlockLevel { get; set; }
    }

    public class ModTheme
    {
        [JsonProperty("accentColor")]
        public string? AccentColor { get; set; }

        [JsonProperty("accentLightColor")]
        public string? AccentLightColor { get; set; }

        [JsonProperty("accentDarkColor")]
        public string? AccentDarkColor { get; set; }

        [JsonProperty("backgroundColor")]
        public string? BackgroundColor { get; set; }

        [JsonProperty("panelColor")]
        public string? PanelColor { get; set; }

        [JsonProperty("surfaceColor")]
        public string? SurfaceColor { get; set; }

        [JsonProperty("filterColor")]
        public string? FilterColor { get; set; }
    }

    public class ModIdentity
    {
        [JsonProperty("companionName")]
        public string? CompanionName { get; set; }

        [JsonProperty("userTerm")]
        public string? UserTerm { get; set; }

        [JsonProperty("modeDisplayName")]
        public string? ModeDisplayName { get; set; }

        [JsonProperty("talkToLabel")]
        public string? TalkToLabel { get; set; }

        [JsonProperty("takeoverLabel")]
        public string? TakeoverLabel { get; set; }
    }

    public class ModTriggers
    {
        [JsonProperty("freeze")]
        public string? Freeze { get; set; }

        [JsonProperty("reset")]
        public string? Reset { get; set; }

        [JsonProperty("cumAndCollapse")]
        public string? CumAndCollapse { get; set; }

        [JsonProperty("autonomyOn")]
        public string? AutonomyOn { get; set; }
    }

    public class ModMessages
    {
        [JsonProperty("attentionCheckFail")]
        public string? AttentionCheckFail { get; set; }

        [JsonProperty("attentionCheckMercy")]
        public string? AttentionCheckMercy { get; set; }

        [JsonProperty("bubbleCountRetry")]
        public string? BubbleCountRetry { get; set; }
    }

    public class ModBrowser
    {
        [JsonProperty("defaultUrl")]
        public string? DefaultUrl { get; set; }

        [JsonProperty("siteName")]
        public string? SiteName { get; set; }

        [JsonProperty("showBambiCloudOption")]
        public bool? ShowBambiCloudOption { get; set; }

        [JsonProperty("defaultVideoLinks")]
        public Dictionary<string, string>? DefaultVideoLinks { get; set; }
    }

    /// <summary>
    /// Horizontal offset adjustments for avatar/UI positioning within the tube window.
    /// Positive values shift elements RIGHT from the default position.
    /// Used when a mod's tube image has the glass area in a different position than the default.
    /// </summary>
    public class ModTubeLayout
    {
        [JsonProperty("avatarOffsetX")]
        public int AvatarOffsetX { get; set; }

        [JsonProperty("avatarDetachedOffsetX")]
        public int AvatarDetachedOffsetX { get; set; }

        [JsonProperty("avatarScale")]
        public double? AvatarScale { get; set; }

        [JsonProperty("avatarOffsetY")]
        public int AvatarOffsetY { get; set; }

        [JsonProperty("avatarDetachedOffsetY")]
        public int AvatarDetachedOffsetY { get; set; }
    }

    public class ModEnhancementOverrides
    {
        [JsonProperty("treeTitle")]
        public string? TreeTitle { get; set; }

        [JsonProperty("treeSubtitle")]
        public string? TreeSubtitle { get; set; }

        [JsonProperty("treeWarning")]
        public string? TreeWarning { get; set; }

        [JsonProperty("pointsLabel")]
        public string? PointsLabel { get; set; }

        [JsonProperty("statsTitle")]
        public string? StatsTitle { get; set; }

        [JsonProperty("tabTooltip")]
        public string? TabTooltip { get; set; }

        [JsonProperty("pinkRushName")]
        public string? PinkRushName { get; set; }

        [JsonProperty("pinkRushDescription")]
        public string? PinkRushDescription { get; set; }

        [JsonProperty("luckyFlashLabel")]
        public string? LuckyFlashLabel { get; set; }

        [JsonProperty("luckyBubbleLabel")]
        public string? LuckyBubbleLabel { get; set; }

        [JsonProperty("boostTooltips")]
        public Dictionary<string, string>? BoostTooltips { get; set; }

        [JsonProperty("statPillTooltips")]
        public Dictionary<string, string>? StatPillTooltips { get; set; }
    }

    public class ModPersonality
    {
        [JsonProperty("id")]
        public string Id { get; set; } = "";

        [JsonProperty("name")]
        public string Name { get; set; } = "";

        [JsonProperty("description")]
        public string? Description { get; set; }

        [JsonProperty("promptSettings")]
        public Dictionary<string, string>? PromptSettings { get; set; }
    }
}
