using System;
using System.Text;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// The ONE normaliser every lock-card answer comparison runs through.
    ///
    /// <para><b>The bug (ccp-bugs#1169).</b> A card whose phrase carried a typographic ellipsis
    /// (U+2026) was unpassable: the user types three full stops, the phrase holds one character,
    /// and an ordinal compare says no forever. There is no keyboard route to U+2026, so the card
    /// could never be solved and the run could not move on. The same trap is set by curly quotes
    /// and apostrophes (U+2018/U+2019/U+201C/U+201D), which Word, the Mod Creator's autocorrect
    /// and every AI-written phrase produce by default, and by non-breaking spaces.</para>
    ///
    /// <para>Normalising is safe here because a lock card asks the user to RECITE a line, not to
    /// reproduce its typography. Case is already ignored by the callers; this adds punctuation
    /// shape and whitespace on top.</para>
    ///
    /// <para>Pure and static so every compare path shares one definition - the session lock card,
    /// a lockdown card and a mod-supplied or AI-supplied phrase all funnel through
    /// <c>LockCardWindow</c> - and so the contract is unit testable without a window.</para>
    /// </summary>
    internal static class LockCardText
    {
        /// <summary>
        /// Folds a phrase or a typed answer to its comparable form: typographic punctuation
        /// flattened to ASCII, whitespace collapsed to single spaces, ends trimmed. Case is left
        /// alone - callers compare with <see cref="StringComparison.OrdinalIgnoreCase"/>.
        /// </summary>
        internal static string Normalize(string? text)
        {
            if (string.IsNullOrEmpty(text)) return "";

            var sb = new StringBuilder(text.Length + 4);
            bool pendingSpace = false;

            foreach (var raw in text)
            {
                // Every flavour of space (incl. NBSP U+00A0 and the narrow/figure spaces) folds to
                // one, and a run of them never reaches the output as more than a single space.
                // A leading run never opens one and a trailing run is never flushed: that is the trim.
                if (char.IsWhiteSpace(raw))
                {
                    if (sb.Length > 0) pendingSpace = true;
                    continue;
                }

                if (IsInvisible(raw)) continue;

                if (pendingSpace)
                {
                    sb.Append(' ');
                    pendingSpace = false;
                }
                sb.Append(MapPunctuation(raw));
            }

            return sb.ToString();
        }

        /// <summary>
        /// True when a typed answer matches an expected phrase once both are normalised.
        /// </summary>
        internal static bool Matches(string? typed, string? expected)
            => string.Equals(Normalize(typed), Normalize(expected), StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// True when what has been typed so far is still on track for the phrase. Drives the
        /// mistake counter, which must not score an error just because the user is spelling an
        /// ellipsis out as three characters where the phrase holds one.
        /// </summary>
        internal static bool IsPrefixOf(string? typed, string? expected)
        {
            var t = Normalize(typed);
            if (t.Length == 0) return true;
            return Normalize(expected).StartsWith(t, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Zero-width and format marks: they carry no sound, cannot be typed, and are exactly what
        /// a paste out of a rich-text editor leaves behind. Dropped outright.
        /// </summary>
        private static bool IsInvisible(char c)
            => c == (char)0x00AD      // soft hyphen
            || c == (char)0x200B      // zero-width space
            || c == (char)0x200C      // zero-width non-joiner
            || c == (char)0x200D      // zero-width joiner
            || c == (char)0xFEFF;     // BOM / zero-width no-break space

        /// <summary>
        /// One character in, its ASCII shape out. Multi-character expansions come back as a
        /// string, which is why this is not a char-to-char map. Every entry here is a VISIBLE
        /// character, so the map stays reviewable in a diff.
        /// </summary>
        private static string MapPunctuation(char c) => c switch
        {
            '…' => "...",                                                  // horizontal ellipsis
            '‘' or '’' or '‛' or 'ʼ' => "'",                 // curly / modifier apostrophes
            '“' or '”' or '‟' => "\"",                            // curly double quotes
            '–' or '—' or '‒' or '―' or '−' => "-",     // en / em / figure / bar / minus
            _ => c.ToString(),
        };
    }
}
