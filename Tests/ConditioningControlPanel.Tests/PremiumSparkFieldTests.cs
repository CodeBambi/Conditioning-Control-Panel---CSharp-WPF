using System;
using System.Linq;
using ConditioningControlPanel.Controls.Header;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Polish 12 round 2 (owner 2026-10-07: "there aren't enough particles"): the spark's particle
/// field. Basic keeps a steady trickle of gold glitter, orbiting twinkles and glints; Prime a
/// denser diamond field, tip stars and the odd flare; Reduced a slow few; Free and Off nothing.
/// Nothing ever outgrows the control's fixed pool.
/// </summary>
public class PremiumSparkFieldTests
{
    private static PremiumSparkField Run(SparkTier tier, SparkMotion motion, bool particles, double seconds,
        int seed = 11, Action<PremiumSparkField>? each = null)
    {
        var field = new PremiumSparkField(new Random(seed));
        field.Configure(tier, motion, particles);
        for (double t = 0; t < seconds; t += 1.0 / 30)
        {
            field.Step(1.0 / 30);
            Assert.True(field.Alive.Count <= PremiumSparkField.PoolSize, "the field outgrew the pool");
            each?.Invoke(field);
        }
        return field;
    }

    private static int Ambient(PremiumSparkField f) =>
        f.Alive.Count(m => m.Kind != MoteKind.Burst && m.Kind != MoteKind.Flare);

    [Theory]
    [InlineData(SparkTier.Free, SparkMotion.Full)]
    [InlineData(SparkTier.Free, SparkMotion.Reduced)]
    [InlineData(SparkTier.Basic, SparkMotion.Off)]
    [InlineData(SparkTier.Prime, SparkMotion.Off)]
    public void FreeAndOffSpawnNothing(SparkTier tier, SparkMotion motion)
    {
        var f = Run(tier, motion, true, 6);
        Assert.Empty(f.Alive);
    }

    [Fact]
    public void BasicSpillsASteadyTrickleOfGlitter()
    {
        int minSteady = int.MaxValue, maxAmbient = 0;
        bool orbit = false, glint = false, glitter = false;
        var cap = PremiumSparkRules.AmbientCap(SparkTier.Basic, SparkMotion.Full, true);
        int frame = 0;
        Run(SparkTier.Basic, SparkMotion.Full, true, 8, each: f =>
        {
            frame++;
            var n = Ambient(f);
            maxAmbient = Math.Max(maxAmbient, n);
            if (frame > 60) minSteady = Math.Min(minSteady, n);
            orbit |= f.Alive.Any(m => m.Kind == MoteKind.Orbit);
            glint |= f.Alive.Any(m => m.Kind == MoteKind.Glint);
            glitter |= f.Alive.Any(m => m.Kind == MoteKind.Glitter);
            Assert.DoesNotContain(f.Alive, m => m.Kind == MoteKind.Diamond || m.Kind == MoteKind.Flare);
        });
        Assert.True(glitter && orbit && glint);
        Assert.True(maxAmbient <= cap, $"over the cap: {maxAmbient}");
        Assert.True(minSteady >= 9, $"the trickle ran dry: {minSteady}");
    }

    [Fact]
    public void PrimeRunsADenserDiamondFieldWithFlares()
    {
        int minSteady = int.MaxValue, flares = 0, frame = 0;
        bool glint = false, wasFlare = false;
        Run(SparkTier.Prime, SparkMotion.Full, true, 14, each: f =>
        {
            frame++;
            if (frame > 60) minSteady = Math.Min(minSteady, Ambient(f));
            glint |= f.Alive.Any(m => m.Kind == MoteKind.Glint);
            var isFlare = f.Alive.Any(m => m.Kind == MoteKind.Flare);
            if (isFlare && !wasFlare) flares++;
            wasFlare = isFlare;
            Assert.True(Ambient(f) <= PremiumSparkRules.PrimeCap);
            Assert.DoesNotContain(f.Alive, m => m.Kind == MoteKind.Glitter || m.Kind == MoteKind.Orbit);
        });
        Assert.True(glint);
        Assert.True(minSteady >= 14, $"Prime field too thin: {minSteady}");
        Assert.InRange(flares, 2, 5);
    }

    [Theory]
    [InlineData(SparkTier.Basic)]
    [InlineData(SparkTier.Prime)]
    public void ReducedKeepsAFew(SparkTier tier)
    {
        bool any = false;
        Run(tier, SparkMotion.Reduced, false, 10, each: f =>
        {
            any |= f.Alive.Count > 0;
            Assert.True(f.Alive.Count <= PremiumSparkRules.FewCap);
            Assert.DoesNotContain(f.Alive, m => m.Kind == MoteKind.Orbit || m.Kind == MoteKind.Flare);
        });
        Assert.True(any);
    }

    [Fact]
    public void BurstsRideOnTopButNeverOutgrowThePool()
    {
        var f = Run(SparkTier.Prime, SparkMotion.Full, true, 3);
        for (int i = 0; i < 5; i++) f.Burst(14);
        Assert.True(f.Alive.Count(m => m.Kind == MoteKind.Burst) <= PremiumSparkField.BurstHeadroom);
        Assert.True(f.Alive.Count <= PremiumSparkField.PoolSize);
        for (int i = 0; i < 40; i++) f.Step(1.0 / 30);
        Assert.DoesNotContain(f.Alive, m => m.Kind == MoteKind.Burst && m.Age > m.Life);
    }

    [Fact]
    public void ConfiguringToFreeClearsTheField()
    {
        var f = Run(SparkTier.Prime, SparkMotion.Full, true, 2);
        Assert.NotEmpty(f.Alive);
        f.Configure(SparkTier.Free, SparkMotion.Full, true);
        Assert.Empty(f.Alive);
        f.Step(1);
        Assert.Empty(f.Alive);
    }

    [Fact]
    public void TheSameSeedDrawsTheSameField()
    {
        var a = Run(SparkTier.Basic, SparkMotion.Full, true, 2, seed: 5);
        var b = Run(SparkTier.Basic, SparkMotion.Full, true, 2, seed: 5);
        Assert.Equal(a.Alive.Select(m => (m.Kind, Math.Round(m.X, 6), Math.Round(m.Y, 6))),
                     b.Alive.Select(m => (m.Kind, Math.Round(m.X, 6), Math.Round(m.Y, 6))));
    }

    [Fact]
    public void GlitterFallsAndEveryMoteFadesInAndOut()
    {
        var f = new PremiumSparkField(new Random(3));
        f.Configure(SparkTier.Basic, SparkMotion.Full, true);
        // Opacity and scale stay in 0..1 over every mote's whole life.
        for (int i = 0; i < 120; i++)
        {
            f.Step(1.0 / 30);
            foreach (var m in f.Alive)
            {
                Assert.InRange(PremiumSparkField.OpacityOf(m), 0, 1);
                Assert.InRange(PremiumSparkField.ScaleOf(m), 0, 1.0001);
            }
        }
        // Glitter that has been out a while is moving down: it spills.
        var old = f.Alive.Where(m => m.Kind == MoteKind.Glitter && m.Age > 0.6).ToList();
        Assert.NotEmpty(old);
        Assert.True(old.Average(m => m.Vy) > 0, "glitter should fall");
    }

    [Fact]
    public void PoolCoversTheBiggestLook() =>
        Assert.Equal(PremiumSparkRules.MaxAmbientCap + PremiumSparkField.BurstHeadroom + 1, PremiumSparkField.PoolSize);
}
