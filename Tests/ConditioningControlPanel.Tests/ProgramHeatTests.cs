using System;
using ConditioningControlPanel.Helpers;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The Ignition Curve's arithmetic - the one scalar the whole Programs restyle is switched by.
///
/// <para><b>Why this suite is exhaustive rather than representative.</b> Every visual in that
/// restyle is chosen by <see cref="ProgramHeat.TierOf"/>, so a boundary that moves by 0.001 silently
/// changes what day 12 of a 28-day program looks like, on a surface where "it looks slightly
/// different" is not a bug anyone reports. And because the FX code hands these numbers straight to
/// storyboards, a value that escapes its range is not a wrong colour - it is a NaN duration or a
/// strobing panel. The curve is pure precisely so this can be pinned here instead of eyeballed on a
/// screenshot.</para>
/// </summary>
public class ProgramHeatTests
{
    /// <summary>A fine sweep of the whole domain. Anything that must hold, must hold on all of it.</summary>
    private static double[] HeatSweep()
    {
        var values = new double[201];
        for (var i = 0; i < values.Length; i++) values[i] = i / 200.0;
        return values;
    }

    // =====================================================================================
    //  the curve
    // =====================================================================================

    /// <summary>
    /// The thesis, stated as arithmetic: day 1 is COLD. If this ever fails, the restyle has lost the
    /// thing it is for - the panel would greet a brand-new enrollment already lit, and there would be
    /// nothing left for the next twenty-seven days to earn.
    /// </summary>
    [Theory]
    [InlineData(8)]
    [InlineData(14)]
    [InlineData(21)]
    [InlineData(28)]
    [InlineData(60)]
    public void DayOneOfAPlainRunIsCold(int length)
    {
        var heat = ProgramHeat.Compute(1, length, 0.0, false);
        Assert.True(heat < ProgramHeat.WarmAt,
            $"day 1 of a {length}-day run starts at {heat:0.###} - the panel is already lit on arrival");
        Assert.Equal(ProgramHeatTier.Cold, ProgramHeat.TierOf(heat));
    }

    /// <summary>
    /// The one place the cold start is not literal, recorded here rather than left to be discovered:
    /// a SEVEN-day run's day 1 is already one seventh of the way through, and the curve pays that -
    /// it lands at ~0.203, a hair over the Warm line, where the only thing switched on is the sigil
    /// breathing at its smallest amplitude.
    ///
    /// <para>That is the curve behaving correctly (progress is proportional, so a short program
    /// genuinely warms faster) rather than a boundary to nudge, and the visible consequence is one
    /// slow halo instead of a still one. What must not drift is how FAR over the line it goes: past
    /// ~0.25 the shortest shipped programs would open somewhere the design never intended.</para>
    /// </summary>
    [Fact]
    public void ASevenDayRunOpensAtTheVeryBottomOfWarm()
    {
        var heat = ProgramHeat.Compute(1, 7, 0.0, false);
        Assert.Equal(ProgramHeatTier.Warm, ProgramHeat.TierOf(heat));
        Assert.InRange(heat, ProgramHeat.WarmAt, 0.25);
    }

    /// <summary>
    /// And the other end: the last plain day of a run has earned real heat. Not IGNITED on its own -
    /// that tier is reserved for boss days and for genuinely intense finales - but well past the
    /// point where the panel is still pretending to be quiet.
    /// </summary>
    [Theory]
    [InlineData(7)]
    [InlineData(28)]
    public void TheLastPlainDayIsHot(int length)
    {
        var heat = ProgramHeat.Compute(length, length, 0.0, false);
        Assert.True(ProgramHeat.TierOf(heat) >= ProgramHeatTier.Hot,
            $"the final day of a {length}-day run only reaches {heat:0.###}");
    }

