namespace ConditioningControlPanel.Services.EmiDesk;

/// <summary>
/// When her window has to be put back in the topmost band (ccp-bugs #1171, "sometimes EMI goes
/// behind other apps"). Only a window that has LOST WS_EX_TOPMOST is repaired. One that is still
/// topmost but sits under a newer topmost window (a video, a flash, the tint host) is left alone:
/// raising her over those on a timer is the #776 flicker, and those windows go away by themselves.
/// </summary>
public static class EmiTopmostRule
{
    public const int WsExTopmost = 0x00000008;

    /// <summary>How often the watch looks. Cheap: one GetWindowLong per tick.</summary>
    public const int PollMs = 3000;

    public static bool NeedsRepair(bool visible, bool wantsTopmost, int exStyle)
        => visible && wantsTopmost && (exStyle & WsExTopmost) == 0;
}
