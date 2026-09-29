using System;

namespace ConditioningControlPanel.Services.Friends;

/// <summary>What the friends feed can say happened. The service raises these
/// (<see cref="IFriendsService.Happened"/>); the feed stores and draws them.</summary>
public enum FriendEventKind
{
    /// <summary>A friend poked me. Detail = the poke id.</summary>
    PokeReceived,
    /// <summary>A friend invited me somewhere. Destination = where.</summary>
    InviteReceived,
    /// <summary>A friend sent me something to watch. Detail = its title, when it has one.</summary>
    WatchReceived,
    /// <summary>Someone asked to be my friend.</summary>
    RequestReceived,
    /// <summary>A friend request I sent was accepted.</summary>
    RequestAccepted,
    /// <summary>Something I sent reached their screen. Detail = the receipt kind (poke, invite, watch, request).</summary>
    ItemSeen,
    /// <summary>An invite I sent was answered. Detail = joined or declined.</summary>
    InviteAnswered,
    /// <summary>An invite I sent ran out with no answer.</summary>
    InviteUnanswered,
}

/// <summary>One line of the friends feed. <see cref="Key"/> is unique per server event (an item id
/// plus its state, or a request plus its kind), so a line never lands twice.</summary>
public sealed record FriendEvent(
    FriendEventKind Kind,
    string FriendId,
    string? FriendName,
    DateTime AtUtc,
    string Key,
    string? Destination = null,
    string? Detail = null);
