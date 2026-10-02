using System;
using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Services.Friends;
using ConditioningControlPanel.Services.GoonGame;
using ConditioningControlPanel.Services.Lobby;
using Xunit;

namespace ConditioningControlPanel.Tests;

public class LobbyMergeTests
{
    private const long Now = 1_000_000_000L;

    private static PbpLobbyReply Chess(IEnumerable<PbpOpenSeat>? open = null, IEnumerable<PbpPlayingGame>? playing = null) =>
        new((open ?? Array.Empty<PbpOpenSeat>()).ToList(), (playing ?? Array.Empty<PbpPlayingGame>()).ToList(), true);

    private static OpenTable Goon(string code, string name, bool friend = false, string? friendId = null, int waiting = 0) =>
        new(code, name, 3, null, friend, friendId, Song: true, CardSec: 45, Pictures: false, WaitingSec: waiting);

    private static OpenTablesReply GoonReply(params OpenTable[] t) => new(true, true, t, null);

    private static Friend F(string id, string name, PresenceActivity act = PresenceActivity.Panel, bool online = true) =>
        new(id, name, null, 0, online, new FriendPresence(act, null, DateTimeOffset.UtcNow), false);

    [Fact]
    public void Signed_out_is_empty_whatever_the_wires_say()
    {
        var snap = LobbyMerge.Build(Chess(new[] { new PbpOpenSeat("p_1", "ann", 600000, 0, Now - 5000) }),
            GoonReply(Goon("ABCD", "bo")), null, null, signedIn: false, Now);
        Assert.False(snap.SignedIn);
        Assert.True(snap.IsEmpty);
        Assert.Equal(0, snap.OpenCount);
    }

    [Fact]
    public void Open_tables_newest_first_across_games()
    {
        var snap = LobbyMerge.Build(
            Chess(new[] { new PbpOpenSeat("p_new", "new", 600000, 0, Now - 2_000), new PbpOpenSeat("p_old", "old", 300000, 3000, Now - 90_000) }),
            GoonReply(Goon("GOON1", "mid", waiting: 30)),
            new[] { new RemoteSeat("u_r", "rem", 4, "light", new[] { "trance" }, Claimed: false) },
            null, true, Now);
        Assert.Equal(new[] { "rem", "new", "mid", "old" }, snap.Open.Select(r => r.HostName).ToArray());
        Assert.All(snap.Open, r => Assert.True(r.CanJoin));
        Assert.Equal("u_r", snap.Open[0].Key);
        Assert.Equal("GOON1", snap.Open[2].Key);
        Assert.Equal("p_old", snap.Open[3].Key);
    }

    [Fact]
    public void Playing_rows_carry_no_join_and_no_watch()
    {
        var snap = LobbyMerge.Build(
            Chess(playing: new[] { new PbpPlayingGame("white", "black", 600000, 0, Now - 60_000, 12) }),
            null,
            new[] { new RemoteSeat("u_c", "taken", 2, "full", Array.Empty<string>(), Claimed: true) },
            null, true, Now);
        Assert.Empty(snap.Open);
        Assert.Equal(2, snap.Playing.Count);
        Assert.All(snap.Playing, r => { Assert.False(r.CanJoin); Assert.False(r.CanWatch); Assert.Null(r.Key); });
        Assert.Equal("black", snap.Playing.Single(r => r.Game == LobbyGame.Chess).OpponentName);
    }

    [Fact]
    public void Friend_rows_go_to_the_friends_list_only_open_first()
    {
        var friends = new[] { F("u_f1", "fran"), F("u_f2", "remy") };
        var snap = LobbyMerge.Build(
            Chess(playing: new[] { new PbpPlayingGame("fran", "stranger", 600000, 0, Now - 1000, 3) }),
            GoonReply(Goon("FRND", "gina", friend: true, friendId: "u_f3"), Goon("STRA", "stranger")),
            new[] { new RemoteSeat("u_f2", "remy", 9, "standard", Array.Empty<string>(), false) },
            friends, true, Now);
        Assert.Equal(new[] { "STRA" }, snap.Open.Select(r => r.Key).ToArray());
        Assert.Empty(snap.Playing);
        Assert.Equal(3, snap.Friends.Count);
        Assert.Equal(LobbyRowState.Open, snap.Friends[0].State);
        Assert.Equal(LobbyRowState.Open, snap.Friends[1].State);
        Assert.Equal(LobbyRowState.Playing, snap.Friends[2].State);
        // the launcher counts friends' open tables first, then the rest
        Assert.Equal(3, snap.OpenCount);
        Assert.True(snap.Joinable[0].Friend);
        Assert.False(snap.Joinable[2].Friend);
    }

