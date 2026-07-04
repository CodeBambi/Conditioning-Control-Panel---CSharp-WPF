using ConditioningControlPanel.Core.Services.Chaos;
using Xunit;

namespace ConditioningControlPanel.Core.Tests;

/// <summary>
/// Pins the pure spawn-director math extracted from the WPF chaos SpawnTick into
/// <see cref="ChaosSpawnDirector"/>: the effective-intensity difficulty bias
/// (WPF ChaosModeService.cs:1111), the field density cap (:1117), the self-retuning
/// refill cadence with its 280ms floor and SpawnRateMult clamp (:1219-1227), the
/// end-of-loop video strip predicate (:1127-1134), the gentle/behavioral roll table
/// (:1247-1307, :1188-1190) and the side-drift grace (:1145-1148).
/// </summary>
public class ChaosSpawnDirectorTests
{
    // ================================================================
    // EffIntensity (WPF ChaosModeService.cs:1111): clamp(i + (diff-1)*0.15, 0, 1)

    [Theory]
    [InlineData(0.5, 1.0, 0.5)]     // Easy: no bias
    [InlineData(0.5, 1.3, 0.545)]   // Medium: +0.045
    [InlineData(0.5, 1.7, 0.605)]   // Hard: +0.105
    [InlineData(0.5, 2.2, 0.68)]    // Extreme: +0.18
    [InlineData(0.0, 1.0, 0.0)]
    [InlineData(0.0, 2.2, 0.18)]
    public void EffIntensity_FoldsDifficultyBias(double intensity, double diff, double expected)
        => Assert.Equal(expected, ChaosSpawnDirector.EffIntensity(intensity, diff), precision: 12);

    [Fact]
    public void EffIntensity_ClampsAtOne()
        => Assert.Equal(1.0, ChaosSpawnDirector.EffIntensity(0.95, 2.2), precision: 12);

    // ================================================================
    // MaxConcurrent (WPF ChaosModeService.cs:1117): round((6 + i*10) * sqrt(diff))

    [Theory]
    [InlineData(0.0, 1.0, 6)]    // Easy start
    [InlineData(1.0, 1.0, 16)]   // Easy end (contract: 6 -> 16)
    [InlineData(0.0, 1.3, 7)]    // Medium 6.84 -> 7
    [InlineData(1.0, 1.3, 18)]   // Medium 18.24 -> 18
    [InlineData(0.0, 1.7, 8)]    // Hard 7.82 -> 8
    [InlineData(1.0, 1.7, 21)]   // Hard 20.86 -> 21
    [InlineData(0.0, 2.2, 9)]    // Extreme 8.90 -> 9 (contract: 9 -> 24)
    [InlineData(1.0, 2.2, 24)]   // Extreme 23.73 -> 24
    public void MaxConcurrent_DensityCurve(double intensity, double diff, int expected)
        => Assert.Equal(expected, ChaosSpawnDirector.MaxConcurrent(intensity, diff));

    // ================================================================
    // SpawnIntervalMs (WPF ChaosModeService.cs:1219-1227)

    [Theory]
    [InlineData(0.0, 1.0, 1000.0)]              // Easy start: 1000ms
    [InlineData(1.0, 1.0, 320.0)]               // Easy end: 320ms
    [InlineData(0.0, 1.3, 1000.0 / 1.3)]        // Medium start ~769.2
    [InlineData(1.0, 1.3, 280.0)]               // Medium end 246.2 -> floor 280
    [InlineData(0.0, 1.7, 1000.0 / 1.7)]        // Hard start ~588.2
    [InlineData(1.0, 1.7, 280.0)]               // Hard end 188.2 -> floor 280
    [InlineData(0.0, 2.2, 1000.0 / 2.2)]        // Extreme start ~454.5
    [InlineData(1.0, 2.2, 280.0)]               // Extreme end 145.5 -> floor 280
    public void SpawnIntervalMs_CadenceCurvePerDifficulty(double intensity, double diff, double expected)
        => Assert.Equal(expected, ChaosSpawnDirector.SpawnIntervalMs(intensity, diff, 1.0, slowMoActive: false), precision: 9);

    [Fact]
    public void SpawnIntervalMs_SpawnRateMultDividesTheGap()
        // 0.6 rate = fewer spawns = a LONGER gap (WPF :1221-1223): 1000 / 0.6.
        => Assert.Equal(1000.0 / 0.6, ChaosSpawnDirector.SpawnIntervalMs(0.0, 1.0, 0.6, false), precision: 9);

    [Fact]
    public void SpawnIntervalMs_RateMultClampsLow()
        // rate 0.01 clamps to 0.1 (WPF :1223 clamp 0.1..10): 1000 / 0.1 = 10000.
        => Assert.Equal(10000.0, ChaosSpawnDirector.SpawnIntervalMs(0.0, 1.0, 0.01, false), precision: 9);

    [Fact]
    public void SpawnIntervalMs_RateMultClampsHigh_ThenFloors()
        // rate 100 clamps to 10 -> 100ms -> floor 280 (WPF :1227).
        => Assert.Equal(280.0, ChaosSpawnDirector.SpawnIntervalMs(0.0, 1.0, 100.0, false), precision: 9);

