using System;
using System.Collections.Generic;
using System.Globalization;

namespace ConditioningControlPanel.Services.Billboard.Providers
{
    /// <summary>Text helpers for the pure halves. <c>loc</c> is <c>Loc.Get</c> in the app, identity in tests.</summary>
    internal static class CardText
    {
        public static string F(Func<string, string> loc, string key, params object[] args)
        {
            var pattern = loc(key);
            try { return string.Format(CultureInfo.CurrentCulture, pattern, args); }
            catch (FormatException) { return pattern; }
        }

        /// <summary>"a", "a and b", "a, b and c".</summary>
        public static string JoinAnd(Func<string, string> loc, IReadOnlyList<string> parts)
        {
            if (parts.Count == 0) return string.Empty;
            if (parts.Count == 1) return parts[0];
            var head = string.Join(", ", System.Linq.Enumerable.Take(parts, parts.Count - 1));
            return F(loc, "billboard_card_and", head, parts[parts.Count - 1]);
        }
    }
}
