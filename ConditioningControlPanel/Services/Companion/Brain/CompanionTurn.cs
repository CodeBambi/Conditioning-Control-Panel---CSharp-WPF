using System;
using ConditioningControlPanel.Services.AIService;

namespace ConditioningControlPanel.Services.Companion.Brain
{
    /// <summary>
    /// What kind of thing happened in the companion's conversation. Doc 01 §1.2.
    /// </summary>
    public enum TurnKind
    {
        /// <summary>Something the user typed in the chat box.</summary>
        UserChat,

        /// <summary>An LLM reply that was shown to the user.</summary>
        AssistantChat,

        /// <summary>
        /// A compressed record of a one-shot trigger — "finished mandatory video 'X'",
        /// "level up → 41". One line, ≤25 tokens, carried as a user-role message wearing the
        /// <see cref="CompanionTurn.EventOpen"/> sigil so it can never be mistaken for typed input
        /// and so an echo of it is recognisable in the reply.
        /// </summary>
        AmbientEvent,

        /// <summary>
        /// The model's answer to an <see cref="AmbientEvent"/> — an unprompted quip, not a reply to
        /// anything the user typed. Assistant-role on the wire so the live window knows what she just
        /// said (and a "why'd you say that?" follow-up has context), but deliberately NOT
        /// <see cref="CompanionTurn.IsDialogue"/>, so it never reaches disk.
        ///
        /// <para>The event that caused it is not persisted either (it is stale the moment it is over),
        /// and persisting one half of the pair is worse than persisting neither: the next launch would
        /// restore a run of assistant one-liners with nothing they were answering, which is exactly the
        /// few-shot bait that made ambient calls stateless in the first place. Awareness replies also
        /// name the app/tab they saw, and the toggle that governs disk writes is labelled *chat*
        /// memory — browsing commentary is not what a user ticks that box about.</para>
        /// </summary>
        AmbientReply,

        /// <summary>
        /// What the pre-recorded voice just said out loud, carried as an assistant-role line so the
        /// model knows what "she" already said and doesn't repeat it. Flavor, not content: capped
        /// hard in the prompt window and never persisted (barks replay from bark_rules.json).
        /// </summary>
        BarkEcho,

        /// <summary>
        /// Session boundary / housekeeping marker ("app closed", "mod switched to Sissy").
        /// Never sent verbatim to a model — excluded from every prompt window.
        /// </summary>
        SystemNote
    }

    /// <summary>
    /// One entry in the companion's turn log. Immutable; <see cref="ChatSession"/> owns the ordering.
    ///
    /// <see cref="ApproxTokens"/> is cached at construction because window assembly re-reads it on
    /// every call. The estimate is chars/4 throughout the brain — the same arithmetic
    /// <see cref="AiMeter"/> uses, so the two numbers are comparable in a log.
    /// </summary>
    public sealed record CompanionTurn(
        string Id,
        DateTime Utc,
        TurnKind Kind,
        string Text,
        string? Mood,
        bool Voiced,
        int ApproxTokens)
    {
        /// <summary>Opening sigil of an <see cref="TurnKind.AmbientEvent"/> line.</summary>
        public const string EventOpen = "«event: ";
        /// <summary>Closing sigil of an <see cref="TurnKind.AmbientEvent"/> line.</summary>
        public const string EventClose = "»";
        /// <summary>Opening sigil of a <see cref="TurnKind.BarkEcho"/> line (the companion's name is filled in).</summary>
        public const string SpokenAloudClose = "»";

        /// <summary>
        /// Builds a turn, stamping <see cref="Utc"/>, a fresh <see cref="Id"/> and the token estimate.
        /// <paramref name="utc"/> is injectable so window/TTL logic is testable without a clock.
        /// </summary>
        public static CompanionTurn Create(TurnKind kind, string text, string? mood = null,
            bool voiced = false, DateTime? utc = null)
        {
            var body = text ?? string.Empty;
            return new CompanionTurn(
                Id: Guid.NewGuid().ToString("N"),
                Utc: utc ?? DateTime.UtcNow,
                Kind: kind,
                Text: body,
                Mood: mood,
                Voiced: voiced,
                ApproxTokens: ChatSession.ApproxTokens(body));
        }

        /// <summary>Real dialogue — the only two kinds that persist to disk.</summary>
        public bool IsDialogue => Kind is TurnKind.UserChat or TurnKind.AssistantChat;

        /// <summary>
        /// Wire role. AmbientEvents ride as user-role (they are things that happened *to* her),
        /// BarkEchoes as assistant-role (they are things she said). SystemNotes have no role — they
        /// never reach a model; callers must filter them out before mapping.
        /// </summary>
        public string Role => Kind switch
        {
            TurnKind.AssistantChat => ChatMessage.RoleAssistant,
            TurnKind.AmbientReply => ChatMessage.RoleAssistant,
            TurnKind.BarkEcho => ChatMessage.RoleAssistant,
            _ => ChatMessage.RoleUser
        };

        /// <summary>
        /// The exact text sent on the wire. <see cref="Text"/> is stored raw (so the memory panel
        /// and the persisted file stay readable); the sigils are applied here, once, at the edge.
        /// </summary>
        public string WireText => Kind switch
        {
            TurnKind.AmbientEvent => EventOpen + Text + EventClose,
            TurnKind.BarkEcho => Text,
            _ => Text
        };

        /// <summary>Maps this turn onto a transport message. Never call on a SystemNote.</summary>
        public ChatMessage ToMessage() => new(Role, WireText);

        /// <summary>
        /// Formats a bark line as the assistant-role "she already said this out loud" echo.
        /// <paramref name="speaker"/> is the active mod's companion name.
        /// </summary>
        public static string FormatBarkEcho(string speaker, string line) =>
            $"«{speaker} said aloud: \"{(line ?? string.Empty).Trim()}\"{SpokenAloudClose}";
    }
}
