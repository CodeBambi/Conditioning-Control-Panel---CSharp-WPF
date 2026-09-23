using System;
using System.Collections.Generic;
using System.Globalization;

namespace ConditioningControlPanel.Services.Chaster;

/// <summary>
/// "Circe misses you": the one price that charges for NOT opening CCP. 5:00 for the first full
/// day away, doubling each day, never more than 60:00 for any one day. It lands on the TAB when
/// the player comes back, never on the lock while they are gone, and the first finished session
/// after coming back forgives half of it.
///
/// <para>Pure. The service books each away day on that day's own date, so the tab's ordinary
/// rules still hold: 60:00 per local day, and never more than
/// <see cref="CircesTab.BacklogCapSeconds"/> unpaid in total. That backlog cap is the real
/// ceiling: a month away is three hours, not a month.</para>
/// </summary>
public static class CircesMisses
{
    public const string EventId = "misses";
    public const string ForgivenEventId = "misses_forgiven";
    public const int FirstDaySeconds = 5 * 60;

    /// <summary>Its own ceiling, 60:00 a day away, whatever the player set as the daily limit.</summary>
    public const int MaxDaySeconds = 60 * 60;

    /// <summary>More away days than this change nothing (the backlog cap is long since hit),
    /// so the loop that books them stays short whatever the file says.</summary>
    public const int MaxDaysCounted = 14;

    /// <summary>FULL local days nobody opened CCP. Seen Monday, back Wednesday: one (Tuesday).
    /// Seen yesterday, seen today, never seen, a date that does not parse, a clock set
    /// backwards: zero.</summary>
    public static int DaysAway(string? lastSeenDay, DateTime localNow)
    {
        if (!TryDay(lastSeenDay, out var last)) return 0;
        var gap = (localNow.Date - last).Days - 1;
        return Math.Clamp(gap, 0, MaxDaysCounted);
    }

    /// <summary>What each away day costs, first day first.</summary>
    public static IReadOnlyList<int> Charges(int daysAway)
    {
        var list = new List<int>();
        for (var i = 0; i < Math.Clamp(daysAway, 0, MaxDaysCounted); i++)
            list.Add((int)Math.Min((long)FirstDaySeconds << i, MaxDaySeconds));
        return list;
    }

    /// <summary>Half of what was actually booked, which can be less than what was charged.</summary>
    public static int Forgivable(int bookedSeconds) => Math.Max(0, bookedSeconds) / 2;

    public static bool TryDay(string? dayKey, out DateTime day) =>
        DateTime.TryParseExact(dayKey, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out day);
}
