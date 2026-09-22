using System;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Chaos;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Brain Drain's blur dial at zero.
///
/// <para>The slider floored at 1 until 2026-09-21, and 1 was not off: <c>BrainDrainLayer.AlphaFor</c>
/// clamps its input up to 1 and starts at <c>AlphaFloor</c> there, so the gentlest setting the app
/// offered still laid a haze over the screen. Someone who wanted the audio and none of the picture
/// had nowhere to go (accessibility report, "doesn't go below 1% which still hurts my eyes").</para>
///
/// <para>Zero has to survive four call sites, each of which used to carry its own <c>Max(1, ...)</c>,
/// so the floor is asserted per site rather than once.</para>
/// </summary>
public class BrainDrainBlurZeroTests
{
    // ---- the settings model ----------------------------------------------------------

    [Fact]
    public void TheSettingAcceptsZero()
    {
        var s = new AppSettings { BrainDrainBlurStrength = 0 };
        Assert.Equal(0, s.BrainDrainBlurStrength);
    }

    [Theory]
    [InlineData(-5, 0)]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(50, 50)]
    [InlineData(100, 100)]
    [InlineData(250, 100)]
    public void TheClampStillHoldsBothEnds(int written, int expected)
    {
        var s = new AppSettings { BrainDrainBlurStrength = written };
        Assert.Equal(expected, s.BrainDrainBlurStrength);
    }

    /// <summary>
    /// Widening the floor must not rewrite a saved choice. Someone sitting on the old minimum of 1
    /// keeps 1 - which is a different setting from 0 and always was.
    /// </summary>
    [Fact]
    public void AnOldFileHoldingTheOldFloorIsLeftAlone()
    {
        var s = new AppSettings { BrainDrainBlurStrength = 1 };
        Assert.Equal(1, s.BrainDrainBlurStrength);
        Assert.False(BrainDrainVisualPolicy.IsSilent(s.BrainDrainBlurStrength));
    }

    // ---- the policy ------------------------------------------------------------------

    [Theory]
    [InlineData(0, true)]
    [InlineData(-1, true)]      // defensive: a corrupt file that dodged the setter
    [InlineData(1, false)]
    [InlineData(100, false)]
    public void SilentMeansZeroAndNothingElse(int strength, bool silent) =>
        Assert.Equal(silent, BrainDrainVisualPolicy.IsSilent(strength));

    [Theory]
    [InlineData(true, 50, true)]
    [InlineData(true, 1, true)]
    [InlineData(true, 0, false)]    // the feature is on, the picture is not
    [InlineData(false, 50, false)]
    [InlineData(false, 0, false)]
    public void TheBaseFeatureWantsTheBlurOnlyWhenBothHalvesSayYes(bool enabled, int strength, bool wants) =>
        Assert.Equal(wants, BrainDrainVisualPolicy.WantsBlur(enabled, strength));

    // ---- the melt bubble -------------------------------------------------------------

    /// <summary>
    /// The prize bubble borrows the user's own dial, so it must floor where the dial does. This
    /// clamped up to 1 and was the last path that could blur a screen that had asked for none.
    /// </summary>
    [Theory]
    [InlineData(0, 0.0)]
    [InlineData(1, 0.01)]
    [InlineData(50, 0.50)]
    [InlineData(100, 1.0)]
    public void TheMeltBubbleOpacityFloorsAtZeroToo(int strength, double expected) =>
        Assert.Equal(expected, BrainDrainBubble.OverlayOpacity(strength), 3);

    // ---- the call sites --------------------------------------------------------------

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        Assert.True(dir != null, "could not locate the repo root from " + AppContext.BaseDirectory);
        return dir!.FullName;
    }

    private static string ReadSource(params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { RepoRoot(), "ConditioningControlPanel" }.Concat(parts).ToArray()));

    /// <summary>
    /// Every path that can put the BASE feature's blur on screen asks the policy first. Source
    /// text because all four live on WPF services that a unit test cannot stand up; the policy
    /// itself is covered by value above.
    /// </summary>
    [Theory]
    [InlineData("Services/Notifications/OverlayService.cs", 2)]   // the start gate + RefreshBrainDrainState
    [InlineData("Services/AutonomyService.cs", 1)]                // the 5-second pulse
    [InlineData("Services/Chaos/EffectPayload.cs", 1)]            // the melt bubble's pop
    public void EveryVisualCallSiteAsksThePolicy(string file, int expectedMentions)
    {
        var src = ReadSource(file.Split('/'));
        var hits = src.Split("BrainDrainVisualPolicy.").Length - 1;
        Assert.True(hits >= expectedMentions,
            $"{file} consults BrainDrainVisualPolicy {hits} time(s), expected at least {expectedMentions} - " +
            "a path that skips it can still draw a blur at strength 0");
    }

    /// <summary>The slider itself has to reach the value the setting now accepts.</summary>
    [Fact]
    public void TheSliderGoesToZero()
    {
        var xaml = ReadSource("Views", "Controls", "Studio", "BrainDrainFeatureControl.xaml");
        var at = xaml.IndexOf("x:Name=\"SliderBlurStrength\"", StringComparison.Ordinal);
        Assert.True(at > 0, "SliderBlurStrength is no longer in BrainDrainFeatureControl.xaml");
        var slider = xaml.Substring(at, Math.Min(300, xaml.Length - at));
        Assert.Contains("Minimum=\"0\"", slider, StringComparison.Ordinal);
    }

    /// <summary>
    /// The audio half never reads the blur dial and must not start: that separation IS the fix.
    /// </summary>
    [Fact]
    public void TheAudioHalfDoesNotReadTheBlurDial()
    {
        var src = ReadSource("Services", "LockCard", "BrainDrainService.cs");
        Assert.DoesNotContain("BrainDrainBlurStrength", src, StringComparison.Ordinal);
    }
}
