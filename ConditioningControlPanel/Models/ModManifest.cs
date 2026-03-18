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
        public string? DefaultVideoLinks { get; set; }
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
