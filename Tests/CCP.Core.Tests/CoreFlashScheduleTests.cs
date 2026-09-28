using ConditioningControlPanel;
using Xunit;

namespace CCP.Core.Tests;

// WPF FlashService.ScheduleNextFlash / TriggerFlash rules, now CoreFlash.
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
}
