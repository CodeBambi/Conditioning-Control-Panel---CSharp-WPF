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
        /// reliable mention. "Overload" (8) is a real catalogue entry; matching it inside "sensory
        /// overload" would attach a video to a sentence that never suggested one.</summary>
        internal const int MinimumTitleLength = 10;

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
            lock (Gate) return _urls.Contains(Trim(url));
        }

        /// <summary>
        /// Finds the catalogue title mentioned in <paramref name="text"/>, longest first so
        /// "Bambi TikTok - In Beat" wins over a shorter title contained inside it. Returns null when
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
                if (at >= 0 && IsWholeMention(text, at, entry.Title.Length)) return entry;
            }
            return null;
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
            urls.Add(Trim(url));

            if (string.IsNullOrWhiteSpace(title) || title.Trim().Length < MinimumTitleLength) return;
            entries.Add(new Entry(title.Trim(), url.Trim()));
        }

        /// <summary>
        /// Cheap stand-in for "did the catalogue change": the built-in list is compile-time constant,
        /// so only the knowledge base can move underneath us.
        /// </summary>
        private static string Fingerprint()
        {
            var kb = App.Settings?.Current?.GlobalKnowledgeBaseLinks;
            if (kb == null || kb.Count == 0) return "kb:0";

            var sb = new System.Text.StringBuilder("kb:").Append(kb.Count);
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
