using System;
using System.Collections.Generic;
using System.Linq;

namespace ConditioningControlPanel.Services.Leash;

// SEEN RECEIPTS for the leash items (CONTRACT.md "Receipts", mirrored from FRIENDS-RECEIPTS.md in
// CC-Labs-llc/CCP-Server). The holder sends; the server says when the item reached the leashed
// side's client (`arrived`) and the leashed client says when it was on screen (`seen`); the leash's
// own events finish the story (done, accepted, declined, skipped, missed). A refused send never
// left. Pure: no WPF, no HTTP, no clock of its own.

/// <summary>The five leash items that carry receipts.</summary>
public enum LeashItemKind { Offer, Punish, Assign, Reward, Tug }

/// <summary>How far one sent item got. <see cref="Sent"/>, <see cref="Arrived"/>, <see cref="Seen"/>,
/// then one ending. A step never moves back and an ending never changes.</summary>
public enum LeashStep { Sent, Arrived, Seen, Done, Accepted, Declined, Skipped, Missed, Refused }

public static class LeashSteps
{
    /// <summary>0 sent, 1 arrived, 2 seen, 3 any ending.</summary>
    public static int Rank(LeashStep s) => s switch
    {
        LeashStep.Sent => 0,
        LeashStep.Arrived => 1,
        LeashStep.Seen => 2,
        _ => 3,
    };

    public static bool IsEnding(LeashStep s) => Rank(s) == 3;

    public static LeashItemKind? KindFromWire(string? s) => s switch
    {
        "leash_offer" => LeashItemKind.Offer,
        "leash_punish" => LeashItemKind.Punish,
        "leash_assign" => LeashItemKind.Assign,
        "leash_reward" => LeashItemKind.Reward,
        "leash_tug" => LeashItemKind.Tug,
        _ => null,
    };

    public static string KindToWire(LeashItemKind k) => k switch
    {
        LeashItemKind.Offer => "leash_offer",
        LeashItemKind.Punish => "leash_punish",
        LeashItemKind.Assign => "leash_assign",
        LeashItemKind.Reward => "leash_reward",
        _ => "leash_tug",
    };

    /// <summary>The only states a leash kind reaches on the wire. Anything else is dropped.</summary>
    public static LeashStep? StateFromWire(string? s) => s switch
    {
        "arrived" => LeashStep.Arrived,
        "seen" => LeashStep.Seen,
        _ => null,
    };

    /// <summary>A receipt id: 16 lower-case hex, the shape of every leash item id (pid, aid,
    /// event id, offer id).</summary>
    public static bool IsId(string? s)
    {
        if (s is not { Length: 16 }) return false;
        foreach (var c in s) if (!(c is >= '0' and <= '9' || c is >= 'a' and <= 'f')) return false;
        return true;
    }
}

/// <summary>One sender receipt (<c>RC</c>) of a leash kind: <paramref name="Id"/> is the item id
/// (for a punishment or an assignment it IS the pid / aid, which <paramref name="Ref"/> repeats).</summary>
public sealed record LeashReceipt(string Id, LeashItemKind Kind, string To, string? ToName, LeashStep State, DateTimeOffset At, string? Ref = null);

