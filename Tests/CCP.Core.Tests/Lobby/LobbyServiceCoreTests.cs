using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.Friends;
using ConditioningControlPanel.Services.GoonGame;
using ConditioningControlPanel.Services.Lobby;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>The Core half of the Lobby the Avalonia head leans on: the directory parse, the lease
/// rule (polls only while watched), the back-off and the signed-out round.</summary>
public class LobbyServiceCoreTests
{
    private sealed class FakeTimer : IUiTimer
    {
        public TimeSpan Interval { get; set; }
        public bool Running { get; private set; }
        public event EventHandler? Tick;
        public void Start() => Running = true;
        public void Stop() => Running = false;
        public void OnUiThread(Action action) => action();
        public void Fire() => Tick?.Invoke(this, EventArgs.Empty);
    }

    private static LobbyService Service(FakeTimer timer, bool signedIn = true, bool chessOk = true, bool remoteOk = true)
    {
        var s = new LobbyService(() => timer)
        {
            SignedIn = () => signedIn,
            ReadFriends = () => Array.Empty<Friend>(),
        };
        s.FetchChess = _ => Task.FromResult(chessOk
            ? new PbpLobbyReply(new[] { new PbpOpenSeat("p_1", "ann", 600000, 0, 0) }, Array.Empty<PbpPlayingGame>(), true)
            : PbpLobbyReply.Empty);
        s.FetchGoon = () => Task.FromResult(OpenTablesReply.Empty);
        s.FetchRemote = () => Task.FromResult<(IReadOnlyList<RemoteSeat>, bool)>((Array.Empty<RemoteSeat>(), remoteOk));
        return s;
    }

    [Fact]
    public async Task Polls_only_while_a_lease_is_held()
    {
        var timer = new FakeTimer();
        using var s = Service(timer);
        Assert.False(timer.Running);
        var a = s.Watch();
        var b = s.Watch();
        Assert.True(timer.Running);
        Assert.Equal(LobbyPollPlan.Base, timer.Interval);
        await s.RefreshAsync();
        Assert.Equal(1, s.Snapshot.OpenCount);
        a.Dispose();
        a.Dispose(); // a lease released twice counts once
        Assert.True(timer.Running);
        b.Dispose();
        Assert.False(timer.Running);
        Assert.False(s.IsWatched);
    }

    [Fact]
    public async Task A_failed_round_doubles_the_wait_and_a_good_one_snaps_back()
    {
        var timer = new FakeTimer();
        using var s = Service(timer, chessOk: false, remoteOk: false);
        using var lease = s.Watch();
        await s.RefreshAsync();
        Assert.False(s.LastRoundOk);
        Assert.True(timer.Interval > LobbyPollPlan.Base, $"a failed round backs off (saw {timer.Interval})");
        s.FetchRemote = () => Task.FromResult<(IReadOnlyList<RemoteSeat>, bool)>((Array.Empty<RemoteSeat>(), true));
        await s.RefreshAsync();
        Assert.True(s.LastRoundOk);
        Assert.Equal(LobbyPollPlan.Base, timer.Interval);
    }

    [Fact]
    public async Task Signed_out_asks_nobody()
    {
        var timer = new FakeTimer();
        using var s = Service(timer, signedIn: false);
        bool asked = false;
        s.FetchChess = _ => { asked = true; return Task.FromResult(PbpLobbyReply.Empty); };
        await s.RefreshAsync();
        Assert.False(asked);
        Assert.False(s.Snapshot.SignedIn);
    }

    [Fact]
    public async Task Unseeded_wires_send_nothing_and_read_empty()
    {
        var chess = await new PbpLobbyApi(identity: () => ("u", "t"), baseUrl: "").FetchAsync();
        Assert.False(chess.Ok);
        var goon = await new GoonOpenTablesApi(identity: () => null, baseUrl: "http://127.0.0.1:1").FetchAsync();
        Assert.Same(OpenTablesReply.Empty, goon);
        var remote = await new RemoteDirectoryApi(identity: () => null, baseUrl: "http://127.0.0.1:1").FetchAsync();
        Assert.False(remote.Ok);
    }

    [Fact]
    public void Directory_parse_keeps_the_wpf_defaults_and_drops_idless_rows()
    {
        var json = "{\"entries\":[{\"unified_id\":\"u1\",\"display_name\":\"ann\",\"level\":7,\"tier\":\"full\",\"tags\":[\"a\",\"\",\"b\"],\"claimed\":true}," +
                   "{\"unified_id\":\"u2\"},{\"display_name\":\"noid\"}]}";
        var (seats, ok) = RemoteDirectoryApi.Parse(json);
        Assert.True(ok);
        Assert.Equal(2, seats.Count);
        Assert.Equal(new[] { "a", "b" }, seats[0].Tags);
        Assert.True(seats[0].Claimed);
        Assert.Equal("full", seats[0].Tier);
        Assert.Equal("Anonymous", seats[1].Name);
        Assert.Equal(1, seats[1].Level);
        Assert.Equal("light", seats[1].Tier);
        Assert.False(RemoteDirectoryApi.Parse("<html/>").Ok);
    }
}
