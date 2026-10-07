using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>ccp-bugs #1104: Brain Drain clips have their own volume on top of the master volume.</summary>
public class BrainDrainVolumeTests
{
    [Theory]
    [InlineData(100, 100, 1f)]
    [InlineData(50, 100, 0.5f)]
    [InlineData(100, 25, 0.25f)]
    [InlineData(50, 50, 0.25f)]
    [InlineData(80, 0, 0f)]
    [InlineData(0, 80, 0f)]
    [InlineData(150, 200, 1f)]
    [InlineData(-5, 50, 0f)]
    public void Effective_volume_is_master_times_own(int master, int own, float expected)
        => Assert.Equal(expected, BrainDrainService.EffectiveVolume(master, own), 3);

    [Fact]
    public void Default_is_full_so_old_installs_sound_the_same()
        => Assert.Equal(100, new AppSettings().BrainDrainVolume);

    [Fact]
    public void Setting_is_clamped()
    {
        var s = new AppSettings { BrainDrainVolume = 140 };
        Assert.Equal(100, s.BrainDrainVolume);
        s.BrainDrainVolume = -3;
        Assert.Equal(0, s.BrainDrainVolume);
    }
}
