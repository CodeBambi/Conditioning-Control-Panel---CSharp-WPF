namespace ConditioningControlPanel.Services.Friends;

/// <summary>
/// When the friends poll runs (CONTRACT.md, Presence). Pure, so the cadence is tested without a
/// timer: every 20 s while the drawer is open, or while a friend is online and the app is in the
/// foreground; every 120 s otherwise; never while signed out. The full list (<c>state</c>) rides
/// every fifth poll, and any poll right after the drawer opens.
/// </summary>
public static class FriendsPollRule
{
    public const int FastSeconds = 20;
    public const int SlowSeconds = 120;

    /// <summary>How often the full <c>state</c> rides along with a poll.</summary>
    public const int StateEvery = 5;

    /// <summary>Seconds until the next poll; 0 means do not poll at all.</summary>
    public static int NextIntervalSeconds(bool signedIn, bool drawerOpen, bool anyOnline, bool foreground)
    {
        if (!signedIn) return 0;
        if (drawerOpen) return FastSeconds;
        if (anyOnline && foreground) return FastSeconds;
        return SlowSeconds;
    }

    /// <summary>Whether poll number <paramref name="pollIndex"/> (0 = the first since sign-in)
    /// also fetches the full list.</summary>
    public static bool FetchState(int pollIndex, bool drawerJustOpened)
    {
        if (drawerJustOpened) return true;
        if (pollIndex < 0) return true;
        return pollIndex % StateEvery == 0;
    }
}
