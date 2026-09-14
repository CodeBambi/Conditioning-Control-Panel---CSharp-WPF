using System;

namespace ConditioningControlPanel.Services.Banner
{
    /// <summary>
    /// A read split into its two beats. The body lands first, holds, and then the tag arrives on
    /// its own with a small hit of its own. Every authored rd_ line is written to end on a short
    /// tag ("hm?", "didn't you.", "thought so."), which is the whole reason the interlude is worth
    /// five seconds instead of one.
    /// </summary>
    public readonly struct MarqueeReadBeats
    {
        public MarqueeReadBeats(string body, string tag)
        {
            Body = body;
            Tag = tag;
        }

        /// <summary>Everything up to the tag. Never empty for a real line.</summary>
        public string Body { get; }

        /// <summary>The closer. Empty only when the line had no separable tail at all.</summary>
        public string Tag { get; }

        public bool HasTag => !string.IsNullOrWhiteSpace(Tag);
    }

    /// <summary>
    /// The tag splitter. Pure string work so it can be pinned by tests against the shipped pool.
    ///
    /// <para>The rule: find the last sentence-ish break (". ", "? ", "! " or ", "), take what
    /// follows it, and accept that as the tag when it is three words or fewer. Anything longer
    /// means the line does not end on a tag the way the pool is authored, so the split falls back
    /// to the last two words, which still gives the second beat something to land on.</para>
    /// </summary>
    public static class MarqueeReadSplit
    {
        /// <summary>A tag is never longer than this, or it is just the end of a sentence.</summary>
        private const int MaxTagWords = 3;

        private static readonly string[] Breaks = { ". ", "? ", "! ", ", " };

        public static MarqueeReadBeats Split(string? line)
        {
            var text = (line ?? string.Empty).Trim();
            if (text.Length == 0) return new MarqueeReadBeats(string.Empty, string.Empty);

            int cut = -1;
            foreach (var brk in Breaks)
            {
                int i = text.LastIndexOf(brk, StringComparison.Ordinal);
                if (i > cut) cut = i;
            }

            if (cut >= 0)
            {
                // +2 clears the punctuation and the space it sits on; every break is two chars.
                var body = text.Substring(0, cut + 1).Trim();
                var tag = text.Substring(cut + 2).Trim();
                if (body.Length > 0 && tag.Length > 0 && WordCount(tag) <= MaxTagWords)
                    return new MarqueeReadBeats(body, tag);
            }

            return LastTwoWords(text);
        }

        /// <summary>
        /// The fallback. Two words off the end, whatever they are, as long as something is left in
        /// front of them: a one or two word line is all body and the interlude plays it as one beat.
        /// </summary>
        private static MarqueeReadBeats LastTwoWords(string text)
        {
            int seen = 0;
            for (int i = text.Length - 1; i > 0; i--)
            {
                if (text[i] != ' ') continue;
                // Skip a run of spaces so "a  b" does not count twice.
                if (i > 0 && text[i - 1] == ' ') continue;

                seen++;
                if (seen < 2) continue;

                var body = text.Substring(0, i).Trim();
                var tag = text.Substring(i + 1).Trim();
                if (body.Length > 0 && tag.Length > 0) return new MarqueeReadBeats(body, tag);
                break;
            }
            return new MarqueeReadBeats(text, string.Empty);
        }

        public static int WordCount(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0;
            int n = 0;
            bool inWord = false;
            foreach (var c in text)
            {
                if (c == ' ')
                {
                    inWord = false;
                    continue;
                }
                if (!inWord) { n++; inWord = true; }
            }
            return n;
        }
    }
}
