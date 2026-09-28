using System;
using System.Collections.Generic;

namespace ConditioningControlPanel.Services.Chaster;

/// <summary>The deeper rows (see <see cref="TabDayEnd"/>): heat counts, the streak, and the two
/// verdicts on a day that just ended. Every verdict books on TODAY's date, so it counts against
/// today's limit like any other cost and never rewinds a day that has already closed.</summary>
public sealed partial class ChasterService
{
    /// <summary>Conditioning minutes on a local day ("yyyy-MM-dd"), or null when nobody can tell.
    /// Set by the app from the feature day log; tests hand in their own.</summary>
    public Func<string, int?>? MinutesOn { get; set; }

    // Caller does not hold _gate.
    private int HeatCount(string eventId)
    {
        lock (_gate)
        {
            var today = CircesTab.DayKey(_localNow());
            return _tab.HeatDay == today && _tab.Heat != null && _tab.Heat.TryGetValue(eventId, out var n) ? Math.Max(0, n) : 0;
        }
    }

    // Caller holds _gate. Counts every cost that landed, whether or not heat is on, so switching
    // heat on at noon knows what the morning already cost.
    private void NoteHeat(string eventId)
    {
        var today = CircesTab.DayKey(_localNow());
        if (_tab.HeatDay != today || _tab.Heat == null)
        {
            _tab.HeatDay = today;
            _tab.Heat = new Dictionary<string, int>(StringComparer.Ordinal);
        }
        _tab.Heat[eventId] = (_tab.Heat.TryGetValue(eventId, out var n) ? n : 0) + 1;
    }

    // A session finished. The streak is tracked whether or not its row is on, so switching it on
    // mid-streak counts the days already done.
    private void NoteStreak(ChasterOptions options)
    {
        bool pays;
        lock (_gate)
        {
            var local = _localNow();
            var before = _tab.StreakDay == CircesTab.DayKey(local) ? _tab.Streak : -1;
            var after = TabDayEnd.NextStreak(_tab.StreakDay, _tab.Streak, local);
            pays = TabDayEnd.StreakPays(before, after);
            _tab.StreakDay = CircesTab.DayKey(local);
            _tab.Streak = after;
            SaveTab();
        }
        if (pays && options.Prices.Contains(TabDayEnd.StreakEventId))
            BookSeconds(TabDayEnd.StreakEventId, TabDayEnd.StreakSeconds);
    }

    /// <summary>The daily quest board as it stands: how many of today's dailies are still open.
    /// Call on every board change. When the board it last saw belongs to a day that is over, that
    /// day is judged first, before today's board replaces it.</summary>
    public void NoteQuestBoard(int open)
    {
        if (!IsLinked) return;
        JudgeBoard();
        lock (_gate)
        {
            _tab.BoardDay = CircesTab.DayKey(_localNow());
            _tab.BoardOpen = Math.Clamp(open, 0, TabDayEnd.MaxDailies);
            SaveTab();
        }
    }

    // A day CCP was open on just ended (NoteSeen found a new day). Idle is judged here; the
    // dailies are judged off their own snapshot, which may be older.
    private void JudgeEndedDay(string? endedDay)
    {
        JudgeBoard();
        if (string.IsNullOrEmpty(endedDay) || !Active(out var options)) return;
        if (!options.Prices.Contains(TabDayEnd.IdleEventId)) return;
        int? minutes;
        try { minutes = MinutesOn?.Invoke(endedDay); }
        catch (Exception ex) { Diag.Swallowed(ex, "chaster idle day"); minutes = null; }
        var charge = TabDayEnd.IdleCharge(minutes);
        if (charge > 0) BookSeconds(TabDayEnd.IdleEventId, charge);
    }

    private void JudgeBoard()
    {
        int open;
        lock (_gate)
        {
            var today = CircesTab.DayKey(_localNow());
            if (string.IsNullOrEmpty(_tab.BoardDay) || _tab.BoardDay == today) return;
            open = _tab.BoardOpen;
            // Judged once, whatever the verdict: the snapshot is spent.
            _tab.BoardDay = null;
            _tab.BoardOpen = 0;
            SaveTab();
        }
        if (!Active(out var options) || !options.Prices.Contains(TabDayEnd.DailiesEventId)) return;
        var charge = TabDayEnd.DailiesCharge(open);
        if (charge > 0) BookSeconds(TabDayEnd.DailiesEventId, charge);
    }
}
