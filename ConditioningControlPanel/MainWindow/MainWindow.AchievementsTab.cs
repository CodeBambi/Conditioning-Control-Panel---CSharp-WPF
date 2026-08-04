using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Rectangle = System.Windows.Shapes.Rectangle;
using NAudio.Wave;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Helpers;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel
{
    // Achievements tab: grid, counts, and season recap.
    public partial class MainWindow
    {
        #region Achievements Tab

        private void BtnAchievements_Click(object sender, RoutedEventArgs e)
        {
            ShowTab("achievements");
        }

        private void BtnCompanion_Click(object sender, RoutedEventArgs e)
        {
            ShowTab("companion");
        }

        private void BtnLeaderboard_Click(object sender, RoutedEventArgs e)
        {
            ShowTab("leaderboard");
            // Surface the Season Recap re-view button only when a persisted snapshot exists.
            try
            {
                if (LeaderboardTab.BtnViewSeasonRecap != null)
                    LeaderboardTab.BtnViewSeasonRecap.Visibility = Services.SeasonRecapService.HasAnySnapshot()
                        ? Visibility.Visible : Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "SeasonRecap: failed to update re-view button visibility");
            }
        }

        /// <summary>Re-view the most recent season's recap card from its persisted snapshot.</summary>
        internal void BtnViewSeasonRecap_Click(object sender, RoutedEventArgs e)
        {
            try { App.Bark?.NotifyUiAction("season_recap"); } catch { }
            try
            {
                var snapshot = Services.SeasonRecapService.LoadLatest();
                if (snapshot == null)
                {
                    App.Notifications?.Show(Loc.Get("recap_toast_none"), Services.NotificationType.Info);
                    return;
                }
                var vm = new ViewModels.SeasonRecapViewModel(snapshot);
                var win = new Controls.SeasonRecapWindow(vm) { Owner = this };
                win.ShowDialog();
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "SeasonRecap: failed to open re-view window");
            }
        }






        private void UpdateAchievementCount()
        {
            if (App.Achievements == null) return;

            // Free and patron counts are kept strictly separate — never summed.
            if (AchievementsTab.TxtAchievementCount != null)
            {
                var unlocked = App.Achievements.GetUnlockedCount(exclusive: false);
                var total = App.Achievements.GetTotalCount(exclusive: false);
                AchievementsTab.TxtAchievementCount.Text = Loc.GetF("label_0_1_achievements_unlocked", unlocked, total);
            }

            if (AchievementsTab.TxtPatronAchievementCount != null)
            {
                var pUnlocked = App.Achievements.GetUnlockedCount(exclusive: true);
                var pTotal = App.Achievements.GetTotalCount(exclusive: true);
                AchievementsTab.TxtPatronAchievementCount.Text = Loc.GetF("label_0_1_achievements_unlocked", pUnlocked, pTotal);
            }

            // Free users see the patron collection as a labeled, locked section.
            if (AchievementsTab.PatronAchievementsOverlay != null)
            {
                AchievementsTab.PatronAchievementsOverlay.Visibility = App.Patreon?.HasPremiumAccess == true
                    ? Visibility.Collapsed
                    : Visibility.Visible;
            }
        }




        private void PopulateAchievementGrid()
        {
            if (AchievementsTab.AchievementGrid == null) return;
            
            AchievementsTab.AchievementGrid.Children.Clear();
            AchievementsTab.PatronAchievementGrid?.Children.Clear();
            _achievementImages.Clear();
            // The old tiles are gone; drop the FX bookkeeping with them.
            _achievementTilesUnlocked.Clear();
            _achievementTilesWired.Clear();

            var tileStyle = FindResource("AchievementTile") as Style;

            // Add all achievements (patron-exclusive ones routed to the separate grid)
            foreach (var kvp in Models.Achievement.All)
            {
                var achievement = kvp.Value;
                // Skip parked achievements (no reachable unlock path in this build).
                if (achievement.IsHidden) continue;
                var isUnlocked = App.Achievements?.Progress.IsUnlocked(achievement.Id) ?? false;
                
                var border = new Border { Style = tileStyle };
                var achName = App.Mods?.MakeModAware(achievement.Name) ?? achievement.Name;
                var achFlavor = App.Mods?.MakeModAware(achievement.FlavorText) ?? achievement.FlavorText;
                var achReq = App.Mods?.MakeModAware(achievement.Requirement) ?? achievement.Requirement;
                border.ToolTip = isUnlocked
                    ? $"{achName}\n\n\"{achFlavor}\""
                    : $"???\n\nRequirement: {achReq}";

                var image = new Image
                {
                    Stretch = Stretch.Uniform,
                    Source = LoadAchievementImage(achievement.ImageName)
                };

                // Apply blur if locked
                if (!isUnlocked)
                {
                    image.Effect = new BlurEffect { Radius = 15 };
                }

                border.Child = image;
                // Hover holo-foil tilt, unlocked tiles only (see
                // MainWindow.TabFxPresetsQuestsAchievements.cs).
                PrepareAchievementTileFx(border, isUnlocked);

                if (achievement.IsExclusive)
                    AchievementsTab.PatronAchievementGrid?.Children.Add(border);
                else
                    AchievementsTab.AchievementGrid.Children.Add(border);

                // Store reference for later updates
                _achievementImages[achievement.Id] = image;
            }
            
            // Note: All placeholders have been replaced with real achievements
            
            UpdateAchievementCount();
            App.Logger?.Information("Achievement grid populated with {Count} achievements", _achievementImages.Count);
        }
        
        private BitmapImage? LoadAchievementImage(string imageName)
        {
            try
            {
                var image = Services.ModResourceResolver.ResolveImage($"achievements/{imageName}");
                return image as BitmapImage ?? new BitmapImage(new Uri($"pack://application:,,,/Resources/achievements/{imageName}", UriKind.Absolute));
            }
            catch (Exception ex)
            {
                App.Logger?.Warning("Failed to load achievement image {Name}: {Error}", imageName, ex.Message);
                return null;
            }
        }
        
        private void RefreshAchievementTile(string achievementId)
        {
            if (!_achievementImages.TryGetValue(achievementId, out var image)) return;

            var isUnlocked = App.Achievements?.Progress.IsUnlocked(achievementId) ?? false;

            // Update blur
            image.Effect = isUnlocked ? null : new BlurEffect { Radius = 15 };

            // Update tooltip
            if (Models.Achievement.All.TryGetValue(achievementId, out var achievement))
            {
                var parent = image.Parent as Border;
                if (parent != null)
                {
                    // A tile that just unlocked starts offering the hover tilt (and one that was
                    // somehow re-locked stops).
                    SetAchievementTileUnlocked(parent, isUnlocked);
                    var achName2 = App.Mods?.MakeModAware(achievement.Name) ?? achievement.Name;
                    var achFlavor2 = App.Mods?.MakeModAware(achievement.FlavorText) ?? achievement.FlavorText;
                    var achReq2 = App.Mods?.MakeModAware(achievement.Requirement) ?? achievement.Requirement;
                    parent.ToolTip = isUnlocked
                        ? $"{achName2}\n\n\"{achFlavor2}\""
                        : $"???\n\nRequirement: {achReq2}";
                }
            }

            UpdateAchievementCount();
        }

        private void RefreshAllAchievementTiles()
        {
            // Refresh all achievement tiles to reflect current unlock state
            foreach (var achievementId in _achievementImages.Keys.ToList())
            {
                RefreshAchievementTile(achievementId);
            }
            App.Logger?.Debug("All achievement tiles refreshed");
        }

        private void OnAchievementUnlockedInMainWindow(object? sender, Models.Achievement achievement)
        {
            Dispatcher.Invoke(() =>
            {
                RefreshAchievementTile(achievement.Id);
                // Event FX (PR-5): tile reveal + burst, or a burst on the Achievements nav button
                // when the grid is not the tab on screen. See MainWindow.EventFx.cs.
                CelebrateAchievementUnlock(achievement.Id);
                App.Logger?.Information("Achievement tile refreshed: {Name}", achievement.Name);
            });
        }
        #endregion
    }
}
