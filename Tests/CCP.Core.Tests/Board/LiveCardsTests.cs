using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Services.Billboard;
using ConditioningControlPanel.Services.Billboard.Providers;
using ConditioningControlPanel.Services.Lobby;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Core.Tests.Board;

/// <summary>WPF 7.1.5 BillboardProvidersTests "Live" section, verbatim against the Core twin.</summary>
public class LiveCardsTests
{
    private static string AppDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "CCP.Core", "CCP.Core.csproj")))
            dir = dir.Parent;
        Assert.True(dir != null, "could not locate the repo root from " + AppContext.BaseDirectory);
        return Path.Combine(dir!.FullName, "CCP.Core");
    }

    private static readonly Lazy<Dictionary<string, string>> English = new(() =>
        JObject.Parse(File.ReadAllText(Path.Combine(AppDir(), "Localization", "Languages", "en.json")))
            .Properties().ToDictionary(p => p.Name, p => (string?)p.Value ?? ""));

    private static string Loc(string key) => English.Value.TryGetValue(key, out var v) ? v : key;

    private static LobbyRow Open(LobbyGame game, string key, string host, bool friend = false) =>
        new() { Game = game, State = LobbyRowState.Open, Key = key, HostName = host, Friend = friend };

    private static LobbySnapshot Snap(IEnumerable<LobbyRow>? open = null, IEnumerable<LobbyRow>? playing = null,
        IEnumerable<LobbyRow>? friends = null, bool signedIn = true) =>
        new((open ?? Array.Empty<LobbyRow>()).ToList(), (playing ?? Array.Empty<LobbyRow>()).ToList(),
            (friends ?? Array.Empty<LobbyRow>()).ToList(), signedIn);

    [Fact]
    public void Live_signed_out_or_empty_says_nothing()
    {
        Assert.Null(LiveCards.Decide(null, Loc));
        Assert.Null(LiveCards.Decide(LobbySnapshot.Empty, Loc));
        Assert.Null(LiveCards.Decide(Snap(new[] { Open(LobbyGame.Chess, "p_1", "ann") }, signedIn: false), Loc));
        Assert.Null(LiveCards.Decide(Snap(), Loc));
    }

    [Fact]
    public void Live_a_friends_table_comes_first_and_joins_it()
    {
        var card = LiveCards.Decide(Snap(
            open: new[] { Open(LobbyGame.Goon, "ABCD", "stranger") },
            friends: new[] { Open(LobbyGame.Chess, "p_9", "ann", friend: true) }), Loc)!;

        Assert.Equal(LiveCards.CardJoinFriend, card.Id);
        Assert.Equal(BillboardCardKind.Live, card.Kind);
        Assert.Equal("ann is waiting", card.Title);
        Assert.Equal("Chess, and the second seat is yours", card.Line);
        Assert.Equal(BillboardActionKind.Callback, card.Action.Kind);
        Assert.Equal("join:chess:p_9", card.Action.Target);
        Assert.Equal("Join", card.Action.Label);
        Assert.Equal(CardArt.Tables, card.ArtKey);
    }

    [Fact]
    public void Live_one_open_table_joins_it()
    {
        var card = LiveCards.Decide(Snap(open: new[] { Open(LobbyGame.Goon, "ABCD", "bo") }), Loc)!;
        Assert.Equal(LiveCards.CardTables, card.Id);
        Assert.Equal("1 open table", card.Title);
        Assert.Equal("Goon Game, waiting for a second player", card.Line);
        Assert.Equal("join:goon:ABCD", card.Action.Target);
    }

    [Fact]
    public void Live_several_tables_open_the_lobby()
    {
        var card = LiveCards.Decide(Snap(open: new[]
        {
            Open(LobbyGame.Goon, "ABCD", "bo"),
            Open(LobbyGame.Chess, "p_1", "cy"),
            Open(LobbyGame.Chess, "p_2", "di"),
        }), Loc)!;
        Assert.Equal("3 open tables", card.Title);
        Assert.Equal("Chess and Goon Game, each waiting for a second player", card.Line);
        Assert.Equal(BillboardActionKind.Tab, card.Action.Kind);
        Assert.Equal("availablesubjects", card.Action.Target);
        Assert.Equal(3, ((IReadOnlyDictionary<string, int>)card.ArtData!)["count"]);
    }

    [Fact]
    public void Live_game_lists_read_as_a_sentence()
    {
        Assert.Equal("", CardText.JoinAnd(Loc, Array.Empty<string>()));
        Assert.Equal("a", CardText.JoinAnd(Loc, new[] { "a" }));
        Assert.Equal("a and b", CardText.JoinAnd(Loc, new[] { "a", "b" }));
        Assert.Equal("a, b and c", CardText.JoinAnd(Loc, new[] { "a", "b", "c" }));
    }

    [Fact]
    public void Live_a_remote_seat_goes_through_the_lobby_page()
    {
        var card = LiveCards.Decide(Snap(open: new[] { Open(LobbyGame.Remote, "u_1", "eve") }), Loc)!;
        Assert.Equal(BillboardActionKind.Tab, card.Action.Kind);
        Assert.Equal("availablesubjects", card.Action.Target);
    }

    [Fact]
    public void Live_a_friend_in_a_game_with_no_seat_points_at_the_lobby()
    {
        var playing = new LobbyRow { Game = LobbyGame.Chess, State = LobbyRowState.Playing, HostName = "ann", Friend = true };
        var card = LiveCards.Decide(Snap(friends: new[] { playing }), Loc)!;
        Assert.Equal(LiveCards.CardPlaying, card.Id);
        Assert.Equal("ann is in a game", card.Title);
        Assert.Equal("Chess, no seat open yet", card.Line);
        Assert.Equal(BillboardActionKind.Tab, card.Action.Kind);
    }

    [Theory]
    [InlineData("join:chess:p_1", true, LobbyGame.Chess, "p_1")]
    [InlineData("join:goon:AB:CD", true, LobbyGame.Goon, "AB:CD")]
    [InlineData("join:remote:u_1", false, LobbyGame.Chess, "")]
    [InlineData("join:chess:", false, LobbyGame.Chess, "")]
    [InlineData("join:", false, LobbyGame.Chess, "")]
    [InlineData("invites", false, LobbyGame.Chess, "")]
    [InlineData(null, false, LobbyGame.Chess, "")]
    public void Live_targets_read_back_only_what_the_provider_issued(string? target, bool ok, LobbyGame game, string key)
    {
        Assert.Equal(ok, LiveCards.TryParseTarget(target, out var g, out var k));
        Assert.Equal(key, k);
        if (ok) Assert.Equal(game, g);
    }

}
