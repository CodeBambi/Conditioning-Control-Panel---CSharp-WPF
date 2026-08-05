using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ConditioningControlPanel.Views.Controls;

namespace ConditioningControlPanel.Views.Tabs
{
    public partial class DiscordTabView : UserControl
    {
        /// <summary>
        /// The sharing controls that used to occupy this tab's right-hand column. They now live in
        /// the Privacy &amp; Sharing dialog, but the instance is created here and kept for the life
        /// of the tab: MainWindow writes into these checkboxes from ~25 places (login state changes,
        /// settings loads, cross-tab toggle mirroring) and must never depend on a dialog being open.
        /// The dialog borrows this panel while it is on screen and hands it back on close.
        /// </summary>
        internal ProfilePrivacyPanel PrivacyPanel { get; }

        public DiscordTabView()
        {
            InitializeComponent();
            PrivacyPanel = new ProfilePrivacyPanel();
            // FX lifecycle (PR-4b): entrance stagger on the profile column, and the search box's
            // focus glow wiring. No ambient loop is added here - the OG border already owns that.
            IsVisibleChanged += DiscordTabView_IsVisibleChanged;
        }

        // ---- passthroughs -------------------------------------------------------------
        // Every control MainWindow's partials address as `DiscordTab.X` keeps that exact name and
        // type after the relocation, so not one partial had to change. Reference equality holds
        // (MainWindow.Patreon compares `DiscordTab.ChkDiscordTabAllowDm != chk` to break toggle
        // feedback loops), because these return the panel's single instance.

        internal TextBlock TxtDiscordTabStatus => PrivacyPanel.TxtDiscordTabStatus;
        internal TextBlock TxtDiscordTabInfo => PrivacyPanel.TxtDiscordTabInfo;
        internal Button BtnDiscordTabLogin => PrivacyPanel.BtnDiscordTabLogin;
        internal CheckBox ChkDiscordTabRichPresence => PrivacyPanel.ChkDiscordTabRichPresence;
        internal CheckBox ChkDiscordTabShowLevel => PrivacyPanel.ChkDiscordTabShowLevel;
        internal CheckBox ChkDiscordTabShowOnline => PrivacyPanel.ChkDiscordTabShowOnline;
        internal CheckBox ChkDiscordTabShareAchievements => PrivacyPanel.ChkDiscordTabShareAchievements;
        internal CheckBox ChkDiscordTabShareLevelUps => PrivacyPanel.ChkDiscordTabShareLevelUps;
        internal CheckBox ChkDiscordTabAllowDm => PrivacyPanel.ChkDiscordTabAllowDm;
        internal CheckBox ChkDiscordTabSharePfp => PrivacyPanel.ChkDiscordTabSharePfp;
        internal CheckBox ChkGoonShareAvatar => PrivacyPanel.ChkGoonShareAvatar;
        internal CheckBox ChkGoonShareDiscordDm => PrivacyPanel.ChkGoonShareDiscordDm;
        internal CheckBox ChkGoonRichPresence => PrivacyPanel.ChkGoonRichPresence;

        // The hero avatar became an AdornedAvatar in Phase 3 (avatar + decoration + presence dot in
        // one reusable control). Its two writable parts keep their old names and types here, so the
        // MainWindow partials that have always written DiscordTab.ProfileViewerAvatar.ImageSource
        // and DiscordTab.ProfileOnlineIndicator.Fill did not have to change.
        internal System.Windows.Media.ImageBrush ProfileViewerAvatar => ProfileHeroAvatar.AvatarBrush;
        internal System.Windows.Shapes.Ellipse ProfileOnlineIndicator => ProfileHeroAvatar.PresenceDot;

        // ---- forwarding ---------------------------------------------------------------

        private void DiscordTabView_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.OnProfileTabVisibilityChanged(IsVisible);
        }

        private void BtnChangeDisplayName_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnChangeDisplayName_Click(sender, e);
        }
        private void BtnClearProfile_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnClearProfile_Click(sender, e);
        }
        private void BtnDeleteProfile_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnDeleteProfile_Click(sender, e);
        }
        private void BtnProfileDiscord_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnProfileDiscord_Click(sender, e);
        }
        private void BtnProfileSearch_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnProfileSearch_Click(sender, e);
        }
        private void BtnViewMyProfile_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnViewMyProfile_Click(sender, e);
        }
        private void BtnProfilePrivacy_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.OpenProfilePrivacyDialog();
        }
        private void BtnProfileCustomize_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.OpenProfileCustomizeDialog();
        }
        private void TxtProfileSearch_KeyDown(object sender, KeyEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.TxtProfileSearch_KeyDown(sender, e);
        }
    }
}
