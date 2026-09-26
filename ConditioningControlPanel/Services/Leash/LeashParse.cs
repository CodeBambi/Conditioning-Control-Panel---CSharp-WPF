using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.Leash;

/// <summary>
/// Wire strings and the poll's <c>leash</c> block (CONTRACT B) into the shared model. Tolerant on
/// purpose: an item that does not fit the grammar is dropped, never thrown on, because the gate
/// acts on these. Pure.
/// </summary>
public static class LeashParse
{
    // ---- enums <-> wire ----

    public static string IntensityToWire(LeashIntensity i) => i switch
    {
        LeashIntensity.Soft => "soft",
        LeashIntensity.Strict => "strict",
        _ => "standard",
    };

    public static LeashIntensity? IntensityFromWire(string? s) => s switch
    {
        "soft" => LeashIntensity.Soft,
        "standard" => LeashIntensity.Standard,
        "strict" => LeashIntensity.Strict,
        _ => null,
    };

    public static string RemoteModeToWire(LeashRemoteMode m) => m == LeashRemoteMode.Take ? "take" : "ask";

    public static string PunishToWire(PunishKind k) => k switch
    {
        PunishKind.Lines => "lines",
        PunishKind.Pink => "pink",
        PunishKind.Bubbles => "bubbles",
        PunishKind.Detention => "detention",
        PunishKind.Video => "video",
        _ => "chaster",
    };

    public static PunishKind? PunishFromWire(string? s) => s switch
    {
        "lines" => PunishKind.Lines,
        "pink" => PunishKind.Pink,
        "bubbles" => PunishKind.Bubbles,
        "detention" => PunishKind.Detention,
        "video" => PunishKind.Video,
        "chaster" => PunishKind.Chaster,
        _ => null,
    };

    public static string AssignToWire(AssignKind k) => k switch
    {
        AssignKind.Minutes => "minutes",
        AssignKind.Quests => "quests",
        _ => "video",
    };

    public static AssignKind? AssignFromWire(string? s) => s switch
    {
        "minutes" => AssignKind.Minutes,
        "quests" => AssignKind.Quests,
        "video" => AssignKind.Video,
        _ => null,
    };

    public static string RewardToWire(RewardKind k) => k switch
    {
        RewardKind.Sticker => "sticker",
        RewardKind.Credit => "credit",
        RewardKind.Pardon => "pardon",
        _ => "praise",
    };

    public static RewardKind? RewardFromWire(string? s) => s switch
    {
        "sticker" => RewardKind.Sticker,
        "credit" => RewardKind.Credit,
        "pardon" => RewardKind.Pardon,
        "praise" => RewardKind.Praise,
        _ => null,
    };

    public static LeashEventKind? EventFromWire(string? s) => s switch
    {
        "tug" => LeashEventKind.Tug,
        "reward" => LeashEventKind.Reward,
        "punish" => LeashEventKind.Punish,
        "assign" => LeashEventKind.Assign,
        "answered" => LeashEventKind.Answered,
        "ended" => LeashEventKind.Ended,
        "assign_done" => LeashEventKind.AssignDone,
        "assign_missed" => LeashEventKind.AssignMissed,
        _ => null,
    };

    public static WeekMark? MarkFromWire(string? s) => s switch
    {
        "g" => WeekMark.Did,
        "x" => WeekMark.Idle,
        "r" => WeekMark.Punished,
        "t" => WeekMark.Today,
        _ => null,
    };

    public static LeashSendStatus SendFromWire(string? s) => s switch
    {
        "sent" => LeashSendStatus.Sent,
        "queued" => LeashSendStatus.Queued,
        "replaced" => LeashSendStatus.Replaced,
        "already" => LeashSendStatus.Already,
        "not_friends" => LeashSendStatus.NotFriends,
        "cooldown" => LeashSendStatus.Cooldown,
        "full" => LeashSendStatus.Full,
        "taken" => LeashSendStatus.Taken,
        "self" => LeashSendStatus.Self,
        "not_allowed" => LeashSendStatus.NotAllowed,
        "cap" => LeashSendStatus.Cap,
        "dnd" => LeashSendStatus.Dnd,
        "too_fast" => LeashSendStatus.TooFast,
        "refused" or "bad_input" or "not_leashed" or "not_found" => LeashSendStatus.Refused,
        // "busy" (a lock held for a moment) and anything unknown read as try again.
        "off" => LeashSendStatus.Off,
        _ => LeashSendStatus.Failed,
    };

    public static JObject WatchToWire(LeashWatch w)
    {
        var o = new JObject { ["kind"] = w.Kind, ["id"] = w.Id };
        if (!string.IsNullOrEmpty(w.Title)) o["title"] = w.Title;
        return o;
    }

    // ---- primitives ----

    internal static string? Str(JToken? t) => t?.Type == JTokenType.String ? (string?)t : null;

