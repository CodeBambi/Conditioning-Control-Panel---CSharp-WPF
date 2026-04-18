using System;
using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Models.AiEnrichment;

namespace ConditioningControlPanel.Services.AIService.Enrichment;

public interface IPromptService
{
    MessageDto BuildEnrichmentMessage(string factsJson, string timeStamp);
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
              Do not send the same video repeatedly.
              Videos should be rarely used, they are not a substitute for a mantra.
              Videos should be used sparingly, they are highly time-consuming and disruptive.

              
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
              - Follow output constraints (character limits, emoji limits, etc.) when applicable.
              - Keep the response relevant to the current context.
              - Keep in mind the passage of time or changes in circumstances.
              - Each bracket must be properly closed.

              [END CONTEXT BLOCK]
              """
        );
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
