using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using ConditioningControlPanel.Models.Race;

namespace ConditioningControlPanel.Services.Race;

/// <summary>
/// The energy pass: RMS per bin normalised to the 98th percentile, then build / peak / release /
/// silence events and a first cut of the acts from the energy shape alone (CHART.md thresholds).
/// </summary>
public static class TrackAnalyzer
{
    public const double BinSec = 0.5;

    /// <summary>A build is a rise held for at least this long.</summary>
    private const double BuildMinSec = 8.0;
    /// <summary>...that gains more than this much of full scale across the rise.</summary>
    private const double BuildRise = 0.25;
    /// <summary>A rise survives a dip this deep below its best level so far; RMS is never smooth.</summary>
    private const double BuildSlack = 0.05;
    /// <summary>A rise that has stopped making new highs for this long has ended, plateau or not.</summary>
    private const double BuildStallSec = 4.0;
    private const double PeakLevel = 0.8;
    private const double PeakGapSec = 4.0;
    private const double ReleaseDrop = 0.4;
    private const double ReleaseWindowSec = 3.0;
    private const double SilenceLevel = 0.06;
    private const double SilenceMinSec = 3.0;

    /// <summary>
    /// Charts <paramref name="pcm"/> from its energy alone: the normalised curve, the four energy
    /// event kinds and the acts. The words pass (PR c5) folds its own events into the same chart.
    /// </summary>
    public static TrackChart Energy(TrackPcm pcm, IProgress<double>? progress, CancellationToken ct)
    {
        if (pcm == null) throw new ArgumentNullException(nameof(pcm));

        var curve = Curve(pcm, progress, ct);
        var events = Events(curve);
        var duration = Math.Round(pcm.DurationSec, 3);

        var chart = new TrackChart
        {
            Version = TrackChart.CurrentVersion,
            BinSec = BinSec,
            Source = new TrackSource
            {
                Name = pcm.Name,
                Hash = pcm.Hash,
                DurationSec = duration,
                SampleRate = TrackPcm.SampleRate
            },
            Analysis = new TrackAnalysis
            {
                Energy = "rms-flux-v1",
                Words = "none",
                Lexicon = new List<string>(),
                GeneratedAt = DateTime.UtcNow,
                Partial = false
            },
            Energy = new List<double>(curve.Length),
            Acts = TrackActs.Build(curve, duration, events),
            Events = events
        };

        foreach (var v in curve) chart.Energy.Add(Round(v));
        progress?.Report(1);
        return chart;
    }

    /// <summary>
    /// RMS per <see cref="BinSec"/> bin, normalised so the file's 98th percentile is 1 and clamped
    /// into 0..1. Length is ceil(durationSec / BinSec), the length CHART.md promises the page.
    /// </summary>
    private static double[] Curve(TrackPcm pcm, IProgress<double>? progress, CancellationToken ct)
    {
        int binSamples = (int)Math.Round(BinSec * TrackPcm.SampleRate);
        int bins = Math.Max(1, (int)Math.Ceiling(pcm.Mono16k.Length / (double)binSamples));
        var rms = new double[bins];

        for (int b = 0; b < bins; b++)
        {
            ct.ThrowIfCancellationRequested();
            int from = b * binSamples;
            int to = Math.Min(from + binSamples, pcm.Mono16k.Length);
            double sum = 0;
            for (int i = from; i < to; i++) sum += (double)pcm.Mono16k[i] * pcm.Mono16k[i];
            rms[b] = to > from ? Math.Sqrt(sum / (to - from)) : 0;
            if (progress != null && (b & 0xFF) == 0) progress.Report(b / (double)bins);
        }

        var sorted = (double[])rms.Clone();
        Array.Sort(sorted);
        double p98 = sorted[Math.Clamp((int)Math.Round(0.98 * (sorted.Length - 1)), 0, sorted.Length - 1)];
        // A near-silent file (or one whose top 2 percent is all there is) still has to normalise to
        // something; fall back to the loudest bin, and to a flat zero curve when even that is silent.
        if (p98 <= 1e-9) p98 = sorted[sorted.Length - 1];
        if (p98 <= 1e-9) return rms;

        for (int b = 0; b < bins; b++) rms[b] = Math.Clamp(rms[b] / p98, 0, 1);
        return rms;
    }

