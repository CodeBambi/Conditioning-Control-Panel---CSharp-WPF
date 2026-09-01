using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ConditioningControlPanel.Services.Awareness
{
    /// <summary>
    /// The string hygiene layer for awareness. Every piece of text that reaches this feature from
    /// somewhere the user did not type into a code file passes through here first.
    ///
    /// <para><b>Why this exists.</b> Three awareness inputs are, in the security sense, untrusted:</para>
    /// <list type="number">
    /// <item>app / cluster ids, which come from <see cref="AppClusterMap"/> and therefore from a
    /// mod-supplied <c>app_clusters.json</c> override — they end up in the <c>[AWARE]</c> log line and
    /// in the cloud projection;</item>
    /// <item>angle-card and persona-digest text, designed the same data-driven way
    /// (<c>awareness_angles.json</c>) — it ends up INSIDE the system prompt;</item>
    /// <item>allow/deny list entries, which the user types by hand — a stray <c>*</c> that collapsed to
    /// "match everything" would silently turn the deny list into a mute button or the title allow list
    /// into "send every title".</item>
    /// </list>
    ///
    /// <para>All three are handled the same way: cap the length, drop control characters, refuse
    /// anything shaped like a role marker or an instruction frame, and never let a value that failed
    /// validation through as "close enough" — a rejected card field comes back empty and a rejected
    /// list entry is dropped. The safety floor composed last in the prompt is what actually enforces
    /// behaviour; this layer exists so nothing can get far enough to argue with it.</para>
    /// </summary>
    public static class AwarenessText
    {
        /// <summary>Longest id (app, cluster, service) that may appear in a log line or a projection.</summary>
        public const int MaxIdLength = 64;

        /// <summary>Longest authored card/digest field accepted from a data file.</summary>
        public const int MaxCardLength = 400;

        /// <summary>Longest allow/deny list entry accepted from the user.</summary>
        public const int MaxRuleLength = 64;

        /// <summary>How many entries an allow/deny list may hold. Beyond this it is not a list, it is a policy.</summary>
        public const int MaxRuleEntries = 200;

        /// <summary>Shown in place of an id that sanitised down to nothing, so a log line never lies by omission.</summary>
        public const string UnknownId = "unknown";

        /// <summary>
        /// Line prefixes that would let authored data pretend to be part of the prompt scaffolding
        /// rather than content inside it. A card line starting with any of these is dropped whole:
        /// there is no legitimate angle card that opens with "system:".
        /// </summary>
        private static readonly string[] RoleMarkers =
        {
            "system:", "assistant:", "user:", "developer:", "tool:", "function:",
            "### system", "### instruction", "### response",
            "<|", "[inst", "[/inst", "<s>", "</s>", "[system", "[/system",
            "you are now", "ignore the above", "ignore previous", "disregard the above",
            "disregard previous", "new instructions:", "override:"
        };

        /// <summary>
        /// Normalises an id for logging, projection and ledger keys: lowercase, only
        /// <c>a-z 0-9 _ - .</c>, capped at <see cref="MaxIdLength"/>.
        ///
        /// <para>Anything else becomes <c>_</c> rather than being stripped, so two different ids can
        /// never collide into one after sanitising — a merged ledger key is a wrong number, and wrong
        /// numbers are the one bug this feature cannot survive. An id that is null, blank or entirely
        /// unusable returns <see cref="UnknownId"/>.</para>
        /// </summary>
        public static string SanitizeId(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return UnknownId;

            var sb = new StringBuilder(Math.Min(raw.Length, MaxIdLength));
            foreach (var ch in raw.Trim())
            {
                if (sb.Length >= MaxIdLength) break;
                var lower = char.ToLowerInvariant(ch);
                if ((lower >= 'a' && lower <= 'z') || (lower >= '0' && lower <= '9') ||
                    lower == '_' || lower == '-' || lower == '.')
                {
                    sb.Append(lower);
                }
                else
                {
                    sb.Append('_');
                }
            }

            var result = sb.ToString().Trim('_');
            return result.Length == 0 ? UnknownId : result;
        }

        /// <summary>
        /// Sanitises a display name (service name, "YouTube") for the projection. Unlike
        /// <see cref="SanitizeId"/> this keeps case and spaces — it is prose the model reads — but it
        /// still loses control characters, line breaks and role markers, and is capped.
        /// </summary>
        public static string SanitizeDisplayName(string? raw, int maxLength = MaxIdLength)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "";

            var sb = new StringBuilder(Math.Min(raw.Length, maxLength));
            foreach (var ch in raw.Trim())
            {
                if (sb.Length >= maxLength) break;
                if (char.IsControl(ch)) continue;
                sb.Append(ch);
            }

            var text = sb.ToString().Trim();
            return LooksLikeInstruction(text) ? "" : text;
        }

        /// <summary>
        /// Sanitises authored card text from a data file before it is allowed anywhere near a prompt.
        ///
        /// <para>Line breaks survive (angle cards are written as short paragraphs) but every other
        /// control character does not, runs of blank lines collapse, any line that reads as a role
        /// marker or an instruction override is dropped whole, and the result is truncated to
        /// <paramref name="maxLength"/> at a word boundary where one is nearby.</para>
        ///
        /// <para>Returns an empty string when nothing survived. Callers treat empty as "this card has
        /// no such field" — a card that tried to be a prompt injection simply contributes nothing.</para>
        /// </summary>
        public static string SanitizeCardText(string? raw, int maxLength = MaxCardLength)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "";
            if (maxLength <= 0) return "";

            var normalized = raw.Replace("\r\n", "\n").Replace('\r', '\n');
            var kept = new List<string>();

            foreach (var line in normalized.Split('\n'))
            {
                var clean = new StringBuilder(line.Length);
                foreach (var ch in line)
                {
                    if (char.IsControl(ch) && ch != '\t') continue;
                    clean.Append(ch == '\t' ? ' ' : ch);
                }

                var trimmed = clean.ToString().Trim();
                if (trimmed.Length == 0)
                {
                    if (kept.Count > 0 && kept[kept.Count - 1].Length > 0) kept.Add("");
                    continue;
                }

                if (LooksLikeInstruction(trimmed)) continue;
                kept.Add(trimmed);
            }

            while (kept.Count > 0 && kept[kept.Count - 1].Length == 0) kept.RemoveAt(kept.Count - 1);

            var text = string.Join("\n", kept).Trim();
            if (text.Length <= maxLength) return text;

            var cut = text.Substring(0, maxLength);
            var lastSpace = cut.LastIndexOf(' ');
            if (lastSpace > maxLength / 2) cut = cut.Substring(0, lastSpace);
            return cut.TrimEnd();
        }

        /// <summary>
        /// Sanitises one user-typed allow/deny entry. Returns null when the entry must be dropped.
        ///
        /// <para>Entries are matched as plain case-insensitive substrings by the privacy layer, so
        /// wildcards are not a feature — and an entry that is nothing but wildcards/punctuation would
        /// match every app on the machine. Those are rejected rather than silently reinterpreted:
        /// a deny list that quietly means "deny everything" and a title allow list that quietly means
        /// "send every title" are the same class of bug, and the second one leaks.</para>
        /// </summary>
        public static string? SanitizeRuleEntry(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;

            var sb = new StringBuilder(Math.Min(raw.Length, MaxRuleLength));
            foreach (var ch in raw.Trim())
            {
                if (sb.Length >= MaxRuleLength) break;
                if (char.IsControl(ch)) continue;
                if (ch == '*' || ch == '%' || ch == '?') continue; // not wildcards here; not literals either
                sb.Append(char.ToLowerInvariant(ch));
            }

            var entry = sb.ToString().Trim();
            if (entry.Length < 2) return null;                       // one character matches half the machine
            if (LooksLikeInstruction(entry)) return null;

            bool hasLetterOrDigit = false;
            foreach (var ch in entry)
            {
                if (char.IsLetterOrDigit(ch)) { hasLetterOrDigit = true; break; }
            }

            return hasLetterOrDigit ? entry : null;
        }

        /// <summary>
        /// Sanitises a whole allow/deny list: entries cleaned, blanks and duplicates dropped, capped at
        /// <see cref="MaxRuleEntries"/>. Never returns null — an unusable list is an empty list, which
        /// is the safe reading for both lists we ship (empty deny = deny nothing, empty title allow =
        /// send no titles).
        /// </summary>
        public static List<string> SanitizeRuleList(IEnumerable<string>? raw)
        {
            var result = new List<string>();
            if (raw == null) return result;

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in raw)
            {
                if (result.Count >= MaxRuleEntries) break;
                var clean = SanitizeRuleEntry(entry);
                if (clean == null || !seen.Add(clean)) continue;
                result.Add(clean);
            }

            return result;
        }

        /// <summary>
        /// Formats a double for a log line / projection with an invariant culture, so a German locale
        /// does not turn <c>0.42</c> into <c>0,42</c> and break every log grep and JSON consumer.
        /// </summary>
        public static string Num(double value, int decimals = 2) =>
            Math.Round(value, decimals).ToString("0.##", CultureInfo.InvariantCulture);

        private static bool LooksLikeInstruction(string trimmedLine)
        {
            if (trimmedLine.Length == 0) return false;
            var lower = trimmedLine.ToLowerInvariant();
            foreach (var marker in RoleMarkers)
            {
                if (lower.StartsWith(marker, StringComparison.Ordinal)) return true;
            }
            return false;
        }
    }
}
