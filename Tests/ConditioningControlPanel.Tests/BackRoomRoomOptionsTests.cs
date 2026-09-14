using System.Linq;
using System.Collections.Generic;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.BackRoom;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// THE BACK ROOM's own Options (CONTRACT 10.14): the tunnel vision and melt switches default ON for new and
/// existing settings files and keep a player's off, <c>room-option</c> accepts only its three shapes, and a
/// valid one lands in the settings.
/// </summary>
public class BackRoomRoomOptionsTests
{
    [Fact]
    public void Switches_DefaultOn_ForANewProfile()
    {
        var s = new AppSettings();
        Assert.True(s.BackRoomTunnel);
        Assert.True(s.BackRoomMelt);
    }

    [Fact]
    public void NoSettings_ShutsEveryGate_TheTunnelToo()
    {
        // FxGates.Tunnel defaults to true, so the no-settings fallback must name it.
        foreach (var p in typeof(FxGates).GetProperties().Where(p => p.PropertyType == typeof(bool)))
            Assert.False((bool)p.GetValue(FxGates.AllOff)!, p.Name);
    }

    [Fact]
    public void Switches_ReadOn_FromAFileWrittenBeforeTheyExisted_AndKeepAPlayersOff()
    {
        // An existing user's file has no key: the default applies.
        var old = JsonConvert.DeserializeObject<AppSettings>("""{"BrainDrainEnabled":false,"BrainDrainMeltEnabled":false}""")!;
        Assert.True(old.BackRoomTunnel);
        Assert.True(old.BackRoomMelt);

        var chosen = new AppSettings { BackRoomTunnel = false, BackRoomMelt = false };
        var back = JsonConvert.DeserializeObject<AppSettings>(JsonConvert.SerializeObject(chosen))!;
        Assert.False(back.BackRoomTunnel);
        Assert.False(back.BackRoomMelt);
    }

    [Theory]
    [InlineData("""{"key":"tunnel","value":false}""", "tunnel", false, null)]
    [InlineData("""{"key":"melt","value":true}""", "melt", true, null)]
    [InlineData("""{"key":"intensity","value":"calm"}""", "intensity", false, BackRoomFxIntensity.Calm)]
    [InlineData("""{"key":"intensity","value":"full"}""", "intensity", false, BackRoomFxIntensity.Full)]
    public void RoomOption_KnownShapes(string json, string key, bool on, BackRoomFxIntensity? intensity)
        => Assert.Equal(new BackRoomBridge.RoomOption(key, on, intensity), BackRoomBridge.ReadRoomOption(JObject.Parse(json)));

    [Theory]
    [InlineData("""{"key":"tunnel","value":"false"}""")]
    [InlineData("""{"key":"melt","value":0}""")]
    [InlineData("""{"key":"melt"}""")]
    [InlineData("""{"key":"intensity","value":"FULL"}""")]
    [InlineData("""{"key":"intensity","value":2}""")]
    [InlineData("""{"key":"BrainDrainEnabled","value":true}""")]
    [InlineData("""{"value":true}""")]
    public void RoomOption_AnythingElse_IsNull(string json)
        => Assert.Null(BackRoomBridge.ReadRoomOption(JObject.Parse(json)));

    [Fact]
    public void Bridge_ForwardsAValidOption_DropsTheRest_AndNothingOnceClosing()
    {
        var got = new List<BackRoomBridge.RoomOption>();
        var bridge = new BackRoomBridge(new BackRoomBridge.Deps
        {
            Post = _ => { },
            Relay = new BackRoomBridgeTests.Relay(),
            BuildInit = () => new { type = "init" },
            CloseWindow = () => { },
            Schedule = (_, _) => () => { },
            SetOption = got.Add,
        });
        bridge.Handle(JObject.Parse("""{"type":"room-option","key":"tunnel","value":false}"""));
        bridge.Handle(JObject.Parse("""{"type":"room-option","key":"tunnel","value":"no"}"""));
        Assert.Equal(new[] { new BackRoomBridge.RoomOption("tunnel", false, null) }, got);

        bridge.Handle(JObject.Parse("""{"type":"exit","reason":"back"}"""));
        bridge.Handle(JObject.Parse("""{"type":"room-option","key":"melt","value":false}"""));
        Assert.Single(got);
    }

    [Fact]
    public void ApplyRoomOption_WritesTheSetting()
    {
        var s = new AppSettings();
        BackRoomHostService.ApplyRoomOption(s, new BackRoomBridge.RoomOption("tunnel", false, null));
        BackRoomHostService.ApplyRoomOption(s, new BackRoomBridge.RoomOption("melt", false, null));
        BackRoomHostService.ApplyRoomOption(s, new BackRoomBridge.RoomOption("intensity", false, BackRoomFxIntensity.Full));
        Assert.False(s.BackRoomTunnel);
        Assert.False(s.BackRoomMelt);
        Assert.Equal(BackRoomFxIntensity.Full, s.BackRoomFxIntensity);
        Assert.Equal("full", BackRoomHostService.IntensityChoiceWire(s));
        Assert.Equal("normal", BackRoomHostService.IntensityChoiceWire(null));
    }
}
