using System;
using System.Collections.Generic;

namespace ConditioningControlPanel.Services.Chaster;

/// <summary>One square of the lock calendar. A served day is crossed out, today is the cell
/// tonight's push lands on, a locked day still waits with its little padlock, and the one
/// extra cell after the last day is the key. Pure data: the page only draws it.</summary>
public readonly record struct LockDay(DateTime Date, int DayOfMonth, bool Served, bool Today, bool Locked, bool IsKey, bool Elided);

/// <summary>
/// The lock as a calendar: one cell per day of the lock, first day to last, then the key. A lock
/// longer than a month shows its LAST 31 days ending on the end date, and the first cell shown
/// is marked <see cref="LockDay.Elided"/> so the page can draw a "..." over it. Dates are
/// compared by calendar day in whatever kind the caller passes: hand it local days for a page
/// that says "tonight", never a mix.
/// </summary>
public static class LockCalendar
{
    public const int MaxDays = 31;
    public const int Columns = 12;

    public static IReadOnlyList<LockDay> CellsFor(DateTime start, DateTime end, DateTime today)
    {
        var first = start.Date;
        var last = end.Date;
        if (last < first) last = first;
        var now = today.Date;

        var total = (last - first).Days + 1;
        var elided = total > MaxDays;
        var shownFirst = elided ? last.AddDays(-(MaxDays - 1)) : first;
        var count = elided ? MaxDays : total;

        var cells = new List<LockDay>(count + 1);
        for (var i = 0; i < count; i++)
        {
            var date = shownFirst.AddDays(i);
            cells.Add(new LockDay(
                Date: date,
                DayOfMonth: date.Day,
                Served: date < now,
                Today: date == now,
                Locked: date > now,
                IsKey: false,
                Elided: elided && i == 0));
        }
        var opens = last.AddDays(1);
        cells.Add(new LockDay(opens, opens.Day, Served: false, Today: false, Locked: false, IsKey: true, Elided: false));
        return cells;
    }

    /// <summary>How many days the elided marker stands for: the days before the first shown cell.</summary>
    public static int ElidedDays(DateTime start, DateTime end)
    {
        var total = (end.Date - start.Date).Days + 1;
        return total > MaxDays ? total - MaxDays : 0;
    }
}
