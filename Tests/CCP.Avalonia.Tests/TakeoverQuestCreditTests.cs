using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;
using AvApp = global::ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

/// <summary>WPF bfd5a2d22 (ccp-bugs #1327): Takeover quest time is the measured interval between
/// ticks, so a minimised panel's late ticks count in full, and a sleep-sized gap counts nothing.</summary>
public sealed class TakeoverQuestCreditTests
{
    [Fact]
    public Task LateTicksCreditInFullAndASleepCreditsNothing() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<AvApp>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var s = CoreSettings.Current;
        var (consent, enabled, resume) = (s.AutonomyConsentGiven, s.AutonomyModeEnabled, s.AutonomyResumeOnStartup);
        var oldQuests = AvApp.Quests;
        var oldPremium = CoreEntitlement.HasPremiumProvider;
        var dir = Directory.CreateTempSubdirectory("ccp-takeover-quest-").FullName;
        var quests = new QuestService(null, dir);
        var shell = new MainShellWindow();
        try
        {
            s.AutonomyConsentGiven = true;
            s.AutonomyResumeOnStartup = false;
            CoreEntitlement.HasPremiumProvider = () => true;
            quests.Progress.DailyQuests = new List<ActiveQuest> { new("takeover_drift_d"), new("pop_parade_d"), new("flash_rush_d") };
            AvApp.Quests = quests;
            var now = TimeSpan.Zero;
            shell.TakeoverClock = () => now;
            shell.Show();

            Assert.True(shell.SetAutonomyEnabled(true));
            Dispatcher.UIThread.RunJobs();
            Assert.True(shell.TakeoverQuestTickRunning);

            // Ticks 40 s apart (a throttled, minimised panel): 13 of them bank 8 whole minutes (8.67).
            for (var i = 0; i < 13; i++) { now += TimeSpan.FromSeconds(40); shell.CreditTakeoverTime(); }
            Assert.Equal(8, quests.Progress.DailyQuests[0]!.CurrentProgress);

            // A 4-minute gap is the PC asleep: nothing.
            now += TimeSpan.FromMinutes(4);
            shell.CreditTakeoverTime();
            Assert.Equal(8, quests.Progress.DailyQuests[0]!.CurrentProgress);

            shell.Autonomy.Stop();
            Dispatcher.UIThread.RunJobs();
            Assert.False(shell.TakeoverQuestTickRunning);
            now += TimeSpan.FromSeconds(30);
            shell.CreditTakeoverTime();
            Assert.Equal(8, quests.Progress.DailyQuests[0]!.CurrentProgress);
        }
        finally
        {
            shell.Autonomy.Stop();
            CoreEntitlement.HasPremiumProvider = oldPremium;
            (s.AutonomyConsentGiven, s.AutonomyModeEnabled, s.AutonomyResumeOnStartup) = (consent, enabled, resume);
            AvApp.Quests = oldQuests;
            quests.Dispose();
            CoreEngine.Stop();
            shell.RequestExit();
            try { Directory.Delete(dir, true); } catch { }
        }
        return Task.CompletedTask;
    });
}