    [Fact]
    public void A_friend_in_a_game_with_no_listed_table_shows_as_playing()
    {
        var friends = new[]
        {
            F("u_a", "alex", PresenceActivity.Chess),
            F("u_b", "bea", PresenceActivity.GoonHosting),
            F("u_c", "cy", PresenceActivity.Remote, online: false),
            F("u_d", "dee", PresenceActivity.Breakout),
        };
        var snap = LobbyMerge.Build(Chess(), GoonReply(Goon("BEA1", "bea", friend: true, friendId: "u_b")), null, friends, true, Now);
        // bea's table is listed (open, joinable); alex gets a presence row; cy is offline; dee is not in a lobby game
        Assert.Equal(2, snap.Friends.Count);
        Assert.Equal("bea", snap.Friends[0].HostName);
        Assert.True(snap.Friends[0].CanJoin);
        Assert.Equal("alex", snap.Friends[1].HostName);
        Assert.Equal(LobbyGame.Chess, snap.Friends[1].Game);
        Assert.False(snap.Friends[1].CanJoin);
    }

    [Fact]
    public void Lists_are_capped()
    {
        var many = Enumerable.Range(0, 60).Select(i => new PbpOpenSeat("p_" + i, "n" + i, 600000, 0, Now - i * 1000)).ToList();
        var snap = LobbyMerge.Build(Chess(many), null, null, null, true, Now);
        Assert.Equal(LobbyMerge.MaxPerList, snap.Open.Count);
    }

    [Theory]
    [InlineData(600000, 0, "10+0")]
    [InlineData(300000, 3000, "5+3")]
    [InlineData(900000, 10000, "15+10")]
    [InlineData(90000, 0, "1.5+0")]
    public void Clock_reads_like_the_chess_menu(int init, int inc, string expected) =>
        Assert.Equal(expected, LobbySummary.Clock(init, inc));

    [Fact]
    public void Summaries_per_game()
    {
        Func<string, string> loc = k => k switch
        {
            "lobby_moves" => "{0} moves",
            "lobby_goon_cards" => "cards {0}s",
            _ => k,
        };
        Assert.Equal("10+0", LobbySummary.For(new LobbyRow { Game = LobbyGame.Chess, State = LobbyRowState.Open, InitialMs = 600000 }, loc));
        Assert.Equal("5+3 · 12 moves", LobbySummary.For(new LobbyRow { Game = LobbyGame.Chess, State = LobbyRowState.Playing, InitialMs = 300000, IncrementMs = 3000, Moves = 12 }, loc));
        Assert.Equal("lobby_goon_song · cards 45s · lobby_goon_pictures",
            LobbySummary.For(new LobbyRow { Game = LobbyGame.Goon, Song = true, CardSec = 45, Pictures = true }, loc));
        Assert.Equal("lobby_remote_tier_full · a, b, c",
            LobbySummary.For(new LobbyRow { Game = LobbyGame.Remote, RemoteTier = "full", Tags = new[] { "a", "b", "c", "d" } }, loc));
        Assert.Equal("", LobbySummary.For(new LobbyRow { Game = LobbyGame.Goon }, loc));
    }

    [Fact]
    public void Gates_follow_each_games_existing_rule()
    {
        var free = LobbyGates.From(signedIn: true, goonHosting: false, remoteHosting: false);
        Assert.True(free.CanHost(LobbyGame.Chess));
        Assert.False(free.CanHost(LobbyGame.Goon));
        Assert.False(free.CanHost(LobbyGame.Remote));
        Assert.True(free.CanJoin(LobbyGame.Goon));
        Assert.True(free.CanJoin(LobbyGame.Remote));

        var patron = LobbyGates.From(true, true, true);
        Assert.True(patron.CanHost(LobbyGame.Goon));
        Assert.True(patron.CanHost(LobbyGame.Remote));

        var signedOut = LobbyGates.From(false, true, true);
        foreach (var g in new[] { LobbyGame.Chess, LobbyGame.Goon, LobbyGame.Remote })
        {
            Assert.False(signedOut.CanHost(g));
            Assert.False(signedOut.CanJoin(g));
        }
    }
}
