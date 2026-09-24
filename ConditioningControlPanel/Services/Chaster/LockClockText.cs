using System;
using System.Collections.Generic;
using System.Linq;

namespace ConditioningControlPanel.Services.Chaster;

/// <summary>What the rail chip's clock is showing, and therefore how the chip is painted. One
/// value covers both, so the padlock and the digits under it can never disagree.</summary>
public enum LockClockState
{
    /// <summary>No Chaster account linked. Dim padlock, no digits.</summary>
    Unlinked,

    /// <summary>Linked, nothing locked. Open shackle, no digits.</summary>
    NoLock,

    /// <summary>Linked and locked, a countdown to show.</summary>
    Locked,

    /// <summary>Locked but frozen: the clock is stopped, not running down.</summary>
    Frozen,

    /// <summary>Locked, and the keyholder hid the timer. The chip says so rather than guessing.</summary>
    Hidden,

    /// <summary>Chaster could not be asked just now. Whatever was last seen still shows, with a
    /// ring that says it is stale.</summary>
    Away,

    /// <summary>Panic or the emergency exit opened a hold. Nothing books, and the chip counts the
    /// hold down instead of the lock, because that is the number that matters in that minute.</summary>
    Held,

    /// <summary>The player paused the tab. The lock still runs on Chaster, so the digits stay;
    /// the chip greys out and wears a pause mark, because nothing CCP does lands right now.</summary>
    Paused,
}

/// <summary>The chip's readout, with no WPF in sight: the state to paint and the string under the
/// padlock.</summary>
public readonly record struct LockClock(LockClockState State, string Text);

/// <summary>
/// The rail chip's clock, as text. Pure so the state table is a test rather than a screenshot.
///
/// <para><b>Two digits, never three.</b> The readout lives under a 40px padlock in a 56px
/// collapsed rail, so it is written for width first: "12d 4h" over a day, "4h 12m" under one,
/// "12m" under an hour. A lock with four months on it reads "128d", because the hours on a number
/// that size are noise and the column is not there to spend.</para>
/// </summary>
public static class LockClockText
{
    /// <summary>What a hidden timer shows instead of digits.</summary>
    public const string HiddenMark = "?";

    /// <summary>
    /// "12d 4h" over a day, "4h 12m" under a day, "12m" under an hour, "0m" at zero or below.
    /// Days past 99 drop the hours ("128d") rather than wrap the rail.
    /// </summary>
    public static string Countdown(TimeSpan left)
    {
        if (left <= TimeSpan.Zero) return "0m";

        var days = (int)left.TotalDays;
        if (days >= 100) return days + "d";
        if (days >= 1) return $"{days}d {left.Hours}h";

        var hours = (int)left.TotalHours;
        if (hours >= 1) return $"{hours}h {left.Minutes}m";

        // Under a minute is still "a minute left", not "none": a chip that reads 0m while the lock
        // is shut would be the one lie this readout can tell.
        var minutes = (int)left.TotalMinutes;
        return Math.Max(1, minutes) + "m";
    }

    /// <summary>m:ss, for the safety hold. Short enough that seconds are worth showing, and the
    /// only place on the chip where they are.</summary>
    public static string HoldClock(TimeSpan left)
    {
        if (left <= TimeSpan.Zero) return "0:00";
        var total = (int)Math.Ceiling(left.TotalSeconds);
        return $"{total / 60}:{total % 60:00}";
    }

    /// <summary>
    /// The chip's whole state in one call.
    ///
    /// <para>Order is deliberate. The safety hold wins over everything, including a missing link,
    /// because it is the app promising that nothing is being added right now and that promise is
    /// worth more than the lock's own number. After it, being unlinked wins over any stale
    /// snapshot; then the lookup decides; then the snapshot's own flags.</para>
    ///
    /// <para><see cref="LockLookup.Ambiguous"/> (several locks, none picked) reads as
    /// <see cref="LockClockState.NoLock"/>: there is no clock to show until the player picks one,
    /// and an eighth state would need an eighth padlock nobody can tell from the seventh at 24px.
    /// The tooltip is where "pick a lock" belongs, and the chip carries it there.</para>
    ///
    /// <para>The pending balance is not read here on purpose: the pending badge is its own layer
    /// on the chip and must be able to sit over any of these states.</para>
    /// </summary>
    public static LockClock State(LockLookup lookup, LockSnapshot? snapshot, bool linked,
        TimeSpan safetyHold, DateTime utcNow, bool paused = false)
    {
        if (safetyHold > TimeSpan.Zero) return new LockClock(LockClockState.Held, HoldClock(safetyHold));
        if (!linked || lookup == LockLookup.Unlinked) return new LockClock(LockClockState.Unlinked, "");
        // Paused after the hold (the hold is the louder promise) and before the lookup: whatever
        // the lock says, the chip's first job is to say the tab is not running.
        if (paused)
            return new LockClock(LockClockState.Paused,
                snapshot == null || lookup is LockLookup.None or LockLookup.Ambiguous ? "" : snapshot.TimerHidden ? HiddenMark : Digits(snapshot, utcNow));

        // Away keeps the last snapshot, so it still shows a number; it is the ring that says the
        // number is old. Away with nothing ever fetched has nothing to show.
        if (lookup == LockLookup.Away)
            return new LockClock(LockClockState.Away, snapshot == null ? "" : Digits(snapshot, utcNow));

        if (lookup == LockLookup.None || snapshot == null) return new LockClock(LockClockState.NoLock, "");
        if (snapshot.TimerHidden) return new LockClock(LockClockState.Hidden, HiddenMark);
        if (snapshot.IsFrozen) return new LockClock(LockClockState.Frozen, Digits(snapshot, utcNow));
        return new LockClock(LockClockState.Locked, Digits(snapshot, utcNow));
    }

