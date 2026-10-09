using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Nav;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>Owner, 2026-10-09: a rail icon always opens its section's first pill, never the last
/// pill chosen there (a change from WPF 7.1.5, which remembered it).</summary>
public sealed class NavRailFirstTabTests
{
    [Fact]
    public Task ARailIconOpensTheFirstPillEvenAfterAnotherWasChosen() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();

        var w = new MainShellWindow();
        try
        {
            w.Show();
            Dispatcher.UIThread.RunJobs();

            w.ShowTab("leaderboard");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("leaderboard", w.CurrentTab);

            w.OpenNavSection(NavSections.Social);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("availablesubjects", w.CurrentTab);

            w.ShowTab("quests");
            Dispatcher.UIThread.RunJobs();
            w.OpenNavSection(NavSections.You);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("discord", w.CurrentTab);
        }
        finally { w.Close(); }
        return Task.CompletedTask;
    });
}
