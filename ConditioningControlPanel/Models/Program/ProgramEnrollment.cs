using System;
using System.Collections.Generic;
using System.Linq;

namespace ConditioningControlPanel.Models.Program;

/// <summary>
/// How much of a run leaves the machine. Private is the default - an informed opt-out beats a
/// silent default in both directions, so the enrollment ceremony asks explicitly.
/// </summary>
public enum ProgramShareLevel
{
    /// <summary>Nothing leaves the machine. Local ledger only.</summary>
    Private,

    /// <summary>Day completions count toward the Devotion leaderboard. No posts.</summary>
    Ledger,

    /// <summary>Ledger plus milestone cards posted under the user's display name.</summary>
    Public
}

/// <summary>Where an enrollment currently sits.</summary>
public enum ProgramEnrollmentState
{
    Active,

    /// <summary>Clock stopped by the user. Free, visible, unshamed - spends nothing.</summary>
    Paused,

    /// <summary>Out of days off. Awaiting the user's choice to restart or withdraw.</summary>
    Lapsed,

    Graduated,

    /// <summary>User withdrew. Always available, on every screen, without commentary.</summary>
    Withdrawn
}

/// <summary>What happened on one day of a run.</summary>
public class ProgramDayRecord
{
    public int DayIndex { get; set; }

    /// <summary>The program-date this day belongs to (already shifted by the boundary hour).</summary>
    public DateTime ProgramDate { get; set; }

    public bool SessionCompleted { get; set; }
    public DateTime? SessionCompletedAt { get; set; }

    /// <summary>Task id to accumulated progress. Auto-verified tasks count up to the target.</summary>
    public Dictionary<string, int> TaskProgress { get; set; } = new();

    public List<string> CompletedTaskIds { get; set; } = new();

    public bool DayCompleted { get; set; }
    public DateTime? CompletedAt { get; set; }

    /// <summary>Rolled over without both slots done.</summary>
    public bool Missed { get; set; }

    /// <summary>A day off was spent covering this absence.</summary>
    public bool DayOffSpent { get; set; }

    /// <summary>First day back after an absence. Presented as a reduced Return Day, never a wall of failure.</summary>
    public bool IsReturnDay { get; set; }

    /// <summary>Ambient minutes accumulated, when the day carries an ambient requirement.</summary>
    public int AmbientMinutes { get; set; }

    /// <summary>
    /// Ritual task id to the on-disk path of the photo filed for it.
    ///
    /// This exists so a program can read its own days back - Presentation's day-1/day-14 diptych
    /// cannot be built from the roadmap alone, because a program ritual only advances a roadmap step
    /// when the user happens to be standing on it. Paths are local; the photo is copied into the
    /// existing diary folder and is never uploaded, never synced, and never rendered onto a share
    /// card. Storing the path rather than the bytes keeps the enrollment JSON small.
    /// </summary>
    public Dictionary<string, string> RitualPhotos { get; set; } = new();
}

/// <summary>What a rollover evaluation concluded. Returned by the pure clock so the service can act.</summary>
public class ProgramRolloverResult
{
    /// <summary>True when the program date advanced at all.</summary>
    public bool Advanced { get; set; }

    /// <summary>Day indices that rolled over incomplete.</summary>
    public List<int> MissedDays { get; set; } = new();

    /// <summary>Absences that consumed the allowance.</summary>
    public int DaysOffSpent { get; set; }

    /// <summary>The allowance ran out - the run lapses and the user is offered a restart.</summary>
    public bool Lapsed { get; set; }

    /// <summary>Day index after the rollover.</summary>
    public int NewCurrentDay { get; set; }

    /// <summary>The new current day should be presented as a Return Day.</summary>
    public bool IsReturnDay { get; set; }

    /// <summary>The rollover ran past the end of the program.</summary>
    public bool RanPastEnd { get; set; }
}

/// <summary>
/// The day clock, as pure functions. Deliberately free of App reads, timers and WPF so the
/// rollover rules - which are the part most likely to be wrong - are unit-testable.
/// </summary>
public static class ProgramClock
{
    /// <summary>
    /// The program-date a wall-clock instant belongs to. With the default 04:00 boundary a 2am
    /// session counts for the day before, which is how people actually use this app.
    /// </summary>
    public static DateTime ProgramDate(DateTime localNow, int boundaryHour)
    {
        var h = Math.Clamp(boundaryHour, 0, 23);
        return localNow.AddHours(-h).Date;
    }

