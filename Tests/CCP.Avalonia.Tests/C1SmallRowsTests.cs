using System;
using System.Linq;
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

    /// <summary>X7: the shell's ambient loops ride the shared 30 fps beat. An infinite Avalonia Animation
    /// ticks at render rate and keeps the whole window composing at 60 Hz for as long as it runs.</summary>
    [Theory]
    [InlineData("CCP.Avalonia/Views/Windows/MainShellWindow.TabFxTakeoverLabStatus.cs")]
    [InlineData("CCP.Avalonia/Views/Windows/MainShellWindow.Animations.cs")]
    [InlineData("CCP.Avalonia/Views/Windows/MainShellWindow.DescentFuse.cs")]
    public void ShellAmbientLoops_RunNoInfiniteAnimation(string file)
    {
        var code = string.Join("\n", System.IO.File.ReadAllLines(System.IO.Path.Combine(Root(), file))
            .Where(l => !l.TrimStart().StartsWith("//")));
        Assert.DoesNotContain("IterationCount.Infinite", code);
        Assert.DoesNotContain(".RunAsync(", code);
    }

    [Fact]
    public void AmbientLoop_Curves_MatchTheAnimationsTheyReplace()
    {
        Assert.Equal(0, global::ConditioningControlPanel.Avalonia.Helpers.AmbientLoop.Breath(0, 2), 6);
        Assert.Equal(1, global::ConditioningControlPanel.Avalonia.Helpers.AmbientLoop.Breath(2, 2), 6);
        Assert.Equal(0, global::ConditioningControlPanel.Avalonia.Helpers.AmbientLoop.Breath(4, 2), 6);
        Assert.Equal(0.5, global::ConditioningControlPanel.Avalonia.Helpers.AmbientLoop.PingPong(0.75, 1.5), 6);
        Assert.Equal(0.5, global::ConditioningControlPanel.Avalonia.Helpers.AmbientLoop.PingPong(2.25, 1.5), 6);
        Assert.Equal(0.25, global::ConditioningControlPanel.Avalonia.Helpers.AmbientLoop.Saw(3.75, 3), 6);
    }

    private static string Root([System.Runtime.CompilerServices.CallerFilePath] string here = "") =>
        System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(here)!, "..", ".."));

    /// <summary>W9 (PresetsTabView): the session detail's corner GIF option. The switch folds its
    /// settings, and the picks ride the session as it starts; unticked means off.</summary>
    [Fact]
    public async Task SessionCornerGifOption_FoldsItsSettings_AndThePicksRideTheSession()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            EnsureApp();
            var tab = new PresetsTabView();
            T Part<T>(string name) where T : global::Avalonia.Controls.Control => global::Avalonia.Controls.ControlExtensions.FindControl<T>(tab, name)!;
            var session = new ConditioningControlPanel.Models.Session
            {
                Id = "c1", Name = "c1", DurationMinutes = 10, HasCornerGifOption = true,
                Settings = new ConditioningControlPanel.Models.SessionSettings(),
            };
            var chk = Part<global::Avalonia.Controls.CheckBox>("ChkCornerGifEnabled");
            var fold = Part<global::Avalonia.Controls.StackPanel>("CornerGifSettings");
            Assert.False(fold.IsVisible);
            chk.IsChecked = true;
            Assert.True(fold.IsVisible);

            Part<global::Avalonia.Controls.RadioButton>("RbCornerTR").IsChecked = true;
            Part<global::Avalonia.Controls.Slider>("SliderCornerGifSize").Value = 240;
            Part<global::Avalonia.Controls.Slider>("SliderCornerGifOpacity").Value = 35;
            Assert.Equal("240px", Part<global::Avalonia.Controls.TextBlock>("TxtCornerGifSize").Text);
            Assert.Equal("35%", Part<global::Avalonia.Controls.TextBlock>("TxtCornerGifOpacity").Text);
            tab.SetCornerGifPath(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "mine.gif"));

            tab.ApplyCornerGifPicks(session);
            Assert.True(session.Settings.CornerGifEnabled);
            Assert.EndsWith("mine.gif", session.Settings.CornerGifPath);
            Assert.Equal(ConditioningControlPanel.Models.CornerPosition.TopRight, session.Settings.CornerGifPosition);
            Assert.Equal(240, session.Settings.CornerGifSize);
            Assert.Equal(35, session.Settings.CornerGifOpacity);

            chk.IsChecked = false;
            Assert.False(fold.IsVisible);
            tab.ApplyCornerGifPicks(session);
            Assert.False(session.Settings.CornerGifEnabled);   // never left on from the last start
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
