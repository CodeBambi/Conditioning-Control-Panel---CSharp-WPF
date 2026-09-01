using System;
using System.Globalization;

namespace ConditioningControlPanel.Helpers
{
    /// <summary>
    /// Time-of-day parsing for the scheduler's start/end fields.
    ///
    /// <para>#984/#985/#999: the fields are free text and the scheduler used a bare
    /// <c>TimeSpan.TryParse</c> on them, which refused perfectly reasonable entries and logged
    /// "Could not parse end time '2.5'" / "'22:0'" before silently falling back to 22:00 - so a
    /// schedule the user had visibly configured simply never ran at the hour they set. Two of
    /// TimeSpan's own behaviours made it worse: it is CULTURE-SENSITIVE (a locale whose time
    /// separator is not ':' rejects "22:00"), and a bare "7" parses as SEVEN DAYS, not 07:00.</para>
    ///
    /// <para>Accepted here, always invariant: <c>H</c>, <c>HH</c>, <c>H:m</c>, <c>HH:mm</c>, and
    /// <c>H.m</c> / <c>HH.mm</c> - the dot is treated as a separator, exactly like the colon
    /// (a locale/typo variant), so "2.5" is 02:05 and never "two and a half hours". Out-of-range
    /// values CLAMP rather than fail: hours above 23 pin to 23:59 (the end of the day the user was
    /// reaching for) and minutes above 59 pin to 59.</para>
    /// </summary>
    internal static class SchedulerTime
    {
        /// <summary>Last representable minute of a day; where an over-range hour clamps to.</summary>
        internal static readonly TimeSpan EndOfDay = new TimeSpan(23, 59, 0);

        /// <summary>
        /// Parse a scheduler time-of-day field. Returns false only for input that carries no usable
        /// time at all (null/blank/letters/negatives); every other case yields a clamped, valid
        /// time of day in <paramref name="time"/>.
        /// </summary>
        internal static bool TryParse(string? text, out TimeSpan time)
        {
            time = default;
            if (string.IsNullOrWhiteSpace(text)) return false;

            var s = text.Trim();

            // One separator at most, and ':' / '.' mean the same thing here.
            var parts = s.Split(new[] { ':', '.' }, StringSplitOptions.None);
            if (parts.Length > 2) return false;

            var hourText = parts[0].Trim();
            if (hourText.Length == 0) return false;
            if (!int.TryParse(hourText, NumberStyles.None, CultureInfo.InvariantCulture, out var hours))
                return false;   // NumberStyles.None also rejects a leading sign and thousands separators

            var minutes = 0;
            if (parts.Length == 2)
            {
                var minuteText = parts[1].Trim();
                // "22:" is the user mid-typing an hour, not an error.
                if (minuteText.Length > 0
                    && !int.TryParse(minuteText, NumberStyles.None, CultureInfo.InvariantCulture, out minutes))
                    return false;
            }

            if (hours > 23) { time = EndOfDay; return true; }
            if (minutes > 59) minutes = 59;

            time = new TimeSpan(hours, minutes, 0);
            return true;
        }

        /// <summary>
        /// <see cref="TryParse"/> with a caller-supplied fallback, for the scheduler's two fields.
        /// </summary>
        internal static TimeSpan ParseOrDefault(string? text, TimeSpan fallback)
            => TryParse(text, out var t) ? t : fallback;
    }
}
