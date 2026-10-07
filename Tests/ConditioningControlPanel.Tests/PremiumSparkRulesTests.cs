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
        Assert.False(PremiumSparkRules.Glints(SparkTier.Free, motion));
        Assert.False(PremiumSparkRules.Halo(SparkTier.Free));
        Assert.False(PremiumSparkRules.Motes(SparkTier.Free, motion, true));
        Assert.False(PremiumSparkRules.Wobble(SparkTier.Free, motion));
        Assert.Equal(0, PremiumSparkRules.BurstCount(SparkTier.Free, motion));
    }

    [Fact]
    public void BasicShimmersBreathesAndGlintsAtFull()
    {
        Assert.Equal(1.0, PremiumSparkRules.Opacity(SparkTier.Basic));
        Assert.True(PremiumSparkRules.Sheen(SparkTier.Basic, SparkMotion.Full));
        Assert.True(PremiumSparkRules.Breath(SparkTier.Basic, SparkMotion.Full));
        Assert.True(PremiumSparkRules.Glints(SparkTier.Basic, SparkMotion.Full));
        Assert.False(PremiumSparkRules.Halo(SparkTier.Basic));
        Assert.False(PremiumSparkRules.Motes(SparkTier.Basic, SparkMotion.Full, true));
        Assert.True(PremiumSparkRules.BurstCount(SparkTier.Basic, SparkMotion.Full) > 0);
    }

    [Fact]
    public void PrimeGlowsAndThrowsDiamondsAtFull()
    {
        Assert.True(PremiumSparkRules.Sheen(SparkTier.Prime, SparkMotion.Full));
        Assert.True(PremiumSparkRules.Halo(SparkTier.Prime));
        Assert.True(PremiumSparkRules.HaloPulse(SparkTier.Prime, SparkMotion.Full));
        Assert.True(PremiumSparkRules.Motes(SparkTier.Prime, SparkMotion.Full, particlesAllowed: true));
        Assert.False(PremiumSparkRules.Motes(SparkTier.Prime, SparkMotion.Full, particlesAllowed: false));
        Assert.True(PremiumSparkRules.BurstCount(SparkTier.Prime, SparkMotion.Full)
                    > PremiumSparkRules.BurstCount(SparkTier.Basic, SparkMotion.Full));
    }

    [Theory]
    [InlineData(SparkTier.Basic)]
    [InlineData(SparkTier.Prime)]
    public void ReducedKeepsOnlyASlowerSheen(SparkTier tier)
    {
        Assert.True(PremiumSparkRules.Sheen(tier, SparkMotion.Reduced));
        Assert.True(PremiumSparkRules.SheenCycleSec(tier, SparkMotion.Reduced)
                    > PremiumSparkRules.SheenCycleSec(tier, SparkMotion.Full));
        Assert.True(PremiumSparkRules.SheenPassFor(SparkMotion.Reduced) > PremiumSparkRules.SheenPassFor(SparkMotion.Full));
        Assert.False(PremiumSparkRules.Breath(tier, SparkMotion.Reduced));
        Assert.False(PremiumSparkRules.Glints(tier, SparkMotion.Reduced));
        Assert.False(PremiumSparkRules.HaloPulse(tier, SparkMotion.Reduced));
        Assert.False(PremiumSparkRules.Motes(tier, SparkMotion.Reduced, true));
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
            var v = PremiumSparkRules.Between(rng, PremiumSparkRules.GlintMinSec, PremiumSparkRules.GlintMaxSec);
            Assert.InRange(v, PremiumSparkRules.GlintMinSec, PremiumSparkRules.GlintMaxSec);
        }
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
