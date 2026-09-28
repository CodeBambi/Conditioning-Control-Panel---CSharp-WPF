using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>"Stay until popped": ambient flashes wait for a click, with a 10-minute safety and a 40 cap.</summary>
public class FlashStayRuleTests
{
    [Fact]
    public void DefaultsOff() => Assert.False(new AppSettings().FlashStayUntilPopped);

    [Fact]
    public void OwnerNumbers()
    {
        Assert.Equal(600_000, FlashStayRule.SafetyLifetimeMs);
        Assert.Equal(40, FlashStayRule.MaxOnScreen);
    }

    [Theory]
    [InlineData(true, true, false, true)]   // ambient, clickable: stays
    [InlineData(true, true, true, false)]   // point-fired keeps its authored timing
    [InlineData(true, false, false, false)] // nobody can click it: normal lifetime
    [InlineData(false, true, false, false)] // option off
    public void Applies(bool on, bool clickable, bool pointFired, bool expected)
        => Assert.Equal(expected, FlashStayRule.Applies(on, clickable, pointFired));

    [Fact]
    public void CapIsFortyOnTheSharedHostOnly()
    {
        Assert.Equal(40, FlashStayRule.Cap(30, stay: true, sharedHost: true));
        Assert.Equal(10, FlashStayRule.Cap(10, stay: true, sharedHost: false));
        Assert.Equal(30, FlashStayRule.Cap(30, stay: false, sharedHost: true));
    }

    [Fact]
    public void SchedulerWaitsWhileTheScreenIsFull()
    {
        Assert.True(FlashStayRule.ScreenFull(true, 40, 40));
        Assert.False(FlashStayRule.ScreenFull(true, 39, 40));
        Assert.False(FlashStayRule.ScreenFull(false, 99, 40));
    }
}
