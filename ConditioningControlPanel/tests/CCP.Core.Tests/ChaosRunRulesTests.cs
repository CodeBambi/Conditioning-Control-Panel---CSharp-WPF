using ConditioningControlPanel.Core.Services.Chaos;
using Xunit;

namespace ConditioningControlPanel.Core.Tests;

/// <summary>
/// Pins the pure run-config rules extracted from the WPF chaos engine into
/// <see cref="ChaosRunRules"/>: the difficulty payout scale (WPF ChaosModels.cs:267-274),
/// the sin-slot ramp (WPF ChaosModels.cs:204-217) and the FromSettings clamps
/// (WPF ChaosModels.cs:195-201).
/// </summary>
public class ChaosRunRulesTests
{
    // ================================================================
    // DifficultyMultFor (WPF ChaosModels.cs:267-274)

    [Theory]
    [InlineData("Easy", 1.0)]
    [InlineData("Medium", 1.3)]
    [InlineData("Hard", 1.7)]
    [InlineData("Extreme", 2.2)]
    public void DifficultyMultFor_MapsEveryPill(string difficulty, double expected)
        => Assert.Equal(expected, ChaosRunRules.DifficultyMultFor(difficulty));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("easy")]      // case-sensitive, like the WPF enum switch
    [InlineData("Nightmare")]
    public void DifficultyMultFor_UnknownFallsBackToOne(string? difficulty)
        => Assert.Equal(1.0, ChaosRunRules.DifficultyMultFor(difficulty));

    // ================================================================
    // DefaultSinChance ramp (WPF ChaosModels.cs:204-217)

    [Theory]
    [InlineData(0, 0.0)]     // before the debut: no sins at all
    [InlineData(1, 0.0)]
    [InlineData(2, 0.25)]    // debut run: SIN_CHANCE_DEBUT
    [InlineData(10, 0.5)]    // ramp tops out at SIN_FULL_RUNS
    [InlineData(11, 0.5)]    // stays flat afterwards
    [InlineData(100, 0.5)]
    public void DefaultSinChance_RampEndpoints(int runs, double expected)
        => Assert.Equal(expected, ChaosRunRules.DefaultSinChance(runs), precision: 12);

    [Fact]
    public void DefaultSinChance_MidpointIsLinear()
    {
        // Halfway through the ramp (run 6 of the 2..10 window): 0.25 + 0.25 * 4/8 = 0.375.
        Assert.Equal(0.375, ChaosRunRules.DefaultSinChance(6), precision: 12);
        // One step in (run 3): 0.25 + 0.25 * 1/8 = 0.28125.
        Assert.Equal(0.28125, ChaosRunRules.DefaultSinChance(3), precision: 12);
        // Last step before the top (run 9): 0.25 + 0.25 * 7/8 = 0.46875.
        Assert.Equal(0.46875, ChaosRunRules.DefaultSinChance(9), precision: 12);
    }

    [Fact]
    public void DefaultSinChance_ConstantsMatchWpf()
    {
        Assert.Equal(2, ChaosRunRules.SIN_DEBUT_RUNS);
        Assert.Equal(10, ChaosRunRules.SIN_FULL_RUNS);
        Assert.Equal(0.25, ChaosRunRules.SIN_CHANCE_DEBUT);
        Assert.Equal(0.5, ChaosRunRules.SIN_CHANCE_FULL);
    }

    // ================================================================
    // FromSettings clamps (WPF ChaosModels.cs:195-201)

    [Theory]
    [InlineData(0, 60)]      // below the floor
    [InlineData(59, 60)]
    [InlineData(60, 60)]     // floor passes through
    [InlineData(180, 180)]   // in-range value untouched
    [InlineData(900, 900)]   // ceiling passes through
    [InlineData(901, 900)]
    [InlineData(int.MaxValue, 900)]
    public void ClampDurationSec_Pins60To900(int input, int expected)
        => Assert.Equal(expected, ChaosRunRules.ClampDurationSec(input));

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(5, 5)]
    [InlineData(12, 12)]
    [InlineData(13, 12)]
    public void ClampWaveCount_Pins1To12(int input, int expected)
        => Assert.Equal(expected, ChaosRunRules.ClampWaveCount(input));

    [Theory]
    [InlineData(0.0, 0.2)]
    [InlineData(0.2, 0.2)]
    [InlineData(0.85, 0.85)]
    [InlineData(1.5, 1.5)]
    [InlineData(2.0, 1.5)]
    public void ClampEffectIntensity_Pins02To15(double input, double expected)
        => Assert.Equal(expected, ChaosRunRules.ClampEffectIntensity(input), precision: 12);

    [Theory]
    [InlineData(-0.5, 0.0)]
    [InlineData(0.0, 0.0)]
    [InlineData(0.8, 0.8)]
    [InlineData(1.0, 1.0)]
    [InlineData(1.5, 1.0)]
    public void ClampShakeIntensity_Pins0To1(double input, double expected)
        => Assert.Equal(expected, ChaosRunRules.ClampShakeIntensity(input), precision: 12);
}
