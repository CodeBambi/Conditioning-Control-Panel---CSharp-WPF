using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace ConditioningControlPanel.Views.Controls
{
    /// <summary>
    /// The Profile tab's "Privacy &amp; Sharing" body. Every handler here forwards to the
    /// identically-named MainWindow method, exactly as DiscordTabView used to.
    ///
    /// The one difference from a normal tab control: this panel is created by DiscordTabView but
    /// only ever parented by <see cref="ProfilePrivacyDialog"/>, so it is usually outside any
    /// window's visual tree. <c>Window.GetWindow(this)</c> would therefore return the dialog (or
    /// null) instead of MainWindow — <see cref="Host"/> looks MainWindow up on the application
    /// instead, which is correct in both states.
    /// </summary>
    public partial class ProfilePrivacyPanel : UserControl
    {
        public ProfilePrivacyPanel()
        {
            InitializeComponent();
        }

        // IsInitialized gate: this panel is constructed inside DiscordTabView's constructor, i.e.
        // while MainWindow.xaml is still being parsed — and the XAML defaults (IsChecked="True")
        // raise Checked during that parse. MainWindow is already in Application.Windows at that
        // point but its named fields (DiscordTab, ...) are not assigned yet, and forwarding a
        // parse-time default into a settings handler would overwrite the user's saved preference.
        // The old in-tab controls got this for free: Window.GetWindow(this) was null during parse.
        private static MainWindow? Host
        {
            get
            {
                var mw = Application.Current?.Windows.OfType<MainWindow>().FirstOrDefault()
                         ?? Application.Current?.MainWindow as MainWindow;
                return mw?.IsInitialized == true ? mw : null;
            }
        }

        private void BtnDiscordTabLogin_Click(object sender, RoutedEventArgs e)
            => Host?.BtnDiscordTabLogin_Click(sender, e);

        private void ChkDiscordRichPresence_Changed(object sender, RoutedEventArgs e)
            => Host?.ChkDiscordRichPresence_Changed(sender, e);

        private void ChkShowLevelInPresence_Changed(object sender, RoutedEventArgs e)
            => Host?.ChkShowLevelInPresence_Changed(sender, e);

        private void ChkShowOnlineStatus_Changed(object sender, RoutedEventArgs e)
            => Host?.ChkShowOnlineStatus_Changed(sender, e);

        private void ChkShareAchievements_Changed(object sender, RoutedEventArgs e)
            => Host?.ChkShareAchievements_Changed(sender, e);

        private void ChkShareLevelUps_Changed(object sender, RoutedEventArgs e)
            => Host?.ChkShareLevelUps_Changed(sender, e);

        private void ChkAllowDiscordDm_Changed(object sender, RoutedEventArgs e)
            => Host?.ChkAllowDiscordDm_Changed(sender, e);

        private void ChkShareProfilePicture_Changed(object sender, RoutedEventArgs e)
            => Host?.ChkShareProfilePicture_Changed(sender, e);

        private void ChkGoonShareAvatar_Changed(object sender, RoutedEventArgs e)
            => Host?.ChkGoonShareAvatar_Changed(sender, e);

        private void ChkGoonShareDiscordDm_Changed(object sender, RoutedEventArgs e)
            => Host?.ChkGoonShareDiscordDm_Changed(sender, e);

        private void ChkGoonRichPresence_Changed(object sender, RoutedEventArgs e)
            => Host?.ChkGoonRichPresence_Changed(sender, e);
    }
}