    internal static int? Int(JToken? t)
    {
        if (t?.Type != JTokenType.Integer) return null;
        var l = t.Value<long>();
        return l >= int.MinValue && l <= int.MaxValue ? (int)l : null;
    }

    internal static DateTimeOffset? Time(JToken? t)
    {
        var s = Str(t);
        return s != null && DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var v)
            ? v : null;
    }

    public static LeashPerson? Person(JToken? t)
    {
        if (t is not JObject o || Str(o["id"]) is not { Length: > 0 } id) return null;
        return new LeashPerson(id, Str(o["name"]) ?? "", Str(o["avatar"]));
    }

    public static LeashWatch? Watch(JToken? t)
    {
        if (t is not JObject o) return null;
        var w = new LeashWatch(Str(o["kind"]) ?? "", Str(o["id"]) ?? "", Str(o["title"]));
        return LeashGrammar.ValidWatch(w) ? w : null;
    }

    public static Punishment? Punishment(JToken? t, LeashPerson? fallbackFrom = null)
    {
        if (t is not JObject o) return null;
        var pid = Str(o["pid"]);
        var kind = PunishFromWire(Str(o["kind"]));
        var size = Int(o["size"]);
        var at = Time(o["at"]);
        var from = Person(o["from"]) ?? fallbackFrom;
        if (string.IsNullOrEmpty(pid) || kind == null || size == null || at == null || from == null) return null;
        var watch = kind == PunishKind.Video ? Watch(o["watch"]) : null;
        if (!LeashGrammar.ValidPunish(kind.Value, size.Value, watch)) return null;
        var expires = Time(o["expires_at"]) ?? at.Value.AddHours(72);
        return new Punishment(pid, kind.Value, size.Value, watch, from, at.Value, expires);
    }

    public static Assignment? Assignment(JToken? t)
    {
        if (t is not JObject o) return null;
        var aid = Str(o["aid"]);
        var kind = AssignFromWire(Str(o["kind"]));
        var size = Int(o["size"]);
        if (string.IsNullOrEmpty(aid) || kind == null || size == null) return null;
        var watch = kind == AssignKind.Video ? Watch(o["watch"]) : null;
        if (!LeashGrammar.ValidAssign(kind.Value, size.Value, watch)) return null;
        var status = Str(o["status"]) switch { "done" => AssignStatus.Done, "missed" => AssignStatus.Missed, _ => AssignStatus.Open };
        return new Assignment(aid, kind.Value, size.Value, watch, Str(o["day"]) ?? "", status, Time(o["at"]) ?? DateTimeOffset.MinValue);
    }

    public static DayReport? Report(JToken? t)
    {
        if (t is not JObject o || Str(o["day"]) is not { Length: > 0 } day) return null;
        return new DayReport(day,
            Math.Max(0, Int(o["minutes"]) ?? 0),
            Math.Max(0, Int(o["quests_done"]) ?? 0),
            Math.Max(0, Int(o["quests_total"]) ?? 0),
            Math.Max(0, Int(o["streak"]) ?? 0),
            o.Value<bool?>("chaster_linked") == true,
            Int(o["lock_left_s"]),
            Int(o["tab_s"]),
            o.Value<bool?>("assign_done") == true,
            Time(o["at"]) ?? DateTimeOffset.MinValue);
    }

    private static List<Punishment> PunishList(JToken? t, LeashPerson? fallbackFrom)
    {
        var list = new List<Punishment>();
        if (t is JArray a)
            foreach (var x in a)
                if (Punishment(x, fallbackFrom) is { } p) list.Add(p);
        return System.Linq.Enumerable.ToList(System.Linq.Enumerable.OrderBy(list, p => p.At));
    }

    private static List<Sticker> Stickers(JToken? t)
    {
        var list = new List<Sticker>();
        if (t is not JArray a) return list;
        foreach (var x in a)
        {
            if (x is not JObject o) continue;
            var s = Str(o["sticker"]);
            var from = Person(o["from"]);
            if (s == null || from == null || !LeashGrammar.Stickers.Contains(s)) continue;
            list.Add(new Sticker(s, from, Time(o["at"]) ?? DateTimeOffset.MinValue));
        }
        return list;
    }

    private static List<WeekDay> Week(JToken? t)
    {
        var list = new List<WeekDay>();
        if (t is not JArray a) return list;
        foreach (var x in a)
            if (x is JObject o && Str(o["day"]) is { } d && MarkFromWire(Str(o["c"])) is { } m) list.Add(new WeekDay(d, m));
        return list;
    }

    public static MyLeash? Me(JToken? t)
    {
        if (t is not JObject o) return null;
        var holder = Person(o["holder"]);
        var intensity = IntensityFromWire(Str(o["intensity"]));
        if (holder == null || intensity == null) return null;
        return new MyLeash(
            holder, intensity.Value,
            Time(o["since"]) ?? DateTimeOffset.MinValue,
            Math.Max(1, Int(o["day"]) ?? 1),
            Time(o["dnd_until"]),
            Str(o["remote_mode"]) == "take" ? LeashRemoteMode.Take : LeashRemoteMode.Ask,
            PunishList(o["pending"], holder),
            Assignment(o["assignment"]),
            Math.Clamp(Int(o["pardons"]) ?? 0, 0, LeashGrammar.MaxPardons),
            Stickers(o["stickers"]));
    }

    public static HeldLeash? Held(JToken? t)
    {
        if (t is not JObject o) return null;
        var who = Person(o["who"]);
        var intensity = IntensityFromWire(Str(o["intensity"]));
        if (who == null || intensity == null) return null;
        return new HeldLeash(
            who, o.Value<bool?>("online") == true, intensity.Value,
            Time(o["since"]) ?? DateTimeOffset.MinValue,
            Math.Max(1, Int(o["day"]) ?? 1),
            Time(o["dnd_until"]),
            Report(o["report"]),
            Week(o["week"]),
            // PUN.from is the holder (this account); a block that leaves it out still lists the punishment.
            PunishList(o["pending"], new LeashPerson("", "", null)),
            Assignment(o["assignment"]),
            Math.Max(0, Int(o["punished_today"]) ?? 0));
    }

    public static LeashOffer? Offer(JToken? t)
    {
        if (t is not JObject o || Person(o["from"]) is not { } from) return null;
        var at = Time(o["at"]) ?? DateTimeOffset.MinValue;
        return new LeashOffer(from, at, Time(o["expires_at"]) ?? at.AddDays(7));
    }

    /// <summary>
    /// One event E. Kind-specific fields: <c>punish</c> carries <c>punishment: PUN</c>,
    /// <c>assign</c> / <c>assign_done</c> / <c>assign_missed</c> carry <c>assignment: A</c>,
    /// <c>reward</c> carries <c>reward</c> (kind), <c>sticker</c> or <c>poke</c>, <c>size</c>,
    /// <c>answered</c> carries <c>accepted</c>. Null when it does not fit.
    /// </summary>
    public static LeashEvent? Event(JToken? t)
    {
        if (t is not JObject o) return null;
        var id = Str(o["id"]);
        var kind = EventFromWire(Str(o["kind"]));
        var from = Person(o["from"]);
        var at = Time(o["at"]);
        if (string.IsNullOrEmpty(id) || kind == null || from == null || at == null) return null;
        switch (kind.Value)
        {
            case LeashEventKind.Punish:
                var p = Punishment(o["punishment"], from);
                return p == null ? null : new LeashEvent(id, kind.Value, from, at.Value, Punishment: p, Size: p.Size);
            case LeashEventKind.Assign:
            case LeashEventKind.AssignDone:
            case LeashEventKind.AssignMissed:
                return new LeashEvent(id, kind.Value, from, at.Value, Assignment: Assignment(o["assignment"]));
            case LeashEventKind.Reward:
                var r = RewardFromWire(Str(o["reward"]));
                if (r == null) return null;
                var token = r == RewardKind.Sticker ? Str(o["sticker"]) : r == RewardKind.Praise ? Str(o["poke"]) : null;
                var size = r == RewardKind.Credit ? Int(o["size"]) : null;
                if (!LeashGrammar.ValidReward(r.Value, token, size)) return null;
                return new LeashEvent(id, kind.Value, from, at.Value, Reward: r, StickerOrPoke: token, Size: size);
            case LeashEventKind.Answered:
                return new LeashEvent(id, kind.Value, from, at.Value, Accepted: o.Value<bool?>("accepted"));
            default:
                return new LeashEvent(id, kind.Value, from, at.Value);
        }
    }

    /// <summary>The whole block. Null in = nothing on the wire = <see cref="LeashSnapshot.Empty"/>.</summary>
    public static (LeashSnapshot Snapshot, IReadOnlyList<LeashEvent> Events) Block(JObject? b)
    {
        if (b == null) return (LeashSnapshot.Empty, Array.Empty<LeashEvent>());
        var holding = new List<HeldLeash>();
        if (b["holding"] is JArray ha) foreach (var x in ha) if (Held(x) is { } h) holding.Add(h);
        var offers = new List<LeashOffer>();
        if (b["offers"] is JArray oa) foreach (var x in oa) if (Offer(x) is { } of) offers.Add(of);
        var events = new List<LeashEvent>();
        if (b["events"] is JArray ea) foreach (var x in ea) if (Event(x) is { } e) events.Add(e);
        // Stable: two events with the same instant keep the server's order.
        var ordered = System.Linq.Enumerable.ToList(System.Linq.Enumerable.OrderBy(events, e => e.At));
        return (new LeashSnapshot(Me(b["me"]), holding, offers), ordered);
    }

    private static bool Contains(this IReadOnlyList<string> list, string value)
    {
        for (var i = 0; i < list.Count; i++) if (list[i] == value) return true;
        return false;
    }
}
