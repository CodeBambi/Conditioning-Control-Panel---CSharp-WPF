using System;
using System.Collections.Generic;

namespace ConditioningControlPanel.Services.UI
{
    /// <summary>
    /// The do-not-disturb process list as text: how one entry is cleaned, how the settings
    /// textbox parses into the stored list, and how the list renders back. Pure string work,
    /// pulled out of <c>DoNotDisturbGuard</c> (which stays in the head: the guard itself
    /// enumerates windows through Win32) so the Settings page can edit the list on any head.
    /// </summary>
    public static class DndProcessList
    {
        /// <summary>One list entry, cleaned: trimmed, lower-cased, a trailing ".exe" removed,
        /// surrounding quotes dropped. "VLC.exe" and " vlc " are the same app and must compare equal.</summary>
        public static string Normalize(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "";
            var name = raw.Trim().Trim('"').Trim();
            if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                name = name.Substring(0, name.Length - 4);
            return name.Trim().ToLowerInvariant();
        }

        /// <summary>Parses the settings textbox into the stored list. Accepts one process per line,
        /// commas, semicolons or any mix. Entries are normalised, blanks dropped, duplicates
        /// collapsed, order preserved so the box reads back the way it was typed.</summary>
        public static List<string> Parse(string? raw)
        {
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(raw)) return result;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var piece in raw.Split(new[] { '\r', '\n', ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var name = Normalize(piece);
                if (name.Length == 0) continue;
                if (seen.Add(name)) result.Add(name);
            }
            return result;
        }

        /// <summary>Renders the stored list back into the textbox, one process per line.</summary>
        public static string Format(IEnumerable<string>? list)
            => list == null ? "" : string.Join(Environment.NewLine, list);
    }
}