    [Fact]
    public void SpawnIntervalMs_SlowMoStretchesCadence()
        // Slow-mo divides by SLOWMO_FACTOR 0.12 (WPF :1224): 1000 / 0.12 ~ 8333.
        => Assert.Equal(1000.0 / 0.12, ChaosSpawnDirector.SpawnIntervalMs(0.0, 1.0, 1.0, slowMoActive: true), precision: 9);

    [Fact]
    public void SpawnIntervalMs_PerfBackoffMultipliesAfterTheFloor()
        // WPF :1227 applies the governor AFTER the floor: max(280, 320) * 1.5.
        => Assert.Equal(480.0, ChaosSpawnDirector.SpawnIntervalMs(1.0, 1.0, 1.0, false, perfBackoff: 1.5), precision: 9);

    [Fact]
    public void SlowMoConstants_MatchWpf()
    {
        Assert.Equal(0.12, ChaosSpawnDirector.SLOWMO_FACTOR);       // WPF ChaosModeService.cs:2323
        Assert.Equal(6.0, ChaosSpawnDirector.SLOWMO_DURATION_SEC);  // WPF ChaosModeService.cs:2324
    }

    // ================================================================
    // ShouldStripVideo (WPF ChaosModeService.cs:1127-1134)

    [Fact]
    public void StripVideo_NullPoolIsNeverStripped()
        // WPF requires enabled != null && Contains("video") (:1131).
        => Assert.False(ChaosSpawnDirector.ShouldStripVideo(null, heavyEffectActive: true, waveLeftSec: 0, runLeftSec: 0));

    [Fact]
    public void StripVideo_PoolWithoutVideoIsNeverStripped()
        => Assert.False(ChaosSpawnDirector.ShouldStripVideo(new[] { "flash", "spiral" }, true, 0, 0));

    [Theory]
    [InlineData(true, 100.0, 100.0, true)]    // heavy effect running
    [InlineData(false, 13.99, 100.0, true)]   // waveLeft < 14
    [InlineData(false, 14.0, 100.0, false)]   // boundary: exactly 14 is fine
    [InlineData(false, 100.0, 17.99, true)]   // runLeft < 18
    [InlineData(false, 100.0, 18.0, false)]   // boundary: exactly 18 is fine
    [InlineData(false, 100.0, 100.0, false)]  // plenty of tape left
    public void StripVideo_WindowMath(bool heavy, double waveLeft, double runLeft, bool expected)
        => Assert.Equal(expected, ChaosSpawnDirector.ShouldStripVideo(new[] { "video", "flash" }, heavy, waveLeft, runLeft));

    // ================================================================
    // GentleMult + behavioral chance table (WPF ChaosModeService.cs:1247 + ChaosTuning)

    [Fact]
    public void GentleMult_HalvesOnEasyOnly()
    {
        Assert.Equal(0.5, ChaosSpawnDirector.GentleMult(easyDifficulty: true));
        Assert.Equal(1.0, ChaosSpawnDirector.GentleMult(easyDifficulty: false));
    }

    [Theory]
    [InlineData(ChaosTuning.ECHO_SPAWN_CHANCE, false, 0.05)]        // Echo (WPF :1249-1250)
    [InlineData(ChaosTuning.ECHO_SPAWN_CHANCE, true, 0.025)]
    [InlineData(ChaosTuning.CHAPERONE_SPAWN_CHANCE, false, 0.04)]   // Chaperone (WPF :1267-1268)
    [InlineData(ChaosTuning.CHAPERONE_SPAWN_CHANCE, true, 0.02)]
    [InlineData(ChaosTuning.BOUND_SPAWN_CHANCE, false, 0.03)]       // Bound (WPF :1287-1288)
    [InlineData(ChaosTuning.BOUND_SPAWN_CHANCE, true, 0.015)]
    [InlineData(ChaosTuning.BRITTLE_SPAWN_CHANCE, false, 0.035)]    // Brittle rider (WPF :1188-1190)
    [InlineData(ChaosTuning.BRITTLE_SPAWN_CHANCE, true, 0.0175)]
    public void BehavioralChance_Table(double baseChance, bool easy, double expected)
        => Assert.Equal(expected, ChaosSpawnDirector.BehavioralChance(baseChance, easy), precision: 12);

    [Fact]
    public void BehavioralChance_TeaseRowMatchesBound()
    {
        // Tease (WPF :1306-1307) shares the Bound's 0.03 base — asserted separately because
        // identical InlineData rows would collapse to one xUnit test case.
        Assert.Equal(0.03, ChaosTuning.TEASE_SPAWN_CHANCE);
        Assert.Equal(0.03, ChaosSpawnDirector.BehavioralChance(ChaosTuning.TEASE_SPAWN_CHANCE, false), precision: 12);
        Assert.Equal(0.015, ChaosSpawnDirector.BehavioralChance(ChaosTuning.TEASE_SPAWN_CHANCE, true), precision: 12);
    }

    // ================================================================
    // SideDriftChance (WPF ChaosModeService.cs:1145-1148)

    [Theory]
    [InlineData(0, 0.0)]
    [InlineData(4, 0.0)]     // still inside the 5-spawn grace
    [InlineData(5, 0.30)]    // grace over: SIDE_DRIFT_CHANCE
    [InlineData(100, 0.30)]
    public void SideDriftChance_GraceThenConstant(int ordinarySpawns, double expected)
        => Assert.Equal(expected, ChaosSpawnDirector.SideDriftChance(ordinarySpawns), precision: 12);
}
