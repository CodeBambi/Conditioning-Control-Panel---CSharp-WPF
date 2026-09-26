using System;
using System.Collections.Generic;

namespace ConditioningControlPanel.Services.Chaster;

/// <summary>
/// The deeper rows (owner, 2026-09-26: "too few and kind of shallow"). Four rows that look at a
/// whole day instead of one moment, plus the rule that makes the same slip cost more the second
/// time. Pure: the service hands in what it saw and books what comes back.
///
/// <list type="bullet">
/// <item><b>Idle day</b>: a day CCP was open but under <see cref="IdleMinutes"/> of conditioning
/// happened. Days CCP was never opened are the "misses" row's business, never this one.</item>
/// <item><b>Dailies left</b>: each daily quest still open when its day ended.</item>
/// <item><b>Streak</b>: every <see cref="StreakEvery"/>th day in a row with a finished session
/// takes time back. The one big carrot.</item>
/// <item><b>Heat</b>: a modifier, not an event. With it on, the same cost booked again on the same
/// day costs <see cref="HeatStep"/> times the last, never more than <see cref="HeatMax"/> times
/// the list price.</item>
/// </list>
/// </summary>
public static class TabDayEnd
{
    public const string IdleEventId = "idle_day";
    public const string DailiesEventId = "dailies_left";
    public const string StreakEventId = "streak";
    public const string HeatId = "heat";

    public const int IdleSeconds = 20 * 60;
    public const int IdleMinutes = 10;
    public const int PerDailySeconds = 5 * 60;
    /// <summary>A day deals three dailies; a file that claims more is not believed.</summary>
    public const int MaxDailies = 3;
    public const int StreakSeconds = -30 * 60;
    public const int StreakEvery = 7;

    public const double HeatStep = 1.5;
    public const double HeatMax = 3.0;

    /// <summary>Rows the service books on its own (no feature raises them), so nothing has to
    /// hook them. Heat never books at all.</summary>
    public static readonly IReadOnlyList<string> ServiceRows = new[] { IdleEventId, DailiesEventId, StreakEventId, HeatId };

    /// <summary>Rows heat never touches: the day-end rows (already a day's verdict), "misses"
    /// (it has its own doubling), and the escape row, whose 9:00 a day ceiling is a safety
    /// promise about the moment a player is trying to get out.</summary>
    public static bool HeatApplies(string? id) =>
        !string.IsNullOrEmpty(id)
        && id != IdleEventId && id != DailiesEventId && id != StreakEventId
        && id != CircesMisses.EventId && id != "escape";

    /// <summary>A cost with heat on. <paramref name="bookedToday"/> is how many times this row
    /// already booked today. Credits are never heated.</summary>
    public static int Heated(int seconds, int bookedToday)
    {
        if (seconds <= 0 || bookedToday <= 0) return seconds;
        var factor = Math.Min(Math.Pow(HeatStep, Math.Min(bookedToday, 16)), HeatMax);
        return (int)Math.Min(Math.Round(seconds * factor), TabLimits.MaxDailySeconds);
    }

    /// <summary>What an ended day costs for being idle, or 0. <paramref name="minutes"/> null
    /// means the day log could not be read, and a day nobody can see is never charged.</summary>
    public static int IdleCharge(int? minutes) =>
        minutes is { } m && m < IdleMinutes ? IdleSeconds : 0;

    /// <summary>What an ended day costs for the dailies it left open.</summary>
    public static int DailiesCharge(int open) => Math.Clamp(open, 0, MaxDailies) * PerDailySeconds;

    /// <summary>The streak after a finished session today. Same day: unchanged. Yesterday:
    /// one longer. Anything else (a gap, a clock set backwards, no history): starts at one.</summary>
    public static int NextStreak(string? lastDay, int streak, DateTime localNow)
    {
        var today = CircesTab.DayKey(localNow);
        if (lastDay == today) return Math.Max(1, streak);
        return lastDay == CircesTab.DayKey(localNow.AddDays(-1)) ? Math.Max(0, streak) + 1 : 1;
    }

    /// <summary>Whether reaching <paramref name="streak"/> pays the streak credit.</summary>
    public static bool StreakPays(int before, int after) =>
        after != before && after > 0 && after % StreakEvery == 0;
}
