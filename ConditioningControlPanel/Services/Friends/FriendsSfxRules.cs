using System;
using System.Collections.Generic;
using System.Linq;

namespace ConditioningControlPanel.Services.Friends;

/// <summary>
/// When a friends cue may sound, as pure rules. <see cref="FriendsSfx"/> reads the world and asks.
/// Two gaps: any two cues stand at least <see cref="MinGapMs"/> apart (a double click is one
/// sound), and things that arrive from a friend (a poke, a knock, a request) stand at least
/// <see cref="IncomingGapMs"/> apart, so a held queue let go at once is one cue, not five.
/// </summary>
public sealed class FriendsSfxGate
{
    public const double MinGapMs = 130;
    public const double IncomingGapMs = 1500;

    /// <summary>What an incoming cue plays at while a session runs (owner call: half).</summary>
    public const float SessionIncomingLevel = 0.5f;

    private DateTime _lastAny = DateTime.MinValue;
    private DateTime _lastIncoming = DateTime.MinValue;

    /// <summary>True when a cue may play now; records it when so.</summary>
    public bool TryPass(DateTime now, bool incoming)
    {
        if ((now - _lastAny).TotalMilliseconds < MinGapMs) return false;
        if (incoming && (now - _lastIncoming).TotalMilliseconds < IncomingGapMs) return false;
        _lastAny = now;
        if (incoming) _lastIncoming = now;
        return true;
    }

    /// <summary>The level multiplier for a cue. Only incoming cues duck under a running session;
    /// the player's own clicks stay where they are.</summary>
    public static float Level(bool incoming, bool sessionRunning)
        => incoming && sessionRunning ? SessionIncomingLevel : 1f;
}

/// <summary>
/// Which incoming friend requests are new. The first list after a start (or after the account
/// changes) is taken as the baseline and reports nothing, so requests that were already waiting do
/// not all announce themselves at launch. Nothing is persisted.
/// </summary>
public sealed class FriendRequestWatch
{
    private HashSet<string>? _known;

    /// <summary>Forget everything: the next list is a fresh baseline.</summary>
    public void Reset() => _known = null;

    public bool Seeded => _known != null;

    /// <summary>Takes the current incoming list; returns the requests that appeared since the last
    /// list and the ids that left it. The baseline call returns two empty lists.</summary>
    public (IReadOnlyList<FriendRequest> Arrived, IReadOnlyList<string> Gone) Update(IReadOnlyList<FriendRequest>? incoming)
    {
        incoming ??= Array.Empty<FriendRequest>();
        var now = new HashSet<string>(incoming.Where(r => r != null && !string.IsNullOrEmpty(r.Id)).Select(r => r.Id), StringComparer.Ordinal);
        if (_known == null)
        {
            _known = now;
            return (Array.Empty<FriendRequest>(), Array.Empty<string>());
        }
        var arrived = new List<FriendRequest>();
        var added = new HashSet<string>(StringComparer.Ordinal);
        foreach (var r in incoming)
            if (r != null && !string.IsNullOrEmpty(r.Id) && !_known.Contains(r.Id) && added.Add(r.Id)) arrived.Add(r);
        var gone = _known.Where(id => !now.Contains(id)).ToList();
        _known = now;
        return (arrived, gone);
    }
}
