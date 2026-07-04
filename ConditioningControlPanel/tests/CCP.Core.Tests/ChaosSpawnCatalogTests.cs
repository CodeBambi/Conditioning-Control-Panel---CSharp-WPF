using System;
using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Core.Services.Chaos;
using Xunit;

namespace ConditioningControlPanel.Core.Tests;

/// <summary>
/// Pins the faithful port of the WPF chaos bubble spawn catalog
/// (WPF ChaosBubbleVariants.cs -> Core ChaosSpawnCatalog): the 8-row variant table,
/// the weighted intensity-gated picker with its two fallback levels, the ordinary
/// Build size/strength/motion/fuse formulas, and every special-bubble builder
/// (golden, heart, droplet, heavy, prism, brittle, echo, echo child, chaperone,
/// bound, tease, darter, welcome shower). Randomness is injected, so tests drive
/// exact values through a scripted Random.
/// </summary>
public class ChaosSpawnCatalogTests
{
    /// <summary>Deterministic Random: NextDouble()/Next(max) pop from scripted queues (0/0.0 when exhausted).</summary>
    private sealed class ScriptedRandom : Random
    {
        private readonly Queue<double> _doubles;
        private readonly Queue<int> _ints;

        public ScriptedRandom(double[]? doubles = null, int[]? ints = null) : base(0)
        {
            _doubles = new Queue<double>(doubles ?? Array.Empty<double>());
            _ints = new Queue<int>(ints ?? Array.Empty<int>());
        }

        public override double NextDouble() => _doubles.Count > 0 ? _doubles.Dequeue() : 0.0;
        public override int Next(int maxValue) => _ints.Count > 0 ? Math.Min(_ints.Dequeue(), maxValue - 1) : 0;
    }

    private static string TintHex(ChaosSpawnCatalog.VariantDef v) => $"{v.TintR:X2}{v.TintG:X2}{v.TintB:X2}";
    private static string TintHex(ChaosBubbleSpec s) => $"{s.TintR:X2}{s.TintG:X2}{s.TintB:X2}";

    // ================================================================
    // (1) The 8-row variant table, row by row (WPF ChaosBubbleVariants.cs:649-676)
    // ================================================================

    [Theory]
    [InlineData(0, "flash",       "flash",       null,          false, 150, 210, ChaosMotion.FloatUp,    "FFD0E8", "",  3.0,  0.00, 0,    0)]
    [InlineData(1, "subliminal",  "subliminal",  null,          false, 170, 220, ChaosMotion.FloatUp,    "B080FF", "♥", 3.0,  0.00, 0,    0)]
    [InlineData(2, "pink",        "pink",        "pink_filter", true,  180, 240, ChaosMotion.RainDown,   "FF3DA5", "◑", 2.0,  0.10, 3500, 5000)]
    [InlineData(3, "spiral",      "spiral",      "spiral",      true,  180, 240, ChaosMotion.RoamBounce, "40D0C0", "◎", 2.0,  0.15, 3500, 5000)]
    [InlineData(4, "braindrain",  "braindrain",  "braindrain",  true,  240, 320, ChaosMotion.RoamBounce, "4060C0", "☁", 1.4,  0.25, 4500, 6500)]
    [InlineData(5, "bambifreeze", "bambifreeze", null,          false, 190, 250, ChaosMotion.FloatUp,    "8AE6FF", "❄", 0.5,  0.15, 0,    0)]
    [InlineData(6, "video",       "video",       null,          true,  240, 300, ChaosMotion.RainDown,   "E0404D", "▶", 0.5,  0.50, 5000, 7000)]
    [InlineData(7, "htlink",      "htlink",      null,          true,  200, 280, ChaosMotion.FloatUp,    "FFC83D", "▼", 0.45, 0.60, 4500, 6500)]
    public void Table_Row_MatchesWpfVerbatim(int index, string id, string payloadKind, string? overlayKind,
        bool isLive, double minSize, double maxSize, ChaosMotion motion, string tintHex, string label,
        double weight, double minIntensity, int fuseMin, int fuseMax)
    {
        var v = ChaosSpawnCatalog.All[index];
        Assert.Equal(id, v.Id);
        Assert.Equal(payloadKind, v.PayloadKind);
        Assert.Equal(overlayKind, v.OverlayKind);
        Assert.Equal(isLive, v.IsLive);
        Assert.Equal(minSize, v.MinSize);
        Assert.Equal(maxSize, v.MaxSize);
        Assert.Equal(motion, v.Motion);
        Assert.Equal(tintHex, TintHex(v));
        Assert.Equal(label, v.Label);
        Assert.Equal(weight, v.Weight);
        Assert.Equal(minIntensity, v.MinIntensity);
        Assert.Equal(fuseMin, v.FuseMinMs);
        Assert.Equal(fuseMax, v.FuseMaxMs);
    }

    [Fact]
    public void Table_HasExactlyEightRows_AndAllIdsInOrder()
    {
        Assert.Equal(8, ChaosSpawnCatalog.All.Count);
        Assert.Equal(
            new[] { "flash", "subliminal", "pink", "spiral", "braindrain", "bambifreeze", "video", "htlink" },
            ChaosSpawnCatalog.AllIds());
    }

    // ================================================================
    // (2) Pick: intensity gating, enabledIds filter, fallbacks (WPF ChaosBubbleVariants.cs:682-704)
    // ================================================================

    [Fact]
    public void Pick_IntensityZero_OnlyFlashAndSubliminalEligible()
    {
        var rng = new Random(42);
        for (int i = 0; i < 200; i++)
        {
            var spec = ChaosSpawnCatalog.Pick(0.0, 1.0, null, null, 1.0, 1.0, 0.0, rng);
            Assert.Contains(spec.VariantId, new[] { "flash", "subliminal" });
        }
    }

    [Fact]
    public void Pick_IntensityPointSix_AllEightVariantsEligible()
    {
        var rng = new Random(42);
        var seen = new HashSet<string>();
        for (int i = 0; i < 2000; i++)
            seen.Add(ChaosSpawnCatalog.Pick(0.6, 1.0, null, null, 1.0, 1.0, 0.0, rng).VariantId);
        Assert.Equal(8, seen.Count);
    }

