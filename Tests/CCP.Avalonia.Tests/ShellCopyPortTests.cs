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

    [Fact]
    public Task PassiveStartupCardsOpenOneAtATime() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        EnsureApp();   // WPF 8cbcbea01: the second card waits for the first, it never stacks
        StartupLadder.ResetForTests();
        try
        {
            int a = 0, b = 0;
            StartupLadder.PresentOrInbox(new InboxItem { Key = "a", Open = () => a++ });
            StartupLadder.PresentOrInbox(new InboxItem { Key = "b", Open = () => b++ });
            Assert.Equal(1, a);
            Assert.Equal(0, b);                                   // held, not stacked
            Assert.Equal(0, StartupLadder.Inbox.UnreadCount);     // and not filed away either

            var until = DateTime.UtcNow + StartupQueueCore.PassiveSettle + TimeSpan.FromSeconds(2);
            while (b == 0 && DateTime.UtcNow < until) { await Task.Delay(100); Dispatcher.UIThread.RunJobs(); }
            Assert.Equal(1, b);                                   // opens once the first has settled
        }
        finally { StartupLadder.ResetForTests(); }
    });

    [Fact]
    public Task RecapWindowSaysMonthlyNotSeason() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        EnsureApp();
        Assert.Equal("Monthly Recap", new SeasonRecapWindow().Title);
        return Task.CompletedTask;
    });
}
