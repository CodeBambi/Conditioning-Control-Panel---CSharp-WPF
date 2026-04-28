using Newtonsoft.Json;

namespace ConditioningControlPanel.Models
{
    public enum BucketSource
    {
        VideoSubfolder,
        ContentPack
    }

    /// <summary>
    /// A single content bucket available to the Focus Training game (Lab Box 2).
    /// Sourced from a video subfolder or an installed content pack.
    /// User toggles Include and IsTarget independently to compose target / decoy pools.
    /// </summary>
    public class FocusGameBucket
    {
        [JsonProperty] public BucketSource Source { get; set; }

        /// <summary>Stable identifier — relative folder path or pack guid.</summary>
        [JsonProperty] public string Id { get; set; } = "";

        [JsonProperty] public string DisplayName { get; set; } = "";

        [JsonProperty] public bool IsIncluded { get; set; }

        [JsonProperty] public bool IsTarget { get; set; }
    }
}
