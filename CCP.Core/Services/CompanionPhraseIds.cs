using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// Shared, platform-neutral shapes for phrase and bark IDs. Settings migrations use these
    /// without depending on a head's audio or UI service, while each head keeps its existing API.
    /// </summary>
    internal static class CompanionPhraseIds
    {
        internal const string VoiceLineCategory = "VoiceLine";

        /// <summary>
        /// Matches the voice generator's slug: remove format tokens, lowercase, collapse every
        /// non-alphanumeric run, and trim. The result is part of persisted bark IDs.
        /// </summary>
        internal static string Slugify(string? text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            var noTokens = Regex.Replace(text, @"\{[^}]*\}", " ");
            var sb = new StringBuilder(noTokens.Length);
            foreach (var ch in noTokens.ToLowerInvariant())
            {
                if ((ch >= 'a' && ch <= 'z') || (ch >= '0' && ch <= '9')) sb.Append(ch);
                else sb.Append(' ');
            }
            return Regex.Replace(sb.ToString(), @"\s+", " ").Trim();
        }

        internal static string VoiceLineId(string filePath) =>
            VoiceLineCategory + ":" + Path.GetFileNameWithoutExtension(filePath);

        internal static string BarkLineId(string ruleId, string text, string? audio)
        {
            var key = string.IsNullOrWhiteSpace(audio)
                ? "t_" + Slugify(text)
                : Path.GetFileNameWithoutExtension(audio);
            return "Bark:" + ruleId + ":" + key;
        }
    }
}
