using System;
using System.Windows;
using ConditioningControlPanel.Services.Super;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Juice round (2026-10-01) for Super Lights Down. Everything here is
/// cosmetic: none of it moves depth, attention, or lock-in timing. The pins
/// are the sizes (subtle), the motion branches (Reduced = half, Off = still) and photosafe.
/// </summary>
[Collection(CompanionWpfRenderCollection.Name)]
public class LightsDownJuiceTests
{
    private const double Dt = 1.0 / 60;

    // ---- Lights Down ----

    [Fact]
    public void Soft_start_eases_out_of_zero_and_joins_the_line_at_the_knee()
    {
        Assert.Equal(0, LightsDownMath.SoftStart(0));
        Assert.Equal(0, LightsDownMath.SoftStart(-1));
        Assert.True(LightsDownMath.SoftStart(0.01) < 0.01 * 0.2, "no hard edge out of zero");
        double k = LightsDownMath.SoftKnee;
        Assert.Equal(k, LightsDownMath.SoftStart(k), 9);
        Assert.Equal(0.5, LightsDownMath.SoftStart(0.5));
        // Slope matches at the knee and the curve only rises.
        double slope = (LightsDownMath.SoftStart(k) - LightsDownMath.SoftStart(k - 1e-5)) / 1e-5;
        Assert.InRange(slope, 0.99, 1.01);
        double prev = 0;
        for (double d = 0; d <= 0.3; d += 0.001)
        {
            double v = LightsDownMath.SoftStart(d);
            Assert.True(v >= prev - 1e-12);
            prev = v;
        }
    }

    [Theory]
    [InlineData(LightsDownMotion.Full, 1.0)]
    [InlineData(LightsDownMotion.Reduced, 0.5)]
    [InlineData(LightsDownMotion.Off, 0.0)]
    public void Breath_and_pulse_are_tiny_slow_and_size_with_motion(LightsDownMotion motion, double share)
    {
        double maxB = 0, maxP = 0;
        for (double t = 0; t < 20; t += 0.01)
        {
            maxB = Math.Max(maxB, Math.Abs(LightsDownMath.Breath(t, motion) - 1));
            maxP = Math.Max(maxP, Math.Abs(LightsDownMath.GlowPulse(t, motion) - 1));
        }
        Assert.InRange(maxB, LightsDownMath.BreathAmp * share * 0.95, LightsDownMath.BreathAmp * share + 1e-9);
        Assert.InRange(maxP, LightsDownMath.GlowPulseAmp * share * 0.95, LightsDownMath.GlowPulseAmp * share + 1e-9);
        // Photosafe: far under 3 Hz, and a sine never steps.
        Assert.True(LightsDownMath.BreathHz < 1 && LightsDownMath.GlowPulseHz < 1);
    }

    [Fact]
    public void Lock_flare_rises_fast_then_eases_out_once()
    {
        Assert.Equal(0, LightsDownMath.LockFlare(-0.1));
        Assert.Equal(0, LightsDownMath.LockFlare(double.PositiveInfinity));
        Assert.Equal(1, LightsDownMath.LockFlare(LightsDownMath.FlareRise), 9);
        Assert.Equal(0, LightsDownMath.LockFlare(LightsDownMath.FlareRise + LightsDownMath.FlareFall));
        double prev = -1;
        for (double a = 0; a <= LightsDownMath.FlareRise; a += 0.005) { double v = LightsDownMath.LockFlare(a); Assert.True(v >= prev); prev = v; }
        prev = 1;
        for (double a = LightsDownMath.FlareRise; a < 0.7; a += 0.005) { double v = LightsDownMath.LockFlare(a); Assert.True(v <= prev + 1e-12); prev = v; }
    }

    [Theory]
    [InlineData(LightsDownMotion.Full, 1.0)]
    [InlineData(LightsDownMotion.Reduced, 0.5)]
    [InlineData(LightsDownMotion.Off, 0.0)]
    public void Frame_carries_the_juice_scaled_by_motion(LightsDownMotion motion, double gain)
    {
        var s = new LightsDownState();
        double maxFlare = 0;
        LightsDownFrame f = default;
        for (int i = 0; i < 60 * 30; i++)
        {
            f = s.Step(Dt, true, motion, false);
            maxFlare = Math.Max(maxFlare, f.LockFlare);
        }
        Assert.Equal(gain, f.LeanGain);
        Assert.InRange(maxFlare, 0.9 * gain, gain + 1e-9);
        if (motion == LightsDownMotion.Off)
        {
            Assert.Equal(1, f.ApertureBreath);
            Assert.Equal(1, f.GlowPulse);
        }
    }

