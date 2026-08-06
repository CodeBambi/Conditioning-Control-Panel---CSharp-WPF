using System;
using System.Text;
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
    /// <see cref="BrainAwarenessLineSource"/>, which routes through <c>App.Brain.ReactAsync</c> so the
    /// moderation spine, the single-flight gate and the turn log all apply unchanged.
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
    /// What came back from the model, parsed.
    ///
    /// <para><b>The response contract this parses</b> (the prompt package owns instructing the model;
    /// this owns honouring it):</para>
    /// <list type="bullet">
    /// <item>One line of text — what she says.</item>
    /// <item>An optional second line beginning <c>ALT:</c> — the same beat written as a past-tense
    /// callback ("I saw you on X a minute ago…"). It is used ONLY when the line arrived too late and
    /// the user has already moved on; a present-tense line about the wrong window is the single most
    /// common way this feature reads as broken (doc 02 §4.3).</item>
    /// <item><c>[PASS]</c> alone — she has nothing good. Nothing is said and nothing is spent.</item>
    /// </list>
    ///
    /// <para>Model text is untrusted in the same sense authored card text is: it is echoed into a
    /// bubble and back into later prompts as the ban list. Every field goes through
    /// <see cref="AwarenessText.SanitizeDisplayName"/>, which strips control characters and rejects
    /// anything shaped like a role marker.</para>
    /// </summary>
    public sealed record AwarenessReply(string? Line, string? Alternate, bool IsPass)
    {
        /// <summary>The silence token. Trim- and case-tolerant when parsed.</summary>
        public const string PassSentinel = "[PASS]";

        /// <summary>Prefix marking the stale-delivery alternate line.</summary>
        public const string AlternatePrefix = "ALT:";

        /// <summary>
        /// Hard cap on a delivered line. The output contract asks for ≤140 characters; this is the
        /// backstop that stops a model ignoring it from pasting an essay into the bubble.
        /// </summary>
        public const int MaxLineLength = 400;

        /// <summary>Nothing usable came back. The arbiter falls back to a bark exactly once.</summary>
        public static readonly AwarenessReply Empty = new(null, null, false);

        /// <summary>She chose silence. Honoured, logged, and not charged to the budget.</summary>
        public static readonly AwarenessReply Pass = new(null, null, true);

        /// <summary>True when there is a primary line worth delivering.</summary>
        public bool HasLine => !string.IsNullOrWhiteSpace(Line);

        /// <summary>True when a past-tense callback variant was offered for the stale case.</summary>
        public bool HasAlternate => !string.IsNullOrWhiteSpace(Alternate);

        /// <summary>
        /// Parses raw model text into the contract above. Tolerant by design: a model that answers with
        /// just the line, or wraps <c>[PASS]</c> in quotes, or puts the ALT line first, all parse.
        /// Anything it cannot make sense of becomes <see cref="Empty"/>, which costs a bark, not a crash.
        /// </summary>
        public static AwarenessReply Parse(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return Empty;

            string? alternate = null;
            var primary = new StringBuilder();

            var normalized = raw.Replace("\r\n", "\n").Replace('\r', '\n');
            foreach (var rawLine in normalized.Split('\n'))
            {
                var line = rawLine.Trim();
                if (line.Length == 0) continue;

                if (line.StartsWith(AlternatePrefix, StringComparison.OrdinalIgnoreCase))
                {
                    // Last ALT wins; a model that emits two has already lost the plot, and taking the
                    // first would silently prefer the one it changed its mind about.
                    alternate = line.Substring(AlternatePrefix.Length).Trim();
                    continue;
                }

                if (primary.Length > 0) primary.Append(' ');
                primary.Append(line);
            }

            var text = primary.ToString().Trim();
            if (IsPassToken(text)) return Pass;

            var line1 = AwarenessText.SanitizeDisplayName(text, MaxLineLength);
            var line2 = AwarenessText.SanitizeDisplayName(alternate, MaxLineLength);

            if (line1.Length == 0 && line2.Length == 0) return Empty;
            return new AwarenessReply(
                line1.Length == 0 ? null : line1,
                line2.Length == 0 ? null : line2,
                IsPass: false);
        }

        /// <summary>
        /// Whether the whole answer is the silence token, allowing for the punctuation and quoting
        /// small models sprinkle around sentinels.
        /// </summary>
        private static bool IsPassToken(string text)
        {
            if (text.Length == 0) return false;

            var trimmed = text.Trim().Trim('"', '\'', '`', '*', '.', '!', ' ');
            return string.Equals(trimmed, PassSentinel, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(trimmed, "PASS", StringComparison.OrdinalIgnoreCase);
        }
    }
}
