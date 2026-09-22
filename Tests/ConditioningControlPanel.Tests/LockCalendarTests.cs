using System;
using System.Linq;
using ConditioningControlPanel.Services.Chaster;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The lock calendar as data: one cell a day from the first day to the last, then the key; a
/// long lock shows its last 31 days with the first shown cell marked as standing for the rest.
/// The page draws whatever comes out of here and decides nothing itself.
/// </summary>
public class LockCalendarTests
{
    private static readonly DateTime Start = new(2026, 9, 3);
    private static readonly DateTime End = new(2026, 10, 3);   // 31 days first to last
    private static readonly DateTime Today = new(2026, 9, 22, 14, 30, 0);

    [Fact]
    public void A_month_long_lock_is_thirty_one_days_and_a_key()
    {
        var cells = LockCalendar.CellsFor(Start, End, Today);
        Assert.Equal(32, cells.Count);
        Assert.Equal(31, cells.Count(c => !c.IsKey));
        Assert.True(cells[^1].IsKey);
        Assert.Equal(new DateTime(2026, 10, 4), cells[^1].Date);
        Assert.All(cells, c => Assert.False(c.Elided));
        Assert.Equal(0, LockCalendar.ElidedDays(Start, End));
    }

    [Fact]
    public void Served_today_and_locked_split_on_the_calendar_day_not_the_hour()
    {
        var cells = LockCalendar.CellsFor(Start, End, Today);
        Assert.Equal(19, cells.Count(c => c.Served));
        var today = Assert.Single(cells, c => c.Today);
        Assert.Equal(22, today.DayOfMonth);
        Assert.False(today.Served);
        Assert.False(today.Locked);
        Assert.Equal(11, cells.Count(c => c.Locked));
        var key = cells[^1];
        Assert.False(key.Served || key.Today || key.Locked);
        // the served days are exactly the ones before today, in order
        Assert.Equal(Enumerable.Range(3, 19), cells.Where(c => c.Served).Select(c => c.DayOfMonth));
    }

    [Fact]
    public void A_longer_lock_shows_its_last_thirty_one_days_and_marks_the_first_shown_as_elided()
    {
        var start = new DateTime(2026, 1, 1);
        var end = new DateTime(2026, 12, 31);
        var cells = LockCalendar.CellsFor(start, end, new DateTime(2026, 12, 20));
        Assert.Equal(32, cells.Count);
        Assert.True(cells[0].Elided);
        Assert.Equal(new DateTime(2026, 12, 1), cells[0].Date);
        Assert.Single(cells, c => c.Elided);
        Assert.Equal(new DateTime(2027, 1, 1), cells[^1].Date);
        Assert.Equal(365 - 31, LockCalendar.ElidedDays(start, end));
        Assert.Equal(19, cells.Count(c => c.Served));
        Assert.Equal(11, cells.Count(c => c.Locked));
    }

    [Fact]
    public void A_lock_of_exactly_a_month_is_not_elided_and_one_day_more_is()
    {
        var start = new DateTime(2026, 3, 1);
        Assert.False(LockCalendar.CellsFor(start, start.AddDays(30), start)[0].Elided);
        Assert.True(LockCalendar.CellsFor(start, start.AddDays(31), start)[0].Elided);
        Assert.Equal(1, LockCalendar.ElidedDays(start, start.AddDays(31)));
    }

    [Fact]
    public void A_lock_that_starts_today_or_has_no_span_is_one_day_and_a_key()
    {
        var cells = LockCalendar.CellsFor(Today, Today, Today);
        Assert.Equal(2, cells.Count);
        Assert.True(cells[0].Today);
        Assert.True(cells[1].IsKey);
        // an end before the start is a one-day lock, never an empty or negative one
        var odd = LockCalendar.CellsFor(Today, Today.AddDays(-5), Today);
        Assert.Equal(2, odd.Count);
    }

    [Fact]
    public void Before_the_first_day_nothing_is_served_and_after_the_last_everything_is()
    {
        var early = LockCalendar.CellsFor(Start, End, Start.AddDays(-3));
        Assert.DoesNotContain(early, c => c.Served || c.Today);
        Assert.Equal(31, early.Count(c => c.Locked));
        var late = LockCalendar.CellsFor(Start, End, End.AddDays(2));
        Assert.Equal(31, late.Count(c => c.Served));
        Assert.DoesNotContain(late, c => c.Today || c.Locked);
    }

    [Fact]
    public void Twelve_columns_put_a_month_on_three_rows()
    {
        var cells = LockCalendar.CellsFor(Start, End, Today);
        Assert.Equal(3, (int)Math.Ceiling(cells.Count / (double)LockCalendar.Columns));
    }
}
