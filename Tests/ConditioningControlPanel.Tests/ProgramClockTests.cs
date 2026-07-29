using System;
using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Models.Program;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The multi-day program day clock. Everything a Program promises the user - "you kept the streak",
/// "you have one day off left" - reduces to two judgements this class makes: which calendar day a
/// 2am session belongs to, and how many days actually went missing while the user was away.
/// Both are cheap to get subtly wrong and expensive to be wrong about in public: an over-count
/// spends a day off nobody used, or lapses a 28-day run on day 25 for an absence that was one day
/// long. The count is also an off-by-one magnet, because the day the user *returns* on has not been
/// missed yet - only the days strictly between the last anchor and today have.
/// Evaluate additionally runs on a one-minute timer, so calling it repeatedly inside a single
/// program-date must be completely inert; a clock that advanced on its own poll would eat a run.
/// Pure functions with no App reads, so these run without a WPF Application.
/// </summary>
public class ProgramClockTests
{
    // ---- ProgramDate: which day does this instant belong to? ----

    [Fact]
    public void TwoAm_BelongsToThePreviousDay()
    {
        var date = ProgramClock.ProgramDate(new DateTime(2026, 7, 10, 2, 0, 0), 4);
        Assert.Equal(new DateTime(2026, 7, 9), date);
    }

    [Fact]
    public void FiveAm_BelongsToTheSameDay()
    {
        var date = ProgramClock.ProgramDate(new DateTime(2026, 7, 10, 5, 0, 0), 4);
        Assert.Equal(new DateTime(2026, 7, 10), date);
    }

    [Fact]
    public void ExactlyTheBoundaryHour_BelongsToTheNewDay()
    {
        // 04:00:00 sharp is the first instant of the new program-day, not the last of the old one.
        var date = ProgramClock.ProgramDate(new DateTime(2026, 7, 10, 4, 0, 0), 4);
        Assert.Equal(new DateTime(2026, 7, 10), date);

        // One second earlier is still yesterday.
        var justBefore = ProgramClock.ProgramDate(new DateTime(2026, 7, 10, 3, 59, 59), 4);
        Assert.Equal(new DateTime(2026, 7, 9), justBefore);
    }

    [Fact]
    public void ZeroBoundary_IsPlainCalendarDate()
    {
        var midnight = new DateTime(2026, 7, 10, 0, 0, 0);
        var lateNight = new DateTime(2026, 7, 10, 23, 59, 0);

        Assert.Equal(midnight.Date, ProgramClock.ProgramDate(midnight, 0));
        Assert.Equal(lateNight.Date, ProgramClock.ProgramDate(lateNight, 0));
    }

    [Fact]
    public void OutOfRangeBoundaryHours_AreClamped()
    {
        var now = new DateTime(2026, 7, 10, 12, 0, 0);

        // Above 23 clamps to 23, below 0 clamps to 0 - a corrupt settings value must not shift the
        // whole run by days.
        Assert.Equal(ProgramClock.ProgramDate(now, 23), ProgramClock.ProgramDate(now, 99));
        Assert.Equal(ProgramClock.ProgramDate(now, 0), ProgramClock.ProgramDate(now, -7));
    }

    // ---- Evaluate: rollover, misses and lapses ----

    [Fact]
    public void SameProgramDate_DoesNotAdvanceAndMissesNothing()
    {
        var anchor = new DateTime(2026, 7, 10);

        var result = ProgramClock.Evaluate(anchor, 3, anchor, 28, 1, _ => false);

        Assert.False(result.Advanced);
        Assert.Empty(result.MissedDays);
        Assert.Equal(0, result.DaysOffSpent);
        Assert.False(result.Lapsed);
        Assert.False(result.IsReturnDay);
        Assert.False(result.RanPastEnd);
        Assert.Equal(3, result.NewCurrentDay);
    }

    [Fact]
    public void RepeatedEvaluationInsideOneDay_IsIdempotent()
    {
        // Called every minute by the service; sixty inert calls must look exactly like one.
        var anchor = new DateTime(2026, 7, 10);
        var calls = 0;

        for (int i = 0; i < 60; i++)
        {
            var result = ProgramClock.Evaluate(anchor, 5, anchor, 28, 1, _ => { calls++; return false; });
            Assert.False(result.Advanced);
            Assert.Equal(5, result.NewCurrentDay);
            Assert.Empty(result.MissedDays);
        }

        // The completion probe should never even be consulted when nothing elapsed.
        Assert.Equal(0, calls);
    }

