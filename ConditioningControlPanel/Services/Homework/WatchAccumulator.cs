using System;

namespace ConditioningControlPanel.Services.Homework;

/// <summary>
/// Counts how much of the homework video was really watched. Fed from the page's
/// <c>&lt;video&gt;</c> (<see cref="Deeper.BrowserVideoTimeSource"/>) a couple of times a second.
///
/// <para>Only forward, playback-sized steps count: a delta above <see cref="MaxStepSeconds"/> is a
/// seek (or a stall followed by a jump) and a negative one is a rewind, and neither adds anything.
/// Steps taken while the watch surface is hidden or minimised do not count either. Complete at
/// <see cref="CompleteShare"/> of the duration, inside the same 30 s to 3 h window the proxy
/// enforces, so a short pre-roll clip can never hand the homework in.</para>
/// </summary>
public sealed class WatchAccumulator
{
    public const double MaxStepSeconds = 1.5;
    public const double CompleteShare = 0.9;
    public const double MinDurationSeconds = 30;
    public const double MaxDurationSeconds = 10800;

    /// <summary>A duration that moves by more than this is a different video: start over.</summary>
    private const double SameDurationSlack = 1.0;

    private double? _lastTime;

    public double WatchedSeconds { get; private set; }
    public double DurationSeconds { get; private set; }

    public bool IsComplete =>
        DurationSeconds >= MinDurationSeconds && DurationSeconds <= MaxDurationSeconds
        && WatchedSeconds >= CompleteShare * DurationSeconds;

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

    /// <summary>
    /// The browser is on the homework video: same host (a leading <c>www.</c> ignored) and same
    /// path, case-insensitive, query and fragment ignored.
    /// </summary>
    public static bool IsHomeworkPage(string? pageUrl, string? homeworkUrl)
    {
        if (!Uri.TryCreate(pageUrl, UriKind.Absolute, out var page)) return false;
        if (!Uri.TryCreate(homeworkUrl, UriKind.Absolute, out var hw)) return false;
        return string.Equals(Host(page), Host(hw), StringComparison.OrdinalIgnoreCase)
            && string.Equals(page.AbsolutePath.TrimEnd('/'), hw.AbsolutePath.TrimEnd('/'), StringComparison.OrdinalIgnoreCase);
    }

    private static string Host(Uri u) =>
        u.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? u.Host[4..] : u.Host;
}
