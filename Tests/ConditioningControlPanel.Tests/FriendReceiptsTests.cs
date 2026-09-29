using System;
using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Services.Friends;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>The pure half of receipts (CCP-Server FRIENDS-RECEIPTS.md v1): what the reply is read
/// as, what the body carries, and which receipts become feed lines.</summary>
public class FriendReceiptsTests
{
    private const string Id1 = "0123456789abcdef";
    private const string Id2 = "fedcba9876543210";

    [Fact]
    public void StatesRankInWireOrder_TerminalsRankTheSame()
    {
        Assert.True(ReceiptState.Rank(ReceiptState.Sent) < ReceiptState.Rank(ReceiptState.Arrived));
        Assert.True(ReceiptState.Rank(ReceiptState.Arrived) < ReceiptState.Rank(ReceiptState.Seen));
        Assert.True(ReceiptState.Rank(ReceiptState.Seen) < ReceiptState.Rank(ReceiptState.Joined));
        Assert.Equal(ReceiptState.Rank(ReceiptState.Joined), ReceiptState.Rank(ReceiptState.Expired));
        Assert.Equal(0, ReceiptState.Rank("bogus"));
        Assert.False(FriendReceipts.MovesForward(ReceiptState.Seen, ReceiptState.Arrived));
        Assert.True(FriendReceipts.MovesForward(null, ReceiptState.Arrived));
    }

    [Fact]
    public void Parse_KeepsGoodRows_SkipsMalformedOnes()
    {
        var a = JArray.Parse($$"""
        [
          { "id": "{{Id1}}", "kind": "invite", "to": "u_a", "to_name": "Sam", "state": "seen", "at": "2026-09-29T10:00:00Z" },
          { "id": null, "kind": "request", "to": "u_b", "to_name": null, "state": "accepted", "at": "2026-09-29T10:01:00Z" },
          { "id": "{{Id2}}", "kind": "leash_punish", "to": "u_c", "state": "arrived", "at": "2026-09-29T10:02:00Z", "ref": "p1" },
          { "id": "NOTHEX", "kind": "poke", "to": "u_d", "state": "seen" },
          { "id": null, "kind": "poke", "to": "u_d", "state": "seen" },
          { "id": "{{Id1}}", "kind": "poke", "to": "u_d", "state": "teleported" },
          "not an object"
        ]
        """);
        var rows = FriendReceipts.Parse(a);
        Assert.Equal(3, rows.Count);
        Assert.Equal(("invite", "u_a", "Sam", "seen"), (rows[0].Kind, rows[0].To, rows[0].ToName, rows[0].State));
        Assert.Equal(new DateTime(2026, 9, 29, 10, 0, 0, DateTimeKind.Utc), rows[0].AtUtc);
        Assert.Null(rows[1].Id);
        Assert.Equal("p1", rows[2].Ref);
        Assert.Empty(FriendReceipts.Parse(null));
        Assert.Empty(FriendReceipts.Parse(new JObject()));
    }

    [Fact]
    public void ToWire_SendsValidReportsOnly_AtMostFifty()
    {
        var reports = new List<ReceiptReport>
        {
            ReceiptReport.Item(Id1, ReceiptState.Seen),
            ReceiptReport.Request("u_b"),
            ReceiptReport.Item(Id2, ReceiptState.Arrived),      // arrived is the server's to set
            ReceiptReport.Item("short", ReceiptState.Seen),
            new ReceiptReport(null, "u_c", ReceiptState.Joined), // a request can only be seen
        };
        var wire = FriendReceipts.ToWire(reports);
        Assert.Equal(2, wire.Count);
        Assert.Equal(Id1, (string?)wire[0]!["id"]);
        Assert.Equal("u_b", (string?)wire[1]!["request_from"]);

        var many = Enumerable.Range(0, 80).Select(i => ReceiptReport.Item(i.ToString("x16"), ReceiptState.Seen));
        Assert.Equal(FriendReceipts.MaxReportsPerPoll, FriendReceipts.ToWire(many).Count);
    }

    [Theory]
    [InlineData("invite", "joined", FriendEventKind.InviteAnswered, "joined")]
    [InlineData("invite", "declined", FriendEventKind.InviteAnswered, "declined")]
    [InlineData("invite", "expired", FriendEventKind.InviteUnanswered, null)]
    [InlineData("poke", "seen", FriendEventKind.ItemSeen, "poke")]
    [InlineData("invite", "seen", FriendEventKind.ItemSeen, "invite")]
    public void FeedLine_TellsTheStepsWorthTelling(string kind, string state, FriendEventKind expected, string? detail)
    {
        var line = FriendsService.FeedLine(new SenderReceipt(Id1, kind, "u_a", "Sam", state, DateTime.UtcNow, null));
        Assert.NotNull(line);
        Assert.Equal(expected, line!.Kind);
        Assert.Equal(detail, line.Detail);
        Assert.Equal("u_a", line.FriendId);
    }

    [Fact]
    public void FeedLine_StaysQuietOnArrivedDeclinedRequestsAndLeash()
    {
        Assert.Null(FriendsService.FeedLine(new SenderReceipt(Id1, "poke", "u_a", null, "arrived", DateTime.UtcNow, null)));
        Assert.Null(FriendsService.FeedLine(new SenderReceipt(null, "request", "u_a", null, "declined", DateTime.UtcNow, null)));
        Assert.Null(FriendsService.FeedLine(new SenderReceipt(Id1, "leash_tug", "u_a", null, "seen", DateTime.UtcNow, null)));
        var accepted = FriendsService.FeedLine(new SenderReceipt(null, "request", "u_a", "Sam", "accepted", DateTime.UtcNow, null));
        Assert.Equal(FriendEventKind.RequestAccepted, accepted!.Kind);
    }
}
