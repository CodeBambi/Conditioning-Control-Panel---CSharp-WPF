using ConditioningControlPanel;
using ConditioningControlPanel.Models;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// ccp-bugs#1154: mandatory videos got their own monitor pick. The default must be the
/// follow-global sentinel so every existing install keeps today's placement.
/// </summary>
public class VideoTargetMonitorTests
{
    [Fact]
    public void Default_follows_the_global_pick() =>
        Assert.Equal(App.MonitorTargetFollowGlobal, new AppSettings().VideoTargetMonitor);
}
