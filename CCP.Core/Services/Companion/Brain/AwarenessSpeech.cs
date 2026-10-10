using System;
using System.Threading;
using System.Threading.Tasks;

namespace ConditioningControlPanel.Services.Awareness
{
    /// <summary>
    /// The companion's mouth, as the arbiter sees it. Two ways to speak and one question:
    /// "what is on screen right now?", which is the only thing that can tell a fresh line from a
    /// stale one (doc 02 §4.3).
    ///
    /// <para>It is an interface because the whole "exactly one reaction per frame" guarantee is a
    /// counting argument, and a counting argument that can only be checked by watching a speech bubble
    /// is not one anybody will check. Every decision-table row in
    /// <c>AwarenessArbiterDecisionTests</c> counts calls on a fake of this.</para>
    /// </summary>
    public interface IAwarenessSpeaker
    {
        /// <summary>
        /// The app id in the foreground NOW, or null when it cannot be determined.
        ///
        /// <para><b>Null means "unknown", not "different".</b> The staleness rule drops a line only
        /// when it can prove the user has moved on; an unknowable foreground is the normal case for an
        /// unclassified window, and dropping every line for it would be a silent mute.</para>
        /// </summary>
        string? CurrentAppId { get; }

        /// <summary>
        /// Speaks a canned, voiced, free bark for this frame through the existing BarkService path —
        /// including its own gates, which stay in force (BarkService's 60s global min-gap remains the
        /// outer floor for non-safety lines).
        /// </summary>
        /// <returns>True only if a line actually reached the user.</returns>
        bool TrySpeakBark(ContextFrame frame);

        /// <summary>
        /// Speaks a model-written line through the existing speech-bubble entry point (priority bubble,
        /// AI badge, double-bounce for the rare tiers) and mutes it for OCR/keyword matching.
        /// </summary>
        /// <returns>True only if the line was handed to the bubble path.</returns>
        bool TrySpeakLine(string line, RarityTier tier);
    }

    /// <summary>
    /// Where an awareness line comes from. Implemented in production by
    /// <see cref="BrainAwarenessLineSource"/>.
    ///
    /// <para>It does NOT route through <c>App.Brain.ReactAsync</c> — the reaction prompt is its own
    /// small one, sent straight to <c>IAiService.SendAsync</c>, which is where the moderation spine
    /// lives and therefore still applies unchanged. What that path does not give it for free is the
    /// brain's single-flight gate and its turn log, so it asks <c>CompanionBrain.IsBusy</c> itself and
    /// stands down when a chat call is in flight (ambient requests are dropped when busy), and an
    /// awareness line does not land in the chat thread. Train 4's memory seam reconnects the latter.</para>
    /// </summary>
    public interface IAwarenessLineSource
    {
        /// <summary>Whether an LLM line could be produced at all right now (entitlement, provider, kill switches).</summary>
        bool IsAvailable { get; }

        /// <summary>
        /// Asks for one line about this frame. Must never throw for an ordinary failure — return
        /// <see cref="AwarenessReply.Empty"/> and let the arbiter fall back to a bark.
        /// </summary>
        Task<AwarenessReply> RequestAsync(ContextFrame frame, CancellationToken cancellationToken);
    }

    /// <summary>
    /// What came back from the model, as the arbiter consumes it.
    ///
    /// <para><b>The response contract</b> (the prompt package owns instructing the model;
    /// <see cref="AwarenessReactionService.Parse"/> owns honouring it, and is the ONLY parser — this
    /// type used to carry a second one that expected <c>ALT:</c> while the shipped prompt teaches
    /// <c>CALLBACK:</c>, so any caller that reached for "the obvious parser on the type that models
    /// the contract" would have folded every callback into the spoken line and silently killed the
    /// staleness re-tag):</para>
    /// <list type="bullet">
    /// <item>One line of text — what she says.</item>
    /// <item>An optional second line beginning <c>CALLBACK:</c> — the same beat written as a past-tense
    /// callback ("I saw you on X a minute ago…"), carried here as <see cref="Alternate"/>. It is used
    /// ONLY when the line arrived too late and the user has already moved on; a present-tense line
    /// about the wrong window is the single most common way this feature reads as broken (doc 02 §4.3).</item>
    /// <item><c>[PASS]</c> alone — she has nothing good. Nothing is said and nothing is spent.</item>
    /// </list>
    ///
    /// <para>Model text is untrusted in the same sense authored card text is: it is echoed into a
    /// bubble and back into later prompts as the ban list. The sanitising lives in the one parser.</para>
    /// </summary>
    public sealed record AwarenessReply(string? Line, string? Alternate, bool IsPass)
    {
        /// <summary>The silence token, as the one parser recognises it.</summary>
        public const string PassSentinel = AwarenessReactionService.PassToken;

        /// <summary>Prefix marking the stale-delivery callback line, as the shipped prompt teaches it.</summary>
        public const string CallbackPrefix = AwarenessReactionService.CallbackPrefix;

        /// <summary>Hard cap on a delivered line, owned by the one parser's clamp.</summary>
        public const int MaxLineLength = AwarenessReactionService.MaxLineLength;

        /// <summary>Nothing usable came back. The arbiter falls back to a bark exactly once.</summary>
        public static readonly AwarenessReply Empty = new(null, null, false);

        /// <summary>She chose silence. Honoured, logged, and not charged to the budget.</summary>
        public static readonly AwarenessReply Pass = new(null, null, true);

        /// <summary>True when there is a primary line worth delivering.</summary>
        public bool HasLine => !string.IsNullOrWhiteSpace(Line);

        /// <summary>True when a past-tense callback variant was offered for the stale case.</summary>
        public bool HasAlternate => !string.IsNullOrWhiteSpace(Alternate);

        /// <summary>
        /// Parses raw model text through <see cref="AwarenessReactionService.Parse"/> — the parser the
        /// production path already uses — so there is exactly one implementation of one contract.
        /// Anything it cannot make sense of becomes <see cref="Empty"/>, which costs a bark, not a crash.
        /// </summary>
        public static AwarenessReply Parse(string? raw)
        {
            var reaction = AwarenessReactionService.Parse(raw);
            if (reaction == null) return Empty;
            if (reaction.Passed) return Pass;
            if (!reaction.HasLine) return Empty;
            return new AwarenessReply(reaction.Line, reaction.Callback, IsPass: false);
        }
    }
}
