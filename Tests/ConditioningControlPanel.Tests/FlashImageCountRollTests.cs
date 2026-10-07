using System;
using System.Linq;
using ConditioningControlPanel.Models;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// ccp-bugs #658: images per flash as a random range. Off = the flat Images count; on = a roll
/// between the two ends inclusive, whichever order the sliders are in.
/// </summary>
public class FlashImageCountRollTests
{
    [Fact]
    public void Off_returns_the_flat_count()
        => Assert.Equal(7, AppSettings.RollFlashImageCount(false, 2, 7, new Random(1)));

    [Fact]
    public void On_stays_inside_the_range_and_reaches_both_ends()
    {
        var rng = new Random(42);
        var rolls = Enumerable.Range(0, 2000).Select(_ => AppSettings.RollFlashImageCount(true, 2, 6, rng)).ToList();
        Assert.All(rolls, n => Assert.InRange(n, 2, 6));
        Assert.Contains(2, rolls);
        Assert.Contains(6, rolls);
    }

    [Fact]
    public void A_floor_above_the_ceiling_is_sorted_out()
    {
        var rng = new Random(7);
        var rolls = Enumerable.Range(0, 500).Select(_ => AppSettings.RollFlashImageCount(true, 9, 3, rng)).ToList();
        Assert.All(rolls, n => Assert.InRange(n, 3, 9));
    }

    [Fact]
    public void Equal_ends_always_give_that_count()
        => Assert.Equal(4, AppSettings.RollFlashImageCount(true, 4, 4, new Random(3)));

    [Fact]
    public void Defaults_are_off_with_a_floor_of_one()
    {
        var s = new AppSettings();
        Assert.False(s.SimultaneousImagesRandom);
        Assert.Equal(1, s.SimultaneousImagesMin);
        s.SimultaneousImagesMin = 40;
        Assert.Equal(20, s.SimultaneousImagesMin);
    }
}
