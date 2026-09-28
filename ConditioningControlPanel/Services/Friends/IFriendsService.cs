using System;
using System.Threading.Tasks;

namespace ConditioningControlPanel.Services.Friends;

/// <summary>The friends feature as every surface sees it. One implementation
/// (<c>FriendsService</c>, hung off <c>App.Friends</c>); the drawer, the launcher chip
/// and the landing side only ever talk to this. Every member is UI-thread safe: events
/// are raised on the dispatcher, snapshots are immutable.</summary>
public interface IFriendsService
{
    /// <summary>True while an account is signed in. Everything else is a no-op when false.</summary>
    bool Available { get; }

    /// <summary>The last snapshot; <see cref="FriendsSnapshot.Empty"/> before the first refresh.</summary>
    FriendsSnapshot Snapshot { get; }

    /// <summary>Whether this account publishes what it is doing. Off by default; the drawer
    /// asks once. Reads and writes <c>AppSettings.FriendsPresenceShared</c>.</summary>
    bool PresenceShared { get; set; }

    /// <summary>Raised on the UI thread after every refresh that changed anything.</summary>
    event Action<FriendsSnapshot>? SnapshotChanged;

    /// <summary>Raised on the UI thread once per new inbox item, in arrival order, never
    /// for an item that has already expired. The landing side owns what happens next.</summary>
    event Action<InboxItem>? Delivered;

    /// <summary>Raised on the UI thread after a successful send, for the sender's own juice.</summary>
    event Action<SendKind, Friend>? Sent;

    /// <summary>Raised on the UI thread once per incoming friend request that was not there on
    /// the last refresh. The first list after a start or a sign-in is a silent baseline.</summary>
    event Action<FriendRequest>? RequestArrived;

    /// <summary>Raised on the UI thread with the id of an incoming request that left the list
    /// (accepted, declined or withdrawn).</summary>
    event Action<string>? RequestGone;

    /// <summary>Full list now (drawer opened, pull to refresh). Cheap to call twice.</summary>
    Task RefreshAsync();

    Task<SendResult> PokeAsync(string friendId, string pokeId);

    /// <summary><paramref name="code"/> is the system-generated join code for Goon and Remote
    /// invites, null for the rest.</summary>
    Task<SendResult> InviteAsync(string friendId, string destination, string? code);

    Task<SendResult> SendWatchAsync(string friendId, WatchRef watch);

    Task<AddResult> AddByCodeAsync(string code);
    Task AcceptAsync(string requesterId);
    Task DeclineAsync(string requesterId);
    Task CancelRequestAsync(string targetId);
    Task RemoveAsync(string friendId);
    Task BlockAsync(string friendId);
    Task UnblockAsync(string friendId);
    Task SetSquelchAsync(string friendId, bool on);
    Task ReportAsync(string friendId, string reason);

    /// <summary>Hosts call this when the player enters or leaves a surface. Published to the
    /// server only while <see cref="PresenceShared"/>; the poll cadence reads it regardless.</summary>
    void SetActivity(PresenceActivity activity);

    /// <summary>The drawer calls this with true while open, false when it folds: it is one of
    /// the two things that put the poll on its fast cadence (the other is a friend online).</summary>
    void SetDrawerOpen(bool open);
}
