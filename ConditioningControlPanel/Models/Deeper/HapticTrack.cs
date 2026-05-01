using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Models.Deeper
{
    /// <summary>
    /// Shared shape for anything carrying a stock-or-custom haptic pattern
    /// (haptic events on a track, trigger_haptic actions on rules). Lets the
    /// editor's curve UI bind against either without knowing the concrete type.
    /// </summary>
    public interface IHapticPatternTarget
    {
        string? PatternName { get; set; }
        List<double[]>? CustomPattern { get; set; }
    }

    public class HapticTrack
    {
        [JsonProperty("id")]
        public string Id { get; set; } = "primary";

        [JsonProperty("events")]
        public List<HapticEvent> Events { get; set; } = new();

        [JsonExtensionData]
        public IDictionary<string, JToken>? UnknownFields { get; set; }
    }

    public class HapticEvent : IHapticPatternTarget
    {
        [JsonProperty("start")]
        public double Start { get; set; }

        [JsonProperty("duration")]
        public double Duration { get; set; }

        [JsonProperty("intensity")]
        public double Intensity { get; set; } = 1.0;

        // Exactly one of pattern_name / custom_pattern must be set (validator enforces).
        [JsonProperty("pattern_name", NullValueHandling = NullValueHandling.Ignore)]
        public string? PatternName { get; set; }

        // Keyframes as [[t_frac, intensity], ...]. t_frac in [0, 1] monotonically increasing.
        // Sampled to a flat float[] at dispatch time via HapticService.SetSyncPatternAsync.
        [JsonProperty("custom_pattern", NullValueHandling = NullValueHandling.Ignore)]
        public List<double[]>? CustomPattern { get; set; }

        [JsonExtensionData]
        public IDictionary<string, JToken>? UnknownFields { get; set; }
    }
}
