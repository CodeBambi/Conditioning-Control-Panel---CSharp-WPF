using System;
using System.Linq;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.BackRoom;
using Xunit;
using static ConditioningControlPanel.Tests.BackRoomFxTests;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// THE BACK ROOM dispatcher, Hypno v3 half (CONTRACT 10.13.B): the gaps that drop instead of delaying,
/// the one-hero queue for the new non-heroes, holds released by token or by station-close, the tunnel
/// feed (always honoured, Calm, 10 a second) and cancel on suspend/close. Fake clock, recording sink.
/// </summary>
public class BackRoomHypnoFxTests
{
    private static BackRoomFxAck Fire(BackRoomFx fx, string id, string station, string? token, BackRoomFxArgs? args = null, params string[] symbols)
        => fx.Fire(id, station, symbols, Deal, args, token);

    [Fact]
    public void Wash_InsideTheGap_IsDroppedBusy_NotDelayed()
    {
        var (fx, clock, sink) = Make();
        Assert.Equal(new[] { "wash" }, Fire(fx, "fx.wash", "wheel", "t1", new BackRoomFxArgs(Color: "#5fffd0", Strength: 0.55)).Fired);
        clock.Advance(359);
        var busy = Fire(fx, "fx.wash", "wheel", "t2");
        Assert.Empty(busy.Fired);
        Assert.Equal(new BackRoomFxSkip("wash", BackRoomFxSkipReason.Busy), Assert.Single(busy.Skipped));
        clock.Advance(1);
        Assert.Equal(new[] { "wash" }, Fire(fx, "fx.wash", "wheel", "t3", null, "g2").Fired);
        clock.Advance(5000);
        Assert.Equal(new[] { (0L, "wash:#5fffd0:0.231:"), (360L, "wash:#9b6bff:0.294:g2") },
            sink.Calls.Where(c => c.Call.StartsWith("wash")).ToArray());
    }

    [Fact]
    public void GifFrom_OneAtATime_TheSecondIsBusy()
    {
        var (fx, clock, sink) = Make();
        var rect = new FxCssRect(612, 188, 60, 44);
        Assert.Equal(new[] { "gif-from" }, Fire(fx, "fx.gif_from", "wheel", "a", new BackRoomFxArgs(From: rect, Ms: 3400), "g1").Fired);
        clock.Advance(3000);
        Assert.Equal(BackRoomFxSkipReason.Busy, Assert.Single(Fire(fx, "fx.gif_from", "wheel", "b").Skipped).Why);
        clock.Advance(400);
        Assert.Single(Fire(fx, "fx.gif_from", "wheel", "c", new BackRoomFxArgs(Scale: 0.46), "g0").Fired);
        clock.Advance(10_000);
        Assert.Equal(new[] { (0L, "giffrom:g1:612,188,60,44:3400:1:1"), (3400L, "giffrom:g0:centre:3400:0.46:1") },
            sink.Calls.Where(c => c.Call.StartsWith("giffrom")).ToArray());
    }

    [Fact]
    public void JackpotMoment_WashAndPictureWaitBehindASlotHero_LikeAnyNonHero()
    {
        var (fx, clock, sink) = Make();
        Fire(fx, "fx.jackpot", "slot", "hero");
        clock.Advance(500);
        Fire(fx, "fx.loom_spiral", "wheel", "s", new BackRoomFxArgs(Preset: "screen", Ms: 4200, Alpha: 0.9));
        Fire(fx, "fx.gif_from", "wheel", "g", new BackRoomFxArgs(Ms: 4600, Scale: 0.46));
        Fire(fx, "fx.wash", "wheel", "w", new BackRoomFxArgs(Color: "#e8c27a", Strength: 1));
        clock.Advance(10_000);
        Assert.Contains((4000L, "spiral:screen.gif:4200:0.9:False:False"), sink.Calls);
        Assert.Contains(sink.Calls, c => c.At == 4000 && c.Call.StartsWith("giffrom:") && c.Call.Contains(":4600:0.46:"));
        Assert.Contains(sink.Calls, c => c.At == 4000 && c.Call.StartsWith("wash:#"));
    }

