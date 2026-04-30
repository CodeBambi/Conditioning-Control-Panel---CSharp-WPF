using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Models.Deeper
{
    public class Enhancement
    {
        public const int CurrentVersion = 1;
        public const string SchemaTag = "ccp-enhancement/v1";

        [JsonProperty("$schema")]
        public string Schema { get; set; } = SchemaTag;

        [JsonProperty("version")]
        public int Version { get; set; } = CurrentVersion;

        [JsonProperty("media_type")]
        public string MediaType { get; set; } = MediaTypes.Video;

        [JsonProperty("media_source")]
        public string MediaSource { get; set; } = "*";

        [JsonProperty("metadata")]
        public EnhancementMetadata Metadata { get; set; } = new();

        [JsonProperty("regions")]
        public List<Region> Regions { get; set; } = new();

        [JsonProperty("haptic_tracks")]
        public List<HapticTrack> HapticTracks { get; set; } = new();

        [JsonProperty("rules")]
        public List<EnhancementRule> Rules { get; set; } = new();

        [JsonExtensionData]
        public IDictionary<string, JToken>? UnknownFields { get; set; }
    }

    public static class MediaTypes
    {
        public const string Video = "video";
        public const string Audio = "audio";
    }

    public class EnhancementMetadata
    {
        [JsonProperty("name")]
        public string Name { get; set; } = "";

        [JsonProperty("creator")]
        public string Creator { get; set; } = "";

        [JsonProperty("description")]
        public string Description { get; set; } = "";

        [JsonProperty("tags")]
        public List<string> Tags { get; set; } = new();

        [JsonProperty("license")]
        public string License { get; set; } = "";

        [JsonExtensionData]
        public IDictionary<string, JToken>? UnknownFields { get; set; }
    }

    public class Region
    {
        [JsonProperty("id")]
        public string Id { get; set; } = "";

        [JsonProperty("start")]
        public double Start { get; set; }

        [JsonProperty("end")]
        public double End { get; set; }

        [JsonProperty("label")]
        public string Label { get; set; } = "";

        [JsonProperty("color")]
        public string Color { get; set; } = "#7B5CFF";

        [JsonExtensionData]
        public IDictionary<string, JToken>? UnknownFields { get; set; }
    }
}
