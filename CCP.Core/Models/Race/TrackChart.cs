using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Models.Race;

/// <summary>
/// A hypno track's chart: the energy curve, the acts and the timed events Racing Thoughts lays on
/// the road. Mirrors Resources/web/dtrh/race/CHART.md (chart JSON version 1) field for field; the
/// page reads this JSON as-is, so the names here are the wire names.
/// </summary>
public sealed class TrackChart
{
    public const int CurrentVersion = 1;

    [JsonProperty("version")] public int Version { get; set; } = CurrentVersion;
    [JsonProperty("source")] public TrackSource Source { get; set; } = new();
    [JsonProperty("analysis")] public TrackAnalysis Analysis { get; set; } = new();
    /// <summary>Seconds per energy bin.</summary>
    [JsonProperty("binSec")] public double BinSec { get; set; } = 0.5;
    /// <summary>One value per bin, 0..1, the file's 98th percentile RMS normalised to 1.</summary>
    [JsonProperty("energy")] public List<double> Energy { get; set; } = new();
    /// <summary>Contiguous, sorted; the first starts at 0 and the last ends at the duration.</summary>
    [JsonProperty("acts")] public List<TrackAct> Acts { get; set; } = new();
    /// <summary>Sorted by <see cref="TrackEvent.T"/>; ids unique within the chart.</summary>
    [JsonProperty("events")] public List<TrackEvent> Events { get; set; } = new();
    /// <summary>
    /// True on a chart a person wrote. An authored chart always wins: it is used instead of anything
    /// generated, it is never merged into, never written over and never charted again.
    /// </summary>
    [JsonProperty("hand", NullValueHandling = NullValueHandling.Ignore)] public bool? Hand { get; set; }
    /// <summary>Anything the chart carries that this build has no property for. Round trips.</summary>
    [JsonExtensionData] public IDictionary<string, JToken> Extra { get; set; } = new Dictionary<string, JToken>();
}

public sealed class TrackSource
{
    [JsonProperty("name")] public string Name { get; set; } = "";
    /// <summary>SHA1 hex of the file length (8 bytes little endian) + the first 1 MiB of the file.</summary>
    [JsonProperty("hash")] public string Hash { get; set; } = "";
    [JsonProperty("durationSec")] public double DurationSec { get; set; }
    [JsonProperty("sampleRate")] public int SampleRate { get; set; } = 16000;
    /// <summary>Optional second key for an authored chart: the stable name of the file on the CDN,
    /// derived from its url. Survives the file being renamed, which the hash does not.</summary>
    [JsonProperty("cloudId", NullValueHandling = NullValueHandling.Ignore)] public string? CloudId { get; set; }
    /// <summary>Anything the chart carries that this build has no property for. Round trips.</summary>
    [JsonExtensionData] public IDictionary<string, JToken> Extra { get; set; } = new Dictionary<string, JToken>();
}

public sealed class TrackAnalysis
{
    /// <summary>"rms-flux-v1".</summary>
    [JsonProperty("energy")] public string Energy { get; set; } = "rms-flux-v1";
    /// <summary>"vosk-v1" or "none".</summary>
    [JsonProperty("words")] public string Words { get; set; } = "none";
    [JsonProperty("lexicon")] public List<string> Lexicon { get; set; } = new();
    [JsonProperty("generatedAt")] public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
    /// <summary>True on the chart posted after the energy pass and before the word pass lands.</summary>
    [JsonProperty("partial")] public bool Partial { get; set; }
    /// <summary>Anything the chart carries that this build has no property for. Round trips.</summary>
    [JsonExtensionData] public IDictionary<string, JToken> Extra { get; set; } = new Dictionary<string, JToken>();
}

/// <summary>kind: induction | deepening | triggers | mantra | build | silence | wake | free.</summary>
public sealed class TrackAct
{
    [JsonProperty("id")] public int Id { get; set; }
    [JsonProperty("t0")] public double T0 { get; set; }
    [JsonProperty("t1")] public double T1 { get; set; }
    [JsonProperty("kind")] public string Kind { get; set; } = "free";
    /// <summary>A room id from the page's consts.js ROOM_IDS.</summary>
    [JsonProperty("room")] public string Room { get; set; } = "";
    [JsonProperty("name")] public string Name { get; set; } = "";
    /// <summary>Anything the chart carries that this build has no property for. Round trips.</summary>
    [JsonExtensionData] public IDictionary<string, JToken> Extra { get; set; } = new Dictionary<string, JToken>();
}

/// <summary>
/// kind: trigger | word | count | drop | chant (words) or build | peak | release | silence (energy).
/// Optional fields serialise only when set so the wire JSON stays the shape CHART.md shows.
/// </summary>
public sealed class TrackEvent
{
    [JsonProperty("id")] public string Id { get; set; } = "";
    [JsonProperty("t")] public double T { get; set; }
    [JsonProperty("kind")] public string Kind { get; set; } = "";
    [JsonProperty("label", NullValueHandling = NullValueHandling.Ignore)] public string? Label { get; set; }
    /// <summary>0..1; 1 for energy events.</summary>
    [JsonProperty("conf")] public double Conf { get; set; } = 1;
    /// <summary>Seconds; 0 for point events.</summary>
    [JsonProperty("dur")] public double Dur { get; set; }
    /// <summary>0..1, scales how loud the cue is.</summary>
    [JsonProperty("weight")] public double Weight { get; set; } = 1;
    // count
    [JsonProperty("n", NullValueHandling = NullValueHandling.Ignore)] public int? N { get; set; }
    [JsonProperty("of", NullValueHandling = NullValueHandling.Ignore)] public int? Of { get; set; }
    [JsonProperty("last", NullValueHandling = NullValueHandling.Ignore)] public bool? Last { get; set; }
    // drop
    [JsonProperty("strength", NullValueHandling = NullValueHandling.Ignore)] public double? Strength { get; set; }
    // chant
    [JsonProperty("reps", NullValueHandling = NullValueHandling.Ignore)] public int? Reps { get; set; }
    [JsonProperty("period", NullValueHandling = NullValueHandling.Ignore)] public double? Period { get; set; }
    // The Track Maker's per-event author fields (`hand`, `cue`, `note`) ride along in Extra below.
    /// <summary>Anything the chart carries that this build has no property for. Round trips.</summary>
    [JsonExtensionData] public IDictionary<string, JToken> Extra { get; set; } = new Dictionary<string, JToken>();
}
