using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Moderation;

namespace ConditioningControlPanel.Services.Companion;

/// <summary>A bounded conversational prefix. The media shelf supplies facts, never a sales script.</summary>
internal static class ConversationPrompt
{
    internal static string Build(CompanionPromptSettings persona, string name, bool spicy,
        bool localEffects, IEnumerable<string>? titles = null, bool preserveOutputRules = true)
    {
        var text = new StringBuilder();
        text.AppendLine("YOUR NAME: " + Field(name, 120) + ". This is your name, not the user's name. Address the user by name only when they supplied it.");
        var variant = spicy && !string.IsNullOrWhiteSpace(persona.SlutModePersonality);
        text.AppendLine(Field(variant ? persona.SlutModePersonality : persona.Personality, 3600));
        if (!variant) text.AppendLine(Field(persona.ExplicitReaction, 800));
        text.AppendLine("The current character overrides the style of earlier assistant messages. Keep the user's topic and facts, but do not imitate the previous character.");
        if (!string.IsNullOrWhiteSpace(persona.KnowledgeBase))
            text.AppendLine("CHARACTER BACKGROUND, use only when relevant:\n" + Field(persona.KnowledgeBase, 1200));
        if (preserveOutputRules && !string.IsNullOrWhiteSpace(persona.OutputRules))
            text.AppendLine("STYLE PREFERENCE:\n" + Field(persona.OutputRules, 700));
        text.AppendLine("CAPABILITIES: You can chat and use the conversation and supplied memory. You cannot browse the web, see the screen unless context is supplied, change settings, award XP, or save a memory merely by saying you did.");
        text.AppendLine(localEffects
            ? "Effects may be requested only through the supplied effect contract and its allowed actions. A request is not proof it ran."
            : "No effect controls are available in this conversation. Do not claim you triggered anything.");
        var shelf = (titles ?? Array.Empty<string>()).Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(t => t, StringComparer.OrdinalIgnoreCase)
            .Take(6).Select(t => Field(t, 100)).ToArray();
        if (shelf.Length > 0)
            text.AppendLine("AVAILABLE TITLES, only if the user asks for a recommendation: " + string.Join("; ", shelf));
        text.AppendLine("CONVERSATION: Answer the user's actual message first. Be concrete, curious and in character. Usually one to three sentences; explain enough when asked. Follow the latest correction over older preferences. Honour requested length, including sentence counts. A question is optional. Never steer ordinary conversation into media, links, breathing or a session. Do not output URLs or invent media titles. Do not repeat the last answer. Complete sentences, no raw JSON unless an explicit effects contract requires it.");
        return SafetyComposer.Wrap(text.ToString());
    }

    private static string Field(string? text, int limit)
    {
        var value = (text ?? string.Empty).Trim();
        return value.Length <= limit ? value : value[..limit];
    }
}