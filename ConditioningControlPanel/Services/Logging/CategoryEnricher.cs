using System;
using System.Collections.Concurrent;
using Serilog.Core;
using Serilog.Events;

namespace ConditioningControlPanel.Services.Logging
{
    /// <summary>
    /// Derives the <c>Cat</c> (category) column from the message template.
    ///
    /// <para>The app has no <c>SourceContext</c> to lean on: with 7,300 call sites written as
    /// <c>App.Logger?.Information("[EMOTE] ...")</c> and <c>Log.Information("ProfileSync: ...")</c>,
    /// the category is already IN the message, just in two different hand-rolled conventions. This
    /// reads whichever one a line uses and lifts it into a real property, so the formatter can put
    /// it in a fixed column and strip the now-duplicated prefix from the text.</para>
    ///
    /// <para>Derivation runs once per distinct template, not once per event: the template TEXT is a
    /// literal from the call site, so the same string instance comes back every time. The cache is
    /// bounded and cleared wholesale when it fills, which cannot happen with a fixed set of call
    /// sites but can if someone builds templates by concatenation.</para>
    /// </summary>
    public sealed class CategoryEnricher : ILogEventEnricher
    {
        public const string PropertyName = "Cat";
        private const int CacheCap = 4096;

        private static readonly ConcurrentDictionary<string, string> _cache = new(StringComparer.Ordinal);

        public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
        {
            if (logEvent == null) return;
            // An explicit ForContext("Cat", ...) always wins.
            if (logEvent.Properties.ContainsKey(PropertyName)) return;

            var text = logEvent.MessageTemplate?.Text;
            if (string.IsNullOrEmpty(text)) return;

            if (!_cache.TryGetValue(text!, out var cat))
            {
                cat = Derive(text!);
                if (_cache.Count >= CacheCap) _cache.Clear();
                _cache[text!] = cat;
            }

            logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty(PropertyName, cat));
        }

        /// <summary>
        /// <c>[Tag] rest</c> -> Tag; <c>Word: rest</c> or <c>Word.Method: rest</c> -> Word (a
        /// trailing "Service" is dropped, because "ProfileSyncService" and "ProfileSync" are the
        /// same thing to a reader); anything else -> App.
        /// </summary>
        public static string Derive(string text)
        {
            if (text.Length > 1 && text[0] == '[')
            {
                int close = text.IndexOf(']', 1);
                if (close > 1 && close <= 20 && IsTagBody(text, 1, close))
                    return Normalise(text.Substring(1, close - 1));
                return "App";
            }

            // Scan a leading PascalCase word, optionally followed by ".Method", up to a colon.
            if (text.Length < 2 || !IsUpper(text[0])) return "App";
            int i = 1;
            while (i < text.Length && IsWordChar(text[i])) i++;
            int wordEnd = i;
            if (i < text.Length && text[i] == '.')
            {
                i++;
                int methodStart = i;
                while (i < text.Length && IsWordChar(text[i])) i++;
                if (i == methodStart) return "App";
            }
            if (i >= text.Length || text[i] != ':') return "App";
            // "Word:" with nothing after the colon but a placeholder is still a category.
            var word = text.Substring(0, wordEnd);
            if (word.Length > 7 && word.EndsWith("Service", StringComparison.Ordinal))
                word = word.Substring(0, word.Length - 7);
            return word;
        }

        /// <summary>
        /// SHOUTED tags are title-cased so <c>[AVATAR]</c> and <c>[Avatar]</c> land in one column
        /// value; a tag that already carries internal case (<c>[EmiDesk]</c>) is left alone,
        /// because lowering it would read worse than the inconsistency it fixes.
        /// </summary>
        private static string Normalise(string tag)
        {
            bool allUpper = true;
            for (int i = 0; i < tag.Length; i++)
            {
                if (tag[i] >= 'a' && tag[i] <= 'z') { allUpper = false; break; }
            }
            if (!allUpper || tag.Length < 2) return tag;
            return char.ToUpperInvariant(tag[0]) + tag.Substring(1).ToLowerInvariant();
        }

        private static bool IsTagBody(string s, int start, int end)
        {
            for (int i = start; i < end; i++)
            {
                if (!IsWordChar(s[i]) && s[i] != '-') return false;
            }
            return end > start;
        }

        private static bool IsUpper(char c) => c >= 'A' && c <= 'Z';

        private static bool IsWordChar(char c) =>
            (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_';
    }
}
