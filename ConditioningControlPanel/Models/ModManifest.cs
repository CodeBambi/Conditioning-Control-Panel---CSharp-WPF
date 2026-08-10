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

        [JsonProperty("fxPalette")]
        public ModFxPalette? FxPalette { get; set; }

        [JsonProperty("identity")]
        public ModIdentity? Identity { get; set; }

        [JsonProperty("subliminalPool")]
        public Dictionary<string, bool>? SubliminalPool { get; set; }

        [JsonProperty("lockCardPhrases")]
        public Dictionary<string, bool>? LockCardPhrases { get; set; }

        [JsonProperty("customTriggers")]
        public List<string>? CustomTriggers { get; set; }

        [JsonProperty("bouncingTextPool")]
        public Dictionary<string, bool>? BouncingTextPool { get; set; }

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

    /// <summary>
    /// Optional per-mod overrides for the ambient FX palette (fog/aurora wash, particles, glow,
    /// flash tint). Every slot is nullable: an unset slot resolves through Theme.FilterColor →
    /// Theme.AccentColor → the app default, so existing mods get coherent FX with no manifest edit.
    /// See ModService.GetMistColorHex and Services/FxTheme.cs.
    /// </summary>
    public class ModFxPalette
    {
        [JsonProperty("mistColor")]
        public string? MistColor { get; set; }

        [JsonProperty("particleColor")]
        public string? ParticleColor { get; set; }

        [JsonProperty("glowColor")]
        public string? GlowColor { get; set; }

        [JsonProperty("flashTint")]
        public string? FlashTint { get; set; }

        [JsonProperty("mistOpacity")]
        public double? MistOpacity { get; set; }
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

        /// <summary>
        /// Affirmation/praise term used in welcome screens and generic praise contexts.
        /// e.g. "Good Girl" for Bambi, "babe" for Sissy, "Unit" for Drone, "Subject" for CCP Default.
        /// </summary>
        [JsonProperty("affirmation")]
        public string? Affirmation { get; set; }

        /// <summary>
        /// Optional subject term specifically for rank/progression labels (e.g. "Beginner {RankSubject}").
        /// Falls back to <see cref="UserTerm"/> when not supplied. Sissy uses "Babe" here while keeping
        /// the lowercase "babe" everywhere else.
        /// </summary>
        [JsonProperty("rankSubject")]
        public string? RankSubject { get; set; }

        /// <summary>
        /// Descent vocabulary: what this mod calls ONE user inside narrative copy — the value that
        /// replaces the <c>{petname}</c> token in localized strings (see
        /// <see cref="Localization.VocabTokens"/>). Unset on every built-in mod today, which means
        /// every token resolves to the vanilla default.
        ///
        /// Deliberately NOT the same field as <see cref="UserTerm"/>: UserTerm is a UI label
        /// ("Bambi", "Subject", "Unit") that appears title-cased in buttons and headings, while
        /// petName is written into running prose and wants the mod's in-fiction lowercase address.
        /// A mod that wants them identical just sets both.
        /// </summary>
        [JsonProperty("petName")]
        public string? PetName { get; set; }

        /// <summary>
        /// Descent vocabulary: what this mod calls the user base as a group — the value that
        /// replaces the <c>{collective}</c> token (plural of <see cref="PetName"/> in practice, but
        /// free-form: a mod may prefer a group noun over a plural). Unset falls back to the vanilla
        /// default; there is no automatic pluralization of <see cref="PetName"/>, because guessing
        /// plurals across nine localized languages is a worse failure than a default.
        /// </summary>
        [JsonProperty("collective")]
        public string? Collective { get; set; }
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
