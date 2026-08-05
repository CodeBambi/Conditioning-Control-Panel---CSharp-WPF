using CcpClient.Desktop.Ai;

namespace CcpClient.Desktop.Features.Companion;

/// <summary>
/// One chat bubble. Immutable: the thinking bubble is REMOVED (never mutated) when the
/// operation resolves — nothing partial ever edits into a final state (panic-quiet).
///
/// Badge truth (contract §1; admission §2 rule 4 — LOAD-BEARING): the AI badge derives
/// from the typed <see cref="AiReply"/> BY TYPE and nothing else — exactly
/// <see cref="AiReply.Generated"/> carries the badge (WPF mechanism: IsAiGenerated →
/// badge, AvatarTubeWindow.Speech.cs:426-436; the bool superseded by the typed variants).
/// A Fallback bubble with text identical to a Generated bubble carries NO badge (the
/// falsifiable pair, tested). User bubbles carry no reply and are never badged.
/// </summary>
public sealed class CompanionBubbleModel
{
    private CompanionBubbleModel(string text, AiReply? reply, bool isUser, bool isThinking)
    {
        Text = text;
        Reply = reply;
        IsUser = isUser;
        IsThinking = isThinking;
    }

    /// <summary>A user-authored bubble. Never badged, never a refusal.</summary>
    public static CompanionBubbleModel User(string text) => new(text, null, isUser: true, isThinking: false);

    /// <summary>The in-flight affordance (WPF thinking-dots shape, ChatInput.cs:705-707). Removed whole on resolve.</summary>
    public static CompanionBubbleModel Thinking() => new("…", null, isUser: false, isThinking: true);

    /// <summary>An assistant-class bubble from the typed reply (the ONLY badge authority).</summary>
    public static CompanionBubbleModel ForReply(AiReply reply)
    {
        ArgumentNullException.ThrowIfNull(reply);
        var text = reply switch
        {
            AiReply.Generated generated => generated.Text,
            AiReply.Fallback fallback => fallback.Text,
            // Refusal presentation (WPF Speech.cs:366-396; localized strings land with the
            // localization slice — the refusal CLASS is the contract, the copy is honest English).
            AiReply.Refused refused => refused.Refusal.Source == AiModerationSource.Input
                ? "This message can't be sent under the content policy."
                : "The AI declined to respond under the content policy.",
            AiReply.Unavailable unavailable => $"The companion is unavailable ({unavailable.Code}).",
            _ => "The companion returned an unknown typed state.",
        };
        return new CompanionBubbleModel(text, reply, isUser: false, isThinking: false);
    }

    public string Text { get; }

    /// <summary>The typed reply behind this bubble (null for user/thinking bubbles).</summary>
    public AiReply? Reply { get; }

    public bool IsUser { get; }

    public bool IsThinking { get; }

    /// <summary>THE badge mapping: provenance-driven, type-only. Generated → badged; Fallback/Unavailable/Refused → never.</summary>
    public bool IsAiBadged => Reply is AiReply.Generated;

    /// <summary>Refusal bubble class (interactive surfacing, contract §4 — awareness never produces these).</summary>
    public bool IsRefusal => Reply is AiReply.Refused;

    /// <summary>Typed non-generated assistant state (Fallback/Unavailable) — distinct subdued treatment, never badged.</summary>
    public bool IsSubdued => Reply is AiReply.Fallback or AiReply.Unavailable;

    /// <summary>Provenance disclosure for a badged bubble (the endpoint CLASS, contract §6 — never endpoint text).</summary>
    public string? ProvenanceClass => Reply is AiReply.Generated generated ? generated.Provenance.ToString() : null;
}
