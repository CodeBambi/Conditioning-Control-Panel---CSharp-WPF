using System;
using System.Collections.Generic;

namespace ConditioningControlPanel.Services.Friends;

// The friends feature's shared vocabulary. Pure data, no WPF, no HTTP: the wire lane
// (FriendsApi/FriendsService), the drawer and the landing side all speak these types,
// and CONTRACT.md beside this file is the wire they map to. Change a value here and
// the server's friends.js grammar changes with it, in the same PR.

/// <summary>What a friend is doing right now. Published by the app itself, never typed.</summary>
public enum PresenceActivity
{
    Offline,
    Panel,
    Session,
    BackRoom,
    Race,
    Goon,
    GoonHosting,
    Arcademy,
    Breakout,
    Deeper,
    Remote,
}

/// <summary>A friend's presence as the server last reported it. <see cref="LockDay"/> is
/// the day count of their chastity lock when they share it, else null.</summary>
public sealed record FriendPresence(PresenceActivity Activity, int? LockDay, DateTimeOffset LastSeen)
{
    public static readonly FriendPresence None = new(PresenceActivity.Offline, null, DateTimeOffset.MinValue);
}

/// <summary>One friend. <see cref="AvatarUrl"/> is a first-party proxy path or null (draw initials).
/// <see cref="Tier"/> is 0 free, 1 Basic Subject, 2 Prime Subject.</summary>
public sealed record Friend(
    string Id,
    string Name,
    string? AvatarUrl,
    int Tier,
    bool Online,
    FriendPresence Presence,
    bool Squelched);

/// <summary>A pending request, incoming or outgoing. <see cref="Via"/> is the mutual friend's
/// name when the server knows one, else null.</summary>
public sealed record FriendRequest(string Id, string Name, string? AvatarUrl, string? Via, DateTimeOffset At);

/// <summary>Everything the drawer draws, in one object. Replaced whole on every refresh.</summary>
public sealed record FriendsSnapshot(
    IReadOnlyList<Friend> Friends,
    IReadOnlyList<FriendRequest> Incoming,
    IReadOnlyList<FriendRequest> Outgoing,
    string MyCode,
    FriendPresence Me)
{
    public static readonly FriendsSnapshot Empty = new(
        Array.Empty<Friend>(), Array.Empty<FriendRequest>(), Array.Empty<FriendRequest>(), "", FriendPresence.None);

    public int OnlineCount
    {
        get
        {
            var n = 0;
            foreach (var f in Friends) if (f.Online) n++;
            return n;
        }
    }
}

/// <summary>The three things a friend can send. There is no fourth: no text, ever.</summary>
public enum SendKind { Poke, Invite, Watch }

/// <summary>The poke vocabulary. Every id has a loc key <c>friends_poke_&lt;id&gt;</c> and the
/// server refuses any other id. Which eight ship in the picker is the owner's pick
/// (<see cref="Shipped"/>); the other four exist so the pick can change without a wire change.</summary>
public static class PokeSet
{
    public static readonly IReadOnlyList<string> All = new[]
    {
        "hi", "wp", "tut", "gl", "oops", "thanks", "drop", "peek", "deeper", "still", "more", "sleepy",
    };

    public static readonly IReadOnlyList<string> Shipped = new[]
    {
        "hi", "wp", "tut", "gl", "oops", "thanks", "drop", "peek",
    };

    /// <summary>Seconds between two pokes to the same friend. Mirrored server-side.</summary>
    public const int CooldownSeconds = 10;

    public static bool IsValid(string? id) => id != null && All.Contains(id);
}

/// <summary>Where an invite can point. Loc key <c>friends_invite_&lt;id&gt;</c>.</summary>
public static class InviteDestination
{
    public const string Goon = "goon";
    public const string BackRoom = "backroom";
    public const string Remote = "remote";
    public const string Ramp = "ramp";

    public static readonly IReadOnlyList<string> All = new[] { Goon, BackRoom, Remote, Ramp };

    /// <summary>Seconds an invite stays answerable. Mirrored server-side.</summary>
    public const int LifetimeSeconds = 90;

    public static bool IsValid(string? id) => id != null && All.Contains(id);
}

/// <summary>A watch is a REFERENCE the receiver resolves, never a URL. Catalogue = an
/// enhancement id from the site catalogue; Flavour = a Scrolller preset name; Ht = a
/// numeric video id on the one allowlisted host.</summary>
public enum WatchKind { Catalogue, Flavour, Ht }

public sealed record WatchRef(WatchKind Kind, string Id, string? Title)
{
    public static readonly IReadOnlyList<string> Flavours = new[] { "trance", "pink", "frills", "shiny", "censored" };

    /// <summary>The client-side twin of the server grammar: refuse before the wire does.</summary>
    public bool IsValid()
    {
        if (string.IsNullOrEmpty(Id)) return false;
        switch (Kind)
        {
            case WatchKind.Catalogue:
                if (Id.Length > 64) return false;
                foreach (var c in Id)
                    if (!(char.IsAsciiLetterOrDigit(c) || c == '_' || c == '-')) return false;
                return true;
            case WatchKind.Flavour:
                return Flavours.Contains(Id);
            case WatchKind.Ht:
                if (Id.Length > 8) return false;
                foreach (var c in Id) if (!char.IsAsciiDigit(c)) return false;
                return true;
        }
        return false;
    }
}

/// <summary>One delivery from the inbox. Exactly one of the kind-specific fields is set.
/// <see cref="Code"/> is a system-generated join code carried by a Goon or Remote invite.</summary>
public sealed record InboxItem(
    string Id,
    SendKind Kind,
    string FromId,
    string FromName,
    string? FromAvatarUrl,
    string? PokeId,
    string? Destination,
    string? Code,
    WatchRef? Watch,
    DateTimeOffset At,
    DateTimeOffset ExpiresAt)
{
    public bool IsExpired(DateTimeOffset now) => now >= ExpiresAt;
}

/// <summary>What a send came back with. Only <see cref="Sent"/> did anything.</summary>
public enum SendResult { Sent, Squelched, TooFast, NotFriends, Offline, Refused, TryLater }

/// <summary>What adding by code came back with.</summary>
public enum AddResult { Sent, Accepted, Already, NotFound, Blocked, Self, Full, TryLater }

/// <summary>Report reasons. A fixed list, no text box, mirrored server-side.</summary>
public static class ReportReason
{
    public static readonly IReadOnlyList<string> All = new[] { "spam", "harassment", "underage", "impersonation", "other" };
    public static bool IsValid(string? id) => id != null && All.Contains(id);
}

internal static class ListContains
{
    public static bool Contains(this IReadOnlyList<string> list, string value)
    {
        for (var i = 0; i < list.Count; i++) if (list[i] == value) return true;
        return false;
    }
}