/// <summary>One thing the holder sent and how far it got. <see cref="Id"/> is null until the
/// item is matched to its server id (a punishment or an assignment learns its pid / aid from the
/// next poll; an offer, a tug and a reward get theirs in the send reply).</summary>
public sealed record LeashSentItem(
    string Key, LeashItemKind Kind, string To, string? Id, LeashStep Step, DateTimeOffset SentAt, DateTimeOffset StepAt)
{
    public PunishKind? Punish { get; init; }
    public AssignKind? Assign { get; init; }
    public RewardKind? Reward { get; init; }
    public int? Size { get; init; }

    /// <summary>The sticker or praise word of a reward.</summary>
    public string? Token { get; init; }

    public DateTimeOffset? ArrivedAt { get; init; }
    public DateTimeOffset? SeenAt { get; init; }

    /// <summary><see cref="LeashStep.Refused"/>: what the server said.</summary>
    public LeashSendStatus? Refusal { get; init; }

    /// <summary><see cref="LeashStep.Skipped"/>: why (<c>unplayable</c>).</summary>
    public string? Reason { get; init; }

    /// <summary>True once the item reached the other client (a receipt said so, or an ending
    /// only its client could cause).</summary>
    public bool Reached => ArrivedAt != null || Step is LeashStep.Arrived or LeashStep.Seen
        or LeashStep.Done or LeashStep.Accepted or LeashStep.Declined or LeashStep.Skipped;

    /// <summary>True once it was on the other side's screen. A missed task was not necessarily
    /// seen; a done, answered or skipped one was.</summary>
    public bool WasSeen => SeenAt != null || Step is LeashStep.Seen
        or LeashStep.Done or LeashStep.Accepted or LeashStep.Declined or LeashStep.Skipped;
}

/// <summary>
/// The holder's log of what it sent and how far each item got. Fed by the send replies, the
/// poll's snapshot (a punishment's pid and an assignment's aid), the sender receipts and the
/// leash events. Every mutator answers true when something a card draws changed.
///
/// <para>An item only shows steps once this server has proved it speaks receipts
/// (<see cref="Supported"/>): an id in a send reply, any leash receipt, or a
/// <c>punish_skipped</c>. An older server never flips it, so nothing is drawn.</para>
/// </summary>
public sealed class LeashSentLog
{
    /// <summary>Items kept per leashed friend (the card draws the newest few).</summary>
    public const int PerFriend = 12;

    /// <summary>The server keeps a leash receipt's record 4 days; nothing older can move.</summary>
    public static readonly TimeSpan Keep = TimeSpan.FromDays(4);

    private readonly List<LeashSentItem> _items = new();
    private readonly Dictionary<string, PunishKind> _pendingKinds = new(StringComparer.Ordinal);
    private int _next;

    /// <summary>True once the server has shown it speaks receipts. Never goes back.</summary>
    public bool Supported { get; private set; }

    /// <summary>The poll reply carried receipts (even none): the server speaks them.</summary>
    public bool MarkSupported()
    {
        if (Supported) return false;
        Supported = true;
        return true;
    }

    public void Clear()
    {
        _items.Clear();
        _pendingKinds.Clear();
        Supported = false;
    }

    /// <summary>What was sent to <paramref name="to"/> lately, newest first.</summary>
    public IReadOnlyList<LeashSentItem> For(string to, DateTimeOffset now) =>
        _items.Where(i => i.To == to && now - i.SentAt <= Keep).OrderByDescending(i => i.SentAt).ToList();

    /// <summary>The newest offer to <paramref name="to"/>, or null.</summary>
    public LeashSentItem? LatestOffer(string to) =>
        _items.Where(i => i.To == to && i.Kind == LeashItemKind.Offer).OrderByDescending(i => i.SentAt).FirstOrDefault();

    /// <summary>A send answered. Sent / queued / replaced start an item at <see cref="LeashStep.Sent"/>;
    /// a worded refusal is an item that never left (<see cref="LeashStep.Refused"/>); a network fault,
    /// <c>off</c> and <c>already</c> leave nothing to track.</summary>
    public bool NoteSent(LeashItemKind kind, string to, LeashSendResult r, DateTimeOffset now,
        PunishKind? punish = null, AssignKind? assign = null, RewardKind? reward = null, int? size = null, string? token = null)
    {
        if (string.IsNullOrEmpty(to)) return false;
        LeashStep? step = r.Status switch
        {
            LeashSendStatus.Sent or LeashSendStatus.Queued or LeashSendStatus.Replaced => LeashStep.Sent,
            LeashSendStatus.Failed or LeashSendStatus.Off or LeashSendStatus.Already => null,
            _ => LeashStep.Refused,
        };
        if (step == null) return false;
        var id = step == LeashStep.Sent && LeashSteps.IsId(r.ItemId) ? r.ItemId : null;
        if (id != null) Supported = true;
        if (id != null && _items.Any(i => i.Id == id)) return false;
        Add(new LeashSentItem("l" + (++_next), kind, to, id, step.Value, now, now)
        {
            Punish = punish, Assign = assign, Reward = reward, Size = size, Token = token,
            Refusal = step == LeashStep.Refused ? r.Status : null,
        });
        return true;
    }

