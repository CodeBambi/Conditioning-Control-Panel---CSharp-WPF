using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using ConditioningControlPanel.Services.AIService;
using ConditioningControlPanel.Services.Companion.Brain;
using ConditioningControlPanel.Services.Moderation;

namespace ConditioningControlPanel.Services.Companion;

internal static class ConversationDelivery
{
    // Angle tags survive the deployed proxy's bracket-scaffolding filter.
    private static readonly Regex Marker = new(@"<ccp-action>([^<\r\n]*)</ccp-action>", RegexOptions.IgnoreCase);
    private static readonly Regex BrokenMarker = new(@"<ccp-action\b[^\r\n]*$|\[\[ccp:[^\r\n]*$|\[\s*\]|\[video link\]", RegexOptions.IgnoreCase);
    private static readonly Regex DetailRequest = new(
        @"\b(explain|details?|step.by.step|thorough|longer|expand|spiega|dettagli|erkläre|ausführlich|explica|detalles|explique|détails|detalhes)\b|詳しく|説明|자세히|설명|详细|解释",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static bool Matches(string input, string pattern) => Regex.IsMatch(input, pattern, RegexOptions.IgnoreCase);
    internal static bool WantsMedia(string input) => Matches(input,
        @"\b(any|suggest|recommend|pick|find|show|want|give|got|have|another)\b.{0,55}\b(video|watch|listen|audio|file|clip|media)\b|\b(video|audio|file)\b.{0,25}\b(for me|please|recommend)\b");
    internal static bool WantsActivity(string input) => Matches(input,
        @"\b(suggest|recommend|pick|find|show|open)\b.{0,50}\b(games?|activities|activity|something|things?|options?|studio|presets?|quests?)\b|\bwhat (can|could|should) (i|we) (do|try|play)\b|\bwhat can you do\b|\b(another idea|bored|help me pick)\b");

    internal static CompanionActivity[] Select(IReadOnlyList<CompanionActivity> candidates, string input, IReadOnlyList<CompanionTurn> turns)
    {
        var media = WantsMedia(input);
        var explicitAsk = media || WantsActivity(input);
        var declined = DeclinesSuggestions(input);
        var sinceSuggestion = turns.Reverse().TakeWhile(t => t.ActivityIds.Length == 0).Count(t => t.Kind == TurnKind.UserChat);
        var gentleNudge = !turns.Where(t => t.Kind == TurnKind.UserChat).TakeLast(8).Any(t => DeclinesSuggestions(t.Text))
            && sinceSuggestion >= 8 && turns.Count(t => t.Kind == TurnKind.AssistantChat) >= 8
            && Matches(input, @"\b(my goal|my routine|my practice|daily session|consistency|stay consistent)\b");
        if (declined || (!explicitAsk && !gentleNudge)) return Array.Empty<CompanionActivity>();
        return candidates.Where(a => a.Allowed && (media ? a.Id == "page.assets"
            : explicitAsk ? !a.Id.StartsWith("media.", StringComparison.Ordinal) : a.Id == "page.presets"))
            .Take(10).ToArray();
    }

    private static bool DeclinesSuggestions(string input) => Matches(input,
        @"\b(no|not|don't|do not|stop|rather)\b.{0,45}\b(games?|suggestions?|suggest|recommendations?|recommend|activities|activity|videos?)\b");

    internal static AiCallOptions Options(AiCallOptions options, string input, bool effects) =>
        AiCallOptions.ForPreview(options) with { MaxTokens = effects || DetailRequest.IsMatch(input) ? 240 : 120 };

    internal static string Instructions(IReadOnlyList<CompanionActivity> offered) =>
        "DELIVERY: Answer the latest message in character. Everyday chat: one brief thought, 15-35 words. " +
        "At most one small flourish. Stop there. No generic encouragement, sales pitch or compulsory question. " +
        "Only explain at length when asked. Never imitate a verbose or promotional earlier reply. " +
        "Respect a declined suggestion; return to the user's topic. Never claim to browse or to have watched/played something. " +
        (offered.Count == 0
            ? "No suggestions this turn. Stay with the conversation. If asked for media and no catalog is supplied, admit you have no matching link; never invent a title, URL or placeholder."
            : "AVAILABLE NOW: Only these entries are offered, with current access checked. Choose one when relevant, never invent another. " +
              "Use its exact title and append <ccp-action>ID</ccp-action> for a working button (at most two). " +
              "The app opens the saved destination only after a click. Never claim it already ran. " +
              "Never type URLs, [video link], or bracket action markers. For an effects envelope, keep the tag inside response text.\n" +
              string.Join("\n", offered.Take(10).Select(a => Field(a.Id, 80) + " | " + Field(a.Label, 120) + " | " + Field(a.Description, 220))));

    internal static PromptRequest Apply(PromptRequest request, string input, IReadOnlyList<CompanionActivity> offered, bool emi)
    {
        var extra = Instructions(offered) + (emi ? "\n" + EmiVoiceExamples.For(input) : string.Empty);
        if (WantsMedia(input)) extra += "\nYou have no retrieved video link. The library button opens the media library, not a specific video. Say this plainly; never substitute an invented video or link.";
        var original = string.Join("\n\n", request.Messages.Where(m => m.Role == ChatMessage.RoleSystem).Select(m => m.Content))
            .Replace(SafetyComposer.Floor, string.Empty).Trim();
        var system = AiService.MiddleCutSystemPrompt(original, 10000 - extra.Length - SafetyComposer.Floor.Length - 4)
            + "\n\n" + extra + "\n\n" + SafetyComposer.Floor;
        var messages = new List<ChatMessage> { ChatMessage.System(system) };
        messages.AddRange(request.Messages.Where(m => m.Role != ChatMessage.RoleSystem));
        return new PromptRequest(system, messages);
    }

    private static string Field(string text, int limit) => text.Length <= limit ? text : text[..limit];

    internal static (string Text, string[] Ids) Parse(string text, IReadOnlyList<CompanionActivity> offered)
    {
        var allowed = offered.Select(a => a.Id).ToHashSet(StringComparer.Ordinal);
        var ids = Marker.Matches(text).Select(m => m.Groups[1].Value.Trim())
            .Where(allowed.Contains).Distinct(StringComparer.Ordinal).Take(2).ToArray();
        var prose = BrokenMarker.Replace(Marker.Replace(text, ""), "").Trim();
        return (prose, ids);
    }
}
