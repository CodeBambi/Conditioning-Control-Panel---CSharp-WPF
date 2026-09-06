using System;
using System.Collections.Generic;
using System.Threading;
using ConditioningControlPanel.Models.Race;

namespace ConditioningControlPanel.Services.Race;

/// <summary>
/// The word pass on the bundled Vosk model (grammar recognizer over the lexicon, SetWords(true),
/// 8000-sample chunks, word timings from result[]). No model on disk = an empty list and the chart
/// says analysis.words = "none"; never an exception to the caller. Filled in by PR c5.
/// </summary>
public static class TrackWordSpotter
{
    public static bool ModelAvailable
        => throw new NotImplementedException("PR c5: TrackWordSpotter.ModelAvailable");

    public static List<TrackEvent> Spot(TrackPcm pcm, IReadOnlyList<string> lexicon, IProgress<double>? progress, CancellationToken ct)
        => throw new NotImplementedException("PR c5: TrackWordSpotter.Spot");
}

/// <summary>
/// Folds spotted words into a chart: trigger / word / count / drop / chant events per CHART.md,
/// upgrades the acts, sets analysis.words and analysis.lexicon. Filled in by PR c5.
/// </summary>
public static class TrackChartWords
{
    public static void Apply(TrackChart chart, List<TrackEvent> words, IReadOnlyList<string> lexicon)
        => throw new NotImplementedException("PR c5: TrackChartWords.Apply");
}
