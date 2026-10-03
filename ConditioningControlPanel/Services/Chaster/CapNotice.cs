using System.Windows.Media;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services.Chaster;

/// <summary>
/// The "day is full" tag a red bubble shows when the tab has no room left for its price.
///
/// <para>Owner, 2026-10-03: once Circe's tab hits its daily limit (or its backlog limit), red
/// bubbles keep spawning as before, but a pop the player caused used to book nothing and show
/// nothing, which read as broken. Now the pop shows a short muted tag where the bubble was.</para>
///
/// <para><b>Narrow on purpose.</b> Only Natasha's favourite popped by the player, only an add, and
/// only the two refusals that mean "the tab is full": <see cref="TabRefusal.DailyCap"/> and
/// <see cref="TabRefusal.Backlog"/>. A paused tab, the safety hold, a Remote limit, a row's own
/// cap or an unprompted booking (a red flash's ring running out) stay silent, and so does every
/// row that has no float of its own today. A resist (the -1:00 credit) never reaches here.</para>
/// </summary>
public static class CapNotice
{
    /// <summary>One tag per this many ms, so a burst of pops does not stack tags.</summary>
    public const int ThrottleMs = 2000;

    /// <summary>Muted lilac grey: information, not a price. Never the add red or the credit mint.</summary>
    public static readonly Color Colour = Color.FromRgb(0xB8, 0xB0, 0xC8);

    /// <summary>Does this refused booking get the tag.</summary>
    public static bool Shows(string? eventId, int seconds, TabBooking booking, bool unprompted)
    {
        if (unprompted || seconds <= 0 || booking.Booked) return false;
        if (eventId != NatashasFavourite.EventId) return false;
        return booking.Refusal is TabRefusal.DailyCap or TabRefusal.Backlog;
    }

    /// <summary>Is a tag still inside the throttle window of the last one shown.</summary>
    public static bool Throttled(long? lastShownMs, long nowMs) =>
        lastShownMs is { } last && nowMs - last < ThrottleMs;

    /// <summary>The loc key for the refusal: the day's limit, or the tab's backlog limit.</summary>
    public static string TextKey(TabRefusal refusal) =>
        refusal == TabRefusal.Backlog ? "chaster_pop_tab_full" : "chaster_pop_day_full";

    /// <summary>The tag as a figure plan: no source badge, no blink, a small lift at full motion.</summary>
    public static BookedFlashPlan.Plan Look(string text, MotionLevel motion) =>
        new(text, Colour, motion == MotionLevel.Full ? 24.0 : 0.0, 1300, false, null);

    /// <summary>How the tag moves. Smaller than a price and never punches or throws sparks;
    /// Motion Off (and Reduced) only fades.</summary>
    public static BookedPopLayout.PopPlan Pop(MotionLevel motion) => motion switch
    {
        MotionLevel.Full => new BookedPopLayout.PopPlan(30, 28, 1300, 0, 0, 450, 0),
        _ => new BookedPopLayout.PopPlan(30, 0, 1300, 0, 0, 450, 0),
    };
}
