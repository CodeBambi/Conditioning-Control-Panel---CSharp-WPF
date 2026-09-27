using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.BackRoom;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>Standalone Breakout keeps its own three levels (owner, 2026-09-27): its sliders never move the casino's.</summary>
public class BreakoutVolumeTests
{
    private static BackRoomBridge.RoomOption Option(string key, int value)
        => BackRoomBridge.ReadRoomOption(new JObject { ["type"] = "room-option", ["key"] = key, ["value"] = value })!;

    [Theory]
    [InlineData("subVolume")]
    [InlineData("sfxVolume")]
    [InlineData("musicVolume")]
    public void BreakoutSlider_WritesBreakoutLevel_AndLeavesTheRoomAlone(string key)
    {
        var s = new AppSettings();
        var room = (s.BackRoomSubVolume, s.BackRoomSfxVolume, s.BackRoomMusicVolume);
        BackRoomHostService.ApplyRoomOption(s, Option(key, 42), breakout: true);

        Assert.Equal(room, (s.BackRoomSubVolume, s.BackRoomSfxVolume, s.BackRoomMusicVolume));
        var written = key switch { "subVolume" => s.BreakoutSubVolume, "sfxVolume" => s.BreakoutSfxVolume, _ => s.BreakoutMusicVolume };
        Assert.Equal(42, written);
    }

    [Fact]
    public void RoomSlider_StillWritesTheRoom()
    {
        var s = new AppSettings();
        BackRoomHostService.ApplyRoomOption(s, Option("sfxVolume", 30));
        Assert.Equal(30, s.BackRoomSfxVolume);
        Assert.Equal(100, s.BreakoutSfxVolume);
    }

    [Fact]
    public void AudioWire_ReadsTheLevelsOfThePageThatIsOpen()
    {
        var s = new AppSettings { BackRoomSubVolume = 10, BackRoomSfxVolume = 20, BackRoomMusicVolume = 30,
            BreakoutSubVolume = 40, BreakoutSfxVolume = 50, BreakoutMusicVolume = 60 };
        var room = JObject.FromObject(BackRoomHostService.AudioWire(s));
        var game = JObject.FromObject(BackRoomHostService.AudioWire(s, breakout: true));

        Assert.Equal(new[] { .1, .2, .3 }, new[] { (double)room["sub"]!, (double)room["sfx"]!, (double)room["music"]! });
        Assert.Equal(new[] { .4, .5, .6 }, new[] { (double)game["sub"]!, (double)game["sfx"]!, (double)game["music"]! });
        Assert.Equal(40, BackRoomHostService.SubVolume(s, breakout: true));
    }

    [Fact]
    public void BreakoutLevels_PushASettingsFrame_AndDefaultToTheRoomsDefaults()
    {
        foreach (var name in new[] { nameof(AppSettings.BreakoutSubVolume), nameof(AppSettings.BreakoutSfxVolume), nameof(AppSettings.BreakoutMusicVolume) })
            Assert.Contains(name, BackRoomHostService.SettingsFrameProperties);
        var s = new AppSettings();
        Assert.Equal((100, 100, 15), (s.BreakoutSubVolume, s.BreakoutSfxVolume, s.BreakoutMusicVolume));
    }
}
