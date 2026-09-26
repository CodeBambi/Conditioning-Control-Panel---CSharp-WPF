using System;
using System.Collections.Generic;
using ConditioningControlPanel.Services.Companion.Brain;

namespace ConditioningControlPanel.Services.Companion;

/// <summary>Familiarity from actual chat use. No streak, decay, lock or pressure to return.</summary>
internal static class ConversationRelationship
{
    internal static int Stage(int acceptedTurns) => acceptedTurns >= 40 ? 2 : acceptedTurns >= 8 ? 1 : 0;
    internal static int Turns(IReadOnlyDictionary<string, RelationshipState> relationships, string? modId)
    {
        var key = string.IsNullOrWhiteSpace(modId) ? "default" : modId.Trim();
        return relationships.TryGetValue(key, out var state) ? Math.Max(0, state.ChatTurnsTotal) : 0;
    }
    internal static string? PromptContext(IReadOnlyDictionary<string, RelationshipState> relationships, string? modId)
        => Stage(Turns(relationships, modId)) switch
        {
            1 => "FAMILIARITY: there have been several chats. Skip repeated introductions. Use only supplied facts for callbacks; keep the user's preferred tone.",
            2 => "FAMILIARITY: this is a regular conversation. A relaxed, familiar tone is welcome when the user wants it. Never invent shared events, escalate intimacy, or pressure a return.",
            _ => null
        };
}
