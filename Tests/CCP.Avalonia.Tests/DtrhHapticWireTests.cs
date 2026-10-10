using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Skia;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Services.Haptics;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

/// <summary>
/// The descent's host feeds Core DtrhHapticDirector where WPF DtrhHostService does: launch, the
/// haptic-state frame, run-started / run-ended, the freeze, a covering video and the close.
/// </summary>
[Collection(RunsAloneCollection.Name)]
public sealed class DtrhHapticWireTests
{
    [Fact]
    public Task TheHostFeedsTheDirector_AndClosingStopsIt() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<AvApp>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var w = new GameWindow(new GameWindow.Game("dtrh", "launcher_game_dtrh_title", "dtrh/index.html"));
        w.Show();
        try
        {
            Assert.True(DtrhHapticDirector.Snapshot.Active);                      // launch

            w.HandleMessage("{\"type\":\"run-started\",\"difficulty\":\"Gentle\"}");
            Assert.True(DtrhHapticDirector.Snapshot.RunActive);

            w.HandleMessage("{\"type\":\"haptic-state\",\"running\":true,\"depth\":0.4,\"melt\":0.25}");
            var s = DtrhHapticDirector.Snapshot;
            Assert.True(s.PageRunning);
            Assert.Equal(0.4, s.Depth, 3);
            Assert.Equal(0.25, s.Melt, 3);

            w.HandleMessage("{\"type\":\"bark\",\"event\":\"detonated\"}");       // the tap: no device, no throw

            w.HandleMessage("{\"type\":\"freeze-state\",\"on\":true}");
            Assert.True(DtrhHapticDirector.Snapshot.WorldFrozen);
            w.HandleMessage("{\"type\":\"freeze-state\",\"on\":false}");
            Assert.False(DtrhHapticDirector.Snapshot.WorldFrozen);

            w.OnDtrhVideoStartedForTest();
            Assert.True(DtrhHapticDirector.Snapshot.VideoCovering);
            w.OnDtrhVideoEndedForTest();
            Assert.False(DtrhHapticDirector.Snapshot.VideoCovering);

            w.HandleMessage("{\"type\":\"run-ended\",\"elapsed\":5}");
            Assert.False(DtrhHapticDirector.Snapshot.RunActive);
        }
        finally { w.Close(); }
        Assert.False(DtrhHapticDirector.Snapshot.Active);                          // the close (and so a panic) ends it
        return Task.CompletedTask;
    });
}
