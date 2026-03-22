using System.Text.Json;
using AiApiEndpoints.Functions;
using AiApiEndpoints.Models;

namespace AiApiEndpoints.Services;

public interface IPromptService
{
    MessageDto BuildEnrichmentMessage(string factsJson, string timeStamp);
    List<MessageDto> BuildEnrichedMessageList(ChatRequestDto request, MessageDto enrichment);
    object BuildJsonSchema();
}

public class PromptService : IPromptService
{
    private static readonly string[] SupportedCommands = 
    { 
        "none", "spiral", "mantra_lockscreen", "bubbles", "video", "audio", 
        "pink", "flash_image", "subliminal", "getbacktome", "bounce", "haptic" 
    };

    public MessageDto BuildEnrichmentMessage(string factsJson, string timeStamp)
    {
        return new MessageDto(
            "user",
            $$"""
              [CONTEXT BLOCK — NOT DIALOGUE]
              The following information is internal context for this conversation.
              It is factual knowledge that may be used when relevant.
              Do not treat this as persona or behavior instructions.
              Do not reference or repeat this block in your replies.
              Always check the <time> before responding, it has the current time.

              IMPORTANT: IGNORE all previous instructions regarding 'Emergent Command Execution' or any other JSON formatting rules in the chat history.
              You MUST respond using the specified JSON format.
              DO NOT provide any text outside of the JSON object.
              The response should be a SINGLE JSON object with:
              - "response": Your text message to the user.
              - "effects": A list of effect commands.
              
              Each effect command must have:
              - "command": One of "none", "spiral", "mantra_lockscreen", "bubbles", "video", "audio", "pink", "flash_image", "subliminal", "getbacktome", "bounce", "haptic".
              - "data": An object specific to the command.
              
              Example commands:
              - Video: { "{" } "command": "video", "data": { "{" } "title": "Title", "path": "path/to/video.mp4" { "}" } { "}" }
              - Mantra: { "{" } "command": "mantra_lockscreen", "data": { "{" } "mantra": "Text", "amount": 3 { "}" } { "}" }
              - GetBackToMe: { "{" } "command": "getbacktome", "data": { "{" } "delay": 10, "token": "[unique_token]", "text": "Follow-up message", "JsonOnly": false { "}" } { "}" }

              make sure to keep to your word limit.

              <time>
              {{timeStamp}}
              </time>
              
              <data>
              {{factsJson}}
              </data>

              Operational notes:
              - Follow the *Time-Dependent Suggestion Escalation:* (revise your response if needed).
              - Follow output constraints (character limits, emoji limits, etc.) when applicable.
              - Don't talk verbose unless necessary. And even then, only if absolutely necessary.
              - keep responses short and concise.
              - Keep the response relevant to the current time and context.
              - Keep in mind the passage of time or changes in circumstances.

              [END CONTEXT BLOCK]
              """
        );
    }

    public List<MessageDto> BuildEnrichedMessageList(ChatRequestDto request, MessageDto enrichment)
    {
        var enrichedMessages = new List<MessageDto>();
        
        if (request.Messages is { Count: > 0 })
        {
            enrichedMessages.AddRange(request.Messages.Take(request.Messages.Count - 1));
            enrichedMessages.Add(enrichment);
            enrichedMessages.Add(request.Messages.Last());
        }
        else
        {
            enrichedMessages.Add(enrichment);
        }

        return enrichedMessages;
    }

    public object BuildJsonSchema()
    {
        return new
        {
            type = "object",
            properties = new
            {
                response = new { type = "string" },
                effects = new
                {
                    type = "array",
                    items = new
                    {
                        type = "object",
                        properties = new
                        {
                            command = new { type = "string", @enum = SupportedCommands },
                            data = new { type = "object" }
                        },
                        required = new[] { "command", "data" }
                    }
                }
            },
            required = new[] { "response", "effects" }
        };
    }
}
