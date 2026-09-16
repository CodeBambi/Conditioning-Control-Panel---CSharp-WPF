using System;

namespace ConditioningControlPanel
{
    /// <summary>
    /// What the welcome-back sheet should be, if anything. A value, not a window: everything here
    /// is decided from plain inputs so the rules can be read (and tested) without a dispatcher, a
    /// settings file or a network round trip.
    /// </summary>
    /// <param name="ShowSheet">False means say nothing at all this launch.</param>
    /// <param name="ShowRestoreRow">Offer to pull the cloud settings backup onto this PC.</param>
    /// <param name="ShowFlavourRow">Offer to fetch the mod the backup was running.</param>
    /// <param name="FlavourPackId">The pack behind that mod, or null when there is nothing to fetch.</param>
    public sealed record WelcomeBackPlan(
        bool ShowSheet,
        bool ShowRestoreRow,
        bool ShowFlavourRow,
        string? FlavourPackId)
    {
        /// <summary>The "say nothing" answer. Every early return uses this one instance.</summary>
        public static readonly WelcomeBackPlan Nothing = new(false, false, false, null);
    }

    /// <summary>
    /// The rules behind the one sheet a returning user gets on a new PC.
    ///
    /// <para><b>What it replaced.</b> Three unowned, task-modal MessageBoxes ("a cloud backup was
    /// found", "restore failed", "settings restored") that fired on exactly the population the
    /// first-run wizard also claims, plus the What's New dialog and the season-rollover box. On a
    /// new machine a returning user could take all five in a row before reaching the app. The
    /// sheet is one surface, at one point on the startup ladder, that says all of it.</para>
    ///
    /// <para><b>Conservative by construction.</b> Every input that is unknown reads as "no". A
    /// sheet that fires when it should not is a popup in a redesign whose entire point was to
    /// delete popups, and there is no cost to staying quiet: the Mod Manager still downloads
    /// flavour, Settings still restores a backup by hand, and the ? panel still carries the patch
    /// notes.</para>
    /// </summary>
    public static class WelcomeBackDecision
    {
        /// <summary>
        /// Level at which a returning user with no cloud backup is still worth greeting. Below it
        /// there is nothing to welcome back TO - no backup to offer, no progress to carry - so the
        /// sheet would be a modal that says only hello.
        /// </summary>
        public const int GreetWithoutBackupFromLevel = 2;

        /// <param name="settingsFileWasMissing">This launch started with no settings file.</param>
        /// <param name="factoryReset">The missing file is a deliberate Settings, Data wipe. Its own
        /// undo must not be offered back under fresh-install copy.</param>
        /// <param name="hasCloudIdentity">A unified id is present, so there is an account to be
        /// welcomed back.</param>
        /// <param name="backupExists">The server holds a settings backup for that account.</param>
        /// <param name="playerLevel">Level currently on this device (server-synced by the time the
        /// sheet is decided).</param>
        /// <param name="backupFlavourPackId">Pack id behind the backup's active mod, or null when
        /// the backup ran CCP Default, a user mod, or nothing that maps to a pack.</param>
        /// <param name="flavourPackInstalled">That pack is already on this disk.</param>
        public static WelcomeBackPlan Decide(
            bool settingsFileWasMissing,
            bool factoryReset,
            bool hasCloudIdentity,
            bool backupExists,
            int playerLevel,
            string? backupFlavourPackId,
            bool flavourPackInstalled)
        {
            // Not a fresh device: this user's settings are right where they left them.
            if (!settingsFileWasMissing) return WelcomeBackPlan.Nothing;

            // A factory reset is a fresh settings file the user asked for. Offering to undo it is
            // the one thing the sheet must never do.
            if (factoryReset) return WelcomeBackPlan.Nothing;

            // No account, nobody to welcome back. A genuinely new user gets the wizard instead.
            if (!hasCloudIdentity) return WelcomeBackPlan.Nothing;

            // No backup AND nothing accomplished: an account that exists but has never been used
            // is, for this purpose, a new install.
            if (!backupExists && playerLevel < GreetWithoutBackupFromLevel) return WelcomeBackPlan.Nothing;

            // The flavour row only means something when the backup names a downloadable pack that
            // is not already here. A pack already on disk needs no offer, and a backup running CCP
            // Default has no pack at all.
            var showFlavour = backupExists
                              && !string.IsNullOrWhiteSpace(backupFlavourPackId)
                              && !flavourPackInstalled;

            return new WelcomeBackPlan(
                ShowSheet: true,
                ShowRestoreRow: backupExists,
                ShowFlavourRow: showFlavour,
                FlavourPackId: showFlavour ? backupFlavourPackId : null);
        }

        /// <summary>
        /// Whether the sheet may print its one muted line about the leaderboard having rotated.
        ///
        /// <para>Only the server gets to say a season ended. <c>CurrentSeasonKey</c> falls back to
        /// the wall-clock month when no sync has happened, and that fallback rolls itself over on
        /// the 1st for every never-synced install - which is how a machine that has never spoken to
        /// the server announces a rotation it invented. The same rule guards the recap card itself
        /// (<c>SeasonRecapService.IsSeasonKeyServerConfirmed</c>); this is its one-line twin.</para>
        ///
        /// <para>And the season has to have actually moved. A fresh settings file holds no season
        /// at all, and lane D's silent adoption then writes the server's key straight in, so
        /// "different from what this device holds" is only true when there is a real earlier key to
        /// compare against - the backup's, or a device key that predates the server's. When in
        /// doubt, no line.</para>
        /// </summary>
        /// <param name="currentSeason">The season key in force, from the server.</param>
        /// <param name="seasonAlreadySeen">The most recent season this user is known to have seen -
        /// this device's <c>LastSeasonResetSeen</c> when it holds one, otherwise the backup's.</param>
        /// <param name="serverConfirmed"><c>SeasonRecapService.IsSeasonKeyServerConfirmed</c>.</param>
        public static bool ShouldShowSeasonLine(string? currentSeason, string? seasonAlreadySeen, bool serverConfirmed)
        {
            if (!serverConfirmed) return false;
            if (string.IsNullOrWhiteSpace(currentSeason)) return false;
            if (string.IsNullOrWhiteSpace(seasonAlreadySeen)) return false;

            // Zero-padded yyyy-MM: ordinal order is chronological order. Strictly after only -
            // an equal key is the same season and a backward one is a desync, and neither is news.
            return string.CompareOrdinal(currentSeason, seasonAlreadySeen) > 0;
        }
    }
}
