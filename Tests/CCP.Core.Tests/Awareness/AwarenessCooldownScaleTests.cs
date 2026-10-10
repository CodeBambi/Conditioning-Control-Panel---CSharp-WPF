using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Awareness;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// ccp-bugs #640: the Awareness cooldowns reach an hour. The sliders walk a ladder of stops
/// so the low end stays usable, and the settings keep plain seconds.
/// </summary>
public class AwarenessCooldownScaleTests
{
    [Fact]
    public void Ladder_runs_from_one_second_to_an_hour_in_rising_order()
    {
        var stops = AwarenessCooldownScale.Stops;
        Assert.Equal(1, stops[0]);
        Assert.Equal(3600, stops[^1]);
        for (int i = 1; i < stops.Count; i++) Assert.True(stops[i] > stops[i - 1]);
    }

    [Fact]
    public void Xaml_slider_maximum_matches_the_ladder()
    {
        // AwarenessTabView.xaml hard-codes Maximum="73" for both sliders.
        Assert.Equal(73, AwarenessCooldownScale.MaxIndex);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(15)]
    [InlineData(30)]
    [InlineData(120)]
    [InlineData(600)]
    [InlineData(3600)]
    public void Every_stop_round_trips(int seconds)
    {
        Assert.Equal(seconds, AwarenessCooldownScale.SecondsAt(AwarenessCooldownScale.IndexFor(seconds)));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(32, 30)]
    [InlineData(33, 35)]
    [InlineData(9999, 3600)]
    public void Off_ladder_values_land_on_the_nearest_stop(int seconds, int expected)
    {
        Assert.Equal(expected, AwarenessCooldownScale.SecondsAt(AwarenessCooldownScale.IndexFor(seconds)));
    }

    [Theory]
    [InlineData(-3, 0)]
    [InlineData(500, 73)]
    public void Slider_positions_outside_the_range_are_clamped(double index, int expectedIndex)
    {
        Assert.Equal(AwarenessCooldownScale.Stops[expectedIndex], AwarenessCooldownScale.SecondsAt(index));
    }

    [Theory]
    [InlineData(1, "1s")]
    [InlineData(59, "59s")]
    [InlineData(60, "1:00")]
    [InlineData(90, "1:30")]
    [InlineData(3600, "60:00")]
    public void Label_switches_to_minutes_from_one_minute(int seconds, string expected)
    {
        Assert.Equal(expected, AwarenessCooldownScale.Format(seconds));
    }

    [Fact]
    public void Settings_accept_an_hour_on_both_cooldowns()
    {
        var s = new AppSettings
        {
            KeywordGlobalCooldownSeconds = 3600,
            KeywordPerKeywordCooldownSeconds = 3600,
        };
        Assert.Equal(3600, s.KeywordGlobalCooldownSeconds);
        Assert.Equal(3600, s.KeywordPerKeywordCooldownSeconds);

        s.KeywordGlobalCooldownSeconds = 5000;
        Assert.Equal(3600, s.KeywordGlobalCooldownSeconds);
    }
}
