using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Awareness;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The <see cref="ActivityLedger"/>'s counters (doc 02 §2.1/§2.4).
///
/// <para>These are not routine coverage. "Fifth visit" when it was the second does not read as an
/// off-by-one — it reads as the companion being fake, and the entire callback feature dies with it
/// (doc 02 §9 risk list, MASTER-SCOPE §9.7). Every number the ledger can produce has a boundary test
/// here: the visit model and its sub-30s excursion tolerance, midnight rollover, the trailing week
/// window, streak increment AND break, the bucket histogram, the ring cap, retention pruning, corrupt
/// -file recovery, and the privacy invariant that no window title can reach the file.</para>
///
/// <para>Every ledger is built on a throwaway temp directory with an injected clock, so nothing here
/// touches %LOCALAPPDATA%, no timer fires and no wall-clock time passes.</para>
/// </summary>
public class AwarenessLedgerTests : IDisposable
{
    private readonly string _dir;
    private readonly string _path;
    private readonly List<ActivityLedger> _ledgers = new();

    public AwarenessLedgerTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "ccp-aware-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _path = Path.Combine(_dir, "awareness_ledger.json");
    }

    public void Dispose()
    {
        foreach (var ledger in _ledgers)
        {
            try { ledger.Dispose(); } catch { }
        }
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    /// <summary>Monday 2026-08-03, 09:00 local. A weekday morning, comfortably away from any boundary.</summary>
    private static readonly DateTime Monday9Am = new(2026, 8, 3, 9, 0, 0, DateTimeKind.Local);

    private ActivityLedger NewLedger(Func<DateTime>? clock = null, int retention = ActivityLedger.DefaultRetentionDays,
        string? path = null)
    {
        var ledger = new ActivityLedger(path ?? _path, clock ?? (() => Monday9Am), () => retention);
        _ledgers.Add(ledger);
        return ledger;
    }

    /// <summary>Focus an app and sit on it for <paramref name="seconds"/>, as the poll heartbeat would.</summary>
    private static DateTime Visit(ActivityLedger ledger, string appId, DateTime at, int seconds, string? cluster = null)
    {
        ledger.NoteFocus(appId, cluster, ActivityCategory.Media, at);
        var end = at.AddSeconds(seconds);
        ledger.Heartbeat(end);
        return end;
    }

    // ===================== visit counting & excursion tolerance =====================

    [Fact]
    public void FirstFocus_IsOneVisitAndFirstEver()
    {
        var ledger = NewLedger();
        ledger.NoteFocus("youtube", "site_video", ActivityCategory.Media, Monday9Am);

        var snap = ledger.Snapshot("youtube", Monday9Am.AddSeconds(30));

        Assert.Equal(1, snap.VisitsToday);
        Assert.True(snap.FirstEverVisit);
        Assert.True(snap.FirstVisitToday);
        Assert.Null(snap.SinceLastVisit);
    }

    [Fact]
    public void ReturningAfterALongGap_CountsANewVisit()
    {
        var ledger = NewLedger();
        var t = Visit(ledger, "amazon", Monday9Am, 120);
        t = Visit(ledger, "discord", t, 600);              // ten minutes away — well past tolerance
        Visit(ledger, "amazon", t, 60);

        var snap = ledger.Snapshot("amazon", t.AddSeconds(60));

        Assert.Equal(2, snap.VisitsToday);
        Assert.False(snap.FirstVisitToday);
        Assert.Equal(TimeSpan.FromSeconds(600), snap.SinceLastVisit);
    }

    [Fact]
    public void SubThirtySecondExcursion_ContinuesTheSameVisit()
    {
        // The Discord-title-flicker case (doc 02 §2.4): a brief bounce elsewhere must not invent a
        // visit or reset the dwell clock, or every LongHaul milestone becomes unreachable.
        var ledger = NewLedger();
        var t = Visit(ledger, "hades", Monday9Am, 600, "game_singleplayer");
        t = Visit(ledger, "discord", t, 20);               // 20s < 30s tolerance
        Visit(ledger, "hades", t, 600, "game_singleplayer");

        var snap = ledger.Snapshot("hades", t.AddSeconds(600));

        Assert.Equal(1, snap.VisitsToday);
        Assert.Equal(1200, snap.CurrentVisitDwellSeconds);
    }

    [Fact]
    public void ExcursionExactlyAtTheTolerance_StillContinuesTheVisit()
    {
        var ledger = NewLedger();
        var t = Visit(ledger, "hades", Monday9Am, 300);
        t = Visit(ledger, "discord", t, ActivityLedger.ExcursionToleranceSeconds);
        Visit(ledger, "hades", t, 60);

        Assert.Equal(1, ledger.Snapshot("hades", t.AddSeconds(60)).VisitsToday);
    }

    [Fact]
    public void ExcursionOneSecondPastTheTolerance_StartsANewVisit()
    {
        var ledger = NewLedger();
        var t = Visit(ledger, "hades", Monday9Am, 300);
        t = Visit(ledger, "discord", t, ActivityLedger.ExcursionToleranceSeconds + 1);
        Visit(ledger, "hades", t, 60);

        var snap = ledger.Snapshot("hades", t.AddSeconds(60));
        Assert.Equal(2, snap.VisitsToday);
        Assert.Equal(60, snap.CurrentVisitDwellSeconds);   // the new visit's clock started at zero
    }

    [Fact]
    public void ReFocusingTheSameApp_DoesNotCountAVisit()
    {
        var ledger = NewLedger();
        ledger.NoteFocus("obs", null, ActivityCategory.Working, Monday9Am);
        ledger.NoteFocus("obs", null, ActivityCategory.Working, Monday9Am.AddSeconds(5));
        ledger.NoteFocus("obs", null, ActivityCategory.Working, Monday9Am.AddSeconds(9));

        Assert.Equal(1, ledger.Snapshot("obs", Monday9Am.AddSeconds(10)).VisitsToday);
    }

    [Fact]
    public void UnresolvableAppId_RecordsNothing()
    {
        // Fail closed: when the classifier cannot answer, the ledger does not guess.
        var ledger = NewLedger();
        ledger.NoteFocus("   ", null, ActivityCategory.Unknown, Monday9Am);
        ledger.Heartbeat(Monday9Am.AddMinutes(30));

        Assert.Equal(0, ledger.AppCount);
    }

    // ===================== minutes, weeks, buckets =====================

    [Fact]
    public void MinutesToday_AccrueWhileFocused()
    {
        var ledger = NewLedger();
        Visit(ledger, "youtube", Monday9Am, 25 * 60);

        Assert.Equal(25, ledger.Snapshot("youtube", Monday9Am.AddMinutes(25)).MinutesToday);
    }

    [Fact]
    public void MidnightRollover_SplitsTimeBetweenTheTwoDays()
    {
        // A session from 23:50 to 00:20 is twenty minutes of "today", not thirty of yesterday.
        var start = new DateTime(2026, 8, 3, 23, 50, 0, DateTimeKind.Local);
        var end = start.AddMinutes(30);
        var ledger = NewLedger(() => end);

        ledger.NoteFocus("youtube", null, ActivityCategory.Media, start);
        ledger.Heartbeat(end);

        Assert.Equal(20, ledger.Snapshot("youtube", end).MinutesToday);
    }

    /// <summary>
    /// Yesterday's visit count does not follow the user into today — when the visit actually ENDED
    /// before midnight.
    /// </summary>
    [Fact]
    public void MidnightRollover_ResetsVisitsToday()
    {
        var lateNight = new DateTime(2026, 8, 3, 23, 55, 0, DateTimeKind.Local);
        var ledger = NewLedger(() => lateNight);

        var t = Visit(ledger, "reddit", lateNight, 60);
        t = Visit(ledger, "discord", t, 120);
        Visit(ledger, "reddit", t, 60);
        Assert.Equal(2, ledger.Snapshot("reddit", t.AddSeconds(60)).VisitsToday);

        // They closed it BEFORE the day turned (23:59:30), so the new day genuinely has no visit yet.
        ledger.NoteFocusEnd(new DateTime(2026, 8, 3, 23, 59, 30, DateTimeKind.Local));

        var afterMidnight = new DateTime(2026, 8, 4, 0, 30, 0, DateTimeKind.Local);
        Assert.Equal(0, ledger.Snapshot("reddit", afterMidnight).VisitsToday);
    }

    /// <summary>
    /// A visit that SPANS midnight is a visit on the new day too. `Visits` is only incremented by
    /// NoteFocus's new-visit branch, and an unchanged foreground takes its early return — so the new
    /// day used to accrue MINUTES against a visit count of ZERO. A Milestone frame cut at 00:30 after
    /// three hours then projected "visits_today: 0, minutes_today: 30" and re-read as "first visit
    /// today". Doc 02 §9: a wrong number destroys the trick entirely.
    /// </summary>
    [Fact]
    public void AVisitThatSpansMidnight_OpensAVisitOnTheNewDay()
    {
        var yesterday = new DateTime(2026, 8, 2, 12, 0, 0, DateTimeKind.Local);
        var lateNight = new DateTime(2026, 8, 3, 22, 0, 0, DateTimeKind.Local);
        var ledger = NewLedger(() => yesterday);

        // Seen before, so this is about the visit COUNT and not about first-ever novelty.
        Visit(ledger, "youtube", yesterday, 600);
        ledger.NoteFocusEnd(yesterday.AddMinutes(10));

        ledger.NoteFocus("youtube", "site_video", ActivityCategory.Media, lateNight);

        // Never leaves. The observer's poll (or the rollover timer) keeps the clock moving.
        var afterMidnight = new DateTime(2026, 8, 4, 0, 30, 0, DateTimeKind.Local);
        ledger.Heartbeat(afterMidnight);

        var snap = ledger.Snapshot("youtube", afterMidnight);

        Assert.True(snap.MinutesToday > 0, "the new day accrued minutes");
        Assert.Equal(1, snap.VisitsToday);
    }

    [Fact]
    public void MinutesThisWeek_CoversSevenTrailingDaysAndStopsThere()
    {
        var ledger = NewLedger();

        // 10 minutes a day for nine days ending today.
        for (int daysAgo = 8; daysAgo >= 0; daysAgo--)
        {
            var day = Monday9Am.AddDays(-daysAgo);
            Visit(ledger, "youtube", day, 10 * 60);
            ledger.NoteFocusEnd(day.AddMinutes(10));
        }

        var snap = ledger.Snapshot("youtube", Monday9Am.AddMinutes(11));
        Assert.Equal(70, snap.MinutesThisWeek);   // 7 days × 10, not 9 × 10
    }

    [Fact]
    public void Histogram_AttributesTimeToTheBucketItWasSpentIn()
    {
        // 05:50 → 06:10 straddles the LateNight/Morning boundary; ten minutes belong to each.
        var start = new DateTime(2026, 8, 3, 5, 50, 0, DateTimeKind.Local);
        var ledger = NewLedger(() => start);

        ledger.NoteFocus("youtube", null, ActivityCategory.Media, start);
        ledger.Heartbeat(start.AddMinutes(20));
        ledger.SaveNow();

        var day = ReadDay(_path, "youtube", ActivityLedgerDayKey(start));
        Assert.Equal(600, day.Buckets[(int)TimeBucket.LateNight]);
        Assert.Equal(600, day.Buckets[(int)TimeBucket.Morning]);
    }

    [Theory]
    [InlineData(0, TimeBucket.LateNight)]
    [InlineData(5, TimeBucket.LateNight)]
    [InlineData(6, TimeBucket.Morning)]
    [InlineData(11, TimeBucket.Morning)]
    [InlineData(12, TimeBucket.Afternoon)]
    [InlineData(17, TimeBucket.Afternoon)]
    [InlineData(18, TimeBucket.Evening)]
    [InlineData(23, TimeBucket.Evening)]
    public void BucketOf_SplitsTheDayIntoFourSixHourBands(int hour, TimeBucket expected)
        => Assert.Equal(expected, ActivityLedger.BucketOf(new DateTime(2026, 8, 3, hour, 30, 0, DateTimeKind.Local)));

    [Fact]
    public void LongestDwellToday_TracksTheBiggestVisitNotTheLast()
    {
        var ledger = NewLedger();
        var t = Visit(ledger, "hades", Monday9Am, 40 * 60);
        t = Visit(ledger, "discord", t, 10 * 60);
        Visit(ledger, "hades", t, 5 * 60);

        Assert.Equal(40 * 60, ledger.Snapshot("hades", t.AddMinutes(5)).LongestDwellTodaySeconds);
    }

    [Fact]
    public void MachineSleep_IsNotCountedAsScreenTime()
    {
        // A lid closed for eight hours is not eight hours on YouTube.
        var ledger = NewLedger();
        ledger.NoteFocus("youtube", null, ActivityCategory.Media, Monday9Am);
        ledger.Heartbeat(Monday9Am.AddHours(8));

        Assert.Equal(0, ledger.Snapshot("youtube", Monday9Am.AddHours(8)).MinutesToday);
    }

    // ===================== day streak =====================

    [Fact]
    public void DayStreak_CountsConsecutiveDaysEndingToday()
    {
        var ledger = NewLedger();
        for (int daysAgo = 4; daysAgo >= 0; daysAgo--)
        {
            var day = Monday9Am.AddDays(-daysAgo);
            Visit(ledger, "youtube", day, 60);
            ledger.NoteFocusEnd(day.AddMinutes(1));
        }

        Assert.Equal(5, ledger.Snapshot("youtube", Monday9Am.AddMinutes(2)).DayStreak);
    }

    [Fact]
    public void DayStreak_BreaksOnAMissedDay()
    {
        var ledger = NewLedger();
        foreach (var daysAgo in new[] { 5, 4, 2, 1, 0 })   // day 3 missing
        {
            var day = Monday9Am.AddDays(-daysAgo);
            Visit(ledger, "youtube", day, 60);
            ledger.NoteFocusEnd(day.AddMinutes(1));
        }

        Assert.Equal(3, ledger.Snapshot("youtube", Monday9Am.AddMinutes(2)).DayStreak);
    }

    [Fact]
    public void DayStreak_SurvivesADayNotYetOpened()
    {
        // Yesterday counted, today has not happened yet: the streak is alive, not zero.
        var ledger = NewLedger();
        for (int daysAgo = 3; daysAgo >= 1; daysAgo--)
        {
            var day = Monday9Am.AddDays(-daysAgo);
            Visit(ledger, "youtube", day, 60);
            ledger.NoteFocusEnd(day.AddMinutes(1));
        }

        Assert.Equal(3, ledger.Snapshot("youtube", Monday9Am).DayStreak);
    }

    // ===================== session ring =====================

    [Fact]
    public void SessionRing_KeepsTheLastFiftyTransitionsAndNoMore()
    {
        var ledger = NewLedger();
        var t = Monday9Am;
        for (int i = 0; i < 80; i++)
        {
            t = Visit(ledger, "app" + (i % 7), t, 45);
        }

        var ring = ledger.RecentTransitions;
        Assert.Equal(ActivityLedger.SessionRingCapacity, ring.Count);
        Assert.True(ring.Last().At > ring.First().At);
    }

    [Fact]
    public void SwitchesLast10Min_CountsOnlyTheRecentOnes()
    {
        var ledger = NewLedger();
        var t = Monday9Am;
        for (int i = 0; i < 6; i++) t = Visit(ledger, "app" + i, t, 60);   // 6 minutes of switching
        t = Visit(ledger, "settled", t, 30 * 60);                          // then half an hour still

        var snap = ledger.Snapshot("settled", t.AddMinutes(30));
        Assert.Equal(0, snap.SwitchesLast10Min);
    }

    [Fact]
    public void DayArcSummary_NamesTheBusiestAppPerBucketAndEndsWithNow()
    {
        var ledger = NewLedger();
        var morning = new DateTime(2026, 8, 3, 9, 0, 0, DateTimeKind.Local);
        var afterWork = Visit(ledger, "vscode", morning, 90 * 60, "work");
        ledger.NoteFocusEnd(afterWork);

        var afternoon = new DateTime(2026, 8, 3, 13, 0, 0, DateTimeKind.Local);
        var end = Visit(ledger, "youtube", afternoon, 40 * 60, "site_video");

        var arc = ledger.Snapshot("youtube", end).DayArcSummary;

        Assert.Contains("morning: vscode 1h30m", arc);
        Assert.Contains("afternoon: youtube 40m", arc);
        Assert.EndsWith("now", arc);
    }

    [Fact]
    public void DayArcSummary_CollapsesAdultAppsToTheClusterId()
    {
        // The arc rides into EVERY frame's cloud projection. One adult visit must not put that site's
        // id in front of the model for the rest of the day (doc 02 §6.1).
        var ledger = NewLedger();
        var t = Visit(ledger, "some_adult_site", Monday9Am, 30 * 60, AwarenessClusters.Adult);
        t = Visit(ledger, "vscode", t, 10 * 60, "work");

        var arc = ledger.Snapshot("vscode", t.AddMinutes(10)).DayArcSummary;

        Assert.DoesNotContain("some_adult_site", arc);
        Assert.Contains(AwarenessClusters.Adult, arc);
    }

    // ===================== retention & lifecycle =====================

    [Fact]
    public void PruneRetention_DropsDaysPastTheWindowAndKeepsTheRest()
    {
        var ledger = NewLedger(retention: 7);
        for (int daysAgo = 20; daysAgo >= 0; daysAgo -= 2)
        {
            var day = Monday9Am.AddDays(-daysAgo);
            Visit(ledger, "youtube", day, 5 * 60);
            ledger.NoteFocusEnd(day.AddMinutes(5));
        }

        ledger.PruneRetention(Monday9Am);
        ledger.SaveNow();

        var oldest = Monday9Am.AddDays(-6).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var days = ReadDays(_path, "youtube");
        Assert.NotEmpty(days);
        Assert.All(days, key => Assert.True(string.CompareOrdinal(key, oldest) >= 0,
            $"{key} should have been pruned"));
    }

    [Fact]
    public void PruneRetention_RunsOnStart_WithNoUiAnywhere()
    {
        // The lazy-on-UI bug (partner media that only got cleaned when a page happened to be
        // reopened) must not recur here: retention is honoured by the service starting, full stop.
        var stale = Monday9Am.AddDays(-60);
        var seed = NewLedger(() => stale, retention: 30);
        Visit(seed, "youtube", stale, 10 * 60);
        seed.NoteFocusEnd(stale.AddMinutes(10));
        seed.SaveNow();
        Assert.Contains("youtube", File.ReadAllText(_path));

        var fresh = NewLedger(() => Monday9Am, retention: 30);
        fresh.Start();          // no window, no tab, no user
        fresh.SaveNow();

        Assert.Equal(0, fresh.AppCount);
        Assert.DoesNotContain("youtube", File.ReadAllText(_path));
    }

    [Fact]
    public void DayRollover_PrunesWithoutAnyFocusChange()
    {
        var ledger = NewLedger(() => Monday9Am, retention: 7);

        var stale = Monday9Am.AddDays(-10);
        Visit(ledger, "oldapp", stale, 5 * 60);
        ledger.NoteFocusEnd(stale.AddMinutes(5));
        Assert.Equal(1, ledger.AppCount);

        // The machine sat there and the date changed. Nothing else happened.
        ledger.Heartbeat(Monday9Am.AddDays(1));

        Assert.Equal(0, ledger.AppCount);
    }

    // ===================== persistence =====================

    [Fact]
    public void RoundTrip_PreservesVisitsMinutesAndStreaks()
    {
        var ledger = NewLedger();
        for (int daysAgo = 2; daysAgo >= 0; daysAgo--)
        {
            var day = Monday9Am.AddDays(-daysAgo);
            Visit(ledger, "youtube", day, 12 * 60, "site_video");
            ledger.NoteFocusEnd(day.AddMinutes(12));
        }
        ledger.SaveNow();

        var reloaded = NewLedger();
        reloaded.Start();
        var snap = reloaded.Snapshot("youtube", Monday9Am.AddMinutes(20));

        Assert.Equal(1, snap.VisitsToday);
        Assert.Equal(12, snap.MinutesToday);
        Assert.Equal(36, snap.MinutesThisWeek);
        Assert.Equal(3, snap.DayStreak);
    }

    [Fact]
    public void CorruptFile_LoadsAsEmptyInsteadOfThrowing()
    {
        File.WriteAllText(_path, "{\"version\":1,\"apps\":{\"youtube\":{\"days\":");   // truncated mid-write

        var ledger = NewLedger();
        ledger.Start();

        Assert.Equal(0, ledger.AppCount);
        Assert.Equal(0, ledger.Snapshot("youtube", Monday9Am).VisitsToday);
    }

    [Fact]
    public void EmptyFile_LoadsAsEmptyInsteadOfThrowing()
    {
        File.WriteAllText(_path, "");
        var ledger = NewLedger();
        ledger.Start();
        Assert.Equal(0, ledger.AppCount);
    }

    [Fact]
    public void SaveNow_LeavesNoTempSiblingBehind()
    {
        var ledger = NewLedger();
        Visit(ledger, "youtube", Monday9Am, 60);
        ledger.SaveNow();

        Assert.True(File.Exists(_path));
        Assert.False(File.Exists(ledger.LedgerTempPath));
    }

    // ===================== privacy =====================

    [Fact]
    public void SerializedLedger_ContainsNothingButIdsCountersAndDayKeys()
    {
        // The structural privacy claim: there is no parameter on this class that can carry a window
        // title, so the file cannot contain one. This asserts it over the real serialized output
        // rather than over the API's good intentions.
        var ledger = NewLedger();
        var t = Visit(ledger, "youtube", Monday9Am, 20 * 60, "site_video");
        t = Visit(ledger, "some_adult_site", t, 5 * 60, AwarenessClusters.Adult);
        Visit(ledger, "vscode", t, 30 * 60, "work");
        ledger.SaveNow();

        using var doc = JsonDocument.Parse(File.ReadAllText(_path));
        var root = doc.RootElement;

        var allowedIds = new HashSet<string>(StringComparer.Ordinal)
        {
            "youtube", "some_adult_site", "vscode", "site_video", AwarenessClusters.Adult, "work"
        };

        foreach (var app in root.GetProperty("apps").EnumerateObject())
        {
            Assert.Contains(app.Name, allowedIds);
            foreach (var member in app.Value.EnumerateObject())
            {
                switch (member.Name)
                {
                    case "cluster":
                        Assert.Contains(member.Value.GetString() ?? "", allowedIds);
                        break;
                    case "firstSeen":
                    case "lastSeen":
                        Assert.True(DateTime.TryParse(member.Value.GetString(), out _));
                        break;
                    case "days":
                        foreach (var day in member.Value.EnumerateObject())
                        {
                            Assert.Matches(@"^\d{4}-\d{2}-\d{2}$", day.Name);
                            foreach (var counter in day.Value.EnumerateObject())
                            {
                                Assert.True(counter.Value.ValueKind is JsonValueKind.Number or JsonValueKind.Array,
                                    $"{counter.Name} should be a counter, not text");
                            }
                        }
                        break;
                    default:
                        Assert.Fail($"unexpected app member '{member.Name}' in the ledger file");
                        break;
                }
            }
        }

        foreach (var hourDay in root.GetProperty("hours").EnumerateObject())
        {
            Assert.Matches(@"^\d{4}-\d{2}-\d{2}$", hourDay.Name);
            Assert.Equal(JsonValueKind.Array, hourDay.Value.ValueKind);
        }
    }

    [Fact]
    public void AppIdsAreSanitizedBeforeTheyReachTheFile()
    {
        // app_clusters.json takes a mod-supplied override, so an app id is attacker-authored input.
        var ledger = NewLedger();
        Visit(ledger, "Evil App\nNAME", Monday9Am, 60, "clus\nter");
        ledger.SaveNow();

        var json = File.ReadAllText(_path);
        Assert.DoesNotContain("Evil App", json);
        Assert.Contains("evil_app_name", json);
        Assert.Contains("clus_ter", json);
    }

    // ===================== erasure =====================

    [Fact]
    public void Wipe_RemovesTheFileTheTempSiblingAndEveryInMemoryArtifact()
    {
        var ledger = NewLedger();
        var t = Visit(ledger, "youtube", Monday9Am, 20 * 60, "site_video");
        Visit(ledger, "reddit", t, 10 * 60, "site_doomscroll");
        ledger.SaveNow();

        // An interrupted atomic write leaves this behind holding a full copy of the data, and nothing
        // else in the app ever deletes it. A wipe that skips it is a wipe that failed.
        File.WriteAllText(ledger.LedgerTempPath, File.ReadAllText(_path));
        Assert.True(File.Exists(ledger.LedgerTempPath));

        ledger.Wipe();

        Assert.False(File.Exists(_path));
        Assert.False(File.Exists(ledger.LedgerTempPath));
        Assert.Equal(0, ledger.AppCount);
        Assert.Empty(ledger.RecentTransitions);
        Assert.Equal(0, ledger.Snapshot("youtube", Monday9Am.AddHours(1)).VisitsToday);
        Assert.Equal("", ledger.Snapshot("youtube", Monday9Am.AddHours(1)).DayArcSummary);
    }

    [Fact]
    public void Wipe_IsNotResurrectedByAPendingDebouncedSave()
    {
        var ledger = NewLedger();
        ledger.Start();                                   // arms the debounce timer
        Visit(ledger, "youtube", Monday9Am, 20 * 60);     // schedules a save
        ledger.Wipe();
        ledger.SaveNow();                                 // whatever lands must be empty

        Assert.DoesNotContain("youtube", File.Exists(_path) ? File.ReadAllText(_path) : "");
    }

    [Fact]
    public void Forget_RemovesOneAppFromMemoryTheRingAndDiskImmediately()
    {
        var ledger = NewLedger();
        var t = Visit(ledger, "youtube", Monday9Am, 20 * 60, "site_video");
        t = Visit(ledger, "vscode", t, 20 * 60, "work");
        Visit(ledger, "youtube", t, 5 * 60, "site_video");
        ledger.SaveNow();
        Assert.Contains("youtube", File.ReadAllText(_path));

        ledger.Forget("youtube");

        Assert.DoesNotContain("youtube", File.ReadAllText(_path));   // written through, not debounced
        Assert.DoesNotContain(ledger.RecentTransitions, tr => tr.AppId == "youtube");
        Assert.Equal(0, ledger.Snapshot("youtube", t.AddHours(1)).VisitsToday);
        Assert.Contains("vscode", File.ReadAllText(_path));          // and only that app
    }

    // ===================== helpers =====================

    private static string ActivityLedgerDayKey(DateTime at) =>
        at.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private sealed record DayRow(int Visits, int Seconds, int LongestDwellSeconds, int[] Buckets);

    private static DayRow ReadDay(string path, string appId, string dayKey)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var day = doc.RootElement.GetProperty("apps").GetProperty(appId).GetProperty("days").GetProperty(dayKey);
        return new DayRow(
            day.GetProperty("visits").GetInt32(),
            day.GetProperty("seconds").GetInt32(),
            day.GetProperty("longestDwellSeconds").GetInt32(),
            day.GetProperty("buckets").EnumerateArray().Select(e => e.GetInt32()).ToArray());
    }

    private static List<string> ReadDays(string path, string appId)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        if (!doc.RootElement.GetProperty("apps").TryGetProperty(appId, out var app)) return new List<string>();
        return app.GetProperty("days").EnumerateObject().Select(p => p.Name).ToList();
    }
}
