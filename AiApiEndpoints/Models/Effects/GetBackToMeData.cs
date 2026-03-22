using System.Text.Json.Serialization;
using AiApiEndpoints.Enum;

namespace AiApiEndpoints.Models.Effects;

public class GetBackToMeData
{
    [JsonPropertyName("delay")]
    public int Delay { get; set; }

    [JsonPropertyName("token")]
    public string Token { get; set; } = string.Empty;

    [JsonPropertyName("commands")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public List<Command>? Commands { get; set; }

    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("JsonOnly")]
    public bool JsonOnly { get; set; }
}
