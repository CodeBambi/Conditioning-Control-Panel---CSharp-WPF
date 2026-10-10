using System.Collections.Generic;
using ConditioningControlPanel.Services.Chaos;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>ChaosRunEngine on a stepped clock: the same order WPF RunTick calls it in
/// (Advance, ending-soon, CheckEnd, WaveAt), driven tick by tick with no timers.</summary>
public class ChaosRunEngineTests
{
    private sealed record Beat(double At, string What);

    /// <summary>Steps the run exactly like RunTick until it ends; logs every beat.</summary>
    private static List<Beat> Drive(ChaosRunState s)
    {
        var log = new List<Beat>();
        bool endingSoon = false;
        for (int guard = 0; guard < 100_000; guard++)
        {
            double t = ChaosRunEngine.Advance(s);
            if (!endingSoon && ChaosRunEngine.IsEndingSoon(s, t)) { endingSoon = true; log.Add(new(t, "ending")); }
            var end = ChaosRunEngine.CheckEnd(s, t);
            if (end == ChaosRunEngine.EndCheck.RunOver) { log.Add(new(t, "over")); return log; }
            if (end == ChaosRunEngine.EndCheck.Relapse) log.Add(new(t, "relapse"));
            var (wave, _) = ChaosRunEngine.WaveAt(s, t);
            if (wave > s.WaveIndex) { s.WaveIndex = wave; s.ActIndex = ChaosRunEngine.ActFor(wave); log.Add(new(t, "wave" + wave)); }
        }
        throw new Xunit.Sdk.XunitException("run never ended");
    }

    private static ChaosRunState Run(int sec, int waves) =>
        new(new ChaosRunConfig { DurationSec = sec, WaveCount = waves });

    [Fact]
    public void A_run_steps_through_its_waves_warns_at_ten_seconds_and_ends_on_time()
    {
        var log = Drive(Run(60, 3));
        Assert.Equal(new List<Beat> { new(20, "wave2"), new(40, "wave3"), new(50, "ending"), new(60, "over") }, log);
    }

    [Fact]
    public void An_armed_relapse_buys_exactly_one_more_loop()
    {
        var s = Run(60, 3);
        s.RelapseLoopArmed = true;
        var log = Drive(s);
        Assert.Equal(new List<Beat> { new(20, "wave2"), new(40, "wave3"), new(50, "ending"), new(60, "relapse"), new(60, "wave4"), new(80, "over") }, log);
        Assert.Equal(80, s.RunDurationSec);
        Assert.Equal(4, s.WaveCount);
    }

    [Fact]
    public void Lust_cools_each_tick_and_never_goes_negative()
    {
        var s = Run(60, 3);
        s.Heat = 0.002;
        ChaosRunEngine.Advance(s);
        Assert.Equal(0.0005, s.Heat, 6);
        ChaosRunEngine.Advance(s);
        Assert.Equal(0, s.Heat);
    }

    [Fact]
    public void Wave_progress_and_acts_follow_the_clock()
    {
        var s = Run(100, 10);   // 10 s loops
        var (wave, len) = ChaosRunEngine.WaveAt(s, 57.5);
        Assert.Equal((6, 10.0), (wave, len));
        Assert.Equal(0.75, s.WaveProgress, 6);
        Assert.Equal(1, ChaosRunEngine.ActFor(5));
        Assert.Equal(2, ChaosRunEngine.ActFor(6));
    }

    [Fact]
    public void The_relapse_loop_doubles_gold_and_drops()
    {
        var s = Run(60, 3);
        s.DropPerPop = 2;
        Assert.Equal((7, 2), (ChaosRunEngine.GoldScaled(s, 7), ChaosRunEngine.DropsPerPop(s)));
        s.ExtendOneLoop();
        Assert.Equal((14, 4), (ChaosRunEngine.GoldScaled(s, 7), ChaosRunEngine.DropsPerPop(s)));
        Assert.Equal(200, ChaosRunEngine.BasePoints(100));
    }

    [Fact]
    public void A_drafted_boon_lands_as_a_ribbon_tile_with_its_mult()
    {
        var s = Run(60, 3);
        var boon = ChaosBoonPool.All.Find(b => b.Id == "golden_touch")!;
        ChaosRunState.IconResolver = (cat, id) => cat + "/" + id;
        try { s.ApplyBoon(boon); }
        finally { ChaosRunState.IconResolver = null; }
        Assert.Equal(1 + boon.RunMultBonus, s.BoonMult, 6);
        var tile = Assert.Single(s.RunPickTiles);
        Assert.Equal(("golden_touch", "boons/golden_touch", false, true), (tile.Id, tile.Icon, tile.HasLevelBadge, tile.HasDesc));
    }
}
