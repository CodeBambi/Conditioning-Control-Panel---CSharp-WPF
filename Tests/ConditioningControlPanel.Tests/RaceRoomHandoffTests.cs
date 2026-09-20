using System;
using ConditioningControlPanel;
using ConditioningControlPanel.Services.BackRoom;
using Xunit;

namespace ConditioningControlPanel.Tests;

public class RaceRoomHandoffTests
{
    [Theory]
    [InlineData("https://ccp.game/backroom/index.html", "https://ccp.game/dtrh/race.html", false)]
    [InlineData("https://ccp.game/backroom/index.html?raceReturn=1", "https://ccp.game/backroom/index.html", true)]
    [InlineData("https://evil.test/dtrh/race.html", "https://ccp.game/dtrh/race.html", false)]
    [InlineData("http://ccp.game/dtrh/race.html", "https://ccp.game/dtrh/race.html", false)]
    [InlineData("https://ccp.game/dtrh/race.html", "https://ccp.game/dtrh/race.html", true)]
    public void HandoffRejectsMessagesFromTheOldDocument(string source, string target, bool expected)
        => Assert.Equal(expected, ChaosWebViewHost.SameDocument(source, target));

    [Fact]
    public void RaceTransferCancelsEffectsAndWaitsForPageCleanup()
    {
        var rig = new BackRoomBridgeTests.Rig();
        rig.Bridge.RequestClose("race");
        Assert.Equal("race", (string?)Assert.Single(rig.Of("close"))["reason"]);
        Assert.True(rig.Fx.Cancelled > 0);
        Assert.Equal(0, rig.Closed);
        rig.Send("{\"type\":\"exit\",\"station\":\"slot\",\"cursor\":{\"tapeId\":\"tape-test\",\"played\":3}}");
        Assert.Contains(rig.Relay.Calls, c => c.Op == "cursor" && (int?)c.Body?["played"] == 3);
        rig.Send("{\"type\":\"exit-done\"}");
        rig.Send("{\"type\":\"exit-done\"}");
        rig.Clock.Fire(TimeSpan.FromMilliseconds(BackRoomBridge.ForceCloseMs));
        Assert.Equal(1, rig.Closed);
    }

    [Fact]
    public void RaceTransferStillFinishesIfPageStopsResponding()
    {
        var rig = new BackRoomBridgeTests.Rig();
        rig.Bridge.RequestClose("race");
        rig.Clock.Fire(TimeSpan.FromMilliseconds(BackRoomBridge.ForceCloseMs));
        Assert.Equal(1, rig.Closed);
        Assert.True(rig.Fx.Cancelled > 0);
    }
}
