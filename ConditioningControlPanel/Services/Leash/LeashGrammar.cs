using System;
using System.Collections.Generic;

namespace ConditioningControlPanel.Services.Leash;

/// <summary>
/// The client-side twin of the server grammar (CONTRACT "Grammar"). Every preset the sheets offer
/// comes from here, so a holder can never pick something the wire would refuse. Pure.
/// </summary>
public static class LeashGrammar
{
    public static readonly IReadOnlyList<string> Stickers = new[] { "good", "star", "pet", "heart", "wow" };
    public static readonly IReadOnlyList<string> Praise = new[] { "good", "proud", "cute", "more" };
    public static readonly IReadOnlyList<int> CreditSizes = new[] { 900, 1800 };

    /// <summary>Pardon tokens the leashed side may hold.</summary>
    public const int MaxPardons = 3;

    /// <summary>How many a holder may hold at once.</summary>
    public const int MaxHeld = 5;

    public static IReadOnlyList<int> PunishSizes(PunishKind kind) => kind switch
    {
        PunishKind.Lines => new[] { 3, 5, 10 },
        PunishKind.Pink => new[] { 10, 15, 20 },
        PunishKind.Bubbles => new[] { 50, 100, 200 },
        PunishKind.Detention => new[] { 10, 20, 30 },
        PunishKind.Video => new[] { LeashVideoCap.Default },   // any minute 1..90 is valid, see ValidPunish
        PunishKind.Chaster => new[] { 900, 1800, 3600 },
        _ => Array.Empty<int>(),
    };

    public static IReadOnlyList<int> AssignSizes(AssignKind kind) => kind switch
    {
        AssignKind.Minutes => new[] { 15, 30, 60 },
        AssignKind.Quests => new[] { 1, 2, 3 },
        AssignKind.Video => new[] { 1 },
        _ => Array.Empty<int>(),
    };

    /// <summary>The lowest intensity that allows a punishment kind.</summary>
    public static LeashIntensity LowestIntensity(PunishKind kind) => kind switch
    {
        PunishKind.Lines or PunishKind.Pink => LeashIntensity.Soft,
        PunishKind.Bubbles or PunishKind.Detention or PunishKind.Video => LeashIntensity.Standard,
        _ => LeashIntensity.Strict,
    };

    /// <summary>Whether <paramref name="kind"/> is allowed at <paramref name="intensity"/>. The
    /// holder side HIDES what this refuses (never greys it).</summary>
    public static bool Allowed(PunishKind kind, LeashIntensity intensity) => intensity >= LowestIntensity(kind);

    public static bool ValidPunish(PunishKind kind, int size, LeashWatch? watch) =>
        (kind == PunishKind.Video ? size >= LeashVideoCap.Min && size <= LeashVideoCap.Max : Contains(PunishSizes(kind), size))
        && (kind == PunishKind.Video) == (watch != null) && (watch == null || ValidWatch(watch));

    public static bool ValidAssign(AssignKind kind, int size, LeashWatch? watch) =>
        Contains(AssignSizes(kind), size) && (kind == AssignKind.Video) == (watch != null) && (watch == null || ValidWatch(watch));

    /// <summary>The friends <c>watch</c> grammar, the two kinds a leash can carry.</summary>
    public static bool ValidWatch(LeashWatch w)
    {
        if (w == null || string.IsNullOrEmpty(w.Id)) return false;
        switch (w.Kind)
        {
            case "catalogue":
                if (w.Id.Length > 64) return false;
                foreach (var c in w.Id) if (!(char.IsAsciiLetterOrDigit(c) || c == '_' || c == '-')) return false;
                return true;
            case "ht":
                if (w.Id.Length > 8) return false;
                foreach (var c in w.Id) if (!char.IsAsciiDigit(c)) return false;
                return true;
            default:
                return false;
        }
    }

    public static bool ValidReward(RewardKind kind, string? stickerOrPoke, int? size) => kind switch
    {
        RewardKind.Sticker => stickerOrPoke != null && Contains(Stickers, stickerOrPoke),
        RewardKind.Praise => stickerOrPoke != null && Contains(Praise, stickerOrPoke),
        RewardKind.Credit => size is int s && Contains(CreditSizes, s),
        RewardKind.Pardon => true,
        _ => false,
    };

    private static bool Contains<T>(IReadOnlyList<T> list, T value)
    {
        for (var i = 0; i < list.Count; i++) if (EqualityComparer<T>.Default.Equals(list[i], value)) return true;
        return false;
    }
}
