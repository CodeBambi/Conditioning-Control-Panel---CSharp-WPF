using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// PR-0b/0c — the two gates every ambient loop must pass before it starts a clock: the
/// per-tier performance budget and the effective reduced-motion level. The Performance tier
/// must cost exactly zero (no loop, no particles), not "a cheaper loop that still burns CPU".
/// </summary>
public class AmbientMotionProfileTests
{
    [Theory]
    [InlineData(PerformanceTier.Quality, true)]
    [InlineData(PerformanceTier.Balanced, true)]
    [InlineData(PerformanceTier.Performance, false)]
    public void AllowAmbientMotion_PerTier(PerformanceTier tier, bool expected)
        => Assert.Equal(expected, PerformanceProfile.AllowAmbientMotion(tier));

    [Theory]
    [InlineData(PerformanceTier.Quality, 60)]
    [InlineData(PerformanceTier.Balanced, 24)]
    [InlineData(PerformanceTier.Performance, 0)]
    public void MaxAmbientParticles_PerTier(PerformanceTier tier, int expected)
        => Assert.Equal(expected, PerformanceProfile.MaxAmbientParticles(tier));

    [Theory]
    [InlineData(PerformanceTier.Quality, 30)]
    [InlineData(PerformanceTier.Balanced, 24)]
    [InlineData(PerformanceTier.Performance, 0)]
    public void FxTargetFps_PerTier(PerformanceTier tier, int expected)
        => Assert.Equal(expected, PerformanceProfile.FxTargetFps(tier));

    [Fact]
    public void PerformanceTier_IsZeroCost_NotJustCheaper()
    {
        Assert.False(PerformanceProfile.AllowAmbientMotion(PerformanceTier.Performance));
        Assert.Equal(0, PerformanceProfile.MaxAmbientParticles(PerformanceTier.Performance));
        Assert.Equal(0, PerformanceProfile.FxTargetFps(PerformanceTier.Performance));
    }

    [Fact]
    public void ParticleBudget_NeverGrows_AsTheTierGetsCheaper()
    {
        Assert.True(PerformanceProfile.MaxAmbientParticles(PerformanceTier.Quality)
                    >= PerformanceProfile.MaxAmbientParticles(PerformanceTier.Balanced));
        Assert.True(PerformanceProfile.MaxAmbientParticles(PerformanceTier.Balanced)
                    >= PerformanceProfile.MaxAmbientParticles(PerformanceTier.Performance));
    }

    [Fact]
    public void MotionLevel_DefaultsToFull()
        => Assert.Equal(MotionLevel.Full, new AppSettings().MotionLevel);

    [Theory]
    [InlineData(MotionLevel.Full, true, MotionLevel.Full)]
    [InlineData(MotionLevel.Reduced, true, MotionLevel.Reduced)]
    [InlineData(MotionLevel.Off, true, MotionLevel.Off)]
    public void OsAnimationsOn_LeavesTheSettingAlone(MotionLevel setting, bool osAnim, MotionLevel expected)
        => Assert.Equal(expected, MotionFx.ResolveLevel(setting, osAnim));

    [Theory]
    [InlineData(MotionLevel.Full, MotionLevel.Reduced)]
    [InlineData(MotionLevel.Reduced, MotionLevel.Reduced)]
    [InlineData(MotionLevel.Off, MotionLevel.Off)]
    public void OsAnimationsOff_CapsToReduced_ButNeverAddsMotionBack(MotionLevel setting, MotionLevel expected)
        => Assert.Equal(expected, MotionFx.ResolveLevel(setting, osAnimationsEnabled: false));

    [Theory]
    [InlineData(MotionLevel.Full)]
    [InlineData(MotionLevel.Reduced)]
    [InlineData(MotionLevel.Off)]
    public void MotionLevel_RoundTripsThroughSettingsJson(MotionLevel level)
    {
        var json = Newtonsoft.Json.JsonConvert.SerializeObject(new AppSettings { MotionLevel = level });
        var restored = Newtonsoft.Json.JsonConvert.DeserializeObject<AppSettings>(json);
        Assert.NotNull(restored);
        Assert.Equal(level, restored!.MotionLevel);
    }

    [Fact]
    public void ExistingSettingsFile_WithoutTheKey_LandsOnFull()
    {
        // Every user upgrading into this build has no MotionLevel in their settings.json.
        var restored = Newtonsoft.Json.JsonConvert.DeserializeObject<AppSettings>("{\"PerformanceMode\":false}");
        Assert.NotNull(restored);
        Assert.Equal(MotionLevel.Full, restored!.MotionLevel);
    }

    [Fact]
    public void SettingsComboIndex_MatchesTheEnumOrder()
    {
        // MainWindow binds the picker by SelectedIndex; a reorder here would silently mis-set it.
        Assert.Equal(0, (int)MotionLevel.Full);
        Assert.Equal(1, (int)MotionLevel.Reduced);
        Assert.Equal(2, (int)MotionLevel.Off);
    }
}
