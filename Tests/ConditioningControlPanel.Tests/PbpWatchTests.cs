using System;
using System.Linq;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Friends;
using ConditioningControlPanel.Services.Lobby;
using ConditioningControlPanel.Services.PieceByPiece;
using ConditioningControlPanel.Views.Tabs;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>Chess spectating, desktop half: the lobby's watchable rows and the
/// "Let people watch my games" setting's round trip through the page frames.</summary>
public class PbpWatchTests
{
    private const long Now = 1_000_000_000L;
    private const string Mid = "m_0123456789abcdef";

    private static PbpLobbyReply Chess(params PbpPlayingGame[] playing) =>
        new(Array.Empty<PbpOpenSeat>(), playing.ToList(), true);

    [Fact]
    public void Parse_reads_match_id_watchable_and_watchers()
    {
        var json = "{\"players\":[],\"playing\":[" +
                   "{\"white\":\"w\",\"black\":\"b\",\"moves\":3,\"match_id\":\"" + Mid + "\",\"watchable\":true,\"watchers\":4}," +
                   "{\"white\":\"x\",\"black\":\"y\",\"moves\":1,\"watchable\":false,\"watchers\":2}]}";
        var r = PbpLobbyApi.Parse(json);
        Assert.Equal(2, r.Playing.Count);
        var a = r.Playing[0];
        Assert.True(a.Watchable);
        Assert.Equal(Mid, a.MatchId);
        Assert.Equal(4, a.Watchers);
        var b = r.Playing[1];
        Assert.False(b.Watchable);
        Assert.Null(b.MatchId);
        Assert.Equal(2, b.Watchers);
    }

    [Theory]
    // Watchable without an id, a malformed id, a string "true", or an id with watchable false.
    [InlineData("\"watchable\":true")]
    [InlineData("\"watchable\":true,\"match_id\":\"p_0123456789abcdef\"")]
    [InlineData("\"watchable\":true,\"match_id\":\"m_../../x\"")]
    [InlineData("\"watchable\":\"true\",\"match_id\":\"" + Mid + "\"")]
    [InlineData("\"watchable\":false,\"match_id\":\"" + Mid + "\"")]
    public void Parse_never_offers_watch_without_a_clean_watchable_id(string fields)
    {
        var r = PbpLobbyApi.Parse("{\"players\":[],\"playing\":[{\"white\":\"w\",\"black\":\"b\"," + fields + "}]}");
        var g = Assert.Single(r.Playing);
        Assert.False(g.Watchable);
        Assert.Null(g.MatchId);
    }

    [Fact]
    public void Parse_of_an_old_server_has_no_watch_fields()
    {
        var r = PbpLobbyApi.Parse("{\"players\":[],\"playing\":[{\"white\":\"w\",\"black\":\"b\",\"watchers\":-3}]}");
        var g = Assert.Single(r.Playing);
        Assert.False(g.Watchable);
        Assert.Equal(0, g.Watchers);
    }

    [Fact]
    public void Merge_gives_watchable_rows_a_key_and_watch_but_no_join()
    {
        var snap = LobbyMerge.Build(Chess(
                new PbpPlayingGame("w", "b", 600000, 0, Now - 60_000, 10, Mid, true, 3),
                new PbpPlayingGame("x", "y", 600000, 0, Now - 30_000, 2, null, false, 1)),
            null, null, null, true, Now);
        Assert.Equal(2, snap.Playing.Count);
        var watch = snap.Playing.Single(r => r.HostName == "w");
        Assert.True(watch.CanWatch);
        Assert.False(watch.CanJoin);
        Assert.Equal(Mid, watch.Key);
        Assert.Equal(3, watch.Watchers);
        var plain = snap.Playing.Single(r => r.HostName == "x");
        Assert.False(plain.CanWatch);
        Assert.Null(plain.Key);
        // A watchable match is never a joinable table: the launcher's "N open" is unchanged.
        Assert.Equal(0, snap.OpenCount);
    }

    [Fact]
    public void Merge_ignores_watchable_without_an_id()
    {
        var snap = LobbyMerge.Build(Chess(new PbpPlayingGame("w", "b", 0, 0, 0, 0, null, true, 0)), null, null, null, true, Now);
        Assert.False(Assert.Single(snap.Playing).CanWatch);
    }

