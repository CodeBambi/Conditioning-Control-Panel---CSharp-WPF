using System;
using System.Collections.Generic;

namespace ConditioningControlPanel.Services.Leash;

/// <summary>Every leash sound, by meaning (the file behind each lives in LeashFx).</summary>
public enum LeashCue
{
    Jingle, Snap, Sent, Stamp,
    Ask, Refused, Gift, Scold, Assigned, Done, Denied, Tick, Cut, CapReached,
}

/// <summary>
/// When a leash sound may play. Pure (the clock is passed in) so the rules are testable:
/// one global gap so a poll draining several events does not stack its cues; the mandatory
/// video silences everything; while the punishment video window is up only the cues that
/// answer the player's own hands (a refused close, the hold-to-cut tick, the cut) may play;
/// and a cut sounds once even when both the local cut and the server's "ended" arrive.
/// </summary>
public sealed class LeashSfxRules
{
    public static readonly TimeSpan MinGap = TimeSpan.FromMilliseconds(130);
    public static readonly TimeSpan CutOnce = TimeSpan.FromSeconds(3);

    private DateTime _last = DateTime.MinValue;
    private DateTime _lastCut = DateTime.MinValue;

    /// <summary>The cues that may play over the punishment video window.</summary>
    public static bool AllowedOverPunishWindow(LeashCue cue) => cue is LeashCue.Denied or LeashCue.Tick or LeashCue.Cut;

    /// <summary>True = play it now (and it counts toward the gap).</summary>
    public bool TryPlay(LeashCue cue, DateTime now, bool punishWindowOpen, bool mandatoryVideo)
    {
        if (mandatoryVideo) return false;
        if (punishWindowOpen && !AllowedOverPunishWindow(cue)) return false;
        if (cue == LeashCue.Cut && now >= _lastCut && now - _lastCut < CutOnce) return false;
        if (now >= _last && now - _last < MinGap) return false;
        _last = now;
        if (cue == LeashCue.Cut) _lastCut = now;
        return true;
    }
}

/// <summary>The holder's tug button: at most one tug per friend every ten seconds, answered on
/// the client (the server's own limit replies too_fast). Pure.</summary>
public sealed class LeashTugThrottle
{
    public static readonly TimeSpan Gap = TimeSpan.FromSeconds(10);

    private readonly Dictionary<string, DateTime> _last = new(StringComparer.Ordinal);

    /// <summary>True = send the tug (and start that friend's ten seconds).</summary>
    public bool TryTug(string friendId, DateTime now)
    {
        if (_last.TryGetValue(friendId, out var t) && now >= t && now - t < Gap) return false;
        _last[friendId] = now;
        return true;
    }
}

/// <summary>The hold-to-cut tick: one per whole second of the hold, never for second zero. Pure.</summary>
public static class LeashHoldTick
{
    /// <summary>The whole second to tick now, or null. <paramref name="lastTicked"/> is the last
    /// second ticked in this hold (0 at the start of a hold).</summary>
    public static int? Due(TimeSpan held, int lastTicked)
    {
        if (held < TimeSpan.Zero) return null;
        var s = (int)Math.Floor(held.TotalSeconds);
        if (s < 1 || s <= lastTicked || s > (int)LeashHoldToCut.Hold.TotalSeconds) return null;
        return s;
    }
}
