using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Models;
using Newtonsoft.Json;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The wire shape of <c>stats.feature_day_log</c> (Spiral rail contracts 1 and 2). The server
/// whitelists keys and treats an absent key as 0, so what matters here is exactly which keys a
/// day puts on the wire: counters and engagement events above zero, nothing else, ever.
/// Only the model is exercised; <c>FeatureDayLogService</c> reads live app statics.
/// </summary>
public class FeatureDayLogTests
{
    [Fact]
    public void ToWire_OmitsZeroCountersAndCarriesDay()
    {
        var e = new FeatureDayEntry("2026-09-11");
        e.Add("fl", 12);
        e.Add("cm", 30);

        var wire = e.ToWire();

        Assert.Equal("2026-09-11", wire["d"]);
        Assert.Equal(12, wire["fl"]);
        Assert.Equal(30, wire["cm"]);
        Assert.Equal(3, wire.Count);
    }

    [Fact]
    public void EventKeys_RideBesideCountersOnTheWire()
    {
        var e = new FeatureDayEntry("2026-09-11");
        e.Add("e_fl", 1);
        e.Add("e_fl", 1);
        e.Add("e_dt", 3);

        var wire = e.ToWire();

        Assert.Equal(2, wire["e_fl"]);
        Assert.Equal(3, wire["e_dt"]);
        Assert.Equal(2, e.Get("e_fl"));
        Assert.False(e.IsEmpty);
        Assert.Equal(3, wire.Count);
    }

    [Fact]
    public void UnknownKeys_NeverStoredOrSent()
    {
        var e = new FeatureDayEntry("2026-09-11");
        e.Add("e_nope", 5);
        e.Add("bogus", 5);

        Assert.True(e.IsEmpty);
        Assert.Equal(0, e.Get("e_nope"));
        Assert.Single(e.ToWire());          // only "d"
        Assert.Empty(e.Events);
    }

    [Fact]
    public void AllEventKeys_HaveWireNamesAndNoOverlapWithCounters()
    {
        Assert.Equal(16, FeatureDayEntry.EventKeys.Length);
        Assert.All(FeatureDayEntry.EventKeys, k => Assert.StartsWith("e_", k));
        Assert.Empty(FeatureDayEntry.EventKeys.Intersect(FeatureDayEntry.CounterKeys));
        Assert.Equal(FeatureDayEntry.EventKeys.Length, FeatureDayEntry.EventKeys.Distinct().Count());
    }

    [Fact]
    public void SeasonFeatureKeys_MapOntoDayLogEvents()
    {
        // Every catalogued conditioning effect and every Lab mode reaches the wire under a
        // whitelisted event key; an unknown season key maps to nothing.
        foreach (var def in SeasonFeatureKeys.Catalog)
        {
            var ev = SeasonFeatureKeys.ToDayLogEvent(def.Key);
            Assert.NotNull(ev);
            Assert.True(FeatureDayEntry.IsEventKey(ev!), $"{def.Key} -> {ev} is not whitelisted");
        }
        foreach (var lab in new[] { SeasonFeatureKeys.ChaosMode, SeasonFeatureKeys.Dtrh, SeasonFeatureKeys.Race,
                                    SeasonFeatureKeys.PieceByPiece, SeasonFeatureKeys.PopQuiz,
                                    SeasonFeatureKeys.BlinkTrainer, SeasonFeatureKeys.Companion })
        {
            Assert.True(FeatureDayEntry.IsEventKey(SeasonFeatureKeys.ToDayLogEvent(lab)!), lab);
        }
        Assert.Null(SeasonFeatureKeys.ToDayLogEvent("not-a-feature"));
    }

    [Fact]
    public void LocalFile_RoundTripsEventsThroughBothSerializers()
    {
        var log = new FeatureDayLog();
        var day = log.GetOrAddDay("2026-09-11");
        day.Add("xp", 100);
        day.Add("e_cp", 4);
        log.Baseline["xp"] = 100.5;

        // Local file: System.Text.Json.
        var stj = System.Text.Json.JsonSerializer.Serialize(log);
        var backStj = System.Text.Json.JsonSerializer.Deserialize<FeatureDayLog>(stj)!;
        Assert.Equal(4, backStj.Days.Single().Get("e_cp"));
        Assert.Equal(100, backStj.Days.Single().Xp);
        Assert.Equal(100.5, backStj.Baseline["xp"]);

        // Sync body: Newtonsoft, which is what the wire dictionaries go through.
        var nsj = JsonConvert.SerializeObject(day.ToWire());
        var wire = JsonConvert.DeserializeObject<Dictionary<string, object>>(nsj)!;
        Assert.Equal("2026-09-11", wire["d"]);
        Assert.Equal(4L, wire["e_cp"]);
        Assert.False(wire.ContainsKey("ev"));   // events are flat on the wire, never nested
    }

    [Fact]
    public void Prune_KeepsOnlyTheWindow()
    {
        var log = new FeatureDayLog();
        log.GetOrAddDay("2025-01-01").Add("e_fl", 1);
        log.GetOrAddDay("2026-09-10").Add("e_fl", 1);
        log.GetOrAddDay("2026-09-11").Add("e_fl", 1);

        log.Prune("2026-01-01");

        Assert.Equal(new[] { "2026-09-10", "2026-09-11" }, log.Days.Select(d => d.D).OrderBy(d => d));
    }
}
