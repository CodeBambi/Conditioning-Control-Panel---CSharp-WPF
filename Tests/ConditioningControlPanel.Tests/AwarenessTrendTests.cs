using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Awareness;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Every trend event's trigger boundary (doc 02 §2.4). These are the callback generators — the whole
/// "how did she know?!" payload — and each one has an exact number attached to it, so each one is
/// tested at n−1 (silence) and n (fires), plus the once-only guards that stop a trend re-offering the
/// same joke on every subsequent frame of the same visit.
/// </summary>
public class AwarenessTrendTests : IDisposable
{
    private readonly string _dir;
    private readonly string _path;
    private readonly List<ActivityLedger> _ledgers = new();

    public AwarenessTrendTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "ccp-aware-trend-tests", Guid.NewGuid().ToString("N"));
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

    private static readonly DateTime Monday9Am = new(2026, 8, 3, 9, 0, 0, DateTimeKind.Local);

    private ActivityLedger NewLedger(Func<DateTime>? clock = null)
    {
        var ledger = new ActivityLedger(_path, clock ?? (() => Monday9Am), () => 30);
        _ledgers.Add(ledger);
        return ledger;
    }

    private static DateTime Visit(ActivityLedger ledger, string appId, DateTime at, int seconds, string? cluster = null)
    {
        ledger.NoteFocus(appId, cluster, ActivityCategory.Media, at);
        var end = at.AddSeconds(seconds);
        ledger.Heartbeat(end);
        return end;
    }

    /// <summary>Bounces to another app and back, far enough apart to end the visit.</summary>
    private static DateTime BounceAway(ActivityLedger ledger, DateTime at, int awaySeconds)
        => Visit(ledger, "elsewhere", at, awaySeconds);

    // ===================== ReturnVisit =====================

    [Fact]
    public void ReturnVisit_DoesNotFireOnTheSecondVisit()
    {
        var ledger = NewLedger();
        var t = Visit(ledger, "amazon", Monday9Am, 60, "site_shopping");
        t = BounceAway(ledger, t, 120);
        t = Visit(ledger, "amazon", t, 60, "site_shopping");

        var trends = ledger.DeriveTrends("amazon", "site_shopping", t);

        Assert.DoesNotContain(trends, e => e.Kind == TrendKind.ReturnVisit);
    }

    [Fact]
    public void ReturnVisit_FiresOnTheThirdWithTheRightNumbers()
    {
        var ledger = NewLedger();
        var t = Monday9Am;
        for (int i = 0; i < 3; i++)
        {
            t = Visit(ledger, "amazon", t, 5 * 60, "site_shopping");
            if (i < 2) t = BounceAway(ledger, t, 120);
        }

        var trends = ledger.DeriveTrends("amazon", "site_shopping", t);
        var rv = Assert.Single(trends.Where(e => e.Kind == TrendKind.ReturnVisit));

        Assert.Equal(3, rv.Magnitude);
        Assert.Equal(3, rv.VisitsToday);
        Assert.Equal(15, rv.MinutesToday);
        Assert.Equal("amazon", rv.AppId);
        Assert.Equal("ReturnVisit(3)", rv.Label);
    }

    [Fact]
    public void ReturnVisit_FiresOncePerVisitNumber()
    {
        var ledger = NewLedger();
        var t = Monday9Am;
        for (int i = 0; i < 3; i++)
        {
            t = Visit(ledger, "amazon", t, 60, "site_shopping");
            if (i < 2) t = BounceAway(ledger, t, 120);
        }

        Assert.Contains(ledger.DeriveTrends("amazon", "site_shopping", t), e => e.Kind == TrendKind.ReturnVisit);
        Assert.DoesNotContain(ledger.DeriveTrends("amazon", "site_shopping", t.AddSeconds(30)),
            e => e.Kind == TrendKind.ReturnVisit);
    }

    // ===================== LongHaul =====================

    [Fact]
    public void LongHaul_DoesNotFireJustUnderTheFirstMilestone()
    {
        var ledger = NewLedger();
        var t = Visit(ledger, "hades", Monday9Am, 30 * 60 - 1, "game_singleplayer");

        Assert.DoesNotContain(ledger.DeriveTrends("hades", "game_singleplayer", t), e => e.Kind == TrendKind.LongHaul);
    }

    [Fact]
    public void LongHaul_FiresExactlyOnTheMilestone()
    {
        var ledger = NewLedger();
        var t = Visit(ledger, "hades", Monday9Am, 30 * 60, "game_singleplayer");

        var lh = Assert.Single(ledger.DeriveTrends("hades", "game_singleplayer", t).Where(e => e.Kind == TrendKind.LongHaul));
        Assert.Equal(30, lh.Magnitude);
        Assert.Equal(30 * 60, lh.DwellSeconds);
    }

    [Fact]
    public void LongHaul_FiresOncePerMilestoneThenTheNextOne()
    {
        var ledger = NewLedger();
        var t = Visit(ledger, "hades", Monday9Am, 30 * 60, "game_singleplayer");
        Assert.Equal(30, LongHaulMagnitude(ledger, t));

        // Still on it two minutes later: the 30m joke has been told.
        ledger.Heartbeat(t.AddMinutes(2));
        Assert.Null(LongHaulMagnitudeOrNull(ledger, t.AddMinutes(2)));

        // An hour in, the next milestone lands.
        ledger.Heartbeat(Monday9Am.AddMinutes(60));
        Assert.Equal(60, LongHaulMagnitude(ledger, Monday9Am.AddMinutes(60)));
    }

    [Fact]
    public void LongHaul_SkippedMilestonesDoNotBackfire()
    {
        // A three-hour marathon observed once reports "3h", not 30m then 1h then 2h then 3h.
        var ledger = NewLedger();
        var t = Visit(ledger, "hades", Monday9Am, 3 * 3600, "game_singleplayer");

        var longHauls = ledger.DeriveTrends("hades", "game_singleplayer", t)
            .Where(e => e.Kind == TrendKind.LongHaul).ToList();

        Assert.Single(longHauls);
        Assert.Equal(180, longHauls[0].Magnitude);
        Assert.Null(LongHaulMagnitudeOrNull(ledger, t.AddMinutes(5)));
    }

    [Fact]
    public void LongHaul_CountsCumulativeDwellAcrossShortExcursions()
    {
        // 20 minutes, a 15-second bounce, 10 more minutes = 30 minutes. Today's implementation would
        // have reset the clock and never reached a milestone at all.
        var ledger = NewLedger();
        var t = Visit(ledger, "hades", Monday9Am, 20 * 60, "game_singleplayer");
        t = Visit(ledger, "discord", t, 15);
        t = Visit(ledger, "hades", t, 10 * 60, "game_singleplayer");

        Assert.Equal(30, LongHaulMagnitude(ledger, t));
    }

    private static int LongHaulMagnitude(ActivityLedger ledger, DateTime at) =>
        LongHaulMagnitudeOrNull(ledger, at) ?? throw new InvalidOperationException("expected a LongHaul");

    private static int? LongHaulMagnitudeOrNull(ActivityLedger ledger, DateTime at) =>
        ledger.DeriveTrends("hades", "game_singleplayer", at)
            .Where(e => e.Kind == TrendKind.LongHaul)
            .Select(e => (int?)e.Magnitude)
            .FirstOrDefault();

    // ===================== Streak =====================

    [Fact]
    public void Streak_DoesNotFireOnTwoDays()
    {
        var ledger = NewLedger();
        var t = SeedDailyVisits(ledger, "youtube", days: 2);

        Assert.DoesNotContain(ledger.DeriveTrends("youtube", "site_video", t), e => e.Kind == TrendKind.Streak);
    }

    [Fact]
    public void Streak_FiresOnThreeDaysAndOnlyOncePerDay()
    {
        var ledger = NewLedger();
        var t = SeedDailyVisits(ledger, "youtube", days: 3);

        var streak = Assert.Single(ledger.DeriveTrends("youtube", "site_video", t).Where(e => e.Kind == TrendKind.Streak));
        Assert.Equal(3, streak.Magnitude);

        Assert.DoesNotContain(ledger.DeriveTrends("youtube", "site_video", t.AddMinutes(5)),
            e => e.Kind == TrendKind.Streak);
    }

    private static DateTime SeedDailyVisits(ActivityLedger ledger, string appId, int days)
    {
        DateTime last = Monday9Am;
        for (int daysAgo = days - 1; daysAgo >= 0; daysAgo--)
        {
            var day = Monday9Am.AddDays(-daysAgo);
            last = Visit(ledger, appId, day, 5 * 60, "site_video");
            if (daysAgo > 0) ledger.NoteFocusEnd(last);
        }
        return last;
    }

    // ===================== MediaLoop =====================

    [Theory]
    [InlineData(2, false)]
    [InlineData(3, true)]
    [InlineData(7, true)]
    public void MediaLoop_NeedsThreeConsecutivePlays(int repeats, bool expected)
    {
        var ledger = NewLedger();
        var t = Visit(ledger, "spotify", Monday9Am, 10 * 60, "site_music");

        var trends = ledger.DeriveTrends("spotify", "site_music", t, mediaRepeatCount: repeats);

        Assert.Equal(expected, trends.Any(e => e.Kind == TrendKind.MediaLoop));
    }

    [Fact]
    public void MediaLoop_FiresAgainWhenTheCountClimbs()
    {
        var ledger = NewLedger();
        var t = Visit(ledger, "spotify", Monday9Am, 10 * 60, "site_music");

        Assert.Contains(ledger.DeriveTrends("spotify", "site_music", t, mediaRepeatCount: 3),
            e => e.Kind == TrendKind.MediaLoop);
        Assert.DoesNotContain(ledger.DeriveTrends("spotify", "site_music", t, mediaRepeatCount: 3),
            e => e.Kind == TrendKind.MediaLoop);
        Assert.Contains(ledger.DeriveTrends("spotify", "site_music", t, mediaRepeatCount: 4),
            e => e.Kind == TrendKind.MediaLoop);
    }

    // ===================== Backslide =====================

    [Fact]
    public void Backslide_FiresWhenADoomscrollSiteIsReopenedInsideFiveMinutes()
    {
        var ledger = NewLedger();
        var t = Visit(ledger, "reddit", Monday9Am, 10 * 60, AwarenessClusters.Doomscroll);
        t = BounceAway(ledger, t, 120);                       // gone for two minutes
        t = Visit(ledger, "reddit", t, 60, AwarenessClusters.Doomscroll);

        var backslide = Assert.Single(ledger.DeriveTrends("reddit", AwarenessClusters.Doomscroll, t)
            .Where(e => e.Kind == TrendKind.Backslide));

        Assert.Equal(120, backslide.Magnitude);
    }

    [Fact]
    public void Backslide_DoesNotFirePastTheFiveMinuteWindow()
    {
        var ledger = NewLedger();
        var t = Visit(ledger, "reddit", Monday9Am, 10 * 60, AwarenessClusters.Doomscroll);
        t = BounceAway(ledger, t, ActivityLedger.BacksideWindowSeconds + 1);
        t = Visit(ledger, "reddit", t, 60, AwarenessClusters.Doomscroll);

        Assert.DoesNotContain(ledger.DeriveTrends("reddit", AwarenessClusters.Doomscroll, t),
            e => e.Kind == TrendKind.Backslide);
    }

    [Fact]
    public void Backslide_DoesNotFireForAShortExcursionThatNeverEndedTheVisit()
    {
        var ledger = NewLedger();
        var t = Visit(ledger, "reddit", Monday9Am, 10 * 60, AwarenessClusters.Doomscroll);
        t = BounceAway(ledger, t, 10);                        // under the tolerance: same visit
        t = Visit(ledger, "reddit", t, 60, AwarenessClusters.Doomscroll);

        Assert.DoesNotContain(ledger.DeriveTrends("reddit", AwarenessClusters.Doomscroll, t),
            e => e.Kind == TrendKind.Backslide);
    }

    [Fact]
    public void Backslide_IsDoomscrollOnly()
    {
        var ledger = NewLedger();
        var t = Visit(ledger, "vscode", Monday9Am, 10 * 60, "work");
        t = BounceAway(ledger, t, 120);
        t = Visit(ledger, "vscode", t, 60, "work");

        Assert.DoesNotContain(ledger.DeriveTrends("vscode", "work", t), e => e.Kind == TrendKind.Backslide);
    }

    // ===================== GhostTown =====================

    [Theory]
    [InlineData(3 * 3600 - 1, false, 0)]
    [InlineData(3 * 3600, true, 3)]
    [InlineData(9 * 3600, true, 9)]
    public void GhostTown_NeedsThreeRealIdleHours(int idleSeconds, bool expected, int magnitude)
    {
        var ledger = NewLedger();
        var t = Visit(ledger, "discord", Monday9Am, 60, "site_chat");

        var ghost = ledger.DeriveTrends("discord", "site_chat", t, inputIdleSecondsBeforeWake: idleSeconds)
            .FirstOrDefault(e => e.Kind == TrendKind.GhostTown);

        Assert.Equal(expected, ghost != null);
        if (expected) Assert.Equal(magnitude, ghost!.Magnitude);
    }

    [Fact]
    public void GhostTown_IsAGreetingNotACallback()
    {
        // The tier ladder keys off this: everything else is backed by persisted history and earns the
        // Rare tier; "welcome back, sleepyhead" is a live-signal greeting.
        var ledger = NewLedger();
        var t = Visit(ledger, "discord", Monday9Am, 60, "site_chat");

        var ghost = ledger.DeriveTrends("discord", "site_chat", t, inputIdleSecondsBeforeWake: 4 * 3600)
            .Single(e => e.Kind == TrendKind.GhostTown);

        Assert.False(ghost.CarriesLedgerHistory);
    }

    // ===================== NightShift =====================

    [Fact]
    public void NightShift_StaysQuietWithoutEnoughNightsToLearnFrom()
    {
        var lateNight = new DateTime(2026, 8, 4, 2, 0, 0, DateTimeKind.Local);
        var ledger = NewLedger(() => lateNight);

        // Two nights of evidence — below the minimum sample count.
        SeedNight(ledger, new DateTime(2026, 8, 1, 22, 0, 0, DateTimeKind.Local), hours: 1);
        SeedNight(ledger, new DateTime(2026, 8, 2, 22, 0, 0, DateTimeKind.Local), hours: 1);

        var t = Visit(ledger, "reddit", lateNight, 5 * 60, AwarenessClusters.Doomscroll);

        Assert.Null(ledger.LearnedBedtimeHour(lateNight));
        Assert.DoesNotContain(ledger.DeriveTrends("reddit", AwarenessClusters.Doomscroll, t),
            e => e.Kind == TrendKind.NightShift);
    }

    [Fact]
    public void NightShift_LearnsTheUsualBedtimeAndFiresPastIt()
    {
        // Five nights of shutting down at 23:00. Tonight it is 02:00.
        var lateNight = new DateTime(2026, 8, 8, 2, 0, 0, DateTimeKind.Local);
        var ledger = NewLedger(() => lateNight);

        for (int day = 2; day <= 6; day++)
        {
            SeedNight(ledger, new DateTime(2026, 8, day, 22, 0, 0, DateTimeKind.Local), hours: 2);
        }

        Assert.Equal(23.0, ledger.LearnedBedtimeHour(lateNight)!.Value);

        var t = Visit(ledger, "reddit", lateNight, 5 * 60, AwarenessClusters.Doomscroll);
        var night = Assert.Single(ledger.DeriveTrends("reddit", AwarenessClusters.Doomscroll, t)
            .Where(e => e.Kind == TrendKind.NightShift));

        Assert.Equal(3, night.Magnitude);   // 02:00 is night-index 26, three hours past 23
    }

    [Fact]
    public void NightShift_StaysQuietBeforeTheLearnedBoundary()
    {
        var earlyNight = new DateTime(2026, 8, 8, 22, 0, 0, DateTimeKind.Local);
        var ledger = NewLedger(() => earlyNight);

        for (int day = 2; day <= 6; day++)
        {
            SeedNight(ledger, new DateTime(2026, 8, day, 22, 0, 0, DateTimeKind.Local), hours: 2);
        }

        var t = Visit(ledger, "reddit", earlyNight, 5 * 60, AwarenessClusters.Doomscroll);

        Assert.DoesNotContain(ledger.DeriveTrends("reddit", AwarenessClusters.Doomscroll, t),
            e => e.Kind == TrendKind.NightShift);
    }

    [Fact]
    public void NightShift_IsNotMovedByASingleAllNighter()
    {
        // Median, not mean: one 04:00 night must not redraw the boundary for the next fortnight.
        var tonight = new DateTime(2026, 8, 9, 1, 0, 0, DateTimeKind.Local);
        var ledger = NewLedger(() => tonight);

        for (int day = 2; day <= 6; day++)
        {
            SeedNight(ledger, new DateTime(2026, 8, day, 22, 0, 0, DateTimeKind.Local), hours: 1);  // asleep by 23
        }
        SeedNight(ledger, new DateTime(2026, 8, 7, 22, 0, 0, DateTimeKind.Local), hours: 8);        // up until 06

        Assert.Equal(22.0, ledger.LearnedBedtimeHour(tonight)!.Value);
    }

    /// <summary>Puts <paramref name="hours"/> of activity on the clock starting at <paramref name="from"/>.</summary>
    private static void SeedNight(ActivityLedger ledger, DateTime from, int hours)
    {
        ledger.NoteFocus("seed", "work", ActivityCategory.Working, from);
        for (int h = 1; h <= hours; h++) ledger.Heartbeat(from.AddHours(h));
        ledger.NoteFocusEnd(from.AddHours(hours));
    }
}
