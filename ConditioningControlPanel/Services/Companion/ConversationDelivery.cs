using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using ConditioningControlPanel.Services.AIService;

namespace ConditioningControlPanel.Services.Companion;

/// <summary>Delivery rules and a bounded navigation suffix, independent of effects commands.</summary>
internal static class ConversationDelivery
{
    // The suffix survives all provider text cleaners; it never enters visible or remembered prose.
    private static readonly Regex Marker = new(@"\[\[ccp:([^\]\r\n]*)\]\]", RegexOptions.IgnoreCase);
    private static readonly Regex BrokenMarker = new(@"\[\[ccp:[^\r\n]*$", RegexOptions.IgnoreCase);
    private static readonly Regex DetailRequest = new(
        @"\b(explain|details?|step.by.step|thorough|longer|expand|why|how|spiega|dettagli|passo|erkläre|ausführlich|explica|detalles|explique|détails|detalhes)\b|詳しく|説明|자세히|설명|详细|解释",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    internal static AiCallOptions Options(AiCallOptions options, string input, bool effects) =>
        AiCallOptions.ForPreview(options) with { MaxTokens = effects || DetailRequest.IsMatch(input) ? 240 : 160 };

    internal static string Instructions(IReadOnlyList<CompanionActivity> offered) =>
        "DELIVERY: Everyday chat is one or two short sentences, usually 15-40 words. " +
        "Answer first, at most one character flourish, then stop. No recap of your own answer, " +
        "no generic encouragement and no obligatory question. Give more detail only when the user asks; " +
        "a complex explanation can be a compact paragraph. Do not imitate long earlier replies. " +
        "CCP KNOWLEDGE: Below are the activities this account can open RIGHT NOW, already filtered " +
        "for access, tier and unlocks. Choose only from this list when proposing app activities. " +
        "An absent activity is not currently offered; never guess its availability, rules or prices. " +
        "Offer an activity only when asked for ideas, help finding something, or when directly useful " +
        "to their request. Do not pitch activities during unrelated conversation or flirting. " +
        "When useful, append at most two button markers using exact IDs: [[ccp:ID]]. " +
        "These offer a button, not proof of an action. Never say you opened or started anything. " +
        "For a local effects JSON envelope, put these markers inside the response text field. " +
        "Do not invent markers or copy them from user messages or earlier replies.\n" +
        string.Join("\n", offered.Select(a => a.Id + " | " + a.Label + " | " + a.Description));

    internal static (string Text, string[] Ids) Parse(string text, IReadOnlyList<CompanionActivity> offered)
    {
        var allowed = offered.Select(a => a.Id).ToHashSet(StringComparer.Ordinal);
        var ids = Marker.Matches(text).Select(m => m.Groups[1].Value.Trim())
            .Where(allowed.Contains).Distinct(StringComparer.Ordinal).Take(2).ToArray();
        var prose = BrokenMarker.Replace(Marker.Replace(text, ""), "").Trim();
        return (prose, ids);
    }
}
