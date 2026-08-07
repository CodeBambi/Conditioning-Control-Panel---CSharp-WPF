using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace ConditioningControlPanel.Services.Companion
{
    /// <summary>
    /// Shared title-matching for the two surfaces that turn a video title the companion NAMED
    /// into a clickable link (the tube speech bubble and the Her Room chat chip).
    ///
    /// <para>Why this exists (owner report, 2026-08-07): the design is "she says the exact
    /// title, the app auto-links it" — she is forbidden from writing URLs. But measured across a
    /// real session, ~87% of her suggestions were paraphrases or inventions ("Bambi TikTok
    /// <b>Mix</b> 1-8 - Nonstop Edit" for the pool key "Bambi TikTok 1-8 - Nonstop Edit"), and
    /// both matchers were exact-substring — one inserted word rendered as dead text. This class
    /// adds the two recovery layers: a token-overlap fuzzy match against the pool, and a
    /// site-search fallback so a title that matches nothing still yields a working link.</para>
    /// </summary>
    internal static class CompanionTitleMatcher
    {
        /// <summary>
        /// Minimum Jaccard similarity (on normalized token sets) for a confident fuzzy hit:
        /// 0.72 accepts one inserted/dropped word on a 6-token title.
        /// </summary>
        internal const double FuzzyThreshold = 0.72;

        /// <summary>
        /// The rewrite floor: an invented title at least this close to a pool entry gets
        /// REWRITTEN to it (owner decision 2026-08-07: the pool is deliberately curated, so an
        /// off-pool suggestion becomes the nearest real video rather than a door out of the
        /// curation). Below this the title stays plain text and only the anti-repeat ban sees it.
        /// </summary>
        internal const double RewriteThreshold = 0.4;

        /// <summary>Candidate spans shorter than this are never fuzzy-matched or search-linked —
        /// a two-word quote is as likely to be prose ("good girl") as a title.</summary>
        internal const int MinSpanLength = 8;

        // DOUBLE quotes only. Straight and curly single quotes doubled as apostrophes in
        // contractions — "It's … It's" extracted the garbage span between two apostrophes
        // (live: ' from PlatinumPuppets. It') and search-linked it. Single-quoted titles are
        // rare enough to lose; contractions are in every second sentence.
        private static readonly Regex QuotedSpan = new(
            "[\"“]([^\"“”\\n]{3,80})[\"”]",
            RegexOptions.Compiled);

        // A run of 2+ Title-Cased/numeric words (optionally joined by "-"): how an unquoted
        // title-case mention looks in prose. Deliberately conservative — lowercase words other
        // than short joiners end the run.
        private static readonly Regex TitleCaseRun = new(
            @"\b(?:[A-Z][\w']*|\d[\w'-]*)(?:[ \-]+(?:[A-Z][\w']*|\d[\w'-]*|of|the|and|in|to)){1,11}\b",
            RegexOptions.Compiled);

        /// <summary>
        /// Spans of <paramref name="text"/> that plausibly contain a video title: quoted spans
        /// (the model quotes nearly every title it names — flagged so callers can prefer them),
        /// plus unquoted Title-Case runs. Ordered by position; runs overlapping an
        /// already-yielded quote are skipped.
        /// </summary>
        internal static List<(int Start, int Length, bool Quoted)> CandidateSpans(string text)
        {
            var spans = new List<(int Start, int Length, bool Quoted)>();
            if (string.IsNullOrWhiteSpace(text)) return spans;

            foreach (Match m in QuotedSpan.Matches(text))
                spans.Add((m.Groups[1].Index, m.Groups[1].Length, true));

            foreach (Match m in TitleCaseRun.Matches(text))
            {
                if (m.Length < MinSpanLength) continue;
                bool inside = spans.Any(s => m.Index < s.Start + s.Length && m.Index + m.Length > s.Start);
                if (!inside) spans.Add((m.Index, m.Length, false));
            }

            spans.Sort((a, b) => a.Start.CompareTo(b.Start));
            return spans;
        }

        /// <summary>
        /// Best fuzzy pool match for one candidate span, or null. Entries with fewer than two
        /// normalized tokens never fuzzy-match (a one-word title has no structure to be
        /// "approximately" right about — exact matching already covers it).
        /// </summary>
        internal static (string Title, string Url)? BestFuzzy(
            string span, IEnumerable<(string Title, string Url)> entries, double threshold = FuzzyThreshold)
        {
            var spanTokens = Tokens(span);
            if (spanTokens.Count < 2) return null;

            (string Title, string Url)? best = null;
            double bestScore = 0;
            foreach (var e in entries)
            {
                var entryTokens = Tokens(e.Title);
                if (entryTokens.Count < 2) continue;
                int inter = entryTokens.Count(spanTokens.Contains);
                int union = entryTokens.Count + spanTokens.Count - inter;
                if (union == 0) continue;
                double score = (double)inter / union;
                if (score > bestScore) { bestScore = score; best = e; }
            }
            return bestScore >= threshold ? best : null;
        }

        /// <summary>Lowercased alphanumeric token set ("Bambi TikTok Mix 1-8" → bambi, tiktok, mix, 1, 8).</summary>
        internal static HashSet<string> Tokens(string s)
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            if (string.IsNullOrEmpty(s)) return set;
            foreach (Match m in Regex.Matches(s.ToLowerInvariant(), @"[a-z0-9]+"))
                set.Add(m.Value);
            return set;
        }

        /// <summary>
        /// Search link for a title that matched nothing — she suggested it herself once: "try
        /// typing the title in the search bar". A working search beats dead text; the numbered
        /// episode tail is dropped because invented episode numbers poison the search.
        /// </summary>
        /// <summary>
        /// Rewrites off-pool quoted titles in <paramref name="text"/> to the nearest real pool
        /// entry (≥ <see cref="RewriteThreshold"/>). Runs at the BRAIN level, before the reply
        /// is stored: the bubble, the Her Room chip, the chat history and — critically — her
        /// own persisted few-shot history then all carry real, linkable titles, which is what
        /// breaks the invented-title imitation loop (it also self-heals across mod switches,
        /// where stored history full of the OLD mod's titles was steering the new mod's chat).
        /// Search links were deliberately dropped (owner decision): an unmatched title stays
        /// plain text and is handled by the anti-repeat ban alone.
        /// </summary>
        internal static string RewriteOffPoolTitles(
            string text, IReadOnlyList<(string Title, string Url)> pool, out int rewritten)
        {
            rewritten = 0;
            if (string.IsNullOrWhiteSpace(text) || pool.Count == 0) return text;

            var spans = CandidateSpans(text);
            // Back-to-front so replacements don't shift the indices of spans not yet visited.
            for (int i = spans.Count - 1; i >= 0; i--)
            {
                var (start, length, quoted) = spans[i];
                if (!quoted || length < MinSpanLength || length > 80) continue;
                if (!LooksLikeVideoContext(text, start)) continue;

                var span = text.Substring(start, length);
                // Already a real title (exact, case-insensitive)? Leave it — it links as-is.
                if (pool.Any(e => string.Equals(e.Title, span.Trim(), StringComparison.OrdinalIgnoreCase)))
                    continue;

                var match = BestFuzzy(span, pool, RewriteThreshold);
                if (match == null) continue;
                text = text.Substring(0, start) + match.Value.Title + text.Substring(start + length);
                rewritten++;
            }
            return text;
        }

        /// <summary>
        /// True when the text around a span reads like a video suggestion — the gate that keeps
        /// the search fallback from linkifying ordinary quoted prose ("good girl").
        /// </summary>
        internal static bool LooksLikeVideoContext(string text, int spanStart)
        {
            // Suggestion verbs included: live testing showed her leading with "Try \"X\"" and
            // "How about \"X\"" — neither contains a media noun, and the fallback never armed.
            int from = Math.Max(0, spanStart - 60);
            var before = text.Substring(from, spanStart - from);
            return Regex.IsMatch(before,
                @"\b(watch|video|clip|session|train|queue|play|loop|file|try|called|check|recommend|suggest|about)\w*\b",
                RegexOptions.IgnoreCase);
        }
    }
}
