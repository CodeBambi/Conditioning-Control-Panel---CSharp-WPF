using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Models.Deeper
{
    public static class TriggerTypes
    {
        public const string GazeTarget = "gaze_target";
        public const string GazeAvoid = "gaze_avoid";
        public const string AttentionLost = "attention_lost";
        public const string BlinkDetected = "blink_detected";
        public const string MouthOpen = "mouth_open";
        public const string TimeReached = "time_reached";
        public const string RegionEntered = "region_entered";
        public const string RegionExited = "region_exited";
        public const string Never = "never";
    }

    [JsonConverter(typeof(EnhancementTriggerConverter))]
    public abstract class EnhancementTrigger
    {
        [JsonProperty("type", Order = -2)]
        public abstract string Type { get; }

        public abstract bool IsVideoOnly { get; }

        [JsonExtensionData]
        public IDictionary<string, JToken>? UnknownFields { get; set; }
    }

    public class GazeTargetTrigger : EnhancementTrigger
    {
        public override string Type => TriggerTypes.GazeTarget;
        public override bool IsVideoOnly => true;

        // Normalized rect [x, y, width, height] in [0,1] within the video display rect.
        [JsonProperty("rect")]
        public double[] Rect { get; set; } = new[] { 0.0, 0.0, 1.0, 1.0 };

        [JsonProperty("min_dwell_ms")]
        public int MinDwellMs { get; set; } = 500;
    }

    public class GazeAvoidTrigger : EnhancementTrigger
    {
        public override string Type => TriggerTypes.GazeAvoid;
        public override bool IsVideoOnly => true;

        [JsonProperty("rect")]
        public double[] Rect { get; set; } = new[] { 0.0, 0.0, 1.0, 1.0 };

        [JsonProperty("min_dwell_ms")]
        public int MinDwellMs { get; set; } = 500;
    }

    public class AttentionLostTrigger : EnhancementTrigger
    {
        public override string Type => TriggerTypes.AttentionLost;
        public override bool IsVideoOnly => true;

        [JsonProperty("min_duration_ms")]
        public int MinDurationMs { get; set; } = 1500;
    }

    public class BlinkDetectedTrigger : EnhancementTrigger
    {
        public override string Type => TriggerTypes.BlinkDetected;
        public override bool IsVideoOnly => true;
    }

    public class MouthOpenTrigger : EnhancementTrigger
    {
        public override string Type => TriggerTypes.MouthOpen;
        public override bool IsVideoOnly => true;
    }

    public class TimeReachedTrigger : EnhancementTrigger
    {
        public override string Type => TriggerTypes.TimeReached;
        public override bool IsVideoOnly => false;

        [JsonProperty("time")]
        public double Time { get; set; }
    }

    public class RegionEnteredTrigger : EnhancementTrigger
    {
        public override string Type => TriggerTypes.RegionEntered;
        public override bool IsVideoOnly => false;

        [JsonProperty("region_id")]
        public string RegionId { get; set; } = "";
    }

    public class RegionExitedTrigger : EnhancementTrigger
    {
        public override string Type => TriggerTypes.RegionExited;
        public override bool IsVideoOnly => false;

        [JsonProperty("region_id")]
        public string RegionId { get; set; } = "";
    }

    /// <summary>
    /// Placeholder for triggers whose <c>type</c> is unknown to this version of CCP.
    /// Round-trips its original type through serialization (so a newer-version file
    /// re-saved here doesn't drop unknown trigger types). Never fires at runtime.
    /// </summary>
    public class NeverFiringTrigger : EnhancementTrigger
    {
        [JsonIgnore]
        public string OriginalType { get; set; } = TriggerTypes.Never;

        public override string Type => OriginalType;
        public override bool IsVideoOnly => false;
    }

    public class EnhancementTriggerConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType) => typeof(EnhancementTrigger).IsAssignableFrom(objectType);

        public override bool CanWrite => false;

        public override object? ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null) return null;

            var obj = JObject.Load(reader);
            var typeName = obj["type"]?.ToString();

            EnhancementTrigger trigger = typeName switch
            {
                TriggerTypes.GazeTarget     => new GazeTargetTrigger(),
                TriggerTypes.GazeAvoid      => new GazeAvoidTrigger(),
                TriggerTypes.AttentionLost  => new AttentionLostTrigger(),
                TriggerTypes.BlinkDetected  => new BlinkDetectedTrigger(),
                TriggerTypes.MouthOpen      => new MouthOpenTrigger(),
                TriggerTypes.TimeReached    => new TimeReachedTrigger(),
                TriggerTypes.RegionEntered  => new RegionEnteredTrigger(),
                TriggerTypes.RegionExited   => new RegionExitedTrigger(),
                _                           => new NeverFiringTrigger { OriginalType = typeName ?? TriggerTypes.Never }
            };

            serializer.Populate(obj.CreateReader(), trigger);
            return trigger;
        }

        public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
        {
            throw new NotImplementedException();
        }
    }
}