    private static string Digits(LockSnapshot snapshot, DateTime utcNow) =>
        snapshot.Remaining(utcNow) is { } left ? Countdown(left) : HiddenMark;
}

/// <summary>
/// The lock's clock as CCP shows it LIVE, ticking every second (owner, 2026-09-23: eyes are on
/// CCP, so CCP's clock is the first to move; the phone catches up at the next sync).
///
/// <para><b>No extra calls.</b> Everything here is arithmetic over the last <see cref="LockSnapshot"/>
/// plus the tab. The snapshot keeps its own refresh cadence; a second tick costs nothing.</para>
///
/// <para><b>The tab is folded in.</b> A positive balance is time the wearer WILL get: CCP booked
/// it and pushes it on its own schedule. Showing it now is the point of CCP's clock being first.
/// A negative balance (credits) is not taken off: a wearer link can only ADD, so credits wait on
/// the tab to cancel later slip-ups and never shorten the lock.</para>
/// </summary>
public static class LiveLockClock
{
    /// <summary>Seconds on the tab that will land on the lock. Credits never shorten it.</summary>
    public static int PendingAdd(int balanceSeconds) => Math.Max(0, balanceSeconds);

    /// <summary>Time left counting what the tab will add. Null when there is no number to show
    /// (no lock, hidden timer, no end date). An ended lock stays at zero: the key is ready and a
    /// push cannot reopen it.</summary>
    public static TimeSpan? Remaining(LockSnapshot? snapshot, int balanceSeconds, DateTime utcNow)
    {
        if (snapshot?.Remaining(utcNow) is not { } left) return null;
        if (left <= TimeSpan.Zero) return TimeSpan.Zero;
        return left + TimeSpan.FromSeconds(PendingAdd(balanceSeconds));
    }

    /// <summary>
    /// When the lock ends as the live clock counts it: Chaster's own end plus what the tab will
    /// add, so the "Ends" line and the ticking clock never disagree. Null when there is no clock.
    /// A lock already at its end stays at its own end: a push cannot reopen a finished clock.
    /// </summary>
    public static DateTime? EndsAt(LockSnapshot? snapshot, int balanceSeconds, DateTime utcNow)
    {
        if (snapshot is not { TimerHidden: false, EndsAtUtc: { } end }) return null;
        if (Remaining(snapshot, balanceSeconds, utcNow) is not { } left || left <= TimeSpan.Zero) return end;
        return end.AddSeconds(PendingAdd(balanceSeconds));
    }

    /// <summary>Whether the clock runs down on its own: a lock with an end date, not frozen, not hidden.</summary>
    public static bool Ticks(LockSnapshot? snapshot) =>
        snapshot is { TimerHidden: false, IsFrozen: false, EndsAtUtc: not null };

    /// <summary>
    /// The rail's live readout, written for a 56px column: "128d" past 99 days, "2d 4h" over a day,
    /// "3:12:45" under one, "12:05" under an hour. Seconds show whenever a day is not in the way,
    /// because the tick is the thing the rail is for.
    /// </summary>
    public static string Compact(TimeSpan left)
    {
        if (left <= TimeSpan.Zero) return "0:00";
        var days = (int)left.TotalDays;
        if (days >= 100) return days + "d";
        if (days >= 1) return $"{days}d {left.Hours}h";
        var total = (int)Math.Floor(left.TotalSeconds);
        var h = total / 3600;
        var m = total / 60 % 60;
        var s = total % 60;
        return h > 0 ? $"{h}:{m:00}:{s:00}" : $"{m}:{s:00}";
    }

    /// <summary>The big clock's fields, largest first, units as "d" "h" "m" "s". Days only when
    /// there are some; hours only when there are some or days lead; minutes and seconds always.
    /// Fields after the first are two digits so the number does not jump width every second.</summary>
    public static IReadOnlyList<(string Value, string Unit)> Parts(TimeSpan left)
    {
        if (left < TimeSpan.Zero) left = TimeSpan.Zero;
        var total = (long)Math.Floor(left.TotalSeconds);
        var d = total / 86400;
        var h = total / 3600 % 24;
        var m = total / 60 % 60;
        var s = total % 60;
        var parts = new List<(string, string)>();
        if (d > 0) parts.Add((d.ToString(), "d"));
        if (d > 0 || h > 0) parts.Add((parts.Count == 0 ? h.ToString() : h.ToString("00"), "h"));
        parts.Add((parts.Count == 0 ? m.ToString() : m.ToString("00"), "m"));
        parts.Add((s.ToString("00"), "s"));
        return parts;
    }

    /// <summary>The fields as one line, "2d 04h 12m 05s".</summary>
    public static string Big(TimeSpan left) =>
        string.Join(" ", Parts(left).Select(p => p.Value + p.Unit));

    /// <summary>The pending badge, short enough for the shut rail: "+3m", "+2h", "+45s".
    /// Empty when nothing is owed.</summary>
    public static string Badge(int balanceSeconds)
    {
        if (balanceSeconds == 0) return string.Empty;
        var sign = balanceSeconds > 0 ? "+" : "-";
        var a = Math.Abs((long)balanceSeconds);
        if (a >= 3600) return $"{sign}{a / 3600}h";
        if (a >= 60) return $"{sign}{a / 60}m";
        return $"{sign}{a}s";
    }
}
