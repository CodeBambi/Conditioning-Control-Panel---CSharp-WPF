using System;
using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Services.BackRoom;
using ConditioningControlPanel.Services.Haptics;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The <c>haptic</c> frame (CONTRACT 10.23): the bridge's validation, the stops it sends on its own,
/// and the director's rate limit. No toy, no WPF, no clock.
/// </summary>
public class BackRoomHapticTests
{
    private sealed class Rig
    {
        public readonly List<BackRoomHaptic> Got = new();
        public readonly List<string> Log = new();
        public readonly BackRoomBridgeTests.Clock Clock = new();
        public readonly BackRoomBridge Bridge;

        public Rig(bool wired = true)
        {
            Bridge = new BackRoomBridge(new BackRoomBridge.Deps
            {
                Post = _ => { },
                Relay = new BackRoomBridgeTests.Relay(),
                BuildInit = () => new { type = "init", protocol = 1 },
                CloseWindow = () => { },
                Schedule = Clock.Schedule,
                Haptic = wired ? new Action<BackRoomHaptic>(Got.Add) : null,
                Log = Log.Add,
            });
        }

        public void Send(string json) => Bridge.Handle(JObject.Parse(json));
        public List<BackRoomHaptic> Pulses => Got.Where(h => !h.IsStop).ToList();
        public List<BackRoomHaptic> Stops => Got.Where(h => h.IsStop).ToList();
    }

    [Fact]
    public void ValidPulse_ReachesTheDelegate_AsSent()
    {
        var rig = new Rig();
        rig.Send("{\"type\":\"haptic\",\"station\":\"breakout\",\"level\":0.45,\"ms\":120,\"tag\":\"perfect\"}");
        Assert.Equal(new BackRoomHaptic("breakout", 0.45, 120, "perfect"), Assert.Single(rig.Got));
        Assert.DoesNotContain(rig.Log, l => l.Contains("unhandled"));
    }

    [Theory]
    [InlineData("{\"type\":\"haptic\",\"level\":0.5,\"ms\":100}")]                                  // no station
    [InlineData("{\"type\":\"haptic\",\"station\":\"../etc\",\"level\":0.5,\"ms\":100}")]           // not a station id
    [InlineData("{\"type\":\"haptic\",\"station\":\"breakout\",\"level\":\"0.5\",\"ms\":100}")]     // string level
    [InlineData("{\"type\":\"haptic\",\"station\":\"breakout\",\"level\":true,\"ms\":100}")]
    [InlineData("{\"type\":\"haptic\",\"station\":\"breakout\",\"ms\":100}")]                       // no level
    [InlineData("{\"type\":\"haptic\",\"station\":\"breakout\",\"level\":0.5}")]                    // a pulse with no ms
    [InlineData("{\"type\":\"haptic\",\"station\":\"breakout\",\"level\":0.5,\"ms\":\"long\"}")]
    [InlineData("{\"type\":\"haptic\",\"station\":\"breakout\",\"level\":0.5,\"ms\":{\"a\":1}}")]
    public void Garbage_IsDropped(string json)
    {
        var rig = new Rig();
        rig.Send(json);
        Assert.Empty(rig.Got);
    }

    [Fact]
    public void NotANumber_IsDropped()
    {
        var m = new JObject { ["type"] = "haptic", ["station"] = "breakout", ["level"] = double.NaN, ["ms"] = 100 };
        Assert.Null(BackRoomBridge.ReadHaptic(m));
        m["level"] = 0.5; m["ms"] = double.PositiveInfinity;
        Assert.Null(BackRoomBridge.ReadHaptic(m));
    }

    [Theory]
    [InlineData(7.0, 100, 1.0, 100)]
    [InlineData(0.3, 1, 0.3, BackRoomBridge.HapticMinMs)]
    [InlineData(0.3, 999999, 0.3, BackRoomBridge.HapticMaxMs)]
    [InlineData(1, 250.7, 1.0, 250)]
    public void LevelAndMs_AreClamped(double level, double ms, double wantLevel, int wantMs)
    {
        var got = BackRoomBridge.ReadHaptic(new JObject { ["type"] = "haptic", ["station"] = "breakout", ["level"] = level, ["ms"] = ms });
        Assert.NotNull(got);
        Assert.Equal(wantLevel, got!.Level);
        Assert.Equal(wantMs, got.Ms);
    }

    [Fact]
    public void ZeroOrNegativeLevel_IsAStop_WhateverTheMsSays()
    {
        var stop = BackRoomBridge.ReadHaptic(JObject.Parse("{\"type\":\"haptic\",\"station\":\"breakout\",\"level\":0,\"ms\":0,\"tag\":\"stop\"}"));
        Assert.True(stop!.IsStop);
        var negative = BackRoomBridge.ReadHaptic(JObject.Parse("{\"type\":\"haptic\",\"station\":\"breakout\",\"level\":-4,\"ms\":\"x\"}"));
        Assert.True(negative!.IsStop);
        Assert.Equal(0, negative.Ms);
    }

