using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using System.Text.Json.Serialization;

namespace ConditioningControlPanel.Models;

/// <summary>
/// One calendar day of feature use, as whole numbers. <c>D</c> is the same local day key
/// (yyyy-MM-dd, invariant) <see cref="QuestLogEntry.D"/> uses, so a day here lines up with the
/// quest log and the streak calendar. The short names are the wire names of
/// <c>stats.feature_day_log</c> (Spiral rail wire contract 1); both serializer attributes are here
/// because the local file goes through System.Text.Json and the sync body through Newtonsoft.
/// </summary>
public class FeatureDayEntry
{
    /// <summary>The 11 counter keys, in contract order. <c>d</c> is not a counter.</summary>
    public static readonly string[] CounterKeys = { "xp", "cm", "fl", "bb", "pf", "sp", "vd", "lk", "ac", "bc", "ss" };

    [JsonProperty("d")]
    [JsonPropertyName("d")]
    public string D { get; set; } = "";

    /// <summary>XP earned.</summary>
    [JsonProperty("xp")] [JsonPropertyName("xp")] public int Xp { get; set; }
    /// <summary>Conditioning minutes.</summary>
    [JsonProperty("cm")] [JsonPropertyName("cm")] public int Cm { get; set; }
    /// <summary>Flash images shown.</summary>
    [JsonProperty("fl")] [JsonPropertyName("fl")] public int Fl { get; set; }
    /// <summary>Bubbles popped.</summary>
    [JsonProperty("bb")] [JsonPropertyName("bb")] public int Bb { get; set; }
    /// <summary>Pink filter minutes.</summary>
    [JsonProperty("pf")] [JsonPropertyName("pf")] public int Pf { get; set; }
    /// <summary>Spiral minutes.</summary>
    [JsonProperty("sp")] [JsonPropertyName("sp")] public int Sp { get; set; }
    /// <summary>Video minutes.</summary>
    [JsonProperty("vd")] [JsonPropertyName("vd")] public int Vd { get; set; }
    /// <summary>Lock cards completed.</summary>
    [JsonProperty("lk")] [JsonPropertyName("lk")] public int Lk { get; set; }
    /// <summary>Attention checks passed.</summary>
    [JsonProperty("ac")] [JsonPropertyName("ac")] public int Ac { get; set; }
    /// <summary>Bubble-count games played.</summary>
    [JsonProperty("bc")] [JsonPropertyName("bc")] public int Bc { get; set; }
    /// <summary>Sessions started.</summary>
    [JsonProperty("ss")] [JsonPropertyName("ss")] public int Ss { get; set; }

    public FeatureDayEntry() { }

    public FeatureDayEntry(string day) { D = day; }

    public int Get(string key) => key switch
    {
        "xp" => Xp, "cm" => Cm, "fl" => Fl, "bb" => Bb, "pf" => Pf, "sp" => Sp,
        "vd" => Vd, "lk" => Lk, "ac" => Ac, "bc" => Bc, "ss" => Ss,
        _ => 0
    };

    /// <summary>Add <paramref name="amount"/> (never negative) to one counter.</summary>
    public void Add(string key, int amount)
    {
        if (amount <= 0) return;
        switch (key)
        {
            case "xp": Xp += amount; break;
            case "cm": Cm += amount; break;
            case "fl": Fl += amount; break;
            case "bb": Bb += amount; break;
            case "pf": Pf += amount; break;
            case "sp": Sp += amount; break;
            case "vd": Vd += amount; break;
            case "lk": Lk += amount; break;
            case "ac": Ac += amount; break;
            case "bc": Bc += amount; break;
            case "ss": Ss += amount; break;
        }
    }

    /// <summary>True when every counter is zero: such a day is never put on the wire.</summary>
    [Newtonsoft.Json.JsonIgnore]
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsEmpty => CounterKeys.All(k => Get(k) <= 0);

    /// <summary>
    /// The wire shape: <c>d</c> always, then only the counters that are above zero. Zero-valued
    /// keys are ABSENT, not 0, which is what the server's normaliser expects.
    /// </summary>
    public Dictionary<string, object> ToWire()
    {
        var wire = new Dictionary<string, object> { ["d"] = D };
        foreach (var key in CounterKeys)
        {
            var v = Get(key);
            if (v > 0) wire[key] = v;
        }
        return wire;
    }
}

/// <summary>
/// Per-day feature usage, kept beside the lifetime counters rather than fed by per-feature hooks.
/// <see cref="Baseline"/> holds, per counter key, the lifetime value as of the last time the day
/// log was credited (a running snapshot, so a re-baseline after a cloud merge or a reset never
/// loses what today had already banked); today's entry grows by the whole units the lifetime
/// counter moved since then. Persisted to feature_day_log.json, never inside AppSettings, and
/// pruned to <see cref="MaxDays"/> like the quest log cutoff.
/// </summary>
public class FeatureDayLog
{
    public const int MaxDays = 400;

    /// <summary>Lifetime counter value per key at the last credit. Missing key = not yet baselined.</summary>
    [JsonProperty("baseline")]
    [JsonPropertyName("baseline")]
    public Dictionary<string, double> Baseline { get; set; } = new();

    [JsonProperty("days")]
    [JsonPropertyName("days")]
    public List<FeatureDayEntry> Days { get; set; } = new();

    /// <summary>The entry for <paramref name="dayKey"/>, created on first use.</summary>
    public FeatureDayEntry GetOrAddDay(string dayKey)
    {
        var entry = Days.FirstOrDefault(e => e != null && e.D == dayKey);
        if (entry == null)
        {
            entry = new FeatureDayEntry(dayKey);
            Days.Add(entry);
        }
        return entry;
    }

    /// <summary>
    /// Drop days older than <paramref name="cutoffKey"/> (ordinal day-key compare, same as the
    /// quest log) and anything beyond the newest <see cref="MaxDays"/>.
    /// </summary>
    public void Prune(string cutoffKey)
    {
        Days.RemoveAll(e => e == null || string.IsNullOrEmpty(e.D) || string.CompareOrdinal(e.D, cutoffKey) < 0);
        if (Days.Count > MaxDays)
        {
            Days = Days.OrderBy(e => e.D, StringComparer.Ordinal).Skip(Days.Count - MaxDays).ToList();
        }
    }
}
