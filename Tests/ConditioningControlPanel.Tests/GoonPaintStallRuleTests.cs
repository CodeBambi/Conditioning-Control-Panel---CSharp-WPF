// Both assemblies carry a GoonHostService in this namespace (the WPF window host, and the windowless
// half ported to CCP.Core); this suite pins the WPF app's.
extern alias wpf;
using GoonHostService = wpf::ConditioningControlPanel.Services.GoonGame.GoonHostService;
using ConditioningControlPanel.Services.GoonGame;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>ccp-bugs #1326: the paint-stall watchdog closed a live match when the app's own UI
/// thread was the stalled one. A late watch tick or a stalled UI thread restarts the clock; only a
/// frozen frame counter seen by a healthy UI thread past the threshold is a stall.</summary>
public class GoonPaintStallRuleTests
{
    [Theory]
    [InlineData(5, 5.0, 0, "Healthy")]
    [InlineData(11, 5.0, 0, "Healthy")]    // the #1326 figure is no longer a stall
    [InlineData(20, 5.0, 0, "Healthy")]
    [InlineData(21, 5.0, 0, "Stalled")]
    [InlineData(21, 7.9, 2999, "Stalled")]  // ordinary jitter still counts
    [InlineData(25, 19.4, 0, "UiStalled")]  // the watch itself ticked 14 s late
    [InlineData(25, 5.0, 14378, "UiStalled")] // the app's own UI stall age
    public void A_stall_needs_a_healthy_ui_thread(double frozen, double tickGap, long uiStallMs,
        string expected)
        => Assert.Equal(expected, GoonHostService.PaintStallVerdict(frozen, tickGap, uiStallMs).ToString());

    [Fact]
    public void The_threshold_is_twenty_seconds()
        => Assert.Equal(20, GoonHostService.PaintStallSeconds);
}
