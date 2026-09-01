using System.Text.Json.Serialization;

namespace ConditioningControlPanel.Models.CommandData
{
    public record MantraLockscreen(
        [property: JsonPropertyName("mantra")] string Mantra,
        [property: JsonPropertyName("amount")] int Amount
    ) : IAiCommandData;
}
