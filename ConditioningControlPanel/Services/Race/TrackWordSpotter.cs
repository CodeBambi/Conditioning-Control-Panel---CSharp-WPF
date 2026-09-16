using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using ConditioningControlPanel.Models.Race;
using Newtonsoft.Json.Linq;
using Vosk;

namespace ConditioningControlPanel.Services.Race;

/// <summary>
/// The word pass on the bundled Vosk model (grammar recognizer over the lexicon, SetWords(true),
/// 8000-sample chunks, word timings from result[]). No model on disk = an empty list and the chart
/// says analysis.words = "none"; never an exception to the caller.
/// </summary>
public static class TrackWordSpotter
{
    /// <summary>Half a second of 16 kHz mono: the chunk Vosk likes and a fine progress step.</summary>
    private const int ChunkSamples = 8000;
    /// <summary>Below this the recognizer is guessing, and a guessed trigger is a wrong bubble.</summary>
    private const double MinConf = 0.5;

    private static bool _noModelLogged;

    /// <summary>True when a Vosk model is unpacked under SpeechService.ModelRoot.</summary>
    public static bool ModelAvailable => ResolveModelDir() != null;

    /// <summary>
    /// Run the whole file through a grammar recognizer and return the phrases it heard, in time
    /// order, as "word" (a CHART.md structure word) or "trigger" (a mod or settings phrase) events.
    /// Ids are left empty: <see cref="TrackChartWords.Apply"/> numbers the chart.
    /// Never throws; a missing model, a broken model or a cancelled pass all return what is in hand.
    /// </summary>
    public static List<TrackEvent> Spot(TrackPcm pcm, IReadOnlyList<string> lexicon, IProgress<double>? progress, CancellationToken ct)
    {
        var events = new List<TrackEvent>();
        if (pcm == null || pcm.Mono16k.Length == 0 || lexicon == null || lexicon.Count == 0) return events;

        var dir = ResolveModelDir();
        if (dir == null)
        {
            // Once per process: no model is a normal state for anyone who never installed one,
            // and the chart says so in analysis.words rather than shouting at the user.
            if (!_noModelLogged)
            {
                _noModelLogged = true;
                App.Logger?.Information("TrackWordSpotter: no Vosk model under {Root}, the word pass is skipped", Speech.SpeechService.ModelRoot);
            }
            return events;
        }

        Model? model = null;
        try
        {
            // SpeechService keeps its own Model private and holds it for the mic, so the pass loads
            // its own copy and drops it again the moment the file is charted.
            try { Vosk.Vosk.SetLogLevel(-1); } catch { /* native lib may be absent in odd builds */ }
            model = new Model(dir);

            using var rec = BuildRecognizer(model, lexicon);
            rec.SetWords(true);

            var heard = new List<Spotted>();
            var samples = pcm.Mono16k;
            int chunks = Math.Max(1, (samples.Length + ChunkSamples - 1) / ChunkSamples);
            var bytes = new byte[ChunkSamples * 2];

            for (int c = 0; c < chunks; c++)
            {
                if (ct.IsCancellationRequested) break;

                int offset = c * ChunkSamples;
                int n = Math.Min(ChunkSamples, samples.Length - offset);
                for (int i = 0; i < n; i++)
                {
                    int s = (int)Math.Round(Math.Clamp(samples[offset + i], -1f, 1f) * 32767f);
                    bytes[i * 2] = (byte)(s & 0xFF);
                    bytes[i * 2 + 1] = (byte)((s >> 8) & 0xFF);
                }
                if (rec.AcceptWaveform(bytes, n * 2)) ReadWords(rec.Result(), heard);
                progress?.Report((c + 1) / (double)chunks);
            }
            ReadWords(rec.FinalResult(), heard);

            events = Group(heard, lexicon);
            App.Logger?.Information("TrackWordSpotter: {Count} phrases over {Dur:F1}s of audio", events.Count, pcm.DurationSec);
        }
        catch (Exception ex) { App.Logger?.Information(ex, "TrackWordSpotter: the word pass failed, charting energy only"); }
        finally { try { model?.Dispose(); } catch { } }
        return events;
    }

    /// <summary>
    /// A grammar over the lexicon, or the closest thing the model will accept. A phrase whose words
    /// are outside the model's vocabulary makes the grammar ctor throw, so the structure words are
    /// the second try and free dictation the last. The fold keeps only lexicon phrases either way,
    /// so a wider recognizer costs accuracy, never correctness.
    /// </summary>
    private static VoskRecognizer BuildRecognizer(Model model, IReadOnlyList<string> lexicon)
    {
        foreach (var attempt in new[] { lexicon, TrackLexicon.StructureWords })
        {
            try
            {
                var grammar = new JArray();
                foreach (var phrase in attempt) grammar.Add(phrase);
                grammar.Add("[unk]"); // lets Vosk say "not one of these" instead of forcing a match
                return new VoskRecognizer(model, TrackPcm.SampleRate, grammar.ToString(Newtonsoft.Json.Formatting.None));
            }
            catch (Exception ex)
            { App.Logger?.Information(ex, "TrackWordSpotter: the model refused a grammar of {Count} phrases, narrowing", attempt.Count); }
        }
        return new VoskRecognizer(model, TrackPcm.SampleRate);
    }

