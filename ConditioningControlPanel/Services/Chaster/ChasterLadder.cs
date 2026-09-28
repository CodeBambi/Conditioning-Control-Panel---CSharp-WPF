using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace ConditioningControlPanel.Services.Chaster;

/// <summary>One row of the month's top ten. <see cref="Named"/> is false when the player did not
/// opt in: <see cref="Name"/> is then a label the server made up ("Locked 7F3A9C").</summary>
public sealed record LadderRow(int Rank, string Name, bool Named, int AddedSeconds, bool You);

/// <summary>The month's top ten by time CCP added (brag rights only, no prize rides on it),
/// plus the caller's own row when they have one. <see cref="ShowName"/> is what the server holds
/// for the caller's name opt-in.</summary>
public sealed record LadderBoard(string Month, IReadOnlyList<LadderRow> Rows, LadderRow? You, bool ShowName);

/// <summary>What the server read off Chaster for this month (total and days counted), or why
/// it would not.</summary>
public sealed record LadderVerify(bool Ok, int Seconds, string? Reason, int DaysCounted = 0);

/// <summary>
/// The heads-up clock's pure half: the gross "time CCP added" counter kept in
/// <see cref="TabState"/>, and when it is worth asking the server to read the lock again for the
/// Locktober raffle (<see cref="ChasterRaffle"/>).
///
/// <para>ADDED ONLY. The counter moves on an add that landed (or is counted as landed after a
/// push nobody answered) and on nothing else: a credit, a removal, the jackpot wipe and time the
/// player takes off on Chaster never lower it. The page says so in plain words.</para>
///
/// <para>THE RAFFLE NEVER TRUSTS THIS COUNTER (anti-cheat review, 2026-09-26). It is what this
/// machine saw, shown on the page. The raffle's days and total are the ones the server reads off
/// the lock's own Chaster history (<c>/chaster/raffle/verify</c>), so a hand-edited tab file moves
/// nothing but its own display.</para>
///
/// <para>Months are UTC ("yyyy-MM"), the server's key, so every player's month turns over at the
/// same moment.</para>
/// </summary>
public static class ChasterLadder
{
    /// <summary>The fewest minutes between two verifies (the server allows four an hour).</summary>
    public static readonly TimeSpan MinVerifyInterval = TimeSpan.FromMinutes(15);

    public const int TopCount = 10;

    public static string MonthKey(DateTime nowUtc) =>
        nowUtc.ToString("yyyy-MM", CultureInfo.InvariantCulture);

    /// <summary>Count seconds CCP put on the lock. Anything not positive is ignored.</summary>
    public static void NoteAdded(TabState state, int seconds, DateTime nowUtc)
    {
        if (seconds <= 0) return;
        state.AddedTotalSeconds = Math.Max(0, state.AddedTotalSeconds) + seconds;
        var month = MonthKey(nowUtc);
        if (state.AddedMonth != month) { state.AddedMonth = month; state.AddedMonthSeconds = 0; }
        state.AddedMonthSeconds = Math.Max(0, state.AddedMonthSeconds) + seconds;
    }

    /// <summary>Lifetime adds. A tab older than this counter still knows what it pushed (net is
    /// a floor for gross, since removals never exceed adds), so the total starts from there.</summary>
    public static long Lifetime(TabState state) =>
        Math.Max(Math.Max(0, state.AddedTotalSeconds), Math.Max(0, state.PushedNetSeconds));

    /// <summary>Adds in the UTC month of <paramref name="nowUtc"/>. 0 once the month has turned.</summary>
    public static int ThisMonth(TabState state, DateTime nowUtc) =>
        state.AddedMonth == MonthKey(nowUtc) ? Math.Max(0, state.AddedMonthSeconds) : 0;

    /// <summary>Whether a verify may go now: never before, or the last one is old enough.</summary>
    public static bool VerifyDue(DateTime? lastUtc, DateTime nowUtc) =>
        lastUtc is not { } last || nowUtc - last >= MinVerifyInterval;

    /// <summary>When the next verify may go, or null when one may go now.</summary>
    public static DateTime? VerifyAt(DateTime? lastUtc, DateTime nowUtc) =>
        VerifyDue(lastUtc, nowUtc) ? null : lastUtc!.Value + MinVerifyInterval;

    /// <summary>The player's own row below the ten, when they are ranked but not in them.</summary>
    public static LadderRow? OwnRowBelow(LadderBoard board) =>
        board.You != null && !board.Rows.Any(r => r.You) ? board.You : null;

    /// <summary>The heads-up clock: "0:00:00", hours unbounded ("126:04:09").</summary>
    public static string FormatClock(long seconds)
    {
        var s = Math.Max(0, seconds);
        return $"{s / 3600}:{s / 60 % 60:00}:{s % 60:00}";
    }
}
