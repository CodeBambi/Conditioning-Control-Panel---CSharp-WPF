using System;

namespace ConditioningControlPanel.Services.Invites;

/// <summary>
/// The header invite ticket (MainWindow.InviteTicket.cs): a small ticket glyph between the help
/// button and the profile bubble that says "you have an invite to give". Pure, so the suite can
/// hold the rules without a window.
/// </summary>
public static class InviteTicketRule
{
    /// <summary>First read after the panel comes up, so sign-in and session restore settle first.</summary>
    public static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(6);

    /// <summary>A parked app re-reads this often (a new month mints a fresh code).</summary>
    public static readonly TimeSpan RefreshEvery = TimeSpan.FromMinutes(30);

    /// <summary>The idle wobble waits between these two gaps, picked at random each time.</summary>
    public static readonly TimeSpan WobbleMin = TimeSpan.FromSeconds(20);
    public static readonly TimeSpan WobbleMax = TimeSpan.FromSeconds(40);

    /// <summary>Show the ticket only when the server answered and this month has a code nobody used.
    /// Offline, signed out, not a subscriber, or every code taken: hidden.</summary>
    public static bool ShouldShow(InviteMine? mine)
    {
        if (mine == null || !mine.Reachable || mine.Snapshot?.Slots == null) return false;
        foreach (var slot in mine.Snapshot.Slots)
            if (slot != null && slot.State == InviteSlotState.Open) return true;
        return false;
    }

    /// <summary>The friends drawer's "Invite them" link: subscribers only, and not an account that is
    /// only on a borrowed invite week (it has no codes of its own).</summary>
    public static bool OffersInviteLink(bool hasPremiumAccess, bool inviteWeekOnly)
        => hasPremiumAccess && !inviteWeekOnly;

    /// <summary>The gap before the next wobble, inside [<see cref="WobbleMin"/>, <see cref="WobbleMax"/>].</summary>
    public static TimeSpan NextWobble(Random rng)
    {
        var span = (WobbleMax - WobbleMin).TotalMilliseconds;
        return WobbleMin + TimeSpan.FromMilliseconds(rng.NextDouble() * span);
    }
}
