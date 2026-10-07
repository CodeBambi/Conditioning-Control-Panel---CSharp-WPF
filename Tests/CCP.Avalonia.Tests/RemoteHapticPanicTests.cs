using System;
using System.Threading;
using Avalonia;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Remote;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>Safety review P1 (decisions 2026-10-08): the panic key, the tray's Stop everything and the
/// spoken safe word each stop a looping remote haptic, not just mute the toy for 400 ms.</summary>
public sealed class RemoteHapticPanicTests
{
    /// <summary>Time never moves: the driver's tick timer never fires (P08).</summary>
    private sealed class FrozenClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch;
        public override ITimer CreateTimer(TimerCallback cb, object? state, TimeSpan due, TimeSpan period) => new Never();
        private sealed class Never : ITimer
        {
            public bool Change(TimeSpan due, TimeSpan period) => true;
            public void Dispose() { }
            public System.Threading.Tasks.ValueTask DisposeAsync() => default;
        }
    }

    [Fact]
    public void EveryPanicPathStopsTheRemoteHapticLoop() => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var s = CoreSettings.Current;
        var (enabled, key) = (s.PanicKeyEnabled, s.PanicKey);
        (s.PanicKeyEnabled, s.PanicKey) = (true, "F8");
        var prev = RemoteCommands.RemoteHaptics;
        var shell = new ConditioningControlPanel.Avalonia.Views.Windows.MainShellWindow();
        shell.Show();
        try
        {
            foreach (var (name, panic) in new (string, Action)[]
            {
                ("panic key", () => shell.HandlePanicKeyPress(new DateTime(2026, 1, 1))),
                ("tray", ConditioningControlPanel.Avalonia.Views.Windows.MainShellWindow.StopEverything),
                ("voice", shell.VoicePanic),
            })
            {
                var d = RemoteCommands.RemoteHaptics = new CoreRemoteHapticDriver(() => 1.0, new FrozenClock()) { Sink = _ => null };
                Assert.Null(RemoteCommands.Execute("haptic_pattern", JObject.Parse("{\"levels\":[50,80],\"loop\":true}")));
                Assert.True(d.IsPlaying, name);
                panic();
                Assert.False(d.IsPlaying, name);
            }
        }
        finally
        {
            RemoteCommands.RemoteHaptics = prev;
            shell.Close();
            (s.PanicKeyEnabled, s.PanicKey) = (enabled, key);
        }
    });
}
