using System;
using System.Linq;

namespace ConditioningControlPanel.Services.Fyp
{
    /// <summary>
    /// Normalises what a user typed into a subreddit name: strips a leading "r/" or anything
    /// up to the last "/r/", keeps the leading run of [A-Za-z0-9_], and accepts 2..40 characters.
    /// Pure string work, so it lives in Core; the settings model validates with it on every
    /// edit and the online coordinator delegates here.
    /// </summary>
    public static class SubredditName
    {
        public static string? Sanitize(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            var s = raw.Trim();
            int idx = s.LastIndexOf("/r/", StringComparison.OrdinalIgnoreCase);
            if (idx >= 0) s = s[(idx + 3)..];
            else if (s.StartsWith("r/", StringComparison.OrdinalIgnoreCase)) s = s[2..];
            s = new string(s.TakeWhile(ch => char.IsLetterOrDigit(ch) || ch == '_').ToArray());
            return s.Length is >= 2 and <= 40 ? s : null;
        }
    }
}
