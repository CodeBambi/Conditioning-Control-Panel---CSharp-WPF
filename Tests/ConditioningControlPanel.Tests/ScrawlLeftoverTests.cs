using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Super;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>Super Scrawl's leftover stamps (afterglow, ghost echo, melt) and the bouncing text speed curve.</summary>
public class ScrawlLeftoverTests
{
    private const double Life = ScrawlRules.StampLife;

    [Fact]
    public void Afterglow_is_brightest_at_landing_and_settles_to_a_breath()
    {
        double land = ScrawlRules.AfterglowStrength(0, Life, MotionLevel.Full);
        double later = ScrawlRules.AfterglowStrength(6, Life, MotionLevel.Full);
        Assert.True(land > later);
        Assert.InRange(later, 0.2, 0.5);
        Assert.Equal(0.35, ScrawlRules.AfterglowStrength(3, Life, MotionLevel.Off));   // still, steady
    }

    [Fact]
    public void The_ghost_echo_grows_and_fades_once_then_is_gone()
    {
        Assert.True(ScrawlRules.EchoAt(0.1, MotionLevel.Full, out double s1, out double a1));
        Assert.True(ScrawlRules.EchoAt(0.9, MotionLevel.Full, out double s2, out double a2));
        Assert.True(s2 > s1);
        Assert.True(a2 < a1);
        Assert.False(ScrawlRules.EchoAt(ScrawlRules.EchoLife, MotionLevel.Full, out _, out _));
        Assert.False(ScrawlRules.EchoAt(0.1, MotionLevel.Off, out _, out _));

        ScrawlRules.EchoAt(0.9, MotionLevel.Reduced, out double sr, out _);
        Assert.Equal(1 + (s2 - 1) / 2, sr, 9);
    }

    [Fact]
    public void Melt_waits_for_forty_percent_of_the_life_then_eases_to_full()
    {
        Assert.Equal(0, ScrawlRules.MeltAmount(Life * 0.39, Life, MotionLevel.Full));
        Assert.True(ScrawlRules.MeltAmount(Life * 0.7, Life, MotionLevel.Full) > 0);
        Assert.Equal(1, ScrawlRules.MeltAmount(Life, Life, MotionLevel.Full), 9);
        Assert.Equal(0, ScrawlRules.MeltAmount(Life * 0.9, Life, MotionLevel.Off));   // Off never melts
    }

    [Fact]
    public void A_drip_starts_after_its_delay_and_never_before_the_melt()
    {
        Assert.Equal(0, ScrawlRules.DripProgress(Life * 0.39, Life, 0, MotionLevel.Full));
        Assert.Equal(0, ScrawlRules.DripProgress(Life * 0.5, Life, 1, MotionLevel.Full));   // the latest drip is not out yet
        Assert.True(ScrawlRules.DripProgress(Life * 0.8, Life, 0, MotionLevel.Full) > 0);
        Assert.Equal(1, ScrawlRules.DripProgress(Life, Life, 0.5, MotionLevel.Full), 9);
        Assert.Equal(0, ScrawlRules.DripProgress(Life * 0.9, Life, 0, MotionLevel.Off));
    }

    [Theory]
    [InlineData(1, 0.1)]
    [InlineData(5, 0.5)]
    [InlineData(6, 0.8)]
    [InlineData(10, 2.0)]
    public void The_speed_slider_keeps_the_low_end_and_doubles_the_top(int slider, double expected)
        => Assert.Equal(expected, BouncingTextService.SpeedMultiplier(slider), 9);

    [Fact]
    public void The_speed_slider_is_clamped_and_never_slows_as_it_rises()
    {
        Assert.Equal(BouncingTextService.SpeedMultiplier(1), BouncingTextService.SpeedMultiplier(-4), 9);
        Assert.Equal(BouncingTextService.SpeedMultiplier(10), BouncingTextService.SpeedMultiplier(99), 9);
        for (int i = 1; i < 10; i++)
            Assert.True(BouncingTextService.SpeedMultiplier(i + 1) > BouncingTextService.SpeedMultiplier(i));
    }
}
