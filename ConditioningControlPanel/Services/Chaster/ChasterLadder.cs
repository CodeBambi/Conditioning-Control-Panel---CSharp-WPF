using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json;

namespace ConditioningControlPanel.Services.Chaster;

/// <summary>One row of the month's top ten. <see cref="Named"/> is false when the player did not
/// opt in: <see cref="Name"/> is then a label the server made up ("Locked 7F3A9C").</summary>
public sealed record LadderRow(int Rank, string Name, bool Named, int AddedSeconds, bool You);

/// <summary>The month's top ten by time CCP added (brag rights only, no prize rides on it),
/// plus the caller's own row when they have one. <see cref="ShowName"/> is what the server holds
/// for the caller's name opt-in.</summary>
public sealed record LadderBoard(string Month, IReadOnlyList<LadderRow> Rows, LadderRow? You, bool ShowName);

/// <summary>What the server read off Chaster for this month (total and days counted), or why
/// it would not. <see cref="ClaimedSeconds"/> is the part of the total this PC's day ledger
/// added (time a credit cancelled before it reached the lock), already inside <see cref="Seconds"/>.</summary>
public sealed record LadderVerify(bool Ok, int Seconds, string? Reason, int DaysCounted = 0, int ClaimedSeconds = 0);

/// <summary>One UTC day of the raffle's own ledger (<see cref="LadderLedger"/>).</summary>
public sealed class LadderDay
{
    /// <summary>Seconds of accepted positive bookings that UTC day, after the day and backlog
    /// limits. A refused add never gets here; credits, the jackpot wipe and forgiven misses never
    /// lower it.</summary>
    [JsonProperty("gross")] public int Gross { get; set; }

    /// <summary>Seconds CCP put on the lock that UTC day: pushes that landed, or that nobody
    /// answered and so count as landed.</summary>
    [JsonProperty("pushed")] public int Pushed { get; set; }

    /// <summary>The tab's positive balance (booked, not on the lock yet) when the day's first line
    /// was written, and after its last line. Time still owed at the end of a day reaches the lock on
    /// a later one; these two are how <see cref="ChasterLadder.Claims"/> moves it there.</summary>
    [JsonProperty("open0")] public int OpenAtStart { get; set; }
    [JsonProperty("open")] public int Open { get; set; }
}

/// <summary>The raffle's day ledger, kept in <see cref="TabState"/>: this UTC month and the one
/// before (the server still reads last month for three days).</summary>
public sealed class LadderLedger
{
    [JsonProperty("days")] public Dictionary<string, LadderDay> Days { get; set; } = new(StringComparer.Ordinal);

    /// <summary>The tab's positive balance after the last line written, on any day. Null before the first.</summary>
    [JsonProperty("open")] public int? Open { get; set; }
}

/// <summary>What the raffle verify sends for one UTC day: the time CCP booked that reached the
/// lock or was cancelled by a credit that day (<see cref="Gross"/>), and what went to the lock
/// (<see cref="Pushed"/>). The server only ever counts the difference on top of a day Chaster
/// already verified, and never more than that day verified.</summary>
public readonly record struct LadderClaim(int Gross, int Pushed);

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
///
/// <para>ONLY ADDED TIME COUNTS (owner, 2026-09-29). A wearer link can only add, so a credit
/// cancels part of a slip-up before the push: +1:00, then -0:20, and the lock gets +0:40. Chaster's
/// log can only show the 0:40; the raffle counts 1:00. The day ledger (<see cref="NoteBooked"/>,
/// <see cref="NoteAdded"/>, <see cref="Claims"/>) is what lets the server see the cancelled part.
/// The server takes it only on top of a day Chaster already verified and never more than that day
/// verified, so the ledger can never make a day count and can at most double one.</para>
/// </summary>
public static class ChasterLadder
{
    /// <summary>The fewest minutes between two verifies (the server allows four an hour).</summary>
    public static readonly TimeSpan MinVerifyInterval = TimeSpan.FromMinutes(15);

    public const int TopCount = 10;

    /// <summary>The most one claim ever says for one day, either number. The server bounds every
    /// claim by Chaster's own reading anyway; this only keeps a mangled tab file out of the payload.</summary>
    public const int MaxClaimSeconds = 7 * 24 * 3600;

    public static string MonthKey(DateTime nowUtc) =>
        nowUtc.ToString("yyyy-MM", CultureInfo.InvariantCulture);

    /// <summary>The ledger's day key, the UTC date ("yyyy-MM-dd"), the server's own.</summary>
    public static string DayKey(DateTime nowUtc) =>
        nowUtc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>Count seconds CCP put on the lock. Anything not positive is ignored. Called with
    /// the tab's balance already lowered by the push (<see cref="CircesTab.ApplyPush"/>,
    /// <see cref="CircesTab.ResolvePending"/>), which the day ledger samples.</summary>
    public static void NoteAdded(TabState state, int seconds, DateTime nowUtc)
    {
        if (seconds <= 0) return;
        state.AddedTotalSeconds = Math.Max(0, state.AddedTotalSeconds) + seconds;
        var month = MonthKey(nowUtc);
        if (state.AddedMonth != month) { state.AddedMonth = month; state.AddedMonthSeconds = 0; }
        state.AddedMonthSeconds = Math.Max(0, state.AddedMonthSeconds) + seconds;

        var day = LedgerDay(state, nowUtc, openBefore: (long)state.BalanceSeconds + seconds);
        day.Pushed = Sum(day.Pushed, seconds);
        Sample(state, day);
    }

