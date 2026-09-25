namespace ConditioningControlPanel.Services
{
    /// <summary>Where the "post your achievements to Discord?" offer goes right now.</summary>
    public enum AchievementSharePromptRouting
    {
        /// <summary>Nothing to ask: sharing is already on.</summary>
        Skip,

        /// <summary>Ask now, as a dialog owned by the panel.</summary>
        Ask,

        /// <summary>Park it as an Inbox row and ask when the user comes back.</summary>
        Inbox,
    }

    /// <summary>
    /// Whether the app may ask about achievement sharing at this moment.
    ///
    /// <para>The offer used to be an ownerless <c>MessageBox</c> fired the instant a Discord link
    /// resolved. A link resolves whenever the OAuth round trip finishes, which can be minutes after
    /// the click - long enough for the user to have started a game - and an ownerless message box
    /// takes the active window as its owner. One player linked Discord, opened Down the Rabbit
    /// Hole, and got the prompt laid over the two doors with the choice underneath it unreachable
    /// (ticket 2026-09-15). Nothing about a permanent opt-in is worth interrupting a run for, so a
    /// game on screen sends it to the Inbox instead, where it waits to be asked for.</para>
    /// </summary>
    public static class AchievementSharePromptRule
    {
        /// <summary>
        /// Whether a game owns the screen, asked two ways. <paramref name="webGameHostUp"/> is the
        /// WebView2 game-window counter; it cannot see the Rabbit Hole DESCENT, which runs inside
        /// the app (its spiral and the two doors) rather than in a game window. The panic key's
        /// surface registry does see it, so a prompt at the doors still goes to the Inbox
        /// (ticket 2026-09-24, Pika: "I couldn't click the thing underneath it").
        /// </summary>
        internal static bool GameOnScreen(bool webGameHostUp)
            => webGameHostUp || Safety.GameSurfaces.AnyOwnsTheScreen();

        /// <param name="alreadySharing">The user has already opted in; there is nothing to offer.</param>
        /// <param name="gameHostUp">A game window (DtRH, the race, the Back Room, the Arcademy and
        /// the rest) is on screen.</param>
        /// <param name="launcherHasTheScreen">The CC Labs launcher is up and the panel is tucked in
        /// the tray. Its Sign in pill reaches this same flow, so without this the offer would open
        /// owned by a HIDDEN window: no visible parent, and free to sit behind the launcher where
        /// nobody can answer it.</param>
        /// <param name="panelOnScreen">The panel window is visible and not minimized. The two
        /// reasons above are the KNOWN ways it loses the screen; this is the general one. The
        /// panel closes to the tray, and a link that resolves minutes after the click can land
        /// with no game and no launcher and still nothing to own a dialog.</param>
        /// <param name="userAsked">The user clicked the Inbox row. That IS the asking, so it beats
        /// every "somebody else has the screen" reason - otherwise the row removes itself,
        /// re-enters here, is told to wait again, and quietly re-posts itself. Safe against the
        /// visibility check too: a row can only be clicked on a panel that is on screen.</param>
        public static AchievementSharePromptRouting Decide(bool alreadySharing, bool gameHostUp,
            bool launcherHasTheScreen, bool panelOnScreen, bool userAsked = false)
        {
            if (alreadySharing) return AchievementSharePromptRouting.Skip;
            if (userAsked) return AchievementSharePromptRouting.Ask;
            return gameHostUp || launcherHasTheScreen || !panelOnScreen
                ? AchievementSharePromptRouting.Inbox
                : AchievementSharePromptRouting.Ask;
        }
    }
}
