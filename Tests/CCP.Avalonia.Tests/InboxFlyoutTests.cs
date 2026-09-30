using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Skia;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services.Startup;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>shell-inbox + ctrl-inbox-flyout: the quiet window parks a card, the badge counts it, the
/// flyout opens / dismisses it (WPF MainWindow.Inbox.cs, Controls/InboxFlyout.xaml.cs).</summary>
public sealed class InboxFlyoutTests
{
    private static Task Run(Action body) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        StartupLadder.ResetForTests();
        try { body(); }
        finally { StartupLadder.ResetForTests(); Dispatcher.UIThread.RunJobs(); }
        return Task.CompletedTask;
    });

    private static void Click(Button b) => b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    [Fact]
    public Task QuietWindowParksARowAndOpenRunsIt() => Run(() =>
    {
        int opened = 0;
        var item = new InboxItem { Key = "k", Title = "T", Open = () => opened++ };

        StartupLadder.PresentOrInbox(item);   // nothing quiet: presented now, no row
        Assert.Equal(1, opened);
        Assert.Equal(0, StartupLadder.Inbox.UnreadCount);

        StartupLadder.BeginFirstLaunchQuiet(TimeSpan.FromMinutes(10));
        StartupLadder.PresentOrInbox(item);
        StartupLadder.PresentOrInbox(item);   // one row per key
        Assert.Equal(1, opened);
        Assert.Equal(1, StartupLadder.Inbox.UnreadCount);

        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            Dispatcher.UIThread.RunJobs();
            var badge = shell.Named<Button>("BtnInbox")!;
            Assert.True(badge.IsVisible);
            Assert.Equal("1", badge.Content);

            Click(badge);
            Dispatcher.UIThread.RunJobs();
            Assert.True(shell.IsInboxOpen);

            var flyout = shell.GetVisualDescendants().OfType<InboxFlyout>().SingleOrDefault()
                         ?? TopLevel.GetTopLevel(shell)!.GetVisualDescendants().OfType<InboxFlyout>().Single();
            flyout.UpdateLayout();
            var open = flyout.GetVisualDescendants().OfType<Button>().First(b => b.Classes.Contains("open"));
            Click(open);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(2, opened);
            Assert.False(shell.IsInboxOpen);
            Assert.False(badge.IsVisible);
        }
        finally { shell.Close(); }
    });

    [Fact]
    public Task DismissRunsBookkeepingNotTheSurface() => Run(() =>
    {
        int opened = 0, dismissed = 0;
        StartupLadder.BeginFirstLaunchQuiet(TimeSpan.FromMinutes(10));
        StartupLadder.PresentOrInbox(new InboxItem { Key = "d", Open = () => opened++, Dismiss = () => dismissed++ });

        var host = new Window { Content = new InboxFlyout() };
        try
        {
            host.Show();
            Dispatcher.UIThread.RunJobs();
            var flyout = (InboxFlyout)host.Content!;
            bool closed = false;
            flyout.RequestClose += () => closed = true;
            Click(flyout.GetVisualDescendants().OfType<Button>().First(b => b.Classes.Contains("dismiss")));
            Dispatcher.UIThread.RunJobs();

            Assert.Equal((0, 1), (opened, dismissed));
            Assert.True(closed);                                    // last row gone -> host closes
            Assert.True(flyout.FindControl<TextBlock>("TxtInboxEmpty")!.IsVisible);
        }
        finally { host.Close(); }
    });

    [Fact]
    public Task BadgeCapsAt99Plus() => Run(() =>
    {
        StartupLadder.BeginFirstLaunchQuiet(TimeSpan.FromMinutes(10));
        for (int i = 0; i < 100; i++) StartupLadder.PresentOrInbox(new InboxItem { Key = "n" + i });
        var shell = new MainShellWindow();
        try { Assert.Equal("99+", shell.Named<Button>("BtnInbox")!.Content); }
        finally { shell.Close(); }
    });
}
