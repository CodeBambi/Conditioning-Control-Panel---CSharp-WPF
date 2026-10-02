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
            SizeChanged += (_, e) => SetNarrow(IsNarrowWidth(e.NewSize.Width));
            SetNarrow(false);
        }

        /// <summary>Below this content width the three columns become a three-tab switcher.</summary>
        internal const double NarrowWidth = 900;

        internal static bool IsNarrowWidth(double width) => width > 0 && width < NarrowWidth;

        /// <summary>Row ids already drawn, so only a table that is new to this page pops.</summary>
        internal System.Collections.Generic.HashSet<string> SeenRows { get; } = new();

        public bool IsNarrow { get; private set; }
        public int SelectedColumn { get; private set; }

        /// <summary>Wide: three columns side by side. Narrow: the tab strip shows and only the
        /// selected column is drawn, full width.</summary>
        internal void SetNarrow(bool narrow)
        {
            IsNarrow = narrow;
            LobbyTabStrip.Visibility = narrow ? Visibility.Visible : Visibility.Collapsed;
            var cols = new FrameworkElement[] { LobbyOpenColumn, LobbyPlayingSection, LobbyFriendsSection };
            for (int i = 0; i < cols.Length; i++)
            {
                bool shown = !narrow || i == SelectedColumn;
                cols[i].Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
                Grid.SetColumn(cols[i], narrow ? 0 : i);
                Grid.SetColumnSpan(cols[i], narrow ? 3 : 1);
                cols[i].Margin = narrow ? new Thickness(0) : new Thickness(i == 0 ? 0 : i == 1 ? 4 : 8, 0, i == 0 ? 8 : i == 1 ? 4 : 0, 0);
            }
            var tabs = new[] { TabOpen, TabPlaying, TabFriends };
            for (int i = 0; i < tabs.Length; i++) tabs[i].Opacity = i == SelectedColumn ? 1.0 : 0.6;
        }

        internal void SelectColumn(int index)
        {
            SelectedColumn = Math.Clamp(index, 0, 2);
            SetNarrow(IsNarrow);
        }

        private void LobbyTab_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && int.TryParse(fe.Tag as string, out var i)) SelectColumn(i);
        }

        private void LobbyCard_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
            => Services.MotionFx.HoverLift(sender as FrameworkElement, true);

        private void LobbyCard_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
            => Services.MotionFx.HoverLift(sender as FrameworkElement, false);

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
