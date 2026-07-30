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
            QuestsTab.TxtStreakFixCharges.Text = (App.Settings?.Current?.StreakFixCharges ?? 0).ToString();

            // Update header stats
            int completedToday = dailyCompleted + (weeklyProgress?.IsCompleted == true ? 1 : 0);
            QuestsTab.TxtQuestStats.Text = $"{completedToday} completed today";

            // Refresh streak calendar
            RefreshStreakCalendar();

            // Refresh the intake punch card. Riding the tail of RefreshQuestUI is deliberate: this
            // method is already called from every trigger the card cares about (the quest service
            // event, the tab switch, both rerolls, quest completion and login), so the card gets all
            // five repaint paths without a single new subscription to leak or unhook.
            RefreshPunchCard();
        }

        /// <summary>
        /// Repaints the eight-hole intake punch card (see <see cref="IntakePunchCardService"/>).
        ///
        /// <para>Drawn from code rather than bound, for the same reason <see cref="RefreshStreakCalendar"/>
        /// is: this tab has no ViewModel and the repo has no bool-to-brush converter, so the
        /// Style/DataTrigger idiom used on the leaderboard would need both invented before it drew a
        /// single circle. Eight throwaway shapes on an already-throttled refresh pass is the cheaper
        /// trade.</para>
        ///
        /// <para>Must tolerate running before the tab is realised - RefreshQuestUI can fire from the
        /// quest service during startup - hence the null guard, and must never throw: a decorative
        /// strip is not worth taking the whole quests tab down for.</para>
        /// </summary>
        private void RefreshPunchCard()
        {
            var holes = QuestsTab?.PunchCardHoles;
            if (holes == null) return;

            // Hidden while the eight-hole prize is TBD (IntakePunchCardService.UiEnabled).
            // Stamps still accrue in the service; only the paint is suppressed.
            if (!IntakePunchCardService.UiEnabled)
            {
                if (QuestsTab?.PunchCardPanel != null)
                    QuestsTab.PunchCardPanel.Visibility = Visibility.Collapsed;
                return;
            }

            try
            {
                var card = App.IntakePunchCard;
                if (card == null)
                {
                    // Service not up yet (or disposed during shutdown). Hide rather than show an
                    // empty card, which would read as "you have earned nothing" instead of "not
                    // loaded" - and the first hole is supposed to be free.
                    if (QuestsTab?.PunchCardPanel != null)
                        QuestsTab.PunchCardPanel.Visibility = Visibility.Collapsed;
                    return;
                }

                if (QuestsTab?.PunchCardPanel != null)
                    QuestsTab.PunchCardPanel.Visibility = Visibility.Visible;

                // Accent rather than a literal pink: mods retint the app, and a punch card stuck at
                // #FF69B4 would be the only element on the tab that ignored the theme.
                Color accent;
                try { accent = (Color)ColorConverter.ConvertFromString(App.Mods?.GetAccentColorHex() ?? "#FF69B4"); }
                catch { accent = Color.FromRgb(0xFF, 0x69, 0xB4); }

                holes.Children.Clear();
                for (int i = 0; i < IntakePunchCardService.TotalHoles; i++)
                    holes.Children.Add(BuildPunchHole(card.HoleAt(i), accent));

                if (QuestsTab?.TxtPunchCardProgress != null)
                {
                    QuestsTab.TxtPunchCardProgress.Text =
                        Loc.GetF("label_punch_card_x_of_y", card.PunchedCount, IntakePunchCardService.TotalHoles);
                }

                // Status line: the half-stamp nudge is the reason this panel exists at all, so it
                // gets the loudest treatment short of a banner. Card-full swaps to the "complete"
                // green used elsewhere on this tab (the quest complete overlay, the banner).
                var status = QuestsTab?.TxtPunchCardStatus;
                if (status != null)
                {
                    if (card.IsComplete)
                    {
                        status.Text = Loc.Get("label_punch_card_full");
                        status.Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0xE6, 0x76));
                        status.Visibility = Visibility.Visible;
                    }
                    else if (card.HasPendingStamp)
                    {
                        status.Text = Loc.GetF("label_punch_card_pending_hint", IntakePunchCardService.SessionCreditPercent);
                        status.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xD7, 0x00));
                        status.Visibility = Visibility.Visible;
                    }
                    else
                    {
                        status.Visibility = Visibility.Collapsed;
                    }
                }

                // The rules only need saying while there are holes left to earn; on a full card the
                // sentence would still be dangling "a reward waits at eight" at someone standing on
                // eight.
                var rules = QuestsTab?.TxtPunchCardRules;
                if (rules != null)
                {
                    rules.Text = card.IsComplete ? string.Empty : Loc.Get("label_punch_card_rules");
                    rules.Visibility = card.IsComplete ? Visibility.Collapsed : Visibility.Visible;
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning("RefreshPunchCard: {E}", ex.Message);
            }
        }

        /// <summary>
        /// Builds one hole, boxed into a fixed 46x46 cell so all eight sit on the same grid no matter
        /// which state they are in.
        ///
        /// <para>The concentric-ring "stamp" is lifted from the season recap card's identity mark
        /// (Controls/SeasonRecapCard.xaml) - it already reads as <i>stamped</i> in this app's visual
        /// language - with that file's privately-scoped Recap* brushes swapped for the live accent.</para>
        /// </summary>
        /// <param name="state">How the service says this hole should read.</param>
        /// <param name="accent">Current mod accent colour, resolved once by the caller rather than
        /// eight times here.</param>
        private static FrameworkElement BuildPunchHole(PunchHoleView state, Color accent)
        {
            var accentBrush = new SolidColorBrush(accent);

            // Explicit Center on both axes: an Ellipse with a fixed size inside a Grid defaults to
            // Stretch, which WPF then treats as centred - true, but only by accident, and it stops
            // being true the moment someone drops the Width.
            static System.Windows.Shapes.Ellipse Circle(double size, Brush? fill, Brush? stroke, double thickness)
                => new System.Windows.Shapes.Ellipse
                {
                    Width = size,
                    Height = size,
                    Fill = fill,
                    Stroke = stroke,
                    StrokeThickness = thickness,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };

            var cell = new Grid { Width = 46, Height = 46, Margin = new Thickness(4) };

            switch (state)
            {
                case PunchHoleView.Punched:
                {
                    // Filled, ringed and glowing. The disc fill is the accent at low alpha rather
                    // than a second palette entry so it follows the mod colour for free.
                    var disc = Circle(40, new SolidColorBrush(Color.FromArgb(0x3A, accent.R, accent.G, accent.B)), accentBrush, 2);
                    disc.Effect = new DropShadowEffect { Color = accent, BlurRadius = 14, ShadowDepth = 0, Opacity = 0.85 };
                    cell.Children.Add(disc);
                    cell.Children.Add(Circle(24, null, accentBrush, 1.5));
                    cell.Children.Add(Circle(10, accentBrush, null, 0));
                    cell.ToolTip = Loc.Get("tooltip_punch_card_hole_punched");
                    break;
                }

                case PunchHoleView.Pending:
                {
                    // "Nearly there", not "done": the ring is already the accent colour so it clearly
                    // belongs with the punched holes, but it is dashed and hollow-cored - a stamp that
                    // landed at half pressure. This is the hole doing the re-engagement work, and the
                    // status line under the grid appears alongside it to say what to do about it.
                    var ring = Circle(40, new SolidColorBrush(Color.FromArgb(0x18, accent.R, accent.G, accent.B)), accentBrush, 2);
                    ring.StrokeDashArray = new DoubleCollection { 3, 2.5 };
                    ring.Effect = new DropShadowEffect { Color = accent, BlurRadius = 10, ShadowDepth = 0, Opacity = 0.5 };
                    cell.Children.Add(ring);
                    cell.Children.Add(Circle(24, null, new SolidColorBrush(Color.FromArgb(0x88, accent.R, accent.G, accent.B)), 1.5));
                    cell.ToolTip = Loc.Get("tooltip_punch_card_hole_pending");

                    // Breathing pulse, same shape as the one RefreshStreakCalendar puts on fixable
                    // days. Animated in code instead of via an inline Storyboard because the element
                    // itself is built in code and has no resource dictionary to hang one off. A
                    // Forever repeat is fine here: the cell is discarded and rebuilt on the next
                    // refresh, so the clocks cannot accumulate.
                    var pulse = new DoubleAnimation
                    {
                        From = 1.0,
                        To = 0.45,
                        Duration = TimeSpan.FromMilliseconds(900),
                        AutoReverse = true,
                        RepeatBehavior = RepeatBehavior.Forever,
                        EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
                    };
                    cell.BeginAnimation(UIElement.OpacityProperty, pulse);
                    break;
                }

                default:
                {
                    // Hollow and dim. TryFindResource rather than an indexer lookup - this runs on a
                    // path that has promised never to throw, and a missing theme key should cost a
                    // washed-out circle, not the tab.
                    var panelBrush = Application.Current?.TryFindResource("PanelBgBrush") as Brush;
                    cell.Children.Add(Circle(40, panelBrush ?? Brushes.Transparent,
                        new SolidColorBrush(Color.FromRgb(0x3D, 0x3D, 0x60)), 1.5));
                    cell.ToolTip = Loc.Get("tooltip_punch_card_hole_empty");
                    break;
                }
            }

            return cell;
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

            // Fix Day button. Streak fixes are now a cumulable charge balance every account earns
            // (+1 per season, never expires, free to spend), so the button is visible to EVERYONE —
            // owning the "oopsie_insurance" skill only buys the automatic spend, not the button.
            var settings = App.Settings?.Current;
            int charges = settings?.StreakFixCharges ?? 0;

            QuestsTab.BtnFixStreak.Visibility = Visibility.Visible;
            // Cancel must stay clickable even at 0 charges, otherwise fix mode has no exit.
            QuestsTab.BtnFixStreak.IsEnabled = _isStreakFixMode || (charges > 0 && hasMissedDays);

            QuestsTab.BtnFixStreak.Content = _isStreakFixMode
                ? Loc.Get("btn_cancel_2")
                : Loc.GetF("btn_fix_day_with_count", charges);

            if (charges <= 0)
                QuestsTab.BtnFixStreak.ToolTip = Loc.Get("tooltip_no_streak_fixes_left");
            else if (!hasMissedDays)
                QuestsTab.BtnFixStreak.ToolTip = Loc.Get("tooltip_no_missed_days_your_streak_is_perfect");
            else
                QuestsTab.BtnFixStreak.ToolTip = Loc.GetF("tooltip_use_streak_fix", charges);
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

            // Validate prerequisites with user-friendly messages.
            // No skill and no XP cost any more: the only gate is the charge balance.
            var settings = App.Settings?.Current;
            if (settings == null) return;

            if (settings.StreakFixCharges < 1)
            {
                QuestsTab.TxtFixStreakStatus.Text = Loc.Get("label_no_streak_fixes_left");
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

            // Enter fix mode
            _isStreakFixMode = true;
            QuestsTab.TxtFixStreakStatus.Text = Loc.Get("label_click_a_missed_day_to_fix_it_free");
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
                Loc.GetF("label_fix_day_confirm_body", fixDate.ToString("MMMM d"), settings.StreakFixCharges),
                Loc.Get("label_fix_day_confirm_title"),
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

                // Server succeeded - update local state. The fix itself is free now, but the server
                // still echoes the account's current XP, so adopt it when present (it may have moved
                // on another device); never deduct locally.
                if (newXp.HasValue)
                {
                    // Server returns total XP; convert back to current-level XP
                    var currentLevel = settings.PlayerLevel;
                    var newLevelXp = App.Progression?.GetCurrentLevelXP(currentLevel, newXp.Value) ?? settings.PlayerXP;
                    settings.PlayerXP = Math.Max(0, newLevelXp);
                }
                settings.StreakFixCharges = Math.Max(0, settings.StreakFixCharges - 1);
                settings.SeasonalStreakRecoveryUsed = true; // back-compat flag, no longer a gate
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
            App.Logger?.Information("Streak fix used on {Date} (server-validated), {Remaining} charge(s) left",
                fixDate, settings.StreakFixCharges);

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