    /// <summary>
    /// The exact tier boundaries, each probed on both sides. Inclusive at the bottom: 0.20 IS warm.
    /// </summary>
    [Theory]
    [InlineData(-5.0, ProgramHeatTier.Cold)]
    [InlineData(0.0, ProgramHeatTier.Cold)]
    [InlineData(0.199, ProgramHeatTier.Cold)]
    [InlineData(0.20, ProgramHeatTier.Warm)]
    [InlineData(0.399, ProgramHeatTier.Warm)]
    [InlineData(0.40, ProgramHeatTier.Charged)]
    [InlineData(0.599, ProgramHeatTier.Charged)]
    [InlineData(0.60, ProgramHeatTier.Hot)]
    [InlineData(0.799, ProgramHeatTier.Hot)]
    [InlineData(0.80, ProgramHeatTier.Ignited)]
    [InlineData(1.0, ProgramHeatTier.Ignited)]
    [InlineData(5.0, ProgramHeatTier.Ignited)]
    public void TierBoundariesAreExactAndInclusiveAtTheBottom(double heat, ProgramHeatTier expected) =>
        Assert.Equal(expected, ProgramHeat.TierOf(heat));

    /// <summary>A NaN heat is COLD, not a tier chosen by whichever comparison happened to be first.</summary>
    [Fact]
    public void ANanHeatReadsAsCold() => Assert.Equal(ProgramHeatTier.Cold, ProgramHeat.TierOf(double.NaN));

    /// <summary>
    /// A boss day is fully lit whatever the arithmetic says - including day 1 of a 60-day program at
    /// zero intensity, which is the case the pin exists for.
    /// </summary>
    [Fact]
    public void EveryBossDayIsPinnedFullyLit()
    {
        foreach (var length in new[] { 1, 7, 28, 60 })
        foreach (var day in new[] { 1, 2, length / 2, length })
        foreach (var intensity in new[] { 0.0, 0.5, 1.0 })
        {
            Assert.Equal(1.0, ProgramHeat.Compute(day, length, intensity, isBoss: true));
            Assert.Equal(ProgramHeatTier.Ignited,
                ProgramHeat.TierFor(day, length, intensity, isBoss: true));
        }
    }

    /// <summary>
    /// The curve never leaves 0-1, whatever it is handed. Definitions are author-supplied and an
    /// enrollment persists only the program id, so a length of zero and a day past the end are both
    /// reachable states rather than hypotheticals - and a heat outside the range would arrive at a
    /// storyboard as a negative duration or an opacity above 1.
    /// </summary>
    [Fact]
    public void GarbageInputsCannotEscapeTheCurve()
    {
        var days = new[] { int.MinValue, -3, 0, 1, 14, 28, 999, int.MaxValue };
        var lengths = new[] { int.MinValue, -5, 0, 1, 7, 28 };
        var intensities = new[] { double.NaN, double.NegativeInfinity, -9.0, 0.0, 0.5, 1.0, 9.0,
                                  double.PositiveInfinity };

        foreach (var day in days)
        foreach (var length in lengths)
        foreach (var intensity in intensities)
        foreach (var boss in new[] { false, true })
        {
            var heat = ProgramHeat.Compute(day, length, intensity, boss);
            Assert.False(double.IsNaN(heat), $"NaN heat from ({day}, {length}, {intensity}, {boss})");
            Assert.InRange(heat, 0.0, 1.0);
        }
    }

    /// <summary>
    /// Later is never colder, and more intense is never colder. The exponent above 1 is what keeps
    /// the early days flat, but it must not make the curve turn back on itself anywhere.
    /// </summary>
    [Fact]
    public void TheCurveIsMonotoneInBothInputs()
    {
        const int length = 28;

        var previous = -1.0;
        for (var day = 0; day <= length; day++)
        {
            var heat = ProgramHeat.Compute(day, length, 0.3, false);
            Assert.True(heat >= previous - 1e-9,
                $"day {day} ({heat:0.####}) is colder than day {day - 1} ({previous:0.####})");
            previous = heat;
        }

        previous = -1.0;
        for (var step = 0; step <= 20; step++)
        {
            var heat = ProgramHeat.Compute(10, length, step / 20.0, false);
            Assert.True(heat >= previous - 1e-9, $"intensity {step / 20.0:0.##} went backwards");
            previous = heat;
        }
    }

