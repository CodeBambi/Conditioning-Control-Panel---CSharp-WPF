using System;
using System.Linq;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.BackRoom;
using Newtonsoft.Json.Linq;
using Xunit;
using static ConditioningControlPanel.Tests.BackRoomBridgeTests;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Back Room protocol 1, the lifecycle half: the close timings (300 ms page budget, 800 ms force),
/// the cursor backstop, silent SP adoption, the day-log hook and the null fx/media objects the host
/// runs on until C3 and C4 land. Same manual clock and fake relay as <see cref="BackRoomBridgeTests"/>.
/// </summary>
public class BackRoomBridgeLifecycleTests
{
    [Fact]
    public void Close_PostsClose_AndForceClosesAt800ms()
    {
        var rig = new Rig();
        rig.Bridge.RequestClose("panic");
        var c = Assert.Single(rig.Of("close"));
        Assert.Equal("panic", (string?)c["reason"]);
        Assert.Equal(0, rig.Closed);

        Assert.Contains(rig.Clock.Timers, t => t.At == TimeSpan.FromMilliseconds(800));
        Assert.DoesNotContain(rig.Clock.Timers, t => t.At > TimeSpan.FromMilliseconds(800) && t.At < TimeSpan.FromSeconds(5));
        rig.Clock.Fire(TimeSpan.FromMilliseconds(800));
        Assert.Equal(1, rig.Closed);
        Assert.Equal(300, BackRoomBridge.PageSettleMs);
    }

    [Fact]
    public void ExitDone_ClosesImmediately_AndOnlyOnce()
    {
        var rig = new Rig();
        rig.Bridge.RequestClose("app-exit");
        rig.Send("{\"type\":\"exit-done\"}");
        Assert.Equal(1, rig.Closed);
        Assert.True(rig.Clock.Timers.Single().Cancelled);
        rig.Clock.Fire(TimeSpan.FromMilliseconds(800));
        rig.Send("{\"type\":\"exit-done\"}");
        Assert.Equal(1, rig.Closed);
    }

    [Fact]
    public void PageExit_ArmsTheSameWatchdog()
    {
        var rig = new Rig();
        rig.Send("{\"type\":\"exit\",\"reason\":\"key\"}");
        Assert.Equal(0, rig.Closed);
        rig.Clock.Fire(TimeSpan.FromMilliseconds(800));
        Assert.Equal(1, rig.Closed);
    }

    [Fact]
    public void Exit_FlushesTheReportedCursor_ToTheCursorRoute()
    {
        var rig = new Rig();
        rig.Send("{\"type\":\"exit\",\"reason\":\"back\",\"station\":\"slot\",\"cursor\":{\"tapeId\":\"t_1\",\"played\":7}}");
        var call = Assert.Single(rig.Relay.Calls);
        Assert.Equal(("slot", "cursor"), (call.Station, call.Op));
        Assert.Equal(7, (int)call.Body!["played"]!);
        rig.Send("{\"type\":\"station-close\",\"station\":\"slot\",\"cursor\":{\"tapeId\":\"t_1\",\"played\":7}}");
        Assert.Single(rig.Relay.Calls);   // already flushed: not sent twice
    }

    [Fact]
    public async Task AdoptedSp_IsNotPushedBack_ButAnOutsideChangeIs()
    {
        var rig = new Rig();
        rig.Bridge.AdoptSp(51);
        Assert.Equal(51, rig.Sp);
        Assert.Empty(rig.Of("balance"));

        rig.Bridge.OnSpChanged(60, "earn");
        var b = Assert.Single(await rig.SettleAsync("balance", 1));
        Assert.Equal((60, "earn"), ((int)b["sp"]!, (string?)b["why"]));
    }

    [Fact]
    public void StationOpen_NotesTheDayLogEvent_ForValidIdsOnly()
    {
        var rig = new Rig();
        rig.Send("{\"type\":\"station-open\",\"station\":\"slot\"}");
        rig.Send("{\"type\":\"station-open\",\"station\":\"../x\"}");
        Assert.Equal(new[] { "e_backroom_slot" }, rig.Events);
        Assert.Contains("e_backroom_slot", ConditioningControlPanel.Models.FeatureDayEntry.EventKeys);
    }

    [Fact]
    public void Media_DealsFallbackPresets_AndFxAcksUnknown_OnTheNullObjects()
    {
        var rig = new Rig();
        rig.Send("{\"type\":\"media-request\",\"reqId\":\"" + Req + "\",\"station\":\"slot\"}");
        var media = Assert.Single(rig.Of("media"));
        Assert.Equal(42, (int)media["seed"]!);
        Assert.Equal(4, ((JArray)media["gifs"]!).Count);
        Assert.Equal(new[] { "Drop", "Relax", "Let Go", "Sink" }, ((JArray)media["words"]!).Select(w => (string)w["text"]!));
        Assert.All((JArray)media["gifs"]!, g => Assert.StartsWith("https://ccp.game/", (string)g["url"]!));

        rig.Send("{\"type\":\"fx\",\"token\":\"k1\",\"fxId\":\"fx.gif_storm\",\"station\":\"slot\",\"symbols\":[\"gif1\"]}");
        var ack = Assert.Single(rig.Of("fx-ack"));
        Assert.Equal("k1", (string?)ack["token"]);
        Assert.Empty((JArray)ack["fired"]!);
        var skip = Assert.Single((JArray)ack["skipped"]!);
        Assert.Equal(("fx.gif_storm", "unknown"), ((string)skip["prim"]!, (string)skip["why"]!));
    }

    [Fact]
    public void Suspend_IsForwardedWithItsReason()
    {
        var rig = new Rig();
        rig.Bridge.Suspend(true, "minimise");
        var s = Assert.Single(rig.Of("suspend"));
        Assert.Equal((true, "minimise"), ((bool)s["on"]!, (string?)s["reason"]));
    }
}