    [Fact]
    public void A_friends_watchable_match_keeps_its_watch_button_in_the_friends_list()
    {
        var friend = new Friend("u_f", "w", null, 0, true, new FriendPresence(PresenceActivity.Chess, null, DateTimeOffset.UtcNow), false);
        var snap = LobbyMerge.Build(Chess(new PbpPlayingGame("w", "b", 0, 0, Now - 5_000, 1, Mid, true, 0)),
            null, null, new[] { friend }, true, Now);
        Assert.Empty(snap.Playing);
        var row = Assert.Single(snap.Friends);
        Assert.True(row.CanWatch);
        Assert.Equal(Mid, row.Key);
    }

    [Fact]
    public void Row_view_shows_watch_and_the_watcher_count()
    {
        var row = new LobbyRow
        {
            Game = LobbyGame.Chess, State = LobbyRowState.Playing, HostName = "w", OpponentName = "b",
            Key = Mid, CanWatch = true, Watchers = 2, Moves = 5, InitialMs = 600000,
        };
        var gates = LobbyGates.From(true, false, false);
        var v = new LobbyRowView(row, gates, k => k);
        Assert.True(v.HasButton);
        Assert.Equal("lobby_btn_watch", v.ButtonText);
        Assert.False(v.Locked);
        Assert.Contains("lobby_watchers", v.Summary);
        Assert.Equal("lobby_watchers", LobbySummary.For(row with { Moves = 0, InitialMs = 0 }, k => k));
        Assert.Equal("", LobbySummary.For(row with { Moves = 0, InitialMs = 0, Watchers = 0 }, k => k));
    }

    [Theory]
    [InlineData(Mid, true)]
    [InlineData("m_deadbeef", true)]
    [InlineData("m_", false)]
    [InlineData("m_xyz12345", false)]
    [InlineData("c_0123456789abcdef", false)]
    [InlineData(null, false)]
    public void Match_id_shape(string? id, bool ok) => Assert.Equal(ok, PbpWatchRules.IsMatchId(id));

    [Fact]
    public void Let_people_watch_defaults_on()
    {
        Assert.True(new AppSettings().PbpLetPeopleWatch);
        Assert.True(PbpWatchRules.LetPeopleWatch(null));
    }

    [Fact]
    public void Setting_round_trips_through_json_and_the_page_frame()
    {
        var s = new AppSettings();
        var frame = JObject.Parse("{\"type\":\"pbp:setting\",\"key\":\"letPeopleWatch\",\"value\":false}");
        Assert.True(PbpWatchRules.TryRead(frame, out var key, out var value));
        Assert.True(PbpWatchRules.Apply(s, key, value));
        Assert.False(s.PbpLetPeopleWatch);
        Assert.False(PbpWatchRules.LetPeopleWatch(s));
        // Same value again changes nothing (no save).
        Assert.False(PbpWatchRules.Apply(s, key, value));

        var json = Newtonsoft.Json.JsonConvert.SerializeObject(s);
        Assert.Contains("\"pbpLetPeopleWatch\":false", json);
        var back = Newtonsoft.Json.JsonConvert.DeserializeObject<AppSettings>(json)!;
        Assert.False(back.PbpLetPeopleWatch);

        var on = JObject.Parse("{\"type\":\"pbp:setting\",\"key\":\"letPeopleWatch\",\"value\":true}");
        Assert.True(PbpWatchRules.TryRead(on, out key, out value));
        Assert.True(PbpWatchRules.Apply(back, key, value));
        Assert.True(back.PbpLetPeopleWatch);
    }

    [Theory]
    [InlineData("{\"type\":\"pbp:setting\",\"key\":\"letPeopleWatch\",\"value\":\"false\"}")]
    [InlineData("{\"type\":\"pbp:setting\",\"key\":\"letPeopleWatch\"}")]
    [InlineData("{\"type\":\"pbp:setting\",\"key\":\"autoStartEngine\",\"value\":true}")]
    [InlineData("{\"type\":\"pbp:settings\",\"key\":\"letPeopleWatch\",\"value\":false}")]
    public void Setting_frame_refuses_anything_else(string json)
    {
        Assert.False(PbpWatchRules.TryRead(JObject.Parse(json), out _, out _));
        var s = new AppSettings();
        Assert.False(PbpWatchRules.Apply(s, "autoStartEngine", false));
        Assert.True(s.PbpLetPeopleWatch);
    }
}
