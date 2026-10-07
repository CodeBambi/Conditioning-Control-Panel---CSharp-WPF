using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Startup;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>main-sync 20261005 shellcopy pack: the rail's Back arrow (WPF eb6ccc404) and the
/// season-free recap title (WPF d9921236d).</summary>
public sealed class ShellCopyPortTests
{
    private static void EnsureApp()
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    [Fact]
    public Task BackArrowAppearsAfterATabSwitchAndReturnsToThePreviousTab() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        EnsureApp();
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;

        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            Dispatcher.UIThread.RunJobs();
            var back = shell.Named<Button>("BtnNavBack")!;
            Assert.False(back.IsVisible);            // nowhere to go back to on the dashboard

            shell.ShowTab("studio");
            Assert.True(back.IsVisible);

            back.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal("settings", shell.CurrentTab);
            Assert.False(back.IsVisible);

            Assert.True(shell.TabHistoryStep(back: false));   // forward (mouse XButton2 / Alt+Right)
            Assert.Equal("studio", shell.CurrentTab);
            Assert.True(back.IsVisible);
        }
        finally { shell.Close(); CoreSettings.ServiceProvider = null; }
        return Task.CompletedTask;
    });

    [Fact]
    public Task BugReportStaysAboveOverlays() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        EnsureApp();
        var w = new BugReportWindow();   // WPF bfe45f6db, ccp-bugs #704
        try
        {
            w.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.True(w.Topmost);
            Assert.True(w.IsActive);
        }
        finally { w.Close(); }
        return Task.CompletedTask;
    });

    private sealed class SteppedClock : TimeProvider
    {
        public DateTimeOffset Now = new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class FakePassiveCard : Window, IPassiveStartupSurface { }

    private static void WithLadder(Action<SteppedClock> body)
    {
        EnsureApp();
        StartupLadder.ResetForTests();
        var clock = new SteppedClock();
        StartupLadder.Time = clock;
        try { body(clock); }
        finally { StartupLadder.ResetForTests(); }
    }

    [Fact]
    public Task PassiveStartupCardsOpenOneAtATime() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        // WPF 8cbcbea01: the second card waits for the first, it never stacks.
        WithLadder(clock =>
        {
            int a = 0, b = 0;
            StartupLadder.PresentOrInbox(new InboxItem { Key = "a", Open = () => a++ });
            StartupLadder.PresentOrInbox(new InboxItem { Key = "b", Open = () => b++ });
            Assert.Equal(1, a);
            Assert.Equal(0, b);                                   // held, not stacked
            Assert.Equal(0, StartupLadder.Inbox.UnreadCount);     // and not filed away either

            clock.Now += StartupQueueCore.PassiveSettle - TimeSpan.FromMilliseconds(1);
            StartupLadder.PumpHeld();
            Assert.Equal(0, b);                                   // still settling
            clock.Now += TimeSpan.FromMilliseconds(1);
            StartupLadder.PumpHeld();
            Assert.Equal(1, b);                                   // opens once the first has settled
        });
        return Task.CompletedTask;
    });

    [Fact]
    public Task PassiveCardWaitsWhileAPassiveWindowIsOnScreen() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        WithLadder(clock =>
        {
            var card = new FakePassiveCard();
            card.Show();
            try
            {
                int b = 0;
                StartupLadder.PresentOrInbox(new InboxItem { Key = "b", Open = () => b++ });
                Assert.Equal(0, b);                               // a card is up: wait

                clock.Now += StartupQueueCore.PassiveSettle + TimeSpan.FromMinutes(1);
                StartupLadder.PumpHeld();
                Assert.Equal(0, b);                               // settle elapsed, card still up

                card.Close();
                StartupLadder.PumpHeld();
                Assert.Equal(1, b);                               // it closed: the next one opens
            }
            finally { card.Close(); }
        });
        return Task.CompletedTask;
    });

    [Fact]
    public Task BackIsRefusedUnderLockdown() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        // Deactivate in release (P02): Activate forces panic-key off and writes a recovery file,
        // which would otherwise leak to disk and into later tests.
        var ld = new LockdownService();
        BackRefused(() =>
        {
            LockdownService.Current = ld;
            ld.Activate(TimeSpan.FromMinutes(30));
        }, () => { ld.Deactivate(); LockdownService.Current = null; });
        return Task.CompletedTask;
    });

    [Fact]
    public Task BackIsRefusedDuringATutorial() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        BackRefused(() => CoreTutorial.IsActiveProvider = () => true,
                    () => CoreTutorial.IsActiveProvider = null);
        return Task.CompletedTask;
    });

    // PLAYBOOK P05: the Back arrow and the side buttons must not switch the page under a hold.
    private static void BackRefused(Action hold, Action release)
    {
        EnsureApp();
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            Dispatcher.UIThread.RunJobs();
            shell.ShowTab("studio");
            var back = shell.Named<Button>("BtnNavBack")!;
            hold();
            back.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal("studio", shell.CurrentTab);
            Assert.False(shell.TabHistoryStep(back: true));
            Assert.Equal("studio", shell.CurrentTab);
        }
        finally
        {
            release();
            shell.Close();
            CoreSettings.ServiceProvider = null;
        }
    }

    [Fact]
    public Task RecapWindowSaysMonthlyNotSeason() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        EnsureApp();
        Assert.Equal("Monthly Recap", new SeasonRecapWindow().Title);
        return Task.CompletedTask;
    });
}