    /// <summary>
    /// Evaluate a rollover. <paramref name="isDayComplete"/> is asked once per elapsed day index;
    /// only the day that was current can be complete, but the callback keeps the rule explicit
    /// rather than assumed.
    /// </summary>
    public static ProgramRolloverResult Evaluate(
        DateTime currentDayDate,
        int currentDay,
        DateTime todayProgramDate,
        int lengthDays,
        int daysOffRemaining,
        Func<int, bool> isDayComplete)
    {
        var result = new ProgramRolloverResult { NewCurrentDay = currentDay };

        var gap = (todayProgramDate - currentDayDate).Days;
        if (gap <= 0) return result;

        result.Advanced = true;

        for (int i = 0; i < gap; i++)
        {
            var dayIndex = currentDay + i;
            if (dayIndex > lengthDays) break;
            if (!isDayComplete(dayIndex)) result.MissedDays.Add(dayIndex);
        }

        result.DaysOffSpent = result.MissedDays.Count;
        result.Lapsed = result.DaysOffSpent > daysOffRemaining;
        result.IsReturnDay = result.MissedDays.Count > 0 && !result.Lapsed;

        var target = currentDay + gap;
        if (target > lengthDays)
        {
            result.RanPastEnd = true;
            target = lengthDays;
        }

        result.NewCurrentDay = target;
        return result;
    }
}

/// <summary>
/// One user's run of one program. Persisted inside programs.json; the pure rules live in
/// <see cref="ProgramClock"/> so this stays a data carrier.
/// </summary>
public class ProgramEnrollment
{
    public string ProgramId { get; set; } = "";

    /// <summary>Reserved for cohorts so shipping them later is not a migration.</summary>
    public string? CohortId { get; set; }

    public DateTime StartedAt { get; set; }

    /// <summary>Local hour the day rolls over. Chosen at enrollment, default 04:00.</summary>
    public int DayBoundaryHour { get; set; } = 4;

    /// <summary>Hour the nudge fires, local. -1 disables it.</summary>
    public int NudgeHour { get; set; } = 20;

    public int CurrentDay { get; set; } = 1;

    /// <summary>The program-date the current day began on. The anchor the clock measures from.</summary>
    public DateTime CurrentDayDate { get; set; }

    public bool StrictMode { get; set; }

    public ProgramShareLevel ShareLevel { get; set; } = ProgramShareLevel.Private;

    public int DaysOffRemaining { get; set; } = 1;

    /// <summary>Counted and surfaced in-fiction. A restart re-read as devotion, not failure.</summary>
    public int AttemptNumber { get; set; } = 1;

    public ProgramEnrollmentState State { get; set; } = ProgramEnrollmentState.Active;

    public DateTime? PausedAt { get; set; }
    public DateTime? GraduatedAt { get; set; }
    public DateTime? LapsedAt { get; set; }

    public Dictionary<int, ProgramDayRecord> Records { get; set; } = new();

    /// <summary>
    /// Chapter rewards already banked. Survives a restart untouched - you lose your position,
    /// never your possessions.
    /// </summary>
    public List<string> BankedRewards { get; set; } = new();

    /// <summary>Chapters whose reward has already been granted this attempt.</summary>
    public List<string> CompletedChapterIds { get; set; } = new();

    public bool IsActive => State == ProgramEnrollmentState.Active;

    public ProgramDayRecord GetOrCreateRecord(int dayIndex, DateTime programDate)
    {
        if (!Records.TryGetValue(dayIndex, out var record))
        {
            record = new ProgramDayRecord { DayIndex = dayIndex, ProgramDate = programDate };
            Records[dayIndex] = record;
        }

        return record;
    }

    public ProgramDayRecord? GetRecord(int dayIndex) =>
        Records.TryGetValue(dayIndex, out var record) ? record : null;

    public bool IsDayComplete(int dayIndex) => GetRecord(dayIndex)?.DayCompleted == true;

    public int CompletedDayCount => Records.Values.Count(r => r.DayCompleted);

    /// <summary>Days finished without spending anything. Feeds the Devotion weighting later.</summary>
    public int PerfectDayCount => Records.Values.Count(r => r.DayCompleted && !r.DayOffSpent && !r.Missed);

    /// <summary>
    /// Reset for another attempt. Keeps banked rewards and increments the attempt counter;
    /// everything else starts clean.
    /// </summary>
    public void RestartForNewAttempt(DateTime now, ProgramRules rules)
    {
        AttemptNumber++;
        CurrentDay = 1;
        StartedAt = now;
        CurrentDayDate = ProgramClock.ProgramDate(now, DayBoundaryHour);
        DaysOffRemaining = StrictMode ? 0 : rules.DaysOffAllowed;
        State = ProgramEnrollmentState.Active;
        PausedAt = null;
        LapsedAt = null;
        GraduatedAt = null;
        Records.Clear();
        CompletedChapterIds.Clear();
    }
}

/// <summary>Everything ProgramService persists. One active run, plus finished ones for the ledger.</summary>
public class ProgramState
{
    public ProgramEnrollment? Active { get; set; }
    public List<ProgramEnrollment> History { get; set; } = new();

    /// <summary>Programs the user has graduated at least once, by id.</summary>
    public List<string> GraduatedProgramIds { get; set; } = new();
}
