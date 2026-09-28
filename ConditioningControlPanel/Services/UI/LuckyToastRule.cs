namespace ConditioningControlPanel.Services.UI;

/// <summary>
/// When the "LUCKY! 10x XP" toast may appear, and whether a proc needs a NEW show or only a
/// refresh of the one already up.
///
/// <para><b>Why it exists (ccp-bugs #1312).</b> Winning the Back Room's Room Service prize fires a
/// burst of flashes and bubbles, several of them roll a lucky proc, and every proc used to build a
/// brand-new Topmost, AllowsTransparency window with a 30 px drop shadow. The first Show() of a
/// fresh layered window renders synchronously against the render thread, which the Back Room's own
/// fullscreen effects were already keeping busy: the dispatcher log shows the lucky handler taking
/// 869 ms, then 2965 ms, then hanging the UI thread for good (watchdog op
/// <c>MainWindow.OnLuckyProc</c>).</para>
///
/// <para>The rule has two halves. A game surface on screen (the <c>GameSurfaces</c> registry) owns
/// the screen: the proc is still banked, only the toast is skipped. Otherwise ONE window is
/// reused: a proc that lands while the toast is visible refreshes its text and hold timer instead
/// of creating anything.</para>
/// </summary>
internal static class LuckyToastRule
{
    internal enum Decision
    {
        /// <summary>No toast at all: perk notices are off, or a game owns the screen.</summary>
        Skip,
        /// <summary>The toast is hidden: show the reused window.</summary>
        Show,
        /// <summary>The toast is already up: update its text and restart its hold, no Show().</summary>
        Refresh,
    }

    internal static Decision Decide(bool notificationsSuppressed, bool gameOwnsScreen, bool toastVisible)
    {
        if (notificationsSuppressed || gameOwnsScreen) return Decision.Skip;
        return toastVisible ? Decision.Refresh : Decision.Show;
    }
}
