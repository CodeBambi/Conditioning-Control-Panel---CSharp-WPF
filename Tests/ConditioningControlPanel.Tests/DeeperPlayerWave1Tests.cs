using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Deeper;
using Newtonsoft.Json;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Deeper player wave 1: the volume level must survive with no output device
/// (it used to be a no-op setter + a hard-coded 80 getter) and the persisted
/// setting clamps.
/// </summary>
public class DeeperPlayerWave1Tests
{
    [Fact]
    public void Volume_IsCachedBeforeAnyOutputExists()
    {
        using var player = new EnhancementAudioPlayer();
        Assert.Equal(EnhancementAudioPlayer.DefaultVolume, player.Volume);
        player.Volume = 35;
        Assert.Equal(35, player.Volume);
    }

    [Theory]
    [InlineData(-10, 0)]
    [InlineData(0, 0)]
    [InlineData(100, 100)]
    [InlineData(250, 100)]
    public void Volume_Clamps(int set, int expected)
    {
        using var player = new EnhancementAudioPlayer();
        player.Volume = set;
        Assert.Equal(expected, player.Volume);
        Assert.Equal(expected, EnhancementAudioPlayer.ClampVolume(set));
    }

    [Fact]
    public void Volume_SurvivesStop()
    {
        using var player = new EnhancementAudioPlayer();
        player.Volume = 42;
        player.Stop();
        Assert.Equal(42, player.Volume);
    }

    [Fact]
    public void Settings_DeeperPlayerVolume_DefaultsAndClamps()
    {
        var fresh = new AppSettings();
        Assert.Equal(80, fresh.DeeperPlayerVolume);

        fresh.DeeperPlayerVolume = 140;
        Assert.Equal(100, fresh.DeeperPlayerVolume);
        fresh.DeeperPlayerVolume = -5;
        Assert.Equal(0, fresh.DeeperPlayerVolume);

        var loaded = JsonConvert.DeserializeObject<AppSettings>("{\"DeeperPlayerVolume\": 55}")!;
        Assert.Equal(55, loaded.DeeperPlayerVolume);
    }
}
