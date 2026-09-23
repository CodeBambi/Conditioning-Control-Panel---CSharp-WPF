using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ConditioningControlPanel.Controls.Friends;
using ConditioningControlPanel.Services.Friends;
using ConditioningControlPanel.Services.GoonGame;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>Open tables on the desktop: the /v2/goon/open parse, the join code grammar, the
/// friend pin, the shared cache's coalescing, and the drawer's pink hosting row.</summary>
[Collection(GoonOpenTablesCollection.Name)]
public class GoonOpenTablesTests
{
    private const string Body = """
    {"ok":true,"you":{"canHost":true,"canJoin":false},
     "tables":[
       {"code":"k7m-2qx","name":"Mia","level":33,"avatar":null,"friend":true,"friendId":"u_mia","song":true,"cardSec":60,"pictures":true,"waitingSec":42},
       {"code":"ZZ9QRT","name":"stranger","level":4,"friend":false,"friendId":"u_leak","song":false,"cardSec":90,"pictures":false,"waitingSec":5},
       {"code":"no","name":"bad code"},
       "junk"
     ],
     "lastOpenedAgoSec":120}
    """;

    [Fact]
    public void Parse_reads_the_wire_and_drops_rows_without_a_code()
    {
        var r = GoonOpenTablesApi.Parse(Body);
        Assert.True(r.CanHost);
        Assert.False(r.CanJoin);
        Assert.Equal(120, r.LastOpenedAgoSec);
        Assert.Equal(2, r.Tables.Count);
        var mia = r.Tables[0];
        Assert.Equal("K7M2QX", mia.Code);
        Assert.Equal("Mia", mia.Name);
        Assert.Equal(33, mia.Level);
        Assert.True(mia.Friend);
        Assert.Equal("u_mia", mia.FriendId);
        Assert.True(mia.Song);
        Assert.Equal(60, mia.CardSec);
        Assert.True(mia.Pictures);
        Assert.Equal(42, mia.WaitingSec);
        // A non-friend row never keeps an id, whatever the server sent.
        Assert.Null(r.Tables[1].FriendId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("<html>404</html>")]
    [InlineData("{\"ok\":false,\"reason\":\"signin\"}")]
    [InlineData("[]")]
    public void Anything_but_an_ok_object_is_empty(string? text)
    {
        var r = GoonOpenTablesApi.Parse(text);
        Assert.Same(OpenTablesReply.Empty, r);
        Assert.Equal(0, GoonOpenTables.OpenCount(r));
    }

    [Fact]
    public void Parse_caps_the_list()
    {
        var rows = string.Join(",", Enumerable.Range(0, 30).Select(i => $"{{\"code\":\"ABCD{i:00}\"}}"));
        var r = GoonOpenTablesApi.Parse("{\"ok\":true,\"tables\":[" + rows + "]}");
        Assert.Equal(GoonOpenTablesApi.MaxTables, r.Tables.Count);
    }

    [Theory]
    [InlineData("k7m-2qx", "K7M2QX")]
    [InlineData(" abcd ", "ABCD")]
    [InlineData("abc", null)]
    [InlineData("ABCDEFGHIJKLM", null)]
    [InlineData("AB$D", null)]
    [InlineData(null, null)]
    public void Join_code_matches_the_pages_normalizeCode(string? input, string? expected)
        => Assert.Equal(expected, GoonJoinCode.Normalize(input));

    [Fact]
    public void Friend_pin_is_by_id_and_a_name_match_is_only_a_fallback()
    {
        var byId = GoonOpenTablesApi.Parse(Body);
        Assert.NotNull(GoonOpenTables.ForFriend(byId, "u_mia", "someone else"));
        Assert.Null(GoonOpenTables.ForFriend(byId, "u_other", "Mia"));   // an id was sent: names never match
        Assert.Null(GoonOpenTables.ForFriend(byId, "u_leak", "stranger")); // not a friend row

        var noIds = GoonOpenTablesApi.Parse("""{"ok":true,"tables":[{"code":"AAAA1","name":"Mia","friend":true}]}""");
        Assert.NotNull(GoonOpenTables.ForFriend(noIds, "u_mia", "Mia"));

        var twoMias = GoonOpenTablesApi.Parse(
            """{"ok":true,"tables":[{"code":"AAAA1","name":"Mia","friend":true},{"code":"AAAA2","name":"Mia","friend":true}]}""");
        Assert.Null(GoonOpenTables.ForFriend(twoMias, "u_mia", "Mia"));
    }

    [Fact]
    public async Task Refresh_coalesces_inside_the_gap_and_raises_changed()
    {
        GoonOpenTables.ResetForTests();
        var now = new DateTime(2026, 9, 23, 12, 0, 0, DateTimeKind.Utc);
        int calls = 0;
        var oldFetch = GoonOpenTables.Fetch;
        var oldNow = GoonOpenTables.UtcNow;
        try
        {
            GoonOpenTables.UtcNow = () => now;
            GoonOpenTables.Fetch = _ => { calls++; return Task.FromResult(GoonOpenTablesApi.Parse(Body)); };
            OpenTablesReply? seen = null;
            GoonOpenTables.Changed += r => seen = r;

            var first = await GoonOpenTables.RefreshAsync(TestContext.Current.CancellationToken);
            Assert.Equal(1, calls);
            Assert.Equal(2, first.Tables.Count);
            Assert.Same(first, seen);
            Assert.Same(first, GoonOpenTables.Latest);

            await GoonOpenTables.RefreshAsync(TestContext.Current.CancellationToken);
            Assert.Equal(1, calls);

            now = now + GoonOpenTables.MinGap;
            await GoonOpenTables.RefreshAsync(TestContext.Current.CancellationToken);
            Assert.Equal(2, calls);

            GoonOpenTables.Fetch = _ => throw new InvalidOperationException("boom");
            now = now + GoonOpenTables.MinGap;
            var failed = await GoonOpenTables.RefreshAsync(TestContext.Current.CancellationToken);
            Assert.Same(OpenTablesReply.Empty, failed);
        }
        finally
        {
            GoonOpenTables.Fetch = oldFetch;
            GoonOpenTables.UtcNow = oldNow;
            GoonOpenTables.ResetForTests();
        }
    }

    [Fact]
    public void Hosting_friends_float_to_the_top_even_from_offline()
    {
        var now = DateTimeOffset.UtcNow;
        Friend F(string id, bool online) => new(id, id, null, 0, online,
            new FriendPresence(online ? PresenceActivity.Panel : PresenceActivity.Offline, null, now), false);
        IReadOnlyList<Friend> on = new[] { F("a", true), F("b", true) };
        IReadOnlyList<Friend> off = new[] { F("c", false), F("d", false) };

        var (o, f) = FriendsDrawerRules.HostingFirst(on, off, x => x.Id is "b" or "d");
        Assert.Equal(new[] { "b", "d", "a" }, o.Select(x => x.Id));
        Assert.Equal(new[] { "c" }, f.Select(x => x.Id));

        var (o2, f2) = FriendsDrawerRules.HostingFirst(on, off, _ => false);
        Assert.Same(on, o2);
        Assert.Same(off, f2);
    }
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class GoonOpenTablesCollection
{
    public const string Name = "GoonOpenTables statics";
}
