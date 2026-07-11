namespace ConditioningControlPanel.Core.Services.Chaos;

/// <summary>
/// Classifies whether a credited video watch counts as a "skip" in DTRH run telemetry.
/// Extracted from the inline predicate so the boundary semantics can be unit-pinned.
///
/// Byte-identical to the WPF reference <c>DtrhHostService.OnVideoWatchCredited</c>
/// (DtrhHostService.cs:618-623): a watch is a skip when the clip has a positive
/// duration AND the watched fraction is under 90%. The <c>durationSec &gt; 0</c> guard
/// also neutralises the Infinity/NaN division case — a zero or negative duration is
/// never a skip.
/// </summary>
public static class ChaosSkipClassification
{
    /// <summary>
    /// True when <paramref name="watchedSec"/> represents a skipped watch of a clip of
    /// length <paramref name="durationSec"/> — i.e. the clip had a positive duration and
    /// strictly less than 90% of it was watched. The 0.90 boundary itself is NOT a skip.
    /// </summary>
    public static bool IsSkip(double watchedSec, double durationSec)
        => durationSec > 0 && (watchedSec / durationSec) < 0.90;
}
