using System;

namespace ConditioningControlPanel.Services.Leash;

/// <summary>
/// How much of a leash video was really watched (a video punishment or a video assignment). Fed
/// from the page's <c>&lt;video&gt;</c> about once a second. Only forward, playback-sized steps
/// count: a jump is a seek and a negative step is a rewind, and steps while the surface is hidden
/// or minimised do not count either. Done at <see cref="CompleteShare"/> of the duration, inside a
/// 30 s to 3 h window so a pre-roll clip never counts. Same rules as the Homework watch, its own
/// file on purpose. Pure.
/// </summary>
public sealed class LeashWatchMeter
{
    /// <summary>The runner samples once a second; a step above this is a seek.</summary>
    public const double MaxStepSeconds = 2.5;
    public const double CompleteShare = 0.9;
    public const double MinDurationSeconds = 30;
    public const double MaxDurationSeconds = 10800;

    private const double SameDurationSlack = 1.0;

    private double? _lastTime;

    public double WatchedSeconds { get; private set; }
    public double DurationSeconds { get; private set; }

    public bool IsComplete =>
        DurationSeconds >= MinDurationSeconds && DurationSeconds <= MaxDurationSeconds
        && WatchedSeconds >= CompleteShare * DurationSeconds;

    /// <summary>0..100 toward done.</summary>
    public int Percent => DurationSeconds <= 0 ? 0
        : (int)Math.Clamp(Math.Floor(100 * WatchedSeconds / (CompleteShare * DurationSeconds)), 0, 100);

    public void Sample(double currentTime, double duration, bool visible)
    {
        if (!double.IsFinite(currentTime) || currentTime < 0) return;
        if (double.IsFinite(duration) && duration > 0)
        {
            if (DurationSeconds > 0 && Math.Abs(duration - DurationSeconds) > SameDurationSlack)
            {
                WatchedSeconds = 0;
                _lastTime = null;
            }
            DurationSeconds = duration;
        }
        if (_lastTime is double last && visible)
        {
            var step = currentTime - last;
            if (step > 0 && step <= MaxStepSeconds) WatchedSeconds += step;
            if (DurationSeconds > 0 && WatchedSeconds > DurationSeconds) WatchedSeconds = DurationSeconds;
        }
        _lastTime = currentTime;
    }
}
