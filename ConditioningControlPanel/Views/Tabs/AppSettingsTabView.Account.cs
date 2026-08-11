namespace ConditioningControlPanel.Views.Tabs
{
    /// <summary>
    /// Settings · ACCOUNT compat surface.
    ///
    /// <para>Every name below used to resolve as <c>PatreonTab.X</c> and is now written by the same
    /// MainWindow partial as <c>AppSettingsTab.X</c> — the controls moved into
    /// <c>Views/Controls/AppSettings/AccountSettingsSection.xaml</c> when the
    /// <c>DetachAccountSectionsInto</c> reparenting trick was retired (gap-report R-2). Nothing here
    /// holds logic; these are the Workshop-cell passthroughs.</para>
    ///
    /// <para>Writers: <c>MainWindow.Patreon.cs</c> (status/link/visibility),
    /// <c>MainWindow.SubscribeStar.cs</c>, <c>MainWindow.CloudBackup.cs</c>,
    /// <c>MainWindow.UiUpdates.cs</c> (offline disabling), <c>MainWindow.xaml.cs</c>
    /// (mod-aware takeover label in <c>RunPatreonFeatures</c>).</para>
    /// </summary>
    public partial class AppSettingsTabView
    {
        // --- Patreon card ---
        internal System.Windows.Controls.Border PatreonLoginCard => SectionAccount.PatreonLoginCard;
        internal System.Windows.Controls.TextBlock TxtPatreonStatus => SectionAccount.TxtPatreonStatus;
        internal System.Windows.Controls.TextBlock TxtPatreonTier => SectionAccount.TxtPatreonTier;
        internal System.Windows.Controls.Button BtnPatreonLogin => SectionAccount.BtnPatreonLogin;

        // --- SubscribeStar card ---
        internal System.Windows.Controls.Border SubscribeStarLoginCard => SectionAccount.SubscribeStarLoginCard;
        internal System.Windows.Controls.TextBlock TxtSubscribeStarStatus => SectionAccount.TxtSubscribeStarStatus;
        internal System.Windows.Controls.TextBlock TxtSubscribeStarTier => SectionAccount.TxtSubscribeStarTier;
        internal System.Windows.Controls.Button BtnSubscribeStarLogin => SectionAccount.BtnSubscribeStarLogin;

        // --- Discord card ---
        internal System.Windows.Controls.Border DiscordLoginCard => SectionAccount.DiscordLoginCard;
        internal System.Windows.Controls.TextBlock TxtDiscordStatus => SectionAccount.TxtDiscordStatus;
        internal System.Windows.Controls.TextBlock TxtDiscordInfo => SectionAccount.TxtDiscordInfo;
        internal System.Windows.Controls.Button BtnDiscordLogin => SectionAccount.BtnDiscordLogin;

        // --- linking ---
        internal System.Windows.Controls.Border AccountLinkingSection => SectionAccount.AccountLinkingSection;
        internal System.Windows.Controls.Button BtnLinkPatreon => SectionAccount.BtnLinkPatreon;
        internal System.Windows.Controls.Button BtnLinkDiscord => SectionAccount.BtnLinkDiscord;

        // --- cloud backup ---
        internal System.Windows.Controls.Border CloudSettingsBackupSection => SectionAccount.CloudSettingsBackupSection;
        internal System.Windows.Controls.TextBlock TxtCloudBackupStatus => SectionAccount.TxtCloudBackupStatus;
        internal System.Windows.Controls.Button BtnBackupSettingsNow => SectionAccount.BtnBackupSettingsNow;
        internal System.Windows.Controls.Button BtnRestoreSettings => SectionAccount.BtnRestoreSettings;

        // --- data & privacy ---
        internal System.Windows.Controls.Border DataPrivacySection => SectionAccount.DataPrivacySection;
        internal System.Windows.Controls.Button BtnExportData => SectionAccount.BtnExportData;

        // --- support development ---
        internal System.Windows.Controls.Border SupportDevelopmentCard => SectionAccount.SupportDevelopmentCard;
        internal System.Windows.Documents.Run RunPatreonFeatures => SectionAccount.RunPatreonFeatures;

        /// <summary>
        /// Repaints the Account section's tier card. Called from <c>UpdatePatreonUI</c>, the choke
        /// point every auth change already runs through.
        /// </summary>
        internal void RefreshAccountTierBadge() => SectionAccount?.RefreshTierBadge();
    }
}
