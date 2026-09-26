using System;
using System.Collections.Generic;
using System.Globalization;
using ConditioningControlPanel.Services.Leash;

namespace ConditioningControlPanel.Controls.Leash;

/// <summary>What the gate needs to know about the world, read fresh at every check.</summary>
public readonly record struct LeashGateWorld(
    bool Due,
    bool SessionRunning,
    bool GameUp,
    bool LockCardOpen,
    bool FullscreenEffect,
    bool ModalUp,
    bool PanelAway,
    bool Snoozed);

/// <summary>
/// The pure half of every leash surface: which rows a holder may see, the preset sizes, how a
/// figure is written, which loc key words a reply. No WPF here, so the suite pins all of it.
/// </summary>
public static class LeashUiRules
{
    // ---- the grammar the sheets offer (CONTRACT "Grammar") ------------------------------

    /// <summary>Every punishment kind in the order the sheet lists them.</summary>
    public static readonly IReadOnlyList<PunishKind> PunishOrder = new[]
    {
        PunishKind.Lines, PunishKind.Pink, PunishKind.Bubbles, PunishKind.Detention, PunishKind.Video, PunishKind.Chaster,
    };

    public static readonly IReadOnlyList<AssignKind> AssignOrder = new[] { AssignKind.Minutes, AssignKind.Quests, AssignKind.Video };

    public static readonly IReadOnlyList<string> Stickers = new[] { "good", "star", "pet", "heart", "wow" };
    public static readonly IReadOnlyList<string> Praise = new[] { "good", "proud", "cute", "more" };
    public static readonly IReadOnlyList<int> CreditSizes = new[] { 900, 1800 };

    /// <summary>The lowest intensity that allows a punishment.</summary>
    public static LeashIntensity LowestFor(PunishKind k) => k switch
    {
        PunishKind.Lines or PunishKind.Pink => LeashIntensity.Soft,
        PunishKind.Chaster => LeashIntensity.Strict,
        _ => LeashIntensity.Standard,
    };

    public static bool Allowed(PunishKind k, LeashIntensity level) => (int)level >= (int)LowestFor(k);

    /// <summary>The rows a holder sees. A row the leashed side does not allow is HIDDEN, not greyed
    /// (owner call). Chaster also needs the leashed side's Chaster link.</summary>
    public static IReadOnlyList<PunishKind> VisiblePunishments(LeashIntensity level, bool chasterLinked)
    {
        var list = new List<PunishKind>();
        foreach (var k in PunishOrder)
        {
            if (!Allowed(k, level)) continue;
            if (k == PunishKind.Chaster && !chasterLinked) continue;
            list.Add(k);
        }
        return list;
    }

    public static IReadOnlyList<int> Sizes(PunishKind k) => k switch
    {
        PunishKind.Lines => new[] { 3, 5, 10 },
        PunishKind.Pink => new[] { 10, 15, 20 },
        PunishKind.Bubbles => new[] { 50, 100, 200 },
        PunishKind.Detention => new[] { 10, 20, 30 },
        PunishKind.Chaster => new[] { 900, 1800, 3600 },
        _ => new[] { 1 },
    };

    public static IReadOnlyList<int> Sizes(AssignKind k) => k switch
    {
        AssignKind.Minutes => new[] { 15, 30, 60 },
        AssignKind.Quests => new[] { 1, 2, 3 },
        _ => new[] { 1 },
    };

    public static string Key(PunishKind k) => "leash_pun_" + k.ToString().ToLowerInvariant();
    public static string Key(AssignKind k) => "leash_assign_" + k.ToString().ToLowerInvariant();
    public static string Key(RewardKind k) => "leash_rew_" + k.ToString().ToLowerInvariant();
    public static string Key(LeashIntensity i) => "leash_level_" + i.ToString().ToLowerInvariant();

    /// <summary>A size as the chip shows it: "5", "20", "+15:00".</summary>
    public static string SizeText(PunishKind k, int size) => k == PunishKind.Chaster ? "+" + Clock(size) : size.ToString(CultureInfo.InvariantCulture);

    /// <summary>The big line on the gate, as (loc key, arg).</summary>
    public static (string Key, object Arg) GateTitle(Punishment p) => p.Kind switch
    {
        PunishKind.Lines => ("leash_gate_lines", p.Size),
        PunishKind.Pink => ("leash_gate_pink", p.Size),
        PunishKind.Bubbles => ("leash_gate_bubbles", p.Size),
        PunishKind.Detention => ("leash_gate_detention", p.Size),
        PunishKind.Video => ("leash_gate_video", p.Watch?.Title ?? ""),
        _ => ("leash_gate_chaster", Clock(p.Size)),
    };

    /// <summary>The gate's one action, as a loc key.</summary>
    public static string GateActionKey(Punishment p, int done) => p.Kind switch
    {
        PunishKind.Lines => done == 0 ? "leash_gate_go_lines" : "leash_gate_go_lines_next",
        PunishKind.Video => "leash_gate_go_video",
        PunishKind.Bubbles => "leash_gate_go_bubbles",
        _ => "leash_gate_go_session",
    };

    /// <summary>How many pips the gate draws. Lines get one per card; everything else is a bar.</summary>
    public static int Pips(Punishment p) => p.Kind == PunishKind.Lines ? Math.Clamp(p.Size, 1, 10) : 0;

    // ---- figures ------------------------------------------------------------------------

    /// <summary>Daily minutes goal the ring fills toward: the open minutes assignment, else 30.</summary>
    public static int MinutesGoal(Assignment? a)
        => a is { Kind: AssignKind.Minutes, Status: AssignStatus.Open } ? a.Size : 30;