    [Fact]
    public void Release_FadesWhatTheTokenHolds_AndIgnoresOtherStations()
    {
        var (fx, clock, sink) = Make(BackRoomFxIntensity.Full);
        Fire(fx, "fx.loom_spiral", "roulette", "wake1", new BackRoomFxArgs(Preset: "wake", Hold: true, Alpha: 0.65));
        Fire(fx, "fx.haze", "roulette", "haze1", new BackRoomFxArgs(Hold: true));
        clock.Advance(1000);
        Assert.Contains((0L, "spiral:wake.gif:20000:0.65:True:False"), sink.Calls);
        Assert.Contains((0L, "drain:20000:0.5:False"), sink.Calls);

        fx.Release("wake1", "wheel");      // not that station's token
        Assert.DoesNotContain(sink.Calls, c => c.Call == "spiral-release");
        fx.Release("wake1", "roulette");
        fx.Release("haze1", "roulette");
        fx.Release("wake1", "roulette");   // twice is once
        Assert.Equal(1, sink.Calls.Count(c => c.Call == "spiral-release"));
        Assert.Equal(1, sink.Calls.Count(c => c.Call == "drain-release"));
    }

    [Fact]
    public void Release_OfAReplacedSpiral_LeavesTheNewOneAlone()
    {
        var (fx, clock, sink) = Make();
        Fire(fx, "fx.loom_spiral", "roulette", "old", new BackRoomFxArgs(Hold: true));
        clock.Advance(500);
        Fire(fx, "fx.loom_spiral", "wheel", "new", new BackRoomFxArgs(Ms: 4200));
        clock.Advance(10);
        fx.Release("old", "roulette");
        Assert.DoesNotContain(sink.Calls, c => c.Call == "spiral-release");
    }

    [Fact]
    public void Release_BeforeAQueuedHoldStarts_MeansItNeverShows()
    {
        var (fx, clock, sink) = Make();
        Fire(fx, "fx.jackpot", "slot", "hero");
        clock.Advance(100);
        Assert.Single(Fire(fx, "fx.loom_spiral", "roulette", "late", new BackRoomFxArgs(Hold: true)).Fired);
        clock.Advance(100);
        fx.Release("late", "roulette");
        clock.Advance(10_000);
        Assert.Single(sink.Calls, c => c.Call.StartsWith("spiral:"));   // only the jackpot's own
    }

    [Fact]
    public void StationClose_ReleasesThatStationsHolds_AndCancelsItsTunnel()
    {
        var (fx, clock, sink) = Make();
        Fire(fx, "fx.loom_spiral", "roulette", "r1", new BackRoomFxArgs(Hold: true));
        fx.Tunnel("roulette", 0.6);
        clock.Advance(50);
        fx.ReleaseStation("wheel");
        Assert.DoesNotContain(sink.Calls, c => c.Call is "spiral-release" or "tunnel-cancel");
        fx.ReleaseStation("roulette");
        Assert.Contains(sink.Calls, c => c.Call == "spiral-release");
        Assert.Contains(sink.Calls, c => c.Call == "tunnel-cancel");
    }

    [Fact]
    public void Tunnel_TenASecond_TheLaterReplacesTheEarlier()
    {
        var (fx, clock, sink) = Make();
        fx.Tunnel("wheel", 0.2);
        clock.Advance(30);
        fx.Tunnel("wheel", 0.4);
        clock.Advance(30);
        fx.Tunnel("wheel", 0.62);
        clock.Advance(200);
        fx.Tunnel("wheel", 0.7);
        clock.Advance(1);
        Assert.Equal(new[] { (0L, "tunnel:0.2"), (100L, "tunnel:0.62"), (260L, "tunnel:0.7") },
            sink.Calls.Where(c => c.Call.StartsWith("tunnel")).ToArray());
    }

