using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ConditioningControlPanel.Controls.Header;
using ConditioningControlPanel.Models;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Polish 12, BADGE lane: the header Premium spark's rules. Free is dim and still forever, Basic
/// shimmers gold, Prime glows cyan with diamond motes; Reduced keeps only a slower sheen, Off is
/// the static lit card. Plus the source pins that keep it in the header row without growing it.
/// </summary>
public class PremiumSparkRulesTests
{
    [Theory]
    [InlineData(false, false, SparkTier.Free)]
    [InlineData(true, false, SparkTier.Basic)]
    [InlineData(true, true, SparkTier.Prime)]
    [InlineData(false, true, SparkTier.Prime)]   // Lab wins, whatever Premium says
    public void TierFollowsTheTwoCanonicalGates(bool premium, bool lab, SparkTier expected) =>
        Assert.Equal(expected, PremiumSparkRules.TierFrom(premium, lab));

    [Theory]
    [InlineData(MotionLevel.Full, true, SparkMotion.Full)]
    [InlineData(MotionLevel.Full, false, SparkMotion.Reduced)]
    [InlineData(MotionLevel.Reduced, true, SparkMotion.Reduced)]
    [InlineData(MotionLevel.Off, true, SparkMotion.Off)]
    [InlineData(MotionLevel.Off, false, SparkMotion.Off)]
    public void MotionFoldsTheLevelWithThePerformanceTier(MotionLevel level, bool ambient, SparkMotion expected) =>
        Assert.Equal(expected, PremiumSparkRules.MotionFrom(level, ambient));

    [Theory]
    [InlineData(SparkMotion.Full)]
    [InlineData(SparkMotion.Reduced)]
    [InlineData(SparkMotion.Off)]
    public void FreeIsDimAndNeverMoves(SparkMotion motion)
    {
        Assert.Equal(0.40, PremiumSparkRules.Opacity(SparkTier.Free), 3);
        Assert.False(PremiumSparkRules.Sheen(SparkTier.Free, motion));
        Assert.False(PremiumSparkRules.Breath(SparkTier.Free, motion));
        Assert.Equal(0, PremiumSparkRules.AmbientCap(SparkTier.Free, motion, true));
        Assert.False(PremiumSparkRules.Clock(SparkTier.Free, motion));
        Assert.False(PremiumSparkRules.Halo(SparkTier.Free));
        Assert.False(PremiumSparkRules.Wobble(SparkTier.Free, motion));
        Assert.Equal(0, PremiumSparkRules.BurstCount(SparkTier.Free, motion));
    }

    [Fact]
    public void BasicShimmersBreathesAndSpillsGlitterAtFull()
    {
        Assert.Equal(1.0, PremiumSparkRules.Opacity(SparkTier.Basic));
        Assert.True(PremiumSparkRules.Sheen(SparkTier.Basic, SparkMotion.Full));
        Assert.True(PremiumSparkRules.Breath(SparkTier.Basic, SparkMotion.Full));
        Assert.True(PremiumSparkRules.Orbits(SparkTier.Basic, SparkMotion.Full));
        Assert.False(PremiumSparkRules.Flares(SparkTier.Basic, SparkMotion.Full));
        Assert.False(PremiumSparkRules.Halo(SparkTier.Basic));
        // Round 2 (owner: "there aren't enough particles"): a steady trickle, 18-24 alive.
        Assert.InRange(PremiumSparkRules.AmbientCap(SparkTier.Basic, SparkMotion.Full, true), 18, 24);
        Assert.True(PremiumSparkRules.BurstCount(SparkTier.Basic, SparkMotion.Full) > 0);
    }

    [Fact]
    public void PrimeGlowsAndThrowsDiamondsAtFull()
    {
        Assert.True(PremiumSparkRules.Sheen(SparkTier.Prime, SparkMotion.Full));
        Assert.True(PremiumSparkRules.Halo(SparkTier.Prime));
        Assert.True(PremiumSparkRules.HaloPulse(SparkTier.Prime, SparkMotion.Full));
        Assert.True(PremiumSparkRules.Flares(SparkTier.Prime, SparkMotion.Full));
        Assert.InRange(PremiumSparkRules.AmbientCap(SparkTier.Prime, SparkMotion.Full, particlesAllowed: true), 18, 24);
        Assert.True(PremiumSparkRules.AmbientCap(SparkTier.Prime, SparkMotion.Full, true)
                    > PremiumSparkRules.AmbientCap(SparkTier.Basic, SparkMotion.Full, true));
        // A performance tier with no particle budget keeps only the few.
        Assert.Equal(PremiumSparkRules.FewCap, PremiumSparkRules.AmbientCap(SparkTier.Prime, SparkMotion.Full, particlesAllowed: false));
        Assert.True(PremiumSparkRules.BurstCount(SparkTier.Prime, SparkMotion.Full)
                    > PremiumSparkRules.BurstCount(SparkTier.Basic, SparkMotion.Full));
    }

