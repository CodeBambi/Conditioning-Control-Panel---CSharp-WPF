using System;
using System.Collections.Generic;
using System.Linq;

namespace ConditioningControlPanel.Services.Chaster;

/// <summary>
/// The player's own raffle card, as the server read it off Chaster (<c>/chaster/raffle/me</c>).
/// <see cref="Today"/> is the UTC day of the month: 0 before it starts, past
/// <see cref="DaysInMonth"/> once it is over. <see cref="Days"/> are the counted days.
/// </summary>
public sealed record RaffleCard(
    string Month,
    int DaysInMonth,
    int Today,
    IReadOnlyList<int> Days,
    long TotalSeconds,
    int NeedDays,
    long NeedSeconds,
    bool PostDays,
    bool TicketsFrozen,
    int? Ticket);

/// <summary>Where a player stands, in the order the card checks it.</summary>
public enum RaffleStatus
{
    /// <summary>The month has not begun.</summary>
    NotStarted,
    /// <summary>Both bars met; the ticket number comes with the freeze.</summary>
    InTheDraw,
    /// <summary>Days short, and still reachable.</summary>
    NeedDays,
    /// <summary>Days met, time short.</summary>
    NeedTime,
    /// <summary>Not enough days left in the month to reach the bar.</summary>
    Out,
    /// <summary>The month is over (or the list frozen) and the player is not on it.</summary>
    Missed,
    /// <summary>The list is frozen and the player holds a ticket.</summary>
    Ticket,
}

/// <summary>One pip of the month strip.</summary>
public enum RafflePip { Counted, Today, Missed, Ahead }

/// <summary>
/// The Locktober raffle's pure half (owner, 2026-09-26). A raffle, not a competition: anyone with
/// the Chaster link can enter, and a player is in the draw when time CCP added counted on at
/// least <see cref="RaffleCard.NeedDays"/> of the month's days AND the month's total reaches
/// <see cref="RaffleCard.NeedSeconds"/>. A day counts on ANY add, so a Gentle player can make
/// the days. Both numbers are the server's reading of Chaster's log, never this PC's counter.
///
/// <para>The winner is drawn live on stream over the frozen ticket list; this side only shows
/// the player where they stand and, after the freeze, their ticket number.</para>
/// </summary>
public static class ChasterRaffle
{
    public const int DefaultNeedDays = 25;
    public const long DefaultNeedSeconds = 31 * 3600;

    /// <summary>The rules page. PLACEHOLDER until the owner publishes the final text.</summary>
    public const string RulesUrl = "https://cclabs.app/locktober-rules";

    /// <summary>The counted days that fall inside the month, once each, ascending.</summary>
    public static IReadOnlyList<int> Counted(RaffleCard card) =>
        card.Days.Where(d => d >= 1 && d <= card.DaysInMonth).Distinct().OrderBy(d => d).ToList();

    public static int DaysCounted(RaffleCard card) => Counted(card).Count;

    /// <summary>Days that can still count: today and after, not counted yet. The whole month
    /// before it starts, none once it is over.</summary>
    public static int DaysStillOpen(RaffleCard card)
    {
        if (card.Today > card.DaysInMonth) return 0;
        var from = Math.Max(1, card.Today);
        var counted = Counted(card);
        var open = 0;
        for (var d = from; d <= card.DaysInMonth; d++)
            if (!counted.Contains(d)) open++;
        return open;
    }

    public static bool Eligible(RaffleCard card) =>
        DaysCounted(card) >= card.NeedDays && card.TotalSeconds >= card.NeedSeconds;

    /// <summary>True once 25 days are out of reach, whatever happens next.</summary>
    public static bool CannotReachDays(RaffleCard card) =>
        DaysCounted(card) + DaysStillOpen(card) < card.NeedDays;

    public static RaffleStatus Status(RaffleCard card)
    {
        if (card.TicketsFrozen) return card.Ticket is > 0 ? RaffleStatus.Ticket : RaffleStatus.Missed;
        if (card.Today <= 0) return RaffleStatus.NotStarted;
        if (Eligible(card)) return RaffleStatus.InTheDraw;
        if (card.Today > card.DaysInMonth) return RaffleStatus.Missed;
        if (CannotReachDays(card)) return RaffleStatus.Out;
        if (DaysCounted(card) < card.NeedDays) return RaffleStatus.NeedDays;
        return RaffleStatus.NeedTime;
    }

    /// <summary>Days still to count (NeedDays), or 0.</summary>
    public static int DaysShort(RaffleCard card) => Math.Max(0, card.NeedDays - DaysCounted(card));

    /// <summary>Time still to add (NeedTime), or 0.</summary>
    public static long TimeShort(RaffleCard card) => Math.Max(0, card.NeedSeconds - Math.Max(0, card.TotalSeconds));

    /// <summary>
    /// The status line as a loc key and its one argument (already a string), so the page and the
    /// tests agree on which sentence a card gets.
    /// </summary>
    public static (string Key, string? Arg) StatusText(RaffleCard card) =>
        Status(card) switch
        {
            RaffleStatus.NotStarted => ("chaster_raffle_soon", null),
            RaffleStatus.InTheDraw => ("chaster_raffle_in", null),
            RaffleStatus.NeedDays when DaysShort(card) == 1 => ("chaster_raffle_need_day", null),
            RaffleStatus.NeedDays => ("chaster_raffle_need_days", DaysShort(card).ToString()),
            RaffleStatus.NeedTime => ("chaster_raffle_need_time", ChasterLadder.FormatClock(TimeShort(card))),
            RaffleStatus.Out => ("chaster_raffle_out", card.NeedDays.ToString()),
            RaffleStatus.Ticket => ("chaster_raffle_ticket", card.Ticket!.Value.ToString()),
            _ => ("chaster_raffle_missed", null),
        };

    /// <summary>The line under the status about the ticket: its number comes on the 1st of the
    /// next month while in the draw and not yet frozen. Null otherwise.</summary>
    public static DateTime? TicketDue(RaffleCard card) =>
        Status(card) == RaffleStatus.InTheDraw && MonthStart(card.Month) is { } start ? start.AddMonths(1) : null;

    /// <summary>"2026-10" -> 1 October 2026 (UTC date), or null.</summary>
    public static DateTime? MonthStart(string month) =>
        DateTime.TryParseExact(month + "-01", "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var d)
            ? DateTime.SpecifyKind(d, DateTimeKind.Utc)
            : null;

    /// <summary>The month strip, one pip a day.</summary>
    public static IReadOnlyList<RafflePip> Pips(RaffleCard card)
    {
        var counted = Counted(card);
        var pips = new List<RafflePip>(card.DaysInMonth);
        for (var d = 1; d <= card.DaysInMonth; d++)
        {
            if (counted.Contains(d)) pips.Add(RafflePip.Counted);
            else if (d == card.Today) pips.Add(RafflePip.Today);
            else if (d < card.Today) pips.Add(RafflePip.Missed);
            else pips.Add(RafflePip.Ahead);
        }
        return pips;
    }

    /// <summary>"Day 12 of 31", clamped into the month (0 before it, the last day after it).</summary>
    public static int DayShown(RaffleCard card) => Math.Clamp(card.Today, 0, card.DaysInMonth);
}
