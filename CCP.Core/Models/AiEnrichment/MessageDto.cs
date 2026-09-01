using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace ConditioningControlPanel.Models.AiEnrichment
{
    public record MessageDto(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] string Content,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        [property: JsonPropertyName("images")] List<string>? Images = null,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        [property: JsonPropertyName("tool_calls")] object? ToolCalls = null
    );
}
