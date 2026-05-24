using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Models.Deeper
{
    public static class ActionTypes
    {
        public const string Seek = "seek";
        public const string LoopRegion = "loop_region";
        public const string Pause = "pause";
        public const string PlayAudio = "play_audio";
        public const string TriggerHaptic = "trigger_haptic";
        public const string TriggerEffect = "trigger_effect";
        public const string ScreenShake = "screen_shake";
        public const string SetIntensity = "set_intensity";
        public const string NoOp = "noop";
    }

    public static class SeekTargets
    {
        public const string Time = "time";
        public const string RegionStart = "region_start";
        public const string RegionEnd = "region_end";
    }

    /// <summary>
    /// Lifecycle phase for a dispatched effect. Synthesized by the engine at
    /// dispatch time and never serialized — controls how <see cref="RealActionDispatcher"/>
    /// routes a band-mode effect (start on band entry, stop on exit, restart
    /// when a seek lands inside an already-active band).
    /// </summary>
    public enum EffectPhase
    {
        OneShot,
        Start,
        Stop,
        Restart
    }

    [JsonConverter(typeof(EnhancementActionConverter))]
    public abstract class EnhancementAction
    {
        [JsonProperty("type", Order = -2)]
        public abstract string Type { get; }

        [JsonExtensionData]
        public IDictionary<string, JToken>? UnknownFields { get; set; }
    }

    public class SeekAction : EnhancementAction
    {
        public override string Type => ActionTypes.Seek;

        // One of "time" / "region_start" / "region_end".
        [JsonProperty("target")]
        public string Target { get; set; } = SeekTargets.Time;

        [JsonProperty("time", NullValueHandling = NullValueHandling.Ignore)]
        public double? Time { get; set; }

        [JsonProperty("region_id", NullValueHandling = NullValueHandling.Ignore)]
        public string? RegionId { get; set; }
    }

    public class LoopRegionAction : EnhancementAction
    {
        public override string Type => ActionTypes.LoopRegion;

        // null means "use the region the playhead is currently inside".
        [JsonProperty("region_id", NullValueHandling = NullValueHandling.Ignore)]
        public string? RegionId { get; set; }
    }

    public class PauseAction : EnhancementAction
    {
        public override string Type => ActionTypes.Pause;
    }

    public class PlayAudioAction : EnhancementAction
    {
        public override string Type => ActionTypes.PlayAudio;

        [JsonProperty("path")]
        public string Path { get; set; } = "";

        [JsonProperty("volume")]
        public int Volume { get; set; } = 80;

        [JsonProperty("duck_other_audio")]
        public bool DuckOtherAudio { get; set; } = true;
    }

    public class TriggerHapticAction : EnhancementAction, IHapticPatternTarget
    {
        public override string Type => ActionTypes.TriggerHaptic;

        // Exactly one of pattern_name / custom_pattern (validator enforces).
        [JsonProperty("pattern_name", NullValueHandling = NullValueHandling.Ignore)]
        public string? PatternName { get; set; }

        [JsonProperty("custom_pattern", NullValueHandling = NullValueHandling.Ignore)]
        public List<double[]>? CustomPattern { get; set; }

        [JsonProperty("intensity")]
        public double Intensity { get; set; } = 1.0;

        [JsonProperty("duration_ms")]
        public int DurationMs { get; set; } = 1000;

        // Runtime-only routing for band-mode haptics. Engine sets Phase + EffectId
        // when dispatching Start/Stop/Restart so the dispatcher can keep per-band
        // handles (multiple overlapping bands route independently).
        [JsonIgnore]
        public EffectPhase Phase { get; set; } = EffectPhase.OneShot;

        [JsonIgnore]
        public string? EffectId { get; set; }
    }

    /// <summary>
    /// Generic effect-firing action used by Rules whose Action is one of the five
    /// CCP effect types (haptic, flash, bubble, subliminal, overlay). The flat
    /// fields mirror <see cref="TimelineItem"/>'s effect payload so the editor's
    /// effect-settings UI can bind against either without branching on type.
    /// Validator/dispatcher route by <see cref="EffectType"/>.
    /// </summary>
    public class TriggerEffectAction : EnhancementAction
    {
        public override string Type => ActionTypes.TriggerEffect;

        // One of EffectTypes.Haptic / Flash / Bubble / Subliminal / Overlay.
        [JsonProperty("effect_type")]
        public string EffectType { get; set; } = EffectTypes.Haptic;

        [JsonProperty("intensity")]
        public double Intensity { get; set; } = 1.0;

        [JsonProperty("duration_ms")]
        public int DurationMs { get; set; } = 1000;

        // Haptic.
        [JsonProperty("pattern_name", NullValueHandling = NullValueHandling.Ignore)]
        public string? PatternName { get; set; }

        [JsonProperty("custom_pattern", NullValueHandling = NullValueHandling.Ignore)]
        public List<double[]>? CustomPattern { get; set; }

        // Flash.
        [JsonProperty("image_path", NullValueHandling = NullValueHandling.Ignore)]
        public string? ImagePath { get; set; }

        [JsonProperty("play_sound")]
        public bool PlaySound { get; set; } = true;

        // Subliminal.
        [JsonProperty("text", NullValueHandling = NullValueHandling.Ignore)]
        public string? Text { get; set; }

        // Overlay.
        [JsonProperty("overlay_kind", NullValueHandling = NullValueHandling.Ignore)]
        public string? OverlayKind { get; set; }

        [JsonProperty("opacity")]
        public double Opacity { get; set; } = 0.5;

        // Bubble.
        [JsonProperty("max_bubbles")]
        public int MaxBubbles { get; set; } = 3;

        // Runtime-only routing for band-mode effects. See TriggerHapticAction.Phase.
        [JsonIgnore]
        public EffectPhase Phase { get; set; } = EffectPhase.OneShot;

        [JsonIgnore]
        public string? EffectId { get; set; }
    }

    public class ScreenShakeAction : EnhancementAction
    {
        public override string Type => ActionTypes.ScreenShake;

        [JsonProperty("intensity")]
        public double Intensity { get; set; } = 0.5;

        [JsonProperty("duration_ms")]
        public int DurationMs { get; set; } = 500;
    }

    public class SetIntensityAction : EnhancementAction
    {
        public override string Type => ActionTypes.SetIntensity;

        // Modifies CCP session intensity for users who have the session-intensity
        // system enabled. No-ops silently otherwise.
        [JsonProperty("value")]
        public double Value { get; set; } = 0.5;
    }

    /// <summary>
    /// Placeholder for actions whose <c>type</c> is unknown to this version of CCP.
    /// Round-trips its original type through serialization. Never dispatches.
    /// </summary>
    public class NoOpEnhancementAction : EnhancementAction
    {
        [JsonIgnore]
        public string OriginalType { get; set; } = ActionTypes.NoOp;

        public override string Type => OriginalType;
    }

    public class EnhancementActionConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType) => typeof(EnhancementAction).IsAssignableFrom(objectType);

        public override bool CanWrite => false;

        public override object? ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null) return null;

            var obj = JObject.Load(reader);
            var typeName = obj["type"]?.ToString();

            EnhancementAction action = typeName switch
            {
                ActionTypes.Seek          => new SeekAction(),
                ActionTypes.LoopRegion    => new LoopRegionAction(),
                ActionTypes.Pause         => new PauseAction(),
                ActionTypes.PlayAudio     => new PlayAudioAction(),
                ActionTypes.TriggerHaptic => new TriggerHapticAction(),
                ActionTypes.TriggerEffect => new TriggerEffectAction(),
                ActionTypes.ScreenShake   => new ScreenShakeAction(),
                ActionTypes.SetIntensity  => new SetIntensityAction(),
                _                         => new NoOpEnhancementAction { OriginalType = typeName ?? ActionTypes.NoOp }
            };

            serializer.Populate(obj.CreateReader(), action);
            return action;
        }

        public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
        {
            throw new NotImplementedException();
        }
    }
}