    [Fact]
    public void Pick_EnabledIdsFilter_RestrictsPool()
    {
        var rng = new Random(7);
        for (int i = 0; i < 50; i++)
        {
            var spec = ChaosSpawnCatalog.Pick(1.0, 1.0, null, new[] { "pink" }, 1.0, 1.0, 0.0, rng);
            Assert.Equal("pink", spec.VariantId);
        }
    }

    [Fact]
    public void Pick_FallbackLevel1_IgnoresIntensityGateWhenPoolEmpty()
    {
        // video needs intensity >= 0.50; at intensity 0 the gated pool is empty, so
        // Pick falls back to the enabled-but-gated pool (WPF ChaosBubbleVariants.cs:688-690).
        var spec = ChaosSpawnCatalog.Pick(0.0, 1.0, null, new[] { "video" }, 1.0, 1.0, 0.0, new Random(3));
        Assert.Equal("video", spec.VariantId);
    }

    [Fact]
    public void Pick_FallbackLevel2_LastDitchIsFlash()
    {
        var spec = ChaosSpawnCatalog.Pick(0.0, 1.0, null, new[] { "no_such_variant" }, 1.0, 1.0, 0.0, new Random(3));
        Assert.Equal("flash", spec.VariantId);
    }

    [Fact]
    public void Pick_WeightedWalk_SubtractsWeightsInTableOrder()
    {
        // Pool at intensity 0 = flash(3.0) + subliminal(3.0), total 6.
        // roll 0.4*6=2.4 lands in flash; roll 0.6*6=3.6 lands in subliminal.
        var flash = ChaosSpawnCatalog.Pick(0.0, 1.0, null, null, 1.0, 1.0, 0.0,
            new ScriptedRandom(doubles: new[] { 0.4, 0.5 }));
        Assert.Equal("flash", flash.VariantId);

        var subliminal = ChaosSpawnCatalog.Pick(0.0, 1.0, null, null, 1.0, 1.0, 0.0,
            new ScriptedRandom(doubles: new[] { 0.6, 0.5 }));
        Assert.Equal("subliminal", subliminal.VariantId);
    }

    [Fact]
    public void Pick_FullPool_HighRollLandsOnHtlink()
    {
        // Full-pool total weight = 12.85; cumulative up to video = 12.4, so
        // roll 0.99*12.85 = 12.7215 walks past every earlier row and lands on htlink.
        var spec = ChaosSpawnCatalog.Pick(1.0, 1.0, null, null, 1.0, 1.0, 0.0,
            new ScriptedRandom(doubles: new[] { 0.99, 0.5 }, ints: new[] { 0 }));
        Assert.Equal("htlink", spec.VariantId);
    }

    // ================================================================
    // (3) Build: size + strength formulas (WPF ChaosBubbleVariants.cs:714-775)
    // ================================================================

    [Fact]
    public void Build_SizeAndStrength_ExactValues_Flash()
    {
        // t = clamp(0.5*0.7 + 0.4*0.45, 0, 1) = 0.53; classic = 150 + 60*0.53 = 181.8
        // strength = round(clamp((181.8-150)/170, 0, 1)*100) = round(18.705...) = 19
        // visual = 0.75 * max(0.5, 1.0) = 0.75 -> SizePx = 136.35
        var spec = ChaosSpawnCatalog.Build(ChaosSpawnCatalog.All[0], 0.4, 1.0, null, 1.0, 1.0, 0.0,
            new ScriptedRandom(doubles: new[] { 0.5 }));
        Assert.Equal(136.35, spec.SizePx, 6);
        Assert.Equal(19, spec.Strength);
        Assert.Equal("flash", spec.VariantId);
        Assert.Equal("flash", spec.PayloadKind);
        Assert.Null(spec.OverlayKind);
        Assert.False(spec.IsLive);
        Assert.Equal(0, spec.FuseMs);
        Assert.False(spec.IsFreeze);
        Assert.Equal(ChaosMotion.FloatUp, spec.Motion);
        Assert.Equal("FFD0E8", TintHex(spec));
    }

    [Fact]
    public void Build_SizeBias_ClampsAtBandTop()
    {
        // t = clamp(0.9*0.7 + 1.0*0.45, 0, 1) = clamp(1.08) = 1 -> classic = MaxSize.
        var spec = ChaosSpawnCatalog.Build(ChaosSpawnCatalog.All[0], 1.0, 1.0, null, 1.0, 1.0, 0.0,
            new ScriptedRandom(doubles: new[] { 0.9 }));
        Assert.Equal(210 * 0.75, spec.SizePx, 6);
        Assert.Equal(35, spec.Strength);   // round(clamp(60/170,0,1)*100) = 35
    }

    [Fact]
    public void Build_Giants_GetExtraSeventyPercentScale()
    {
        // video: classic 240 -> visual 0.75*0.70 = 0.525 -> 126; strength keyed to CLASSIC size:
        // round(clamp(90/170,0,1)*100) = 53. Live: fuse = 5000 + Next(2000)=0 at intensity 0.
        var spec = ChaosSpawnCatalog.Build(ChaosSpawnCatalog.All[6], 0.0, 1.0, null, 1.0, 1.0, 0.0,
            new ScriptedRandom(doubles: new[] { 0.0 }, ints: new[] { 0 }));
        Assert.Equal(126.0, spec.SizePx, 6);
        Assert.Equal(53, spec.Strength);
        Assert.Equal(5000, spec.FuseMs);
        Assert.True(spec.IsLive);
    }

    [Fact]
    public void Build_SizeScale_FlooredAtHalf_AndSwells()
    {
        var floored = ChaosSpawnCatalog.Build(ChaosSpawnCatalog.All[0], 0.0, 1.0, null, 1.0, 0.2, 0.0,
            new ScriptedRandom(doubles: new[] { 0.0 }));
        Assert.Equal(150 * 0.75 * 0.5, floored.SizePx, 6);   // max(0.5, 0.2) = 0.5

        var swollen = ChaosSpawnCatalog.Build(ChaosSpawnCatalog.All[0], 0.0, 1.0, null, 1.0, 2.0, 0.0,
            new ScriptedRandom(doubles: new[] { 0.0 }));
        Assert.Equal(150 * 0.75 * 2.0, swollen.SizePx, 6);
    }