    [Theory]
    [InlineData(SparkTier.Basic)]
    [InlineData(SparkTier.Prime)]
    public void ReducedKeepsASlowerSheenAndAFewParticles(SparkTier tier)
    {
        Assert.True(PremiumSparkRules.Sheen(tier, SparkMotion.Reduced));
        Assert.True(PremiumSparkRules.SheenCycleSec(tier, SparkMotion.Reduced)
                    > PremiumSparkRules.SheenCycleSec(tier, SparkMotion.Full));
        Assert.True(PremiumSparkRules.SheenPassFor(SparkMotion.Reduced) > PremiumSparkRules.SheenPassFor(SparkMotion.Full));
        Assert.False(PremiumSparkRules.Breath(tier, SparkMotion.Reduced));
        Assert.False(PremiumSparkRules.HaloPulse(tier, SparkMotion.Reduced));
        Assert.False(PremiumSparkRules.Orbits(tier, SparkMotion.Reduced));
        Assert.False(PremiumSparkRules.Flares(tier, SparkMotion.Reduced));
        Assert.InRange(PremiumSparkRules.AmbientCap(tier, SparkMotion.Reduced, true), 1, 5);
        Assert.InRange(PremiumSparkRules.AmbientCap(tier, SparkMotion.Reduced, false), 1, 5);
        Assert.True(PremiumSparkRules.ReducedPace < 1);
    }

    [Theory]
    [InlineData(SparkTier.Basic)]
    [InlineData(SparkTier.Prime)]
    public void OffIsTheStaticLitCard(SparkTier tier)
    {
        Assert.Equal(1.0, PremiumSparkRules.Opacity(tier));
        Assert.False(PremiumSparkRules.Sheen(tier, SparkMotion.Off));
        Assert.False(PremiumSparkRules.Breath(tier, SparkMotion.Off));
        Assert.False(PremiumSparkRules.Wobble(tier, SparkMotion.Off));
        Assert.Equal(0, PremiumSparkRules.BurstCount(tier, SparkMotion.Off));
        Assert.Equal(0, PremiumSparkRules.AmbientCap(tier, SparkMotion.Off, true));
        Assert.False(PremiumSparkRules.Clock(tier, SparkMotion.Off));
        // Prime keeps its (still) halo: the cyan reads even with motion off.
        Assert.Equal(tier == SparkTier.Prime, PremiumSparkRules.Halo(tier));
    }

    [Fact]
    public void DepthComesFromDepthRules()
    {
        Assert.Equal(0, PremiumSparkRules.Travel(pressed: false, hovered: false));
        Assert.Equal(-2, PremiumSparkRules.Travel(pressed: false, hovered: true));   // hover lifts 2 px
        Assert.Equal(2, PremiumSparkRules.Travel(pressed: true, hovered: true));     // press pushes in
        Assert.Equal(0, PremiumSparkRules.ShadowLength(pressed: true, hovered: true));
        Assert.True(PremiumSparkRules.ShadowLength(false, true) > PremiumSparkRules.ShadowLength(false, false));
    }

    [Fact]
    public void ClickOpensPremiumOrFallsBackToTheOldKey()
    {
        Assert.Equal("premium", PremiumSparkRules.TargetTab(k => k == "premium" ? "home" : null));
        Assert.Equal("exclusives", PremiumSparkRules.TargetTab(_ => null));
    }

    [Fact]
    public void EveryTierNamesItselfInTheTooltip()
    {
        var keys = Enum.GetValues<SparkTier>().Select(PremiumSparkRules.TooltipKey).ToArray();
        Assert.Equal(keys.Length, keys.Distinct().Count());
        var en = File.ReadAllText(Path.Combine(RepoRoot(), "ConditioningControlPanel", "Localization", "Languages", "en.json"));
        foreach (var key in keys.Append("premium_spark_label"))
            Assert.Contains("\"" + key + "\"", en);
    }

    [Fact]
    public void BetweenStaysInsideItsRange()
    {
        var rng = new Random(7);
        for (int i = 0; i < 200; i++)
        {
            var v = PremiumSparkRules.Between(rng, PremiumSparkRules.FlareGapMin, PremiumSparkRules.FlareGapMax);
            Assert.InRange(v, PremiumSparkRules.FlareGapMin, PremiumSparkRules.FlareGapMax);
        }
    }

    // ---- the star: slim, folded, lit from the top-left lamp -------------------------------

    [Fact]
    public void TheArmsAreSlim()
    {
        // The waist of each flank (its midpoint) sits close to the heart: slim concave arms.
        foreach (var flank in PremiumSparkRules.Flanks)
        {
            var mid = PremiumSparkRules.Split(flank, 0.5).a.Item4;
            var d = Math.Sqrt(Math.Pow(mid.X - 15, 2) + Math.Pow(mid.Y - 15, 2));
            Assert.InRange(d, 3.5, 5.5);
        }
        // Long vertical points, shorter horizontal ones, all inside the StarSize box.
        Assert.Equal(0, PremiumSparkRules.Flanks[0].p0.Y);
        Assert.Equal(30, PremiumSparkRules.Flanks[2].p0.Y);
        Assert.True(PremiumSparkRules.Flanks[1].p0.X - 15 < 15);
    }

