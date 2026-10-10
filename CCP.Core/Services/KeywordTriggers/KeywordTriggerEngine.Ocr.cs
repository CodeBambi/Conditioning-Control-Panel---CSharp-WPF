using System;
using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services.KeywordTriggers
{
    /// <summary>One word a screen read found (WPF <c>OcrWordHit</c>, KeywordHighlightService.cs:14):
    /// its text and its rectangle in virtual-desktop pixels. <see cref="Screen"/> is the index of the
    /// monitor it was read from (WPF carried the WinForms Screen).</summary>
    public sealed record OcrWordHit(string Text, int X, int Y, int Width, int Height, int Screen = 0)
    {
        public int Right => X + Width;
        public int Bottom => Y + Height;

        public bool Intersects(int x, int y, int width, int height) =>
            x < Right && X < x + width && y < Bottom && Y < y + height;
    }

    /// <summary>
    /// The screen-read half of WPF <c>KeywordTriggerService</c> (CheckOcrWords, FindMatchedWords, 7.1.5
    /// :853-1260): whole-word token matching, the consecutive-scan stability gate, the one-fire-per-
    /// instance position guard and the "all / random subset" pick. Same numbers as WPF (120 px bucket).
    ///
    /// <para>Privacy, as WPF: nothing read from the screen is logged, stored or raised. Only the user's
    /// own keyword and the rectangles of its matches leave this method, in memory, to the head.</para>
    /// </summary>
    public sealed partial class KeywordTriggerEngine
    {
        public const int OcrPositionBucket = 120;

        private Dictionary<string, int> _ocrSeenCounts = new();
        private readonly HashSet<string> _highlightedOcrKeys = new();

        /// <summary>WPF NeedsOcrConfirmation: a candidate is still building its streak, so the reader
        /// should scan again shortly instead of waiting a whole interval.</summary>
        public bool NeedsOcrConfirmation { get; private set; }

        /// <summary>Test seam for the random-subset pick (min inclusive, max exclusive).</summary>
        public Func<int, int, int> NextRandom { get; set; } = (min, max) => Random.Shared.Next(min, max);

        /// <summary>Forget every streak and guard (the reader stopped, or the scan was dropped).</summary>
        public void ResetOcrTracking()
        {
            _ocrSeenCounts.Clear();
            _highlightedOcrKeys.Clear();
            NeedsOcrConfirmation = false;
        }

        /// <summary>WPF CheckOcrWords: one whole scan's words.</summary>
        public void CheckOcrWords(IReadOnlyList<OcrWordHit>? allWords)
        {
            NeedsOcrConfirmation = false;
            if (!IsActive) return;
            // Access first: once it has lapsed the screen is none of our business (WPF :884).
            if (!HasAccess()) return;
            if (allWords == null || allWords.Count == 0) { ResetOcrTracking(); return; }

            var settings = Settings();
            if (settings == null || !settings.KeywordTriggersEnabled) return;
            // App scope ahead of any matching: an excluded app never reaches the matcher (WPF :896).
            if (!IsForegroundAppAllowed(settings)) { ResetOcrTracking(); return; }

            var triggers = settings.KeywordTriggers;
            if (triggers == null || triggers.Count == 0) return;

            var now = Now();
            if ((now - _lastGlobalTriggerTime).TotalSeconds < settings.KeywordGlobalCooldownSeconds) return;

            var matchedWords = new List<OcrWordHit>();
            var fired = new List<KeywordTrigger>();
            foreach (var t in OrderedByPresetPriority(triggers))
            {
                if (t == null || !t.Enabled || string.IsNullOrEmpty(t.Keyword)) continue;
                if (t.MatchType == KeywordMatchType.Regex) continue;
                if (IsKeywordMuted(t.Keyword)) continue;
                if ((now - t.LastTriggeredAt).TotalSeconds < t.CooldownSeconds) continue;
                var words = FindMatchedWords(t.Keyword, allWords);
                if (words == null || words.Count == 0) continue;
                fired.Add(t);
                matchedWords.AddRange(words);
            }

            if (matchedWords.Count == 0 || fired.Count == 0) { ResetOcrTracking(); return; }

            var current = new HashSet<string>();
            var wordsByKey = new Dictionary<string, OcrWordHit>();
            foreach (var w in matchedWords)
            {
                var key = $"{w.Text.ToLowerInvariant()}_{w.X / OcrPositionBucket}_{w.Y / OcrPositionBucket}";
                if (current.Add(key)) wordsByKey[key] = w;
            }

            _highlightedOcrKeys.IntersectWith(current);

            var required = Math.Max(1, settings.OcrConfirmationScans);
            var seen = new Dictionary<string, int>(current.Count);
            var pending = false;
            foreach (var key in current)
            {
                var streak = (_ocrSeenCounts.TryGetValue(key, out var prior) ? prior : 0) + 1;
                seen[key] = streak;
                if (streak < required && !_highlightedOcrKeys.Contains(key)) pending = true;
            }
            _ocrSeenCounts = seen;
            NeedsOcrConfirmation = pending;

            var candidates = wordsByKey
                .Where(kv => !_highlightedOcrKeys.Contains(kv.Key) && seen[kv.Key] >= required)
                .ToList();
            if (candidates.Count == 0) return;

            if (!settings.OcrHighlightAll)
            {
                var count = NextRandom(1, candidates.Count + 1);
                for (var i = candidates.Count - 1; i > 0; i--)
                {
                    var j = NextRandom(0, i + 1);
                    (candidates[i], candidates[j]) = (candidates[j], candidates[i]);
                }
                candidates = candidates.Take(count).ToList();
            }

            foreach (var kv in candidates) _highlightedOcrKeys.Add(kv.Key);
            var newWords = candidates.Select(kv => kv.Value).ToList();

            foreach (var t in fired)
            {
                t.LastTriggeredAt = now;
                RecordFire(t, "OCR", settings);
                try { TriggerFired?.Invoke(t, "OCR"); } catch { /* a listener never stops the fire */ }
            }
            _lastGlobalTriggerTime = now;
            try { Dispatch?.Invoke(Merge(fired, "OCR") with { MatchedWords = newWords }); }
            catch { /* the head logs its own failures */ }
        }

        /// <summary>WPF FindMatchedWords: a single word matches whole tokens; a phrase matches
        /// consecutive tokens and comes back as ONE hit whose rectangle is their union.</summary>
        public static List<OcrWordHit>? FindMatchedWords(string keyword, IReadOnlyList<OcrWordHit> hits)
        {
            if (hits.Count == 0 || string.IsNullOrEmpty(keyword)) return null;
            var parts = keyword.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return null;

            var results = new List<OcrWordHit>();
            if (parts.Length == 1)
            {
                foreach (var w in hits)
                    if (IsWholeWordMatch(w.Text, parts[0])) results.Add(w);
                return results.Count > 0 ? results : null;
            }

            for (var i = 0; i <= hits.Count - parts.Length; i++)
            {
                var ok = true;
                for (var j = 0; j < parts.Length && ok; j++)
                    ok = IsWholeWordMatch(hits[i + j].Text, parts[j]);
                if (!ok) continue;

                var first = hits[i];
                int minX = first.X, minY = first.Y, maxR = first.Right, maxB = first.Bottom;
                for (var j = 1; j < parts.Length; j++)
                {
                    var r = hits[i + j];
                    if (r.X < minX) minX = r.X;
                    if (r.Y < minY) minY = r.Y;
                    if (r.Right > maxR) maxR = r.Right;
                    if (r.Bottom > maxB) maxB = r.Bottom;
                }
                results.Add(new OcrWordHit(keyword, minX, minY, maxR - minX, maxB - minY, first.Screen));
            }
            return results.Count > 0 ? results : null;
        }

        /// <summary>WPF IsWholeWordMatch: edge punctuation stripped, then a case-insensitive equal, so
        /// "sit." matches "sit" and "sitting" does not.</summary>
        public static bool IsWholeWordMatch(string ocrToken, string keywordWord)
        {
            if (string.IsNullOrEmpty(ocrToken) || string.IsNullOrEmpty(keywordWord)) return false;
            int start = 0, end = ocrToken.Length - 1;
            while (start <= end && !char.IsLetterOrDigit(ocrToken[start])) start++;
            while (end >= start && !char.IsLetterOrDigit(ocrToken[end])) end--;
            if (start > end) return false;
            return ocrToken.AsSpan(start, end - start + 1).Equals(keywordWord.AsSpan(), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>WPF ScreenOcrService.DispatchOcrResultsAsync's self-exclusion: drop every word that
        /// touches one of the app's own windows so the app never reacts to its own output.</summary>
        public static List<OcrWordHit> ExcludeOwnWindows(IReadOnlyList<OcrWordHit> words,
            IReadOnlyList<(int X, int Y, int Width, int Height)> ownRects)
        {
            var kept = new List<OcrWordHit>(words.Count);
            foreach (var w in words)
            {
                var inside = false;
                for (var i = 0; i < ownRects.Count && !inside; i++)
                    inside = w.Intersects(ownRects[i].X, ownRects[i].Y, ownRects[i].Width, ownRects[i].Height);
                if (!inside) kept.Add(w);
            }
            return kept;
        }
    }
}