    [Fact]
    public void Build_EffectIntensity_ScalesStrength_TruncatingAndClamping()
    {
        // braindrain t=1 -> classic 320 -> strength 100.
        var scaled = ChaosSpawnCatalog.Build(ChaosSpawnCatalog.All[4], 1.0, 1.0, null, 0.75, 1.0, 0.0,
            new ScriptedRandom(doubles: new[] { 1.0 }, ints: new[] { 0 }));
        Assert.Equal(75, scaled.Strength);   // (int)clamp(100*0.75) = 75

        var clamped = ChaosSpawnCatalog.Build(ChaosSpawnCatalog.All[4], 1.0, 1.0, null, 1.5, 1.0, 0.0,
            new ScriptedRandom(doubles: new[] { 1.0 }, ints: new[] { 0 }));
        Assert.Equal(100, clamped.Strength);   // clamp(150) = 100

        // Truncation, not rounding: flash strength 19 at effectIntensity 0.5 -> (int)9.5 = 9.
        var truncated = ChaosSpawnCatalog.Build(ChaosSpawnCatalog.All[0], 0.4, 1.0, null, 0.5, 1.0, 0.0,
            new ScriptedRandom(doubles: new[] { 0.5 }));
        Assert.Equal(9, truncated.Strength);
    }

    [Fact]
    public void Build_WithSeededRandom_MatchesFormulaReplication()
    {
        var spec = ChaosSpawnCatalog.Build(ChaosSpawnCatalog.All[2], 0.5, 1.0, null, 0.85, 1.0, 0.0,
            new Random(1234));

        // Replicate the exact WPF formula with an identically-seeded Random.
        var rng = new Random(1234);
        double t = Math.Clamp(rng.NextDouble() * 0.7 + 0.5 * 0.45, 0, 1);
        double classic = 180 + (240 - 180) * t;
        int strength = (int)Math.Round(Math.Clamp((classic - 150) / 170, 0, 1) * 100);
        int baseFuse = 3500 + rng.Next(1500);
        int fuse = (int)Math.Max(1200, baseFuse * (1.0 - 0.5 * 0.25) * 1.0);

        Assert.Equal(classic * 0.75, spec.SizePx, 9);
        Assert.Equal((int)Math.Clamp(strength * 0.85, 0, 100), spec.Strength);
        Assert.Equal(fuse, spec.FuseMs);
        Assert.Equal(ChaosMotion.RainDown, spec.Motion);
    }

    [Fact]
    public void Build_CarriesEffectIntensityAndSideDriftChance()
    {
        // Side-drift roll of 0.4 is NOT < 0.3, so motion stays FloatUp.
        var spec = ChaosSpawnCatalog.Build(ChaosSpawnCatalog.All[0], 0.0, 1.0, null, 0.75, 1.0, 0.3,
            new ScriptedRandom(doubles: new[] { 0.5, 0.4 }));
        Assert.Equal(0.75, spec.EffectIntensity);
        Assert.Equal(0.3, spec.SideDriftChance);
        Assert.Equal(ChaosMotion.FloatUp, spec.Motion);
    }

    // ================================================================
    // (3b) Ambient Build branch: dashboard "Trigger Bubbles" reuse
    //      (WPF ChaosBubbleVariants.cs:759-775 — S6, deferred by S1 EXTRA-1)
    // ================================================================

    [Fact]
    public void Build_Ambient_ForcesBenignFloatUpTreat()
    {
        // The video variant is a live, RainDown threat; the ambient branch strips its fuse and
        // floats it up as a 7s treat, stamping the per-instance Ambient flag (WPF :759-775).
        var video = ChaosSpawnCatalog.All[6];
        Assert.True(video.IsLive);
        Assert.Equal(ChaosMotion.RainDown, video.Motion);

        var spec = ChaosSpawnCatalog.Build(video, 0.0, 1.0, null, 1.0, 1.0, 0.0,
            new ScriptedRandom(doubles: new[] { 0.0 }, ints: new[] { 0 }), ambient: true);

        Assert.False(spec.IsLive);                        // WPF :772
        Assert.Equal(0, spec.FuseMs);                     // WPF :773
        Assert.Equal(ChaosMotion.FloatUp, spec.Motion);   // WPF :764
        Assert.Equal(7000, spec.TreatLifeMs);             // WPF :775
        Assert.True(spec.Ambient);                        // WPF :764 payload.Ambient=true
    }

    [Fact]
    public void Build_NonAmbient_KeepsLiveFuseAndFlagOff()
    {
        // The run path (ambient:false, the default) is unchanged: video stays a live threat with
        // its fuse, no treat life, Ambient off (regression guard for the new branch).
        var spec = ChaosSpawnCatalog.Build(ChaosSpawnCatalog.All[6], 0.0, 1.0, null, 1.0, 1.0, 0.0,
            new ScriptedRandom(doubles: new[] { 0.0 }, ints: new[] { 0 }));

        Assert.True(spec.IsLive);
        Assert.Equal(5000, spec.FuseMs);
        Assert.Equal(0, spec.TreatLifeMs);
        Assert.False(spec.Ambient);
    }

    // ================================================================
    // (4) Fuse formula: floor 1200 + intensity shortening (WPF ChaosBubbleVariants.cs:747-752)
    // ================================================================

    [Fact]
    public void Fuse_FlooredAt1200()
    {
        // pink baseFuse 3500 * (1 - 1.0*0.25) * 0.1 = 262.5 -> floor 1200.
        var spec = ChaosSpawnCatalog.Build(ChaosSpawnCatalog.All[2], 1.0, 0.1, null, 1.0, 1.0, 0.0,
            new ScriptedRandom(doubles: new[] { 0.0 }, ints: new[] { 0 }));
        Assert.Equal(1200, spec.FuseMs);
    }

