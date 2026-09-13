using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.BackRoom;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The host half of Back Room protocol 1 (CONTRACT section 2), requests: one reply per reqId, the
/// relay whitelist and the refusal shapes. Everything runs on a manual clock and a fake relay, so
/// nothing here waits on real time or a network. The rig is shared with the lifecycle suite.
/// </summary>
public class BackRoomBridgeTests
{
    internal const string Req = "req-0123456789abcdef";

    internal sealed class Clock
    {
        public readonly List<(TimeSpan At, Action Fn, bool Cancelled)> Timers = new();
        public Action Schedule(TimeSpan at, Action fn)
        {
            int i = Timers.Count;
            Timers.Add((at, fn, false));
            return () => Timers[i] = (Timers[i].At, Timers[i].Fn, true);
        }
        public void Fire(TimeSpan at)
        {
            foreach (var t in Timers.ToList()) if (!t.Cancelled && t.At == at) t.Fn();
        }
    }

    internal sealed class Relay : IBackRoomRelay
    {
        public readonly List<(string Station, string Op, string? Idem, JObject? Body)> Calls = new();
        public TaskCompletionSource<BackRoomStationResult> Next = New();
        public static TaskCompletionSource<BackRoomStationResult> New() => new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<BackRoomStationResult> RelayAsync(string station, string op, string? idem, JObject? body, CancellationToken ct)
        {
            Calls.Add((station, op, idem, body));
            return op == "cursor" ? Task.FromResult(new BackRoomStationResult(true, 200, null, null)) : Next.Task;
        }
    }

    internal sealed class Rig
    {
        public readonly List<JObject> Posted = new();
        public readonly Clock Clock = new();
        public readonly Relay Relay = new();
        public readonly List<string> Events = new();
        public int Closed;
        public int Sp;
        public BackRoomBridge Bridge = null!;

        public Rig()
        {
            Bridge = new BackRoomBridge(new BackRoomBridge.Deps
            {
                Post = m => { lock (Posted) Posted.Add(JObject.FromObject(m)); },
                Relay = Relay,
                BuildInit = () => new { type = "init", protocol = 1 },
                CloseWindow = () => Closed++,
                Schedule = Clock.Schedule,
                NoteEvent = Events.Add,
                SetSp = v => { Sp = v; Bridge.OnSpChanged(v, "earn"); },
                NextSeed = () => 42,
            });
        }

        public void Send(string json) => Bridge.Handle(JObject.Parse(json));
        public List<JObject> Of(string type) { lock (Posted) return Posted.Where(p => (string?)p["type"] == type).ToList(); }

        public async Task<List<JObject>> SettleAsync(string type, int count)
        {
            for (int i = 0; i < 200 && Of(type).Count < count; i++) await Task.Delay(5);
            return Of(type);
        }
    }

    private static string StationRequest(string reqId = Req, string op = "tape", string station = "slot") =>
        JsonConvert.SerializeObject(new { type = "station-request", reqId, station, op, idem = "idem-0123456789abcdef", body = new { count = 10 } });

    [Fact]
    public void Ready_PostsInitExactlyOnce()
    {
        var rig = new Rig();
        rig.Bridge.OnReady();
        rig.Bridge.OnReady();
        Assert.Single(rig.Of("init"));
    }

    [Fact]
    public async Task StationRequest_RelaysAndRepliesOnce_EvenWhenTheReqIdIsRepeated()
    {
        var rig = new Rig();
        rig.Send(StationRequest());
        rig.Send(StationRequest());   // same reqId: never a second relay, never a second reply
        rig.Relay.Next.SetResult(new BackRoomStationResult(true, 200, null, JObject.Parse("{\"ok\":true,\"sp\":9}")));

        await rig.SettleAsync("station-result", 1);
        await Task.Delay(30);
        var r = Assert.Single(rig.Of("station-result"));
        Assert.Single(rig.Relay.Calls);
        Assert.Equal(Req, (string?)r["reqId"]);
        Assert.True((bool)r["ok"]!);
        Assert.Equal(200, (int)r["status"]!);
        Assert.Equal(9, (int)r["body"]!["sp"]!);
    }

    [Theory]
    [InlineData("slot", "withdraw")]
    [InlineData("wheel", "state")]
    [InlineData("SLOT", "state")]
    public async Task UnlistedOps_AreRefusedBadOp_WithoutARelay(string station, string op)
    {
        var rig = new Rig();
        rig.Send(StationRequest(op: op, station: station));
        var r = Assert.Single(await rig.SettleAsync("station-result", 1));
        Assert.Equal((false, 0, "bad_op"), ((bool)r["ok"]!, (int)r["status"]!, (string?)r["reason"]));
        Assert.Empty(rig.Relay.Calls);
    }

    [Fact]
    public async Task ShortReqId_IsRefusedBadOp()
    {
        var rig = new Rig();
        rig.Send(StationRequest(reqId: "short"));
        var r = Assert.Single(await rig.SettleAsync("station-result", 1));
        Assert.Equal("bad_op", (string?)r["reason"]);
        Assert.Equal("short", (string?)r["reqId"]);
    }

    [Fact]
    public async Task RequestAfterExit_IsRefusedClosed()
    {
        var rig = new Rig();
        rig.Send("{\"type\":\"exit\",\"reason\":\"back\"}");
        rig.Send(StationRequest());
        var r = Assert.Single(await rig.SettleAsync("station-result", 1));
        Assert.Equal((false, 0, "closed"), ((bool)r["ok"]!, (int)r["status"]!, (string?)r["reason"]));
        Assert.Empty(rig.Relay.Calls);
    }

    [Fact]
    public async Task HungRelay_GetsATimeoutFromTheGuard_AndTheLateAnswerIsDropped()
    {
        var rig = new Rig();
        rig.Send(StationRequest());
        rig.Clock.Fire(BackRoomBridge.ReplyGuard);
        rig.Relay.Next.SetResult(new BackRoomStationResult(true, 200, null, null));
        await Task.Delay(40);
        var r = Assert.Single(rig.Of("station-result"));
        Assert.Equal("timeout", (string?)r["reason"]);
        Assert.True(BackRoomBridge.ReplyGuard < TimeSpan.FromSeconds(6), "the page gives up at 6 s");
    }
}
