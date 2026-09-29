using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ConditioningControlPanel.Services.Leash;

/// <summary>The leash as every surface sees it. One implementation (<c>LeashService</c>, hung off
/// <c>App.Leash</c>); the drawer card, the ask card and the gate only ever talk to this. It rides
/// the friends poll (CONTRACT "The poll piggyback"). Every member is UI-thread safe: events are
/// raised on the dispatcher, snapshots are immutable.</summary>
public interface ILeashService
{
    /// <summary>False while signed out or while the server answers <c>off</c>.</summary>
    bool Available { get; }

    LeashSnapshot Snapshot { get; }

    /// <summary>Raised on the UI thread after a poll changed the snapshot.</summary>
    event Action<LeashSnapshot>? SnapshotChanged;

    /// <summary>Raised on the UI thread once per one-shot event, in arrival order.</summary>
    event Action<LeashEvent>? EventArrived;

    /// <summary>The oldest pending punishment that still needs the gate (never a Chaster one),
    /// or null. The gate host reads this.</summary>
    Punishment? GateDue { get; }

    // Holder side.
    Task<LeashSendResult> OfferAsync(string friendId);
    /// <summary>The holder lets go. True when the server took it.</summary>
    Task<bool> ReleaseAsync(string leashedId);
    Task<LeashSendResult> AssignAsync(string leashedId, AssignKind kind, int size, LeashWatch? watch = null);
    Task<LeashSendResult> PunishAsync(string leashedId, PunishKind kind, int size, LeashWatch? watch = null);
    Task<LeashSendResult> RewardAsync(string leashedId, RewardKind kind, string? stickerOrPoke = null, int? size = null);
    Task<LeashSendResult> TugAsync(string leashedId);

    // Leashed side.
    /// <summary>Only <see cref="LeashAnswerResult.Done"/> is a success; the ask card keeps itself
    /// open and says why on anything else.</summary>
    Task<LeashAnswerResult> AnswerAsync(string holderId, bool accept, LeashIntensity intensity);
    /// <summary>Never refused, never priced, never gated. Works offline-first: the local state
    /// drops the leash at once and the call is retried until the server has it.</summary>
    Task CutAsync();
    Task SetIntensityAsync(LeashIntensity intensity);
    Task SetDndAsync(LeashDnd dnd);
    Task SetRemoteModeAsync(LeashRemoteMode mode);
    /// <summary>The longest a leash video may run, minutes 1..90.</summary>
    Task SetVideoMaxAsync(int minutes);
    Task CompleteAsync(string pid);
    Task<bool> PardonAsync(string pid);

    /// <summary>A punishment video that will not play: ask the server to drop it (<c>punish_skip</c>,
    /// reason <c>unplayable</c>). When it cannot, the punishment stays pending but this client
    /// leaves it off the gate for a day and says so.</summary>
    Task<LeashSkipResult> SkipUnplayableAsync(string pid) => Task.FromResult(LeashSkipResult.Marked);

    /// <summary>True while this client holds a "will not play" mark on the punishment.</summary>
    bool IsUnplayable(string pid) => false;

    // Receipts (CONTRACT "Receipts"). Defaults keep a service that predates them quiet.

    /// <summary>Leashed side: the item with this id (an offer's id, a pid, an aid, a reward's or a
    /// tug's event id) is on screen right now. Reported <c>seen</c> once, on the next poll.</summary>
    void NoteShown(string? id) { }

    /// <summary>Holder side: what this account sent <paramref name="leashedId"/> lately, newest
    /// first, with how far each got.</summary>
    IReadOnlyList<LeashSentItem> SentTo(string leashedId) => Array.Empty<LeashSentItem>();

    /// <summary>True once the server has shown it speaks receipts. False = draw no steps at all.</summary>
    bool ReceiptsSupported => false;

    /// <summary>Raised on the UI thread when a receipt, a send or an ending moved a sent item.</summary>
    event Action? ReceiptsChanged { add { } remove { } }
}