    [Fact]
    public void ClockRolledBackwards_DoesNotAdvance()
    {
        // A user changing their system clock (or a DST/timezone move) must not rewind or accuse.
        var result = ProgramClock.Evaluate(
            currentDayDate: new DateTime(2026, 7, 10),
            currentDay: 4,
            todayProgramDate: new DateTime(2026, 7, 8),
            lengthDays: 28,
            daysOffRemaining: 1,
            isDayComplete: _ => false);

        Assert.False(result.Advanced);
        Assert.Empty(result.MissedDays);
        Assert.Equal(4, result.NewCurrentDay);
    }

    [Fact]
    public void OneDayElapsed_WithCurrentDayComplete_AdvancesCleanly()
    {
        var result = ProgramClock.Evaluate(
            currentDayDate: new DateTime(2026, 7, 10),
            currentDay: 4,
            todayProgramDate: new DateTime(2026, 7, 11),
            lengthDays: 28,
            daysOffRemaining: 1,
            isDayComplete: day => day == 4);

        Assert.True(result.Advanced);
        Assert.Empty(result.MissedDays);
        Assert.Equal(0, result.DaysOffSpent);
        Assert.False(result.Lapsed);
        Assert.False(result.IsReturnDay);
        Assert.False(result.RanPastEnd);
        Assert.Equal(5, result.NewCurrentDay);
    }

    [Fact]
    public void OneDayElapsed_WithCurrentDayIncomplete_SpendsExactlyOneDayOff()
    {
        var result = ProgramClock.Evaluate(
            currentDayDate: new DateTime(2026, 7, 10),
            currentDay: 4,
            todayProgramDate: new DateTime(2026, 7, 11),
            lengthDays: 28,
            daysOffRemaining: 1,
            isDayComplete: _ => false);

        Assert.True(result.Advanced);
        Assert.Equal(new[] { 4 }, result.MissedDays.ToArray());
        Assert.Equal(1, result.DaysOffSpent);
        Assert.False(result.Lapsed);
        Assert.True(result.IsReturnDay);
        Assert.Equal(5, result.NewCurrentDay);
    }

    [Fact]
    public void ThreeDaysElapsed_WithCurrentDayComplete_MissesTwoNotThree()
    {
        // The classic off-by-one. Day 4 was finished before the user vanished; days 5 and 6 went by
        // untouched; day 7 is the day they came back on and has not been missed yet. Two, not three.
        var result = ProgramClock.Evaluate(
            currentDayDate: new DateTime(2026, 7, 10),
            currentDay: 4,
            todayProgramDate: new DateTime(2026, 7, 13),
            lengthDays: 28,
            daysOffRemaining: 5,
            isDayComplete: day => day == 4);

        Assert.Equal(new[] { 5, 6 }, result.MissedDays.ToArray());
        Assert.Equal(2, result.DaysOffSpent);
        Assert.DoesNotContain(4, result.MissedDays);
        Assert.DoesNotContain(7, result.MissedDays);
        Assert.Equal(7, result.NewCurrentDay);
        Assert.True(result.IsReturnDay);
        Assert.False(result.Lapsed);
    }

    [Fact]
    public void ThreeDaysElapsed_WithCurrentDayIncomplete_MissesThree()
    {
        var result = ProgramClock.Evaluate(
            currentDayDate: new DateTime(2026, 7, 10),
            currentDay: 4,
            todayProgramDate: new DateTime(2026, 7, 13),
            lengthDays: 28,
            daysOffRemaining: 5,
            isDayComplete: _ => false);

        Assert.Equal(new[] { 4, 5, 6 }, result.MissedDays.ToArray());
        Assert.Equal(3, result.DaysOffSpent);
        Assert.DoesNotContain(7, result.MissedDays);
        Assert.Equal(7, result.NewCurrentDay);
    }