    /// <summary>The four energy event kinds, sorted by time with ids "e1", "e2", ... in that order.</summary>
    private static List<TrackEvent> Events(double[] e)
    {
        var events = new List<TrackEvent>();
        Builds(e, events);
        var peaks = Peaks(e);
        foreach (int p in peaks) events.Add(Point("peak", p));
        Releases(e, peaks, events);
        Silences(e, events);

        events.Sort((a, b) => a.T != b.T
            ? a.T.CompareTo(b.T)
            : string.CompareOrdinal(a.Kind, b.Kind));
        for (int i = 0; i < events.Count; i++)
            events[i].Id = "e" + (i + 1).ToString(CultureInfo.InvariantCulture);
        return events;
    }

    /// <summary>RMS rising for 8 s or more and gaining more than 0.25 of full scale: one per rise.</summary>
    private static void Builds(double[] e, List<TrackEvent> events)
    {
        int minBins = (int)Math.Round(BuildMinSec / BinSec);
        int stallBins = (int)Math.Round(BuildStallSec / BinSec);
        int i = 0;
        while (i < e.Length - 1)
        {
            // Walk forward while the curve keeps making new highs, allowing a shallow dip, and take
            // the highest bin reached as the top of the rise. The stall window is what stops a long
            // flat plateau from being read as one enormous build because it crept up a hundredth.
            double best = e[i];
            int top = i;
            int j = i + 1;
            while (j < e.Length && e[j] >= best - BuildSlack && j - top <= stallBins)
            {
                if (e[j] >= best) { best = e[j]; top = j; }
                j++;
            }
            if (top - i >= minBins && e[top] - e[i] > BuildRise)
                events.Add(new TrackEvent
                {
                    Kind = "build",
                    T = Round(i * BinSec),
                    Dur = Round((top - i) * BinSec)
                });
            i = Math.Max(top, i + 1);
        }
    }

    /// <summary>Local maxima above 0.8, thinned to the loudest one inside each 4 s window.</summary>
    private static List<int> Peaks(double[] e)
    {
        var peaks = new List<int>();
        int gapBins = (int)Math.Round(PeakGapSec / BinSec);
        for (int i = 1; i < e.Length - 1; i++)
        {
            if (e[i] <= PeakLevel || e[i] < e[i - 1] || e[i] < e[i + 1]) continue;
            if (peaks.Count > 0 && i - peaks[peaks.Count - 1] < gapBins)
            {
                if (e[i] > e[peaks[peaks.Count - 1]]) peaks[peaks.Count - 1] = i;
                continue;
            }
            peaks.Add(i);
        }
        return peaks;
    }

    /// <summary>A fall of more than 0.4 within 3 s of a peak, at the bin where the fall lands.</summary>
    private static void Releases(double[] e, List<int> peaks, List<TrackEvent> events)
    {
        int windowBins = (int)Math.Round(ReleaseWindowSec / BinSec);
        foreach (int p in peaks)
        {
            for (int i = p + 1; i <= p + windowBins && i < e.Length; i++)
            {
                if (e[p] - e[i] <= ReleaseDrop) continue;
                events.Add(Point("release", i));
                break;
            }
        }
    }

    /// <summary>Runs under 0.06 lasting 3 s or more; t is the start of the run.</summary>
    private static void Silences(double[] e, List<TrackEvent> events)
    {
        int i = 0;
        while (i < e.Length)
        {
            if (e[i] >= SilenceLevel) { i++; continue; }
            int start = i;
            while (i < e.Length && e[i] < SilenceLevel) i++;
            double dur = (i - start) * BinSec;
            if (dur >= SilenceMinSec)
                events.Add(new TrackEvent { Kind = "silence", T = Round(start * BinSec), Dur = Round(dur) });
        }
    }

    private static TrackEvent Point(string kind, int bin)
        => new TrackEvent { Kind = kind, T = Round(bin * BinSec) };

    /// <summary>Three decimals everywhere: the chart travels as JSON and nobody needs bin 1743.0000001.</summary>
    internal static double Round(double v) => Math.Round(v, 3, MidpointRounding.AwayFromZero);
}
