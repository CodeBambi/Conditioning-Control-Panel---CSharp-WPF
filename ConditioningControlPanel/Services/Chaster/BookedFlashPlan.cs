using System;
using System.Windows.Media;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services.Chaster;

/// <summary>
/// Everything the flashing "+0:30" decides, with no window, no dispatcher and no clock of its own.
///
/// <para>The figure is the only thing most players will ever see of Circe's tab while they play:
/// a price lands, a small mono number lifts off the rail padlock and is gone. It has to say three
/// things at a glance - how much, which way, and whether it was the jackpot - and it has to say
/// them the same way at every motion level, because the number is the message and the travel is
/// only the manners.</para>
///
/// <para><b>Colour carries the sign, never the text alone.</b> A "+" and a "-" one pixel wide is
/// not a reading at the size this floats past at, so red is time added, mint is time given back,
/// and gold is the jackpot wipe (the fuse's own gold, the app's "this one is different" colour).
/// The sign still prints, for anyone who cannot separate the two hues.</para>
///
/// <para><b>Coalescing.</b> One player action can book several prices in the same breath - a lock
/// card finishes and pays a typo row and a completion row inside a few milliseconds. Three figures
/// racing each other off the same 40px badge is noise, so bookings inside
/// <see cref="CoalesceMs"/> of the FIRST one sum into a single figure. The window is anchored at
/// the first booking, not slid forward by each new one, so a steady drip of events can never hold
/// one figure on screen forever.</para>
/// </summary>
public static class BookedFlashPlan
{
    /// <summary>Time added. Not the mod accent: the figure means the same under every skin.</summary>
    public static readonly Color AddColour = Color.FromRgb(0xFF, 0x6B, 0x8A);

    /// <summary>Time handed back.</summary>
    public static readonly Color CreditColour = Color.FromRgb(0x5F, 0xFF, 0xD0);

    /// <summary>The jackpot wipe, whichever way it moved the tab.</summary>
    public static readonly Color JackpotColour = Color.FromRgb(0xE0, 0xB0, 0x52);

    /// <summary>Bookings this close to the first one become one figure.</summary>
    public const int CoalesceMs = 400;

    /// <summary>What one figure is showing, and when its coalescing window opened.</summary>
    public readonly record struct Figure(int Seconds, string EventId, long StartedAtMs);

    /// <summary>The drawn figure: text, colour, how far it lifts, how long it lives, and whether
    /// it blinks on arrival.</summary>
    public readonly record struct Plan(string Text, Color Colour, double TravelPx, int DurationMs, bool Flash);

    /// <summary>
    /// Fold a new booking into the figure that is already up, or start a new one.
    ///
    /// <para><paramref name="nowMs"/> is any monotonic millisecond count (Environment.TickCount64
    /// at the call site, a plain counter in the tests). The jackpot wins the merged identity: a
    /// wipe that arrives with an ordinary price beside it still reads gold, because the wipe is
    /// the event worth naming.</para>
    /// </summary>
    public static (Figure Figure, bool IsNew) Merge(Figure? open, int seconds, string? eventId, long nowMs)
    {
        var id = eventId ?? "";
        if (open is not { } live || nowMs - live.StartedAtMs >= CoalesceMs)
            return (new Figure(seconds, id, nowMs), true);

        var merged = live.EventId == CircesTab.JackpotEventId || id == CircesTab.JackpotEventId
            ? CircesTab.JackpotEventId
            : live.EventId;
        return (live with { Seconds = live.Seconds + seconds, EventId = merged }, false);
    }

    /// <summary>
    /// The plan for a figure, or null when there is nothing to show. Zero seconds is nothing: a
    /// refused booking never reaches here, and a merge that nets to zero (a price and its credit
    /// in the same breath) has no number worth floating.
    /// </summary>
    public static Plan? For(Figure figure, MotionLevel motion) => For(figure.EventId, figure.Seconds, motion);

    /// <inheritdoc cref="For(Figure, MotionLevel)"/>
    public static Plan? For(string? eventId, int seconds, MotionLevel motion)
    {
        if (seconds == 0) return null;

        var colour = eventId == CircesTab.JackpotEventId
            ? JackpotColour
            : seconds > 0 ? AddColour : CreditColour;

        // Off still fades - the number is information, and removing it would be removing the
        // feedback rather than the motion. It simply never travels and never blinks.
        var (travel, duration, flash) = motion switch
        {
            MotionLevel.Off => (0.0, 700, false),
            MotionLevel.Reduced => (24.0, 600, false),
            _ => (48.0, 900, true),
        };

        return new Plan(CircesTab.Format(seconds), colour, travel, duration, flash);
    }
}
