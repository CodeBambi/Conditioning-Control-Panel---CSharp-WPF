using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ConditioningControlPanel.Views.Tabs
{
    public partial class DiscordTabView : UserControl
    {
        public DiscordTabView()
        {
            InitializeComponent();
            // FX lifecycle (PR-4b): entrance stagger on the profile column, and the search box's
            // focus glow wiring. No ambient loop is added here - the OG border already owns that.
            IsVisibleChanged += DiscordTabView_IsVisibleChanged;
        }

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
        private void BtnDiscordTabLogin_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnDiscordTabLogin_Click(sender, e);
        }
        private void BtnDiscord_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnDiscord_Click(sender, e);
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
        private void ChkAllowDiscordDm_Changed(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.ChkAllowDiscordDm_Changed(sender, e);
        }
        private void ChkDiscordRichPresence_Changed(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.ChkDiscordRichPresence_Changed(sender, e);
        }
        // Goon Game sharing toggles — forwarded to MainWindow.Patreon like every other
        // checkbox on this tab (the settings write + sync push lives there).
        private void ChkGoonShareAvatar_Changed(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.ChkGoonShareAvatar_Changed(sender, e);
        }
        private void ChkGoonShareDiscordDm_Changed(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.ChkGoonShareDiscordDm_Changed(sender, e);
        }
        private void ChkGoonRichPresence_Changed(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.ChkGoonRichPresence_Changed(sender, e);
        }
        private void ChkShareAchievements_Changed(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.ChkShareAchievements_Changed(sender, e);
        }
        private void ChkShareLevelUps_Changed(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.ChkShareLevelUps_Changed(sender, e);
        }
        private void ChkShareProfilePicture_Changed(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.ChkShareProfilePicture_Changed(sender, e);
        }
        private void ChkShowLevelInPresence_Changed(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.ChkShowLevelInPresence_Changed(sender, e);
        }
        private void ChkShowOnlineStatus_Changed(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.ChkShowOnlineStatus_Changed(sender, e);
        }
        private void TxtProfileSearch_KeyDown(object sender, KeyEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.TxtProfileSearch_KeyDown(sender, e);
        }
    }
}
