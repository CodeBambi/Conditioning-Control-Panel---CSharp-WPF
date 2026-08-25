using ConditioningControlPanel.Helpers;
using ConditioningControlPanel.Models;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The manual Intensity Ramp's factor math (<see cref="RampMath.ResolveFactor"/>), which is the
/// single number every linked feature's base value gets multiplied by on each
/// <c>MainWindow.StartStop.RampTimer_Tick</c>.
///
/// <para>Two things are being protected here. First, that adding Range mode did not move
/// Multiplier mode by a hair: legacy presets and settings files carry no mode field, deserialize
/// to <see cref="RampMode.Multiplier"/>, and must ramp exactly as they always did. Second, that
/// Range mode really can resolve BELOW 1.0 - the wind-down is the whole request, and the old code
/// path could never express it because <c>SchedulerMultiplier</c> is clamped to 1..3.</para>
/// </summary>
public class RampFactorTests
{
    private const double Tol = 1e-9;

    // ---------------------------------------------------------------- Multiplier mode (legacy)

    /// <summary>
    /// The pre-range formula, reproduced here on purpose rather than called: if someone edits
    /// RampMath, this line is what tells them Multiplier mode changed.
    /// </summary>
    private static double LegacyFactor(double maxMultiplier, double progress, RampCurve curve)
        => 1.0 + (maxMultiplier - 1.0) * RampCurves.ApplyCurve(progress, curve);

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.5)]
    [InlineData(1.0)]
    public void MultiplierModeMatchesTheLegacyFormula(double t)
    {
        foreach (var mult in new[] { 1.0, 1.5, 2.0, 3.0 })
        {
            var actual = RampMath.ResolveFactor(RampMode.Multiplier, t, RampCurve.Linear, mult, 100, 100);
            Assert.Equal(LegacyFactor(mult, t, RampCurve.Linear), actual, Tol);
        }
    }

    /// <summary>Every curve, not just Linear - the curve was already shared before this change.</summary>
    [Theory]
    [InlineData(RampCurve.Linear)]
    [InlineData(RampCurve.EaseIn)]
    [InlineData(RampCurve.EaseOut)]
    [InlineData(RampCurve.SCurve)]
    [InlineData(RampCurve.Exponential)]
    public void MultiplierModeMatchesTheLegacyFormulaOnEveryCurve(RampCurve curve)
    {
        foreach (var t in new[] { 0.0, 0.25, 0.5, 0.75, 1.0 })
        {
            var actual = RampMath.ResolveFactor(RampMode.Multiplier, t, curve, 2.0, 100, 100);
            Assert.Equal(LegacyFactor(2.0, t, curve), actual, Tol);
        }
    }

    [Fact]
    public void MultiplierModeStartsAtOneAndEndsAtTheMaximum()
    {
        Assert.Equal(1.0, RampMath.ResolveFactor(RampMode.Multiplier, 0.0, RampCurve.Linear, 2.5, 100, 100), Tol);
        Assert.Equal(1.75, RampMath.ResolveFactor(RampMode.Multiplier, 0.5, RampCurve.Linear, 2.5, 100, 100), Tol);
        Assert.Equal(2.5, RampMath.ResolveFactor(RampMode.Multiplier, 1.0, RampCurve.Linear, 2.5, 100, 100), Tol);
    }

    /// <summary>
    /// The range sliders are ignored in Multiplier mode. Otherwise a user who tried Range once and
    /// switched back would find their legacy ramp quietly re-scaled.
    /// </summary>
    [Fact]
    public void MultiplierModeIgnoresTheRangeEndpoints()
    {
        var withDefaults = RampMath.ResolveFactor(RampMode.Multiplier, 0.5, RampCurve.Linear, 2.0, 100, 100);
        var withOddRange = RampMath.ResolveFactor(RampMode.Multiplier, 0.5, RampCurve.Linear, 2.0, 10, 300);
        Assert.Equal(withDefaults, withOddRange, Tol);
    }

    // ------------------------------------------------------------------------------ Range mode

    /// <summary>The headline case: 100% -> 10%, linear. 1.0 / 0.55 / 0.1.</summary>
    [Theory]
    [InlineData(0.0, 1.0)]
    [InlineData(0.5, 0.55)]
    [InlineData(1.0, 0.1)]
    public void RangeModeWindsDownFrom100To10(double t, double expected)
        => Assert.Equal(expected, RampMath.ResolveFactor(RampMode.Range, t, RampCurve.Linear, 3.0, 100, 10), Tol);

    [Theory]
    [InlineData(0.0, 0.1)]
    [InlineData(0.5, 1.05)]
    [InlineData(1.0, 2.0)]
    public void RangeModeAlsoRampsUp(double t, double expected)
        => Assert.Equal(expected, RampMath.ResolveFactor(RampMode.Range, t, RampCurve.Linear, 3.0, 10, 200), Tol);

    /// <summary>
    /// The default (100 -> 100) is a deliberate no-op, so merely flipping the mode toggle never
    /// changes what the user already had.
    /// </summary>
    [Theory]
    [InlineData(0.0)]
    [InlineData(0.5)]
    [InlineData(1.0)]
    public void RangeModeDefaultsToANoOp(double t)
    {
        foreach (var curve in new[] { RampCurve.Linear, RampCurve.EaseIn, RampCurve.EaseOut, RampCurve.SCurve, RampCurve.Exponential })
        {
            Assert.Equal(1.0, RampMath.ResolveFactor(RampMode.Range, t, curve, 3.0, 100, 100), Tol);
        }
    }

    /// <summary>
    /// Range mode is the ONLY path that can dip under 1.0 - which is exactly why the per-feature
    /// writes in RampTimer_Tick had to grow an explicit 0 floor.
    /// </summary>
    [Fact]
    public void OnlyRangeModeCanResolveBelowOne()
    {
        Assert.True(RampMath.ResolveFactor(RampMode.Range, 1.0, RampCurve.Linear, 3.0, 100, 10) < 1.0);
        for (var i = 0; i <= 10; i++)
        {
            var t = i / 10.0;
            Assert.True(RampMath.ResolveFactor(RampMode.Multiplier, t, RampCurve.Linear, 3.0, 100, 10) >= 1.0);
        }
    }

    [Fact]
    public void RangeEndpointsAreClampedTo0Through300()
    {
        Assert.Equal(3.0, RampMath.ResolveFactor(RampMode.Range, 0.0, RampCurve.Linear, 1.0, 900, 100), Tol);
        Assert.Equal(0.0, RampMath.ResolveFactor(RampMode.Range, 1.0, RampCurve.Linear, 1.0, 100, -50), Tol);
    }

    // ---------------------------------------------------------------------------------- curves

    /// <summary>
    /// Every curve is monotonic between the endpoints in both directions - a ramp that briefly
    /// reverses would read as a glitch, and Exponential/SCurve are the easy ones to get wrong.
    /// </summary>
    [Theory]
    [InlineData(RampCurve.Linear)]
    [InlineData(RampCurve.EaseIn)]
    [InlineData(RampCurve.EaseOut)]
    [InlineData(RampCurve.SCurve)]
    [InlineData(RampCurve.Exponential)]
    public void EveryCurveIsMonotonicUpAndDown(RampCurve curve)
    {
        AssertMonotonic(curve, RampMode.Multiplier, 3.0, 100, 100, rising: true);
        AssertMonotonic(curve, RampMode.Range, 1.0, 20, 250, rising: true);
        AssertMonotonic(curve, RampMode.Range, 1.0, 100, 10, rising: false);
    }

    private static void AssertMonotonic(RampCurve curve, RampMode mode, double mult, int start, int end, bool rising)
    {
        var previous = RampMath.ResolveFactor(mode, 0.0, curve, mult, start, end);
        for (var i = 1; i <= 200; i++)
        {
            var next = RampMath.ResolveFactor(mode, i / 200.0, curve, mult, start, end);
            if (rising) Assert.True(next >= previous - Tol, $"{curve} dipped at t={i / 200.0}");
            else Assert.True(next <= previous + Tol, $"{curve} rose at t={i / 200.0}");
            previous = next;
        }
    }

    /// <summary>
    /// Curves reshape the path, never the endpoints - the ramp must still land exactly on its
    /// configured end value whichever curve is picked.
    /// </summary>
    [Theory]
    [InlineData(RampCurve.Linear)]
    [InlineData(RampCurve.EaseIn)]
    [InlineData(RampCurve.EaseOut)]
    [InlineData(RampCurve.SCurve)]
    [InlineData(RampCurve.Exponential)]
    public void EveryCurvePreservesTheEndpoints(RampCurve curve)
    {
        Assert.Equal(1.0, RampMath.ResolveFactor(RampMode.Range, 0.0, curve, 1.0, 100, 10), Tol);
        Assert.Equal(0.1, RampMath.ResolveFactor(RampMode.Range, 1.0, curve, 1.0, 100, 10), Tol);
        Assert.Equal(1.0, RampMath.ResolveFactor(RampMode.Multiplier, 0.0, curve, 2.0, 100, 100), Tol);
        Assert.Equal(2.0, RampMath.ResolveFactor(RampMode.Multiplier, 1.0, curve, 2.0, 100, 100), Tol);
    }

    /// <summary>Progress outside 0..1 is clamped, not extrapolated (the tick can overshoot).</summary>
    [Fact]
    public void ProgressIsClampedToTheRampWindow()
    {
        Assert.Equal(1.0, RampMath.ResolveFactor(RampMode.Range, -0.5, RampCurve.Linear, 1.0, 100, 10), Tol);
        Assert.Equal(0.1, RampMath.ResolveFactor(RampMode.Range, 4.0, RampCurve.Linear, 1.0, 100, 10), Tol);
    }
}