    /// <summary>A booking landed on the tab (<see cref="ChasterService.Booked"/>), either sign,
    /// with the tab's balance already moved by it. A positive booking is GROSS: added time, and
    /// nothing later takes it back out of the ledger. Every booking moves the balance, which the
    /// ledger samples.</summary>
    public static void NoteBooked(TabState state, int appliedSeconds, DateTime nowUtc)
    {
        if (appliedSeconds == 0) return;
        var day = LedgerDay(state, nowUtc, openBefore: (long)state.BalanceSeconds - appliedSeconds);
        if (appliedSeconds > 0) day.Gross = Sum(day.Gross, appliedSeconds);
        Sample(state, day);
    }

    /// <summary>
    /// The ledger as the raffle verify sends it: every day of this UTC month and the one before
    /// with anything on it.
    ///
    /// <para><see cref="LadderClaim.Gross"/> is the booked time that reached the lock or was
    /// cancelled by a credit that day. Booked time still owed when a UTC day ends (Chaster down, no
    /// lock picked, the day's push limit met) is not that day's: it counts on the day it lands, as
    /// pushed. Without that move, owed time would count twice, once as "cancelled" on the day it was
    /// booked and again as verified on the day it reached the lock. So gross minus pushed is exactly
    /// what credits cancelled. <paramref name="settled"/> false sends the raw bookings instead.</para>
    ///
    /// <para>A day that pushed nothing is not a day the server can verify, so what its credits
    /// cancelled rides with the time still owed and is claimed on the day that time lands (TAB-12:
    /// +5:00 at 23:59:40 UTC, -1:40 at 23:59:50, the 3:20 lands at 00:00:20, and the raffle counts
    /// 5:00 on the second day). A day that ends with nothing owed and nothing pushed drops it: every
    /// slip-up was cancelled and none of it reached a lock.</para>
    /// </summary>
    public static IReadOnlyDictionary<string, LadderClaim> Claims(TabState state, DateTime nowUtc, bool settled = true)
    {
        var claims = new SortedDictionary<string, LadderClaim>(StringComparer.Ordinal);
        if (state.Ladder?.Days is not { } days) return claims;
        var first = MonthKey(PreviousMonth(nowUtc));
        var last = MonthKey(nowUtc);
        long carried = 0;
        foreach (var (key, day) in days.Where(d => d.Value != null && IsDayKey(d.Key)).OrderBy(d => d.Key, StringComparer.Ordinal))
        {
            var raw = settled ? (long)day.Gross + day.OpenAtStart - day.Open : day.Gross;
            var pushed = Math.Clamp(day.Pushed, 0, MaxClaimSeconds);
            if (settled)
            {
                if (pushed > 0) { raw += carried; carried = 0; }
                else if (day.Open > 0) { carried += Math.Max(0, raw); raw = 0; }
                else carried = 0;
            }
            var month = key[..7];
            if (string.CompareOrdinal(month, first) < 0 || string.CompareOrdinal(month, last) > 0) continue;
            var gross = (int)Math.Clamp(raw, 0, MaxClaimSeconds);
            if (gross > 0 || pushed > 0) claims[key] = new LadderClaim(gross, pushed);
        }
        return claims;
    }

    // The day's line, made on first use. Its opening balance is where the last line left the tab,
    // or, on the very first line ever, the balance before this event. Anything older than last
    // month goes: the server never reads it.
    private static LadderDay LedgerDay(TabState state, DateTime nowUtc, long openBefore)
    {
        var ledger = state.Ladder ??= new LadderLedger();
        ledger.Days ??= new Dictionary<string, LadderDay>(StringComparer.Ordinal);
        var key = DayKey(nowUtc);
        if (!ledger.Days.TryGetValue(key, out var day) || day == null)
        {
            day = new LadderDay { OpenAtStart = ledger.Open ?? Positive(openBefore) };
            ledger.Days[key] = day;
            var first = MonthKey(PreviousMonth(nowUtc));
            foreach (var old in ledger.Days.Keys.Where(k => !IsDayKey(k) || string.CompareOrdinal(k[..7], first) < 0).ToList())
                ledger.Days.Remove(old);
        }
        return day;
    }

    private static void Sample(TabState state, LadderDay day)
    {
        var open = Positive(state.BalanceSeconds);
        day.Open = open;
        state.Ladder!.Open = open;
    }

    private static int Positive(long seconds) => (int)Math.Clamp(seconds, 0, int.MaxValue);

    private static int Sum(int a, int b) => (int)Math.Clamp((long)Math.Max(0, a) + b, 0, int.MaxValue);

    private static DateTime PreviousMonth(DateTime nowUtc) => new DateTime(nowUtc.Year, nowUtc.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(-1);

    private static bool IsDayKey(string key) =>
        key.Length == 10 && DateTime.TryParseExact(key, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);

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
