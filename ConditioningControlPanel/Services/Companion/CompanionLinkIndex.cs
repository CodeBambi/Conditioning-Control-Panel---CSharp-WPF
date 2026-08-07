using System;
using System.Collections.Generic;
using System.Linq;

namespace ConditioningControlPanel.Services.Companion
{
    /// <summary>
    /// The catalogue of links the app itself can stand behind: the built-in clickable content plus
    /// whatever the user put in their global knowledge base.
    ///
    /// <para>Two jobs, both born from the same live bug (2026-08-06). Asked "any video to train
    /// on?" while the active mod had no configured links, the model invented YouTube URLs out of
    /// training data — plausible-looking, unvetted, and in one case not even a valid video id. The
    /// prompt's ban on links only existed in the branch that HAD links to offer, so the one case
    /// with nothing real to say was also the one case under no prohibition.</para>
    ///
    /// <list type="number">
    ///   <item><see cref="IsSanctioned"/> answers "did we give her this link?" for
    ///   <see cref="AiTextHygiene.StripUnsanctionedLinks"/> — prompt rules are advisory for a small
    ///   model, so the hygiene pass is the part that actually holds.</item>
    ///   <item><see cref="FindMentionedTitle"/> turns a title she NAMED into a link the app owns, so
    ///   the chat can offer a real, working affordance instead of her guessing at a URL.</item>
    /// </list>
    ///
    /// <para>Rebuilt on demand from a settings fingerprint: the knowledge base is user-editable at
    /// runtime, and a stale index would either strip a link the user just added or offer one they
    /// just deleted.</para>
    /// </summary>
    internal static class CompanionLinkIndex
    {
        /// <summary>A title short enough to appear inside ordinary prose by accident is not a
        /// reliable BARE mention. "Overload" (8) is a real catalogue entry; matching it inside
        /// "sensory overload" would attach a video to a sentence that never suggested one. Short
        /// titles still match when QUOTED ("watch \"Overload\"") — quoting is the deliberate-
        /// mention signal, so the length gate no longer disables them outright.</summary>
        internal const int MinimumTitleLength = 10;

        /// <summary>Shortest title indexed at all. Below this even a quoted match is noise.</summary>
        internal const int AbsoluteMinimumTitleLength = 4;

        private static readonly object Gate = new();
        private static string? _fingerprint;
        private static Entry[] _entries = Array.Empty<Entry>();
        private static HashSet<string> _urls = new(StringComparer.OrdinalIgnoreCase);

        internal readonly record struct Entry(string Title, string Url);

        /// <summary>Diagnostics + tests: how many times the index was actually rebuilt.</summary>
        internal static int BuildCount { get; private set; }

        /// <summary>
        /// True when <paramref name="url"/> is one the app handed her. Compared without the
        /// trailing punctuation a sentence tends to leave on a URL, and ignoring case because a
        /// model rarely echoes a link back byte-exact.
        /// </summary>
        internal static bool IsSanctioned(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;
            EnsureCurrent();
            lock (Gate) return _urls.Contains(Canonicalize(url));
        }

        /// <summary>
        /// Finds the catalogue title mentioned in <paramref name="text"/>, longest first so
        /// "Bambi TikTok - In Beat" wins over a shorter title contained inside it. Short titles
        /// (under <see cref="MinimumTitleLength"/>) only match when quoted. When no exact mention
        /// exists, falls back to a fuzzy token match on quoted/Title-Case spans — the model
        /// paraphrases pool titles far more often than it lands them verbatim. Returns null when
        /// nothing is named — the common case, and it must stay cheap.
        /// </summary>
        internal static Entry? FindMentionedTitle(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            EnsureCurrent();

            Entry[] entries;
            lock (Gate) entries = _entries;

            foreach (var entry in entries)          // pre-sorted longest-first
            {
                var at = text.IndexOf(entry.Title, StringComparison.OrdinalIgnoreCase);
                if (at < 0 || !IsWholeMention(text, at, entry.Title.Length)) continue;
                if (entry.Title.Length < MinimumTitleLength && !IsQuotedMention(text, at, entry.Title.Length))
                    continue;
                return entry;
            }

            // Fuzzy fallback: a near-miss of a real title ("Bambi TikTok Mix 1-8" for
            // "Bambi TikTok 1-8") still deserves its chip.
            var pool = entries.Select(e => (e.Title, e.Url)).ToList();
            foreach (var (start, length) in CompanionTitleMatcher.CandidateSpans(text))
            {
                if (length < CompanionTitleMatcher.MinSpanLength) continue;
                var fuzzy = CompanionTitleMatcher.BestFuzzy(text.Substring(start, length), pool);
                if (fuzzy != null) return new Entry(fuzzy.Value.Title, fuzzy.Value.Url);
            }
            return null;
        }

        /// <summary>Quoted = deliberately named: the char just outside either boundary is a quote.</summary>
        private static bool IsQuotedMention(string text, int start, int length)
        {
            const string Quotes = "\"“”‘’'";
            bool before = start > 0 && Quotes.IndexOf(text[start - 1]) >= 0;
            var end = start + length;
            bool after = end < text.Length && Quotes.IndexOf(text[end]) >= 0;
            return before && after;
        }

