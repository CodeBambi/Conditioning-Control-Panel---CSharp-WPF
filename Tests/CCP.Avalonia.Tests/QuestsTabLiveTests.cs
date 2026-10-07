using System;
using System.Collections.Generic;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Skia;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

/// <summary>The Quests tab paints the live Core QuestService and follows its events
/// (WPF MainWindow.QuestsTab.cs RefreshQuestUI / MainWindow.Quests.cs OnQuestCompleted).</summary>
public sealed class QuestsTabLiveTests
{
    [Fact]
    public void TabPaintsTheBoard_FollowsCompletion_AndSpendsRerolls()
    {
        AvaloniaTestDispatcher.Run(() =>
        {
            var dir = Directory.CreateTempSubdirectory("ccp-quests-tab-").FullName;
            var oldQuests = AvApp.Quests;
            Window? host = null;
            QuestService? quests = null;
            // CompleteQuest reads CoreSettings.Service (null when no earlier test set a provider, so it
            // paid unscaled XP) and advances the streak first; pin both so quote and payout match in any order.
            var oldProvider = ConditioningControlPanel.CoreSettings.ServiceProvider;
            var service = new SettingsService();
            ConditioningControlPanel.CoreSettings.ServiceProvider = () => service;
            var settings = ConditioningControlPanel.CoreSettings.Current;
            try
            {
                if (Application.Current is null)
                    AppBuilder.Configure<AvApp>().UseSkia()
                        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                        .SetupWithoutStarting();

                // Explicit, not whatever an earlier test left in the shared settings.
                settings.PlayerLevel = 1;
                // Today's first completion re-starts a gap-broken streak at 1 (AdvanceQuestStreak), so 1
                // is the streak both before and after the payout.
                settings.DailyQuestStreak = 1;
                settings.StreakShieldUsedDates?.Clear();
                settings.LastDailyQuestDate = null;   // no shield fill (CoreQuests.UseStreakShieldProvider)
                quests = new QuestService(null, dir);
                quests.Progress.DailyQuests = new List<ActiveQuest> { new("pop_parade_d"), new("flash_rush_d"), new("spiral_sink_d") };
                quests.Progress.WeeklyQuest = new ActiveQuest("flash_monsoon_w");
                AvApp.Quests = quests;

                var tab = new QuestsTabView();
                host = new Window { Width = 1200, Height = 900, Content = tab };
                host.Show();
                Dispatcher.UIThread.RunJobs();

                var card0 = tab.FindControl<DailyQuestCard>("DailyCard0")!;
                var pop = QuestDefinition.DailyQuests.Find(d => d.Id == "pop_parade_d")!;
                Assert.Equal("0/3", tab.FindControl<TextBlock>("TxtDailyQuestCounter")!.Text);
                Assert.Equal($"0 / {pop.TargetValue}", card0.FindControl<TextBlock>("TxtProgress")!.Text);
                // The card quotes what CompleteQuest pays (one Core formula); the payout lands below.
                var payout = QuestService.ScaledQuestXp(pop.XPReward, settings);
                Assert.Equal($"🎁 {payout} XP", card0.FindControl<TextBlock>("TxtXp")!.Text);

                // A language switch after the paint (any earlier test's SetLanguage, posted from
                // off the UI thread, used to land here) keeps what code wrote, as WPF does, and still
                // re-localizes text code never touched.
                var seasonTitle = tab.FindControl<TextBlock>("TxtSeasonTitle")!;
                var loc = ConditioningControlPanel.Localization.LocalizationManager.Instance;
                var lang = loc.CurrentLanguage;
                try
                {
                    loc.SetLanguage(lang == "de" ? "en" : "de");
                    Dispatcher.UIThread.RunJobs();
                    Assert.Equal(ConditioningControlPanel.Localization.Loc.Get("section_seasons"), seasonTitle.Text);
                    Assert.Equal(QuestDefinition.WeeklyQuests.Find(d => d.Id == "flash_monsoon_w")!.Name,
                        tab.FindControl<TextBlock>("TxtWeeklyQuestName")!.Text);
                }
                finally { loc.SetLanguage(lang); }
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(ConditioningControlPanel.Localization.Loc.Get("section_seasons"), seasonTitle.Text);
                Assert.Equal(QuestDefinition.WeeklyQuests.Find(d => d.Id == "flash_monsoon_w")!.Name,
                    tab.FindControl<TextBlock>("TxtWeeklyQuestName")!.Text);

                // Progress + completion arrive through the service's events, not a manual refresh.
                quests.TrackBubblesPopped(pop.TargetValue);
                Dispatcher.UIThread.RunJobs();
                Assert.Equal("1/3", tab.FindControl<TextBlock>("TxtDailyQuestCounter")!.Text);
                Assert.True(tab.FindControl<Border>("QuestCompleteBanner")!.IsVisible);
                Assert.Equal("1", tab.FindControl<TextBlock>("TxtTotalDailyCompleted")!.Text);
                Assert.Equal(payout.ToString(), tab.FindControl<TextBlock>("TxtTotalQuestXP")!.Text);

                // A reroll click spends the budget on that seat.
                var seat1 = quests.Progress.DailyQuests[1]!.DefinitionId;
                var budget = quests.GetRemainingDailyRerolls();
                tab.FindControl<DailyQuestCard>("DailyCard1")!.FindControl<Button>("BtnReroll")!
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Dispatcher.UIThread.RunJobs();
                Assert.NotEqual(seat1, quests.Progress.DailyQuests[1]!.DefinitionId);
                Assert.Equal(budget - 1, quests.GetRemainingDailyRerolls());

                var weekly = quests.Progress.WeeklyQuest!.DefinitionId;
                tab.FindControl<Button>("BtnRerollWeekly")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Dispatcher.UIThread.RunJobs();
                Assert.NotEqual(weekly, quests.Progress.WeeklyQuest!.DefinitionId);
            }
            finally
            {
                try { host?.Close(); } catch { }
                quests?.Dispose();
                ConditioningControlPanel.CoreSettings.ServiceProvider = oldProvider;
                AvApp.Quests = oldQuests;
                try { Directory.Delete(dir, recursive: true); } catch { }
            }
        });
    }
}
