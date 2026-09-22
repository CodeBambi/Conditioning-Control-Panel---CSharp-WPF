using System;

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
        TimeSpan safetyHold, DateTime utcNow)
    {
        if (safetyHold > TimeSpan.Zero) return new LockClock(LockClockState.Held, HoldClock(safetyHold));
        if (!linked || lookup == LockLookup.Unlinked) return new LockClock(LockClockState.Unlinked, "");

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