    [Fact]
    public void MissesBeyondTheAllowance_LapseTheRun()
    {
        var result = ProgramClock.Evaluate(
            currentDayDate: new DateTime(2026, 7, 10),
            currentDay: 4,
            todayProgramDate: new DateTime(2026, 7, 13),
            lengthDays: 28,
            daysOffRemaining: 1,
            isDayComplete: _ => false);

        Assert.Equal(3, result.DaysOffSpent);
        Assert.True(result.Lapsed);

        // A lapsed run is not softened into a Return Day - the user is offered a restart instead.
        Assert.False(result.IsReturnDay);
    }

    [Fact]
    public void MissesExactlyEqualToTheAllowance_DoNotLapse()
    {
        var result = ProgramClock.Evaluate(
            currentDayDate: new DateTime(2026, 7, 10),
            currentDay: 4,
            todayProgramDate: new DateTime(2026, 7, 12),
            lengthDays: 28,
            daysOffRemaining: 2,
            isDayComplete: _ => false);

        Assert.Equal(2, result.DaysOffSpent);
        Assert.False(result.Lapsed);
        Assert.True(result.IsReturnDay);
    }

    [Fact]
    public void StrictMode_LapsesOnTheFirstMiss()
    {
        var result = ProgramClock.Evaluate(
            currentDayDate: new DateTime(2026, 7, 10),
            currentDay: 2,
            todayProgramDate: new DateTime(2026, 7, 11),
            lengthDays: 7,
            daysOffRemaining: 0,
            isDayComplete: _ => false);

        Assert.Single(result.MissedDays);
        Assert.True(result.Lapsed);
        Assert.False(result.IsReturnDay);
    }

    [Fact]
    public void StrictMode_CompletedDayDoesNotLapse()
    {
        var result = ProgramClock.Evaluate(
            currentDayDate: new DateTime(2026, 7, 10),
            currentDay: 2,
            todayProgramDate: new DateTime(2026, 7, 11),
            lengthDays: 7,
            daysOffRemaining: 0,
            isDayComplete: _ => true);

        Assert.Empty(result.MissedDays);
        Assert.False(result.Lapsed);
        Assert.Equal(3, result.NewCurrentDay);
    }

    [Fact]
    public void GapPastTheEnd_ClampsToLengthAndFlagsRanPastEnd()
    {
        var result = ProgramClock.Evaluate(
            currentDayDate: new DateTime(2026, 7, 10),
            currentDay: 5,
            todayProgramDate: new DateTime(2026, 7, 20),   // ten days later, only seven in the program
            lengthDays: 7,
            daysOffRemaining: 99,
            isDayComplete: _ => false);

        Assert.True(result.Advanced);
        Assert.True(result.RanPastEnd);
        Assert.Equal(7, result.NewCurrentDay);
    }

    [Fact]
    public void MissedDays_NeverExceedTheProgramLength()
    {
        // Even a huge gap must not report day 14 of a 7-day program - the records dictionary and the
        // chapter lookups are both indexed by real day numbers.
        var result = ProgramClock.Evaluate(
            currentDayDate: new DateTime(2026, 7, 10),
            currentDay: 6,
            todayProgramDate: new DateTime(2026, 8, 10),
            lengthDays: 7,
            daysOffRemaining: 99,
            isDayComplete: _ => false);

        Assert.NotEmpty(result.MissedDays);
        Assert.All(result.MissedDays, d => Assert.InRange(d, 1, 7));
        Assert.Equal(7, result.NewCurrentDay);
        Assert.True(result.RanPastEnd);
    }

    [Fact]
    public void CompletionProbe_IsAskedOnlyForElapsedDays()
    {
        // The probe hits the records dictionary; asking about the day the user is about to start
        // would be the same off-by-one seen from the other side.
        var asked = new List<int>();

        ProgramClock.Evaluate(
            currentDayDate: new DateTime(2026, 7, 10),
            currentDay: 4,
            todayProgramDate: new DateTime(2026, 7, 13),
            lengthDays: 28,
            daysOffRemaining: 9,
            isDayComplete: day => { asked.Add(day); return true; });

        Assert.Equal(new[] { 4, 5, 6 }, asked.ToArray());
    }
}
