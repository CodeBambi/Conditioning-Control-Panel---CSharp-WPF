using ConditioningControlPanel.Models.Program;
using ConditioningControlPanel.Services.Program;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// #959: "I do the training program, but after (i think) day 4, it gives me 'training program
/// stopped'. The run stopped. You ran out of days off." The pre-6.8.0 day clock condemned days the
/// user had actually finished - 57adfb174 describes the resulting ledger row as carrying "Missed
/// and, once finished, DayCompleted", a state that means nothing. The clock is fixed for future
/// runs, but nothing un-lapsed the saves it had already broken.
///
/// These tests lock the audit rule. It forgives ONLY a self-contradictory ledger, because that
/// contradiction is the bug's fingerprint and a consistent ledger is a user who really did miss
/// the days.
/// </summary>
public class ProgramLapseAuditTests
{
    private static ProgramEnrollment Enrollment(params (int Day, bool Missed, bool Completed)[] days)
    {
        var enrollment = new ProgramEnrollment { ProgramId = "test", CurrentDay = days.Length };
        foreach (var (day, missed, completed) in days)
        {
            enrollment.Records[day] = new ProgramDayRecord
            {
                DayIndex = day,
                Missed = missed,
                DayCompleted = completed,
            };
        }
        return enrollment;
    }

    [Fact]
    public void ADayStampedBothMissedAndComplete_IsTheBugAndTheRunComesBack()
    {
        // Day 4 finished AND counted as the absence that spent the last allowance - the exact
        // shape #959 reports.
        var audit = ProgramService.AuditLapse(
            Enrollment((1, false, true), (2, false, true), (3, false, true), (4, true, true)),
            daysOffAllowed: 1);

        Assert.True(audit.ShouldRestore);
        Assert.Equal(new[] { 4 }, audit.ContradictoryDays);
        Assert.Equal(0, audit.CorrectedMissedDays);
    }

    [Fact]
    public void AConsistentLedgerStaysLapsed()
    {
        // Two real absences against one day off. Nothing contradictory, so the user genuinely
        // ran out - forgiving this would make the allowance meaningless.
        var audit = ProgramService.AuditLapse(
            Enrollment((1, false, true), (2, true, false), (3, true, false)),
            daysOffAllowed: 1);

        Assert.False(audit.ShouldRestore);
        Assert.Empty(audit.ContradictoryDays);
        Assert.Equal(2, audit.CorrectedMissedDays);
    }

    [Fact]
    public void AContradictionThatDoesNotChangeTheVerdict_IsCosmeticAndChangesNothing()
    {
        // One bogus row, but three real absences against one day off: the run lapses either way,
        // so un-lapsing it would be a gift rather than a repair.
        var audit = ProgramService.AuditLapse(
            Enrollment((1, true, true), (2, true, false), (3, true, false), (4, true, false)),
            daysOffAllowed: 1);

        Assert.False(audit.ShouldRestore);
        Assert.Equal(new[] { 1 }, audit.ContradictoryDays);
        Assert.Equal(3, audit.CorrectedMissedDays);
    }

    [Fact]
    public void TheCorrectedCountIsWhatTheAllowanceIsMeasuredAgainst()
    {
        // Four missed rows, two of which are contradictions. Two real absences against a
        // two-day allowance survives - only just, which is the case worth pinning.
        var audit = ProgramService.AuditLapse(
            Enrollment((1, true, true), (2, true, true), (3, true, false), (4, true, false)),
            daysOffAllowed: 2);

        Assert.True(audit.ShouldRestore);
        Assert.Equal(new[] { 1, 2 }, audit.ContradictoryDays);
        Assert.Equal(2, audit.CorrectedMissedDays);
    }

    [Fact]
    public void StrictModeHasNoAllowance_SoOnlyAFullyBogusLapseIsReversed()
    {
        // Strict runs carry zero days off. One surviving absence is still a lapse.
        var survives = ProgramService.AuditLapse(
            Enrollment((1, true, true), (2, false, true)), daysOffAllowed: 0);
        Assert.True(survives.ShouldRestore);

        var doesNot = ProgramService.AuditLapse(
            Enrollment((1, true, true), (2, true, false)), daysOffAllowed: 0);
        Assert.False(doesNot.ShouldRestore);
    }

    [Fact]
    public void AnUntouchedLedgerIsNeverForgiven()
    {
        var audit = ProgramService.AuditLapse(Enrollment((1, false, true)), daysOffAllowed: 1);

        Assert.False(audit.ShouldRestore);
        Assert.Empty(audit.ContradictoryDays);
        Assert.Equal(0, audit.CorrectedMissedDays);
    }
}