    /// <summary>The poll's holder rows: a punishment's pid and an assignment's aid, matched to
    /// the oldest unmatched send of the same kind to the same person.</summary>
    public bool Bind(IEnumerable<HeldLeash> holding)
    {
        bool changed = false;
        foreach (var h in holding)
        {
            foreach (var p in h.Pending.OrderBy(p => p.At))
            {
                _pendingKinds[p.Pid] = p.Kind;
                if (!LeashSteps.IsId(p.Pid) || Find(p.Pid) >= 0) continue;
                var i = OldestUnbound(LeashItemKind.Punish, h.Who.Id, x => x.Punish == p.Kind);
                if (i < 0) continue;
                // The server may have lowered a video to their cap: its size wins.
                _items[i] = _items[i] with { Id = p.Pid, Size = p.Size };
                changed = true;
            }
            if (h.Assignment is { } a && LeashSteps.IsId(a.Aid) && Find(a.Aid) < 0)
            {
                var i = OldestUnbound(LeashItemKind.Assign, h.Who.Id, x => x.Assign == a.Kind);
                if (i >= 0)
                {
                    _items[i] = _items[i] with { Id = a.Aid };
                    changed = true;
                }
            }
        }
        if (_pendingKinds.Count > 200) _pendingKinds.Clear();
        return changed;
    }

    /// <summary>One sender receipt. Forward only. A receipt for something this client never sent
    /// (another device, before a restart) becomes an item of its own.</summary>
    public bool Apply(LeashReceipt r)
    {
        if (!LeashSteps.IsId(r.Id) || string.IsNullOrEmpty(r.To)) return false;
        bool changed = MarkSupported();
        var i = Find(r.Id);
        if (i < 0) i = BindUnknown(r.Kind, r.To, r.Id, _pendingKinds.TryGetValue(r.Id, out var pk) ? pk : null, null);
        if (i < 0)
        {
            Add(new LeashSentItem("r" + (++_next), r.Kind, r.To, r.Id, LeashStep.Sent, r.At, r.At)
            {
                Punish = r.Kind == LeashItemKind.Punish && _pendingKinds.TryGetValue(r.Id, out var k) ? k : null,
            });
            i = _items.Count - 1;
        }
        var it = _items[i];
        var next = it;
        if (r.State == LeashStep.Arrived && it.ArrivedAt == null) next = next with { ArrivedAt = r.At };
        if (r.State == LeashStep.Seen && it.SeenAt == null) next = next with { SeenAt = r.At, ArrivedAt = it.ArrivedAt ?? r.At };
        if (!LeashSteps.IsEnding(it.Step) && LeashSteps.Rank(r.State) > LeashSteps.Rank(it.Step))
            next = next with { Step = r.State, StepAt = r.At };
        if (next == it) return changed;
        _items[i] = next;
        return true;
    }

