using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace ConditioningControlPanel.Services.Commands
{
    /// <summary>
    /// Finds the local video an AI request names (ccp-bugs #1330). The AI only knows a title or a
    /// file name it made up from one, never what is on disk, so the request is matched against the
    /// library by file name without its extension, case, punctuation and separators ignored.
    /// Pure: the caller hands in the file list.
    ///
    /// <para>The rule leans hard toward "no match". A named recommendation that plays an unrelated
    /// video is the #1325 report, and playing nothing is the safe miss. A match is one of:</para>
    /// <list type="bullet">
    /// <item>the same name once punctuation and spacing are gone (score 1);</item>
    /// <item>every word on both sides, in any order (0.95);</item>
    /// <item>a partial title: the shorter name's words run in order inside the longer name and
    /// cover at least half of it (0.8 to 1).</item>
    /// </list>
    /// <para>Words that differ by one letter count as the same word from five letters up
    /// ("bambi" / "bambis"). Quality tags (1080p, hd) and long numeric ids (a HypnoTube video id
    /// in a download's file name) are ignored. Two different files tied at the top below an exact
    /// match is ambiguous and is no match.</para>
    /// </summary>
    internal static class VideoTitleMatcher
    {
        /// <summary>Lowest score that counts as the named video.</summary>
        internal const double Threshold = 0.8;

        private static readonly HashSet<string> Noise = new(StringComparer.Ordinal)
        {
            "hd", "uhd", "fhd", "4k", "8k", "x264", "x265", "h264", "h265", "hevc", "html", "htm",
        };

        /// <summary>The best match for the first query that has one (queries in order: Path, then
        /// Title), or null.</summary>
        internal static string? FindBest(IEnumerable<string?> queries, IReadOnlyList<string> library)
        {
            if (library == null || library.Count == 0) return null;
            foreach (var q in queries)
            {
                var hit = FindBest(q, library);
                if (hit != null) return hit;
            }
            return null;
        }

        /// <summary>The best match for one query, or null when nothing clears the threshold or two
        /// different names tie below an exact match.</summary>
        internal static string? FindBest(string? query, IReadOnlyList<string> library)
        {
            if (library == null || library.Count == 0) return null;
            var q = Tokens(KeyOf(query));
            if (q.Count == 0) return null;

            string? best = null;
            string? bestName = null;
            double bestScore = 0;
            bool tie = false;
            foreach (var file in library)
            {
                if (string.IsNullOrWhiteSpace(file)) continue;
                var name = Path.GetFileNameWithoutExtension(file);
                var s = Score(q, Tokens(name));
                if (s > bestScore + 1e-9)
                {
                    best = file; bestName = Compact(Tokens(name)); bestScore = s; tie = false;
                }
                else if (s > 0 && Math.Abs(s - bestScore) <= 1e-9 && Compact(Tokens(name)) != bestName)
                {
                    tie = true;
                }
            }

            if (bestScore < Threshold) return null;
            if (tie && bestScore < 1.0) return null;
            return best;
        }

        /// <summary>Score of a query against a file name, 0 to 1.</summary>
        internal static double Score(string? query, string? fileName)
            => Score(Tokens(KeyOf(query)), Tokens(fileName));

        private static double Score(List<string> q, List<string> c)
        {
            if (q.Count == 0 || c.Count == 0) return 0;
            if (Compact(q) == Compact(c)) return 1.0;

            var shorter = q.Count <= c.Count ? q : c;
            var longer = ReferenceEquals(shorter, q) ? c : q;

            // Every word on both sides, any order.
            if (q.Count == c.Count && AllMatchUnordered(shorter, longer)) return 0.95;

            // A partial title: the shorter name runs in order inside the longer one.
            var ratio = (double)shorter.Count / longer.Count;
            if (ratio >= 0.5 && ContainsRun(longer, shorter)) return 0.6 + 0.4 * ratio;

            // Some words in common: scored, never enough on its own to clear the threshold
            // unless nearly everything lines up.
            var matched = shorter.Count(t => longer.Any(l => SameWord(t, l)));
            return 0.75 * matched / longer.Count;
        }

        private static bool AllMatchUnordered(List<string> a, List<string> b)
        {
            var left = new List<string>(b);
            foreach (var t in a)
            {
                var i = left.FindIndex(l => SameWord(t, l));
                if (i < 0) return false;
                left.RemoveAt(i);
            }
            return true;
        }

        private static bool ContainsRun(List<string> longer, List<string> run)
        {
            for (var start = 0; start + run.Count <= longer.Count; start++)
            {
                var ok = true;
                for (var k = 0; k < run.Count && ok; k++) ok = SameWord(run[k], longer[start + k]);
                if (ok) return true;
            }
            return false;
        }

        private static bool SameWord(string a, string b)
        {
            if (a == b) return true;
            if (a.Length < 5 || b.Length < 5) return false;
            return WithinOneEdit(a, b);
        }

        private static bool WithinOneEdit(string a, string b)
        {
            if (Math.Abs(a.Length - b.Length) > 1) return false;
            int i = 0, j = 0, edits = 0;
            while (i < a.Length && j < b.Length)
            {
                if (a[i] == b[j]) { i++; j++; continue; }
                if (++edits > 1) return false;
                if (a.Length > b.Length) i++;
                else if (b.Length > a.Length) j++;
                else { i++; j++; }
            }
            return edits + (a.Length - i) + (b.Length - j) <= 1;
        }

        /// <summary>The part of a query that names the video: a URL's last path segment, a path's
        /// file name without its extension, or the text as given.</summary>
        internal static string KeyOf(string? query)
        {
            if (string.IsNullOrWhiteSpace(query)) return "";
            var s = query.Trim();
            if (Uri.TryCreate(s, UriKind.Absolute, out var uri) &&
                (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            {
                var seg = uri.AbsolutePath.TrimEnd('/');
                var slash = seg.LastIndexOf('/');
                return slash >= 0 ? seg[(slash + 1)..] : seg;
            }
            var slashAt = s.LastIndexOfAny(new[] { '/', '\\' });
            if (slashAt >= 0) s = s[(slashAt + 1)..];
            var dot = s.LastIndexOf('.');
            if (dot > 0 && s.Length - dot is >= 3 and <= 5 && char.IsLetter(s[dot + 1]) && s[(dot + 1)..].All(char.IsLetterOrDigit))
                s = s[..dot];
            return s;
        }

        /// <summary>Lowercase words, apostrophes dropped, everything else not a letter or digit a
        /// separator, quality tags and long numeric ids left out.</summary>
        internal static List<string> Tokens(string? text)
        {
            var words = new List<string>();
            if (string.IsNullOrWhiteSpace(text)) return words;
            var sb = new StringBuilder();
            void Flush()
            {
                if (sb.Length == 0) return;
                var w = sb.ToString();
                sb.Clear();
                if (!IsNoise(w)) words.Add(w);
            }
            foreach (var ch in text.ToLowerInvariant())
            {
                if (ch == '\'' || ch == (char)0x2019) continue;
                if (char.IsLetterOrDigit(ch)) sb.Append(ch);
                else Flush();
            }
            Flush();
            return words;
        }

        private static bool IsNoise(string w)
        {
            if (Noise.Contains(w)) return true;
            if (w.Length >= 4 && w.All(char.IsDigit)) return true;                    // an id
            if (w.EndsWith('p') && w.Length >= 4 && w[..^1].All(char.IsDigit)) return true; // 1080p
            return false;
        }

        private static string Compact(List<string> tokens) => string.Concat(tokens);
    }
}