    [Fact]
    public void Fuse_IntensityShortens_BoonsLengthen()
    {
        var atZero = ChaosSpawnCatalog.Build(ChaosSpawnCatalog.All[2], 0.0, 1.0, null, 1.0, 1.0, 0.0,
            new ScriptedRandom(doubles: new[] { 0.0 }, ints: new[] { 0 }));
        Assert.Equal(3500, atZero.FuseMs);   // baseFuse = 3500 + Next(1500)=0

        var atOne = ChaosSpawnCatalog.Build(ChaosSpawnCatalog.All[2], 1.0, 1.0, null, 1.0, 1.0, 0.0,
            new ScriptedRandom(doubles: new[] { 0.0 }, ints: new[] { 0 }));
        Assert.Equal(2625, atOne.FuseMs);    // 3500 * 0.75

        var boon = ChaosSpawnCatalog.Build(ChaosSpawnCatalog.All[2], 0.0, 2.0, null, 1.0, 1.0, 0.0,
            new ScriptedRandom(doubles: new[] { 0.0 }, ints: new[] { 0 }));
        Assert.Equal(7000, boon.FuseMs);     // 3500 * 2.0

        var topOfBand = ChaosSpawnCatalog.Build(ChaosSpawnCatalog.All[2], 0.0, 1.0, null, 1.0, 1.0, 0.0,
            new ScriptedRandom(doubles: new[] { 0.0 }, ints: new[] { 1499 }));
        Assert.Equal(4999, topOfBand.FuseMs);   // 3500 + 1499
    }

    [Fact]
    public void Treats_NeverGetAFuse()
    {
        foreach (int i in new[] { 0, 1, 5 })   // flash, subliminal, bambifreeze
        {
            var spec = ChaosSpawnCatalog.Build(ChaosSpawnCatalog.All[i], 1.0, 1.0, null, 1.0, 1.0, 0.0,
                new ScriptedRandom(doubles: new[] { 0.5 }));
            Assert.Equal(0, spec.FuseMs);
            Assert.False(spec.IsLive);
        }
    }

    // ================================================================
    // (5) Freeze motion remap (WPF ChaosBubbleVariants.cs:736-738)
    // ================================================================

    [Fact]
    public void Freeze_RoamBounceOverride_RemapsToFloatUp()
    {
        // Even with sideDriftChance 1.0 no side-drift rolls: motionOverride != null.
        var spec = ChaosSpawnCatalog.Build(ChaosSpawnCatalog.All[5], 0.5, 1.0, ChaosMotion.RoamBounce, 1.0, 1.0, 1.0,
            new ScriptedRandom(doubles: new[] { 0.5 }));
        Assert.Equal(ChaosMotion.FloatUp, spec.Motion);
        Assert.True(spec.IsFreeze);
    }

    [Fact]
    public void Freeze_NaturalMotion_MaySideDrift()
    {
        // SideDrift exits on its own, so freeze stays legal (WPF ChaosBubbleVariants.cs:739-743).
        var spec = ChaosSpawnCatalog.Build(ChaosSpawnCatalog.All[5], 0.5, 1.0, null, 1.0, 1.0, 1.0,
            new ScriptedRandom(doubles: new[] { 0.5, 0.0 }));
        Assert.Equal(ChaosMotion.SideDrift, spec.Motion);
        Assert.True(spec.IsFreeze);
    }

    // ================================================================
    // (6) Side-drift roll gating (WPF ChaosBubbleVariants.cs:739-743)
    // ================================================================

    [Fact]
    public void SideDrift_AppliesOnlyWithoutOverride()
    {
        var drifted = ChaosSpawnCatalog.Build(ChaosSpawnCatalog.All[0], 0.0, 1.0, null, 1.0, 1.0, 1.0,
            new ScriptedRandom(doubles: new[] { 0.5, 0.0 }));
        Assert.Equal(ChaosMotion.SideDrift, drifted.Motion);

        var overridden = ChaosSpawnCatalog.Build(ChaosSpawnCatalog.All[0], 0.0, 1.0, ChaosMotion.FloatUp, 1.0, 1.0, 1.0,
            new ScriptedRandom(doubles: new[] { 0.5, 0.0 }));
        Assert.Equal(ChaosMotion.FloatUp, overridden.Motion);
    }

    [Fact]
    public void SideDrift_NeverAppliesToRoamBounce()
    {
        // spiral roams; the side-drift branch requires motion != RoamBounce.
        var spec = ChaosSpawnCatalog.Build(ChaosSpawnCatalog.All[3], 0.0, 1.0, null, 1.0, 1.0, 1.0,
            new ScriptedRandom(doubles: new[] { 0.5, 0.0 }, ints: new[] { 0 }));
        Assert.Equal(ChaosMotion.RoamBounce, spec.Motion);
    }

    // ================================================================
    // (7) Special builders
    // ================================================================

    [Fact]
    public void Golden_SpecNumbers()
    {
        // size = 110 + 30*0.5 = 125; motion double 0.4 < 0.5 -> FloatUp.
        var spec = ChaosSpawnCatalog.BuildGolden(new ScriptedRandom(doubles: new[] { 0.5, 0.4 }));
        Assert.Equal("golden", spec.VariantId);
        Assert.Equal("flash", spec.PayloadKind);
        Assert.Equal(0, spec.Strength);
        Assert.Equal(125.0, spec.SizePx, 6);
        Assert.Equal("FFD700", TintHex(spec));
        Assert.Equal("🍀", spec.Label);
        Assert.False(spec.IsLive);
        Assert.Equal(0, spec.FuseMs);
        Assert.Equal(ChaosMotion.FloatUp, spec.Motion);
        Assert.True(spec.IsGolden);
        Assert.Equal(2.8, spec.SpeedMult);
    }

    [Fact]
    public void Golden_MotionIsFiftyFiftyVertical()
    {
        var down = ChaosSpawnCatalog.BuildGolden(new ScriptedRandom(doubles: new[] { 0.5, 0.6 }));
        Assert.Equal(ChaosMotion.RainDown, down.Motion);
    }

