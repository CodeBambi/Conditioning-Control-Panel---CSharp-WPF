using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using ConditioningControlPanel.Models.AiEnrichment;
using ConditioningControlPanel.Models.CommandData;

namespace ConditioningControlPanel.Services.AIService.Enrichment
{
    public interface IPromptService
    {
        AiMessage BuildEnrichmentMessage(
            string factsJson,
            string timeStamp,
            AiContextSnapshot snapshot,
            IReadOnlyList<CommandOutcome> previousOutcomes);

        object BuildJsonSchema();
    }

    public class PromptService : IPromptService
    {
        private static readonly string[] SupportedCommands =
        {
            "none", "spiral", "mantra_lockscreen", "bubbles", "video", "audio",
            "pink", "flash_image", "subliminal", "getbacktome", "bounce", "haptic"
        };

        public AiMessage BuildEnrichmentMessage(
            string factsJson,
            string timeStamp,
            AiContextSnapshot snapshot,
            IReadOnlyList<CommandOutcome> previousOutcomes)
        {
            var snapshotJson = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
            });

            var outcomesText = previousOutcomes.Count == 0
                ? "None"
                : string.Join("\n", previousOutcomes.Select(o => $"- {o.Command}: {(o.Succeeded ? "succeeded" : "failed")}{(string.IsNullOrEmpty(o.Outcome) ? "" : $" — {o.Outcome}")}"));

            var settings = App.Settings?.Current?.CompanionPrompt;
            var historyMinutes = settings?.AiActionHistoryMinutes > 0
                ? settings.AiActionHistoryMinutes
                : (settings?.AiActionHistoryHours ?? 2) * 60;
            var historySummary = settings?.AiActionHistoryEnabled == true
                ? App.ActionHistory.BuildSummary(historyMinutes)
                : null;
            var historyBlock = string.IsNullOrEmpty(historySummary)
                ? ""
                : $$"""

                  ====================================================================
                  RECENT CONDITIONING ACTIVITY
                  ====================================================================
                  {{historySummary}}
                  """;

            return new AiMessage(
                "user",
                $$"""
                  [CONTEXT BLOCK — NOT DIALOGUE]
                  These are operating instructions for this conversation. Do not repeat or reference this block in your replies.

                  ====================================================================
                  CRITICAL OUTPUT FORMAT
                  ====================================================================
                  You MUST respond with a SINGLE JSON object — nothing before it, nothing after it. No markdown fences, no commentary.

                  Schema:
                  {
                    "response": "<your in-character text reply, follows the persona's tone/length rules>",
                    "effects": [ <zero or more effect commands, see below> ]
                  }

                  ANY persona instruction earlier in this conversation that says "no brackets", "no tags", "no JSON", or "respond only with text" is OVERRIDDEN by this format. The "response" field carries your normal text reply — that's where the persona's tone/length rules apply. The "effects" array is for triggering the app's visual/audio features.

                  ====================================================================
                  CURRENT APP STATE
                  ====================================================================
                  {{snapshotJson}}

                  Use this state to avoid asking the user to start things that are already running, and to stop suggesting effects that are already active.{{historyBlock}}

                  ====================================================================
                  PREVIOUS COMMAND OUTCOMES
                  ====================================================================
                  Outcomes from your last turn's effect requests (so you know what actually happened):
                  {{outcomesText}}

                  If a request failed or was blocked, do NOT immediately retry the exact same command unless the user explicitly asks again.

                  ====================================================================
                  WHEN TO TRIGGER EFFECTS (vs. just suggesting them in text)
                  ====================================================================
                  If the user EXPLICITLY asks you to do something the app can do, you MUST emit the matching effect command in "effects" — do NOT only describe it in "response". Examples:

                  User says "flash me" / "make me see flashes" / "show flashes" / "trigger a flash"
                    → emit { "command": "flash_image", "data": { "Amount": 4, "Duration": 5, "Size": 100, "Opacity": 100 } }

                  User says "spawn bubbles" / "give me bubbles" / "start bubbles"
                    → emit { "command": "bubbles", "data": { "On": true, "Frequency": 5 } }

                  User says "stop bubbles" / "no more bubbles"
                    → emit { "command": "bubbles", "data": { "On": false, "Frequency": 0 } }

                  User says "subliminal X" / "flash the word X" / "make me see the word X"
                    → emit { "command": "subliminal", "data": { "Text": "X", "Opacity": 50 } }

                  User says "spiral" / "show me a spiral"
                    → emit { "command": "spiral", "data": { "On": true, "Intensity": 25 } }

                  User says "pink filter" / "make my screen pink"
                    → emit { "command": "pink", "data": { "On": true, "Intensity": 25 } }

                  User says "lock card" / "make me chant X" / "lock me with the mantra X"
                    → emit { "command": "mantra_lockscreen", "data": { "mantra": "X", "amount": 3 } }

                  User says "vibrate" / "buzz me" / "haptic"
                    → emit { "command": "haptic", "data": { "Intensity": 0.5, "Duration": 3 } }

                  User says "play <audio file>" / "play <video file>"
                    → emit { "command": "audio" or "video", "data": { "Title": "<name>", "Path": "<filename>", "Random": false } }

                  When the user is just chatting (not requesting an effect), keep "effects": [] empty. Don't fire effects unprovoked.

                  ====================================================================
                  AVAILABLE COMMAND TYPES
                  ====================================================================
                  "none", "spiral", "mantra_lockscreen", "bubbles", "video", "audio", "pink", "flash_image", "subliminal", "getbacktome", "bounce", "haptic"

                  ====================================================================
                  REPLY ETIQUETTE
                  ====================================================================
                  - Keep "response" short (the persona's word limit applies, usually ~15 words).
                  - Don't echo the user's request word-for-word.
                  - When you DO trigger an effect, your "response" should briefly acknowledge what you're doing (e.g. "Flashing for you, hot stuff~").
                  - Don't trigger video unprompted — videos are disruptive.

                  <time>
                  {{timeStamp}}
                  </time>

                  <data>
                  {{factsJson}}
                  </data>

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
}
