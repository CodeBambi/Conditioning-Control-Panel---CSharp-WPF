using Xunit;
using static ConditioningControlPanel.Services.OverlayService;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Brain Drain bands died mid-video: the base feature is disabled for rework, so
/// settings.BrainDrainEnabled is false for everyone and RefreshBrainDrainState tore the blur down
/// on the next RefreshOverlays() tick — killing a live Deeper band (and any timed effect) whenever
/// autonomy, remote control, or the user toggling pink/spiral happened to poke the reconciler.
/// The hold guard pink/spiral already had is locked in here.
/// </summary>
public class OverlayHoldGuardTests
{
    [Fact]
    public void FeatureOff_NoHolds_TearsDown()
    {
        // The intended teardown: nothing ad-hoc owns the overlay and the persistent feature is off.
        Assert.True(ShouldStopHeldOverlay(featureWantsIt: false, timedHolds: 0, sustainedHeld: false));
    }

    [Fact]
    public void FeatureOff_SustainedBand_SurvivesTheReconciler()
    {
        // The regression: a Deeper braindrain band is live, base feature off — must NOT be stopped.
        Assert.False(ShouldStopHeldOverlay(featureWantsIt: false, timedHolds: 0, sustainedHeld: true));
    }

    [Fact]
    public void FeatureOff_TimedEffectInFlight_SurvivesUntilTheLastHoldReleases()
    {
        Assert.False(ShouldStopHeldOverlay(featureWantsIt: false, timedHolds: 2, sustainedHeld: false));
        Assert.False(ShouldStopHeldOverlay(featureWantsIt: false, timedHolds: 1, sustainedHeld: false));
        Assert.True(ShouldStopHeldOverlay(featureWantsIt: false, timedHolds: 0, sustainedHeld: false));
    }

    [Fact]
    public void FeatureOn_NeverTearsDown_TheReconcilerOwnsIt()
    {
        // Base feature wants the overlay: the reconciler keeps it alive regardless of ad-hoc holds.
        Assert.False(ShouldStopHeldOverlay(featureWantsIt: true, timedHolds: 0, sustainedHeld: false));
        Assert.False(ShouldStopHeldOverlay(featureWantsIt: true, timedHolds: 3, sustainedHeld: true));
    }
}