    [Fact]
    public void Heart_SpecNumbers()
    {
        var spec = ChaosSpawnCatalog.BuildHeart(new ScriptedRandom(doubles: new[] { 0.0 }));
        Assert.Equal("heart", spec.VariantId);
        Assert.Equal(88.0, spec.SizePx, 6);   // band 88-110
        Assert.Equal("FF4D6E", TintHex(spec));
        Assert.Equal("💖", spec.Label);
        Assert.Equal(ChaosMotion.RainDown, spec.Motion);
        Assert.True(spec.IsHeart);
        Assert.Equal(0.8, spec.SpeedMult);
        Assert.Equal(0, spec.Strength);
        Assert.False(spec.IsLive);
    }

    [Fact]
    public void GoldDroplet_SpecNumbers_AndPinnedSpawn()
    {
        var spec = ChaosSpawnCatalog.BuildGoldDroplet(333, 444, new ScriptedRandom(doubles: new[] { 0.5 }));
        Assert.Equal("gold_droplet", spec.VariantId);
        Assert.Equal(66.0, spec.SizePx, 6);   // 58 + 16*0.5, band 58-74
        Assert.Equal("FFD700", TintHex(spec));
        Assert.Equal("✧", spec.Label);
        Assert.Equal(ChaosMotion.RainDown, spec.Motion);
        Assert.True(spec.IsDroplet);
        Assert.Equal(2.2, spec.SpeedMult);
        Assert.Equal(333, spec.SpawnAtPxX);
        Assert.Equal(444, spec.SpawnAtPxY);
    }

    [Fact]
    public void Heavy_SpecNumbers_FlashRow()
    {
        // classic = flash MaxSize 210 -> strength round(clamp(60/170)*100) = 35;
        // SizePx = 210 * 0.75 * max(0.5, 1.0) * 1.55 = 244.125.
        var spec = ChaosSpawnCatalog.BuildHeavy(0.5, 1.0, 1.0, new ScriptedRandom(ints: new[] { 0 }));
        Assert.Equal("flash", spec.VariantId);
        Assert.Equal(35, spec.Strength);
        Assert.Equal(244.125, spec.SizePx, 6);
        Assert.Equal(ChaosMotion.RainDown, spec.Motion);
        Assert.Equal(0.45, spec.SpeedMult);
        Assert.Equal(3.0, spec.PayMult);
        Assert.Equal(9000, spec.TreatLifeMs);
        Assert.False(spec.IsLive);
        Assert.Equal(0, spec.FuseMs);
    }

    [Fact]
    public void Heavy_SubliminalRow()
    {
        // classic = subliminal MaxSize 220 -> strength round(clamp(70/170)*100) = 41; SizePx = 255.75.
        var spec = ChaosSpawnCatalog.BuildHeavy(0.5, 1.0, 1.0, new ScriptedRandom(ints: new[] { 1 }));
        Assert.Equal("subliminal", spec.VariantId);
        Assert.Equal(41, spec.Strength);
        Assert.Equal(255.75, spec.SizePx, 6);
    }

    [Fact]
    public void Prism_SpecNumbers_MimicsPink()
    {
        // Pool (treatOnly=false) = flash, subliminal, pink, spiral, braindrain, htlink (6 rows).
        // ints[0]=2 -> pink. size = 165 + 50*0.5 = 190 -> strength round(40/170*100) = 24.
        // SizePx = 190 * 0.75 = 142.5 (prism takes NO sizeScale, verbatim WPF).
        var spec = ChaosSpawnCatalog.BuildPrism(0.5, 1.0, false,
            new ScriptedRandom(doubles: new[] { 0.5, 0.4 }, ints: new[] { 2 }));
        Assert.Equal("prism", spec.VariantId);
        Assert.Equal("pink", spec.PayloadKind);
        Assert.Equal("pink_filter", spec.OverlayKind);
        Assert.Equal("pink", spec.MimicVariantId);
        Assert.Equal(24, spec.Strength);
        Assert.Equal(142.5, spec.SizePx, 6);
        Assert.Equal("C8A8FF", TintHex(spec));
        Assert.Equal("❂", spec.Label);
        Assert.True(spec.IsPrism);
        Assert.False(spec.IsLive);
        Assert.Equal(ChaosMotion.RainDown, spec.Motion);   // 0.4 < 0.5
        Assert.Equal(0.7, spec.SpeedMult);
    }

    [Fact]
    public void Prism_PoolExcludesVideoAndFreeze()
    {
        for (int i = 0; i < 6; i++)
        {
            var spec = ChaosSpawnCatalog.BuildPrism(0.5, 1.0, false,
                new ScriptedRandom(doubles: new[] { 0.5, 0.6 }, ints: new[] { i }));
            Assert.NotEqual("video", spec.MimicVariantId);
            Assert.NotEqual("bambifreeze", spec.MimicVariantId);
            Assert.Equal(ChaosMotion.RoamBounce, spec.Motion);   // 0.6 >= 0.5
        }
    }

    [Fact]
    public void Prism_TreatOnly_PoolIsFlashAndSubliminalOnly()
    {
        var first = ChaosSpawnCatalog.BuildPrism(0.5, 1.0, true,
            new ScriptedRandom(doubles: new[] { 0.5, 0.4 }, ints: new[] { 0 }));
        Assert.Equal("flash", first.MimicVariantId);
        var second = ChaosSpawnCatalog.BuildPrism(0.5, 1.0, true,
            new ScriptedRandom(doubles: new[] { 0.5, 0.4 }, ints: new[] { 1 }));
        Assert.Equal("subliminal", second.MimicVariantId);
        // Any higher index clamps into the 2-row pool -> never a live mimic.
        var clamped = ChaosSpawnCatalog.BuildPrism(0.5, 1.0, true,
            new ScriptedRandom(doubles: new[] { 0.5, 0.4 }, ints: new[] { 5 }));
        Assert.Contains(clamped.MimicVariantId, new[] { "flash", "subliminal" });
    }

