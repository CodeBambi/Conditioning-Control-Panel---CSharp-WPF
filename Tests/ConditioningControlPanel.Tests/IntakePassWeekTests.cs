using System;
using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Week arithmetic for the weekly Graded Intake pass. The pass is gated on an ISO week KEY rather
/// than a "7 days since last run" delta, so these boundaries are the whole feature: get the key
/// wrong and a user either gets a free intake they did not earn or loses one they did.
///
/// The New Year cases are the reason this file exists. A hand-rolled week number - the obvious
/// implementation - hands out a bonus pass every January, because the ISO week containing Jan 1
/// usually belongs to the PREVIOUS year.
/// </summary>
public class IntakePassWeekTests
{
    // ---- StartOfWeek: always the Monday ----

    [Theory]
    [InlineData(2026, 7, 27)]  // Monday itself
    [InlineData(2026, 7, 28)]  // Tuesday
    [InlineData(2026, 7, 29)]  // Wednesday
    [InlineData(2026, 7, 30)]  // Thursday
    [InlineData(2026, 7, 31)]  // Friday
    [InlineData(2026, 8, 1)]   // Saturday
    [InlineData(2026, 8, 2)]   // Sunday - the off-by-one trap, since DayOfWeek counts it as 0
    public void StartOfWeek_AlwaysReturnsTheMonday(int y, int m, int d)
    {
        var start = IntakePassService.StartOfWeek(new DateTime(y, m, d));
        Assert.Equal(DayOfWeek.Monday, start.DayOfWeek);
        Assert.Equal(new DateTime(2026, 7, 27), start);
    }

    [Fact]
    public void StartOfWeek_StripsTimeOfDay()
    {
        var start = IntakePassService.StartOfWeek(new DateTime(2026, 7, 29, 23, 59, 59));
        Assert.Equal(new DateTime(2026, 7, 27), start);
        Assert.Equal(TimeSpan.Zero, start.TimeOfDay);
    }

    // ---- WeekKey: stable within a week, changes across the Sunday/Monday seam ----

    [Fact]
    public void WeekKey_IsStableAcrossOneWeek()
    {
        var monday = IntakePassService.WeekKey(new DateTime(2026, 7, 27));
        var sunday = IntakePassService.WeekKey(new DateTime(2026, 8, 2, 23, 0, 0));
        Assert.Equal(monday, sunday);
    }

    [Fact]
    public void WeekKey_ChangesOnTheMondayBoundary()
    {
        var sunday = IntakePassService.WeekKey(new DateTime(2026, 8, 2, 23, 59, 59));
        var monday = IntakePassService.WeekKey(new DateTime(2026, 8, 3, 0, 0, 0));
        Assert.NotEqual(sunday, monday);
    }

    [Fact]
    public void WeekKey_HasSortableFixedWidthShape()
    {
        // "YYYY-Www" - zero padded so string comparison and eyeballing both behave.
        var key = IntakePassService.WeekKey(new DateTime(2026, 1, 5));
        Assert.Matches(@"^\d{4}-W\d{2}$", key);
    }

    // ---- The New Year cases ----

    [Fact]
    public void WeekKey_NewYearsDay_BelongsToThePreviousIsoYear()
    {
        // 1 Jan 2027 is a Friday. Its ISO week runs Mon 28 Dec 2026 - Sun 3 Jan 2027, and the
        // week's Thursday (31 Dec 2026) sits in 2026 - so the whole week is a 2026 week.
        // A naive implementation reports 2027-W01 here and silently mints an extra pass.
        var key = IntakePassService.WeekKey(new DateTime(2027, 1, 1));
        Assert.StartsWith("2026-W", key);
    }

    [Fact]
    public void WeekKey_AcrossNewYear_StaysOneContinuousWeek()
    {
        // 31 Dec 2026 (Thu) and 1 Jan 2027 (Fri) are the same week. If the key flips at midnight
        // on the calendar year, someone who ran an intake on the 31st gets a second one free.
        var dec31 = IntakePassService.WeekKey(new DateTime(2026, 12, 31));
        var jan01 = IntakePassService.WeekKey(new DateTime(2027, 1, 1));
        Assert.Equal(dec31, jan01);
    }

    [Fact]
    public void StartOfWeek_AcrossNewYear_ReturnsTheDecemberMonday()
        => Assert.Equal(new DateTime(2026, 12, 28), IntakePassService.StartOfWeek(new DateTime(2027, 1, 1)));

    [Fact]
    public void WeekKey_LeapYearFebruaryBoundary_IsContinuous()
    {
        // 2028 is a leap year: 29 Feb (Tue) and 1 Mar (Wed) share a week.
        var feb29 = IntakePassService.WeekKey(new DateTime(2028, 2, 29));
        var mar01 = IntakePassService.WeekKey(new DateTime(2028, 3, 1));
        Assert.Equal(feb29, mar01);
    }

    // ---- Next-pass projection ----

    [Fact]
    public void NextPassLocal_IsAFutureMonday()
    {
        var next = IntakePassService.NextPassLocal;
        Assert.Equal(DayOfWeek.Monday, next.DayOfWeek);
        Assert.True(next > DateTime.Now, "the next pass must always be in the future");
        Assert.Equal(TimeSpan.Zero, next.TimeOfDay);
    }

    [Fact]
    public void DaysUntilNextPass_IsWithinAWeekAndNeverZero()
    {
        // Floored at 1: "unlocks in 0 days" is never a sentence the UI should be able to render.
        var days = IntakePassService.DaysUntilNextPass;
        Assert.InRange(days, 1, 7);
    }
}
