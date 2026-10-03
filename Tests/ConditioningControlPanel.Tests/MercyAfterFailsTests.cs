using ConditioningControlPanel.Models;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>ccp-bugs#1145: the Mercy threshold was a hard 3; it is a setting now, clamped 2..10.</summary>
public class MercyAfterFailsTests
{
    [Fact]
    public void Default_is_three() => Assert.Equal(3, new AppSettings().MercyAfterFails);

    [Theory]
    [InlineData(-5, 2)]
    [InlineData(0, 2)]
    [InlineData(1, 2)]
    [InlineData(2, 2)]
    [InlineData(7, 7)]
    [InlineData(10, 10)]
    [InlineData(11, 10)]
    [InlineData(999, 10)]
    public void Setter_clamps_into_two_to_ten(int written, int expected)
    {
        var s = new AppSettings { MercyAfterFails = written };
        Assert.Equal(expected, s.MercyAfterFails);
    }
}
