using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// #719: "turn on spiral" by voice shows a SUSTAINED overlay (OverlayService.ShowOverlaySustained)
/// and never sets settings.SpiralEnabled, but the progress tracker credited time only while the
/// persistent setting was on — so the spiral spun and the quest stayed at zero. The gate now also
/// accepts "the effect is actually on screen", which covers voice commands, Deeper bands and
/// dashboard trigger bubbles without a transient command rewriting persisted preferences.
/// </summary>
public class OverlayQuestCreditTests
{
    [Fact]
    public void PersistentFeatureDuringARun_StillCredits()
    {
        Assert.True(AchievementService.IsOverlayEffectActive(
            featureEnabled: true, overlayRunning: true, effectVisible: true));
        // Setting on, run active, but the overlay itself no-oped (e.g. no spiral asset): unchanged
        // from the original rule so existing sessions credit exactly as before.
        Assert.True(AchievementService.IsOverlayEffectActive(
            featureEnabled: true, overlayRunning: true, effectVisible: false));
    }

    [Fact]
    public void SettingOnButNoOverlayRun_DoesNotCredit()
    {
        Assert.False(AchievementService.IsOverlayEffectActive(
            featureEnabled: true, overlayRunning: false, effectVisible: false));
    }

    [Fact]
    public void VoiceSustainedOverlay_CreditsWithoutTheSetting()
    {
        // The bug: setting off (voice never flips it), yet the spiral is on screen.
        Assert.True(AchievementService.IsOverlayEffectActive(
            featureEnabled: false, overlayRunning: true, effectVisible: true));
        // ...and it counts even outside an overlay run, because the user is still looking at it.
        Assert.True(AchievementService.IsOverlayEffectActive(
            featureEnabled: false, overlayRunning: false, effectVisible: true));
    }

    [Fact]
    public void NothingOn_NeverCredits()
    {
        Assert.False(AchievementService.IsOverlayEffectActive(
            featureEnabled: false, overlayRunning: true, effectVisible: false));
        Assert.False(AchievementService.IsOverlayEffectActive(
            featureEnabled: false, overlayRunning: false, effectVisible: false));
    }
}
