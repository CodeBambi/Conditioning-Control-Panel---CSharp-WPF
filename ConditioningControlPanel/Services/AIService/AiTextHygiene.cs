using System.Text.RegularExpressions;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// #739: shared cleanup for anything a language model hands back, whatever provider it came from.
    ///
    /// A user reported the companion "randomly spitting out gibberish text instead of an actual
    /// message". Two hygiene steps existed but were wired into only one of the three provider paths:
    ///
    ///   - the tokenizer-artifact strip lived as a private helper in OpenAiCompatibleService, so the
    ///     cloud path (AiService) and the local path (LocalAiService) never ran it, even though the
    ///     team had already identified that exact class of garbage;
    ///   - nothing anywhere stripped reasoning blocks. LocalAiService asks Ollama for think:false,
    ///     but the cloud proxy sends no equivalent, so a reasoning-capable model's chain of thought
    ///     would be rendered straight into the speech bubble.
    ///
    /// Centralised here so a fourth provider cannot quietly miss it again.
    /// </summary>
    internal static class AiTextHygiene
    {
        // Reasoning models wrap their scratchpad in these. Non-greedy, multi-line, and tolerant of an
        // unclosed opener: a reply truncated mid-thought would otherwise render the whole scratchpad.
        private static readonly Regex ReasoningBlock = new(
            @"<(think|thinking|reasoning|thought)>.*?(</\1>|$)",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

        // A stray closing tag left over once its opener was trimmed upstream.
        private static readonly Regex OrphanReasoningClose = new(
            @"</(think|thinking|reasoning|thought)>",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Context metadata the model echoes back instead of answering, e.g.
        // "[Category: Media | App: VLC | Title: ... | Duration: 12m]".
        private static readonly Regex ClosedCategoryTag = new(
            @"\[Category:[^\]]*\]", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Reaction category tags like [Media/Streaming] or [Gaming/Casual].
        private static readonly Regex ReactionCategoryTag = new(
            @"\[[A-Za-z]+/[A-Za-z]+\]", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Any standalone bracket tag that looks like metadata.
        private static readonly Regex ClosedMetadataTag = new(
            @"\[(?:Category|App|Title|Duration|Context):[^\]]*\]", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Replies are hard-capped at 100 tokens, so a fabricated tag can be cut off before its closing
        // bracket - and every pass above needs that bracket, so the fragment rendered raw in the bubble.
        // Both passes exclude newlines: truncation can only land on the LAST line, and letting [^\]]
        // cross \n would delete everything after a mid-reply bracket in a multi-line answer.
        // Two end-of-string passes, because the cap can land anywhere:
        //   1. a known metadata keyword (\b so "[Apple pie" isn't eaten by "App"), colon optional;
        private static readonly Regex UnclosedKnownTag = new(
            @"\[(?:Category|App|Title|Duration|Context)\b[^\]\r\n]*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        //   2. anything shaped like "[Word:" for tags we haven't seen yet - production also showed a
        //      fabricated "[Satisf: ...". The colon is what marks the fragment as metadata rather than
        //      prose, so stage directions ("[giggles") and citations ("[3") survive while an unknown
        //      "[Mood: playful" does not. Matching bare keyword prefixes instead (e.g. "[Cat") would
        //      widen this to ordinary words, which is the one failure this must not have.
        private static readonly Regex UnclosedKeyedTag = new(
            @"\[[A-Za-z][A-Za-z0-9 _-]*:[^\]\r\n]*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Strip leaked context-metadata tags, closed or truncated, then collapse the whitespace the
        /// removal leaves behind. An empty result means the reply was nothing but metadata - the caller
        /// decides what to say instead.
        /// </summary>
        internal static string StripMetadataTags(string? text)
        {
            if (string.IsNullOrEmpty(text)) return text ?? string.Empty;

            var sanitized = ClosedCategoryTag.Replace(text, "");
            sanitized = ReactionCategoryTag.Replace(sanitized, "");
            sanitized = ClosedMetadataTag.Replace(sanitized, "");
            sanitized = UnclosedKnownTag.Replace(sanitized, "");
            sanitized = UnclosedKeyedTag.Replace(sanitized, "");

            sanitized = Regex.Replace(sanitized, @"\s{2,}", " ");
            return sanitized.Trim();
        }

        // The CompanionBrain window renders bark lines to the model as
        //   «Bambi said aloud: "the rabbit hole~"»
        // (see CompanionTurn.FormatBarkEcho). Small models imitate the pattern and wrap their OWN
        // replies in it - observed live on 2026-08-06, every reply arriving as «... said aloud: "..."».
        // The speaker segment is capped so a reply that merely mentions the phrase mid-sentence
        // ("she said aloud: yes" with no « prefix, or a « that opens a long clause) is left alone.
        private static readonly Regex SpokenSigilWrapper = new(
            "^\\s*«[^«»\r\n]{0,80}?\\bsaid aloud:\\s*(?<inner>.*?)\\s*»?\\s*$",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

        /// <summary>
        /// Unwraps a reply the model delivered inside the bark-echo sigil («X said aloud: "…"»),
        /// returning the inner speech. Applied repeatedly (bounded) for the double-wrapped case.
        /// Text that does not start with the sigil is returned unchanged; an all-shell reply unwraps
        /// to empty and the caller decides what to say instead.
        /// </summary>
        internal static string UnwrapSpokenSigil(string? text)
        {
            if (string.IsNullOrEmpty(text)) return text ?? string.Empty;

            var current = TrimSigilDebris(text.Trim());
            for (int i = 0; i < 2; i++)
            {
                var m = SpokenSigilWrapper.Match(current);
                if (!m.Success) break;

                var inner = m.Groups["inner"].Value.Trim();
                // The sigil quotes its payload; shed one symmetric (or truncation-orphaned) layer.
                if (inner.StartsWith("\"", System.StringComparison.Ordinal))
                    inner = inner.Substring(1);
                if (inner.EndsWith("\"", System.StringComparison.Ordinal))
                    inner = inner.Substring(0, inner.Length - 1);

                current = TrimSigilDebris(inner.Trim());
                if (current.Length == 0) break;
            }

            return current;
        }

        // Second live shape (0806): the model closed a sigil it never opened and tacked on the
        // prompt's section separator - `...right?"» ###`. The end-anchored wrapper can't match that,
        // so trailing debris is shed first: hash-runs, then an orphan » (only when the text has no
        // opening «), then the quote the orphan close leaves unbalanced.
        private static string TrimSigilDebris(string current)
        {
            current = Regex.Replace(current, @"(?:\s|#)+$", "");

            if (current.EndsWith("»", System.StringComparison.Ordinal) && current.IndexOf('«') < 0)
            {
                current = current.Substring(0, current.Length - 1).TrimEnd();

                int quotes = 0;
                foreach (var ch in current) if (ch == '"') quotes++;
                if ((quotes & 1) == 1 && current.EndsWith("\"", System.StringComparison.Ordinal))
                    current = current.Substring(0, current.Length - 1).TrimEnd();
            }

            return current;
        }

        /// <summary>
        /// Strip tokenizer artifacts and reasoning blocks. Whitespace is normalised but the text is
        /// otherwise left alone - callers layer their own product-specific sanitising on top.
        /// </summary>
        internal static string Clean(string? text)
        {
            if (string.IsNullOrEmpty(text)) return text ?? string.Empty;

            var cleaned = ReasoningBlock.Replace(text, "");
            cleaned = OrphanReasoningClose.Replace(cleaned, "");

            // GPT-2/GPT-Neo/llama.cpp tokenizers sometimes emit 'Ġ' for leading spaces, and 'Ċ' for
            // newlines. Seeing either in user-visible text means raw tokens reached the bubble.
            cleaned = cleaned.Replace("Ġ", " ").Replace("Ċ", "\n");

            return cleaned.Trim();
        }
    }
}
