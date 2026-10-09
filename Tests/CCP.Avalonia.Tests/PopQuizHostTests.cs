using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>PopQuizHost: the Test button path opens a quiz, a lock card defers it until the card
/// closes, and CoreEngine.Stop closes an open one.</summary>
public sealed class PopQuizHostTests
{
    [Fact]
    public Task OpensDefersBehindLockCardAndClosesOnEngineStop() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var scheduler = PopQuizHost.Instance.Scheduler;
        var wasEnabled = CoreSettings.Current.PopQuizEnabled;
        CoreEngine.PopQuiz = scheduler;
        try
        {
            CoreSettings.Current.PopQuizEnabled = true;
            scheduler.Start();
            scheduler.Show(isTest: true);
            Assert.True(PopQuizWindow.IsAnyOpen());
            CoreEngine.Stop();
            Assert.False(PopQuizWindow.IsAnyOpen());

            LockCardWindow.ShowOnAllMonitors("good girl", 1, strictMode: false, isTest: true);
            scheduler.Show(isTest: true);
            Assert.False(PopQuizWindow.IsAnyOpen());   // deferred, not stacked on the card
            LockCardWindow.ForceCloseAll();
            Dispatcher.UIThread.RunJobs();
            Assert.True(PopQuizWindow.IsAnyOpen());    // replayed once the card left

            // #763 the other way: a card waits behind an open quiz and replays when it closes.
            LockCardWindow.ShowOnAllMonitors("good girl", 1, strictMode: false, isTest: true);
            Assert.False(LockCardWindow.IsAnyOpen());
            PopQuizWindow.ForceCloseAll();
            Assert.True(LockCardWindow.IsAnyOpen());
            LockCardWindow.ForceCloseAll();

            // Audit #1833: a card held behind a quiz must not flash up while the engine stops.
            scheduler.Start();
            scheduler.Show(isTest: true);
            Assert.True(PopQuizWindow.IsAnyOpen());
            LockCardWindow.ShowOnAllMonitors("good girl", 1, strictMode: false, isTest: true);
            Assert.False(LockCardWindow.IsAnyOpen());   // held
            CoreEngine.Stop();
            Dispatcher.UIThread.RunJobs();
            Assert.False(PopQuizWindow.IsAnyOpen());
            Assert.False(LockCardWindow.IsAnyOpen());
        }
        finally
        {
            PopQuizWindow.ForceCloseAll();
            LockCardWindow.ForceCloseAll();
            CoreEngine.PopQuiz = null;
            CoreSettings.Current.PopQuizEnabled = wasEnabled;
        }
        return Task.CompletedTask;
    });
}
