using System;
using System.Collections.Generic;

namespace ConditioningControlPanel.Services.Friends;

/// <summary>The latest thing this account sent one friend, and how far it got: sent, arrived,
/// seen, then for an invite joined / declined / expired. <see cref="ItemId"/> is the server's
/// <c>item_id</c> from the send reply, null from a server that predates receipts (the trail then
/// stays at sent). <see cref="At"/> is when it last moved.</summary>
public sealed record SentTrail(string FriendId, SendKind Kind, string? ItemId, string? Detail, string State, DateTimeOffset At)
{
    /// <summary>Where the walk ends for this kind: a poke or a watch is done once seen, an invite
    /// waits for its answer.</summary>
    public bool Done => ReceiptState.IsTerminal(State) || (Kind != SendKind.Invite && State == ReceiptState.Seen);

    /// <summary>How many steps the walk has: sent, arrived, seen, and for an invite the answer.</summary>
    public int Steps => Kind == SendKind.Invite ? 4 : 3;

    /// <summary>How many of <see cref="Steps"/> are lit: 1 at sent, one more per step.</summary>
    public int Lit => Math.Clamp(ReceiptState.Rank(State), 1, Steps);
}

/// <summary>
/// The sender's side of receipts (FRIENDS-RECEIPTS v1): per friend, the latest poke, invite or
/// watch, walked forward by the receipts the poll hands back. Only the LATEST item per friend is
/// kept (a new send replaces it), a receipt for anything else is ignored, and a state only ever
/// moves forward. Pure: the service feeds it, the drawer reads it.
/// </summary>
public sealed class FriendsSentBook
{
    /// <summary>A trail stops showing this long after it last moved. Long enough to see an
    /// answer come back, short enough that a day-old poke does not sit under a name.</summary>
    public static readonly TimeSpan ShowFor = TimeSpan.FromMinutes(30);

    private readonly Dictionary<string, SentTrail> _latest = new(StringComparer.Ordinal);

    /// <summary>A send came back <c>sent</c>. Replaces whatever was there for that friend.</summary>
    public void Note(string friendId, SendKind kind, string? itemId, string? detail, DateTimeOffset at)
    {
        if (string.IsNullOrEmpty(friendId)) return;
        _latest[friendId] = new SentTrail(friendId, kind, FriendReceipts.IsItemId(itemId) ? itemId : null, detail,
            ReceiptState.Sent, at);
    }

    /// <summary>Walks the trails forward. Friends kinds only; a request receipt has no item and
    /// the leash kinds belong to the leash. Returns true when anything moved.</summary>
    public bool Apply(IReadOnlyList<SenderReceipt>? receipts)
    {
        if (receipts == null || receipts.Count == 0) return false;
        bool moved = false;
        foreach (var r in receipts)
        {
            if (r == null || r.Id == null || string.IsNullOrEmpty(r.To)) continue;
            if (ReceiptKind.IsLeash(r.Kind) || r.Kind == ReceiptKind.Request) continue;
            if (!_latest.TryGetValue(r.To, out var t) || t.ItemId != r.Id) continue;
            if (!FriendReceipts.MovesForward(t.State, r.State)) continue;
            if (!Fits(t.Kind, r.State)) continue;
            var at = r.AtUtc == default ? t.At : new DateTimeOffset(DateTime.SpecifyKind(r.AtUtc, DateTimeKind.Utc));
            _latest[r.To] = t with { State = r.State, At = at };
            moved = true;
        }
        return moved;
    }

    /// <summary>The trail to show under a friend's name now, or null (nothing sent, or it last
    /// moved more than <see cref="ShowFor"/> ago).</summary>
    public SentTrail? Latest(string friendId, DateTimeOffset now)
    {
        if (string.IsNullOrEmpty(friendId) || !_latest.TryGetValue(friendId, out var t)) return null;
        return now - t.At > ShowFor ? null : t;
    }

    public void Clear() => _latest.Clear();

    /// <summary>A poke or a watch cannot be joined, declined or expire: the server never says so,
    /// and a stray one must not paint an answer the kind cannot have.</summary>
    private static bool Fits(SendKind kind, string state) => kind == SendKind.Invite
        ? state is ReceiptState.Arrived or ReceiptState.Seen or ReceiptState.Joined or ReceiptState.Declined or ReceiptState.Expired
        : state is ReceiptState.Arrived or ReceiptState.Seen;
}