    /// <summary>One word as Vosk timed it.</summary>
    private readonly record struct Spotted(string Word, double Start, double End, double Conf);

    /// <summary>Pull the timed result[] entries out of a Vosk result, dropping [unk] and the unsure.</summary>
    private static void ReadWords(string json, List<Spotted> into)
    {
        try
        {
            if (JObject.Parse(json)["result"] is not JArray words) return;
            foreach (var w in words)
            {
                var text = ((string?)w["word"] ?? "").Trim().ToLowerInvariant();
                if (text.Length == 0 || text == "[unk]") continue;
                double conf = (double?)w["conf"] ?? 0;
                if (conf < MinConf) continue;
                into.Add(new Spotted(text, (double?)w["start"] ?? 0, (double?)w["end"] ?? 0, conf));
            }
        }
        catch { /* a malformed result is one lost utterance, not a failed chart */ }
    }

    /// <summary>
    /// Fold consecutive words into the longest lexicon phrase they spell ("good" + "girl" is one
    /// "good girl" trigger, not two structure words), then stamp each as a word or a trigger.
    /// </summary>
    private static List<TrackEvent> Group(List<Spotted> heard, IReadOnlyList<string> lexicon)
    {
        // Phrases indexed by their first word, longest first, so the greedy walk below prefers
        // "good girl" over "good" without backtracking.
        var byHead = new Dictionary<string, List<string[]>>(StringComparer.Ordinal);
        foreach (var phrase in lexicon)
        {
            var parts = phrase.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) continue;
            if (!byHead.TryGetValue(parts[0], out var list)) byHead[parts[0]] = list = new List<string[]>();
            list.Add(parts);
        }
        foreach (var list in byHead.Values) list.Sort((a, b) => b.Length.CompareTo(a.Length));

        var events = new List<TrackEvent>();
        for (int i = 0; i < heard.Count;)
        {
            if (!byHead.TryGetValue(heard[i].Word, out var candidates)) { i++; continue; }

            string[]? matched = null;
            foreach (var parts in candidates)
            {
                if (i + parts.Length > heard.Count) continue;
                bool ok = true;
                for (int k = 1; k < parts.Length && ok; k++) ok = heard[i + k].Word == parts[k];
                if (ok) { matched = parts; break; }
            }
            if (matched == null) { i++; continue; }

            var label = string.Join(' ', matched);
            double conf = double.MaxValue;
            for (int k = 0; k < matched.Length; k++) conf = Math.Min(conf, heard[i + k].Conf);
            events.Add(new TrackEvent
            {
                Kind = TrackLexicon.IsStructureWord(label) ? "word" : "trigger",
                T = heard[i].Start, Label = label, Conf = Math.Round(conf, 3), Weight = 1,
                Dur = Math.Max(0, heard[i + matched.Length - 1].End - heard[i].Start)
            });
            i += matched.Length;
        }
        return events;
    }

    /// <summary>
    /// The same model SpeechService picks, resolved by the same code: the root itself or a nested
    /// unpack, grammar-capable model first. This pass is nothing but grammar, so it wants that pick.
    /// </summary>
    private static string? ResolveModelDir()
    {
        try { return Speech.SpeechService.ResolveModelDir(); }
        catch { return null; }
    }
}

/// <summary>
/// Folds spotted words into a chart: trigger / word / count / drop / chant events per CHART.md,
/// upgrades the acts, sets analysis.words and analysis.lexicon.
/// </summary>
public static class TrackChartWords
{
    /// <summary>Numbers this far apart still belong to the same countdown.</summary>
    private const double CountGapSec = 2.5;
    /// <summary>A phrase repeated inside this window is a chant, not three separate words.</summary>
    private const double ChantWindowSec = 25;
    /// <summary>How long after a count "now" still means "drop".</summary>
    private const double NowAfterCountSec = 1.5;

    /// <summary>CHART.md ACT_ROOM: the room each act kind opens in before the no-repeat rule.</summary>
    private static readonly Dictionary<string, string> ActRoom = new(StringComparer.Ordinal)
    {
        ["induction"] = "teagarden", ["deepening"] = "undertow", ["triggers"] = "toybox",
        ["mantra"] = "chapel", ["build"] = "mirrors", ["silence"] = "greyward",
        ["wake"] = "coronation", ["free"] = "casino"
    };

