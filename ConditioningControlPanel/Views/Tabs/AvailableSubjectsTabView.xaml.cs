using System;
using System.Windows;
using System.Windows.Controls;

namespace ConditioningControlPanel.Views.Tabs
{
    /// <summary>The Lobby page (the old Available Subjects tab). Forwards to MainWindow.Lobby.</summary>
    public partial class AvailableSubjectsTabView : UserControl
    {
        public AvailableSubjectsTabView()
        {
            InitializeComponent();
            // FX lifecycle (PR-4b): starts the tab's one ambient canvas and staggers the rows in.
            IsVisibleChanged += AvailableSubjectsTabView_IsVisibleChanged;
        }

        private void AvailableSubjectsTabView_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.OnAvailableSubjectsTabVisibilityChanged(IsVisible);
        }

        private void BtnLobbyRow_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw && sender is FrameworkElement fe && fe.DataContext is LobbyRowView row)
                mw.OnLobbyRowClick(row, fe);
        }

        private void BtnHostChess_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw) mw.LobbyHost(Services.Lobby.LobbyGame.Chess);
        }

        private void BtnHostGoon_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw) mw.LobbyHost(Services.Lobby.LobbyGame.Goon);
        }

        private void BtnBecomeASubject_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw) mw.LobbyHost(Services.Lobby.LobbyGame.Remote);
        }
    }
}
