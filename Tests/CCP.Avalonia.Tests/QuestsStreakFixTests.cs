using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

/// <summary>Quests "Fix day" against WPF MainWindow.QuestsTab.cs :737-960: the button shows for everyone and is
/// armed only signed in, with a charge and a missed day; a press enters fix mode (rings on missed days, Cancel);
/// a ring asks, spends on the server and only then stamps the day and takes the charge.</summary>
[Collection(RunsAloneCollection.Name)]
public sealed class QuestsStreakFixTests
{
    [Fact]
    public Task FixDayArmsEntersFixModeAndSpendsOnlyOnAServerYes() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var dir = Directory.CreateTempSubdirectory("ccp-quests-fix-").FullName;
        var oldQuests = AvApp.Quests;
        var oldProvider = ConditioningControlPanel.CoreSettings.ServiceProvider;
        var service = new SettingsService();
        ConditioningControlPanel.CoreSettings.ServiceProvider = () => service;
        var s = ConditioningControlPanel.CoreSettings.Current;
        Window? host = null;
        QuestService? quests = null;
        try
        {
            if (Application.Current is null)
                AppBuilder.Configure<AvApp>().UseSkia()
                    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            LocalizationManager.Instance.SetLanguage("en");

            var today = DateTime.Today;
            s.UnifiedId = null;
            s.StreakFixCharges = 2;
            s.StreakShieldUsedDates?.Clear();
            quests = new QuestService(null, dir);
            quests.Progress.DailyQuests = new List<ActiveQuest> { new("pop_parade_d"), new("flash_rush_d"), new("spiral_sink_d") };
            quests.Progress.WeeklyQuest = new ActiveQuest("flash_monsoon_w");
            quests.Progress.DailyQuestCompletionDates.Clear();
            AvApp.Quests = quests;

            var tab = new QuestsTabView();
            host = new Window { Width = 1200, Height = 900, Content = tab };
            host.Show();
            Dispatcher.UIThread.RunJobs();

            var button = tab.FindControl<Button>("BtnFixStreak")!;
            var status = tab.FindControl<TextBlock>("TxtFixStreakStatus")!;
            string Caption() => ((TextBlock)button.Content!).Text!;

            // Shown for everyone; signed out it is greyed with the reason.
            Assert.True(button.IsVisible);
            Assert.False(button.IsEnabled);
            Assert.Equal(Loc.GetF("btn_fix_day_with_count", 2), Caption());
            Assert.Equal(Loc.Get("tooltip_streak_fixes_need_account"), ToolTip.GetTip(button));

            s.UnifiedId = "u_k2";
            tab.RefreshQuestUI();
            if (today.Day == 1)
            {
                // The first of the month has no missed day to fix.
                Assert.False(button.IsEnabled);
                Assert.Equal(Loc.Get("tooltip_no_missed_days_your_streak_is_perfect"), ToolTip.GetTip(button));
                return;
            }
            Assert.True(button.IsEnabled);
            Assert.Equal(Loc.GetF("tooltip_use_streak_fix", 2), ToolTip.GetTip(button));

            // A press enters fix mode: every missed day wears a ring, the button is the way out.
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.True(tab.IsStreakFixMode);
            Assert.Equal(today.Day - 1, tab.StreakFixRings.Count);
            Assert.Equal(Loc.Get("btn_cancel_2"), Caption());
            Assert.Equal(Loc.Get("label_click_a_missed_day_to_fix_it_free"), status.Text);
            Assert.True(status.IsVisible);

            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.False(tab.IsStreakFixMode);
            Assert.Empty(tab.StreakFixRings);
            Assert.False(status.IsVisible);

            // "No" at the question spends nothing.
            var spent = new List<DateTime>();
            bool answer = false;
            (bool, string?, int?) reply = (false, "Server says no", null);
            tab.AskStreakFix = (_, _, _) => Task.FromResult(answer);
            tab.SpendStreakFix = day => { spent.Add(day); return Task.FromResult(reply); };
            var missed = today.AddDays(-1);
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await tab.FixDayAsync(missed);
            Assert.Empty(spent);
            Assert.Equal(2, s.StreakFixCharges);

            // A server refusal: the reason in red, the charge and the day untouched.
            answer = true;
            await tab.FixDayAsync(missed);
            Assert.Single(spent);
            Assert.Equal(2, s.StreakFixCharges);
            Assert.DoesNotContain(quests.Progress.DailyQuestCompletionDates, d => d.Date == missed);
            Assert.Contains("Server says no", status.Text);
            Assert.True(tab.IsStreakFixMode);

            // A server yes: its balance, the day stamped once, fix mode over, the stats tile repainted.
            reply = (true, null, 1);
            await tab.FixDayAsync(missed);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, s.StreakFixCharges);
            Assert.Single(quests.Progress.DailyQuestCompletionDates, d => d.Date == missed);
            Assert.False(tab.IsStreakFixMode);
            Assert.Equal(Loc.GetF("label_streak_fixed_success", missed.ToString("MMMM d")), status.Text);
            Assert.Equal("1", tab.FindControl<TextBlock>("TxtStreakFixCharges")!.Text);
            Assert.Equal(Loc.GetF("btn_fix_day_with_count", 1), Caption());
        }
        finally
        {
            try { host?.Close(); } catch { }
            quests?.Dispose();
            service.SaveImmediate();
            ConditioningControlPanel.CoreSettings.ServiceProvider = oldProvider;
            AvApp.Quests = oldQuests;
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    });
}