    /// <summary>The rooms in ACT_ROOM order, walked when the no-repeat rule has to move an act.</summary>
    private static readonly string[] RoomRing =
        { "teagarden", "undertow", "toybox", "chapel", "mirrors", "greyward", "coronation", "casino" };

    private static readonly Dictionary<string, int> Numbers = new(StringComparer.Ordinal)
    {
        ["zero"] = 0, ["one"] = 1, ["two"] = 2, ["three"] = 3, ["four"] = 4, ["five"] = 5,
        ["six"] = 6, ["seven"] = 7, ["eight"] = 8, ["nine"] = 9, ["ten"] = 10
    };

    /// <summary>
    /// Fold a word pass into a chart: countdowns become count runs ending in a drop, drop words
    /// become drops, a phrase repeated three times in 25 s becomes one chant, the acts are upgraded
    /// and re-roomed, every event renumbered e1..eN in time order. Never throws.
    /// </summary>
    public static void Apply(TrackChart chart, List<TrackEvent> words, IReadOnlyList<string> lexicon)
    {
        if (chart == null) return;
        try
        {
            var spoken = (words ?? new List<TrackEvent>())
                .Where(e => e != null && !string.IsNullOrEmpty(e.Label)).OrderBy(e => e.T).ToList();

            var folded = Fold(spoken);

            // Keep the energy pass's events and drop any earlier word pass, so re-charting a file
            // with a wider lexicon replaces the words instead of stacking a second set on them.
            var merged = chart.Events.Where(e => e.Kind is "build" or "peak" or "release" or "silence").ToList();
            merged.AddRange(folded);
            merged.Sort((a, b) => a.T.CompareTo(b.T));
            for (int i = 0; i < merged.Count; i++) merged[i].Id = "e" + (i + 1);
            chart.Events = merged;

            UpgradeActs(chart, folded);

            chart.Analysis.Words = folded.Count == 0 && !TrackWordSpotter.ModelAvailable ? "none" : "vosk-v1";
            chart.Analysis.Lexicon = lexicon?.ToList() ?? new List<string>();
            chart.Analysis.Partial = false;
        }
        catch (Exception ex) { App.Logger?.Information(ex, "TrackChartWords: fold failed, the chart keeps its energy pass"); }
    }

    /// <summary>
    /// The CHART.md event table in order: countdown runs first (a spoken number belongs to its
    /// count before anything else), then chants (a repeated phrase is one cue, not three), then the
    /// leftover drop words. Everything not claimed stays the word or trigger it came in as.
    /// </summary>
    private static List<TrackEvent> Fold(List<TrackEvent> spoken)
    {
        var claimed = new bool[spoken.Count];
        var outEvents = new List<TrackEvent>();
        var counts = new List<TrackEvent>();

        // 1. Countdown runs: numbers within 2.5 s of each other, all descending or all ascending.
        for (int i = 0; i < spoken.Count;)
        {
            if (!IsNumber(spoken[i], out int first)) { i++; continue; }
            int j = i + 1, dir = 0, prev = first;
            while (j < spoken.Count && IsNumber(spoken[j], out int n)
                   && spoken[j].T - spoken[j - 1].T <= CountGapSec && n != prev)
            {
                int step = Math.Sign(n - prev);
                if (dir == 0) dir = step;
                else if (step != dir) break;
                prev = n; j++;
            }
            int len = j - i;
            if (len < 2) { i++; continue; }

            for (int k = i; k < j; k++)
            {
                IsNumber(spoken[k], out int n);
                claimed[k] = true;
                var ev = spoken[k];
                ev.Kind = "count";
                ev.Label = n.ToString(System.Globalization.CultureInfo.InvariantCulture);
                ev.N = n; ev.Of = len; ev.Last = k == j - 1;
                outEvents.Add(ev);
                counts.Add(ev);
            }
            // The end of a run is a drop, and a longer count earns a harder one.
            outEvents.Add(new TrackEvent
            {
                Kind = "drop", T = spoken[j - 1].T, Conf = spoken[j - 1].Conf,
                Weight = 1, Strength = Math.Min(1, 0.6 + 0.1 * len)
            });
            i = j;
        }

        // 2. Chants: the same phrase three or more times inside 25 s becomes one event.
        var byLabel = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        for (int i = 0; i < spoken.Count; i++)
        {
            if (claimed[i]) continue;
            var label = spoken[i].Label!;
            if (!byLabel.TryGetValue(label, out var idx)) byLabel[label] = idx = new List<int>();
            idx.Add(i);
        }
        foreach (var (label, idx) in byLabel)
        {
            for (int a = 0; a < idx.Count;)
            {
                int b = a + 1;
                while (b < idx.Count && spoken[idx[b]].T - spoken[idx[a]].T <= ChantWindowSec) b++;
                int reps = b - a;
                if (reps < 3) { a++; continue; }

                double t0 = spoken[idx[a]].T, t1 = spoken[idx[b - 1]].T;
                double conf = 1;
                for (int k = a; k < b; k++) { claimed[idx[k]] = true; conf = Math.Min(conf, spoken[idx[k]].Conf); }
                outEvents.Add(new TrackEvent
                {
                    Kind = "chant", T = t0, Label = label, Conf = Math.Round(conf, 3),
                    Dur = Math.Max(0, t1 - t0), Weight = 1, Reps = reps,
                    Period = reps > 1 ? Math.Round((t1 - t0) / (reps - 1), 3) : 0
                });
                a = b;
            }
        }

        // 3. Drop words: a leftover DROP_WORDS hit becomes the drop itself rather than a treat on
        //    top of one. "now" only counts when a count just landed, per CHART.md.
        for (int i = 0; i < spoken.Count; i++)
        {
            if (claimed[i]) continue;
            var ev = spoken[i];
            var label = ev.Label!;
            bool isDrop = ev.Kind == "word" && TrackLexicon.IsDropWord(label);
            if (isDrop && label == "now")
                isDrop = counts.Any(c => ev.T - c.T is > 0 and <= NowAfterCountSec);
            // A word right on the heels of a countdown's own drop would double the jump.
            if (isDrop && outEvents.Any(d => d.Kind == "drop" && ev.T - d.T is >= 0 and <= NowAfterCountSec))
                isDrop = false;
            if (isDrop) { ev.Kind = "drop"; ev.Strength = Math.Min(1, 0.5 * ev.Conf); }
            outEvents.Add(ev);
        }

        outEvents.Sort((a, b) => a.T.CompareTo(b.T));
        return outEvents;
    }

