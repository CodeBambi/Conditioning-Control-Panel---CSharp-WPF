using ConditioningControlPanel;
using Xunit;

namespace CCP.Core.Tests;

// WPF FlashService.ScheduleNextFlash / TriggerFlash rules, now CoreFlash.
[Collection(SessionStatics.Name)]
public sealed class CoreFlashScheduleTests
{
    [Theory]
    [InlineData(60, 0.5, 60)]      // 3600/60, no jitter at mid roll
    [InlineData(60, 0.0, 42)]      // -30%
    [InlineData(60, 0.999999, 78)] // ~+30%
    [InlineData(0, 0.5, 3600)]     // frequency floors at 1/hour
    [InlineData(180, 0.0, 14)]     // 20 s -30%
    [InlineData(3600, 0.0, 3)]     // 0.7 s floors at 3 s
    public void Interval_is_3600_over_frequency_jittered_30pct_min_3s(int freq, double roll, double expected)
        => Assert.Equal(expected, CoreFlash.NextIntervalSeconds(freq, roll), 3);

    [Theory]
    [InlineData(true, true, false, false, true)]
    [InlineData(false, true, false, false, false)] // stopped
    [InlineData(true, false, false, false, false)] // feature off
    [InlineData(true, true, true, false, false)]   // previous burst still spawning
    [InlineData(true, true, false, true, false)]   // display change settling
    public void Tick_fires_only_when_running_enabled_idle_and_display_settled(bool running, bool enabled, bool busy, bool settling, bool fires)
        => Assert.Equal(fires, CoreFlash.ShouldFire(running, enabled, busy, settling));

    // WPF FlashService :682: with "suppress flashes" on, a do-not-disturb app in front skips the
    // scheduled spawn; another app in front lets it through.
    [Fact]
    public void Dnd_app_in_front_skips_the_scheduled_flash()
    {
        var s = CoreSettings.Current;
        var (list, on, enabled) = (s.DndProcessList, s.DndSuppressFlashes, s.FlashEnabled);
        var (show, front0) = (CoreFlash.ShowProvider, ConditioningControlPanel.Services.UI.DndGuard.ForegroundProcess);
        var tick = typeof(CoreFlash).GetMethod("OnTick", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        int shown = 0;
        var front = "mpv";
        try
        {
            (s.DndProcessList, s.DndSuppressFlashes, s.FlashEnabled) = (new System.Collections.Generic.List<string> { "mpv" }, true, true);
            CoreFlash.ShowProvider = () => shown++;
            ConditioningControlPanel.Services.UI.DndGuard.ForegroundProcess = () => front;
            ConditioningControlPanel.Services.UI.DndGuard.ResetCacheForTests();
            CoreFlash.Start();
            tick.Invoke(null, new object?[] { null });
            Assert.Equal(0, shown);
            front = "firefox";
            ConditioningControlPanel.Services.UI.DndGuard.ResetCacheForTests();
            tick.Invoke(null, new object?[] { null });
            Assert.Equal(1, shown);
        }
        finally
        {
            CoreFlash.Stop();
            (s.DndProcessList, s.DndSuppressFlashes, s.FlashEnabled) = (list, on, enabled);
            (CoreFlash.ShowProvider, ConditioningControlPanel.Services.UI.DndGuard.ForegroundProcess) = (show, front0);
            ConditioningControlPanel.Services.UI.DndGuard.ResetCacheForTests();
        }
    }
}
