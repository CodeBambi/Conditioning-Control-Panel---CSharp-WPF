using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace ConditioningControlPanel.Models.CommandData
{
    public record Bounce(
        List<string> Words,
        [property: JsonPropertyName("On")] bool On
    ) : IAiCommandData;
}
