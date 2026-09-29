using System;
using System.Collections.Generic;

namespace ConditioningControlPanel.Services.Friends;

/// <summary>
/// The recipient's side of receipts, once each: a surface that draws a poke, a knock card or a
/// request row calls <see cref="Report"/> every time it draws, and only the first call for that
/// thing and state reaches <see cref="IFriendsService.ReportReceipt"/>. The drawer repaints often;
/// the server would ignore the repeats anyway, but they would ride every poll.
/// </summary>
public sealed class FriendsSeen
{
    /// <summary>How many reported keys are remembered; the oldest go first.</summary>
    public const int Cap = 500;

    private readonly object _gate = new();
    private readonly HashSet<string> _done = new(StringComparer.Ordinal);
    private readonly Queue<string> _order = new();

    /// <summary>The app's one memory (every drawer and landing surface shares it).</summary>
    public static FriendsSeen Shared { get; } = new();

    /// <summary>Queues <paramref name="report"/> unless this thing already reported this state.
    /// <paramref name="onceKey"/> narrows "this thing" (a request is keyed by its sender AND its
    /// time, so a second request from the same person is seen again). True when it was queued.</summary>
    public bool Report(IFriendsService? svc, ReceiptReport? report, string? onceKey = null)
    {
        if (svc == null || report == null || !report.IsValid()) return false;
        var key = (onceKey ?? report.Key) + "|" + report.State;
        lock (_gate)
        {
            if (!_done.Add(key)) return false;
            _order.Enqueue(key);
            while (_order.Count > Cap) _done.Remove(_order.Dequeue());
        }
        try { svc.ReportReceipt(report); }
        catch (Exception ex) { App.Logger?.Debug("[Friends] receipt report: {E}", ex.Message); }
        return true;
    }

    /// <summary>An item on screen: <c>seen</c>.</summary>
    public bool Seen(IFriendsService? svc, string? itemId)
        => FriendReceipts.IsItemId(itemId) && Report(svc, ReceiptReport.Item(itemId!, ReceiptState.Seen));

    /// <summary>An incoming friend request on screen: <c>seen</c>, once per request.</summary>
    public bool RequestSeen(IFriendsService? svc, FriendRequest? request)
    {
        if (request == null || string.IsNullOrEmpty(request.Id)) return false;
        return Report(svc, ReceiptReport.Request(request.Id), "r:" + request.Id + ":" + request.At.ToUnixTimeSeconds());
    }

    public void Clear()
    {
        lock (_gate)
        {
            _done.Clear();
            _order.Clear();
        }
    }
}