    /// <summary>The published constants are the ones the curve actually uses.</summary>
    [Fact]
    public void TheCurveIsTheDocumentedFormula()
    {
        const int day = 9, length = 28;
        const double intensity = 0.4;

        var expected = ProgramHeat.BaseHeat
                     + ProgramHeat.ProgressWeight * Math.Pow(day / (double)length, ProgramHeat.ProgressExponent)
                     + ProgramHeat.IntensityWeight * intensity;

        Assert.Equal(expected, ProgramHeat.Compute(day, length, intensity, false), 10);
    }

    // =====================================================================================
    //  the no-strobe contract
    // =====================================================================================

    /// <summary>
    /// The hard rule the whole rig is built under: nothing pulses faster than ~0.45Hz, at ANY heat.
    /// <see cref="ProgramHeat.BreathSeconds"/> is a half cycle (it is used as an AutoReverse
    /// duration), so a full cycle is twice it and that is what the floor applies to.
    /// </summary>
    [Fact]
    public void NoPulseEverCyclesFasterThanTheStrobeFloor()
    {
        foreach (var heat in HeatSweep())
        {
            var cycle = ProgramHeat.BreathSeconds(heat) * 2;
            Assert.True(cycle >= ProgramHeat.MinPulseCycleSeconds - 1e-9,
                $"heat {heat:0.##} breathes on a {cycle:0.###}s cycle - past the strobe floor");
            Assert.True(cycle <= 4.0, $"heat {heat:0.##} breathes on a {cycle:0.###}s cycle - imperceptible");
        }
    }

    /// <summary>Hotter breathes faster and wider, and both stay inside their stated range.</summary>
    [Fact]
    public void BreathingTightensAndWidensWithHeat()
    {
        Assert.True(ProgramHeat.BreathSeconds(1.0) < ProgramHeat.BreathSeconds(0.0));
        Assert.True(ProgramHeat.BreathAmplitude(1.0) > ProgramHeat.BreathAmplitude(0.0));

        double? previousSeconds = null, previousAmplitude = null;
        foreach (var heat in HeatSweep())
        {
            var seconds = ProgramHeat.BreathSeconds(heat);
            var amplitude = ProgramHeat.BreathAmplitude(heat);

            if (previousSeconds is { } ps) Assert.True(seconds <= ps + 1e-9);
            if (previousAmplitude is { } pa) Assert.True(amplitude >= pa - 1e-9);

            // The amplitude is a +/- swing on a 0.55 resting opacity; past 0.45 it would drive the
            // trough to zero and the halo would blink rather than breathe.
            Assert.InRange(amplitude, 0.0, 0.45);
            previousSeconds = seconds;
            previousAmplitude = amplitude;
        }
    }

    // =====================================================================================
    //  the derived tuning
    // =====================================================================================

    /// <summary>
    /// Particles are OFF below Charged - not dimmed, off - because the point of a cold tier is that
    /// it holds no clock at all, and a field of one particle is still a running Skia canvas.
    /// </summary>
    [Fact]
    public void ParticlesAreOffBelowChargedAndCappedAtTheBudget()
    {
        Assert.Equal(0, ProgramHeat.ParticleCount(0.0));
        Assert.Equal(0, ProgramHeat.ParticleCount(ProgramHeat.ChargedAt - 0.001));
        Assert.True(ProgramHeat.ParticleCount(ProgramHeat.ChargedAt) > 0);
        Assert.Equal(ProgramHeat.MaxParticles, ProgramHeat.ParticleCount(1.0));

        var previous = -1;
        foreach (var heat in HeatSweep())
        {
            var count = ProgramHeat.ParticleCount(heat);
            Assert.InRange(count, 0, ProgramHeat.MaxParticles);
            Assert.True(count >= previous, $"particle count fell going into heat {heat:0.##}");
            previous = count;
        }
    }

