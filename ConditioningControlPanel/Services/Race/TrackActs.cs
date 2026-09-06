using System;
using System.Collections.Generic;
using ConditioningControlPanel.Models.Race;

namespace ConditioningControlPanel.Services.Race;

/// <summary>
/// Acts cut from the energy shape alone: split at the long silences and at the biggest sustained
/// level changes, then labelled by position and by which way the act's own energy leans. The words
/// pass (PR c5) upgrades kinds afterwards; the rooms follow CHART.md's ACT_ROOM mapping and never
/// repeat back to back.
/// </summary>
internal static class TrackActs
{
    /// <summary>A silence this long is a scene change, not a breath.</summary>
    private const double SilenceCutSec = 6.0;
    /// <summary>Roughly one act per this many seconds: a 30 minute file lands at 7, a 3 minute one at 2.</summary>
    private const double SecondsPerAct = 270.0;
    private const int MaxActs = 8;
    /// <summary>How much an act's second half has to lean before it is a build or a deepening.</summary>
    private const double LeanLevel = 0.03;

    /// <summary>ACT_ROOM from CHART.md; the rotation below walks this order to break a repeat.</summary>
    private static readonly string[] RoomOrder =
        { "teagarden", "undertow", "toybox", "chapel", "mirrors", "greyward", "coronation", "casino" };

    private static readonly Dictionary<string, string> ActRoom = new()
    {
        ["induction"] = "teagarden",
        ["deepening"] = "undertow",
        ["triggers"] = "toybox",
        ["mantra"] = "chapel",
        ["build"] = "mirrors",
        ["silence"] = "greyward",
        ["wake"] = "coronation",
        ["free"] = "casino"
    };

    private static readonly Dictionary<string, string> ActName = new()
    {
        ["induction"] = "the settle",
        ["deepening"] = "the undertow",
        ["triggers"] = "the toybox",
        ["mantra"] = "the chant",
        ["build"] = "the climb",
        ["silence"] = "the hush",
        ["wake"] = "the waking",
        ["free"] = "the drift"
    };

    /// <summary>Contiguous acts covering 0 .. <paramref name="durationSec"/>, in order.</summary>
    internal static List<TrackAct> Build(double[] e, double durationSec, List<TrackEvent> events)
    {
        int n = e.Length;
        int target = Math.Clamp((int)Math.Round(durationSec / SecondsPerAct), 2, MaxActs);
        double minActSec = Math.Max(20.0, Math.Min(45.0, durationSec / (target + 1)));
        int minBins = Math.Max(1, (int)Math.Round(minActSec / TrackAnalyzer.BinSec));

        var cuts = Cuts(e, events, target, minBins, n);
        var acts = new List<TrackAct>();
        double middle = Mean(e, (int)(n * 0.25), (int)(n * 0.75));
        double tail = Mean(e, (int)(n * 0.9), n);

        for (int i = 0; i <= cuts.Count; i++)
        {
            int from = i == 0 ? 0 : cuts[i - 1];
            int to = i == cuts.Count ? n : cuts[i];
            var act = new TrackAct
            {
                Id = i,
                T0 = TrackAnalyzer.Round(from * TrackAnalyzer.BinSec),
                T1 = i == cuts.Count ? durationSec : TrackAnalyzer.Round(to * TrackAnalyzer.BinSec),
                Kind = "free"
            };
            if (i == 0) act.Kind = "induction";
            else if (i == cuts.Count) act.Kind = tail < middle ? "wake" : "free";
            else act.Kind = Lean(e, from, to);
            acts.Add(act);
        }

        string previous = "";
        foreach (var act in acts)
        {
            act.Room = Room(act.Kind, previous);
            act.Name = ActName.TryGetValue(act.Kind, out var name) ? name : "the drift";
            previous = act.Room;
        }
        return acts;
    }

    /// <summary>
    /// The bins to split at: the longest silences first (they are unarguable scene changes), then
    /// the biggest before-and-after level changes until the file has the act count its length asks
    /// for. Every cut keeps at least one minimum act length from its neighbours and from both ends.
    /// </summary>
    private static List<int> Cuts(double[] e, List<TrackEvent> events, int target, int minBins, int n)
    {
        var cuts = new List<int>();
        var silences = new List<TrackEvent>();
        foreach (var ev in events)
            if (ev.Kind == "silence" && ev.Dur >= SilenceCutSec) silences.Add(ev);
        silences.Sort((a, b) => b.Dur.CompareTo(a.Dur));

        foreach (var ev in silences)
        {
            if (cuts.Count >= target - 1) break;
            int bin = (int)Math.Round(ev.T / TrackAnalyzer.BinSec);
            if (Fits(cuts, bin, minBins, n)) cuts.Add(bin);
        }

        while (cuts.Count < target - 1)
        {
            int bin = BiggestChange(e, cuts, minBins, n);
            if (bin < 0) break;
            cuts.Add(bin);
        }

        cuts.Sort();
        return cuts;
    }

    /// <summary>The bin whose 30 s before and 30 s after differ most, among the bins still free.</summary>
    private static int BiggestChange(double[] e, List<int> cuts, int minBins, int n)
    {
        int window = Math.Min(60, minBins);
        int best = -1;
        double bestScore = -1;
        for (int i = minBins; i <= n - minBins; i++)
        {
            if (!Fits(cuts, i, minBins, n)) continue;
            double score = Math.Abs(Mean(e, i - window, i) - Mean(e, i, i + window));
            if (score <= bestScore) continue;
            bestScore = score;
            best = i;
        }
        return best;
    }

    private static bool Fits(List<int> cuts, int bin, int minBins, int n)
    {
        if (bin < minBins || bin > n - minBins) return false;
        foreach (int c in cuts) if (Math.Abs(c - bin) < minBins) return false;
        return true;
    }

    /// <summary>Which way the act leans: rising is a build, falling a deepening, flat a free run.</summary>
    private static string Lean(double[] e, int from, int to)
    {
        int mid = from + (to - from) / 2;
        double delta = Mean(e, mid, to) - Mean(e, from, mid);
        if (delta > LeanLevel) return "build";
        if (delta < -LeanLevel) return "deepening";
        return "free";
    }

    /// <summary>The act's room, rotated on along ROOM_IDS when it would repeat the last one.</summary>
    private static string Room(string kind, string previous)
    {
        string room = ActRoom.TryGetValue(kind, out var mapped) ? mapped : "casino";
        if (room != previous) return room;
        int at = Array.IndexOf(RoomOrder, room);
        for (int step = 1; step <= RoomOrder.Length; step++)
        {
            string next = RoomOrder[(at + step + RoomOrder.Length) % RoomOrder.Length];
            if (next != previous) return next;
        }
        return room;
    }

    private static double Mean(double[] e, int from, int to)
    {
        from = Math.Clamp(from, 0, e.Length);
        to = Math.Clamp(to, 0, e.Length);
        if (to <= from) return 0;
        double sum = 0;
        for (int i = from; i < to; i++) sum += e[i];
        return sum / (to - from);
    }
}