    /// <summary>A leash event that ends an item on the holder side: an answered offer, a
    /// punishment done or skipped, a task done or missed. Anything else is not its business.</summary>
    public bool ApplyEvent(LeashEvent e)
    {
        switch (e.Kind)
        {
            case LeashEventKind.Answered:
            {
                var step = e.Accepted == true ? LeashStep.Accepted : LeashStep.Declined;
                var i = _items.FindLastIndex(x => x.Kind == LeashItemKind.Offer && x.To == e.From.Id && !LeashSteps.IsEnding(x.Step));
                if (i < 0) { Add(new LeashSentItem("e" + (++_next), LeashItemKind.Offer, e.From.Id, null, step, e.At, e.At)); return true; }
                return End(i, step, e.At);
            }
            case LeashEventKind.PunishDone:
            case LeashEventKind.PunishSkipped:
            {
                bool changed = e.Kind == LeashEventKind.PunishSkipped && MarkSupported();
                var g = e.Given;
                if (g == null || !LeashSteps.IsId(g.Pid)) return changed;
                var step = e.Kind == LeashEventKind.PunishDone ? LeashStep.Done : LeashStep.Skipped;
                var i = Find(g.Pid);
                if (i < 0) i = BindUnknown(LeashItemKind.Punish, e.From.Id, g.Pid, g.Kind, null);
                if (i < 0)
                {
                    Add(new LeashSentItem("e" + (++_next), LeashItemKind.Punish, e.From.Id, g.Pid, step, e.At, e.At)
                    {
                        Punish = g.Kind, Size = g.Size, Reason = step == LeashStep.Skipped ? e.Reason : null,
                    });
                    return true;
                }
                if (step == LeashStep.Skipped && _items[i].Reason == null && !LeashSteps.IsEnding(_items[i].Step))
                    _items[i] = _items[i] with { Reason = e.Reason };
                return End(i, step, e.At) | changed;
            }
            case LeashEventKind.AssignDone:
            case LeashEventKind.AssignMissed:
            {
                var a = e.Assignment;
                if (a == null || !LeashSteps.IsId(a.Aid)) return false;
                var step = e.Kind == LeashEventKind.AssignDone ? LeashStep.Done : LeashStep.Missed;
                var i = Find(a.Aid);
                if (i < 0) i = BindUnknown(LeashItemKind.Assign, e.From.Id, a.Aid, null, a.Kind);
                if (i < 0)
                {
                    Add(new LeashSentItem("e" + (++_next), LeashItemKind.Assign, e.From.Id, a.Aid, step, e.At, e.At)
                    {
                        Assign = a.Kind, Size = a.Size,
                    });
                    return true;
                }
                return End(i, step, e.At);
            }
            default:
                return false;
        }
    }

    // ---- internals ----

    private bool End(int i, LeashStep step, DateTimeOffset at)
    {
        var it = _items[i];
        if (LeashSteps.IsEnding(it.Step)) return false;
        _items[i] = it with { Step = step, StepAt = at };
        return true;
    }

    private int Find(string id) => _items.FindIndex(i => i.Id == id);

    /// <summary>An id this log has not matched yet: the oldest unmatched send of the same kind
    /// to the same person takes it. A punishment's kind must agree when either side knows it;
    /// an unknown punishment only claims a Chaster send (those never sit in the queue, so the
    /// snapshot never names their pid). Offers, tugs and rewards carry their id from the reply,
    /// so an unknown one is a new item.</summary>
    private int BindUnknown(LeashItemKind kind, string to, string id, PunishKind? punish, AssignKind? assign)
    {
        int i = kind switch
        {
            LeashItemKind.Punish => punish is { } pk
                ? OldestUnbound(kind, to, x => x.Punish == null || x.Punish == pk)
                : OldestUnbound(kind, to, x => x.Punish == PunishKind.Chaster),
            LeashItemKind.Assign => OldestUnbound(kind, to, x => assign == null || x.Assign == null || x.Assign == assign),
            _ => -1,
        };
        if (i >= 0) _items[i] = _items[i] with { Id = id };
        return i;
    }

    private int OldestUnbound(LeashItemKind kind, string to, Func<LeashSentItem, bool> match) =>
        _items.FindIndex(x => x.Kind == kind && x.To == to && x.Id == null && x.Step == LeashStep.Sent && match(x));

    private void Add(LeashSentItem item)
    {
        _items.Add(item);
        var mine = _items.Where(i => i.To == item.To).ToList();
        if (mine.Count <= PerFriend) return;
        foreach (var old in mine.OrderBy(i => i.SentAt).Take(mine.Count - PerFriend)) _items.Remove(old);
    }
}
