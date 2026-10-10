using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>Lane c1 small rows. X3: a pop quiz waits its turn behind a mandatory video or a bubble count
/// (WPF InteractionQueue.IsBusy) and takes it when the slot comes free. X15: the Deeper hub's Start / Stop
/// tracker button follows the tracker (WPF RefreshBlinkTrainerTrackerButton).</summary>
[Collection(RunsAloneCollection.Name)]
public sealed class C1SmallRowsTests
{
    private static void EnsureApp()
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    [Fact]
    public async Task PopQuiz_WaitsBehindAnotherInteraction_AndTakesItsTurnWhenTheSlotIsFree()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            EnsureApp();
            var old = PopQuizHost.OtherInteractionUp;
            var host = PopQuizHost.Instance;
            try
            {
                bool busy = true;
                PopQuizHost.OtherInteractionUp = () => busy;
                Assert.True(host.IsInteractionBusy);

                int replays = 0;
                Assert.True(host.Defer(() => replays++));
                Dispatcher.UIThread.RunJobs();
                host.BusyWatchTick();                       // still busy: it keeps waiting
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(0, replays);
                Assert.True(host.HasDeferred);

                busy = false;
                Assert.False(host.IsInteractionBusy);
                host.BusyWatchTick();                       // the slot is free: its turn
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(1, replays);
                Assert.False(host.HasDeferred);
                host.BusyWatchTick();                       // and only once
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(1, replays);
            }
            finally
            {
                host.DropDeferred();
                host.BusyWatchTick();
                PopQuizHost.OtherInteractionUp = old;
            }
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task DeeperTrackerButton_FollowsTheTracker()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            EnsureApp();
            var old = DeeperTabView.TrackerRunning;
            try
            {
                var tab = new DeeperTabView();
                DeeperTabView.TrackerRunning = () => true;
                tab.RefreshTrackerButton();
                Assert.Equal("Stop tracker", tab.BtnDeeperWebcamStartStopTracker.Content);
                DeeperTabView.TrackerRunning = () => false;
                tab.RefreshTrackerButton();
                Assert.Equal("Start tracker", tab.BtnDeeperWebcamStartStopTracker.Content);
            }
            finally { DeeperTabView.TrackerRunning = old; }
            return Task.CompletedTask;
        });
    }
}