    /// <summary>
    /// Every remaining knob: bounded at both ends and pointing the right way. These all end up as
    /// storyboard durations or opacities, so an unbounded one is a hang or an invisible panel rather
    /// than a design opinion.
    /// </summary>
    [Fact]
    public void EveryTuningKnobIsBoundedAndPointsTheRightWay()
    {
        foreach (var heat in HeatSweep())
        {
            Assert.InRange(ProgramHeat.BorderLapSeconds(heat), 7.0, 16.0);
            Assert.InRange(ProgramHeat.SheenSweepSeconds(heat), 2.0, 4.2);
            Assert.InRange(ProgramHeat.CometLapSeconds(heat), 3.0, 4.6);
            Assert.InRange(ProgramHeat.WashOpacity(heat), 0.0, 1.0);
            Assert.InRange(ProgramHeat.EdgeGlowOpacity(heat), 0.0, 1.0);

            // AmbientFxCanvas.Burst clamps to 60-150; a value outside that is silently retargeted,
            // which would make the heat-sized burst a lie at one end or the other.
            Assert.InRange(ProgramHeat.BurstCount(heat), 60, 150);
        }

        // Hotter = faster loops, brighter surfaces, bigger bursts.
        Assert.True(ProgramHeat.BorderLapSeconds(1.0) < ProgramHeat.BorderLapSeconds(0.0));
        Assert.True(ProgramHeat.SheenSweepSeconds(1.0) < ProgramHeat.SheenSweepSeconds(0.0));
        Assert.True(ProgramHeat.CometLapSeconds(1.0) < ProgramHeat.CometLapSeconds(0.0));
        Assert.True(ProgramHeat.WashOpacity(1.0) > ProgramHeat.WashOpacity(0.0));
        Assert.True(ProgramHeat.EdgeGlowOpacity(1.0) > ProgramHeat.EdgeGlowOpacity(0.0));
        Assert.True(ProgramHeat.BurstCount(1.0) > ProgramHeat.BurstCount(0.0));

        // The wash's cold value is the constant the tab shipped with: at COLD the band is exactly
        // as bright as it always was, which is what "T0 is today's flat UI" means.
        Assert.Equal(0.40, ProgramHeat.WashOpacity(0.0), 3);
    }

    /// <summary>A NaN heat must not become a NaN duration on a storyboard.</summary>
    [Fact]
    public void ANanHeatStillProducesUsableTuning()
    {
        Assert.False(double.IsNaN(ProgramHeat.BreathSeconds(double.NaN)));
        Assert.False(double.IsNaN(ProgramHeat.BorderLapSeconds(double.NaN)));
        Assert.False(double.IsNaN(ProgramHeat.WashOpacity(double.NaN)));
        Assert.Equal(0, ProgramHeat.ParticleCount(double.NaN));
    }

    // =====================================================================================
    //  the honest number
    // =====================================================================================

    /// <summary>
    /// The floating "+XP" states the XP the service actually awarded, so this mirrors
    /// ProgramService.AwardDayXp exactly: <c>200 + round(400·intensity)</c>, x1.5 on a boss day.
    /// If that award is ever retuned, this is the line that has to move with it - the alternative is
    /// a screen full of honest counters with one decorative number in the middle of it.
    /// </summary>
    [Theory]
    [InlineData(0.0, false, 200)]
    [InlineData(0.5, false, 400)]
    [InlineData(1.0, false, 600)]
    [InlineData(0.0, true, 300)]
    [InlineData(1.0, true, 900)]
    [InlineData(-4.0, false, 200)]
    [InlineData(4.0, false, 600)]
    [InlineData(double.NaN, false, 200)]
    public void TheFloatingXpMirrorsTheServiceAward(double intensity, bool boss, int expected) =>
        Assert.Equal(expected, ProgramHeat.DayXp(intensity, boss));
}
