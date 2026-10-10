using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Skia;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Chaos;
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

/// <summary>Lane c2, the web descent's desktop half (WPF Services/Chaos/DtrhHostService.cs 7.1.5): a run's video
/// arms its random slice before it triggers, the page's bark events reach the bark engine (never over her VN
/// line), the main window steps aside while the descent is up and comes back on close, and boot-error
/// remembers the failure and opens the classic door. Process-wide seams, so it runs alone.</summary>
[Collection(RunsAloneCollection.Name)]
public sealed class DtrhDirectorTests
{
    private static void EnsureApp()
    {
        if (Application.Current is null)
            AppBuilder.Configure<AvApp>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    private static GameWindow Dtrh() => new(new GameWindow.Game("dtrh", "launcher_game_dtrh_title", "dtrh/index.html"));

    [Fact]
    public void Video_payload_arms_the_fifteen_second_slice_before_it_triggers()
    {
        var (has, video, arm) = (DtrhPayloadBridge.HasVideos, DtrhPayloadBridge.TriggerVideo, DtrhPayloadBridge.ArmSegment);
        var order = new List<string>();
        try
        {
            DtrhPayloadBridge.HasVideos = () => true;
            DtrhPayloadBridge.ArmSegment = sec => order.Add("arm " + sec);
            DtrhPayloadBridge.TriggerVideo = () => { order.Add("trigger"); return true; };
            Assert.True(DtrhPayloadBridge.Fire("video", 60));
            Assert.Equal(new[] { "arm 15", "trigger" }, order);
            order.Clear();
            DtrhPayloadBridge.HasVideos = () => false;   // an empty library arms nothing
            Assert.False(DtrhPayloadBridge.Fire("video", 60));
            Assert.Empty(order);
        }
        finally { (DtrhPayloadBridge.HasVideos, DtrhPayloadBridge.TriggerVideo, DtrhPayloadBridge.ArmSegment) = (has, video, arm); }
    }

    [Fact]
    public async Task Bark_frames_raise_the_trigger_but_never_over_her_line_and_the_shell_steps_aside()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            EnsureApp();
            var (raise, tuck, restore) = (CoreBark.RaiseProvider, GameWindow.DtrhTuckShell, GameWindow.DtrhRestoreShell);
            var raised = new List<string>();
            int tucked = 0, restored = 0;
            CoreBark.RaiseProvider = (trigger, values, _) =>
            {
                raised.Add(trigger + ":" + (values != null && values.TryGetValue("combo", out var c) ? c : ""));
                return true;
            };
            GameWindow.DtrhTuckShell = () => { tucked++; return true; };
            GameWindow.DtrhRestoreShell = () => restored++;
            var w = Dtrh();
            w.Show();
            try
            {
                w.HandleMessage("{\"type\":\"ready\"}");
                Assert.Equal(1, tucked);
                Assert.Equal(0, restored);
                w.HandleMessage("{\"type\":\"run-started\",\"difficulty\":\"Gentle\"}");
                w.HandleMessage("{\"type\":\"bark\",\"event\":\"combo-milestone\",\"combo\":10,\"difficulty\":\"Gentle\"}");
                w.HandleMessage("{\"type\":\"bark\",\"event\":\"effect-fired\"}");
                w.HandleMessage("{\"type\":\"vn-speaking\",\"on\":true}");
                w.HandleMessage("{\"type\":\"bark\",\"event\":\"ending-soon\"}");
                Assert.Equal(new[] { "ChaosRunStarted:", "ChaosComboMilestone:10" }, raised);
            }
            finally
            {
                w.Close();
                (CoreBark.RaiseProvider, GameWindow.DtrhTuckShell, GameWindow.DtrhRestoreShell) = (raise, tuck, restore);
            }
            Assert.Equal(1, restored);
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task Boot_error_remembers_the_failure_closes_and_says_so()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            EnsureApp();
            var (door, tuck, restore) = (GameWindow.DtrhBootErrorNotice, GameWindow.DtrhTuckShell, GameWindow.DtrhRestoreShell);
            int opened = 0;
            GameWindow.DtrhBootErrorNotice = (_, _) => opened++;
            GameWindow.DtrhTuckShell = () => false;
            GameWindow.DtrhRestoreShell = () => { };
            var w = Dtrh();
            bool closed = false;
            w.Closed += (_, _) => closed = true;
            w.Show();
            try
            {
                w.HandleMessage("{\"type\":\"boot-error\",\"msg\":\"webgl\"}");
                Dispatcher.UIThread.RunJobs();
                Assert.True(GameWindow.DtrhBootFailedThisSession);
                Assert.True(closed);
                Assert.Equal(1, opened);
            }
            finally
            {
                if (!closed) w.Close();
                (GameWindow.DtrhBootErrorNotice, GameWindow.DtrhTuckShell, GameWindow.DtrhRestoreShell) = (door, tuck, restore);
            }
            return Task.CompletedTask;
        });
    }
}
