using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Models.Deeper
{
    public class EnhancementRule
    {
        [JsonProperty("trigger")]
        public EnhancementTrigger Trigger { get; set; } = new NeverFiringTrigger();

        [JsonProperty("action")]
        public EnhancementAction Action { get; set; } = new NoOpEnhancementAction();

        // Optional region scope: rule only fires while the playhead is inside this region.
        [JsonProperty("region_constraint", NullValueHandling = NullValueHandling.Ignore)]
        public string? RegionConstraint { get; set; }

        [JsonProperty("cooldown_ms")]
        public int CooldownMs { get; set; } = 1000;

        [JsonProperty("enabled")]
        public bool Enabled { get; set; } = true;

        [JsonExtensionData]
        public IDictionary<string, JToken>? UnknownFields { get; set; }
    }
}
