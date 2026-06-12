using Newtonsoft.Json;
using System.Text.Json.Serialization;

namespace ConditioningControlPanel.Models.AiEnrichment
{
    /// <summary>
    /// Shared chat message DTO used across AI provider pipelines.
    /// Providers map this to their transport-specific message types (e.g.,
    /// <see cref="ProxyChatMessage"/> for the cloud proxy, the local
    /// <c>ChatMessage</c> disk format, or an OpenAI-compatible request body)
    /// before sending.
    /// </summary>
    public sealed class AiMessage
    {
        [JsonProperty("role")]
        [JsonPropertyName("role")]
        public string Role { get; set; } = string.Empty;

        [JsonProperty("content")]
        [JsonPropertyName("content")]
        public string Content { get; set; } = string.Empty;

        public AiMessage() { }

        public AiMessage(string role, string content)
        {
            Role = role;
            Content = content;
        }
    }
}
