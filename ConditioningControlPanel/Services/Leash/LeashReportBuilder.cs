using System;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.Leash;

/// <summary>The numbers the leashed client already counts, read fresh for one report.</summary>
/// <param name="LocalNow">The local clock (the report's day is the local day).</param>
/// <param name="MinutesToday">Conditioned minutes today, the feature day log's <c>cm</c>.</param>
/// <param name="LockEndsUtc">Chaster lock end, null when unknown or hidden.</param>
/// <param name="TabSeconds">Circe's tab balance, null when not linked.</param>
public readonly record struct LeashDayInputs(
    DateTime LocalNow,
    int MinutesToday,
    int QuestsDone,
    int QuestsTotal,
    int Streak,
    bool ChasterLinked,
    DateTime? LockEndsUtc,
    bool LockTimerHidden,
    int? TabSeconds);

/// <summary>
/// Builds R, the leashed client's own day report (CONTRACT R), and its wire form. Only the
/// leashed account's client ever writes it, and only while leashed. Chaster numbers ride only
/// when Chaster is linked, and a hidden lock timer stays hidden from the holder too. Pure.
/// </summary>
public static class LeashReportBuilder
{
    public static DayReport Build(in LeashDayInputs w, Assignment? assignment, bool videoWatched, DateTimeOffset nowUtc)
    {
        var today = LeashAssignRule.DayKey(w.LocalNow);
        int? lockLeft = null;
        int? tab = null;
        if (w.ChasterLinked)
        {
            if (w.LockEndsUtc is { } end && !w.LockTimerHidden)
            {
                var left = (end - nowUtc.UtcDateTime).TotalSeconds;
                lockLeft = (int)Math.Clamp(Math.Floor(left), 0, int.MaxValue);
            }
            tab = w.TabSeconds;
        }
        var minutes = Math.Max(0, w.MinutesToday);
        var total = Math.Max(0, w.QuestsTotal);
        var done = Math.Min(Math.Max(0, w.QuestsDone), total);
        return new DayReport(
            Day: today,
            Minutes: minutes,
            QuestsDone: done,
            QuestsTotal: total,
            Streak: Math.Max(0, w.Streak),
            ChasterLinked: w.ChasterLinked,
            LockLeftSeconds: lockLeft,
            TabSeconds: tab,
            AssignDone: LeashAssignRule.Met(assignment, today, minutes, done, videoWatched),
            At: nowUtc);
    }

    public static JObject ToWire(DayReport r)
    {
        var o = new JObject
        {
            ["day"] = r.Day,
            ["minutes"] = r.Minutes,
            ["quests_done"] = r.QuestsDone,
            ["quests_total"] = r.QuestsTotal,
            ["streak"] = r.Streak,
            ["chaster_linked"] = r.ChasterLinked,
            ["lock_left_s"] = r.LockLeftSeconds is int l ? l : JValue.CreateNull(),
            ["tab_s"] = r.TabSeconds is int t ? t : JValue.CreateNull(),
            ["assign_done"] = r.AssignDone,
            ["at"] = r.At.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", System.Globalization.CultureInfo.InvariantCulture),
        };
        return o;
    }
}