    [Fact]
    public void TheFoldsAreLitFromTheTopLeft()
    {
        double Shade(int flank, bool firstHalf)
        {
            var f = PremiumSparkRules.Flanks[flank];
            var (a, b) = PremiumSparkRules.Split(f, 0.5);
            return firstHalf ? PremiumSparkRules.FacetShade(f.p0, a.Item4) : PremiumSparkRules.FacetShade(a.Item4, f.p3);
        }
        // Flank 3 runs left tip -> top tip: its second half is the top arm's left face, the lit one.
        var lit = Shade(3, firstHalf: false);
        // Flank 1 runs right tip -> bottom tip: its second half is the bottom arm's right face.
        var dark = Shade(1, firstHalf: false);
        Assert.True(lit > 0.2, $"lit face {lit}");
        Assert.True(dark < -0.2, $"dark face {dark}");
        // Each arm reads folded: its two faces differ.
        Assert.True(Math.Abs(Shade(0, true) - Shade(3, false)) > 0.2);
    }

    [Fact]
    public void TheClockValuesStayInRange()
    {
        for (double t = 0; t < 12; t += 0.07)
        {
            Assert.InRange(PremiumSparkRules.SheenAt(t, SparkTier.Prime, SparkMotion.Full), -20, 42);
            Assert.InRange(PremiumSparkRules.BreathAt(t, SparkTier.Basic, SparkMotion.Full), 1.0, PremiumSparkRules.BreathScale);
            Assert.InRange(PremiumSparkRules.HaloAt(t, SparkTier.Prime, SparkMotion.Full, 1.0), PremiumSparkRules.HaloLow, 1.0);
        }
        Assert.Equal(-20, PremiumSparkRules.SheenAt(3, SparkTier.Free, SparkMotion.Full));
        Assert.Equal(1.0, PremiumSparkRules.BreathAt(3, SparkTier.Basic, SparkMotion.Reduced));
        Assert.Equal(PremiumSparkRules.HaloStill, PremiumSparkRules.HaloAt(3, SparkTier.Prime, SparkMotion.Off, 1));
        Assert.Equal(0, PremiumSparkRules.HaloAt(3, SparkTier.Basic, SparkMotion.Full, 1));
    }

    // ---- source pins: where it sits and that it never grows the header ---------------------

    [Fact]
    public void TheSparkSitsBetweenTheBannerAndTheUpdatePill()
    {
        var xaml = File.ReadAllText(Path.Combine(RepoRoot(), "ConditioningControlPanel", "MainWindow", "MainWindow.xaml"));
        var spark = xaml.IndexOf("x:Name=\"HeaderPremiumSpark\"", StringComparison.Ordinal);
        var banner = xaml.IndexOf("x:Name=\"HeaderBannerHost\"", StringComparison.Ordinal);
        var update = xaml.IndexOf("x:Name=\"BtnUpdateAvailable\"", StringComparison.Ordinal);
        Assert.True(spark > 0 && banner > 0 && update > 0);
        Assert.True(banner < spark && spark < update, "the spark must sit after the banner and before the update pill");
        Assert.Single(Regex.Matches(xaml, "x:Name=\"HeaderPremiumSpark\""));

        // The -4 px bleed holds the 42 px card to a 34 px layout footprint.
        var tag = Regex.Match(xaml, "<header:PremiumSpark[^>]*>", RegexOptions.Singleline).Value;
        Assert.Contains("Margin=\"0,-4,12,-4\"", tag);
        Assert.Equal(34, PremiumSparkRules.ControlHeight - 2 * PremiumSparkRules.LayoutBleed);
    }

    [Fact]
    public void NoEffectOnTheSpark()
    {
        var xaml = File.ReadAllText(Path.Combine(RepoRoot(), "ConditioningControlPanel", "Controls", "Header", "PremiumSpark.xaml"));
        var cs = File.ReadAllText(Path.Combine(RepoRoot(), "ConditioningControlPanel", "Controls", "Header", "PremiumSpark.xaml.cs"));
        Assert.DoesNotContain("Effect>", xaml);
        Assert.DoesNotContain("DropShadowEffect", cs);
        Assert.DoesNotContain("BlurEffect", cs);
        Assert.DoesNotContain(".Effect =", cs);
        // The particle overlay never takes clicks.
        Assert.Contains("x:Name=\"FxLayer\" IsHitTestVisible=\"False\"", xaml);
    }

    [Fact]
    public void MotionChangesReachTheSpark()
    {
        var ui = File.ReadAllText(Path.Combine(RepoRoot(), "ConditioningControlPanel", "MainWindow", "MainWindow.UiUpdates.cs"));
        var handler = Regex.Match(ui, @"void CmbMotionLevel_SelectionChanged\(.*?\n        \}", RegexOptions.Singleline);
        Assert.True(handler.Success);
        Assert.Contains("RefreshPremiumSpark()", handler.Value);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        Assert.True(dir != null);
        return dir!.FullName;
    }
}
