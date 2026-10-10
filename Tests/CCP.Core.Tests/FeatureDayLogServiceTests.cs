using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>
/// The feature day log (WPF 7.1.5 FeatureDayLogService + Models/FeatureDayLog.cs). The model rules
/// are the WPF FeatureDayLogTests; the service rules (baseline, whole units, the event whitelist,
/// the file, the wire) had no WPF tests because the WPF class read live app statics.
/// </summary>
public class FeatureDayLogServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ccp-daylog-" + Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(_dir, "feature_day_log.json");
    private DateTime _today = new(2026, 10, 10);
    private Dictionary<string, double>? _counters = Zero();

    private static Dictionary<string, double> Zero() => FeatureDayEntry.CounterKeys.ToDictionary(k => k, _ => 0.0);

    private FeatureDayLogService New() => new(FilePath, () => _counters == null ? null : new Dictionary<string, double>(_counters), () => _today, startTimer: false);

    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    // --- the model, as WPF FeatureDayLogTests ---

    [Fact]
    public void Wire_CarriesTheDayAndOnlyCountersAboveZero()
    {
        var e = new FeatureDayEntry("2026-09-11");
        e.Add("xp", 120);
        e.Add("fl", 3);
        var wire = e.ToWire();
        Assert.Equal("2026-09-11", wire["d"]);
        Assert.Equal(120, wire["xp"]);
        Assert.Equal(3, wire["fl"]);
        Assert.Equal(3, wire.Count);
    }

    [Fact]
    public void Add_IgnoresNegativesAndUnknownKeys()
    {
        var e = new FeatureDayEntry("2026-09-11");
        e.Add("xp", -5);
        e.Add("nope", 4);
        e.Add("e_nope", 4);
        Assert.True(e.IsEmpty);
        e.Add("e_fl", 2);
        Assert.False(e.IsEmpty);
        Assert.Equal(2, e.ToWire()["e_fl"]);
    }

    [Fact]
    public void TheEventWhitelist_IsTheSixteenFeaturesAndTheFiveStations()
    {
        Assert.Equal(21, FeatureDayEntry.EventKeys.Length);
        Assert.Equal(16, FeatureDayEntry.EventKeys.Count(k => !k.StartsWith("e_backroom_", StringComparison.Ordinal)));
        Assert.Equal(11, FeatureDayEntry.CounterKeys.Length);
    }

    [Fact]
    public void EverySeasonFeatureKeyWithAWireName_IsOnTheWhitelist()
    {
        var keys = new[]
        {
            SeasonFeatureKeys.Flash, SeasonFeatureKeys.Video, SeasonFeatureKeys.Subliminal, SeasonFeatureKeys.Overlay,
            SeasonFeatureKeys.Bubbles, SeasonFeatureKeys.BubbleCount, SeasonFeatureKeys.BouncingText, SeasonFeatureKeys.LockCard,
            SeasonFeatureKeys.PopQuiz, SeasonFeatureKeys.MindWipe, SeasonFeatureKeys.BlinkTrainer, SeasonFeatureKeys.Companion,
            SeasonFeatureKeys.ChaosMode, SeasonFeatureKeys.Dtrh, SeasonFeatureKeys.Race, SeasonFeatureKeys.PieceByPiece,
        };
        var events = keys.Select(SeasonFeatureKeys.ToDayLogEvent).ToList();
        Assert.All(events, ev => Assert.True(ev != null && FeatureDayEntry.IsEventKey(ev)));
        Assert.Equal(16, events.Distinct().Count());
        Assert.Null(SeasonFeatureKeys.ToDayLogEvent("something-else"));
    }

    [Fact]
    public void Prune_DropsDaysBeforeTheCutoffAndBeyondTheCap()
    {
        var log = new FeatureDayLog();
        log.GetOrAddDay("2025-01-01").Add("xp", 1);
        log.GetOrAddDay("2026-10-09").Add("xp", 1);
        log.Prune("2026-01-01");
        Assert.Single(log.Days);
        Assert.Equal("2026-10-09", log.Days[0].D);
    }

    // --- the service ---

    [Fact]
    public void TheFirstSightOfACounter_IsABaselineAndCreditsNothing()
    {
        _counters!["fl"] = 500;
        using var s = New();
        Assert.Empty(s.Log.Days);
        Assert.Equal(500, s.Log.Baseline["fl"]);
    }

    [Fact]
    public void AGain_IsCreditedToTodayInWholeUnits_AndFractionsWait()
    {
        using var s = New();
        _counters!["cm"] = 0.6;
        s.Tick();
        Assert.Equal(0, s.MinutesOn("2026-10-10"));
        _counters["cm"] = 1.3;
        s.Tick();
        Assert.Equal(1, s.MinutesOn("2026-10-10"));
        _counters["cm"] = 2.1;   // 0.3 carried in the baseline + 0.8
        s.Tick();
        Assert.Equal(2, s.MinutesOn("2026-10-10"));
    }

    [Fact]
    public void ACounterThatDropped_RebaselinesAndNeverWritesANegative()
    {
        _counters!["bb"] = 100;
        using var s = New();
        _counters["bb"] = 40;
        s.Tick();
        Assert.Empty(s.Log.Days);
        _counters["bb"] = 45;
        s.Tick();
        Assert.Equal(5, s.Log.Days.Single().Bb);
    }

    [Fact]
    public void Rebaseline_MovesTheBaselineWithoutCreditingToday()
    {
        using var s = New();
        _counters!["xp"] = 9000;   // a cloud merge lifted the lifetime number
        s.Rebaseline("test");
        s.Tick();
        Assert.Empty(s.Log.Days);
        _counters["xp"] = 9010;
        s.Tick();
        Assert.Equal(10, s.Log.Days.Single().Xp);
    }

    [Fact]
    public void ANewDay_GetsItsOwnEntry()
    {
        using var s = New();
        _counters!["fl"] = 2;
        s.Tick();
        _today = _today.AddDays(1);
        _counters["fl"] = 5;
        s.Tick();
        Assert.Equal(new[] { 2, 3 }, s.Log.Days.OrderBy(d => d.D, StringComparer.Ordinal).Select(d => d.Fl));
    }

    [Fact]
    public void Note_CountsAWhitelistedEvent_AndDropsEverythingElse()
    {
        using var s = New();
        s.Note("e_backroom_slot");
        s.Note("e_backroom_slot");
        s.Note("e_made_up");
        s.Note("xp");
        s.Note("e_fl", 0);
        var day = s.Log.Days.Single();
        Assert.Equal(2, day.Get("e_backroom_slot"));
        Assert.Equal(1, day.Events.Count);
        Assert.Equal(0, day.Xp);
    }

    [Fact]
    public void NoSources_NoTick()
    {
        _counters = null;
        using var s = New();
        s.Tick();
        s.Rebaseline("test");
        Assert.Empty(s.Log.Baseline);
    }

    [Fact]
    public void Flush_WritesTheFile_AndTheNextRunReadsItBack()
    {
        using (var s = New())
        {
            _counters!["cm"] = 7;
            s.Note("e_dt");
            s.Flush();
            Assert.True(File.Exists(FilePath));
        }
        using var again = New();
        Assert.Equal(7, again.MinutesOn("2026-10-10"));
        Assert.Equal(1, again.Log.Days.Single().Get("e_dt"));
        Assert.Equal(7, again.Log.Baseline["cm"]);
    }

    [Fact]
    public void AnUnreadableFile_StartsAFreshLog()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, "{ not json");
        using var s = New();
        Assert.Empty(s.Log.Days);
    }

    [Fact]
    public void TheWirePayload_IsTheNewestNonEmptyDays_OldestFirst()
    {
        using var s = New();
        s.Log.GetOrAddDay("2026-10-08").Add("xp", 5);
        s.Log.GetOrAddDay("2026-10-07");               // empty: never on the wire
        s.Log.GetOrAddDay("2026-10-09").Add("e_pb", 1);
        s.Log.GetOrAddDay("2026-10-06").Add("fl", 1);
        var wire = s.BuildWirePayload(2);
        Assert.Equal(new[] { "2026-10-08", "2026-10-09" }, wire.Select(d => (string)d["d"]));
        Assert.Equal(1, wire[1]["e_pb"]);
    }

    // --- the sync body ---

    [Fact]
    public void TheSyncBody_CarriesTheLogInsideStats_WhereWpfPutsIt()
    {
        var days = new List<Dictionary<string, object>> { new() { ["d"] = "2026-10-10", ["cm"] = 12, ["e_fl"] = 1 } };
        var body = JObject.Parse(JsonConvert.SerializeObject(SyncPush.Body(new AppSettings { UnifiedId = "u_1" }, null, featureDayLog: days)));
        var stats = (JObject)body["stats"]!;
        Assert.Equal(new[] { "feature_day_log" }, stats.Properties().Select(p => p.Name));
        Assert.Equal(12, (int)stats["feature_day_log"]![0]!["cm"]!);
        Assert.Equal("2026-10-10", (string?)stats["feature_day_log"]![0]!["d"]);
    }

    [Fact]
    public void WithNoDayLog_TheSyncBodyHasNoStatsKey()
    {
        var body = JObject.Parse(JsonConvert.SerializeObject(SyncPush.Body(new AppSettings { UnifiedId = "u_1" }, null)));
        Assert.Null(body["stats"]);
    }

    // --- the session hook ---

    [Fact]
    public void ASessionStart_NotesEachEnabledFeatureOnce()
    {
        var before = FeatureDayLogService.Current;
        using var s = New();
        try
        {
            FeatureDayLogService.Current = s;
            SessionRunner.TrackSessionFeatures(new SessionSettings
            {
                FlashEnabled = true, SpiralEnabled = true, PinkFilterEnabled = true, BubblesEnabled = true,
                MandatoryVideosEnabled = false, SubliminalEnabled = false, BubbleCountEnabled = false,
                BouncingTextEnabled = false, LockCardEnabled = false, MindWipeEnabled = false,
            });
            var day = s.Log.Days.Single();
            Assert.Equal(1, day.Get("e_fl"));
            Assert.Equal(1, day.Get("e_ov"));   // spiral OR pink filter: one overlay event
            Assert.Equal(1, day.Get("e_bb"));
            Assert.Equal(3, day.Events.Count);
        }
        finally { FeatureDayLogService.Current = before; }
    }
}
