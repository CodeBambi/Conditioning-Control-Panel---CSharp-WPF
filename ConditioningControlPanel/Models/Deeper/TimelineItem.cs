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
        // Voice prompt: shows an on-screen cue and listens (offline Vosk) for the
        // user to say a target phrase, scoring it and giving correct/incorrect
        // feedback for N reps before releasing the band. Band-capable.
        public const string Speak      = "speak";
    }

    /// <summary>How a <see cref="EffectTypes.Speak"/> cue is presented on screen.</summary>
    public enum SpeakCueMode
    {
        /// <summary>Flash the cue text repeatedly at a fixed interval (subliminal style).</summary>
        Intermittent,
        /// <summary>Keep the cue text on top of the screen until the prompt completes.</summary>
        Persistent
    }

    /// <summary>When a <see cref="EffectTypes.Speak"/> prompt is considered done.</summary>
    public enum SpeakCompletion
    {
        /// <summary>Listen for the band's duration; partial credit, never holds playback.</summary>
        Duration,
        /// <summary>Hold the region (per <see cref="SpeakHoldMode"/>) until the required reps are met.</summary>
        UntilSatisfied
    }

    /// <summary>What playback does while an "until satisfied" Speak prompt waits for the user.</summary>
    public enum SpeakHoldMode
    {
        /// <summary>Loop back to the region start as the playhead reaches its end.</summary>
        LoopRegion,
        /// <summary>Pause playback at the region end until satisfied, then resume.</summary>
        Pause,
        /// <summary>Let the video play on; the cue just persists (soft target).</summary>
        KeepPlaying
    }

    /// <summary>
    /// Overlay kind discriminators for <see cref="TimelineItem.EffectOverlayKind"/>.
    /// </summary>
    public static class OverlayKinds
    {
        public const string PinkFilter = "pink_filter";
        public const string Spiral     = "spiral";
        public const string BrainDrain = "braindrain";
        /// <summary>Melt variant of Brain Drain. Shares the one underlying blur overlay (and all of
        /// its hold/ramp state) with <see cref="BrainDrain"/> — the two never co-exist. The melt
        /// warp is live: the same blur plus a slow Perlin displacement drift, rendered on the
        /// compositor's capture pump (Services/Compositor/BrainDrainCapturePump).</summary>
        public const string BrainDrainMelt = "braindrain_melt";

        /// <summary>
        /// True for overlay kinds withheld from the editor pickers and playback. NONE today:
        /// Brain Drain / Brain Melt were re-enabled when the rework shipped (see
        /// OverlayService.BrainDrainWithheld, the runtime half of the same decision — keep the
        /// two in sync). The helper and its editor plumbing stay so a future withholding is a
        /// two-line change again.
        /// </summary>
        public static bool IsWithheld(string? kind) => false;
    }

    /// <summary>
    /// How an effect on the timeline relates to its band on playback.
    /// <list type="bullet">
    /// <item><b>Region</b> — show on entry to the band, hide on exit. Tracks the playhead through loops and scrubs.</item>
    /// <item><b>Duration</b> — fire once at <c>Start</c>, run for <c>EffectDurationMs</c> wall-clock (legacy).</item>
    /// </list>
    /// The property is nullable; absent/null means "use the default for this effect type"
    /// — see <see cref="EffectActivationHelpers.Resolve"/>.
    /// </summary>
    public enum EffectActivation
    {
        Region,
        Duration
    }

    public static class EffectActivationHelpers
    {
        /// <summary>
        /// Default activation for an effect type when no explicit value is set.
        /// Overlay/haptic default to <see cref="EffectActivation.Region"/> (band-active).
        /// Flash/subliminal/bubble default to <see cref="EffectActivation.Duration"/> (one-shot).
        /// </summary>
        public static EffectActivation Resolve(EffectActivation? explicitValue, string? effectType)
        {
            if (explicitValue.HasValue) return explicitValue.Value;
            return effectType switch
            {
                EffectTypes.Overlay => EffectActivation.Region,
                EffectTypes.Haptic  => EffectActivation.Region,
                // Speak prompts run across the whole band (Start on entry, Stop on
                // exit) so the cue/listen session spans the region.
                EffectTypes.Speak   => EffectActivation.Region,
                _                   => EffectActivation.Duration
            };
        }
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

        // Null = let EffectActivationHelpers.Resolve pick the default for this EffectType.
        // Old saved files have no field and pick up the resolver's default automatically,
        // which is the intended migration path for the v5.9.10 Region default.
        [JsonProperty("effect_activation", NullValueHandling = NullValueHandling.Ignore)]
        [JsonConverter(typeof(StringEnumConverter))]
        public EffectActivation? EffectActivation { get; set; }

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

        // Per-instance opt-out of the auto-haptic buzz that flash and subliminal
        // effects fire when they pop. Absent in old files => false => unchanged
        // (haptic still fires). Only meaningful for flash/subliminal effects.
        [JsonProperty("effect_suppress_haptic")]
        public bool EffectSuppressHaptic { get; set; } = false;

        // Subliminal-specific.
        [JsonProperty("effect_text", NullValueHandling = NullValueHandling.Ignore)]
        public string? EffectText { get; set; }

        // Overlay-specific.
        [JsonProperty("effect_overlay_kind", NullValueHandling = NullValueHandling.Ignore)]
        public string? EffectOverlayKind { get; set; }

        [JsonProperty("effect_opacity")]
        public double EffectOpacity { get; set; } = 0.5;

        // Optional opacity ramp for overlay effects: interpolate opacity from
        // start→end across the item's duration (all overlay kinds — braindrain maps
        // the 0..1 ramp to blur intensity, see SetSustainedOverlayOpacity). Both absent
        // (old files) => flat EffectOpacity, no ramp. Additive; no schema bump.
        [JsonProperty("effect_opacity_start", NullValueHandling = NullValueHandling.Ignore)]
        public double? EffectOpacityStart { get; set; }

        [JsonProperty("effect_opacity_end", NullValueHandling = NullValueHandling.Ignore)]
        public double? EffectOpacityEnd { get; set; }

        // Bubble-specific.
        [JsonProperty("effect_max_bubbles")]
        public int EffectMaxBubbles { get; set; } = 3;

        // Speak-specific (voice prompt). All additive + NullValueHandling.Ignore so
        // old files are untouched; the runtime falls back to sane defaults.
        [JsonProperty("effect_speak_target", NullValueHandling = NullValueHandling.Ignore)]
        public string? EffectSpeakTarget { get; set; }

        // On-screen cue text. Null/empty => "Say {target}".
        [JsonProperty("effect_speak_cue", NullValueHandling = NullValueHandling.Ignore)]
        public string? EffectSpeakCue { get; set; }

        [JsonProperty("effect_speak_cue_mode", NullValueHandling = NullValueHandling.Ignore)]
        [JsonConverter(typeof(StringEnumConverter))]
        public SpeakCueMode EffectSpeakCueMode { get; set; } = SpeakCueMode.Intermittent;

        [JsonProperty("effect_speak_cue_interval_ms")]
        public int EffectSpeakCueIntervalMs { get; set; } = 250;

        [JsonProperty("effect_speak_required_reps")]
        public int EffectSpeakRequiredReps { get; set; } = 1;

        [JsonProperty("effect_speak_completion", NullValueHandling = NullValueHandling.Ignore)]
        [JsonConverter(typeof(StringEnumConverter))]
        public SpeakCompletion EffectSpeakCompletion { get; set; } = SpeakCompletion.UntilSatisfied;

        [JsonProperty("effect_speak_hold_mode", NullValueHandling = NullValueHandling.Ignore)]
        [JsonConverter(typeof(StringEnumConverter))]
        public SpeakHoldMode EffectSpeakHoldMode { get; set; } = SpeakHoldMode.LoopRegion;

        [JsonProperty("effect_speak_correct", NullValueHandling = NullValueHandling.Ignore)]
        public string? EffectSpeakCorrectMessage { get; set; }

        [JsonProperty("effect_speak_incorrect", NullValueHandling = NullValueHandling.Ignore)]
        public string? EffectSpeakIncorrectMessage { get; set; }

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
