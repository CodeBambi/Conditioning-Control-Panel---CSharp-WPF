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
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls.Billboard;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Billboard;
using ConditioningControlPanel.Services.Billboard.Providers;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

/// <summary>WPF BillboardProviders.CreateAll on this head: Core's Waiting/Resume/Event adapters over
/// the shell's services, from the shell's own Home deck. A service that is not up says nothing.</summary>
public sealed class BillboardAppProvidersTests
{
    private static void Click(Control c) => c.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    [Fact]
    public Task WaitingAndResumeCardsComeFromTheShellsServices() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<AvApp>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var dir = Directory.CreateTempSubdirectory("ccp-billboard-providers-").FullName;
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var s = CoreSettings.Current;
        s.DashboardBrowserCollapsed = true;
        s.MotionLevel = MotionLevel.Off;
        s.PerformanceMode = false;
        s.OfflineMode = false;
        s.BillboardSnoozedUntil.Clear();
        s.DeeperRecentFiles?.Clear();
        BillboardCardHost.Time = new FrozenClock();
        WaitingSignals.ResetForTests();

        // Today's board: three slots, one done. No Chaster service: the Locktober card stays silent.
        var quests = new QuestService(null, dir);
        quests.Progress.DailyQuests = new List<ActiveQuest>
        {
            new("pop_parade_d") { IsCompleted = true }, new("flash_rush_d"), new("spiral_sink_d"),
        };
        AvApp.Quests = quests;
        var runner = AvApp.Sessions = new SessionRunner(new SessionLogService());
        ChasterHead.Service = null;

        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            Dispatcher.UIThread.RunJobs();
            var board = shell.BillboardHost!;

            var quest = board.Deck.Cards.Single(c => c.Spec.Id == WaitingCards.CardQuests).Spec;
            Assert.Equal(string.Format(Loc.Get("billboard_card_quests_title_many"), 2), quest.Title);
            Assert.Equal(string.Format(Loc.Get("billboard_card_quests_line"), 1, 3), quest.Line);
            Assert.DoesNotContain(board.Deck.Cards, c => c.Spec.Kind == BillboardCardKind.Event);

            // A session ends: the log's LogReady marks the deck dirty (posted to the UI thread) and
            // the next dot press rebuilds it with the Resume card.
            runner.SessionLog.BeginSession(new Session { Id = "gone-session", Name = "Gone Session" });
            runner.SessionLog.EndSession(false, TimeSpan.FromSeconds(5), 0);
            Dispatcher.UIThread.RunJobs();
            Assert.True(board.Deck.IsDirty);
            Click((Button)board.ChipButtons[1]);   // not the card on screen: that only pauses
            int at = board.Deck.Cards.ToList().FindIndex(c => c.Spec.Id == ResumeCards.CardSession);
            Assert.True(at >= 0, "the Resume card joins the deck");
            Click((Button)board.ChipButtons[at]);
            Assert.Equal("Gone Session", board.CurrentCard!.Spec.Title);
            Assert.Equal(Loc.Get("billboard_card_resume_when_today"), board.CurrentCard.Spec.Line);

            // Its Start: the session is not in the library, so the Sessions page opens (WPF
            // ResumeProvider.Invoke -> ShowTab("presets")).
            Click(board.Cta);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("presets", shell.CurrentTab);
        }
        finally
        {
            shell.Close();
            WaitingSignals.ResetForTests();
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
        await Task.CompletedTask;
    });

    /// <summary>The hold never moves on its own: the deck changes only when the test presses.</summary>
    private sealed class FrozenClock : TimeProvider
    {
        public override long GetTimestamp() => 0;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    }
}
