using System;
using System.Collections.Generic;
using ConditioningControlPanel.Models.Program;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Restarting a lapsed run. The whole reason a restart is offered instead of a dead end is the
/// promise "you lose your position, never your possessions" - chapter rewards already banked must
/// survive the reset, or a lapse on day 20 silently confiscates two chapters of earned content and
/// the restart becomes a punishment nobody would accept. Everything else has to go the other way:
/// leftover day records or completed-chapter ids surviving into attempt two would let the new run
/// re-grant rewards, count old days toward the streak, or start "already finished".
/// The allowance also has to come back from the program's rules rather than from whatever the
/// lapsed run had left, which was by definition zero.
/// Pure data class, so no WPF Application is required.
/// </summary>
public class ProgramEnrollmentTests
{
    private static ProgramEnrollment LapsedEnrollment()
    {
        var enrollment = new ProgramEnrollment
        {
            ProgramId = "test-program",
            StartedAt = new DateTime(2026, 6, 1, 9, 0, 0),
            DayBoundaryHour = 4,
            CurrentDay = 17,
            CurrentDayDate = new DateTime(2026, 6, 17),
            DaysOffRemaining = 0,
            AttemptNumber = 1,
            State = ProgramEnrollmentState.Lapsed,
            LapsedAt = new DateTime(2026, 6, 18, 4, 0, 0),
            PausedAt = new DateTime(2026, 6, 15, 12, 0, 0),
            GraduatedAt = null
        };

        enrollment.Records[1] = new ProgramDayRecord { DayIndex = 1, DayCompleted = true };
        enrollment.Records[2] = new ProgramDayRecord { DayIndex = 2, DayCompleted = true };
        enrollment.CompletedChapterIds.Add("chapter-1");
        enrollment.CompletedChapterIds.Add("chapter-2");
        enrollment.BankedRewards.Add("reward-veil");
        enrollment.BankedRewards.Add("reward-collar");

        return enrollment;
    }

    private static ProgramRules Rules(int daysOff = 2) => new() { DaysOffAllowed = daysOff };

    [Fact]
    public void Restart_KeepsBankedRewardsUntouched()
    {
        // The product promise. If this ever regresses, a lapse quietly confiscates earned content.
        var enrollment = LapsedEnrollment();

        enrollment.RestartForNewAttempt(new DateTime(2026, 7, 1, 10, 0, 0), Rules());

        Assert.Equal(new[] { "reward-veil", "reward-collar" }, enrollment.BankedRewards.ToArray());
    }

    [Fact]
    public void Restart_IncrementsTheAttemptCounter()
    {
        var enrollment = LapsedEnrollment();

        enrollment.RestartForNewAttempt(new DateTime(2026, 7, 1, 10, 0, 0), Rules());
        Assert.Equal(2, enrollment.AttemptNumber);

        enrollment.RestartForNewAttempt(new DateTime(2026, 7, 5, 10, 0, 0), Rules());
        Assert.Equal(3, enrollment.AttemptNumber);
    }

    [Fact]
    public void Restart_ReturnsToDayOneAndReactivates()
    {
        var enrollment = LapsedEnrollment();
        var now = new DateTime(2026, 7, 1, 10, 0, 0);

        enrollment.RestartForNewAttempt(now, Rules());

        Assert.Equal(1, enrollment.CurrentDay);
        Assert.Equal(now, enrollment.StartedAt);
        Assert.Equal(ProgramEnrollmentState.Active, enrollment.State);
        Assert.True(enrollment.IsActive);
        Assert.Null(enrollment.PausedAt);
        Assert.Null(enrollment.LapsedAt);
        Assert.Null(enrollment.GraduatedAt);
    }

    [Fact]
    public void Restart_ClearsRecordsAndCompletedChapters()
    {
        // A surviving completed-chapter id would let attempt two re-grant a reward it never earned;
        // a surviving day record would count toward the new run's completion and streak counts.
        var enrollment = LapsedEnrollment();

        enrollment.RestartForNewAttempt(new DateTime(2026, 7, 1, 10, 0, 0), Rules());

        Assert.Empty(enrollment.Records);
        Assert.Empty(enrollment.CompletedChapterIds);
        Assert.Equal(0, enrollment.CompletedDayCount);
        Assert.Equal(0, enrollment.PerfectDayCount);
    }

    [Fact]
    public void Restart_RestoresTheAllowanceFromTheProgramRules()
    {
        var enrollment = LapsedEnrollment();   // ran itself down to zero
        Assert.Equal(0, enrollment.DaysOffRemaining);

        enrollment.RestartForNewAttempt(new DateTime(2026, 7, 1, 10, 0, 0), Rules(daysOff: 3));

        Assert.Equal(3, enrollment.DaysOffRemaining);
    }

    [Fact]
    public void Restart_StrictRunGetsNoDaysOffBack()
    {
        var enrollment = LapsedEnrollment();
        enrollment.StrictMode = true;

        enrollment.RestartForNewAttempt(new DateTime(2026, 7, 1, 10, 0, 0), Rules(daysOff: 3));

        Assert.Equal(0, enrollment.DaysOffRemaining);
    }

    [Fact]
    public void Restart_AnchorsTheDayDateToTheBoundaryHour()
    {
        // Restarting at 2am must anchor to the previous program-date, exactly like a session run at
        // 2am counts for the day before. Otherwise the fresh run "rolls over" a few hours later and
        // day 1 is missed before it began.
        var enrollment = LapsedEnrollment();
        enrollment.DayBoundaryHour = 4;

        enrollment.RestartForNewAttempt(new DateTime(2026, 7, 1, 2, 0, 0), Rules());

        Assert.Equal(new DateTime(2026, 6, 30), enrollment.CurrentDayDate);
    }

    [Fact]
    public void PerfectDayCount_ExcludesDaysThatSpentAnAbsence()
    {
        // "Perfect" feeds the Devotion weighting, so a day that was rescued by a day off or that
        // rolled over missed must not count even once its record says completed.
        var enrollment = new ProgramEnrollment();
        enrollment.Records[1] = new ProgramDayRecord { DayIndex = 1, DayCompleted = true };
        enrollment.Records[2] = new ProgramDayRecord { DayIndex = 2, DayCompleted = true, DayOffSpent = true };
        enrollment.Records[3] = new ProgramDayRecord { DayIndex = 3, DayCompleted = true, Missed = true };
        enrollment.Records[4] = new ProgramDayRecord { DayIndex = 4, DayCompleted = false };
        enrollment.Records[5] = new ProgramDayRecord { DayIndex = 5, DayCompleted = true };

        Assert.Equal(2, enrollment.PerfectDayCount);
        Assert.Equal(4, enrollment.CompletedDayCount);
    }

    [Fact]
    public void GetOrCreateRecord_IsStableForTheSameDay()
    {
        var enrollment = new ProgramEnrollment();
        var first = enrollment.GetOrCreateRecord(3, new DateTime(2026, 7, 10));
        first.TaskProgress["pop-bubbles"] = 12;

        var second = enrollment.GetOrCreateRecord(3, new DateTime(2026, 7, 11));

        Assert.Same(first, second);
        Assert.Equal(12, second.TaskProgress["pop-bubbles"]);
        Assert.Equal(new DateTime(2026, 7, 10), second.ProgramDate);   // first anchor wins
        Assert.False(enrollment.IsDayComplete(3));
    }
}
