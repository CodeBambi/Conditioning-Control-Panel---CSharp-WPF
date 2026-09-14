using System;
using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.BackRoom;
using Xunit;
using static ConditioningControlPanel.Tests.BackRoomFxTests;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// THE BACK ROOM effects pay XP the way the app's own effects do (CONTRACT 10.14). Flash images and subliminals already
/// pay inside FlashService / SubliminalService, so the dispatcher pays only the pictures that bypass them (gif-full,
/// gif-from, a wash with a picture), once each, and only when they actually show. Spirals, Brain Drain, the tunnel,
/// the rain and the glitch wash pay nothing, as in normal play. Also: a GIF index past the deal cycles.
/// </summary>
public class BackRoomFxXpTests
{
    private static (BackRoomFx Fx, FakeScheduler Clock, RecordingSink Sink, List<int> Paid) Make(
        BackRoomFxIntensity intensity = BackRoomFxIntensity.Normal, MotionLevel motion = MotionLevel.Full)
    {
        var clock = new FakeScheduler();
        var sink = new RecordingSink(clock);
        var paid = new List<int>();
        var fx = new BackRoomFx(sink, clock, () => new FxEnvironment(motion, intensity, FxGates.AllOn, BackRoomFxPlanTests.Woven),
            new Random(5), paid.Add);
        return (fx, clock, sink, paid);
    }

    private static BackRoomFxAck Fire(BackRoomFx fx, string id, string? token = null, BackRoomFxArgs? args = null, params string[] symbols)
        => fx.Fire(id, "wheel", symbols, Deal, args, token);

    [Fact]
    public void Policy_OnlyThePicturesThatBypassFlashService_PayHere()
    {
        foreach (var prim in Enum.GetValues<FxPrim>())
            Assert.Equal(prim is FxPrim.GifFull or FxPrim.GifFrom or FxPrim.Wash ? BackRoomFxXp.PictureXp : null, BackRoomFxXp.For(prim));
        Assert.Equal(4, BackRoomFxXp.PictureXp);   // FlashService: an image with no flash sound playing
    }

    [Fact]
    public void GifFrom_PaysOnce_WhenItShows_AndNothingWhenItCannot()
    {
        var (fx, clock, sink, paid) = Make();
        Fire(fx, "fx.gif_from", "a", null, "g1");
        clock.Advance(4000);
        Assert.Equal(new[] { 4 }, paid);

        sink.GifFromFound = false;
        Fire(fx, "fx.gif_from", "b", null, "g2");
        clock.Advance(4000);
        Assert.Equal(new[] { 4 }, paid);
    }

    [Fact]
    public void Wash_PaysOnlyWithAPicture_AndABusyOneNeverPays()
    {
        var (fx, clock, _, paid) = Make();
        Fire(fx, "fx.wash", "plain");
        clock.Advance(400);
        Assert.Empty(paid);

        Fire(fx, "fx.wash", "pic", null, "g0");
        Assert.Empty(Fire(fx, "fx.wash", "busy", null, "g1").Fired);   // inside the 360 ms gap: dropped
        clock.Advance(1000);
        Assert.Equal(new[] { 4 }, paid);
    }

    [Fact]
    public void ServicesThatAlreadyPay_AreNotPaidAgain()
    {
        // fx.gif_storm is flash-burst + rain + glitch: FlashService pays each image itself.
        var (fx, clock, sink, paid) = Make(BackRoomFxIntensity.Full);
        fx.Fire("fx.gif_storm", "slot", Array.Empty<string>(), Deal);
        clock.Advance(10_000);
        Assert.Contains(sink.Calls, c => c.Call.StartsWith("flash:"));
        Assert.Empty(paid);

        // fx.sub_cascade: nine words (SubliminalService pays each) and one gif-full (paid here, once).
        fx.Fire("fx.sub_cascade", "slot", new[] { "gif2" }, Deal);
        clock.Advance(10_000);
        Assert.Equal(9, sink.Calls.Count(c => c.Call.StartsWith("sub:")));
        Assert.Equal(new[] { 4 }, paid);
    }

    [Fact]
    public void SpiralsDrainHazeMeltAndTunnel_PayNothing_AsInNormalPlay()
    {
        var (fx, clock, sink, paid) = Make(BackRoomFxIntensity.Full);
        Fire(fx, "fx.loom_spiral", "s", new BackRoomFxArgs(Preset: "wake", Ms: 2000));
        clock.Advance(3000);
        fx.Fire("fx.spiral_full", "slot", Array.Empty<string>(), Deal);
        clock.Advance(5000);
        fx.Fire("fx.melt", "slot", Array.Empty<string>(), Deal);
        clock.Advance(10_000);
        Fire(fx, "fx.haze", "h", new BackRoomFxArgs(Ms: 2000));
        fx.Tunnel("wheel", 0.7);
        clock.Advance(3000);
        Assert.Contains(sink.Calls, c => c.Call.StartsWith("spiral:"));
        Assert.Contains(sink.Calls, c => c.Call.StartsWith("drain:"));
        Assert.Contains(sink.Calls, c => c.Call.StartsWith("tunnel:"));
        Assert.Empty(paid);
    }

    [Fact]
    public void AMergedDuplicate_PaysOnce_AndACancelledOnsetNever()
    {
        var (fx, clock, _, paid) = Make(BackRoomFxIntensity.Full);
        fx.Fire("fx.jackpot", "slot", new[] { "gif1" }, Deal);   // Full: gif-full at 4000 ms
        fx.Fire("fx.jackpot", "slot", new[] { "gif1" }, Deal);   // merged into the one on stage
        clock.Advance(20_000);
        Assert.Equal(new[] { 4 }, paid);

        Fire(fx, "fx.gif_from", "late", new BackRoomFxArgs(Ms: 3400), "g0");
        fx.Fire("fx.jackpot", "slot", new[] { "gif1" }, Deal);
        fx.CancelAll();
        clock.Advance(20_000);
        Assert.Equal(new[] { 4 }, paid);   // cancelled before either onset ran: nothing showed, nothing paid
    }

    [Fact]
    public void GifIndexPastTheDeal_Cycles_LikeThePagesDrawIt()
    {
        var two = new BackRoomMediaDeal(1, Deal.Gifs.Take(2).ToArray(), Deal.Words);
        for (int seed = 0; seed < 10; seed++)
        {
            var media = BackRoomFxPlan.ResolveSymbols(new[] { "gif0", "gif1", "gif2", "gif3", "g12" }, two, new Random(seed));
            Assert.Equal(new[] { "g0", "g1", "g0", "g1", "g0" }, media.Gifs.Select(g => g.Key));
        }
        var none = new BackRoomMediaDeal(1, Array.Empty<BackRoomGif>(), Deal.Words);
        Assert.Empty(BackRoomFxPlan.ResolveSymbols(new[] { "gif3" }, none, new Random(0)).Gifs);
    }
}
