using System.Text.Json.Serialization;
using AiApiEndpoints.Enum;

namespace AiApiEndpoints.Models.Effects;

public class StructuredResponse
{
    [JsonPropertyName("response")]
    public string Response { get; set; } = string.Empty;

    [JsonPropertyName("effects")]
    public List<EffectCommand> Effects { get; set; } = new();
}

public class EffectCommand
{
    [JsonPropertyName("command")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public Command Command { get; set; }

    [JsonPropertyName("data")]
    public object Data { get; set; } = new();
}
