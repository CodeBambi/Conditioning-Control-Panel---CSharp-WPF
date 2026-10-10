using System.Collections.Generic;
using System.Globalization;

namespace ConditioningControlPanel.Avalonia.Controls
{
    /// <summary>A label split into its leading icon cluster(s), the text, and whether the text
    /// ended with a mirror of the first icon ("⚠ SPOILERS BELOW ⚠").</summary>
    public readonly record struct IconSplit(string[] Icons, string Rest, bool Mirror);

    /// <summary>
    /// Splits Core strings such as "⚙ System" or "⭐⭐ Hard" into icon clusters + text. Walks
    /// grapheme clusters, so VS16 ("⚙️") and ZWJ ("👯‍♀️") sequences stay whole. Only icon clusters
    /// are taken: "+15 XP" keeps its "+". The JSON is never edited; the head renders the split.
    /// </summary>
    public static class IconText
    {
        public static IconSplit Split(string? s)
        {
            if (string.IsNullOrEmpty(s)) return new IconSplit(System.Array.Empty<string>(), "", false);
            var icons = new List<string>();
            int i = 0;
            while (true)
            {
                while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
                if (i >= s.Length) break;
                string c = StringInfo.GetNextTextElement(s, i);
                if (!IsIconCluster(c)) break;
                icons.Add(c);
                i += c.Length;
            }
            string rest = s[i..].TrimEnd();
            bool mirror = false;
            if (icons.Count > 0 && rest.Length > 0)
            {
                var elems = StringInfo.ParseCombiningCharacters(rest);
                string last = rest[elems[^1]..];
                if (IconMap.Normalize(last) == IconMap.Normalize(icons[0]) && elems.Length > 1)
                {
                    rest = rest[..elems[^1]].TrimEnd();
                    mirror = true;
                }
            }
            return new IconSplit(icons.ToArray(), rest, mirror);
        }

        /// <summary>The text without its leading icon(s) or mirror.</summary>
        public static string Bare(string? s) => Split(s).Rest;

        /// <summary>An icon cluster: mapped in <see cref="IconMap"/>, carrying VS16, or starting in
        /// a pictograph/symbol block (the oracle's guard ranges; .NET has no Extended_Pictographic).</summary>
        public static bool IsIconCluster(string cluster)
        {
            if (string.IsNullOrEmpty(cluster)) return false;
            if (IconMap.TryGet(cluster, out _) || cluster.Contains('\uFE0F')) return true;
            if (System.Text.Rune.DecodeFromUtf16(cluster, out var rune, out _) != System.Buffers.OperationStatus.Done) return false;
            int r = rune.Value;
            return r is >= 0x1F000 and <= 0x1FAFF or >= 0x2600 and <= 0x27BF or >= 0x2B00 and <= 0x2BFF
                or >= 0x2190 and <= 0x21FF or >= 0x2300 and <= 0x23FF or >= 0x25A0 and <= 0x25FF
                or 0x2122 or 0x2139 or 0x3030 or 0x303D;
        }
    }
}
