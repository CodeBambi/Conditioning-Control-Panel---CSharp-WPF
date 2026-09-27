using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.BackRoom;
using ConditioningControlPanel.Services.Chaster;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// CONTRACT 10.24: Circe's tab books a slot melt or jackpot only off an outcome the SERVER dealt
/// and the host relayed. The page names which outcome landed; the line is the host's own copy.
/// A demo spin never touches the tape, so it can never reach here.
/// </summary>
public class BackRoomTabLedgerTests
{
    private static readonly DateTime T0 = new(2026, 9, 24, 12, 0, 0, DateTimeKind.Utc);

    private static JObject TapeBody(string id, int played, params string[] lines)
    {
        var outcomes = new JArray();
        for (var i = 0; i < lines.Length; i++) outcomes.Add(new JObject { ["i"] = i, ["kind"] = "paid", ["line"] = lines[i] });
        return new JObject { ["ok"] = true, ["tape"] = new JObject { ["id"] = id, ["played"] = played, ["outcomes"] = outcomes } };
    }

    [Fact]
    public void A_landed_outcome_reads_the_servers_line_once()
    {
        var l = new BackRoomTabLedger();
        l.Relayed("slot", TapeBody("t1", 0, "none", "melt", "emi3"));

        Assert.Equal("melt", l.Land("slot", "t1", false, 1, T0));
        Assert.Null(l.Land("slot", "t1", false, 1, T0.AddSeconds(3)));
        Assert.Equal("emi3", l.Land("slot", "t1", false, 2, T0.AddSeconds(6)));
    }

    [Fact]
    public void Nothing_the_host_did_not_relay_can_land()
    {
        var l = new BackRoomTabLedger();
        Assert.Null(l.Land("slot", "t1", false, 0, T0));
        l.Relayed("slot", TapeBody("t1", 0, "melt"));
        Assert.Null(l.Land("slot", "t2", false, 0, T0));
        Assert.Null(l.Land("slot", "t1", false, 5, T0));
        Assert.Null(l.Land("slot", "t1", false, -1, T0));
        Assert.Null(l.Land("slot", null, true, 0, T0));
        Assert.Null(l.Land("wheel", "t1", false, 0, T0));
    }

    [Fact]
    public void Another_station_or_a_tape_with_no_outcomes_is_not_written_down()
    {
        var l = new BackRoomTabLedger();
        l.Relayed("wheel", TapeBody("t1", 0, "melt"));
        l.Relayed("slot", JObject.Parse("{\"tape\":{\"id\":\"t2\",\"played\":1}}"));   // a freeze reply's stored-tape stub
        Assert.Null(l.Land("slot", "t1", false, 0, T0));
        Assert.Null(l.Land("slot", "t2", false, 0, T0));
    }

    [Fact]
    public void A_resumed_tape_never_books_what_the_server_already_counted_as_played()
    {
        var l = new BackRoomTabLedger();
        l.Relayed("slot", TapeBody("t1", 2, "melt", "melt", "melt"));
        Assert.Null(l.Land("slot", "t1", false, 0, T0));
        Assert.Null(l.Land("slot", "t1", false, 1, T0));
        Assert.Equal("melt", l.Land("slot", "t1", false, 2, T0));
    }

    [Fact]
    public void A_freeze_lands_by_side_index_and_a_new_freeze_replaces_it()
    {
        var l = new BackRoomTabLedger();
        var freeze = JObject.Parse("{\"tape\":{\"id\":\"t1\",\"played\":3},\"freeze\":{\"col\":1,\"outcomes\":[{\"line\":\"emi3\"},{\"line\":\"none\"}]}}");
        l.Relayed("slot", freeze);
        Assert.Equal("emi3", l.Land("slot", null, true, 0, T0));
        Assert.Null(l.Land("slot", null, true, 0, T0.AddSeconds(3)));
        l.Relayed("slot", freeze);
        Assert.Equal("emi3", l.Land("slot", null, true, 0, T0.AddSeconds(6)));
    }

    [Fact]
    public void Landings_are_rate_limited_per_second()
    {
        var l = new BackRoomTabLedger();
        l.Relayed("slot", TapeBody("t1", 0, "melt", "melt", "melt", "melt", "melt", "melt"));
        for (var i = 0; i < BackRoomTabLedger.MaxPerSecond; i++) Assert.Equal("melt", l.Land("slot", "t1", false, i, T0));
        Assert.Null(l.Land("slot", "t1", false, 4, T0.AddMilliseconds(500)));
        Assert.Equal("melt", l.Land("slot", "t1", false, 5, T0.AddSeconds(1)));
    }

    [Theory]
    [InlineData("melt", "melt")]
    [InlineData("emi3", CircesTab.JackpotEventId)]
    [InlineData("gif3same", null)]
    [InlineData("none", null)]
    [InlineData(null, null)]
    public void Only_the_melt_line_and_the_jackpot_line_touch_the_tab(string? line, string? row)
    {
        Assert.Equal(row, ChasterHooks.SlotLineRow(line));
    }

    [Fact]
    public async Task The_bridge_hands_over_the_relayed_line_for_a_landed_frame_and_ignores_a_page_that_names_its_own()
    {
        var relay = new BackRoomBridgeTests.Relay();
        var clock = new BackRoomBridgeTests.Clock();
        var landed = new List<string>();
        var posted = new List<JObject>();
        var bridge = new BackRoomBridge(new BackRoomBridge.Deps
        {
            Post = m => { lock (posted) posted.Add(JObject.FromObject(m)); },
            Relay = relay,
            BuildInit = () => new { type = "init" },
            CloseWindow = () => { },
            Schedule = clock.Schedule,
            SlotLanded = landed.Add,
            UtcNow = () => T0,
        });

        bridge.Handle(JObject.Parse("{\"type\":\"station-request\",\"reqId\":\"" + BackRoomBridgeTests.Req + "\",\"station\":\"slot\",\"op\":\"tape\",\"idem\":\"idem-0123456789abcdef\",\"body\":{\"count\":2}}"));
        relay.Next.SetResult(new BackRoomStationResult(true, 200, null, TapeBody("t1", 0, "melt", "none")));
        for (var i = 0; i < 200; i++) { lock (posted) if (posted.Count > 0) break; await Task.Delay(5); }

        // A page claiming a jackpot on a blank row still gets the blank: the line is never read off the frame.
        bridge.Handle(JObject.Parse("{\"type\":\"landed\",\"station\":\"slot\",\"tapeId\":\"t1\",\"i\":1,\"line\":\"emi3\"}"));
        bridge.Handle(JObject.Parse("{\"type\":\"landed\",\"station\":\"slot\",\"tapeId\":\"t1\",\"i\":0}"));
        bridge.Handle(JObject.Parse("{\"type\":\"landed\",\"station\":\"slot\",\"tapeId\":\"t1\",\"i\":0}"));
        bridge.Handle(JObject.Parse("{\"type\":\"landed\",\"station\":\"slot\",\"tapeId\":\"t1\",\"i\":\"0\"}"));

        Assert.Equal(new[] { "none", "melt" }, landed);
    }
}
