using System;
using System.Threading;
using ConditioningControlPanel.Models.Race;

namespace ConditioningControlPanel.Services.Race;

/// <summary>
/// The energy pass: RMS per bin normalised to the 98th percentile, then build / peak / release /
/// silence events and a first cut of the acts from the energy shape alone (CHART.md thresholds).
/// Filled in by PR c4.
/// </summary>
public static class TrackAnalyzer
{
    public const double BinSec = 0.5;

    public static TrackChart Energy(TrackPcm pcm, IProgress<double>? progress, CancellationToken ct)
        => throw new NotImplementedException("PR c4: TrackAnalyzer.Energy");
}
