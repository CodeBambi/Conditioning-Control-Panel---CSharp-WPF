using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using ConditioningControlPanel.Helpers;

namespace ConditioningControlPanel.Services.Companion
{
    /// <summary>
    /// The pure half of WPF <c>AvatarTubeWindow.Speech.cs</c> <c>BuildLinkedInlines</c>: one line
    /// of companion text in, an ordered list of plain and linked segments out. No window, no
    /// inlines, so the rule is unit tested and both heads can draw the same links.
    ///
    /// <para>Same four sources, same priority as WPF: markdown <c>[text](url)</c> pairs (the url
    /// is the authoritative signal), exact known titles longest first, fuzzy title hits (shown as
    /// the REAL pool title), then raw urls (shown as the pool title or a readable slug).</para>
    /// </summary>
    internal static class CompanionLinkSegments
    {
        /// <summary>One piece of a line. <see cref="Url"/> null = plain text.</summary>
        internal readonly record struct Segment(string Text, string? Url)
        {
            internal bool IsLink => Url != null;
        }

        private static readonly Regex Markdown = new(@"\[([^\]]+)\]\(([^)]+)\)", RegexOptions.Compiled);
        private static readonly Regex Malformed = new(@"\s*\[Url\]|\s*\(url\)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex RawUrl = new(@"https?://[^\s,""'<>]+", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>
        /// WPF's link table: the active mod's LIVE pool (https only), overridden by the known
        /// title table, with the full built-in catalogue folded in underneath so a title the
        /// prompt could offer is never dead text after a mod swap.
        /// </summary>
        internal static Dictionary<string, string> BuildTable(
            IEnumerable<KeyValuePair<string, string>>? livePool,
            IEnumerable<KeyValuePair<string, string>>? known,
            IEnumerable<KeyValuePair<string, string>>? builtIn)
        {
            var table = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (livePool != null)
                foreach (var kvp in livePool)
                    if (!string.IsNullOrEmpty(kvp.Key) && Uri.TryCreate(kvp.Value, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps)
                        table[kvp.Key] = kvp.Value;
            if (known != null)
                foreach (var kvp in known)
                    if (!string.IsNullOrEmpty(kvp.Key)) table[kvp.Key] = kvp.Value;
            if (builtIn != null)
                foreach (var kvp in builtIn)
                    if (!string.IsNullOrEmpty(kvp.Key) && !table.ContainsKey(kvp.Key)) table[kvp.Key] = kvp.Value;
            return table;
        }

        /// <summary>The text with markdown links collapsed to their words: what the bubble shows
        /// while it is still typing, and what segment offsets are measured against.</summary>
        internal static string Flatten(string? text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            return Malformed.Replace(Markdown.Replace(text, m => m.Groups[1].Value), "");
        }

        internal static List<Segment> Parse(string? text, IReadOnlyDictionary<string, string>? linkTable)
        {
            var result = new List<Segment>();
            if (string.IsNullOrEmpty(text)) return result;
            linkTable ??= new Dictionary<string, string>();

            // Pass 1: collapse markdown to its words, remembering (words, url).
            var mdLinks = new List<(string LinkText, string Url)>();
            text = Markdown.Replace(text, m =>
            {
                var linkText = m.Groups[1].Value;
                var url = m.Groups[2].Value.Trim();
                if (IsWeb(url)) mdLinks.Add((linkText, url));
                return linkText;
            });
            text = Malformed.Replace(text, "");

            var hits = new List<(int Start, int Length, string Name, string Url)>();
            bool Overlaps(int start, int length) => hits.Any(h => start < h.Start + h.Length && start + length > h.Start);

            // Pass 2: the markdown words, now plain, claim their place first.
            foreach (var (linkText, url) in mdLinks)
            {
                var idx = text.IndexOf(linkText, StringComparison.Ordinal);
                if (idx >= 0 && !Overlaps(idx, linkText.Length)) hits.Add((idx, linkText.Length, linkText, url));
            }

            // Exact known titles, longest first so a short title never eats part of a long one.
            foreach (var kvp in linkTable.OrderByDescending(k => k.Key.Length))
            {
                if (kvp.Key.Length == 0 || !IsWeb(kvp.Value)) continue;
                var idx = text.IndexOf(kvp.Key, StringComparison.OrdinalIgnoreCase);
                if (idx >= 0 && !Overlaps(idx, kvp.Key.Length)) hits.Add((idx, kvp.Key.Length, kvp.Key, kvp.Value));
            }

            // Pass 3: fuzzy recovery for near-miss titles; the pool title is what gets shown.
            var entries = linkTable.Where(k => IsWeb(k.Value)).Select(k => (Title: k.Key, Url: k.Value)).ToList();
            foreach (var (spanStart, spanLength, _) in CompanionTitleMatcher.CandidateSpans(text))
            {
                if (spanLength < CompanionTitleMatcher.MinSpanLength || Overlaps(spanStart, spanLength)) continue;
                var fuzzy = CompanionTitleMatcher.BestFuzzy(text.Substring(spanStart, spanLength), entries);
                if (fuzzy != null) hits.Add((spanStart, spanLength, fuzzy.Value.Title, fuzzy.Value.Url));
            }

            // Raw urls the model copied out of the pool.
            foreach (Match m in RawUrl.Matches(text))
                if (!Overlaps(m.Index, m.Length) && IsWeb(m.Value)) hits.Add((m.Index, m.Length, m.Value, m.Value));

            if (hits.Count == 0)
            {
                result.Add(new Segment(text, null));
                return result;
            }

            int last = 0;
            foreach (var (start, length, name, url) in hits.OrderBy(h => h.Start))
            {
                if (start > last) result.Add(new Segment(text.Substring(last, start - last), null));

                var actual = text.Substring(start, length);
                var display = actual;
                if (actual.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    // A bare url is ugly as link text: the pool title when known, else a readable slug.
                    string? knownTitle = null;
                    foreach (var kvp in linkTable)
                        if (string.Equals(kvp.Value, url, StringComparison.OrdinalIgnoreCase)) { knownTitle = kvp.Key; break; }
                    display = knownTitle ?? HtUrlHelper.DeriveTitleFromUrl(url);
                }
                else if (!string.Equals(name, actual, StringComparison.OrdinalIgnoreCase))
                {
                    display = name;   // a fuzzy hit shows the real pool title
                }

                result.Add(new Segment(string.IsNullOrEmpty(display) ? actual : display, url));
                last = start + length;
            }
            if (last < text.Length) result.Add(new Segment(text.Substring(last), null));
            return result;
        }

        private static bool IsWeb(string? url) =>
            Uri.TryCreate(url, UriKind.Absolute, out var u) && (u.Scheme == Uri.UriSchemeHttps || u.Scheme == Uri.UriSchemeHttp);
    }
}
