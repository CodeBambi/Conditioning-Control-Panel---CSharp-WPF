using System;
using System.Collections.Generic;
using System.Linq;

namespace ConditioningControlPanel.Services.Companion.Asks;

/// <summary>The world as the pacing rule needs it, read once per tick by the glue.</summary>
public readonly record struct AskGate(bool Enabled, bool CompanionOn, bool Busy, DateTime? LastUserChatUtc);

/// <summary>
/// When the companion may ask on its own. At most one card per 15 minutes, never while something
/// owns the user, never within 5 minutes of their last chat line, a declined kind waits an hour,
/// and three declines in a row halve the rate for the rest of the local day. Clock injected.
/// </summary>
public sealed class AskPlanner
{
    public static readonly TimeSpan Gap = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan ChatQuiet = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan DeclineCooldown = TimeSpan.FromMinutes(60);
    public const int DeclineStreakLimit = 3;

    private readonly Func<DateTime> _utcNow;
    private readonly Func<DateTime, DateTime> _toLocal;
    private readonly Dictionary<AskKind, DateTime> _declined = new();
    private DateTime? _lastShownUtc;
    private int _declineStreak;
    private DateTime? _halvedDay;

    public AskPlanner(Func<DateTime>? utcNow = null, Func<DateTime, DateTime>? toLocal = null)
    {
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
        _toLocal = toLocal ?? (u => u.ToLocalTime());
    }

    private DateTime Today => _toLocal(_utcNow()).Date;

    /// <summary>True for the rest of the local day once three declines landed in a row.</summary>
    public bool Halved => _halvedDay == Today;

    /// <summary>The unprompted gap in force right now: doubled when the rate is halved.</summary>
    public TimeSpan CurrentGap => Halved ? Gap + Gap : Gap;

    public bool MayAsk(in AskGate gate)
    {
        if (!gate.Enabled || !gate.CompanionOn || gate.Busy) return false;
        var now = _utcNow();
        if (gate.LastUserChatUtc is DateTime chat && now - chat < ChatQuiet) return false;
        return _lastShownUtc is not DateTime last || now - last >= CurrentGap;
    }

    public bool KindReady(AskKind kind) =>
        !_declined.TryGetValue(kind, out var at) || _utcNow() - at >= DeclineCooldown;

    /// <summary>Only an unprompted card spends the gap; one the user asked for does not.</summary>
    public void NoteShown(bool unprompted)
    {
        if (unprompted) _lastShownUtc = _utcNow();
    }

    public void NoteAnswer(AskKind kind, bool declined)
    {
        if (!declined) { _declineStreak = 0; return; }
        _declined[kind] = _utcNow();
        if (++_declineStreak >= DeclineStreakLimit) _halvedDay = Today;
    }

    /// <summary>Picks a ready kind at random, or null when none is.</summary>
    public AskKind? PickKind(IEnumerable<AskKind> candidates, Random rng)
    {
        var ready = candidates.Distinct().Where(KindReady).ToArray();
        return ready.Length == 0 ? null : ready[rng.Next(ready.Length)];
    }
}
