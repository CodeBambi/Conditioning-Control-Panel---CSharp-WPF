using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Models.Deeper
{
    public enum TimelineItemKind
    {
        Effect,
        Rule
    }

    /// <summary>
    /// Effect type discriminators for <see cref="TimelineItem.EffectType"/>.
    /// Keep stable — these strings ship in saved JSON.
    /// </summary>
    public static class EffectTypes
    {
        public const string Haptic     = "haptic";
        public const string Flash      = "flash";
        public const string Bubble     = "bubble";
        public const string Subliminal = "subliminal";
        public const string Overlay    = "overlay";
    }

    /// <summary>
    /// Overlay kind discriminators for <see cref="TimelineItem.EffectOverlayKind"/>.
    /// </summary>
    public static class OverlayKinds
    {
        public const string PinkFilter = "pink_filter";
        public const string Spiral     = "spiral";
        public const string BrainDrain = "braindrain";
    }

    /// <summary>
    /// Unified timeline item — either an Effect (rendered as a dot or short capsule)
    /// or a Rule (rendered as a colored band whose duration is the trigger's firing
    /// window). Replaces the v1 separate Region/HapticEvent/EnhancementRule trio
    /// in the editor's mental model. The on-disk format keeps the legacy collections
    /// populated alongside <c>timeline_items</c> for one release cycle so old clients
    /// can still load files saved by the new editor.
    /// </summary>
    public class TimelineItem : IHapticPatternTarget
    {
        [JsonProperty("id")]
        public string Id { get; set; } = NewId();

        [JsonProperty("kind")]
        [JsonConverter(typeof(StringEnumConverter))]
        public TimelineItemKind Kind { get; set; } = TimelineItemKind.Rule;

        [JsonProperty("start")]
        public double Start { get; set; }

        [JsonProperty("duration")]
        public double Duration { get; set; }

        [JsonProperty("label", NullValueHandling = NullValueHandling.Ignore)]
        public string? Label { get; set; }

        [JsonProperty("color", NullValueHandling = NullValueHandling.Ignore)]
        public string? Color { get; set; }

        // -- Effect-only fields (Kind == Effect) ----------------------------------

        [JsonProperty("effect_type", NullValueHandling = NullValueHandling.Ignore)]
        public string? EffectType { get; set; }

        [JsonProperty("effect_intensity")]
        public double EffectIntensity { get; set; } = 1.0;

        [JsonProperty("effect_duration_ms")]
        public int EffectDurationMs { get; set; } = 1000;

        // Haptic-specific.
        [JsonProperty("effect_pattern_name", NullValueHandling = NullValueHandling.Ignore)]
        public string? EffectPatternName { get; set; }

        [JsonProperty("effect_custom_pattern", NullValueHandling = NullValueHandling.Ignore)]
        public List<double[]>? EffectCustomPattern { get; set; }

        // Flash-specific.
        [JsonProperty("effect_image_path", NullValueHandling = NullValueHandling.Ignore)]
        public string? EffectImagePath { get; set; }

        [JsonProperty("effect_play_sound")]
        public bool EffectPlaySound { get; set; } = true;

        // Subliminal-specific.
        [JsonProperty("effect_text", NullValueHandling = NullValueHandling.Ignore)]
        public string? EffectText { get; set; }

        // Overlay-specific.
        [JsonProperty("effect_overlay_kind", NullValueHandling = NullValueHandling.Ignore)]
        public string? EffectOverlayKind { get; set; }

        [JsonProperty("effect_opacity")]
        public double EffectOpacity { get; set; } = 0.5;

        // Bubble-specific.
        [JsonProperty("effect_max_bubbles")]
        public int EffectMaxBubbles { get; set; } = 3;

        // -- Rule-only fields (Kind == Rule) --------------------------------------

        [JsonProperty("trigger", NullValueHandling = NullValueHandling.Ignore)]
        public EnhancementTrigger? Trigger { get; set; }

        [JsonProperty("action", NullValueHandling = NullValueHandling.Ignore)]
        public EnhancementAction? Action { get; set; }

        [JsonProperty("cooldown_ms")]
        public int CooldownMs { get; set; } = 1000;

        [JsonProperty("enabled")]
        public bool Enabled { get; set; } = true;

        [JsonExtensionData]
        public IDictionary<string, JToken>? UnknownFields { get; set; }

        // -- IHapticPatternTarget plumbing for the haptic curve editor ------------

        [JsonIgnore]
        string? IHapticPatternTarget.PatternName
        {
            get => EffectPatternName;
            set => EffectPatternName = value;
        }

        [JsonIgnore]
        List<double[]>? IHapticPatternTarget.CustomPattern
        {
            get => EffectCustomPattern;
            set => EffectCustomPattern = value;
        }

        public static string NewId() => Guid.NewGuid().ToString("N").Substring(0, 8);
    }
}
