using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Views.Tabs
{
    public partial class LeaderboardTabView : UserControl
    {
        /// <summary>
        /// Ticks the season countdown in the header. One minute is plenty for a
        /// "2d 14h" readout, and the timer is stopped whenever the tab is hidden or
        /// unloaded so it can't keep a dead visual tree alive (CLAUDE.md: "Event
        /// handlers on closed windows").
        /// </summary>
        private DispatcherTimer? _seasonTimer;

        public LeaderboardTabView()
        {
            InitializeComponent();
            Loaded += OnLeaderboardTabLoaded;
            Unloaded += OnLeaderboardTabUnloaded;
            IsVisibleChanged += OnLeaderboardTabVisibleChanged;
        }

        /// <summary>
        /// Whether the viewer has the trophy_case skill. Gates the Streak column and
        /// the Best Session line in the row tooltip - the same gate the old
        /// GridView columns used, just expressed as a bindable property so the
        /// DataTemplates can read it. Set by MainWindow.UpdateTrophyCaseColumns().
        /// </summary>
        public static readonly DependencyProperty ShowTrophyStatsProperty =
            DependencyProperty.Register(nameof(ShowTrophyStats), typeof(bool), typeof(LeaderboardTabView),
                new PropertyMetadata(false));

        public bool ShowTrophyStats
        {
            get => (bool)GetValue(ShowTrophyStatsProperty);
            set => SetValue(ShowTrophyStatsProperty, value);
        }

        /// <summary>True while the All-Time board is showing (no season countdown).</summary>
        internal bool IsAllTimeMode { get; private set; }

        internal void SetLeaderboardMode(bool isAllTime)
        {
            IsAllTimeMode = isAllTime;
            RefreshSeasonHeader();
            ApplyModeLabels();
        }

        /// <summary>
        /// Retitle the Level column for the active board.
        ///
        /// The All-Time board ranks by cumulative XP while the Level column shows
        /// <c>HighestLevelEver</c>, so a lower-ranked player can legitimately show a HIGHER
        /// level (rank 4 at 300, rank 7 at 309) and under a "Level" header that reads as a
        /// sorting bug. The pre-rebuild code hid the column outright for this reason; the
        /// number is genuinely interesting though, so it is relabelled "Peak" instead of
        /// thrown away. Row tooltips and the podium pill follow the same label through
        /// <see cref="Services.LeaderboardEntry.LevelLabel"/>.
        /// </summary>
        private void ApplyModeLabels()
        {
            try
            {
                if (HdrLevel == null) return;
                HdrLevel.Content = Loc.Get(IsAllTimeMode ? "lb_col_peak" : "label_level");
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Failed to relabel the leaderboard Level column");
            }
        }

        // ------------------------------------------------------------------
        // Season countdown
        // ------------------------------------------------------------------

        private void OnLeaderboardTabLoaded(object sender, RoutedEventArgs e)
        {
            RefreshSeasonHeader();
            ApplyModeLabels();
            if (IsVisible) StartSeasonTimer();
        }

        private void OnLeaderboardTabUnloaded(object sender, RoutedEventArgs e)
        {
            StopSeasonTimer();
        }

        private void OnLeaderboardTabVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (IsVisible)
            {
                RefreshSeasonHeader();
                StartSeasonTimer();
            }
            else
            {
                StopSeasonTimer();
            }
        }

        private void StartSeasonTimer()
        {
            try
            {
                if (Application.Current?.Dispatcher == null) return;
                if (Application.Current.Dispatcher.HasShutdownStarted) return;
                if (_seasonTimer != null) { _seasonTimer.Start(); return; }

                _seasonTimer = new DispatcherTimer(DispatcherPriority.Background, Application.Current.Dispatcher)
                {
                    Interval = TimeSpan.FromMinutes(1)
                };
                _seasonTimer.Tick += (_, _) => RefreshSeasonHeader();
                _seasonTimer.Start();
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Leaderboard season countdown failed to start");
            }
        }

        private void StopSeasonTimer()
        {
            try
            {
                if (_seasonTimer == null) return;
                _seasonTimer.Stop();
                _seasonTimer = null;
            }
            catch { }
        }

        /// <summary>
        /// Repaint the season name + countdown. Derived entirely locally: the board's
        /// season key is <c>DateTime.UtcNow.ToString("yyyy-MM")</c>, so the season ends
        /// at the first instant of the next UTC month. No server call.
        /// </summary>
        internal void RefreshSeasonHeader()
        {
            try
            {
                if (TxtLeaderboardSeason == null || TxtLeaderboardSubtitle == null) return;

                if (IsAllTimeMode)
                {
                    TxtLeaderboardSeason.Text = Loc.Get("lb_all_time_title");
                    TxtLeaderboardSubtitle.Text = Loc.Get("lb_all_time_sub");
                    return;
                }

                var seasonTitle = App.QuestDefinitions?.SeasonTitle;
                TxtLeaderboardSeason.Text = string.IsNullOrWhiteSpace(seasonTitle)
                    ? Loc.Get("section_seasons")
                    : seasonTitle;

                var now = DateTime.UtcNow;
                var seasonEnd = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(1);
                var left = seasonEnd - now;

                if (left <= TimeSpan.Zero)
                {
                    TxtLeaderboardSubtitle.Text = Loc.Get("lb_season_ended");
                    return;
                }

                string span;
                if (left.TotalDays >= 1)
                    span = Loc.GetF("lb_time_dh", (int)left.TotalDays, left.Hours);
                else if (left.TotalHours >= 1)
                    span = Loc.GetF("lb_time_hm", (int)left.TotalHours, left.Minutes);
                else
                    span = Loc.GetF("lb_time_m", Math.Max(1, (int)left.TotalMinutes));

                TxtLeaderboardSubtitle.Text = Loc.GetF("lb_season_ends_in", span);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Failed to refresh leaderboard season header");
            }
        }

        // ------------------------------------------------------------------
        // Forwarders to MainWindow (the tab owns no logic of its own)
        // ------------------------------------------------------------------

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

        /// <summary>Replaces GridViewColumnHeader.Click - the legend headers now sort.</summary>
        private void LeaderboardSortHeader_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.LeaderboardSortHeader_Click(sender, e);
        }

        private void LeaderboardSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.LeaderboardSearch_TextChanged(sender, e);
        }

        private void LeaderboardFilter_Checked(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.LeaderboardFilter_Checked(sender, e);
        }

        private void LstLeaderboard_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.LstLeaderboard_MouseDoubleClick(sender, e);
        }

        // ------------------------------------------------------------------
        // Wheel speed
        // ------------------------------------------------------------------

        /// <summary>
        /// Restore the pre-Pixel-scrolling wheel feel.
        ///
        /// The roster runs on VirtualizingPanel.ScrollUnit=Pixel so "Jump to me" can centre a
        /// row instead of leaving it half-cut against the sticky you-bar (see the note on the
        /// ListView). The side effect is that WPF's built-in wheel handling switches from
        /// "3 ITEMS per notch" to "3 text lines = 48dip per notch", which is barely one row -
        /// a 200-row board would take 200 notches to walk. So the wheel is driven here instead,
        /// at three measured rows per notch, which is what it did before.
        ///
        /// Held modifiers are left alone: Ctrl+wheel and Shift+wheel are not ours to eat.
        /// </summary>
        private void LstLeaderboard_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            try
            {
                if (e.Handled || e.Delta == 0) return;
                if (Keyboard.Modifiers != ModifierKeys.None) return;

                var scroller = RosterScrollViewer();
                if (scroller == null) return;

                var step = e.Delta / 120.0 * 3.0 * RosterRowPitch();
                scroller.ScrollToVerticalOffset(scroller.VerticalOffset - step);
                e.Handled = true;
            }
            catch (Exception ex)
            {
                // Never let a wheel notch take the tab down; the worst case is the WPF default.
                App.Logger?.Debug(ex, "Leaderboard wheel scroll failed");
            }
        }

        private ScrollViewer? _rosterScroller;

        private ScrollViewer? RosterScrollViewer()
        {
            if (_rosterScroller != null) return _rosterScroller;
            if (LstLeaderboard == null || VisualTreeHelper.GetChildrenCount(LstLeaderboard) == 0) return null;
            _rosterScroller = FindDescendant<ScrollViewer>(LstLeaderboard);
            return _rosterScroller;
        }

        /// <summary>
        /// Height of a real roster row. Measured rather than hard-coded because the row pitch is
        /// set by padding/margins in LbRowTemplate that are explicitly called out as tunable -
        /// and the LARGEST realised container is taken, because the list interleaves short
        /// non-selectable tier-band rows with real ones.
        /// </summary>
        private double RosterRowPitch()
        {
            try
            {
                var generator = LstLeaderboard?.ItemContainerGenerator;
                if (generator == null) return 52;

                double pitch = 0;
                var scan = Math.Min(LstLeaderboard!.Items.Count, 12);
                for (int i = 0; i < scan; i++)
                {
                    if (generator.ContainerFromIndex(i) is FrameworkElement fe && fe.ActualHeight > pitch)
                        pitch = fe.ActualHeight;
                }
                return pitch > 12 ? pitch : 52;
            }
            catch
            {
                return 52;
            }
        }

        private static T? FindDescendant<T>(DependencyObject? root) where T : DependencyObject
        {
            if (root == null) return null;
            var count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is T hit) return hit;
                var deep = FindDescendant<T>(child);
                if (deep != null) return deep;
            }
            return null;
        }
    }

    /// <summary>
    /// Non-selectable separator injected between rank groups in the roster.
    /// Carries the same trigger surface as a real row (<see cref="IsSeason0Og"/>,
    /// <see cref="IsCurrentUser"/>) purely so the shared ItemContainerStyle's
    /// DataTriggers resolve against it instead of logging binding errors.
    /// </summary>
    public sealed class LeaderboardTierBand
    {
        public bool IsBand => true;
        public bool IsSeason0Og => false;
        public bool IsCurrentUser => false;

        /// <summary>e.g. "HOLLOWED  -  ranks 4-10".</summary>
        public string HeaderText { get; set; } = "";

        public string Subtitle { get; set; } = "";
    }

    /// <summary>
    /// Picks the row template or the tier-band template out of the roster's
    /// heterogeneous ItemsSource. A template selector keeps plain
    /// VirtualizingStackPanel virtualization intact, which CollectionViewSource
    /// grouping only manages with IsVirtualizingWhenGrouping and a lot of caveats.
    /// </summary>
    public class LeaderboardItemTemplateSelector : DataTemplateSelector
    {
        public DataTemplate? RowTemplate { get; set; }
        public DataTemplate? BandTemplate { get; set; }

        public override DataTemplate? SelectTemplate(object item, DependencyObject container)
            => item is LeaderboardTierBand ? BandTemplate : RowTemplate;
    }
}
