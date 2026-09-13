using System;
using System.Collections.Generic;

namespace ConditioningControlPanel.Services.BackRoom;

// THE BACK ROOM, effects half two: the Brake as code. The hero gate (Brake 2, one hero at a time)
// and the strobe pacer (no strobe over 6 Hz) are pure and clock-driven, so the rules are pinned by
// tests instead of by watching a desk run.

/// <summary>What the hero gate decided for one arriving fx.</summary>
public enum FxAdmitKind
{
    /// <summary>Play now.</summary>
    Now,
    /// <summary>Queued: play after <see cref="FxAdmission.DelayMs"/>.</summary>
    Queued,
    /// <summary>The same id is already running or waiting; it plays once for both.</summary>
    Merged,
    /// <summary>It would wait more than <see cref="FxHeroGate.MaxQueueMs"/>; dropped.</summary>
    Busy,
}

public readonly record struct FxAdmission(FxAdmitKind Kind, int DelayMs);

/// <summary>
/// One hero window at a time (CONTRACT section 4, Brake 2). An fx arriving while a hero window runs
/// is queued, merged if the same id is already running or queued, and dropped as <c>busy</c> when it
/// would wait more than four seconds. A queued hero opens its own window when it starts, so heroes
/// chain; non-heroes behind a hero all start when the stage frees and never extend the wait.
/// The decision is made at arrival, which is what lets the ack go back synchronously.
/// </summary>
public sealed class FxHeroGate
{
    public const int MaxQueueMs = 4000;

    private readonly Func<long> _nowMs;
    private readonly object _lock = new();
    // Every hero window that is running or waiting, in start order: (id, startMs, endMs).
    private readonly List<(string Id, long Start, long End)> _heroes = new();
    // Non-heroes waiting behind a hero: (id, startMs).
    private readonly List<(string Id, long Start)> _waiting = new();

    public FxHeroGate(Func<long> nowMs) => _nowMs = nowMs;

    public FxAdmission Admit(string fxId, int heroMs)
    {
        lock (_lock)
        {
            long now = _nowMs();
            _heroes.RemoveAll(h => h.End <= now);
            _waiting.RemoveAll(w => w.Start <= now);

            // The stage frees when the last running or queued hero window closes.
            long tail = _heroes.Count == 0 ? now : Math.Max(now, _heroes[^1].End);
            if (tail <= now)
            {
                if (heroMs > 0) _heroes.Add((fxId, now, now + heroMs));
                return new FxAdmission(FxAdmitKind.Now, 0);
            }

            // Overlapping wins merge into one (Brake 2): the same id already running or waiting.
            if (_heroes.Exists(h => h.Id == fxId) || _waiting.Exists(w => w.Id == fxId))
                return new FxAdmission(FxAdmitKind.Merged, 0);

            long wait = tail - now;
            if (wait > MaxQueueMs) return new FxAdmission(FxAdmitKind.Busy, 0);

            if (heroMs > 0) _heroes.Add((fxId, tail, tail + heroMs));
            else _waiting.Add((fxId, tail));
            return new FxAdmission(FxAdmitKind.Queued, (int)wait);
        }
    }

    /// <summary>Forget every running and waiting window (suspend, close).</summary>
    public void Clear()
    {
        lock (_lock) { _heroes.Clear(); _waiting.Clear(); }
    }
}

/// <summary>The families that share one on-screen channel and so share one onset clock.</summary>
public enum FxChannel
{
    Subliminal,
    Flash,
    Glitch,
}

/// <summary>
/// Brake: no strobe over 6 Hz at any intensity. Every onset on a channel is pushed back until it
/// sits at least <see cref="MinGapMs"/> after the previous one on that channel, ACROSS fx: a
/// <c>fx.sub_single</c> landing on top of a <c>fx.sub_cascade</c> is paced into the same run rather
/// than doubling its rate. An onset that is really a run (a flash-burst's staggered images) reserves
/// its whole span, so the next onset is paced from the run's LAST image, not its first.
/// </summary>
public sealed class FxStrobePacer
{
    private readonly object _lock = new();
    private readonly Dictionary<FxChannel, long> _last = new();

    public static int MinGapMs(FxChannel c) => c switch
    {
        FxChannel.Subliminal => BackRoomFxPlan.WordGapMs,
        FxChannel.Glitch => BackRoomFxPlan.GlitchWashMs,
        // Measured from the previous burst's last image. FlashService clears its busy flag as soon as
        // a burst is scheduled, so bursts can overlap unless the room spaces them itself.
        _ => 1000,
    };

    /// <summary>The absolute time the onset may happen, never earlier than <paramref name="desiredMs"/>.
    /// <paramref name="spanMs"/> is how long the onset keeps producing onsets of its own (a burst's
    /// last image); the channel stays reserved until then.</summary>
    public long Reserve(FxChannel channel, long desiredMs, int spanMs = 0)
    {
        lock (_lock)
        {
            long at = desiredMs;
            if (_last.TryGetValue(channel, out long prev)) at = Math.Max(at, prev + MinGapMs(channel));
            _last[channel] = at + Math.Max(0, spanMs);
            return at;
        }
    }

    public void Clear()
    {
        lock (_lock) _last.Clear();
    }
}
