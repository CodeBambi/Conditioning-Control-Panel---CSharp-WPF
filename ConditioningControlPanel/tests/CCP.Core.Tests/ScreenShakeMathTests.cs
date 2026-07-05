using ConditioningControlPanel.Core.Platform;
using Xunit;

namespace ConditioningControlPanel.Core.Tests;

/// <summary>
/// Pins the pure screen-shake math extracted from the WPF ScreenShakeService
/// (Services/UI/ScreenShakeService.cs) into <see cref="ScreenShakeMath"/>: the
/// intensity clamp (:46), the no-op short-circuit (:46-47), the amplitude formula
/// (:84, MaxOffsetPx * intensity) and the symmetric per-tick jitter (:202-203).
/// The WPF service has NO decay curve — amplitude is constant for the duration.
/// </summary>
public class ScreenShakeMathTests
{
    // ================================================================
    // ClampIntensity (WPF ScreenShakeService.cs:46 — Math.Clamp(intensity, 0, 1))

    [Theory]
    [InlineData(-1.0, 0.0)]     // well below the floor
    [InlineData(-0.001, 0.0)]   // just below
    [InlineData(0.0, 0.0)]      // floor passes through
    [InlineData(0.5, 0.5)]      // in-range untouched
    [InlineData(1.0, 1.0)]      // ceiling passes through
    [InlineData(1.5, 1.0)]      // above the ceiling
    [InlineData(100.0, 1.0)]    // far above
    public void ClampIntensity_PinsToUnitBand(double input, double expected)
        => Assert.Equal(expected, ScreenShakeMath.ClampIntensity(input), precision: 12);

    // ================================================================
    // IsNoOp (WPF ScreenShakeService.cs:46-47 — clamped intensity <= 0 OR durationMs <= 0)

    [Theory]
    [InlineData(-0.5, 300, true)]   // negative intensity → no-op
    [InlineData(0.0, 300, true)]    // zero intensity → no-op
    [InlineData(0.5, 0, true)]      // zero duration → no-op
    [InlineData(0.5, -100, true)]   // negative duration → no-op
    [InlineData(0.5, 300, false)]   // valid → fires
    [InlineData(1.5, 300, false)]   // over-range intensity clamps to 1, still fires
    [InlineData(0.0001, 1, false)]  // tiny-but-positive intensity + duration fires
    public void IsNoOp_MatchesWpfShortCircuit(double intensity, int durationMs, bool expected)
        => Assert.Equal(expected, ScreenShakeMath.IsNoOp(intensity, durationMs));

    // ================================================================
    // Amplitude (WPF ScreenShakeService.cs:84 — MaxOffsetPx * clampedIntensity)

    [Theory]
    [InlineData(1.0, 28.0)]    // full intensity → MaxOffsetPx
    [InlineData(0.5, 14.0)]    // half
    [InlineData(0.25, 7.0)]    // quarter
    [InlineData(0.0, 0.0)]     // zero
    [InlineData(2.0, 28.0)]    // clamps to 1 before scaling
    [InlineData(-1.0, 0.0)]    // clamps to 0 before scaling
    public void Amplitude_ScalesMaxOffsetByClampedIntensity(double intensity, double expected)
        => Assert.Equal(expected, ScreenShakeMath.Amplitude(intensity), precision: 12);

    // ================================================================
    // Offset (WPF ScreenShakeService.cs:202-203 — (random*2-1) * amplitude)

    [Theory]
    [InlineData(10.0, 0.5, 0.0)]      // dead centre → no offset
    [InlineData(10.0, 0.0, -10.0)]    // random 0 → -amplitude
    [InlineData(10.0, 1.0, 10.0)]     // random 1 → +amplitude
    [InlineData(28.0, 0.75, 14.0)]    // upper quarter
    [InlineData(28.0, 0.25, -14.0)]   // lower quarter
    public void Offset_MapsUnitRandomToSymmetricBand(double amplitude, double random01, double expected)
        => Assert.Equal(expected, ScreenShakeMath.Offset(amplitude, random01), precision: 12);

    // ================================================================
    // Constants + invariants

    [Fact]
    public void Constants_MatchWpf()
    {
        Assert.Equal(28.0, ScreenShakeMath.MaxOffsetPx, precision: 12);   // WPF :25
        Assert.Equal(30, ScreenShakeMath.TickIntervalMs);                 // WPF :24
    }

    [Fact]
    public void Offset_StaysWithinAmplitudeBand_ForSampledRandoms()
    {
        const double amplitude = 28.0;
        // Deterministic sweep of the [0,1] random domain: every offset must land in
        // [-amp, +amp] (the symmetric band the WPF jitter can never exceed).
        for (int i = 0; i <= 20; i++)
        {
            double random01 = i / 20.0;
            double offset = ScreenShakeMath.Offset(amplitude, random01);
            Assert.InRange(offset, -amplitude, amplitude);
        }
    }
}
