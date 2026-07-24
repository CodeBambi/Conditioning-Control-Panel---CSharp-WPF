using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ConditioningControlPanel.Views.Tabs
{
    public partial class LeaderboardTabView : UserControl
    {
        public LeaderboardTabView()
        {
            InitializeComponent();
        }

        private void BtnLeaderboardDiscord_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnLeaderboardDiscord_Click(sender, e);
        }

        private void BtnLeaderboardMode_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnLeaderboardMode_Click(sender, e);
        }

        private void BtnRefreshLeaderboard_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnRefreshLeaderboard_Click(sender, e);
        }

        private void BtnJumpToMe_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnJumpToMe_Click(sender, e);
        }

        private void BtnViewSeasonRecap_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnViewSeasonRecap_Click(sender, e);
        }

        private void LeaderboardColumnHeader_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.LeaderboardColumnHeader_Click(sender, e);
        }

        private void LstLeaderboard_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.LstLeaderboard_MouseDoubleClick(sender, e);
        }
    }
}
