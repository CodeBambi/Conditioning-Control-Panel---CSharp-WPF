using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace ConditioningControlPanel.Models.AiEnrichment
{
    public record MessageDto(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] string Content,
        [property: JsonPropertyName("images")] List<string>? Images = null,
        [property: JsonPropertyName("tool_calls")] object? ToolCalls = null
    );
}