        /// <summary>
        /// The match must not be the middle of a longer word. Quotes, brackets and ordinary
        /// punctuation around a title are normal ("Watch <i>"Naughty Bambi"</i> for me~"), so only
        /// letters and digits on the boundary disqualify it.
        /// </summary>
        private static bool IsWholeMention(string text, int start, int length)
        {
            if (start > 0 && char.IsLetterOrDigit(text[start - 1])) return false;
            var end = start + length;
            return end >= text.Length || !char.IsLetterOrDigit(text[end]);
        }

        private static string Trim(string url) => url.Trim().TrimEnd('.', ',', ')', ']', '!', '?', ';', ':', '"', '\'');

        /// <summary>
        /// Canonical comparison form: scheme differences (http/https), a "www." prefix, host
        /// case, and a trailing slash are all the SAME link — a model echoing a pool URL rarely
        /// reproduces it byte-exact, and the strip deletes the whole sentence on a miss, so a
        /// legitimate suggestion used to die over "https vs http". Query strings are kept: on
        /// tube sites they select the video.
        /// </summary>
        private static string Canonicalize(string url)
        {
            var trimmed = Trim(url);
            if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var u) ||
                (u.Scheme != Uri.UriSchemeHttp && u.Scheme != Uri.UriSchemeHttps))
                return trimmed.ToLowerInvariant();

            var host = u.Host.ToLowerInvariant();
            if (host.StartsWith("www.", StringComparison.Ordinal)) host = host[4..];
            var path = u.AbsolutePath.TrimEnd('/');
            return host + path.ToLowerInvariant() + u.Query.ToLowerInvariant();
        }

        private static void EnsureCurrent()
        {
            var fingerprint = Fingerprint();
            lock (Gate)
            {
                if (_fingerprint == fingerprint) return;
                Rebuild(fingerprint);
            }
        }

        private static void Rebuild(string fingerprint)
        {
            var entries = new List<Entry>();
            var urls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                // The ACTIVE MOD'S POOL FIRST — user override, else the mod's shipped
                // DefaultVideoLinks. This is what she is actually told to name, so it is the set
                // that most needs a working chip; omitting it would also strip her own pool's URLs
                // as "unsanctioned" if she ever echoed one.
                var pool = App.Mods?.GetVideoLinks();
                if (pool != null)
                    foreach (var kvp in pool)
                        Add(entries, urls, kvp.Key, kvp.Value);

                foreach (var (title, url) in BambiSprite.ContentCatalogue)
                    Add(entries, urls, title, url);

                var kb = App.Settings?.Current?.GlobalKnowledgeBaseLinks;
                if (kb != null)
                    foreach (var link in kb)
                        Add(entries, urls, link?.Title, link?.Url);
            }
            catch (Exception ex)
            {
                // A half-built index would silently sanction fewer links than it should, and the
                // caller strips whatever is not sanctioned — so fail to the empty index (strip
                // everything) rather than to a partial one, and say why.
                App.Logger?.Warning(ex, "CompanionLinkIndex: failed to build the sanctioned-link index");
                entries.Clear();
                urls.Clear();
            }

            entries.Sort((a, b) => b.Title.Length.CompareTo(a.Title.Length));
            _entries = entries.ToArray();
            _urls = urls;
            _fingerprint = fingerprint;
            BuildCount++;
        }

        private static void Add(List<Entry> entries, HashSet<string> urls, string? title, string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return;
            urls.Add(Canonicalize(url));

            // Short titles are indexed too — FindMentionedTitle demands a QUOTED mention for
            // them, so "Overload"/"Fixation" can finally chip without prose false-positives.
            if (string.IsNullOrWhiteSpace(title) || title.Trim().Length < AbsoluteMinimumTitleLength) return;
            entries.Add(new Entry(title.Trim(), url.Trim()));
        }

        /// <summary>
        /// Cheap stand-in for "did the catalogue change". The built-in list is compile-time
        /// constant; the two things that move are the knowledge base and the active mod's pool —
        /// and a mod switch replaces the whole pool, so the mod id has to be in here or a switch
        /// would keep sanctioning the previous mod's links.
        /// </summary>
        private static string Fingerprint()
        {
            var sb = new System.Text.StringBuilder("mod:").Append(App.Mods?.ActiveModId ?? "none");

            // Contents, not Count: renaming a row or swapping a URL in place used to leave a
            // stale index that stripped the just-edited link as unsanctioned.
            var pool = App.Mods?.GetVideoLinks();
            sb.Append("|pool:").Append(pool?.Count ?? 0);
            if (pool != null)
                foreach (var kvp in pool) sb.Append('|').Append(kvp.Key).Append('#').Append(kvp.Value);

            var kb = App.Settings?.Current?.GlobalKnowledgeBaseLinks;
            sb.Append("|kb:").Append(kb?.Count ?? 0);
            if (kb != null)
                foreach (var link in kb) sb.Append('|').Append(link?.Url).Append('#').Append(link?.Title);

            return sb.ToString();
        }

        /// <summary>Tests only: forget the cached index so the next call rebuilds.</summary>
        internal static void ResetForTests()
        {
            lock (Gate)
            {
                _fingerprint = null;
                _entries = Array.Empty<Entry>();
                _urls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }
        }
    }
}
