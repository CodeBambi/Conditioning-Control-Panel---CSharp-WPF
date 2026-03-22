using System.Text.Json.Serialization;
using AiApiEndpoints.Enum;

namespace AiApiEndpoints.Models;

public record ChatRequestDto(
    [property: JsonPropertyName("model")] string? Model,
    [property: JsonPropertyName("messages")] List<MessageDto>? Messages,
    [property: JsonPropertyName("tools")] object? Tools = null,
    [property: JsonPropertyName("format")] object? Format = null,
    [property: JsonPropertyName("options")] Dictionary<string, object>? Options = null,
    [property: JsonPropertyName("stream")] bool? Stream = null,
    [property: JsonPropertyName("keep_alive")] string? KeepAlive = null,
    [property: JsonPropertyName("think")] bool? Think = null,
    [property: JsonPropertyName("system")] string? System = null
);

public record MessageDto(
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("content")]
    string Content,
    [property: JsonPropertyName("images")] List<string>? Images = null,
    [property: JsonPropertyName("tool_calls")]
    object? ToolCalls = null
);

public record ResponseMessage(string Role, string Content);

public class CustomChatResponse 
{
    public ResponseMessage Message { get; set; }
    public Command Effect { get; set; }
}
