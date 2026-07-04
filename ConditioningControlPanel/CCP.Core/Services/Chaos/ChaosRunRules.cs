namespace ConditioningControlPanel.Core.Services.Chaos;

/// <summary>
/// Pure run-config rules extracted verbatim from the WPF chaos engine so both heads —
/// and the Core unit tests — share one source of truth for the difficulty payout scale,
/// the sin-slot ramp and the <c>ChaosRunConfig.FromSettings</c> clamps
/// (WPF ChaosModels.cs: DifficultyMult :267-274, DefaultSinChance :204-217,
/// FromSettings clamps :195-201).
/// </summary>
public static class ChaosRunRules
{
    // ---- sin-slot ramp constants (WPF ChaosModels.cs:204-208, copied verbatim) ----

    /// <summary>Runs completed before sins deal at all (WPF ChaosModels.cs:205).</summary>
    public const int SIN_DEBUT_RUNS = 2;
    /// <summary>Runs completed when the ramp tops out — Slipping (WPF ChaosModels.cs:206).</summary>
    public const int SIN_FULL_RUNS = 10;
    /// <summary>Sin-slot chance on the debut run (WPF ChaosModels.cs:207).</summary>
    public const double SIN_CHANCE_DEBUT = 0.25;
    /// <summary>Sin-slot chance once the ramp tops out (WPF ChaosModels.cs:208).</summary>
    public const double SIN_CHANCE_FULL = 0.5;

    /// <summary>
    /// Per-difficulty payout/intensity scalar baked into the multiplier stack
    /// (WPF ChaosModels.cs:267-274): Easy 1.0, Medium 1.3, Hard 1.7, Extreme 2.2;
    /// anything unrecognised (including null) falls back to 1.0 like the WPF
    /// <c>_ =&gt; 1.0</c> switch arm.
    /// </summary>
    public static double DifficultyMultFor(string? difficulty) => difficulty switch
    {
        "Easy" => 1.0,
        "Medium" => 1.3,
        "Hard" => 1.7,
        "Extreme" => 2.2,
        _ => 1.0,
    };

    /// <summary>
    /// Happy-path default for <c>ChaosRunConfig.SinChance</c> by lifetime completed
    /// descents (WPF ChaosModels.cs:210-217): 0 before the debut, then a linear
    /// 0.25 → 0.5 ramp between runs 2 and 10, then 0.5.
    /// </summary>
    public static double DefaultSinChance(int runsCompleted)
    {
        if (runsCompleted < SIN_DEBUT_RUNS) return 0.0;
        if (runsCompleted >= SIN_FULL_RUNS) return SIN_CHANCE_FULL;
        double t = (runsCompleted - SIN_DEBUT_RUNS) / (double)(SIN_FULL_RUNS - SIN_DEBUT_RUNS);
        return SIN_CHANCE_DEBUT + (SIN_CHANCE_FULL - SIN_CHANCE_DEBUT) * t;
    }

    /// <summary>Run duration clamp from FromSettings (WPF ChaosModels.cs:196: clamp 60..900).</summary>
    public static int ClampDurationSec(int durationSec) => Math.Clamp(durationSec, 60, 900);

    /// <summary>Wave/loop count clamp from FromSettings (WPF ChaosModels.cs:197: clamp 1..12).</summary>
    public static int ClampWaveCount(int waveCount) => Math.Clamp(waveCount, 1, 12);

    /// <summary>Effect intensity clamp from FromSettings (WPF ChaosModels.cs:201: clamp 0.2..1.5).</summary>
    public static double ClampEffectIntensity(double effectIntensity) => Math.Clamp(effectIntensity, 0.2, 1.5);

    /// <summary>Screen-shake intensity clamp from FromSettings (WPF ChaosModels.cs:200: clamp 0..1).</summary>
    public static double ClampShakeIntensity(double shakeIntensity) => Math.Clamp(shakeIntensity, 0.0, 1.0);
}
