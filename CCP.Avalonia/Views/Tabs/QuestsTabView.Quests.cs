using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Avalonia.Views.Tabs
{
    /// <summary>
    /// The quest half of the tab, painted off the Core <see cref="QuestService"/> (App.Quests).
    /// PORTED from MainWindow.QuestsTab.cs (RefreshQuestUI, BuildDailyCardModel,
    /// ComputeQuestXpDisplay) and MainWindow.Quests.cs (OnQuestCompleted / OnQuestProgressChanged),
    /// plus WPF's QuestsRefreshed subscription (MainWindow.xaml.cs:452-454). Lives in the view, not
    /// the shell partials: every control it paints is this view's own x:Name field.
    /// </summary>
    public partial class QuestsTabView
    {
        private static readonly IBrush Gold = new SolidColorBrush(Color.FromRgb(0xFF, 0xD7, 0x00));
        private static readonly IBrush Grey = new SolidColorBrush(Color.FromRgb(0x3D, 0x3D, 0x60));

        private Windows.QuestCompletePopup? _questCompletePopup;
        private double _weeklyFraction;
        private IDisposable? _bannerTimer;

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            UpdateAmbient(attached: true);
            if (App.Quests is not { } quests) return;
            quests.QuestCompleted += OnQuestCompleted;
            quests.QuestProgressChanged += OnQuestProgressChanged;
            quests.QuestsRefreshed += OnQuestsRefreshed;
            WeeklyProgressTrack.SizeChanged += OnWeeklyTrackSizeChanged;
            RefreshQuestUI();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnDetachedFromVisualTree(e);
            UpdateAmbient(attached: false);
            if (App.Quests is not { } quests) return;
            quests.QuestCompleted -= OnQuestCompleted;
            quests.QuestProgressChanged -= OnQuestProgressChanged;
            quests.QuestsRefreshed -= OnQuestsRefreshed;
            WeeklyProgressTrack.SizeChanged -= OnWeeklyTrackSizeChanged;
        }

        private void OnWeeklyTrackSizeChanged(object? sender, SizeChangedEventArgs e) => ApplyWeeklyBar();

        // ShowTab calls RefreshQuestUI on every entry in WPF; IsVisible flipping true is that entry.
        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (change.Property != IsVisibleProperty) return;
            UpdateAmbient(TopLevel.GetTopLevel(this) != null);
            if (IsVisible && App.Quests != null) RefreshQuestUI();
        }

        private void OnQuestsRefreshed(object? sender, EventArgs e) => Dispatcher.UIThread.Post(RefreshQuestUI);

        // WPF refreshes only while the tab is on screen.
        private void OnQuestProgressChanged(object? sender, QuestProgressEventArgs e) =>
            Dispatcher.UIThread.Post(() => { if (IsVisible) RefreshQuestUI(); });

        /// <summary>MainWindow.Quests.cs:40. ponytail: no flash voice line (App.Flash.PlayRandomSound
        /// - FlashService audio is not on this head; the service's own chime still plays) and no header
        /// stamps (MainWindow.QuestStamps.cs).</summary>
        private void OnQuestCompleted(object? sender, QuestCompletedEventArgs e) => Dispatcher.UIThread.Post(() =>
        {
            // Perk-announcement opt-out: the popup goes, the in-tab banner stays (WPF :47).
            bool announce = !CoreSettings.Current.SuppressPerkNotifications;
            try { _questCompletePopup?.Close(); } catch { }
            _questCompletePopup = null;
            if (announce)
            {
                _questCompletePopup = new Windows.QuestCompletePopup(e.QuestDefinition.Name, e.XPAwarded);
                _questCompletePopup.Show();
            }

            QuestCompleteBanner.IsVisible = true;
            TxtQuestComplete.Text = $"{e.QuestDefinition.Name} COMPLETE! +{e.XPAwarded} XP";
            RefreshQuestUI();
            CelebrateQuestComplete(e.QuestType, e.QuestDefinition.Id);

            _bannerTimer?.Dispose();
            _bannerTimer = DispatcherTimer.RunOnce(() => QuestCompleteBanner.IsVisible = false, TimeSpan.FromSeconds(5));
            Serilog.Log.Information("Quest completed: {Name} (+{XP} XP)", e.QuestDefinition.Name, e.XPAwarded);

            // Sync quest streak data to the server (WPF: ProfileSync.SyncProfileAsync when enabled).
            if (Platform.AccountSeed.Sync is { Loaded: true } sync) _ = sync.PushAsync("quest-complete");
        });

        /// <summary>MainWindow.QuestsTab.cs:115; the XP is QuestService.ScaledQuestXp, what CompleteQuest pays.</summary>
        internal static (int Xp, string? Bonus) ComputeQuestXpDisplay(QuestDefinition def, AppSettings s)
        {
            var rerollMult = SkillTreeRules.GetRerollBonusMultiplier(s);
            // The streak this completion pays at (the day's first daily advances it first).
            var streak = App.Quests?.StreakPaidOn(def.Type, s) ?? s.DailyQuestStreak;
            var xp = QuestService.ScaledQuestXp(def.XPReward, s, streak);
            string? bonus = null;
            if (streak > 0) bonus = $"+{streak * 3}%\U0001f525";
            if (rerollMult > 1.0)
            {
                var r = $"+{(int)((rerollMult - 1.0) * 100)}%\U0001f503";
                bonus = bonus == null ? r : $"{bonus} {r}";
            }
            return (xp, bonus);
        }

        // Server-rolled quests have no loc key, so the lookup returns the raw key (WPF :150).
        private static string Localized(string localized, string fallback) =>
            string.IsNullOrWhiteSpace(localized) || localized.StartsWith("quest_", StringComparison.Ordinal) ? fallback : localized;

        private static DailyQuestCardModel BuildDailyCardModel(int slot, ActiveQuest quest, QuestDefinition def, int rerollsLeft, AppSettings s)
        {
            var (xp, bonus) = ComputeQuestXpDisplay(def, s);
            bool canReroll = !quest.IsCompleted && rerollsLeft > 0;
            return new DailyQuestCardModel
            {
                Slot = slot,
                Icon = def.Icon,
                Name = CoreMods.MakeModAware(Localized(def.LocalizedName, def.Name)),
                Description = CoreMods.MakeModAware(Localized(def.LocalizedDescription, def.Description)),
                Current = quest.CurrentProgress,
                Target = def.TargetValue,
                IsCompleted = quest.IsCompleted,
                XpText = $"\U0001f381 {xp} XP",
                BonusText = bonus,
                // ponytail: no quest art - GetModeAwareQuestImagePath resolves pack:// resources this
                // head does not ship; the card draws its icon instead.
                Art = null,
                CanReroll = canReroll,
                RerollText = canReroll ? Loc.GetF("btn_reroll_with_count", rerollsLeft) : Loc.Get("btn_reroll_none"),
                RerollTooltip = canReroll ? Loc.Get("quest_card_reroll_tip") : Loc.Get("quest_card_reroll_tip_none"),
            };
        }

        /// <summary>MainWindow.QuestsTab.cs:188 RefreshQuestUI, minus the punch card (its service is
        /// WPF-only) and the season title (App.QuestDefinitions is not exposed; the XAML title stays).</summary>
        internal void RefreshQuestUI()
        {
            var quests = App.Quests;
            if (quests == null) return;
            var s = CoreSettings.Current;
            quests.RecalculateStreak();

            int dailyCompleted = quests.GetDailyQuestsCompletedToday();
            TxtDailyQuestCounter.Text = $"{dailyCompleted}/{QuestService.MaxDailyQuestsPerDay}";
            bool allDailyDone = quests.AreAllDailyQuestsCompleted();
            DailySegment1.Background = dailyCompleted >= 1 ? Gold : Grey;
            DailySegment2.Background = dailyCompleted >= 2 ? Gold : Grey;
            DailySegment3.Background = dailyCompleted >= 3 ? Gold : Grey;
            DailyAllCompletedMessage.IsVisible = allDailyDone;

            var board = quests.GetDailySlots();
            int rerollsLeft = quests.GetRemainingDailyRerolls();
            for (int i = 0; i < _dailyCards.Length; i++)
            {
                var quest = i < board.Count ? board[i].Quest : null;
                var def = i < board.Count ? board[i].Definition : null;
                if (quest == null || def == null)
                    _dailyCards[i].ShowEmpty(i, Loc.Get("quest_card_empty_title"), Loc.Get("quest_card_empty_body"));
                else
                    _dailyCards[i].Apply(BuildDailyCardModel(i, quest, def, rerollsLeft, s));
            }

            TxtDailyRerollPool.Text = allDailyDone ? ""
                : rerollsLeft > 1 ? Loc.GetF("quest_daily_pool_many", rerollsLeft)
                : rerollsLeft == 1 ? Loc.Get("quest_daily_pool_one")
                : Loc.Get("quest_daily_pool_none");

            var weeklyDef = quests.GetCurrentWeeklyDefinition();
            var weekly = quests.Progress.WeeklyQuest;
            var rerollText = (TextBlock)BtnRerollWeekly.Content!;
            if (weeklyDef != null && weekly != null)
            {
                TxtWeeklyQuestIcon.Text = weeklyDef.Icon;
                TxtWeeklyQuestName.Text = CoreMods.MakeModAware(weeklyDef.Name);
                TxtWeeklyQuestDesc.Text = CoreMods.MakeModAware(weeklyDef.Description);
                TxtWeeklyProgress.Text = $"{weekly.CurrentProgress} / {weeklyDef.TargetValue}";
                var (xp, _) = ComputeQuestXpDisplay(weeklyDef, s);
                TxtWeeklyXP.Text = $"\U0001f381 {xp} XP";
                var rerollMult = SkillTreeRules.GetRerollBonusMultiplier(s);
                TxtWeeklyStreakBonus.Text = $"(+{s.DailyQuestStreak * 3}%\U0001f525)";
                TxtWeeklyStreakBonus.IsVisible = s.DailyQuestStreak > 0;
                TxtWeeklyRerollBonus.Text = $"(+{(int)((rerollMult - 1.0) * 100)}%\U0001f503)";
                TxtWeeklyRerollBonus.IsVisible = rerollMult > 1.0;
                _weeklyFraction = weeklyDef.TargetValue > 0
                    ? Math.Min(1.0, (double)weekly.CurrentProgress / weeklyDef.TargetValue) : 0;
                ApplyWeeklyBar();

                WeeklyCompletedOverlay.IsVisible = weekly.IsCompleted;
                int weeklyRerolls = quests.GetRemainingWeeklyRerolls();
                BtnRerollWeekly.IsEnabled = !weekly.IsCompleted && weeklyRerolls > 0;
                rerollText.Text = weekly.IsCompleted ? Loc.Get("btn_completed")
                    : weeklyRerolls > 0 ? $"🔄 Reroll ({weeklyRerolls} left)" : "🔄 No rerolls left";
            }
            else if (weekly is { IsCompleted: true })
            {
                // Rotated-away definition of a finished weekly (#496): a "done for the week" card.
                TxtWeeklyQuestIcon.Text = "✅";
                TxtWeeklyQuestName.Text = Loc.Get("quest_weekly_done_name");
                TxtWeeklyQuestDesc.Text = Loc.Get("quest_weekly_done_desc");
                TxtWeeklyProgress.Text = TxtWeeklyXP.Text = "";
                TxtWeeklyStreakBonus.IsVisible = TxtWeeklyRerollBonus.IsVisible = false;
                WeeklyCompletedOverlay.IsVisible = true;
                BtnRerollWeekly.IsEnabled = false;
                rerollText.Text = Loc.Get("btn_completed");
            }

            // Desktop totals + the adopted read-only mobile ledger (WPF :318).
            TxtTotalDailyCompleted.Text = (quests.Progress.TotalDailyQuestsCompleted + s.MobileQuestDailyCompleted).ToString();
            TxtTotalWeeklyCompleted.Text = (quests.Progress.TotalWeeklyQuestsCompleted + s.MobileQuestWeeklyCompleted).ToString();
            TxtTotalQuestXP.Text = (quests.Progress.TotalXPFromQuests + s.MobileQuestXP).ToString();
            TxtStreakFixCharges.Text = s.StreakFixCharges.ToString();
            TxtQuestStats.Text = $"{dailyCompleted + (weekly?.IsCompleted == true ? 1 : 0)} completed today";
            // The bonus the next daily pays at, as the daily cards quote it.
            TxtQuestStreakCount.Text = s.DailyQuestStreak > 0
                ? $"\U0001f525 {s.DailyQuestStreak} day streak (+{quests.StreakPaidOn(QuestType.Daily, s) * 3}% XP)" : "";

            PaintStreakCalendar();
        }

        private void ApplyWeeklyBar()
        {
            var width = WeeklyProgressTrack.Bounds.Width;
            if (width > 0) WeeklyProgressFill.Width = width * _weeklyFraction;
        }

        private void PaintMonth(QuestService quests)
        {
            double width = StreakCalendarCanvas.Bounds.Width;
            if (width <= 0) return;
            var completed = new HashSet<DateTime>(quests.Progress.DailyQuestCompletionDates.Select(d => d.Date));
            var shielded = new HashSet<DateTime>(CoreSettings.Current.StreakShieldUsedDates.Select(d => d.Date));
            IBrush accent;
            try { accent = new SolidColorBrush(Color.Parse(CoreMods.AccentColorHex)); }
            catch { accent = new SolidColorBrush(Color.FromRgb(0xFF, 0x69, 0xB4)); }
            var panel = this.TryFindResource("PanelBgBrush", out var p) && p is IBrush pb ? pb : Brushes.Transparent;

            var today = DateTime.Today;
            int days = DateTime.DaysInMonth(today.Year, today.Month);
            double spacing = width / days, centerY = 25, prevX = 0;
            bool prevDone = false;
            const string letters = "SMTWTFS";
            for (int i = 0; i < days; i++)
            {
                var day = new DateTime(today.Year, today.Month, i + 1);
                bool done = completed.Contains(day), future = day > today, isToday = day == today;
                double size = day.DayOfWeek == DayOfWeek.Sunday ? 26 : 20, x = spacing * i + spacing / 2;
                if (i > 0)
                    StreakCalendarCanvas.Children.Add(new Line
                    {
                        StartPoint = new Point(prevX, centerY), EndPoint = new Point(x, centerY),
                        StrokeThickness = 2, Stroke = done && prevDone ? accent : Grey,
                    });
                var node = new Border
                {
                    Width = size, Height = size, CornerRadius = new CornerRadius(size / 2),
                    Background = done ? accent : panel,
                    BorderBrush = isToday ? Gold : Grey, BorderThickness = new Thickness(isToday ? 2 : 1),
                    Child = new TextBlock
                    {
                        Text = $"{letters[(int)day.DayOfWeek]}{day.Day}", FontSize = 7, FontWeight = FontWeight.Bold,
                        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                        Foreground = done ? Brushes.White : new SolidColorBrush(future ? Color.Parse("#444444") : Color.Parse("#888888")),
                    },
                };
                Canvas.SetLeft(node, x - size / 2);
                Canvas.SetTop(node, centerY - size / 2);
                StreakCalendarCanvas.Children.Add(node);
                if (shielded.Contains(day))
                {
                    var shield = new TextBlock { Text = "🛡️", FontSize = 10 };
                    Canvas.SetLeft(shield, x - 6);
                    Canvas.SetTop(shield, centerY - size / 2 - 12);
                    StreakCalendarCanvas.Children.Add(shield);
                }
                prevX = x;
                prevDone = done;
            }
        }
    }
}
