// PORTED from ConditioningControlPanel/MainWindow/MainWindow.AchievementsTab.cs. The card grid, meter,
// filters, reward band, unlock refresh and tile FX live in CCP.Avalonia/Views/Tabs/AchievementsTabView
// (that view owns the grid this file built into on WPF). What stays here is what WPF keeps on the
// window: the Season Recap re-view, reading Core SeasonRecapStore.
using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        /// <summary>WPF AchievementsTab.cs:115 (the old rail entry's handler): surface the Season
        /// Recap re-view button only when a persisted snapshot exists. The entry left the rail, so this
        /// rides every way into the Leaderboard (OnTabShown), where WPF's strip pill lost it.</summary>
        private void RefreshSeasonRecapReview()
        {
            try
            {
                if (Named<Tabs.LeaderboardTabView>("LeaderboardTab")?.FindControl<Button>("BtnViewSeasonRecap") is { } btn)
                    btn.IsVisible = SeasonRecapStore.HasAnySnapshot();
            }
            catch (Exception ex) { Log.Warning(ex, "SeasonRecap: failed to update re-view button visibility"); }
        }

        /// <summary>WPF AchievementsTab.cs:132: re-view the most recent season's recap card.</summary>
        internal void BtnViewSeasonRecap_Click(object? sender, RoutedEventArgs e)
        {
            try { CoreBark.NotifyUiAction("season_recap"); } catch { }
            try
            {
                var snapshot = SeasonRecapStore.LoadLatest();
                if (snapshot == null)
                {
                    App.Notifications.Show(Loc.Get("recap_toast_none"));
                    return;
                }
                LastSeasonRecap = new SeasonRecapWindow(new SeasonRecapCardViewModel(snapshot));
                _ = LastSeasonRecap.ShowDialogSafe(this);
            }
            catch (Exception ex) { Log.Warning(ex, "SeasonRecap: failed to open re-view window"); }
        }

        /// <summary>Test hook: the re-view window last opened.</summary>
        internal SeasonRecapWindow? LastSeasonRecap { get; private set; }
    }
}