    [Fact]
    public void Brittle_SpecNumbers_MimicsWholeLivePool()
    {
        // Live pool = pink, spiral, braindrain, video, htlink (5 rows); ints[0]=3 -> video.
        // size = 150 + 35*0.5 = 167.5 -> strength round(17.5/170*100) = 10;
        // SizePx = 167.5 * 0.75 * max(0.5, 1.0) = 125.625.
        var spec = ChaosSpawnCatalog.BuildBrittle(0.5, 1.0, 1.0,
            new ScriptedRandom(doubles: new[] { 0.5, 0.4 }, ints: new[] { 3 }));
        Assert.Equal("brittle", spec.VariantId);
        Assert.Equal("video", spec.MimicVariantId);
        Assert.Equal("video", spec.PayloadKind);
        Assert.Equal(10, spec.Strength);
        Assert.Equal(125.625, spec.SizePx, 6);
        Assert.Equal("D9EFFF", TintHex(spec));
        Assert.Equal("◇", spec.Label);
        Assert.True(spec.IsBrittle);
        Assert.False(spec.IsLive);
        Assert.Equal(0, spec.FuseMs);
        Assert.Equal(ChaosMotion.FloatUp, spec.Motion);   // 0.4 < 0.5 -> FloatUp
        Assert.Equal(ChaosTuning.BRITTLE_SPEED_MULT, spec.SpeedMult);
    }

    [Fact]
    public void Brittle_VerticalOnly_RainDownOnHighRoll()
    {
        var spec = ChaosSpawnCatalog.BuildBrittle(0.5, 1.0, 1.0,
            new ScriptedRandom(doubles: new[] { 0.5, 0.6 }, ints: new[] { 0 }));
        Assert.Equal(ChaosMotion.RainDown, spec.Motion);
        Assert.Equal("pink", spec.MimicVariantId);   // live pool row 0
    }

    [Fact]
    public void Echo_SpecNumbers()
    {
        // t = 0 -> size 180; SizePx = 135. baseFuse = 3500 + Next(1500)=0 -> fuse 3500.
        var spec = ChaosSpawnCatalog.BuildEcho(0.0, 1.0, 1.0, 1.0,
            new ScriptedRandom(doubles: new[] { 0.0 }, ints: new[] { 0 }));
        Assert.Equal("echo", spec.VariantId);
        Assert.Equal(0, spec.Strength);   // never fires — the split IS the trigger
        Assert.Equal(135.0, spec.SizePx, 6);
        Assert.Equal("C9C4E8", TintHex(spec));
        Assert.Equal("◌", spec.Label);
        Assert.True(spec.IsLive);
        Assert.Equal(3500, spec.FuseMs);
        Assert.Equal(ChaosMotion.FloatUp, spec.Motion);
        Assert.True(spec.IsEcho);
    }

    [Fact]
    public void Echo_FuseMult_FlooredAtTenth_ThenFloor1200()
    {
        // fuseMult 0.01 floors to 0.1 -> 3500*0.1 = 350 -> fuse floor 1200.
        var spec = ChaosSpawnCatalog.BuildEcho(0.0, 1.0, 1.0, 0.01,
            new ScriptedRandom(doubles: new[] { 0.0 }, ints: new[] { 0 }));
        Assert.Equal(1200, spec.FuseMs);
    }

    [Fact]
    public void EchoChild_SpecNumbers()
    {
        // parent 200 -> size max(60, 200*0.6) = 120; classicEq = 160 -> strength round(10/170*100) = 6;
        // fuse = 2500 + Next(500)=0. Light-trio ints[0]=0 -> pink.
        var spec = ChaosSpawnCatalog.BuildEchoChild(200, 10, 20, 1.0,
            new ScriptedRandom(ints: new[] { 0, 0 }));
        Assert.Equal("pink", spec.VariantId);
        Assert.Equal(6, spec.Strength);
        Assert.Equal(120.0, spec.SizePx, 6);
        Assert.Equal(2500, spec.FuseMs);
        Assert.True(spec.IsLive);
        Assert.False(spec.IsEcho);   // children never re-split
        Assert.Equal(ChaosMotion.RoamBounce, spec.Motion);
        Assert.Equal(ChaosTuning.ECHO_CHILD_SPEED_MULT, spec.SpeedMult);
        Assert.Equal(10, spec.SpawnAtPxX);
        Assert.Equal(20, spec.SpawnAtPxY);
    }

    [Fact]
    public void EchoChild_SizeFlooredAtSixty_AndLightTrioOnly()
    {
        var tiny = ChaosSpawnCatalog.BuildEchoChild(50, 0, 0, 1.0, new ScriptedRandom(ints: new[] { 2, 499 }));
        Assert.Equal(60.0, tiny.SizePx, 6);      // max(60, 50*0.6=30)
        Assert.Equal("braindrain", tiny.VariantId);
        Assert.Equal(2999, tiny.FuseMs);         // 2500 + 499
    }

    [Fact]
    public void ChaperonePair_LiveAndEscort_SpecNumbers()
    {
        // Live: trio ints[0]=0 -> pink; t=0 -> size 180 -> strength round(30/170*100) = 18;
        // SizePx 135; fuse 3500. Escort: ints[2]=0 -> flash; esize 95 -> estrength 0 -> floor 10.
        var (live, escort) = ChaosSpawnCatalog.BuildChaperonePair(0.0, 1.0, 1.0, 1.0, 1.0,
            new ScriptedRandom(doubles: new[] { 0.0, 0.0 }, ints: new[] { 0, 0, 0 }));

        Assert.Equal("pink", live.VariantId);
        Assert.Equal(18, live.Strength);
        Assert.Equal(135.0, live.SizePx, 6);
        Assert.Equal(3500, live.FuseMs);
        Assert.True(live.IsLive);
        Assert.True(live.IsChaperoneLive);
        Assert.Equal(ChaosMotion.RoamBounce, live.Motion);

        Assert.Equal("flash", escort.VariantId);
        Assert.Equal(10, escort.Strength);   // Max(10, 0) — escort strength floor
        Assert.Equal(95 * 0.75, escort.SizePx, 6);
        Assert.False(escort.IsLive);
        Assert.True(escort.IsEscort);
        Assert.Equal(0, escort.FuseMs);
        Assert.Equal(ChaosMotion.RoamBounce, escort.Motion);
    }

