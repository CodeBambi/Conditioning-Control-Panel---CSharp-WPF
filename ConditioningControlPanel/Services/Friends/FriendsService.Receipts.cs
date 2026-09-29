using System;
using System.Collections.Generic;
using System.Linq;

namespace ConditioningControlPanel.Services.Friends;

/// <summary>
/// Receipts and the feed (CCP-Server <c>proxy/FRIENDS-RECEIPTS.md</c> v1). Recipient reports queue here
/// and ride the next poll; the reply's sender receipts are raised as they come, every kind including
/// leash_*; <see cref="Happened"/> folds the service's own events into feed lines, so a surface
/// listens to one event to draw one list.
/// </summary>
public sealed partial class FriendsService
{
    private readonly object _reportGate = new();
    private readonly Dictionary<string, ReceiptReport> _reports = new(StringComparer.Ordinal);

    public event Action<IReadOnlyList<SenderReceipt>>? ReceiptsArrived;
    public event Action<FriendEvent>? Happened;

    public void ReportReceipt(ReceiptReport report)
    {
        if (report == null || !report.IsValid()) return;
        lock (_reportGate)
        {
            if (_reports.TryGetValue(report.Key, out var cur) && !FriendReceipts.MovesForward(cur.State, report.State)) return;
            _reports[report.Key] = report;
        }
    }

    /// <summary>What goes out with this poll, at most <see cref="FriendReceipts.MaxReportsPerPoll"/>.</summary>
    private List<ReceiptReport> TakeReports()
    {
        lock (_reportGate) return _reports.Values.Take(FriendReceipts.MaxReportsPerPoll).ToList();
    }

    /// <summary>After an answered poll: forget what went out, unless a further state was queued meanwhile.
    /// A poll that failed keeps them for the next one.</summary>
    private void ReportsSent(List<ReceiptReport> sent)
    {
        if (sent.Count == 0) return;
        lock (_reportGate)
            foreach (var r in sent)
                if (_reports.TryGetValue(r.Key, out var cur) && cur.State == r.State) _reports.Remove(r.Key);
    }

    private void ClearReports()
    {
        lock (_reportGate) _reports.Clear();
    }

    private void RaiseReceipts(IReadOnlyList<SenderReceipt>? receipts)
    {
        if (receipts == null || receipts.Count == 0) return;
        try { ReceiptsArrived?.Invoke(receipts); }
        catch (Exception ex) { App.Logger?.Debug("Friends receipts handler failed: {E}", ex.Message); }
        foreach (var r in receipts)
            if (FeedLine(r) is { } line) RaiseHappened(line);
    }

    private void RaiseHappened(FriendEvent e)
    {
        try { Happened?.Invoke(e); }
        catch (Exception ex) { App.Logger?.Debug("Friends feed handler failed: {E}", ex.Message); }
    }

    /// <summary>Called once from the constructor: the service's own events become feed lines.</summary>
    private void WireFeed()
    {
        Delivered += item =>
        {
            FriendEventKind? kind = item.Kind switch
            {
                SendKind.Poke => FriendEventKind.PokeReceived,
                SendKind.Invite => FriendEventKind.InviteReceived,
                SendKind.Watch => FriendEventKind.WatchReceived,
                _ => null,
            };
            if (kind is not { } k) return;
            var detail = item.Kind == SendKind.Poke ? item.PokeId : item.Watch?.Title;
            RaiseHappened(new FriendEvent(k, item.FromId, item.FromName, item.At.UtcDateTime,
                "in:" + item.Id, item.Destination, detail));
        };
        RequestArrived += r => RaiseHappened(new FriendEvent(FriendEventKind.RequestReceived, r.Id, r.Name,
            r.At.UtcDateTime, "req-in:" + r.Id + ":" + r.At.ToUnixTimeSeconds()));
    }

    /// <summary>A sender receipt as a feed line, or null for a step the feed does not tell: an
    /// arrived, a declined friend request (nobody needs that one read back to them), and every
    /// leash kind (the leash surfaces own those).</summary>
    internal static FriendEvent? FeedLine(SenderReceipt r)
    {
        if (ReceiptKind.IsLeash(r.Kind)) return null;
        var key = "rc:" + (r.Id ?? "req-out:" + r.To) + ":" + r.State;
        FriendEventKind? kind = (r.Kind, r.State) switch
        {
            (ReceiptKind.Request, ReceiptState.Accepted) => FriendEventKind.RequestAccepted,
            (ReceiptKind.Invite, ReceiptState.Joined) => FriendEventKind.InviteAnswered,
            (ReceiptKind.Invite, ReceiptState.Declined) => FriendEventKind.InviteAnswered,
            (ReceiptKind.Invite, ReceiptState.Expired) => FriendEventKind.InviteUnanswered,
            (_, ReceiptState.Seen) => FriendEventKind.ItemSeen,
            _ => null,
        };
        if (kind is not { } k) return null;
        var detail = k == FriendEventKind.ItemSeen ? r.Kind : k == FriendEventKind.InviteAnswered ? r.State : null;
        return new FriendEvent(k, r.To, r.ToName, r.AtUtc, key, null, detail);
    }
}