    [Theory]
    [InlineData("\"a tag with spaces\"")]
    [InlineData("\"this-tag-is-far-too-long-to-be-a-tag\"")]
    [InlineData("42")]
    [InlineData("\"<script>\"")]
    public void ABadTag_ReadsAsEmpty_AndThePulseStillLands(string tag)
    {
        var got = BackRoomBridge.ReadHaptic(JObject.Parse("{\"type\":\"haptic\",\"station\":\"breakout\",\"level\":0.5,\"ms\":100,\"tag\":" + tag + "}"));
        Assert.Equal(string.Empty, got!.Tag);
        Assert.Equal(0.5, got.Level);
    }

    [Fact]
    public void Suspended_DropsPulses_ButAStopAlwaysLands()
    {
        var rig = new Rig();
        rig.Bridge.Suspend(true, "panic");
        Assert.Single(rig.Stops);   // the suspend itself stops the toy
        rig.Send("{\"type\":\"haptic\",\"station\":\"breakout\",\"level\":0.9,\"ms\":800,\"tag\":\"wall\"}");
        Assert.Empty(rig.Pulses);
        rig.Send("{\"type\":\"haptic\",\"station\":\"breakout\",\"level\":0,\"ms\":0,\"tag\":\"stop\"}");
        Assert.Equal(2, rig.Stops.Count);
        rig.Bridge.Suspend(false, "panic");
        rig.Send("{\"type\":\"haptic\",\"station\":\"breakout\",\"level\":0.9,\"ms\":800,\"tag\":\"wall\"}");
        Assert.Single(rig.Pulses);
    }

    [Fact]
    public void StationClose_Exit_AndClose_EachStopTheToy()
    {
        var rig = new Rig();
        rig.Send("{\"type\":\"station-close\",\"station\":\"breakout\"}");
        Assert.Equal("breakout", Assert.Single(rig.Stops).Station);

        rig.Send("{\"type\":\"exit\",\"reason\":\"back\"}");
        Assert.Equal(2, rig.Stops.Count);
        rig.Send("{\"type\":\"haptic\",\"station\":\"breakout\",\"level\":0.5,\"ms\":100}");
        Assert.Empty(rig.Pulses);   // closing

        var closing = new Rig();
        closing.Bridge.CloseNow();
        Assert.NotEmpty(closing.Stops);
    }

    [Fact]
    public void NoHapticDep_IsHarmless()
    {
        var rig = new Rig(wired: false);
        rig.Send("{\"type\":\"haptic\",\"station\":\"breakout\",\"level\":0.5,\"ms\":100}");
        rig.Bridge.Suspend(true, "minimise");
        rig.Bridge.CloseNow();
        Assert.Empty(rig.Got);
    }

    // ------------------------------------------------------------ the director's pure half

    [Fact]
    public void Limiter_HoldsAGapBetweenPulses()
    {
        var l = new BackRoomHapticDirector.Limiter();
        Assert.True(l.Accept(1000));
        Assert.False(l.Accept(1000 + BackRoomHapticDirector.Limiter.MinGapMs - 1));
        Assert.True(l.Accept(1000 + BackRoomHapticDirector.Limiter.MinGapMs));
    }

    [Fact]
    public void Limiter_CapsAFlood_AndRecoversWhenTheWindowRolls()
    {
        var l = new BackRoomHapticDirector.Limiter();
        int accepted = 0;
        for (long t = 0; t < 1000; t += 10) if (l.Accept(t)) accepted++;   // a page gone wrong: 100 Hz
        Assert.Equal(BackRoomHapticDirector.Limiter.MaxPerWindow, accepted);
        Assert.False(l.Accept(999));
        Assert.True(l.Accept(5000));
    }

    [Fact]
    public void Limiter_PassesAnHonestPage()
    {
        var l = new BackRoomHapticDirector.Limiter();
        for (long t = 0; t < 10_000; t += 320) Assert.True(l.Accept(t));   // the page's own three a second
    }

    [Fact]
    public void Priority_And_ButtplugStretch()
    {
        Assert.Equal(1, BackRoomHapticDirector.PriorityFor(0.12));
        Assert.Equal(2, BackRoomHapticDirector.PriorityFor(0.5));
        Assert.Equal(3, BackRoomHapticDirector.PriorityFor(0.9));
        Assert.Equal(120, BackRoomHapticDirector.DurationFor(120, buttplug: false));
        Assert.Equal(240, BackRoomHapticDirector.DurationFor(120, buttplug: true));
        Assert.Equal(BackRoomBridge.HapticMaxMs, BackRoomHapticDirector.DurationFor(1200, buttplug: true));
    }

    [Fact]
    public void Director_WithNoApp_DoesNothingAndDoesNotThrow()
    {
        BackRoomHapticDirector.OnHaptic(new BackRoomHaptic("breakout", 0.5, 100, "test"));
        BackRoomHapticDirector.OnHaptic(new BackRoomHaptic("breakout", 0, 0, "stop"));
        BackRoomHapticDirector.Stop();
    }
}
