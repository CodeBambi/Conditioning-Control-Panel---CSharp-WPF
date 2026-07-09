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
    // Quests tab: daily/weekly quests and streak calendar UI.
    public partial class MainWindow
    {
        #region Quests Tab

        internal void BtnRerollDaily_Click(object sender, RoutedEventArgs e)
        {
            try { App.Bark?.NotifyUiAction("reroll_daily"); } catch { }
            if (App.Quests?.RerollDailyQuest() == true)
            {
                RefreshQuestUI();
            }
            else
            {
                var hasPatreon = App.Patreon?.HasPremiumAccess == true;
                var msg = hasPatreon
                    ? "You've used all 3 daily rerolls! Rerolls reset at midnight."
                    : "You've used your daily reroll! Patreon supporters get 2 extra rerolls.";
                MessageBox.Show(msg, "Reroll Limit", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        internal void BtnRerollWeekly_Click(object sender, RoutedEventArgs e)
        {
            try { App.Bark?.NotifyUiAction("reroll_weekly"); } catch { }
            if (App.Quests?.RerollWeeklyQuest() == true)
            {
                RefreshQuestUI();
            }
            else
            {
                var hasPatreon = App.Patreon?.HasPremiumAccess == true;
                var msg = hasPatreon
                    ? "You've used all 3 weekly rerolls! Rerolls reset on Sunday."
                    : "You've used your weekly reroll! Patreon supporters get 2 extra rerolls.";
                MessageBox.Show(msg, "Reroll Limit", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }


        private void RefreshQuestUI()
        {
            var questService = App.Quests;
            if (questService == null) return;

            // Proactively recalculate streak from calendar so stale values are caught immediately
            questService.RecalculateStreak();

            // Update season title from server or defaults
            var seasonTitle = App.QuestDefinitions?.SeasonTitle;
            if (!string.IsNullOrEmpty(seasonTitle))
            {
                QuestsTab.TxtSeasonTitle.Text = seasonTitle;
            }

            // Update daily quest counter badge
            int dailyCompleted = questService.GetDailyQuestsCompletedToday();
            QuestsTab.TxtDailyQuestCounter.Text = $"{dailyCompleted}/{QuestService.MaxDailyQuestsPerDay}";
            bool allDailyDone = questService.AreAllDailyQuestsCompleted();

            // Update daily progress segments
            var goldBrush = _dailySegmentGold ??= new SolidColorBrush(Color.FromRgb(0xFF, 0xD7, 0x00));
            var greyBrush = _dailySegmentGrey ??= new SolidColorBrush(Color.FromRgb(0x3D, 0x3D, 0x60));
            QuestsTab.DailySegment1.Background = dailyCompleted >= 1 ? goldBrush : greyBrush;
            QuestsTab.DailySegment2.Background = dailyCompleted >= 2 ? goldBrush : greyBrush;
            QuestsTab.DailySegment3.Background = dailyCompleted >= 3 ? goldBrush : greyBrush;

            // Refresh daily quest display
            var dailyDef = questService.GetCurrentDailyDefinition();
            var dailyProgress = questService.Progress.DailyQuest;
            if (allDailyDone)
            {
                // All 3 daily quests completed - show the "all done" message
                QuestsTab.DailyQuestCard.Visibility = Visibility.Collapsed;
                QuestsTab.DailyAllCompletedMessage.Visibility = Visibility.Visible;
                QuestsTab.BtnRerollDaily.Visibility = Visibility.Collapsed;
            }
            else if (dailyDef != null && dailyProgress != null)
            {
                QuestsTab.DailyQuestCard.Visibility = Visibility.Visible;
                QuestsTab.DailyAllCompletedMessage.Visibility = Visibility.Collapsed;
                QuestsTab.BtnRerollDaily.Visibility = Visibility.Visible;

                QuestsTab.TxtDailyQuestIcon.Text = dailyDef.Icon;
                QuestsTab.TxtDailyQuestName.Text = App.Mods?.MakeModAware(dailyDef.Name) ?? dailyDef.Name;
                QuestsTab.TxtDailyQuestDesc.Text = App.Mods?.MakeModAware(dailyDef.Description) ?? dailyDef.Description;
                QuestsTab.TxtDailyProgress.Text = $"{dailyProgress.CurrentProgress} / {dailyDef.TargetValue}";
                // Show scaled XP based on level (+4% per level), reroll bonus, and streak bonus
                var playerLevel = App.Settings?.Current?.PlayerLevel ?? 1;
                var rerollMult = App.SkillTree?.GetRerollBonusMultiplier() ?? 1.0;
                var questStreak = App.Settings?.Current?.DailyQuestStreak ?? 0;
                var streakMult = 1.0 + (questStreak * 0.03);
                var scaledDailyXP = (int)Math.Round(dailyDef.XPReward * (1 + playerLevel * 0.04) * rerollMult * streakMult);
                QuestsTab.TxtDailyXP.Text = $"🎁 {scaledDailyXP} XP";
                if (questStreak > 0)
                {
                    QuestsTab.TxtDailyStreakBonus.Text = $"(+{questStreak * 3}%\U0001f525)";
                    QuestsTab.TxtDailyStreakBonus.Visibility = Visibility.Visible;
                }
                else
                {
                    QuestsTab.TxtDailyStreakBonus.Visibility = Visibility.Collapsed;
                }
                if (rerollMult > 1.0)
                {
                    QuestsTab.TxtDailyRerollBonus.Text = $"(+{(int)((rerollMult - 1.0) * 100)}%\U0001f503)";
                    QuestsTab.TxtDailyRerollBonus.Visibility = Visibility.Visible;
                }
                else
                {
                    QuestsTab.TxtDailyRerollBonus.Visibility = Visibility.Collapsed;
                }

                // Load quest image (supports remote cached images)
                try
                {
                    var dailyImagePath = GetModeAwareQuestImagePath(dailyDef);
                    var dailyImage = LoadQuestImage(dailyImagePath);
                    if (dailyImage != null)
                    {
                        QuestsTab.ImgDailyQuest.Source = dailyImage;
                    }
                }
                catch { /* Image load failed, leave blank */ }

                // Update progress bar
                double progressPercent = dailyDef.TargetValue > 0
                    ? Math.Min(1.0, (double)dailyProgress.CurrentProgress / dailyDef.TargetValue)
                    : 0;
                QuestsTab.DailyProgressFill.Width = QuestsTab.DailyProgressTrack.ActualWidth > 0
                    ? QuestsTab.DailyProgressTrack.ActualWidth * progressPercent
                    : 0;

                // Show completed overlay if done (briefly visible before next quest loads)
                if (dailyProgress.IsCompleted)
                {
                    QuestsTab.DailyCompletedOverlay.Visibility = Visibility.Visible;
                    QuestsTab.BtnRerollDaily.IsEnabled = false;
                    QuestsTab.BtnRerollDaily.Content = Loc.Get("btn_completed");
                }
                else
                {
                    QuestsTab.DailyCompletedOverlay.Visibility = Visibility.Collapsed;
                    int remainingRerolls = questService.GetRemainingDailyRerolls();
                    QuestsTab.BtnRerollDaily.IsEnabled = remainingRerolls > 0;
                    QuestsTab.BtnRerollDaily.Content = remainingRerolls > 0 ? $"🔄 Reroll ({remainingRerolls} left)" : "🔄 No rerolls left";
                }
            }

            // Refresh weekly quest display
            var weeklyDef = questService.GetCurrentWeeklyDefinition();
            var weeklyProgress = questService.Progress.WeeklyQuest;
            if (weeklyDef != null && weeklyProgress != null)
            {
                QuestsTab.TxtWeeklyQuestIcon.Text = weeklyDef.Icon;
                QuestsTab.TxtWeeklyQuestName.Text = App.Mods?.MakeModAware(weeklyDef.Name) ?? weeklyDef.Name;
                QuestsTab.TxtWeeklyQuestDesc.Text = App.Mods?.MakeModAware(weeklyDef.Description) ?? weeklyDef.Description;
                QuestsTab.TxtWeeklyProgress.Text = $"{weeklyProgress.CurrentProgress} / {weeklyDef.TargetValue}";
                // Show scaled XP based on level (+4% per level), reroll bonus, and streak bonus
                var wPlayerLevel = App.Settings?.Current?.PlayerLevel ?? 1;
                var wRerollMult = App.SkillTree?.GetRerollBonusMultiplier() ?? 1.0;
                var wQuestStreak = App.Settings?.Current?.DailyQuestStreak ?? 0;
                var wStreakMult = 1.0 + (wQuestStreak * 0.03);
                var scaledWeeklyXP = (int)Math.Round(weeklyDef.XPReward * (1 + wPlayerLevel * 0.04) * wRerollMult * wStreakMult);
                QuestsTab.TxtWeeklyXP.Text = $"🎁 {scaledWeeklyXP} XP";
                if (wQuestStreak > 0)
                {
                    QuestsTab.TxtWeeklyStreakBonus.Text = $"(+{wQuestStreak * 3}%\U0001f525)";
                    QuestsTab.TxtWeeklyStreakBonus.Visibility = Visibility.Visible;
                }
                else
                {
                    QuestsTab.TxtWeeklyStreakBonus.Visibility = Visibility.Collapsed;
                }
                if (wRerollMult > 1.0)
                {
                    QuestsTab.TxtWeeklyRerollBonus.Text = $"(+{(int)((wRerollMult - 1.0) * 100)}%\U0001f503)";
                    QuestsTab.TxtWeeklyRerollBonus.Visibility = Visibility.Visible;
                }
                else
                {
                    QuestsTab.TxtWeeklyRerollBonus.Visibility = Visibility.Collapsed;
                }

                // Load quest image (supports remote cached images)
                try
                {
                    var weeklyImagePath = GetModeAwareQuestImagePath(weeklyDef);
                    var weeklyImage = LoadQuestImage(weeklyImagePath);
                    if (weeklyImage != null)
                    {
                        QuestsTab.ImgWeeklyQuest.Source = weeklyImage;
                    }
                }
                catch { /* Image load failed, leave blank */ }

                // Update progress bar
                double progressPercent = weeklyDef.TargetValue > 0
                    ? Math.Min(1.0, (double)weeklyProgress.CurrentProgress / weeklyDef.TargetValue)
                    : 0;
                QuestsTab.WeeklyProgressFill.Width = QuestsTab.WeeklyProgressTrack.ActualWidth > 0
                    ? QuestsTab.WeeklyProgressTrack.ActualWidth * progressPercent
                    : 0;

                // Show completed overlay if done
                if (weeklyProgress.IsCompleted)
                {
                    QuestsTab.WeeklyCompletedOverlay.Visibility = Visibility.Visible;
                    QuestsTab.BtnRerollWeekly.IsEnabled = false;
                    QuestsTab.BtnRerollWeekly.Content = Loc.Get("btn_completed");
                }
                else
                {
                    QuestsTab.WeeklyCompletedOverlay.Visibility = Visibility.Collapsed;
                    int remainingRerolls = questService.GetRemainingWeeklyRerolls();
                    QuestsTab.BtnRerollWeekly.IsEnabled = remainingRerolls > 0;
                    QuestsTab.BtnRerollWeekly.Content = remainingRerolls > 0 ? $"🔄 Reroll ({remainingRerolls} left)" : "🔄 No rerolls left";
                }
            }
            else if (weeklyProgress != null && weeklyProgress.IsCompleted)
            {
                // The stored weekly is completed but its definition no longer resolves — the server
                // rotated the quest pool (ids change on a "20 free + 20 patron" refresh) since it was
                // done. Without this branch the card kept its XAML defaults ("Loading…" + blank image)
                // forever, looking broken, and reroll was refused because the quest is completed.
                // Render a graceful "done for the week" card instead; a fresh weekly generates on the
                // Monday rollover (IsWeeklyExpired). We do NOT regenerate a completed quest — that
                // would hand out a second weekly reward after every server refresh. (#496)
                QuestsTab.TxtWeeklyQuestIcon.Text = "✅";
                QuestsTab.TxtWeeklyQuestName.Text = "Weekly quest complete!";
                QuestsTab.TxtWeeklyQuestDesc.Text = "Nice work! Your next weekly quest arrives Monday.";
                QuestsTab.TxtWeeklyProgress.Text = "";
                QuestsTab.TxtWeeklyXP.Text = "";
                QuestsTab.TxtWeeklyStreakBonus.Visibility = Visibility.Collapsed;
                QuestsTab.TxtWeeklyRerollBonus.Visibility = Visibility.Collapsed;
                QuestsTab.WeeklyCompletedOverlay.Visibility = Visibility.Visible;
                QuestsTab.BtnRerollWeekly.IsEnabled = false;
                QuestsTab.BtnRerollWeekly.Content = Loc.Get("btn_completed");
            }

            // Update statistics
            QuestsTab.TxtTotalDailyCompleted.Text = questService.Progress.TotalDailyQuestsCompleted.ToString();
            QuestsTab.TxtTotalWeeklyCompleted.Text = questService.Progress.TotalWeeklyQuestsCompleted.ToString();
            QuestsTab.TxtTotalQuestXP.Text = questService.Progress.TotalXPFromQuests.ToString();

            // Update header stats
            int completedToday = dailyCompleted + (weeklyProgress?.IsCompleted == true ? 1 : 0);
            QuestsTab.TxtQuestStats.Text = $"{completedToday} completed today";

            // Refresh streak calendar
            RefreshStreakCalendar();
        }

        private void RefreshStreakCalendar()
        {
            if (QuestsTab.StreakCalendarCanvas == null) return;

            QuestsTab.StreakCalendarCanvas.Children.Clear();

            var questService = App.Quests;
            var completedDates = new HashSet<DateTime>(
                questService?.Progress?.DailyQuestCompletionDates?.Select(d => d.Date)
                ?? Enumerable.Empty<DateTime>());

            var shieldedDates = new HashSet<DateTime>(
                App.Settings?.Current?.StreakShieldUsedDates?.Select(d => d.Date)
                ?? Enumerable.Empty<DateTime>());

            var today = DateTime.Today;

            // Show current month's days
            int daysInMonth = DateTime.DaysInMonth(today.Year, today.Month);
            var days = Enumerable.Range(1, daysInMonth)
                .Select(d => new DateTime(today.Year, today.Month, d)).ToList();

            // Canvas doesn't auto-stretch, so use parent's actual width minus padding
            double canvasWidth = QuestsTab.StreakCalendarCanvas.ActualWidth;
            if (canvasWidth <= 0)
            {
                var parent = QuestsTab.StreakCalendarCanvas.Parent as FrameworkElement;
                canvasWidth = parent?.ActualWidth ?? 0;
            }
            if (canvasWidth <= 0) canvasWidth = 600;

            double spacing = canvasWidth / daysInMonth;
            double centerY = 25;

            double prevCenterX = 0;
            bool prevCompleted = false;
            bool hasMissedDays = false;

            string[] dayLetters = { "S", "M", "T", "W", "T", "F", "S" };

            for (int i = 0; i < days.Count; i++)
            {
                var day = days[i];
                bool isSunday = day.DayOfWeek == DayOfWeek.Sunday;
                bool isToday = day.Date == today;
                bool isCompleted = completedDates.Contains(day.Date);
                bool isFuture = day.Date > today;
                bool isMissed = !isCompleted && !isFuture && day.Date < today;

                if (isMissed) hasMissedDays = true;

                double nodeSize = isSunday ? 26 : 20;
                double centerX = spacing * i + spacing / 2.0;

                // Draw connecting line from previous node
                if (i > 0)
                {
                    var line = new System.Windows.Shapes.Line
                    {
                        X1 = prevCenterX,
                        Y1 = centerY,
                        X2 = centerX,
                        Y2 = centerY,
                        StrokeThickness = 2,
                        Stroke = (isCompleted && prevCompleted)
                            ? new SolidColorBrush((Color)ColorConverter.ConvertFromString(App.Mods?.GetAccentColorHex() ?? "#FF69B4"))
                            : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3D3D60"))
                    };
                    Canvas.SetZIndex(line, 0);
                    QuestsTab.StreakCalendarCanvas.Children.Add(line);
                }

                // Draw node (rounded rectangle to fit text)
                var rect = new System.Windows.Shapes.Rectangle
                {
                    Width = nodeSize,
                    Height = nodeSize,
                    RadiusX = nodeSize / 2.0,
                    RadiusY = nodeSize / 2.0,
                    Fill = isCompleted
                        ? new SolidColorBrush((Color)ColorConverter.ConvertFromString(App.Mods?.GetAccentColorHex() ?? "#FF69B4"))
                        : (SolidColorBrush)Application.Current.Resources["PanelBgBrush"],
                    Stroke = isToday
                        ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFD700"))
                        : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3D3D60")),
                    StrokeThickness = isToday ? 2 : 1
                };

                Canvas.SetLeft(rect, centerX - nodeSize / 2.0);
                Canvas.SetTop(rect, centerY - nodeSize / 2.0);
                Canvas.SetZIndex(rect, 1);
                QuestsTab.StreakCalendarCanvas.Children.Add(rect);

                // Day letter + day number label (e.g. "S1", "M2", "T3")
                string dayLetter = dayLetters[(int)day.DayOfWeek];
                var label = new TextBlock
                {
                    Text = $"{dayLetter}{day.Day}",
                    Foreground = isCompleted
                        ? Brushes.White
                        : isFuture
                            ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#444444"))
                            : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#888888")),
                    FontSize = 7,
                    FontWeight = FontWeights.Bold,
                    TextAlignment = TextAlignment.Center
                };
                label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                Canvas.SetLeft(label, centerX - label.DesiredSize.Width / 2.0);
                Canvas.SetTop(label, centerY - label.DesiredSize.Height / 2.0);
                Canvas.SetZIndex(label, 2);
                QuestsTab.StreakCalendarCanvas.Children.Add(label);

                // Shield overlay on days protected by streak shield
                if (shieldedDates.Contains(day.Date))
                {
                    var shieldLabel = new TextBlock
                    {
                        Text = "🛡️",
                        FontFamily = new FontFamily("Segoe UI Emoji"),
                        FontSize = 10,
                        TextAlignment = TextAlignment.Center
                    };
                    shieldLabel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    Canvas.SetLeft(shieldLabel, centerX - shieldLabel.DesiredSize.Width / 2.0);
                    Canvas.SetTop(shieldLabel, centerY - nodeSize / 2.0 - shieldLabel.DesiredSize.Height + 2);
                    Canvas.SetZIndex(shieldLabel, 4);
                    QuestsTab.StreakCalendarCanvas.Children.Add(shieldLabel);
                }

                // In fix mode, overlay a pulsing pink highlight on missed days
                if (_isStreakFixMode && isMissed)
                {
                    double highlightSize = nodeSize + 4;
                    var highlight = new System.Windows.Shapes.Rectangle
                    {
                        Width = highlightSize,
                        Height = highlightSize,
                        RadiusX = highlightSize / 2.0,
                        RadiusY = highlightSize / 2.0,
                        Fill = Brushes.Transparent,
                        Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString(App.Mods?.GetAccentColorHex() ?? "#FF69B4")),
                        StrokeThickness = 2,
                        Cursor = System.Windows.Input.Cursors.Hand,
                        Tag = day.Date
                    };

                    // Pulsing opacity animation
                    var pulseAnim = new System.Windows.Media.Animation.DoubleAnimation
                    {
                        From = 1.0,
                        To = 0.3,
                        Duration = TimeSpan.FromMilliseconds(600),
                        AutoReverse = true,
                        RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever
                    };
                    highlight.BeginAnimation(OpacityProperty, pulseAnim);

                    highlight.MouseLeftButtonDown += StreakFixDay_Click;

                    Canvas.SetLeft(highlight, centerX - highlightSize / 2.0);
                    Canvas.SetTop(highlight, centerY - highlightSize / 2.0);
                    Canvas.SetZIndex(highlight, 3);
                    QuestsTab.StreakCalendarCanvas.Children.Add(highlight);
                }

                prevCenterX = centerX;
                prevCompleted = isCompleted;
            }

            // Update streak text
            var streak = App.Settings?.Current?.DailyQuestStreak ?? 0;
            QuestsTab.TxtQuestStreakCount.Text = streak > 0 ? $"\U0001f525 {streak} day streak (+{streak * 3}% XP)" : "";

            // Show/hide/enable Fix Day button based on skill, XP, season usage, and missed days
            var settings = App.Settings?.Current;
            bool hasSkill = App.SkillTree?.HasSkill("oopsie_insurance") == true;
            bool alreadyUsed = settings?.SeasonalStreakRecoveryUsed == true;
            bool hasEnoughXP = (settings?.PlayerXP ?? 0) >= 500;

            if (hasSkill)
            {
                QuestsTab.BtnFixStreak.Visibility = Visibility.Visible;
                QuestsTab.BtnFixStreak.IsEnabled = !_isStreakFixMode || _isStreakFixMode; // Always enabled when skill owned

                if (_isStreakFixMode)
                {
                    QuestsTab.BtnFixStreak.Content = Loc.Get("btn_cancel_2");
                }
                else
                {
                    QuestsTab.BtnFixStreak.Content = Loc.Get("btn_fix_day");
                }

                if (alreadyUsed)
                    QuestsTab.BtnFixStreak.ToolTip = Loc.Get("tooltip_already_used_this_season");
                else if (!hasEnoughXP)
                    QuestsTab.BtnFixStreak.ToolTip = Loc.Get("tooltip_requires_500_xp");
                else if (!hasMissedDays)
                    QuestsTab.BtnFixStreak.ToolTip = Loc.Get("tooltip_no_missed_days_your_streak_is_perfect");
                else
                    QuestsTab.BtnFixStreak.ToolTip = Loc.Get("tooltip_use_oopsie_insurance_to_fix_a_missed_day_500");
            }
            else
            {
                QuestsTab.BtnFixStreak.Visibility = Visibility.Collapsed;
            }
        }

        internal void StreakCalendarCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            RefreshStreakCalendar();
        }

        internal void BtnFixStreak_Click(object sender, RoutedEventArgs e)
        {
            if (_isStreakFixMode)
            {
                ExitStreakFixMode();
                return;
            }

            // Validate prerequisites with user-friendly messages
            var settings = App.Settings?.Current;
            if (settings == null) return;
            if (App.SkillTree?.HasSkill("oopsie_insurance") != true) return;

            if (settings.SeasonalStreakRecoveryUsed)
            {
                QuestsTab.TxtFixStreakStatus.Text = Loc.Get("label_already_used_oopsie_insurance_this_season");
                QuestsTab.TxtFixStreakStatus.Visibility = Visibility.Visible;
                return;
            }

            // Check if there are any missed days
            var questService = App.Quests;
            var completedDates = new HashSet<DateTime>(
                questService?.Progress?.DailyQuestCompletionDates?.Select(d => d.Date)
                ?? Enumerable.Empty<DateTime>());
            var today = DateTime.Today;
            bool hasMissedDays = Enumerable.Range(1, today.Day - 1)
                .Select(d => new DateTime(today.Year, today.Month, d))
                .Any(d => !completedDates.Contains(d.Date));

            if (!hasMissedDays)
            {
                QuestsTab.TxtFixStreakStatus.Text = Loc.Get("label_no_broken_streak_you_re_doing_great_sweetie");
                QuestsTab.TxtFixStreakStatus.Visibility = Visibility.Visible;
                return;
            }

            if (settings.PlayerXP < 500)
            {
                QuestsTab.TxtFixStreakStatus.Text = Loc.Get("label_not_enough_xp_you_need_500_xp_to_fix_a_day");
                QuestsTab.TxtFixStreakStatus.Visibility = Visibility.Visible;
                return;
            }

            // Enter fix mode
            _isStreakFixMode = true;
            QuestsTab.TxtFixStreakStatus.Text = Loc.Get("label_click_a_missed_day_to_fix_it_costs_500_xp_onc");
            QuestsTab.TxtFixStreakStatus.Visibility = Visibility.Visible;
            RefreshStreakCalendar();
        }

        private void ExitStreakFixMode()
        {
            _isStreakFixMode = false;
            QuestsTab.TxtFixStreakStatus.Visibility = Visibility.Collapsed;
            QuestsTab.TxtFixStreakStatus.Text = "";
            RefreshStreakCalendar();
        }

        private async void StreakFixDay_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is not System.Windows.Shapes.Rectangle highlight) return;
            if (highlight.Tag is not DateTime fixDate) return;

            var settings = App.Settings?.Current;
            if (settings == null) return;

            // Confirm with user
            var result = MessageBox.Show(
                $"Fix {fixDate:MMMM d}?\n\nThis will cost 500 XP and can only be used once per season.",
                "Oopsie Insurance",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes) return;

            // Use server-side oopsie insurance if online
            var fixDateStr = fixDate.ToString("yyyy-MM-dd");
            if (App.ProfileSync != null && !string.IsNullOrEmpty(App.Settings?.Current?.UnifiedId))
            {
                QuestsTab.TxtFixStreakStatus.Text = Loc.Get("label_processing");
                QuestsTab.TxtFixStreakStatus.Visibility = Visibility.Visible;

                var (success, error, newXp) = await App.ProfileSync.UseOopsieInsuranceAsync(fixDateStr);
                if (!success)
                {
                    QuestsTab.TxtFixStreakStatus.Text = $"❌ {error ?? "Failed to use Oopsie Insurance"}";
                    QuestsTab.TxtFixStreakStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF5252"));
                    QuestsTab.TxtFixStreakStatus.Visibility = Visibility.Visible;
                    return;
                }

                // Server succeeded - update local state
                if (newXp.HasValue)
                {
                    // Server returns total XP; convert back to current-level XP
                    var currentLevel = settings.PlayerLevel;
                    var newLevelXp = App.Progression?.GetCurrentLevelXP(currentLevel, newXp.Value) ?? (settings.PlayerXP - 500);
                    settings.PlayerXP = Math.Max(0, newLevelXp);
                }
                else
                {
                    settings.PlayerXP -= 500;
                }
                settings.SeasonalStreakRecoveryUsed = true;
            }
            else
            {
                // No cloud account
                QuestsTab.TxtFixStreakStatus.Text = Loc.Get("label_oopsie_insurance_requires_a_cloud_account_ple");
                QuestsTab.TxtFixStreakStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF5252"));
                QuestsTab.TxtFixStreakStatus.Visibility = Visibility.Visible;
                return;
            }

            // Add the fixed date to completion dates
            var questService = App.Quests;
            if (questService?.Progress != null)
            {
                questService.Progress.DailyQuestCompletionDates.Add(fixDate);
                questService.Save();
            }

            // Recalculate the streak
            RecalculateDailyQuestStreak();

            App.Settings?.Save();
            App.Logger?.Information("Oopsie Insurance used to fix {Date} for 500 XP (server-validated)", fixDate);

            // Exit fix mode and refresh
            _isStreakFixMode = false;
            QuestsTab.TxtFixStreakStatus.Text = $"✅ Fixed {fixDate:MMMM d}! Streak updated.";
            QuestsTab.TxtFixStreakStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#00E676"));
            QuestsTab.TxtFixStreakStatus.Visibility = Visibility.Visible;
            RefreshStreakCalendar();

            // Auto-hide status after 3 seconds
            await Task.Delay(3000);
            if (!_isStreakFixMode)
            {
                QuestsTab.TxtFixStreakStatus.Visibility = Visibility.Collapsed;
                QuestsTab.TxtFixStreakStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(App.Mods?.GetAccentColorHex() ?? "#FF69B4"));
            }
        }

        private void RecalculateDailyQuestStreak()
        {
            App.Quests?.RecalculateStreak();
        }
        #endregion
    }
}
