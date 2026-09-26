using System;
using System.Collections.Generic;

namespace ConditioningControlPanel.Services.Leash;

// The leash's shared vocabulary. Pure data, no WPF, no HTTP: the wire lane (LeashApi /
// LeashService), the drawer card, the ask card and the gate all speak these types, and
// CONTRACT.md beside this file is the wire they map to. Change a value here and the server's
// leash.js grammar changes with it, in the same PR.

/// <summary>Which punishments the leashed side allows. Only the leashed side sets it.</summary>
public enum LeashIntensity { Soft, Standard, Strict }

/// <summary>How a v2 remote starts on the leashed side. <see cref="Ask"/> = 5 s countdown card.</summary>
public enum LeashRemoteMode { Ask, Take }

public enum PunishKind { Lines, Pink, Bubbles, Detention, Video, Chaster }

public enum RewardKind { Sticker, Credit, Pardon, Praise }

public enum AssignKind { Minutes, Quests, Video }

public enum AssignStatus { Open, Done, Missed }

/// <summary>One cell of the 7-day strip.</summary>
public enum WeekMark { Did, Idle, Punished, Today }

public enum LeashEventKind { Tug, Reward, Punish, Assign, Answered, Ended, AssignDone, AssignMissed }

/// <summary>DND lengths the leashed side can pick. <see cref="Off"/> ends a pause.</summary>
public enum LeashDnd { Off, OneHour, FourHours, Today }

/// <summary>A person on the other end. Server-resolved; <see cref="AvatarUrl"/> may be null.</summary>
public sealed record LeashPerson(string Id, string Name, string? AvatarUrl);

/// <summary>A video reference, the friends <c>watch</c> grammar (<c>catalogue</c> or <c>ht</c>).</summary>
public sealed record LeashWatch(string Kind, string Id, string? Title);

public sealed record Punishment(
    string Pid, PunishKind Kind, int Size, LeashWatch? Watch, LeashPerson From,
    DateTimeOffset At, DateTimeOffset ExpiresAt);

public sealed record Assignment(
    string Aid, AssignKind Kind, int Size, LeashWatch? Watch, string Day, AssignStatus Status, DateTimeOffset At);

public sealed record Sticker(string Id, LeashPerson From, DateTimeOffset At);

/// <summary>What the leashed client reports about its own day (CONTRACT R).</summary>
public sealed record DayReport(
    string Day, int Minutes, int QuestsDone, int QuestsTotal, int Streak,
    bool ChasterLinked, int? LockLeftSeconds, int? TabSeconds, bool AssignDone, DateTimeOffset At);

public sealed record WeekDay(string Day, WeekMark Mark);

/// <summary>My own leash, seen from the leashed side (CONTRACT L).</summary>
public sealed record MyLeash(
    LeashPerson Holder, LeashIntensity Intensity, DateTimeOffset Since, int Day,
    DateTimeOffset? DndUntil, LeashRemoteMode RemoteMode,
    IReadOnlyList<Punishment> Pending, Assignment? Assignment, int Pardons,
    IReadOnlyList<Sticker> Stickers);

/// <summary>One account I hold, seen from the holder side (CONTRACT H).</summary>
public sealed record HeldLeash(
    LeashPerson Who, bool Online, LeashIntensity Intensity, DateTimeOffset Since, int Day,
    DateTimeOffset? DndUntil, DayReport? Report, IReadOnlyList<WeekDay> Week,
    IReadOnlyList<Punishment> Pending, Assignment? Assignment, int PunishedToday);

public sealed record LeashOffer(LeashPerson From, DateTimeOffset At, DateTimeOffset ExpiresAt);

/// <summary>A one-shot event. Extra fields ride in the typed payloads; null when not relevant.</summary>
public sealed record LeashEvent(
    string Id, LeashEventKind Kind, LeashPerson From, DateTimeOffset At,
    Punishment? Punishment = null, Assignment? Assignment = null,
    RewardKind? Reward = null, string? StickerOrPoke = null, int? Size = null, bool? Accepted = null);

/// <summary>Everything the leash surfaces draw, in one object (CONTRACT B minus events).</summary>
public sealed record LeashSnapshot(MyLeash? Me, IReadOnlyList<HeldLeash> Holding, IReadOnlyList<LeashOffer> Offers)
{
    public static readonly LeashSnapshot Empty = new(null, Array.Empty<HeldLeash>(), Array.Empty<LeashOffer>());

    /// <summary>True when the poll must run on the 20 s cadence.</summary>
    public bool Active => Me != null || Holding.Count > 0;
}

/// <summary>Worded replies from the holder-side ops. <see cref="Failed"/> = network or 5xx.</summary>
public enum LeashSendStatus
{
    Sent, Queued, Replaced, Already, NotFriends, Cooldown, Full, Taken, Self,
    NotAllowed, Cap, Dnd, TooFast, Refused, Off, Failed,
}

public sealed record LeashSendResult(LeashSendStatus Status, DateTimeOffset? DndUntil = null);
