using System;

namespace ConditioningControlPanel.Services;

/// <summary>
/// Turns "is it running?" samples taken at irregular ticks into credited minutes, measured on a
/// monotonic clock rather than counted per tick.
///
/// <para>WHY: the achievement tracking timer is a 1 s DispatcherTimer, and Windows throttles the
/// timers of a minimised or hidden app (power throttling coalesces them, sometimes to tens of
/// seconds apart). The old Takeover credit was <c>min(elapsed, 10 s)</c> per tick, so a tick that
/// arrived 40 s late credited 10 s and the "On Autopilot" quest crawled whenever the panel was
/// minimised (ccp-bugs #1327). Here every interval between two ticks that both saw it running is
/// credited in full, however late the second tick came.</para>
///
/// <para>GAP CEILING: an interval longer than <see cref="MaxGap"/> is not a slow tick, it is the
/// PC asleep or hibernating, the process suspended, or the UI thread hung. Takeover's own timers
/// did not run through that either, so such an interval credits NOTHING. Crediting it in part
/// would still be a guess: the sleep can start one second after the last tick. Losing at most one
/// throttled interval per sleep is the price of never crediting time that did not run.</para>
///
/// <para>ANTICHEAT: credit is only ever the measured time between two running samples, at most
/// <see cref="MaxGap"/> each. A clock that goes backwards credits nothing. Pass a monotonic
/// reading (a Stopwatch) so moving the system clock cannot mint minutes.</para>
/// </summary>
internal sealed class RunningTimeCredit
{
    /// <summary>
    /// Default ceiling for one interval. Three minutes is well past the longest throttled tick
    /// seen on a minimised app (tens of seconds) and short enough that a sleep can never be
    /// mistaken for running time.
    /// </summary>
    public static readonly TimeSpan DefaultMaxGap = TimeSpan.FromMinutes(3);

    private TimeSpan? _lastRunningSample;

    public RunningTimeCredit() : this(DefaultMaxGap) { }

    public RunningTimeCredit(TimeSpan maxGap)
    {
        if (maxGap <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(maxGap));
        MaxGap = maxGap;
    }

    /// <summary>Longest interval between two samples that still counts as running time.</summary>
    public TimeSpan MaxGap { get; }

    /// <summary>
    /// Record one sample. Returns the minutes to credit for the interval since the previous
    /// sample: the full interval when both samples saw it running and the interval is within
    /// <see cref="MaxGap"/>, otherwise zero.
    /// </summary>
    /// <param name="running">Whether the tracked thing is running at this sample.</param>
    /// <param name="now">A monotonic clock reading (for example <c>Stopwatch.Elapsed</c>).</param>
    public double Sample(bool running, TimeSpan now)
    {
        if (!running)
        {
            // Stopped: forget the stamp, so the next start credits only from its own first sample.
            _lastRunningSample = null;
            return 0;
        }

        var previous = _lastRunningSample;
        _lastRunningSample = now;
        if (previous is not { } since) return 0; // first sample of a run only opens the stamp

        var gap = now - since;
        if (gap <= TimeSpan.Zero) return 0;  // clock went backwards or a duplicate sample
        if (gap > MaxGap) return 0;          // sleep / hibernate / suspended: not running time
        return gap.TotalMinutes;
    }
}