    [Fact]
    public void Nothing_draws_at_rest_with_the_soft_knee()
    {
        var f = new LightsDownState().Step(Dt, false, LightsDownMotion.Full, false);
        Assert.Equal(0, f.DimAlpha);
        Assert.Equal(0, f.ApertureAlpha);
        Assert.Equal(0, f.RayAlpha);
        Assert.Equal(0, f.LockFlare);
    }

    [Fact]
    public void Lean_target_is_five_percent_clamped_and_scaled()
    {
        var (x, y) = LightsDownMath.LeanTarget(1100, 500, 1000, 500, 1);
        Assert.Equal(5, x, 9);
        Assert.Equal(0, y, 9);
        var (fx, fy) = LightsDownMath.LeanTarget(5000, 5000, 0, 0, 1);
        Assert.Equal(LightsDownMath.LeanMax, Math.Sqrt(fx * fx + fy * fy), 6);
        var (hx, _) = LightsDownMath.LeanTarget(5000, 0, 0, 0, 0.5);
        Assert.Equal(LightsDownMath.LeanMax / 2, hx, 6);
        Assert.Equal((0.0, 0.0), LightsDownMath.LeanTarget(5000, 0, 0, 0, 0));
        Assert.Equal((0.0, 0.0), LightsDownMath.LeanTarget(double.NaN, 0, 0, 0, 1));
    }

    [Theory]
    [InlineData(1.0 / 60)]
    [InlineData(1.0 / 20)]
    public void Lean_spring_lags_overshoots_a_little_and_settles(double dt)
    {
        double x = 0, v = 0, peak = 0;
        double atHalfSecond = double.NaN;
        for (double t = 0; t < 3; t += dt)
        {
            LightsDownMath.LeanStep(ref x, ref v, 10, dt);
            peak = Math.Max(peak, x);
            if (double.IsNaN(atHalfSecond) && t >= 0.25) atHalfSecond = x;
        }
        Assert.True(atHalfSecond < 9, "it lags behind the cursor");
        Assert.InRange((peak - 10) / 10, 0.02, 0.08);
        Assert.Equal(10, x, 1);
    }

    [Fact]
    public void Lean_spring_survives_a_hitch()
    {
        double x = 0, v = 0;
        for (int i = 0; i < 20; i++) LightsDownMath.LeanStep(ref x, ref v, 20, 0.25);
        Assert.InRange(x, 19, 21);
        Assert.False(double.IsNaN(x));
    }

    [Fact]
    public void Motes_sway_gently_and_vary_in_size()
    {
        Assert.Equal(0, LightsDownMath.MoteSway(1.2, 0.3, 0));
        for (double a = 0; a < 5; a += 0.05)
        {
            Assert.InRange(LightsDownMath.MoteSway(a, 1, 1), -LightsDownMath.MoteSwayAmp, LightsDownMath.MoteSwayAmp);
            Assert.InRange(Math.Abs(LightsDownMath.MoteSway(a, 1, 0.5)), 0, LightsDownMath.MoteSwayAmp / 2 + 1e-9);
        }
        Assert.Equal(2, LightsDownMath.MoteSize(0));
        Assert.Equal(3.6, LightsDownMath.MoteSize(1), 9);
    }

    [Fact]
    public void Overlay_iris_leans_toward_the_pointer_and_holds_still_when_motion_is_off()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var o = LightsDownOverlay.ForTest(1920, 1080, 16.0 / 9);
            o.SetPointerPx(1900, 540, true);
            var f = new LightsDownFrame { Depth = 0.9, ApertureClose = 1, Swell = 1, LockRun = -1, LeanGain = 1 };
            for (int i = 0; i < 120; i++) o.Apply(f, i * Dt, Dt);
            var (lx, ly) = o.LeanForTest;
            Assert.InRange(lx, 20, LightsDownMath.LeanMax * 1.08);
            Assert.Equal(0, ly, 3);

            f.LeanGain = 0;
            o.Apply(f, 3, Dt);
            Assert.Equal((0.0, 0.0), o.LeanForTest);
        });
    }

}
