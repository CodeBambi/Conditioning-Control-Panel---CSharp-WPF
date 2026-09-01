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

        // New unified timeline. Coexists with the legacy regions/haptic_tracks/rules
        // collections during the additive-schema transition; the loader projects
        // legacy → timeline_items if this list is empty, and the saver back-projects
        // so older clients keep working.
        [JsonProperty("timeline_items")]
        public List<TimelineItem> TimelineItems { get; set; } = new();

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

        // Optional second credit slot — the local user who edited the rules/effects
        // on top of the original Creator's media. Independent from Creator so HT
        // auto-fill can lock the upstream uploader while leaving the remix slot free.
        [JsonProperty("remixer", NullValueHandling = NullValueHandling.Ignore)]
        public string? Remixer { get; set; }

        [JsonProperty("description")]
        public string Description { get; set; } = "";

        [JsonProperty("tags")]
        public List<string> Tags { get; set; } = new();

        // Detected at save time by EnhancementAutoTagger — answers
        // "what hardware do I need before opening this?". Distinct from user-set
        // Tags so re-saves can refresh without trampling creator input.
        [JsonProperty("auto_tags")]
        public List<string> AutoTags { get; set; } = new();

        [JsonProperty("license")]
        public string License { get; set; } = "";

        // Set by the catalogue/bundled-content pipeline when an enhancement is
        // officially featured. No client path sets it true today; the Director's
        // Cut achievement reads it and stays dormant until a source populates it.
        [JsonProperty("featured")]
        public bool Featured { get; set; }

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
        // Default region color MUST match DeeperAccent in
        // Resources/Theme/Colors.xaml — change both together. Kept as a
        // literal (not a DynamicResource) because Region is a serialized
        // data model object and the value lands in saved .ccpenh.json
        // files that need to round-trip stably across builds.
        public string Color { get; set; } = "#7B5CFF";

        [JsonExtensionData]
        public IDictionary<string, JToken>? UnknownFields { get; set; }
    }
}