    private static bool IsNumber(TrackEvent ev, out int n) =>
        Numbers.TryGetValue(ev.Kind == "word" ? ev.Label ?? "" : "", out n);

    /// <summary>
    /// The words pass upgrade of the energy pass's acts, then the rooms. An act is judged only on
    /// the events inside it, and the first drop is left where the energy pass put it: the settle at
    /// the top of a file is an induction even when it ends on the word "drop".
    /// </summary>
    private static void UpgradeActs(TrackChart chart, List<TrackEvent> folded)
    {
        if (chart.Acts.Count == 0) return;
        double duration = Math.Max(chart.Source.DurationSec, chart.Acts[^1].T1);
        double lateFrom = duration * 0.88; // CHART.md's last 12 percent
        var firstDrop = folded.FirstOrDefault(e => e.Kind == "drop");

        foreach (var act in chart.Acts)
        {
            var inAct = folded.Where(e => e.T >= act.T0 && e.T <= act.T1).ToList();
            if (inAct.Count == 0) continue;

            int triggers = inAct.Count(e => e.Kind == "trigger");
            int chants = inAct.Count(e => e.Kind == "chant");
            bool wakeLate = inAct.Any(e => e.T >= lateFrom && e.Label is "wake" or "awake");
            bool countdownDrop = inAct.Any(e => e.Kind == "count" && e.Last == true) && inAct.Any(e => e.Kind == "drop");
            bool holdsFirstDrop = firstDrop != null && firstDrop.T >= act.T0 && firstDrop.T <= act.T1;

            // CHART.md's thresholds, in the order a listener would hear them. A chant counts for
            // the three-odd repeats it swallowed, so one chant does not lose an act full of
            // triggers, and a single trigger does not lose an act that is plainly a mantra.
            if (wakeLate) act.Kind = "wake";
            else if (holdsFirstDrop && act.Kind == "induction") { /* the settle stays a settle */ }
            else if (countdownDrop) act.Kind = "deepening";
            else if (chants > 0 && chants * 3 >= triggers) act.Kind = "mantra";
            else if (triggers >= 3) act.Kind = "triggers";
        }

        // Rooms come from the kind, except that two acts in a row never open the same door: the
        // drive out of a gate should look like somewhere new even when the chart says otherwise.
        string prev = "";
        foreach (var act in chart.Acts)
        {
            var room = ActRoom.TryGetValue(act.Kind, out var r) ? r : "casino";
            if (room == prev)
            {
                int at = Array.IndexOf(RoomRing, room);
                for (int k = 1; k <= RoomRing.Length; k++)
                {
                    var next = RoomRing[(at + k) % RoomRing.Length];
                    if (next != prev) { room = next; break; }
                }
            }
            act.Room = room;
            prev = room;
        }
    }
}
