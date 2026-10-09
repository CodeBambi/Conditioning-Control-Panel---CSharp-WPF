using System;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Flash;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>WPF 7.1.5 flash glow / lucky / hydra / corners / stay / exit rules, now in Core.</summary>
public class FlashFxRulesTests
{
    private sealed class FixedRandom : Random
    {
        private readonly double _v;
        public FixedRandom(double v) => _v = v;
        public override double NextDouble() => _v;
        public override int Next(int maxValue) => 0;
    }

    [Fact]
    public void Lucky_NeedsSkill_AndNeverOnHydraOrMirror()
    {
        Assert.Equal(10, FlashFxRules.RollLucky(true, 0, false, new FixedRandom(0.01)));
        Assert.Equal(1, FlashFxRules.RollLucky(true, 0, false, new FixedRandom(0.06)));
        Assert.Equal(1, FlashFxRules.RollLucky(false, 0, false, new FixedRandom(0.0)));
        Assert.Equal(1, FlashFxRules.RollLucky(true, 1, false, new FixedRandom(0.0)));
        Assert.Equal(1, FlashFxRules.RollLucky(true, 0, true, new FixedRandom(0.0)));
    }

    [Fact]
    public void Glow_OnlyLuckySparkleOrNatasha()
    {
        Assert.False(FlashFxRules.ResolveGlow(true, PerformanceTier.Quality, false, 0, false).HasGlow);
        var lucky = FlashFxRules.ResolveGlow(true, PerformanceTier.Quality, true, 0, false);
        Assert.Equal(FlashFxRules.Gold, lucky.Rgb);
        Assert.Equal(24, lucky.BlurRadius);   // 60 capped at Quality 24
        Assert.True(lucky.LuckyPulse);
        var pink = FlashFxRules.ResolveGlow(true, PerformanceTier.Balanced, false, 1, false);
        Assert.Equal(FlashFxRules.HotPink, pink.Rgb);
        Assert.Equal(18, pink.BlurRadius);
        Assert.Equal(0.5, pink.Opacity);
        // The toggle and the Performance tier kill lucky/sparkle glow.
        Assert.False(FlashFxRules.ResolveGlow(false, PerformanceTier.Quality, true, 3, false).HasGlow);
        Assert.False(FlashFxRules.ResolveGlow(true, PerformanceTier.Performance, true, 3, false).HasGlow);
        // Natasha ignores the toggle: a thin red halo.
        var red = FlashFxRules.ResolveGlow(false, PerformanceTier.Quality, false, 2, true);
        Assert.Equal(14, red.BlurRadius);
        Assert.Equal(0.2, red.Opacity);
        Assert.True(red.NatashaBlink);
    }

    [Fact]
    public void LuckyPulse_SwingsBetweenBaseAnd160Percent()
    {
        Assert.Equal((10.0, 0.7), FlashFxRules.LuckyPulseAt(10, 0));
        var peak = FlashFxRules.LuckyPulseAt(10, 0.4);
        Assert.Equal(16, peak.Blur, 6);
        Assert.Equal(1.0, peak.Opacity, 6);
    }

    [Fact]
    public void Xp_MatchesWpf()
    {
        Assert.Equal(4, FlashFxRules.Xp(false, true, 0, 1));
        Assert.Equal(80, FlashFxRules.Xp(true, true, 0, 10));
        Assert.Equal(6, FlashFxRules.Xp(true, false, 1, 1));
        Assert.Equal(1, FlashFxRules.Xp(true, false, 2, 1));
        Assert.Equal(8, FlashFxRules.Xp(true, true, 3, 1));
    }

    [Fact]
    public void Hydra_TwoChildrenUnderTheLimit()
    {
        Assert.Equal(0, FlashFxRules.HydraSpawnCount(false, 20, false, false, 0, 0));
        Assert.Equal(2, FlashFxRules.HydraSpawnCount(true, 20, false, false, 0, 0));
        Assert.Equal(2, FlashFxRules.HydraSpawnCount(true, 5, false, false, 0, 3));
        Assert.Equal(0, FlashFxRules.HydraSpawnCount(true, 5, false, false, 0, 4));
        Assert.Equal(2, FlashFxRules.HydraSpawnCount(true, 99, false, false, 0, 17));   // capped at 20
        Assert.Equal(0, FlashFxRules.HydraSpawnCount(true, 99, false, false, 0, 19));
        Assert.Equal(2, FlashFxRules.HydraSpawnCount(true, 20, false, true, 0, 0));     // gaze: first hop only
        Assert.Equal(0, FlashFxRules.HydraSpawnCount(true, 20, false, true, 1, 0));
        Assert.Equal(0, FlashFxRules.HydraSpawnCount(true, 20, true, false, 0, 0));     // remix
        Assert.Equal(1000, FlashFxRules.HydraChildLifetimeMs(true, 6000, 200));
        Assert.Equal(6000, FlashFxRules.HydraChildLifetimeMs(false, 6000, 200));
    }

    [Fact]
    public void Corners_RoundOnlyWithV2Grant()
    {
        Assert.Equal(0, FlashCorners.Resolve(true, false, false, 400, 1));
        Assert.Equal(12, FlashCorners.Resolve(true, false, true, 400, 1));
        Assert.Equal(28, FlashCorners.Resolve(true, true, false, 400, 1), 6);
        Assert.Equal(10, FlashCorners.Resolve(false, false, true, 40, 1));
    }

    [Fact]
    public void Stay_AppliesToClickableAmbientOnly()
    {
        Assert.True(FlashStayRule.Applies(true, true, false));
        Assert.False(FlashStayRule.Applies(true, false, false));
        Assert.False(FlashStayRule.Applies(true, true, true));
        Assert.Equal(40, FlashStayRule.Cap(30, true, true));
        Assert.True(FlashStayRule.ScreenFull(true, 40, 40));
    }

    [Fact]
    public void Exit_MixNeverRepeats_OffIsAFade()
    {
        var rng = new Random(1);
        FlashExitStyle? last = null;
        for (var i = 0; i < 50; i++)
        {
            var s = FlashExit.Pick(FlashExitStyle.Mix, last, rng);
            Assert.NotEqual(last, s);
            last = s;
        }
        Assert.Null(FlashExit.Pick(FlashExitStyle.None, null, rng));
        var off = FlashExit.Begin(FlashExitStyle.Spiral, MotionLevel.Off, 1);
        Assert.Equal(0.12, off.DurationSec);
    }
}
