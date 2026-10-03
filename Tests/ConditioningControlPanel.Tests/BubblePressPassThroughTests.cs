using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>ccp-bugs #1342: a red (Natasha) bubble is judged by how long the real button stays
/// down, so its press must reach the system instead of being swallowed by the hook.</summary>
public class BubblePressPassThroughTests
{
    [Theory]
    [InlineData(false, false, false)]   // plain bubble: swallowed, one click
    [InlineData(true, false, true)]     // live defuse bubble
    [InlineData(false, true, true)]     // red bubble
    [InlineData(true, true, true)]
    public void Only_hold_judged_bubbles_pass_the_press_through(bool defuse, bool natasha, bool expected)
        => Assert.Equal(expected, BubbleService.PressPassesThrough(defuse, natasha));
}
