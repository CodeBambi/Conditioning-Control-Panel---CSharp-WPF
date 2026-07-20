using System;
using ConditioningControlPanel.Services;
using Xunit;
using static ConditioningControlPanel.Services.VideoService;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// #600 — a video output that appears then VANISHES mid-clip (white screen partway through) went
/// unhealed because the vout watchdog only fired once at start-of-play. The mid-play poll is a small
/// state machine: it must ignore a never-yet-seen vout (start watchdog's job), start a loss clock on
/// first absence, wait out a grace window (so a brief seek/segment drop doesn't truncate a healthy
/// clip), then heal exactly once.
/// </summary>
public class VoutMidPlayTests
{
    private const int GraceMs = 5000;
    private static long Ms(long ms) => ms * TimeSpan.TicksPerMillisecond;

    [Fact]
    public void VoutPresent_IsHealthy_AndResetsLossClock()
    {
        var d = EvaluateVoutMidPlay(voutCount: 1, everSeen: true, lostSinceTicks: Ms(1000),
            nowTicks: Ms(9999), graceMs: GraceMs, out var newLost);
        Assert.Equal(VoutMidDecision.Healthy, d);
        Assert.Equal(0, newLost); // loss window cleared
    }

    [Fact]
    public void NeverSeen_AndAbsent_DefersToStartWatchdog()
    {
        var d = EvaluateVoutMidPlay(voutCount: 0, everSeen: false, lostSinceTicks: 0,
            nowTicks: Ms(1000), graceMs: GraceMs, out var newLost);
        Assert.Equal(VoutMidDecision.DeferToStartWatchdog, d);
        Assert.Equal(0, newLost); // don't start a loss clock before the vout has ever appeared
    }

    [Fact]
    public void SeenThenAbsent_StartsLossClock()
    {
        var d = EvaluateVoutMidPlay(voutCount: 0, everSeen: true, lostSinceTicks: 0,
            nowTicks: Ms(1234), graceMs: GraceMs, out var newLost);
        Assert.Equal(VoutMidDecision.StartLossClock, d);
        Assert.Equal(Ms(1234), newLost); // clock stamped at 'now'
    }

    [Fact]
    public void LostButWithinGrace_Waits()
    {
        var d = EvaluateVoutMidPlay(voutCount: 0, everSeen: true, lostSinceTicks: Ms(1000),
            nowTicks: Ms(1000 + GraceMs - 1), graceMs: GraceMs, out var newLost);
        Assert.Equal(VoutMidDecision.WithinGrace, d);
        Assert.Equal(Ms(1000), newLost); // clock unchanged while waiting
    }

    [Fact]
    public void LostPastGrace_Heals()
    {
        var d = EvaluateVoutMidPlay(voutCount: 0, everSeen: true, lostSinceTicks: Ms(1000),
            nowTicks: Ms(1000 + GraceMs), graceMs: GraceMs, out _);
        Assert.Equal(VoutMidDecision.Heal, d);
    }

    [Fact]
    public void FullSequence_SeenLostRecovered_NeverHeals()
    {
        bool everSeen = false;
        long lost = 0;

        // t=0: vout up
        var d1 = EvaluateVoutMidPlay(1, everSeen, lost, Ms(0), GraceMs, out lost);
        if (d1 == VoutMidDecision.Healthy) everSeen = true;
        Assert.True(everSeen);
        Assert.Equal(0, lost);

        // t=2000: vout drops (first absence) -> start clock
        var d2 = EvaluateVoutMidPlay(0, everSeen, lost, Ms(2000), GraceMs, out lost);
        Assert.Equal(VoutMidDecision.StartLossClock, d2);
        Assert.Equal(Ms(2000), lost);

        // t=4000: still gone but only 2s in (< 5s grace) -> wait
        var d3 = EvaluateVoutMidPlay(0, everSeen, lost, Ms(4000), GraceMs, out lost);
        Assert.Equal(VoutMidDecision.WithinGrace, d3);

        // t=4500: vout came back before grace elapsed -> healthy, clock reset. No heal ever happened.
        var d4 = EvaluateVoutMidPlay(1, everSeen, lost, Ms(4500), GraceMs, out lost);
        Assert.Equal(VoutMidDecision.Healthy, d4);
        Assert.Equal(0, lost);
    }

    [Fact]
    public void FullSequence_SustainedLoss_HealsAfterGrace()
    {
        bool everSeen = true; // vout was up earlier this playback
        long lost = 0;

        var a = EvaluateVoutMidPlay(0, everSeen, lost, Ms(10_000), GraceMs, out lost);
        Assert.Equal(VoutMidDecision.StartLossClock, a);

        var b = EvaluateVoutMidPlay(0, everSeen, lost, Ms(12_000), GraceMs, out lost);
        Assert.Equal(VoutMidDecision.WithinGrace, b);

        var c = EvaluateVoutMidPlay(0, everSeen, lost, Ms(15_000), GraceMs, out lost);
        Assert.Equal(VoutMidDecision.Heal, c); // 5s sustained loss -> heal
    }
}