    public static double RingFraction(int minutes, int goal) => goal <= 0 ? 0 : Math.Clamp(minutes / (double)goal, 0, 1);

    /// <summary>Seconds as m:ss or h:mm:ss ("15:00", "1:00:00").</summary>
    public static string Clock(int seconds)
    {
        seconds = Math.Abs(seconds);
        var t = TimeSpan.FromSeconds(seconds);
        return t.TotalHours >= 1
            ? string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}:{2:00}", (int)t.TotalHours, t.Minutes, t.Seconds)
            : string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}", t.Minutes, t.Seconds);
    }

    /// <summary>Lock time left in two units: "3d 4h", "5h 12m", "40m".</summary>
    public static string LockLeft(int seconds)
    {
        if (seconds <= 0) return "0m";
        var t = TimeSpan.FromSeconds(seconds);
        if (t.TotalDays >= 1) return $"{(int)t.TotalDays}d {t.Hours}h";
        if (t.TotalHours >= 1) return $"{(int)t.TotalHours}h {t.Minutes}m";
        return $"{Math.Max(1, t.Minutes)}m";
    }

    /// <summary>A tab balance: "+22:30" for time owed, "-5:00" for credit, "0:00".</summary>
    public static string Tab(int seconds) => seconds > 0 ? "+" + Clock(seconds) : seconds < 0 ? "-" + Clock(seconds) : "0:00";

    /// <summary>The weekday letter under a strip cell ("20260926" -> "S").</summary>
    public static string DayLetter(string day)
    {
        if (DateTime.TryParseExact(day, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
            return d.DayOfWeek.ToString().Substring(0, 1);
        return "";
    }

    // ---- the Offer chip -----------------------------------------------------------------

    public enum OfferChip { Hidden, Offer, Greyed, Sent, Holding }

    /// <summary>What the Offer chip on a friend row shows. Online or seen in the last 7 days
    /// can be offered (CONTRACT: the client greys, the server does not check presence).</summary>
    public static OfferChip OfferState(bool available, bool holdingThem, bool offered, bool online,
        DateTimeOffset lastSeen, DateTimeOffset now, int holdingCount)
    {
        if (!available) return OfferChip.Hidden;
        if (holdingThem) return OfferChip.Holding;
        if (offered) return OfferChip.Sent;
        if (holdingCount >= 5) return OfferChip.Greyed;
        if (online) return OfferChip.Offer;
        return now - lastSeen <= TimeSpan.FromDays(7) ? OfferChip.Offer : OfferChip.Greyed;
    }

    // ---- replies and events -------------------------------------------------------------

    public static string ResultKey(LeashSendStatus s) => "leash_res_" + s switch
    {
        LeashSendStatus.NotFriends => "not_friends",
        LeashSendStatus.NotAllowed => "not_allowed",
        LeashSendStatus.TooFast => "too_fast",
        _ => s.ToString().ToLowerInvariant(),
    };

    public static bool IsGood(LeashSendStatus s)
        => s is LeashSendStatus.Sent or LeashSendStatus.Queued or LeashSendStatus.Replaced or LeashSendStatus.Already;

    /// <summary>The toast a one-shot event raises on the side that receives it, as a loc key
    /// taking the sender's name. Null = no toast (the surface itself reacts).</summary>
    public static string? EventKey(LeashEvent e) => e.Kind switch
    {
        LeashEventKind.Tug => "leash_evt_tug",
        LeashEventKind.Reward => e.Reward switch
        {
            RewardKind.Sticker => "leash_evt_sticker",
            RewardKind.Credit => "leash_evt_credit",
            RewardKind.Pardon => "leash_evt_pardon",
            _ => "leash_evt_praise",
        },
        LeashEventKind.Punish => "leash_evt_punish",
        LeashEventKind.Assign => "leash_evt_assign",
        LeashEventKind.Answered => e.Accepted == true ? "leash_evt_answered_yes" : "leash_evt_answered_no",
        LeashEventKind.Ended => "leash_evt_ended",
        LeashEventKind.AssignDone => "leash_evt_assign_done",
        LeashEventKind.AssignMissed => "leash_evt_assign_missed",
        LeashEventKind.PunishDone => "leash_evt_punish_done",
        _ => null,
    };

    /// <summary>"until 18:30" style wording for a DND end, in the viewer's local time.</summary>
    public static bool DndOn(DateTimeOffset? until, DateTimeOffset now) => until is { } u && u > now;

    public static string DndUntilText(DateTimeOffset until) => until.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture);

    /// <summary>The DND end the leashed client sends with <c>today</c>: its own local midnight.</summary>
    public static DateTimeOffset LocalMidnight(DateTimeOffset now)
    {
        var local = now.ToLocalTime();
        return new DateTimeOffset(local.Date.AddDays(1), local.Offset);
    }

    // ---- the gate -----------------------------------------------------------------------

    /// <summary>The Homework gate's busy shape: the card never lands on a busy user.</summary>
    public static bool Busy(in LeashGateWorld w) =>
        w.SessionRunning || w.GameUp || w.LockCardOpen || w.FullscreenEffect || w.ModalUp || w.PanelAway || w.Snoozed;

    public static bool ShouldShow(in LeashGateWorld w) => w.Due && !Busy(w);

    /// <summary>After a panic press the gate stands back this long, then returns at the next idle moment.</summary>
    public static readonly TimeSpan PanicSnooze = TimeSpan.FromMinutes(10);
}
