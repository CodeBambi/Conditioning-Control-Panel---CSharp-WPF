using System;
using System.Globalization;

namespace ConditioningControlPanel.Services.Leash;

/// <summary>
/// Did the leashed side meet today's assignment (CONTRACT "Rules", Assignments). Decided on the
/// leashed client from the same numbers the report carries; the server trusts <c>assign_done</c>
/// in R. An assignment belongs to one local day: a different day's assignment is never met here.
/// Pure.
/// </summary>
public static class LeashAssignRule
{
    /// <summary>The wire's day key, <c>yyyymmdd</c>, for a local date.</summary>
    public static string DayKey(DateTime localNow) => localNow.ToString("yyyyMMdd", CultureInfo.InvariantCulture);

    /// <param name="minutesToday">Conditioned minutes today (the day log's <c>cm</c>).</param>
    /// <param name="questsDoneToday">Daily quests done today.</param>
    /// <param name="videoWatched">The verified watch of this assignment's video finished.</param>
    public static bool Met(Assignment? a, string todayKey, int minutesToday, int questsDoneToday, bool videoWatched)
    {
        if (a == null || a.Status != AssignStatus.Open) return false;
        if (!string.Equals(a.Day, todayKey, StringComparison.Ordinal)) return false;
        return a.Kind switch
        {
            AssignKind.Minutes => minutesToday >= a.Size,
            AssignKind.Quests => questsDoneToday >= a.Size,
            AssignKind.Video => videoWatched,
            _ => false,
        };
    }

    /// <summary>How far along it is, for the card: (done, total). Video is 0/1 or 1/1.</summary>
    public static (int Done, int Total) Progress(Assignment a, int minutesToday, int questsDoneToday, bool videoWatched) => a.Kind switch
    {
        AssignKind.Minutes => (Math.Clamp(minutesToday, 0, a.Size), a.Size),
        AssignKind.Quests => (Math.Clamp(questsDoneToday, 0, a.Size), a.Size),
        _ => (videoWatched ? 1 : 0, 1),
    };
}
