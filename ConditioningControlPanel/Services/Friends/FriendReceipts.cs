using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.Friends;

/// <summary>Receipt states on the wire (CCP-Server <c>proxy/FRIENDS-RECEIPTS.md</c> v1, mirrored in
/// CONTRACT.md). Order sent &lt; arrived &lt; seen &lt; joined | declined | expired | accepted; the
/// last four are terminal and rank the same.</summary>
public static class ReceiptState
{
    public const string Sent = "sent";
    public const string Arrived = "arrived";
    public const string Seen = "seen";
    public const string Joined = "joined";
    public const string Declined = "declined";
    public const string Expired = "expired";
    public const string Accepted = "accepted";

    /// <summary>0 for anything unknown, so an unknown state never moves a row forward.</summary>
    public static int Rank(string? state) => state switch
    {
        Sent => 1,
        Arrived => 2,
        Seen => 3,
        Joined or Declined or Expired or Accepted => 4,
        _ => 0,
    };

    public static bool IsTerminal(string? state) => Rank(state) == 4;
}

/// <summary>Receipt kinds on the wire: four friends kinds and five leash kinds.</summary>
public static class ReceiptKind
{
    public const string Poke = "poke";
    public const string Invite = "invite";
    public const string Watch = "watch";
    public const string Request = "request";
    public const string LeashOffer = "leash_offer";
    public const string LeashPunish = "leash_punish";
    public const string LeashAssign = "leash_assign";
    public const string LeashReward = "leash_reward";
    public const string LeashTug = "leash_tug";

    public static bool IsLeash(string? kind) => kind != null && kind.StartsWith("leash_", StringComparison.Ordinal);
}

/// <summary>One sender receipt (RC) from the poll reply: something this account sent moved on.
/// <see cref="Id"/> is null for a friend request; <see cref="Ref"/> is the leash pid or aid.</summary>
public sealed record SenderReceipt(string? Id, string Kind, string To, string? ToName, string State, DateTime AtUtc, string? Ref);

/// <summary>One recipient report (RQ) for the poll body: an item (by id) reached seen, joined or
/// declined, or a friend request waiting for me (by <see cref="RequestFrom"/>) was seen.</summary>
public sealed record ReceiptReport(string? Id, string? RequestFrom, string State)
{
    public static ReceiptReport Item(string id, string state) => new(id, null, state);
    public static ReceiptReport Request(string fromUid) => new(null, fromUid, ReceiptState.Seen);

    /// <summary>The key the queue folds on: one entry per item or request.</summary>
    public string Key => Id != null ? "i:" + Id : "r:" + RequestFrom;

    public bool IsValid()
    {
        if ((Id == null) == (RequestFrom == null)) return false;
        if (Id != null)
            return FriendReceipts.IsItemId(Id)
                && State is ReceiptState.Seen or ReceiptState.Joined or ReceiptState.Declined;
        return !string.IsNullOrWhiteSpace(RequestFrom) && State == ReceiptState.Seen;
    }
}

/// <summary>The pure half of receipts: read the reply, write the body, compare states.</summary>
public static class FriendReceipts
{
    /// <summary>The server reads at most this many reports a poll and ignores the rest.</summary>
    public const int MaxReportsPerPoll = 50;

    public static bool IsItemId(string? s)
    {
        if (s == null || s.Length != 16) return false;
        foreach (var c in s)
            if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'))) return false;
        return true;
    }

    /// <summary>The reply's <c>receipts</c>, oldest first. Anything malformed is skipped, never thrown.</summary>
    public static IReadOnlyList<SenderReceipt> Parse(JToken? token)
    {
        var list = new List<SenderReceipt>();
        if (token is not JArray a) return list;
        foreach (var t in a)
        {
            if (t is not JObject o) continue;
            var kind = Str(o["kind"]);
            var to = Str(o["to"]);
            var state = Str(o["state"]);
            if (kind == null || to == null || ReceiptState.Rank(state) == 0) continue;
            var id = Str(o["id"]);
            if (id != null && !IsItemId(id)) continue;
            if (id == null && kind != ReceiptKind.Request) continue;
            list.Add(new SenderReceipt(id, kind, to, Str(o["to_name"]), state!, At(o["at"]), Str(o["ref"])));
        }
        return list;
    }

    /// <summary>The body's <c>receipts</c>: valid reports only, at most <see cref="MaxReportsPerPoll"/>.</summary>
    public static JArray ToWire(IEnumerable<ReceiptReport> reports)
    {
        var a = new JArray();
        foreach (var r in reports)
        {
            if (a.Count >= MaxReportsPerPoll) break;
            if (r == null || !r.IsValid()) continue;
            var o = new JObject { ["state"] = r.State };
            if (r.Id != null) o["id"] = r.Id;
            else o["request_from"] = r.RequestFrom;
            a.Add(o);
        }
        return a;
    }

    /// <summary>True when <paramref name="next"/> is further along than <paramref name="current"/>:
    /// a surface keeps the furthest state per item (one drain can carry arrived then seen).</summary>
    public static bool MovesForward(string? current, string next) =>
        ReceiptState.Rank(next) > ReceiptState.Rank(current);

    /// <summary>The server's ISO time, whether the reader kept it a string (the friends wire does)
    /// or already turned it into a date; now when it is missing or unreadable.</summary>
    private static DateTime At(JToken? t)
    {
        if (t is JValue { Type: JTokenType.Date, Value: DateTime d }) return d.ToUniversalTime();
        if (t is JValue { Type: JTokenType.Date, Value: DateTimeOffset dto }) return dto.UtcDateTime;
        return DateTime.TryParse(Str(t), CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed : DateTime.UtcNow;
    }

    private static string? Str(JToken? t) =>
        t != null && t.Type == JTokenType.String && ((string?)t) is { Length: > 0 } s ? s : null;
}
