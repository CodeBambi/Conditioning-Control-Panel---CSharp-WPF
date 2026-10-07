namespace ConditioningControlPanel.Services.Chaster;

/// <summary>The rule half of the WPF head's <c>CapNotice</c> (main cd9bee118): which refused booking
/// gets the "day is full" tag. In Core because <see cref="ChasterService"/> raises
/// <see cref="ChasterService.CapRefused"/> on it; the tag's colour and pop layout stay in the head.</summary>
public static class CapNoticeRule
{
    /// <summary>Does this refused booking get the tag.</summary>
    public static bool Shows(string? eventId, int seconds, TabBooking booking, bool unprompted)
    {
        if (unprompted || seconds <= 0 || booking.Booked) return false;
        if (eventId != NatashasFavourite.EventId) return false;
        return booking.Refusal is TabRefusal.DailyCap or TabRefusal.Backlog;
    }
}
