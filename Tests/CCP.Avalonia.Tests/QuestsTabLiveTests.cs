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
            try
            {
                if (Application.Current is null)
                    AppBuilder.Configure<AvApp>().UseSkia()
                        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                        .SetupWithoutStarting();

                var quests = new QuestService(null, dir);
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
                Assert.Equal("🎁 156 XP", card0.FindControl<TextBlock>("TxtXp")!.Text); // what CompleteQuest pays
                Assert.Equal(QuestDefinition.WeeklyQuests.Find(d => d.Id == "flash_monsoon_w")!.Name,
                    tab.FindControl<TextBlock>("TxtWeeklyQuestName")!.Text);

                // Progress + completion arrive through the service's events, not a manual refresh.
                quests.TrackBubblesPopped(pop.TargetValue);
                Dispatcher.UIThread.RunJobs();
                Assert.Equal("1/3", tab.FindControl<TextBlock>("TxtDailyQuestCounter")!.Text);
                Assert.True(tab.FindControl<Border>("QuestCompleteBanner")!.IsVisible);
                Assert.Equal("1", tab.FindControl<TextBlock>("TxtTotalDailyCompleted")!.Text);
                Assert.Equal("156", tab.FindControl<TextBlock>("TxtTotalQuestXP")!.Text);

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
                quests.Dispose();
            }
            finally
            {
                try { host?.Close(); } catch { }
                AvApp.Quests = oldQuests;
                try { Directory.Delete(dir, recursive: true); } catch { }
            }
        });
    }
}
