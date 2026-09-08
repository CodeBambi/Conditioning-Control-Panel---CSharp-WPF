using System.Collections.Generic;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Haptics;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The "takeover outlives the engine" family (#1180, #1153, #1184). One shape, three symptoms: an
/// effect a takeover started is torn down by rules that belong to something else - the feature
/// toggles, the session tick, or a settings flag that only says "armed". These pin the pure halves
/// of the three fixes; the WPF-bound halves (OverlayService, MindWipeService, SetWallFeature) are
/// play-test territory.
/// </summary>
public class TakeoverOutlivesEngineTests
{
    // ---- #1180: the pink filter that never went away -------------------------------------------

    [Fact]
    public void PulseThatStartedTheOverlayService_StopsItAgain()
    {
        // Engine off, takeover on: nothing was on screen, so the pulse started the service and owns it.
        Assert.True(AutonomyService.ShouldStopOverlayAfterPulse(
            overlayWasRunningBeforePulse: false, otherPulseActive: false));
    }

    [Fact]
    public void PulseThatBorrowedARunningOverlayService_LeavesItRunning()
    {
        // The engine (or another feature) had it up first. Not ours to stop.
        Assert.False(AutonomyService.ShouldStopOverlayAfterPulse(
            overlayWasRunningBeforePulse: true, otherPulseActive: false));
    }

    [Fact]
    public void SiblingPulseStillRunning_KeepsTheOverlayServiceUp()
    {
        // Pink and spiral can overlap; whoever finishes first must not pull the floor out.
        Assert.False(AutonomyService.ShouldStopOverlayAfterPulse(
            overlayWasRunningBeforePulse: false, otherPulseActive: true));
        Assert.False(AutonomyService.ShouldStopOverlayAfterPulse(
            overlayWasRunningBeforePulse: true, otherPulseActive: true));
    }

    // ---- #1153: the sting that landed after the stop --------------------------------------------

    [Fact]
    public void AnnouncedAction_FiresWhenTheTakeoverIsStillTheSameOne()
    {
        Assert.True(AutonomyService.ShouldRunDelayedAction(enabled: true, generationAtSchedule: 4, generationNow: 4));
    }

    [Fact]
    public void AnnouncedAction_IsDroppedWhenTakeoverStoppedDuringTheDelay()
    {
        // Stop() clears _isEnabled AND bumps the generation - either alone is enough to drop it.
        Assert.False(AutonomyService.ShouldRunDelayedAction(enabled: false, generationAtSchedule: 4, generationNow: 5));
        Assert.False(AutonomyService.ShouldRunDelayedAction(enabled: false, generationAtSchedule: 4, generationNow: 4));
    }

    [Fact]
    public void AnnouncedAction_IsDroppedWhenPanicCancelledThePulsesDuringTheDelay()
    {
        // CancelActivePulses bumps the generation without stopping the service: a mind wipe sting
        // announced two seconds before the panic key must not still land.
        Assert.False(AutonomyService.ShouldRunDelayedAction(enabled: true, generationAtSchedule: 4, generationNow: 5));
    }

    // ---- #1184: the lockdown that ran empty ------------------------------------------------------

    private static AppSettings QuietRoomWithTakeoverArmed() => new()
    {
        FlashEnabled = false,
        SubliminalEnabled = false,
        SpiralEnabled = false,
        PinkFilterEnabled = false,
        BouncingTextEnabled = false,
        BubblesEnabled = false,
        MandatoryVideosEnabled = false,
        MindWipeEnabled = false,
        LockCardEnabled = false,
        BubbleCountEnabled = false,
        BrainDrainEnabled = false,
        PopQuizEnabled = false,
        AudioOnlySession = false,
        CornerGifOverlays = new List<CornerGifOverlaySetting>(),
        // The flags persist across launches even though the service starts OFF.
        AutonomyModeEnabled = true,
        AutonomyConsentGiven = true,
    };

    [Fact]
    public void TakeoverArmedButNotRunning_IsStillAnEmptyRoom()
    {
        // The bug: these two flags alone told the keeper the room was dosed, so it skipped the
        // conscription and started a bare engine. Nothing was ever going to happen.
        var s = QuietRoomWithTakeoverArmed();

        Assert.True(LockdownDoseKeeper.DoseIsEmpty(s, takeoverRunning: false));
        Assert.False(LockdownDoseKeeper.CountsAsOffWallDose(s, takeoverRunning: false));
    }

    [Fact]
    public void TakeoverActuallyRunning_CountsAsADose()
    {
        // The original intent survives: the keeper must not talk over a takeover that IS driving.
        var s = QuietRoomWithTakeoverArmed();

        Assert.False(LockdownDoseKeeper.DoseIsEmpty(s, takeoverRunning: true));
        Assert.True(LockdownDoseKeeper.CountsAsOffWallDose(s, takeoverRunning: true));
    }

    [Fact]
    public void ARunningTakeoverWithoutConsent_IsNotADose()
    {
        var s = QuietRoomWithTakeoverArmed();
        s.AutonomyConsentGiven = false;

        Assert.True(LockdownDoseKeeper.DoseIsEmpty(s, takeoverRunning: true));
    }

    [Fact]
    public void TheOtherOffWallDoses_AreUnaffectedByTheTakeoverRule()
    {
        // Pop Quiz, the audio bed and corner GIFs still count with the takeover service down.
        var s = QuietRoomWithTakeoverArmed();
        s.AutonomyModeEnabled = false;
        s.AutonomyConsentGiven = false;

        s.PopQuizEnabled = true;
        Assert.False(LockdownDoseKeeper.DoseIsEmpty(s, takeoverRunning: false));

        s.PopQuizEnabled = false;
        s.AudioOnlySession = true;
        Assert.False(LockdownDoseKeeper.DoseIsEmpty(s, takeoverRunning: false));
    }

    [Fact]
    public void AnEmptyRoomIsStillEmptyWhateverTheTakeoverIsDoing()
    {
        var s = QuietRoomWithTakeoverArmed();
        s.AutonomyModeEnabled = false;
        s.AutonomyConsentGiven = false;

        Assert.True(LockdownDoseKeeper.DoseIsEmpty(s, takeoverRunning: false));
        Assert.True(LockdownDoseKeeper.DoseIsEmpty(s, takeoverRunning: true));
    }
}