    [Fact]
    public void ChaperoneEscort_SizeBand95To120()
    {
        var (_, escort) = ChaosSpawnCatalog.BuildChaperonePair(0.0, 1.0, 1.0, 1.0, 1.0,
            new ScriptedRandom(doubles: new[] { 0.0, 1.0 }, ints: new[] { 1, 0, 1 }));
        Assert.Equal("subliminal", escort.VariantId);
        Assert.Equal(120 * 0.75, escort.SizePx, 6);
    }

    [Fact]
    public void BoundPair_SharesPairId_DistinctAcrossPairs()
    {
        var rng = new Random(11);
        var (a1, b1) = ChaosSpawnCatalog.BuildBoundPair(0.5, 1.0, 1.0, 1.0, 1.0, rng);
        var (a2, b2) = ChaosSpawnCatalog.BuildBoundPair(0.5, 1.0, 1.0, 1.0, 1.0, rng);

        Assert.Equal(a1.PairId, b1.PairId);
        Assert.Equal(a2.PairId, b2.PairId);
        Assert.NotEqual(a1.PairId, a2.PairId);
    }

    [Fact]
    public void BoundPair_SpecNumbers()
    {
        // Each half: trio ints -> pink (0); t=0 -> size 180, strength 18, SizePx 135, fuse 3500.
        var (a, b) = ChaosSpawnCatalog.BuildBoundPair(0.0, 1.0, 1.0, 1.0, 1.0,
            new ScriptedRandom(doubles: new[] { 0.0, 0.0 }, ints: new[] { 0, 0, 0, 0 }));
        foreach (var half in new[] { a, b })
        {
            Assert.Equal("pink", half.VariantId);
            Assert.Equal(18, half.Strength);
            Assert.Equal(135.0, half.SizePx, 6);
            Assert.Equal(3500, half.FuseMs);
            Assert.True(half.IsLive);
            Assert.True(half.IsBoundHalf);
            Assert.Equal(ChaosMotion.RoamBounce, half.Motion);
            Assert.Equal(ChaosTuning.BOUND_WINDOW_MS, half.BoundWindowMs);
        }
    }

    [Fact]
    public void Tease_SpecNumbers()
    {
        // Pool excludes video + bambifreeze -> flash, subliminal, pink, spiral, braindrain, htlink.
        // ints[0]=5 -> htlink payload. size = 170 + 40*0.5 = 190 -> strength 24; SizePx = 142.5.
        var spec = ChaosSpawnCatalog.BuildTease(0.5, 1.0, 1.0,
            new ScriptedRandom(doubles: new[] { 0.5 }, ints: new[] { 5 }));
        Assert.Equal("tease", spec.VariantId);
        Assert.Equal("htlink", spec.PayloadKind);
        Assert.Equal(24, spec.Strength);
        Assert.Equal(142.5, spec.SizePx, 6);
        Assert.Equal("B30E2E", TintHex(spec));
        Assert.Equal("✖", spec.Label);
        Assert.False(spec.IsLive);
        Assert.Equal(0, spec.FuseMs);
        Assert.Equal(ChaosMotion.RoamBounce, spec.Motion);
        Assert.True(spec.IsTease);
        Assert.Equal(ChaosTuning.TEASE_LIFE_MS, spec.LifetimeMs);
    }

    [Fact]
    public void Tease_PoolExcludesVideoAndFreeze()
    {
        for (int i = 0; i < 6; i++)
        {
            var spec = ChaosSpawnCatalog.BuildTease(0.5, 1.0, 1.0,
                new ScriptedRandom(doubles: new[] { 0.5 }, ints: new[] { i }));
            Assert.NotEqual("video", spec.PayloadKind);
            Assert.NotEqual("bambifreeze", spec.PayloadKind);
        }
    }

    [Fact]
    public void Darter_SpecNumbers()
    {
        // size = 72 + 24*0.5 = 84 (no spotlight).
        var spec = ChaosSpawnCatalog.BuildDarter(0.5, false, false, new ScriptedRandom(doubles: new[] { 0.5 }));
        Assert.Equal("darter", spec.VariantId);
        Assert.Equal("flash", spec.PayloadKind);
        Assert.Equal(8, spec.Strength);   // a brief micro-flash on catch
        Assert.Equal(84.0, spec.SizePx, 6);
        Assert.Equal("FF4DC4", TintHex(spec));
        Assert.Equal("", spec.Label);
        Assert.False(spec.IsLive);
        Assert.Equal(0, spec.FuseMs);
        Assert.Equal(ChaosMotion.RoamBounce, spec.Motion);
        Assert.True(spec.IsDarter);
        Assert.False(spec.IsSweeper);
        Assert.False(spec.Spotlight);
        Assert.Equal(8000, spec.LifetimeMs);
        Assert.Equal(400, spec.TelegraphMs);
        Assert.Equal(500, spec.QuickWindowMs);
        Assert.Equal(9.0, spec.DarterSpeed);
        Assert.Equal(3, spec.DarterMaxBounces);
    }

    [Fact]
    public void Darter_Spotlight_RunsBigger()
    {
        var spec = ChaosSpawnCatalog.BuildDarter(0.5, true, false, new ScriptedRandom(doubles: new[] { 0.5 }));
        Assert.Equal(84.0 * 1.15, spec.SizePx, 6);
        Assert.True(spec.Spotlight);
    }

    [Fact]
    public void Darter_Sweeper_TelegraphsFaster_AndPinsSpawn()
    {
        var spec = ChaosSpawnCatalog.BuildDarter(0.5, false, true, new ScriptedRandom(doubles: new[] { 0.5 }),
            atPxX: 100, atPxY: 200);
        Assert.True(spec.IsSweeper);
        Assert.Equal(150, spec.TelegraphMs);   // sweepers bolt almost immediately
        Assert.Equal(100, spec.SpawnAtPxX);
        Assert.Equal(200, spec.SpawnAtPxY);
    }

    // ================================================================
    // (8) RollDarter chance formula (WPF ChaosBubbleVariants.cs:155-160)
    // ================================================================

