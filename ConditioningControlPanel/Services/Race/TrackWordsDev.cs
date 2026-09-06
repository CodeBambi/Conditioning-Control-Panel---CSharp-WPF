using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using ConditioningControlPanel.Models.Race;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace ConditioningControlPanel.Services.Race;

/// <summary>
/// The `--race-words` dev arg: decode a file, run the word pass on it and log every event the fold
/// produces. Carries its own tiny decoder rather than waiting on TrackDecoder (PR c4), so the word
/// pass can be exercised against real audio before the rest of the stack lands.
/// </summary>
internal static class TrackWordsDev
{
    /// <summary>Decode to mono 16 kHz float PCM. MediaFoundation first, AudioFileReader as backup.</summary>
    private static TrackPcm Decode(string path)
    {
        WaveStream reader;
        try { reader = new MediaFoundationReader(path); }
        catch { reader = new AudioFileReader(path); }
        using (reader)
        {
            ISampleProvider sp = reader.ToSampleProvider();
            if (sp.WaveFormat.Channels == 2) sp = new StereoToMonoSampleProvider(sp);
            if (sp.WaveFormat.SampleRate != TrackPcm.SampleRate)
                sp = new WdlResamplingSampleProvider(sp, TrackPcm.SampleRate);

            var all = new List<float>((int)Math.Max(0, reader.TotalTime.TotalSeconds * TrackPcm.SampleRate));
            var buf = new float[TrackPcm.SampleRate];
            int n;
            while ((n = sp.Read(buf, 0, buf.Length)) > 0) all.AddRange(new ArraySegment<float>(buf, 0, n));
            var samples = all.ToArray();
            return new TrackPcm
            {
                Mono16k = samples, DurationSec = samples.Length / (double)TrackPcm.SampleRate,
                Name = Path.GetFileName(path), Path = path
            };
        }
    }

    /// <summary>
    /// Decode, build the lexicon, spot, fold onto a one-act chart and log the lot. Never throws:
    /// this runs from App.OnStartup, where a bad path is a log line and not a crash dialog.
    /// </summary>
    public static void Run(string path)
    {
        try
        {
            if (!File.Exists(path)) { App.Logger?.Information("--race-words: no such file {Path}", path); return; }

            var pcm = Decode(path);
            var lexicon = TrackLexicon.Build();
            App.Logger?.Information("--race-words: {Name} {Dur:F1}s, lexicon {Count} phrases, model {Model}",
                pcm.Name, pcm.DurationSec, lexicon.Count, TrackWordSpotter.ModelAvailable ? "present" : "absent");

            var words = TrackWordSpotter.Spot(pcm, lexicon, new Progress<double>(), CancellationToken.None);

            // The minimal chart the fold needs: no energy pass, one free act over the whole file.
            var chart = new TrackChart
            {
                Source = new TrackSource { Name = pcm.Name, DurationSec = pcm.DurationSec },
                Acts = { new TrackAct { Id = 0, T0 = 0, T1 = pcm.DurationSec, Kind = "free", Name = "the file" } }
            };
            TrackChartWords.Apply(chart, words, lexicon);

            foreach (var e in chart.Events)
                App.Logger?.Information("--race-words: t={T:F1} kind={Kind} label={Label} conf={Conf:F2}{Extra}",
                    e.T, e.Kind, e.Label ?? "", e.Conf, Extra(e));
            foreach (var a in chart.Acts)
                App.Logger?.Information("--race-words: act {Id} {T0:F1}..{T1:F1} kind={Kind} room={Room}", a.Id, a.T0, a.T1, a.Kind, a.Room);
            App.Logger?.Information("--race-words: {Count} events, analysis.words={Words}", chart.Events.Count, chart.Analysis.Words);
        }
        catch (Exception ex) { App.Logger?.Error(ex, "--race-words failed"); }
    }

    /// <summary>The kind-specific tail of a log line, so counts and chants read at a glance.</summary>
    private static string Extra(TrackEvent e) =>
        e.N.HasValue ? $" n={e.N} of={e.Of} last={e.Last}"
        : e.Strength.HasValue ? $" strength={e.Strength:F2}"
        : e.Reps.HasValue ? $" reps={e.Reps} period={e.Period:F2} dur={e.Dur:F1}"
        : "";
}
