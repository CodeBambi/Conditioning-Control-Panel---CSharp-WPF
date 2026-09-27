namespace ConditioningControlPanel.Services
{
    /// <summary>What the Patreon row in Settings &gt; Account &gt; Link Accounts is offering.</summary>
    public enum PatreonLinkAction
    {
        /// <summary>Nothing to offer: no cloud identity, or the desktop already holds a working grant.</summary>
        Hidden,

        /// <summary>Never linked. The original behaviour, unchanged.</summary>
        Link,

        /// <summary>
        /// The server record carries a patreon_id but this PC holds no working Patreon grant.
        /// Same button, different word.
        /// </summary>
        Reconnect
    }

    /// <summary>
    /// The row's whole state, so the code-behind only reads properties. <see cref="Prominent"/> is
    /// only ever true for <see cref="PatreonLinkAction.Reconnect"/>.
    /// </summary>
    public readonly struct PatreonLinkRow
    {
        public PatreonLinkRow(PatreonLinkAction action, bool prominent)
        {
            Action = action;
            Prominent = prominent;
        }

        public PatreonLinkAction Action { get; }

        /// <summary>
        /// The reconnect matters RIGHT NOW: premium is locked while the server still says this
        /// account is a patron. Drives the accent styling and the one explanatory line.
        /// </summary>
        public bool Prominent { get; }

        /// <summary>Whether the Patreon button is on screen at all.</summary>
        public bool ShowsButton => Action != PatreonLinkAction.Hidden;

        /// <summary>The explanatory line under the row. Only when the loss is already felt.</summary>
        public bool ShowsHint => Prominent;

        /// <summary>
        /// Filled accent button vs. quiet outline. A first link is filled because it is the whole
        /// point of the section; a reconnect only when something is actually missing.
        /// </summary>
        public bool Filled => Action == PatreonLinkAction.Link || Prominent;
    }

    /// <summary>
    /// Decides whether Settings &gt; Account offers "Link Patreon", "Reconnect Patreon", or nothing.
    ///
    /// <para>The bug this exists for (ticket 1551244367040221235, Sep 2026): a patron's Patreon
    /// OAuth grant dies on their PC - refresh token revoked, reinstall, new machine, or they now
    /// sign in with Discord. The SERVER record still carries patreon_id and tier 2, so
    /// <c>HasLinkedPatreon</c> stays true, the 14-day premium grace lapses with nothing to renew
    /// it, premium shuts off, and the old rule HID the Patreon button precisely because the account
    /// was "already linked". The user saw "connected" everywhere with no way back but signing out.</para>
    ///
    /// <para>Deliberately NOT a policy change: nothing here stamps or extends the grace window. It
    /// only decides what the row says.</para>
    ///
    /// <para>Two people must never be nagged, and only one of them by this rule's own whitelist
    /// branch. A WHITELISTED account is entitled with no Patreon grant at all - but
    /// <c>IsWhitelisted</c> is an IN-MEMORY flag written by a validate or a sync, and on a launch
    /// where neither reached the server it reads false. What actually spares those accounts is
    /// <paramref name="hasPremiumNow"/>: ProfileSync re-stamps their premium window on every sync,
    /// so they land on the QUIET row and never the prominent one. A SubscribeStar patron is quiet
    /// for exactly the same reason.</para>
    /// </summary>
    public static class PatreonReconnectRule
    {
        /// <param name="hasUnifiedId">A cloud identity exists. Without one the whole section is moot.</param>
        /// <param name="linkedServerSide">Settings.HasLinkedPatreon - the server record's patreon_id.</param>
        /// <param name="desktopAuthenticated">
        /// This PC holds a grant that WORKS: App.Patreon.IsAuthenticated AND not
        /// <c>GrantLooksDead</c>. IsAuthenticated alone is only "the .dat holds a non-empty access
        /// token", which stays true forever after a refused refresh - the ticket's exact shape.
        /// </param>
        /// <param name="hasPremiumNow">App.Patreon.HasPremiumAccess - tier, whitelist, grace or SubscribeStar.</param>
        /// <param name="whitelisted">App.Patreon.IsWhitelisted - entitled without any Patreon grant.</param>
        public static PatreonLinkRow Decide(
            bool hasUnifiedId,
            bool linkedServerSide,
            bool desktopAuthenticated,
            bool hasPremiumNow,
            bool whitelisted)
        {
            // No cloud account: the linking section belongs to signed-in users only.
            if (!hasUnifiedId) return new PatreonLinkRow(PatreonLinkAction.Hidden, false);

            // The grant works. Whatever the server thinks, there is nothing to fix here.
            if (desktopAuthenticated) return new PatreonLinkRow(PatreonLinkAction.Hidden, false);

            // Never linked - the original offer, and it is still the right one.
            if (!linkedServerSide) return new PatreonLinkRow(PatreonLinkAction.Link, false);

            // Linked, but no working grant on this PC. A whitelisted account loses nothing by that,
            // so it gets no button rather than a chore it can never usefully do.
            if (whitelisted) return new PatreonLinkRow(PatreonLinkAction.Hidden, false);

            return new PatreonLinkRow(PatreonLinkAction.Reconnect, prominent: !hasPremiumNow);
        }
    }
}