    [Fact]
    public void RollDarter_ChanceAtIntensityZero_IsPointZeroOneTwoFive()
    {
        var hit = ChaosSpawnCatalog.RollDarter(0.0, 1.0, new ScriptedRandom(doubles: new[] { 0.0124, 0.5 }));
        Assert.NotNull(hit);
        Assert.True(hit!.IsDarter);

        var miss = ChaosSpawnCatalog.RollDarter(0.0, 1.0, new ScriptedRandom(doubles: new[] { 0.0126 }));
        Assert.Null(miss);
    }

    [Fact]
    public void RollDarter_ChanceAtIntensityOne_IsPointZeroFourTwoFive()
    {
        var hit = ChaosSpawnCatalog.RollDarter(1.0, 1.0, new ScriptedRandom(doubles: new[] { 0.0424, 0.5 }));
        Assert.NotNull(hit);

        var miss = ChaosSpawnCatalog.RollDarter(1.0, 1.0, new ScriptedRandom(doubles: new[] { 0.0426 }));
        Assert.Null(miss);
    }

    [Fact]
    public void RollDarter_RateMult_ScalesAndClampsAtZero()
    {
        // rateMult 2 doubles the chance: intensity 0 -> 0.025.
        var doubled = ChaosSpawnCatalog.RollDarter(0.0, 2.0, new ScriptedRandom(doubles: new[] { 0.0249, 0.5 }));
        Assert.NotNull(doubled);

        // rateMult 0 (or negative, clamped by Max(0, ...)) -> chance 0 -> never spawns.
        var never = ChaosSpawnCatalog.RollDarter(1.0, 0.0, new ScriptedRandom(doubles: new[] { 0.0 }));
        Assert.Null(never);
        var negative = ChaosSpawnCatalog.RollDarter(1.0, -1.0, new ScriptedRandom(doubles: new[] { 0.0 }));
        Assert.Null(negative);
    }

    [Fact]
    public void RollDarter_Spotlight_PassesThrough()
    {
        var spec = ChaosSpawnCatalog.RollDarter(1.0, 10.0, new ScriptedRandom(doubles: new[] { 0.0, 0.5 }),
            spotlight: true);
        Assert.NotNull(spec);
        Assert.True(spec!.Spotlight);
    }

    // ================================================================
    // Welcome Shower treat (WPF ChaosModeService.cs:1649-1665)
    // ================================================================

    [Fact]
    public void WelcomeShowerTreat_IsRainingTreat()
    {
        var flash = ChaosSpawnCatalog.BuildWelcomeShowerTreat(0.0, 1.0, 1.0, 1.0,
            new ScriptedRandom(doubles: new[] { 0.5 }, ints: new[] { 0 }));
        Assert.Equal("flash", flash.VariantId);
        Assert.Equal(ChaosMotion.RainDown, flash.Motion);
        Assert.False(flash.IsLive);
        Assert.Equal(0, flash.FuseMs);

        var subliminal = ChaosSpawnCatalog.BuildWelcomeShowerTreat(0.0, 1.0, 1.0, 1.0,
            new ScriptedRandom(doubles: new[] { 0.5 }, ints: new[] { 1 }));
        Assert.Equal("subliminal", subliminal.VariantId);
        Assert.Equal(ChaosMotion.RainDown, subliminal.Motion);
    }

    // ================================================================
    // (10) Presets (WPF ChaosBubbleVariants.cs:640-647)
    // ================================================================

    [Fact]
    public void Presets_HasExactlyThree()
    {
        Assert.Equal(new[] { "Balanced", "Tease", "Flash-only" },
            ChaosSpawnCatalog.Presets.Select(p => p.Name).ToArray());
    }

    [Fact]
    public void Preset_Balanced_IsAllIds()
    {
        var balanced = ChaosSpawnCatalog.Presets.First(p => p.Name == "Balanced");
        Assert.Equal(ChaosSpawnCatalog.AllIds(), balanced.VariantIds);
    }

    [Fact]
    public void Preset_Tease_And_FlashOnly_MatchWpf()
    {
        var tease = ChaosSpawnCatalog.Presets.First(p => p.Name == "Tease");
        Assert.Equal(new[] { "flash", "subliminal", "pink", "spiral", "bambifreeze" }, tease.VariantIds);

        var flashOnly = ChaosSpawnCatalog.Presets.First(p => p.Name == "Flash-only");
        Assert.Equal(new[] { "flash", "subliminal" }, flashOnly.VariantIds);
    }

    // ================================================================
    // Global constants (WPF ChaosBubbleVariants.cs:134-135, 707-709)
    // ================================================================

    [Fact]
    public void GlobalConstants_MatchWpf()
    {
        Assert.Equal(150, ChaosSpawnCatalog.SizeMinGlobal);
        Assert.Equal(320, ChaosSpawnCatalog.SizeMaxGlobal);
        Assert.Equal(0.75, ChaosSpawnCatalog.GLOBAL_SIZE_SCALE);
        Assert.Equal(0.70, ChaosSpawnCatalog.GIANT_SIZE_SCALE);
        Assert.Equal(8000, ChaosSpawnCatalog.DARTER_LIFETIME_MS);
        Assert.Equal(500, ChaosSpawnCatalog.DARTER_QUICK_WINDOW_MS);
        Assert.Equal(400, ChaosSpawnCatalog.DARTER_TELEGRAPH_MS);
        Assert.Equal(9.0, ChaosSpawnCatalog.DARTER_SPEED);
        Assert.Equal(3, ChaosSpawnCatalog.DARTER_MAX_BOUNCES);
        Assert.Equal(120, ChaosSpawnCatalog.DARTER_BASE_POINTS);
        Assert.Equal(90, ChaosSpawnCatalog.DARTER_QUICK_BONUS);
        Assert.Equal(2.8, ChaosSpawnCatalog.GOLDEN_SPEED_MULT);
        Assert.Equal(1.55, ChaosSpawnCatalog.HEAVY_SIZE_MULT);
        Assert.Equal(0.45, ChaosSpawnCatalog.HEAVY_SPEED_MULT);
        Assert.Equal(3.0, ChaosSpawnCatalog.HEAVY_PAY_MULT);
        Assert.Equal(0.7, ChaosSpawnCatalog.PRISM_SPEED_MULT);
    }
}