    [Fact]
    public void Tunnel_AlwaysHonoured_HalvedUnderCalm_ClampedAndEasedAtOff()
    {
        var (normal, _, normalSink) = Make();
        normal.Tunnel("wheel", 0.9);
        Assert.Equal("tunnel:0.9", Assert.Single(normalSink.Calls).Call);

        var (calm, _, calmSink) = Make(BackRoomFxIntensity.Calm);
        calm.Tunnel("cards", 0.75);
        Assert.Equal("tunnel:0.375", Assert.Single(calmSink.Calls).Call);

        // Reduced motion is not a gate: the tunnel still plays (and still eases; there is no still variant).
        var (off, _, offSink) = Make(motion: MotionLevel.Off);
        off.Tunnel("cards", 2);
        off.Tunnel("cards", double.NaN);
        Assert.Equal("tunnel:1", Assert.Single(offSink.Calls).Call);   // clamped to 1, nothing forces Calm
    }

    [Fact]
    public void CancelAll_DropsHoldsAndAPendingTunnel_AndFreesGifFrom()
    {
        var (fx, clock, sink) = Make();
        Fire(fx, "fx.gif_from", "wheel", "g");
        Fire(fx, "fx.loom_spiral", "roulette", "s", new BackRoomFxArgs(Hold: true));
        fx.Tunnel("roulette", 0.3);
        fx.Tunnel("roulette", 0.5);   // pending
        fx.CancelAll();
        Assert.Equal(0, fx.HoldCount);
        clock.Advance(1000);
        Assert.DoesNotContain(sink.Calls, c => c.Call == "tunnel:0.5");
        fx.Release("s", "roulette");
        Assert.DoesNotContain(sink.Calls, c => c.Call == "spiral-release");   // StopAll already took it
        Assert.Single(Fire(fx, "fx.gif_from", "wheel", "g2").Fired);
    }

    [Fact]
    public void GifFrom_ReleasedBeforeItsOnset_OrWithNoFile_FreesTheSlot()
    {
        var (fx, clock, sink) = Make();
        Fire(fx, "fx.jackpot", "slot", "hero");
        clock.Advance(500);
        Assert.Single(Fire(fx, "fx.gif_from", "wheel", "g", new BackRoomFxArgs(Ms: 3400)).Fired);   // waits behind the hero
        fx.Release("g", "wheel");
        clock.Advance(2500);
        Assert.DoesNotContain(sink.Calls, c => c.Call.StartsWith("giffrom"));
        Assert.Single(Fire(fx, "fx.gif_from", "wheel", "g2").Fired);   // not busy for a gif that never showed

        sink.GifFromFound = false;
        clock.Advance(10_000);
        Assert.Single(Fire(fx, "fx.gif_from", "wheel", "nofile").Fired);
        clock.Advance(1);
        Assert.Single(Fire(fx, "fx.gif_from", "wheel", "next").Fired);
    }

    [Fact]
    public void MergedBehindAHero_TheSecondTokenSharesTheHold()
    {
        var (fx, clock, sink) = Make();
        Fire(fx, "fx.jackpot", "slot", "hero");
        clock.Advance(500);
        Fire(fx, "fx.loom_spiral", "roulette", "s1", new BackRoomFxArgs(Hold: true));
        Fire(fx, "fx.loom_spiral", "roulette", "s2", new BackRoomFxArgs(Hold: true));   // merged into s1's copy
        clock.Advance(5000);   // past the 4 s hero
        Assert.Single(sink.Calls, c => c.Call.StartsWith("spiral:") && c.Call.Contains(":True:"));   // the held one plays once
        fx.Release("s1", "roulette");
        Assert.DoesNotContain(sink.Calls, c => c.Call == "spiral-release");   // s2 still holds it
        fx.Release("s2", "roulette");
        Assert.Contains(sink.Calls, c => c.Call == "spiral-release");
    }

    [Fact]
    public void Holds_DoNotPileUp_OverALongSession()
    {
        var (fx, clock, _) = Make();
        for (int i = 0; i < 400; i++)
        {
            Fire(fx, "fx.wash", "cards", "tok" + i);
            clock.Advance(1000);
        }
        Assert.True(fx.HoldCount < 20, $"{fx.HoldCount} holds tracked");
    }
}
