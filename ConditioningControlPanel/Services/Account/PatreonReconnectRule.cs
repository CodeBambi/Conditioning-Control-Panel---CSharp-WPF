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
        /// The server record carries a patreon_id but this PC holds no working Patreon token.
        /// Same button, different word.
        /// </summary>
        Reconnect
    }

    /// <summary>
    /// The row's whole state, so the code-behind only reads properties and never re-derives
    /// anything. <see cref="Prominent"/> is only ever true for <see cref="PatreonLinkAction.Reconnect"/>.
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
        /// point of the section; a reconnect is filled only when something is actually missing.
        /// </summary>
        public bool Filled => Action == PatreonLinkAction.Link || Prominent;
    }

    /// <summary>
    /// Decides whether Settings &gt; Account offers "Link Patreon", "Reconnect Patreon", or nothing.
    ///
    /// <para>The bug this exists for (ticket 1551244367040221235, Sep 2026): a patron's Patreon
    /// OAuth grant dies on their PC - refresh token expired, reinstall, new machine, or they now
    /// sign in with Discord. The SERVER record still carries patreon_id and tier 2, so
    /// <c>HasLinkedPatreon</c> stays true, the 14-day premium grace lapses with nothing to renew
    /// it, premium shuts off, and the old rule HID the Patreon button precisely because the
    /// account was "already linked". The user saw "connected" on every surface and had no way
    /// back except signing out.</para>
    ///
    /// <para>Deliberately NOT a policy change: nothing here stamps or extends the grace window.
    /// It only decides what the row says.</para>
    ///
    /// <para>Two people must never be nagged. A WHITELISTED account is entitled without a Patreon
    /// grant at all, so a missing token costs it nothing. A SubscribeStar patron keeps premium
    /// from the other provider, so <paramref name="hasPremiumNow"/> is true for them and the row
    /// stays quiet even if some ancient Patreon link is still on the record.</para>
    /// </summary>
    public static class PatreonReconnectRule
    {
        /// <param name="hasUnifiedId">A cloud identity exists. Without one the whole section is moot.</param>
        /// <param name="linkedServerSide">Settings.HasLinkedPatreon - the server record's patreon_id.</param>
        /// <param name="desktopAuthenticated">App.Patreon.IsAuthenticated - this PC holds usable tokens.</param>
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

            // Linked, but no token on this PC. A whitelisted account loses nothing by that, so
            // it gets no button at all rather than a chore it can never usefully do.
            if (whitelisted) return new PatreonLinkRow(PatreonLinkAction.Hidden, false);

            return new PatreonLinkRow(PatreonLinkAction.Reconnect, prominent: !hasPremiumNow);
        }
    }
}
