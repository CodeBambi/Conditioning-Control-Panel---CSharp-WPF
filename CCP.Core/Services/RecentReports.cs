using System;
using System.Collections.Generic;
using System.Globalization;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// What the user is filing. Both kinds ride the exact same transport, endpoint, and signing —
    /// a Suggestion is just tagged in the description text and skips crash/app-log collection.
    /// </summary>
    public enum ReportKind { Bug, Suggestion }

    /// <summary>
    /// One decoded entry of <see cref="Models.AppSettings.RecentBugReports"/>.
    /// </summary>
    public class RecentReport
    {
        public string Token { get; set; } = string.Empty;
        /// <summary>UTC instant the report was filed. <c>null</c> for a legacy/unparseable stamp.</summary>
        public DateTime? TimestampUtc { get; set; }
        public ReportKind Kind { get; set; } = ReportKind.Bug;
    }

    /// <summary>
    /// The stored form of #769's remembered report numbers: text over
    /// <see cref="Models.AppSettings.RecentBugReports"/>, no settings access and no IO. The service
    /// that uploads a report and the window that lists them both live in a head; the record format
    /// is shared, so it lives here and every head reads and writes the same one.
    /// </summary>
    public static class RecentReports
    {
        /// <summary>How many report numbers we remember. Newest last; the oldest are trimmed on insert.</summary>
        public const int Max = 20;

        /// <summary>
        /// Ring-buffer insert. Appends "{token}|{ISO-8601 UTC}|{kind}" and trims the oldest entries
        /// so the list never exceeds <see cref="Max"/>. Blank tokens and a null list are ignored.
        /// </summary>
        public static void Append(List<string> list, string? token, DateTime timestampUtc, ReportKind kind)
        {
            if (list == null || string.IsNullOrWhiteSpace(token)) return;

            var kindText = kind == ReportKind.Suggestion ? "suggestion" : "bug";
            var stamp = timestampUtc.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);
            // The record is pipe-delimited, so a pipe inside the token would shift the stamp and kind
            // into the wrong fields on read. Server tokens are BUG-XXXXXXXXXX today; strip anyway.
            var safeToken = token.Trim().Replace("|", "");
            if (safeToken.Length == 0) return;
            list.Add($"{safeToken}|{stamp}|{kindText}");

            if (list.Count > Max)
                list.RemoveRange(0, list.Count - Max);
        }

        /// <summary>
        /// Decode stored entries into <see cref="RecentReport"/> rows, NEWEST FIRST.
        /// Tolerates malformed/legacy entries (a bare token still yields a row). Never throws.
        /// </summary>
        public static List<RecentReport> Parse(IEnumerable<string>? entries)
        {
            var result = new List<RecentReport>();
            if (entries == null) return result;

            foreach (var raw in entries)
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                var parts = raw.Split('|');
                var token = parts[0].Trim();
                if (token.Length == 0) continue;

                // EXACT "o" (the format Append writes), not a lenient TryParse: a corrupt field like
                // "5" parses leniently into a plausible-looking date and the UI then shows the user a
                // filing date that never happened. Anything else is treated as "no stamp", which the
                // row already renders gracefully.
                // NB: RoundtripKind alone — combining it with AdjustToUniversal throws ArgumentException.
                DateTime? stamp = null;
                if (parts.Length > 1 && DateTime.TryParseExact(
                        parts[1], "o", CultureInfo.InvariantCulture,
                        DateTimeStyles.RoundtripKind, out var parsed))
                {
                    stamp = parsed.Kind == DateTimeKind.Unspecified
                        ? DateTime.SpecifyKind(parsed, DateTimeKind.Utc)   // no offset written: it was UTC
                        : parsed.ToUniversalTime();
                }

                var kind = parts.Length > 2 && string.Equals(parts[2].Trim(), "suggestion", StringComparison.OrdinalIgnoreCase)
                    ? ReportKind.Suggestion
                    : ReportKind.Bug;

                result.Add(new RecentReport { Token = token, TimestampUtc = stamp, Kind = kind });
            }

            result.Reverse(); // stored oldest-first; the UI shows newest-first
            return result;
        }
    }
}
